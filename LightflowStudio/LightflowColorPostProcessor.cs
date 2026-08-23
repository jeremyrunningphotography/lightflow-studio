using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using FlyleafLib.MediaFramework.MediaRenderer;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LightflowStudio;

internal sealed class LightflowColorPostProcessorFactory : IVideoPostProcessorFactory
{
    private readonly object _sync = new();
    private PlayerColorPipeline? _pipeline;
    private bool _bypass;
    private long _revision;

    public void SetPipeline(PlayerColorPipeline? pipeline, bool bypass)
    {
        lock (_sync) { _pipeline = pipeline; _bypass = bypass; _revision++; }
    }

    internal (PlayerColorPipeline? Pipeline, bool Bypass, long Revision) Snapshot()
    {
        lock (_sync) return (_pipeline, _bypass, _revision);
    }

    public IVideoPostProcessor Create(ID3D11Device device) => new Processor(this, device);

    private sealed class Processor : IVideoPostProcessor
    {
        private const string VertexShader = """
            struct O { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            O main(uint id : SV_VertexID) { O o; float2 p=float2((id<<1)&2,id&2); o.uv=float2(p.x,1-p.y); o.position=float4(p*float2(2,-2)+float2(-1,1),0,1); return o; }
            """;
        private const string PixelShader = """
            Texture2D inputTexture : register(t0);
            StructuredBuffer<float4> cameraLut : register(t1);
            StructuredBuffer<float4> creativeLut : register(t2);
            SamplerState inputSampler : register(s0);
            cbuffer State : register(b0) { float4 cameraMin; float4 cameraMax; float4 creativeMin; float4 creativeMax; uint cameraSize; uint creativeSize; uint2 padding; };
            float3 ApplyLut(float3 v, StructuredBuffer<float4> lut, uint n, float3 low, float3 high) {
                v=saturate((v-low)/(high-low))*(n-1); uint3 a=(uint3)floor(v); uint3 b=min(a+1,n-1); float3 f=frac(v);
                uint n2=n*n;
                float3 c000=lut[a.x+n*a.y+n2*a.z].rgb, c100=lut[b.x+n*a.y+n2*a.z].rgb;
                float3 c010=lut[a.x+n*b.y+n2*a.z].rgb, c110=lut[b.x+n*b.y+n2*a.z].rgb;
                float3 c001=lut[a.x+n*a.y+n2*b.z].rgb, c101=lut[b.x+n*a.y+n2*b.z].rgb;
                float3 c011=lut[a.x+n*b.y+n2*b.z].rgb, c111=lut[b.x+n*b.y+n2*b.z].rgb;
                return lerp(lerp(lerp(c000,c100,f.x),lerp(c010,c110,f.x),f.y),lerp(lerp(c001,c101,f.x),lerp(c011,c111,f.x),f.y),f.z);
            }
            float4 main(float4 p:SV_POSITION,float2 uv:TEXCOORD0):SV_TARGET { float4 v=inputTexture.Sample(inputSampler,uv); if(cameraSize>1)v.rgb=ApplyLut(v.rgb,cameraLut,cameraSize,cameraMin.rgb,cameraMax.rgb); if(creativeSize>1)v.rgb=ApplyLut(v.rgb,creativeLut,creativeSize,creativeMin.rgb,creativeMax.rgb); return v; }
            """;

        private readonly LightflowColorPostProcessorFactory _owner;
        private readonly ID3D11Device _device;
        private readonly ID3D11VertexShader _vs;
        private readonly ID3D11PixelShader _ps;
        private readonly ID3D11SamplerState _sampler;
        private ID3D11Buffer? _cameraBuffer, _creativeBuffer, _stateBuffer;
        private ID3D11ShaderResourceView? _cameraView, _creativeView;
        private long _revision = -1;

        public Processor(LightflowColorPostProcessorFactory owner, ID3D11Device device)
        {
            _owner=owner; _device=device;
            using var vs=Compile(VertexShader,"vs_5_0"); using var ps=Compile(PixelShader,"ps_5_0");
            _vs=device.CreateVertexShader(vs); _ps=device.CreatePixelShader(ps);
            _sampler=device.CreateSamplerState(new() { Filter=Filter.MinMagMipLinear, AddressU=TextureAddressMode.Clamp, AddressV=TextureAddressMode.Clamp, AddressW=TextureAddressMode.Clamp, MaxLOD=float.MaxValue });
        }

        public void Process(in VideoPostProcessContext frame)
        {
            var state=_owner.Snapshot();
            if (_revision!=state.Revision) Rebuild(state.Pipeline, state.Revision);
            if (state.Bypass || state.Pipeline?.HasColor != true)
            {
                frame.DeviceContext.CopyResource(frame.Output.Resource, frame.Input.Resource);
                return;
            }
            var context=frame.DeviceContext;
            context.OMSetRenderTargets(frame.Output); context.RSSetViewport(new Viewport(frame.OutputWidth,frame.OutputHeight));
            context.IASetInputLayout(null); context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vs); context.PSSetShader(_ps); context.PSSetSampler(0,_sampler);
            context.PSSetShaderResource(0,frame.Input); context.PSSetShaderResource(1,_cameraView!); context.PSSetShaderResource(2,_creativeView!);
            context.PSSetConstantBuffer(0,_stateBuffer); context.Draw(3,0);
        }

        private void Rebuild(PlayerColorPipeline? pipeline,long revision)
        {
            _cameraView?.Dispose(); _creativeView?.Dispose(); _cameraBuffer?.Dispose(); _creativeBuffer?.Dispose(); _stateBuffer?.Dispose();
            (_cameraBuffer,_cameraView)=CreateLut(pipeline?.Camera); (_creativeBuffer,_creativeView)=CreateLut(pipeline?.Creative);
            var current=_owner.Snapshot();
            var state = new ShaderState(pipeline?.Camera, pipeline?.Creative,
                current.Bypass ? 0u : (uint)(pipeline?.Camera?.Size ?? 0), current.Bypass ? 0u : (uint)(pipeline?.Creative?.Size ?? 0));
            _stateBuffer=_device.CreateBuffer([state], BindFlags.ConstantBuffer); _revision=revision;
        }

        private (ID3D11Buffer?,ID3D11ShaderResourceView?) CreateLut(CubeLutData? lut)
        {
            lut ??= new CubeLutData(2, [0,0,0,1, 1,0,0,1, 0,1,0,1, 1,1,0,1, 0,0,1,1, 1,0,1,1, 0,1,1,1, 1,1,1,1]);
            var vectors=new Vector4[lut.Samples.Length/4]; for(var i=0;i<vectors.Length;i++) vectors[i]=new(lut.Samples[i*4],lut.Samples[i*4+1],lut.Samples[i*4+2],1);
            var buffer=_device.CreateBuffer(vectors, BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured);
            var description = new ShaderResourceViewDescription(buffer, Format.Unknown, 0, (uint)vectors.Length);
            var view=_device.CreateShaderResourceView(buffer, description); return(buffer,view);
        }

        public void Dispose() { _cameraView?.Dispose(); _creativeView?.Dispose(); _cameraBuffer?.Dispose(); _creativeBuffer?.Dispose(); _stateBuffer?.Dispose(); _sampler.Dispose(); _ps.Dispose(); _vs.Dispose(); }
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct ShaderState
        {
            private readonly Vector4 _cameraMin, _cameraMax, _creativeMin, _creativeMax;
            private readonly uint _cameraSize, _creativeSize, _padding0, _padding1;
            public ShaderState(CubeLutData? camera, CubeLutData? creative, uint cameraSize, uint creativeSize)
            {
                _cameraMin = new(camera?.DomainMin ?? Vector3.Zero, 0); _cameraMax = new(camera?.DomainMax ?? Vector3.One, 0);
                _creativeMin = new(creative?.DomainMin ?? Vector3.Zero, 0); _creativeMax = new(creative?.DomainMax ?? Vector3.One, 0);
                _cameraSize=cameraSize; _creativeSize=creativeSize; _padding0=_padding1=0;
            }
        }
        private static Blob Compile(string source,string target) { Compiler.Compile(Encoding.UTF8.GetBytes(source),null!,null!,"main",null!,target,ShaderFlags.OptimizationLevel3,out var shader,out var errors); using(errors){} return shader; }
    }
}

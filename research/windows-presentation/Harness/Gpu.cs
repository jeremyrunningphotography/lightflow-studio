using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using static Silk.NET.Core.Native.SilkMarshal;

namespace WindowsPresentation;

// Public D3D11 producer. CPU reads are confined to explicit capture/evidence.
unsafe sealed class Gpu : IDisposable
{
    public ComPtr<ID3D11Device> Device;
    public ComPtr<ID3D11DeviceContext> Context;
    public string Adapter = "";
    public Gpu(byte[]? luid)
    {
        using var dxgi = new DXGI(DXGI.CreateDefaultContext(["dxgi.dll"]));
        using var d3d = new D3D11(D3D11.CreateDefaultContext(["d3d11.dll"]));
        using var factory = dxgi.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; ; i++)
        {
            ComPtr<IDXGIAdapter> adapter = default;
            if (factory.EnumAdapters(i, adapter.GetAddressOf()) < 0) throw new Exception("Matching GPU adapter not found");
            using (adapter)
            {
                AdapterDesc desc;
                ThrowHResult(adapter.GetDesc(&desc));
                var bytes = new ReadOnlySpan<byte>(&desc.AdapterLuid, 8).ToArray();
                if (luid != null && !bytes.SequenceEqual(luid)) continue;
                Adapter = PtrToString((nint)desc.Description, NativeStringEncoding.LPWStr)!;
                D3DFeatureLevel level;
                var levels = stackalloc D3DFeatureLevel[] { D3DFeatureLevel.Level111, D3DFeatureLevel.Level110 };
                ThrowHResult(d3d.CreateDevice(adapter, D3DDriverType.Unknown, 0, (uint)CreateDeviceFlag.BgraSupport,
                    levels, 2, D3D11.SdkVersion, Device.GetAddressOf(), &level, Context.GetAddressOf()));
                return;
            }
        }
    }
    public void Dispose() { Context.Dispose(); Device.Dispose(); }
}

sealed class Slot : IAsyncDisposable
{
    public readonly Frame Identity;
    public readonly int Width, Height;
    public ComPtr<ID3D11Texture2D> Texture;
    ComPtr<IDXGIKeyedMutex> mutex;
    ComPtr<ID3D11RenderTargetView> target;
    readonly Gpu gpu;
    readonly nint handle;
    public ICompositionImportedGpuImage? Imported;
    public Task? Update;
    public bool Released;
    public int Leases;
    public bool Destroyed;
    public unsafe Slot(Gpu gpu, Frame frame, int width, int height)
    {
        this.gpu = gpu; Identity = frame; Width = width; Height = height;
        var desc = new Texture2DDesc { Width=(uint)width, Height=(uint)height, MipLevels=1, ArraySize=1,
            Format=Format.FormatB8G8R8A8Unorm, SampleDesc=new SampleDesc(1,0), Usage=Usage.Default,
            BindFlags=(uint)(BindFlag.RenderTarget|BindFlag.ShaderResource), MiscFlags=(uint)ResourceMiscFlag.SharedKeyedmutex };
        ThrowHResult(gpu.Device.CreateTexture2D(&desc, (SubresourceData*)null, Texture.GetAddressOf()));
        mutex = Texture.QueryInterface<IDXGIKeyedMutex>();
        using var resource = Texture.QueryInterface<IDXGIResource>();
        void* shared = null; ThrowHResult(resource.GetSharedHandle(ref shared)); handle=(nint)shared;
        ThrowHResult(gpu.Device.CreateRenderTargetView(Texture, null, ref target));
        ThrowHResult(mutex.AcquireSync(0, 2000));
        var color = stackalloc float[] { frame.R/255f, frame.G/255f, frame.B/255f, 1 };
        gpu.Context.ClearRenderTargetView(target, color);
        using var context1=gpu.Context.QueryInterface<ID3D11DeviceContext1>();
        var marker=stackalloc float[] { 1,1,1,1 };
        var rectangle=new Silk.NET.Maths.Box2D<int>(new(0,0),new(width/4,height/4));
        context1.ClearView((ID3D11View*)target.Handle,marker,&rectangle,1);
        gpu.Context.Flush();
    }
    // ReleaseSync publishes the producer writes; the consumer's acquire is the GPU-ready boundary.
    public void SignalReady() { ThrowHResult(mutex.ReleaseSync(1)); }
    public void Submit(ICompositionGpuInterop interop, CompositionDrawingSurface surface)
    {
        Imported = interop.ImportImage(new PlatformHandle(handle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
            new PlatformGraphicsExternalImageProperties { Width=Width, Height=Height, TopLeftOrigin=true, Format=PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm });
        Update = surface.UpdateWithKeyedMutexAsync(Imported, 1, 2);
    }
    public bool TryRelease()
    {
        if (Released) return true;
        int hr=mutex.AcquireSync(2, 0);
        if (hr == 0) { Released=true; ThrowHResult(mutex.ReleaseSync(3)); return true; }
        if (hr == 0x102 || hr == unchecked((int)0x887A0027)) return false;
        ThrowHResult(hr); return false;
    }
    public unsafe byte[] Capture()
    {
        if (!Released || Leases == 0) throw new Exception("Capture requires independent release and retained lease");
        Texture2DDesc desc; Texture.GetDesc(&desc);
        desc.Usage=Usage.Staging; desc.BindFlags=0; desc.MiscFlags=0; desc.CPUAccessFlags=(uint)CpuAccessFlag.Read;
        ComPtr<ID3D11Texture2D> staging=default;
        ThrowHResult(gpu.Device.CreateTexture2D(&desc,(SubresourceData*)null,staging.GetAddressOf()));
        using (staging)
        {
            ThrowHResult(mutex.AcquireSync(3, 2000));
            gpu.Context.CopyResource(staging,Texture);
            gpu.Context.Flush();
            ThrowHResult(mutex.ReleaseSync(3));
            MappedSubresource map;
            ThrowHResult(gpu.Context.Map(staging,0,Map.Read,0,&map));
            try {
                var bytes=new byte[Width*Height*4];
                for (int y=0;y<Height;y++) new ReadOnlySpan<byte>((byte*)map.PData+y*map.RowPitch,Width*4).CopyTo(bytes.AsSpan(y*Width*4));
                return bytes;
            } finally { gpu.Context.Unmap(staging,0); }
        }
    }
    public ValueTask DisposeAsync()
    {
        if (Leases != 0 || !Released || Update is { IsCompleted: false }) throw new Exception("Premature surface destruction");
        return Finish();
    }
    async ValueTask Finish()
    {
        if (Imported != null) await Imported.DisposeAsync();
        target.Dispose(); mutex.Dispose(); Texture.Dispose(); Destroyed=true;
    }
}


#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <AppKit/AppKit.h>
#import <QuartzCore/CAMetalLayer.h>
#include <math.h>

// Small GPU proof: trilinear 2^3 Camera then Creative cubes, retained BGRA,
// authored rotation, viewport, overlay composition and full-resolution readback.
static NSString *shader=@"#include <metal_stdlib>\nusing namespace metal;\n"
"struct Params { uint w,h,rotation,bypass; float zoom,panx,pany; uint overlay; };\n"
"float3 cube(float3 x,device const float4 *v,float3 lo,float3 hi){ float3 p=clamp((x-lo)/(hi-lo),0.0f,1.0f);float3 z=0;for(uint b=0;b<2;b++)for(uint g=0;g<2;g++)for(uint r=0;r<2;r++){float w=(r?p.x:1-p.x)*(g?p.y:1-p.y)*(b?p.z:1-p.z);z+=v[r+2*g+4*b].xyz*w;}return z;}\n"
"kernel void proof(texture2d<float,access::read> src [[texture(0)]],texture2d<float,access::write> dst [[texture(1)]],device const float4 *camera [[buffer(0)]],device const float4 *creative [[buffer(1)]],constant Params &p [[buffer(2)]],uint2 q [[thread_position_in_grid]]){if(q.x>=p.w||q.y>=p.h)return;float2 uv=(float2(q)+.5)/float2(p.w,p.h);uv=(uv-.5)/p.zoom+.5+float2(p.panx,p.pany);float2 s=uv;if(p.rotation==1)s=float2(uv.y,1-uv.x);if(p.rotation==2)s=1-uv;if(p.rotation==3)s=float2(1-uv.y,uv.x);float3 rgb=0;if(all(s>=0)&&all(s<1)){uint2 a=min(uint2(s*float2(src.get_width(),src.get_height())),uint2(src.get_width()-1,src.get_height()-1));rgb=src.read(a).rgb;if(!p.bypass){rgb=cube(rgb,camera,float3(.1),float3(.9));rgb=cube(rgb,creative,float3(0),float3(1));}}if(p.overlay&&q.x<32&&q.y<24)rgb=float3(1,0,0);dst.write(float4(clamp(rgb,0.0f,1.0f),1),q);}\n";
typedef struct {uint32_t w,h,rotation,bypass;float zoom,panx,pany;uint32_t overlay;} Params;
static void cubeCPU(float *x,float *v,float lo,float hi){float p[3],z[3]={0};for(int c=0;c<3;c++)p[c]=fmaxf(0,fminf(1,(x[c]-lo)/(hi-lo)));for(int b=0;b<2;b++)for(int g=0;g<2;g++)for(int r=0;r<2;r++){float w=(r?p[0]:1-p[0])*(g?p[1]:1-p[1])*(b?p[2]:1-p[2]);for(int c=0;c<3;c++)z[c]+=v[(r+2*g+4*b)*4+c]*w;}memcpy(x,z,sizeof(z));}
int main(int argc,char **argv){setbuf(stdout,NULL);@autoreleasepool{
    if(argc<2)return 1;NSData *raw=[NSData dataWithContentsOfFile:@(argv[1])];if(raw.length!=320*180*4)return 2;
    id<MTLDevice> device=MTLCreateSystemDefaultDevice();if(!device)return 3;NSError *error=nil;
    MTLCompileOptions *options=[MTLCompileOptions new];options.fastMathEnabled=NO;
    id<MTLLibrary> lib=[device newLibraryWithSource:shader options:options error:&error];if(!lib){fprintf(stderr,"%s\n",error.description.UTF8String);return 4;}
    id<MTLComputePipelineState> pipeline=[device newComputePipelineStateWithFunction:[lib newFunctionWithName:@"proof"] error:&error];
    id<MTLCommandQueue> queue=[device newCommandQueue];
    MTLTextureDescriptor *desc=[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:320 height:180 mipmapped:NO];desc.usage=MTLTextureUsageShaderRead;
    id<MTLTexture> src=[device newTextureWithDescriptor:desc];[src replaceRegion:MTLRegionMake2D(0,0,320,180) mipmapLevel:0 withBytes:raw.bytes bytesPerRow:320*4];
    float cam[32],creative[32];for(int b=0;b<2;b++)for(int g=0;g<2;g++)for(int r=0;r<2;r++){int i=(r+2*g+4*b)*4;cam[i]=r*.7+.05+.1*g*b;cam[i+1]=g*.6+.1+.1*r*b;cam[i+2]=b*.5+.15+.1*r*g;cam[i+3]=1;creative[i]=1-r*.8+.05*g*b;creative[i+1]=g*.9+.05*r*b;creative[i+2]=b*.8+.08*r*g;creative[i+3]=1;}
    id<MTLBuffer> cb=[device newBufferWithBytes:cam length:sizeof(cam) options:MTLResourceStorageModeShared];id<MTLBuffer> cr=[device newBufferWithBytes:creative length:sizeof(creative) options:MTLResourceStorageModeShared];
    for(int variant=0;variant<10;variant++){
        Params p={320,180,variant%4,variant>=4,1,0,0,0};if(p.rotation%2){p.w=180;p.h=320;}if(variant==7){p.w=640;p.h=360;p.zoom=1.5;p.panx=.1;p.pany=-.1;p.overlay=1;}
        if(variant==8){p.w=3840;p.h=2160;p.rotation=0;p.bypass=0;}if(variant==9){p.w=1920;p.h=1080;p.rotation=0;p.bypass=0;}
        MTLTextureDescriptor *d=[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:p.w height:p.h mipmapped:NO];d.usage=MTLTextureUsageShaderWrite|MTLTextureUsageShaderRead;d.storageMode=MTLStorageModeShared;id<MTLTexture> dst=[device newTextureWithDescriptor:d];
        id<MTLCommandBuffer> command=[queue commandBuffer];id<MTLComputeCommandEncoder> enc=[command computeCommandEncoder];[enc setComputePipelineState:pipeline];[enc setTexture:src atIndex:0];[enc setTexture:dst atIndex:1];[enc setBuffer:cb offset:0 atIndex:0];[enc setBuffer:cr offset:0 atIndex:1];[enc setBytes:&p length:sizeof(p) atIndex:2];[enc dispatchThreads:MTLSizeMake(p.w,p.h,1) threadsPerThreadgroup:MTLSizeMake(16,16,1)];[enc endEncoding];[command commit];[command waitUntilCompleted];
        NSMutableData *capture=[NSMutableData dataWithLength:p.w*p.h*4];[dst getBytes:capture.mutableBytes bytesPerRow:p.w*4 fromRegion:MTLRegionMake2D(0,0,p.w,p.h) mipmapLevel:0];
        const unsigned char *in=raw.bytes,*gpu=capture.bytes;int maxerr=0;long total=0;
        for(int y=0;y<p.h;y++)for(int x=0;x<p.w;x++){
            float u=(((x+.5f)/p.w)-.5f)/p.zoom+.5f+p.panx,v=(((y+.5f)/p.h)-.5f)/p.zoom+.5f+p.pany,s=u,t=v;
            if(p.rotation==1){s=v;t=1-u;}if(p.rotation==2){s=1-u;t=1-v;}if(p.rotation==3){s=1-v;t=u;}
            float rgb[3]={0};if(s>=0&&s<1&&t>=0&&t<1){int a=((int)(t*180)*320+(int)(s*320))*4;rgb[0]=in[a+2]/255.f;rgb[1]=in[a+1]/255.f;rgb[2]=in[a]/255.f;if(!p.bypass){cubeCPU(rgb,cam,.1,.9);cubeCPU(rgb,creative,0,1);}}
            if(p.overlay&&x<32&&y<24){rgb[0]=1;rgb[1]=rgb[2]=0;}
            for(int c=0;c<3;c++){int expected=lroundf(fmaxf(0,fminf(1,rgb[c]))*255);int e=abs(expected-gpu[(y*p.w+x)*4+2-c]);maxerr=MAX(maxerr,e);total+=e;}
        }
        printf("{\"variant\":%d,\"rotation\":%d,\"bypass\":%d,\"width\":%d,\"height\":%d,\"max_channel_error\":%d,\"mean_channel_error\":%.9f,\"command_status\":%lu,\"gpu_seconds\":%.9f}\n",variant,p.rotation,p.bypass,p.w,p.h,maxerr,(double)total/(p.w*p.h*3),(unsigned long)command.status,command.GPUEndTime-command.GPUStartTime);
        [capture writeToFile:[NSString stringWithFormat:@"work/results/metal-%d.bgra",variant] atomically:YES];
    }
    [NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
    NSWindow *window=[[NSWindow alloc] initWithContentRect:NSMakeRect(-10000,-10000,640,360) styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];
    window.releasedWhenClosed=NO;
    NSView *view=[[NSView alloc]initWithFrame:NSMakeRect(0,0,640,360)];view.wantsLayer=YES;CAMetalLayer *layer=[CAMetalLayer layer];layer.device=device;layer.pixelFormat=MTLPixelFormatBGRA8Unorm;layer.frame=view.bounds;layer.contentsScale=2;layer.drawableSize=CGSizeMake(1280,720);layer.framebufferOnly=NO;view.layer=layer;window.contentView=view;
    NSView *overlay=[[NSView alloc]initWithFrame:NSMakeRect(10,10,100,30)];overlay.wantsLayer=YES;overlay.layer.backgroundColor=NSColor.redColor.CGColor;[view addSubview:overlay];
    id<CAMetalDrawable> drawable=[layer nextDrawable];
    if(drawable){id<MTLCommandBuffer> c=[queue commandBuffer];Params p={1280,720,0,0,1,0,0,1};id<MTLComputeCommandEncoder> enc=[c computeCommandEncoder];[enc setComputePipelineState:pipeline];[enc setTexture:src atIndex:0];[enc setTexture:drawable.texture atIndex:1];[enc setBuffer:cb offset:0 atIndex:0];[enc setBuffer:cr offset:0 atIndex:1];[enc setBytes:&p length:sizeof(p) atIndex:2];[enc dispatchThreads:MTLSizeMake(1280,720,1) threadsPerThreadgroup:MTLSizeMake(16,16,1)];[enc endEncoding];[c presentDrawable:drawable];[c commit];[c waitUntilCompleted];printf("{\"native_source_render_command_status\":%lu}\n",(unsigned long)c.status);}
    printf("{\"surface\":\"NSView/CAMetalLayer\",\"metal_device\":\"%s\",\"retina_drawable\":%d,\"drawable_width\":%lu,\"drawable_height\":%lu,\"overlay_subviews\":%lu,\"window_visible\":%d}\n",device.name.UTF8String,drawable!=nil,(unsigned long)drawable.texture.width,(unsigned long)drawable.texture.height,(unsigned long)view.subviews.count,window.visible);
    layer.drawableSize=CGSizeMake(640,360);view.frame=NSMakeRect(0,0,320,180);layer.frame=view.bounds;
    printf("{\"surface_resize\":true,\"logical_width\":%.0f,\"pixel_width\":%.0f,\"retained_texture_width\":%lu}\n",view.frame.size.width,layer.drawableSize.width,(unsigned long)src.width);
    [window close];
    for(int cycle=0;cycle<20;cycle++){@autoreleasepool{
        NSWindow *w=[[NSWindow alloc]initWithContentRect:NSMakeRect(-10000,-10000,320,180) styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];w.releasedWhenClosed=NO;
        NSView *v=[[NSView alloc]initWithFrame:NSMakeRect(0,0,320,180)];v.wantsLayer=YES;CAMetalLayer *l=[CAMetalLayer layer];l.device=device;l.pixelFormat=MTLPixelFormatBGRA8Unorm;l.frame=v.bounds;l.contentsScale=2;l.drawableSize=CGSizeMake(640,360);v.layer=l;w.contentView=v;
        id<CAMetalDrawable> d=[l nextDrawable];printf("{\"native_lifecycle_cycle\":%d,\"drawable\":%d,\"window_visible\":%d,\"same_source_texture\":%d}\n",cycle,d!=nil,w.visible,src.width==320);[w close];
    }}
}return 0;}

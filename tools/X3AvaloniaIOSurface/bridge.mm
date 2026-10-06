#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <IOSurface/IOSurface.h>
#import <AppKit/AppKit.h>
#include <atomic>
#include <string>
#include <thread>
#include <chrono>
struct Slot { IOSurfaceRef surface; id<MTLTexture> texture; id<MTLSharedEvent> ready, released; bool leased=false; uint64_t value=0; };
struct Producer { id<MTLDevice> device; id<MTLCommandQueue> queue; id<MTLRenderPipelineState> pipeline; Slot slots[3]; int width,height; std::atomic<int> pending{0}; std::atomic<double> gpuMs{0}; };
static std::atomic<int> surfaces{0}, producers{0};
extern "C" void* x3_create(int w,int h) { @autoreleasepool {
 auto p=new Producer; p->width=w;p->height=h;p->device=MTLCreateSystemDefaultDevice();p->queue=[p->device newCommandQueue];
 NSString* code=@"#include <metal_stdlib>\nusing namespace metal;\nstruct O{float4 p[[position]];};vertex O v(uint i[[vertex_id]]){float2 a[3]={float2(-1,-1),float2(3,-1),float2(-1,3)};return {float4(a[i],0,1)};} fragment float4 f(O o[[stage_in]],constant uint &s[[buffer(0)]]){uint x=uint(o.p.x),y=uint(o.p.y);if(y<32){uint b=(s>>(x/32%16))&1;return b?float4(1,1,1,1):float4(0,0,0,1);}return float4(float(s%5+1)/8.,float((s/5)%5+1)/8.,float((s/25)%5+1)/8.,1);}";
 NSError* e=nil;auto lib=[p->device newLibraryWithSource:code options:nil error:&e];if(!lib){fprintf(stderr,"%s\n",e.description.UTF8String);delete p;return nullptr;}
 auto desc=[MTLRenderPipelineDescriptor new];desc.vertexFunction=[lib newFunctionWithName:@"v"];desc.fragmentFunction=[lib newFunctionWithName:@"f"];desc.colorAttachments[0].pixelFormat=MTLPixelFormatBGRA8Unorm;p->pipeline=[p->device newRenderPipelineStateWithDescriptor:desc error:&e];
 for(auto &s:p->slots){size_t row=IOSurfaceAlignProperty(kIOSurfaceBytesPerRow,w*4);s.surface=IOSurfaceCreate((__bridge CFDictionaryRef)@{(id)kIOSurfaceWidth:@(w),(id)kIOSurfaceHeight:@(h),(id)kIOSurfaceBytesPerElement:@4,(id)kIOSurfaceBytesPerRow:@(row),(id)kIOSurfaceAllocSize:@(row*h),(id)kIOSurfacePixelFormat:@((uint32_t)0x42475241)});if(!s.surface)abort();auto td=[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:w height:h mipmapped:NO];td.usage=MTLTextureUsageRenderTarget|MTLTextureUsageShaderRead;td.storageMode=MTLStorageModeShared;s.texture=[p->device newTextureWithDescriptor:td iosurface:s.surface plane:0];s.ready=[p->device newSharedEvent];s.released=[p->device newSharedEvent];surfaces++;}
 producers++;return p;}}
extern "C" uint64_t x3_device(void* q){return ((Producer*)q)->device.registryID;}
extern "C" void* x3_surface(void*q,int i){return ((Producer*)q)->slots[i].surface;}
extern "C" void* x3_event(void*q,int i,int release){auto &s=((Producer*)q)->slots[i];return (__bridge void*)(release?s.released:s.ready);}
extern "C" uint64_t x3_value(void*q,int i,int release){auto &s=((Producer*)q)->slots[i];return release?s.released.signaledValue:s.ready.signaledValue;}
extern "C" uint32_t x3_id(void*q,int i){return IOSurfaceGetID(((Producer*)q)->slots[i].surface);}
extern "C" uint64_t x3_stride(void*q,int i){return IOSurfaceGetBytesPerRow(((Producer*)q)->slots[i].surface);}
extern "C" int x3_texture_identity(void*q,int i){auto &s=((Producer*)q)->slots[i];return s.texture.iosurface==s.surface && s.texture.pixelFormat==MTLPixelFormatBGRA8Unorm;}
extern "C" int x3_acquire(void*q,int i,uint64_t value){auto &s=((Producer*)q)->slots[i];if(s.leased||s.released.signaledValue<s.value)return 0;s.leased=true;s.value=value;return 1;}
extern "C" int x3_produce(void*q,int i,uint64_t value,uint32_t serial,int delayMs){auto p=(Producer*)q;auto &s=p->slots[i];if(!s.leased||value!=s.value)return 0;
 // Delayed readiness is independently submitted from a worker; no UI/native input.
 p->pending++;std::thread([p,i,value,serial,delayMs]{@autoreleasepool{if(delayMs)std::this_thread::sleep_for(std::chrono::milliseconds(delayMs));auto &s=p->slots[i];auto cb=[p->queue commandBuffer];auto rd=[MTLRenderPassDescriptor renderPassDescriptor];rd.colorAttachments[0].texture=s.texture;rd.colorAttachments[0].loadAction=MTLLoadActionDontCare;rd.colorAttachments[0].storeAction=MTLStoreActionStore;auto enc=[cb renderCommandEncoderWithDescriptor:rd];[enc setRenderPipelineState:p->pipeline];[enc setFragmentBytes:&serial length:sizeof(serial) atIndex:0];[enc drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:3];[enc endEncoding];[cb encodeSignalEvent:s.ready value:value];[cb commit];[cb waitUntilCompleted];p->gpuMs=(cb.GPUEndTime-cb.GPUStartTime)*1000;p->pending--;}}).detach();return 1;}
extern "C" int x3_release(void*q,int i){auto &s=((Producer*)q)->slots[i];if(s.released.signaledValue<s.value)return 0;s.leased=false;return 1;}
extern "C" void x3_cancel(void*q,int i){auto&s=((Producer*)q)->slots[i];while(s.ready.signaledValue<s.value)std::this_thread::sleep_for(std::chrono::milliseconds(1));s.released.signaledValue=s.value;s.leased=false;}
extern "C" double x3_gpu_ms(void*q){return ((Producer*)q)->gpuMs;}
extern "C" uint64_t x3_hash(void*q,int i){auto&s=((Producer*)q)->slots[i];uint64_t h=1469598103934665603ULL;IOSurfaceLock(s.surface,kIOSurfaceLockReadOnly,nullptr);auto b=(uint8_t*)IOSurfaceGetBaseAddress(s.surface);size_t stride=IOSurfaceGetBytesPerRow(s.surface);auto p=(Producer*)q;for(int y=0;y<p->height;y++)for(int x=0;x<p->width*4;x++){h^=b[y*stride+x];h*=1099511628211ULL;}IOSurfaceUnlock(s.surface,kIOSurfaceLockReadOnly,nullptr);return h;}
extern "C" void x3_read(void*q,int i,void*out){auto p=(Producer*)q;auto&s=p->slots[i];[s.texture getBytes:out bytesPerRow:p->width*4 fromRegion:MTLRegionMake2D(0,0,p->width,p->height) mipmapLevel:0];}
extern "C" void x3_destroy(void*q){auto p=(Producer*)q;while(p->pending)std::this_thread::sleep_for(std::chrono::milliseconds(1));@autoreleasepool{for(auto&s:p->slots){s.texture=nil;s.ready=nil;s.released=nil;CFRelease(s.surface);surfaces--;}p->pipeline=nil;p->queue=nil;p->device=nil;}delete p;producers--;}
extern "C" int x3_live_surfaces(){return surfaces;}
extern "C" int x3_live_producers(){return producers;}
extern "C" const char* x3_windows(){static std::string result;@autoreleasepool{NSMutableArray* a=[NSMutableArray new];for(NSWindow*w in NSApp.windows)if([w.title containsString:@"IOSurface"])[a addObject:@{@"title":w.title,@"visible":@(w.visible),@"miniaturized":@(w.miniaturized),@"fullscreen":@((w.styleMask&NSWindowStyleMaskFullScreen)!=0),@"scale":@(w.backingScaleFactor),@"width":@(w.contentView.bounds.size.width),@"height":@(w.contentView.bounds.size.height),@"active":@(NSApp.active)}];NSData*d=[NSJSONSerialization dataWithJSONObject:a options:0 error:nil];result.assign((const char*)d.bytes,d.length);}return result.c_str();}

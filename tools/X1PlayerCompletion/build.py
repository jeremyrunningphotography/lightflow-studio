"""Generate an instrumented derivative without altering accepted G1b source."""
from pathlib import Path
import subprocess,hashlib
r=Path(__file__).resolve().parents[2]
s=(r/'tools/X1MacPlayerG1b/integrated.mm').read_text()
def change(old,new):
 global s
 assert s.count(old)==1,(old,s.count(old))
 s=s.replace(old,new)
change('class Audio {', 'static std::atomic<bool> starved{false};static std::atomic<int> missedRefills{0};\nclass Audio {')
change('auto a=(Audio*)u;size_t samples=', 'auto a=(Audio*)u;if(starved){missedRefills++;return;}size_t samples=')
change('class Renderer {','static bool backendOnly=false;static double injectedRenderDelay=0;\nclass Renderer {')
change('double gpu=0,copy=0,ackDelay=0;','double gpu=0,copy=0,ackDelay=0,importMs=0,producerWaitMs=0,drawableMs=0,consumerWaitMs=0; id<MTLTexture> snapshots[3];')
change('CVMetalTextureCacheCreate(NULL,NULL,dev,NULL,&cache);open();','CVMetalTextureCacheCreate(NULL,NULL,dev,NULL,&cache);if(!backendOnly)open();')
change('++serial;size(', 'double phaseStart=now();++serial;size(')
change(' auto cmd=[prodQueue commandBuffer];',' importMs=(now()-phaseStart)*1000; auto cmd=[prodQueue commandBuffer];')
change('[cmd commit];[cmd waitUntilCompleted];gpu=', 'double pw=now();[cmd commit];if(injectedRenderDelay>0){std::this_thread::sleep_for(std::chrono::duration<double>(injectedRenderDelay));injectedRenderDelay=0;}[cmd waitUntilCompleted];producerWaitMs=(now()-pw)*1000;gpu=')
old=s[s.index(' auto d=[layer nextDrawable]'):s.index(' NSDictionary *record=')]
new="""
 double dw=now();auto c=[uiQueue commandBuffer];[c encodeWaitForEvent:ready value:serial];
 double displayTime=0;
 if(backendOnly){
 if(!snapshots[selected]||snapshots[selected].width!=ow||snapshots[selected].height!=oh){auto desc=[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:ow height:oh mipmapped:NO];desc.usage=MTLTextureUsageShaderRead;snapshots[selected]=[consumer newTextureWithDescriptor:desc];}
 auto blit=[c blitCommandEncoder];[blit copyFromTexture:imported sourceSlice:0 sourceLevel:0 sourceOrigin:MTLOriginMake(0,0,0) sourceSize:MTLSizeMake(ow,oh,1) toTexture:snapshots[selected] destinationSlice:0 destinationLevel:0 destinationOrigin:MTLOriginMake(0,0,0)];[blit endEncoding];drawableMs=(now()-dw)*1000;[c encodeSignalEvent:released value:serial];t=now();[c commit];[c waitUntilCompleted];consumerWaitMs=(now()-t)*1000;acked=c.status==MTLCommandBufferStatusCompleted;ackDelay=now()-t;
 }else{
 auto d=[layer nextDrawable];if(!d)exit(6);drawableMs=(now()-dw)*1000;MTLRenderPassDescriptor *pass=[MTLRenderPassDescriptor renderPassDescriptor];pass.colorAttachments[0].texture=d.texture;pass.colorAttachments[0].loadAction=MTLLoadActionClear;pass.colorAttachments[0].storeAction=MTLStoreActionStore;auto re=[c renderCommandEncoderWithDescriptor:pass];[re setRenderPipelineState:display];[re setFragmentTexture:imported atIndex:0];[re drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:3];[re endEncoding];[c encodeSignalEvent:released value:serial];auto ack=std::make_shared<std::atomic<bool>>(false);auto stamp=std::make_shared<std::atomic<double>>(0);[d addPresentedHandler:^(id<MTLDrawable> dr){stamp->store(dr.presentedTime);ack->store(true,std::memory_order_release);}];t=now();[c presentDrawable:d];[c commit];[c waitUntilCompleted];consumerWaitMs=(now()-t)*1000;double timeout=now()+1;while(!ack->load(std::memory_order_acquire)&&now()<timeout)pump(.001);displayTime=stamp->load();acked=ack->load()&&displayTime>0;ackDelay=now()-t;
 }
 if(acked&&generation==a->generation)retained=a;if(y)CFRelease(y);if(uv)CFRelease(uv);
"""
change(old,new)
change('@"presented_ack":@(acked),','@"authority_kind":backendOnly?@"GPU-completed-snapshot":@"native-drawable-callback",@"presented_ack":@(backendOnly?false:acked),@"render_ready":@(acked),@"import_ms":@(importMs),@"producer_wait_ms":@(producerWaitMs),@"drawable_ms":@(drawableMs),@"consumer_wait_ms":@(consumerWaitMs),')
s=s[:s.index('int main(int argc,char**argv)')]
s=s.replace('auto row=[record mutableCopy]', 'NSMutableDictionary *row=[record mutableCopy]')
s+=(r/'tools/X1PlayerCompletion/completion.mm').read_text()
s=s.replace('auto record=[r.present(current,state,@"completion-playback",false) mutableCopy]', 'NSMutableDictionary *record=[r.present(current,state,@"completion-playback",false) mutableCopy]')
o=r/'work/completion';o.mkdir(parents=True,exist_ok=True);(o/'generated.mm').write_text(s)
p=r/'work/deps/lgpl-ffmpeg'
subprocess.run(['clang++','-std=c++17','-O2','-fobjc-arc',str(o/'generated.mm'),'-I'+str(p/'include'),'-L'+str(p/'lib'),'-lavformat','-lavcodec','-lavutil','-lavfilter','-lswscale','-framework','Metal','-framework','AppKit','-framework','QuartzCore','-framework','CoreVideo','-framework','IOSurface','-framework','AudioToolbox','-o',str(o/'completion')],check=True)

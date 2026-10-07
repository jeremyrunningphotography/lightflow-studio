#include <AudioUnit/AudioUnit.h>
#include <CoreAudio/CoreAudio.h>
#include <CoreFoundation/CoreFoundation.h>
#include <stdio.h>
#include <string.h>
#include <stdatomic.h>
#include <time.h>
#include <unistd.h>
static _Atomic unsigned long calls=0,frames=0;
static OSStatus render(void*u,AudioUnitRenderActionFlags*f,const AudioTimeStamp*t,UInt32 b,UInt32 n,AudioBufferList*data){for(UInt32 i=0;i<data->mNumberBuffers;i++)memset(data->mBuffers[i].mData,0,data->mBuffers[i].mDataByteSize);atomic_fetch_add(&calls,1);atomic_fetch_add(&frames,n);return 0;}
int main(){AudioComponentDescription d={0};d.componentType=kAudioUnitType_Output;d.componentSubType=kAudioUnitSubType_DefaultOutput;d.componentManufacturer=kAudioUnitManufacturer_Apple;AudioComponent c=AudioComponentFindNext(NULL,&d);AudioUnit u=NULL;OSStatus create=AudioComponentInstanceNew(c,&u);if(create){printf("{\"create\":%d}\n",create);return 1;}AURenderCallbackStruct cb={render,NULL};OSStatus callback=AudioUnitSetProperty(u,kAudioUnitProperty_SetRenderCallback,kAudioUnitScope_Input,0,&cb,sizeof(cb));OSStatus init=AudioUnitInitialize(u),start=init?init:AudioOutputUnitStart(u);struct timespec begin,end;clock_gettime(CLOCK_MONOTONIC,&begin);for(int i=0;i<30;i++){CFRunLoopRunInMode(kCFRunLoopDefaultMode,.001,false);usleep(100000);}clock_gettime(CLOCK_MONOTONIC,&end);double seconds=(end.tv_sec-begin.tv_sec)+(end.tv_nsec-begin.tv_nsec)/1e9;printf("{\"create\":%d,\"callback\":%d,\"initialize\":%d,\"start\":%d,\"seconds\":%.6f,\"render_callbacks\":%lu,\"frames\":%lu,\"scope\":\"three-second all-zero CoreAudio output-unit RCA, not audio clock proof\"}\n",create,callback,init,start,seconds,atomic_load(&calls),atomic_load(&frames));AudioOutputUnitStop(u);AudioUnitUninitialize(u);AudioComponentInstanceDispose(u);return start?2:0;}

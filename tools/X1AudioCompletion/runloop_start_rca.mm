#import <AppKit/AppKit.h>
#include <pthread.h>
#include <unistd.h>
#include <AudioToolbox/AudioToolbox.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdatomic.h>
#include <time.h>
static unsigned char *pcm;static size_t length,offset;static _Atomic unsigned long filled;
static double now(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return t.tv_sec+t.tv_nsec/1e9;}
static void fill(void *u,AudioQueueRef queue,AudioQueueBufferRef buffer){
    unsigned char *dst=(unsigned char*)buffer->mAudioData;size_t n=buffer->mAudioDataBytesCapacity;
    for(size_t i=0;i<n;i++){dst[i]=pcm[offset++];if(offset==length)offset=0;}
    buffer->mAudioDataByteSize=(UInt32)n;AudioQueueEnqueueBuffer(queue,buffer,0,NULL);atomic_fetch_add(&filled,n/4);
}
int runProbe(int argc,char **argv){if(argc<2)return 1;FILE *f=fopen(argv[1],"rb");if(!f)return 2;fseek(f,0,SEEK_END);length=ftell(f);rewind(f);pcm=(unsigned char*)malloc(length);fread(pcm,1,length,f);fclose(f); for(size_t j=0;j<length;j++)pcm[j]=0;
    AudioStreamBasicDescription format={0};format.mSampleRate=48000;format.mFormatID=kAudioFormatLinearPCM;format.mFormatFlags=kAudioFormatFlagIsFloat|kAudioFormatFlagIsPacked;format.mBytesPerPacket=4;format.mFramesPerPacket=1;format.mBytesPerFrame=4;format.mChannelsPerFrame=1;format.mBitsPerChannel=32;
    AudioQueueRef queue=NULL;OSStatus status=AudioQueueNewOutput(&format,fill,NULL,NULL,NULL,0,&queue);if(status){printf("{\"error\":%d}\n",(int)status);return 3;}
    AudioQueueSetParameter(queue,kAudioQueueParam_Volume,1); // No audible output.
    for(int i=0;i<3;i++){AudioQueueBufferRef b;AudioQueueAllocateBuffer(queue,4800*4,&b);fill(NULL,queue,b);}
    status=AudioQueueStart(queue,NULL);if(status){printf("{\"start_status\":%d}\n",(int)status);return 4;}setbuf(stdout,NULL);
    double start=now();for(int i=0;i<10;i++){
        CFRunLoopRunInMode(kCFRunLoopDefaultMode,.1,false);AudioTimeStamp stamp={0};Boolean discontinuity=false;
        status=AudioQueueGetCurrentTime(queue,NULL,&stamp,&discontinuity);
        printf("{\"index\":%d,\"wall_seconds\":%.9f,\"sample_time\":%.6f,\"sample_rate\":48000,\"host_time\":%llu,\"flags\":%u,\"status\":%d,\"discontinuity\":%d,\"enqueued_frames\":%lu}\n",i,now()-start,stamp.mSampleTime,(unsigned long long)stamp.mHostTime,(unsigned)stamp.mFlags,(int)status,(int)discontinuity,atomic_load(&filled));
    }
    AudioQueueStop(queue,true);AudioQueueDispose(queue,true);free(pcm);return 0;
}

static int aa;static char**vv;static volatile int done=0,result=0;static void*run(void*x){result=runProbe(aa,vv);done=1;return NULL;}
int main(int argc,char**argv){@autoreleasepool{[NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];aa=argc;vv=argv;pthread_t t;pthread_create(&t,NULL,run,NULL);while(!done){CFRunLoopRunInMode(kCFRunLoopDefaultMode,.01,false);}pthread_join(t,NULL);return result;}}

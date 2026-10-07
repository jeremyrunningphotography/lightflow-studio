#import <Foundation/Foundation.h>
#import <CoreAudio/CoreAudio.h>
#import <AudioToolbox/AudioToolbox.h>
#import <CommonCrypto/CommonDigest.h>
#include <atomic>
#include <chrono>
#include <thread>
static std::atomic<unsigned> calls{0};static std::atomic<bool> active{true};static int mode=0;static double mono(){return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();}
static void log(const char*api,OSStatus s){printf("{\"api\":\"%s\",\"status\":%d}\n",api,s);}
static NSString* normalized(CFStringRef uid){NSString*t=(__bridge NSString*)uid;NSData*d=[t dataUsingEncoding:NSUTF8StringEncoding];unsigned char h[32];CC_SHA256(d.bytes,(CC_LONG)d.length,h);NSMutableString*r=[NSMutableString stringWithString:@"normalized-"];for(int i=0;i<8;i++)[r appendFormat:@"%02x",h[i]];return r;}
static void cb(void*u,AudioQueueRef q,AudioQueueBufferRef b){calls++;if(active&&mode!=3)log("refill",AudioQueueEnqueueBuffer(q,b,0,NULL));}
int main(int argc,char**argv){setbuf(stdout,NULL);@autoreleasepool{mode=argc>1?atoi(argv[1]):0;id activity=nil;if(mode==2)activity=[[NSProcessInfo processInfo] beginActivityWithOptions:NSActivityUserInitiatedAllowingIdleSystemSleep reason:@"Bounded X1 native output probe"];
 AudioObjectID device=0;UInt32 n=4;AudioObjectPropertyAddress a={kAudioHardwarePropertyDefaultOutputDevice,kAudioObjectPropertyScopeGlobal,kAudioObjectPropertyElementMain};OSStatus e=AudioObjectGetPropertyData(kAudioObjectSystemObject,&a,0,NULL,&n,&device);log("HAL default output",e);CFStringRef uid=NULL;a.mSelector=kAudioDevicePropertyDeviceUID;n=sizeof(uid);e=AudioObjectGetPropertyData(device,&a,0,NULL,&n,&uid);log("HAL UID",e);
 AudioStreamBasicDescription f={};f.mSampleRate=48000;f.mFormatID=kAudioFormatLinearPCM;f.mFormatFlags=kAudioFormatFlagIsFloat|kAudioFormatFlagIsPacked;f.mBytesPerPacket=f.mBytesPerFrame=4;f.mFramesPerPacket=1;f.mChannelsPerFrame=1;f.mBitsPerChannel=32;AudioQueueRef q=NULL;e=AudioQueueNewOutput(&f,cb,NULL,NULL,NULL,0,&q);log("new",e);if(e)return 1;
 if(mode==1){e=AudioQueueSetProperty(q,kAudioQueueProperty_CurrentDevice,&uid,sizeof(uid));log("set queue to same default UID",e);}
 CFStringRef current=NULL;n=sizeof(current);e=AudioQueueGetProperty(q,kAudioQueueProperty_CurrentDevice,&current,&n);printf("{\"api\":\"queue UID\",\"status\":%d,\"sameDefault\":%d,\"uid\":\"%s\"}\n",e,current&&uid&&CFEqual(current,uid),current?normalized(current).UTF8String:"");if(current)CFRelease(current);
 Float64 rate=0;n=sizeof(rate);e=AudioQueueGetProperty(q,kAudioQueueDeviceProperty_SampleRate,&rate,&n);printf("{\"api\":\"queue device rate\",\"status\":%d,\"value\":%.0f}\n",e,rate);
 for(int i=0;i<3;i++){AudioQueueBufferRef b=NULL;e=AudioQueueAllocateBuffer(q,4096,&b);log("allocate",e);if(e)return 2;memset(b->mAudioData,0,4096);b->mAudioDataByteSize=4096;e=AudioQueueEnqueueBuffer(q,b,0,NULL);log("enqueue",e);if(e)return 3;}
 double t=mono();e=AudioQueueStart(q,NULL);printf("{\"api\":\"start\",\"status\":%d,\"ms\":%.3f,\"mode\":%d,\"taskPID\":%d}\n",e,(mono()-t)*1000,mode,getpid());OSStatus started=e;
 for(int i=0;i<30&&!started;i++){std::this_thread::sleep_for(std::chrono::milliseconds(10));UInt32 running=0;n=4;OSStatus r=AudioQueueGetProperty(q,kAudioQueueProperty_IsRunning,&running,&n);AudioTimeStamp ts={};Boolean disc=false;OSStatus c=AudioQueueGetCurrentTime(q,NULL,&ts,&disc);printf("{\"poll\":%d,\"runningStatus\":%d,\"running\":%u,\"timeStatus\":%d,\"raw\":%.0f,\"callbacks\":%u,\"flags\":%u}\n",i,r,running,c,ts.mSampleTime,calls.load(),ts.mFlags);}
 active=false;log("stop",AudioQueueStop(q,true));log("dispose",AudioQueueDispose(q,true));if(uid)CFRelease(uid);if(activity)[[NSProcessInfo processInfo] endActivity:activity];return started?4:0;}}

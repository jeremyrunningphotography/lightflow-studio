#import <AVFoundation/AVFoundation.h>
#import <ImageIO/ImageIO.h>
#include <time.h>
#include <sys/resource.h>
static double milliseconds(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC_RAW,&t);return t.tv_sec*1000.+t.tv_nsec/1000000.;}
int main(int argc,const char **argv){
    @autoreleasepool {
        if(argc!=4)return 64;
        NSString *source=@(argv[1]),*output=@(argv[2]);NSString *task=@(getenv("LF036_TASK_ROOT")?:"");
        if(!task.length || ![source hasPrefix:[task stringByAppendingString:@"/local-media/"]] || ![output hasPrefix:[task stringByAppendingString:@"/private/"]])return 64;
        double started=milliseconds(),requested=strtod(argv[3],NULL);
        AVURLAsset *asset=[AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:source] options:@{AVURLAssetPreferPreciseDurationAndTimingKey:@YES}];
        double initialized=milliseconds();dispatch_semaphore_t metadata=dispatch_semaphore_create(0);
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        [asset loadValuesAsynchronouslyForKeys:@[@"duration",@"tracks"] completionHandler:^{dispatch_semaphore_signal(metadata);}];
        if(dispatch_semaphore_wait(metadata,dispatch_time(DISPATCH_TIME_NOW,30*NSEC_PER_SEC))!=0){[asset cancelLoading];return 2;}
        NSError *error=nil;if([asset statusOfValueForKey:@"duration" error:&error]!=AVKeyValueStatusLoaded)return 3;
        double loaded=milliseconds();AVAssetImageGenerator *generator=[[AVAssetImageGenerator alloc]initWithAsset:asset];generator.maximumSize=CGSizeMake(512,512);generator.appliesPreferredTrackTransform=YES;
        generator.requestedTimeToleranceBefore=CMTimeMake(1,120);generator.requestedTimeToleranceAfter=CMTimeMake(1,120);
        dispatch_semaphore_t done=dispatch_semaphore_create(0);__block CGImageRef frame=NULL;__block CMTime actual=kCMTimeInvalid;
        [generator generateCGImagesAsynchronouslyForTimes:@[[NSValue valueWithCMTime:CMTimeMakeWithSeconds(requested,10000000)]] completionHandler:^(CMTime req,CGImageRef image,CMTime timestamp,AVAssetImageGeneratorResult result,NSError *failure){(void)req;(void)failure;if(result==AVAssetImageGeneratorSucceeded&&image){frame=CGImageRetain(image);actual=timestamp;}dispatch_semaphore_signal(done);}];
        if(dispatch_semaphore_wait(done,dispatch_time(DISPATCH_TIME_NOW,30*NSEC_PER_SEC))!=0){[generator cancelAllCGImageGeneration];dispatch_semaphore_wait(done,DISPATCH_TIME_FOREVER);return 4;}
        if(!frame)return 5;double decoded=milliseconds();
        CGImageDestinationRef destination=CGImageDestinationCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:output],CFSTR("public.png"),1,NULL);if(!destination){CGImageRelease(frame);return 6;}
        CGImageDestinationAddImage(destination,frame,NULL);BOOL ok=CGImageDestinationFinalize(destination);CFRelease(destination);CGImageRelease(frame);
        struct rusage usage;getrusage(RUSAGE_SELF,&usage);
        NSDictionary *result=@{@"initializationMs":@(initialized-started),@"metadataMs":@(loaded-initialized),@"seekDecodeMs":@(decoded-loaded),@"pngMs":@(milliseconds()-decoded),@"totalMs":@(milliseconds()-started),@"requested":@(requested),@"actualValue":@(actual.value),@"actualTimescale":@(actual.timescale),@"duration":@(CMTimeGetSeconds(asset.duration)),@"maxRss":@(usage.ru_maxrss)};
#pragma clang diagnostic pop
        NSData *json=[NSJSONSerialization dataWithJSONObject:result options:NSJSONWritingPrettyPrinted error:NULL];fwrite(json.bytes,1,json.length,stdout);return ok?0:7;
    }
}

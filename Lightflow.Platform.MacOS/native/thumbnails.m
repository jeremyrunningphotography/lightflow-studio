#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <ImageIO/ImageIO.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <CoreGraphics/CoreGraphics.h>
#include <sys/stat.h>
#include <sys/resource.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdatomic.h>
#include <time.h>

// Per-operation diagnostics contain no paths or media metadata.
typedef struct { double openMs, initializationMs, metadataMs, frameMs, pixelsMs, readMs, totalMs; long long bytesRead, readCalls, requests; int activeReaders, metadataStatus; long long errorCode; } LFThumbnailTrace;
static double previewMilliseconds(void) { struct timespec now;clock_gettime(CLOCK_MONOTONIC_RAW,&now);return now.tv_sec*1000.0+now.tv_nsec/1000000.0; }

// No asynchronous decoder may reopen the media pathname and implicitly hydrate it.
// AVFoundation reads through a resource loader over this admitted, non-following descriptor.
static atomic_int previewReaders=0;
int lf_preview_readers(void) { return atomic_load(&previewReaders); }
int lf_preview_begin(void) {
    int old=getiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES,IOPOL_SCOPE_THREAD);
    if(old<0 || setiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES,IOPOL_SCOPE_THREAD,IOPOL_MATERIALIZE_DATALESS_FILES_OFF)!=0) return -1;
    return old;
}
int lf_preview_end(int old) { return setiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES,IOPOL_SCOPE_THREAD,old); }
static BOOL cancelled(const int *flag) { return __atomic_load_n(flag,__ATOMIC_ACQUIRE)!=0; }
static int admitted(const char *path) {
    struct stat s;
    if(lstat(path,&s)!=0 || !S_ISREG(s.st_mode) || (s.st_flags&SF_DATALESS)!=0) return -1;
    int old=lf_preview_begin(); if(old<0)return -1;
    int fd=open(path,O_RDONLY|O_NOFOLLOW|O_NONBLOCK);
    int restored=lf_preview_end(old);
    struct stat actual;
    if(fd>=0 && (restored!=0 || fstat(fd,&actual)!=0 || s.st_dev!=actual.st_dev || s.st_ino!=actual.st_ino || (actual.st_flags&SF_DATALESS)!=0)) {close(fd);fd=-1;}
    return fd;
}
int lf_preview_open(const char *path) { int fd=admitted(path);if(fd>=0)atomic_fetch_add(&previewReaders,1);return fd; }
void lf_preview_close(int fd) { if(fd>=0){close(fd);atomic_fetch_sub(&previewReaders,1);} }
@interface LFPreviewReader : NSObject<AVAssetResourceLoaderDelegate>
@property(nonatomic) int descriptor;
@property(nonatomic) LFThumbnailTrace *trace;
@property(nonatomic) const int *cancel;
@property(nonatomic) long long length;
@property(nonatomic,strong) NSString *type;
@property(nonatomic,strong) dispatch_queue_t queue;
@property(nonatomic,strong) dispatch_group_t reads;
@property(atomic) BOOL stopping;
@end
@implementation LFPreviewReader
- (instancetype)init { self=[super init];if(self)_descriptor=-1;return self; }
- (void)scheduleRead:(AVAssetResourceLoadingRequest *)request {
    // Return the delegate callback immediately. Each bounded chunk yields to new ranges/cancellations.
    dispatch_group_async(self.reads,self.queue, ^{
        @autoreleasepool {
            if(self.stopping || request.cancelled || request.finished)return;
            if(cancelled(self.cancel)) {
                [request finishLoadingWithError:[NSError errorWithDomain:NSURLErrorDomain code:NSURLErrorCancelled userInfo:nil]];return;
            }
            AVAssetResourceLoadingDataRequest *data=request.dataRequest;
            long long offset=MAX(data.currentOffset,data.requestedOffset);
            long long end=data.requestsAllDataToEndOfResource ? self.length : MIN(self.length,data.requestedOffset+data.requestedLength);
            if(offset<0 || offset>end) {
                [request finishLoadingWithError:[NSError errorWithDomain:NSPOSIXErrorDomain code:EINVAL userInfo:nil]];return;
            }
            if(offset==end) {[request finishLoading];return;}
            unsigned char bytes[65536];double readStarted=self.trace?previewMilliseconds():0;
            int old=lf_preview_begin();
            ssize_t count=old<0?-1:pread(self.descriptor,bytes,(size_t)MIN((long long)sizeof(bytes),end-offset),offset);
            if(old>=0 && lf_preview_end(old)!=0)count=-1;
            if(self.trace) {self.trace->readMs+=previewMilliseconds()-readStarted;self.trace->readCalls++;if(count>0)self.trace->bytesRead+=count;}
            if(self.stopping || request.cancelled || request.finished)return;
            if(count<=0) {[request finishLoadingWithError:[NSError errorWithDomain:NSPOSIXErrorDomain code:EIO userInfo:nil]];return;}
            [data respondWithData:[NSData dataWithBytes:bytes length:(NSUInteger)count]];
            if(data.currentOffset>=end) {if(!request.cancelled && !request.finished)[request finishLoading];}
            else if(!self.stopping)[self scheduleRead:request];
        }
    });
}
- (BOOL)resourceLoader:(AVAssetResourceLoader *)loader shouldWaitForLoadingOfRequestedResource:(AVAssetResourceLoadingRequest *)request {
    (void)loader;
    if(self.stopping || request.cancelled || request.finished)return NO;
    if(self.trace)self.trace->requests++;
    if(request.contentInformationRequest) {
        request.contentInformationRequest.contentType=self.type;
        request.contentInformationRequest.contentLength=self.length;
        request.contentInformationRequest.byteRangeAccessSupported=YES;
    }
    if(request.dataRequest)[self scheduleRead:request];
    else [request finishLoading];
    return YES;
}
@end

typedef struct { double duration; int width,height,orientation,mechanism; long long actualValue; int actualScale; } LFThumbnailInfo;
static BOOL saveImage(CGImageRef image,NSString *output,LFThumbnailInfo *info) {
    if(!image)return NO;
    size_t w=CGImageGetWidth(image),h=CGImageGetHeight(image);
    double scale=MIN(1.,512./MAX(w,h)); w=MAX(1,(size_t)llround(w*scale));h=MAX(1,(size_t)llround(h*scale));
    CGColorSpaceRef color=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context=CGBitmapContextCreate(NULL,w,h,8,w*4,color,kCGImageAlphaPremultipliedLast|kCGBitmapByteOrder32Big);
    CGColorSpaceRelease(color);if(!context)return NO;
    CGContextSetInterpolationQuality(context,kCGInterpolationHigh);CGContextDrawImage(context,CGRectMake(0,0,w,h),image);
    CGImageRef normalized=CGBitmapContextCreateImage(context);CGContextRelease(context);
    CGImageDestinationRef destination=CGImageDestinationCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:output],CFSTR("public.png"),1,NULL);
    BOOL ok=NO;if(destination) {CGImageDestinationAddImage(destination,normalized,NULL);ok=CGImageDestinationFinalize(destination);CFRelease(destination);}
    CGImageRelease(normalized);info->width=(int)w;info->height=(int)h;return ok;
}
// mode 0: video metadata; mode 1: image thumbnail; mode 2: video poster; mode 3: image properties only.
int lf_thumbnail_profile(const char *path,int mode,double requested,const char *output,const int *cancel,LFThumbnailInfo *info,LFThumbnailTrace *trace) {
    double started=trace?previewMilliseconds():0;if(trace)memset(trace,0,sizeof(*trace));
    @autoreleasepool {
        memset(info,0,sizeof(*info));int fd=admitted(path);if(trace)trace->openMs=previewMilliseconds()-started;if(fd<0)return 2;
        int active=atomic_fetch_add(&previewReaders,1)+1;if(trace)trace->activeReaders=active;
        int status=3;
        @try {
            if(mode==1 || mode==3) {
                // ImageIO owns a dup of the already-admitted descriptor, with materialization disabled on this thread.
                int old=lf_preview_begin();
                if(old<0) {status=2;return status;}
                @try {
                NSString *descriptor=[NSString stringWithFormat:@"/dev/fd/%d",fd];
                UTType *hint=[UTType typeWithFilenameExtension:[@(path) pathExtension]];
                NSDictionary *sourceOptions=hint?@{(__bridge NSString *)kCGImageSourceTypeIdentifierHint:hint.identifier}:@{};
                CGImageSourceRef source=CGImageSourceCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:descriptor],(__bridge CFDictionaryRef)sourceOptions);
                if(source) {
                    NSDictionary *properties=CFBridgingRelease(CGImageSourceCopyPropertiesAtIndex(source,0,NULL));
                    info->orientation=[properties[(__bridge NSString *)kCGImagePropertyOrientation] intValue];
                    if(mode==3) {
                        info->width=[properties[(__bridge NSString *)kCGImagePropertyPixelWidth] intValue];
                        info->height=[properties[(__bridge NSString *)kCGImagePropertyPixelHeight] intValue];
                        status=cancelled(cancel)?1:(info->width>0 && info->height>0 ? 0 : 3);
                    } else {
                        NSDictionary *options=@{(__bridge NSString *)kCGImageSourceThumbnailMaxPixelSize:@512,(__bridge NSString *)kCGImageSourceCreateThumbnailWithTransform:@YES,(__bridge NSString *)kCGImageSourceShouldCacheImmediately:@YES};
                        CGImageRef image=CGImageSourceCreateThumbnailAtIndex(source,0,(__bridge CFDictionaryRef)options);
                        info->mechanism=1; // existing embedded preview
                        if(!image) {
                            NSMutableDictionary *fallback=[options mutableCopy];fallback[(__bridge NSString *)kCGImageSourceCreateThumbnailFromImageAlways]=@YES;
                            image=CGImageSourceCreateThumbnailAtIndex(source,0,(__bridge CFDictionaryRef)fallback);info->mechanism=2;
                        }
                        if(image) {status=cancelled(cancel)?1:(saveImage(image,@(output),info)?0:4);CGImageRelease(image);}
                    }
                    CFRelease(source);
                }
                } @finally {if(lf_preview_end(old)!=0)status=2;}
            } else {
                double initialized=trace?previewMilliseconds():0;
                LFPreviewReader *reader=[LFPreviewReader new];reader.descriptor=fd;reader.cancel=cancel;reader.trace=trace;
                struct stat facts;if(fstat(fd,&facts)!=0)return 2;reader.length=facts.st_size;
                reader.type=[UTType typeWithFilenameExtension:[@(path) pathExtension]].identifier ?: @"public.movie";
                dispatch_queue_t queue=dispatch_queue_create("studio.lightflow.preview.source",DISPATCH_QUEUE_SERIAL);
                AVURLAsset *asset=[AVURLAsset URLAssetWithURL:[NSURL URLWithString:@"lightflow-resident://source"] options:@{AVURLAssetPreferPreciseDurationAndTimingKey:@YES}];
                reader.queue=queue;reader.reads=dispatch_group_create();
                [asset.resourceLoader setDelegate:reader queue:queue];
                dispatch_semaphore_t loaded=dispatch_semaphore_create(0);
                if(trace)trace->initializationMs=previewMilliseconds()-initialized;
                double metadataStarted=trace?previewMilliseconds():0;
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                [asset loadValuesAsynchronouslyForKeys:@[@"duration",@"tracks"] completionHandler:^{dispatch_semaphore_signal(loaded);}];
                while(dispatch_semaphore_wait(loaded,dispatch_time(DISPATCH_TIME_NOW,50*NSEC_PER_MSEC))!=0) if(cancelled(cancel)) [asset cancelLoading];
                NSError *error=nil;
                AVKeyValueStatus metadataStatus=[asset statusOfValueForKey:@"duration" error:&error];
                if(trace){trace->metadataMs=previewMilliseconds()-metadataStarted;trace->metadataStatus=(int)metadataStatus;trace->errorCode=error.code;}
                if(cancelled(cancel))status=1;
                else if(metadataStatus!=AVKeyValueStatusLoaded)status=3;
                else {
                    info->duration=CMTimeGetSeconds(asset.duration);
                    if(!isfinite(info->duration)||info->duration<=0)info->duration=0;
                    if(mode==0) {
                        AVAssetTrack *track=[[asset tracksWithMediaType:AVMediaTypeVideo] firstObject];
                        CGSize size=track.naturalSize;
                        if(isfinite(size.width) && isfinite(size.height) && size.width>0 && size.height>0 && size.width<=INT_MAX && size.height<=INT_MAX) {
                            info->width=(int)size.width;info->height=(int)size.height;
                        }
                        status=0;
                    }
                    else {
                        double frameStarted=trace?previewMilliseconds():0;
                        AVAssetImageGenerator *generator=[[AVAssetImageGenerator alloc] initWithAsset:asset];
                        generator.appliesPreferredTrackTransform=YES;generator.maximumSize=CGSizeMake(512,512);
                        // Small time window, not default infinite keyframe tolerance. Actual PTS is always recorded.
                        generator.requestedTimeToleranceBefore=CMTimeMake(1,120);
                        generator.requestedTimeToleranceAfter=CMTimeMake(1,120);
                        dispatch_semaphore_t done=dispatch_semaphore_create(0);__block CGImageRef frame=NULL;__block CMTime actual=kCMTimeInvalid;
                        [generator generateCGImagesAsynchronouslyForTimes:@[[NSValue valueWithCMTime:CMTimeMakeWithSeconds(requested,10000000)]] completionHandler:^(CMTime req,CGImageRef image,CMTime time,AVAssetImageGeneratorResult result,NSError *failure) {
                            (void)req;(void)failure;if(result==AVAssetImageGeneratorSucceeded && image) {frame=CGImageRetain(image);actual=time;}dispatch_semaphore_signal(done);
                        }];
                        while(dispatch_semaphore_wait(done,dispatch_time(DISPATCH_TIME_NOW,50*NSEC_PER_MSEC))!=0) if(cancelled(cancel)) [generator cancelAllCGImageGeneration];
                        if(trace)trace->frameMs=previewMilliseconds()-frameStarted;
                        double pixelsStarted=trace?previewMilliseconds():0;
                        if(cancelled(cancel))status=1;
                        else if(frame) {info->mechanism=3;info->actualValue=actual.value;info->actualScale=actual.timescale;status=saveImage(frame,@(output),info)?0:4;}
                        if(frame)CGImageRelease(frame);
                        if(trace)trace->pixelsMs=previewMilliseconds()-pixelsStarted;
                    }
                }
#pragma clang diagnostic pop
                [asset cancelLoading];reader.stopping=YES;[asset.resourceLoader setDelegate:nil queue:NULL];
                dispatch_sync(queue, ^{});
                dispatch_group_wait(reader.reads,DISPATCH_TIME_FOREVER);
                dispatch_sync(queue, ^{}); // No source read can outlive fd/cancel/trace or release its worker early.
            }
        } @finally {close(fd);atomic_fetch_sub(&previewReaders,1);if(trace)trace->totalMs=previewMilliseconds()-started;}
        return status;
    }
}

int lf_thumbnail(const char *path,int mode,double requested,const char *output,const int *cancel,LFThumbnailInfo *info) {return lf_thumbnail_profile(path,mode,requested,output,cancel,info,NULL);}

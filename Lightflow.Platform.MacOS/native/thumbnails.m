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
@property(nonatomic) const int *cancel;
@property(nonatomic) long long length;
@property(nonatomic,strong) NSString *type;
@end
@implementation LFPreviewReader
- (instancetype)init { self=[super init];if(self)_descriptor=-1;return self; }
- (BOOL)resourceLoader:(AVAssetResourceLoader *)loader shouldWaitForLoadingOfRequestedResource:(AVAssetResourceLoadingRequest *)request {
    (void)loader;
    @autoreleasepool {
        if(request.contentInformationRequest) {
            request.contentInformationRequest.contentType=self.type;
            request.contentInformationRequest.contentLength=self.length;
            request.contentInformationRequest.byteRangeAccessSupported=YES;
        }
        AVAssetResourceLoadingDataRequest *data=request.dataRequest;
        if(data) {
            long long offset=MAX(data.currentOffset,data.requestedOffset);
            long long end=data.requestsAllDataToEndOfResource ? self.length : MIN(self.length,data.requestedOffset+data.requestedLength);
            unsigned char bytes[65536];
            while(offset<end) {
                if(cancelled(self.cancel)) { [request finishLoadingWithError:[NSError errorWithDomain:NSURLErrorDomain code:NSURLErrorCancelled userInfo:nil]];return YES; }
                int old=lf_preview_begin();
                ssize_t count=old<0 ? -1 : pread(self.descriptor,bytes,(size_t)MIN((long long)sizeof(bytes),end-offset),offset);
                if(old>=0 && lf_preview_end(old)!=0) count=-1;
                if(count<=0) { [request finishLoadingWithError:[NSError errorWithDomain:NSPOSIXErrorDomain code:EIO userInfo:nil]];return YES; }
                @autoreleasepool { [data respondWithData:[NSData dataWithBytes:bytes length:(NSUInteger)count]]; }
                offset+=count;
            }
        }
        [request finishLoading];return YES;
    }
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
// mode 0: video metadata; mode 1: image thumbnail; mode 2: video poster.
int lf_thumbnail(const char *path,int mode,double requested,const char *output,const int *cancel,LFThumbnailInfo *info) {
    @autoreleasepool {
        memset(info,0,sizeof(*info));int fd=admitted(path);if(fd<0)return 2;
        atomic_fetch_add(&previewReaders,1);
        int status=3;
        @try {
            if(mode==1) {
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
                    NSDictionary *options=@{(__bridge NSString *)kCGImageSourceThumbnailMaxPixelSize:@512,(__bridge NSString *)kCGImageSourceCreateThumbnailWithTransform:@YES,(__bridge NSString *)kCGImageSourceShouldCacheImmediately:@YES};
                    CGImageRef image=CGImageSourceCreateThumbnailAtIndex(source,0,(__bridge CFDictionaryRef)options);
                    info->mechanism=1; // existing embedded preview
                    if(!image) {
                        NSMutableDictionary *fallback=[options mutableCopy];fallback[(__bridge NSString *)kCGImageSourceCreateThumbnailFromImageAlways]=@YES;
                        image=CGImageSourceCreateThumbnailAtIndex(source,0,(__bridge CFDictionaryRef)fallback);info->mechanism=2;
                    }
                    if(image) {status=cancelled(cancel)?1:(saveImage(image,@(output),info)?0:4);CGImageRelease(image);}
                    CFRelease(source);
                }
                } @finally {if(lf_preview_end(old)!=0)status=2;}
            } else {
                LFPreviewReader *reader=[LFPreviewReader new];reader.descriptor=fd;reader.cancel=cancel;
                struct stat facts;if(fstat(fd,&facts)!=0)return 2;reader.length=facts.st_size;
                reader.type=[UTType typeWithFilenameExtension:[@(path) pathExtension]].identifier ?: @"public.movie";
                dispatch_queue_t queue=dispatch_queue_create("studio.lightflow.preview.source",DISPATCH_QUEUE_SERIAL);
                AVURLAsset *asset=[AVURLAsset URLAssetWithURL:[NSURL URLWithString:@"lightflow-resident://source"] options:@{AVURLAssetPreferPreciseDurationAndTimingKey:@YES}];
                [asset.resourceLoader setDelegate:reader queue:queue];
                dispatch_semaphore_t loaded=dispatch_semaphore_create(0);
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                [asset loadValuesAsynchronouslyForKeys:@[@"duration",@"tracks"] completionHandler:^{dispatch_semaphore_signal(loaded);}];
                while(dispatch_semaphore_wait(loaded,dispatch_time(DISPATCH_TIME_NOW,50*NSEC_PER_MSEC))!=0) if(cancelled(cancel)) [asset cancelLoading];
                NSError *error=nil;
                if(cancelled(cancel))status=1;
                else if([asset statusOfValueForKey:@"duration" error:&error]!=AVKeyValueStatusLoaded)status=3;
                else {
                    info->duration=CMTimeGetSeconds(asset.duration);
                    if(!isfinite(info->duration)||info->duration<=0)info->duration=0;
                    if(mode==0)status=0;
                    else {
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
                        if(cancelled(cancel))status=1;
                        else if(frame) {info->mechanism=3;info->actualValue=actual.value;info->actualScale=actual.timescale;status=saveImage(frame,@(output),info)?0:4;}
                        if(frame)CGImageRelease(frame);
                    }
                }
#pragma clang diagnostic pop
                [asset cancelLoading];[asset.resourceLoader setDelegate:nil queue:NULL];
                dispatch_sync(queue, ^{}); // Drain resource callbacks before fd/cancel lifetime ends.
            }
        } @finally {close(fd);atomic_fetch_sub(&previewReaders,1);}
        return status;
    }
}

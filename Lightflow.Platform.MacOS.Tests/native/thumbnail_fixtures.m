#import <AVFoundation/AVFoundation.h>
#import <CoreVideo/CoreVideo.h>
#include <unistd.h>

// Generated disposable test video only. This helper is never part of the application bundle.
int lf_test_thumbnail_video(const char *path) {
    @autoreleasepool {
        NSError *error=nil;
        AVAssetWriter *writer=[[AVAssetWriter alloc] initWithURL:[NSURL fileURLWithPath:@(path)] fileType:AVFileTypeMPEG4 error:&error];
        if(!writer)return 1;
        writer.shouldOptimizeForNetworkUse=NO;
        AVAssetWriterInput *input=[AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeVideo outputSettings:@{AVVideoCodecKey:AVVideoCodecTypeH264,AVVideoWidthKey:@320,AVVideoHeightKey:@180}];
        AVAssetWriterInputPixelBufferAdaptor *adaptor=[AVAssetWriterInputPixelBufferAdaptor assetWriterInputPixelBufferAdaptorWithAssetWriterInput:input sourcePixelBufferAttributes:@{(id)kCVPixelBufferPixelFormatTypeKey:@(kCVPixelFormatType_32ARGB),(id)kCVPixelBufferWidthKey:@320,(id)kCVPixelBufferHeightKey:@180}];
        if(![writer canAddInput:input])return 2;
        [writer addInput:input];if(![writer startWriting])return 3;[writer startSessionAtSourceTime:kCMTimeZero];
        for(int frame=0;frame<90;frame++) {
            int waits=0;while(!input.readyForMoreMediaData && writer.status==AVAssetWriterStatusWriting && waits++<10000)usleep(1000);
            if(!input.readyForMoreMediaData){[writer cancelWriting];return 4;}
            CVPixelBufferRef buffer=NULL;
            if(CVPixelBufferPoolCreatePixelBuffer(NULL,adaptor.pixelBufferPool,&buffer)!=kCVReturnSuccess){[writer cancelWriting];return 5;}
            CVPixelBufferLockBaseAddress(buffer,0);unsigned char *bytes=CVPixelBufferGetBaseAddress(buffer);size_t stride=CVPixelBufferGetBytesPerRow(buffer);
            for(int y=0;y<180;y++)for(int x=0;x<320;x++){unsigned char *p=bytes+y*stride+x*4;p[0]=255;p[1]=(unsigned char)(frame*2);p[2]=(unsigned char)x;p[3]=(unsigned char)y;}
            CVPixelBufferUnlockBaseAddress(buffer,0);BOOL ok=[adaptor appendPixelBuffer:buffer withPresentationTime:CMTimeMake(frame,30)];CVPixelBufferRelease(buffer);
            if(!ok){[writer cancelWriting];return 6;}
        }
        [input markAsFinished];dispatch_semaphore_t done=dispatch_semaphore_create(0);[writer finishWritingWithCompletionHandler:^{dispatch_semaphore_signal(done);}];
        if(dispatch_semaphore_wait(done,dispatch_time(DISPATCH_TIME_NOW,10*NSEC_PER_SEC))!=0){[writer cancelWriting];return 7;}
        return writer.status==AVAssetWriterStatusCompleted?0:8;
    }
}

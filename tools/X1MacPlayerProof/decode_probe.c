#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
#include <libavutil/display.h>
#include <libswscale/swscale.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <time.h>
#ifdef __OBJC__
#import <Metal/Metal.h>
#import <CoreVideo/CoreVideo.h>
static void metal_bridge(AVFrame *frame){
    CVPixelBufferRef pb=(CVPixelBufferRef)frame->data[3];
    id<MTLDevice> device=MTLCreateSystemDefaultDevice();CVMetalTextureCacheRef cache=NULL;
    CVReturn a=CVMetalTextureCacheCreate(NULL,NULL,device,NULL,&cache);
    CVMetalTextureRef y=NULL,uv=NULL;
    CVReturn b=a==0?CVMetalTextureCacheCreateTextureFromImage(NULL,cache,pb,NULL,MTLPixelFormatR8Unorm,CVPixelBufferGetWidthOfPlane(pb,0),CVPixelBufferGetHeightOfPlane(pb,0),0,&y):a;
    CVReturn c=a==0?CVMetalTextureCacheCreateTextureFromImage(NULL,cache,pb,NULL,MTLPixelFormatRG8Unorm,CVPixelBufferGetWidthOfPlane(pb,1),CVPixelBufferGetHeightOfPlane(pb,1),1,&uv):a;
    fprintf(stderr,"Metal bridge: device=%s format=%u planes=%zu cache=%d Y=%d UV=%d textures=%d\n",device.name.UTF8String,(unsigned)CVPixelBufferGetPixelFormatType(pb),CVPixelBufferGetPlaneCount(pb),(int)a,(int)b,(int)c,(y&&uv&&CVMetalTextureGetTexture(y)&&CVMetalTextureGetTexture(uv))?1:0);
    if(y)CFRelease(y);if(uv)CFRelease(uv);if(cache)CFRelease(cache);
}
#endif
static enum AVPixelFormat choose(AVCodecContext *c,const enum AVPixelFormat *f){for(;*f!=AV_PIX_FMT_NONE;f++)if(*f==AV_PIX_FMT_VIDEOTOOLBOX)return *f;return AV_PIX_FMT_NONE;}
static void check(int n){if(n<0){char b[256];av_strerror(n,b,sizeof(b));fprintf(stderr,"%s\n",b);exit(2);}}
static AVFormatContext *fmt;static AVCodecContext *dec;static int stream;static int hw;static AVRational tb;static int save_capture;
static uint64_t hash_frame(AVFrame *frame,int save){
    AVFrame *sw=av_frame_alloc();AVFrame *f=frame;
    if(frame->format==AV_PIX_FMT_VIDEOTOOLBOX){check(av_hwframe_transfer_data(sw,frame,0));f=sw;}
    uint8_t *pixels=av_malloc(f->width*f->height*4);uint8_t *dst[]={pixels,0,0,0};int stride[]={f->width*4,0,0,0};
    struct SwsContext *s=sws_getContext(f->width,f->height,f->format,f->width,f->height,AV_PIX_FMT_BGRA,SWS_BILINEAR,0,0,0);
    if(!s)exit(3);sws_scale(s,(const uint8_t *const*)f->data,f->linesize,0,f->height,dst,stride);
    uint64_t h=14695981039346656037ull;for(int j=0;j<f->width*f->height*4;j++){h^=pixels[j];h*=1099511628211ull;}
    if(save){FILE *o=fopen(hw?"work/data/decoded-hardware.bgra":"work/data/decoded.bgra","wb");fwrite(pixels,1,f->width*f->height*4,o);fclose(o);}
    av_free(pixels);sws_freeContext(s);av_frame_free(&sw);return h;
}
static void scan(const char *phase,int64_t target){
    AVPacket *p=av_packet_alloc();AVFrame *f=av_frame_alloc();int end=0,index=0;int64_t previous=AV_NOPTS_VALUE;uint64_t previous_hash=0;
    while(!end){
        int n=av_read_frame(fmt,p);if(n<0){check(avcodec_send_packet(dec,NULL));end=1;}
        else if(p->stream_index==stream){check(avcodec_send_packet(dec,p));}
        av_packet_unref(p);
        while(avcodec_receive_frame(dec,f)==0){
#ifdef __OBJC__
            if(hw && index==0 && !strcmp(phase,"linear"))metal_bridge(f);
#endif
            uint64_t h=hash_frame(f,save_capture && !strcmp(phase,"linear") && index==0);int64_t pts=f->best_effort_timestamp;
            if(!strcmp(phase,"linear"))printf("{\"phase\":\"linear\",\"index\":%d,\"pts\":%lld,\"frame_pts\":%lld,\"seconds\":%.9f,\"hw\":%d,\"hash\":\"%016llx\"}\n",index,(long long)pts,(long long)f->pts,pts*av_q2d(tb),f->format==AV_PIX_FMT_VIDEOTOOLBOX,(unsigned long long)h);
            else if(pts>=target){printf("{\"phase\":\"seek-predecessor\",\"target\":%lld,\"settled\":%lld,\"previous\":%lld,\"decoded_count\":%d,\"previous_hash\":\"%016llx\",\"hash\":\"%016llx\"}\n",(long long)target,(long long)pts,(long long)previous,index+1,(unsigned long long)previous_hash,(unsigned long long)h);av_frame_unref(f);goto done;}
            previous=pts;previous_hash=h;index++;av_frame_unref(f);
        }
    }
done:av_packet_free(&p);av_frame_free(&f);
}
int main(int argc,char **argv){if(argc<3)return 1;hw=atoi(argv[2]);save_capture=argc<4;
    fprintf(stderr,"FFmpeg=%s license=%s configuration=%s\n",av_version_info(),avcodec_license(),avcodec_configuration());
    check(avformat_open_input(&fmt,argv[1],0,0));check(avformat_find_stream_info(fmt,0));stream=av_find_best_stream(fmt,AVMEDIA_TYPE_VIDEO,-1,-1,0,0);check(stream);tb=fmt->streams[stream]->time_base;
    const AVPacketSideData *side=av_packet_side_data_get(fmt->streams[stream]->codecpar->coded_side_data,fmt->streams[stream]->codecpar->nb_coded_side_data,AV_PKT_DATA_DISPLAYMATRIX);
    fprintf(stderr,"source_clockwise_rotation=%.0f start_pts=%lld time_base=%d/%d\n",side?-av_display_rotation_get((int32_t*)side->data):0,(long long)fmt->streams[stream]->start_time,tb.num,tb.den);
    const AVCodec *codec=avcodec_find_decoder(fmt->streams[stream]->codecpar->codec_id);dec=avcodec_alloc_context3(codec);check(avcodec_parameters_to_context(dec,fmt->streams[stream]->codecpar));dec->thread_count=1;
    if(hw){check(av_hwdevice_ctx_create(&dec->hw_device_ctx,AV_HWDEVICE_TYPE_VIDEOTOOLBOX,0,0,0));dec->get_format=choose;}
    check(avcodec_open2(dec,codec,0));scan("linear",0);
    int64_t start=fmt->streams[stream]->start_time;
    for(int i=1;i<=3;i++){
        int64_t target=start+av_rescale_q(i, (AVRational){1,1},tb);
        check(av_seek_frame(fmt,stream,start,AVSEEK_FLAG_BACKWARD));avcodec_flush_buffers(dec);scan("seek",target);
    }
    avcodec_free_context(&dec);avformat_close_input(&fmt);return 0;
}

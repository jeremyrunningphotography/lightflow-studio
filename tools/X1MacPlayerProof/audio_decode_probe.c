#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libswresample/swresample.h>
#include <stdio.h>
#include <stdlib.h>
static void check(int n){if(n<0){char b[128];av_strerror(n,b,sizeof(b));fprintf(stderr,"%s\n",b);exit(2);}}
int main(int argc,char **argv){if(argc<3)return 1;AVFormatContext *fmt=NULL;check(avformat_open_input(&fmt,argv[1],NULL,NULL));check(avformat_find_stream_info(fmt,NULL));int index=av_find_best_stream(fmt,AVMEDIA_TYPE_AUDIO,-1,-1,NULL,0);check(index);
    AVCodecContext *dec=avcodec_alloc_context3(avcodec_find_decoder(fmt->streams[index]->codecpar->codec_id));check(avcodec_parameters_to_context(dec,fmt->streams[index]->codecpar));check(avcodec_open2(dec,dec->codec,NULL));
    SwrContext *swr=NULL;AVChannelLayout mono=AV_CHANNEL_LAYOUT_MONO;check(swr_alloc_set_opts2(&swr,&mono,AV_SAMPLE_FMT_FLT,48000,&dec->ch_layout,dec->sample_fmt,dec->sample_rate,0,NULL));check(swr_init(swr));FILE *output=fopen(argv[2],"wb");if(!output)return 3;
    fprintf(stderr,"FFmpeg=%s license=%s source_rate=%d\n",av_version_info(),avcodec_license(),dec->sample_rate);
    AVPacket *p=av_packet_alloc();AVFrame *frame=av_frame_alloc();int eof=0,count=0;int64_t total=0;
    while(!eof){int n=av_read_frame(fmt,p);if(n<0){check(avcodec_send_packet(dec,NULL));eof=1;}else if(p->stream_index==index)check(avcodec_send_packet(dec,p));av_packet_unref(p);
        while(avcodec_receive_frame(dec,frame)==0){uint8_t *dst=NULL;int capacity=swr_get_out_samples(swr,frame->nb_samples);check(av_samples_alloc(&dst,NULL,1,capacity,AV_SAMPLE_FMT_FLT,0));int samples=swr_convert(swr,&dst,capacity,(const uint8_t **)frame->extended_data,frame->nb_samples);check(samples);fwrite(dst,4,samples,output);total+=samples;
            printf("{\"index\":%d,\"pts\":%lld,\"source_samples\":%d,\"output_samples\":%d,\"total_samples\":%lld}\n",count++,(long long)frame->pts,frame->nb_samples,samples,(long long)total);av_freep(&dst);av_frame_unref(frame);}
    }
    fclose(output);av_packet_free(&p);av_frame_free(&frame);swr_free(&swr);avcodec_free_context(&dec);avformat_close_input(&fmt);return 0;
}

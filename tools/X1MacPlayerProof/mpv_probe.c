#include <mpv/client.h>
#include <mpv/render.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <unistd.h>
#include <time.h>
#include <dlfcn.h>
#include <string.h>

static mpv_handle *m;
static mpv_render_context *r;
static unsigned char pixels[320*180*4] __attribute__((aligned(64)));
static int renders;
static double now(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return t.tv_sec+t.tv_nsec/1e9;}
static void check(int n){if(n<0){fprintf(stderr,"mpv: %s\n",mpv_error_string(n));exit(2);}}
static double prop(const char *name){double v=-999;mpv_get_property(m,name,MPV_FORMAT_DOUBLE,&v);return v;}
static void pump(double seconds){
    double until=now()+seconds;
    do {
        while(mpv_wait_event(m,0)->event_id!=MPV_EVENT_NONE){}
        if(mpv_render_context_update(r)&MPV_RENDER_UPDATE_FRAME){
            int size[]={320,180}; size_t stride=320*4;
            mpv_render_param p[]={
                {MPV_RENDER_PARAM_SW_SIZE,size},{MPV_RENDER_PARAM_SW_FORMAT,"rgb0"},
                {MPV_RENDER_PARAM_SW_STRIDE,&stride},{MPV_RENDER_PARAM_SW_POINTER,pixels},{0,0}};
            check(mpv_render_context_render(r,p));mpv_render_context_report_swap(r);renders++;
        }
        usleep(1000);
    } while(now()<until);
}
static void cmd(const char *a,const char *b,const char *c){const char *v[]={a,b,c,NULL};check(mpv_command(m,v));}
static void record(const char *phase,int i){
    uint64_t h=14695981039346656037ull;for(int j=0;j<sizeof(pixels);j++){h^=pixels[j];h*=1099511628211ull;}
    int pause=0;mpv_get_property(m,"pause",MPV_FORMAT_FLAG,&pause);
    printf("{\"phase\":\"%s\",\"index\":%d,\"video_pts\":%.9f,\"time_pos\":%.9f,\"audio_pts\":%.9f,\"avsync\":%.9f,\"speed\":%.3f,\"volume\":%.1f,\"paused\":%d,\"renders\":%d,\"hash\":\"%016llx\",\"monotonic\":%.9f}\n",phase,i,prop("video-pts"),prop("time-pos"),prop("audio-pts"),prop("avsync"),prop("speed"),prop("volume"),pause,renders,(unsigned long long)h,now());fflush(stdout);
}
int main(int argc,char **argv){
    if(argc<2)return 1;
    m=mpv_create();
    check(mpv_set_option_string(m,"config","no"));check(mpv_set_option_string(m,"vo","libmpv"));
    check(mpv_set_option_string(m,"pause","yes"));check(mpv_set_option_string(m,"keep-open","yes"));
    int audio=argc>3;
    check(mpv_set_option_string(m,"hwdec","no"));check(mpv_set_option_string(m,"audio",audio?"auto":"no"));
    if(audio){check(mpv_set_option_string(m,"ao","coreaudio"));check(mpv_set_option_string(m,"mute","yes"));check(mpv_set_option_string(m,"volume","37"));}
    check(mpv_set_option_string(m,"terminal","no"));check(mpv_initialize(m));
    mpv_render_param init[]={{MPV_RENDER_PARAM_API_TYPE,MPV_RENDER_API_TYPE_SW},{0,0}};
    check(mpv_render_context_create(&r,m,init));
    char *version=mpv_get_property_string(m,"mpv-version");fprintf(stderr,"%s api=%lu\n",version,mpv_client_api_version());mpv_free(version);
    if(!strcmp(argv[1],"--version")){
        const char *(*avversion)(void)=dlsym(RTLD_DEFAULT,"av_version_info");
        const char *(*config)(void)=dlsym(RTLD_DEFAULT,"avcodec_configuration");
        const char *(*license)(void)=dlsym(RTLD_DEFAULT,"avcodec_license");
        printf("FFmpeg=%s\nconfiguration=%s\nlicense=%s\n",avversion?avversion():"unavailable",config?config():"unavailable",license?license():"unavailable");
        mpv_render_context_free(r);mpv_terminate_destroy(m);return 0;
    }
    cmd("loadfile",argv[1],NULL);pump(1);record("open",0);
    if(audio){
        check(mpv_set_property_string(m,"pause","no"));
        for(int i=0;i<125;i++){pump(.2);record("audio-play",i);}
        double rates[]={.125,.25,.5,1,2,4};
        for(int j=0;j<6;j++){check(mpv_set_property(m,"speed",MPV_FORMAT_DOUBLE,&rates[j]));for(int i=0;i<5;i++){pump(.2);record("audio-speed",j*5+i);}}
        check(mpv_set_property_string(m,"pause","yes"));pump(.3);record("audio-pause",0);pump(.5);record("audio-retained",0);
        cmd("frame-step","1","mute");pump(.3);record("audio-silent-step",0);
        check(mpv_set_property_string(m,"ab-loop-a",".5"));check(mpv_set_property_string(m,"ab-loop-b","1.5"));check(mpv_set_property_string(m,"speed","1"));
        cmd("seek",".5","absolute+exact");pump(.3);check(mpv_set_property_string(m,"pause","no"));for(int i=0;i<20;i++){pump(.2);record("audio-loop",i);}
        check(mpv_set_property_string(m,"pause","yes"));cmd("loadfile",argv[1],NULL);pump(.5);record("audio-reopen",0);
        char *ao=mpv_get_property_string(m,"current-ao");fprintf(stderr,"current-ao=%s\n",ao?ao:"unavailable");mpv_free(ao);
        mpv_render_context_free(r);mpv_terminate_destroy(m);return 0;
    }
    int count=argc>2?atoi(argv[2]):15;
    for(int i=1;i<=count;i++){cmd("frame-step",NULL,NULL);pump(.15);record("forward",i);}
    for(int i=1;i<=count;i++){cmd("frame-back-step",NULL,NULL);pump(.15);record("reverse",i);}
    cmd("seek","1.7","absolute+exact");pump(.5);record("seek",0);pump(.5);record("retained",0);
    for(int i=0;i<5;i++){cmd("frame-back-step",NULL,NULL);pump(.2);record("reverse-after-seek",i);}
    cmd("seek","0","absolute+exact");pump(.3);cmd("frame-back-step",NULL,NULL);pump(.2);record("zero-reverse",0);
    cmd("seek","100","absolute+exact");pump(.4);record("eof-seek",0);cmd("frame-step",NULL,NULL);pump(.3);record("eof-forward",0);
    cmd("loadfile",argv[1],NULL);pump(.5);record("reopen",0);
    // Null audio output proves engine clock behavior only, not physical output drift.
    // Audio was disabled at initialization; this run deliberately remains silent.
    check(mpv_set_property_string(m,"pause","no"));
    for(int i=0;i<10;i++){pump(.1);record("play",i);}
    check(mpv_set_property_string(m,"speed","2"));
    for(int i=0;i<6;i++){pump(.1);record("speed2",i);}
    check(mpv_set_property_string(m,"pause","yes"));pump(.2);record("pause",0);
    check(mpv_set_property_string(m,"ab-loop-a",".5"));check(mpv_set_property_string(m,"ab-loop-b","1"));
    cmd("seek",".5","absolute+exact");pump(.2);check(mpv_set_property_string(m,"pause","no"));
    for(int i=0;i<15;i++){pump(.1);record("loop",i);}
    check(mpv_set_property_string(m,"pause","yes"));pump(.2);
    mpv_render_context_free(r);mpv_terminate_destroy(m);return 0;
}

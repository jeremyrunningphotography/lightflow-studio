// Proof-only streaming decode/tempo feeder. AudioQueue callbacks never decode.
#include <mutex>
#include <array>
class StreamFeed {
 AVFormatContext *fmt=nullptr; AVCodecContext *dec=nullptr;
 AVPacket *pkt=av_packet_alloc();AVFrame *in=av_frame_alloc(),*out=av_frame_alloc();
 AVFilterGraph *graph=nullptr;AVFilterContext *src=nullptr,*sink=nullptr;
 int stream=-1;bool draining=false,flushed=false;double begin=0,end=0,rate=1;
 std::deque<float> fifo;
public:
 uint64_t decodedSamples=0,producedSamples=0,decodedFrames=0,maxFIFO=0;
 double inputExtent=0;bool eof=false;
 StreamFeed(const char*path,double origin,double speed,double limit):begin(origin),end(limit),rate(speed){
  ck(avformat_open_input(&fmt,path,0,0));ck(avformat_find_stream_info(fmt,0));
  stream=av_find_best_stream(fmt,AVMEDIA_TYPE_AUDIO,-1,-1,0,0);ck(stream);
  auto st=fmt->streams[stream];dec=avcodec_alloc_context3(avcodec_find_decoder(st->codecpar->codec_id));ck(avcodec_parameters_to_context(dec,st->codecpar));ck(avcodec_open2(dec,dec->codec,0));
  // Preserve AAC priming at stream start; seek earlier for codec preroll elsewhere.
  if(begin>0){ck(av_seek_frame(fmt,stream,av_rescale_q((int64_t)(std::max(0.,begin-1)*48000),(AVRational){1,48000},st->time_base),AVSEEK_FLAG_BACKWARD));avcodec_flush_buffers(dec);}
 }
 ~StreamFeed(){avfilter_graph_free(&graph);av_frame_free(&in);av_frame_free(&out);av_packet_free(&pkt);avcodec_free_context(&dec);avformat_close_input(&fmt);}
 void makeGraph(){
  graph=avfilter_graph_alloc();char layout[128],arg[512];av_channel_layout_describe(&in->ch_layout,layout,sizeof(layout));
  snprintf(arg,sizeof(arg),"time_base=1/%d:sample_rate=%d:sample_fmt=%s:channel_layout=%s",in->sample_rate,in->sample_rate,av_get_sample_fmt_name((AVSampleFormat)in->format),layout);
  ck(avfilter_graph_create_filter(&src,avfilter_get_by_name("abuffer"),"source",arg,0,graph));ck(avfilter_graph_create_filter(&sink,avfilter_get_by_name("abuffersink"),"output",0,0,graph));
  AVFilterInOut *a=avfilter_inout_alloc(),*b=avfilter_inout_alloc();a->name=av_strdup("out");a->filter_ctx=sink;a->pad_idx=0;b->name=av_strdup("in");b->filter_ctx=src;b->pad_idx=0;
  std::string filters="aformat=sample_fmts=flt:sample_rates=48000:channel_layouts=mono";
  if(rate!=1)filters+=",atempo="+std::to_string(rate);
  ck(avfilter_graph_parse_ptr(graph,filters.c_str(),&a,&b,0));ck(avfilter_graph_config(graph,0));avfilter_inout_free(&a);avfilter_inout_free(&b);
 }
 bool decode(){
  while(!flushed){
   int x=avcodec_receive_frame(dec,in);
   if(x==0){
    if(in->pts==AV_NOPTS_VALUE)exit(25);
    double pts=in->pts*av_q2d(fmt->streams[stream]->time_base);
    int lo=std::max(0,(int)llround((begin-pts)*in->sample_rate));int hi=std::min(in->nb_samples,(int)llround((end-pts)*in->sample_rate));
    lo=std::min(lo,in->nb_samples);hi=std::max(0,hi);
    if(hi>lo){
     if(!graph)makeGraph();AVFrame *f=av_frame_alloc();f->format=in->format;f->sample_rate=in->sample_rate;av_channel_layout_copy(&f->ch_layout,&in->ch_layout);f->nb_samples=hi-lo;f->pts=decodedSamples;ck(av_frame_get_buffer(f,0));
     int bytes=av_get_bytes_per_sample((AVSampleFormat)in->format);bool planar=av_sample_fmt_is_planar((AVSampleFormat)in->format);int channels=in->ch_layout.nb_channels;
     for(int c=0;c<(planar?channels:1);c++)memcpy(f->extended_data[c],in->extended_data[c]+lo*bytes*(planar?1:channels),(hi-lo)*bytes*(planar?1:channels));
     decodedSamples+=hi-lo;decodedFrames++;inputExtent=std::min(end,pts+hi/(double)in->sample_rate);ck(av_buffersrc_add_frame(src,f));av_frame_free(&f);
    }
    bool done=pts+in->nb_samples/(double)in->sample_rate>=end;av_frame_unref(in);
    if(done){flushed=true;if(graph)ck(av_buffersrc_add_frame(src,nullptr));}
    if(!graph&&!done)continue;
    return graph!=nullptr;
   }
   if(x==AVERROR_EOF){flushed=true;if(graph)ck(av_buffersrc_add_frame(src,nullptr));return graph!=nullptr;}
   if(x!=AVERROR(EAGAIN))ck(x);
   if(draining){flushed=true;return false;}
   int n=av_read_frame(fmt,pkt);if(n<0){ck(avcodec_send_packet(dec,nullptr));draining=true;}else if(pkt->stream_index==stream)ck(avcodec_send_packet(dec,pkt));av_packet_unref(pkt);
  }return false;
 }
 bool exhausted()const{return eof&&fifo.empty();}
 size_t pull(float *dst,size_t count){
  while(fifo.size()<count&&!eof){
   int rc=graph?av_buffersink_get_frame(sink,out):AVERROR(EAGAIN);
   if(rc==0){auto p=(float*)out->data[0];for(int i=0;i<out->nb_samples;i++)fifo.push_back(p[i]);av_frame_unref(out);maxFIFO=std::max(maxFIFO,(uint64_t)fifo.size());continue;}
   if(rc==AVERROR_EOF){eof=true;break;}
   if(rc!=AVERROR(EAGAIN))ck(rc);
   if(!decode()||flushed){
    if(graph){while(av_buffersink_get_frame(sink,out)==0){auto p=(float*)out->data[0];for(int i=0;i<out->nb_samples;i++)fifo.push_back(p[i]);av_frame_unref(out);}}
    maxFIFO=std::max(maxFIFO,(uint64_t)fifo.size());eof=true;
   }
  }
  size_t n=std::min(count,fifo.size());for(size_t i=0;i<n;i++){dst[i]=fifo.front();fifo.pop_front();}producedSamples+=n;return n;
 }
};
class Audio {
 struct Slot{AudioQueueBufferRef b=nullptr;bool queued=false;uint64_t start=0,end=0,gen=0;};
 std::array<Slot,3> slots;std::mutex mutex;std::unique_ptr<StreamFeed> feed;
 const char*path;double limit=0,rawBase=0,playedBase=0,lastRaw=0,protectedSamples=0,pauseRaw=0;
 uint64_t submitted=0,completed=0,stale=0,freeCount=0,underruns=0,backpressure=0;bool valid=false,paused=false,withhold=false,draining=false,finished=false;
 std::vector<NSDictionary*> events;
public:
 AudioQueueRef q=nullptr;std::atomic<uint64_t> filled{0},callbacks{0};double origin=0,speed=1;uint64_t restart=0,generation=0;bool running=false;OSStatus lastStart=0;
 static std::atomic<int> activeQueues;
 Audio(const char*p):path(p){}
 static void fill(void*u,AudioQueueRef queue,AudioQueueBufferRef b){
  auto a=(Audio*)u;std::lock_guard<std::mutex> lock(a->mutex);
  if(queue!=a->q){a->stale++;return;}
  for(auto&s:a->slots)if(s.b==b){
   if(!s.queued||s.gen!=a->generation){a->stale++;return;}
   s.queued=false;a->completed=std::max(a->completed,s.end);a->callbacks++;
   a->events.push_back(@{@"kind":@"buffer-completion",@"generation":@(s.gen),@"start":@(s.start),@"end":@(s.end),@"wall":@(now())});return;
  }a->stale++;
 }
 void drainEvents(){std::vector<NSDictionary*> e;{std::lock_guard<std::mutex> lock(mutex);e.swap(events);}for(auto x:e)emit(x);}
 void service(){
  // Check against the OLD submitted extent before filling: a late refill cannot
  // retroactively legitimize a raw counter that advanced during a supply gap.
  if(running&&!paused)clock();
  std::lock_guard<std::mutex> lock(mutex);if(!q||withhold||paused||!valid)return;
  bool any=false;for(auto&s:slots){if(s.queued){any=true;continue;}
   size_t n=feed->pull((float*)s.b->mAudioData,1024);if(!n)continue;
   s.b->mAudioDataByteSize=(UInt32)n*4;s.start=submitted;s.end=submitted+n;s.gen=generation;
   OSStatus rc=AudioQueueEnqueueBuffer(q,s.b,0,0);if(rc){valid=false;events.push_back(@{@"kind":@"enqueue-error",@"status":@(rc)});break;}
   s.queued=true;submitted+=n;filled=submitted;any=true;
   events.push_back(@{@"kind":@"submit",@"generation":@(generation),@"start":@(s.start),@"end":@(s.end),@"input_extent":@(feed->inputExtent),@"fifo_samples":@(feed->maxFIFO),@"wall":@(now())});
  }if(any)backpressure++;
  if(running&&feed->exhausted()&&!draining){
   OSStatus rc=AudioQueueStop(q,false);if(rc){valid=false;events.push_back(@{@"kind":@"drain-error",@"status":@(rc)});}
   else{draining=true;events.push_back(@{@"kind":@"drain-request",@"status":@(rc),@"generation":@(generation),@"supplied":@(submitted),@"wall":@(now())});}
  }
 }
 OSStatus startEpoch(double seek,double rate,double end){
  stop();origin=seek;speed=rate;limit=end;generation++;restart++;submitted=completed=0;protectedSamples=rawBase=playedBase=lastRaw=0;valid=true;paused=false;withhold=false;draining=false;finished=false;feed=std::make_unique<StreamFeed>(path,seek,rate,end);
  AudioStreamBasicDescription f={};f.mSampleRate=48000;f.mFormatID=kAudioFormatLinearPCM;f.mFormatFlags=kAudioFormatFlagIsFloat|kAudioFormatFlagIsPacked;f.mBytesPerPacket=f.mBytesPerFrame=4;f.mFramesPerPacket=1;f.mChannelsPerFrame=1;f.mBitsPerChannel=32;
  OSStatus rc=AudioQueueNewOutput(&f,fill,this,NULL,NULL,0,&q);if(rc){q=nullptr;valid=false;return rc;}activeQueues++;
  // Owner-only audition is opt-in and never changes system volume.
  aqck(AudioQueueSetParameter(q,kAudioQueueParam_Volume,getenv("X1_AUDIBLE")&&!strcmp(getenv("X1_AUDIBLE"),"1")?.5:0));for(auto&s:slots){s={};aqck(AudioQueueAllocateBuffer(q,4096,&s.b));}
  service();if(!submitted){valid=false;lastStart=kAudioQueueErr_BufferEmpty;emit(@{@"kind":@"empty-feed-rejected",@"generation":@(generation)});return lastStart;}
  UInt32 prepared=0;OSStatus primed=0;
  if(getenv("X1_PRIME"))primed=AudioQueuePrime(q,0,&prepared);
  lastStart=AudioQueueStart(q,NULL);running=lastStart==0;if(lastStart)valid=false;
  emit(@{@"kind":@"epoch-start",@"generation":@(generation),@"origin":@(origin),@"rate":@(speed),@"supplied":@(submitted),@"status":@(lastStart),@"prime_status":@(primed),@"prepared_frames":@(prepared),@"wall":@(now()),@"active_queues":@(activeQueues.load())});drainEvents();return lastStart;
 }
 void start(double seek,double rate){aqck(startEpoch(seek,rate,120));}
 double raw(){if(!q)return -1;AudioTimeStamp t={};Boolean discontinuity=false;OSStatus rc=AudioQueueGetCurrentTime(q,NULL,&t,&discontinuity);
  if(rc||!(t.mFlags&kAudioTimeStampSampleTimeValid))return -1;
  if(discontinuity){valid=false;emit(@{@"kind":@"timeline-discontinuity",@"generation":@(generation)});}return t.mSampleTime;
 }
 double clock(){double r=raw();UInt32 is=1,n=sizeof(is);OSStatus state=draining&&q?AudioQueueGetProperty(q,kAudioQueueProperty_IsRunning,&is,&n):-1;std::lock_guard<std::mutex> lock(mutex);
  if(draining&&!finished&&!state&&!is){finished=true;valid=false;protectedSamples=submitted;protectedSamples=std::min(protectedSamples,std::max(0.,(feed->inputExtent-origin)*48000/speed));events.push_back(@{@"kind":@"drain-complete",@"generation":@(generation),@"raw":@(r),@"supplied":@(submitted),@"protected_samples":@(protectedSamples),@"drained_source_end":@(feed->inputExtent),@"decoded_samples":@(feed->decodedSamples),@"running_status":@(state),@"running":@(is),@"wall":@(now())});}
  if(!valid||r<0||paused)return origin+protectedSamples/48000*speed;
  double candidate=playedBase+std::max(0.,r-rawBase);if(r+1<lastRaw){valid=false;return origin+protectedSamples/48000*speed;}lastRaw=r;
  protectedSamples=std::max(protectedSamples,std::min(candidate,(double)submitted));
  protectedSamples=std::min(protectedSamples,std::max(0.,(feed->inputExtent-origin)*48000/speed));
  bool outstanding=false;for(auto&s:slots)outstanding|=s.queued;
  // Reuse callbacks acknowledge acquisition, not a played source endpoint.
  // EOF drains to its bounded supplied extent; depletion remains conservative.
  if(candidate>=submitted||(!outstanding&&completed>=submitted&&!feed->eof)){
   valid=false;underruns++;events.push_back(@{@"kind":@"clock-invalidated",@"reason":feed->eof?@"source-ended":@"underrun",@"generation":@(generation),@"raw":@(r),@"supplied":@(submitted),@"protected_samples":@(protectedSamples),@"wall":@(now())});
  }return origin+protectedSamples/48000*speed;
 }
 double drainedSourceEnd(){std::lock_guard<std::mutex> lock(mutex);return finished&&feed?feed->inputExtent:-1;}
 bool ended(){clock();std::lock_guard<std::mutex> lock(mutex);return finished;}
 bool usable(){std::lock_guard<std::mutex> lock(mutex);return valid&&!paused&&running;}
 void pause(){clock();aqck(AudioQueuePause(q));paused=true;running=false;pauseRaw=raw();}
 void resume(){double frozen=origin+protectedSamples/48000*speed;double r=speed,e=limit;aqck(startEpoch(frozen,r,e));}
 void hold(bool x){withhold=x;}
 void rejectStale(uint64_t g){std::lock_guard<std::mutex> lock(mutex);double before=protectedSamples;if(g!=generation)stale++;emit(@{@"kind":@"stale-injection",@"old_generation":@(g),@"current_generation":@(generation),@"rejected":@(g!=generation),@"clock_unchanged":@(before==protectedSamples),@"scope":@"protocol fault injection; disposed native queue callbacks joined"});}
 NSDictionary* snapshot(NSString*tag){double c=clock(),r=raw();UInt32 is=0,n=sizeof(is);OSStatus rc=q?AudioQueueGetProperty(q,kAudioQueueProperty_IsRunning,&is,&n):-1;std::lock_guard<std::mutex> lock(mutex);
  int outstanding=0;for(auto&s:slots)outstanding+=s.queued;
  return @{@"kind":@"clock",@"tag":tag,@"generation":@(generation),@"source":@(c),@"origin":@(origin),@"rate":@(speed),@"raw_samples":@(r),@"protected_samples":@(protectedSamples),@"supplied":@(submitted),@"completed_extent":@(completed),@"outstanding":@(outstanding),@"valid":@(valid),@"paused":@(paused),@"draining":@(draining),@"finished":@(finished),@"running_property":@(is),@"running_status":@(rc),@"callbacks":@(callbacks.load()),@"decoded_samples":@(feed?feed->decodedSamples:0),@"fifo_max":@(feed?feed->maxFIFO:0),@"input_extent":@(feed?feed->inputExtent:0),@"stale":@(stale),@"backpressure_ticks":@(backpressure),@"wall":@(now())};
 }
 void stop(){if(q){AudioQueueRef old=q;AudioQueueStop(old,true);AudioQueueDispose(old,true);q=nullptr;activeQueues--;for(auto&s:slots)s={};running=false;valid=false;}feed.reset();}
 ~Audio(){stop();}
};
std::atomic<int> Audio::activeQueues{0};

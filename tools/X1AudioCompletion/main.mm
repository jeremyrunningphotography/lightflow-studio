// Native bounded audio/recovery proof. No physical scanout or listening claim.
static void tick(Audio&a,double seconds,NSString*tag,bool servicing=true){double end=now()+seconds,next=0;while(now()<end){if(servicing)a.service();a.clock();a.drainEvents();if(now()>next){emit(a.snapshot(tag));next=now()+.005;}std::this_thread::sleep_for(std::chrono::milliseconds(1));}}
static void starvation(const char*path){Audio a(path);aqck(a.startEpoch(0,1,120));tick(a,.25,@"normal");a.hold(true);emit(a.snapshot(@"withhold-start"));tick(a,.5,@"starving");double protectedEnd=a.clock();uint64_t old=a.generation;emit(a.snapshot(@"starvation-end"));double t=now();aqck(a.startEpoch(protectedEnd,1,120));a.rejectStale(old);emit(@{@"kind":@"underrun-recovery",@"origin":@(protectedEnd),@"generation":@(a.generation),@"gap_ms":@((now()-t)*1000)});tick(a,.4,@"recovered");a.pause();double before=a.clock();tick(a,.15,@"paused",false);double after=a.clock();a.resume();emit(@{@"kind":@"pause-invariant",@"before":@(before),@"after":@(after),@"delta":@(after-before),@"resume_source":@(a.clock())});tick(a,.2,@"resumed");old=a.generation;aqck(a.startEpoch(2,1,120));a.rejectStale(old);tick(a,.2,@"seek");a.stop();a.drainEvents();emit(@{@"kind":@"queue-count",@"active":@(Audio::activeQueues.load())});}
static void avRun(const char*media,const char*audio,double rate,double seconds,bool transitions,bool loops,bool underrun){
 Decoder d(media,true);Audio a(audio);Renderer r;State state;state.camera=state.creative=true;Frame current=d.next(),future=d.next();
 double playbackEnd=loops?1:120;aqck(a.startEpoch(0,rate,playbackEnd));r.generation=a.generation;double start=now(),nextClock=0;int transition=0;uint64_t published=UINT64_MAX,draws=0,skips=0,loop=0,invalidSchedules=0;bool held=false,recovered=false;double recoveryAt=0,lastRender=0;
 while(now()-start<seconds){@autoreleasepool{
  a.service();double c=a.clock();double wall=now()-start;a.drainEvents();
  if(underrun&&!held&&wall>1){held=true;a.hold(true);emit(a.snapshot(@"av-withhold"));}
  if(underrun&&held&&!recovered&&!a.usable()){double t=now();uint64_t old=a.generation;emit(a.snapshot(@"av-invalid"));std::this_thread::sleep_for(std::chrono::milliseconds(100));aqck(a.startEpoch(c,rate,120));r.generation=a.generation;a.rejectStale(old);recovered=true;recoveryAt=wall;emit(@{@"kind":@"av-recovery",@"origin":@(c),@"restart_ms":@((now()-t)*1000),@"generation":@(a.generation)});continue;}
  if(transitions&&transition<4&&wall>=(transition+1)*3){double oldRate=rate,oldSource=c;uint64_t old=a.generation;double rates[]={2,1,.5,1};rate=rates[transition++];double t=now();aqck(a.startEpoch(c,rate,120));r.generation=a.generation;a.rejectStale(old);emit(@{@"kind":@"rate-transition",@"from":@(oldRate),@"to":@(rate),@"origin":@(oldSource),@"restart_ms":@((now()-t)*1000),@"generation":@(a.generation),@"last_render_wall":@(lastRender),@"wall":@(now())});continue;}
  if(loops&&a.ended()){
   double t=now();uint64_t old=a.generation;emit(a.snapshot(@"loop-last-audio"));double lastPTS=r.retained?r.retained->source->pts*av_q2d(d.tb):-1;double drainedEnd=a.drainedSourceEnd();
   aqck(a.startEpoch(0,rate,1));r.generation=a.generation;a.rejectStale(old);d.seek(0);current=d.next();future=d.next();published=UINT64_MAX;loop++;
   emit(@{@"kind":@"loop-rebase",@"loop":@(loop),@"last_audio_source":@(c),@"drained_source_end":@(drainedEnd),@"exclusive_source_end":@(playbackEnd),@"last_video_pts":@(lastPTS),@"first_audio_source":@0,@"first_video_pts":@(current->pts*av_q2d(d.tb)),@"generation":@(a.generation),@"restart_ms":@((now()-t)*1000),@"last_render_wall":@(lastRender),@"wall":@(now())});continue;
  }
  if(now()>nextClock){emit(a.snapshot(@"av"));nextClock=now()+.025;}
  if(!a.usable()){std::this_thread::sleep_for(std::chrono::milliseconds(1));continue;}
  int advanced=0;while(future&&future->pts*av_q2d(d.tb)<=c&&(!loops||future->pts*av_q2d(d.tb)<playbackEnd)){current=future;future=d.next();advanced++;}
  if(current&&current->sequence!=published){
   if(!a.usable()){invalidSchedules++;continue;}
   double before=now();NSMutableDictionary* row=[r.present(current,state,@"audio-clock-render-ready",false) mutableCopy];double after=a.clock();double pts=current->pts*av_q2d(d.tb);
   row[@"kind"]=@"frame";row[@"rate"]=@(rate);row[@"source_pts"]=@(pts);row[@"audio_source"]=@(after);row[@"source_offset_ms"]=@((pts-after)*1000);row[@"wall_offset_ms"]=@((pts-after)/rate*1000);row[@"clock_valid_after"]=@(a.usable());row[@"render_ms"]=@((now()-before)*1000);row[@"skipped"]=@(std::max(0,advanced-1));row[@"loop"]=@(loop);row[@"wall"]=@(now());row[@"elapsed"]=@(wall);row[@"semantic_ui_accepted"]=@NO;emit(row);lastRender=now();published=current->sequence;draws++;skips+=std::max(0,advanced-1);
  }else std::this_thread::sleep_for(std::chrono::milliseconds(1));
 }}a.stop();a.drainEvents();emit(@{@"kind":@"av-summary",@"frames":@(draws),@"skips":@(skips),@"loops":@(loop),@"known_invalid_schedules":@(invalidSchedules),@"active_queues":@(Audio::activeQueues.load()),@"rss":@(rss()),@"seconds":@(now()-start)});
}
static void stress(const char*path,int count){int failures=0,recovered=0;uint64_t low=rss();for(int i=0;i<count;i++){@autoreleasepool{Audio a(path);double origin=(i%3)*.05;double rate=(i%3==0)?.5:(i%3==1?1:2);double t=now();OSStatus rc=a.startEpoch(origin,rate,120);if(rc){failures++;rc=a.startEpoch(origin,rate,120);if(!rc)recovered++;}if(!rc){tick(a,.015,@"stress-start");a.pause();std::this_thread::sleep_for(std::chrono::milliseconds(3));a.resume();tick(a,.01,@"stress-resume");}
 a.stop();a.drainEvents();emit(@{@"kind":@"stress-attempt",@"attempt":@(i),@"status":@(rc),@"ms":@((now()-t)*1000),@"active":@(Audio::activeQueues.load()),@"rss":@(rss())});}}
 emit(@{@"kind":@"stress-summary",@"attempts":@(count),@"initial_failures":@(failures),@"recovered":@(recovered),@"active_queues":@(Audio::activeQueues.load()),@"rss_before":@(low),@"rss_after":@(rss())});}
static void startRCA(const char*path){
 // Empty queue start is a deliberate negative state, not a valid product start.
 for(int i=0;i<12;i++){
  AudioStreamBasicDescription f={};f.mSampleRate=48000;f.mFormatID=kAudioFormatLinearPCM;f.mFormatFlags=kAudioFormatFlagIsFloat|kAudioFormatFlagIsPacked;f.mBytesPerPacket=f.mBytesPerFrame=4;f.mFramesPerPacket=1;f.mChannelsPerFrame=1;f.mBitsPerChannel=32;AudioQueueRef q=nullptr;
  OSStatus create=AudioQueueNewOutput(&f,[](void*,AudioQueueRef,AudioQueueBufferRef){},nullptr,nullptr,nullptr,0,&q),start=create;
  if(!create){AudioQueueSetParameter(q,kAudioQueueParam_Volume,0);start=AudioQueueStart(q,nullptr);AudioQueueStop(q,true);AudioQueueDispose(q,true);}
  emit(@{@"kind":@"empty-start",@"attempt":@(i),@"create":@(create),@"start":@(start),@"scope":@"deliberately no buffers; not attribution of prior intermittent failure"});
 }stress(path,40);
}
int main(int argc,char**argv){setbuf(stdout,nullptr);@autoreleasepool{
 const char*root=nullptr;for(int i=1;i+1<argc;i++)if(!strcmp(argv[i],"--data-root"))root=argv[i+1];if(!root||root[0]!='/'||argc<3)return 64;
 fprintf(stderr,"FFmpeg %s license %s configuration %s\n",av_version_info(),avcodec_license(),avcodec_configuration());
 if(!strcmp(argv[1],"starve")){starvation(argv[2]);return 0;}
 if(!strcmp(argv[1],"stress")){stress(argv[2],getenv("X1_STRESS_COUNT")?atoi(getenv("X1_STRESS_COUNT")):40);return 0;}
 if(!strcmp(argv[1],"feed")){
  for(double rate:{.5,1.,2.}){StreamFeed f(argv[2],0,rate,10);float buf[1024];uint64_t n=0,h=14695981039346656037ULL;while(auto got=f.pull(buf,1024)){n+=got;auto bytes=(uint8_t*)buf;for(size_t i=0;i<got*4;i++){h^=bytes[i];h*=1099511628211ULL;}}
   emit(@{@"kind":@"stream-feed",@"rate":@(rate),@"decoded_samples":@(f.decodedSamples),@"produced_samples":@(n),@"decoded_frames":@(f.decodedFrames),@"max_fifo":@(f.maxFIFO),@"input_extent":@(f.inputExtent),@"eof":@(f.eof),@"output_hash":hs(h)});
  }
  for(double at:{0.,2.,5.}){StreamFeed f(argv[2],at,1,at+.5);float buf[1024];uint64_t n=f.pull(buf,1024);auto before=f.decodedSamples;std::this_thread::sleep_for(std::chrono::milliseconds(20));bool unchanged=f.decodedSamples==before;while(auto got=f.pull(buf,1024))n+=got;
   emit(@{@"kind":@"feed-seek-backpressure",@"origin":@(at),@"end":@(at+.5),@"decoded_samples":@(f.decodedSamples),@"produced_samples":@(n),@"decoded_paused_without_pull":@(unchanged),@"max_fifo":@(f.maxFIFO)});
  }return 0;
 }
 if(!strcmp(argv[1],"empty")){startRCA(argv[2]);return 0;}
 if(argc<6)return 64;[NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];backendOnly=true;
 avRun(argv[2],argv[3],atof(argv[4]),atof(argv[5]),!strcmp(argv[1],"rates"),!strcmp(argv[1],"loops"),!strcmp(argv[1],"recover"));
 }return 0;}

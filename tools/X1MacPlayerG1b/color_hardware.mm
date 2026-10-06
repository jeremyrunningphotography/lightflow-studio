// Task-only complete hardware decode -> quarter turns -> ordered Color -> capture probe.
#define main baselineMain
#include "integrated.mm"
#undef main
int main(int argc, char** argv) {
 @autoreleasepool {
  if(argc<2)return 1;
  [NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
  [NSApp finishLaunching];[NSApp activateIgnoringOtherApps:YES];pump(.5);
  Renderer r;[r.window makeKeyAndOrderFront:nil];pump(.3);r.asset=@(argv[1]);Decoder d(argv[1],true);Frame f=d.at(av_rescale_q(2,(AVRational){1,1},d.tb));
  State s;s.rotation=d.rotation;emit(r.present(f,s,@"hardware-warmup",true));
  for(int rot=0;rot<4;rot++)for(int color=0;color<5;color++){
   s.rotation=(d.rotation+rot)%4;s.camera=color==1||color>=3;s.creative=color==2||color>=3;s.compare=color==4;
   emit(r.present(f,s,@"hardware-rotation-color-capture",true));
  }
  [r.window close];pump(.05);
 }
 return 0;
}

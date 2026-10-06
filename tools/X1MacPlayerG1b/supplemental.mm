// Supplemental task-local fault and lifecycle probes reuse the measured G1b implementation.
#define main baselineMain
#include "integrated.mm"
#undef main

static uint64_t textureHash(const Authority& a) {
    std::vector<uint8_t> pixels(a.texture.width * a.texture.height * 4);
    [a.texture getBytes:pixels.data() bytesPerRow:a.texture.width * 4
            fromRegion:MTLRegionMake2D(0, 0, a.texture.width, a.texture.height) mipmapLevel:0];
    return hash(pixels.data(), pixels.size());
}
static void leases(Renderer& r, const char* path, bool pressure) {
    Decoder d(path, false);
    State s;
    Frame f = d.next();
    emit(r.present(f, s, @"lease-warmup", true));
    emit(r.present(f, s, @"lease-acquire", true));
    auto held = r.retained;
    if (!held) exit(14);
    auto before = textureHash(*held);
    std::vector<std::shared_ptr<const Authority>> occupied{held};
    for (int i = 0; i < 6; ++i) {
        f = d.next();
        emit(r.present(f, s, @"lease-next-frame", true));
        if (pressure) occupied.push_back(r.retained);
    }
    emit(@{@"action": @"lease-retained-capture", @"serial": @(held->serial),
           @"pts": @(held->source->pts), @"before": hs(before),
           @"after": hs(textureHash(*held)), @"unchanged": @(before == textureHash(*held))});
}
static void shownLifecycle(Renderer& r, const char* path) {
    Decoder d(path, false);
    Frame f = d.next(); State s;
    [NSApp activateIgnoringOtherApps:YES]; pump(.2);
    emit(@{@"action": @"activate", @"active": @(NSApp.active)});
    emit(r.present(f, s, @"active-open", true));
    emit(r.present(f, s, @"active-open-retry", true));
    [r.window toggleFullScreen:nil]; pump(2);
    r.layer.contentsScale = r.window.backingScaleFactor;
    r.layer.drawableSize = CGSizeMake(r.view.bounds.size.width*r.layer.contentsScale,
                                      r.view.bounds.size.height*r.layer.contentsScale);
    for (int i=0; i<3; ++i) emit(r.present(f, s, @"active-fullscreen-present", true));
    emit(@{@"action": @"active-fullscreen-state", @"fullscreen": @((r.window.styleMask&NSWindowStyleMaskFullScreen)!=0),
           @"width": @(r.layer.drawableSize.width), @"height": @(r.layer.drawableSize.height), @"scale": @(r.layer.contentsScale)});
    [r.window toggleFullScreen:nil]; pump(2);
    [NSApp deactivate]; pump(.15);
    emit(@{@"action": @"deactivate", @"active": @(NSApp.active)});
    [NSApp activateIgnoringOtherApps:YES]; pump(.2);
    emit(@{@"action": @"reactivate", @"active": @(NSApp.active)});
    for (int i=0; i<4; ++i) {
        [r.window close]; pump(.1); r.generation++; r.open();
        emit(r.present(f, s, @"active-reopen-first", true));
        emit(r.present(f, s, @"active-reopen-retry", true));
    }
}
static void audioRestart(const char* pcm) {
    Audio a(pcm); a.start(0, 1); pump(.3);
    double before = a.clock();
    aqck(AudioQueueStop(a.q, true)); // Actual queue stop, a simulated device/underrun interruption.
    double stopped = a.clock(); pump(.2); double after = a.clock();
    a.start(std::max(0.0, before), 1); pump(.3);
    emit(@{@"action": @"audio-stop-recreate", @"before": @(before), @"stopped": @(stopped),
           @"after_wait": @(after), @"resumed": @(a.clock()), @"restart_count": @(a.restart),
           @"scope": @"actual queue disposal/recreation; simulated interruption, no physical device change or callback-starvation injection"});
    aqck(AudioQueueSetParameter(a.q, kAudioQueueParam_Volume, .25));
    Float32 gain=0; aqck(AudioQueueGetParameter(a.q, kAudioQueueParam_Volume, &gain));
    aqck(AudioQueueSetParameter(a.q, kAudioQueueParam_Volume, 0));
    emit(@{@"action": @"volume-parameter", @"roundtrip": @(gain),
           @"scope": @"queue parameter only; no audible amplitude oracle"});
    a.stop();
}
int main(int argc, char** argv) {
    @autoreleasepool {
        if (argc<3) return 1;
        [NSApplication sharedApplication]; [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        NSString* mode=@(argv[1]);
        if ([mode isEqual:@"audio-fault"]) { audioRestart(argv[2]); return 0; }
        Renderer r; r.asset=@(argv[2]);
        if ([mode isEqual:@"leases"]) leases(r, argv[2], false);
        else if ([mode isEqual:@"pressure"]) leases(r, argv[2], true);
        else if ([mode isEqual:@"shown-lifecycle"]) shownLifecycle(r, argv[2]);
        else if ([mode isEqual:@"sustained"] && argc>=5) {
            std::atomic<bool> done{false};
            std::thread monitor([&] {
                double begin=now();
                while (!done) {
                    @autoreleasepool { emit(@{@"phase": @"thermal-rss", @"wall": @(now()-begin),
                      @"thermal_state": @([NSProcessInfo processInfo].thermalState), @"rss": @(rss())}); }
                    for (int i=0;i<50&&!done;i++) std::this_thread::sleep_for(std::chrono::milliseconds(100));
                }
            });
            playback(argv[2],argv[3],r,atof(argv[4]),1,true,true,false);
            done=true; monitor.join();
        } else return 1;
        [r.window close]; pump(.05);
    }
    return 0;
}

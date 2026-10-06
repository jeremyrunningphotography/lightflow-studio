# X1 Player semantic contract

Source authority: main e302a20d888161d6face6e311610d67eab9c1512. This is a proof contract, not a production implementation or acceptance claim.

| Requirement | Existing authority | Mac proof obligation |
|---|---|---|
| Display/source timing | MediaPlayback.cs: MediaPresentationTimestamp, FramePresented, CapturePresentedFrameAsync | Publish the actual retained decoded frame PTS, normalized explicitly against stream start; distinguish raw source PTS, source-relative position and wall clock. Preserve rational timestamp until conversion to 100 ns domain ticks. |
| Forward | FlyleafPlaybackBackend.StepForwardAsync; PlayerActionDecodedFrameTests | Immediate next real decoded presentation timestamp, silent and paused; retain last at EOF. Never add nominal frame duration. |
| Reverse | FlyleafPlaybackBackend.StepBackwardAsync and SettleOnKnownTimestampAsync | Immediate predecessor, VFR/B-frame/long-GOP included. Existing bounded earlier seeks double a 1 s window up to eight attempts, scan at most 2000 frames per attempt, settle on known PTS, or fail truthfully. Suppress intermediate authoring events. |
| Seek/pause | SeekPlayerAsync, MediaPlaybackService serialization | Stop audio, await actual decoded/presented settle, retain correct image. Current Windows native seek uses integer milliseconds but reports actual decoded ticks. Cancel obsolete generations rather than restore obsolete source. |
| Capture | CapturePresentedFrameAsync, FrameScreengrab.cs | Full resolution effective orientation/current Color and actual retained PTS. Capture must not move playback. GetFrameAsync temporarily seeks/restores under lifetime protection. |
| Audio | FfmpegAudioPlayback.cs, backend Play/Pause/Step | Selected stream, persisted volume 0–100/mute across opens, silent seeks/steps, speed tempo, error truthfulness, clock/device/restart behavior. Native measured output drift required. |
| Speed/cadence/loop | PlayerReviewOptions.cs, PlayerViewerHost.Review.cs, PlayerReviewSet.cs | Speeds .125/.25/.5/1/2/4, cadence independent of speed; source PTS stays authoritative. Loop/review transition remains shared semantic policy. |
| Authored timing | PlayerActionDecodedFrameTests, PlayerViewerHost.Markers.cs, docs/ARCHITECTURE.md | In/Out and markers use displayed PTS. Inclusive UI Out converts to actual next-frame exclusive processing boundary; Subclip/review timing/IDs remain shared domain authority. |
| Color | ColorManagement.cs, docs/ARCHITECTURE.md live Color | Camera then Creative; 3D cube red-fastest, domain clamp/trilinear, no implicit interstage color-space conversion, content/hash validation. Compare Original is transient hold/bypass; redraw retained frame without seek. |
| Rotation | docs/VIDEO_ROTATION.md, PlayerRotationTests | Source orientation plus authored quarter-turn exactly once; swap effective dimensions; no seek/reopen/PTS change. Capture already oriented, Preview adjustment separate. |
| Surface | MediaPlaybackPresentation, PlayerFullscreenPresentation, PlayerSurfaceInput | One source/session, compositable host, clip/resize/Retina, overlay input ownership, zoom/pan, fullscreen without reopen/audio restart; stale operations and resource lifetime protected. |

The task must measure exact frame sequence/identity. Pixel/GPU/display and audio tolerances are recommendations until owner approval; no approximate visual assessment satisfies exact frame identity. Native production UI/domain parity is not inferred from a standalone harness.

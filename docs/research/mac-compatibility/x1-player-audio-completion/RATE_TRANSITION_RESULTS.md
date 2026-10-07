# Live rate transitions

1→2,2→1,1→.5,.5→1 are prepared with the protected clock, flush/new source epoch, bounded streaming graph and queue recreation. Native transition measurements were not executed because output startup failed. No new gaps, settling times or AV offsets exist.

The current concrete strategy recreates the graph/queue; its necessity versus atempo runtime commands is not established. Pinned FFmpeg af_atempo.c supports a tempo command, but that API alone does not solve queued-old-rate sample ranges/latency and content mapping. No indefinite optimization is attempted. Prior #378 restart costs63.45/29.73/126.69/32.02ms remain historical evidence; they are not protected-clock results or audible acceptance.

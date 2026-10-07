# Underrun and recovery result

New real protected-clock underrun did NOT execute: native start failed before the first playback interval. Earlier #378 actual withheld-refill result remains unchanged: raw clock/running advanced past supplied samples. It cannot qualify this new clock.

The candidate withholds refills, logs raw/protected/supplied/completion/running state, checks the old supply boundary before refilling, invalidates scheduling on depletion, disposes/recreates at frozen source position and rejects old epoch work. A subsequent AV recovery case is prepared. No successful native detection time, recovery gap, stale-buffer rejection or post-recovery AV offset is claimed. See PROCESS_RESULTS.json and raw/underrun.jsonl.

/* Lightflow Studio v0.10.0 — experimental Premiere Pro ExtendScript helper.
   Test on a duplicate project. Requires an Adobe Media Encoder .epr preset. */
(function () {
    if (!app.project || !app.project.activeSequence) {
        alert("Open a project and make a sequence active first."); return;
    }
    var sequence = app.project.activeSequence;
    if (!sequence.videoTracks || sequence.videoTracks.numTracks < 1) {
        alert("The active sequence has no video tracks."); return;
    }
    var outputFolder = Folder.selectDialog("Choose the output folder");
    if (!outputFolder) return;
    var preset = File.openDialog("Choose an Adobe Media Encoder preset", "Adobe Media Encoder preset:*.epr");
    if (!preset) return;

    var clips = sequence.videoTracks[0].clips;
    if (!clips || clips.numItems === 0) { alert("No clips were found on V1."); return; }
    app.encoder.launchEncoder();
    var queued = 0;
    for (var i = 0; i < clips.numItems; i++) {
        var clip = clips[i];
        sequence.setInPoint(clip.start.seconds);
        sequence.setOutPoint(clip.end.seconds);
        var rawName = clip.name || ("clip_" + (i + 1));
        var safeName = rawName.replace(/[\\\/:*?\"<>|]/g, "_").replace(/\.[^.]+$/, "");
        var output = outputFolder.fsName + "/" + safeName + "_export.mp4";
        // ENCODE_IN_TO_OUT = 1. Queue without starting until all jobs are added.
        app.encoder.encodeSequence(sequence, output, preset.fsName, 1, 0);
        queued++;
    }
    sequence.clearInPoint(); sequence.clearOutPoint();
    app.encoder.startBatch();
    alert("Queued " + queued + " V1 clips in Adobe Media Encoder.\n\nVisible effects and adjustment layers are rendered for each marked range.");
}());

using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingColorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-encoding-color-").FullName;

    [Fact]
    public async Task Resource_snapshot_is_content_addressed_and_survives_source_move()
    {
        var source = WriteCube("camera.cube", IdentityCube);
        var hash = Hash(IdentityCube);
        var resource = new ManagedLutResource(Guid.NewGuid(), "Camera", "camera.cube", hash,
            LutDimension.ThreeDimensional, 2, LutResourceAvailability.Available, source);
        var store = new EncodingLutResourceStore(Path.Combine(_root, "resources"));

        var snapshot = await store.SnapshotAsync(ColorLutStage.Camera, resource);
        File.Move(source, source + ".moved");

        Assert.Equal(hash, snapshot.ContentSha256);
        Assert.True(File.Exists(store.Resolve(snapshot)));
    }

    [Fact]
    public async Task Resource_snapshot_rejects_changed_content_and_resolve_rejects_tampering()
    {
        var source = WriteCube("creative.cube", IdentityCube);
        var resource = new ManagedLutResource(Guid.NewGuid(), "Creative", "creative.cube", Hash(IdentityCube),
            LutDimension.ThreeDimensional, 2, LutResourceAvailability.Available, source);
        var store = new EncodingLutResourceStore(Path.Combine(_root, "resources"));
        var snapshot = await store.SnapshotAsync(ColorLutStage.Creative, resource);
        File.WriteAllText(store.Resolve(snapshot), InvertCube);

        var error = Assert.Throws<InvalidDataException>(() => store.Resolve(snapshot));
        Assert.Contains("does not match", error.Message);
    }

    [Fact]
    public void Assigned_filters_are_camera_then_creative_before_existing_transforms()
    {
        var trimRange = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        var trim = new ResolvedMediaRange(trimRange, TimeSpan.Zero, TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4.04), TimeSpan.FromSeconds(3.04));
        var options = EncodingPresetCatalog.Recommended with { Deinterlace = true, FrameRate = 24 };

        var args = FfmpegCommandBuilder.Encode("in", "out", null, RecoveryStrategy.Normal,
            OutputResolution.FullHd, encoding: options, trim: trim,
            assignedLuts: ["camera.cube", "creative.cube"]);

        Assert.Contains("trim=start=1:end=4.04,setpts=PTS-STARTPTS,lut3d=file='camera.cube',lut3d=file='creative.cube',bwdif,fps=24,scale=-2:1080", args);
    }

    [Fact]
    public void Manual_and_assigned_luts_cannot_be_composed()
    {
        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.Encode("in", "out", "manual.cube",
            RecoveryStrategy.Normal, OutputResolution.Source, assignedLuts: ["camera.cube"]));
    }

    [Fact]
    public async Task Planner_preflight_identifies_missing_stage_and_never_falls_back_to_original()
    {
        var missing = new MaterializedLutResource(Guid.NewGuid(), ColorLutStage.Creative, "Look",
            new string('a', 64), $"aa/{new string('a', 64)}.cube");
        var color = new MaterializedColorPipeline(true, Creative: missing);
        var options = new EncodingJobOptions(_root, _root, OutputResolution.Source, RecoveryStrategy.Normal,
            EncodingPresetCatalog.Recommended, null, "_out", false, true, false,
            ColorMode: EncodingColorMode.Assigned);
        var source = WriteCube("source.mov", "source");
        var definition = EncodingJobPlanner.Define(options,
            [new EncodingSource(source, new FileInfo(source).Length, TimeSpan.FromSeconds(1), AssignedColor: color)]);

        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0), colorResources:
            new EncodingLutResourceStore(Path.Combine(_root, "resources")));

        var issue = Assert.Single(plan.Items.Single().Issues, value => value.Code == "encoding.missing-creative-lut");
        Assert.Contains("Look", issue.Message);
        Assert.False(plan.IsValid);
        await Task.CompletedTask;
    }

    [Fact]
    public void Output_identity_changes_with_color_snapshot_and_render_mode()
    {
        var first = Resource(ColorLutStage.Camera, 'a');
        var second = Resource(ColorLutStage.Camera, 'b');
        var item = new JobItemDefinition(Guid.NewGuid(), Path.Combine(_root, "clip.mov"), 1,
            AssignedColor: new MaterializedColorPipeline(true, Camera: first));
        var options = new EncodingJobOptions(_root, _root, OutputResolution.Source, RecoveryStrategy.Normal,
            EncodingPresetCatalog.Recommended, null, "_out", false, false, false,
            ColorMode: EncodingColorMode.Assigned);

        Assert.NotEqual(EncodingOutputIdentity.Create(item, options).OptionsHash,
            EncodingOutputIdentity.Create(item with
            { AssignedColor = new MaterializedColorPipeline(true, Camera: second) }, options).OptionsHash);
        Assert.NotEqual(EncodingOutputIdentity.Create(item, options).OptionsHash,
            EncodingOutputIdentity.Create(item, options with
            { ColorMode = EncodingColorMode.OriginalOrManual }).OptionsHash);
    }

    [Fact]
    public void Real_ffmpeg_renders_camera_then_creative_cube_pipeline_and_ffprobe_validates_output()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var ffmpeg = Path.Combine(dependencies, "ffmpeg.exe");
        var ffprobe = Path.Combine(dependencies, "ffprobe.exe");
        var source = Path.Combine(_root, "color-source.mkv");
        var output = Path.Combine(_root, "color-output.mkv");
        var camera = WriteCube("real-camera.cube", IdentityCube);
        var creative = WriteCube("real-creative.cube", InvertCube);
        Assert.Equal(0, Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            "testsrc2=size=64x48:rate=4:duration=1", "-c:v", "ffv1", source));
        var filters = $"lut3d=file='{FfmpegCommandBuilder.EscapeFilterPath(camera)}'," +
                      $"lut3d=file='{FfmpegCommandBuilder.EscapeFilterPath(creative)}'";

        Assert.Equal(0, Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", source,
            "-vf", filters, "-c:v", "ffv1", output));
        Assert.Equal(0, Run(ffprobe, "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=codec_name,width,height", "-of", "default=nw=1", output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    private MaterializedLutResource Resource(ColorLutStage stage, char hash) =>
        new(Guid.NewGuid(), stage, stage.ToString(), new string(hash, 64), $"{hash}{hash}/{new string(hash, 64)}.cube");
    private string WriteCube(string name, string content)
    { var path = Path.Combine(_root, name); File.WriteAllText(path, content); return path; }
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static int Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        process.WaitForExit();
        return process.ExitCode;
    }
    private const string IdentityCube = "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n";
    private const string InvertCube = "LUT_3D_SIZE 2\n1 1 1\n0 1 1\n1 0 1\n0 0 1\n1 1 0\n0 1 0\n1 0 0\n0 0 0\n";
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

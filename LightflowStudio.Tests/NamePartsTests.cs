using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class NamePartsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-name-parts-").FullName;

    [Fact]
    public void OriginalCustomReorderedAndUnicodeParts_RenderInExplicitOrder()
    {
        var definition = new NamePartsDefinition([
            new(NamePartKind.CustomText, "Café"), new(NamePartKind.Sequence01), new(NamePartKind.OriginalName)
        ], NamePartSeparator.Hyphen);
        Assert.Equal("Café-03-撮影", NamePartsRenderer.Preview(definition, new("撮影", 3)));
    }

    [Theory]
    [InlineData((int)NamePartKind.Sequence1, "7")]
    [InlineData((int)NamePartKind.Sequence01, "07")]
    [InlineData((int)NamePartKind.Sequence001, "007")]
    [InlineData((int)NamePartKind.Sequence0001, "0007")]
    [InlineData((int)NamePartKind.Sequence00001, "00007")]
    public void SequenceWidths_ArePresentationsOfOneBasedValue(int kind, string expected) =>
        Assert.Equal(expected, NamePartsRenderer.Preview(new([new((NamePartKind)kind)]), new("clip", 7)));

    [Theory]
    [InlineData((int)NamePartSeparator.Underscore, "a_b")]
    [InlineData((int)NamePartSeparator.Hyphen, "a-b")]
    [InlineData((int)NamePartSeparator.Space, "a b")]
    [InlineData((int)NamePartSeparator.None, "ab")]
    public void Separators_RenderExplicitly(int separator, string expected) =>
        Assert.Equal(expected, NamePartsRenderer.Preview(
            new([new(NamePartKind.CustomText, "a"), new(NamePartKind.CustomText, "b")], (NamePartSeparator)separator), new("clip", 1)));

    [Theory]
    [InlineData("DJI_0042", "0042")]
    [InlineData("IMG_1234", "1234")]
    [InlineData("C0007", "0007")]
    public void IndexNumber_IsTrailingRunAndPreservesLeadingZeros(string original, string expected)
    {
        var result = NamePartsRenderer.Materialize(new([new(NamePartKind.IndexNumber)]), new(original, 1));
        Assert.Equal(expected, result.Stem);
        Assert.Equal(expected, result.IndexNumber);
    }

    [Fact]
    public void MissingIndexNumber_IsInputSpecificAndDoesNotFallBackToSequence()
    {
        var result = NamePartsRenderer.Materialize(new([new(NamePartKind.IndexNumber)]), new("clip", 9));
        Assert.Null(result.Stem);
        Assert.Contains("clip", result.Problem);
        Assert.DoesNotContain("9", result.Problem);
    }

    [Fact]
    public void DateAndTime_UseOnlyExplicitTimestampAndHaveStableFormats()
    {
        var definition = new NamePartsDefinition([new(NamePartKind.Date), new(NamePartKind.Time)]);
        Assert.Equal("2026-08-24_13-14-15", NamePartsRenderer.Preview(definition,
            new("clip", 1, new DateTimeOffset(2026, 8, 24, 13, 14, 15, TimeSpan.FromHours(-7)))));
        Assert.Contains("no explicit naming timestamp", NamePartsRenderer.Materialize(definition, new("clip", 1)).Problem);
    }

    [Fact]
    public void DefinitionAndMaterialization_SerializeRoundTrip()
    {
        var definition = new NamePartsDefinition([new(NamePartKind.OriginalName), new(NamePartKind.CustomText, "é")]);
        var materialized = NamePartsRenderer.Materialize(definition, new("source", 1));
        var restoredDefinition = JsonSerializer.Deserialize<NamePartsDefinition>(JsonSerializer.Serialize(definition))!;
        Assert.Equal(definition.Separator, restoredDefinition.Separator);
        Assert.Equal(definition.Parts, restoredDefinition.Parts);
        Assert.Equal(materialized, JsonSerializer.Deserialize<MaterializedName>(JsonSerializer.Serialize(materialized)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad:name")]
    [InlineData("CON")]
    [InlineData("name.")]
    [InlineData("name ")]
    public void WindowsValidation_RejectsUnsafeStems(string stem) => Assert.NotNull(WindowsOutputNameValidator.ValidateStem(stem));

    [Fact]
    public void Planner_ReportsOverlongFilename()
    {
        var input = Path.Combine(_root, "long-input");
        Directory.CreateDirectory(input);
        var options = Options(input, Path.Combine(_root, "long-output")) with
        { Naming = new([new(NamePartKind.CustomText, new string('x', 256))]) };
        var plan = EncodingJobPlanner.Plan(EncodingJobPlanner.Define(options,
            [Source(Path.Combine(input, "a.mp4"), "mp4")]), _ => new(false, 0));
        Assert.Contains(plan.Items.Single().Issues, issue => issue.Code == "naming.path-too-long");
    }

    [Fact]
    public void Planner_MaterializesStableOrderAndContainerExtensionsOutsideNameParts()
    {
        var input = Path.Combine(_root, "input");
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(input);
        var options = Options(input, output) with
        {
            Naming = new([new(NamePartKind.OriginalName), new(NamePartKind.Sequence01)])
        };
        var sources = new[]
        {
            Source(Path.Combine(input, "b.mov"), "mov", 2),
            Source(Path.Combine(input, "a.mkv"), "mkv", 1),
            Source(Path.Combine(input, "c.mp4"), "mp4", 3)
        };
        var plan = EncodingJobPlanner.Plan(EncodingJobPlanner.Define(options, sources), _ => new(false, 0));
        Assert.Equal(["a_01.mkv", "b_02.mov", "c_03.mp4"], plan.Items.Select(item => Path.GetFileName(item.OutputPaths.Single())));
        Assert.Equal([1, 2, 3], plan.Items.Select(item => item.Definition.MaterializedName!.Sequence));
        Assert.All(plan.Items, item => Assert.DoesNotContain('.', item.Definition.MaterializedName!.Stem!));
    }

    [Fact]
    public void Planner_DetectsCaseInsensitiveDuplicatesAndSourceCollision()
    {
        var input = Path.Combine(_root, "input2");
        Directory.CreateDirectory(input);
        var duplicateOptions = Options(input, Path.Combine(_root, "out2")) with
        { Naming = new([new(NamePartKind.CustomText, "Same")]) };
        var duplicate = EncodingJobPlanner.Plan(EncodingJobPlanner.Define(duplicateOptions,
            [Source(Path.Combine(input, "a.mp4"), "mp4"), Source(Path.Combine(input, "b.mp4"), "mp4")]), _ => new(false, 0));
        Assert.Contains(duplicate.Issues, issue => issue.Code == "encoding.output-collision");

        var sourcePath = Path.Combine(input, "same.mp4");
        var collisionOptions = Options(input, input) with { Naming = new([new(NamePartKind.CustomText, "same")]) };
        var sourceCollision = EncodingJobPlanner.Plan(EncodingJobPlanner.Define(collisionOptions,
            [Source(sourcePath, "mp4")]), _ => new(false, 0));
        Assert.Contains(sourceCollision.Items.Single().Issues, issue => issue.Code == "encoding.source-overwrite");
    }

    [Fact]
    public void ExistingOutputPolicy_IsAppliedToAlreadyMaterializedPath()
    {
        var input = Path.Combine(_root, "input3");
        Directory.CreateDirectory(input);
        var options = Options(input, Path.Combine(_root, "out3")) with
        { Naming = new([new(NamePartKind.CustomText, "reserved")]) };
        var plan = EncodingJobPlanner.Plan(EncodingJobPlanner.Define(options,
            [Source(Path.Combine(input, "a.mp4"), "mp4")]), _ => new(true, 10));
        Assert.Equal(JobPlanDisposition.Skip, plan.Items.Single().Disposition);
        Assert.Equal("reserved.mp4", Path.GetFileName(plan.Items.Single().OutputPaths.Single()));
    }

    [Fact]
    public void Naming_IsSnapshottedAndChangesOutputIdentity()
    {
        var input = Path.Combine(_root, "input4");
        Directory.CreateDirectory(input);
        var mutableParts = new List<NamePart> { new(NamePartKind.OriginalName) };
        var firstOptions = Options(input, Path.Combine(_root, "out4")) with { Naming = new(mutableParts) };
        var first = EncodingJobPlanner.Define(firstOptions, [Source(Path.Combine(input, "a.mp4"), "mp4")]);
        mutableParts.Add(new(NamePartKind.CustomText, "changed-after-queue"));
        Assert.Single(first.Options.Naming!.Parts);
        Assert.Equal("a", first.Items.Single().MaterializedName!.Stem);

        var secondOptions = firstOptions with
        { Naming = new([new(NamePartKind.OriginalName), new(NamePartKind.CustomText, "new")]) };
        var second = EncodingJobPlanner.Define(secondOptions, [Source(Path.Combine(input, "a.mp4"), "mp4")]);
        Assert.NotEqual(EncodingOutputIdentity.Create(first.Items.Single(), first.Options).OptionsHash,
            EncodingOutputIdentity.Create(second.Items.Single(), second.Options).OptionsHash);
    }

    private static EncodingJobOptions Options(string input, string output) => new(input, output,
        OutputResolution.Source, RecoveryStrategy.Normal, new EncodingOptions(), null, "_legacy", false, false, false,
        MaterializationPolicy: new(Container: OutputContainerPolicy.SameAsSource));

    private static EncodingSource Source(string path, string container, int? order = null) =>
        new(path, 1, TimeSpan.FromSeconds(1), CapabilityOrder: order,
            MediaTraits: new("h264", 1920, 1080, 24, container));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ExportFilenameDefaultsTests
{
    [Theory]
    [InlineData((int)ExportNamingDefault.Ordinary)]
    [InlineData((int)ExportNamingDefault.Subclip)]
    public void OriginalNameIsSoleDefaultAndOtherExportDefaultsStayUnchanged(int naming)
    {
        using var fixture=new Fixture();var model=fixture.Model(naming:(ExportNamingDefault)naming);
        Assert.Equal(new[]{NamePartKind.OriginalName},model.NameParts.Select(part=>part.Kind));
        Assert.Equal("clip001.mp4",model.PreviewName);
        Assert.Equal(Path.Combine(fixture.Root,"Exports","clip001.mp4"),model.CurrentPlan!.Items[0].OutputPaths[0]);
        Assert.Equal(fixture.Root,model.Destination);Assert.Equal(ExportDestinationMode.SpecificFolder,model.DestinationMode);
        Assert.True(model.CreateSubfolder);Assert.Equal("Exports",model.SubfolderName);
        Assert.Equal(ExportContainerChoice.SameAsSource,model.Container);Assert.Equal(ExportCodecChoice.SameAsSource,model.Codec);
        Assert.Equal(AudioEncodingMode.Copy,model.Encoding.AudioMode);
        Assert.Equal(NamePartSeparator.Hyphen,model.Separator);
        Assert.Equal(Enum.GetValues<NamePartKind>().Order(),ExportPresentation.NameParts.Select(choice=>choice.Value).Order());
        Assert.True(model.CanExport);
    }

    [Fact]
    public void EmptyCustomIsARealEmptyTokenAndNeverSerializesItsDisplayHint()
    {
        using var fixture=new Fixture();var model=fixture.Model();model.AddPart(NamePartKind.CustomText);
        Assert.Equal("",model.NameParts[1].Text);Assert.Equal(2,model.NameParts.Count);
        Assert.Equal("clip001.mp4",model.PreviewName);Assert.True(model.CanExport);
        var naming=model.CurrentPlan!.Definition.Options.Naming!;
        var json=JsonSerializer.Serialize(naming);
        Assert.Equal("",JsonSerializer.Deserialize<NamePartsDefinition>(json)!.Parts[1].Text);
        Assert.DoesNotContain("Custom",json);Assert.DoesNotContain("Text\":\"Text",json);
        model.UpdateCustomText(1,"Custom"); // An intentional user value is still legal.
        Assert.Equal("clip001-Custom.mp4",model.PreviewName);
        model.UpdateCustomText(1,"");Assert.Equal("clip001.mp4",model.PreviewName);
        model.RemovePart(0);Assert.False(model.CanExport);
        Assert.Contains(model.Errors,issue=>issue.Code=="naming.invalid-filename" && issue.Message.Contains("empty"));
        Assert.Equal("",model.CurrentPlan!.Items[0].Definition.MaterializedName!.Stem);
    }

    [Theory]
    [InlineData((int)NamePartSeparator.Hyphen,"clip-002")]
    [InlineData((int)NamePartSeparator.Underscore,"clip_002")]
    [InlineData((int)NamePartSeparator.Space,"clip 002")]
    [InlineData((int)NamePartSeparator.None,"clip002")]
    public void EmptyCustomContributesNoLeadingTrailingOrRepeatedSeparator(int separator,string expected)
    {
        var naming=new NamePartsDefinition([new(NamePartKind.CustomText,""),new(NamePartKind.OriginalName),
            new(NamePartKind.CustomText,null),new(NamePartKind.Sequence001),new(NamePartKind.CustomText,"")],(NamePartSeparator)separator);
        Assert.Equal(expected,NamePartsRenderer.Materialize(naming,new("clip",2)).Stem);
        Assert.Equal(" ",NamePartsRenderer.Materialize(new([new(NamePartKind.CustomText," ")]),new("clip",1)).Stem);
        Assert.NotNull(WindowsOutputNameValidator.ValidateStem(" ")); // No new whitespace normalization.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalOnlyNeverOverwritesASelectedSourceEvenWithOverwrite(bool overwrite)
    {
        using var fixture=new Fixture();var model=fixture.Model();
        model.CreateSubfolder=false;model.OverwriteExisting=overwrite;
        Assert.Contains(model.Errors,issue=>issue.Code=="encoding.source-overwrite");Assert.False(model.CanExport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalOnlyRetainsExistingOutputSkipAndOverwriteWarnings(bool overwrite)
    {
        using var fixture=new Fixture();var model=fixture.Model(inspect:_=>new(true,20));model.OverwriteExisting=overwrite;
        var item=Assert.Single(model.CurrentPlan!.Items);
        Assert.Equal(overwrite?JobPlanDisposition.Process:JobPlanDisposition.Skip,item.Disposition);
        Assert.Contains(item.Issues,issue=>issue.Code==(overwrite?"encoding.existing-output-replaced":"encoding.existing-output-differs")
            && issue.Severity==JobIssueSeverity.Warning);
        Assert.Equal(overwrite,model.CanExport);
    }

    [Theory]
    [InlineData("h264","mp4",".mp4")]
    [InlineData("hevc","mov",".mov")]
    [InlineData("hevc","mkv",".mkv")]
    public void OriginalOnlyUsesMaterializedContainerExtension(string codec,string container,string extension)
    {
        using var fixture=new Fixture();var model=fixture.Model(metadata:Metadata(codec,container));
        Assert.Equal("clip001"+extension,model.PreviewName);
        Assert.EndsWith(extension,model.CurrentPlan!.Items[0].OutputPaths[0]);Assert.True(model.CanExport);
        model.Container=ExportContainerChoice.Mkv;
        Assert.Equal("clip001.mkv",model.PreviewName);
    }

    [Fact]
    public void RepeatedSavedNamesRemainBlockedUntilUserAddsSequenceAndKeepSubclipMeaning()
    {
        using var fixture=new Fixture();var input=fixture.Input;
        var inputs=Enumerable.Range(0,2).Select(index=>input with
        {
            InitialTrim=new(TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(index+1),TimeSpan.FromSeconds(index+1.4)),
            RangeIsFixed=true,NamingOriginalName="Saved Take",NamingIndexNumberBasis="clip001",
            ExportProvenance=new(ExportItemKind.Subclip,input.AssetId,Guid.NewGuid(),"Saved Take",1)
        }).ToArray();
        var model=fixture.Model(inputs:inputs,naming:ExportNamingDefault.Subclip);
        model.ApplyResolvedRanges(inputs.Select(value=>(ResolvedMediaRange?)new ResolvedMediaRange(value.InitialTrim!,TimeSpan.Zero,
            value.InitialTrim!.In!.Value,value.InitialTrim.Out!.Value+TimeSpan.FromSeconds(.04),value.InitialTrim.Out.Value)).ToArray());
        Assert.Equal("Saved Take.mp4",model.PreviewName);Assert.Contains(model.Errors,i=>i.Code=="encoding.output-collision");
        Assert.False(model.CanExport);Assert.All(model.CurrentPlan!.Items,i=>Assert.EndsWith("Saved Take.mp4",i.OutputPaths[0]));
        model.AddPart(NamePartKind.Sequence001);
        Assert.True(model.CanExport);Assert.Equal(new[]{"Saved Take-001.mp4","Saved Take-002.mp4"},model.CurrentPlan!.Items.Select(i=>Path.GetFileName(i.OutputPaths[0])));
        Assert.All(model.CurrentPlan.Items,i=>Assert.NotNull(i.Definition.ResolvedRange));
    }

    [Fact]
    public Task NativeCustomHintFocusTypingClearAndDialogInitializationUseRealEmptyValue() => StaDispatcher.RunAsync(async ()=>
    {
        TestWpfApplication.EnsureLoaded();using var fixture=new Fixture();var model=fixture.Model();
        var preflight=new ExportPreflightCoordinator(new("ffprobe",new Runner()),_=>Task.FromResult(Available));
        var dialog=new ExportDialog(model,null!,null,preflight){ShowInTaskbar=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual};
        try
        {
            dialog.Show();await Until(()=>model.CanExport);
            Assert.Single(model.NameParts);Assert.Equal(NamePartKind.OriginalName,model.NameParts[0].Kind);
            dialog.AddPartCombo.SelectedItem=ExportPresentation.NameParts.Single(choice=>choice.Value==NamePartKind.CustomText);
            Descendants(dialog).OfType<Button>().Single(button=>AutomationProperties.GetName(button)=="Add name part")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);dialog.UpdateLayout();
            var box=Descendants(dialog.NamePartsComposer).OfType<TextBox>().Single(text=>text.IsVisible);
            var hint=(TextBlock)box.Template.FindName("InputHint",box);
            Assert.Equal("",box.Text);Assert.Equal("Custom",hint.Text);Assert.Equal(Visibility.Visible,hint.Visibility);
            Assert.False(hint.IsHitTestVisible);Assert.Equal(box.Padding,hint.Margin);
            Assert.Equal(ScrollBarVisibility.Hidden,box.HorizontalScrollBarVisibility);Assert.Equal(TextWrapping.NoWrap,box.TextWrapping);
            await Until(()=>model.CanExport);var beforeFocus=model.CurrentPlan;
            Assert.True(box.Focus());await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("",hint.Text);Assert.Equal("",box.Text);Assert.Equal("",model.NameParts[1].Text);
            Assert.Same(beforeFocus,model.CurrentPlan);Assert.False(model.IsPlanPending);
            box.SelectedText="mine";Assert.Equal("mine",model.NameParts[1].Text);
            Assert.Equal("clip001-mine.mp4",model.PreviewName);Assert.False(dialog.ExportButton.IsEnabled);
            box.SelectAll();EditingCommands.Delete.Execute(null,box);
            Assert.Equal("",box.Text);Assert.Equal("clip001.mp4",model.PreviewName);Assert.Equal("",hint.Text);
            Assert.True(dialog.AddPartCombo.Focus());await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Custom",hint.Text);Assert.Equal("",box.Text);
            await Until(()=>model.CanExport);
            Assert.Equal("",model.CurrentPlan!.Definition.Options.Naming!.Parts[1].Text);
            Assert.DoesNotContain("Custom",model.CurrentPlan.Items[0].OutputPaths[0]);
            Assert.Equal("Exports",model.SubfolderName);Assert.Equal(fixture.Root,model.Destination);
        }
        finally{dialog.Close();}
        Assert.Single(fixture.Model().NameParts); // Reopening has no new preference store.
    });

    private static async Task Until(Func<bool> condition)
    {var timer=Stopwatch.StartNew();while(!condition()){Assert.True(timer.ElapsedMilliseconds<10000,"Validation did not settle.");await Task.Delay(10);}}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var next in Descendants(child))yield return next;}}
    private static readonly EncoderCapability Available=new(EncoderBackend.NvidiaNvenc,EncoderCapabilityState.ImplementedAndAvailable,"fixture");
    private static MediaMetadata Metadata(string codec="h264",string container="mp4")=>new(1920,1080,25,5,7,codec,true,Container:container,AudioCodec:"aac",AudioSampleRate:48000,AudioChannels:2);
    private sealed class Fixture:IDisposable
    {
        public string Root{get;}=Directory.CreateTempSubdirectory("filename-defaults-").FullName;
        public EncodingHandoffInput Input{get;}
        public Fixture(){var path=Path.Combine(Root,"clip001.mp4");File.WriteAllText(path,"fixture");Input=new(Guid.NewGuid(),Guid.NewGuid(),path,"clip001.mp4",7,null,SourceIdentity:TrimSourceIdentity.Read(path));}
        public ExportDialogModel Model(EncodingHandoffInput[]? inputs=null,ExportNamingDefault naming=ExportNamingDefault.Ordinary,
            Func<string,OutputFileSnapshot>? inspect=null,MediaMetadata? metadata=null)
        {
            inputs??=[Input];var model=new ExportDialogModel(new(inputs,[],Root),new(),[],[],new Resources(),inspect??(_=>new(false,0)),namingDefault:naming);
            model.ApplyMetadata(inputs.Select(_=>metadata??Metadata()).ToArray());model.ApplyEncoderCapability(Available);return model;
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    private sealed class Resources:IEncodingLutResourceStore
    {
        public string Resolve(MaterializedLutResource resource)=>throw new NotSupportedException();
        public Task<MaterializedLutResource> SnapshotAsync(ColorLutStage stage,ManagedLutResource resource,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class Runner:IProbeProcessRunner
    {
        public Task<ProbeProcessResult> RunAsync(string executable,IReadOnlyList<string> arguments,CancellationToken cancellationToken=default)=>Task.FromResult(new ProbeProcessResult(0,
            """{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"avg_frame_rate":"25/1","duration":"5"}],"format":{"format_name":"mp4","duration":"5"}}""",""));
    }
}

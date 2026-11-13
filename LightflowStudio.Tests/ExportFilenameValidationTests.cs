using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ExportFilenameValidationTests
{
    private static readonly EncoderCapability Available = new(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedAndAvailable, "fixture");
    private static readonly MediaMetadata Metadata = new(1920,1080,25,5,7,"h264",true,Container:"mp4",AudioCodec:"aac",AudioSampleRate:48000,AudioChannels:2);

    [Fact]
    public Task RapidEditsDebounceAndReturningToOriginalTextUsesCurrentState() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture = new Fixture(100);
        var calls = 0;
        var model = fixture.Model(_ => { Interlocked.Increment(ref calls); return new(false,0); });
        model.AddPart(NamePartKind.CustomText); calls = 0;
        Enable(model);
        for(var i=0;i<30;i++) model.UpdateCustomText(2,"edit"+i);
        model.UpdateCustomText(2,"");
        Assert.False(model.CanExport); Assert.Null(model.CurrentPlan);
        Assert.Equal("clip0-001.mp4",model.PreviewName);
        Assert.Equal(0,Volatile.Read(ref calls));
        await Until(() => !model.IsPlanPending);
        Assert.Equal(100,calls); Assert.True(model.CanExport);
        Assert.Equal("",model.CurrentPlan!.Definition.Options.Naming!.Parts[2].Text);
        var plan=model.CurrentPlan;
        model.UpdateCustomText(2,"");
        Assert.Same(plan,model.CurrentPlan); Assert.False(model.IsPlanPending);
        model.CloseValidation();
    });

    [Fact]
    public Task EditingAndSettingsChangesSupersedeBlockedFilesystemPlan() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(2);
        using var gate=new Gate();
        var model=fixture.Model(_=>{gate.WaitOnce();return new(false,0);});
        model.AddPart(NamePartKind.CustomText);
        Enable(model); await Until(()=>!model.IsPlanPending);
        gate.Arm(); model.UpdateCustomText(2,"old");
        await Until(()=>gate.Entered);
        var sw=Stopwatch.StartNew();
        model.UpdateCustomText(2,"latest");
        model.Destination=Path.Combine(fixture.Root,"changed");
        model.Container=ExportContainerChoice.Mov;
        model.Codec=ExportCodecChoice.Hevc;
        model.Separator=NamePartSeparator.Underscore;
        model.MovePart(2,-1);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds<500,"Edits blocked behind filesystem validation.");
        Assert.Contains("latest",model.PreviewName); Assert.EndsWith(".mov",model.PreviewName);
        Assert.False(model.CanExport);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>model.MaterializeAcceptedPlanAsync());
        gate.Release(); await Until(()=>!model.IsPlanPending);
        Assert.True(model.CanExport);
        Assert.All(model.CurrentPlan!.Items,item=>
        {
            Assert.Contains("changed",item.OutputPaths.Single()); Assert.Contains("latest",item.OutputPaths.Single());
            Assert.EndsWith(".mov",item.OutputPaths.Single());
            Assert.Equal(VideoCodec.Hevc,item.Definition.MaterializedExport!.Encoding.Codec);
        });
        var final=await model.MaterializeAcceptedPlanAsync();
        Assert.All(final.Items,item=>Assert.Contains("latest",item.OutputPaths.Single()));
        model.CloseValidation();
    });

    [Fact]
    public Task CompletedButQueuedOldResultCannotEnableExportOrRestoreOldPreview() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(1);
        var model=fixture.Model(); model.AddPart(NamePartKind.CustomText);
        var queue=new ConcurrentQueue<Action>(); model.EnableBackgroundValidation(action=>queue.Enqueue(action));
        await Until(()=>queue.Count>=2);
        model.UpdateCustomText(2,"newer");
        while(queue.TryDequeue(out var action)) action();
        Assert.False(model.CanExport); Assert.Null(model.CurrentPlan); Assert.Contains("newer",model.PreviewName);
        await Until(()=>queue.Count>=2);
        while(queue.TryDequeue(out var action)) action();
        Assert.True(model.CanExport); Assert.Contains("newer",model.CurrentPlan!.Items[0].OutputPaths[0]);
        model.CloseValidation();
    });

    [Fact]
    public Task SlowLutChangesAndCloseNeverPublishObsoleteValidation() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(2);
        using var gate=new Gate();
        var resources=new Resources(()=>gate.WaitOnce());
        var model=fixture.Model(resources:resources,assigned:true); model.AddPart(NamePartKind.CustomText);
        Enable(model); await Until(()=>!model.IsPlanPending);
        gate.Arm(); model.UpdateCustomText(2,"blocked"); await Until(()=>gate.Entered);
        model.Camera=model.CameraChoices[1]; // No LUT supersedes assigned validation.
        model.UpdateCustomText(2,"final");
        Assert.False(model.CanExport); gate.Release(); await Until(()=>!model.IsPlanPending);
        Assert.True(model.CanExport); Assert.All(model.CurrentPlan!.Items,item=>Assert.False(item.Definition.MaterializedExport!.Color!.ColorEnabled));
        gate.Arm(); model.Camera=model.CameraChoices[0]; await Until(()=>gate.Entered);
        var notices=0; model.PropertyChanged+=(_,_)=>notices++;
        model.CloseValidation(); gate.Release(); await Task.Delay(300);
        Assert.False(model.CanExport);Assert.Null(model.CurrentPlan);Assert.Equal(0,notices);
    });

    [Theory]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    [InlineData("source")]
    [InlineData("existing-skip")]
    [InlineData("existing-overwrite")]
    [InlineData("unresolved-token")]
    public Task LatestPlanRetainsSafetyClassifications(string scenario) => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(2);
        var exists=scenario.StartsWith("existing");
        var model=fixture.Model(_=>new(exists,20));
        model.CreateSubfolder=false;
        model.NameParts.Clear();
        model.AddPart(scenario=="unresolved-token"?NamePartKind.Date:NamePartKind.CustomText);
        if(scenario.StartsWith("existing")) model.AddPart(NamePartKind.Sequence001);
        if(scenario=="existing-overwrite")model.OverwriteExisting=true;
        Enable(model);
        if(scenario!="unresolved-token")model.UpdateCustomText(0,scenario=="invalid"?"bad?name":scenario=="source"?"clip0":"duplicate");
        Assert.False(model.CanExport);await Until(()=>!model.IsPlanPending);
        if(scenario=="invalid") Assert.Contains(model.Errors,i=>i.Code=="naming.invalid-filename");
        if(scenario=="duplicate")Assert.Contains(model.Errors,i=>i.Code=="encoding.output-collision");
        if(scenario=="source")Assert.Contains(model.Errors,i=>i.Code=="encoding.source-overwrite");
        if(scenario=="unresolved-token")Assert.Contains(model.Errors,i=>i.Code=="naming.unresolved");
        if(scenario=="existing-skip") {Assert.False(model.CanExport);Assert.All(model.SubmissionItems,i=>Assert.True(i.WillSkip));}
        else if(scenario=="existing-overwrite") {Assert.True(model.CanExport);Assert.All(model.SubmissionItems,i=>Assert.True(i.WillOverwrite));}
        else Assert.False(model.CanExport);
        model.CloseValidation();
    });

    [Fact]
    public Task MixedRangesAndSelectionRemainAuthoritativeAcrossValidation() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(3);
        var inputs=fixture.Inputs.Select((input,index)=>input with
        {
            InitialTrim=index==2?null:new(TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(3)),
            RangeIsFixed=true,
            ExportProvenance=new(index==0?ExportItemKind.Subclip:index==1?ExportItemKind.NoSubclipActiveRange:ExportItemKind.NoSubclipFullSourceFallback,input.AssetId,index==0?Guid.NewGuid():null,index==0?"Saved":null,1)
        }).ToArray();
        var model=fixture.Model(inputs:inputs); model.AddPart(NamePartKind.CustomText);
        model.ApplyResolvedRanges(inputs.Select(input=>input.InitialTrim is {} range?(ResolvedMediaRange?)new ResolvedMediaRange(range,TimeSpan.Zero,TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(3.04),TimeSpan.FromSeconds(3)):null).ToArray());
        Enable(model);model.UpdateCustomText(2,"mixed");await Until(()=>!model.IsPlanPending);
        Assert.True(model.CanExport); Assert.Equal(3,model.CurrentPlan!.Items.Count);
        Assert.NotNull(model.CurrentPlan.Items[0].Definition.ResolvedRange);Assert.NotNull(model.CurrentPlan.Items[1].Definition.ResolvedRange);
        Assert.Null(model.CurrentPlan.Items[2].Definition.ResolvedRange);
        model.IncludeNoSubclipSources=false;Assert.False(model.CanExport);await Until(()=>!model.IsPlanPending);
        Assert.Single(model.CurrentPlan!.Items);Assert.Equal(ExportItemKind.Subclip,model.CurrentPlan.Items[0].Definition.ExportProvenance!.Kind);
        model.CloseValidation();
    });

    [Fact]
    public Task NativeEditingKeepsViewportCaretSelectionAndDispatcherResponsive() => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();using var fixture=new Fixture(100);using var gate=new Gate();
        var model=fixture.Model(_=>{gate.WaitOnce();return new(false,0);});model.AddPart(NamePartKind.CustomText);
        var coordinator=new ExportPreflightCoordinator(new("ffprobe",new Runner()),_=>Task.FromResult(Available));
        var dialog=new ExportDialog(model,null!,null,coordinator){ShowInTaskbar=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual};
        try
        {
            dialog.Show(); await Until(()=>model.CanExport,15000);dialog.UpdateLayout();
            var box=Descendants(dialog.NamePartsComposer).OfType<TextBox>().Single(x=>x.IsVisible);
            box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
            Assert.Null(typeof(ExportDialog).GetField("_dragPartIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog));
            var observations=new List<string>{"operation,elapsed_ms,viewport_height,horizontal_offset,export_enabled"};
            box.Focus();gate.Arm();var firstEdit=Stopwatch.StartNew();box.Text="blocking";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);firstEdit.Stop();
            observations.Add($"first-edit,{firstEdit.Elapsed.TotalMilliseconds:F3},20,0,{dialog.ExportButton.IsEnabled}");
            await Until(()=>gate.Entered);
            foreach(var operation in new[]{"single-character","rapid-type","backspace","delete","long-paste","home","end","select"})
            {
                var timer=Stopwatch.StartNew();
                switch(operation)
                {
                    case "single-character":box.CaretIndex=box.Text.Length;box.SelectedText="x";break;
                    case "rapid-type":for(var i=0;i<20;i++){box.CaretIndex=box.Text.Length;box.SelectedText="r";}break;
                    case "backspace":EditingCommands.Backspace.Execute(null,box);break;
                    case "delete":box.CaretIndex=0;EditingCommands.Delete.Execute(null,box);break;
                    case "long-paste":box.SelectAll();box.SelectedText=new string('L',160);break;
                    case "home":EditingCommands.MoveToLineStart.Execute(null,box);break;
                    case "end":EditingCommands.MoveToLineEnd.Execute(null,box);break;
                    case "select":box.Select(10,50);break;
                }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);dialog.UpdateLayout();timer.Stop();
                var scroll=Descendants(box).OfType<ScrollViewer>().First();
                observations.Add($"{operation},{timer.Elapsed.TotalMilliseconds:F3},{scroll.ViewportHeight},{scroll.HorizontalOffset},{dialog.ExportButton.IsEnabled}");
                Assert.False(dialog.ExportButton.IsEnabled);Assert.True(scroll.ViewportHeight>=18);
                Assert.Equal(Visibility.Collapsed,scroll.ComputedHorizontalScrollBarVisibility);
                Assert.True(timer.ElapsedMilliseconds<1000,"Dispatcher editing stalled behind blocked validation.");
            }
            Assert.Equal(50,box.SelectionLength);Assert.True(box.IsKeyboardFocusWithin);
            Assert.Equal(box.Text,model.NameParts[2].Text);Assert.Contains(box.Text,model.PreviewName);
            var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../artifacts/filename-validation"));Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory,"native-after.csv"),observations);
            gate.Release();await Until(()=>!model.IsPlanPending);Assert.True(dialog.ExportButton.IsEnabled);
            gate.Arm();box.Text="close-pending";await Until(()=>gate.Entered);
            dialog.Close();gate.Release();await Task.Delay(250);
            Assert.Null(model.CurrentPlan);Assert.False(model.CanExport);
        }
        finally{gate.Release();dialog.Close();}
    });

    [Fact]
    public Task SharedLutIntegrityIsCheckedOncePerPlanAndRecheckedAtSubmission() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(20);int calls=0;
        var model=fixture.Model(resources:new Resources(()=>Interlocked.Increment(ref calls)),assigned:true);
        model.AddPart(NamePartKind.CustomText);Enable(model);await Until(()=>model.CanExport);calls=0;
        model.UpdateCustomText(2,"current");await Until(()=>model.CanExport);Assert.Equal(1,calls);
        await model.MaterializeAcceptedPlanAsync();Assert.Equal(2,calls);
        model.CloseValidation();
    });

    [Fact]
    public Task FinalValidationRejectsAnEditWhileItsFilesystemCheckIsOutstanding() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(1);using var gate=new Gate();
        var model=fixture.Model(_=>{gate.WaitOnce();return new(false,0);});
        Enable(model);await Until(()=>model.CanExport);
        gate.Arm();var submission=model.MaterializeAcceptedPlanAsync();await Until(()=>gate.Entered);
        model.Destination=Path.Combine(fixture.Root,"new-destination");gate.Release();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>submission);
        await Until(()=>model.CanExport);model.CloseValidation();
    });

    [Fact]
    public Task ValidationFailureStaysDisabledAndCanRecoverOnAnotherEdit() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(1);var fail=false;
        var model=fixture.Model(_=>fail?throw new IOException("Destination unavailable"):new(false,0));
        model.AddPart(NamePartKind.CustomText);Enable(model);await Until(()=>model.CanExport);
        fail=true;model.UpdateCustomText(2,"failure");await Until(()=>!model.IsPlanPending);
        Assert.False(model.CanExport);Assert.Contains(model.Errors,i=>i.Message.Contains("Destination unavailable"));
        fail=false;model.UpdateCustomText(2,"recovered");await Until(()=>model.CanExport);model.CloseValidation();
    });

    [Fact]
    public Task MeasureBackgroundPlanningWithComparableSlowDestinationAndRealLutWorkloads() => StaDispatcher.RunAsync(async () =>
    {
        using var fixture=new Fixture(100);
        var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../artifacts/filename-validation"));Directory.CreateDirectory(directory);
        var store=new EncodingLutResourceStore(Path.Combine(fixture.Root,"luts"));
        var cube="LUT_3D_SIZE 33\n"+string.Concat(Enumerable.Repeat("0.25 0.5 0.75\n",33*33*33));
        var bytes=System.Text.Encoding.UTF8.GetBytes(cube);var path=Path.Combine(fixture.Root,"synthetic.cube");File.WriteAllBytes(path,bytes);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var lut=await store.SnapshotAsync(ColorLutStage.Camera,new(Guid.NewGuid(),"Synthetic","synthetic.cube",hash,LutDimension.ThreeDimensional,33,LutResourceAvailability.Available,path));
        var lines=new List<string>{"count,scenario,edit_ms,validation_including_debounce_ms,inspections,pending_generations,applied_generations,max_dispatcher_gap_ms,allocated_bytes,private_bytes_delta"};
        foreach(var count in new[]{1,20,100})
        foreach(var scenario in new[]{"fast","lut","slow-destination"})
        {
            if(scenario=="slow-destination"&&count!=100)continue;
            var inputs=fixture.Inputs.Take(count).Select(i=>i with{AssignedColor=scenario=="lut"?new(true,lut):null}).ToArray();
            int calls=0;bool slow=false;
            var model=new ExportDialogModel(new(inputs,[],fixture.Root),new(),[],[],store,_=>{Interlocked.Increment(ref calls);if(slow)Thread.Sleep(25);return new(false,0);});
            model.ApplyMetadata(inputs.Select(_=>(MediaMetadata?)Metadata).ToArray());model.ApplyEncoderCapability(Available);
            model.AddPart(NamePartKind.Sequence001); // Keep the I01 comparison workload explicit.
            model.AddPart(NamePartKind.CustomText);
            Enable(model);await Until(()=>model.CanExport);calls=0;slow=scenario=="slow-destination";
            var pending=0;var applied=0;model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(ExportDialogModel.IsPlanPending))pending++;if(e.PropertyName==string.Empty)applied++;};
            var heartbeat=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(10)};
            var gap=Stopwatch.StartNew();double maximumGap=0;
            heartbeat.Tick+=(_,_)=>{maximumGap=Math.Max(maximumGap,gap.Elapsed.TotalMilliseconds);gap.Restart();};
            var allocated=GC.GetTotalAllocatedBytes();var memory=Process.GetCurrentProcess().PrivateMemorySize64;
            heartbeat.Start();var total=Stopwatch.StartNew();var edit=Stopwatch.StartNew();model.UpdateCustomText(2,"measured");edit.Stop();
            Assert.False(model.CanExport);await Until(()=>!model.IsPlanPending,15000);total.Stop();heartbeat.Stop();
            Assert.True(model.CanExport);Assert.Equal(count,calls);Assert.Equal(1,pending);Assert.Equal(1,applied);
            lines.Add($"{count},{scenario},{edit.Elapsed.TotalMilliseconds:F3},{total.Elapsed.TotalMilliseconds:F3},{calls},{pending},{applied},{maximumGap:F3},{GC.GetTotalAllocatedBytes()-allocated},{Process.GetCurrentProcess().PrivateMemorySize64-memory}");
            model.CloseValidation();
        }
        File.WriteAllLines(Path.Combine(directory,"planning-after.csv"),lines);
    });

    private static void Enable(ExportDialogModel model)
    {var dispatcher=Dispatcher.CurrentDispatcher;model.EnableBackgroundValidation(action=>dispatcher.BeginInvoke(action));}
    private static async Task Until(Func<bool> condition,int timeout=10000)
    {var timer=Stopwatch.StartNew();while(!condition()){Assert.True(timer.ElapsedMilliseconds<timeout,"Timed out waiting for validation state.");await Task.Delay(10);}}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var next in Descendants(child))yield return next;}}
    private sealed class Fixture : IDisposable
    {
        public string Root {get;}=Directory.CreateTempSubdirectory("filename-validation-").FullName;
        public EncodingHandoffInput[] Inputs {get;}
        public Fixture(int count){Inputs=Enumerable.Range(0,count).Select(i=>{var path=Path.Combine(Root,$"clip{i}.mp4");File.WriteAllText(path,"fixture");return new EncodingHandoffInput(Guid.NewGuid(),Guid.NewGuid(),path,$"clip{i}.mp4",7,null,SourceIdentity:TrimSourceIdentity.Read(path));}).ToArray();}
        public ExportDialogModel Model(Func<string,OutputFileSnapshot>? inspect=null,Resources? resources=null,bool assigned=false,EncodingHandoffInput[]? inputs=null)
        {
            inputs??=Inputs;
            if(assigned)
            {
                var lut=new MaterializedLutResource(Guid.NewGuid(),ColorLutStage.Camera,"fixture",new string('a',64),"fixture.cube");
                inputs=inputs.Select(i=>i with{AssignedColor=new(true,lut)}).ToArray();
            }
            var model=new ExportDialogModel(new(inputs,[],Root),new(),[],[],resources??new Resources(),inspect??(_=>new(false,0)));
            model.AddPart(NamePartKind.Sequence001); // These mutation/race fixtures use an explicit three-part recipe.
            model.ApplyMetadata(inputs.Select(_=>(MediaMetadata?)Metadata).ToArray());model.ApplyEncoderCapability(Available);return model;
        }
        public void Dispose(){Directory.Delete(Root,true);}
    }
    private sealed class Gate : IDisposable
    {
        private readonly ManualResetEventSlim _release=new(false);private int _armed;private int _entered;
        public bool Entered=>Volatile.Read(ref _entered)!=0;
        public void Arm(){_release.Reset();Volatile.Write(ref _entered,0);Volatile.Write(ref _armed,1);}
        public void WaitOnce(){if(Interlocked.Exchange(ref _armed,0)==1){Volatile.Write(ref _entered,1);if(!_release.Wait(15000))throw new TimeoutException("Test gate was not released.");}}
        public void Release()=>_release.Set();public void Dispose(){_release.Set();_release.Dispose();}
    }
    private sealed class Resources(Action? resolve=null):IEncodingLutResourceStore
    {
        public string Resolve(MaterializedLutResource resource){resolve?.Invoke();return "fixture.cube";}
        public Task<MaterializedLutResource> SnapshotAsync(ColorLutStage stage,ManagedLutResource resource,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class Runner:IProbeProcessRunner
    {
        public Task<ProbeProcessResult> RunAsync(string executable,IReadOnlyList<string> arguments,CancellationToken cancellationToken=default)=>Task.FromResult(new ProbeProcessResult(0,"""{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"avg_frame_rate":"25/1","duration":"5"}],"format":{"format_name":"mp4","duration":"5"}}""",""));
    }
}

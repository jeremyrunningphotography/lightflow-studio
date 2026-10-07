using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed record Asset(int Id,string Name){public string Value=>"Image · 4 stars · flagged · orange";public override string ToString()=>Name;}
interface ICounts {int Created{get;}int Prepared{get;}int Cleared{get;}}
class MeasuredTable:TableView,ICounts {
 public int Created{get;private set;}public int Prepared{get;private set;}public int Cleared{get;private set;}
 protected override Type StyleKeyOverride=>typeof(TableView);
 protected override Control CreateContainerForItemOverride(object? item,int index,object? key){Created++;var c=base.CreateContainerForItemOverride(item,index,key);c.Height=32;return c;}
 protected override void PrepareContainerForItemOverride(Control c,object? item,int index){Prepared++;base.PrepareContainerForItemOverride(c,item,index);if(item is Asset a)AutomationProperties.SetName(c,$"{a.Name}, row {index+1}, {a.Value}");}
 protected override void ClearContainerForItemOverride(Control c){Cleared++;base.ClearContainerForItemOverride(c);}
}
// Bounded fallback comparison: public ListBox virtualization and retained cells, not a fork of Avalonia internals.
class RetainedDetails:ListBox,ICounts {
 public int Created{get;private set;}public int Prepared{get;private set;}public int Cleared{get;private set;}
 public int[] Order=Enumerable.Range(0,18).ToArray();public double[] Widths=Enumerable.Range(0,18).Select(i=>i==0?230d:110d).ToArray();
 readonly Dictionary<Control,StackPanel> views=new();
 protected override Type StyleKeyOverride=>typeof(ListBox);
 protected override Control CreateContainerForItemOverride(object? item,int index,object? key){Created++;var c=new ListBoxItem{Height=32};var p=new StackPanel{Orientation=Orientation.Horizontal,Height=24};for(int i=0;i<18;i++)p.Children.Add(new NamedCell{VerticalAlignment=VerticalAlignment.Center});views[c]=p;return c;}
 protected override void PrepareContainerForItemOverride(Control c,object? item,int index){Prepared++;base.PrepareContainerForItemOverride(c,item,index);((ListBoxItem)c).ContentTemplate=null;((ListBoxItem)c).Content=views[c];Refresh(c,item as Asset,index);}
 void Refresh(Control c,Asset? a,int index){var p=views[c];for(int i=0;i<18;i++){var b=(TextBlock)p.Children[i];int col=Order[i];b.Width=Widths[col];b.Text=a==null?"":col==0?a.Name:$"{DetailsProof.Headers[col]} {a.Id%6}";AutomationProperties.SetName(b,a==null?"":$"{a.Name}, row {index+1}, column {col+1} {DetailsProof.Headers[col]}: {b.Text}");}AutomationProperties.SetName(c,a==null?"":$"{a.Name}, row {index+1}, {a.Value}");}
 protected override void ClearContainerForItemOverride(Control c){Cleared++;base.ClearContainerForItemOverride(c);Refresh(c,null,-1);}
 public void ChangeColumns(){(Order[0],Order[1])=(Order[1],Order[0]);Widths[0]=280;foreach(var c in GetRealizedContainers())Refresh(c,c.DataContext as Asset,IndexFromContainer(c));}
}
static class DetailsProof {
 public static string[] Headers={"Name","Kind","Dimensions","Size","Rating","Flag","Capture date","Duration","Frame rate","Modified","Color label","Camera","Lens","Color / LUT","Range","Subclips","Keywords","Preview"};
 public static async Task Run(CompletionWindow w){foreach(int count in new[]{10000,100000})foreach(string mode in new[]{"stock","recycling-template","retained"})await Measure(w,count,mode);await Selection(w);}
 static async Task Measure(CompletionWindow w,int count,string mode){var population=Stopwatch.StartNew();var rows=Enumerable.Range(0,count).Select(i=>new Asset(i,$"Media_{i:D6}.jpg")).ToArray();var populationMs=population.Elapsed.TotalMilliseconds;
 ListBox list;if(mode=="retained")list=new RetainedDetails();else{var t=new MeasuredTable();foreach(var h in Headers){var c=new TableViewColumn{Header=h,Width=new GridLength(h=="Name"?230:110)};if(mode=="stock")c.Binding=new Binding(h=="Name"?"Name":"Value");else c.CellTemplate=new FuncDataTemplate<Asset>((a,_)=>{var b=new TextBlock();b.Bind(TextBlock.TextProperty,new Binding(h=="Name"?"Name":"Value"));return b;},true);t.Columns.Add(c);}list=t;}
 list.ItemsSource=rows;list.SelectionMode=SelectionMode.Multiple;list.AutoScrollToSelectedItem=false;w.Body.Content=list;var usable=Stopwatch.StartNew();await Task.Delay(140);w.UpdateLayout();Program.Log("population",new{count,mode,populationMs,usableMs=usable.Elapsed.TotalMilliseconds});
 var counts=(ICounts)list;var cellIds=new HashSet<int>();var rowIds=new HashSet<int>();int maxRows=0,maxCells=0;var times=new List<double>();var before=GC.GetTotalAllocatedBytes(true);var gc=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};var rss=new List<long>();
 for(int cycle=0;cycle<3;cycle++){for(int n=0;n<60;n++){int idx=n<30?n*3:(n*7919+cycle*271)%count;var timer=Stopwatch.StartNew();list.ScrollIntoView(rows[idx]);w.UpdateLayout();await Task.Delay(1);times.Add(timer.Elapsed.TotalMilliseconds);var realized=list.GetRealizedContainers().ToArray();maxRows=Math.Max(maxRows,realized.Length);var cells=realized.SelectMany(c=>mode=="retained"?c.GetVisualDescendants().OfType<TextBlock>().Cast<Control>():c.GetVisualDescendants().OfType<TableViewCell>().Cast<Control>()).ToArray();maxCells=Math.Max(maxCells,cells.Length);foreach(var c in realized)rowIds.Add(RuntimeHelpers.GetHashCode(c));foreach(var c in cells)cellIds.Add(RuntimeHelpers.GetHashCode(c));}
 using var process=Process.GetCurrentProcess();rss.Add(process.WorkingSet64);Program.Log("details-cycle",new{count,mode,cycle,allocated=GC.GetTotalAllocatedBytes(false)-before,workingSet=process.WorkingSet64,counts.Created,counts.Prepared,counts.Cleared,maxRows,maxCells});}
 var allocated=GC.GetTotalAllocatedBytes(true)-before;var collections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray();times.Sort();
 list.SelectedItems!.Clear();list.SelectedItems.Add(rows[100]);list.SelectedItems.Add(rows[101]);list.ScrollIntoView(rows[100]);await Task.Delay(30);CompletionWindow.Check(list.SelectedItems.Count==2,"Details multi-selection "+mode);
 if(list is MeasuredTable table){table.Columns[0].Width=new GridLength(280);table.Columns.Move(0,1);}else ((RetainedDetails)list).ChangeColumns();w.UpdateLayout();
 Program.Log("details-summary",new{count,mode,steps=180,allocated,collections,maxRows,maxCells,uniqueRows=rowIds.Count,uniqueCells=cellIds.Count,counts.Created,counts.Prepared,counts.Cleared,p50=times[times.Count/2],p95=times[(int)(times.Count*.95)],maxMs=times.Last(),rss,multiselect=list.SelectedItems.Count,columnResizeReorder=true,thumbnailDecode=false});
 CompletionWindow.Check(maxRows<150&&maxCells<2700,"bounded realized containers "+mode);if(count==100000){await w.Snapshot("details-"+mode);DumpPeers(list,"details-"+mode);InputProof.DumpNative("details-"+mode);}
 w.Body.Content=null;list.ItemsSource=null;await Task.Delay(20);}
 public static void DumpPeers(Control c,string name){var root=ControlAutomationPeer.CreatePeerForElement(c);var rows=new List<object>();void Visit(AutomationPeer? p,int depth){if(p==null||depth>18||rows.Count>500)return;if(!string.IsNullOrEmpty(p.GetName()))rows.Add(new{name=p.GetName(),role=p.GetAutomationControlType().ToString(),depth,selected=p.GetProvider<Avalonia.Automation.Provider.ISelectionItemProvider>()?.IsSelected});foreach(var child in p.GetChildren())Visit(child,depth+1);}Visit(root,0);Program.Log("automation-peers",new{name,rows});}
 static async Task Selection(CompletionWindow w){var all=Enumerable.Range(0,100000).Select(i=>new Asset(i,$"Asset {i:D6}")).ToArray();var selected=new HashSet<int>{55555,55556};int primary=55555,visibleAnchor=55550;var details=new RetainedDetails{ItemsSource=all,SelectionMode=SelectionMode.Multiple,AutoScrollToSelectedItem=false};var grid=new RetainedGrid{SelectedIds=selected,ItemsSource=all.Chunk(4).ToArray(),AutoScrollToSelectedItem=false};
 async Task Restore(ListBox view,Asset[] projection,string reason){w.Body.Content=view;await Task.Delay(40);if(view==details){details.ItemsSource=projection;details.SelectedItems!.Clear();details.SelectedItem=projection.FirstOrDefault(a=>a.Id==primary);foreach(var a in projection.Where(a=>selected.Contains(a.Id)&&a.Id!=primary))details.SelectedItems.Add(a);CompletionWindow.Check((details.SelectedItem as Asset)?.Id==primary&&details.SelectedItems.Cast<Asset>().Select(a=>a.Id).ToHashSet().SetEquals(selected),"actual primary and multiselection restored "+reason);int idx=Array.FindIndex(projection,a=>a.Id==visibleAnchor);if(idx<0)idx=Array.FindIndex(projection,a=>a.Id==primary);if(idx<0)idx=0;details.ScrollIntoView(projection[idx]);}else{grid.ItemsSource=projection.Chunk(4).ToArray();var index=Array.FindIndex(projection,a=>a.Id==visibleAnchor);grid.ScrollIntoView(Math.Max(0,index)/4);}w.UpdateLayout();await Task.Delay(30);if(reason=="Details-to-Grid-0"){grid.ScrollIntoView(primary/4);w.UpdateLayout();await Task.Delay(20);DumpPeers(grid,"Grid-selected");InputProof.DumpNative("Grid-selected");}var realized=view.GetRealizedContainers().ToArray();var ids=realized.SelectMany(c=>c.DataContext switch{Asset a=>new[]{a.Id},Asset[] band=>band.Select(x=>x.Id),_=>Array.Empty<int>()}).ToArray();bool anchor=ids.Contains(visibleAnchor);CompletionWindow.Check(anchor,"stable visible anchor "+reason);Program.Log("selection-anchor",new{reason,primary,selected=selected.Order().ToArray(),visibleAnchor,anchor,first=ids.FirstOrDefault(),last=ids.LastOrDefault(),realized=realized.Length});}
 for(int i=0;i<3;i++){await Restore(details,all,"Grid-to-Details-"+i);await Restore(grid,all,"Details-to-Grid-"+i);}await Restore(details,all.Select(x=>x with{}).ToArray(),"refresh-new-instances");await Restore(details,all.Reverse().ToArray(),"sort-reversed");await Restore(details,all.Where(x=>x.Id>=50000).ToArray(),"filter-survivors");w.Body.Content=new TextBlock{Text="Player placeholder"};await Restore(details,all,"return-from-Player");visibleAnchor=99990;await Restore(details,all,"large-jump");
 await Restore(grid,all,"Grid-large-jump");DumpPeers(grid,"Grid");InputProof.DumpNative("Grid");await w.Snapshot("grid-restoration");Program.Log("grid-recycling",new{grid.Created,grid.Prepared});
 // Query changes reset shift-range anchor, preserve stable selection; hidden IDs remain selected.
 var filtered=all.Where(x=>x.Id<100).ToArray();var fallback=filtered[0].Id;Program.Log("filter-hidden-selection",new{selected=selected.ToArray(),primary,visibleFallback=fallback,rangeAnchorReset=true,scope="identity policy; no exact pixel restoration"});}
}
class RetainedGrid:ListBox {
 public HashSet<int> SelectedIds=new();public int Created,Prepared;readonly Dictionary<Control,StackPanel> views=new();
 protected override Type StyleKeyOverride=>typeof(ListBox);
 protected override Control CreateContainerForItemOverride(object? item,int index,object? key){Created++;var c=new ListBoxItem();var p=new StackPanel{Orientation=Orientation.Horizontal,Height=120};for(int i=0;i<4;i++)p.Children.Add(new AssetTile{Width=220,Height=112});views[c]=p;return c;}
 protected override void PrepareContainerForItemOverride(Control c,object? item,int index){Prepared++;base.PrepareContainerForItemOverride(c,item,index);((ListBoxItem)c).Content=views[c];var a=(Asset[])item!;for(int i=0;i<4;i++){var b=(AssetTile)views[c].Children[i];b.IsVisible=i<a.Length;if(i<a.Length){b.Content=a[i].Name;b.Selected=SelectedIds.Contains(a[i].Id);b.SelectionOwner=this;var assetId=a[i].Id;b.SelectionChanged=value=>{if(value)SelectedIds.Add(assetId);else SelectedIds.Remove(assetId);};AutomationProperties.SetName(b,$"{a[i].Name}, Image, rating 4, flagged, orange");}}}
}

class AssetTile:Button {
 public bool Selected;public RetainedGrid? SelectionOwner;public Action<bool>? SelectionChanged;
 protected override Type StyleKeyOverride=>typeof(Button);
 protected override AutomationPeer OnCreateAutomationPeer()=>new TilePeer(this);
 class TilePeer:ButtonAutomationPeer,Avalonia.Automation.Provider.ISelectionItemProvider {
 readonly AssetTile tile;public TilePeer(AssetTile t):base(t){tile=t;}
 public bool IsSelected=>tile.Selected;public Avalonia.Automation.Provider.ISelectionProvider? SelectionContainer=>tile.SelectionOwner is {} g?ControlAutomationPeer.CreatePeerForElement(g)?.GetProvider<Avalonia.Automation.Provider.ISelectionProvider>():null;
 public void Select(){tile.Selected=true;tile.SelectionChanged?.Invoke(true);}public void AddToSelection()=>Select();public void RemoveFromSelection(){tile.Selected=false;tile.SelectionChanged?.Invoke(false);}
 protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.ListItem;
 }
}

class NamedCell:TextBlock {
 protected override Type StyleKeyOverride=>typeof(TextBlock);
 protected override AutomationPeer OnCreateAutomationPeer()=>new CellPeer(this);
 class CellPeer:TextBlockAutomationPeer {public CellPeer(TextBlock c):base(c){}protected override string? GetNameCore()=>AutomationProperties.GetName(Owner)??base.GetNameCore();}
}

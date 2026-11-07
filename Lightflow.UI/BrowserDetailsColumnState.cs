namespace Lightflow.UI;

/// <summary>One immutable, ID-keyed column snapshot shared by headers and every retained/recycled row.</summary>
internal sealed class BrowserDetailsColumnState
{
    public IReadOnlyList<BrowserColumnPreference> Columns { get; private set; } = BrowserDetailsLayout.Normalize(null);
    public event Action? Changed;
    public void Set(IReadOnlyList<BrowserColumnPreference> columns)
    {
        Columns=BrowserDetailsLayout.Normalize(columns);
        Changed?.Invoke();
    }
}

internal sealed record BrowserColumnDrop(int Boundary, double X, bool Valid);
internal static class BrowserColumnReorder
{
    public static BrowserColumnDrop Target(IReadOnlyList<BrowserColumnPreference> columns,string source,double x,bool inside)
    {
        var visible=columns.Where(c=>c.Visible).ToArray();
        var sourceIndex=Array.FindIndex(visible,c=>c.Id==source);
        var boundary=0;double edge=0;
        foreach(var column in visible)
        {
            if(x<edge+column.Width/2)break;
            edge+=column.Width;boundary++;
        }
        var destination=boundary>sourceIndex ? boundary-1 : boundary;
        return new(boundary,edge,inside && double.IsFinite(x) && sourceIndex>=0 && destination!=sourceIndex);
    }
    public static IReadOnlyList<BrowserColumnPreference> Move(IReadOnlyList<BrowserColumnPreference> columns,string source,int boundary)
    {
        var visible=columns.Where(c=>c.Visible).ToList();var index=visible.FindIndex(c=>c.Id==source);
        if(index<0 || boundary<0 || boundary>visible.Count)return columns;
        var moving=visible[index];visible.RemoveAt(index);visible.Insert(boundary>index ? boundary-1 : boundary,moving);
        // Hidden slots keep their remembered positions; a visible column moves as one ID/width/visibility record.
        var order=new Queue<BrowserColumnPreference>(visible);
        return columns.Select(c=>c.Visible ? order.Dequeue() : c).ToArray();
    }
}

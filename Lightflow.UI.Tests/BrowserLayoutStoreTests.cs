using Lightflow.Desktop.MacOS;
using Lightflow.UI;
using Xunit;

namespace Lightflow.UI.Tests;

public sealed class BrowserLayoutStoreTests
{
    [Fact]
    public void RestartRestoresProportionsAndHiddenPanelAndBadPreferencesCannotBlockStartup()
    {
        var root=Path.Combine(Path.GetTempPath(),"lf028-layout-"+Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(new BrowserLayout(),BrowserLayoutStore.Load(root));
            var expected=new BrowserLayout(.3,.5,.2,false);
            BrowserLayoutStore.Save(root,expected);
            Assert.Equal(expected,BrowserLayoutStore.Load(root));
            Assert.Single(Directory.GetFiles(root));
            File.WriteAllText(Path.Combine(root,"browser-layout.json"),"{invalid");
            Assert.Equal(new BrowserLayout(),BrowserLayoutStore.Load(root));
            File.WriteAllText(Path.Combine(root,"browser-layout.json"),"{\"Navigation\":-4}");
            Assert.Equal(new BrowserLayout(),BrowserLayoutStore.Load(root));
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}

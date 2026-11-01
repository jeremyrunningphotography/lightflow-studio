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
            var expected=new BrowserLayout(310,390,true,new ShellWindowGeometry(1500,950,80,100,2),Zoomed:true);
            BrowserLayoutStore.Save(root,expected);
            Assert.Equal(expected,BrowserLayoutStore.Load(root));
            Assert.Single(Directory.GetFiles(root));
            File.WriteAllText(Path.Combine(root,"browser-layout.json"),"{invalid");
            Assert.Equal(new BrowserLayout(),BrowserLayoutStore.Load(root));
            File.WriteAllText(Path.Combine(root,"browser-layout.json"),"{\"Navigation\":-4}");
            Assert.Equal(new BrowserLayout(),BrowserLayoutStore.Load(root));
            File.WriteAllText(Path.Combine(root,"browser-layout.json"),"{\"Navigation\":0.3,\"Assets\":0.5,\"Right\":0.2,\"RightOpen\":true}");
            var migrated=BrowserLayoutStore.Load(root);
            Assert.Equal(370.8,migrated.NavigationWidth,3); Assert.Equal(247.2,migrated.RightWidth,3); Assert.True(migrated.RightOpen);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}

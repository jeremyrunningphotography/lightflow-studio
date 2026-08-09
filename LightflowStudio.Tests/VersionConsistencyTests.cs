using System.Xml.Linq;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public class VersionConsistencyTests
{
    [Fact]
    public void AppVersion_Format_ShowsSemanticVersion()
    {
        Assert.Equal("2.7.4", AppVersion.Format(new Version(2, 7, 4, 19)));
        Assert.Equal("Unknown", AppVersion.Format(null));
    }

    [Fact]
    public void BuiltAssemblyVersion_MatchesAuthoritativeVersion()
    {
        Assert.Equal(ReadVersion(), AppVersion.Display);
    }

    [Fact]
    public void Readme_MatchesAuthoritativeApplicationVersion()
    {
        var root = FindRepositoryRoot();
        var version = ReadVersion();
        Assert.Contains($"Current version: **{version}**", File.ReadAllText(Path.Combine(root, "README.md")));
    }

    private static string ReadVersion()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "Directory.Build.props"));
        return document.Descendants("VersionPrefix").Single().Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the Lightflow Studio repository root.");
    }
}

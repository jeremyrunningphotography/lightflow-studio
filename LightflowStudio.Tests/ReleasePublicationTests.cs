using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ReleasePublicationTests
{
    [Fact]
    public void Publication_PreservesArtifactTagPermissionAndPackagingGatesWithoutCheckout()
    {
        var release = ReleaseJob();
        Assert.Contains("if: startsWith(github.ref, 'refs/tags/v')", release);
        Assert.Contains("needs: package", release);
        Assert.Contains("runs-on: windows-latest", release);
        Assert.Contains("contents: write", release);
        Assert.Contains("actions/download-artifact@v8", release);
        Assert.Contains("name: lightflow-studio-windows", release);
        Assert.Contains("path: dist", release);
        Assert.Contains("shell: pwsh", release);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", release);
        Assert.DoesNotContain("actions/checkout", release);
        Assert.DoesNotContain("working-directory:", release);
        Assert.DoesNotContain("jeremyrunningphotography/lightflow-studio", release);
    }

    [Theory]
    [InlineData("ExampleOwner/lightflow-studio")]
    [InlineData("fork-owner/repo.name_123")]
    public async Task Publication_ExecutesWorkflowCommandWithExplicitRepositoryOutsideGit(string repository)
    {
        // Deliberately support the workflow's existing single-line run steps, not general YAML.
        var commands = Regex.Matches(ReleaseJob(), @"(?m)^        run: (.+)\r?$")
            .Select(match => match.Groups[1].Value.Trim()).ToArray();
        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.StartsWith("gh release ", command));

        var workspace = Path.Combine(Path.GetTempPath(), "Lightflow Agent T publication " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "dist"));
        try
        {
            for (var directory = new DirectoryInfo(workspace); directory is not null; directory = directory.Parent)
                Assert.False(Path.Exists(Path.Combine(directory.FullName, ".git")));
            foreach (var asset in new[] { "LightflowStudio-1.2.3-win-x64-setup.exe", "LightflowStudio-1.2.3-win-x64-portable.zip", "SHA256SUMS.txt" })
                File.WriteAllText(Path.Combine(workspace, "dist", asset), "fixture only");

            // Override gh before evaluating the actual workflow command. No native CLI, token,
            // network, or publication is used; any added repository-scoped command is captured.
            var script = """
                $ErrorActionPreference = 'Stop'
                function gh { ConvertTo-Json -InputObject @($args) -Compress }
                """ + Environment.NewLine + string.Join(Environment.NewLine,
                    commands.Select(command => command.Replace("${{ github.ref_name }}", "v1.2.3", StringComparison.Ordinal)));
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = workspace,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(argument);
            start.Environment["GITHUB_REPOSITORY"] = repository;
            start.Environment.Remove("GH_REPO");
            start.Environment.Remove("GH_TOKEN");
            start.Environment.Remove("GITHUB_TOKEN");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Publication argument simulation timed out.");
            }
            Assert.True(process.ExitCode == 0, await error);
            var invocations = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToArray();
            Assert.Equal(commands.Length, invocations.Length);
            foreach (var arguments in invocations)
            {
                var repoFlag = Array.IndexOf(arguments, "--repo");
                Assert.True(repoFlag >= 0 && repoFlag + 1 < arguments.Length, "Every gh release invocation needs an explicit repository.");
                Assert.Equal(repository, arguments[repoFlag + 1]);
            }
            Assert.Equal(new[] { "release", "create", "v1.2.3", "dist/*", "--repo", repository,
                "--verify-tag", "--generate-notes", "--title", "Lightflow Studio v1.2.3" }, Assert.Single(invocations));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string ReleaseJob()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;
        var workflow = File.ReadAllText(Path.Combine(directory!.FullName, ".github", "workflows", "ci-release.yml"));
        var job = Regex.Match(workflow, @"(?ms)^  release:\r?\n.*?(?=^  \w+:|\z)");
        Assert.True(job.Success, "Release publication job must exist.");
        return job.Value;
    }
}

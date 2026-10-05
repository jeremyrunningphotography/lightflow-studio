using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class NoninteractiveValidationTests
{
    [Theory]
    [InlineData("--startup-smoke-test")]
    [InlineData("--jobs-workspace-smoke-test")]
    public void AutomatedStartup_RequiresIsolatedData_WhileNormalStartupDoesNot(string flag)
    {
        ValidationPresentation.ValidateStartup([], false);
        ValidationPresentation.ValidateStartup([flag], true);
        Assert.Contains("--data-root", Assert.Throws<InvalidOperationException>(() =>
            ValidationPresentation.ValidateStartup([flag], false)).Message);
    }

    [Fact]
    public async Task RealVisibleFocusableHwnd_RemainsOutsideInputDesktop_AndCloses()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            Assert.True(ValidationDesktop.IsNoninteractive);
            var input = OpenInputDesktop(0, false, 0x41);
            Assert.NotEqual(0, input);
            var edit = new TextBox();
            var window = new Window { Content = edit, Width = 480, Height = 300, Topmost = true };
            nint handle = 0;
            try
            {
                // Deliberately exercise the previously intrusive Show/Activate/Focus sequence,
                // on the private desktop. Nothing is moved, hidden or mocked after creation.
                window.Show(); window.Activate(); window.UpdateLayout(); edit.Focus();
                handle = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(0, handle);
                Assert.True(IsWindowVisible(handle));
                Assert.NotNull(PresentationSource.FromVisual(edit));
                Assert.Same(edit, Keyboard.FocusedElement);
                var inputWindows = new List<nint>();
                Assert.True(EnumDesktopWindows(input, (hwnd, _) => { inputWindows.Add(hwnd); return true; }, 0));
                Assert.DoesNotContain(handle, inputWindows);
                foreach (var hwnd in inputWindows)
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    Assert.NotEqual((uint)Environment.ProcessId, pid);
                }
            }
            finally { window.Close(); CloseDesktop(input); }
            Assert.False(IsWindow(handle));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Launcher_PropagatesExitCode_AndOwnsDescendantCleanup()
    {
        var root = Directory.CreateTempSubdirectory("lightflow-desktop-contract-").FullName;
        var pidFile = Path.Combine(root, "child.txt");
        var desktop = new ValidationDesktop();
        Process? child = null;
        try
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var command = "$p = Start-Process powershell.exe -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 120' -WindowStyle Hidden -PassThru; " +
                "$p.Id | Set-Content -LiteralPath '" + pidFile.Replace("'", "''") + "'; exit 7";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            desktop.Start(exe, $"\"{exe}\" -NoProfile -NonInteractive -EncodedCommand {encoded}", root);
            Assert.True(SpinWait.SpinUntil(() => desktop.HasExited, TimeSpan.FromSeconds(20)));
            Assert.Equal(7, desktop.ExitCode);
            child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim()));
            Assert.False(child.HasExited);
            Assert.True(desktop.ActiveProcesses >= 1);
            desktop.Dispose();
            Assert.True(child.WaitForExit(5000), "Disposing the run must terminate its orphaned descendant.");
        }
        finally { desktop.Dispose(); child?.Dispose(); Directory.Delete(root, true); }
    }

    private delegate bool WindowVisitor(nint window, nint context);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDesktopWindows(nint desktop, WindowVisitor visitor, nint context);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}

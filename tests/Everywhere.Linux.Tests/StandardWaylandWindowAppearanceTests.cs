using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Everywhere.Linux.Desktop;
using Everywhere.Linux.Platform;
using Everywhere.Views;

namespace Everywhere.Linux.Tests;

[NonParallelizable]
public sealed class StandardWaylandWindowAppearanceTests
{
    [AvaloniaTest]
    public void GnomeWindow_UsesOpaqueBackgroundAndFollowsThemeChanges()
    {
        using var session = new TestSession("GNOME");
        var desktop = LinuxDesktopServices.Create();
        using var helper = new LinuxWindowHelper(desktop.Overlays, desktop);
        var window = new TransientWindow();
        try
        {
            window.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
            {
                ["WindowBackgroundColor"] = Color.FromArgb(100, 20, 40, 60),
            };
            window.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
            {
                ["WindowBackgroundColor"] = Color.FromArgb(120, 70, 80, 90),
            };
            window.RequestedThemeVariant = ThemeVariant.Light;
            helper.InitializeWindow(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(((ISolidColorBrush)window.Background!).Color, Is.EqualTo(Color.FromRgb(20, 40, 60)));
                Assert.That(window.TransparencyLevelHint, Is.EqualTo(new[] { WindowTransparencyLevel.None }));
                Assert.That(window.WindowDecorations, Is.EqualTo(WindowDecorations.Full));
            });

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.That(((ISolidColorBrush)window.Background!).Color, Is.EqualTo(Color.FromRgb(70, 80, 90)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void HyprlandWindow_KeepsItsOriginalAppearance()
    {
        using var session = new TestSession("Hyprland");
        var desktop = LinuxDesktopServices.Create();
        using var helper = new LinuxWindowHelper(desktop.Overlays, desktop);
        var window = new TransientWindow();
        try
        {
            var originalTransparency = window.TransparencyLevelHint.ToArray();
            var originalDecorations = window.WindowDecorations;

            helper.InitializeWindow(window);

            Assert.Multiple(() =>
            {
                Assert.That(window.TransparencyLevelHint, Is.EqualTo(originalTransparency));
                Assert.That(window.WindowDecorations, Is.EqualTo(originalDecorations));
            });
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class TestSession : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public TestSession(string desktop)
        {
            Set("XDG_SESSION_TYPE", "wayland");
            Set("XDG_CURRENT_DESKTOP", desktop);
            Set("WAYLAND_DISPLAY", "test-wayland");
            Set("DISPLAY", null);
            Set("HYPRLAND_INSTANCE_SIGNATURE", desktop == "Hyprland" ? "test-instance" : null);
            Set("XDG_RUNTIME_DIR", "/tmp");
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        private void Set(string name, string? value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
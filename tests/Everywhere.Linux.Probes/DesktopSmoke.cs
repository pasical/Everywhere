using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Everywhere.Extensions;
using Everywhere.Linux.Desktop;
using SkiaSharp;

namespace Everywhere.Linux.Probes;

partial class Program
{
    /// <summary>Runs actual Main with test-only observation of committed window input.</summary>
    private static int RunDesktopMain(string[] args)
    {
        var report = args[1];
        using var observation = InputElement.TextInputEvent.AddClassHandler<Window>((window, input) =>
        {
            File.AppendAllText(report, JsonSerializer.Serialize(new
            {
                WindowType = window.GetType().FullName, window.Title, input.Text,
                NativeHandleKind = window.TryGetPlatformHandle()?.HandleDescriptor,
            }) + Environment.NewLine);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Everywhere.Linux.Program.Main(args.Skip(2).ToArray());
        return Environment.ExitCode;
    }

    /// <summary>Observes the production state/capture contracts in a controlled desktop, without a UI renderer.</summary>
    private static async Task<int> ReadDesktopStateAsync(string report)
    {
        var desktop = LinuxDesktopServices.Create();
        var outputs = await desktop.Compositor.GetOutputsAsync();
        var windows = await desktop.Compositor.GetWindowsInStackingOrderAsync();
        var active = await desktop.Compositor.GetActiveWindowAsync();
        var pointer = await desktop.Compositor.GetGlobalCursorPositionAsync();
        using var capture = await desktop.ScreenCapturer.CaptureOutputAsync(outputs[0].Name);
        using var image = capture.ToSKImage() ?? throw new InvalidDataException("The desktop capture is empty.");
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.ChangeExtension(report, ".png"), png.ToArray());
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            Kind = desktop.Kind.ToString(), desktop.Compositor.StackingOrderKind, Outputs = outputs,
            Windows = windows.Where(window => window.Title.StartsWith("Everywhere", StringComparison.Ordinal))
                .Select(window => new { window.Id, window.ProcessId, window.Title, window.ClientRect, window.FrameRect, window.OutputName }),
            Active = active?.Id, Pointer = pointer, CaptureBounds = capture.Bounds, CaptureSize = capture.Size,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
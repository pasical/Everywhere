using System.Text.Json;
using Avalonia;
using Everywhere.Automation;
using Everywhere.Linux.Automation;
using Everywhere.Linux.Desktop;

namespace Everywhere.Linux.Probes;

partial class Program
{
    // This probe requires an explicit compositor address. It never dumps other
    // applications, even when the supplied point is covered by another window.
    private static int Observe(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Usage: --observe REPORT ADDRESS X Y");
        var desktop = LinuxDesktopServices.Create();
        using var backend = new LinuxVisualElementBackend(desktop.Compositor, desktop.ScreenCapturer);
        var window = backend.ReadDesktop().Windows.Single(item => item.Id == args[2]);
        var point = new PixelPoint(int.Parse(args[3]), int.Parse(args[4]));
        if (backend.ReadDesktop().WindowAt(point)?.Id != window.Id)
            throw new InvalidOperationException("The test point is outside the explicitly selected visible window.");

        using var context = new VisualContext();
        using var turn = context.BeginTurn();
        using var retention = context.CreateRetention();
        var request = new VisualElementQueryRequest(VisualElementFields.All, 256);
        var isFocused = args[0] == "--observe-focused";
        // The synthetic provider emits focus events after the assistive listener registers.
        if (isFocused) Thread.Sleep(250);
        var hit = backend.Query(retention, isFocused ? VisualElementLocator.Focused : VisualElementLocator.FromPoint(point), request: request);
        var root = backend.GetWindow(retention, window);
        using var tree = VisualContextSnapshotter.CreateSnapshot(context, [root],
            allowedTraverseDirections: VisualContextTraverseDirections.Child);
        var nativeRootBounds = backend.Accessibility.Call(() =>
        {
            var pointer = backend.FindAccessibleWindow(window);
            if (pointer == 0) return (PixelRect?)null;
            try { return AtspiNative.ReadExtents(pointer, AtspiCoordinates.Window); }
            finally { AtspiNative.g_object_unref(pointer); }
        });
        var hitSnapshot = hit?.Snapshot;
        var hitReport = hitSnapshot is { } observed ? new
        {
            observed.Id,
            observed.Type,
            observed.States,
            observed.Name,
            observed.TextPreview,
            observed.Bounds,
            observed.ProcessId,
            NativeWindow = observed.NativeWindowHandle?.ToString("X"),
            FailureKind = hit?.Failure?.Kind.ToString(),
            FailureAgentMessage = hit?.Failure?.AgentMessage,
        } : null;
        var report = new
        {
            Address = window.Id,
            window.ProcessId,
            WindowBounds = window.SelectionBounds,
            NativeRootBounds = nativeRootBounds,
            Point = point,
            Hit = hitReport,
            Tree = VisualContextPromptBuilder.Build(context, tree),
        };
        File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { window.ProcessId, Window = window.SelectionBounds, Hit = hitReport }));
        return 0;
    }
}

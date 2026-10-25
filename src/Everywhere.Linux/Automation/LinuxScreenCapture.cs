using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Platform;
using Everywhere.Automation;
using Everywhere.Linux.Hyprland;
using SkiaSharp;

namespace Everywhere.Linux.Automation;

/// <summary>Owns a bounded visible-desktop capture. Bounds and pixel density are independent.</summary>
public sealed class LinuxScreenCapture : IVisualElementCapture
{
    public PixelRect Bounds
    {
        get;
    }
    public PixelFormat Format => PixelFormat.Bgra8888;
    public AlphaFormat AlphaFormat => AlphaFormat.Premul;
    public nint Data => _bitmap?.GetPixels() ?? 0;
    public PixelSize Size
    {
        get;
    }
    public int Stride
    {
        get;
    }
    private SKBitmap? _bitmap;

    private LinuxScreenCapture(SKBitmap bitmap, PixelRect bounds)
    {
        _bitmap = bitmap;
        Bounds = bounds;
        Size = new PixelSize(bitmap.Width, bitmap.Height);
        Stride = bitmap.RowBytes;
    }

    public static async Task<IVisualElementCapture> CaptureAsync(PixelRect bounds, CancellationToken cancellationToken)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new NotSupportedException("The capture region is empty.");
        var start = new ProcessStartInfo("grim") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-g");
        start.ArgumentList.Add(FormattableString.Invariant($"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}"));
        var monitors = HyprlandDesktop.VisibleOutputs(new HyprlandConnection().Read<HyprlandMonitor[]>("monitors"));
        var density = monitors.Where(monitor => monitor.Bounds.Intersects(bounds)).Select(monitor => monitor.Scale).DefaultIfEmpty(1).Max();
        var scale = Math.Min(density, (double)IVisualElementCapture.MaximumDimension / Math.Max(bounds.Width, bounds.Height));
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(scale.ToString("R", CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the screen capture helper.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var cancellation = timeout.Token.Register(() => { try { process.Kill(); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        using var png = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (count == 0)
                break;
            if (png.Length + count > 64 * 1024 * 1024)
                throw new InvalidDataException("The screenshot exceeded its buffer limit.");
            png.Write(buffer, 0, count);
        }
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Screen capture failed: " + await errors.ConfigureAwait(false));
        using var decoded = SKBitmap.Decode(png.ToArray()) ?? throw new InvalidDataException("Invalid screenshot image.");
        var size = IVisualElementCapture.LimitOutputSize(new PixelSize(decoded.Width, decoded.Height));
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(decoded, new SKRect(0, 0, size.Width, size.Height), new SKSamplingOptions(SKFilterMode.Linear), null);
        return new LinuxScreenCapture(bitmap, bounds);
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}

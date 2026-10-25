using System.Globalization;
using System.Text.Json.Serialization;
using Avalonia;

namespace Everywhere.Linux.Hyprland;

/// <summary>One output's physical mode and compositor logical placement.</summary>
public sealed record HyprlandMonitor
{
    [JsonPropertyName("id")]
    public int Id
    {
        get; init;
    }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("x")]
    public int X
    {
        get; init;
    }
    [JsonPropertyName("y")]
    public int Y
    {
        get; init;
    }
    [JsonPropertyName("width")]
    public int Width
    {
        get; init;
    }
    [JsonPropertyName("height")]
    public int Height
    {
        get; init;
    }
    [JsonPropertyName("scale")] public double Scale { get; init; } = 1;
    [JsonPropertyName("transform")]
    public int Transform
    {
        get; init;
    }
    [JsonPropertyName("focused")]
    public bool IsFocused
    {
        get; init;
    }
    [JsonPropertyName("disabled")]
    public bool IsDisabled
    {
        get; init;
    }
    [JsonPropertyName("mirrorOf")] public string MirrorOf { get; init; } = "none";
    [JsonPropertyName("activeWorkspace")] public HyprlandWorkspace ActiveWorkspace { get; init; } = new();
    [JsonPropertyName("specialWorkspace")] public HyprlandWorkspace SpecialWorkspace { get; init; } = new();

    /// <summary>Bounds in compositor logical desktop coordinates; rotated modes exchange axes.</summary>
    public PixelRect Bounds => new(X, Y,
        (int)Math.Round((Transform % 2 == 0 ? Width : Height) / Scale),
        (int)Math.Round((Transform % 2 == 0 ? Height : Width) / Scale));
}

/// <summary>A compositor workspace identity.</summary>
public sealed record HyprlandWorkspace
{
    [JsonPropertyName("id")]
    public int Id
    {
        get; init;
    }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>One compositor-managed application window, independent of its toolkit or display protocol.</summary>
public sealed record HyprlandWindow
{
    [JsonPropertyName("address")] public string Address { get; init; } = "";
    [JsonPropertyName("pid")]
    public int ProcessId
    {
        get; init;
    }
    [JsonPropertyName("class")] public string ApplicationId { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("initialTitle")] public string InitialTitle { get; init; } = "";
    [JsonPropertyName("at")] public int[] Position { get; init; } = [];
    [JsonPropertyName("size")] public int[] Size { get; init; } = [];
    [JsonPropertyName("mapped")]
    public bool IsMapped
    {
        get; init;
    }
    [JsonPropertyName("hidden")]
    public bool IsHidden
    {
        get; init;
    }
    [JsonPropertyName("monitor")]
    public int MonitorId
    {
        get; init;
    }
    [JsonPropertyName("floating")]
    public bool IsFloating
    {
        get; init;
    }
    [JsonPropertyName("fullscreen")]
    public int Fullscreen
    {
        get; init;
    }
    [JsonPropertyName("focusHistoryID")] public int FocusHistoryId { get; init; } = int.MaxValue;
    [JsonPropertyName("workspace")] public HyprlandWorkspace Workspace { get; init; } = new();

    public PixelRect Bounds => Position.Length == 2 && Size.Length == 2
        ? new PixelRect(Position[0], Position[1], Size[0], Size[1]) : default;

    /// <summary>The compositor address is opaque; it must never be dereferenced as a native pointer.</summary>
    public nint Handle => (nint)ulong.Parse(Address.AsSpan().TrimStart("0x"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>Maps one XWayland output's pixel coordinates to Hyprland's logical coordinate system.</summary>
public readonly record struct HyprlandCoordinateMap(PixelRect AvaloniaBounds, PixelRect LogicalBounds)
{
    public PixelPoint ToLogical(PixelPoint point)
    {
        return new(
        LogicalBounds.X + (int)Math.Round((point.X - AvaloniaBounds.X) * (double)LogicalBounds.Width / AvaloniaBounds.Width),
        LogicalBounds.Y + (int)Math.Round((point.Y - AvaloniaBounds.Y) * (double)LogicalBounds.Height / AvaloniaBounds.Height));
    }

    public PixelRect ToAvalonia(PixelRect bounds)
    {
        var left = AvaloniaBounds.X + (bounds.X - LogicalBounds.X) * (double)AvaloniaBounds.Width / LogicalBounds.Width;
        var top = AvaloniaBounds.Y + (bounds.Y - LogicalBounds.Y) * (double)AvaloniaBounds.Height / LogicalBounds.Height;
        var right = AvaloniaBounds.X + (bounds.Right - LogicalBounds.X) * (double)AvaloniaBounds.Width / LogicalBounds.Width;
        var bottom = AvaloniaBounds.Y + (bounds.Bottom - LogicalBounds.Y) * (double)AvaloniaBounds.Height / LogicalBounds.Height;
        return new PixelRect((int)Math.Floor(left), (int)Math.Floor(top),
            (int)Math.Ceiling(right) - (int)Math.Floor(left), (int)Math.Ceiling(bottom) - (int)Math.Floor(top));
    }
}

/// <summary>A finite desktop observation used by one acquisition or relation enumeration.</summary>
public sealed class HyprlandDesktop(HyprlandMonitor[] monitors, HyprlandWindow[] windows)
{
    public HyprlandMonitor[] Monitors { get; } = monitors;
    public HyprlandWindow[] Windows { get; } = windows;

    /// <summary>Returns independent desktop outputs; mirror sinks share their source's coordinate space.</summary>
    public static HyprlandMonitor[] VisibleOutputs(IEnumerable<HyprlandMonitor> outputs)
    {
        return outputs.Where(item => !item.IsDisabled && item.MirrorOf == "none").OrderBy(item => item.X).ThenBy(item => item.Y).ToArray();
    }

    /// <summary>Finds the visible application below a point, excluding every window owned by the picker process.</summary>
    public HyprlandWindow? WindowAt(PixelPoint point, int excludedProcessId = 0)
    {
        var monitor = Monitors.FirstOrDefault(item => item.Bounds.Contains(point));
        if (monitor is null)
            return null;
        return Windows.Where(window => window.IsMapped && !window.IsHidden && window.ProcessId != excludedProcessId &&
                window.MonitorId == monitor.Id && window.Bounds.Contains(point) &&
                (window.Workspace.Id == monitor.ActiveWorkspace.Id ||
                 monitor.SpecialWorkspace.Id != 0 && window.Workspace.Id == monitor.SpecialWorkspace.Id))
            .OrderByDescending(window => monitor.SpecialWorkspace.Id != 0 && window.Workspace.Id == monitor.SpecialWorkspace.Id)
            .ThenByDescending(window => window.IsFloating)
            .ThenBy(window => window.FocusHistoryId)
            .FirstOrDefault();
    }
}

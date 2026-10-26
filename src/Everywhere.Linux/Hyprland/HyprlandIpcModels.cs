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
    [JsonIgnore]
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

    [JsonIgnore]
    public PixelRect Bounds => Position.Length == 2 && Size.Length == 2
        ? new PixelRect(Position[0], Position[1], Size[0], Size[1]) : default;

    /// <summary>The compositor address is opaque; it must never be dereferenced as a native pointer.</summary>
    [JsonIgnore]
    public nint Handle => (nint)ulong.Parse(Address.AsSpan().TrimStart("0x"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
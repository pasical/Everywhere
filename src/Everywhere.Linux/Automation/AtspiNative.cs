using System.Runtime.InteropServices;
using Everywhere.Automation;
using Avalonia;

namespace Everywhere.Linux.Automation;

/// <summary>AT-SPI C ABI. Returned strings, rectangles and GErrors are explicitly freed.</summary>
public static partial class AtspiNative
{
    private const string Library = "libatspi.so.0";
    [LibraryImport(Library)]
    public static partial int atspi_init();
    [LibraryImport(Library)]
    public static partial int atspi_exit();
    [LibraryImport(Library)]
    public static partial void atspi_set_timeout(int milliseconds, int startupMilliseconds);
    [LibraryImport(Library)]
    public static partial nint atspi_get_desktop(int index);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_name(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_role_name(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_accessible_get_child_count(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_child_at_index(nint item, int index, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_parent(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_accessible_get_index_in_parent(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_accessible_get_process_id(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_state_set(nint item);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_attributes(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_state_set_contains(nint states, AtspiState state);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_component_iface(nint item);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_text_iface(nint item);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_editable_text_iface(nint item);
    [LibraryImport(Library)]
    public static partial nint atspi_accessible_get_action_iface(nint item);
    [LibraryImport(Library)]
    public static partial nint atspi_component_get_extents(nint item, AtspiCoordinates coordinates, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_component_get_accessible_at_point(nint item, int x, int y, AtspiCoordinates coordinates, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_component_grab_focus(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_text_get_character_count(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_text_get_text(nint item, int start, int end, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_text_get_n_selections(nint item, out nint error);
    [LibraryImport(Library)]
    public static partial nint atspi_text_get_selection(nint item, int index, out nint error);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int atspi_editable_text_set_text_contents(nint item, string text, out nint error);
    [LibraryImport(Library)]
    public static partial int atspi_action_do_action(nint item, int index, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void EventCallback(nint nativeEvent, nint userData);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint atspi_event_listener_new(EventCallback callback, nint userData, nint destroy);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int atspi_event_listener_register(nint listener, string eventType, out nint error);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int atspi_event_listener_deregister(nint listener, string eventType, out nint error);
    [LibraryImport("libgobject-2.0.so.0")]
    public static partial nint g_object_ref(nint item);
    [LibraryImport("libgobject-2.0.so.0")]
    public static partial void g_object_unref(nint item);
    [LibraryImport(Library)]
    public static partial nuint atspi_event_get_type();
    [LibraryImport("libgobject-2.0.so.0")]
    public static partial void g_boxed_free(nuint type, nint item);
    [LibraryImport("libglib-2.0.so.0")]
    public static partial void g_free(nint item);
    [LibraryImport("libglib-2.0.so.0")]
    public static partial void g_error_free(nint error);
    [LibraryImport("libglib-2.0.so.0")]
    public static partial void g_hash_table_unref(nint table);
    [LibraryImport("libglib-2.0.so.0")]
    public static partial int g_main_context_iteration(nint context, int shouldBlock);

    public static T Checked<T>(T result, nint error)
    {
        if (error == 0)
            return result;
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "Accessibility provider failure.";
        g_error_free(error);
        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            throw new TimeoutException(message);
        throw new VisualElementProviderException(VisualElementQueryFailureKind.ProviderFailure, message);
    }

    public static string String(nint pointer, nint error)
    {
        try
        {
            Checked(0, error);
            return Marshal.PtrToStringUTF8(pointer) ?? "";
        }
        finally { if (pointer != 0) g_free(pointer); }
    }

    public static int Count(nint item) => Checked(atspi_accessible_get_child_count(item, out var error), error);
    public static nint Child(nint item, int index) => Checked(atspi_accessible_get_child_at_index(item, index, out var error), error);
    public static string Name(nint item) => String(atspi_accessible_get_name(item, out var error), error);
    public static string Role(nint item) => String(atspi_accessible_get_role_name(item, out var error), error);
    public static int Pid(nint item) => Checked(atspi_accessible_get_process_id(item, out var error), error);

    public static void FreeEvent(nint nativeEvent) => g_boxed_free(atspi_event_get_type(), nativeEvent);

    /// <summary>
    /// An ATK plug can be exposed through a socket while its native parent index
    /// is detached. Avoid window-coordinate calls that reenter that bridge.
    /// </summary>
    public static bool IsDetachedEmbeddedRoot(nint item)
    {
        return Role(item) == "filler" && Checked(atspi_accessible_get_index_in_parent(item, out var error), error) < 0;
    }


    /// <summary>Reads a component rectangle and releases both temporary native objects.</summary>
    public static PixelRect? ReadExtents(nint accessible, AtspiCoordinates coordinates)
    {
        var component = atspi_accessible_get_component_iface(accessible);
        if (component == 0)
            return null;
        try
        {
            var pointer = Checked(atspi_component_get_extents(component, coordinates, out var error), error);
            if (pointer == 0)
                return null;
            try
            {
                var rectangle = Marshal.PtrToStructure<NativeRectangle>(pointer);
                if (rectangle.Width <= 0 || rectangle.Height <= 0 || rectangle.X == int.MinValue || rectangle.Y == int.MinValue)
                    return null;
                return new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            }
            finally { g_free(pointer); }
        }
        finally { g_object_unref(component); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }
}

/// <summary>AT-SPI coordinate space; window coordinates work on native Wayland.</summary>
public enum AtspiCoordinates
{
    Screen = 0,
    Window = 1,
    Parent = 2,
}

/// <summary>Native AT-SPI state indices, rather than the shared visual-state bitmask.</summary>
public enum AtspiState
{
    Defunct = 6,
    Editable = 7,
    Enabled = 8,
    Focused = 12,
    Selected = 23,
    Sensitive = 24,
    Showing = 25,
    Visible = 30,
}

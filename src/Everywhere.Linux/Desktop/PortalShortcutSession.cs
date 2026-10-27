using Avalonia.Input;
using Everywhere.Interop;
using Serilog;
using Tmds.DBus.Protocol;

namespace Everywhere.Linux.Desktop;

/// <summary>
/// Owns one portal session on a dedicated bus connection. Disconnecting releases all its grabs.
/// </summary>
internal sealed class PortalShortcutSession : IAsyncDisposable
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string Path = "/org/freedesktop/portal/desktop";
    private const string Interface = "org.freedesktop.portal.GlobalShortcuts";

    /// <summary>
    /// Gets the number of shortcuts actually granted by the desktop.
    /// </summary>
    public int GrantedShortcutCount { get; private set; }

    private readonly DBusConnection _connection;
    private IDisposable? _activationWatch;
    private string? _sessionPath;
    private bool _isDisposed;

    private PortalShortcutSession(string address)
    {
        _connection = new DBusConnection(address);
    }

    /// <summary>
    /// Requests the desktop's approval and listens for the granted shortcuts.
    /// </summary>
    public static async Task<PortalShortcutSession> CreateAsync(
        KeyboardShortcut[] shortcuts,
        Action<string> activated,
        CancellationToken cancellationToken)
    {
        var session = new PortalShortcutSession(DBusAddress.Session
            ?? throw new NotSupportedException("Global shortcuts require a desktop session bus."));
        try
        {
            await session.InitializeAsync(shortcuts, activated, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _isDisposed, true);
        _activationWatch?.Dispose();
        try
        {
            if (_sessionPath is not null)
            {
                await _connection.CallMethodAsync(CreateCloseRequest(_sessionPath))
                    .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // A lost portal or bus cannot acknowledge Close. Disconnect still releases ownership.
            Log.Debug(exception, "The desktop portal session could not acknowledge closure.");
        }
        finally
        {
            _connection.Dispose();
        }
    }

    /// <summary>
    /// Uses a stable identifier so the desktop can remember approval across process launches.
    /// </summary>
    public static string ShortcutId(KeyboardShortcut shortcut) => $"keyboard_{(int)shortcut.Modifiers}_{(int)shortcut.Key}";

    /// <summary>
    /// Formats the base-layer XKB chord required by the XDG shortcuts specification.
    /// </summary>
    public static string FormatTrigger(KeyboardShortcut shortcut)
    {
        var parts = new List<string>();
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("CTRL");
        }
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("ALT");
        }
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("SHIFT");
        }
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("LOGO");
        }
        var symbol = LinuxKeySymbol.FromKey(shortcut.Key);
        parts.Add(shortcut.Key is >= Key.A and <= Key.Z ? symbol.ToLowerInvariant() : symbol);
        return string.Join('+', parts);
    }

    private async Task InitializeAsync(KeyboardShortcut[] shortcuts, Action<string> activated, CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _connection.CallMethodAsync(CreateRegistryRequest()).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException exception) when (exception.ErrorName == "org.freedesktop.DBus.Error.UnknownMethod")
        {
            // Older portals infer the identity from the process instead of offering Registry.
        }

        var created = await RequestAsync("CreateSession", null, [], cancellationToken).ConfigureAwait(false);
        _sessionPath = created["session_handle"].GetString();
        _activationWatch = await _connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = Service,
                Path = Path,
                Interface = Interface,
                Member = "Activated",
            },
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                return (Session: reader.ReadObjectPath().ToString(), Id: reader.ReadString());
            },
            notification =>
            {
                if (notification.HasValue && notification.Value.Session == _sessionPath)
                {
                    // Application callbacks may marshal to the UI thread. Never block the bus reader.
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (!Volatile.Read(ref _isDisposed))
                        {
                            activated(notification.Value.Id);
                        }
                    });
                }
            },
            emitOnCapturedContext: false,
            flags: ObserverFlags.None,
            state: null).ConfigureAwait(false);
        var bound = await RequestAsync("BindShortcuts", _sessionPath, shortcuts, cancellationToken).ConfigureAwait(false);
        if (!bound.TryGetValue("shortcuts", out var granted) || granted.Count == 0)
        {
            throw new InvalidOperationException("The desktop did not grant any global shortcuts.");
        }
        GrantedShortcutCount = granted.Count;
    }

    private async Task<Dictionary<string, VariantValue>> RequestAsync(
        string method,
        string? sessionPath,
        KeyboardShortcut[] shortcuts,
        CancellationToken cancellationToken)
    {
        var token = "everywhere_" + Guid.NewGuid().ToString("N");
        var uniqueName = _connection.UniqueName ?? throw new InvalidOperationException("The session bus has no connection identity.");
        var requestPath = Path + "/request/" + uniqueName.TrimStart(':').Replace('.', '_') + "/" + token;
        var completion = new TaskCompletionSource<Dictionary<string, VariantValue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = await _connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = Service,
                Path = requestPath,
                Interface = "org.freedesktop.portal.Request",
                Member = "Response",
            },
            static (message, _) =>
            {
                var reader = message.GetBodyReader();
                return (Code: reader.ReadUInt32(), Results: reader.ReadDictionaryOfStringToVariantValue());
            },
            notification =>
            {
                if (notification.HasValue)
                {
                    if (notification.Value.Code == 0)
                    {
                        completion.TrySetResult(notification.Value.Results);
                    }
                    else
                    {
                        completion.TrySetException(new InvalidOperationException($"The desktop declined {method} ({notification.Value.Code})."));
                    }
                }
                else if (notification.IsCompletion)
                {
                    completion.TrySetException(notification.Exception);
                }
            },
            emitOnCapturedContext: false,
            flags: ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnReaderFailed,
            state: null).ConfigureAwait(false);
        await _connection.CallMethodAsync(CreateShortcutRequest(method, token, sessionPath, shortcuts))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private MessageBuffer CreateCloseRequest(string sessionPath)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: sessionPath,
            @interface: "org.freedesktop.portal.Session", member: "Close");
        return writer.CreateMessage();
    }

    private MessageBuffer CreateRegistryRequest()
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: Path,
            @interface: "org.freedesktop.host.portal.Registry", member: "Register", signature: "sa{sv}");
        writer.WriteString("com.Sylinko.Everywhere");
        writer.WriteDictionary(new Dictionary<string, VariantValue>());
        return writer.CreateMessage();
    }

    private MessageBuffer CreateShortcutRequest(string method, string token, string? sessionPath, KeyboardShortcut[] shortcuts)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: Path, @interface: Interface,
            member: method, signature: sessionPath is null ? "a{sv}" : "oa(sa{sv})sa{sv}");
        if (sessionPath is not null)
        {
            writer.WriteObjectPath(sessionPath);
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (var shortcut in shortcuts)
            {
                writer.WriteStructureStart();
                writer.WriteString(ShortcutId(shortcut));
                writer.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["description"] = shortcut.ToString(),
                    ["preferred_trigger"] = FormatTrigger(shortcut),
                });
            }
            writer.WriteArrayEnd(array);
            writer.WriteString("");
        }
        var options = new Dictionary<string, VariantValue> { ["handle_token"] = token };
        if (sessionPath is null)
        {
            options["session_handle_token"] = token;
        }
        writer.WriteDictionary(options);
        return writer.CreateMessage();
    }
}
using System.Threading.Channels;
using Avalonia.Input;
using Everywhere.Interop;
using Microsoft.Extensions.Logging;

namespace Everywhere.Linux.Desktop;

/// <summary>
/// Registers ordinary desktop shortcuts through the user-approved GlobalShortcuts portal.
/// Focused recording remains owned by <see cref="LinuxShortcutListener"/>.
/// </summary>
public sealed class PortalShortcutListener : IShortcutListener, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<KeyboardShortcut, List<Action>> _handlers = [];
    private readonly Channel<byte> _changes = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger<PortalShortcutListener> _logger;
    private readonly Task _runTask;
    private SuspensionScope? _suspension;
    private bool _isDisposed;

    /// <summary>
    /// Starts one serialized owner for portal sessions and registration changes.
    /// </summary>
    public PortalShortcutListener(ILogger<PortalShortcutListener> logger)
    {
        _logger = logger;
        _runTask = RunAsync();
    }

    /// <inheritdoc />
    public IDisposable Register(KeyboardShortcut shortcut, Action handler)
    {
        if (!shortcut.IsValid)
        {
            throw new ArgumentException("Invalid keyboard shortcut.", nameof(shortcut));
        }

        // Validate before returning a registration, rather than failing later in the worker.
        PortalShortcutSession.FormatTrigger(shortcut);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_handlers.TryGetValue(shortcut, out var handlers))
            {
                handlers = [];
                _handlers.Add(shortcut, handlers);
            }
            handlers.Add(handler);
        }
        SignalChanged();
        return new Registration(this, shortcut, handler);
    }

    /// <inheritdoc />
    public IDisposable Register(MouseShortcut shortcut, Action handler)
    {
        throw new NotSupportedException("The GlobalShortcuts portal does not register mouse shortcuts.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Closing the active portal session releases its grabs while the focused editor records.
    /// This lease supplies no key events; disposal restores the desired global registrations.
    /// </remarks>
    public IKeyboardShortcutScope StartCaptureKeyboardShortcut()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _suspension ??= new SuspensionScope(this);
            SignalChanged();
            return _suspension;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _handlers.Clear();
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _changes.Writer.TryComplete();
        await _runTask.ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        PortalShortcutSession? session = null;
        KeyboardShortcut[] previous = [];
        try
        {
            await foreach (var change in _changes.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                while (_changes.Reader.TryRead(out _))
                {
                }
                KeyboardShortcut[] desired;
                lock (_gate)
                {
                    desired = _suspension is null
                        ? _handlers.Keys.OrderBy(key => (int)key.Modifiers).ThenBy(key => (int)key.Key).ToArray()
                        : [];
                }
                if (desired.SequenceEqual(previous))
                {
                    continue;
                }

                // BindShortcuts accepts one complete set per session. A dedicated bus connection
                // makes disposal release both the session and any unfinished permission request.
                if (session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    session = null;
                }
                previous = desired;
                if (desired.Length == 0)
                {
                    continue;
                }
                try
                {
                    session = await PortalShortcutSession.CreateAsync(desired, HandleActivated, _lifetime.Token).ConfigureAwait(false);
                    _logger.LogInformation("Desktop portal bound {ShortcutCount} keyboard shortcuts.", session.GrantedShortcutCount);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Denial is a user decision, not an Input Host crash. Do not restart the Host
                    // and repeatedly reopen the permission dialog. A setting change can retry.
                    _logger.LogWarning(exception, "Desktop global shortcuts were not granted. Check the desktop's shortcut permissions.");
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void HandleActivated(string id)
    {
        Action[] handlers;
        lock (_gate)
        {
            if (_isDisposed || _suspension is not null)
            {
                return;
            }
            handlers = _handlers.Where(pair => PortalShortcutSession.ShortcutId(pair.Key) == id)
                .SelectMany(pair => pair.Value).ToArray();
        }
        foreach (var handler in handlers)
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A desktop shortcut callback failed.");
            }
        }
    }

    private void SignalChanged() => _changes.Writer.TryWrite(0);

    private sealed class Registration(PortalShortcutListener owner, KeyboardShortcut shortcut, Action handler) : IDisposable
    {
        private bool _isDisposed;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                if (owner._handlers.TryGetValue(shortcut, out var handlers))
                {
                    handlers.Remove(handler);
                    if (handlers.Count == 0)
                    {
                        owner._handlers.Remove(shortcut);
                    }
                }
            }
            owner.SignalChanged();
        }
    }

    private sealed class SuspensionScope(PortalShortcutListener owner) : IKeyboardShortcutScope
    {
        public KeyboardShortcut PressingShortcut => default;
        public bool IsDisposed { get; private set; }

        public event IKeyboardShortcutScope.PressingShortcutChangedHandler? PressingShortcutChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public event IKeyboardShortcutScope.ShortcutFinishedHandler? ShortcutFinished
        {
            add
            {
            }
            remove
            {
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (IsDisposed)
                {
                    return;
                }
                IsDisposed = true;
                if (ReferenceEquals(owner._suspension, this))
                {
                    owner._suspension = null;
                }
            }
            owner.SignalChanged();
        }
    }
}
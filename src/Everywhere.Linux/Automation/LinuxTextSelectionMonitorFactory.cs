using System.Runtime.InteropServices;
using System.Threading.Channels;
using Avalonia;
using Everywhere.Automation;
using Everywhere.ProcessIsolation.Automation;

namespace Everywhere.Linux.Automation;

/// <summary>Observes toolkit selection events without global input grabs or synthetic clipboard operations.</summary>
public sealed class LinuxTextSelectionMonitorFactory(HyprlandVisualElementBackend backend) : ITextSelectionMonitorFactory
{
    public ITextSelectionMonitor Create(ITextSelectionMonitorContext context, int mainProcessId,
        TextSelectionMonitoringConfiguration configuration, TextSelectionDiagnostics diagnostics,
        Func<TextSelectionObservation, CancellationToken, ValueTask> publish)
    {
        return new Monitor(backend, context, mainProcessId, configuration, publish);
    }

    private sealed class Monitor : ITextSelectionMonitor
    {
        private const string EventType = "object:text-selection-changed";
        private readonly HyprlandVisualElementBackend _backend;
        private readonly ITextSelectionMonitorContext _context;
        private readonly int _mainProcessId;
        private readonly Func<TextSelectionObservation, CancellationToken, ValueTask> _publish;
        private readonly Channel<nint> _events = Channel.CreateBounded<nint>(8);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly AtspiNative.EventCallback _callback;
        private readonly nint _listener;
        private readonly Task _pump;
        private TextSelectionMonitoringConfiguration _configuration;
        private int _isDisposed;

        public Monitor(HyprlandVisualElementBackend backend, ITextSelectionMonitorContext context, int mainProcessId,
            TextSelectionMonitoringConfiguration configuration, Func<TextSelectionObservation, CancellationToken, ValueTask> publish)
        {
            _backend = backend;
            _context = context;
            _mainProcessId = mainProcessId;
            _configuration = configuration;
            _publish = publish;
            _callback = OnEvent;
            _listener = backend.Accessibility.Call(() =>
            {
                var listener = AtspiNative.atspi_event_listener_new(_callback, 0, 0);
                if (listener == 0)
                    throw new InvalidOperationException("Could not create the accessibility selection listener.");
                try
                {
                    AtspiNative.Checked(AtspiNative.atspi_event_listener_register(listener, EventType, out var error), error);
                    return listener;
                }
                catch
                {
                    AtspiNative.g_object_unref(listener);
                    throw;
                }
            });
            _pump = RunAsync();
        }

        public ValueTask UpdateConfigurationAsync(TextSelectionMonitoringConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _configuration, configuration);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return;
            _backend.Accessibility.Call(() =>
            {
                try
                {
                    AtspiNative.Checked(AtspiNative.atspi_event_listener_deregister(_listener, EventType, out var error), error);
                }
                finally { AtspiNative.g_object_unref(_listener); }
                return true;
            });
            _lifetime.Cancel();
            _events.Writer.TryComplete();
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            finally
            {
                while (_events.Reader.TryRead(out var source))
                    _backend.Accessibility.Release(source);
                _lifetime.Dispose();
            }
        }

        private void OnEvent(nint nativeEvent, nint userData)
        {
            if (nativeEvent == 0)
                return;
            try
            {
                if (Volatile.Read(ref _isDisposed) != 0)
                    return;
                var source = Marshal.ReadIntPtr(nativeEvent, IntPtr.Size);
                if (source == 0)
                    return;
                AtspiNative.g_object_ref(source);
                if (!_events.Writer.TryWrite(source))
                    AtspiNative.g_object_unref(source);
            }
            finally { AtspiNative.FreeEvent(nativeEvent); }
        }

        private async Task RunAsync()
        {
            await foreach (var initial in _events.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                var source = initial;
                try
                {
                    await Task.Delay(80, _lifetime.Token).ConfigureAwait(false);
                    while (_events.Reader.TryRead(out var next))
                    {
                        _backend.Accessibility.Release(source);
                        source = next;
                    }
                    var observation = await _context.ExecuteAsync((context, backend, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        var window = _backend.Connection.Read<Hyprland.HyprlandWindow>("activewindow");
                        if (window.ProcessId == _mainProcessId || window.ProcessId <= 0)
                            return null;
                        var configuration = Volatile.Read(ref _configuration);
                        if (configuration.IsFullscreenApplicationExcluded && window.Fullscreen != 0)
                            return null;
                        var path = File.ResolveLinkTarget($"/proc/{window.ProcessId}/exe", true)?.FullName;
                        if (path is null || configuration.ExcludedApplications.Contains(path, StringComparer.Ordinal))
                            return null;
                        if (_backend.Accessibility.Call(() => AtspiNative.Pid(source)) != window.ProcessId)
                            return null;
                        var retention = context.CreateRetention();
                        try
                        {
                            var element = AtspiVisualElement.GetOrCreate(retention, _backend, window, source);
                            var text = element.GetSelectedText(65536);
                            if (string.IsNullOrWhiteSpace(text))
                                return null;
                            var result = element.Query(new VisualElementQueryRequest(VisualElementFields.Id | VisualElementFields.Bounds | VisualElementFields.ProcessId, 0));
                            var pointer = _backend.Connection.Read<Dictionary<string, int>>("cursorpos");
                            var observation = new TextSelectionObservation(_context, text, new TextSelectionSource(retention, result), text.Length >= 32768)
                            {
                                ApplicationIdentity = path,
                                PointerPosition = new PixelPoint(pointer["x"], pointer["y"])
                            };
                            retention = null;
                            return observation;
                        }
                        finally { retention?.Dispose(); }
                    }, _lifetime.Token).ConfigureAwait(false);
                    if (observation is not null)
                    {
                        await using (observation.ConfigureAwait(false))
                            await _publish(observation, _lifetime.Token).ConfigureAwait(false);
                    }
                }
                catch (VisualElementProviderException) { }
                catch (TimeoutException) { }
                finally { _backend.Accessibility.Release(source); }
            }
        }
    }
}

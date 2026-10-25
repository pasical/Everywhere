using System.Collections.Concurrent;
using Everywhere.Automation;

namespace Everywhere.Linux.Automation;

/// <summary>
/// Owns libatspi and its GLib context on one thread. Native proxies and their releases never
/// cross this dispatcher. Individual D-Bus calls have a finite provider timeout.
/// </summary>
public sealed class AtspiClient : IDisposable
{
    private const string ActivityEvent = "object:state-changed:focused";
    private readonly BlockingCollection<Action> _work = new(64);
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isInitialized;
    private readonly AtspiNative.EventCallback _activityCallback = OnActivity;
    private nint _activityListener;

    public AtspiClient()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Everywhere AT-SPI" };
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    public T Call<T>(Func<T> operation)
    {
        if (Thread.CurrentThread == _thread)
        {
            EnsureInitialized();
            return operation();
        }
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.TryAdd(() =>
            {
                try
                {
                    EnsureInitialized();
                    result.SetResult(operation());
                }
                catch (Exception exception) { result.SetException(exception); }
            }))
            throw new TimeoutException("The accessibility dispatcher is busy.");
        return result.Task.GetAwaiter().GetResult();
    }

    public void Release(nint pointer)
    {
        if (pointer != 0)
            Call(() =>
            {
                AtspiNative.g_object_unref(pointer);
                return true;
            });
    }

    public void Dispose()
    {
        if (_work.IsAddingCompleted)
            return;
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        try
        {
            _ready.SetResult();
            // Announce the assistive client when the Host starts, before newly
            // launched applications decide whether to expose their bridges.
            // An unavailable bus still permits compositor-only window access.
            try { EnsureInitialized(); }
            catch (Exception exception)
            {
                Serilog.Log.Debug(exception, "Accessibility initialization failed; compositor access remains usable and semantic requests may retry.");
            }
            while (!_work.IsCompleted)
            {
                if (_work.TryTake(out var operation, 10))
                    operation();
                if (_isInitialized)
                    for (var index = 0; index < 16 && AtspiNative.g_main_context_iteration(0, 0) != 0; index++)
                    {
                    }
            }
        }
        catch (Exception exception) { _ready.TrySetException(exception); }
        finally
        {
            if (_activityListener != 0)
            {
                try
                {
                    AtspiNative.Checked(AtspiNative.atspi_event_listener_deregister(_activityListener, ActivityEvent, out var error), error);
                }
                catch (Exception exception)
                {
                    Serilog.Log.Debug(exception, "The accessibility registry disconnected before listener cleanup.");
                }
                finally { AtspiNative.g_object_unref(_activityListener); }
            }
            if (_isInitialized)
                AtspiNative.atspi_exit();
        }
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
            return;
        // libatspi returns 2 when its bus cannot be opened. Calling its other APIs
        // in that state invokes g_error() and aborts the entire Automation Host.
        var status = AtspiNative.atspi_init();
        if (status is not (0 or 1))
        {
            AtspiNative.atspi_exit();
            throw new NotSupportedException("The AT-SPI accessibility bus is unavailable.");
        }
        AtspiNative.atspi_set_timeout(800, 1500);
        // Register as an assistive client, rather than merely opening the bus.
        // Toolkits use the registry's active listeners to enable their bridges.
        var listener = AtspiNative.atspi_event_listener_new(_activityCallback, 0, 0);
        if (listener == 0)
        {
            AtspiNative.atspi_exit();
            throw new NotSupportedException("The accessibility registry could not create a listener.");
        }
        try
        {
            if (AtspiNative.Checked(AtspiNative.atspi_event_listener_register(listener, ActivityEvent, out var error), error) == 0)
                throw new NotSupportedException("The accessibility registry rejected the client registration.");
        }
        catch
        {
            AtspiNative.g_object_unref(listener);
            AtspiNative.atspi_exit();
            throw;
        }
        _activityListener = listener;
        _isInitialized = true;
    }

    private static void OnActivity(nint nativeEvent, nint userData)
    {
        if (nativeEvent != 0)
            AtspiNative.FreeEvent(nativeEvent);
    }
}

using System;
using System.Threading;

namespace BluetoothManagerPro.Infrastructure;

/// <summary>
/// Keeps one instance per user session. A second launch signals the first one — which is
/// what you want for a tray app: clicking the shortcut again should surface the window,
/// not start a second tray icon.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\BluetoothManagerPro.Instance";
    private const string SignalName = @"Local\BluetoothManagerPro.Activate";

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _signal;
    private readonly CancellationTokenSource? _listener;

    private SingleInstance(bool isFirst, Mutex? mutex, EventWaitHandle? signal)
    {
        IsFirstInstance = isFirst;
        _mutex = mutex;
        _signal = signal;
        _listener = isFirst ? new CancellationTokenSource() : null;
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread when another instance asks us to show ourselves.</summary>
    public event Action? ActivationRequested;

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);

        if (createdNew)
        {
            return new SingleInstance(true, mutex, signal);
        }

        // Someone is already running: poke them and let the caller exit.
        signal.Set();
        mutex.Dispose();
        signal.Dispose();
        return new SingleInstance(false, null, null);
    }

    /// <summary>Starts listening for activation pokes. Only meaningful on the first instance.</summary>
    public void BeginListening()
    {
        if (!IsFirstInstance || _signal is null || _listener is null)
        {
            return;
        }

        CancellationToken token = _listener.Token;
        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (_signal.WaitOne(TimeSpan.FromSeconds(1)) && !token.IsCancellationRequested)
                {
                    ActivationRequested?.Invoke();
                }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceListener",
        };

        thread.Start();
    }

    public void Dispose()
    {
        _listener?.Cancel();
        _listener?.Dispose();

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned — nothing to release.
            }

            _mutex.Dispose();
        }

        _signal?.Dispose();
    }
}

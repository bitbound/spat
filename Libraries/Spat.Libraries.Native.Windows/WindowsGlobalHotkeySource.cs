using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Registers the dictation hotkey with a global low-level keyboard hook, which sees key presses while
/// any other application has focus and reports both press and release, unlike RegisterHotKey.
/// </summary>
public sealed class WindowsGlobalHotkeySource : IGlobalHotkeySource
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;

    // HC_ACTION, the only hook code a low-level keyboard hook receives.
    private const int HcAction = 0;

    private readonly ILogger<WindowsGlobalHotkeySource> _logger;
    private readonly KeyTransition _keyTransition = new();

    private HookWorker? _worker;
    private (HotkeyModifiers Modifiers, uint KeyCode)? _spec;
    private bool _disposed;

    public WindowsGlobalHotkeySource(ILogger<WindowsGlobalHotkeySource> logger)
    {
        _logger = logger;
    }

    public bool IsSupported => true;

    public event EventHandler? Activated;

    public event EventHandler? Deactivated;

    /// <summary>
    /// Never raised on Windows: the hook lives inside this process, so there is no session connection
    /// that can drop and silently take the binding with it.
    /// </summary>
#pragma warning disable CS0067 // Interface member kept for parity with backends that can lose bindings.
    public event EventHandler? BindingLost;
#pragma warning restore CS0067

    public Task<bool> BindAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        (HotkeyModifiers Modifiers, uint KeyCode) spec;

        try
        {
            spec = WindowsShortcutParser.Parse(shortcut);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(exception, "The configured hotkey '{Shortcut}' could not be parsed, so it was not bound.", shortcut);
            return Task.FromResult(false);
        }

        _worker ??= HookWorker.Start(_logger);
        _worker.Source = this;
        _spec = spec;

        _logger.LogInformation("Bound the global hotkey {Shortcut}.", shortcut);

        return Task.FromResult(true);
    }

    public Task UnbindAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_worker?.Source == this)
        {
            _worker.Source = null;
        }

        _spec = null;

        _logger.LogInformation("Unbound the global hotkey.");

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var worker = _worker;
        _worker = null;

        if (worker is not null)
        {
            await worker.StopAsync();
        }
    }

    internal void RaiseActivated() => Activated?.Invoke(this, EventArgs.Empty);

    internal void RaiseDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

    internal KeyTransition Transitions => _keyTransition;

    internal bool Matches(uint keyCode)
    {
        var spec = _spec;

        return spec is not null
            && keyCode == spec.Value.KeyCode
            && ReadPressedModifiers() == spec.Value.Modifiers;
    }

    private static HotkeyModifiers ReadPressedModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (IsDown(VirtualKeys.LControl) || IsDown(VirtualKeys.RControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(VirtualKeys.LMenu) || IsDown(VirtualKeys.RMenu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKeys.LShift) || IsDown(VirtualKeys.RShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsDown(uint vk)
    {
        // The high-order bit of the SHORT result reports whether the key is currently down.
        return (((ushort)(short)SpatWin32.GetAsyncKeyState((int)vk)) & 0x8000) != 0;
    }

    /// <summary>
    /// Tracks whether the hotkey key is physically down so auto-repeat does not re-fire Activated and a
    /// stray key-up without a preceding key-down does not fire Deactivated.
    /// </summary>
    internal sealed class KeyTransition
    {
        private int _down;

        public bool TryBeginPress() => Interlocked.CompareExchange(ref _down, 1, 0) == 0;

        public bool TryEndPress() => Interlocked.CompareExchange(ref _down, 0, 1) == 1;
    }

    /// <summary>
    /// A long-running thread that owns the low-level hook. The hook callback is delivered to the thread
    /// that installed it while that thread pumps messages, so the loop below is load-bearing. The
    /// callback never blocks; consumers move real work to their own dispatchers.
    /// </summary>
    private sealed class HookWorker
    {
        private readonly ILogger _logger;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _installed = new(false);
        private readonly HOOKPROC _procedure;

        private volatile WindowsGlobalHotkeySource? _source;
        private volatile bool _stopRequested;
        private int _nativeThreadId;
        private UnhookWindowsHookExSafeHandle? _hook;

        private HookWorker(ILogger logger)
        {
            _logger = logger;
            _procedure = Callback;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Spat-HotkeyHook",
            };
        }

        public WindowsGlobalHotkeySource? Source
        {
            get => _source;
            set => _source = value;
        }

        public static HookWorker Start(ILogger logger)
        {
            var worker = new HookWorker(logger);

            worker._thread.Start();
            worker._installed.Wait(TimeSpan.FromSeconds(5));

            return worker;
        }

        public Task StopAsync()
        {
            _stopRequested = true;

            // The worker thread is pumping messages, so a thread-posted quit (WM_QUIT) ends the loop cleanly.
            SpatWin32.PostThreadMessage((uint)Volatile.Read(ref _nativeThreadId), WmQuit, 0, 0);

            return Task.Run(_thread.Join);
        }

        private void Run()
        {
            Volatile.Write(ref _nativeThreadId, (int)SpatWin32.GetCurrentThreadId());

            _hook = SpatWin32.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _procedure, default, 0);

            if (_hook is null || _hook.IsInvalid)
            {
                _logger.LogWarning("SetWindowsHookEx failed, so the global hotkey is unavailable.");
                _installed.Set();
                return;
            }

            try
            {
                _installed.Set();

                while (!_stopRequested && SpatWin32.GetMessage(out var message, default, 0, 0))
                {
                    SpatWin32.TranslateMessage(message);
                    SpatWin32.DispatchMessage(message);
                }
            }
            finally
            {
                // The safe handle releases the hook through UnhookWindowsHookEx.
                _hook.Dispose();
                _hook = null;
            }
        }

        // The callback runs in this process on the hook thread. Swallowing the matched key keeps it from
        // reaching the focused application, mirroring how a global shortcut grab consumes the key.
        private LRESULT Callback(int nCode, WPARAM wParam, LPARAM lParam)
        {
            var source = _source;

            if (nCode == HcAction && source is not null)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var message = (uint)wParam.Value;

                var isDown = message == WmKeyDown || message == WmSysKeyDown;
                var isUp = message == WmKeyUp || message == WmSysKeyUp;

                if ((isDown || isUp) && source.Matches(info.vkCode))
                {
                    var owned = isDown ? source.Transitions.TryBeginPress() : source.Transitions.TryEndPress();

                    if (owned)
                    {
                        if (isDown)
                        {
                            source.RaiseActivated();
                        }
                        else
                        {
                            source.RaiseDeactivated();
                        }
                    }

                    // Swallow repeats and releases of our own key even when the transition is not owned, so
                    // the focused application never sees half of the shortcut.
                    return (LRESULT)1;
                }
            }

            return SpatWin32.CallNextHookEx(default, nCode, wParam, lParam);
        }
    }
}

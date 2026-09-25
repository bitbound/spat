using System.Text;
using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Input;
using Spat.Libraries.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using System.Runtime.InteropServices;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Injects text with SendInput in Unicode mode, which delivers characters to the focused window without
/// depending on the active keyboard layout.
/// </summary>
public sealed class WindowsTextInputInjector(
    ISettingsService settings,
    ILogger<WindowsTextInputInjector> logger) : ITextInputInjector
{
    /// <summary>
    /// Bounds on the configured pause. Zero would put the press and release in the same input frame,
    /// which types nothing at all, so the floor is one millisecond.
    /// </summary>
    private const int MinKeyDelayMs = 1;

    private const int MaxKeyDelayMs = 100;

    private static readonly SemaphoreSlim _gate = new(1, 1);

    private readonly ISettingsService _settings = settings;
    private readonly ILogger<WindowsTextInputInjector> _logger = logger;

    public async Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var delay = ResolveDelay(_settings.Current.TypingDelayMs);
            var injected = 0;

            foreach (var character in text)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TypeUnit(character, keyUp: false);
                await PauseAsync(delay, cancellationToken);
                TypeUnit(character, keyUp: true);
                await PauseAsync(delay, cancellationToken);

                injected++;
            }

            // One line per dictation. It records the delay actually in effect, which separates
            // "nothing typed" from "typed too fast for the target application".
            _logger.LogInformation("Injected {Injected} characters with a {Delay} ms key delay.", injected, delay);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static int ResolveDelay(int configuredDelayMs)
    {
        return Math.Clamp(configuredDelayMs, MinKeyDelayMs, MaxKeyDelayMs);
    }

    private static void TypeUnit(char character, bool keyUp)
    {
        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE;

        if (keyUp)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }

        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = flags,
            },
        };

        var sent = SpatWin32.SendInput([input], Marshal.SizeOf<INPUT>());

        if (sent != 1)
        {
            throw new InvalidOperationException("SendInput rejected the keyboard event, so typing stopped.");
        }
    }

    private static Task PauseAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, cancellationToken);
    }
}

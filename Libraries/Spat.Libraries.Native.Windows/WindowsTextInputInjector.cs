using System.Diagnostics;
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
    /// Upper bound on the configured pause. The floor is zero, which selects the single-burst path in
    /// <see cref="TypeAsync"/>: every event goes into one SendInput call and the target still receives
    /// them as ordered key-down/key-up messages. A positive delay exists only for targets that poll key
    /// state instead of processing messages; for them the pause is what makes a press observable.
    /// </summary>
    private const int MinKeyDelayMs = 0;

    private const int MaxKeyDelayMs = 100;

    /// <summary>
    /// The longest pause worth waiting for precisely. Above this the coarse OS timer is accurate enough
    /// and spinning would just burn CPU.
    /// </summary>
    private const int MaxSpunPauseMs = 10;

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

            if (delay == 0)
            {
                // Fast path: one SendInput call carrying every press and release. Message-driven
                // applications - which is every ordinary Windows program - dequeue them as ordered
                // key-down/key-up pairs, so the whole transcript lands at once with nothing to pace.
                TypeBurst(text);
            }
            else
            {
                foreach (var character in text)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    TypeUnit(character, keyUp: false);

                    // The only pause that matters: the gap between a press and its release, so a
                    // target that polls key state can observe both. Nothing needs to settle after
                    // a character is finished, so there is no second pause.
                    await PauseAsync(delay, cancellationToken);

                    TypeUnit(character, keyUp: true);
                }
            }

            // One line per dictation. It records the delay actually in effect, which separates
            // "nothing typed" from "typed too fast for the target application".
            _logger.LogInformation("Injected {Injected} characters with a {Delay} ms key delay.", text.Length, delay);
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
        var sent = SpatWin32.SendInput([BuildInput(character, keyUp)], Marshal.SizeOf<INPUT>());

        if (sent != 1)
        {
            throw new InvalidOperationException("SendInput rejected the keyboard event, so typing stopped.");
        }
    }

    // Every press and release in one SendInput call. Windows inserts them into the target's input
    // stream as ordered events, so the transcript is injected as fast as the queue allows.
    private static void TypeBurst(string text)
    {
        var inputs = new INPUT[text.Length * 2];

        for (var index = 0; index < text.Length; index++)
        {
            inputs[(index * 2)] = BuildInput(text[index], keyUp: false);
            inputs[(index * 2) + 1] = BuildInput(text[index], keyUp: true);
        }

        var sent = SpatWin32.SendInput(inputs, Marshal.SizeOf<INPUT>());

        if (sent != (uint)inputs.Length)
        {
            throw new InvalidOperationException("SendInput rejected the keyboard events, so typing stopped.");
        }
    }

    private static INPUT BuildInput(char character, bool keyUp)
    {
        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE;

        if (keyUp)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = flags,
            },
        };
    }

    internal static async Task PauseAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        // Task.Delay cannot sleep for less than one system timer tick (~15 ms), so routing the short
        // configured pauses through it would turn a 1 ms setting into a 30 ms-per-character typewriter.
        // Short waits are spun against the high-resolution clock instead; the thread is a dedicated
        // typing worker, and a few milliseconds of spinning per character keeps it responsive to
        // cancellation.
        if (milliseconds > MaxSpunPauseMs)
        {
            await Task.Delay(milliseconds, cancellationToken);

            return;
        }

        var until = Stopwatch.GetTimestamp() + (milliseconds * Stopwatch.Frequency / 1000);

        while (Stopwatch.GetTimestamp() < until)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Yield();
        }
    }
}

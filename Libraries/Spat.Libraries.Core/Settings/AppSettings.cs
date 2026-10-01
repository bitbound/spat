namespace Spat.Libraries.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// How the configured shortcut starts a dictation.
/// </summary>
public enum ShortcutTriggerMode
{
    /// <summary>
    /// One activation starts, the next activation stops.
    /// </summary>
    Tap,

    /// <summary>
    /// Recording follows the physical key: it starts on press and stops on release.
    /// </summary>
    Press,
}

public sealed class AppSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+Space";

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public ShortcutTriggerMode TriggerMode { get; set; } = ShortcutTriggerMode.Tap;

    public string Hotkey { get; set; } = DefaultHotkey;

    public int HistoryItemsToKeep { get; set; } = 50;

    /// <summary>
    /// Keeps the captured WAV next to each history entry so it can be played back.
    /// </summary>
    public bool KeepRecordings { get; set; } = true;

    public int MaximumRecordingSeconds { get; set; } = 120;

    public float SilenceRmsThreshold { get; set; } = DefaultSilenceRmsThreshold;

    public const float DefaultSilenceRmsThreshold = 0.003f;

    /// <summary>
    /// Pause between injected key events. Higher values type slower but give the target application
    /// more time to keep up.
    /// </summary>
    /// <remarks>
    /// 2 ms is verified working on common Windows applications. Zero types the whole transcript in one
    /// SendInput burst, which ordinary applications handle fine; raise it from Settings, Dictation,
    /// Typing delay only if a target that polls the keyboard drops characters.
    /// </remarks>
    public int TypingDelayMs { get; set; } = DefaultTypingDelayMs;

    public const int DefaultTypingDelayMs = 2;

    public string? InputDeviceId { get; set; }

    public string? InputDeviceName { get; set; }

    /// <summary>
    /// Launches Spat automatically when the user signs in to Windows.
    /// </summary>
    public bool StartOnLogin { get; set; }

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Raises the log level to Debug so audio capture and input injection activity are recorded.
    /// </summary>
    public bool DebugLogging { get; set; }

    public SpeechToTextSettings SpeechToText { get; set; } = new();

    public PostProcessingSettings PostProcessing { get; set; } = new();

    public CustomDictionarySettings Dictionary { get; set; } = new();
}

/// <summary>
/// Where the custom dictionary reaches beyond its always-on deterministic replacement. The entries
/// themselves live in dictionary.json rather than in settings.
/// </summary>
public sealed class CustomDictionarySettings
{
    /// <summary>
    /// Sends the replacement terms to the transcription endpoint as a recognition hint
    /// (initial_prompt). Endpoints that reject the field are retried without it.
    /// </summary>
    public bool HintFirstPassEnabled { get; set; } = true;

    /// <summary>
    /// Adds the entry list to the post-processing prompt so the text model applies it too.
    /// </summary>
    public bool IncludeInPostProcessingEnabled { get; set; } = true;
}

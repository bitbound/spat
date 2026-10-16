using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Audio;
using Spat.Libraries.Core.Input;
using Spat.Libraries.Core.Platform;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.Speech;
using Spat.Views;

namespace Spat.ViewModels;

public sealed partial class SettingsViewModel : SettingsPageViewModel<SettingsView>
{
    private readonly IAudioCaptureDeviceEnumerator _devices;
    private readonly ISettingsService _settings;
    private readonly ISpeechToTextClient _speechToText;
    private readonly IAutostartService _autostart;
    private readonly ICertificateTrustService _certTrust;
    private readonly FileLoggerProvider _fileLogger;
    private readonly LogLevelSwitch _logLevel;

  [ObservableProperty]
  public partial bool StartOnLogin { get; set; }

  [ObservableProperty]
  public partial string? DeviceMessage { get; set; }
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
  public partial string Hotkey { get; set; } = AppSettings.DefaultHotkey;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HotkeyDisplay))]
  public partial bool IsCapturingHotkey { get; set; }
  [ObservableProperty]
  public partial string? HotkeyCaptureMessage { get; set; }

  [ObservableProperty]
  public partial string TriggerModeName { get; set; } = "Tap";
  [ObservableProperty]
  public partial string ThemeModeName { get; set; } = "System";

  [ObservableProperty]
  public partial int HistoryItemsToKeep { get; set; } = 50;
  [ObservableProperty]
  public partial bool KeepRecordings { get; set; } = true;

  [ObservableProperty]
  public partial bool CheckForUpdates { get; set; } = true;
  [ObservableProperty]
  public partial int MaximumRecordingSeconds { get; set; } = 120;

  [ObservableProperty]
  public partial float SilenceRmsThreshold { get; set; } = AppSettings.DefaultSilenceRmsThreshold;
  [ObservableProperty]
  public partial int TypingDelayMs { get; set; } = AppSettings.DefaultTypingDelayMs;

  [ObservableProperty]
  public partial bool DebugLogging { get; set; }
  [ObservableProperty]
  public partial ObservableCollection<DeviceOption> DeviceOptions { get; set; } = [];

  [ObservableProperty]
  public partial DeviceOption? SelectedDevice { get; set; }
  [ObservableProperty]
  public partial string? SpeechEndpoint { get; set; }

  [ObservableProperty]
  public partial string? SpeechApiKey { get; set; }
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(SpeechModelWarning))]
  [NotifyPropertyChangedFor(nameof(HasSpeechModelWarning))]
  public partial string? SpeechModelId { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(SpeechModelWarning))]
  [NotifyPropertyChangedFor(nameof(HasSpeechModelWarning))]
  public partial bool ModelSelectionEnabled { get; set; } = true;
  [ObservableProperty]
  public partial ObservableCollection<string> SpeechModels { get; set; } = [];

  [ObservableProperty]
  public partial bool IsLoadingSpeechModels { get; set; }

  [ObservableProperty]
    private string? _speechModelsMessage;

  [ObservableProperty]
  public partial bool IsPublisherTrusted { get; set; }

  [ObservableProperty]
    private bool _isTrustingPublisher;

  [ObservableProperty]
  public partial string? PublisherTrustMessage { get; set; }

  public SettingsViewModel(
        ISettingsService settings,
        IAudioCaptureDeviceEnumerator devices,
        ISpeechToTextClient speechToText,
        IAutostartService autostart,
        ICertificateTrustService certTrust,
        FileLoggerProvider fileLogger,
        LogLevelSwitch logLevel)
    {
        _settings = settings;
        _devices = devices;
        _speechToText = speechToText;
        _autostart = autostart;
        _certTrust = certTrust;
        _fileLogger = fileLogger;
        _logLevel = logLevel;
    }

    public IReadOnlyList<string> ThemeModes { get; } = ["System", "Light", "Dark"];

    public IReadOnlyList<string> TriggerModes { get; } = ["Tap", "Press"];

    public string HotkeyDisplay => IsCapturingHotkey
        ? "Press the keys you want to use…"
        : Hotkey;

    public string LogFilePath => _fileLogger.LogFilePath;

    public string? SpeechModelWarning =>
        ModelSelectionEnabled && string.IsNullOrWhiteSpace(SpeechModelId)
            ? "Dictation needs a model. Type its id or load the list. If the server only serves one model, untick \"This server lists its models\"."
            : null;

    public bool HasSpeechModelWarning => !string.IsNullOrEmpty(SpeechModelWarning);

    protected override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        LoadFromSettings();
        RefreshPublisherTrustState();

        _ = LoadDevicesAsync();
    }

    [RelayCommand]
    private void BeginHotkeyCapture()
    {
        IsCapturingHotkey = true;
        HotkeyCaptureMessage = null;
    }

    [RelayCommand]
    public void CancelHotkeyCapture()
    {
        IsCapturingHotkey = false;
        HotkeyCaptureMessage = null;
    }

    // Called by the view once a key press has been turned into a shortcut string.
    public void CompleteHotkeyCapture(string combo)
    {
        Hotkey = combo;
        IsCapturingHotkey = false;
        HotkeyCaptureMessage = null;
    }

    public void FailHotkeyCapture(string message)
    {
        HotkeyCaptureMessage = message;
    }

    [RelayCommand]
    private async Task LoadDevicesAsync()
    {
        try
        {
            var found = await _devices.GetDevicesAsync();
            var options = found.Select(device => new DeviceOption(device)).ToList();
            var selectedId = _settings.Current.InputDeviceId;

            DeviceOptions = new ObservableCollection<DeviceOption>(options);
            SelectedDevice = options.FirstOrDefault(option => option.Device.Id == selectedId)
                ?? options.FirstOrDefault(option => option.Device.IsDefault)
                ?? options.FirstOrDefault();
        }
        catch (Exception ex)
        {
            DeviceMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshSpeechModelsAsync()
    {
        IsLoadingSpeechModels = true;
        SpeechModelsMessage = null;

        try
        {
            var models = await _speechToText.ListModelsAsync(SpeechEndpoint, SpeechApiKey);
            SpeechModels = new ObservableCollection<string>(models.Select(model => model.Id).Order(StringComparer.Ordinal));
            SpeechModelsMessage = $"{SpeechModels.Count} models";
        }
        catch (Exception ex)
        {
            SpeechModelsMessage = ex.Message;
        }
        finally
        {
            IsLoadingSpeechModels = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveError = null;
        SaveMessage = null;

        var settings = _settings.Current;

        settings.Theme = Enum.TryParse<ThemeMode>(ThemeModeName, out var theme) ? theme : ThemeMode.System;
        settings.TriggerMode = Enum.TryParse<ShortcutTriggerMode>(TriggerModeName, out var trigger)
            ? trigger
            : ShortcutTriggerMode.Tap;
        settings.Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? AppSettings.DefaultHotkey : Hotkey.Trim();
        settings.HistoryItemsToKeep = Math.Clamp(HistoryItemsToKeep, 0, 5000);
        settings.KeepRecordings = KeepRecordings;
        settings.MaximumRecordingSeconds = Math.Clamp(MaximumRecordingSeconds, 1, 3600);
        settings.SilenceRmsThreshold = Math.Clamp(SilenceRmsThreshold, 0.0001f, 0.1f);
        settings.TypingDelayMs = Math.Clamp(TypingDelayMs, 0, 100);
        settings.StartOnLogin = StartOnLogin;
        settings.CheckForUpdates = CheckForUpdates;
        settings.DebugLogging = DebugLogging;
        settings.InputDeviceId = SelectedDevice?.Device.Id;
        settings.InputDeviceName = SelectedDevice?.Device.Name;

        settings.SpeechToText.Endpoint = SpeechEndpoint?.Trim();
        settings.SpeechToText.ApiKey = SpeechApiKey?.Trim();
        settings.SpeechToText.ModelId = SpeechModelId?.Trim();
        settings.SpeechToText.ModelSelectionEnabled = ModelSelectionEnabled;

        await _settings.SaveAsync();

        ApplyLogLevel();
        ApplyAutostart();

        ReportSaved();
    }

    // The toggle in the UI is just the intent; the registry entry is only touched here so a cancelled
    // edit never changes what runs at sign-in.
    private void ApplyAutostart()
    {
        try
        {
            if (_autostart.IsEnabled() != StartOnLogin)
            {
                _autostart.SetEnabled(StartOnLogin);
            }
        }
        catch (Exception ex)
        {
            SaveError = $"Could not update start-on-login: {ex.Message}";
        }
    }

    private void ApplyLogLevel()
    {
        _logLevel.Set(DebugLogging ? LogLevel.Debug : LogLevel.Information);
    }

    /// <summary>
    /// Opens the log file so a report of "it just stopped working" can be checked against what the
    /// app actually did.
    /// </summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_fileLogger.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SaveError = $"Could not open the log file: {ex.Message}";
        }
    }

    // Spat is signed with a self-signed cert that public machines have no reason to trust, so the
    // settings page offers to plant the public key in the per-user trust stores (no elevation).
    private void RefreshPublisherTrustState()
    {
        try
        {
            IsPublisherTrusted = _certTrust.IsPublisherTrusted();
        }
        catch (Exception ex)
        {
            PublisherTrustMessage = $"Could not read the certificate stores: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TrustPublisherAsync()
    {
        PublisherTrustMessage = null;
        IsTrustingPublisher = true;

        try
        {
            // Certificate store writes can hit slow group-policy callbacks, so keep the UI free.
            await Task.Run(_certTrust.TrustPublisher);

            IsPublisherTrusted = true;
            PublisherTrustMessage = "Bitbound is now trusted for this user account. Already-downloaded copies of Spat show Bitbound as the verified publisher.";
        }
        catch (Exception ex)
        {
            PublisherTrustMessage = $"Could not trust the certificate: {ex.Message}";
        }
        finally
        {
            IsTrustingPublisher = false;
        }
    }

    private void LoadFromSettings()
    {
        var settings = _settings.Current;

        ThemeModeName = settings.Theme.ToString();
        TriggerModeName = settings.TriggerMode.ToString();
        Hotkey = settings.Hotkey;
        HistoryItemsToKeep = settings.HistoryItemsToKeep;
        KeepRecordings = settings.KeepRecordings;
        MaximumRecordingSeconds = settings.MaximumRecordingSeconds;
        SilenceRmsThreshold = settings.SilenceRmsThreshold;
        TypingDelayMs = settings.TypingDelayMs;
        StartOnLogin = _autostart.IsEnabled();
        CheckForUpdates = settings.CheckForUpdates;
        DebugLogging = settings.DebugLogging;

        SpeechEndpoint = settings.SpeechToText.Endpoint;
        SpeechApiKey = settings.SpeechToText.ApiKey;
        SpeechModelId = settings.SpeechToText.ModelId;
        ModelSelectionEnabled = settings.SpeechToText.ModelSelectionEnabled;
    }

    public sealed record DeviceOption(AudioDevice Device)
    {
        public override string ToString()
        {
            return Device.Name;
        }
    }
}
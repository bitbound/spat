using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Settings;
using Spat.Services;
using Spat.ViewModels;

namespace Spat.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel Create(ISettingsService settings)
    {
        var logLevel = new LogLevelSwitch(LogLevel.Information);

        return new SettingsViewModel(
            settings,
            new FakeAudioDeviceEnumerator(),
            new FakeSpeechToTextClient(),
            new FakeAutostartService(),
            new FakeCertificateTrustService(),
            new FileLoggerProvider(Path.Combine(Path.GetTempPath(), "spat-test-logs"), logLevel),
            logLevel);
    }

    [Fact]
    public async Task InitializeAsync_WhenSettingsHaveValues_FillsNumericFields()
    {
        var settings = TestSettings.Create(new InMemoryFileStore(), new AppSettings
        {
            HistoryItemsToKeep = 17,
            MaximumRecordingSeconds = 42,
            SilenceRmsThreshold = 0.004f,
            TypingDelayMs = 7,
        });
        var viewModel = Create(settings);

        await viewModel.InitializeAsync();

        Assert.Equal("17", viewModel.HistoryItemsToKeep);
        Assert.Equal("42", viewModel.MaximumRecordingSeconds);
        Assert.Equal("0.004", viewModel.SilenceRmsThreshold);
        Assert.Equal("7", viewModel.TypingDelayMs);
    }

    [Fact]
    public async Task SaveCommand_WhenNumericFieldIsNotANumber_ReportsFieldNameAndKeepsSettings()
    {
        var settings = TestSettings.Create(new InMemoryFileStore(), new AppSettings { MaximumRecordingSeconds = 42 });
        var viewModel = Create(settings);
        await viewModel.InitializeAsync();

        viewModel.MaximumRecordingSeconds = "not a number";
        viewModel.ThemeModeName = "Dark";
        viewModel.Hotkey = "Ctrl+Alt+F9";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Max recording seconds must be a whole number.", viewModel.SaveError);
        Assert.Equal(42, settings.Current.MaximumRecordingSeconds);
        Assert.Equal(ThemeMode.System, settings.Current.Theme);
        Assert.Equal(AppSettings.DefaultHotkey, settings.Current.Hotkey);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task SaveCommand_WhenSilenceThresholdIsInvalid_ReportsFieldNameAndKeepsSettings(string value)
    {
        var settings = TestSettings.Create(new InMemoryFileStore(), new AppSettings { SilenceRmsThreshold = 0.004f });
        var viewModel = Create(settings);
        await viewModel.InitializeAsync();

        viewModel.SilenceRmsThreshold = value;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Silence threshold must be a number.", viewModel.SaveError);
        Assert.Equal(0.004f, settings.Current.SilenceRmsThreshold);
    }

    [Fact]
    public async Task SaveCommand_WhenNumericFieldsAreOutOfRange_ClampsAndPersistsValues()
    {
        var settings = TestSettings.Create(new InMemoryFileStore());
        var viewModel = Create(settings);
        await viewModel.InitializeAsync();

        viewModel.HistoryItemsToKeep = "99999";
        viewModel.MaximumRecordingSeconds = "0";
        viewModel.SilenceRmsThreshold = "5";
        viewModel.TypingDelayMs = "-3";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SaveError);
        Assert.Equal(5000, settings.Current.HistoryItemsToKeep);
        Assert.Equal(1, settings.Current.MaximumRecordingSeconds);
        Assert.Equal(0.1f, settings.Current.SilenceRmsThreshold);
        Assert.Equal(0, settings.Current.TypingDelayMs);

        settings.Reload();

        Assert.Equal(5000, settings.Current.HistoryItemsToKeep);
        Assert.Equal(1, settings.Current.MaximumRecordingSeconds);
        Assert.Equal(0.1f, settings.Current.SilenceRmsThreshold);
        Assert.Equal(0, settings.Current.TypingDelayMs);
    }
}

using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Settings;
using Spat.Services;
using Spat.ViewModels;

namespace Spat.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel Create(ISettingsService settings)
    {
        return new SettingsViewModel(
            settings,
            new FakeAudioDeviceEnumerator(),
            new FakeSpeechToTextClient(),
            new FakeAutostartService(),
            new FakeCertificateTrustService(),
            new FileLoggerProvider(Path.Combine(Path.GetTempPath(), "spat-test-logs"), new LogLevelSwitch(LogLevel.Information)),
            new LogLevelSwitch(LogLevel.Information));
    }

    [Fact]
    public async Task InitializeAsync_FillsNumericFieldsFromSettings()
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

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Max recording seconds must be a whole number.", viewModel.SaveError);
        Assert.Equal(42, settings.Current.MaximumRecordingSeconds);
    }

    [Fact]
    public async Task SaveCommand_WhenSilenceThresholdIsNotANumber_ReportsFieldNameAndKeepsSettings()
    {
        var settings = TestSettings.Create(new InMemoryFileStore(), new AppSettings { SilenceRmsThreshold = 0.004f });
        var viewModel = Create(settings);
        await viewModel.InitializeAsync();

        viewModel.SilenceRmsThreshold = "not a number";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Silence threshold must be a number.", viewModel.SaveError);
        Assert.Equal(0.004f, settings.Current.SilenceRmsThreshold);
    }
}

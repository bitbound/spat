using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Audio;
using Spat.Libraries.Core.CustomDictionary;
using Spat.Libraries.Core.Dictation;
using Spat.Libraries.Core.History;
using Spat.Libraries.Core.Input;
using Spat.Libraries.Core.Platform;
using Spat.Libraries.Core.Prompts;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.Speech;
using Spat.Libraries.Core.Theming;
using Spat.Libraries.Core.Updates;
using Spat.Libraries.Native.Windows;
using Spat.Libraries.Speech;
using Spat.Libraries.Updater;
using Spat.Views;

namespace Spat.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpat(this IServiceCollection services)
    {
        // Built up front because logging needs the path before the container exists, and the same
        // instance is registered below so every consumer sees one set of paths.
        var paths = new PlatformPaths();
        var logLevel = new LogLevelSwitch(LogLevel.Information);
        var fileLogger = new FileLoggerProvider(paths.ConfigDirectory, logLevel);

        // The app is a windowed executable with no console attached, so the file sink is the only
        // place diagnostics survive. The console sink is kept for running from a terminal.
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.AddProvider(fileLogger);
            builder.AddFilter((_, level) => logLevel.IsEnabled(level));
        });

        services.AddSingleton(logLevel);
        services.AddSingleton(fileLogger);

        services.AddSingleton<IPlatformPaths>(paths);
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<IAppInfo, AssemblyAppInfo>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SpatHttp>();
        services.AddSingleton(sp => sp.GetRequiredService<SpatHttp>().ForSpeech);
        services.AddSingleton(sp => sp.GetRequiredService<SpatHttp>().ForText);

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPromptService, PromptService>();
        services.AddSingleton<ICustomDictionaryService, CustomDictionaryService>();
        services.AddSingleton<IRecordingStore, RecordingStore>();
        services.AddSingleton<IHistoryService, HistoryService>();

        // One recorder instance backs both interfaces, so the level meter reports the same stream
        // the dictation is recording.
        services.AddSingleton<WasapiRecorder>();
        services.AddSingleton<IAudioRecorder>(sp => sp.GetRequiredService<WasapiRecorder>());
        services.AddSingleton<IAudioLevelMeter>(sp => sp.GetRequiredService<WasapiRecorder>());
        services.AddSingleton<IAudioPlayer, WasapiPlayer>();
        services.AddSingleton<IAudioCaptureDeviceEnumerator, WasapiDeviceEnumerator>();

        services.AddSingleton<ISpeechToTextClient, OpenAiSpeechToTextClient>();
        services.AddSingleton<ITextGenerationClient, OpenAiTextGenerationClient>();

        services.AddSingleton<ITextInputInjector, WindowsTextInputInjector>();
        services.AddSingleton<IGlobalHotkeySource, WindowsGlobalHotkeySource>();
        services.AddSingleton<ISystemColorSchemeSource, RegistryColorSchemeSource>();
        services.AddSingleton<IAutostartService, RegistryAutostartService>();
        services.AddSingleton<ICertificateTrustService, BitboundCertificateTrustService>();

        services.AddSingleton<IUpdateService, GitHubReleaseUpdateService>();
        services.AddSingleton<UpdateHandoffRunner>();
        services.AddSingleton<IUpdatePollingService, UpdatePollingService>();

        services.AddSingleton<IDictationCoordinator, DictationCoordinator>();
        services.AddSingleton<IStatusOverlayController, StatusOverlayController>();

        services.AddSingleton<IThemeProvider, ThemeProvider>();
        services.AddSingleton<INavigationProvider, NavigationProvider>();
        services.AddSingleton<ISnackbarService, SnackbarService>();

        services.AddSingleton<IMainWindowViewModel, MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<AboutViewModel>();

        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsView>();
        services.AddTransient<HistoryView>();
        services.AddTransient<AboutView>();

        return services;
    }
}

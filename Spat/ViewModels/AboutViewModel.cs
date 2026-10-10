using Avalonia.Threading;
using Spat.Libraries.Core.Platform;
using Spat.Libraries.Core.Updates;
using Spat.Views;

namespace Spat.ViewModels;

/// <summary>
/// Backs the update indicator shown beside the version on the About page.
/// </summary>
public enum UpdateIndicatorState
{
    NotChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed partial class AboutViewModel : ViewModelBase<AboutView>
{
    private readonly IAppInfo _appInfo;
    private readonly IUpdateService _updates;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckingForUpdates))]
    [NotifyPropertyChangedFor(nameof(IsUpToDate))]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyPropertyChangedFor(nameof(HasUpdateCheckFailed))]
    [NotifyPropertyChangedFor(nameof(UpdateStatusText))]
    [NotifyPropertyChangedFor(nameof(UpdateStatusIconKey))]
    private UpdateIndicatorState _updateState = UpdateIndicatorState.NotChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateStatusText))]
    private string? _availableVersion;

    public AboutViewModel(IAppInfo appInfo, IUpdateService updates)
    {
        _appInfo = appInfo;
        _updates = updates;

        _updates.UpdateAvailable += OnUpdateAvailable;
    }

    public bool IsCheckingForUpdates => UpdateState == UpdateIndicatorState.Checking;

    public bool IsUpToDate => UpdateState == UpdateIndicatorState.UpToDate;

    public bool IsUpdateAvailable => UpdateState == UpdateIndicatorState.UpdateAvailable;

    public bool HasUpdateCheckFailed => UpdateState == UpdateIndicatorState.Failed;

    public string UpdateStatusText => UpdateState switch
    {
        UpdateIndicatorState.Checking => "Checking...",
        UpdateIndicatorState.UpToDate => "Up to date",
        UpdateIndicatorState.UpdateAvailable when AvailableVersion is { Length: > 0 } version =>
            $"Update available ({version})",
        UpdateIndicatorState.UpdateAvailable => "Update available",
        UpdateIndicatorState.Failed => "Couldn't check. Click to retry",
        _ => "Check for updates",
    };

    public string UpdateStatusIconKey => UpdateState switch
    {
        UpdateIndicatorState.UpToDate => "checkmark_circle_regular",
        UpdateIndicatorState.UpdateAvailable => "arrow_download_regular",
        UpdateIndicatorState.Failed => "warning_regular",
        _ => "arrow_sync_regular",
    };

    public string RepositoryUrl => _appInfo.RepositoryUrl;

    public string LicenseUrl => $"{_appInfo.RepositoryUrl}/blob/main/LICENSE";

    public string IssuesUrl => $"{_appInfo.RepositoryUrl}/issues";

    public IReadOnlyList<LibraryLink> Libraries { get; } =
    [
        new("Avalonia",
            "https://github.com/AvaloniaUI/Avalonia",
            "https://github.com/AvaloniaUI/Avalonia/blob/master/LICENSE"),
        new("Community Toolkit MVVM",
            "https://github.com/CommunityToolkit/dotnet",
            "https://github.com/CommunityToolkit/dotnet/blob/main/LICENSE"),
        new("NAudio",
            "https://github.com/naudio/NAudio",
            "https://github.com/naudio/NAudio/blob/master/license.txt"),
        new(".NET and Microsoft.Extensions",
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT"),
        new("Microsoft.Windows.CsWin32",
            "https://github.com/microsoft/cswin32",
            "https://github.com/microsoft/cswin32/blob/main/LICENSE"),
        new("Fluent UI System Icons",
            "https://github.com/microsoft/fluentui-system-icons",
            "https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE"),
    ];

    protected override Task OnInitializeAsync()
    {
        AppVersion = _appInfo.Version.ToString();

        // The startup check may already have found a release, so reflect it instead of checking again.
        if (_updates.AvailableUpdate is { } update)
        {
            ShowUpdate(update);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateState = UpdateIndicatorState.Checking;

        try
        {
            var result = await _updates.CheckAsync();

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                    ShowUpdate(update);
                    break;
                case UpdateCheckStatus.UpToDate:
                    UpdateState = UpdateIndicatorState.UpToDate;
                    break;
                default:
                    UpdateState = UpdateIndicatorState.Failed;
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex}");
            UpdateState = UpdateIndicatorState.Failed;
        }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not open {url}: {ex}");
        }
    }

    private void OnUpdateAvailable(object? sender, UpdateInfo info)
    {
        Dispatcher.UIThread.Post(() => ShowUpdate(info));
    }

    private void ShowUpdate(UpdateInfo update)
    {
        AvailableVersion = update.Version;
        UpdateState = UpdateIndicatorState.UpdateAvailable;
    }
}

public sealed record LibraryLink(string Name, string SourceUrl, string LicenseUrl);

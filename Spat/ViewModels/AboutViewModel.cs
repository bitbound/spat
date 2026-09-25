using Spat.Libraries.Core.Platform;
using Spat.Views;

namespace Spat.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase<AboutView>
{
    private readonly IAppInfo _appInfo;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    public AboutViewModel(IAppInfo appInfo)
    {
        _appInfo = appInfo;
    }

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

        return Task.CompletedTask;
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
}

public sealed record LibraryLink(string Name, string SourceUrl, string LicenseUrl);

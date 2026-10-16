using Avalonia.Controls;
using Avalonia.Threading;

namespace Spat.ViewModels;

/// <summary>
/// Shared footer state for the settings pages. Each page saves only the slice of settings it owns,
/// so the result and failure message belong to that page alone.
/// </summary>
public abstract partial class SettingsPageViewModel<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TView> : ViewModelBase<TView>
    where TView : Control
{
    private DispatcherTimer? _saveMessageTimer;

    [ObservableProperty]
    private string? _saveMessage;

    [ObservableProperty]
    private string? _saveError;

    protected void ReportSaved()
    {
        SaveError = null;
        SaveMessage = "Saved.";
    }

    protected void ReportFailed(string message)
    {
        SaveMessage = null;
        SaveError = message;
    }

    // A note that sits there forever cannot show that a later click did anything, so it clears itself.
    partial void OnSaveMessageChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        _saveMessageTimer?.Stop();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveMessage = null;
        };

        _saveMessageTimer = timer;
        timer.Start();
    }
}
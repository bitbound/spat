using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.Updates;

namespace Spat.Services;

public interface IUpdatePollingService
{
    /// <summary>
    /// Begins checking for updates in the background on a fixed interval.
    /// </summary>
    void Start();
}

/// <summary>
/// Re-checks GitHub Releases while the app sits in the tray. The launch check only covers startup,
/// so an instance left running for days would otherwise never see a release published since.
/// </summary>
public sealed class UpdatePollingService(
    IUpdateService updates,
    ISettingsService settings,
    TimeProvider timeProvider,
    ILogger<UpdatePollingService> logger) : IUpdatePollingService
{
    /// <summary>
    /// How long to wait between background checks.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(2);

    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _ = RunAsync();
    }

    // Runs for the life of the process. Each pass catches its own failures, so one bad check cannot
    // end the loop.
    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        while (await timer.WaitForNextTickAsync())
        {
            await PollAsync();
        }
    }

    internal async Task PollAsync()
    {
        // Both are re-read on every pass, so turning the setting off or already holding a release
        // stops the network traffic without tearing down the loop.
        if (!settings.Current.CheckForUpdates || updates.AvailableUpdate is not null)
        {
            return;
        }

        try
        {
            await updates.CheckAsync();
        }
        catch (Exception ex)
        {
            // A background check must never surface to the user as a crash. The next pass retries.
            logger.LogWarning(ex, "The scheduled update check failed.");
        }
    }
}

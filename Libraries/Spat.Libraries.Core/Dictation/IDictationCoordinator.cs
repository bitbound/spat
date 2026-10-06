namespace Spat.Libraries.Core.Dictation;

public interface IDictationCoordinator
{
    DictationState State { get; }

    string? LastError { get; }

    bool IsListening { get; }

    event EventHandler? StateChanged;

    /// <summary>
    /// Raised when a recording is cut short by the configured maximum length. It fires before the
    /// take is transcribed, so a listener sees it while the rest of the run is still going.
    /// </summary>
    event EventHandler<DictationLimitReachedEventArgs>? RecordingLimitReached;

    Task ToggleAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

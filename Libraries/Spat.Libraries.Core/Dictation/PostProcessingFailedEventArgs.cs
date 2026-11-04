namespace Spat.Libraries.Core.Dictation;

/// <summary>
/// Reported when the post-processing pass could not be used. The take is not lost: the raw
/// transcription is typed instead, and the reason says why the cleanup was skipped.
/// </summary>
public sealed class PostProcessingFailedEventArgs(string reason) : EventArgs
{
    /// <summary>
    /// A single-line summary of the failure, meant to be shown as is. The full exception goes to the log.
    /// </summary>
    public string Reason { get; } = reason;
}

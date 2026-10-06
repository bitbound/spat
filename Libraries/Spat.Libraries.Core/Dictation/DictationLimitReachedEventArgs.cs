namespace Spat.Libraries.Core.Dictation;

/// <summary>
/// Reported when a recording ends because it reached the configured maximum length. The take is cut
/// short, not discarded, so it still goes on to be transcribed and typed.
/// </summary>
public sealed class DictationLimitReachedEventArgs(TimeSpan limit) : EventArgs
{
    public TimeSpan Limit { get; } = limit;
}

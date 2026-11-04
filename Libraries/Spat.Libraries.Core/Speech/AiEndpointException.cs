namespace Spat.Libraries.Core.Speech;

public sealed class AiEndpointException(string message, int? statusCode = null, string? detail = null, bool isTimeout = false)
    : Exception(detail is null ? message : $"{message} {detail}")
{
    public int? StatusCode { get; } = statusCode;

    public string? Detail { get; } = detail;

    /// <summary>
    /// True when the endpoint never answered within its budget. The dictation path degrades to the raw
    /// transcription for a slow endpoint and a badly-answered one alike, so nothing branches on this
    /// today; the timeout reaches the log through <see cref="Exception.Message"/>, not through this
    /// flag, which is kept so a caller that does want the distinction has it without string matching.
    /// </summary>
    public bool IsTimeout { get; } = isTimeout;
}

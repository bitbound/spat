namespace Spat.Libraries.Core.Speech;

public sealed class AiEndpointException(string message, int? statusCode = null, string? detail = null, bool isTimeout = false)
    : Exception(detail is null ? message : $"{message} {detail}")
{
    public int? StatusCode { get; } = statusCode;

    public string? Detail { get; } = detail;

    /// <summary>
    /// True when the endpoint never answered within its budget. Callers that already hold usable
    /// results (e.g. a raw transcription waiting on post-processing) can treat this as recoverable
    /// rather than a failed request.
    /// </summary>
    public bool IsTimeout { get; } = isTimeout;
}

namespace Spat.Libraries.Core.Speech;

public interface ITextGenerationClient
{
    /// <summary>
    /// Sends the instructions as a system message and the transcription as a user message, the
    /// split that keeps the model from treating dictated words as rules.
    /// </summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);

    // Endpoint and key are arguments rather than settings lookups so the caller can list models from a
    // value the user typed but has not saved yet.
    Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default);
}

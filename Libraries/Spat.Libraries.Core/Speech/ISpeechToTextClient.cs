namespace Spat.Libraries.Core.Speech;

public interface ISpeechToTextClient
{
    /// <param name="initialPrompt">
    /// A recognition hint (Whisper-style initial_prompt), e.g. the custom dictionary's terms. Clients
    /// that send it must tolerate endpoints that reject the field.
    /// </param>
    Task<string> TranscribeAsync(byte[] wavBytes, string? initialPrompt = null, CancellationToken cancellationToken = default);

    // Endpoint and key are arguments rather than settings lookups so the caller can list models from a
    // value the user typed but has not saved yet.
    Task<IReadOnlyList<AiModel>> ListModelsAsync(string? endpoint, string? apiKey, CancellationToken cancellationToken = default);
}

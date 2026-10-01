namespace Spat.Libraries.Core.Prompts;

public static class PromptRenderer
{
    public const string OutputPlaceholder = "${stt_output}";

    public const string DictionaryPlaceholder = "${dictionary}";

    /// <summary>
    /// Injects the transcription at the placeholder. Prompts without the placeholder get the
    /// transcription appended so a user-authored prompt cannot silently discard the audio result.
    /// </summary>
    public static string Render(string instructions, string transcription)
    {
        return Render(instructions, transcription, dictionaryBlock: null);
    }

    /// <summary>
    /// Injects the transcription and the dictionary correction block at their placeholders. Prompts
    /// without a placeholder get the value appended so a user-authored prompt cannot silently
    /// discard either input.
    /// </summary>
    public static string Render(string instructions, string transcription, string? dictionaryBlock)
    {
        var rendered = instructions;

        if (rendered.Contains(DictionaryPlaceholder, StringComparison.Ordinal))
        {
            rendered = rendered.Replace(DictionaryPlaceholder, dictionaryBlock ?? string.Empty, StringComparison.Ordinal);
        }
        else if (!string.IsNullOrWhiteSpace(dictionaryBlock))
        {
            rendered = string.IsNullOrEmpty(rendered) ? dictionaryBlock : $"{rendered}\n\n{dictionaryBlock}";
        }

        if (rendered.Contains(OutputPlaceholder, StringComparison.Ordinal))
        {
            return rendered.Replace(OutputPlaceholder, transcription, StringComparison.Ordinal);
        }

        if (string.IsNullOrEmpty(rendered))
        {
            return transcription;
        }

        return $"{rendered}\n\nTranscription:\n{transcription}";
    }
}

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
    /// Injects the transcription and the dictionary correction block at their placeholders. The block
    /// is kept ahead of the transcription, the position that steers the model best. Prompts without a
    /// placeholder get the value inserted so a user-authored prompt cannot silently discard either input.
    /// </summary>
    public static string Render(string instructions, string transcription, string? dictionaryBlock)
    {
        var rendered = instructions;
        var blockPlaced = false;

        if (rendered.Contains(DictionaryPlaceholder, StringComparison.Ordinal))
        {
            rendered = rendered.Replace(DictionaryPlaceholder, dictionaryBlock ?? string.Empty, StringComparison.Ordinal);
            blockPlaced = true;
        }

        // The correction block trails the text it is meant to fix when it is tacked on after the
        // transcription, so a prompt without a dictionary placeholder gets the block right before the
        // transcription instead.
        if (rendered.Contains(OutputPlaceholder, StringComparison.Ordinal))
        {
            var prefix = !blockPlaced && !string.IsNullOrWhiteSpace(dictionaryBlock)
                ? $"{dictionaryBlock}\n\n"
                : string.Empty;

            return rendered.Replace(OutputPlaceholder, $"{prefix}{transcription}", StringComparison.Ordinal);
        }

        if (!blockPlaced && !string.IsNullOrWhiteSpace(dictionaryBlock))
        {
            rendered = string.IsNullOrEmpty(rendered) ? dictionaryBlock : $"{rendered}\n\n{dictionaryBlock}";
        }

        if (string.IsNullOrEmpty(rendered))
        {
            return transcription;
        }

        return $"{rendered}\n\nTranscription:\n{transcription}";
    }
}

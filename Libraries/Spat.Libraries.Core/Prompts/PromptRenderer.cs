namespace Spat.Libraries.Core.Prompts;

public static class PromptRenderer
{
    public const string OutputPlaceholder = "${stt_output}";

    public const string DictionaryPlaceholder = "${dictionary}";

    public const string SpeechCommandsPlaceholder = "${speech_commands}";

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
        return Render(instructions, transcription, dictionaryBlock, speechCommandsBlock: null);
    }

    /// <summary>
    /// Injects the transcription and both instruction blocks at their placeholders. A block whose
    /// placeholder the prompt does not carry goes ahead of the transcription instead, with the spoken
    /// commands first because they say how to read the words rather than which words to fix.
    /// </summary>
    public static string Render(
        string instructions,
        string transcription,
        string? dictionaryBlock,
        string? speechCommandsBlock)
    {
        var rendered = instructions;
        var commandsPlaced = false;
        var dictionaryPlaced = false;

        if (rendered.Contains(SpeechCommandsPlaceholder, StringComparison.Ordinal))
        {
            rendered = rendered.Replace(SpeechCommandsPlaceholder, speechCommandsBlock ?? string.Empty, StringComparison.Ordinal);
            commandsPlaced = true;
        }

        if (rendered.Contains(DictionaryPlaceholder, StringComparison.Ordinal))
        {
            rendered = rendered.Replace(DictionaryPlaceholder, dictionaryBlock ?? string.Empty, StringComparison.Ordinal);
            dictionaryPlaced = true;
        }

        // An unplaced block lands after the text it is meant to steer when it is tacked on at the end,
        // so it is gathered here and inserted ahead of the transcription instead.
        var unplaced = new List<string>();

        if (!commandsPlaced && !string.IsNullOrWhiteSpace(speechCommandsBlock))
        {
            unplaced.Add(speechCommandsBlock);
        }

        if (!dictionaryPlaced && !string.IsNullOrWhiteSpace(dictionaryBlock))
        {
            unplaced.Add(dictionaryBlock);
        }

        if (rendered.Contains(OutputPlaceholder, StringComparison.Ordinal))
        {
            var prefix = unplaced.Count == 0 ? string.Empty : $"{string.Join("\n\n", unplaced)}\n\n";

            return rendered.Replace(OutputPlaceholder, $"{prefix}{transcription}", StringComparison.Ordinal);
        }

        if (unplaced.Count > 0)
        {
            var block = string.Join("\n\n", unplaced);
            rendered = string.IsNullOrEmpty(rendered) ? block : $"{rendered}\n\n{block}";
        }

        if (string.IsNullOrEmpty(rendered))
        {
            return transcription;
        }

        return $"{rendered}\n\nTranscription:\n{transcription}";
    }
}

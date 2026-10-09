using System.Text;

namespace Spat.Libraries.Core.CustomDictionary;

/// <summary>
/// Applies spoken-phrase substitutions to a transcription. Matching is case-insensitive and ordinal,
/// and a phrase only fires on whole-word boundaries so "are" never fires inside "area". Entries are
/// tried in list order and the first one that matches at a position wins, so list position is priority.
/// </summary>
public static class PhraseReplacer
{
    /// <summary>
    /// Single left-to-right pass. The replacement text is never re-scanned, so one entry's output
    /// cannot trigger another entry in the same run.
    /// </summary>
    public static string Replace(string text, IEnumerable<CustomDictionaryEntry> entries)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var patterns = entries
            .Where(entry => entry.Enabled && !string.IsNullOrWhiteSpace(entry.From) && !string.IsNullOrWhiteSpace(entry.To))
            .Select(entry => (From: entry.From.Trim(), To: entry.To.Trim()))
            .ToList();

        if (patterns.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var position = 0;

        while (position < text.Length)
        {
            var matched = false;

            foreach (var (from, to) in patterns)
            {
                if (position + from.Length > text.Length
                    || string.Compare(text, position, from, 0, from.Length, StringComparison.OrdinalIgnoreCase) != 0
                    || !HasWordBoundary(text, position, position + from.Length, from))
                {
                    continue;
                }

                builder.Append(to);
                position += from.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                builder.Append(text[position]);
                position++;
            }
        }

        return builder.ToString();
    }

    // A boundary is only required where the phrase itself starts or ends on a word character, so a
    // phrase beginning with punctuation can still match after a letter. No neighboring character at
    // all (string start or end) satisfies the boundary.
    private static bool HasWordBoundary(string text, int start, int end, string phrase)
    {
        if (IsWordCharacter(phrase[0])
            && start > 0
            && IsWordCharacter(text[start - 1]))
        {
            return false;
        }

        if (IsWordCharacter(phrase[^1])
            && end < text.Length
            && IsWordCharacter(text[end]))
        {
            return false;
        }

        return true;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value);
}

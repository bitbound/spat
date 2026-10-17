using System.Text.Json.Serialization;

namespace Spat.Libraries.Core.SpeechCommands;

/// <summary>
/// Spoken words that open a block, the words that close it, and what the text model should do with
/// the words in between. The phrases themselves never reach the result.
/// </summary>
public sealed class SpeechCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The words that open the block, e.g. "quote".
    /// </summary>
    public string StartPhrase { get; set; } = string.Empty;

    /// <summary>
    /// The words that close the block, e.g. "no quote". Any one of them ends it.
    /// </summary>
    public List<string> EndPhrases { get; set; } = [];

    /// <summary>
    /// What the text model does with the enclosed words, e.g. "Wrap the enclosed words in double
    /// quotation marks".
    /// </summary>
    public string Instructions { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// The start phrase with its first letter capitalized, which is how a command is named.
    /// </summary>
    [JsonIgnore]
    public string Name => StartPhrase.Length == 0
        ? string.Empty
        : StartPhrase[..1].ToUpperInvariant() + StartPhrase[1..];

    /// <summary>
    /// The end phrases, one per line.
    /// </summary>
    [JsonIgnore]
    public string EndPhrasesText
    {
        get => string.Join('\n', EndPhrases);
        set => EndPhrases = [.. value.Split('\n')];
    }

    /// <summary>
    /// The start and end phrases on one line, e.g. "quote" then "no quote".
    /// </summary>
    [JsonIgnore]
    public string PhrasesText => EndPhrases.Count == 0
        ? $"\"{StartPhrase}\""
        : $"\"{StartPhrase}\" then {string.Join(" or ", EndPhrases.Select(phrase => $"\"{phrase}\""))}";
}

/// <summary>
/// The commands that ship with Spat. Each one can be switched off, but never edited or removed.
/// </summary>
public static class BuiltInSpeechCommands
{
    public static IReadOnlyList<SpeechCommand> All =>
    [
        Create(
            "b1e5c0de-0000-4000-8000-000000000101",
            "quote",
            "no quote",
            "Wrap the enclosed words in double quotation marks, keeping their capitalization and spacing."),
        Create(
            "b1e5c0de-0000-4000-8000-000000000102",
            "pascal",
            "no pascal",
            "Write the enclosed words in Pascal case, capitalizing every word and removing the spaces between them."),
        Create(
            "b1e5c0de-0000-4000-8000-000000000103",
            "camel",
            "no camel",
            "Write the enclosed words in camel case, removing the spaces and capitalizing every word after the first."),
        Create(
            "b1e5c0de-0000-4000-8000-000000000104",
            "snake",
            "no snake",
            "Write the enclosed words in snake case, joining the words with underscores and using lower case throughout."),
        Create(
            "b1e5c0de-0000-4000-8000-000000000105",
            "kebab",
            "no kebab",
            "Write the enclosed words in kebab case, joining the words with hyphens and using lower case throughout."),
        Create(
            "b1e5c0de-0000-4000-8000-000000000106",
            "all caps",
            "no caps",
            "Write the enclosed words in upper case, keeping their spacing and punctuation."),
    ];

    private static SpeechCommand Create(string id, string startPhrase, string endPhrase, string instructions)
    {
        return new SpeechCommand
        {
            Id = new Guid(id),
            StartPhrase = startPhrase,
            EndPhrases = [endPhrase],
            Instructions = instructions,
            Enabled = true,
            IsBuiltIn = true,
        };
    }
}

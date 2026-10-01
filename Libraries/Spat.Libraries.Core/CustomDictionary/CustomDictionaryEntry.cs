namespace Spat.Libraries.Core.CustomDictionary;

/// <summary>
/// One spoken-phrase substitution: when the transcription contains the whole phrase in <see cref="From"/>,
/// <see cref="To"/> is typed instead.
/// </summary>
public sealed class CustomDictionaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The phrase as it tends to come out of the transcription, e.g. "control are".
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// The term to type instead, e.g. "ControlR". Written out exactly as the user spelled it.
    /// </summary>
    public string To { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

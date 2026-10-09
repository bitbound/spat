using System.Text.Json;
using Spat.Libraries.Core.Platform;
using Spat.Libraries.Core.Serialization;

namespace Spat.Libraries.Core.CustomDictionary;

public interface ICustomDictionaryService
{
    IReadOnlyList<CustomDictionaryEntry> GetAll();

    Task<CustomDictionaryEntry> CreateAsync(string from, string to, CancellationToken cancellationToken = default);

    Task UpdateAsync(CustomDictionaryEntry entry, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the whole dictionary in one write, which is how the Settings card saves its rows.
    /// </summary>
    Task SaveAllAsync(IReadOnlyList<CustomDictionaryEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// The replacement terms as a hint for the speech model, or null when nothing is enabled.
    /// </summary>
    string? BuildHintPrompt();

    /// <summary>
    /// The entries as an instruction block for the post-processing model, or null when nothing is enabled.
    /// </summary>
    string? BuildCorrectionBlock();

    /// <summary>
    /// Applies enabled entries to the transcription, earlier entries taking priority.
    /// </summary>
    string Replace(string text);
}

public sealed class CustomDictionaryService : ICustomDictionaryService
{
    // Whisper-style models cap the hint around 224 tokens; the trim stops at entry boundaries.
    private const int HintPromptMaxLength = 1200;

    private const string CorrectionBlockHeader =
        "Apply these term corrections to the transcription. Each line gives a phrase the model may "
        + "hear and the term it should become. Replace a phrase only when the whole words match, case-insensitive.";

    private readonly IFileStore _fileStore;
    private readonly IPlatformPaths _paths;

    public CustomDictionaryService(IPlatformPaths paths, IFileStore fileStore)
    {
        _paths = paths;
        _fileStore = fileStore;
    }

    public IReadOnlyList<CustomDictionaryEntry> GetAll()
    {
        return Read();
    }

    public async Task<CustomDictionaryEntry> CreateAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        var entry = new CustomDictionaryEntry
        {
            From = from.Trim(),
            To = to.Trim(),
        };

        var entries = Read();
        entries.Add(entry);

        await WriteAsync(entries, cancellationToken);

        return entry;
    }

    public async Task UpdateAsync(CustomDictionaryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entries = Read();
        var index = entries.FindIndex(stored => stored.Id == entry.Id);

        if (index < 0)
        {
            throw new InvalidOperationException($"Dictionary entry {entry.Id} does not exist.");
        }

        var normalized = Normalize(entry)
            ?? throw new ArgumentException("Both the phrase and the term are required.", nameof(entry));

        entries[index] = normalized;

        await WriteAsync(entries, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entries = Read();
        var removed = entries.RemoveAll(stored => stored.Id == id) > 0;

        if (removed)
        {
            await WriteAsync(entries, cancellationToken);
        }

        return removed;
    }

    public async Task SaveAllAsync(IReadOnlyList<CustomDictionaryEntry> entries, CancellationToken cancellationToken = default)
    {
        var normalized = entries
            .Select(entry => Normalize(entry))
            .OfType<CustomDictionaryEntry>()
            .ToList();

        await WriteAsync(normalized, cancellationToken);
    }

    public string? BuildHintPrompt()
    {
        var terms = Read()
            .Where(entry => entry.Enabled)
            .Select(entry => entry.To)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (terms.Count == 0)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();

        foreach (var term in terms)
        {
            if (builder.Length > 0 && builder.Length + term.Length + 2 > HintPromptMaxLength)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(term);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    public string? BuildCorrectionBlock()
    {
        var entries = Read().Where(entry => entry.Enabled).ToList();

        if (entries.Count == 0)
        {
            return null;
        }

        var lines = entries.Select(entry => $"- \"{entry.From}\" -> \"{entry.To}\"");

        return $"{CorrectionBlockHeader}\n{string.Join('\n', lines)}";
    }

    public string Replace(string text)
    {
        return PhraseReplacer.Replace(text, Read());
    }

    private static CustomDictionaryEntry? Normalize(CustomDictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.From) || string.IsNullOrWhiteSpace(entry.To))
        {
            return null;
        }

        return new CustomDictionaryEntry
        {
            Id = entry.Id,
            From = entry.From.Trim(),
            To = entry.To.Trim(),
            Enabled = entry.Enabled,
        };
    }

    private List<CustomDictionaryEntry> Read()
    {
        var contents = _fileStore.ReadAllTextOrNull(_paths.DictionaryFilePath);

        if (string.IsNullOrWhiteSpace(contents))
        {
            return [];
        }

        try
        {
            var entries = JsonSerializer.Deserialize(contents, SpatJson.DictionaryInfo) ?? [];

            return [.. entries.Select(Normalize).OfType<CustomDictionaryEntry>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAsync(List<CustomDictionaryEntry> entries, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();

        var contents = JsonSerializer.Serialize(entries, SpatJson.DictionaryInfo);
        await _fileStore.WriteAllTextAsync(_paths.DictionaryFilePath, contents, cancellationToken);
        _fileStore.RestrictToOwner(_paths.DictionaryFilePath);
    }
}

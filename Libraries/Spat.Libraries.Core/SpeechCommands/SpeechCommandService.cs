using System.Text;
using System.Text.Json;
using Spat.Libraries.Core.Platform;
using Spat.Libraries.Core.Serialization;
using Spat.Libraries.Core.Settings;

namespace Spat.Libraries.Core.SpeechCommands;

public interface ISpeechCommandService
{
    /// <summary>
    /// The shipped commands, each carrying the enabled state from settings.
    /// </summary>
    IReadOnlyList<SpeechCommand> GetBuiltIns();

    /// <summary>
    /// The user's own commands, in stored order.
    /// </summary>
    IReadOnlyList<SpeechCommand> GetCustom();

    /// <summary>
    /// Replaces the user's commands in one write, which is how the page saves its rows.
    /// </summary>
    Task SaveAllAsync(IReadOnlyList<SpeechCommand> commands, CancellationToken cancellationToken = default);

    /// <summary>
    /// The enabled commands as an instruction block for the text model, or null when none are enabled.
    /// </summary>
    string? BuildInstructionBlock();
}

public sealed class SpeechCommandService : ISpeechCommandService
{
    private const string BlockHeader =
        "The speaker can wrap words in a command by saying the command's open phrase, the words, and "
        + "then its close phrase. Apply the command to the enclosed words, and leave both phrases out "
        + "of the result. Use a command only when the surrounding words make that intent clear, never "
        + "as ordinary words.";

    private readonly IFileStore _fileStore;
    private readonly IPlatformPaths _paths;
    private readonly ISettingsService _settings;

    public SpeechCommandService(IPlatformPaths paths, IFileStore fileStore, ISettingsService settings)
    {
        _paths = paths;
        _fileStore = fileStore;
        _settings = settings;
    }

    public IReadOnlyList<SpeechCommand> GetBuiltIns()
    {
        var disabled = _settings.Current.SpeechCommands.DisabledBuiltInIds;
        var builtIns = BuiltInSpeechCommands.All;

        foreach (var command in builtIns)
        {
            command.Enabled = !disabled.Contains(command.Id);
        }

        return builtIns;
    }

    public IReadOnlyList<SpeechCommand> GetCustom()
    {
        return Read();
    }

    public async Task SaveAllAsync(IReadOnlyList<SpeechCommand> commands, CancellationToken cancellationToken = default)
    {
        // The shipped commands are never written to the file, so a caller that hands back everything it
        // read cannot freeze a copy of them into storage.
        var normalized = commands
            .Where(command => !command.IsBuiltIn)
            .Select(Normalize)
            .OfType<SpeechCommand>()
            .ToList();

        await WriteAsync(normalized, cancellationToken);
    }

    public string? BuildInstructionBlock()
    {
        var commands = GetBuiltIns()
            .Concat(GetCustom())
            .Where(command => command.Enabled && IsComplete(command))
            .ToList();

        if (commands.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder(BlockHeader);

        foreach (var command in commands)
        {
            var endPhrases = string.Join(" or ", command.EndPhrases.Select(phrase => $"\"{phrase}\""));

            builder.Append('\n')
                .Append("- Opens with \"").Append(command.StartPhrase).Append("\" and closes with ")
                .Append(endPhrases)
                .Append(". ")
                .Append(command.Instructions);
        }

        return builder.ToString();
    }

    private static bool IsComplete(SpeechCommand command)
    {
        return !string.IsNullOrWhiteSpace(command.StartPhrase)
            && !string.IsNullOrWhiteSpace(command.Instructions)
            && command.EndPhrases.Any(phrase => !string.IsNullOrWhiteSpace(phrase));
    }

    // A blank or half-filled row is dropped at save, the same way a dictionary row is.
    private static SpeechCommand? Normalize(SpeechCommand command)
    {
        var startPhrase = command.StartPhrase.Trim();
        var instructions = command.Instructions.Trim();
        var endPhrases = command.EndPhrases
            .Select(phrase => phrase.Trim())
            .Where(phrase => phrase.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (startPhrase.Length == 0 || instructions.Length == 0 || endPhrases.Count == 0)
        {
            return null;
        }

        return new SpeechCommand
        {
            Id = command.Id,
            StartPhrase = startPhrase,
            EndPhrases = endPhrases,
            Instructions = instructions,
            Enabled = command.Enabled,
        };
    }

    private List<SpeechCommand> Read()
    {
        var contents = _fileStore.ReadAllTextOrNull(_paths.SpeechCommandsFilePath);

        if (string.IsNullOrWhiteSpace(contents))
        {
            return [];
        }

        try
        {
            var commands = JsonSerializer.Deserialize(contents, SpatJson.SpeechCommandsInfo) ?? [];

            return [.. commands.Where(command => !command.IsBuiltIn).Select(Normalize).OfType<SpeechCommand>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAsync(List<SpeechCommand> commands, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();

        var contents = JsonSerializer.Serialize(commands, SpatJson.SpeechCommandsInfo);
        await _fileStore.WriteAllTextAsync(_paths.SpeechCommandsFilePath, contents, cancellationToken);
        _fileStore.RestrictToOwner(_paths.SpeechCommandsFilePath);
    }
}

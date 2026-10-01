using System.Diagnostics.CodeAnalysis;

namespace Spat.Libraries.Core.Platform;

public sealed class PlatformPaths : IPlatformPaths
{
    private const string AppFolderName = "spat";

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");

    public string PromptsFilePath => Path.Combine(ConfigDirectory, "prompts.json");

    public string DictionaryFilePath => Path.Combine(ConfigDirectory, "dictionary.json");

    public string HistoryFilePath => Path.Combine(DataDirectory, "history.json");

    public string AudioDirectory => Path.Combine(DataDirectory, "audio");

    public string UpdateStagingDirectory => Path.Combine(Path.GetTempPath(), AppFolderName, "update");

    public PlatformPaths(IEnvironmentVariables? environment = null)
    {
        var env = environment ?? new ProcessEnvironmentVariables();

        var configRoot = FirstNonEmpty(
            env.Get("APPDATA"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        var dataRoot = FirstNonEmpty(
            env.Get("LOCALAPPDATA"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        ConfigDirectory = Path.Combine(configRoot, AppFolderName);
        DataDirectory = Path.Combine(dataRoot, AppFolderName);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AudioDirectory);
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to resolve a per-user directory for Spat data.");
    }
}

[ExcludeFromCodeCoverage]
public sealed class ProcessEnvironmentVariables : IEnvironmentVariables
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}

public interface IEnvironmentVariables
{
    string? Get(string name);
}

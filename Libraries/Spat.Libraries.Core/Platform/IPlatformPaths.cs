namespace Spat.Libraries.Core.Platform;

public interface IPlatformPaths
{
    string ConfigDirectory { get; }

    string DataDirectory { get; }

    string SettingsFilePath { get; }

    string PromptsFilePath { get; }

    string DictionaryFilePath { get; }

    string HistoryFilePath { get; }

    string AudioDirectory { get; }

    /// <summary>
    /// Where a downloaded update binary waits before it replaces the running one.
    /// </summary>
    string UpdateStagingDirectory { get; }

    void EnsureDirectories();
}

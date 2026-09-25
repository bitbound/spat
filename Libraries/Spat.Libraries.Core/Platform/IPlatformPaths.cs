namespace Spat.Libraries.Core.Platform;

public interface IPlatformPaths
{
    string ConfigDirectory { get; }

    string DataDirectory { get; }

    string SettingsFilePath { get; }

    string PromptsFilePath { get; }

    string HistoryFilePath { get; }

    string AudioDirectory { get; }

    void EnsureDirectories();
}

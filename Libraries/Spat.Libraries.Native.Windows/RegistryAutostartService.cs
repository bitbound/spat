using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Spat.Libraries.Core.Platform;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Registers the running executable under the per-user Run key so Spat starts at sign-in.
/// </summary>
public sealed class RegistryAutostartService(ILogger<RegistryAutostartService> logger) : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Spat";

    private readonly ILogger<RegistryAutostartService> _logger = logger;

    public bool IsEnabled()
    {
        var executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);

        return string.Equals(key?.GetValue(ValueName) as string, Quote(executable), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The running executable path could not be determined.");

            key.SetValue(ValueName, Quote(executable));
            _logger.LogInformation("Enabled start-on-login for {Executable}.", executable);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger.LogInformation("Disabled start-on-login.");
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}

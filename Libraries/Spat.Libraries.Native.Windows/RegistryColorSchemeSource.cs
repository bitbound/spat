using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Spat.Libraries.Core.Theming;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Spat.Libraries.Native.Windows;

/// <summary>
/// Follows the Windows AppsUseLightTheme value, which is what applications are expected to honor for
/// the system color scheme, and watches the registry key for live changes.
/// </summary>
public sealed class RegistryColorSchemeSource : ISystemColorSchemeSource, IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly ILogger<RegistryColorSchemeSource> _logger;
    private readonly Thread _watcher;
    private readonly ManualResetEvent _change = new(false);

    private volatile bool _stopRequested;
    private ColorSchemePreference _current;

    public RegistryColorSchemeSource(ILogger<RegistryColorSchemeSource> logger)
    {
        _logger = logger;
        _current = Read();

        _watcher = new Thread(Watch)
        {
            IsBackground = true,
            Name = "Spat-ColorSchemeWatcher",
        };
        _watcher.Start();
    }

    public ColorSchemePreference Current => _current;

    public event EventHandler<ColorSchemePreference>? Changed;

    public void Dispose()
    {
        _stopRequested = true;
        _change.Set();
        _watcher.Join(TimeSpan.FromSeconds(2));
    }

    private void Watch()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);

        if (key is null)
        {
            _logger.LogWarning("The themes personalize key is missing, so color scheme changes are not tracked.");
            return;
        }

        // RegNotifyChangeKeyValue with a wait event instead of a registry callback keeps this watcher
        // allocation free and easy to shut down.
        while (!_stopRequested)
        {
            _change.Reset();

            unsafe
            {
                var result = SpatWin32.RegNotifyChangeKeyValue(
                    new HKEY((void*)key.Handle.DangerousGetHandle()),
                    bWatchSubtree: false,
                    dwNotifyFilter: REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET,
                    hEvent: new HANDLE((void*)_change.SafeWaitHandle.DangerousGetHandle()),
                    fAsynchronous: true);

                if ((uint)result != 0)
                {
                    _logger.LogWarning("RegNotifyChangeKeyValue failed with code {Code}; color scheme tracking stopped.", (uint)result);
                    return;
                }
            }

            // The bounded wait lets Dispose unblock the loop through _stopRequested without a second handle.
            if (_change.WaitOne(TimeSpan.FromMilliseconds(500)) && !_stopRequested)
            {
                var scheme = Read();

                if (scheme != _current)
                {
                    _current = scheme;
                    Changed?.Invoke(this, scheme);
                }
            }
        }
    }

    private static ColorSchemePreference Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as int?;

        return value switch
        {
            null => ColorSchemePreference.Unset,
            0 => ColorSchemePreference.Dark,
            _ => ColorSchemePreference.Light,
        };
    }
}

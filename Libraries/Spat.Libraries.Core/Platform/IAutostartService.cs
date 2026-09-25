namespace Spat.Libraries.Core.Platform;

/// <summary>
/// Keeps the app launching when the user signs in to Windows, through the per-user Run key.
/// </summary>
public interface IAutostartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

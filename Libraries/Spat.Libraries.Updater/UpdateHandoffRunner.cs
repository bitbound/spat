using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Spat.Libraries.Core.Updates;

namespace Spat.Libraries.Updater;

/// <summary>
/// Runs inside a freshly downloaded copy to stop the outdated process, replace its binary, and relaunch it.
/// </summary>
public sealed class UpdateHandoffRunner(ILogger<UpdateHandoffRunner> logger)
{
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan SwapRetryDelay = TimeSpan.FromMilliseconds(500);

    private const int SwapAttempts = 6;

    public bool IsRequested(string[] args)
    {
        return UpdateHandoff.TryParse(args) is not null;
    }

    public async Task<bool> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var handoff = UpdateHandoff.TryParse(args);

        if (handoff is null)
        {
            logger.LogWarning("An update handoff was requested but its arguments are incomplete.");
            return false;
        }

        var target = handoff.OutdatedExecutablePath;
        var stagedExecutable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(stagedExecutable))
        {
            logger.LogError("Could not determine the path of the downloaded executable.");
            return false;
        }

        // The rename source. It is only moved over the target once it is fully written, so it only needs
        // cleanup on failure.
        var stagingCopy = target + ".new";
        var backupCopy = target + ".old";

        try
        {
            await StopOutdatedProcess(handoff.OutdatedProcessId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Windows refuses to delete or overwrite the image file of a process, even a dead one whose
            // handles have not fully closed. So stage a complete copy beside the target, then swap the
            // names: renaming the old binary out of the way is permitted, and moving the staged copy in
            // only runs after that succeeds. Any earlier failure leaves a working binary in place.
            File.Copy(stagedExecutable, stagingCopy, overwrite: true);

            SwapBinaries(target, stagingCopy, backupCopy);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("The Spat update handoff was cancelled.");
            TryDelete(stagingCopy);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to install the Spat update at {Target}.", target);
            TryDelete(stagingCopy);
            return false;
        }

        // The target now holds the new binary. Relaunch it normally so the user keeps running the app.
        if (!TryRelaunch(target, args))
        {
            TryDelete(stagedExecutable);
            return false;
        }

        TryDelete(stagedExecutable);
        TryDelete(backupCopy);

        return true;
    }

    private void SwapBinaries(string target, string stagingCopy, string backupCopy)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // A leftover backup from an interrupted update would block the rename below.
                TryDelete(backupCopy);

                if (File.Exists(target))
                {
                    File.Move(target, backupCopy);
                }

                File.Move(stagingCopy, target);

                return;
            }
            catch (Exception exception) when (attempt < SwapAttempts
                                             && exception is IOException or UnauthorizedAccessException)
            {
                // The outdated process's image handle can linger briefly after exit, and security scanners
                // open new executables on creation. Put the target back and retry.
                logger.LogWarning(
                    exception,
                    "Could not swap the update into {Target} (attempt {Attempt}).",
                    target,
                    attempt);

                RestoreIfSwapped(target, backupCopy);
                Thread.Sleep(SwapRetryDelay);
            }
        }
    }

    private void RestoreIfSwapped(string target, string backupCopy)
    {
        try
        {
            if (!File.Exists(target) && File.Exists(backupCopy))
            {
                File.Move(backupCopy, target);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not restore the previous binary at {Target}.", target);
        }
    }

    private async Task StopOutdatedProcess(int processId, CancellationToken cancellationToken)
    {
        Process process;

        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            logger.LogDebug("Outdated process {ProcessId} has already exited.", processId);
            return;
        }

        using (process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: false);
                }
            }
            catch (InvalidOperationException)
            {
                logger.LogDebug("Outdated process {ProcessId} exited before it could be stopped.", processId);
                return;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(KillTimeout, CancellationToken.None);
            }
            catch (TimeoutException)
            {
                // The swap retries against the still-held image handle, so continue regardless.
                logger.LogWarning("Outdated process {ProcessId} did not exit within {Timeout}.", processId, KillTimeout);
            }
        }
    }

    private bool TryRelaunch(string target, string[] args)
    {
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
        };

        foreach (var argument in HandoffArguments.Strip(args))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            Process.Start(startInfo);

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogError(exception, "The update was installed but {Target} could not be relaunched.", target);

            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of staging files; the update result does not depend on it.
        }
    }
}

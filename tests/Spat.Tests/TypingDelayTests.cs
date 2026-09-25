using Spat.Libraries.Core.Settings;
using Spat.Libraries.Native.Windows;

namespace Spat.Tests;

/// <summary>
/// The key delay is applied between the press and the release of every injected character. Making it
/// too small puts both events in the same input frame, and then some applications drop the character
/// entirely. That is not a degraded result, it is a total failure, so the bounds matter.
/// </summary>
public class TypingDelayTests
{
    [Fact]
    public void DefaultTypingDelay_IsTwoMilliseconds()
    {
        // SendInput delivers press and release as separate events, so a small delay is enough for
        // even slow applications to observe both.
        Assert.Equal(2, AppSettings.DefaultTypingDelayMs);
    }

    [Fact]
    public void ResolveDelay_WithTheDefault_PassesItThrough()
    {
        Assert.Equal(AppSettings.DefaultTypingDelayMs, WindowsTextInputInjector.ResolveDelay(AppSettings.DefaultTypingDelayMs));
    }

    [Fact]
    public void ResolveDelay_WithZero_ClampsToOne()
    {
        // Zero would remove the pause entirely, which can type nothing in slow targets.
        Assert.Equal(1, WindowsTextInputInjector.ResolveDelay(0));
    }

    [Fact]
    public void ResolveDelay_WithANegativeValue_ClampsToOne()
    {
        Assert.Equal(1, WindowsTextInputInjector.ResolveDelay(-25));
    }

    [Fact]
    public void ResolveDelay_WithAnAbsurdlyLargeValue_ClampsToOneHundred()
    {
        Assert.Equal(100, WindowsTextInputInjector.ResolveDelay(100_000));
    }

    [Fact]
    public void ResolveDelay_WithAValueInRange_IsLeftAlone()
    {
        Assert.Equal(37, WindowsTextInputInjector.ResolveDelay(37));
    }

    [Fact]
    public void DefaultTypingDelay_IsWithinTheResolvedRange()
    {
        Assert.Equal(AppSettings.DefaultTypingDelayMs, WindowsTextInputInjector.ResolveDelay(AppSettings.DefaultTypingDelayMs));
    }
}

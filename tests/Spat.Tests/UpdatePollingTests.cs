using Microsoft.Extensions.Logging.Abstractions;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.Updates;
using Spat.Services;

namespace Spat.Tests;

public class UpdatePollingServiceTests
{
    [Fact]
    public void Interval_IsTwoDays()
    {
        Assert.Equal(TimeSpan.FromDays(2), UpdatePollingService.Interval);
    }

    [Fact]
    public async Task PollAsync_WithChecksEnabled_RunsACheck()
    {
        var updates = new FakeUpdateService();
        var service = Create(updates, checkForUpdates: true);

        await service.PollAsync();

        Assert.Equal(1, updates.CheckCount);
    }

    [Fact]
    public async Task PollAsync_WithChecksDisabled_SkipsTheCheck()
    {
        var updates = new FakeUpdateService();
        var service = Create(updates, checkForUpdates: false);

        await service.PollAsync();

        Assert.Equal(0, updates.CheckCount);
    }

    [Fact]
    public async Task PollAsync_WhenAnUpdateIsAlreadyKnown_SkipsTheCheck()
    {
        var updates = new FakeUpdateService
        {
            AvailableUpdate = new UpdateInfo("v9.9.9", "spat.exe", "https://downloads.example.test/spat.exe"),
        };

        var service = Create(updates, checkForUpdates: true);

        await service.PollAsync();

        Assert.Equal(0, updates.CheckCount);
    }

    [Fact]
    public async Task PollAsync_WhenTheSettingIsTurnedOffBetweenPasses_StopsChecking()
    {
        var updates = new FakeUpdateService();
        var settings = TestSettings.Create(new InMemoryFileStore(), new AppSettings { CheckForUpdates = true });
        var service = Create(updates, settings);

        await service.PollAsync();

        settings.Current.CheckForUpdates = false;
        await service.PollAsync();

        Assert.Equal(1, updates.CheckCount);
    }

    [Fact]
    public async Task PollAsync_WhenTheCheckThrows_DoesNotPropagate()
    {
        var updates = new FakeUpdateService { Throw = new HttpRequestException("no route to host") };
        var service = Create(updates, checkForUpdates: true);

        await service.PollAsync();

        Assert.Equal(1, updates.CheckCount);
    }

    private static UpdatePollingService Create(FakeUpdateService updates, bool checkForUpdates) =>
        Create(updates, TestSettings.Create(new InMemoryFileStore(), new AppSettings { CheckForUpdates = checkForUpdates }));

    private static UpdatePollingService Create(FakeUpdateService updates, ISettingsService settings) =>
        new(updates, settings, TimeProvider.System, NullLogger<UpdatePollingService>.Instance);
}

using Spat.Libraries.Core.Updates;
using Spat.ViewModels;

namespace Spat.Tests;

public class AboutViewModelTests
{
    private static readonly UpdateInfo SampleUpdate =
        new("v2.0.0", "spat.exe", "https://downloads.example.test/spat.exe");

    [Fact]
    public async Task Command_WhenNoUpdateIsKnown_ChecksAndReportsUpToDate()
    {
        var updates = new FakeUpdateService();
        var viewModel = new AboutViewModel(new FakeAppInfo(), updates);

        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.CheckCount);
        Assert.Equal(0, updates.ApplyCount);
        Assert.True(viewModel.IsUpToDate);
    }

    [Fact]
    public async Task Command_WhenAnUpdateIsFound_ReportsItWithoutApplying()
    {
        var updates = new FakeUpdateService { Result = UpdateCheckResult.Available(SampleUpdate) };
        var viewModel = new AboutViewModel(new FakeAppInfo(), updates);

        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal(0, updates.ApplyCount);
    }

    [Fact]
    public async Task Command_WhenAnUpdateIsAlreadyKnown_AppliesItInsteadOfCheckingAgain()
    {
        var updates = new FakeUpdateService { Result = UpdateCheckResult.Available(SampleUpdate) };
        var viewModel = new AboutViewModel(new FakeAppInfo(), updates);

        // First press finds the release, second press installs it.
        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);
        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.CheckCount);
        Assert.Equal(1, updates.ApplyCount);
    }

    [Fact]
    public async Task Command_WhenCheckingFails_ReportsFailure()
    {
        var updates = new FakeUpdateService { Throw = new HttpRequestException("no route to host") };
        var viewModel = new AboutViewModel(new FakeAppInfo(), updates);

        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUpdateCheckFailed);
    }

    [Fact]
    public async Task Command_WhenApplyingFails_ReportsFailure()
    {
        var updates = new FakeUpdateService
        {
            Result = UpdateCheckResult.Available(SampleUpdate),
            ThrowOnApply = new IOException("the download was cut off"),
        };

        var viewModel = new AboutViewModel(new FakeAppInfo(), updates);

        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);
        await viewModel.CheckOrApplyUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.ApplyCount);
        Assert.True(viewModel.HasUpdateCheckFailed);
    }
}

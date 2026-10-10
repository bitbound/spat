using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Spat.Libraries.Core.Updates;
using Spat.Libraries.Updater;

namespace Spat.Tests;

public class GitHubReleaseUpdateServiceTests
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/TestOrg/Spat/releases/latest";

    private static string ExpectedAssetName => ReleaseAssetSelector.AssetName;

    [Fact]
    public async Task CheckAsync_WhenReleaseIsNewer_ReturnsTheAssetAndRaisesTheEventOnce()
    {
        var (service, handler) = Create(Release("v1.5.0", withAsset: true));
        var raised = 0;
        service.UpdateAvailable += (_, _) => raised++;
        var ct = TestContext.Current.CancellationToken;

        var result = await service.CheckAsync(ct);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal($"https://downloads.example.test/{ExpectedAssetName}", result.Update?.DownloadUrl);
        Assert.Equal(ExpectedAssetName, result.Update?.AssetName);
        Assert.Equal("v1.5.0", result.Update?.Version);
        Assert.Equal(result.Update, service.AvailableUpdate);

        await service.CheckAsync(ct);

        Assert.Equal(1, raised);
        Assert.Equal(LatestReleaseUrl, handler.RequestUris[0]);
    }

    [Theory]
    [InlineData("v0.9.9")]
    [InlineData("v1.0.0")]
    public async Task CheckAsync_WhenReleaseIsNotNewer_ReportsUpToDate(string tag)
    {
        var (service, _) = Create(Release(tag, withAsset: true));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
        Assert.Null(service.AvailableUpdate);
    }

    [Fact]
    public async Task CheckAsync_WhenSameDayReleaseHasALaterTimeRevision_ReturnsAnUpdate()
    {
        // Releases are tagged yyyy.M.d.HHmm, so a second build the same day must still compare as newer.
        var (service, _) = Create(
            Release("v2026.9.26.1954", withAsset: true),
            currentVersion: new Version(2026, 9, 26, 1900));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("v2026.9.26.1954", result.Update?.Version);
    }

    [Fact]
    public async Task CheckAsync_WhenSameDayReleaseHasAZeroPaddedEarlierRevision_ReportsUpToDate()
    {
        var (service, _) = Create(
            Release("v2026.9.26.0244", withAsset: true),
            currentVersion: new Version(2026, 9, 26, 1900));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task CheckAsync_WhenTagIsNotAVersion_ReportsUpToDate()
    {
        var (service, _) = Create(Release("nightly-build", withAsset: true));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task CheckAsync_WhenReleaseHasNoUsableAsset_ReportsUpToDate()
    {
        var (service, _) = Create(Release("v2.0.0", withAsset: false));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task CheckAsync_WhenGitHubReturnsAnError_ReportsFailure()
    {
        var (service, _) = Create(_ => StubHttpMessageHandler.Json("""{"message":"server error"}""", HttpStatusCode.InternalServerError));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_WhenGitHubIsUnreachable_ReportsFailure()
    {
        var (service, _) = Create(_ => throw new HttpRequestException("no route to host"));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task CheckAsync_WhenReleaseOnlyHasTheUnsuffixedAsset_StillReturnsAnUpdate()
    {
        var (service, _) = Create(ReleaseWithAssets("v1.5.0", "spat"));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("spat", result.Update?.AssetName);
        Assert.Equal("https://downloads.example.test/spat", result.Update?.DownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_WhenBothNamesExist_PrefersTheArchitectureSpecificAsset()
    {
        var (service, _) = Create(ReleaseWithAssets("v1.5.0", "spat", ExpectedAssetName));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedAssetName, result.Update?.AssetName);
    }

    [Fact]
    public async Task CheckAsync_WhenNameDiffersOnlyByCase_ReturnsAnUpdate()
    {
        // Releases used to publish the asset with a capital letter, and the old lookup compared with
        // StringComparison.Ordinal, so it never matched.
        var legacySpelling = char.ToUpperInvariant(ExpectedAssetName[0]) + ExpectedAssetName[1..];
        var (service, _) = Create(ReleaseWithAssets("v1.5.0", legacySpelling));

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(legacySpelling, result.Update?.AssetName);
    }

    private static (GitHubReleaseUpdateService Service, StubHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Version? currentVersion = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var service = new GitHubReleaseUpdateService(
            new HttpClient(handler),
            new FakeAppInfo(currentVersion),
            new TestPlatformPaths(),
            NullLogger<GitHubReleaseUpdateService>.Instance);

        return (service, handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Release(string tag, bool withAsset) =>
        ReleaseWithAssets(tag, withAsset ? [ExpectedAssetName] : ["Spat-linux-x64.exe"]);

    private static Func<HttpRequestMessage, HttpResponseMessage> ReleaseWithAssets(
        string tag,
        params string[] assetNames)
    {
        var assets = string.Join(
            ",",
            assetNames.Select(name =>
                $$"""{"name":"{{name}}","browser_download_url":"https://downloads.example.test/{{name}}","state":"uploaded"}"""));

        var payload = $$"""{"tag_name":"{{tag}}","assets":[{{assets}}]}""";

        return _ => StubHttpMessageHandler.Json(payload);
    }
}

public class UpdateHandoffTests
{
    [Fact]
    public void BuildArguments_RoundTripsThroughTryParse()
    {
        var handoff = new UpdateHandoff(@"C:\Users\user\AppData\Local\Programs\Spat\spat.exe", 4242);

        var parsed = UpdateHandoff.TryParse(UpdateHandoff.BuildArguments(handoff));

        Assert.Equal(handoff, parsed);
    }

    [Fact]
    public void TryParse_WithoutHandoffArguments_ReturnsNull()
    {
        Assert.Null(UpdateHandoff.TryParse(["--flag", "value"]));
        Assert.Null(UpdateHandoff.TryParse([]));
    }

    [Fact]
    public void TryParse_WithUnreadableProcessId_ReturnsNull()
    {
        var args = new[]
        {
            UpdateHandoff.TargetArgument,
            @"C:\Users\user\spat.exe",
            UpdateHandoff.ProcessIdArgument,
            "not-a-pid",
        };

        Assert.Null(UpdateHandoff.TryParse(args));
    }
}

public class ReleaseAssetSelectorTests
{
    [Theory]
    [InlineData(Architecture.X64, "spat-x64.exe")]
    [InlineData(Architecture.Arm64, "spat-arm64.exe")]
    public void BuildAssetName_ForASupportedArchitecture_AppendsThePlatformSuffix(
        Architecture architecture,
        string expected)
    {
        Assert.Equal(expected, ReleaseAssetSelector.BuildAssetName(architecture));
    }

    [Fact]
    public void BuildCandidateNames_PrefersTheSuffixedAssetAndKeepsTheBareOneAsFallback()
    {
        Assert.Equal(
            new[] { "spat-arm64.exe", "spat" },
            ReleaseAssetSelector.BuildCandidateNames(Architecture.Arm64));
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    public void BuildCandidateNames_ForAnUnsupportedArchitecture_IsTheBareName(Architecture architecture)
    {
        Assert.Equal(new[] { "spat" }, ReleaseAssetSelector.BuildCandidateNames(architecture));
        Assert.Equal("spat", ReleaseAssetSelector.BuildAssetName(architecture));
    }
}

public class ReleaseWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_PublishesTheAssetNameTheUpdaterLooksFor()
    {
        var workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release.yml"));

        // The name lives in two places that cannot see each other, so pin them together here.
        Assert.Contains($"ASSET_NAME: {ReleaseAssetSelector.AssetName}", workflow, StringComparison.Ordinal);
        Assert.Contains("./artifacts/$env:ASSET_NAME", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_TagsVersionsWithDateAndTimeSoSameDayReleasesDoNotCollide()
    {
        var workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release.yml"));

        // A date-only tag (yyyy.M.d) made a second release on the same day fail with
        // "a release with the same tag name already exists".
        Assert.Contains("yyyy.M.d.HHmm", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spat.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root above the test binaries.");
    }
}

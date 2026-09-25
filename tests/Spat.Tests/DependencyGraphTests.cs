using Microsoft.Extensions.DependencyInjection;
using Spat.Startup;

namespace Spat.Tests;

public class DependencyGraphTests
{
    [Fact]
    public void AddSpat_BuildsTheProviderWithoutCircularDependencies()
    {
        var services = new ServiceCollection();

        services.AddSpat();

        // Validating on build walks every constructor, so a registration cycle fails here instead of on first resolve.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider);
    }
}

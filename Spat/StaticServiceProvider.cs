using Spat.Startup;

namespace Spat;

internal static class StaticServiceProvider
{
    private static ServiceProvider? _provider;

    public static IServiceProvider Instance =>
        _provider ?? throw new InvalidOperationException("The service provider has not been built yet.");

    public static void Build()
    {
        if (_provider is not null)
        {
            return;
        }

        var services = new ServiceCollection();

        services.AddSpat();

        _provider = services.BuildServiceProvider();
    }
}

using MMONavigator.Interfaces;
using MMONavigator.Models;

namespace MMONavigator.Services;

public class LocationProviderFactory
{
    private readonly IEnumerable<ILocationProvider> _providers;

    public LocationProviderFactory(IEnumerable<ILocationProvider> providers)
    {
        _providers = providers;
    }

    public ILocationProvider GetProvider(WatchMode mode) => mode switch
    {
        WatchMode.Clipboard => _providers.OfType<ClipboardLocationProvider>().First(),
        WatchMode.File   => _providers.OfType<LogFileLocationProvider>().First(),
        WatchMode.SharedMemory => _providers.OfType<SharedMemoryLocationProvider>().First(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
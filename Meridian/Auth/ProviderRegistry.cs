namespace Meridian.Auth;

// Singleton registry — providers are registered at app startup, then looked up by name.
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, ICalendarProvider> _providers = [];

    public void Register(ICalendarProvider provider) =>
        _providers[provider.ProviderName] = provider;

    public ICalendarProvider Get(string providerName) =>
        _providers.TryGetValue(providerName, out var p)
            ? p
            : throw new InvalidOperationException($"No provider registered for '{providerName}'");

    public ICalendarProvider Get(AccountId id) => Get(id.Provider);

    public IEnumerable<ICalendarProvider> All => _providers.Values;
}

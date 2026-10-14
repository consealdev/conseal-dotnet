using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Endpoints;
using ConsealDev.ConsealSync.Hooks;
using ConsealDev.ConsealSync.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Extensions;

/// <summary>Registers Conseal Sync with the host app's services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sync and circle routes' services, with options bound from the <c>ConsealSync</c> section and
    /// checked at startup. Chain <c>AddDynamoStores</c> or <c>AddInMemoryStores</c>, and the collection ids with
    /// <c>WithCollections</c>. Authentication stays the host's: the routes read the user from its sign-in.
    /// </summary>
    /// <param name="services">The host app's services.</param>
    /// <param name="configuration">The configuration holding the <c>ConsealSync</c> section.</param>
    /// <returns>A builder to chain the stores, collections and hooks on.</returns>
    public static ConsealSyncBuilder AddConsealSync(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ConsealSyncOptions>(configuration.GetSection(ConsealSyncOptions.SectionName));
        services.AddSingleton<IValidateOptions<ConsealSyncOptions>, ConsealSyncOptionsValidator>();
        services.AddOptions<ConsealSyncOptions>().ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SyncCursor>();
        services.TryAddScoped<IConsealSyncHooks, NullConsealSyncHooks>();
        services.TryAddScoped<SyncRequestHandler>();
        services.TryAddScoped<CircleRequestHandler>();

        return new ConsealSyncBuilder(services);
    }
}

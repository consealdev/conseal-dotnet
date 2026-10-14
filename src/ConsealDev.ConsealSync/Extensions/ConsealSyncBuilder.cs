using Amazon.DynamoDBv2;
using Amazon.Runtime;
using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Hooks;
using ConsealDev.ConsealSync.Storage;
using ConsealDev.ConsealSync.Storage.Dynamo;
using ConsealDev.ConsealSync.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Extensions;

/// <summary>Returned by <c>AddConsealSync</c>, for chaining the collections, the stores and the hooks.</summary>
/// <param name="services">The host app's services.</param>
public sealed class ConsealSyncBuilder(IServiceCollection services)
{
    /// <summary>The host app's services.</summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Adds collection ids the server accepts, beside any in <c>ConsealSync:Collections</c>: the ids from the app's
    /// collection registry, never its collection names.
    /// </summary>
    /// <param name="collectionIds">The ids, each matching <c>^[A-Za-z0-9_-]{1,64}$</c>.</param>
    /// <returns>The builder.</returns>
    public ConsealSyncBuilder WithCollections(params string[] collectionIds)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);
        Services.Configure<ConsealSyncOptions>(options => options.Collections.AddRange(collectionIds));
        return this;
    }

    /// <summary>Registers the app's hooks in place of the default, which does nothing. They are resolved per request.</summary>
    /// <typeparam name="T">The app's hooks.</typeparam>
    /// <returns>The builder.</returns>
    public ConsealSyncBuilder WithHooks<T>() where T : class, IConsealSyncHooks
    {
        Services.Replace(ServiceDescriptor.Scoped<IConsealSyncHooks, T>());
        return this;
    }

    /// <summary>
    /// Stores records and circles in DynamoDB, in the two tables <see cref="ConsealSyncDynamoTables"/> describes. An
    /// <see cref="IAmazonDynamoDB"/> the app has registered already is used; otherwise one is made from the options,
    /// with credentials from the standard AWS chain.
    /// </summary>
    /// <param name="configure">Sets the table prefix, and the endpoint for DynamoDB Local.</param>
    /// <returns>The builder.</returns>
    public ConsealSyncBuilder AddDynamoStores(Action<ConsealSyncDynamoOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);

        Services.TryAddSingleton<IAmazonDynamoDB>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ConsealSyncDynamoOptions>>().Value;
            if (string.IsNullOrEmpty(options.ServiceUrl)) return new AmazonDynamoDBClient();

            // DynamoDB Local doesn't check credentials.
            return new AmazonDynamoDBClient(
                new BasicAWSCredentials("local", "local"),
                new AmazonDynamoDBConfig { ServiceURL = options.ServiceUrl });
        });
        Services.Replace(ServiceDescriptor.Singleton<IConsealSyncRecordStore, DynamoRecordStore>());
        Services.Replace(ServiceDescriptor.Singleton<IConsealCircleStore, DynamoCircleStore>());
        return this;
    }

    /// <summary>
    /// Stores records and circles in memory, lost when the process ends: for tests and local development only. The
    /// record limit is DynamoDB's, so an app tested against memory keeps to what the DynamoDB store accepts.
    /// </summary>
    /// <returns>The builder.</returns>
    public ConsealSyncBuilder AddInMemoryStores()
    {
        Services.Replace(ServiceDescriptor.Singleton<IConsealSyncRecordStore, InMemoryRecordStore>());
        Services.Replace(ServiceDescriptor.Singleton<IConsealCircleStore, InMemoryCircleStore>());
        return this;
    }
}

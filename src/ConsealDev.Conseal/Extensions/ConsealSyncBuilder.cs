// SPDX-License-Identifier: AGPL-3.0-or-later OR LicenseRef-Commercial

using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.S3;
using ConsealDev.Conseal.Configuration;
using ConsealDev.Conseal.Hooks;
using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Storage.Dynamo;
using ConsealDev.Conseal.Storage.InMemory;
using ConsealDev.Conseal.Storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ConsealDev.Conseal.Extensions;

/// <summary>Returned by <c>AddConsealSync</c>, for chaining the collections, the stores, the blobs and the hooks.</summary>
/// <param name="services">The host app's services.</param>
/// <param name="configuration">The configuration holding the <c>ConsealSync</c> section.</param>
public sealed class ConsealSyncBuilder(IServiceCollection services, IConfiguration configuration)
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
    /// Turns on the blob routes for the collections <paramref name="configure"/> names, with options bound from the
    /// <c>ConsealSync:Blobs</c> section and checked at startup. Blobs go in an S3 bucket, or an S3-compatible store,
    /// through presigned URLs. An <see cref="IAmazonS3"/> the app has registered already is used; otherwise one is made
    /// from the options, with credentials from the standard AWS chain. Without this, the blob routes answer
    /// <c>404 blobs_disabled</c> and nothing needs object storage.
    /// </summary>
    /// <remarks>
    /// It sets <see cref="Amazon.AWSConfigsS3.UseSignatureVersion4"/> for the process: the SDK presigns with Signature
    /// Version 2 in us-east-1 otherwise, and a version 2 URL doesn't sign an upload's size.
    /// </remarks>
    /// <param name="configure">Sets the collections, the bucket, and the endpoint for a local store.</param>
    /// <returns>The builder.</returns>
    public ConsealSyncBuilder WithBlobs(Action<ConsealSyncBlobOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure<ConsealSyncBlobOptions>(configuration.GetSection(ConsealSyncBlobOptions.SectionName));
        Services.Configure(configure);
        Services.AddSingleton<IValidateOptions<ConsealSyncBlobOptions>, ConsealSyncBlobOptionsValidator>();
        Services.AddOptions<ConsealSyncBlobOptions>().ValidateOnStart();

        Services.TryAddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ConsealSyncBlobOptions>>().Value;
            if (string.IsNullOrEmpty(options.ServiceUrl)) return new AmazonS3Client();
            return new AmazonS3Client(new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true });
        });
        Services.Replace(ServiceDescriptor.Singleton<IConsealSyncBlobStore, S3BlobStore>());
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

using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Extensions;
using ConsealDev.ConsealSync.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Tests.Configuration;

public sealed class ConsealSyncBlobOptionsValidatorTests
{
    private readonly ConsealSyncBlobOptionsValidator _validator =
        new(Options.Create(new ConsealSyncOptions { Collections = [TestHost.Notes, TestHost.Lists] }));

    [Fact]
    public void Valid_options_pass()
    {
        _validator.Validate(null, Valid()).Succeeded.Should().BeTrue();
    }

    public static TheoryData<string, Action<ConsealSyncBlobOptions>, string> Invalid => new()
    {
        { "no collections", o => o.Collections.Clear(), "no collections" },
        { "a collection the server doesn't accept", o => o.Collections.Add("2d81ff04"), "'2d81ff04' isn't one of ConsealSync's" },
        { "the keyring", o => o.Collections.Add("keyring"), "'keyring' isn't one of ConsealSync's" },
        { "a collection listed twice", o => o.Collections.Add(TestHost.Notes), "more than once" },
        { "no bucket", o => o.BucketName = " ", "BucketName" },
        { "a limit under an IV and a tag", o => o.MaxBlobBytes = 27, "MaxBlobBytes" },
        { "a limit over one PUT", o => o.MaxBlobBytes = 5L * 1024 * 1024 * 1024 + 1, "MaxBlobBytes" },
        { "URLs that never work", o => o.UrlLifetime = TimeSpan.Zero, "UrlLifetime" },
        { "URLs longer than SigV4 allows", o => o.UrlLifetime = TimeSpan.FromDays(8), "UrlLifetime" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_options_fail_with_a_message_naming_the_setting(string _, Action<ConsealSyncBlobOptions> spoil, string named)
    {
        var options = Valid();
        spoil(options);

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(named);
    }

    [Fact]
    public async Task A_host_whose_blobs_name_no_bucket_doesnt_start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConsealSync:CursorSigningKey"] = TestHost.CursorKey });
        builder.Services.AddConsealSync(builder.Configuration)
            .WithCollections(TestHost.Notes)
            .AddInMemoryStores()
            .WithBlobs(options => options.Collections.Add(TestHost.Notes));
        await using var app = builder.Build();

        var start = () => app.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*BucketName*");
    }

    [Fact]
    public async Task Blob_options_bind_from_the_ConsealSync_Blobs_section()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConsealSync:CursorSigningKey"] = TestHost.CursorKey,
            ["ConsealSync:Blobs:BucketName"] = "app-blobs",
            ["ConsealSync:Blobs:Collections:0"] = TestHost.Notes,
            ["ConsealSync:Blobs:MaxBlobBytes"] = "1048576",
        });
        builder.Services.AddConsealSync(builder.Configuration).WithCollections(TestHost.Notes).AddInMemoryStores().WithBlobs(_ => { });
        await using var app = builder.Build();

        var options = app.Services.GetRequiredService<IOptions<ConsealSyncBlobOptions>>().Value;

        options.BucketName.Should().Be("app-blobs");
        options.Collections.Should().Equal(TestHost.Notes);
        options.MaxBlobBytes.Should().Be(1048576);
    }

    private static ConsealSyncBlobOptions Valid() => new() { Collections = [TestHost.Notes], BucketName = "app-blobs" };
}

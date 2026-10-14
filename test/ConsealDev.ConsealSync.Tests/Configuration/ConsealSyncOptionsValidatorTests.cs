using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Endpoints;
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

public sealed class ConsealSyncOptionsValidatorTests
{
    private readonly ConsealSyncOptionsValidator _validator = new();

    [Fact]
    public void Valid_options_pass()
    {
        _validator.Validate(null, Valid()).Succeeded.Should().BeTrue();
    }

    public static TheoryData<string, Action<ConsealSyncOptions>, string> Invalid => new()
    {
        { "no collections", o => o.Collections.Clear(), "no collections" },
        { "a collection name", o => o.Collections.Add("my notes"), "'my notes'" },
        { "a collection id with a trailing newline", o => o.Collections.Add("7f3a9c22\n"), "doesn't match" },
        { "a collection listed twice", o => o.Collections.Add(TestHost.Notes), "more than once" },
        { "the reserved keyring", o => o.Collections.Add("keyring"), "'keyring' is reserved" },
        { "no cursor key", o => o.CursorSigningKey = "", "CursorSigningKey" },
        { "a 31-byte cursor key", o => o.CursorSigningKey = Convert.ToBase64String(new byte[31]), "CursorSigningKey" },
        { "a cursor key that isn't base64", o => o.CursorSigningKey = "not base64 at all, not even close....", "CursorSigningKey" },
        { "an empty user id claim", o => o.UserIdClaim = " ", "UserIdClaim" },
        { "a batch of nothing", o => o.MaxBatchItems = 0, "MaxBatchItems" },
        { "a page of nothing", o => o.MaxPageItems = 0, "MaxPageItems" },
        { "a request limit under 1 MB", o => o.MaxRequestBytes = 1024, "MaxRequestBytes" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_options_fail_with_a_message_naming_the_setting(string _, Action<ConsealSyncOptions> spoil, string named)
    {
        var options = Valid();
        spoil(options);

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(named);
    }

    [Fact]
    public async Task A_host_with_invalid_options_doesnt_start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddConsealSync(builder.Configuration).WithCollections(TestHost.Notes).AddInMemoryStores();
        await using var app = builder.Build();

        var start = () => app.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*CursorSigningKey*");
    }

    [Fact]
    public void Mapping_the_routes_without_stores_fails_and_says_what_to_call()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConsealSync:CursorSigningKey"] = TestHost.CursorKey });
        builder.Services.AddConsealSync(builder.Configuration).WithCollections(TestHost.Notes);
        var app = builder.Build();

        var map = () => app.MapConsealSyncRoutes();

        map.Should().Throw<InvalidOperationException>().WithMessage("*AddInMemoryStores*");
    }

    private static ConsealSyncOptions Valid() => new()
    {
        Collections = [TestHost.Notes, TestHost.Lists],
        CursorSigningKey = TestHost.CursorKey,
    };
}

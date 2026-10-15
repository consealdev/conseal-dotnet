using ConsealDev.Conseal.Endpoints;
using ConsealDev.Conseal.Extensions;
using ConsealDev.Conseal.Hooks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>An in-process host app with both route groups mapped, signing users in from a header.</summary>
internal sealed class TestHost : IAsyncDisposable
{
    /// <summary>32 bytes, 0 to 31.</summary>
    public const string CursorKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public const string Notes = "7f3a9c21";
    public const string Lists = "b4e70118";

    private readonly WebApplication _app;

    private TestHost(WebApplication app, TestTime time, RecordingHooks hooks)
    {
        _app = app;
        Time = time;
        Hooks = hooks;
    }

    public TestTime Time { get; }

    public RecordingHooks Hooks { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>Starts a host on in-memory stores, unless <paramref name="stores"/> registers others.</summary>
    public static async Task<TestHost> StartAsync(
        Action<ConsealSyncBuilder>? stores = null,
        IReadOnlyDictionary<string, string?>? config = null)
    {
        var time = new TestTime();
        var hooks = new RecordingHooks();

        var settings = new Dictionary<string, string?> { ["ConsealSync:CursorSigningKey"] = CursorKey };
        foreach (var (key, value) in config ?? new Dictionary<string, string?>()) settings[key] = value;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        var sync = builder.Services.AddConsealSync(builder.Configuration).WithCollections(Notes, Lists);
        (stores ?? (b => b.AddInMemoryStores()))(sync);
        builder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
        builder.Services.Replace(ServiceDescriptor.Scoped<IConsealSyncHooks>(_ => hooks));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapConsealSyncRoutes();
        app.MapConsealCircleRoutes();
        await app.StartAsync();
        return new TestHost(app, time, hooks);
    }

    /// <summary>A client signed in as <paramref name="userId"/>; null for one signed in as nobody.</summary>
    public ApiClient Client(string? userId = "user-1")
    {
        var http = _app.GetTestServer().CreateClient();
        if (userId is not null) http.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        return new ApiClient(http);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

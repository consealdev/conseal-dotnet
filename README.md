# Conseal for ASP.NET Core

The backend of [Conseal](https://github.com/consealdev/conseal), end-to-end encryption for web apps. It serves the sync and
circle routes of Conseal's [spec](https://github.com/consealdev/conseal/tree/main/spec) for ASP.NET Core, over DynamoDB,
with blobs in S3. It stores and orders ciphertext it can't open: sealing, opening and merging run on the client, in
[Conseal for JavaScript](https://github.com/consealdev/conseal-js).

It implements version 0.2.0 of the spec, `openapi.yaml` and `circle.yaml`.

| Route group | Mapped by | Serves |
|---|---|---|
| Sync | `MapConsealSyncRoutes("sync")` | The collections and limits, pulling and pushing records, and the blob routes |
| Circle | `MapConsealCircleRoutes("circle")` | The AEK commitment, the devices and their wrapped AEKs, and the join ceremony |

## Installation

```bash
dotnet add package ConsealDev.Conseal
```

It targets .NET 10.

## Configuration

The settings go in `appsettings.json`, under `ConsealSync`:

```json
{
  "ConsealSync": {
    "CursorSigningKey": "…",
    "Collections": ["7f3a9c21", "b4e70118"]
  }
}
```

`CursorSigningKey` is a long random secret of your own, base64 of at least 32 bytes, such as `openssl rand -base64 32`. It
signs the pull cursors. `Collections` are the ids from the client's collection registry, never the collection names. The
app refuses to start without a key and at least one collection.

## Use

Register a store, the collections and the routes:

```csharp
// Sign-in is the app's own, AuthStack or any other: Conseal has no identity of its own.
builder.Services.AddAuthentication(/* ... */);
builder.Services.AddAuthorization();

builder.Services
    .AddConsealSync(builder.Configuration)       // reads the ConsealSync section
    .AddDynamoStores(o => o.TablePrefix = "myapp-")
    .WithBlobs(o =>                              // optional: blobs for the collections named
    {
        o.BucketName = "myapp-blobs";
        o.Collections.Add("7f3a9c21");
    })
    .WithHooks<MyCircleNotifications>();         // optional

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapConsealSyncRoutes("sync");
app.MapConsealCircleRoutes("circle");
```

Every route requires an authenticated user, whose id the routes read from the sign-in: the `sub` claim by default. Each
route group comes back from `Map…`, so the app adds its own conventions, such as `.RequireRateLimiting("sync")`: the spec's
`429` is the app's to send.

`AddDynamoStores` keeps records and circles in two DynamoDB tables, which `ConsealSyncDynamoTables.Definitions(prefix)`
describes for the app's infrastructure code. `AddInMemoryStores()` keeps them in memory, for tests.

`IConsealSyncHooks` has one method for each event in a user's circle: a device joined, a device was revoked, and a revoked
device made contact. `OnDeviceJoinedAsync` is where the app tells the user that a device joined:

```csharp
public sealed class MyCircleNotifications : IConsealSyncHooks
{
    public Task OnDeviceJoinedAsync(string userId, string deviceId, DeviceJoinKind kind, CancellationToken cancellationToken)
    {
        // kind is Founding, Ceremony or Recovery. A Recovery can't be told apart from someone with the user's sign-in adding a device.
        return Task.CompletedTask;
    }

    // OnDeviceRevokedAsync and OnRevokedDeviceContactAsync are required too; they can return Task.CompletedTask.
    public Task OnDeviceRevokedAsync(string userId, string deviceId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task OnRevokedDeviceContactAsync(string userId, string deviceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

## Docs

[conseal.dev/docs/dotnet](https://conseal.dev/docs/dotnet/overview/) has every setting and its default, the storage and its
limits, the blob routes and the bucket they need, and the hooks.

---

Licensed under the GNU Affero General Public License, version 3 or any later version, or the Conseal Commercial License. See
[LICENSE](https://github.com/consealdev/conseal-dotnet/blob/main/LICENSE) and
[COMMERCIAL-LICENSE.md](https://github.com/consealdev/conseal-dotnet/blob/main/COMMERCIAL-LICENSE.md) for full terms.

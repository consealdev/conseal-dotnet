# conseal-sync-dotnet

`ConsealDev.ConsealSync`, the .NET backend for Conseal Sync: the sync and circle routes of
[`conseal-sync-spec`](https://github.com/consealdev/conseal-sync-spec) for ASP.NET Core, over DynamoDB. The server
stores and orders ciphertext it can't open; sealing, opening and merging run on the client, in
[`conseal-sync-js`](https://github.com/consealdev/conseal-sync-js).

It implements version 0.1.0 of the spec, `openapi.yaml` and `circle.yaml`, all but the blob routes.

| Route group | Mapped by | Serves |
|---|---|---|
| Sync | `MapConsealSyncRoutes("sync")` | `GET /` for the collections and limits, `GET` and `POST /{collectionId}` to pull and push |
| Circle | `MapConsealCircleRoutes("circle")` | The AEK commitment, the devices and their wrapped AEKs, and the join ceremony |

## Install

Not published yet. Clone it into `src/sdk/conseal-sync-dotnet` inside the
[`conseal-sync`](https://github.com/consealdev/conseal-sync) umbrella and reference it by `ProjectReference`:

```xml
<ProjectReference Include="..\..\conseal-sync\src\sdk\conseal-sync-dotnet\src\ConsealDev.ConsealSync\ConsealDev.ConsealSync.csproj" />
```

It targets .NET 10.

## Setup

```csharp
// Sign-in is the app's own, AuthStack or any other: Conseal Sync has no identity of its own.
builder.Services.AddAuthentication(/* ... */);
builder.Services.AddAuthorization();

builder.Services
    .AddConsealSync(builder.Configuration)
    .WithCollections("7f3a9c21", "b4e70118")     // the ids from the app's collection registry, never names
    .AddDynamoStores(o => o.TablePrefix = "myapp-")
    .WithHooks<MyCircleNotifications>();          // optional

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapConsealSyncRoutes("sync");
app.MapConsealCircleRoutes("circle");
```

Every route requires an authenticated user. Each route group comes back from `Map…`, so the app adds its own
conventions, such as `.RequireRateLimiting("sync")`: the spec's `429` is the app's to send.

### Configuration

From the `ConsealSync` section, checked at startup:

| Key | Default | |
|---|---|---|
| `CursorSigningKey` | none | Base64 of at least 32 random bytes (`openssl rand -base64 32`). Signs pull cursors; changing it sends clients back to the start |
| `Collections` | `[]` | Collection ids, beside those from `WithCollections`. At least one is needed |
| `UserIdClaim` | `sub` | The claim holding the user's id. With `sub`, `NameIdentifier` is read when `sub` is absent |
| `MaxBatchItems` | `100` | The most items one push carries |
| `MaxPageItems` | `1000` | The most items one pull returns |
| `MaxRequestBytes` | 5 MB | The largest push body, under AWS Lambda's 6 MB |

A user id is at most 256 bytes: it is part of every storage key and cursor. A longer one answers `401`.

## Storage

### DynamoDB

`AddDynamoStores` uses two tables. `ConsealSyncDynamoTables.Definitions(prefix)` returns their definitions, for the
app's infrastructure code, and `ConsealSyncDynamoTables.CreateAsync(client, prefix)` creates any that are missing,
for local development. An `IAmazonDynamoDB` the app has registered already is reused; otherwise one is made with the
standard AWS credential chain, or for DynamoDB Local when `ServiceUrl` is set.

| Table | Keys | |
|---|---|---|
| `{prefix}conseal-sync-records` | `pk` = `{userId}#{collectionId}`, `sk` = record id | Local index `seq-index` on `seq`, with every attribute |
| `{prefix}conseal-sync-circle` | `pk` = user id, `sk` = `circle`, `device#{id}` or `join#{id}` | Time to live on `ttl` drops stale join requests |

**A pull never skips a write.** Each partition has a counter item, and a write commits the record and the counter's
next value in one transaction, on condition that the counter hasn't moved, so `seq` follows commit order. A pull
reads the counter, then pages `seq-index` up to that value with a strongly consistent read, which a local index
allows and a global one doesn't. Every write up to the counter has committed before the read starts, and a write that
commits during it lands past the bound, so no page shows a write without the ones before it.

**Records are at most 202,752 bytes of ciphertext.** `seq-index` keeps a copy of each record, and DynamoDB counts
both copies against its 400 KB item limit. `GET /` advertises the limit, and anything larger belongs in a blob. A
local index also caps one user's collection at 10 GB.

**Writes to one collection are serialized.** Concurrent writers retry on the counter; one that can't commit after 25
attempts answers `503` with `Retry-After`.

### Other storage

Implement `IConsealSyncRecordStore` and `IConsealCircleStore`, and register them in place of the DynamoDB ones. Their
doc comments state what each must make atomic. `AddInMemoryStores()` is for tests.

## Hooks

`IConsealSyncHooks` reports changes to a user's circle, after they are stored:

| Hook | When |
|---|---|
| `OnDeviceJoinedAsync` | A device created the circle (`Founding`), was approved (`Ceremony`), or saved a wrapped AEK without a join request (`Recovery`). The server can't tell a recovery from someone with the user's sign-in adding a device, so tell the user |
| `OnDeviceRevokedAsync` | A device was revoked |
| `OnRevokedDeviceContactAsync` | A request named a revoked device. It still holds the AEK it had |

A hook that throws is logged, and the request still succeeds.

## Development

```sh
dotnet test ConsealDev.ConsealSync.sln
```

The DynamoDB tests run when `CONSEAL_SYNC_DYNAMODB_URL` names a DynamoDB Local endpoint, and are skipped otherwise.
Each test creates tables under a prefix of its own and deletes them afterwards.

```sh
docker run -d --rm --name conseal-sync-dynamodb -p 8010:8000 amazon/dynamodb-local:3.3.1
CONSEAL_SYNC_DYNAMODB_URL=http://localhost:8010 dotnet test ConsealDev.ConsealSync.sln
```

CI runs them against the same image.

## License

[MIT](LICENSE)

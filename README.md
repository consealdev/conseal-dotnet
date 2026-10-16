# conseal-dotnet

`ConsealDev.Conseal`, the .NET backend for conseal's sync: the sync and circle routes of
[conseal's spec](https://github.com/consealdev/conseal/tree/main/spec) for ASP.NET Core, over DynamoDB. The server
stores and orders ciphertext it can't open; sealing, opening and merging run on the client, in
[`@consealdev/conseal/sync`](https://github.com/consealdev/conseal-js).

It implements version 0.2.0 of the spec, `openapi.yaml` and `circle.yaml`.

| Route group | Mapped by | Serves |
|---|---|---|
| Sync | `MapConsealSyncRoutes("sync")` | `GET /` for the collections and limits, `GET` and `POST /{collectionId}` to pull and push, and the blob routes under `/{collectionId}/{recordId}/blobs/{blobId}` |
| Circle | `MapConsealCircleRoutes("circle")` | The AEK commitment, the devices and their wrapped AEKs, and the join ceremony |

## Install

Not published yet. Clone it into `repos/conseal-dotnet` inside the [`conseal`](https://github.com/consealdev/conseal)
umbrella and reference it by `ProjectReference`:

```xml
<ProjectReference Include="..\..\conseal\repos\conseal-dotnet\src\ConsealDev.Conseal\ConsealDev.Conseal.csproj" />
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
    .WithBlobs(o =>                               // optional: blobs for the collections named
    {
        o.BucketName = "myapp-blobs";
        o.Collections.Add("7f3a9c21");
    })
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
| `Collections` | `[]` | Collection ids, beside those from `WithCollections`. At least one is needed. The spec reserves `keyring`, for the user's identity: the routes always accept it, and it can't be configured |
| `UserIdClaim` | `sub` | The claim holding the user's id. With `sub`, `NameIdentifier` is read when `sub` is absent |
| `MaxBatchItems` | `100` | The most items one push carries |
| `MaxPageItems` | `1000` | The most items one pull returns |
| `MaxRequestBytes` | 5 MB | The largest push body, under AWS Lambda's 6 MB |

A user id is at most 256 bytes: it is part of every storage key and cursor. A longer one answers `401`.

With `WithBlobs`, from the `ConsealSync:Blobs` section too:

| Key | Default | |
|---|---|---|
| `Collections` | `[]` | The ids of the collections that have blobs, each one of `ConsealSync:Collections`. At least one is needed |
| `BucketName` | none | The S3 bucket that holds the blobs |
| `KeyPrefix` | `""` | Put before every object key, when the bucket holds other things too |
| `MaxBlobBytes` | 10 MiB | The largest sealed blob: its content plus 28 bytes of IV and tag. At most 5 GiB, one `PUT` |
| `UrlLifetime` | 5 minutes | How long a presigned URL works, at most 7 days |
| `ServiceUrl` | `""` | An S3-compatible endpoint for local development, such as LocalStack's; empty for AWS |

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

### Blobs

Content too large for a record goes in a blob, which the client seals and moves straight to and from S3 through URLs
the routes presign, so its bytes never pass through the app. `WithBlobs` turns the routes on for the collections it
names; without it they answer `404 blobs_disabled` and the app needs no bucket. `GET /` gives each collection with
blobs its `maxBlobBytes`, and null to the rest.

| Route | Answers |
|---|---|
| `POST /{collectionId}/{recordId}/blobs/{blobId}` with `{"size": n}` | A URL to `PUT` exactly n bytes to. Over `MaxBlobBytes` answers `413 blob_too_large` |
| `GET /{collectionId}/{recordId}/blobs/{blobId}` | A URL to `GET` the blob from. A missing blob is the storage's own `404` |
| `DELETE /{collectionId}/{recordId}/blobs/{blobId}` | `204`, whether the blob was there or not |

- **Keys.** `{KeyPrefix}{userId}/{collectionId}/{recordId}/{blobId}`, the user id escaped as a URI component, so a
  record's blobs share a prefix.
- **A tombstone deletes the record's blobs,** listing and deleting under its prefix after the tombstone is written.
  When that fails, the push still succeeds and the failure is logged: a blob left behind costs storage, not privacy.
- **Signature Version 4, with the size signed.** An upload URL signs `Content-Length`, so S3 refuses an upload of
  any other size. The AWS SDK presigns with version 2 in us-east-1 unless `AWSConfigsS3.UseSignatureVersion4` is set,
  so the store sets it for the process.
- **The client.** An `IAmazonS3` the app has registered already is used; otherwise one is made with the standard
  AWS credential chain, or for an S3-compatible endpoint when `ServiceUrl` is set.
- **The bucket.** Its CORS rules must let the app's origins `PUT` and `GET`. The role that presigns needs
  `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject` and `s3:ListBucket` on the prefix; without `ListBucket`, S3
  answers a missing blob with `403` rather than `404`. A lifecycle rule can delete what a client uploaded and never
  pushed a record for, which the server can't tell apart from a blob in use.

### Other storage

Implement `IConsealSyncRecordStore` and `IConsealCircleStore`, and register them in place of the DynamoDB ones. Their
doc comments state what each must make atomic. `AddInMemoryStores()` is for tests. Blobs can go elsewhere through an
`IConsealSyncBlobStore` registered after `WithBlobs`, as long as it presigns.

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
dotnet test ConsealDev.Conseal.sln
```

The DynamoDB tests run when `CONSEAL_SYNC_DYNAMODB_URL` names a DynamoDB Local endpoint, and are skipped otherwise.
Each test creates tables under a prefix of its own and deletes them afterwards.

```sh
docker run -d --rm --name conseal-sync-dynamodb -p 8010:8000 amazon/dynamodb-local:3.3.1
CONSEAL_SYNC_DYNAMODB_URL=http://localhost:8010 dotnet test ConsealDev.Conseal.sln
```

CI runs them against the same image.

The S3 tests run when `CONSEAL_SYNC_S3_URL` names an S3-compatible endpoint that checks signatures, and are skipped
otherwise. Each test makes a bucket of its own and deletes it afterwards. CI runs LocalStack 4.14.0, the last release
before LocalStack needed an auth token:

```sh
docker run -d --rm --name conseal-sync-s3 -p 4566:4566 -e SERVICES=s3 -e S3_SKIP_SIGNATURE_VALIDATION=0 localstack/localstack:4.14.0
CONSEAL_SYNC_S3_URL=http://localhost:4566 dotnet test ConsealDev.Conseal.sln
```

## Issues

Report bugs and request features at [github.com/consealdev/conseal/issues](https://github.com/consealdev/conseal/issues),
the one tracker for conseal-js, conseal-dotnet and the spec. Pull requests are not accepted.

## License

Dual-licensed under [AGPL-3.0](LICENSE) and a [commercial license](COMMERCIAL-LICENSE.md).

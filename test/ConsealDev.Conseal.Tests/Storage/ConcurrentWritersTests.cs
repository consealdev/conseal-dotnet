using System.Collections.Concurrent;
using System.Net;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;

namespace ConsealDev.Conseal.Tests.Storage;

/// <summary>
/// Writers race on one collection while a reader pulls it page by page, as another device would. A pull that skipped
/// a write would leave the reader without a record, or holding an old version of one.
/// </summary>
public abstract class ConcurrentWritersTests
{
    private const int Writers = 8;
    private const int RecordsPerWriter = 20;
    private const int PageItems = 7;

    [Fact]
    public async Task No_pull_skips_a_write_while_writers_race_on_one_collection()
    {
        await using var host = await StartHostAsync();
        var written = new ConcurrentDictionary<string, string>();

        var writers = Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(async () =>
        {
            var client = host.Client();
            for (var i = 0; i < RecordsPerWriter; i++)
            {
                var id = Items.NewId();
                written[id] = await WriteAsync(client, id, baseVersion: null);
                // Every other record is written again, so a skipped write can also show as a stale version.
                if (i % 2 == 0) written[id] = await WriteAsync(client, id, written[id]);
            }
        })));

        var reader = new Reader(host.Client());
        while (!writers.IsCompleted) await reader.PullToEndAsync();
        await writers;
        await reader.PullToEndAsync();

        reader.Seen.Should().HaveCount(Writers * RecordsPerWriter);
        reader.Seen.Should().BeEquivalentTo(written);
    }

    private protected abstract Task<TestHost> StartHostAsync();

    private static async Task<string> WriteAsync(ApiClient client, string id, string? baseVersion)
    {
        while (true)
        {
            var reply = await client.PostAsync($"/sync/{TestHost.Notes}", Items.Push(Items.Write(id, baseVersion)));
            if (reply.Status == HttpStatusCode.ServiceUnavailable)
            {
                // Busy: retry, as a client does after Retry-After.
                await Task.Delay(10);
                continue;
            }

            reply.Status.Should().Be(HttpStatusCode.OK);
            var result = reply.Body!["results"]![0]!;
            result["status"]!.GetValue<int>().Should().Be(200);
            return result["version"]!.GetValue<string>();
        }
    }

    /// <summary>Pulls as a device does, keeping the cursor and the latest version of each record.</summary>
    private sealed class Reader(ApiClient client)
    {
        private string? _cursor;
        private long _highestSeq;

        public Dictionary<string, string> Seen { get; } = [];

        public async Task PullToEndAsync()
        {
            var hasMore = true;
            while (hasMore)
            {
                var query = _cursor is null ? $"?limit={PageItems}" : $"?limit={PageItems}&cursor={_cursor}";
                var reply = await client.GetAsync($"/sync/{TestHost.Notes}{query}");
                reply.Status.Should().Be(HttpStatusCode.OK);

                foreach (var item in reply.Body!["items"]!.AsArray())
                {
                    // Each page continues where the last one ended: its records all come later in the order.
                    var seq = item!["seq"]!.GetValue<long>();
                    seq.Should().BeGreaterThan(_highestSeq);
                    _highestSeq = seq;
                    Seen[item["id"]!.GetValue<string>()] = item["version"]!.GetValue<string>();
                }
                _cursor = reply.Body["cursor"]!.GetValue<string>();
                hasMore = reply.Body["hasMore"]!.GetValue<bool>();
            }
        }
    }
}

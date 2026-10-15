using System.Security.Cryptography;
using ConsealDev.Conseal.Storage;
using ConsealDev.Conseal.Tests.TestSupport;
using FluentAssertions;

namespace ConsealDev.Conseal.Tests.Storage;

/// <summary>What every <see cref="IConsealSyncRecordStore"/> does, run against each store.</summary>
public abstract class RecordStoreContractTests
{
    private const string User = "user-1";
    private const string Collection = "7f3a9c21";

    private protected TestTime Time { get; } = new();

    [Fact]
    public async Task Each_write_gets_a_new_version_and_the_next_seq()
    {
        var store = await CreateStoreAsync();
        var id = Items.NewId();

        var first = (RecordWriteResult.Written)await store.WriteAsync(User, Collection, Create(id), default);
        var second = (RecordWriteResult.Written)await store.WriteAsync(User, Collection, Create(Items.NewId()), default);
        var third = (RecordWriteResult.Written)await store.WriteAsync(User, Collection, new RecordWrite(id, first.Version, Content()), default);

        new[] { first.Seq, second.Seq, third.Seq }.Should().Equal(1, 2, 3);
        new[] { first.Version, second.Version, third.Version }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_write_from_a_stale_version_conflicts_with_the_current_record()
    {
        var store = await CreateStoreAsync();
        var id = Items.NewId();
        var content = Content();
        var created = (RecordWriteResult.Written)await store.WriteAsync(User, Collection, new RecordWrite(id, null, content), default);

        var result = await store.WriteAsync(User, Collection, Create(id), default);

        var current = result.Should().BeOfType<RecordWriteResult.Conflict>().Subject.Current!;
        current.Version.Should().Be(created.Version);
        current.Seq.Should().Be(created.Seq);
        current.Content!.Ciphertext.Should().Equal(content.Ciphertext);
    }

    [Fact]
    public async Task A_write_naming_a_version_of_no_record_conflicts_with_nothing()
    {
        var store = await CreateStoreAsync();

        var result = await store.WriteAsync(User, Collection, new RecordWrite(Items.NewId(), "0123456789abcdef0123456789abcdef", Content()), default);

        result.Should().BeOfType<RecordWriteResult.Conflict>().Which.Current.Should().BeNull();
    }

    [Fact]
    public async Task A_tombstone_drops_the_content_and_keeps_the_time_of_the_delete()
    {
        var store = await CreateStoreAsync();
        var id = Items.NewId();
        var created = (RecordWriteResult.Written)await store.WriteAsync(User, Collection, Create(id), default);

        await store.WriteAsync(User, Collection, new RecordWrite(id, created.Version, null), default);
        var page = (RecordPullResult.Page)await store.PullAsync(User, Collection, 0, 10, default);

        var tombstone = page.Items.Single();
        tombstone.Content.Should().BeNull();
        tombstone.DeletedAt.Should().Be(Time.Now);
    }

    [Fact]
    public async Task A_complete_page_ends_at_the_highest_seq_with_nothing_more()
    {
        var store = await CreateStoreAsync();
        for (var i = 0; i < 3; i++) await store.WriteAsync(User, Collection, Create(Items.NewId()), default);

        var page = (RecordPullResult.Page)await store.PullAsync(User, Collection, 0, 10, default);

        page.Items.Select(item => item.Seq).Should().Equal(1, 2, 3);
        page.Watermark.Should().Be(3);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task A_page_cut_short_by_its_limit_ends_at_its_last_record()
    {
        var store = await CreateStoreAsync();
        for (var i = 0; i < 5; i++) await store.WriteAsync(User, Collection, Create(Items.NewId()), default);

        var first = (RecordPullResult.Page)await store.PullAsync(User, Collection, 0, 2, default);
        var rest = (RecordPullResult.Page)await store.PullAsync(User, Collection, first.Watermark, 10, default);

        first.Items.Select(item => item.Seq).Should().Equal(1, 2);
        first.Watermark.Should().Be(2);
        first.HasMore.Should().BeTrue();
        rest.Items.Select(item => item.Seq).Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task A_record_written_again_moves_to_its_new_seq()
    {
        var store = await CreateStoreAsync();
        var id = Items.NewId();
        var other = Items.NewId();
        var created = (RecordWriteResult.Written)await store.WriteAsync(User, Collection, Create(id), default);
        await store.WriteAsync(User, Collection, Create(other), default);

        await store.WriteAsync(User, Collection, new RecordWrite(id, created.Version, Content()), default);
        var page = (RecordPullResult.Page)await store.PullAsync(User, Collection, 0, 10, default);

        page.Items.Select(item => (item.Id, item.Seq)).Should().Equal((other, 2L), (id, 3L));
    }

    [Fact]
    public async Task A_pull_at_the_highest_seq_is_empty_and_one_past_it_is_beyond_the_head()
    {
        var store = await CreateStoreAsync();
        await store.WriteAsync(User, Collection, Create(Items.NewId()), default);

        var atHead = await store.PullAsync(User, Collection, 1, 10, default);
        var pastHead = await store.PullAsync(User, Collection, 2, 10, default);

        atHead.Should().BeEquivalentTo(new RecordPullResult.Page([], 1, HasMore: false));
        pastHead.Should().BeOfType<RecordPullResult.BeyondHead>();
    }

    [Fact]
    public async Task Each_user_and_collection_has_an_order_of_its_own()
    {
        var store = await CreateStoreAsync();
        await store.WriteAsync(User, Collection, Create(Items.NewId()), default);

        var otherUser = (RecordWriteResult.Written)await store.WriteAsync("user-2", Collection, Create(Items.NewId()), default);
        var otherCollection = (RecordWriteResult.Written)await store.WriteAsync(User, "b4e70118", Create(Items.NewId()), default);
        var page = (RecordPullResult.Page)await store.PullAsync("user-2", Collection, 0, 10, default);

        otherUser.Seq.Should().Be(1);
        otherCollection.Seq.Should().Be(1);
        page.Items.Should().ContainSingle();
    }

    private protected abstract Task<IConsealSyncRecordStore> CreateStoreAsync();

    private static RecordWrite Create(string id) => new(id, null, Content());

    private static SealedContent Content() =>
        new(RandomNumberGenerator.GetBytes(48), RandomNumberGenerator.GetBytes(12), RandomNumberGenerator.GetBytes(60));
}

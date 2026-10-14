using ConsealDev.ConsealSync.Configuration;
using ConsealDev.ConsealSync.Sync;
using ConsealDev.ConsealSync.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ConsealDev.ConsealSync.Tests.Sync;

public sealed class SyncCursorTests
{
    private readonly SyncCursor _cursors = Cursors(TestHost.CursorKey);

    [Fact]
    public void A_cursor_gives_back_the_position_it_was_issued_for()
    {
        var cursor = _cursors.Encode("user-1", "7f3a9c21", 42);

        _cursors.TryDecode(cursor, "user-1", "7f3a9c21", out var seq).Should().BeTrue();
        seq.Should().Be(42);
    }

    [Theory]
    [InlineData("user-2", "7f3a9c21")]
    [InlineData("user-1", "b4e70118")]
    public void A_cursor_is_refused_for_another_user_or_collection(string userId, string collectionId)
    {
        var cursor = _cursors.Encode("user-1", "7f3a9c21", 42);

        _cursors.TryDecode(cursor, userId, collectionId, out _).Should().BeFalse();
    }

    [Fact]
    public void A_cursor_signed_with_another_key_is_refused()
    {
        var cursor = Cursors(Convert.ToBase64String(new byte[32])).Encode("user-1", "7f3a9c21", 42);

        _cursors.TryDecode(cursor, "user-1", "7f3a9c21", out _).Should().BeFalse();
    }

    [Fact]
    public void A_cursor_is_url_safe_and_within_the_contract_s_1024_characters()
    {
        var cursor = _cursors.Encode(new string('u', 256), new string('c', 64), long.MaxValue);

        cursor.Length.Should().BeLessThanOrEqualTo(SyncCursor.MaxLength);
        cursor.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64url!")]
    [InlineData("AAAA")]
    public void Anything_else_is_refused(string cursor)
    {
        _cursors.TryDecode(cursor, "user-1", "7f3a9c21", out _).Should().BeFalse();
    }

    private static SyncCursor Cursors(string key) =>
        new(Options.Create(new ConsealSyncOptions { CursorSigningKey = key }));
}

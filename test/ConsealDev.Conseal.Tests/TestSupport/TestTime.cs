namespace ConsealDev.Conseal.Tests.TestSupport;

/// <summary>
/// A clock the test moves by hand. It starts at the real time, not a fixed date: the stores write expiry times that
/// DynamoDB acts on, and DynamoDB Local deletes an item whose <c>ttl</c> has passed within seconds, in the middle of a
/// test.
/// </summary>
internal sealed class TestTime : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = RealNow();

    /// <summary>The real time in whole seconds, so a time a store writes reads back equal.</summary>
    public static DateTimeOffset RealNow()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

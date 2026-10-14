namespace ConsealDev.ConsealSync.Tests.TestSupport;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestTime : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

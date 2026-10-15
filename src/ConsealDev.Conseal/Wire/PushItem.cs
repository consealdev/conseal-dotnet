using ConsealDev.Conseal.Storage;

namespace ConsealDev.Conseal.Wire;

/// <summary>One push item read: a write to apply, or the result it gets without reaching the store.</summary>
internal abstract record PushItem
{
    private PushItem()
    {
    }

    public sealed record Valid(RecordWrite Write) : PushItem;

    public sealed record Rejected(string Id, int Status, string Code, string Message) : PushItem;
}

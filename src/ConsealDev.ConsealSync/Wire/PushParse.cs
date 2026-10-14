namespace ConsealDev.ConsealSync.Wire;

/// <summary>A push body read: every item, or why the request as a whole fails.</summary>
internal abstract record PushParse
{
    private PushParse()
    {
    }

    public sealed record Items(IReadOnlyList<PushItem> All) : PushParse;

    public sealed record Failed(int Status, string Code, string Message) : PushParse;
}

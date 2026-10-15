namespace ConsealDev.Conseal.Wire;

/// <summary>The contract's error body. Clients act on <paramref name="Code"/>; the message is for developers.</summary>
internal sealed record ErrorBody(string Code, string Message);

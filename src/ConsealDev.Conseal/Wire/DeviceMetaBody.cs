namespace ConsealDev.Conseal.Wire;

/// <summary>What a device says about itself; either field may be absent.</summary>
internal readonly record struct DeviceMetaBody(string? Name, string? Platform);

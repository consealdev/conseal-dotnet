namespace ConsealDev.ConsealSync.Storage;

/// <summary>What became of a <see cref="DeviceSave"/>.</summary>
public enum DeviceSaveResult
{
    /// <summary>The device is new to the circle and now in it.</summary>
    Added,

    /// <summary>The device was in the circle; its wrapped AEK is replaced.</summary>
    Updated,

    /// <summary>The device is revoked, so nothing was stored.</summary>
    Revoked,

    /// <summary>The user has no circle to add the device to.</summary>
    NoCircle,
}

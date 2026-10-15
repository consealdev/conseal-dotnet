namespace ConsealDev.Conseal.Hooks;

/// <summary>How a device came into a circle.</summary>
public enum DeviceJoinKind
{
    /// <summary>It created the circle.</summary>
    Founding,

    /// <summary>A trusted device approved its join request.</summary>
    Ceremony,

    /// <summary>
    /// It saved a wrapped AEK without a join request, as a device does after recovering from the mnemonic. The server
    /// can't tell that from someone with the user's sign-in adding a device, so it is worth telling the user.
    /// </summary>
    Recovery,
}

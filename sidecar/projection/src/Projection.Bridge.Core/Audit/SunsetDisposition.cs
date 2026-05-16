namespace Projection.Bridge.Audit;

/// <summary>
/// The position a Bridge method occupies (or targets) on the V2 inheritance
/// gradient. V2 inherits from V1 by progressing each method through these
/// states. The state is declared at insertion time via
/// <see cref="BridgeMethodAttribute.Current"/> and
/// <see cref="BridgeMethodAttribute.Target"/>; the cutover+30 gate asserts
/// every method's Current equals its Target.
/// </summary>
public enum SunsetDisposition
{
    /// <summary>
    /// Bridge method body calls V1's class via ProjectReference. V1's source
    /// remains in the trunk. The Bridge method exists to surface V1's verb in
    /// V2 vocabulary at the wall (Wire records, BCL types, capability-shaped
    /// names). The entry-point state for every newly-introduced lift.
    /// </summary>
    Delegated = 0,

    /// <summary>
    /// V1's source has been copied into <c>Projection.Bridge.Core/Adopted/</c>
    /// and the Bridge method calls the local copy. The ProjectReference to
    /// V1's assembly is no longer needed for this capability. The code reads
    /// identically to V1's trunk version; the namespace and the location
    /// have changed. V2 has taken custody.
    /// </summary>
    Vendored = 1,

    /// <summary>
    /// The adopted source has been edited. V1's mental-model traps
    /// (string-everywhere config, exception-driven control flow, scattered
    /// overrides, mutation-as-default) have been replaced with V2 idioms
    /// (typed records, Result-shaped returns, structured builders,
    /// immutability). The code is still C# — sometimes because C# is the
    /// right language for the work (SMO, DacFx, ScriptDom), sometimes because
    /// F# would be expensive and the C# is already clean.
    /// </summary>
    RefinedInPlace = 2,

    /// <summary>
    /// The refined C# has been ported to F#, the Bridge method removed, and
    /// V2's F# adapter calls the F# directly. Not every method targets this
    /// state; some are correct at <see cref="RefinedInPlace"/> indefinitely.
    /// </summary>
    TranslatedToFSharp = 3,
}

using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// Where a verb that reads a schema stands before it reads anything (R13): the committed DacFx, named once from
/// Microsoft.SqlServer.Dac.dll, and the pin dbchange/ledgers/toolchain.md records for this dbchange version, with the committed DacFx
/// inside the pin's window. read, diff and check drift start from it and answer its error with the stamp as far as it got; doctor
/// reports the same two as its dacfx item.
/// </summary>
public static class Standing
{
    /// <summary>The stamp of the committed DacFx alone, for an answer refused before the standing is asked (arguments dbchange cannot read); null when the release cannot be named.</summary>
    public static Stamp? Committed => DacFx.Version.Match<Stamp?>(dacfx => new Stamp(dacfx), _ => null);

    /// <summary>The ledger's pin, stamped with the committed DacFx and the pin; or toolchain.dacfx-version, the ledger's own error, or toolchain.outside-window.</summary>
    public static Stamped<Pin> Of(Checkout checkout) => DacFx.Version.Match(
        dacfx => Doctor.Toolchain(checkout.Root, checkout.Version).Match(
            pin => new Stamped<Pin>(new Stamp(dacfx, pin), pin.Rejects(dacfx) is { } outside ? outside : Result.Ok(pin)),
            error => new Stamped<Pin>(new Stamp(dacfx), error)),
        error => new Stamped<Pin>(null, error));
}

using System;
using System.Diagnostics;
using System.Linq;

namespace Estate.Kernel;

/// <summary>
/// A DacFx release version, as its package names it (<c>170.5.96</c>): two to four dot-separated groups of digits, kept as
/// written. Versions order group by group as numbers, a version with fewer groups before one it prefixes, and as written last,
/// so the order agrees with equality. default(DacFxVersion) is not a version.
/// </summary>
public readonly record struct DacFxVersion : IComparable<DacFxVersion>
{
    private readonly string? _text;

    private DacFxVersion(string text) => _text = text;

    /// <summary>A version from its text, or the error <c>toolchain.dacfx-version</c>.</summary>
    public static Result<DacFxVersion> Of(string? text) =>
        text?.Split('.') is { Length: >= 2 and <= 4 } groups && groups.All(group => group.Length > 0 && group.All(char.IsAsciiDigit))
            ? new DacFxVersion(text)
            : new Error("toolchain.dacfx-version", $"'{text}' is not a DacFx release version.", "Name the DacFx package version, such as 170.5.96.");

    public int CompareTo(DacFxVersion other)
    {
        var (mine, theirs) = (Groups(), other.Groups());
        foreach (var (a, b) in mine.Zip(theirs))
        {
            if ((a.Length.CompareTo(b.Length) is var length and not 0 ? length : string.CompareOrdinal(a, b)) is var group and not 0)
            {
                return group;
            }
        }

        return mine.Length.CompareTo(theirs.Length) is var count and not 0 ? count : string.CompareOrdinal(ToString(), other.ToString());
    }

    public override string ToString() => _text ?? throw new InvalidOperationException("default(DacFxVersion) is not a version; make one with DacFxVersion.Of.");

    /// <summary>Each group without its leading zeros, so a longer group is the larger number.</summary>
    private string[] Groups() => [.. ToString().Split('.').Select(group => group.TrimStart('0'))];
}

/// <summary>
/// The DacFx release the toolchain ledger pins for a tool version (R13): <see cref="Pinned"/>, the release the Octopus step runs and,
/// when the ledger names it, the release immediately before it; or <see cref="Unpinned"/>, while the ledger's row reads UNPINNED. The
/// committed DacFx stands inside the window when it is the pin or the release before it, and anything else, a newer release included,
/// is rejected. Unpinned admits every release, and an answer made under it says UNPINNED. The two cases are closed (the constructor is
/// private).
/// </summary>
public abstract record Pin
{
    private Pin()
    {
    }

    /// <summary>A pin from the ledger's release and the release before it: each a DacFx release version, the one before older than the pin.</summary>
    public static Result<Pin> Of(string release, string? before) => DacFxVersion.Of(release).Bind(pin => before is null
        ? Result.Ok<Pin>(new Pinned(pin, null))
        : DacFxVersion.Of(before).Bind(prior => prior.CompareTo(pin) < 0
            ? Result.Ok<Pin>(new Pinned(pin, prior))
            : new Error(
                "toolchain.window-order",
                $"The release before the pin, DacFx {prior}, is not older than the pin, DacFx {pin}.",
                "Write the release immediately before the pin in the ledger row's last column, or —.")));

    public T Match<T>(Func<Unpinned, T> unpinned, Func<Pinned, T> pinned) => this switch
    {
        Unpinned u => unpinned(u),
        Pinned p => pinned(p),
        _ => throw new UnreachableException(),
    };

    /// <summary>The rejection of the committed DacFx outside the window, toolchain.outside-window, or null when the pin admits it.</summary>
    public Error? Rejects(DacFxVersion committed) => Match(
        _ => null,
        pin => committed == pin.Release || committed == pin.Before ? null : new Error(
            "toolchain.outside-window",
            $"The committed DacFx, {committed}, is neither the pinned release {pin.Release} nor the release before it{(pin.Before is { } b ? ", " + b : "")}.",
            $"Rebuild estate with ci/publish against DacFx {pin.Release}, or record the Octopus step's new DacFx release in estate/ledgers/toolchain.md."));

    /// <summary>No DacFx release pinned: the ledger's row reads UNPINNED, or the estate commits no ledger.</summary>
    public sealed record Unpinned : Pin
    {
        public override string ToString() => "UNPINNED";
    }

    /// <summary>The pinned release, and the release immediately before it, which the window also admits; null when the ledger names none.</summary>
    public sealed record Pinned(DacFxVersion Release, DacFxVersion? Before) : Pin
    {
        public override string ToString() => Release.ToString();
    }
}

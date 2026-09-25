using System;
using System.Diagnostics;
using System.Linq;

namespace Estate.Kernel;

/// <summary>
/// What a claim stands on: the five inputs that fix an outcome (the delta, the target, the data facts, the engine
/// and the profile), and where and when the claim was made. Every claim carries all five inputs or names the one it
/// lacks (law 5′). The data facts are the input a claim can lack, when it was made without reading the data;
/// <see cref="Lacking"/> then names that input, so a reader of the receipt is told rather than left to infer it
/// from a null. Two receipts agree on an input exactly when its fingerprints are equal.
/// </summary>
public sealed record Receipt(
    Fingerprint Delta, Fingerprint Target, Fingerprint? DataFacts, Engine Engine, Fingerprint Profile,
    string Where, DateTimeOffset At)
{
    /// <summary>The five inputs that fix an outcome, by name.</summary>
    public enum Input
    {
        Delta,
        Target,
        DataFacts,
        Engine,
        Profile,
    }

    /// <summary>The input this receipt lacks, or null when it carries all five.</summary>
    public Input? Lacking => DataFacts is null ? Input.DataFacts : null;
}

/// <summary>
/// The engine a claim ran on: the DacFx release that planned and published (its package version, as in
/// <c>170.5.96</c>) and, when a container was the substrate, the digest of the SQL Server image it ran (absent on
/// LocalDB). A proof transfers to a prediction only on an equal engine, so both parts take part in equality.
/// default(Engine) is not an engine.
/// </summary>
public readonly record struct Engine
{
    private const string Sha256 = "sha256:";
    private readonly DacFxVersion? _release;

    private Engine(DacFxVersion release, Fingerprint? image) => (_release, Image) = (release, image);

    /// <summary>The DacFx release, as a version the pin's window orders.</summary>
    public DacFxVersion Release => _release ?? throw new InvalidOperationException("default(Engine) is not an engine; make one with Engine.Of.");

    /// <summary>The DacFx package version as written: two to four dot-separated groups of digits.</summary>
    public string DacFx => Release.ToString();

    /// <summary>The digest of the SQL Server image, when a container was the substrate.</summary>
    public Fingerprint? Image { get; }

    /// <summary>An engine from a DacFx version and, optionally, an image digest: <c>sha256:</c> and 64 lowercase hex digits.</summary>
    public static Result<Engine> Of(string dacFx, string? image = null)
    {
        var digest = image is not null && image.StartsWith(Sha256, StringComparison.Ordinal)
            ? Fingerprint.Parse(image[Sha256.Length..])
            : null;
        return DacFxVersion.Of(dacFx).Bind<Engine>(release => (image, digest) switch
        {
            (null, _) => new Engine(release, null),
            (_, Result<Fingerprint>.Ok(var parsed)) => new Engine(release, parsed),
            _ => Result.Fail<Engine>(new Error(
                "engine.image-digest",
                $"'{image}' is not an image digest.",
                "Give the image digest as sha256: and 64 lowercase hex digits.")),
        });
    }

    public override string ToString() => Image is { } image ? $"DacFx {DacFx}, image {Sha256}{image}" : $"DacFx {DacFx}";
}

/// <summary>
/// A DacFx release version, as its package names it (<c>170.5.96</c>): two to four dot-separated groups of digits, kept as
/// written. Versions order group by group as numbers, a version with fewer groups before one it prefixes, and as written last,
/// so the order agrees with equality. default(DacFxVersion) is not a version.
/// </summary>
public readonly record struct DacFxVersion : IComparable<DacFxVersion>
{
    private readonly string? _text;

    private DacFxVersion(string text) => _text = text;

    /// <summary>A version from its text, or the error <c>engine.dacfx-version</c>.</summary>
    public static Result<DacFxVersion> Of(string? text) =>
        text?.Split('.') is { Length: >= 2 and <= 4 } groups && groups.All(group => group.Length > 0 && group.All(char.IsAsciiDigit))
            ? new DacFxVersion(text)
            : new Error("engine.dacfx-version", $"'{text}' is not a DacFx release version.", "Name the DacFx package version, such as 170.5.96.");

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
/// The engine the toolchain ledger pins for a tool version (R13): <see cref="Pinned"/>, the DacFx release the Octopus step runs
/// and, when the ledger names it, the release immediately before it; or <see cref="Unpinned"/>, while the ledger's row reads
/// UNPINNED. A committed engine stands inside the window when it is the pin or the release before it, and anything else, a newer
/// release included, is refused. Unpinned admits every engine, and a receipt made under it says UNPINNED. The two cases are
/// closed (the constructor is private).
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

    /// <summary>The rejection of a committed engine outside the window, toolchain.outside-window, or null when the pin admits it.</summary>
    public Error? Rejects(Engine engine) => Match(
        _ => null,
        pin => engine.Release == pin.Release || engine.Release == pin.Before ? null : new Error(
            "toolchain.outside-window",
            $"The committed engine, DacFx {engine.DacFx}, is neither the pinned release {pin.Release} nor the release before it{(pin.Before is { } b ? ", " + b : "")}.",
            $"Publish estate with DacFx {pin.Release}, or record the Octopus step's new engine in estate/ledgers/toolchain.md."));

    /// <summary>No engine pinned: the ledger's row reads UNPINNED, or the estate commits no ledger.</summary>
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

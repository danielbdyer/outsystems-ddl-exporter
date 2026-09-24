using System;
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
    private readonly string? _dacFx;

    private Engine(string dacFx, Fingerprint? image) => (_dacFx, Image) = (dacFx, image);

    /// <summary>The DacFx package version: two to four dot-separated groups of digits.</summary>
    public string DacFx => _dacFx ?? throw new InvalidOperationException("default(Engine) is not an engine; make one with Engine.Of.");

    /// <summary>The digest of the SQL Server image, when a container was the substrate.</summary>
    public Fingerprint? Image { get; }

    /// <summary>An engine from a DacFx version and, optionally, an image digest: <c>sha256:</c> and 64 lowercase hex digits.</summary>
    public static Result<Engine> Of(string dacFx, string? image = null)
    {
        if (!IsVersion(dacFx))
        {
            return new Refusal(
                "engine.dacfx-version",
                $"'{dacFx}' is not a DacFx release version.",
                "Name the DacFx package version, such as 170.5.96.");
        }

        var digest = image is not null && image.StartsWith(Sha256, StringComparison.Ordinal)
            ? Fingerprint.Parse(image[Sha256.Length..])
            : null;
        return (image, digest) switch
        {
            (null, _) => new Engine(dacFx, null),
            (_, Result<Fingerprint>.Ok(var parsed)) => new Engine(dacFx, parsed),
            _ => new Refusal(
                "engine.image-digest",
                $"'{image}' is not an image digest.",
                "Give the image digest as sha256: and 64 lowercase hex digits."),
        };
    }

    public override string ToString() => Image is { } image ? $"DacFx {_dacFx}, image {Sha256}{image}" : $"DacFx {_dacFx}";

    private static bool IsVersion(string? version) =>
        version?.Split('.') is { Length: >= 2 and <= 4 } groups
        && groups.All(group => group.Length > 0 && group.All(char.IsAsciiDigit));
}

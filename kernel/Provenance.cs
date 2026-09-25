using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// What a claim stands on (V3_MILESTONES.md §3, law 5′): the change claimed, the target's schema and its data conditions, each by its
/// fingerprint; the DacFx release that planned; the SQL Server; the publish profile, by its fingerprint; and the target the claim is about
/// and when it was made. Two claims agree on an input exactly when its values are equal. A claim made without reading the data lacks its
/// data conditions, and a claim on a named environment lacks its server until S8 reports it; <see cref="Lacking"/> names each input
/// that is null, so a reader is told rather than left to infer it. Each claim kind has its own constructor, which puts each fingerprint
/// in its field: <see cref="Drift"/> now; a prediction's (WP 2.1) and a proof's (WP 4.1) arrive with their milestones.
/// </summary>
public sealed record Provenance
{
    private Provenance(Fingerprint change, Fingerprint schema, Fingerprint? dataConditions, DacFxVersion dacFx, Server? server, Fingerprint publishProfile, Target target, DateTimeOffset at) =>
        (Change, Schema, DataConditions, DacFx, Server, PublishProfile, Target, At) = (change, schema, dataConditions, dacFx, server, publishProfile, target, at);

    /// <summary>The inputs a claim stands on, in the order the envelope writes them.</summary>
    public enum Input
    {
        Change,
        Schema,
        DataConditions,
        DacFx,
        Server,
        PublishProfile,
    }

    /// <summary>The fingerprint of the change claimed: for drift, the deploy report; for a prediction and a proof, the Change from base to head.</summary>
    public Fingerprint Change { get; init; }

    /// <summary>The fingerprint of the target's schema, its elements as read.</summary>
    public Fingerprint Schema { get; init; }

    /// <summary>The fingerprint of the target's data conditions, or null when the claim was made without reading the data.</summary>
    public Fingerprint? DataConditions { get; init; }

    public DacFxVersion DacFx { get; init; }

    /// <summary>The SQL Server the target runs on, or null when it was not read, as for a named environment until S8.</summary>
    public Server? Server { get; init; }

    /// <summary>The fingerprint of the publish profile the plan ran under.</summary>
    public Fingerprint PublishProfile { get; init; }

    public Target Target { get; init; }

    public DateTimeOffset At { get; init; }

    /// <summary>The inputs this claim lacks, in the order of <see cref="Input"/>: its data conditions and its server where each is null.</summary>
    public SortedArray<Input> Lacking => new([.. ((Input?[])[DataConditions is null ? Input.DataConditions : null, Server is null ? Input.Server : null]).OfType<Input>()]);

    /// <summary>
    /// A drift check's claim (DECISIONS.md, 2026-09-25): its change is the deploy report of the package against the target, and its
    /// schema the target's elements as read, so a claim repeated on an unchanged target and package agrees on both. It reads no data.
    /// </summary>
    public static Provenance Drift(Fingerprint targetSchema, Fingerprint deployReport, DacFxVersion dacFx, Server? server, Fingerprint publishProfile, Target target, DateTimeOffset at) =>
        new(deployReport, targetSchema, null, dacFx, server, publishProfile, target, at);
}

/// <summary>
/// A SQL Server as a claim records it: its product version as SERVERPROPERTY('ProductVersion') gives it, four dot-separated numbers such
/// as 16.0.4295.3; the database's compatibility level; and, for a copy on the estate-sql container, the digest of the image it runs. A
/// claim transfers from one SQL Server to another of an equal <see cref="Level"/>: the build and the image are not compared (R1).
/// </summary>
public sealed record Server
{
    private const string Sha256 = "sha256:";

    private static readonly Regex ProductVersion = new(@"\A[0-9]{1,5}(\.[0-9]{1,5}){3}\z", RegexOptions.CultureInvariant);

    /// <summary>The compatibility levels SQL Server has had, from SQL Server 2000's 80 to SQL Server 2025's 170.</summary>
    private static readonly int[] Levels = [80, 90, 100, 110, 120, 130, 140, 150, 160, 170];

    private Server(string version, int compatibilityLevel, Fingerprint? image) => (Version, CompatibilityLevel, Image) = (version, compatibilityLevel, image);

    /// <summary>The product version, such as 16.0.4295.3.</summary>
    public string Version { get; }

    public int CompatibilityLevel { get; }

    /// <summary>The digest of the image the server runs in, for a copy on the estate-sql container; null anywhere else.</summary>
    public Fingerprint? Image { get; }

    /// <summary>The product version's first number: 16 for SQL Server 2022.</summary>
    public int Major => int.Parse(Version[..Version.IndexOf('.', StringComparison.Ordinal)], CultureInfo.InvariantCulture);

    /// <summary>What a transfer compares: the major version and the compatibility level.</summary>
    public ServerLevel Level => new(Major, CompatibilityLevel);

    /// <summary>
    /// A server from what SQL Server reports: server.product-version for a version of other than four numbers, server.compatibility-level
    /// for a level SQL Server never had, server.image-digest for an image digest other than sha256: and 64 lowercase hex digits.
    /// </summary>
    public static Result<Server> Of(string? productVersion, int compatibilityLevel, string? imageDigest) =>
        productVersion is null || !ProductVersion.IsMatch(productVersion)
            ? new Error("server.product-version", "'" + productVersion + "' is not a SQL Server product version of four numbers, such as 16.0.4295.3.",
                "Report the server's SERVERPROPERTY('ProductVersion') with this error; estate reads it as SQL Server gives it.")
        : !Levels.Contains(compatibilityLevel)
            ? new Error("server.compatibility-level", string.Create(CultureInfo.InvariantCulture, $"{compatibilityLevel} is no compatibility level SQL Server has had."),
                "Report the database's compatibility_level in sys.databases with this error.")
        : imageDigest is null ? new Server(productVersion, compatibilityLevel, null)
        : imageDigest.StartsWith(Sha256, StringComparison.Ordinal) && Fingerprint.Parse(imageDigest[Sha256.Length..]) is Result<Fingerprint>.Ok(var image)
            ? new Server(productVersion, compatibilityLevel, image)
        : new Error("server.image-digest", "'" + imageDigest + "' is not an image digest.", "Give the image digest as sha256: and 64 lowercase hex digits.");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"SQL Server {Version}, compatibility level {CompatibilityLevel}")
        + (Image is { } image ? ", image " + Sha256 + image : "");
}

/// <summary>A SQL Server's major version and a database's compatibility level: what a claim made on one server says about another.</summary>
public readonly record struct ServerLevel(int Major, int CompatibilityLevel);

/// <summary>
/// What an answer stands on beside the tool's own version: the DacFx release estate runs, made once at start; the toolchain ledger's pin,
/// once the ledger was read; and the SQL Server, once a copy was reached. A verb carries it as far as its work got, failed or not.
/// </summary>
public sealed record Stamp(DacFxVersion DacFx, Pin? Pin = null, Server? Server = null);

/// <summary>A verb's result with the stamp its work reached: the stamp stands whether the result holds its value or an error.</summary>
public sealed record Stamped<T>(Stamp Stamp, Result<T> Result);

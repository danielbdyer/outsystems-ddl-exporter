using System;

namespace Projection.Bridge.Audit;

/// <summary>
/// Audit metadata required on every public Bridge method. The reflection-scanned
/// <see cref="BridgeManifest"/> reads these attributes at test-build time and
/// asserts that the manifest is well-formed; the cutover+30 sunset gate asserts
/// that every method's <see cref="Current"/> equals its <see cref="Target"/>.
/// <para>
/// The attribute IS the manuscript history of the V2 inheritance: every public
/// Bridge method cites the V1 source it descends from (<see cref="V1Source"/>),
/// the chapter that demanded the lift (<see cref="Chapter"/>), and the
/// progression target (<see cref="Target"/>). The Bridge wall analyzer
/// (<c>Projection000BridgeWallDiscipline</c>) rejects any public Bridge method
/// missing this attribute or any of its required fields.
/// </para>
/// </summary>
/// <remarks>
/// See CLAUDE.md operating-disciplines table (Bridge inheritance) and the
/// "V2 inherits from V1" section in README.md for the cultural framing.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BridgeMethodAttribute : Attribute
{
    /// <summary>
    /// The V2 chapter that demanded this lift (e.g., <c>"0.5"</c>, <c>"4.1.B"</c>,
    /// <c>"4.2"</c>). Cites the chapter open/pre-scope document that records the
    /// evidence claim. The analyzer rejects empty values.
    /// </summary>
    public required string Chapter { get; init; }

    /// <summary>
    /// ISO-8601 date the method was added to the manifest (<c>"YYYY-MM-DD"</c>).
    /// Forms part of the manuscript history together with <see cref="V1Source"/>.
    /// </summary>
    public required string AddedDate { get; init; }

    /// <summary>
    /// Fully-qualified V1 source citation: <c>Namespace.Type.Method</c>, or
    /// <c>"OriginAuthoredInV2"</c> for capabilities V2 created without V1
    /// antecedent (e.g., V2-for-V1 surface methods exposing pure V2 algebra).
    /// The analyzer rejects empty values.
    /// </summary>
    public required string V1Source { get; init; }

    /// <summary>
    /// The state this method currently occupies on the inheritance gradient.
    /// New methods enter at <see cref="SunsetDisposition.Delegated"/>; each
    /// transition is a deliberate chapter slice with its own audit trail.
    /// </summary>
    public required SunsetDisposition Current { get; init; }

    /// <summary>
    /// The state this method should reach. The cutover+30 sunset gate asserts
    /// <see cref="Current"/> equals <see cref="Target"/> for every public method
    /// in the manifest. A method whose target is <see cref="SunsetDisposition.TranslatedToFSharp"/>
    /// and whose current state is <see cref="SunsetDisposition.Delegated"/> has
    /// work scheduled against it.
    /// </summary>
    public required SunsetDisposition Target { get; init; }

    /// <summary>
    /// Whether the underlying V1 (or V2-for-V1) operation is byte-deterministic
    /// on stable input. <see cref="Determinism.NonDeterministic"/> methods may
    /// not back T1 claims downstream without explicit canary attestation; the
    /// analyzer enforces this.
    /// </summary>
    public required Determinism Determinism { get; init; }

    /// <summary>
    /// Call-frequency class. The analyzer rejects shape mismatches: a
    /// <see cref="Frequency.PerRow"/> method must return an
    /// <c>IAsyncEnumerable&lt;T&gt;</c> or accept a batched input.
    /// </summary>
    public required Frequency Frequency { get; init; }
}

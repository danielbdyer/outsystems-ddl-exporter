namespace Projection.Bridge.Audit;

/// <summary>
/// Per-method determinism class declared by every <see cref="BridgeMethodAttribute"/>.
/// The analyzer rejects T1 (byte-for-byte determinism) claims downstream of a
/// <see cref="NonDeterministic"/> Bridge method without explicit canary-attested
/// evidence. See AXIOMS.md T1 + the Bridge inheritance operating discipline in
/// CLAUDE.md for the conditional-determinism enforcement story.
/// </summary>
public enum Determinism
{
    /// <summary>
    /// On stable input, the method produces byte-equivalent output across
    /// runs. Downstream passes may claim T1 over its output without
    /// additional evidence.
    /// </summary>
    Deterministic = 0,

    /// <summary>
    /// The method may produce different output across runs on identical
    /// input (V1 SQL with implicit ordering, V1 timestamping, V1 random
    /// GUID generation, etc.). Downstream T1 claims require explicit
    /// canary attestation; the analyzer flags missing attestation.
    /// </summary>
    NonDeterministic = 1,
}

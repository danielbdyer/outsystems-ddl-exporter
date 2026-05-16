namespace Projection.Bridge.Audit;

/// <summary>
/// Per-call frequency class declared by every <see cref="BridgeMethodAttribute"/>.
/// The analyzer enforces the frequency-shape contract: a <see cref="PerRow"/>
/// method MUST return <c>IAsyncEnumerable&lt;T&gt;</c> or accept a batched
/// <c>IReadOnlyList&lt;T&gt;</c>, never <c>Task&lt;T&gt;</c> per-call. The
/// contract makes marshaling cost structurally bounded; see Chapter 0.5 slice
/// β for the analyzer rule and the performance discipline cited in CLAUDE.md.
/// </summary>
public enum Frequency
{
    /// <summary>
    /// Called once per pipeline run (e.g., <c>ExtractMetadataAsync</c> at
    /// pipeline start). Marshaling cost is negligible.
    /// </summary>
    OneShot = 0,

    /// <summary>
    /// Called O(tables) times in the worst case (~300x at canary scale).
    /// Acceptable marshaling cost; no shape constraint imposed.
    /// </summary>
    PerTable = 1,

    /// <summary>
    /// Called O(columns) times (~3,000-10,000x at canary scale). The signature
    /// must accept a batched <c>IReadOnlyList&lt;T&gt;</c> or return an
    /// enumerable; per-column <c>Task&lt;T&gt;</c> is rejected by the analyzer.
    /// </summary>
    PerColumn = 2,

    /// <summary>
    /// Called O(rows) times (potentially millions at production scale). The
    /// return type MUST be <c>IAsyncEnumerable&lt;T&gt;</c> consumed via
    /// <c>Projection.Adapters.Sql.AsyncStream</c>, OR the signature must
    /// accept a batched input and return a batched output. Per-row
    /// <c>Task&lt;T&gt;</c> is rejected by the analyzer.
    /// </summary>
    PerRow = 3,
}

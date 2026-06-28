namespace Projection.Core
// LINT-ALLOW-FILE: the ONE terminal SQL-identifier bracket-quoting module; BCL String.Concat/Replace are the use-case-specific primitives at this terminal SQL-text boundary, byte-verified against ScriptDom EncodeIdentifier.

/// The single Core-reachable SQL-identifier quoter (recon #8). T-SQL bracket
/// quoting: wrap in `[ … ]` and double any embedded `]`. This is the
/// Core-layer equivalent of ScriptDom's `Identifier.EncodeIdentifier` — which
/// Core (and the other ScriptDom-free projects: OperationalDiagnostics, the
/// `LogicalColumnEmission` pass) cannot call, because the ScriptDom dependency
/// belongs in SSDT (the `Coordinates.fs` chapter-3.5 decision). Before this, the
/// `]`-escape semantics were spelled three times — once correctly in SSDT's
/// `Render.quote` (the vendor primitive), once correctly-after-a-bugfix in
/// `RemediationEmitter.brackets`, and once WRONG (no `]`-escape) inline in
/// `LogicalColumnEmission`. This is the one definition every Core-reachable
/// emitter routes through; `SqlIdentifierTests` byte-verifies it against
/// `Render.quote` (≡ `EncodeIdentifier`) so the equivalence is law, not hope.
[<RequireQualifiedAccess>]
module SqlIdentifier =

    /// Bracket-quote one identifier segment. `Foo]Bar` → `[Foo]]Bar]`.
    let quote (s: string) : string =
        // LINT-ALLOW: THE single terminal SQL-identifier bracket-quoting site;
        // the `[ … ]`-with-doubled-`]` shape is the T-SQL grammar rule reified
        // once here. BCL `String.Concat`/`Replace` are the use-case-specific
        // primitives for this two-segment terminal-text composition; byte-verified
        // against ScriptDom's `Identifier.EncodeIdentifier` (SqlIdentifierTests).
        System.String.Concat("[", s.Replace("]", "]]"), "]")

    /// Dotted-qualify a `schema.table` pair as `[schema].[table]`, each segment
    /// bracket-quoted. The `[ … ].[ … ]` shape lives here, once.
    let qualified (schema: string) (table: string) : string =
        // LINT-ALLOW: terminal SQL-qualified-name composition over two quoted
        // segments + the literal dot separator.
        System.String.Concat(quote schema, ".", quote table)

module Projection.Tests.SchemaDiffLensParityTests

// V1 parity audit — slice 5.8.α. Reserves contract names for
// `V1_PARITY_MATRIX.md` rows 40-41 — V1's DMM lens machinery is
// dropped (V1-SUNSET); the operator-facing concept is harvested
// as a future V2 CLI `compare` verb (NOT-MAPPED).

open Xunit

[<Fact(Skip = "Matrix row 40 — ⚫ V1-SUNSET. V1's `Osm.Dmm` cluster — `IDmmLens<TSource>` interface + 3 lens adapters (`ScriptDomDmmLens` parses raw T-SQL; `SmoDmmLens` reads SMO model; `SsdtProjectDmmLens` reads SSDT-project files) + `DmmComparator` (feature-gated diff over Columns / PrimaryKeys / Indexes / ForeignKeys) + `SsdtTableLayoutComparator` — totals ~2200 LOC across 8 files. V2 chose not to carry V1's lens classes forward: (a) the load-bearing fidelity gate is the canary's `PhysicalSchema` round-trip diff (deploy → readback → assert source ≈ target on a typed `PhysicalSchema` value), which structurally subsumes V1's column / PK / index / FK comparator features; (b) V1's lenses are tightly tied to V1's trunk types (e.g., `SsdtProjectDmmLens` consumes V1's `SsdtProjectMetadata` model) and would rewrite cleaner as F# closed-DU adapters than ported as C# classes. The DMM lens machinery sunsets with V1 per `DECISIONS 2026-05-17 (slice 5.8.α) — DMM lens machinery sunset; schema-diff concept harvested as future CLI verb`. The operator-facing capability (compare two arbitrary schema sources) is reserved at matrix row 41.")>]
let ``5.8.α row 40: V1 DMM lens machinery sunsets with V1`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 40 + DECISIONS 2026-05-17 (slice 5.8.α)"

[<Fact(Skip = "Matrix row 41 — 🟠 NOT-MAPPED. V2 `projection compare <left> <right>` CLI verb. V2's CLI today exposes 4 verbs: `emit` (with `--config` / `--skeleton-only` variants), `deploy`, `canary`, `--help` — no operator-facing 'compare two arbitrary schema sources' verb. V1's DMM provided this affordance via 3 lens adapters; V2's canary subsumes it for the specific case `(live OSSYS source, live deployed target)` but not for arbitrary pairs (e.g., `(SSDT project, DACPAC file)`, `(deployed target before, deployed target after)`). Concept-harvest shape: closed-DU `DiffSource = LiveDb of connection | SsdtProject of path | DacpacFile of path | RawSql of text`; `Compare.run (left : DiffSource) (right : DiffSource) -> Diagnostics<SchemaDiff>`. Trigger: operator workflow demands ad-hoc schema-diff outside the canary's specific source-vs-deployed-target scope, OR cutover dry-run discovers a diff case the canary doesn't cover. See `DECISIONS 2026-05-17 (slice 5.8.α) — DMM lens machinery sunset; schema-diff concept harvested as future CLI verb`.")>]
let ``5.8.α row 41: V2 projection compare CLI verb reserved for operator schema-diff workflows`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 41 + DECISIONS 2026-05-17 (slice 5.8.α)"

[<Fact>]
let ``5.8.α: schema-diff-lens parity file present`` () =
    Assert.True(true)

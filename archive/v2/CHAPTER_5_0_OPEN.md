# Chapter 5.0 open — OSSYS catalog producer carbon-copy (Phase 8; offline-first)

**Sessions:** opens with this document. **Posture:** Phase 8 — the live-SQL pivot. Chapter 4.9 closed (2026-05-17) with six A.0'/4.6-shortlist items retired. The pivot is **V2 standing on its own** without V1 as a dependency for catalog acquisition. Principal-PO direction at chapter open:

> "I need V2 to stand on its own ASAP, so everything we can do offline without a live source database I want to get done."

Carbon-copy from V1 per `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance`. The donor is V1's metadata-extraction chain: `outsystems_metadata_rowsets.sql` + `MetadataSnapshotRunner` + supporting result-set processor infrastructure (~1,880 LOC; `Osm.Pipeline.SqlExtraction.*`).

---

## Why this chapter

Per V2 self-containment: V2 has **zero runtime dependency on V1's trunk**. The cutover-window pivot requires V2 to acquire its `Catalog` from a live OSSYS-hosting SQL Server without going through V1's emit. Today V2's `CatalogReader.SnapshotRowsets` accepts a `RowsetBundle` (narrow shape: Modules + Kinds + Attributes + References — 4 rowsets) and produces a Catalog; what's missing is the **SQL → RowsetBundle bridge**.

The chapter's strategic frame: **carbon-copy the offline-doable parts first**, then defer the actual live-DB execution wiring to the last-mile slice. The offline parts are: the SQL itself (static text); the rowset DTO shapes (V1 has them as C# records — V2 inherits the shape, refactors freely); the result-set processor pattern (DbRow → typed DTO); the fixture executor (`FixtureAdvancedSqlExecutor` reads from a pre-captured rowset snapshot file — no live DB required).

---

## What's offline-doable vs online-only

### Offline-doable (this chapter's primary scope)

1. **The SQL file** (`outsystems_metadata_rowsets.sql`) — carbon-copy verbatim with file-header citation. Static text; canonical SQL contract. **Slice α.**
2. **Rowset DTO shapes** — V1 carries 22 `Outsystems*Row` record types; V2's existing `CatalogReader.RowsetBundle` covers 4 of them. Lift V2's `RowsetBundle` to the full 22-rowset shape. Refactor freely at copy-time per the editorial-donor discipline. **Slice β.**
3. **Result-set processor pattern** — `ColumnDefinition<T>` + `ResultSetReader<T>` + per-rowset processor that maps `DbRow → T`. Carbon-copy structure; refactor to F# idioms where natural. **Slice γ.**
4. **Snapshot runner** — `MetadataSnapshotRunner` that calls a SQL command + walks `DbDataReader.NextResult()` reading each result set into its typed DTO. **Slice δ.**
5. **Fixture executor + offline test harness** — V1's `FixtureAdvancedSqlExecutor` reads a pre-captured rowset snapshot (e.g., recorded JSON-of-rowsets file). V2's offline test harness uses this to exercise the full extraction pipeline against fixtures. **Slice ε.**

### Online-only (last-mile; deferred until offline parts are solid)

6. **Live SqlClient execution** — `SqlClientAdvancedSqlExecutor` that takes a connection string + parameters and executes the SQL against a live OSSYS-hosting SQL Server. The only piece that requires a live DB. **Slice ζ** (deferred; gates on offline slices α–ε being green).

### Out of chapter scope

- **JSON path retirement** — V2 keeps the JSON path (`SnapshotJson` variant) as the offline-development source variant per the chapter-2 close commitment. The two paths coexist permanently per `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` §6.
- **V1's `OsmModel` aggregate-root reconstruction** — not inherited per the ADMIRE entry. V2 reconstructs `Catalog` from rowsets fresh.
- **V1's `SnapshotJsonBuilder`** — not inherited (V1 uses this to project rowsets → JSON; V2 consumes rowsets directly into the IR).

---

## Slice arc

| # | Slice | Goal | Scope |
|---|---|---|---|
| α | New `Projection.Adapters.OssysSql` F# project; SQL file carbon-copy + parity test | V1 source landed in V2 with citation; ADMIRE row updated | ~50 LOC + the SQL file |
| β | Lift `RowsetBundle` to V1-shape (22 rowsets) | `RowsetBundle` covers `PhysicalTables` / `ColumnReality` / `ColumnChecks` / `Indexes` / `IndexColumns` / `ForeignKeys` / `ForeignKeyColumns` / `Triggers` / etc.; existing 4 rowsets unchanged | ~300 LOC IR + ~150 test |
| γ | Result-set processor pattern (`ColumnDefinition` + `ResultSetReader` + per-rowset processors) | `DbRow → 'T` mapping for each of the 22 rowsets | ~600 LOC + ~200 test |
| δ | `MetadataSnapshotRunner` — orchestrates result-set enumeration | One function executes the SQL + walks result sets into a `RowsetBundle` | ~150 LOC + ~50 test |
| ε | Fixture executor + offline test harness | `FixtureAdvancedSqlExecutor` reads pre-captured snapshot; end-to-end test on a real OSSYS fixture | ~120 LOC + ~80 test |
| ζ | Live `SqlClient` execution wiring (last-mile; gated) | `SqlClientAdvancedSqlExecutor` for live DB; integration test deferred | ~80 LOC + manual integration |
| η | V1 differential + chapter close ritual | 8-item ritual | close ritual |

---

## Open questions resolved at chapter open

**Q1 — F# or C# for the carbon-copy plumbing?** F#. The carbon-copy preserves the SQL file verbatim (the SQL IS the truth); the C# plumbing is rewritten in F# at copy-time per V2's pure-core / F#-adapter convention. SqlClient is wrappable in F# (see existing `Projection.Adapters.Sql.ReadSide` precedent). DacFx, ScriptDom, and SqlClient all wrap cleanly in F#; no museum-polish C# subproject required for this chapter. The ADMIRE entry's "C# adapter project" plan supersedes here per the V2-self-containment audible at the entry's tail: "The chapter that opens this carbon-copy decides at chapter open whether the C# plumbing is preserved (carbon-copy + museum polish) or rewritten in F# at copy-time. The SQL itself is preserved verbatim either way."

**Q2 — Project naming.** `Projection.Adapters.OssysSql` (new F# project under `sidecar/projection/src/`). Concept-shaped per pillar 8 — names the live-SQL capability the adapter exposes, not the V1 project name. Sibling to `Projection.Adapters.Sql` (deployed-schema reads via SqlClient) and `Projection.Adapters.Osm` (V1 JSON / rowset bundle reads).

**Q3 — Where does the SQL file live in V2?** Embedded resource in the `Projection.Adapters.OssysSql` project (`Resources/outsystems_metadata_rowsets.sql`). Accessed via `System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(...)` at runtime. The byte-identical-to-V1 invariant is structurally guarded by a parity test that compares the embedded resource bytes to V1's source file (file-system-relative read, gated on V1 source presence — the test skips in environments where V1's trunk isn't checked out alongside V2).

**Q4 — RowsetBundle lift strategy.** Slice β lifts `RowsetBundle` to V1's full 22-rowset shape — every rowset becomes an `'a list` field with a corresponding DTO record. Existing fields (Modules / Kinds / Attributes / References) are unchanged in shape; the lift is additive. Test fixtures using the existing 4-rowset shape will need to add `[]` defaults for the 18 new rowset slots (mitigated by an `IRBuilders.mkEmptyBundle` helper or similar).

**Q5 — Fixture format for the offline test harness.** JSON-of-rowsets: a single JSON file that contains the 22 rowsets as 22 arrays of records. V2's fixture loader deserializes via `System.Text.Json` + the existing rowset DTOs. The format is documented in slice ε; one production-shape fixture (captured from V1's `MetadataSnapshotRunner` against a real OSSYS database; sanitized of PII) is the canonical golden fixture.

---

## AXIOMS amendment scan

No new axiom candidates. Chapter operates within A18 amended (Π consumes Catalog × Profile, never Policy) and pillar 9 (the OSSYS adapter carries `DataIntent` only; operator-driven filtering / module selection is `OperatorIntent` and lives at the composition layer — already the case in V2). The rowset-extraction layer surfaces V1-source evidence verbatim; it's the canonical inverse of `Project`.

---

## Closing

Chapter 5.0 is the **cutover-window pivot**. After it closes, V2 produces its own `Catalog` from a live OSSYS-hosting SQL Server without any V1 runtime dependency. The offline-first slice arc (α–ε) lets V2 stand on its own structurally — the SQL is in V2's source tree; the rowset DTOs are in V2's IR; the extraction pipeline is in V2's F# adapter — before the last-mile live-DB integration (ζ) lands. The chapter's strategic frame: **make V2 self-contained against an offline fixture first; the live-DB pivot is a final wiring slice that swaps the fixture executor for the SqlClient one**.

Slice α opens.

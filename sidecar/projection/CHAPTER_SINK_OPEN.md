# Chapter open — The Data Sink Chapter: the acquisition grain gets its ledger

> **Opened 2026-08-15** (operator approval of the execution master plan — plan-mode review
> over three reconnaissance briefs plus one design pass; the three operator rulings recorded
> below). Requirements: `THE_DATA_SINK.md` (PR #695, adopted this date — the banner flips in
> this commit). Decisions codified this date in `DECISIONS.md` ("The data sink chapter
> opens"). Axiom candidates **A49 + T19** land with their `AxiomTests.fs` stubs in the
> opening commit.

---

## 1 — The strategic frame (the axes this chapter is judged on)

**The KPI is post-cutover retrieval.** A tombstoned entity (`Is_Active = 0`, physical table
intact), an extension re-registration under `EspaceKind = 'Extension'`, two metadata claims
on one physical table — and the operator can still answer *"what is the model?"* from the
engine, offline, with the transition on the record:

```
projection sync <env>              → witnessed total snapshot + typed displacement report
projection check environments      → claims adjudicated; contested/tombstone-only/unclaimed on the board
projection diff sink:e@a sink:e@b  → the temporal diff two live: reads can never legitimately give
projection emit … --offline        → the model from the sink alone, provenance and age named
```

Five axes, in judgment order:

1. **Witness totality** — acquisition witnesses, never decides. The sync path carries no
   WHERE-clause policy (`defaultParameters` exactly); the totality gate keeps scoped reads
   out of the journal chain; nothing the source said is erased before the algebra sees it.
2. **Recoverability** — the chapter's forcing incident, proven: the tombstoned entity's
   model is retrievable from the sink alone (K5's `TombstoneOnly` witness over the
   lifecycle seed).
3. **Honesty** — provenance and evidence age on every sink-served line (the existing
   `Cached | Refreshed | Offline | Live | Absent` vocabulary, no new words); store-disabled
   and scoped-skip are named outcomes; a sink-served surface with no freshness line is a
   defect.
4. **Algebra** — the FTC at acquisition grain (T19 via `Ledger.replay`); the
   erasure-witness inequality; the three-way Selection law (A49); K2 parity as a
   construction (the same value parsed is persisted), not an aspiration.
5. **Register** — every operator-facing line through the Voice catalog, twelve-rule
   faithful, `code ⇔ copy` tested.

## 2 — The wave map (the plan of record)

Serial spine, then three tracks:

```
S0  chapter open + A49/T19 stubs                    (this commit)
S1  lifecycle seed + pure snapshot builders          (fixtures; swappable with S2)
S2  rowset 26 `capabilities` (contract 25→26)
S3  MetadataSnapshotCodec (codecVersion 1)
S4a displacement algebra + the EstateStoreLocation compile-order move
S4b SinkJournal (LedgerSpec instance) + SinkStore (digest layout, totality-gated witness)
S5  the witness hook in LiveModelRead (every live read witnesses; advisory)
S6  projection sync <env>  (K1 complete; the standalone-extract deferral cashes)
──────────────────────────────────────────────────────────────────────────────
S7  sink refs + K2 total parity        S8  freshness gating + config (K7)
S9  Selection pass + three-way law + A49 flips (K3; after S7)
S10 journal FTC + T19 flips + erasure witness (K4)
S11 PhysicalClaimRules + estate findings (K5)
S12 residue sweep (K6; S8/O4 deferral cashes)       S13 offline operation (K8)
S14 identity correspondence (K9)                    S15 eject + Twin (K10)
S16 chapter close (the eight-item ritual; the V1-input-envelope walk fires)
```

## 3 — Named non-goals (standing for this chapter)

- **Not event sourcing.** The engine differences witnessed states; it owns no source event
  log (`CONSTELLATION.md` §8's anti-goal stands). The journal is derived, append-only, and
  bounded by the erasure witness.
- **Not a second source of truth.** Live wins when reachable; the sink serves recovery,
  offline operation, history, and adjudication evidence.
- **No OSUSR data rows in the sink.** Bridge staging and the capture journal own data
  acquisition; the sink adjudicates which tables the data legs target.
- **The attribute-activity axis stays a named residual** of the three-way law:
  `OnlyActiveAttributes` has no in-memory sibling seam (the extraction canary's own scope
  note); the sync path is unconditionally total on it, and the law binds it equal across
  legs.
- **No EstateHistory double-nesting fix.** `<root>/estate/estate/…` (when only
  `PROJECTION_LEDGER_DIR` is set) is a named inconsistency; the sink lays out
  `<store>/sink/<connDigest16>/` correctly and does not replicate it; relocating existing
  history files is a separate migration.
- **No new exit codes; no new provenance vocabulary.** `sink.*` maps onto the existing axes
  (`sink.journal.syncRegression` → 9; generic `sink.*` → 2; `.writeFailed` → 1 by the
  existing rule).
- **ConfigSchema regeneration stays test-driven** this chapter (the byte-drift test is the
  mechanism); a regen verb/script is a named papercut, out of scope.

**Chapter-open findings** (named, not fixed here):

- **The lint guardrail is red on the inherited tree.** `lint-discipline.sh --ci` exits 1 at
  chapter open with 82 unmarked violations across five rules (`fsharp-string-concat` 63,
  `string-concat` 7, `set-assign` 7, `let-mutable` 4, `wide-anonymous-tuple` 1) in files
  from the 2026-07-22..25 arcs (`ApprovedDataCorrections.fs`, `DataCorrectionReceipt.fs`,
  `BridgeRetarget.fs`, `SsdtArtifactSeam.fs`, `DeployFeasibility.fs`); the script last
  changed before those files landed, so the sites arrived against a red gate (direct pushes
  — the PR-blocking workflow never fired for them). Dispositioning 82 sites is per-site
  pillar-7 work and is NOT absorbed into this chapter's slices (rushed markers are the named
  performance-of-compliance failure mode). This chapter self-enforces the guardrail's
  substance as a **delta gate**: every sink commit proves `lint-discipline.sh --ci` output
  is line-identical to the recorded baseline (zero new violations), runs the perf-gate
  manually, and commits with the explicit-deviance hatch, named per commit. The 82-site
  disposition is a standing follow-on owed to DECISIONS.
- Three Docker-touching test files sit in the pure pool with ad-hoc soft-skips and no
  `[<Collection>]` (`OssysExtractionCanaryTests.fs`, `OssysComprehensiveFixtureTests.fs`,
  `BtReferenceFkFlowTests.fs`) — `test.sh fast` opens SQL connections from the parallel
  pool, and their skips are summary-invisible (survival rules 1 + 12). New sink Docker
  tests go to `Projection.Tests.Integration`; S9 extends the existing law file in place
  (same law, same file) without perpetuating the placement for new tests.
- Two sequence-SsKey conventions coexist (`OssysTranslation.fs` `OS_SEQ` vs
  `OssysRowsetReader.fs` `OSSYS_SEQUENCE "schema.name"`); the journal keys sequences on
  `(Schema, Name)` so it does not bind, but the CatalogDiff view's sequence identity is
  verified at S10.
- The goldens are structurally untouched by this chapter: the sink writes only under the
  estate store root (`EstateStoreLocation.storeDirFrom`), never `outputs.*` — nothing
  enters `SsdtBundle`/`DataBundle`, so `GoldenEmissionTests`' artifact-set equality never
  sees it. K10's eject slice re-checks `EjectRunTests` explicitly.

## 4 — Where truth lives for this chapter

The execution substance (per-slice files, types, tests, gates, risks R1–R10 with their
rulings) is the approved master plan this chapter opened with; its operative content is
restated across: this file (the frame + Appendix A), the DECISIONS entry (the rulings),
`AXIOMS.md` A49/T19 (the candidates), and `THE_DATA_SINK.md` (the adopted charter, amended
at adoption). When they disagree, DECISIONS and the code win.

---

## Appendix A — the store contract

**Layout** (under the existing store root — `PROJECTION_ESTATE_DIR`, else
`<PROJECTION_LEDGER_DIR>/estate`, else disabled ⇒ live-only, named):

    <store>/sink/<connDigest16>/manifest.json                 latest pointer; env label; capabilities; source identity
    <store>/sink/<connDigest16>/snapshots/<syncId>/snapshot.json   the MetadataSnapshot at rest (codecVersion 1)
    <store>/sink/<connDigest16>/journal.ndjson                append-only typed displacements (LedgerSpec instance)

- `connDigest16` = SHA-256, first 16 hex, lowercase, over the normalized (DataSource,
  InitialCatalog) pair — credential-rotation invariant. At the witness hook the identity
  comes from the open connection's properties; on the verb/ref side from
  `SqlConnectionStringBuilder`; one normalization, agreement property-tested.
- The passive hook writes `EnvLabel = None`; **`projection sync <env>` is the act that
  makes an environment addressable by name** (stamps the manifest). `sink:<env>[@syncId]`
  resolves by manifest env-label scan — config-free, offline-true; two manifests claiming
  one label refuse by name (`sink.envAmbiguous`).
- **The totality gate**: only `defaultParameters`-shaped acquisitions become sink states /
  journal entries; scoped reads skip with a named reason (a scoped read diffed against a
  total predecessor would fabricate removals).
- Journal line: `{ syncId, capturedAtUtc, prevSyncId, table, key, keyBasis, transition,
  before?, after? }` — one line per row-identity delta (total); domain transitions are a
  classification over `RowAppeared | RowVanished | RowReshaped` carriers.
- Freshness: `sink.policy = off | auto | pinned` governs the reuse axis only; **witnessing
  is gated by store presence + totality — one lever, one meaning** (ruled at open;
  implemented at S8).
- Writes atomic (tmp + move), advisory on failure; reads fail-closed; interior journal
  corruption throws, a torn trailing line is tolerated.

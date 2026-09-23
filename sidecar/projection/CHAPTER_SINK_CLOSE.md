# Chapter close — The Data Sink Chapter: the acquisition grain has its ledger

> **Closed 2026-08-16.** Opened 2026-08-15 (`CHAPTER_SINK_OPEN.md`; charter
> `THE_DATA_SINK.md`, adopted at open). Seventeen commits, `15dbed6` (S0) → `f312b99`
> (S15) + this close; branch `claude/f-sharp-projection-data-sink-8lof4q`, PR #695.
> Every slice passed the full gate ladder at its own commit; the close-time sweeps are
> recorded in §4.

---

## 1 — The KPI, answered

The chapter opened on **post-cutover retrieval**: a tombstoned entity with its physical
table intact, an extension re-registration, two claims on one table — and the operator
can still answer *"what is the model?"* from the engine, offline, with the transition on
the record. Each of the opening frame's four verbs is live, witnessed against the
lifecycle seed on a real acquisition:

```
projection sync <env>              → witnessed total snapshot + typed displacement report   (S6; SinkSyncVerbTests)
projection check environments      → claims adjudicated; contested / tombstone-only /
                                     unclaimed / correspondence on the DECIDE lane          (S11b, S12, S14; SinkClaimsSeedTests + EstateSinkClaimsTests)
projection diff sink:e@a sink:e@b  → the temporal diff two live: reads can't give           (S10, S13; SinkDiffViewTests + SinkOfflineReadTests step 6)
sink.policy = pinned / auto        → the model from the sink alone, provenance + age named  (S13; SinkOfflineReadTests — the policy IS the offline lever)
```

The five judgment axes, in the open's order: **witness totality** (the totality gate is
pure and pinned; scoped reads never journal), **recoverability** (the original incident
proven — Invoice's shape addressable from the witnessed edition after the tombstone),
**honesty** (the mandatory `sink.evidenceAge` line rides every sink-backed answer;
store-disabled and scoped-skip are named outcomes), **algebra** (A49 + T19 live — §2),
**register** (nine new Voice codes, all catalog-routed, `code ⇔ copy` total).

## 2 — The acceptance laws (K1–K10), each with its standing witness

| Law | Substance | Witness |
|---|---|---|
| K1 | `projection sync <env>` = forced total witnessed read + displacement report; the naming act | `SinkSyncVerbTests` (incl. the structural `defaultParameters exactly` assert) |
| K2 | A sink read replays THE acquisition: TOTAL `Catalog` parity vs the live read | `SinkRefParityTests`; re-proven at the model seam by `SinkOfflineReadTests` (pin serves = live's catalog) |
| K3 | Acquisition is total; selection is pure — pushdown ≡ filter∘live ≡ filter∘sink | **A49 live**: `OssysExtractionCanaryTests` three-way law (attribute axis held equal — the named residual) |
| K4 | The journal replays: `fold ⊕ genesis = canonical(latest)` at acquisition grain; syncId regression refused | **T19 live**: `SinkJournalTests` FTC + `SinkWitnessTests` step 5 (live chain); `sink.journal.syncRegression` → 9 |
| K5 | The latest-edition question ruled: Adopted / Contested-always / TombstoneOnly / Unclaimed, journal-dated | `PhysicalClaimTests` (ladder + property) + `SinkClaimsSeedTests` (the four staged scenarios live) |
| K6 | Present-but-unclaimed tables surface beside the OSSYS read | `SinkClaimsSeedTests` step 5 (exactly the orphan); `EstateSinkClaimsTests` |
| K7 | Freshness is a closed decision table; policy governs reuse only, never witnessing (R2) | `SinkFreshnessTests` + `SinkWitnessTests` (bellwether move → miss → re-anchor) |
| K8 | Offline operation: pinned serves without the wire; refresh beats the pin; every miss reads live | `SinkOfflineReadTests` (the whole lever, one witness) |
| K9 | Cross-cutover identity correspondence: proposed on evidence, NEVER auto-adopted | `PhysicalClaimTests` proposer property + `EstateSinkClaimsTests` (Ruling lever pinned) |
| K10 | The eject carries the terminal sink state; the Twin imports through a sink ref | `EjectRunTests` K10 pair + `TwinSinkCatalogImportTests` (catalog from the witnessed edition; data over the live connection) |

The erasure-witness inequality (`CatalogDiff.norm(view) ≤ SinkDisplacement.norm(journal)`)
rides K4's file as its companion law — the human-shaped diff can understate the journal,
never invent.

## 3 — The eight-item ritual, walked

1. **Active-deferrals index scan.** Two rows cashed in their stated shapes: the
   standalone-extract half of row 27 (S6 — `projection sync` IS the standalone witnessed
   extract; the profile half's history stated in the S6 entry), and row 43's S8/O4 TABLE
   grain (S12 — the residue sweep). Two armed triggers checked and NOT fired, named at
   open and re-verified: the `ICatalogReader` Position-B→A trigger (a sink read is a
   `SnapshotRowsets` replay — no new reader position) and `LiveOssysConnection` reuse.
   New deferrals this chapter each carry their trigger in DECISIONS (S13/S14/S15
   residuals; §5 below).
2. **Contract-vs-implementation walk.** THE_DATA_SINK.md's K-table: all ten laws carry
   standing witnesses (§2). The estate presentation contract: the four sink finding
   kinds carry all nine total-function rows AND live detectors — the declared
   `NotYetDetected` set emptied at S14 (the coverage law now keeps any future
   vocabulary-first kind honest structurally).
3. **CLAUDE.md staleness check.** §4 gains one survival entry (the `focus`-runs-the-
   Integration-assembly-only vacuous-pass trap, which cost this chapter real time); §2's
   Tier-2 table gains the sink row. Nothing else in the file restates chapter state.
4. **README staleness check.** `tests/README.md` unchanged and correct: the assembly
   split holds — every new Docker witness this chapter went to
   `Projection.Tests.Integration` (or the Twin's own Integration assembly), and the S9
   law extension stayed in its pre-existing file by the open's own ruling.
5. **HANDOFF scope.** The top letter is rewritten as a forward-looking, second-person
   letter (prepended via Edit — the file's history is the operating surface).
6. **Fresh-eye walk.** The operator surfaces read in one sitting: sync's §3/§6 verdict
   pair + §14 store refusal; the estate's provenance-block claim notices BEFORE the
   verdict stands on them (RT-7 held); the diff faces' freshness line on every sink
   operand; the offline read's `sink.evidenceAge` envelope. No unvoiced line found; no
   banned-word regression (`", not "` absent from every new copy).
7. **Operating-disciplines currency.** Codified in the DECISIONS close entry:
   raw-at-rest (persist the acquisition; canonicalize only in the algebra) as the
   K2-parity discipline; "the policy is the lever" (one lever, one meaning — R2's
   pattern, consumed twice); the inherited-red-gate **delta gate** practice
   (substance-normalized diff vs a recorded baseline, per commit, with the deviance
   named). `JsonCodecKernel` extraction to Core is the named audit candidate — the
   second consumer exists (MetadataSnapshotCodec's local decode helpers carbon-copy it);
   the trigger is the third consumer or the next codec slice.
8. **V1-input-envelope walk** — fires for this chapter (the sink persists an
   acquisition envelope). The persisted grain is V2's own `MetadataSnapshot` — the
   26-rowset script `outsystems_metadata_rowsets.sql`, including S2's capability vector
   (rowset 26, the script's own `@Has*` probes) — with `ExpectedResultSets` self-enforced
   in lockstep and the extraction canaries asserting through the constant. **No V1
   artifact is an input to the sink**: the V1 extraction envelope contributes nothing at
   rest, the codec round-trips V2's shape only, and `ADMIRE.md` carries **no entry by
   ruling** — the chapter is new construction (the lifecycle seed authored fresh; the
   builders promoted from this repo's own test idiom; no V1 code carbon-copied).

## 4 — The gates at close

- **Release-config pure pool** — the FS3511 sweep over every `task { }` this chapter
  added, plus the goldens (`GoldenEmissionTests` ride the pure pool): **green at close
  (4851), after the sweep did its job** — it caught two chapter-owned FS3511 state
  machines (`SinkSyncRun.run`, S6; `SinkLifecycleSeedTests`, S1), both fixed in the
  close commit by the hoist pattern (awaits and `use`s stay in the CE; decision trees
  move to plain functions) and re-proven live in the Debug Docker pool. The sweep also
  surfaced eleven PRE-EXISTING FS3511s in the Twin's `SamplePr*` Integration files
  (all from the July sample-PR arcs, untouched by this chapter — verified by git
  history); they block `TEST_CONFIG=Release scripts/test.sh fast`'s solution-wide
  build, so the close ran the pure pool through its own dependency closure
  (`dotnet test tests/Projection.Tests -c Release`), Release-built every Projection
  assembly INCLUDING the Integration pool and the Twin source projects + pure tests
  (all clean), and owes the SamplePr disposition to DECISIONS as the third standing
  follow-on from that arc.
- **Full Docker pool** (`scripts/test.sh docker`): **336 passed, 2 failed at close —
  the two failures are exactly the pre-existing transfer-leg reds** named at open §3
  (`StagedMergeDeployE2ETests`, `T18CycleBreakCanaryTests` — both reproduced at the
  pre-chapter commit `34d4967` on a fresh container; the 2026-07-21..25
  bridge-retargeting arc; standing follow-on, not this chapter's). Every sink Docker
  suite green focused AND in the pool; the two FS3511-fixed files re-proven live.
- **Twin pools**: `twin-test.sh fast` green (68); the K10 witness green in the Twin's
  Integration assembly (its `focus` mode has a vacuous-pass fallback — run the
  Integration assembly filtered directly, per the new survival entry's spirit).
- **Delta-lint**: 82 = 82 substance-normalized against the recorded `825671c` baseline
  on EVERY chapter commit — zero new violations across seventeen commits; the 82-site
  disposition stays the standing follow-on owed at open.
- **Perf-gate**: clean, solo, at every slice including S15; no floor moved
  (`PERF_GATE_RECORD` untouched all chapter).
- **Matrix**: regenerated at every AxiomTests-touching commit; `git diff --exit-code`
  clean at close. **Analyzers**: 0/0 at every Core-touching slice.

## 5 — What remains (named, not silent)

Standing follow-ons owed to DECISIONS (all named at their slices, none blocking):

- **The 82-site lint disposition** (open §3), **the two transfer-leg Docker reds**
  (open §3, added at S6), and **the eleven `SamplePr*` FS3511s** (found by this
  close's Release sweep) — all pre-existing, all from the July direct-push arcs, all
  outside this chapter's surface.
- **Estate-offline env catalogs from the sink** (S13 residual): `--offline` at the
  estate face still reads env catalogs live; serving them from the sink is the same
  fast path applied at the env grain, where `SinkSection.effective (Some env)` already
  binds.
- **The attribute-axis suppression pass** (S9/S13 residual): a pure sibling of the
  `OnlyActiveAttributes` pushdown would widen sink-servability to the config default;
  until then the fast path gates on the axis (never a silent divergence).
- **Finer residue grains** (S12): columns / triggers / computed columns beside the
  table-grain sweep, under the original row's trigger.
- **The eject bundle-writer** (S15): the package carries `SinkStates`; a future
  bundle-directory writer would serialize them beside the episodes. No trigger armed.
- **`JsonCodecKernel` extraction to Core** (S3; ritual item 7): audit candidate at the
  third consumer.
- **The EstateHistory double-nesting** and **ConfigSchema regen verb**: open §3's
  standing non-goals, unchanged.

The masterwork is the destination; the sink is now part of the floor it stands on:
every live read witnesses, every displacement is journaled, every witnessed edition is
addressable, and nothing the source said is erased in silence.

# State/ledger plane — alignment audit workpaper (A5)

Scope: `Projection.Core/{Ledger,Episode,CatalogDiff,ChangeManifest,BridgeRowDelta,Lifecycle,Fixpoint}.fs`,
`Projection.Pipeline/{SinkDisplacement,SinkJournal,CaptureJournal,EstateHistory,LifecycleStore,RunLedger,TransferResume,SinkStore,SinkFreshness,SinkSyncRun,SinkDiffView}.fs`,
refactorlog surfaces (`Projection.Targets.SSDT/RefactorLogEmitter.fs`, `MigrationRun.renameStatements`).
Read-only; code-first, docs checked for drift. All paths relative to `/home/user/outsystems-ddl-exporter/sidecar/projection/`.

---

## 1 Vocabulary inventory (with file anchors)

**Delta vocabularies (the census the brief asked for) — eight distinct carriers:**

| # | Vocabulary | Plane / grain | Torsor operations present | Anchor |
|---|---|---|---|---|
| 1 | `CatalogDiff` (+ `ChannelDiff<'c>`, `*Facet`, `RenameRecord`) | schema model, kind→facet grain | `between`/`applyDiff`/`compose`/`inverse`/`norm`/`channelCounts` — full groupoid | src/Projection.Core/CatalogDiff.fs:211-1096 |
| 2 | `SinkDisplacement.Displacement` (+ `SinkRow`, `KeyBasis`, `DomainTransition`, `RowChange`) | OSSYS acquisition, rowset-row grain | `diff`/`applyOne`/`applyAll`/`norm`; no compose/inverse (list concat is compose) | src/Projection.Pipeline/SinkDisplacement.fs:135-480 |
| 3 | `ChunkRecord` (capture journal) | data-transfer progress, chunk grain | fingerprint + effectful `Apply` into shared remap | src/Projection.Pipeline/CaptureJournal.fs:18-361 |
| 4 | `ChangeManifest` | episode edge, δ-summary (counts, not a δ) | `between`/`series`/`pathLength` (projections of #1) | src/Projection.Core/ChangeManifest.fs:13-99 |
| 5 | `RefactorLogEntry` / `MigrationRun.renameStatements` | emitted realization of #1's rename channel | emission only (deliberately partial: destructive refused) | src/Projection.Targets.SSDT/RefactorLogEmitter.fs:64-88; src/Projection.Pipeline/MigrationRun.fs:372-399 |
| 6 | `BridgeStagedInsert`/`BridgeStagedIdentityUpdate` + `BridgeRowVerdict` | bridge-row staging, per-key | asymmetric by design (insert-or-fill only; overwrite unrepresentable) | src/Projection.Core/BridgeRowDelta.fs:93-269 |
| 7 | `Estate.Burndown` (closed/opened/remaining) + `HistoryFinding` carry | estate findings between readings | hand-rolled set partition; counts only in the value | src/Projection.Pipeline/EstateHistory.fs:117-146 |
| 8 | `PolicyDiff`/`KindDelta` (before/after/changed) | policy axes / run comparison | view only | src/Projection.Pipeline/PolicyDiff.fs:10-40 |

**Ledger vocabulary:** `LedgerSpec`/`LedgerEntry`/`Verified<_>`/`LedgerDrift` + `entryOf`/`writeAdmit`/`resumeAdmit`/`replay`/`resumePoint` (src/Projection.Core/Ledger.fs:28-124). Claimed instances: chunk (CaptureJournal.fs:353), episode (docstring only — Episode.fs:196-201, MigrationRun.fs:330-353), G10 marker (docstring only — TransferResume.fs:52-64), sink (SinkJournal.fs:49). The word "ledger" is additionally borne by `RunLedger` (JSONL run history, RunLedger.fs:15) and `SpineLedger` (per-run stage-visit tracker, RunSpine.fs:187) — three unrelated meanings of the flagship noun.

**Temporal nouns:** `Version` (ordinal×label VO, Lifecycle.fs:9), `Timeline` (VO, Lifecycle.fs:31), `EpisodeCoordinate` (Version×Environment×At, Episode.fs:11), `CatalogSnapshot` (Lifecycle.fs:45), `Episode` (multi-plane, Episode.fs:61), `MetadataSnapshot` (acquisition rowsets), `syncId : int` (bare, ≥11 files — SinkJournal.fs:39, SinkStore.fs:64, SinkFreshness.fs:77, Ref.fs:27, ModelResolution.fs:24, Source.fs:271, SinkSyncRun.fs:37…), "witnessed edition" (prose noun, CHAPTER_SINK_CLOSE.md §1; no carrier), `RunId : string` (EstateHistory.fs:34, RunLedger.fs:27).

**Time fields:** `EpisodeCoordinate.At` (DateTimeOffset, boundary-stamped), `JournalLine.CapturedAtUtc`, `Manifest.CapturedAtUtc` (capture time), `HistoryRecord.AtUtc` (record time) vs `HistoryFinding.FirstSeenAtUtc` (event time — explicitly split), `RunLedger.Ts : string`, `DataCorrectionReceipt.ApprovedAt : string option` (decision time as raw text), G10 `CompletedAt DATETIME2 DEFAULT SYSUTCDATETIME()` (substrate clock, TransferResume.fs:76).

**Norms:** `CatalogDiff.norm : int` (CatalogDiff.fs:1066), `SinkDisplacement.norm : int` (SinkDisplacement.fs:414), `DataObservation.CdcCaptureCount : int` (Episode.fs:34-38), `ChangeManifest.SchemaNorm`+`CdcCaptureCount` side by side (ChangeManifest.fs:24-30), T18 repair-norm ledger (AXIOMS.md:2128-2157). Erasure-witness inequality `CatalogDiff.norm(view) ≤ SinkDisplacement.norm(journal)` executable (SinkDiffView.fs:14-23; DECISIONS 31108).

## 2 The domain space (independent of current code)

The plane must express: (a) state-as-point / change-as-displacement on every stateful substrate the engine owns — schema model, acquired metadata plane, transferred row data, run/estate posture — each with genesis, ⊕, replay = latest, and a named modulus of erasure; (b) admission — an entry enters a durable chain only through a checkable witness, with the write-time/resume-time epistemic split (external witness vs recomputation) and drift refused by name; (c) resume — crash anywhere, continue exactly where the chain ends, never re-doing committed work and never trusting changed sources; (d) temporal address — every witnessed state addressable (`timeline@ordinal`, `sink:env@syncId`) and diffable across any two addresses, offline; (e) cross-plane accounting — schema moves, metadata row displacements, data CDC captures, and repair inflation as comparable magnitudes where the laws relate them (isometry T15, erasure inequality T19, break⊕repair T18, `‖rename‖_data = 0` A43), incommensurable where they are genuinely different rulers (cost vs fidelity); (f) churn vs net (path length vs net displacement); (g) inversion/rollback where non-destructive; (h) time itself split into event/capture/decision/record instants — provenance must say *when it knew* separately from *when it happened*; (i) the not-yet-built: the `migrate` orchestrator recording episodes, the general CDC `‖δ‖=k` series, cross-environment episode correlation (the §12.1 lattice), estate-offline env catalogs, eject-terminal replay in the Twin.

## 3 Findings

| ID | Class | Dimension | Reification axis | One-line claim | Anchor |
|---|---|---|---|---|---|
| A5-F1 | M4/M6 | STATE | epistemic | The Ledger contract's laws are proven on a toy instance while each production grain genuinely exercises one arm and vacates the rest — the sink's `resumeAdmit` is a self-comparison tautology (dead drift branch), the episode grain instantiates no `LedgerSpec`, `resumePoint` has zero production callers | SinkJournal.fs:365-380; Ledger.fs:62-64 |
| A5-F2 | M6/M5 | STATE | epistemic | The sink journal records chain linkage (`PrevSyncId`) but never verifies it; with the tautological fingerprint, interior sync-group loss is structurally undetectable | SinkJournal.fs:40,146-151,365-380 |
| A5-F3 | M5/M7 | STATE | epistemic | `RunLedger` — the R6 cutover gate's evidence — silently drops malformed lines, so a torn red-canary line splices `ConsecutiveGreen` longer; the exact posture SinkJournal names as "the silent forgetting a metadata ledger must never do" | RunLedger.fs:80,99-102,119-134 |
| A5-F4 | M1/M6 | STATE | ontic | `EstateHistory` holds partial sums (Streak, FirstSeen carry) with no admission and no replay-from-records: a torn `latest.json` resets age/streak while the full per-run record chain survives on disk, unreachable as history | EstateHistory.fs:74-111,270-289 |
| A5-F5 | M4 | HIERARCHICAL | ontic | `Lifecycle` (CatalogSnapshot chain) duplicates `EpisodicLifecycle`'s entire chain algebra with zero production consumers — the house dead-algebra rule is directly on point | Lifecycle.fs:85-193 vs Episode.fs:202-279 |
| A5-F6 | M4/M6 | SEMANTIC | ontic | The sink's edition ordinal is a bare `int` in ≥11 files while the sibling plane's ordinal is the `Version` VO; "witnessed edition" — the chapter's own noun — has no carrier | SinkJournal.fs:39; Ref.fs:27; Lifecycle.fs:9 |
| A5-F7 | M6/M7 | SEMANTIC | epistemic | Time is unevenly typed: decision time as raw string, and the otherwise fail-closed LifecycleStore decoder silently defaults a malformed episode instant to `DateTimeOffset.MinValue` — time is the one field allowed to lie | DataCorrectionReceipt.fs:223-224; RunLedger.fs:28; LifecycleStore.fs:322-328 |
| A5-F8 | M7/M6 | RELATIONAL | teleological | Three norms ride as bare ints; the per-edge cross-plane law the domain states (A43 `‖rename‖_data = 0`) has no carrier or witness on `ChangeManifest`, which holds both operands side by side | ChangeManifest.fs:24-30; AXIOMS.md:1995 |

### A5-F1 — the Ledger contract as costume at three of four grains (deepest)

**Evidence.** `Ledger.fs:64` docstring: "The journal (chunk grain), the episode store (episode grain), and the G10 progress marker … are its instances"; T19 (AXIOMS.md:2300-2302): "replay IS `Ledger.replay` and regression-refusal IS `resumeAdmit`". Against the code:
- **Sink journal**: `SinkJournal.admitChain` (SinkJournal.fs:373) calls `Ledger.resumeAdmit line.SyncId (Ledger.entryOf spec line.SyncId line)`. `entryOf` stamps `Fingerprint = spec.FingerprintOf line = line.SyncId` (SinkJournal.fs:53), so the comparison is `line.SyncId = line.SyncId` — always true. The `Error drift` arm (SinkJournal.fs:375-377, "journal fingerprint drift at position %d") is unreachable. The real admission is the hand-written `line.SyncId < lastSync` guard (SinkJournal.fs:370) *outside* the contract. T19's enforcement sentence is doc-drift: regression-refusal is NOT `resumeAdmit`.
- **Capture journal**: admission is real (`Ledger.resumeAdmit (firstPk,lastPk,rawCount)` recomputed from the live slice, TransferRun.fs:2338) but replay is a single-entry effectful fold whose result is discarded — `Ledger.replay … [admitted] |> ignore` (TransferRun.fs:2340), the spec's `Apply` mutating the shared remap (CaptureJournal.fs:353-361). Production resume walks the offsets index (`tryFindRecord`), never `resumePoint`.
- **Episode store**: no `LedgerSpec` value exists (grep: only SinkJournal.fs:49 and CaptureJournal.fs:353 instantiate). `writeAdmit` is genuinely used (MigrationRun.fs:338, B′≡B) but ResumeAdmit is `EpisodicLifecycle.append`'s monotonicity check (Episode.fs:196-206) and replay is a bespoke `List.fold CatalogDiff.applyDiff` (Episode.fs:240), not `Ledger.replay`.
- **G10 marker**: instantiates nothing; honestly documented as degenerate (TransferResume.fs:52-64).
- The generic laws (`LedgerTests.fs:23-58`) run over a synthetic `sumSpec`; only the sink's *replay* arm is law-tested through the production spec (DECISIONS 31108).

**Misalignment.** M4 (one vocabulary, four dialects: `Verified<_>` means "B′≡B held" / "source fingerprint matched" / "syncId ≥ last, checked elsewhere" / nothing) and M6 (the *actual* admission law of each grain lives in per-file folds and comments, not in the shared type). The epistemic promise of the contract — "the type carries HOW the entry is known good" — is exactly what varies silently.

**Candidate reified primitive.** `ChainAdmission<'entry,'fp>` — per-grain admission declared as data on the spec (e.g. `Monotone of ('entry -> int)` | `Recompute of independent-source` | `Linkage of ('entry -> 'fp option)`), so `admitChain` is one Core fold over declared rules and a tautological fingerprint cannot be written. Contract: an instance's admission is what its spec *says*, and the drift refusal is minted by Core, not re-phrased per file.

**Fluency bought.** The fifth ledger (the eject bundle-writer, CHAPTER_SINK_CLOSE.md §5; the Twin's import) gets admission for free and honestly; T19's enforcement sentence becomes true; `Verified<_>` regains one meaning. **Effort M. Risk of inaction:** each new grain re-invents admission; the dead drift branch invites a future reader to believe a check exists that doesn't.

### A5-F2 — recorded-but-unverified chain linkage at the sink grain

**Evidence.** `JournalLine.PrevSyncId` is written at both append sites (SinkStore.fs:322, 352), round-trip-tested (SinkStoreTests.fs:133-210), and read back (SinkJournal.fs:259-262) — but `admitChain` never touches it (SinkJournal.fs:365-380). Combined with F1's tautological fingerprint: delete an interior sync group wholesale and the file still admits (positions non-decreasing), replay silently produces a state missing that group's keys-not-touched-later; nothing refuses. The manifest's `SnapshotSha256` (SinkStore.fs:81) guards only the latest snapshot file, not the journal interior.

**Misalignment.** M6 — the knowledge "every sync chains to its predecessor" is reified as wire data but not as law; M5 — `admitChain` is total over inputs the domain considers inadmissible.

**Candidate primitive.** Verify linkage in `admitChain`: each sync group's `PrevSyncId` must equal the last admitted sync (else `sink.journal.brokenChain`); or make the fingerprint independent (per-sync displacement count or group hash). One fold, one new refusal code.

**Fluency bought.** The journal becomes tamper/truncation-evident at the grain T19 claims it is. **Effort S. Risk:** an operator's `diff sink:e@a sink:e@b` silently understates history after interior loss — the exact "silent forgetting" the chapter's charter forbids.

### A5-F3 — the R6 gate's ledger keeps the skip-malformed posture

**Evidence.** `RunLedger.parseLine` returns `None` on malformed JSON (RunLedger.fs:80, comment: "malformed ledger JSON → None"); `read` drops them (`List.choose parseLine`, RunLedger.fs:101, justified as "forward-compatibility"). `readiness` counts `ConsecutiveGreen` backward over surviving canary verdicts (RunLedger.fs:119-134) and gates R6 eligibility (`Eligible = consecutiveGreen >= 10 …`). A torn/malformed line carrying a *red* canary is skipped, so the greens on either side splice into a longer streak — the gate can read *more* eligible after corruption. SinkJournal.fs:23-25 names this very posture ("`RunLedger`'s skip-malformed posture is exactly the silent forgetting a metadata ledger must never do") — the codebase already adjudicated the discipline and left the governance surface on the wrong side of it. Mitigation: the gauge "measures the evidence, the operator makes the call" (RunLedger.fs:18-19).

**Misalignment.** M5 (partial parse totalized by silent drop over a domain where every line is load-bearing) + M7 (the asymmetry vs SinkJournal's fail-closed load is unjustified precisely where stakes are highest — cutover governance).

**Candidate primitive.** Adopt the sink posture: torn *trailing* line tolerated, interior corrupt line a named refusal (`ledger.runs.corruptLine`), or at minimum surface `SkippedLines : int` on `Readiness` so the gauge names its own erasure.

**Fluency bought.** The cutover streak becomes evidence, not an optimistic projection of surviving bytes. **Effort S. Risk:** a false-eligible R6 reading after disk trouble — low probability, highest blast radius on this plane.

### A5-F4 — EstateHistory: partial sums outside the ledger discipline

**Evidence.** `Streak` is an incrementally-carried partial sum (`1 + previous.Streak`, EstateHistory.fs:104-106) and `FirstSeenAtUtc` an event-time carry (EstateHistory.fs:80-97); both derive from `previous : HistoryRecord option` = `loadLatest` only. `loadLatest` is fail-closed to `None` (EstateHistory.fs:270-277) — correct locally, but there is no function that rebuilds a baseline from the surviving `estate/<runId>.estate.json` records: a torn `latest.json` resets streak to 1 and every finding's age to `nowUtc` while the true history sits beside it on disk. No admission, no chain, no replay law relates the per-run records to the accumulated state.

**Misalignment.** M1 — "recover the burndown's memory from the records" is an outcome the vocabulary cannot express; M6 — the record *directory* is a chain in fact but not in type. This is a stateful surface named by the brief's uniformity question: it lacks the torsor/FTC treatment entirely (the others: `RunLedger` (F3), `ApprovalStore`-style registries — the latter genuinely stateless-per-read, an anti-finding).

**Candidate primitive.** `EstateHistory.replay : HistoryRecord list -> HistoryRecord` (fold readings to the derived latest, streak and first-seen recomputed) + `latest.json` demoted to cache-of-fold; property: `loadLatest = replay (loadAll)` — the FTC at the reading grain.

**Fluency bought.** Age/streak survive pointer loss; the burndown becomes auditable ("show me the streak's derivation"). **Effort M. Risk:** silently-reset ages misprioritize the estate board after any store hiccup; nobody notices because the reset is the fail-closed *success* path.

### A5-F5 — two chain algebras, one domain concept

**Evidence.** `Lifecycle` (Lifecycle.fs:56-193) and `EpisodicLifecycle` (Episode.fs:171-279) each carry genesis/append-with-monotonicity/evolutionChain/reconstruct/netDiff — the `netDiff` and `netSchemaDiff` folds are near-byte-identical (Lifecycle.fs:163-193 vs Episode.fs:253-279, same NM-45 comment, same error, different code string). Bare `Lifecycle` has zero production consumers: every Pipeline/CLI use is `EpisodicLifecycle` (Pipeline.fs, MigrationRun.fs, EjectRun.fs, ReportRun.fs, LifecycleStore.fs); `Lifecycle` survives in its own file, doc cross-refs, and `LifecycleTests.fs`. Episode.fs:169-170 itself says an `EpisodicLifecycle` "*is* a `Lifecycle` enriched with the data/time/decision planes."

**Misalignment.** M4 — split vocabulary for one concept (a monotone chain of states along a timeline), with the split maintained by hand in two files. The house rule is explicit: "zero-consumer symmetry-builds get deleted" (CLAUDE.md §5, the 2026-06-04 dead-algebra precedent).

**Candidate primitive.** Either delete `Lifecycle`/`CatalogSnapshot` (port its tests onto `EpisodicLifecycle` + `Episode.ofSchema`) or extract the one chain algebra generic over the point type (`Chain<'point>` with `versionOf`), instantiated twice. Deletion is the house-consistent move.

**Fluency bought.** One place for the monotone-append law and the NM-45 refusal; T13's witness surface stops naming two folds. **Effort S–M. Risk:** the two folds drift (one already did once — NM-45 was fixed in both by copy).

### A5-F6 — the untyped edition ordinal (syncId) and the carrier-less "edition"

**Evidence.** `syncId : int` bare in ≥11 files (SinkJournal.fs:39; SinkStore.fs:64,241-244; SinkFreshness.fs:77; SinkSyncRun.fs:37-41; Ref.fs:27 `Sink of env: string * syncId: int option`; ModelResolution.fs:24; Source.fs:271; EjectRun, CLI faces). The sibling plane's ordinal is a smart-constructed VO (`Version`, Lifecycle.fs:9-24, non-negative enforced). Monotonicity of syncId is enforced only inside `admitChain` and the store's append path — never by construction. The chapter's own operator noun — "the witnessed edition" (CHAPTER_SINK_CLOSE.md §1; T19 prose "the latest witnessed snapshot") — has no type: an edition travels as loose `(env, syncId)` / `(digest, syncId)` pairs.

**Misalignment.** M4 (one domain concept — a monotone edition ordinal on a timeline — two vocabularies: `Version` typed, `syncId` raw) + M6 (the edition, the thing `sink:<env>@<syncId>` addresses and K5 adjudicates over, is unreified).

**Candidate primitive.** `SyncOrdinal` VO (non-negative, `next`, `Comparable`) and `SinkEdition = { Source : ConnDigest|EnvLabel; Ordinal : SyncOrdinal }` — the address `Ref.Sink` resolves *to*, the operand `diff sink:@a sink:@b` takes, the field `Manifest.LatestSyncId` becomes.

**Fluency bought.** The claims/freshness/diff/eject surfaces stop re-agreeing informally on what an edition is; a negative or swapped ordinal becomes unrepresentable at every one of the 11 seams at once. **Effort M (mechanical, wide). Risk:** low-grade — but every future sink consumer (the Twin import is the newest) re-learns the convention by reading three files.

### A5-F7 — time is the one field allowed to lie

**Evidence.** Decision time: `DataCorrectionReceipt.ApprovedAt : string option` (DataCorrectionReceipt.fs:223-224) — raw text in Core, persisted/loaded as opaque string (LifecycleStore.fs:181,469). Record time: `RunLedger.LedgerRecord.Ts : string` (RunLedger.fs:28). And in the otherwise hard-fail-closed LifecycleStore decoder — where an unknown tolerance token, a malformed SsKey, or a bad count is a hard error — a malformed or missing `at` silently becomes `DateTimeOffset.MinValue` (LifecycleStore.fs:322-328). Contrast the exemplar: `EstateHistory` splits event time from record time and carries first-seen across files ("age is the finding's, not the file's," EstateHistory.fs:17-19,74-97), and `EpisodeCoordinate.At` is boundary-stamped by rule (Episode.fs:3-10).

**Misalignment.** M6 (the epistemic kind of each instant — event vs capture vs decision vs record — is carried by field-name convention, not type; two instants are not even instants) + M7 (the decoder's severity is asymmetric on exactly the provenance axis: everything else fail-closed, time defaulted — a `MinValue` coordinate then silently orders/labels an episode).

**Candidate primitive.** (a) `ApprovedAt/Ts` become `DateTimeOffset` at parse (fail-closed, like every sibling field); (b) make the LifecycleStore `at` decode a hard `ParseFailure`; (c) optional, second-consumer-gated: `CaptureInstant`/`DecisionInstant`/`RecordInstant` single-case wrappers where two kinds meet in one record (Episode, JournalLine, HistoryRecord already mix kinds).

**Fluency bought.** "When did we know?" becomes answerable with the same confidence as "what changed?" — the provenance engine's own axis. **Effort S (a,b) / M (c). Risk:** a MinValue-dated episode or a locale-shaped ApprovedAt string entering the eject bundle — permanent, post-eject-unfixable provenance.

### A5-F8 — the norms are counted but not related where the domain relates them

**Evidence.** `ChangeManifest` carries `SchemaNorm` and `CdcCaptureCount` side by side as bare ints (ChangeManifest.fs:24-30); the domain's per-edge law relating them — A43's `‖emit(π_Rename δ)‖_data = 0`, "the refactorlog is derived, not stipulated" — has no field, no predicate, no witness (AXIOMS.md:1995 "⬚ the `‖rename‖_data = 0` canary"; WAVE_6_ALGEBRA.md:233-234 trigger armed since 2026-06-01, rides 6.D.1). The pairs the domain *does* relate are live: erasure inequality (SinkDiffView.fs:14-23), triangle/churn (`pathLength` vs `netSchemaDiff`, ChangeManifest.fs:90-99, M11), T18's repair equality. All norms are unit-less `int`s; the UoM promotion for mixed-quantity expressions is FIRED but gate-held on R1d (CLAUDE.md §7).

**Misalignment.** M7 (the one cross-plane inequality that is the *stated reason* the refactorlog exists is the one left unwitnessed, while intra-plane relations all have witnesses) + M6 (norm units by convention; acknowledged, trigger-armed — low urgency).

**Candidate primitive.** A `CrossPlaneAccount` line on `ChangeManifest`: for a rename-only edge (`Channels` all zero except renames), the expected data norm is 0 — a named `RenameIsometryViolated` when `CdcCaptureCount > 0` on such an edge. Cheap static half of the deferred live canary; the manifest already holds both operands.

**Fluency bought.** The SSIS consumer's "this sprint renamed only — why did data move?" becomes a machine answer. **Effort S (manifest predicate) / L (live canary, already routed to 6.D.1). Risk:** low now; the account is latent knowledge until the first unfaithful rename ships silently.

## 4 Anti-findings (correct specializations)

- **Schema δ as value vs data δ substrate-fused** (no `RowDiff`): the asymmetry is derived, argued, and enforced (WAVE_6_ALGEBRA.md §12.4; Episode.fs:25-38 persists count+handle, not Profile). The refusal to symmetrize is the *right* refusal — a model-plane RowDiff would be the speculative-abstraction trap.
- **`BridgeRowDelta`'s closed, asymmetric vocabulary** (insert-or-fill-only, blocks as cases not severities, fail-closed evidence): deliberately NOT a torsor — overwrite is made unrepresentable, which is the domain's safety rule reified (BridgeRowDelta.fs:21-36,93-125). Judging it against `CatalogDiff`'s symmetry would be the category error.
- **`SinkDisplacement` lacking `compose`/`inverse`:** at the row grain, journal append *is* composition (list concat replays associatively) and before/after images make inversion derivable; adding operators before a consumer would violate the two-consumer rule. Distinct from `CatalogDiff` by genuine grain, not alias (M4 acquitted).
- **`CatalogSnapshot` vs `MetadataSnapshot`:** one word, two planes (model point vs acquisition rowsets) — both typed, never confused at a seam; the S7 raw-at-rest ruling (canonical only in the algebra) is a real epistemic distinction, kept.
- **The G10 marker's degenerate instance:** "exercises NOTHING of the contract's replay machinery, honestly" (TransferResume.fs:52-64) — a single full-state quantum genuinely has no partial sums; SQL set-membership as fingerprint equality is the right realization, and the docstring says exactly that.
- **`Compare` reusing `CatalogDiff.between`** (Compare.fs:14-19): the multi-environment face did NOT grow a parallel schema-diff dialect — the M4 that didn't happen.
- **CDC vs Bench as two incommensurable rulers** (fidelity vs cost): the incommensurability is the design (CLAUDE.md §1), not a gap.

## 5 Already-aligned (exemplary reifications)

- **`CatalogDiff` facet-as-lens** (CatalogDiff.fs:300-356): detection and application derived from ONE lens table per channel — "no un-captured field silently reconstructed" made structural, the M6-closure pattern the rest of the plane should envy; plus the full concrete groupoid (`between`/`applyDiff`/`compose`/`inverse`/`norm`) with the no-cheat W3 witness.
- **`SinkDisplacement` totality + key honesty**: domain transitions are a classification over a total row-grain carrier, never a filter (Domain = None still journals, SinkDisplacement.fs:13-19,117-131); `KeyBasis` carries rename-fragility on every record (SinkDisplacement.fs:26-41) — knowledge-of-identity-quality reified at the grain it applies.
- **The admission split itself** (Ledger.fs:12-27): WriteAdmit vs ResumeAdmit as *different knowledge available at different times* is a genuinely epistemic type distinction — the contract is right; F1 is about instances honoring it.
- **`Episode.durableProjection`** (Episode.fs:161-163) + LifecycleStore NM-34: "a stored episode equals its own durable projection" — the persisted/in-memory boundary as an equation, tested.
- **`EstateHistory`'s event-time/record-time split** (FirstSeenAtUtc carried by key across readings, EstateHistory.fs:74-97): the time exemplar F7 asks the rest of the plane to match.
- **The erasure-witness inequality as executable law** (SinkDiffView.fs:14-23; SinkDiffViewTests): the view/journal relation stated as an inequality with the erasures *named* — understate-never-invent, proven.
- **`ChangeManifest.pathLength` vs `netSchemaDiff`** (ChangeManifest.fs:90-99): churn = path − net, the work/displacement distinction the operator actually asks about, reified as two comparable numbers.

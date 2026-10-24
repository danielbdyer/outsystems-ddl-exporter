# Follow-on D — the streaming reverse-leg's compensating-undo (M23 arm), ✅ BUILT (with its gate canary)

> **Status: BUILT (2026-06-16, later still) — shipped WITH its deterministic streaming-failure canary.** See
> `DECISIONS.md` "Follow-on D BUILT" for the substance. The migrate "we can't go wrong" envelope
> (M21/M22/M23/M24 + D) is now complete. This doc is retained as the provenance of the instruction set, with ONE
> correction recorded inline below (§2.2) and in DECISIONS: the streaming failure is a **THROW**, not a returned
> `Result.failure`, so the compensation hangs off a `try/with` around the `writePlanStreaming` call (the exception
> path), NOT the `Error es` match arm — which catches only the named resume source-drift refusal (and deliberately
> must NOT revert, lest it delete prior-run committed rows). The §2 seams below are otherwise as built; the §3 canary
> is realized as the two "streaming data canary (D)" witnesses in `TransferCanaryTests`. The §4 minor follow-on
> (`migrate --with-data` DATA-leg revert) remains out of scope.

## 0 — The gate (read first; do not skip)

D ships an **active `DELETE`-by-captured-key on the estate-scale streaming path.** A wrong delete at 10⁸ rows is
**unrecoverable**. Per the engine's standing discipline ("every correctness claim a property test"; "we can't go
wrong" by refusing, never by guessing), **D MUST land in the same commit as a deterministic streaming-failure canary**
that proves the revert deletes exactly the journaled sink-minted rows and leaves pre-existing rows untouched. It was
deliberately not shipped untested at the tail of the 2026-06-16 session. **Do not merge D without its canary.**

## 1 — What D is

The data-leg compensating-undo (M23) realized on the STREAMING realization, mirroring the materialized `writePlan`
arm. On a mid-stream failure the partial chunks are already committed to the sink and recorded in the off-box
`CaptureJournal`; D reverts them (or emits the revert script), honoring the per-environment `revert` policy (M24).

**Why it's a separate arm.** M23's `buildRevertScript` reads an in-memory `PackedSurrogateRemap` at `writePlan`'s
failure point. The streaming path has no such in-memory remap at failure — its captures live in the **journal**
(NDJSON, off-box). And the streaming path returns a `Result.failure` (not a throw, unlike `writePlan`). So D's
compensation hangs off the streaming `Error` branch and reconstructs the remap from the journal.

## 2 — The implementation seams (exact)

### 2.1 — Thread the revert levers into the streaming entry chain
- `runStreamingReverseLegThroughConnections` (`TransferRun.fs` ~2024) — add `(autoRevert: bool)` `(revertDir: string option)` params.
- Its delegators: `runStreamingReconcilingWithRenames` (the body, `TransferRun.fs` ~1820–1995) and
  `runStreamingWithRenames` (~2002) — thread the two params through (the latter passes `false None` for the
  non-reconciling default, or surfaces them too).
- The RunFaces streaming call: in `runReverseLegTransfer`, the `ReverseLegRealization.Streaming` branch
  (`RunFaces.fs` ~902–904) — pass the already-computed `revertAuto revertOut` (currently only the `Materialized`
  branch consumes them; `revertAuto`/`revertOut` are already derived in that face via `RevertPolicy.toEngine`).

### 2.2 — Compensate at the streaming `Error` branch
At `TransferRun.fs` ~1967–1968:
```
match! writePlanStreaming source sink sourceContract renameMap sinkContract plan journal reconciled.Remap reconciledKinds with
| Error es ->
    // D — replay the journal into a remap, then the M23 revert.
    do! runRevertFromJournal sink sinkContract plan journal autoRevert revertDir
    return Result.failure es
| Ok (totals, skips, descents) -> ...
```
where `runRevertFromJournal` (a new `let private` in the `Transfer` module, next to `buildRevertScript`/`runRevert`):
```
let private replayJournalToRemap (catalog: Catalog) (journal: CaptureJournal) : PackedSurrogateRemap =
    let remap = PackedSurrogateRemap.create ()
    let rootToKey =
        Catalog.allKinds catalog
        |> List.map (fun k -> SsKey.rootOriginal k.SsKey, k.SsKey)
        |> Map.ofList
    for KeyValue (_, record) in CaptureJournal.load journal do
        match Map.tryFind record.Kind rootToKey with
        | Some ssKey -> record.Pairs |> Array.iter (fun p -> if p.Length = 2 then PackedSurrogateRemap.capture ssKey p[0] p[1] remap)
        | None -> ()
    remap

let private runRevertFromJournal sink catalog plan (journal: CaptureJournal option) autoRevert revertDir : Task<unit> =
    task {
        match journal with
        | Some j -> do! runRevert sink autoRevert revertDir (buildRevertScript catalog plan (replayJournalToRemap catalog j))
        | None   -> ()   // no journal = no captures to revert (streaming execute requires --journal anyway)
    }
```
Reuses the existing `buildRevertScript` + `runRevert` (the M23 primitives) verbatim — only the remap source differs
(journal-replayed vs in-memory). `CaptureJournal.load` (`CaptureJournal.fs:91`) returns the `ChunkRecord`s; each
carries `Kind` (the root string) + `Pairs` (`[source; assigned]`). Confirm `record.Kind` is `SsKey.rootOriginal`-shaped
when wiring (it is what `load` keys on).

### 2.3 — Notes
- Streaming `execute` already requires `--journal` (the `executeJournalGate`), so at the `Error` branch `journal` is
  `Some` on a real run; the `None` arm is the safe no-op.
- The revert is child-first by construction (`buildRevertScript` iterates `plan.Loads |> List.rev`).
- Only `AssignedBySink` kinds with captures produce DELETEs; pre-existing rows are never targeted.

## 3 — The canary (the gate — ship in the same commit)

A Docker streaming transfer into a `ManagedDml`-shaped sink with `--journal <dir>`, forced to fail mid-stream after at
least one chunk has captured + committed. Suggested forcing mechanism (must be deterministic): poison a LATER kind's
sink table so its chunk write fails (e.g. drop a targeted column, or a sink-side constraint a later chunk violates)
while an earlier `AssignedBySink` kind streams + journals successfully. Then assert:
- **`--auto-revert` (or env `revert: auto`):** the earlier kind's journaled minted rows are DELETED; pre-existing rows
  remain; the run still reports failure.
- **default (`revert: script`):** the revert `.sql` artifact exists at the dir, names `DELETE FROM <kind>`; the rows
  REMAIN (operator runs it).
- Mirror the materialized witnesses in `TransferCanaryTests` ("Build A" canaries) for shape.

If a deterministic mid-stream failure proves too fiddly, a PURE unit test of `replayJournalToRemap` +
`buildRevertScript` (construct a journal file + catalog + plan, assert the DELETE script) is the minimum acceptable
witness — but the live Docker canary is strongly preferred for an estate-scale destructive op.

## 4 — Related minor follow-on (out of scope for D)

`migrate --with-data`'s DATA leg does not yet carry the `--auto-revert` policy — it routes through
`Transfer.runWithRenamesWith` / `runReconcilingWithRenamesWith`, not the `*ThroughConnections*` faces. Threading the
revert there is a small, separate follow-on (the schema leg already honors `--atomic` via M24 follow-on C).

## 5 — Cross-references
`src/Projection.Pipeline/TransferRun.fs` (`writePlanStreaming`, the `Error` branch, `runStreamingReverseLegThroughConnections`,
`buildRevertScript`/`runRevert`); `src/Projection.Pipeline/CaptureJournal.fs` (`load`, `spec`, `ChunkRecord.Pairs`);
`src/Projection.Pipeline/PackedSurrogateRemap.fs` (`capture`, `assignedKeysByKind`); `DECISIONS.md` M23/M24; the
materialized witnesses in `tests/Projection.Tests/TransferCanaryTests.fs`.

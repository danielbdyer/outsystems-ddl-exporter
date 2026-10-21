# Resuming the v3 build after the pause of 2026-10-21

This branch, `claude/v3-session-state`, holds a build session's working state, which lived in an ephemeral container's scratchpad. It is
not part of v3 and is never merged. Pull request #705's branch, `claude/v3-architecture-design-sl95mx`, holds v3 itself.

## Where the build stands

- #705's head is `ec82637`; CI is green on all eleven checks there. The ledger after wave 7: held 28 · partly held 52 · not held yet 38 ·
  cutover tools 3 · retired 6 (127 rows). Waves B1 and 2 to 7 are merged on #705's branch; #705's description is the checklist.
- Wave 8 (run `wf_0484ba23-82e`, arguments in `waves/wave-8b-args.json`) was stopped at the operator's request after every package
  had implemented and committed. Each branch is pushed to origin, based on `ae9db10` or `28d6bd7` (`ec82637` adds only CI and one decision
  line, so each merges cleanly):

  | Branch | Commit | What | Review |
  |---|---|---|---|
  | `wp/3.6c` | `bf5f6cc` | WP 3.6: synthetic-copy check, status, down; merges `wp/3.6` (`d71f339`) | spec: fail, 1 must-fix · safety: pass |
  | `wp/H6` | `4be2756` | the review nits of waves 5 to 7; V3_PARITY.md §5 | spec: fail, 1 must-fix · safety: pass |
  | `wp/2.5a` | `e0934c5` | WP 2.5: io/Predict and predict, M2 exits 1 to 4 | spec, safety, budgets: not finished |
  | `wp/2.5b` | `50cdf1c` | WP 2.5: classify and check cdc, M2 exit 5 | not started |
  | `wp/3.7` | `4175f13` | WP 3.7: io/Profile and profile, the folded signatures | not started |
  | `wp/7.2a` | `b7c66c2` | WP 7.2: knowledge/ description, README, authoring, reviewing | not started |

  Each review's findings, with their evidence and fixes, are the `result` lines of `journals/wave-8b-wf_0484ba23-82e.jsonl`; match a
  result to its `started` line by `agentId`.

## How to continue

1. Copy this branch's folders into the new session's scratchpad (`git fetch origin claude/v3-session-state`, then
   `git archive FETCH_HEAD | tar -x -C <scratchpad>`), and run `bash ci/sql.sh up` and `dbchange doctor`.
2. Finish wave 8 as `CLAUDE.md` and `V3_BUILD_PROMPT.md` §4 *Resume* say, from the journal: the wave script is
   `workflows/v3-wave-wf_97e7638c-372.js`; save the new arguments first. Give 2.5a, 2.5b, 3.7 and 7.2a `startBranch` (their reviews run);
   give 3.6c and H6 a fix package each that branches from `wp/3.6c` or `wp/H6` and fixes the findings their reviews recorded, then is
   reviewed again. Integrate in the order H6, 7.2a, 3.7, 3.6c, 2.5b, 2.5a, then report the tally and refresh #705's description
   (`pr705-body.md` is the last version written).
3. Add `waves/wave-9-envnote-additions.txt` to every later wave's environment note: before reporting, run the fast lane with no SQL
   Server reachable, since CI's ubuntu fast job has none.
4. Wave 9: the fixture lane's cost (`specs/wave9-fixture-lane-cost.txt`: the 16 synthetic-copy tests take 1,088 s on LocalDB, each
   publishing the golden base from scratch); M2's and M3's exits that remain; then M4.
5. Essays: one and two are under `essays/`; three is published as https://claude.ai/artifact/8T12drgVMyMNzzusMsd5ad
   (`essays/03-waves-5-7.html`, with `workflows/v3-essay-three-wf_2951d1ca-d37.js`). The next follows wave 8.

## What the folders hold

- `waves/`: every wave's saved arguments with its run ID, and the rule for wave 9's environment note.
- `specs/`: the scouts' specifications, the review findings no package has applied yet, and wave 9's fixture-lane measurements.
- `journals/`: wave 8's journal, with every result and finding.
- `workflows/`: the wave script and the essay script.
- `ci-logs/`: sql-windows on `ec82637`, each fixture test with its duration.
- `essays/`, `pr705-body.md`.

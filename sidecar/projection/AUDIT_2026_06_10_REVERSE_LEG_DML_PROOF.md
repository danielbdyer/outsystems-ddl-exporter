# AUDIT 2026-06-10 — The reverse leg at full implicature: the DML-only B→A movement, proven / refused-by-name / open

> **Status: evidence report (2026-06-10).** Companion to `DECISIONS 2026-06-10 —
> J3 residual CLOSED`, `THE_DATA_PRODUCERS.md` §0/§6, and
> `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2. The J3 session wired the
> reverse leg and proved it L1 (one table, `PreservedFromSource`). This
> session drove the proof into the previously **untested intersection** —
> rendered cross-rendition contracts × sink-minted identity × multi-kind FK
> graph × DML-only principal — and reports, per constraint in the operator's
> sharpened pre-cutover premise, what is **proven**, what is **refused by
> name**, and what is **open**. Every claim below has a green test behind it;
> the test names are the citations.
>
> Verdict in one line: **the machinery is honest at L2 for the acyclic
> estate; one named refusal (the self-FK IDENTITY shape) is probably on the
> operator's critical path, and the per-row capture envelope (~271 rows/sec)
> will not survive a large production estate without the set-based follow-on.**

---

## 0. The two findings that need the operator's eyes first

**F1 — the cyclic-AssignedBySink refusal is on the critical path.** A
nullable self-FK on an IDENTITY-PK kind — `User.ManagerId`,
`Employee.CreatedBy`, bog-standard OutSystems shapes — classifies as a
deferred FK in a cycle, and a deferred FK on an `AssignedBySink` kind
triggers `transfer.cyclicAssignedBySink` (TransferRun.fs `executeGate`),
refusing the **whole load**. The refusal is honest (phase-2's `UPDATE` keys
on the source PK the sink replaced, so proceeding would silently mis-key),
and it is pinned three ways: pure (`TransferRefusalTests`), live canary
(`TransferCanaryTests` 6.A.2), and universally
(`ReverseLegPropertyTests` — refusal totality over generated shapes). **The
lift is tractable**: the `SurrogateRemapContext` already knows
source → assigned for every minted row, so phase 2 can key its `WHERE` on
the **assigned** PK and re-point the deferred value through the same remap.
That is an engine change for the operator to authorize, not something this
session did unilaterally. The contract is reserved:
`ReverseLegBoundaryTests` › *RESERVED — cyclic AssignedBySink lifted*. The
prevalence question for the real estate: **does any in-scope kind carry a
self-FK (or same-cycle FK) with an IDENTITY PK?** If yes, the reverse leg
refuses that estate today.

**F2 — the per-row capture envelope is measured: ~271 rows/sec.** The
16-kind, all-`AssignedBySink` mesh (chain + diamond edges) moved 4,000 rows
B→A end-to-end in ~14.8 s on the warm container
(`ReverseLegScaleTests` › *Tier 4 envelope*; Bench label
`transfer.reverseLeg.scale.legMs`). That covers ingest + plan + per-row
`INSERT … OUTPUT` + FK re-point. Extrapolated: 100k rows ≈ 6 min, 1M rows ≈
60+ min, on local-loopback latency — a real network hop multiplies the
per-row round-trip cost. **Per-row capture is fine for the mock/UAT-preview
scale; it will not survive a large production estate.** This is exactly the
bench evidence the trigger-gated `MERGE … OUTPUT` set-based capture
(TRANSFER_ISOMORPHISM_SUBSTANTIATION §3) was waiting for: trigger (b)
[measured bottleneck] is now satisfied; trigger (a) [survey P4 under the
real UAT grant] remains.

---

## 1. The operator's premise, constraint by constraint

| # | Constraint (the operator's words) | Verdict | Evidence |
|---|---|---|---|
| 1 | **DML-only on the sink.** No DDL, no ALTER, no `SET IDENTITY_INSERT`; the OutSystems database stays responsible for auto-numbering. | **PROVEN** on mock infrastructure | The full-shape leg succeeds as a login granted exactly `SELECT, INSERT, UPDATE, DELETE` at database scope (`ReverseLegCanaryTests` › *DML-only principal: the full reverse leg succeeds…*). `SET IDENTITY_INSERT` is **denied** to that grant (probe P3, expected-denied, pinned). The `WipeAndLoad` refresh runs child-first `DELETE` (never `TRUNCATE`) and re-runs clean under the same grant. A `SELECT`-only sink refuses by name (`transfer.insufficientGrant`) before any row moves. |
| 2 | **Sink-minted identity, retained for relationality.** PKs generated at insert; every FK re-pointed to the minted key. | **PROVEN** (acyclic graphs) | Keystone canary: colliding source key spaces (every table's IDs ≥ 1000); after the leg, **zero** source keys survive in the sink, and **every FK edge's business-key join is identical across renditions** — including the NULL nullable FK riding through as NULL. At scale: per-edge order-independent join checksums match for all 29 edges of the 16-kind mesh. The remap algebra is a law: `remapRowFks` re-points exactly the captured targeted columns and nothing else (property (c)). |
| 3 | **Two-phase, topologically ordered insert.** | **PROVEN as law** | Order soundness over generated graphs: every FK edge's target strictly precedes its referencer, or the column is deferred (and provably nullable — phase 1 can NULL it), or the edge is the **named** `UnbreakableCycleFk` (properties (a)). The phase-2 re-point on `AssignedBySink` kinds is the F1 refusal — see §0. |
| 4 | **Every cross-boundary erasure is named.** | **PROVEN at the data plane; two NAMED GAPS at the shape/grant plane** | The FK-orphan row is dropped **by name** — `SkippedReferences` carries (owning kind, column, target, unresolved source) and the run maps to exit 9, downgraded only by explicit `--allow-drops` (keystone canary). Unsupported shapes land on their exact named codes universally (property (d)). The gaps: see §2 (G1, G2). |
| 5 | **Everything inside R6** (`--go` + `PROJECTION_ALLOW_EXECUTE`; the cloud sink is `grant: data`). | **HOLDS** | The CLI face's gates were wired at J3 (execute env-gate → 7; reconcile/rekey refusal); this session pinned the reverse-leg-specific refusal live: reconcile specs AND user-maps exit 2 with `transfer.reverseLeg.reconcileUnsupported` (`ReverseLegBoundaryTests`). No engine change was made; all evidence is test-side. |

**The ladder position** (AUDIT_2026_05_31 §0.3): the reverse leg moves from
L1 (single-table witness) to **L2 on the acyclic estate** — Ingest ∘ Project
= id modulo a **named and closed** erasure set (the orphan drop-set, the
empty-text tolerance, the three shape refusals). The L3 witness (the axis
composed under one command against a real grant envelope) is scoped in §4.

---

## 2. The named gaps (pinned, not fixed — each carries a reserved contract)

**G1 — object-scope DENY escapes the database-scope grant probe.**
`Preflight.captureGrantEvidence` reads `sys.fn_my_permissions(NULL,
'DATABASE')` only. A principal with database-scope DML but a table-level
`DENY INSERT` **passes the preflight and crashes mid-load** — upstream kinds
already landed: a **partial write** (pinned:
`ReverseLegCanaryTests` › *NAMED GAP*). The survey-gated P1 object-scope
refinement is the fix; reserved in `ReverseLegBoundaryTests`. Until it
lands, the operational mitigation is `WipeAndLoad` re-run after remediating
the grant.

**G2 — B's live shape drifting from the rendered contract.** Two pins:
a column the contract names but live B lacks dies inside the ingest SELECT
with a **raw SqlException** (`Invalid column name`) — an unnamed refusal;
a live-B column **outside** the model is silently not moved (the model is
the boundary of what crosses — correct by the §0 framing, but invisible
per-run). The named `transfer.sourceShapeDrift` preflight is reserved.

**G3 — the idempotent re-run story is open.** The data path is INSERT-based
and the sink mints fresh surrogates, so a second Incremental Execute into a
populated A **duplicates every AssignedBySink row** — no PK collision warns
(pinned: `ReverseLegCanaryTests` › *re-run honesty*). Today's honest modes,
both proven under the DML grant: `WipeAndLoad` (child-first DELETE then
reload — counts hold, no dupes) and the G10 resumable marker (not exercised
on the reverse leg this session). A true idempotent re-run (business-key
upsert) is unscoped.

---

## 3. The norm, the throughput, and what they say

- **Isometry holds.** With CDC enabled on the mock cloud sink
  (`IsolatedContainerFixture` per the CDC-isolation rule), the first
  reverse-leg load into empty A captures **exactly** row-count change rows —
  ‖δ‖ = CDC capture count = row count (`ReverseLegCdcNormTests`). The
  engine's claim of minimum viable data movement is a measurement, not a
  slogan. (Corollary, documented in `EmissionMode`: a `WipeAndLoad` refresh
  costs 2·|rows| on the CDC norm.)
- **Throughput** — see F2 (§0): ~271 rows/sec end-to-end on the per-row
  capture path, warm container, loopback. Bench label
  `transfer.reverseLeg.scale.legMs`.

---

## 4. What genuinely remains for J5 (the real-UAT spike, now collapsed)

P1 (grant enumeration), P2 (insert-omitting-identity + read-back), P3
(IDENTITY_INSERT expected-denied), P6 (DELETE-based clear) are **answered on
mock infrastructure** by this suite — the spike shrinks to re-running a
proven suite against a real connection. Genuinely open against the real
estate:

1. the **actual OutSystems grant envelope** (object- vs database-scope; G1
   interacts directly);
2. **platform triggers on OSUSR tables**, if any (per-row INSERT…OUTPUT is
   rejected by SQL Server when the target has triggers — that would force
   the `OUTPUT INTO` form, survey P5);
3. **P7 batch ceilings** and real-network round-trip cost (re-run the Tier-4
   envelope over the wire before trusting the 271 rows/sec floor);
4. **P10** (user directory) — only when the reconcile ∘ rendition follow-on
   (the reserved cloud-owns-its-users contract) is authorized.

---

## 5. Test inventory (this session; all green in both pools)

| Tier | File | What it proves |
|---|---|---|
| 1 | `ReverseLegCanaryTests.fs` — keystone + apparatus | The untested intersection closed: rendered contracts × AssignedBySink × depth-4 chain + diamond × business-key join fidelity × named orphan drop (exit 9) × sink-minted surrogates, through `runWithRenames` AND `runReverseLegThroughConnections`; `WipeAndLoad` re-run no-dup. |
| 2 | `ReverseLegCanaryTests.fs` — DML-principal trio + DENY pin | The whole statement vocabulary fits `grant: data`; P3 denied; `transfer.insufficientGrant` pre-write; G1 pinned. |
| 3 | `ReverseLegPropertyTests.fs` (8 laws, FsCheck) | Order soundness; deferral nullability; disposition totality; remap algebra; refusal totality over generated unsatisfiable shapes; rendition invariance ×3 (incl. the identity rename map). |
| 4 | `ReverseLegScaleTests.fs` | The capture envelope (F2) + the CDC isometry norm. |
| 5 | `ReverseLegCanaryTests.fs` (drift pins) + `ReverseLegBoundaryTests.fs` | The boundary map: CLI reconcile/user-map refusal live; 4 reserved contracts (reconcile ∘ rendition; F1 lift; shape-drift preflight; object-scope grants). |

---

## 6. Decision asks (the operator's calls, ranked)

1. **Authorize the F1 lift** (phase-2 keyed on the assigned PK through the
   captured remap) — or confirm no in-scope kind is a self-referencing
   IDENTITY shape. Everything else about the reverse leg is ready for the
   estate this refusal excludes.
2. **Schedule the MERGE…OUTPUT set-based capture** behind survey P4 — the
   measured-bottleneck trigger is now satisfied (F2).
3. **Accept or prioritize** the G1/G2 reserved preflights (object-scope
   grant probe; source-shape drift) — both are partial-write/unnamed-crash
   hazards on the real leg, neither blocks the mock-scale proof.

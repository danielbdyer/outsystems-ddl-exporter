# AUDIT 2026-06-10 — F# Projection Review (three-agent study + session verdicts)

**Provenance.** Session-opening study, three parallel agents over
`/sidecar/projection` at HEAD `cd89e13` (pre-branch): (1) a documentation-corpus
survey (README / CONFIRMED_BACKLOG / HORIZON / audits — the project's own
self-assessment); (2) an independent source-architecture audit (dependency
structure, the six largest files, cross-cutting smells, error flow); (3) a
tests + CLI wiring survey (suite inventory, verb dispatch, reachability gaps).
The findings drove the `claude/fsharp-projection-review-vrbtpr` branch
(PR #591). **Every finding below carries its end-of-session verdict** — this
document is a dated record, not a live ledger; `CONFIRMED_BACKLOG_2026_06_09.md`
remains the living "what's left" surface.

---

## 1. Characterization (agent 2, confirmed by the session)

A maturely-engineered system whose refactoring opportunities are
quality-of-life, not broken invariants. Verified at study time:

- **Clean stratification, no layering inversions**: Core (pure) ← Adapters /
  Targets ← Pipeline (the composition hub, 9 transitive deps — sound for its
  role) ← Cli.
- **Discipline is real, not aspirational**: zero TODO/FIXME markers (deferrals
  live in DECISIONS + named Skip stubs); zero unsafe `.Value` on domain values
  in production; ~95% of string composition at terminal boundaries under
  LINT-ALLOW with rationale; error flow uniformly `Result<'a, ValidationError
  list>` at boundaries with the Kleisli `Pass<'a,'b>` dual-writer inside.
- **The custom analyzer** (`NoUnsafeTimeInCoreAnalyzer`) is a finished,
  actively-maintained Core-purity enforcer, not scaffolding.
- **Test suite**: ~260 files mixing unit (~200), FsCheck property (~80),
  Docker-serial canary/integration (~350), and the executable axiom catalog
  (`AxiomTests.fs`, buckets per the verifiability triangle); ~40 deliberate
  Skip stubs each naming its promotion trigger.

The codebase's signature near-miss pattern was NOT half-built features but
**finished, tested capabilities with no caller** — "the shelf."

## 2. The top-five refactors (the review's ranking) — verdicts

| # | Finding | Verdict (2026-06-10 EOD) |
|---|---------|--------------------------|
| 1 | `Program.fs` (2,151 lines; mixed concerns: console substrate + run faces + dispatch + mutable mode refs) | **SHIPPED** — split into `OperatorConsole.fs` / `RunFaces.fs` / 379-line dispatcher (`80b8ba2`). Optional follow-on: family-split of `RunFaces` (now self-contained). |
| 2 | `Emitter.perKind` combinator (B1, ~7 duplicated per-kind loops) | **Already CLOSED pre-session** — shipped 2026-06-09 (`ac594ba`, 9 sites); the study's doc-pass caught this, the independent audit's "no duplication" claim was the post-fix tree. |
| 3 | "DacFx vs ScriptDom ALTER drift" — dual renderers for schema change | **REFRAMED + WITNESSED, CLOSED** — not drift but the documented decision boundary (declarative dacpac vs imperative in-place executor; `WAVE_6_ONTOLOGY §4`). The genuinely missing piece was a live witness: `DacpacPublishEquivalenceTests` (Docker pool) now proves dacpac-publish ≡ bundle-deploy on `PhysicalSchema`. Do not re-open the "unify the ALTER surface" framing (DECISIONS 2026-06-10). |
| 4 | `Config.fs` (1,4xx lines) syntax-parse / semantic-lift interleaving; finish the config control plane | **HALF-CLOSED** — the control-plane unification (S1–S6.4, A44 residual ∅) was already done by the project; the session added the J2 reconcile field + deleteScope to the same surface. The *parse/interpret module split itself* remains **OPEN** (quality-of-life; the seam at the `parsePolicy`/`parseSelection` cluster is clean). |
| 5 | `ReadSide.fs` drain-loop combinator (B3) + `Deploy.fs` decomposition (B7) | **Largely CLOSED pre-session** (2026-06-09: `drainRows` ×13; Deploy → 3 modules + facade). Residuals: ReadSide parallelization (the lever B3 unblocked, unpulled) and `Deploy.fs`'s remaining ~1,300-line facade body. **OPEN**, low urgency. |

## 3. The independent code-audit items (agent 2's eight) — verdicts

1. **CLI verb routing extraction** — SHIPPED (see §2.1).
2. **Config syntax/semantics split** — OPEN (see §2.4).
3. **Adapter error-boundary propagation** — OPEN. `Adapters.OssysSql`'s
   `MetadataExtractionError` (closed DU + classify + toValidationError) is the
   reference implementation; `Adapters.Sql` (ReadSide/LiveProfiler) still
   catches/wraps inline without a typed intermediate. Pairs with the B4 retry
   deferral (re-open under a real transient-failure incident).
4. **ScriptDomBuild low-level builder extraction** (bracketed identifier /
   schemaObjectName / threadLocalParser as a shared typed-SQL substrate) —
   OPEN; pull at the second emitter consumer (two-consumer threshold not met).
5. **EvidenceCache discovery-then-derive consolidation** into a reusable
   `Cache<'discovered,'axis>` shape — OPEN-by-design; the pattern is already
   codified as an operating discipline (DECISIONS 2026-05-19); reify the type
   when a third consumer appears.
6. **Test-fixture builder ergonomics** (B6 tail / B9) — PARTIAL; B6's `mkName`
   promotion shipped 2026-06-09; **B9** (19 files of raw `Kind` literals;
   codified trigger: an indentation-preserving Python pass,
   `CHAPTER_4_8_CLOSE.md:125`) is the next tractable code item.
7. **Adapter bench observability** (no `Bench` samples inside ReadSide /
   MetadataSnapshotRunner SQL calls — adapters invisible in the rollup) —
   OPEN; low effort, real operator value; candidate for any perf-focused
   session.
8. **`.Value`-outside-tests analyzer** (structural enforcement of the
   test-zone unwrap convention) — OPEN; hedge-priority.

## 4. "On the cusp" (the study's map) — verdicts

- **The wiring shelf** (resumable / scoped delete / rename-aware migrate /
  `diff` verb / dacpac / manifest-only / ModuleFilter / seed-scale knobs):
  **EMPTY as of this branch** — every item either pre-shipped (2026-06-09) or
  shipped here (§⓪′ of the backlog).
- **Cloud-insertion cutover (the big one)**: unchanged — all three producer
  canaries green; **J5** (real-UAT execute, OPEN-2) is the critical path and
  is operator-gated on a writable connection; then the R6 N=10 ladder.
- **Reverse-leg runner arm (J3 residual)**: deliberately unforced pending a
  contract source (shared authored model in both renditions, or
  attribute-scope `V2.SsKey` recovery in `ReadSide.buildAttribute`).
- **A7 polarity**: RESOLVED 2026-06-10 — opt-in stands; the inert combination
  now carries a named note + `moduleFilter.flagsInert` diagnostic.

## 5. Corrections the session made to the study (read before trusting §2–§4)

- The survey's **"~2–3 LOC" estimates for dacpac/manifest exposure were
  wrong** — each was a real slice (config default semantics + atomic-write leg;
  a new `Shape` variant through parse/render/plan/runner/totality).
- The survey's "**dacpac executed at its own site**" reading of the registry
  was generous — `DacpacEmitter.emit` had zero production callers until this
  branch.
- Agent 2's "no copy-paste between adapters / no duplication" conclusions
  described the post-2026-06-09 tree; the backlog's duplication counts (B1/B3/
  B6) had been real and were already fixed. When the doc corpus and a code
  audit disagree on debt, check `git log` first — the debt may have died
  between the doc and the audit.
- The backlog's "--rows N" phrasing for D8 was loose; the design's vocabulary
  (`--scale` factor + `--seed`) is what shipped.

**Certification at close:** pure pool green at every commit; Docker pool
195/195 (incl. the equivalence witness); operator-reality perf gate clean
(one borderline label flaps under host load — three clean runs against two
loaded-host flags; no floor shift); analyzers clean; lint delta zero.

# Partial-Transfer Production-Readiness Log — 2026-07-06

> Append-only operator log for the partial-transfer readiness program: making the
> verb/flow that moves a **subset of tables** between managed Cloud OutSystems
> environments (e.g. QA → UAT, differing physical table names, SS_KEY-matched
> schema shape) ready for a live operator test later today. Newest entries at the
> bottom. Plain description only — findings, accomplishments, decisions taken.

---

## Entry 1 — 2026-07-06, session start: orientation

- Session opened on branch `claude/fsharp-partial-transfer-production-1p4vpw` (clean tree,
  based on `main` @ 4ba720a).
- Read `CLAUDE.md`, the top `HANDOFF.md` letter (operator-shell chapter, 2026-07-03), and
  `src/Projection.Cli/Faces/Transfer.fs` in full.
- Confirmed the feature surface already exists in some depth:
  - `runTransfer` — the forward `transfer` face: dry-run by default, `--execute` gated on
    `PROJECTION_ALLOW_EXECUTE=1`, `--tables` subset selection, `--reconcile
    <table>:<column>` + `--user-map` CSV re-keying, revert policy, resumable runs,
    drop-set fail-loud exits.
  - `runReverseLegTransfer` — the reverse (B→A) leg: takes a **logical source contract**
    and a **physical sink contract** (two SsKey-aligned renditions of one authored
    model) — this is precisely the "same schema shape, different physical table names"
    cross-environment situation. Streaming vs materialized realization chosen by
    `ReverseLegRealization.choose`; journal-gated execute.
- Directly relevant docs identified for the deep study: `THE_USE_CASE_ONTOLOGY.md` (+ its
  acceptance/fitness/obligations satellites), `PRESCOPE_TRANSFER.md`,
  `CROSS_ENVIRONMENT_READINESS.md`, `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`,
  `PREFLIGHT_CLOUD_INSERTION.md`, `J5_MANAGED_ENV_CAPABILITY_PLAYBOOK.md`,
  `REVERSE_LEG_WORK_PLAN.md`, `AUDIT_2026_06_10_REVERSE_LEG_DML_PROOF.md`.
- Next: four parallel deep-study sub-agents (engine, docs/ontology, test coverage, CLI
  dispatch + contract acquisition) to map the current state and find blind spots.

## Entry 2 — 2026-07-06, environment readiness + study fan-out

- Launched four parallel deep-study agents:
  1. **Engine** — Projection.Pipeline transfer flow, `--tables` subset semantics, SsKey
     alignment, FK closure/cycle handling, sink insertion mechanics.
  2. **Ontology/docs** — the use-case ontology + acceptance/obligations satellites,
     transfer prescope, cross-environment readiness, cloud-insertion preflight,
     reverse-leg program status, open backlog items.
  3. **Test estate** — existing transfer/e2e docker tests, two-environment fixture
     patterns, subset + schema-validation coverage, gap list with templates to copy.
  4. **CLI/config** — exact command grammar, contract acquisition (CatalogRendition,
     capture verbs), preflight/compare verbs, environment/connection story, worked
     examples.
- Local substrate verified ready for e2e work: dotnet SDK 9.0.314, Docker 29.3.1, and the
  **warm SQL Server 2022 container is already up** (`projection-mssql-warm`, port 11433).
  Full solution build kicked off in the background.

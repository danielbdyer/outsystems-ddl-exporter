# PLAN 2026-06-14 — Live-source verification + Bootstrap-always (the Docker chapter)

> Branch `claude/projection-invariant-audit-b6iknj`, PR #607. This plan finishes
> the operator-requested chapter started 2026-06-14. **Steps 1, 2, 4 are SHIPPED**
> (build-verified, on the PR); this plan covers the remaining **Docker
> verification** of the live path and **Step 3 (Bootstrap-always)**. Read
> `HANDOFF.md`'s top letter first, then this.

---

## 0 — What the operator asked for (verbatim intent)

1. **`compare` source-profiling** — live-profile the source operand so the
   data-dealbreaker section is populated. → **SHIPPED** (Step 4, `bdd7e799`),
   build-verified; **needs a Docker smoke test** (below).
2. **Live-source adapter** — `live:` refs resolve. → **SHIPPED** (Step 2,
   `42e0dbd0`), build-verified; **needs a Docker smoke test**.
3. **Retire `Data/seed.sql` as a file; per-lane files are the artifacts; the
   fused notion stays internal.** → **SHIPPED** (Step 1, `77bf5ee4`), fully
   verified (goldens re-blessed).
4. **"Bootstrap should be created as well, basically always, both testing with
   Docker and live source."** → **Step 3, NOT done** — the hard part. See §3.

---

## 1 — Ground truth (seams already wired this session)

- **`Source.ofLive conn`** (`src/Projection.Pipeline/Source.fs`) — the live OSSYS
  adapter. `ReadCatalog` = `ReadSide.read cnn` (INFORMATION_SCHEMA → `Catalog`);
  `AcquireProfile` = `LiveProfiler.attach cnn catalog Profile.empty`. `conn` is a
  raw connection string or `env:VAR` (`resolveConnString`). Each capability opens
  its own short-lived `SqlConnection` (`new SqlConnection` + `OpenAsync`).
- **`Ref.resolveSource` / `Ref.resolveCatalog`** (`Ref.fs`) — `live:` refs now
  resolve (was `fail "ref.liveUnavailable"`). `resolveSource` returns the
  capability-typed `Source`; `resolveCatalog` reads the catalog only.
- **`runCompare`** (`src/Projection.Cli/RunFaces.fs:~1239`) — resolves the SOURCE
  operand as a `Source`, live-profiles it when it can, threads the `Profile` into
  `Compare.compute`. Profiling failure → advisory-silent (never aborts).
- **Per-lane artifact contract** (`Pipeline.fs:~730-746`) — `DataBundle` =
  `RenderedDataBundle.perLaneFiles bundle` (each non-empty lane emits its file;
  the ≥2-lane gate is retired; `Data/seed.sql` is no longer written).
  `bundle.Fused` stays in-memory for the leveled deploy (`LeveledDeploymentText`),
  which never reads a `seed.sql` file — so deploy behaviour is unchanged.

---

## 2 — Step A: Docker smoke tests for the live path (Steps 2 + 4)

**Goal:** prove `live:`-ref catalog readback + `compare` live-profiling work
end-to-end against a real SQL Server.

**The fixture surface (already exists):** `tests/Projection.Tests/EphemeralContainerFixture.fs`
- `fixture.MasterConnectionString : string`
- `fixture.WithEphemeralDatabase "prefix" (fun cnn dbName -> task { … })` — creates
  a throwaway DB, hands you an open `SqlConnection` + the db name, reaps on exit.
- `Deploy.ConnectionString.buildPerDb masterConn dbName : string` — the per-DB
  connection string (this is what you feed to `live:`).

**New test file:** `tests/Projection.Tests/LiveSourceDockerTests.fs` (add to
`Projection.Tests.fsproj` after `EphemeralContainerFixture.fs`). Mark
`[<Xunit.Collection("Docker-SqlServer")>]`; soft-skip on
`Deploy.Docker.ensureRunning ()` (mirror `CanaryRoundTripTests.fs`).

**Test 1 — live catalog readback:**
1. `WithEphemeralDatabase "LiveSrc"` → deploy a small schema to `cnn`
   (reuse `SourceFixtures.SourceSchema.minimal` via `Deploy`, or execute a
   `CREATE TABLE` directly on `cnn`).
2. `let connStr = Deploy.ConnectionString.buildPerDb fixture.MasterConnectionString dbName`
3. `let cat = (Ref.resolveCatalog (Ref.parse ("live:" + connStr))).GetAwaiter().GetResult() |> mustOk`
4. Assert the reconstructed `Catalog` carries the deployed table(s)
   (`Catalog.allKinds cat |> List.exists (fun k -> Name.value k.Name = "…")`).
   **Watch:** `ReadSide` marks reconstructed data-bearing tables `Static` (the 4.4
   trap) — compare via `PhysicalSchema` projection, not raw SsKey, if you compare
   to another adapter's catalog.

**Test 2 — compare live-profiling end-to-end:**
1. Deploy schema **B** (the target model) to one ephemeral DB; deploy schema **A**
   (the source env) to a second, and INSERT a row into A that *violates* B's
   declared model (e.g. a value B would reject) — the dealbreaker.
2. `runCompare ("live:" + connA) ("live:" + connB) true` (or call
   `Compare.compute` with operands resolved via `Ref.resolveSource` +
   `Source.profile`), then assert `report.DataEvidenceAvailable = true` and
   `report.DataDealbreakers` is non-empty (the injected violation surfaces).
3. Negative: a file/`@runId` source → `DataEvidenceAvailable = false` (advisory-silent).

**Verify:** `scripts/warm-sql.sh status` first; run the focused class
(`scripts/test.sh focus LiveSourceDockerTests` or `dotnet test --filter`). If you
get `Could not open a connection` mid-pool, the warm container died — survival
rule #2: `scripts/warm-sql.sh restart`, re-run focused.

---

## 3 — Step B: Bootstrap-always (the hard part — has a real open question)

**Operator intent:** `Data/Bootstrap.sql` should be created "basically always,
both testing with Docker and live source."

**Why it's empty today:** `BootstrapEmitter.emitWithTopo` (`src/Projection.Targets.Data/
BootstrapEmitter.fs:116`) builds its plan from **`Map.empty`** rows. The composer
(`DataEmissionComposer.dispatchSiblings`) calls it with no row source. And
`Hydration.hydrateCatalog` (`src/Projection.Pipeline/Hydration.fs:117`) grafts live
rows **only onto Static-marked kinds** (`Ingestion.collectInOrderFor`, scoped to
static kinds) → those become the **StaticSeeds** lane, not Bootstrap. So nothing
ever feeds the Bootstrap lane. With Step 1's per-lane rule, the *file* emits the
instant the lane has content — the only missing piece is **the content**.

**THE OPEN QUESTION (resolve with the operator before coding):** *what is a
"bootstrap-eligible" kind, and where do its rows come from?* `BootstrapEmitter`'s
docstring says "system users, default policies, and any remaining-by-policy kinds
whose data is not in StaticSeeds or MigrationDependencies" — i.e. the **complement
of (Static ∪ Migration)**. Candidates for the row source:
  - (a) **Live hydration of the complement kinds** — extend hydration to also
    stream rows for non-static, non-migration kinds the operator designates as
    bootstrap-bearing (a policy: which kinds? a config list? a modality mark?).
  - (b) **An operator-supplied bootstrap context** (like
    `MigrationDependencyContext`) — explicit rows, no inference.
  - (c) **A `ModalityMark.Bootstrap`** (or reuse `SystemOwned`) marking the
    bootstrap-bearing kinds, hydrated like Static.
Pick with the operator — this is a domain decision, not a mechanical one. The
partition law (`unionSiblings` → `OverlappingEmitterCoverage` `invalidOp`) requires
bootstrap kinds to be **disjoint** from static + migration.

**Implementation once the source is decided:**
1. Add the bootstrap row source (per the chosen option) — likely a
   `Hydration.hydrateBootstrap` sibling that streams the complement kinds, plus a
   `BootstrapDependencyContext` or a marking.
2. Thread it through `DataEmissionComposer.dispatchSiblings` →
   `BootstrapEmitter.emitWithTopo` (replace the `Map.empty` at
   `BootstrapEmitter.fs:116` with the populated plan), mirroring how
   `MigrationDependenciesEmitter` takes its context.
3. `Data/Bootstrap.sql` then emits via the per-lane rule (Step 1) — no Pipeline
   emission change needed.
4. **NM-73 already covers it:** the validate-before-apply guard is on both
   `StaticSeedsEmitter` and `MigrationDependenciesEmitter`; Bootstrap delegates to
   `StaticSeedsEmitter.emitFromPlanWith` (`BootstrapEmitter.fs:88`) — confirm it
   threads `DataVerification` (today it passes the default `Standard`; wire
   `emitFromPlanWithVerification` if the operator wants the guard on bootstrap).

**Goldens / tests:**
- **Non-Docker:** extend `DataLaneGoldenTests` with a bootstrap-bearing scenario
  (once the row source is a pure/in-memory context) → `Data/Bootstrap.sql` joins the
  blessed `Golden/data-lanes/Data/` corpus (`GOLDEN_RECORD=1` to bless; DECISIONS
  note). Update the `DoesNotContain "Data/Bootstrap.sql"` assertion there.
- **Docker:** a hydrated scenario (live source) where Bootstrap carries content,
  asserting `Data/Bootstrap.sql` is emitted + byte-shaped — the operator's
  "testing with Docker and live source."

---

## 4 — Sequencing + guardrails

1. **Step A first** (Docker smoke tests) — verifies Steps 2/4, small, unblocks
   confidence in the live path.
2. **Resolve the Step B open question with the operator**, then implement + golden.
3. Commit each piece; push to PR #607. **Re-bless goldens only with a DECISIONS
   note** (THE_GOLDEN_EMISSION.md §2).

**Survival rules that will bite (CLAUDE.md §4):** never run pure+Docker as one
`dotnet test` (OOM) — use `scripts/test.sh`; CDC classes use
`IsolatedContainerFixture`; a batch of `Could not open a connection` = dead warm
container (`warm-sql.sh restart`), not a regression; FS3511 in Release (no `let rec`
/ tuple `let!` in `task {}`); commits show "Unverified" (empty SSH signing key —
environmental).

**PR housekeeping:** the PR #607 body still lists only the first 3 commits — the
`gh pr edit` needs `gh auth refresh -s read:project`. Update it (the full 8-commit
summary is in the 2026-06-14 HANDOFF letter).

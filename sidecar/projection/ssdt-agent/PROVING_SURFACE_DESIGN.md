# The local proving surface — a system design

**Status: DESIGN (2026-08-26). Nothing here is built beyond what section 4 names as already
existing.** This document answers the question the owner posed after the assessment: what stack
and shape should the local proving surface take — the local development experience that lets a
developer, and the agent helping them, prove a schema change before classifying it. The answer is
derived in order: first the invariant cases every candidate must serve (section 3), then a survey
of the machinery that already exists in this repository (section 4), then the design (section 5),
the build list (section 6), and what stays unverified (section 7).

Three prior surfaces motivate this document. `ASSESSMENT_2026_08_24.md` names the proving path on
the team's machines as the prerequisite that gates everything else. `copilot-package/ADOPTION.md`
tells the team plainly that the Copilot bundle cannot provide that prerequisite. And
`FINDINGS_AND_CHANGES.md` Part 5 records the operator's own framing: a stable SQL target must be
provisioned in the environment so the prove step can always run — otherwise agents are pushed
back toward guessing, which the whole method exists to prevent.

---

## 1 — The promise the surface must keep

On the developer's machine, a small number of commands produce a disposable SQL Server database
that matches the estate's trunk at a named commit and holds data shaped like Dev's. A pinned
`sqlpackage` publishes the change under test against that copy, and the developer's agent reads
what the deployment engine did. The copy resets to its starting state in seconds. A reviewer
stands up an identical copy on a different machine without trusting the author. No publish from
this surface can reach a real environment.

Everything below serves that promise. The proving loop itself — build the dacpac, preview the
generated delta, publish under the Strict profile, publish under the Permissive profile only
after a block, reset, re-prove clean — is already settled in `skills/prove-on-dacpac/SKILL.md`
and `skills/talk-to-local-sql/SKILL.md` and does not change here. This design is about what those
skills publish *against*: where the database comes from, what data it holds, how it stays
current, and how it reaches a Windows laptop.

---

## 2 — Vocabulary used below

- **The estate repository** — the team's SSDT repository in Azure DevOps: the `CREATE TABLE`
  scripts, the refactorlog (the file that records a rename, so SSDT keeps the data instead of
  dropping the column), the pre- and post-deployment scripts, and the publish profiles.
- **The trunk head** — the commit on the estate repository's main branch that a change is being
  authored against.
- **The BEFORE state** — the disposable copy's contents at the start of a proof: the trunk-head
  schema plus the data, before the change under test is published.
- **The template** — the BEFORE state frozen into distributable files, so standing up or
  resetting a copy is a restore rather than a rebuild. Section 5.2 defines it precisely.
- **The verdict layer** — the pinned `sqlpackage` publishes (Strict, Permissive, and the
  pipeline-replica profile) plus the data probes and the content-hash check. The verdict layer
  reads what the engine does; it is the proof.
- **The Twin** — the F# tool in this repository (`../THE_TWIN.md`) that holds a local SQL Server
  current with an SSDT repository's definitions and fills it with deterministic synthetic data.

---

## 3 — The invariant cases, and the requirements they force

These are the cases the tree already proves today, plus the cases the estate facts add. Each one
constrains the surface. A candidate form factor that cannot serve one of them fails, however
convenient it is otherwise.

### 3.1 — What the engine does with the data

**Case 1 — row presence decides the tightening family.** Making a populated column `NOT NULL` is
refused while the table holds any rows, and it is still refused after every blank is cleared,
because the generated guard fires on row presence. The same edit on an empty table applies in
place. A narrowing that every value fits still blocks the same way
(`skills/prove-on-dacpac/SKILL.md`, the make-mandatory finding; `proving-ground/README.md`, the
seed-scenario map). So the copy's per-table row presence must match Dev's — a table that is
populated in Dev must be populated on the copy — and the loop must be able to produce the
empty-table contrast when a case needs it.

**Case 2 — a constraint add is a claim about every existing row.** An orphan row blocks a foreign
key add with `Msg 547`; a duplicate blocks a unique index; an over-length value blocks a
narrowing. Clean data validates, and the constraint ends trusted (`is_not_trusted = 0`). A
blocked publish is non-atomic and can leave the constraint present but untrusted
(`FINDINGS_AND_CHANGES.md` §6.2). So the copy must either hold the violating rows Dev really
holds, or accept an injected violating row so the exact refusal is captured — the violating-row
probe in `skills/prove-on-dacpac/SKILL.md` — and the loop must be able to query `sys.*` state on
the copy after every publish.

**Case 3 — a rename is an identity question the generated delta answers.** With a refactorlog
entry, the delta is `sp_rename` and the data survives. Without one, the diagnostic posture shows
`DROP` + `CREATE`, and the production posture produces a phantom — a green publish that creates
the new table empty and strands the populated original (`sample-prs/rename-entity.md`,
`sample-prs/delete-entity.md`). A table rebuild (an identity swap, a temporal conversion) is also
visible only in the delta. So the publish under test must consume the real project — refactorlog
included — and the loop must be able to preview the generated script without applying it, under
both drop postures.

**Case 4 — deployment scripts run at deploy time, and idempotency is proven by silence.** The
two-release tightening lands because release one runs the backfill and the `ALTER` in a
pre-deployment script with the model still declaring the old shape. A guarded seed `MERGE` proves
itself by affecting zero rows on a re-publish, with an unchanged content digest. So the publish
under test must execute the project's pre- and post-deployment lanes, and the content-hash query
(`skills/talk-to-local-sql/SKILL.md`) must run against the copy.

**Case 5 — the consequence past a block must be observable.** After Strict blocks, the Permissive
publish proceeds so the copy shows exactly which rows a smart default would stamp or a truncation
would cut. So the copy must tolerate a publish that rewrites or drops its data, because being
mutated and then read is part of its job.

### 3.2 — What keeps a proof trustworthy

**Case 6 — publishes contaminate the copy, so every clean re-run starts from a reset.** A blocked
publish can leave an untrusted constraint behind; a Permissive publish mutates data by design.
The proof that a remedy works is a clean Strict publish from an uncontaminated BEFORE state
(`skills/prove-on-dacpac/SKILL.md`, the reset rule). A single proof can reset two or three times.
**The reset cost bounds the whole method**: when a reset takes minutes, an agent is pushed toward
skipping it, and a proof taken over residue proves nothing.

**Case 7 — a change can span releases, and the estate can move between them.** The two-release
pattern needs release one proven to land and release two proven to be a no-op, in order, on the
same copy. The known revert hazard — a second developer's unrelated publish carries the lagging
model and undoes the tightening while every check stays green (`ASSESSMENT_2026_08_24.md` §6) —
is rehearsable on the same surface: publish release one, then publish the unrelated head model
over it, and watch the tightening revert. So the copy must persist across ordered publishes
within one proof, and a second model state must be publishable over the first.

**Case 8 — the reviewer reproduces the proof without trusting the author.** The reviewer's
protocol reproduces the author's claimed outcome on an isolated database
(`self-test/PROTOCOL.md`). That reproduction is only evidence when the reviewer's BEFORE state is
known to equal the author's. Identical bytes from the same named artifact settle that more
strongly than a re-run of any generator. So the BEFORE state must be reproducible on another
machine, from a source both sides can name.

**Case 9 — proofs run in parallel and must not collide or leak.** The self-test fleet runs many
provers against one instance; each owns a unique database and a private scratch copy of the
project, and reaps both on exit — one past session leaked 209 databases and degraded the shared
instance (`self-test/PROTOCOL.md`; the parent `../CLAUDE.md` survival rules). So isolation lives
at the database grain, names carry a unique suffix, and teardown is idempotent.

### 3.3 — Fidelity to the estate

**Case 10 — guard behavior is bound to the engine version.** A declarative foreign-key add ends
trusted on sqlpackage 170.4.83.3 and read as untrusted on DacFx 162.5.57
(`estate/toolchain.md`). The estate pipeline's own DacFx version is not yet recorded. So the
verdict layer's `sqlpackage` is pinned to the pipeline's DacFx version, every finding stamps the
version it ran, and the trust behavior is confirmed once against each pinned version.

**Case 11 — the BEFORE state must match the trunk head being edited.** A stale base makes the
generated delta lie in both directions: it shows differences the change under test did not make,
and it hides differences it did. So the template names the commit and content fingerprint it was
built from, the copy records which template it came from, and refreshing to a new head is
routine.

**Case 12 — the three state-variables are answered from Dev's real facts.** Whether the table is
populated, and whether the existing data violates the new rule, are the two facts that flip a
classification, and both are answered by data. Answers taken from a five-row sample are answers
about the wrong database. So the copy's schema comes from the estate repository, and its data is
shaped by what Dev actually holds — row presence and counts, null counts, duplicates, orphans,
value lengths — either directly (a restore) or through measured evidence (a mint).

**Case 13 — production magnitude is a ledger fact, and the copy does not pretend otherwise.** The
copy proves whether a block fires and what changes; it does not prove how long an index build
takes at millions of rows, or what locks a live workload would see. That stays an added-scrutiny
finding backed by `estate/row-tiers.md`. So the surface must feed the row-tier ledger with real
measured counts, and may offer a large-volume scenario as rehearsal — while the record keeps
stating magnitude claims from the ledger.

### 3.4 — Operating the surface

**Case 14 — a publish from this surface must be unable to reach a real environment.** The Strict
profile sets `DropObjectsNotInSource=True`, the diagnostic posture, which is safe only because
the target is disposable. So every profile on this surface names only local, disposable targets,
and the credentials it uses open nothing else.

**Case 15 — when the substrate is down, the method stalls, and the agent must stop rather than
guess.** The warm container has degraded mid-proof before — a hanging publish, a batch of
connection failures — and the remedy is a four-second restart, never a diagnosis of the change
(`../CLAUDE.md` survival rules; `PROVING_PATH_WINDOWS.md`, the closing rule). So the surface has
a one-command bring-up, a health probe, a one-command recovery measured in seconds, and bounded
memory so the engine cannot starve the machine it shares.

**Case 16 — the surface must exist where the team works.** The team develops in GitHub Copilot
inside Visual Studio, on Windows, against Azure DevOps. Setup must be one-time and centrally
owned — the estate's standing decision is that configuration is central, and the per-change
experience stays simple (`FINDINGS_AND_CHANGES.md` Part 5). Every step must be a terminal command
an agent can run with the developer's approval, because that is how Copilot participates. And the
same surface must serve the practice curriculum: the dojo's katas run against the deterministic
sample project, so the curriculum copy and the working copy are two configurations of one
mechanism (`PHASE_2_CURRICULUM.md` §4).

### The five requirements that carry the design

1. **A cheap reset.** Restoring the BEFORE state costs seconds, so the loop never economizes on
   resets (cases 5, 6, 7).
2. **A named, distributable BEFORE state.** The base is an artifact with a commit and a
   fingerprint in its name, identical bytes on every machine (cases 8, 11, 12).
3. **A pinned verdict layer.** `sqlpackage` at the pipeline's DacFx version, three profiles, the
   probes, the hash — unchanged from today's skills (cases 3, 4, 10).
4. **Database-grain isolation on a local, disposable engine.** Unique database per proof,
   idempotent teardown, no path to a real environment (cases 9, 14, 15).
5. **Windows-native delivery with central configuration.** One-time setup per machine, terminal
   commands only, the curriculum and the working estate side by side (case 16).

---

## 4 — What already exists

The owner asked for the existing enablement to be researched before a line of design was
written. This section is that survey: each piece, what it already serves, and where it falls
short. File citations are to this repository.

### 4.1 — The warm-container loop

`../scripts/warm-sql.sh` owns a persistent SQL Server 2022 container (`projection-mssql-warm`,
port 11433) with bounded memory, a readiness probe, and a restart that completes in seconds. The
skills publish against it today; the self-test fleet isolates on it at the database grain. It
serves cases 9 and 15 well and is the substrate this monorepo's sessions will keep using. It
falls short on delivery: it assumes Docker and this repository, and its data is the five-row
sample.

### 4.2 — The Windows runbook

`PROVING_PATH_WINDOWS.md` stands up the loop on a team machine: SQL Server Express LocalDB (the
lightweight local engine Visual Studio installs) or SQL Server Developer edition, `sqlpackage` as
a pinned dotnet tool, a restored Dev backup as the data, and local-only publish profiles. Its
step 6 already carries the two acceptance checks — the make-mandatory triple and the
constraint-trust probe. It serves the cutover week with nothing new built. It falls short as an
end state: every machine repeats manual setup, the base goes stale until someone restores a
fresh backup by hand, nothing records which base a proof ran against, and nothing feeds the
estate ledgers.

### 4.3 — The Twin

The Twin (`../THE_TWIN.md`; `../src/Twin.Core`, `../src/Twin.Runtime`, `../src/Twin.Cli`) is the
closest existing thing to the surface this document designs. `twin up` reads an SSDT
repository's `CREATE` scripts, publishes them to a managed local SQL Server container via DacFx
(the deployment-engine library `sqlpackage` wraps), and fills the tables with deterministic
synthetic data. A two-plane content fingerprint stored in the database
(`[twin].[__state]`) makes a repeated `twin up` a one-second no-op and a changed estate a
converging republish. Its determinism laws matter here: the same seed re-mints byte-identical
data, and a schema edit re-mints only the columns it touches. It is already wired to the sample
project as the preferred BEFORE-state substrate (`proving-ground/twin.json`;
`skills/talk-to-local-sql/SKILL.md`, the substrate-of-record section).

The code survey behind this design corrected four things the charter's text would otherwise
overstate, and each correction shapes section 5:

- **`twin bake` emits a schema-only image today.** The bake writes a Docker build context —
  Dockerfile, dacpac, entrypoint — whose container publishes the schema at start. No data is in
  the artifact; the code says so in its own comment (`../src/Twin.Runtime/Check.fs`, the bake
  section), while the charter's one line calls the image pre-seeded. The artifact the owner's
  example describes — an image that persists the latest schema *and data* — does not exist yet.
- **The Docker dependency is total for the persistent twin, and confined.** Every verb but one
  requires the docker CLI, and `localhost` and port 1433 are literals inside the
  connection-string builder (`../src/Twin.Runtime/TwinContainer.fs`). The exception proves the
  seam: `twin check` acquires its database through the kernel's connection-honoring path and runs
  against any reachable SQL Server named in `PROJECTION_MSSQL_CONN_STR`, with no Docker at all.
  Everything downstream of the container module already takes connection strings.
- **The Twin's own publish is deliberately not the verdict.** It ignores the refactorlog, runs no
  pre- or post-deployment lanes, and knows nothing of `.sqlproj` files — it globs raw `.sql`.
  The division of labor in `skills/talk-to-local-sql/SKILL.md` stands: the Twin establishes the
  BEFORE state; `sqlpackage` owns every publish whose outcome is the proof.
- **The production-faithful strict publish already exists in the Twin's runtime.**
  `EstateModel.publishStrict` (`../src/Twin.Runtime/EstateModel.fs`) mirrors the Strict profile's
  settings at the production drop posture, and a delta-preview helper exists beside the
  integration tests. Only the test suites call them today.

Two portability facts also matter. The tool targets .NET 9 and contains no Linux-specific paths,
so it runs on Windows where Docker Desktop is present. And the fingerprint hashes raw file bytes
with no line-ending normalization and no `.gitattributes` in the repository, so a Windows
checkout with converted line endings computes a different fingerprint than a Linux checkout of
the same commit — a spurious full republish, and a base that misreports its identity.

### 4.4 — The synthesis engine and the evidence loop

Underneath the Twin, the kernel's synthesis surface generates the data (σ), and the evidence loop
measures a real database to shape it (`../THE_SYNTHETIC_DATA_DESIGN.md`). What the code survey
established:

- **Evidence import is read-only and belongs on a restored copy.** `twin evidence import` runs
  only `SELECT` statements and needs only read permissions; the right source is a restored Dev
  backup, which absorbs the scan cost. Row counts and null counts stay exact even when a sample
  cap limits how many cell values are drained. The captured pack derives a committed "shape"
  tier that is literal-free by construction — real values stay in a "rich" file kept out of the
  repository.
- **Masking is strong above the cardinality threshold and needs review below it.** A column with
  more than fifty distinct values never re-emits a captured value — synthesized tokens are
  disjoint from the source vocabulary by construction. A column with fifty or fewer distinct
  values preserves its real vocabulary unless classified for synthesis, and numeric columns
  reproduce real percentile shapes unless a masking correction says otherwise. `twin classify`
  proposes the classification; a person who knows the domain must review it before minted data
  is distributed broadly.
- **The mint reproduces shapes, and deliberately not individual defects.** Synthetic data holds
  zero foreign-key orphans by construction, reproduces duplicates only statistically, and does
  not reproduce over-length outliers (the captured max length is not consumed by generation
  today). So a minted copy answers "is the table populated" faithfully and answers "does the
  data violate the new rule" only through the evidence pack or a probe against the source — the
  violating-row probe then plants the violation on the copy to capture the engine's exact
  refusal. Section 5.2 builds this split into the design.
- **Scale has a real ceiling.** The bulk load moves roughly thirty thousand rows a second, but
  generation materializes every row in memory before the first byte moves; the demonstrated
  high-water mark is on the order of fifty thousand rows across three hundred tables, and
  millions of rows in one table are unproven. Production-scale minting is not a thing this
  design may assume.

### 4.5 — The executable proof corpus

`../tests/Twin.Tests.Integration/` holds forty-one facts across eleven classes — one per catalog
operation — each publishing a real schema edit against a live Twin-managed SQL Server under the
production-faithful strict options and asserting on the refusal text, the `sys.*` state, and the
content digests (`sample-prs/README.md`, the two-corpora section). A nightly CI lane runs the
whole corpus (`../../../.github/workflows/ssdt-agent-proof-lane.yml`). This is the invariant
cases of section 3.1 in executable form, already green, already scheduled. Its two known gaps:
it runs the in-process DacFx version (162.5.57), which diverges from the live `sqlpackage` on
constraint trust — the live engine is authoritative — and it has no Permissive leg with smart
defaults enabled.

### 4.6 — The Projection engine's accelerants

The pre-cutover engine can emit a buildable SSDT project from a live OutSystems catalog, capture
a data profile that predicts blocks, and diff two catalogs (`ACCELERANT_PLAN.md`). After the Dev
cutover the estate repository itself is the schema's truth, so these surfaces sit off the
critical path of this design; the data-profile capture remains useful as an alternative way to
measure a real database.

---

## 5 — The design

### 5.1 — The shape, in one view

```
the estate repository (Azure DevOps)
  CREATE scripts · refactorlog · pre/post-deploy · profiles/
  twin.json · evidence.shape.json · toolchain pins
        │  each trunk head (commit + fingerprint)
        ▼
the bake job (CI, scheduled and on merge)
  restore-or-mint the data lane → publish the head schema → BACKUP
        │
        ├─► the template image      — for machines with Docker
        └─► the template backup .bak — for Windows engines without it
        ▼
the developer's machine (one-time setup, centrally scripted)
  a local disposable engine: LocalDB / Developer edition / a container
  per proof:  RESTORE the template → PG_<change>_<rand>
  the verdict layer: pinned sqlpackage — preview the delta, publish
  Strict, publish Permissive after a block, probe, hash, reset, re-prove
```

The form factor, stated as one finding: **the BEFORE state becomes a built artifact — the
template — produced centrally at each trunk head, and every local copy is a restore of it.** The
verdict layer stays exactly what the skills already scaffold. The proving loop does not change;
what changes is that its starting state is named, fresh, identical everywhere, and restored in
seconds instead of assembled by hand.

### 5.2 — The template

The template is the trunk-head schema published over the chosen data lane, then frozen with
`BACKUP DATABASE` into a native backup file, and optionally wrapped into a container image that
restores it at start. Its name carries the estate commit and the content fingerprint, and the
single-row `[twin].[__state]` table inside it carries the same identity, so any copy can be asked
which base it came from and any proof can stamp that identity into its record.

The data lane has two sources, and the estate chooses per sensitivity era:

- **The restore lane — for now.** The bake restores the most recent Dev backup and publishes the
  trunk head over it, so the template holds Dev's real rows: real presence, real null counts,
  real orphans, real duplicates, real lengths. Every state-variable is answered directly on the
  copy. This is the right lane while Dev holds no production data, which is the estate's stated
  situation before the first Prod release. It needs no F# work at all.
- **The mint lane — the durable end state.** The bake runs `twin up` and `twin seed` against a
  container: the head schema, then the deterministic mint shaped by the evidence pack captured
  from Dev. The data is masked by construction above the cardinality threshold and by reviewed
  classification below it, so the template can be distributed without carrying real values. Row
  presence and volumes stay faithful to the evidence; individual violations do not survive the
  mint, so the violation half of a classification is answered from the evidence pack or a probe
  against the capture source, and the engine's refusal is then reproduced on the copy by
  planting the violating row — the probe the proving skill already names. Volumes are capped per
  table at the demonstrated generation ceiling, and the evidence's exact real counts flow into
  `estate/row-tiers.md`, so magnitude claims keep citing the ledger.

Both lanes freeze into the same two renditions with the same naming, and the loop on top is
identical. The cutover week needs only the restore lane; the mint lane takes over when Prod data
starts flowing into Dev, or earlier if distributing real Dev rows to laptops becomes
undesirable. The owner's example — a Docker image persisting the latest synthetic schema and
data (`HANDOFF_SESSION_2026_08_26.md` §8) — is the mint lane in the image rendition.

The curriculum estate is a third, tiny template: the sample project with its deliberate seed
defects, baked the same way. The dojo's katas and the self-test fleet run against it, so a
learner's machine and a working machine differ only in which template they restore.

**The mechanic, proven on the sample estate (2026-08-26).** The bake-restore-reset cycle ran
live on the warm container, end to end. The sample estate's BEFORE state (the published schema
plus the seed) froze into a compressed backup in 0.03 seconds, 584 KB. A `RESTORE DATABASE ...
WITH MOVE` stood it up as a per-proof database in 0.5 seconds, holding the exact seed shape
(dbo.Customer: 5 rows, 2 NULL Emails). The make-mandatory edit then published against the
restored copy under the Strict profile and was blocked verbatim — `Could not deploy package`,
`Msg 50000, Level 16, State 127 — Rows were detected` — with Email left nullable, so the
proving loop runs unchanged over a restored template. The reset (drop the contaminated copy,
restore again) took 0.7 seconds, and the unedited model then published clean over the fresh
restore, confirming the template equals the model it was baked from. Engine stamped: sqlpackage
170.5.76.0 (the unpinned-latest install of that day; the block matches the 170.4.83.3
findings). These timings are the sample estate's; a Dev-sized template scales the backup and
restore, which section 7 keeps open.

### 5.3 — The local engine

The engine that hosts the copies is deliberately pluggable, because the loop only ever sees a
connection string:

- **On team laptops: LocalDB or SQL Server Developer edition**, per the existing runbook. No
  Docker requirement stands between the team and proving. The template arrives as the `.bak`
  rendition; `RESTORE DATABASE ... WITH MOVE` into a per-proof name is the BEFORE state, and the
  reset is the same restore again.
- **Where Docker exists — this monorepo's sessions, CI, any developer who prefers it**: the
  template image. A fresh container is a fresh instance; the warm-container pattern with bounded
  memory and a scripted restart carries over unchanged.
- **A shared team server is the fallback, and stays a fallback.** The database-grain isolation
  would hold there too, but one shared instance couples every developer to its degradation and
  puts a non-disposable machine one connection string away from a diagnostic-posture publish.
  The judgment here: prefer local engines; reach for a shared one only if laptops prove too
  weak, and then with per-developer logins that can only see `PG_*` databases.

Isolation and hygiene follow `self-test/PROTOCOL.md` on every engine: one unique database per
proof (`PG_<change>_<rand>`), created by restore, dropped on exit, with teardown idempotent so a
crashed run leaks nothing.

### 5.4 — The verdict layer, unchanged

`sqlpackage` — pinned to the estate pipeline's DacFx version in `estate/toolchain.md` — remains
the only engine whose publishes are proof. The three profiles keep their jobs: Strict surfaces
the block, Permissive shows the consequence, and the pipeline replica shows the
deployment-shaped outcome including phantoms. All three point only at local disposable copies.
The probes and the content-hash check run over the same connection. The Twin's DacFx and the
in-process proof corpus stay CI-side; where the engines diverge, the live `sqlpackage` remains
authoritative (`sample-prs/README.md`). One addition to the record: alongside the `sqlpackage`
version each finding already stamps, a proof also names the template it ran against — the commit
and fingerprint from `[twin].[__state]`.

### 5.5 — The trust chain

A proof on this surface is trustworthy because each link is checked where it is cheapest to
check:

- **CI proves the template lawful before publishing it.** `twin check` gates the mint lane (the
  round-trip law, zero orphans, deterministic re-mint), and the forty-one-fact corpus keeps the
  engine-behavior claims green on schedule.
- **The artifact ties every machine to the same bytes.** The author and the reviewer restore the
  same named template, so neither depends on the other re-running a generator correctly.
- **Each machine proves itself once.** The acceptance checks from `PROVING_PATH_WINDOWS.md`
  step 6 — the make-mandatory triple, the constraint-trust probe, and the silent no-op redeploy —
  run when a machine is set up and again whenever an engine or tool version changes. A machine
  that has not passed them is not a proving surface yet.
- **The pins hold the versions still.** `estate/toolchain.md` records the pipeline's DacFx, the
  matching `sqlpackage`, and the SQL Server engine version the templates are built on; a finding
  proven on one version is re-proved or re-stamped when a pin moves.

### 5.6 — What the surface still refuses to claim

The honest limits in `skills/prove-on-dacpac/SKILL.md` stand unchanged: the copy proves the
forward publish only; it cannot prove the running application keeps working, production-scale
timing or locking, reversibility, or effects on consumers outside the project. The first
promotion into QA or UAT still gets its full-delta drift check against the real target, because
only the real target knows its own drift. The surface makes those limits easier to state
honestly — the ledger holds real magnitudes, the template names its base — and no easier to
forget.

---

## 6 — What must be built, in order

Each item names the cases it serves. The first two are estate-side work with no F# changes; the
cutover week depends on neither.

**B0 — now, before the cutover (no build).** Stand up the runbook path on the owner's corporate
machine against a restored Dev backup; pin the pipeline's DacFx and the matching `sqlpackage` in
`estate/toolchain.md`; run the acceptance checks, which settle the constraint-trust question on
the pinned engine (cases 10, 16; the assessment's prerequisites 1 and 3). This is the degenerate
form of the design — a hand-restored template — and it is enough for the week.

**B1 — the estate configuration and the evidence capture (config; one session).** A `twin.json`
beside the estate's project; `twin evidence import` against a restored Dev backup at the
cutover; `twin classify` with a human review of the proposed classifications; the shape tier
committed to the estate repository; the rich pack held out of it. The evidence's exact counts
fill `estate/row-tiers.md` with real numbers, retiring the sample-seed placeholders (cases 12,
13).

**B2 — the bake job (CI scripting; one to two sessions).** A scheduled and on-merge job that
produces the template from the restore lane: restore Dev's backup, publish the trunk head,
`BACKUP DATABASE`, publish the artifact named by commit and fingerprint, and prune old
templates. The image rendition wraps the same backup. A machine-readable line in the artifact
and in `[twin].[__state]` carries the identity the record will stamp (cases 6, 8, 11). The mint
lane joins the job when B1's evidence pack and review exist; `twin check` gates it there.

**B3 — the per-machine setup script and acceptance (estate-side; one session).** One script per
machine: install or verify the engine, install the pinned `sqlpackage`, fetch the current
template, restore a copy, run the acceptance checks, and print what passed. This is the
one-time, centrally owned setup case 16 demands, and it replaces the runbook's hand-run steps
without changing their content (cases 15, 16).

**B4 — the Twin's existing-server seam (F#; small, confined).** Let `twin.json` name an existing
SQL Server instead of a managed container — the config's container section becomes a choice
between the two — and make the twin database name a knob. The change is confined to the
container module and its call sites; the connection-honoring pattern already exists in `twin
check`. This unlocks every Twin verb — status, evidence, check, a local mint — against LocalDB
or Developer edition with no Docker on the machine (cases 12, 16).

**B5 — fingerprint stability across platforms (small).** Normalize line endings before hashing,
or add the `.gitattributes` the repository currently lacks, so a Windows checkout and a Linux
checkout of the same commit agree on the base's identity (case 11). Fix the bake entrypoint's
carried line endings in the same pass.

**B6 — the peel (packaging; after B4).** Distribute `twin` as a standalone tool the estate
repository can pin, per the charter's designed ejection (`../THE_TWIN.md` §8 — the dry-run
script exists; its outcome is not yet recorded). Needed only when team machines run Twin verbs
locally; the template-consuming path of B2 and B3 does not wait for it.

**B7 — later, as the estate matures.** Carry violation reality (orphan counts, over-length,
duplicate counts) through the Twin's evidence boundary so the mint lane's probes read from the
pack; a large-volume scenario for timing rehearsal, stated as rehearsal; the proof corpus
mirrored into the estate's own pipeline; extracted schema baselines of QA and UAT for rehearsing
the first-promotion drift read.

---

## 7 — Not verified, and open

- **The pipeline's DacFx version is still unrecorded**, and with it the constraint-trust
  behavior on the team's real engine. B0 closes both; until then every trust claim stays
  assumed, with the after-deploy `is_not_trusted = 0` check as the net.
- **The estate project's build style is unconfirmed.** The proving loop builds the project to a
  dacpac; whether the team's `.sqlproj` is SDK-style (`dotnet build`) or classic (Visual
  Studio's msbuild) decides which command the loop's first step uses on their machines. Both
  work; which one is theirs is a fact to record in `estate/toolchain.md`.
- **Docker Desktop availability on corporate laptops is unknown.** The design does not depend on
  it — the `.bak` rendition and LocalDB carry the team path — but the image rendition's audience
  should be confirmed before effort goes to registry plumbing.
- **The engine version on laptops versus the estate's servers.** LocalDB's engine version
  follows what is installed, and guard behavior is engine-bound; the per-machine acceptance
  checks are the control, and the toolchain ledger should record the engine version each
  machine proved against.
- **Dev backup size and restore time on laptops are unmeasured.** The reset promise — seconds —
  holds for the mint lane's capped volumes and for a modest Dev; a very large Dev backup would
  push the restore lane toward per-table volume trimming in the bake, which is scripting, but
  scripting that has to exist.
- **Mint-lane fidelity gaps are named, and open.** Uniform foreign-key fan-out (the skew
  evidence exists in the kernel and is dropped at the Twin's boundary), no over-length
  reproduction, statistical duplicates. The restore lane has none of these gaps, which is
  why it goes first.
- **Sensitivity review is a human step.** Below the cardinality threshold the mint preserves
  real vocabularies unless classified; numeric shapes reproduce unless masked. The classify
  review is on the critical path of distributing minted data, and no code removes it.

---

## 8 — The next move

Run B0 on the corporate machine and pin the toolchain ledger. Then build B1 and B2 in that
order, and put B3's setup script in the estate repository the same week the first template is
published.

# THE_CLI.md — the operator surface

This document is the **target design of the `projection` command-line surface**: the
Apple-clear re-envisioning that subsumes today's ~16 verbs into one engine wearing one
coat with named pockets. It is a **vision surface**, sibling to `THE_VOICE.md` (the
register), `THE_STORYBOARD.md` (the scene-by-scene), and `THE_VOICE_INTEGRATION.md`
(the build plan). It is target-first; the current CLI (`src/Projection.Cli/Program.fs`)
is the provenance, not the contradiction.

Provenance: derived 2026-06-07 from `THE_USE_CASE_ONTOLOGY.md` (the ten axes, the nine
proteins, T16), `WAVE_6_ALGEBRA.md` (`emit(B ⊖ A)`, the torsor, the norm), and the
`WAVE_6_MORPHOLOGY.md` "latent not activated" finding. The design resolves a
meaning-space question: the interface is **not** the engine's algebra exposed — it is a
**namespace of outcomes over the same input**.

---

## 1. The one idea

There is one input — **the model** — and the operator names **where it should land**.
Everything else is a default the engine is smart enough to choose.

The current verbs are not sixteen things. They are one thing — `emit(B ⊖ A)` — seen from
sixteen angles and altitudes. The design does not expose the angles. It exposes the
**intent** and reads the rest.

The realization that collapses the sprawl:

> **Deploy, migrate, load, and export are the same act.** They differ only in *A* — the
> state already at the destination — and the engine reads *A* itself. The operator never
> chooses "deploy or migrate." The direction is *put this here*; the engine computes the
> minimal correct change.

This is simultaneously the Wave-6 algebra (`emit(B ⊖ A)`, *A* read from the substrate)
and the design principle (it just works). The same sentence is true in both
meaning-spaces. That coincidence is how the cut is known to be right.

---

## 2. The mental model — one estate, four verbs

```
project   produce the model at a destination        (the hero — all data movement)
check     assert it is faithful                      (canary · drift · data)
explain   understand a change before it lands        (diff · policy · suggest)
seal      freeze and govern the provenance           (eject · approve)
```

Four plain intents. No verb sits at a different altitude than its siblings. `skeleton` is
not a peer of `transfer`; it is a *shape* of `project`. That re-leveling is the fix.

---

## 3. `project` — the hero

The operator names a **destination** first, because that is the real first question —
*where does this go?* The destination decides the form:

```
project --to ./out          # a folder   → the file bundle
project --to docker         # ephemeral  → a one-touch database, deployed and verified
project --to dev            # live target → read what is there, apply the minimal change
```

Everything past `--to` is optional and defaulted, so the everyday case is one line.

### 3.1 The configuration surface

| Modifier | The axis it names | Default | Reach for it when |
|---|---|---|---|
| `--from <A>` | the prior state in `B ⊖ A` | **auto** (∅ for files; read the target for live) | pin a baseline; force genesis with `--from empty` |
| `--scope all\|schema\|data` | the two legs of T16 (DDL+DML / DML-only) | `all` | a DML-only load onto existing schema |
| `--how merge\|replace\|fresh` | the norm / replacement strategy | `merge` (isometric, CDC-silent) | force a wipe-and-load fallback |
| `--data model\|synthetic\|none\|<target>` | the data origin | `model` | synthetic (Faker), schema-only, or rows from another live target (the transfer) |
| `--rekey <map>` | Reidentify (the user re-key) | off | Dev→UAT identity reconciliation |
| `--shape bundle\|ssdt\|skeleton` | file-bundle composition | `bundle` | SSDT-only; the pre-overlay skeleton |
| `--go` | commit a live write (intent) | off (preview) | apply, rather than preview |

`--data <target>` folds the old `transfer` source in: `--data qa` ingests rows from the
`qa` target into the destination. One flag; no `--source`/`--from` collision.

### 3.2 Defaults that vanish — the auto-*A* principle

For a **folder**, *A* is ∅: the genesis bundle.

For a **live target**, the engine **reads the current deployed state as *A*** and emits
`B ⊖ A` — the minimal change. The operator does not choose deploy-vs-migrate; on an empty
database it is a full create, on an evolved one it is the differential, and on an
unchanged one it is **nothing** (`‖B ⊖ A‖ = 0`, CDC-silent — idempotent redeploy falls
out for free). `--from empty` overrides the auto-read to force a genesis create.

The magic is safe because **live writes preview by default** (§5): the diff is seen
before it is applied.

---

## 4. Targets and aliasing — the configuration file

Pasting connection references each run is friction. A repo-level `projection.json` names
**targets** — an alias to a connection *reference* plus benign defaults and an optional
provenance store.

```json
{
  "targets": {
    "dev":     { "conn": "env:DEV_CONN", "store": "lifecycle/dev.json", "scope": "all" },
    "qa":      { "conn": "env:QA_CONN",  "store": "lifecycle/qa.json" },
    "uat":     { "conn": "env:UAT_CONN", "store": "lifecycle/uat.json" },
    "publish": { "dir": "./publish", "shape": "bundle" }
  },
  "defaults": { "how": "merge", "data": "model" }
}
```

**Discipline, load-bearing:**

- **D9 holds in config.** A target carries a connection *reference* (`env:<VAR>` /
  `file:<path>`), **never a literal connection string**. The secret stays out-of-band;
  only the addressing lives in the file (which is safe to commit).
- **Config holds addressing and benign defaults only** — `conn` / `dir` / `store` /
  `scope` / `how` / `data`. It does **not** hold intent or danger: `--go`,
  `--allow-drops`, and `--rekey` are always explicit on the command line. A re-key or a
  destructive accept is never defaulted out of a file.
- **A target with a `store`** records an episode automatically on `--go`. No store, no
  record. Provenance wiring lives in config; the command stays clean.
- **Precedence:** CLI flag > target config > `defaults` block > built-in default.
- **Override the config path** with `PROJECTION_CONFIG`.

### 4.1 Resolving `--to`

`--to <value>` resolves in order:

1. `docker` — reserved (ephemeral one-touch).
2. a named **target** in config.
3. a **scheme-prefixed** ref — `dir:./out`, `env:DEV_CONN`, `file:secrets/dev.txt` — for
   explicit intent (and to disambiguate a folder that shares a target's name).
4. a path that looks like one (contains a separator or names an existing directory).
5. otherwise: refused, with the known target list named.

---

## 5. The safety model

Two rules, in the register of `THE_VOICE.md` (stative, agentless, imperative direction):

- **Live writes preview by default.** `project --to dev` states the plan and stops. The
  preview footer names the next step: *"Preview only. Re-run with `--go` to apply."*
  `--go` commits.
- **Loss is declared, never silent.** A destructive move refuses with the exact token to
  re-run: *"2 row(s) would drop (transfer.droppedReferences). Re-run with `--allow-drops`
  to accept the loss."*

Two gates guard a live write, by design distinct:

- `--go` is the operator's **intent**.
- `PROJECTION_ALLOW_EXECUTE=1` is the environment's **authorization** (R6).

A live write needs both. The refusal names which is absent.

An irrelevant modifier for a destination is **noted, not silently ignored** (no
silent-drop) and **not hard-failed** (usability): *"`--how` does not apply to a file
destination; ignored."*

---

## 6. The other three verbs

```
check                         # canary: round-trip fidelity on an ephemeral pair (default)
check drift --to dev          # the deployed schema vs the model
check data  --to dev          # row-count + null-count integrity (deployed vs deployed)
check ready                   # the readiness gauge (the run-ledger canary streak)

explain diff  <A> <B>         # the change between two models, before shipping
explain policy <a> <b>        # how two policies project differently
explain suggest <config>      # the suggested config from the evidence

seal                          # eject / freeze: the append-forever provenance package
seal approve <version>        # record an approval decision
```

`check` answers *is it right?* `explain` answers *what would change?* before anything
moves. `seal` answers *make it permanent / sign it off.*

---

## 7. The namespace map (today → target)

| Today | Target |
|---|---|
| `emit <in> <out>` | `project --to <out>` |
| `emit --config` | `project` (config is the default home for the modifiers) |
| `emit --skeleton-only` / `skeleton` | `project --to <out> --shape skeleton` |
| `full-export` | `project --to <out> --shape bundle` |
| `full-export --load` | `project --to <conn> --go` |
| `deploy` | `project --to docker` |
| `transfer` | `project --to <sink> --data <source> [--rekey]` |
| `migrate` (all four forms) | `project --to <conn> [--from <A>] --go` |
| `canary` (+ `--cdc-silence`) | `check` |
| `drift` | `check drift --to <conn>` |
| `verify-data` | `check data --to <conn>` |
| `readiness` | `check ready` |
| `diff` / `policy-diff` | `explain diff` / `explain policy` |
| `suggest-config` | `explain suggest` |
| `eject` | `seal` |
| `approve` | `seal approve` |

Sixteen surfaces → four verbs. **Nothing is deleted**; the machinery underneath is
unchanged. The operator meets one engine wearing one coat with named pockets, instead of
sixteen coats.

---

## 8. Every protein, as one line

The proof the namespace is real — the nine canonical use cases (`THE_USE_CASE_ONTOLOGY.md`
§3) plus the output forms, each a sentence:

```
P-1  Dev load                  project --to dev --go
P-2  QA load                   project --to qa --go
P-3  UAT with re-key           project --to uat --rekey users.csv --go
P-4  SSIS publication          project --to publish
P-5  Idempotent redeploy       project --to dev --go        (re-run; B⊖A = 0 ⇒ CDC-silent)
P-6  In-place migrate          project --to dev --go        (same verb; A is non-empty)
P-7  Eject / freeze            seal
P-8  Drift detection           check drift --to dev
P-9  Self-check canary         check
     —
     Docker one-touch          project --to docker
     SSDT + static + bootstrap project --to publish --shape bundle
     Faker schema              project --to docker --data synthetic
     DB → DB transfer          project --to uat --data qa --rekey users.csv --go
     Skeleton (pre-overlay)    project --to ./out --shape skeleton
```

P-5 and P-6 collapsing into the **identical** command as P-1 is the headline: the
operator stopped needing to know the difference. The engine reads *A* and the difference
disappears.

---

## 9. Exit codes (carried forward, stable)

A single verb returns many codes; the codes are stable and documented, and `check`
separates fidelity from movement.

| Code | Meaning |
|---|---|
| 0 | succeeded |
| 1 | argv error (missing input, unknown target) |
| 2 | parse error (model JSON; spec; config-parse) |
| 3 | execution error (SQL rejected the change; connection open; unbreakable cycle) |
| 4 | Docker unavailable (`project --to docker`, `check`) |
| 5 | fidelity divergence (`check` canary / `check drift`) |
| 6 | config error (file missing / unparseable / D9; connection-ref resolve) |
| 7 | gate refusal (`--go` without `PROJECTION_ALLOW_EXECUTE=1`; permission pre-flight) |
| 8 | data divergence (`check data` row / null) |
| 9 | refused, fail-loud (undeclared drop; inexpressible ALTER; tightening; verify-failed) |

---

## 10. How the red herrings dissolve

- **The orthogonality matrix never runs.** A namespace names only wanted points, so
  `--how merge --to ./folder` is not a sentence anyone forms; if formed, it is noted in
  one line at the boundary. The legal combinations are not something to *prove* — they
  are the proteins, curated by the use-case analysis.
- **"Illegal states unrepresentable"** is satisfied by **progressive disclosure** (each
  destination surfaces only its relevant modifiers), not by a type lattice over a flag
  product. The product is never built, so its illegal cells never exist.
- **The plane-carving** (emission / episode / proof) is an engine fact, not an interface
  fact. The operator sees `project` / `check` / `explain` / `seal`. The planes stay where
  they belong — inside.

---

## 11. The shape decision — resolved

The interface is a **shallow coordinate space with named presets, collapsed by
defaults**:

- the *coordinates* are the modifier axes (`--scope` / `--how` / `--data` / `--rekey` /
  `--shape`);
- the *presets* are the named targets (`dev`, `qa`, `uat`, `publish`) and the default
  coordinate points — which *are* the proteins;
- the *defaults* make the everyday case one word past the destination.

Recombination is available when needed (`--scope data --how fresh` is sayable); the
one-liner is there when it is not. No flag-bag; no god-command.

---

## 12. Decisions — resolved (2026-06-07, shipped)

1. **The hero verb is `project`** — the domain's own word (the Total Projection).
2. **Readiness is `check ready`** (gate-shaped); a `seal history` / `explain history`
   episode browser is deferred until it earns its place.
3. **Axes accepted-but-not-yet-honored surface a named note, never a silent drop.**
   `--scope` / `--how` / `--from` and `--data synthetic|none` are parsed and
   acknowledged; the current engine emits its default and `noteUnhonored` says so.
   Wiring these knobs (and `--data synthetic --rows N`) into the engine is an
   evidence-gated follow-up (THE_CLI_BACKLOG.md).
4. **`project` consolidations** — `emit --config` folds into `project --to <folder>
   --config` (the richer `full-export` bundle); `full-export --load` into `project
   --to <live> --config --go`; `transfer` into `project --to <sink> --data <source>`
   (DryRun without `--go`). Niche flags dropped (named, not silent) are listed in
   THE_CLI_BACKLOG.md.

---

## 13. What this became

Shipped 2026-06-07. `main` discovers `projection.json`, parses argv via
`Surface.parse` into a typed `Intent`, and routes to executors that delegate to
the proven engine faces — one `MovementSpec` (source *A* × destination × legs ×
strategy × data-origin × identity × shape × mode), one engine, four thin faces
(`project` / `check` / `explain` / `seal`). The 16-verb dispatch and its Argu
glue are removed; the per-verb conditional trees documented in the 2026-06-07
verb audit collapsed into the one parameterized path — the activation of the
"latent" calculus the morphology named. Surface types live in
`src/Projection.Pipeline/MovementSpec.fs` + `MovementSurface.fs`; the executors
in `src/Projection.Cli/Program.fs`; tests in `MovementSurfaceTests.fs`.

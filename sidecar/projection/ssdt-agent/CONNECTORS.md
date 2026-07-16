# CONNECTORS — the future wiring seams

This tree is **built and self-contained today** for both personas — the OutSystems-native
developer who authors the change, and the lead's reviewer who gates it. It is also deliberately
shaped so it can be wired into larger machinery later without rewriting the bodies. This file is
the single place every such seam is named. Each entry states: **what it replaces**, **the
contract across the seam**, and **what must be verified before it is built**. Nothing here is
wired now — these are connector points, not a backlog.

---

## 1 — `.claude/skills/` adoption for Claude Code

**What it replaces:** the manual "read `skills/<name>/SKILL.md`" step. Claude Code discovers
skills by directory under `.claude/skills/`.

**The contract:** every `ssdt-agent/skills/*/SKILL.md` already carries valid YAML frontmatter
with `name` (kebab-case, matching the directory) and a triggering `description`. That is
exactly the `.claude/skills/` shape. To adopt: copy or symlink `ssdt-agent/skills/*` into
`<repo>/.claude/skills/` and register `ssdt-agent/agents/*` as agents. **No body changes are
required** — the skills were authored to this shape on purpose.

**Verify first:** that the target Claude Code version reads `name` + `description` frontmatter
(it does as of this writing) and that the `operations/` knowledge files — which are *not*
`SKILL.md` files — are referenced as plain knowledge by the skills rather than registered as
skills themselves. Keep `operations/*.md` as data the skills `Read`, not as discoverable
skills.

---

## 2 — GitHub Copilot custom agents

**What it replaces:** the three `agents/*.md` roles, re-homed as Copilot custom agents so the
same tree drives a Copilot-based developer.

**The contract:** `agents/intake.md`, `agents/change-author.md`, and `agents/reviewer.md`
map 1:1 to three Copilot custom-agent roles. The role statements, inputs, procedure, and
handoff contracts transfer verbatim.

**Verify first — FLAGGED, do not assume:** the GitHub Copilot custom-agent **file format**
(the frontmatter keys it expects, how tool grants are declared, where the files live in the
repo) is **NOT** the same as the Claude agent shape and **must be checked against current
Copilot documentation before any scaffolding**. Treat the mapping below as the intent, not the
syntax:

| ssdt-agent role            | Copilot custom-agent role (intent) | tool grants needed                |
|----------------------------|------------------------------------|-----------------------------------|
| `agents/intake.md`         | intent/triage agent                | read-only (no shell)              |
| `agents/change-author.md`  | the authoring agent                | shell (docker/dotnet/sqlpackage), file edit under `ssdt-agent/` |
| `agents/reviewer.md`       | review/gate agent                  | read-only + PR-comment             |

Do not scaffold the Copilot target until the format is confirmed.

---

## 3 — The F# Projection engine generates the proving-ground project

**What it replaces:** the **hand-authored** `proving-ground/SampleCatalog.sqlproj` + the
`Modules/*.sql` + the post-deploy, swapped for **engine output from a real OutSystems
catalog**.

**The contract:** `src/Projection.Targets.SSDT` already contains `DacpacEmitter`,
`SqlprojEmitter`, and `PostDeployEmitter` — they emit the `.dacpac`, the `.sqlproj`, and the
post-deploy script from a real catalog. They **never drive sqlpackage**; that driving is
exactly what `skills/prove-on-dacpac` adds, kept as agent-run commands. The seam: point the
`prove-on-dacpac` loop at the engine's emitted bundle (entry point: the `Render` /
`SsdtBundle` surface) instead of the hand-authored sample. **The loop is unchanged** — same
build → Script → Strict → Permissive sequence, real schema instead of sample schema.

**Verify first:** that the engine's emitted `.sqlproj` reclassifies pre/post-deploy and data
scripts out of the default `**/*.sql` glob the same way the hand-authored sample does (see
`proving-ground/SampleCatalog.sqlproj`), and that its dacpac targets a DSP the proving-ground
container supports (Sql160). Do **not** call into F# from the skills — reference the artifact,
do not import the assembly.

---

## 4 — sqlpackage driving (the gap this tree closes)

**What it replaces:** nothing yet — this is the missing capability. The engine emits artifacts
but never runs `sqlpackage /Action:Script` or `/Action:Publish`.

**The contract:** `skills/prove-on-dacpac` **is** that driver, kept deliberately as
**agent-run commands** rather than a wrapper script (per the hard constraint that skills
scaffold, they do not orchestrate). The agent runs each `sqlpackage` invocation itself and
reads the result.

**Verify first / connector note:** a future build *could* fold the proven command sequence
into the engine's bundle step (an `SsdtBundle.prove` verb). If that is ever done, the
two-profile discipline and the data-hash snapshot must survive intact — the Strict profile that
detects whether the deployment is blocked, the Permissive profile that surfaces the consequence,
and the snapshot that proves the data is conserved. These are the proof itself.

---

## 5 — Azure DevOps PR promotion

**What it replaces:** the manual handoff from `change-author` to a human reviewer.

**The contract:** `change-author`'s **review packet** — the operation, how it ships and who
must review (the two plain findings, `THE_RECORD.md` §5), the real generated delta, the proof
(the blocked deployment and its row counts), the remedy, and the named trap — is the body of
the pull request `skills/author-pr` composes. `reviewer` is the gate.

**Verify first / sketch only:** an Azure DevOps pipeline wraps the proven delta and the two
profiles into a PR and promotes a Strict-clean change. The contract to preserve: the PR body
is that review packet; a change may only auto-promote if its **Strict re-run is clean** after
the remedy. Build nothing here — this is the shape the output is designed to slot into.

---

## 6 — `warm-sql.sh` substrate reuse

**What it replaces:** any temptation to author a new orchestration script for the disposable
database.

**The contract:** `skills/talk-to-local-sql` consumes the **existing**
`scripts/warm-sql.sh` (plain bash, already in the repo, reusable) for the disposable database.
No new orchestration script is introduced.

**The hard boundary:** the disposable database lives **only** on the warm
container (`projection-mssql-warm`, `localhost,11433`). Never point a publish profile at
anything but `ProvingGround` on `localhost,11433`. The Strict profile sets
`DropObjectsNotInSource=True` precisely because the target is disposable — aimed anywhere real,
that flag drops every object the target holds that the source does not.

---

## 7 — The reviewer persona (Persona 2)

**Status:** built. `agents/reviewer.md` is Persona 2, the lead's adversarial reviewer,
composing `skills/review/{review-change,adversary,dependency-scope,verdict}`. It runs two roles
on one engine — a backstop that gates the OutSystems developer's authored changes so the lead's
queue is decisions-only, and a sparring partner that stress-tests the lead's own proposed
change. It consumes `change-author`'s review packet — the body of the pull request
`skills/author-pr` composes — reproduces the proof on its own isolated database
(`self-test/PROTOCOL.md`), maps the dependency scope, and renders one of the four plain
dispositions: Approved, Approved with a named risk, Returned to the author, or Escalated with
one question for the lead.

**The connector that remains:** re-homing the built reviewer onto an external review surface —
a Copilot custom agent (§2) or the Azure DevOps PR gate (§5). Those are fill-ins on a working
agent, not a redesign. Build them only after the PR-promotion seam (§5) is approved for build.

# V3 — The Instruction Architecture

*The documents, the agent-facing surfaces, and the values they carry: what v3 says about
itself, to whom, and how it stays true. Companion to `V3_ARCHITECTURE.md`, which designs the
code. Written 2026-09-17 on `claude/v3-architecture-design-sl95mx`, after reading every
instruction surface in both prior generations: the root `AGENTS.md` and guardrails, v2's
`CLAUDE.md`, its chapters, handoffs and decision entries, the `ssdt-agent` tree's skills,
agents, registers and gates, the four Claude Code hooks, and the generated Copilot bundle.*

---

## 0. What this document decides

`V3_ARCHITECTURE.md` decided what the code is. This document decides what the *words* are:
which files exist, who reads each one and at what moment, what an agent session reads first
and may write, how a Copilot session in the team's Visual Studio enters and leaves, which
values the codebase upholds and by what mechanism, and how the whole surface is kept from
drifting into the 119,384 lines v2 wrote about itself.

**Contents.** §0 the summary · §1 who reads what, when · §2 what v2 taught · §3 principles ·
§4 the values register · §5 the inventory · §6 the engine repository · §7 the knowledge layer
· §8 the estate repository · §9 the agentic interface · §10 how it stays true · §11 migration
· §12 risks · A retired vocabulary · B reading paths · C numbers.

**The design.** A document is a product with a named reader and a named moment, and it is
sized to the smallest context that must hold it. Anything that can be a mechanism (a test, a
verb that refuses, a hook that is five lines over a verb, a generated file) is not prose.
v3's hand-written prose outside the knowledge tree is nine files and a handful of package
notes: `README.md` (150 lines), `AGENTS.md` (120, tool-agnostic), `CLAUDE.md` (20, one import
line and the hooks), `ARCHITECTURE.md`, `VALUES.md`, `DECISIONS.md` (one line per decision),
`NEXT.md` (40, rewritten, never appended), two pull-request templates, and a README per
package. The knowledge tree is what a developer reads first, and it carries domain truth
only: one authoring skill, one reviewing skill, one page on the pull request description, the
operation catalog, the shared reasoning, the findings with receipts, a dozen samples, the
ledgers. Everything an agent
needs at runtime (the laws, the verb reference, the config reference, the skill index, the
Copilot bundle, the `.claude/` pointers, the document inventory) is generated from tests,
from `--help`, and from the tree, and is never edited by hand. Two hooks replace four; both
are wrappers over a verb. The engine and the estate are two repositories, the companion's
default and its ninth operator decision, still open; §8 says what changes if they merge. The
values the codebase upholds are written once, each with the
mechanism that upholds it and the name of the test, and the register that lists them is the
first thing a new maintainer reads after the README.

**The manifest test.** The inventory of documents is data
(`ci/docs.manifest.json`), and a test asserts that every markdown file in the tree is a row in
it with a reader, a moment and a line budget, that every row is under its budget, and that
generated files carry their banner and hand-written files do not. A new document is therefore a
red build until someone says, in a reviewable line, who will read it and when. That is the part
of v2's write budget (§15 of the companion) a test can hold; the rest (what a session may
write, and whether a name or a rationale is good) is judged by a reviewer, and §4 says so.

**What this document keeps from v2.** The generated dispatch pointers into `.claude/` and the
Copilot bundle, with their fingerprint. The path-scoped instruction files that attach when a
`.sql` file is open. The pull request description's register (agentless, finding first, the
true verb, evidence beneath, the unverified admitted). The findings file with database names
as receipts. The ledgers. A read order stated as a list. "Verify before you diagnose." The
entry prompt. The citation gate.

**What it cuts.** The chapter open and close, the handoff letter, the four-field decision
entry, the pillars and axioms as a vocabulary, the fifteen survival rules as prose (each
becomes a mechanism, a knowledge line, or nothing), the three-agent hand-off protocol (intake to change-author to reviewer, with a change-spec contract between them), the self-test rubric, the
Stop hook that ran a performance gate after every message, the PreToolUse hook that ran
before every shell command, 814 lines of hook script, four surfaces per operation, and every
document whose reader is the project itself.

**What it changes.** One instruction file for all agents (`AGENTS.md`) with `CLAUDE.md` as an
import of it, so the two never disagree. Ownership split between two repositories, stated
per file. The values register. The manifest test. The CLI as the agent's API, with a JSON
contract per verb and an optional MCP server generated from the same table. Permissions that
deny the archive to agents by default, so provenance is reachable on purpose and never by
accident.

---

## 1. Who reads what, and when

Every file in §5 has at least one cell in this table. A file with no cell does not exist in
v3. The columns are the moments a reader meets the repository; the rows are the readers. Each
cell names the *first* file read at that moment, and the file's job is to be sufficient for
that moment or to point, in one hop, to what is.

| Reader | First day | Starting a session | Making a change | Reviewing a change | A failure | A release | A decision |
|---|---|---|---|---|---|---|---|
| **A developer** (OutSystems-native, Copilot in Visual Studio, the estate repository) | the entry prompt (`#prompt:schema-change`) | the router (`copilot-instructions.md`, attaches itself) | `knowledge/authoring.md` → one `ops/<op>.md` → the shared skill it names | — | the verdict's `remedy` line; `ops/<op>.md` "the named trap" | the pull request description they wrote | the one business question, asked by the tool |
| **A reviewer / dev lead** | `knowledge/description.md` (one page) | — | — | the pull request body; `knowledge/reviewing.md` if reproducing | the gate's `gate.json` | the ledgers (`operations.md`, `in-flight.md`) | `refusals.md` (escalations land here) |
| **An engine maintainer** (the team's C# developer, this repository) | `README.md` → `VALUES.md` → `kernel/README.md` | `NEXT.md` | the package README of the area; `LAWS.md` | the pull request; the engine template's sections | `estate doctor`; the test name in the failure | `DECISIONS.md` (append a line) | `DECISIONS.md`, `ARCHITECTURE.md` (the paragraph that became false) |
| **The owner / architect** | this document and its companion | `NEXT.md` | `ARCHITECTURE.md` | — | `VALUES.md` (which value the failure violated) | `DECISIONS.md` | `V3_ARCHITECTURE.md` §16 (the list of decisions only the operator can make) |
| **A Claude Code session** (engineering, this repository) | `AGENTS.md` (via `CLAUDE.md`) | the SessionStart hook's status line (`estate doctor`) | `AGENTS.md` §"how to change things"; the package README | the engine PR template | the verb's refusal (exit code + remedy); `AGENTS.md` §"when a tool is missing" | `NEXT.md` (rewrite) | `DECISIONS.md` (one line) |
| **A Copilot session** (authoring, the estate repository) | the router | the path-scoped instruction that attached | `authoring.md` phases S0–S8 with their verbs | `reviewing.md` | the verdict JSON | the pull request description | the fork form (`ask-the-developer`, folded into `authoring.md`) |
| **A CI lane** (proof, bake, gate) | `ci/README.md` | — | — | `estate gate` | the lane's own log; `gate.json` | `changelog.json` | — |
| **A review bot** (Claude Code Review, Copilot review, on either repository) | the PR template (its sections are the bot's checklist) | — | — | `knowledge/description.md` for a schema PR; `VALUES.md` for an engine PR | — | — | — |
| **A new joiner, either repository** | `README.md` (150 lines, five minutes) | — | — | — | — | — | — |
| **A developer on the bottom rung** (agent mode off; the router and the instructions are all that attach) | the router, read by hand | the router | `knowledge/INDEX.md` → the operation → `authoring.md`, read as documents; the verbs typed by hand | — | the verdict | the pull request description | — |
| **Whoever deploys and refreshes** (the Integration Studio half, per environment) | — | — | — | — | — | the pull request description's after-deploy section for that environment | — |
| **The SSIS team** (maps a legacy database against the schema each sprint) | — | — | — | — | — | `changelog.json` from the gate, with renames by refactorlog key and both shapes during a two-release window | — |

- **The developer never reads the engine repository.** Everything a Copilot session needs is
  vendored into the estate repository as `knowledge/` and the bundle (§8). The engine
  repository's instruction set (§6) is for maintainers and for engineering sessions.
- **Nobody reads the archive.** `archive/` has no cell. It is provenance: reachable on
  purpose (§9.6 says how), invisible by default.
- **Five files are read most.** `AGENTS.md`, the router, `authoring.md`, `description.md`,
  and `VALUES.md` are the first read in twenty of the thirty-two filled cells. They get the
  tightest budgets and the most careful drafts (§6–§8).

The reading paths for the four most common first days are written out in Appendix B.

---

## 2. What v2 taught about instructions

### 2.1 The surface, measured

| Surface | Size | Consumer today |
|---|---:|---|
| v2 root documents | 179 files · 119,384 lines | the project itself; a new session reads `KICKOFF.md` → `CLAUDE.md` → the reading order (§2 of `CLAUDE.md`, ~40 lines of pointers) |
| `sidecar/projection/CLAUDE.md` | 302 lines · 9 sections · 15 survival rules restated in full | every session |
| root `AGENTS.md` | 253 lines · 8 pillars · 4 named failure modes | every session; points to `KICKOFF.md` and the pillars at the top of `DECISIONS.md` |
| `DECISIONS.md` | 30,911 lines · 480 dated entries over 69 days · Status/Context/Decision/Reasoning per entry · a "supreme operating discipline" block at the top | the pillars: every session; the entries: archaeology |
| the chapters | 56 `CHAPTER_*.md` · 11,692 lines · open/close/pre-scope skeleton with a binding presentation contract per chapter | the session that wrote it |
| the handoffs | `HANDOFF.md` 3,887 lines, prepend-only, second-person letters; plus backlogs | the next session, once |
| `.claude/` | 72 files · 4 hooks (814 lines of bash) · 65 skill pointers + 3 agent pointers (8 lines each, generated) · `settings.json` (47 lines) | Claude Code sessions in the monorepo |
| the four hooks | `session-start.sh` 431 · `docker-probe.sh` 171 (PreToolUse, every Bash call) · `session-end.sh` 118 · `perf-gate-stop.sh` 94 (Stop, after every message) | the harness |
| the `ssdt-agent` tree's entry surfaces | `README.md` 162 · `CLAUDE.md` 24 (a routing stub) · `THE_RECORD.md` 308 + `THE_RECORD_FORMS.md` 143 · `THE_DECISION_TREE.md` 215 · 3 agents 743 · `skills/INDEX.md` (generated) | Claude Code and Copilot sessions |
| the Copilot bundle | 80 files · router 60 lines · 4 path-scoped instructions (9–23 lines) · 3 agents · 4 prompts · 65 pointers · PR template ×2 · `ADOPTION.md` 173 · fingerprint `047e6c5dbd9c` | the estate repository (vendored) |
| the gates | `ssdt-agent-gates.mjs` 370 (citations · register · mirror · packaging · estate) + `ssdt-agent-package.mjs` 682 | CI on the tree; the estate pipeline (`copilot-check`) |
| the self-test | 9 files · 2,493 lines · a 1,046-line prompt matrix · two rubrics (six criteria; nine dimensions) | a hand-scored certification nobody has run since the proof lane landed |
| `.github/workflows` | 6 lanes · 440 lines | CI |
| v1's documents | `readme.md` 1,162 and six root files (5,405) · `docs/` 33 files, 23,945 · `handbook/` 32 chapters, 8,980 · `ssdt-playbook/` 61 files, 8,265 · `notes/` 34 files, 31,364; 77,959 in all | twenty skills cite the handbook (through a +3 numbering offset); one cites one playbook page; `AGENTS.md` sends every session to `notes/run-checklist.md` and `tasks.md`; the rest has no reader |

### 2.2 What worked, and is kept

Worked means a consumer used it and the evidence is in the tree.

- **Generated dispatch pointers, for Claude Code.** Sixty-eight eight-line files that route a
  session to the canonical body where its relative citations resolve, regenerated by one
  script, fingerprinted, checked in CI. There is no drift surface because there is nothing to
  drift. Every session in this repository has gone through them. v3 keeps the mechanism whole
  and extends it to the document inventory itself.
- **Path-scoped instructions.** Four small files that attach when a `.sql`, a deployment script,
  or a publish profile is open, carrying the never-rules for that file type. The review called
  them the single most reliable Copilot surface. Kept verbatim in shape.
- **The pull request description's register.** Nine rules and a banned list that turn an agent's output into
  something a lead approves by reading. It is the best-written instruction in the repository
  and the one with the clearest consumer. Kept, on one page, with its lint as tests.
- **Findings with receipts.** `FINDINGS_AND_CHANGES.md` names twenty DacFx behaviours, each
  proven against a live SQL Server, each with the database name that proves it, and one
  overturned in the open (F5 by F9). This is the model for every factual claim v3 makes.
- **A read order stated as a list.** `ssdt-agent/README.md` says: read `THE_RECORD.md`, then
  `agents/`, then `skills/`, then `proving-ground/`. Four lines. Every entry file in v3 ends
  the same way.
- **"Verify before you diagnose."** The root `AGENTS.md` rule that an agent runs the Docker
  check before claiming a tool is missing. In v3 that check is a verb (`estate doctor`), and
  the rule is one line pointing at it.

### 2.2a Built and not yet exercised, and kept on that understanding

These are designs with no consumer that any file names, kept because their shape is right;
the pilot (companion §16, item 2) is where they are first exercised.

- **The Copilot bundle above the router.** `.github/skills/` discovery, the custom agents, and
  `#prompt:` attaching on the team's 2022 build are each listed as unverified by the bundle's own
  `ADOPTION.md`. The router and the path-scoped instructions are the surface that works on every
  rung; the rest is a design until a champion laptop runs it.
- **The entry prompt.** `#prompt:ssdt-schema-change` runs intake and authoring in one
  conversation. Personas were already collapsing into phases; v3 finishes it; whether the prompt
  attaches is the pilot's first check.
- **The ledgers.** Eight append-only tables of 272 lines designed to carry the estate's actual
  state (which operations have shipped, which tables hold how many rows, which windows are
  open). `operations.md` and `in-flight.md` have no rows, by design, until the first change
  ships through the pipeline. A ledger with no rows is a format; a session that remembers a
  fact instead of looking it up is guessing.
- **The estate pipeline.** `ssdt-agent-check.yml` exists and must be registered by a person in
  Azure DevOps; nothing says that it has been.

### 2.3 What did not work, and why

- **Rules as prose that a mechanism could own.** Fifteen survival rules, each restated in full
  because "each has cost an agent real time." Appendix C of the companion lists which are
  still true of v3. Rule 1 (never run the two test pools together) is a CI
  matrix. Rule 2 (a batch of connection failures means the container died) is a verb refusal
  with a remedy. Rule 4 (re-run with the TRX logger) is the default logger. Rules 5, 6, 7 are
  F# compiler facts retired with F#. Rule 10 is about a file v3 does not have. Rules 11 and 13
  are about a gate v3 does not have. Rule 15 (a stale RID directory shadows a build) is a
  `clean` in the build script. Of fifteen, two survive as knowledge (8, the `Static` marking,
  which v3 fixes by construction and keeps as a law; 14, the `''`-versus-`NULL` comparator,
  which is a sentence in `description.md`'s "what proving showed" guidance). The list was
  accurate; prose was the wrong medium.
- **The chapters, the handoffs and the four-field decision entries.** 49,336 lines of process
  artifact written at about seven entries per working day. The reader of a chapter close was
  the session that wrote it. The handoff letter's reader was the next session, once, and the
  next session had `git log`. v3 replaces all three with the pull request (the handoff), one
  line per decision, and a forty-line `NEXT.md` that is rewritten rather than grown.
- **Pillars and axioms as a vocabulary.** "Supreme operating discipline," "pillar 9," "A41,"
  "T11," "the torsor": a private language every session had to learn before it could read the
  code. The August review's diagnosis stands: *every insight becomes a named surface, every
  surface a citation target, every citation a gate*. v3's laws have English names and live in a
  generated file; the retired vocabulary is listed once in Appendix A with the plain word for
  each.
- **Four hooks, 814 lines.** A 431-line SessionStart script that installs a SDK, a tool, a
  daemon, an image and a container; a Docker check on every shell command; a performance gate after
  every message that had to be taught not to feed its own output back to the agent. The
  session-start work was real, but the shape was wrong: a hook should be five lines over a verb
  that does the work, refuses with a reason, and can be run by a human from a terminal.
- **The three-agent hand-off protocol.** Intake, change-author, reviewer, each a 160–320-line
  role file, with a change-spec contract passed from the first to the second and a review packet
  from the second to the third, modelling an organisation that does not exist inside one chat.
  The entry prompt had already merged the first two. v3 has two: authoring (one session, the
  phases inside it) and reviewing (a separate person with a separate trust model), and the
  reviewer's reproduction is the pipeline's.
- **Four surfaces per operation.** A skill, a sample pull request, a self-test prompt, and a
  synthetic-copy fact, kept in agreement by five gates in 370 lines and a 682-line packager (1,875 lines
  of JavaScript under `scripts/` in all). Two surfaces (the
  skill and the proven change as data) need one count to agree.
- **The self-test rubric.** Six criteria, nine dimensions, golden runs, a 1,046-line prompt
  matrix, hand-scored. The nightly proof lane discharges the regression duty with 41 executable
  facts. Conversation quality is judged in the pilot, on the team's build, by the people who
  will use it.
- **Unverified rungs.** The bundle's own `ADOPTION.md` lists what is not verified: whether
  the team's Visual Studio build discovers `.github/skills/`, whether `#prompt:` attaches on
  2022, whether agent mode reads vendored skills when the solution is not at the repository
  root. The bundle is well-made and has not been run where it matters. v3 does not add a
  surface until the pilot says which rung the team is on.
- **Documents that restate counts.** `CLAUDE.md` §8 says restating a count or a feature list is
  a bug, and the same file says there are eight pillars where `DECISIONS.md` says nine, the
  review says 41 operations where the tree has 45, a digest says 351 decisions where there are
  480. The rule was right and had no owner. In v3 the owner is a test.

---

## 3. The principles

The companion's thesis (§5.1–§5.5) and its three corollaries, restated for documents, each
with the v2 fact that taught it and the companion section in brackets; nothing here goes
beyond what the code document asserts.

1. **A document has a reader and a moment, or it does not exist.** [§5.4: sized to consumers] Every file is a row in the
   manifest with `reader` and `moment` filled in; the table in §1 is generated from it. v2's
   counter-example: 56 chapter files whose reader was their author.
2. **Mechanize before you document.** [§5.2: proof is the classifier; §5.5] If a rule can be a test, an analyzer, a verb that
   refuses with a remedy, a generated file, or a five-line hook, it is not prose. Prose is for
   what a mechanism cannot hold: intent, judgment, the business question, the reason a decision
   went one way. v2's counter-example: fifteen survival rules restated in full in the file every
   session reads first.
3. **One entry per consumer, one hop to the body.** [§5.6, one CLI, one layout] `AGENTS.md` for any agent, imported by
   `CLAUDE.md`; the router for Copilot; `README.md` for a person; `knowledge/README.md` for the
   domain. Each is under its budget and each points, once, to where the body lives. v2's
   counter-example: `AGENTS.md` → `KICKOFF.md` → `CLAUDE.md` → the reading order → `DECISIONS.md`
   § "supreme discipline" before a session could start.
4. **Knowledge, not doctrine.** [§5.5: knowledge is kept] The knowledge tree carries what is true of SQL Server, DacFx,
   the estate and the platform, with receipts. No file carries the project's opinion of itself,
   its vision, its principles, or its progress. v2's counter-example: fourteen vision documents,
   12,869 lines.
5. **Generated surfaces are never hand-edited; hand-written surfaces never restate them.** [§5.5: docs generated or short]
   The laws, the verb reference, the config reference, the skill index, the bundle, the pointers
   and the inventory view are generated on every build. A hand-written file that carries a
   count, a line number, a file list or a verb list a generated file also carries is a failed
   test. v2's counter-example: the pillar count, the operation count, the decision count.
6. **The pull request description is the product, the pull request is the handoff, git is the timeline.** [§5.3] A
   session's output is code, tests, a PR body in the register, one decision line if a decision
   was made, and a rewritten `NEXT.md`. Nothing else. v2's counter-example: 49,336 lines of
   chapter, handoff and decision prose.
7. **Sized for the smallest context that must hold it.** [§5.4; §2.3's sizing paragraph] The developer's context is Copilot in
   Visual Studio with the SQL file open. The whole authoring path (router → `authoring.md` → one
   operation → one shared skill → `description.md`) is under 600 lines, and the budgets test computes
   that sum. An operation is under 90 lines; the router under 60; `knowledge/README.md` under 150.
   v2's counter-example: `prove-on-dacpac` at 576 lines and `talk-to-local-sql` at 406, read in
   full before a single command ran.
8. **Values are stated once, each with its mechanism and the mechanism's name.** [§5.6's third corollary: laws as tests] `VALUES.md`
   is a table: the value, the one-sentence requirement, the test or verb or analyzer that holds
   it, and what a violation looks like. A value with no mechanism is marked "prose only" and is
   a known weakness. v2's counter-example: 187 stated guarantees across
   twenty-four themes, of which the ones that held were the ones with a test, and no file said
   which those were.

One rule sits above the eight and applies to every surface, the pull request description
included: **the register.** Agentless, finding first, the true verb, evidence beneath, exact
names, the unverified admitted, the next action last, no private nicknames. `AGENTS.md` is written in it; so is this document's
successor `ARCHITECTURE.md`; so is a verb's refusal message. A reviewer who learns the register
from the pull request should recognise it in the README.

---

## 4. The values register (`VALUES.md`)

The code document listed twelve laws; this section lists the values and non-functional
requirements that the laws do not cover, and it is the draft of `VALUES.md`, the file a new
maintainer reads second. Every row has four parts: the value, the requirement in one
sentence, the mechanism that upholds it, and where the mechanism lives (a test name, a verb,
an analyzer rule, a hook, a generated file). A row whose mechanism is "prose only" is a known
weakness and says so. The v2 source column is provenance: where v2 stated the same value, and
whether it had a mechanism there.

People, and no lint, judge whether a name is the domain's, whether a rationale is substantive,
and whether a pull request description reads well to the lead. v2 stated all three as rules
and then measured compliance by counting markers (566 `LINT-ALLOW` sites, each supposedly
carrying an analysis; the tree's own guide named the failure mode
"performance-of-compliance"). v3's answer is to shrink the surface being judged (an operation
under ninety lines, a one-page register, a ten-section description, a six-section engine
template) and to say "prose only" in the rows below where that is the truth. Of v2's
twenty-four families of stated guarantees, the string-composition, mutation, type-closedness
and layering families collapse into L7 and L8 and the analyzers; lineage and the transform
registry are retired with the chain; the cutover governance archives with the cutover; the
rest are here.

### 4.1 Safety

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| S1 | `BlockOnPossibleDataLoss` is never relaxed | No verb, flag, profile or script in v3 relaxes `BlockOnPossibleDataLoss` against a named environment; the Permissive profile is accepted only for a disposable target. | `io/Publish` refuses a `Target.Environment` under Permissive (exit 9); the only profiles the gate accepts are the two under `ci/profiles/` | `Io.Tests: "permissive never reaches an environment"` | `HANDOFF_SESSION_2026_08_26.md` §2 (the owner's axiom); prose only |
| S2 | No silent downgrade | Where the tool cannot do exactly what was asked it refuses with a code and a remedy; it never approximates, drops, or comments out. | `Error` is the only failure type; a catch-all pattern arm that returns success is an analyzer error; the trigger-body case is a refusal, not a tolerance | `Kernel.Tests: "no arm returns success by default"`; law 3 | `CLAUDE.md` §5 "downgrades never silent"; partly mechanized (`ToleratedDivergence`) |
| S3 | The explicit negative is a finding | Every section of the pull request description renders; a section with nothing to report says so in one sentence and is never omitted or padded. | `PullRequestDescription.render` refuses an empty section | `Kernel.Tests: "every section renders"` | `pr-template/schema-change.md` ground rule; gate `register` |
| S4 | A rename keeps its data | A change containing a rename with no refactorlog entry is refused before any publish. | `classify` raises `RenameWithoutRefactorlog` as `Error` | law 6; `Io.Tests` | `_index/identity-and-refactorlog`; prose only |
| S5 | Data corrections leave a receipt | Any pre-deploy script that modifies rows is recorded with rows-before, rows-after and the approving human in the pull request description's data section. | `PullRequestDescription.TheData` carries the three fields; the gate refuses a data-modifying pre-deploy whose description lacks them | `Io.Tests: "reconcile without receipt is refused"` | `ApprovedDataCorrections` (1,669 lines, cutover-era); the receipt survives, the workflow does not |
| S6 | The lag window is a lock | A release that touches a table with an open multi-phase window is refused at the gate. | `estate check inflight` (exit 9) | `Io.Tests`; the PR gate | `inflight-check.mjs`; mechanized 2026-08-28 |
| S7 | Least privilege by type | Only `move` can write to a named environment, only to one listed as writable in `environments.json`, and never to Prod. | `Target = Disposable | Environment of name`; `Publish` accepts `Disposable` only; `move` checks the environments file list | `Io.Tests: "no verb writes to Prod"` | `WriteSignoff`/`ActConsent` (cutover-era consent); replaced by the type |
| S8 | CDC-tracked tables are known | A column-list change on a CDC-tracked table is an `Error` finding until the pre-deploy names the capture-instance step. | `classify` reads `ledgers/cdc-tracked.md`; `check cdc` fills it from `sys.tables.is_tracked_by_cdc` | `Io.Tests` | the tree removed CDC 2026-08-21; new |

### 4.2 Determinism and idempotence

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| D1 | Same inputs, same bytes | `emit` over the same schema and decisions yields a byte-identical bundle on any machine, any OS, any input ordering. | `SortedArray<T>` sorted at construction with ordinal comparers; no `Dictionary` iteration in `Emit`; `Random` and the clock banned from the kernel | law 1; `BannedSymbols.txt` | T1; `NoUnsafeTimeInCoreAnalyzer`; mechanized |
| D2 | Culture never reaches output | Every number, date and string comparison in emission, JSON, logs and pull request descriptions uses the invariant culture; `$"{d}"` on a German or Turkish laptop must produce the same bytes as on the build agent. | analyzer rules CA1304, CA1305, CA1307, CA1309 as errors in `kernel/` and `io/`; `CultureInfo.CurrentCulture` in `BannedSymbols.txt`; law 1 gets a second leg that runs the golden emit under `tr-TR` and `de-DE` and asserts byte identity | `.editorconfig` severity; `Kernel.Tests: "law 1 under tr-TR"` | `lint-discipline.sh` rule 18d (interpolated strings) named exactly this and is retired with the lint; the estate's laptops are the machines it was protecting |
| D3 | Bytes are pinned, and written by one writer | Every file `emit` creates is UTF-8 without a byte-order mark with `\n` line endings on every platform; a file it overwrites keeps the encoding and line ending it already had; the repository declares the same in `.gitattributes` so a Windows checkout does not rewrite them; and a failed emit leaves the repository byte-identical to before. | one `io/Write.cs` (about forty lines: the pin, preserve-on-overwrite, atomic replace via a staging directory); `File.WriteAllText` and `TextWriter.WriteLine` banned outside it; `.gitattributes` with `* text=auto eol=lf` and `*.sql text eol=lf` (there is none in either generation today); law 1 and law 3 each get a `windows-latest` leg | `Io.Tests: "law 3 on Windows"`; `Io.Tests: "a failed emit leaves no trace"`; `BannedSymbols.txt` | v2 pinned it in `PinnedWriting.fs` and restated it in four emitter comments; atomic emission was `Compose.write` with seven tests; both are in the companion's delete column, and this row is where their value survives |
| D4 | Redeploy is silent | Publishing an unchanged bundle twice generates an empty second script and zero CDC capture rows. | law 8 | `Io.Tests` (CDC-isolated fixture) | CDC-silence (`V2_DRIVER.md:68`); mechanized (T15) |
| D5 | Seeds are idempotent | A guarded `MERGE` over unchanged rows touches zero rows; the content hash is unchanged. | law 11's seed half; `Emit` writes only guarded `MERGE` | `Io.Tests` | F12 (`pg_seed`); `_index/idempotent-seed`; mechanized in the proof lane |
| D6 | The generator repeats itself | The same seed generates byte-identical rows; `SyntheticData.Generate` takes its random number generator as a parameter. | law 11; `Random` banned in `kernel/` | `Kernel.Tests`; the golden fixture of v2's rows at one seed | `THE_TWIN.md` law 1; mechanized (`Check.fs`) |
| D7 | Reading back is faithful | Reading back what was emitted reproduces it after `SqlType.coarsen`, modulo a named list that starts empty. | law 2 | `Io.Tests` | the canary; A18/T16; mechanized |

### 4.3 Privacy

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| P1 | No real data on a laptop | The synthetic copy is generated from a literal-free shape tier; the rich tier never enters a repository; no runbook restores a real backup. | law 12; `synthetic-copy evidence verify` refuses a literal; `.gitignore` and a CI grep refuse `evidence.rich.*` | `Io.Tests`; CI | `THE_TWIN.md` law 3; mechanized; `PROVING_PATH_WINDOWS.md`'s `.bak` route is deleted |
| P2 | Measuring reads shape, not rows | `profile` captures counts, null rates, lengths, distinct counts and distributions; it never writes a row value into evidence, a log, or a verdict. | `Evidence` has no field for a value; the SQL `measure` runs is reviewed against that type; the sampling policy caps reads | `Kernel.Tests` (the type); `Io.Tests` (`measure`'s SQL is aggregate-only) | `SamplingPolicy`; partly mechanized |
| P3 | Masking is the default | Any value that must be realistic (names, emails, phones) is generated by a masking rule, never copied. | `SyntheticData.Generate` (a PII kind becomes a Faker value at the io boundary) | `Kernel.Tests` | `SyntheticCorrection`; mechanized |

### 4.4 Security and secrets

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| X1 | No secret in a file | A connection is always a reference (`env:NAME` or `file:path`), never an inline string; a config with a credential-shaped key is refused. | `ConnectionResolver` accepts references only; `environments.json`'s schema has no credential key | `Io.Tests: "inline credential refused"`; CI secret scan | D9; mechanized (`Config.parse`) |
| X2 | No secret in output | No verdict, log line, `gate.json`, or exception message carries a connection string, a password, or a row value. Where SQL Server's own message carries an offending value (`Msg 2627`, `Msg 547` name the duplicate or the orphan), the message is kept, the value is masked, and "Not checked" says the full text is in the run log. | a `ConnRef` type whose `ToString()` returns the reference (`env:OSM_DEV`), never the resolved string; a `Msg` scrubber for the value fragment; a test drives every refusal path with a password-bearing connection and greps the whole output | `Io.Tests: "no output contains Password="`; `Kernel.Tests: "Msg values are masked"` | D9 governs config only; nothing governs output today; new. This row and P1 are in tension (the pull request description must carry the verbatim message; the synthetic copy must carry no literal), and the masking rule is how the tension resolves |
| X3 | Supply chain is pinned | Every package version is pinned centrally, restored in locked mode, audited, and listed in an allowlist with its licence; a new package is a decision line. | `Directory.Packages.props`; `packages.lock.json` with `RestoreLockedMode` in CI; `NuGetAudit` on; `ci/packages.allow` checked by a test | `Budgets.Tests: "packages equal the allowlist"` | not stated (v2 pinned in `.fsproj`, no lock, no allowlist); new |
| X4 | Builds are deterministic | The same commit produces the same binaries; CI builds with `ContinuousIntegrationBuild` and `Deterministic`. | project properties; a CI step compares two builds' hashes | CI | not stated; new |
| X5 | No telemetry, no phone-home | `estate` opens no network connection except to the SQL Server it was given and, for `synthetic-copy restore`, the artifact path it was given; the build sends nothing either. | `HttpClient` and `System.Net` banned in `kernel/` and `cli/`; `io/` allows only `Microsoft.Data.SqlClient` and Docker's local socket; `DOTNET_CLI_TELEMETRY_OPTOUT=1` in `Directory.Build.props` and every lane; one sentence in the README | `BannedSymbols.txt`; `Io.Tests`; CI | not stated; the opt-out variable appears nowhere in either generation; new |
| X6 | Agents cannot reach the archive by accident | `archive/` is denied to agent file reads and hidden from search tools by default. | `.claude/settings.json` `permissions.deny: Read(./archive/**)`; root `.ignore` lists `archive/` (ripgrep honours it) | `.claude/settings.json`; `.ignore` | not stated; new |

### 4.5 Reproducibility and provenance

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| R1 | DacFx and SQL Server are pinned and stamped | Every verdict names the DacFx version, the SQL Server image digest, and the tool version it ran on; a version that differs from the pin is a refusal. | law 10; the `Provenance` fields `DacFx` and `Server`; `ledgers/toolchain.md` read by `prove` | `Io.Tests` | `estate/toolchain.md`; the pin is UNPINNED today; partly mechanized (the hook stamps `-unpinned`) |
| R2 | Every claim has a receipt | A verdict carries the disposable database's name, the generated script's hash, and the timestamp; a pull request description cites them; a finding in `findings.md` names its database. | `Verdict` fields are required; the register lint refuses a "what proving showed" section with no receipt | `Kernel.Tests`; `Budgets.Tests: register` | `FINDINGS_AND_CHANGES.md` (database names as receipts); prose only |
| R3 | Git is the timeline | There is no run store, no episode store, no lifecycle file; artifacts are named by fingerprint; the pull request is the account of a change. | absence (nothing to mechanize); the manifest has no such file | — | `Episode`/`Lifecycle`/`RunLedger` (deleted) |
| R4 | Findings are refuted in the open | A finding is overturned only by a dated entry that names the receipt that overturns it; the old entry is struck through, never deleted. | `findings.md` format; a test that no `F<n>` identifier disappears between commits | `Budgets.Tests: "findings are append-only"` | F5 → F9; prose only |
| R5 | The supported window is stated | .NET: the current LTS only (10 today; 9 is a standard-term release and leaves support in November 2026). SQL Server: 2019 and 2022, and LocalDB of the same versions, with the image pinned by digest, never `latest`. DacFx: the pinned version and the one before it. Visual Studio: the rung the pilot lands on. | `global.json` pins the LTS feature band with `rollForward: latestPatch` (v1 and v2 pin `9.0.314` with roll-forward disabled; the loosening is a decision line); `synthetic-copy up` and `prove` read `@@VERSION` and refuse a SQL Server or a DacFx outside the window by name; `Io.Tests` runs on both SQL versions | CI matrix; `ledgers/toolchain.md` | `estate/toolchain.md` names the floating `2022-latest` tag as a risk; the rest is new |

### 4.6 Portability and operability

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| O1 | Docker is never a requirement | Every proof that does not need CDC or the scale tier runs on LocalDB; the verb chooses the local server and says which it chose. | `io/SyntheticCopy` has one local-server abstraction with two artifacts; `prove` refuses CDC proofs on LocalDB with the reason | `Io.Tests` on both local servers; `PORTABILITY.md` | `PORTABILITY.md`; partly mechanized (`prove.mjs` config) |
| O2 | Windows is a first-class host | The fast lane and the LocalDB fixture run on `windows-latest`; paths are joined, never concatenated; object names compare case-insensitively, file names ordinally. | CI matrix; analyzer CA1310/CA1862; `Path.Combine` only (banned: string `+` on paths in `io/`) | CI; `BannedSymbols.txt` | not stated (the team is on Windows; the corpus was proven on Linux); new |
| O3 | One command per intent | Every step of every workflow is an `estate` verb with `--json`; no document shows a tool invocation that is not an `estate` verb, and every hook is under ten lines over a verb. | the docs lint refuses `sqlpackage `, `sqlcmd `, `docker ` in prose outside `knowledge/handbook/`; the hooks budget | `Budgets.Tests: "no tool invocation outside a verb"`; `Budgets.Tests: hooks ≤ 30 lines` | the review's Layer 1; `prove.mjs`; partly mechanized |
| O4 | Refuse and route | An unsupported object or operation is refused with who does it and what to check; the tool never guesses. | `Error` codes catalogued in `--help --json`; the "not covered" list in the catalog | `Cli.Tests: "every error code has a remedy"` | `rebuild-index` (refuse-and-route); prose only |
| O5 | Cleanup is guaranteed | A disposable database or container is named `estate_<fingerprint>_<pid>`, dropped on exit or cancellation, and swept by `estate synthetic-copy gc` on the next start if a crash left it. | `try/finally` around every `Disposable`; the sweep on `doctor`; cancellation tokens through `io/` | `Io.Tests: "kill mid-prove, next run sweeps"` | v2 leaked 209 databases on one warm container (rule 2); partly mechanized later |
| O6 | Two sessions do not collide | Two `estate` processes on one machine never share a database name, a scratch directory, a bundle output or the synthetic copy's state. | one prefix per session (`estate_<host>_<pid>_<rand>`); `synthetic-copy up` takes an advisory lock on `[synthetic_copy].[__state]`; `emit --out` refuses a non-empty directory it did not create; the invariant is restated in `tests/README.md` because the only written statement of it today is the self-test protocol, which is deleted | `Io.Tests` (two concurrent proves) | `self-test/PROTOCOL.md` (`PG_<id>_<rand>`); prose only |
| O7 | Long operations can be cancelled | Every verb honours Ctrl-C and `--timeout` within a second, leaves the state a normal exit leaves, and exits 130. | one `CancellationToken` threaded from `cli/` through `io/`; `Console.CancelKeyPress` → cooperative cancel → the cleanup path of O5 | `Io.Tests: "cancel mid-prove"` | eight occurrences of `CancellationToken` in 118,686 lines; a generated set of 1.18 million rows that cannot be interrupted is the concrete failure; new |
| O8 | Time is budgeted | On a 300-table estate: `classify`, `diff`, `decide` under ten seconds; `read` and `emit` under sixty; `prove` bounded by DacFx, not by the tool, and under five minutes at the ≤1M tier; `synthetic-copy restore` under five minutes; an aggregate query capped by the sampling policy and a 300-second timeout. The numbers accrete from real proofs (`prove` records its wall-clock and tier) rather than from a synthetic bench. | the scale lane measures and records in `ledgers/scale-datapoints.md`; one scale test in `Io.Tests` over the golden schema replicated to 300 tables; never a Stop hook | `Scale.Tests` | `Bench` + the perf gate (deleted: void under load, self-feeding); the 62-line datapoints ledger is the part that had value |
| O9 | Memory is bounded | `read` and `emit` stay under 1 GB peak on 300 tables; `profile` streams and never materialises a table; the fast test lane runs under 2 GB. | `readRows` is an `IAsyncEnumerable`; the same scale test asserts peak working set; CI runs the fast lane in a 2 GB container | `Io.Tests`; CI | survival rule 1 (the pools OOM-killed the host); mechanized by the CI matrix |
| O11 | Large output is truncated, never dumped | A verdict's verbatim message and a change over 300 tables do not overflow an agent's context; a payload that can be large carries `"truncated": true, "full": "<path>"` and a `--summary` form, and a 300-table change's default rendering is under a stated line count. | the truncation fields in the JSON contract; a test over the golden schema replicated to 300 tables | `Cli.Tests: "large change renders short"` | not stated; the consumer is a modest-context session; new |
| O12 | Time is UTC, stamped at the boundary | Every date a pull request description, a ledger row or a fingerprint carries is UTC (`yyyy-MM-dd`, or the round-trip format), produced by one clock in `io/`, never in the kernel; the in-flight window comparison uses the same clock. | `DateTime.Now` banned everywhere; `UtcNow` only behind one `IClock` in `io/`; a format lint on ledger dates | `BannedSymbols.txt`; `Budgets.Tests: ledgers` | nothing stated the zone; the window gate compared against "today" on whatever machine ran it; new |
| O10 | Offline works | With no network, every verb except `synthetic-copy restore` from a remote path succeeds against a local SQL Server, and the build restores from the local cache. | X5's bans; `RestoreLockedMode` with a warmed cache in CI | CI (one lane runs with networking disabled) | not stated; new |

### 4.7 Legibility and maintainability

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| L1 | The register applies to every surface | Every pull request description, refusal message, README, and this file are agentless, finding-first, true-verbed, evidence-beneath, exact-named, unverified-admitted, and free of the retired vocabulary. | the register lint runs over `knowledge/samples/`, every `Error.Message`, and every hand-written file in the manifest | `Budgets.Tests: register` (three tests); `Cli.Tests: refusals in register` | `THE_RECORD.md` (nine rules) + gate `register`; mechanized for pull request descriptions only |
| L2 | No restated count | A hand-written file never carries a count, a line number, a file list, or a verb list that a generated file carries. | the docs lint over the manifest's hand-written entries | `Budgets.Tests: "no restated counts"` | `CLAUDE.md` §8; prose only, and violated four times |
| L3 | Budgets are tests | Code per package, tests, docs outside `knowledge/`, `knowledge/`, each hook, `knowledge/README.md`, the router, an operation, and the authoring path each have a ceiling, and exceeding one is a red build. | `Budgets.Tests` reads `ci/budgets.json` | `Budgets.Tests` | `CRYSTALLINE_FORM.md` estimated; nothing enforced; new |
| L4 | The inventory is data | Every markdown file in the tree is a row in `ci/docs.manifest.json` with reader, moment, budget, kind and owner; the tree equals the manifest. | the manifest test | `Budgets.Tests: manifest` | not stated; new |
| L5 | One vocabulary | A term is defined once, in the glossary; the retired vocabulary (Appendix A) is banned from every surface; an SSDT term is defined at first use in `knowledge/`. | the banned-terms lint; the glossary test (every glossary term appears in the tree, every retired term does not) | `Budgets.Tests: vocabulary` | `THE_RECORD.md` §7 (banned list); mechanized for pull request descriptions |
| L6 | Laws are tests | A law is listed in `LAWS.md` only from a green test with an English name; there are no skip attributes in the tree. | `LAWS.md` is generated from the test tree; a test asserts zero `Skip =` | `ci/laws.sh`; `Budgets.Tests: "no skips"` | 209 skips; `AXIOMS.md` with 38 stubs; prose |
| L7 | Dependencies point one way | `kernel` references the BCL only and no public member of a kernel type returns a `Task`, `ValueTask` or `IAsyncEnumerable` (a banned-symbol list matches symbols, not the state machine an `async` method builds); `io` does not reference `cli`; nothing references `knowledge/` or `ci/`. | NetArchTest (the direction and the no-async rule); `BannedSymbols.txt` | law "dependencies point one way" | `hex-*` lints; `NoUnsafeTimeInCoreAnalyzer`; mechanized |
| L9 | Tests never retry, never skip | A flaky test is quarantined by name with a dated finding and an owner, or deleted; no retry attribute exists in the tree; no test skips itself in a way that reads as a pass. | no retry package in `ci/packages.allow`; the zero-skip test; the per-local-server report in the matrix; the TRX logger is the default | CI; `Budgets.Tests: "no skips"` | survival rules 3, 4, 12; 209 skips; prose |
| L10 | CI is fast enough to wait for | The fast lane finishes in five minutes; the fixture lane in twenty; the PR gate in ten; the proof lane in sixty; every job declares `timeout-minutes` and prints its elapsed time. | `timeout-minutes` on every job (today three of the six lanes' jobs declare one, and none of the three required checks do) | CI | not stated; new |
| L11 | Reading time is budgeted | The README reads in five minutes; `knowledge/README.md` in five; an operation in three; the prose a Copilot session must hold to author one change (the router, the one instruction that matched, one operation, one shared file, the description page) fits beside the open file, and the sum is a test. Today that path is 665 lines (router 60 + instruction 23 + the largest operation 131 + `THE_RECORD.md` and its forms page 451); after the trim it is about 320. | the budgets test computes the path sum with a ceiling the pilot sets | `Budgets.Tests: "authoring path"` | the review's §3.6 ("every hop is a failure point"); the companion budgeted a Claude Code session's reading and not a Copilot session's; new |
| L12 | Written for the team | Every agent-facing and reviewer-facing file is written for a developer who knows SQL and OutSystems and has never used SSDT; a pull request description is approvable with no term the reviewer must look up. | the banned-terms lint catches the negative half; `shared/vocabulary.md` is the one anchored-explanation page; the pilot's four reviewers judge the rest | `Budgets.Tests: vocabulary`; prose plus a pilot, and the row says so | `THE_RECORD.md` §9 states it as its one sentence; prose |

### 4.8 Agent conduct

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| A1 | Prove before you claim | An agent never states how a change ships without a verdict; a pre-proof classification is marked provisional in its first word. | `classify` prints `provisional:`; the gate regenerates the pull request description from the verdict and diffs it | `Cli.Tests`; the PR gate | the tree's thesis; the rubric; prose |
| A2 | Verify before you diagnose | Before claiming a tool, a daemon or a database is missing, an agent runs `estate doctor` and quotes its line. | the verb; the SessionStart hook prints it | `AGENTS.md` (one line) | root `AGENTS.md` "verify-before-diagnose"; the `docker-probe.sh` hook |
| A3 | Ask one question | Authoring poses exactly one business question a human must answer, in the developer's words, and never answers it by reading data. | `authoring.md` S0's exit condition; the pull request description's `Not checked` names the open question | `Budgets.Tests: register` (a pull request description with two open questions fails) | `intake` rules; prose |
| A4 | The handoff is the pull request | A session ends with code, tests, a PR body in the register, at most one decision line, and a rewritten `NEXT.md`; it writes no letter, chapter, or status section. | the manifest (no such files can exist); `NEXT.md`'s budget | `Budgets.Tests: manifest` | the write budget (§15); prose |
| A5 | No self-approval, no machine approval | The gate reports; it never approves. A change's author never approves it, at any seniority. | the lanes hold no approval token; branch policy requires a dev lead | ADO branch policy; `ci/README.md` | `estate/reviewers.md`; prose |
| A6 | One hop, and never the archive | An agent reads the entry file for its role and, from it, one hop into `knowledge/`; it does not read `archive/` unless the task names it. | X6's permission and `.ignore`; `AGENTS.md` read order | `.claude/settings.json` | `CLAUDE.md` §2 reading order; prose |
| A7 | Refuted findings stay visible | An agent that overturns a finding writes the new entry with its receipt and strikes the old; it never edits history. | R4 | `Budgets.Tests: "findings are append-only"` | F5 → F9; prose |
| A8 | A decision is one line | `2026-09-17 · <the decision in one sentence> · #<PR>`; the reasoning is in the pull request. | `DECISIONS.md`'s format test (every line matches) | `Budgets.Tests: decisions` | 480 four-field entries; prose |
| A9 | The pull request description is what the gate reads | The PR body's "how it ships" and "what proving showed" sections match the regenerated description or the gate says where they differ. | `estate gate --pr-body` | the PR gate | `author-pr`'s hard rules; prose |
| A10 | A new document is a review, not a habit | Adding a markdown file requires adding its manifest row (reader, moment, budget, owner) in the same pull request. | L4 | `Budgets.Tests: manifest` | the write budget; prose |

### 4.9 Emission fidelity

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| E1 | Emit then read is the identity | Reading back what was emitted reproduces it after `SqlType.coarsen`, modulo a named list that starts empty. | law 2 | `Io.Tests` | the canary; mechanized |
| E2 | Emit over the estate is a no-op | `estate emit` against the repository's own state produces byte-identical files, on every merge. | law 3 and the process law that runs it in CI | `Io.Tests`; the merge lane | the idempotent redeploy; mechanized |
| E3 | A vanilla policy changes nothing | With no decisions, emission is the faithful projection of the source. | law 4 | `Kernel.Tests` | skeleton purity; mechanized |
| E4 | SQL is built, never concatenated | Every emitted statement is a `Statement` value rendered through ScriptDom's generator; in `io/Render` and `io/Emit`, `string.Concat`, `string.Format`, `StringBuilder` and interpolation are banned except in the final writer. | the closed `Statement` hierarchy; `BannedSymbols.txt` scoped to the two modules | `Io.Tests` (the analyzer runs) | pillars 1–3 and eight lint rules with 566 exemptions; mechanized too broadly; scoped down |
| E5 | An unparseable object is refused, not degraded | A trigger body ScriptDom cannot parse, or any object the reader cannot represent exactly, is a refusal with a code, never a comment marker or a tolerated divergence. | an `Error` code (`read.unparseable`) and one test per object kind | `Io.Tests: "unparseable trigger is refused"` | `ToleratedDivergence.TriggerBodyUnparsedDropped`; prose plus a tolerance; the tolerance goes |
| E6 | Data-loss steps are named before the publish | Every statement `BlockOnPossibleDataLoss` will refuse on a populated table is listed by `diff` before any publish runs. | `Change.dataLoss` | `Kernel.Tests` | the tree's classification step; mechanized in the change |
| E7 | Rollback is computed or admitted | Every pull request description says how to reverse the change or names what is not auto-undone, with the recorded originals a manual restore would use. | law 7; `PullRequestDescription`'s rollback section is required | `Kernel.Tests` | `THE_RECORD_FORMS.md`; prose |
| E8 | Load order has an explicit cycle policy | A cycle is refused, deferred on a nullable leg, or broken by a named allow-list; never silently ordered. | `Order.CyclePolicy`; a property on `Order` | `Kernel.Tests` | v1's allow-list and v2's per-component order; mechanized |
| E9 | Windows paths fit | Every path in a bundle is under 260 characters from a plausible root, no two emitted files differ only by case, and file names compare ordinally. | an `Emit` test over the golden schema replicated to 300 tables asserting length and case-insensitive uniqueness; the identifier-budget hashing is the remedy | `Io.Tests: "bundle paths fit Windows"` | nothing checks it; the per-folder layout is exactly the shape that hits 260; new |

### 4.10 Process and gating

| # | Value | Requirement | Mechanism | Where | v2 |
|---|---|---|---|---|---|
| G1 | The pipeline reproduces the proof | The gate rebuilds the dacpac, restores the synthetic copy, publishes the pull request's combined change under the pipeline's publish profile, and posts the verdict and the description diff. | `estate gate`; the PR lane | `ci/` | the review's Layer 3; half-shipped (`inflight-check.mjs`, the ADO template) |
| G2 | The human makes the business call | The one question only a human can answer is posed, recorded with its owner, and never answered by reading data. | `authoring.md` S0 and S6; the pull request description's `Not checked` names it if open | `Budgets.Tests: register` | `THE_DECISION_TREE.md` S6; prose |
| G3 | One approver class, no self-approval, no machine approval | A dev lead approves every schema change and never their own; the gate reports and never approves; a lead's own change needs the other lead. | Azure DevOps branch policy; the lanes hold no approval token; `ledgers/reviewers.md` | `ci/README.md`; prose for the policy | `estate/reviewers.md` (two rows unfilled); prose |
| G4 | First time on this estate is a lookup | The added-scrutiny line comes from `ledgers/operations.md`, appended at the production apply, never from memory; a shipped change with no row is a gate finding. | `classify` reads the ledger; the gate refuses to close a release whose operation has no row | `Io.Tests` | `estate/README.md`; the ledger has no rows; prose |
| G5 | Prod is not Dev | Before any Prod release, Prod's row counts land in `row-tiers.md` with their date, and the gate reads the tier for the target environment. | `estate measure --target prod --counts`; the gate's ledger read is per environment | `Io.Tests` | the tier ledger holds four sample tables; new |
| G6 | Nothing is proven on a shared database | Every proof runs against a disposable copy the session created and owns. | `Target.Disposable` is the only target `Publish` accepts under Permissive or Strict for `prove`; the publish-profile instruction file | `Io.Tests` | the self-test protocol; the profile instruction; mechanized in the bundle only |
| G7 | The invisible half has an owner | A change is complete when the external entity is refreshed in Integration Studio in every environment, and the pull request description's after-deploy section says so per environment. | `PullRequestDescription.AfterDeploy` carries the refresh line; `check outsystems` sees a missing one | `Kernel.Tests`; `Io.Tests` | nine operations mention it; nothing owned it; new |
| G8 | A green deploy can still destroy data, and the design says so | A second publish of a landed contract-phase release can re-create a column, backfill it, and report success; the lag window is a lock and the data section carries receipts because of it. | S5, S6, law 9 | `Io.Tests`; the gate | F3, F17; mechanized 2026-08-28 (`inflight-check.mjs`) |
| G9 | The bundle is fresh, visibly | A developer can see in one glance whether the bundle in their repository is current, and the estate's pipeline refuses a hand-edited copy. | the fingerprint in the router's first line and in `estate --version`; the estate pipeline's `--check` and citation resolution | `Budgets.Tests: PackagerCheck`; the estate pipeline | the fingerprint (`047e6c5dbd9c`) exists; nothing shows it to the developer; new |

### 4.11 What v2 mechanized that v3 does not carry

- **The string-composition lints** (rules 3–5, 18b–e: `+`, `String.Format`, `String.Concat`,
  `String.concat`, interpolation, `String.Join`), 566 exemptions in `src/`. Two properties were
  being defended: that text is built by the typed tree (kept as E4, scoped to the two modules
  that emit text) and, in rule 18d's own words, that interpolated strings pick up the current
  culture and break byte determinism (kept as D2, as analyzer errors and a law-1 leg under
  `tr-TR`). Six hundred lines of shell and 566 exemptions defended two properties that three
  analyzer settings and one forty-line writer defend better.
- **The mutation lints** (`let mutable`, mutable collections, `<-`, mutable record fields).
  Replaced by records, `SortedArray<T>`, and `readonly` structs; the kernel's purity is a project
  reference and `BannedSymbols.txt`, not a grep.
- **The type-erasure, reflection and wide-tuple lints.** Not carried. Reflection is used in
  exactly two places (the JSON source generator's output and NetArchTest) and both are tests or
  generated.
- **The perf gate** (`Bench`, `perf-gate.sh`, the Stop hook, the baseline with a decision
  amendment). Its own rules said its verdict was void under concurrent load. Replaced by O8:
  measured from real proofs, recorded in a ledger, never a hook.
- **`PinnedWriting` and atomic emission** (`Compose.write`'s staging-then-replace, seven
  tests). Both are in the companion's delete column and both had real value: the one shared
  statement of the byte contract, and the guarantee that a failed emit leaves no half-written
  bundle in a repository a developer then commits. D3 is where they survive, in `io/Write.cs`.
- **The cutover governance** (R6, the per-pair sign-off, the T-30/T-15 ladder, N=10 green
  runs). Finite; archived with the cutover.
- **The transform registry's totality tests** and the axiom skip-with-trigger convention.
  Replaced by L6.

### 4.12 Which of the fifteen survival rules survive

| Rule | Fate in v3 |
|---|---|
| 1 never run the pools together; CDC tests isolated; Docker tests in Integration | a CI matrix and a test category; the isolation fixture is kept (S8, D4) |
| 2 connection failures mean the container died | `prove`/`synthetic-copy` refuse with `local-server down: run estate synthetic-copy up` (O5) |
| 3 never `pgrep`-guard or `tail` a run | retired with the shell recipes; the lanes run tests directly and every job has a timeout |
| 4 re-run with the TRX logger; console output interleaves and lies | true of `dotnet test` in any language: the TRX logger is the default (L9) and the sentence is in `tests/README.md` |
| 5, 6 F# compiler shapes | retired with F# |
| 7 `{ x with … }` silently inherits constructor defaults | retired with F#, and its C# counterpart is one line in `tests/README.md`: a `with` expression on a record copies fields and bypasses any validating factory, so invariants that span fields (a primary key names existing columns) live in `Schema.Create` and in the laws, never in a setter |
| 8 `ReadSide` marks every table `Static` | fixed by construction; kept as a case in law 2 |
| 9 `ISNULL(col,col)` strips IDENTITY through `SELECT INTO`; a `CASE` wrapper does not | a proven SQL Server fact: an entry in `knowledge/findings.md` with its receipt, cited by the identity-swap operation |
| 10 `HANDOFF.md` is prepend-only | no such file |
| 11, 13 the perf gate's baseline and solo verdicts | no such gate |
| 12 soft-skipped Docker tests look like passes | still true of xUnit in any language: L6 (no skips) and the matrix's per-local-server report, and the sentence stays in `tests/README.md` |
| 14 the content hash and the `''`/`NULL` comparator | two sentences in `description.md`'s guidance on "what proving showed"; the comparator is `RowFidelity`, kept |
| 15 a stale RID directory shadows a build | still true of the SDK in any language: `dotnet clean` in `ci/build.sh`; one line in `AGENTS.md` |
| 1 (first half) never run the pools together | weakened, not retired: two test projects and a fifth of the lines make the OOM unlikely, and `Io.Tests` is declared serial, which is the same rule in another form; one line in `tests/README.md` |

Four survive as sentences in the test README (1's second half, 4, 12, 15), three become a law or
a finding (8, 9, 14), one gains a C# counterpart (7), and the rest are retired with the code
they describe; the ratio supports §3's second principle.

---

## 5. The inventory

Every document in both repositories, as data. `ci/docs.manifest.json` is the source; `DOCS.md`
is generated from it; §1's table is generated from it; the manifest test (§10) asserts the
tree equals it. The columns are the manifest's fields.

```json
{ "path": "AGENTS.md",
  "kind": "hand",                          // hand | generated
  "owner": "engine",                       // engine | estate | vendored (engine writes, estate receives)
  "reader": ["agent", "maintainer"],
  "moment": ["session-start", "change"],
  "budget": 120,                           // lines; generated files carry null
  "generator": null,                       // for generated files: the script or verb
  "ancestor": ["sidecar/projection/CLAUDE.md", "AGENTS.md"] }
```

### 5.1 The engine repository

| Path | Kind | Reader · moment | Budget | Ancestor |
|---|---|---|---:|---|
| `README.md` | hand | a person · first day | 150 | `readme.md` (1,162), `sidecar/projection/README.md` (661) |
| `AGENTS.md` | hand | any agent, a maintainer · session start, a change | 120 | root `AGENTS.md` (253), `sidecar/projection/CLAUDE.md` (302), `KICKOFF.md` |
| `CLAUDE.md` (**new** at the root; today only the sidecar and the tree have one) | hand | Claude Code · session start | 20 | `sidecar/projection/CLAUDE.md`, `ssdt-agent/CLAUDE.md` |
| `ARCHITECTURE.md` | hand | a maintainer, the owner · a change, a decision | 3,000 | `V3_ARCHITECTURE.md` §5–§10, §13 |
| `VALUES.md` | hand | a maintainer · first day; a review bot · an engine PR | 300 | §4 of this document; `PRODUCT_AXIOMS.md`, `architecture-guardrails.md`, `CLAUDE.md` §5 |
| `DECISIONS.md` | hand | anyone · a decision | one line per entry; format-tested | `DECISIONS.md` (480 four-field entries) |
| `NEXT.md` | hand | a session · session start | 40, rewritten | `HANDOFF.md`, the backlogs |
| `LAWS.md` | generated (`ci/laws.sh`) | a maintainer · a change; a review bot | — | `AXIOMS.md`, `NORTH_STAR.matrix.generated.md` |
| `DOCS.md` | generated (from the manifest) | anyone · first day | — | new |
| `kernel/README.md`, `io/README.md`, `cli/README.md`, `tests/README.md`, `ci/README.md` | hand | a maintainer · a change in that package | 80 each | per-project READMEs; `tests/README.md` |
| `io/SyntheticCopy/README.md` | hand | a maintainer · a change to the synthetic copy | 620 (kept as-is) | `THE_TWIN.md` + `THE_SYNTHETIC_DATA_DESIGN.md` |
| `cli/VERBS.md` | generated (`estate --help --json`) | an agent · a change; the bundle | — | `usageLines` in `Program.fs` |
| `cli/CONFIG.md` | generated (from `environments.json`'s schema) | a maintainer · configuring | — | `CONFIG_REFERENCE.md` + `projection.schema.json` |
| `.github/PULL_REQUEST_TEMPLATE/engine-change.md` | hand | a session, a review bot · an engine PR | 40 | new |
| `.github/PULL_REQUEST_TEMPLATE/schema-change.md` | generated (from `knowledge/description.md`) | a developer · a schema PR (this repository's own golden project) | — | `pr-template/schema-change.md` |
| `.claude/settings.json`, `.claude/hooks/session-start.sh`, `.claude/hooks/session-end.sh` | hand | the harness | 40 · 10 · 10 | `settings.json` (47), four hooks (814) |
| `.claude/skills/*/SKILL.md`, `.claude/agents/*.md` | generated (`estate knowledge package`) | Claude Code · dispatch | — | the same, generated by `ssdt-agent-package.mjs` |
| `.ignore` | hand | search tools | 5 | new |
| v1's agent-facing files: `notes/run-checklist.md`, `notes/meta/{directory-map,rg-signposts,toggle-surface,test-matrix}.md`, `tasks.md`, `architecture-guardrails.md` | archived with v1 | none after step 0 | — | successors: `tests/README.md` (the checklist and the test matrix), `cli/CONFIG.md` (the toggle surface), `VALUES.md` (the guardrails), `NEXT.md` (`tasks.md`); none for the directory map or the signposts |
| `archive/INDEX.md` | generated once (step 0) | the owner · provenance | — | new |
| `ci/docs.manifest.json`, `ci/budgets.json`, `ci/packages.allow` | hand (data) | the tests | — | new |

Hand-written prose outside `knowledge/`: 150 + 120 + 20 + 3,000 + 300 + 40 + 5 × 80 + 620 +
40 = **4,690 lines** plus `DECISIONS.md`, which grows one line per decision. The budget in the
companion's §6.5 is 7,000. The slack is deliberate and is not a target.

### 5.2 The knowledge tree (engine-owned, vendored to the estate)

| Path | Kind | Reader · moment | Budget | Ancestor |
|---|---|---|---:|---|
| `knowledge/README.md` | hand | a developer, a reviewer, an agent · first day; the router's one hop | 150 | `ssdt-agent/README.md` (162) |
| `knowledge/authoring.md` | hand | an authoring session · a change | 150 | `THE_DECISION_TREE.md` (215), `agents/intake.md` (162), `agents/change-author.md` (318), `confirm-intent`, `classify-mechanism`, `decompose`, `ask-the-developer` |
| `knowledge/reviewing.md` | hand | a reviewer, a reviewing session · a review | 100 | `agents/reviewer.md` (263), `skills/review/*` (495), `author-review` |
| `knowledge/description.md` | hand | everyone who writes a pull request description · a change, a review | 120 | `THE_RECORD.md` (308), `THE_RECORD_FORMS.md` (143), `author-pr` (223) |
| `knowledge/ops/<op>.md` × 45 | hand | an authoring session · the change | 90 each | `skills/op/*/SKILL.md` (64–131, median 94) |
| `knowledge/shared/<name>.md` × 8 | hand | an authoring session · when the op points there | 120 each | `skills/_index/*` (6, 750 lines) + `os-vocabulary` + `deploy-scripts` (trimmed) |
| `knowledge/findings.md` | hand, append-only | everyone · when a claim needs its receipt | grows; format-tested | `FINDINGS_AND_CHANGES.md` (502) |
| `knowledge/samples/<shape>.md` × 12 | hand | an authoring session · when the template needs an example | 90 each | `sample-prs/` (50 files, 3,755 lines) |
| `knowledge/handbook/<n>.md` × 8 | hand | an authoring session · when an op cites it | 2,500 total | `handbook/` (32 chapters, 8,980 lines): the eight the ops cite |
| `knowledge/ledgers/*.md` × 8 | hand: the formats and the empty tables only; **owner: estate, seeded once**; excluded from the bundle's fingerprint | the gate, a session · a release | format-tested | `estate/*.md` (272) + `cdc-tracked.md` (new) |
| `knowledge/PORTABILITY.md` | hand | a developer without Docker · first day | 94 | `PORTABILITY.md` (94) |
| `knowledge/INDEX.md` | generated | an agent without skill discovery · dispatch | — | `skills/INDEX.md` |
| `knowledge/copilot/**` | generated | the estate repository · every merge | — | `copilot-package/` (80 files) |

Knowledge total, hand-written: 150 + 150 + 100 + 120 + 45 × 85 + 8 × 110 + 400 + 12 × 80 +
2,500 + 94 + ~300 ≈ **9,500 lines** against the companion's 11,000.

### 5.3 The estate repository

| Path | Kind | Owner | Reader · moment |
|---|---|---|---|
| `.github/copilot-instructions.md` | generated | vendored | Copilot · every session (attaches itself) |
| `.github/instructions/{schema,pre-deploy,post-deploy,publish-profile}.instructions.md` | generated | vendored | Copilot · when a matching file is open |
| `.github/agents/{authoring,reviewing}.agent.md` | generated | vendored | Copilot with custom agents · a change, a review |
| `.github/prompts/schema-change.prompt.md` | generated | vendored | Copilot on Visual Studio 2022 · the entry prompt |
| `.github/skills/*/SKILL.md` | generated | vendored | Copilot with skill discovery · dispatch |
| `.github/PULL_REQUEST_TEMPLATE/schema-change.md`, `.azuredevops/pull_request_template/schema-change.md` | generated | vendored | a developer · a schema PR |
| `AGENTS.md` | generated (the router, for any non-Copilot agent) | vendored | any agent · session start |
| `knowledge/**` (minus `ledgers/`) | generated copy | vendored | as §5.2 |
| `estate/ledgers/*.md` | hand, append-only | **estate** | the gate, a session · a release |
| `estate/environments.json` | hand | **estate** | every verb · always (the environments, the writable targets, the local server) |
| `estate/evidence.shape.json`, `estate/synthetic-copy.json` | hand (produced by verbs, committed) | **estate** | `synthetic-copy` · the bake lane |
| `estate/profiles/{strict,permissive}.publish.xml` | hand (mirrored from the pipeline's task) | **estate** | `prove`, the gate |
| `pipelines/{gate,bake,proof}.yml` | generated (templates) | vendored | Azure DevOps · a PR, a merge, nightly |
| `.editorconfig`, `.gitattributes` | hand | **estate** | D3 |
| the SSDT project, its refactorlog, its scripts | hand | **estate** | the product |

The ownership rule in one sentence: the engine writes what is true of SQL Server and DacFx;
the estate writes what is true of the estate. A vendored file is regenerated by the bake lane
and arrives by pull request (§8.5); an estate-owned file is never touched by the engine except
through a verb that appends a row (`check cdc`, `measure --counts`).

---

## 6. The engine repository's instruction set

The drafts. Each is written to be committed as-is at step 1 of the migration and then kept
true by §10's mechanisms. Where a draft is a skeleton (the README, `ARCHITECTURE.md`), the
skeleton is the contract and the budget is the ceiling.

### 6.1 `README.md` (150 lines, five minutes)

```
# estate — the change engine for an OutSystems estate on SSDT

One paragraph: what the tool does (reads a schema from anywhere, measures the data beneath it,
computes the change between two states, proves the change against a real-shaped disposable
copy by letting DacFx publish it, writes the pull request description a reviewer approves by reading).

## The verbs                       — one line each, pointing to cli/VERBS.md; no count in prose
## Try it in five minutes          — estate doctor · estate synthetic-copy restore <artifact> ·
                                     estate prove --project tests/Golden/Golden.sqlproj · read the verdict
## Where things are                — kernel/ io/ cli/ knowledge/ ci/ tests/ archive/, one line each
## What it upholds                 — a pointer to VALUES.md and LAWS.md; no restatement
## For developers on the estate    — you do not need this repository; the bundle in your own
                                     repository is the product; start at knowledge/README.md there
## For maintainers                 — read AGENTS.md; then NEXT.md; then the package README
## Provenance                      — v1 and v2 are in archive/, indexed; DECISIONS.md is the log
```

No status section, no dated "what shipped," no counts. The five-minute path is the only
procedure the file contains, and it is three verbs.

### 6.2 `AGENTS.md` (120 lines, tool-agnostic)

The one instruction file for any agent that works in this repository. `CLAUDE.md` imports it;
a Copilot session in this repository reads it as `AGENTS.md`; a cloud session reads it first.
The draft, in full:

```markdown
# AGENTS.md — working in this repository

This repository is the engine behind schema changes on an OutSystems estate that moved to
SSDT. The product is `estate`, one CLI (its verbs are in `cli/VERBS.md`), and `knowledge/`,
the files a developer's Copilot session reads in the estate repository. Read this file, then `NEXT.md`,
then the README of the package being changed. Nothing else is required before starting.

## Where truth lives

- What the code is: `ARCHITECTURE.md`. What it upholds: `VALUES.md`. What is proven:
  `LAWS.md` (generated). What was decided: `DECISIONS.md`. What is next: `NEXT.md`.
- The domain (SQL Server, DacFx, the estate, the platform): `knowledge/`, starting at
  `knowledge/README.md`.
- The past: `archive/` holds v1 and v2. It is denied to file reads and hidden from search
  by default. Read it only when the task names it; `archive/INDEX.md` says what is where.

## Before anything

Run `estate doctor`. Its one line names the SDK, the DacFx pin, the local server (Docker or
LocalDB) and the synthetic copy's artifact, and every refusal in it carries a remedy. Quote
that line before claiming a tool, a daemon or a database is missing.

## Building and testing

- `dotnet build` — warnings are errors; the banned-API list is enforced in `kernel/` and `io/`.
- `dotnet test --filter Category=fast` before every push: under five minutes, no SQL Server.
- `dotnet test --filter Category=fixture` when `io/` changed: needs a SQL Server;
  `estate synthetic-copy up` provides one.
- The proof lane and the scale lane run in CI. Run them locally only when the task is about
  them, and never both in one `dotnet test`.
- A stale build after switching branches: `dotnet clean`; an old RID-specific output
  directory shadows a fresh build without an error.

## How to change things

- A kernel change starts with the test (a law or a property), then the type, then the
  function. Types are records and closed hierarchies; `ARCHITECTURE.md` §6.6 gives the encoding.
- A verb change updates its `--help` text in the same commit; `cli/VERBS.md` and the bundle
  are generated from it.
- A knowledge change edits the canonical file under `knowledge/`; the pointers, the index and
  the bundle are regenerated by `estate knowledge package`. A generated file is never edited.
- A red budget or manifest test is a design question. Shrink the thing, or raise the ceiling
  with a decision line in the same pull request.
- A new package is a decision line and an entry in `ci/packages.allow` in the same pull request.
- A new law is a test with an English name; `LAWS.md` picks it up on the next build. A law
  without a green test is not a law.

## What a session writes

Code and tests, without limit, under the laws and the budgets. A pull request body in the
register (below), using the engine template. At most one line in `DECISIONS.md`. `NEXT.md`,
rewritten, under forty lines. A `knowledge/` file, when what is true of the domain changed.

Nothing else: no new top-level document, no chapter, no letter, no status section, no plan,
no assessment. A new markdown file fails the build until its row in `ci/docs.manifest.json`
says who reads it and when. An edit that makes a file shorter is always allowed; it is the one
kind of documentation change that never needs a reason.

Nothing under `archive/` is current. It is provenance: cite it as archive, and re-verify
against the tree before relying on it.

## The register

Everything written here is agentless, leads with the finding, uses the true verb, puts the
evidence beneath, names exact objects, admits what was not checked, and ends with the next
action. No numbered tiers or mechanisms, no pillars, no axioms, no private nicknames; the retired words
and their replacements are listed in `knowledge/description.md`.

## When something fails

- A refusal names its code and its remedy. Do the remedy; do not work around the refusal.
- A red budget test names the ceiling and the file.
- A red law names the law. The law is right until a decision line says otherwise.
- A local-server failure: `estate synthetic-copy up`, then `estate doctor`.
- A verdict that disagrees with a finding in `knowledge/findings.md`: the verdict is a new
  finding with a receipt; append it, strike the old one, never delete it.

## The pull request

The engine template's six sections. CI posts the budget numbers and the law results as a
comment; the author says what moved and why. A human approves; no lane ever does.
```

### 6.3 `CLAUDE.md` (20 lines, new: the root has none today)

```markdown
@AGENTS.md

# Claude Code in this repository

- The SessionStart hook installs `dotnet` when it is absent and runs `estate doctor`; the SessionEnd hook
  runs `estate synthetic-copy down --if-idle`. Both are under ten lines, in `.claude/hooks/`.
- `.claude/skills/` and `.claude/agents/` are generated pointers into `knowledge/`.
  Regenerate with `estate knowledge package`; never edit them.
- `archive/` is denied to file reads and hidden from search (`.ignore`). If the task needs
  provenance, say so and ask before reading it.
- Pre-approved: the read-only verbs (`estate read|diff|classify|decide|check|doctor`),
  `dotnet build`, `dotnet test`. Always asked: `estate move`, anything naming an environment.
```

The `@AGENTS.md` line is Claude Code's import syntax; the two files cannot disagree because
one contains the other. `CLAUDE.md` is not vendored to the estate repository: the agent there
is Copilot, and a Claude-specific file would be noise a Copilot session still reads.

### 6.4 `.claude/settings.json` and the two hooks

```json
{
  "permissions": {
    "allow": [
      "Bash(estate doctor*)", "Bash(estate read*)", "Bash(estate diff*)",
      "Bash(estate classify*)", "Bash(estate decide*)", "Bash(estate check*)",
      "Bash(estate synthetic-copy up*)", "Bash(estate synthetic-copy down*)",
      "Bash(dotnet build*)", "Bash(dotnet test*)", "Bash(dotnet clean*)"
    ],
    "ask": [ "Bash(estate move*)", "Bash(estate prove*--target sql:*)" ],
    "deny": [ "Read(./archive/**)", "Edit(./archive/**)",
              "Edit(./LAWS.md)", "Edit(./DOCS.md)", "Edit(./cli/VERBS.md)",
              "Edit(./.claude/skills/**)", "Edit(./.claude/agents/**)", "Edit(./knowledge/copilot/**)" ]
  },
  "hooks": {
    "SessionStart": [ { "hooks": [ { "type": "command",
                        "command": "$CLAUDE_PROJECT_DIR/.claude/hooks/session-start.sh" } ] } ],
    "SessionEnd":   [ { "hooks": [ { "type": "command",
                        "command": "$CLAUDE_PROJECT_DIR/.claude/hooks/session-end.sh" } ] } ]
  }
}
```

The deny list is the generated-file rule as a permission: an agent cannot hand-edit what a
build regenerates. The archive denial is A6. There is no PreToolUse hook: a verb that needs
the local server refuses with the remedy, which is what v2's `docker-probe.sh` used to say.

```bash
#!/usr/bin/env bash
# .claude/hooks/session-start.sh — install dotnet when it is absent, then run estate doctor.
set -u
[ -n "${CLAUDE_CODE_REMOTE:-}" ] && ! command -v dotnet >/dev/null && { curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --jsonfile global.json --install-dir "$HOME/.dotnet" >/dev/null && export PATH="$HOME/.dotnet:$PATH"; }   # remote sessions only; a laptop is never touched
dotnet tool restore >/dev/null 2>&1 || true
dotnet estate doctor 2>/dev/null || echo "estate doctor: not built yet — run dotnet build"
```

```bash
#!/usr/bin/env bash
# .claude/hooks/session-end.sh — release the local server if nothing else holds it.
dotnet estate synthetic-copy down --if-idle >/dev/null 2>&1 || true
```

Nine lines between them. v2's four hooks were 814. The installation work the old SessionStart
did (a SDK, a tool, a daemon, an image, a container) is now `estate doctor --install` for the
first four and `estate synthetic-copy up` for the fifth, both verbs a human can run from a terminal and
both refusing with a reason when they cannot.

### 6.5 `ARCHITECTURE.md`

The successor of `V3_ARCHITECTURE.md` §5–§10 and §13, at most 3,000 lines, kept true by one
rule and two tests. The rule (§15 of the companion): edited only to make it true again when
code changed what it says, and only the paragraph that became false. The tests: every verb
named in the file exists in `estate --help --json` and every verb in `--help` is named in the
file; every law named in the file is a row in `LAWS.md`. The file carries no counts and no
line numbers; those are in `DOCS.md` and `LAWS.md`.

### 6.6 `DECISIONS.md`

```
# Decisions — one line each; the reasoning is in the pull request
2026-09-17 · v3 is C#; v2's F# is the specification it is ported from · #703
2026-09-17 · the OSSYS reader survives as an optional package, budgeted separately · #703
2026-09-17 · `estate doctor` is the thirteenth verb; every entry file runs it first · #703
2026-09-17 · `global.json` rolls forward to the latest patch (v1 and v2 disabled roll-forward) · #703
```

A test asserts every non-header line matches `^\d{4}-\d{2}-\d{2} · .+ · #\d+$`. Nothing else
is allowed in the file. When a decision is reversed, a new line says so and names the old one
by date; nothing is deleted.

### 6.7 `NEXT.md`

```
# Next — rewritten by the session that finishes something; never appended
Updated 2026-09-17 by #703.

## In flight
- <one line: what, who, the PR>

## Next
- <at most five lines, ordered>

## Waiting on a person
- <the operator decisions from ARCHITECTURE.md §16 still open, one line each>
```

Forty lines. It replaces the handoff letter with the two things the next session needs (what
is in flight; what is next) and the one thing the owner needs (what waits on them).

### 6.8 A package README (80 lines)

```
# kernel/

What it is (three sentences). What it depends on (one line: the BCL and
System.Collections.Immutable; nothing else, and a test says so). The types, one line each,
pointing at the file. The laws that live here, by name. How to test it
(`dotnet test --filter Category=fast`). What is not here and where it is (I/O: io/).
```

No status, no history, no counts. The types list is the one place a hand-written file names
files, because the reader is standing in the directory.

### 6.9 The engine pull-request template (40 lines)

```markdown
# <package>: <what changed, plainly>

## Summary
<!-- Two sentences: what changes and why. -->

## What changed
| File | Change |
|---|---|

## The law or test that pins it
<!-- The test's name. "No new behaviour; covered by <existing test>." is a complete answer. -->

## Budgets
<!-- CI posts the numbers. Say which moved and why, or: No budget moved. -->

## Decision
<!-- The DECISIONS.md line added in this pull request, or: No decision. -->

## Not checked
<!-- What this change could not verify, and who or what will. Never empty. -->
```

Six sections; the schema-change template (§7.4) has ten. Both end on "Not checked" because
the explicit negative is the section a reviewer relies on.

### 6.10 The generated surfaces

- **`LAWS.md`** — from the test tree by `ci/laws.sh` on every build: one row per test in the
  `Laws` categories, with the English name, the test's full name, and the last green run.
  A skipped test is not a row; a test named as a law that is red fails the build.
- **`cli/VERBS.md`** — from `estate --help --json`: every verb, its flags, its JSON output
  schema, its exit codes and error codes with remedies. The bundle's router and
  `authoring.md` cite verbs by name and a test checks that every cited verb exists.
- **`cli/CONFIG.md`** — from `environments.json`'s schema: every key, its type, its default, its
  meaning; the schema is the one the CLI validates against.
- **`DOCS.md`** — from the manifest: §1's table and §5's tables, regenerated so that the
  document about the documents cannot drift from the documents.
- **`knowledge/INDEX.md`** — from the tree: one row per operation and shared skill with its
  trigger phrases and its path, the one-hop route for an agent without skill discovery.
- **`.claude/skills/*`, `.claude/agents/*`, `knowledge/copilot/**`** — by
  `estate knowledge package`, fingerprinted; §8 has their shapes.
- **`archive/INDEX.md`** — once, at step 0 of the migration: one line per archived document,
  its date, its size, and its disposition (provenance / superseded by <v3 file> / knowledge
  moved to <path>).

Every generated file begins with the same one-line banner naming its generator, and the
manifest test refuses a generated file without it and a hand-written file with it.

---

## 7. The knowledge layer's files

`knowledge/` is the only prose a developer's session reads. It carries what is true of SQL
Server, DacFx, the estate and the platform, with receipts, in the register. It carries nothing
about the engine's construction and nothing about itself. The companion's §9 decided its
shape; this section drafts its files.

### 7.1 `knowledge/README.md` — the first file a developer reads (150 lines)

```markdown
# Changing the data model on this estate

The same edit ships differently depending on the data. "Make this attribute mandatory" is a
one-line NOT NULL change; whether it applies in place or is blocked and has to ship as two
releases depends on whether the table holds rows right now, not on whether the column has
blanks. You cannot tell from the SQL. You publish the change to a disposable copy of the
database, populated with real-shaped data, and read what DacFx does. What it does is the
classification. `estate prove` does this in one command.

## Two findings, always both
How it ships (in place · one release with a post-deploy · one release with a pre-deploy ·
a scripted change · across N releases) and what the approving dev lead weighs (additive ·
the application must change · existing data is modified or a relationship added · data is
removed irreversibly), with two lines that add scrutiny when they hold (production row
counts; first time on this estate). Never one label.

## Three facts the data settles
Is the table populated. Does existing data violate the new rule. Must old and new
application code coexist. `estate classify` predicts from the first two; `estate prove`
settles them.

## The map
authoring.md — the one path a change takes, S0 to S8, each step naming its verb
reviewing.md — how a review reproduces, scopes, challenges and disposes
description.md — the register and the ten sections every pull request description has
ops/ — one file per operation, in the developer's words; the trap; how it flips; how to prove it
shared/ — the reasoning several operations share (tightening; a constraint is a claim; seeds;
          identity and the refactorlog; multi-phase; when to index; vocabulary; scripts)
findings.md — what has been proven on a live SQL Server, with the database that proves it
samples/ — a dozen pull request descriptions, one per shape
ledgers/ — the estate's own state: what has shipped, row tiers, open windows, refusals,
           reviewers, toolchain pins, CDC-tracked tables, scale datapoints
handbook/ — the eight chapters the operations cite

## Read order
description.md · authoring.md · the operation that matches · the shared file it points to. That is
the whole path, and a test keeps it short enough to hold beside the open file.

## If `estate` is not on this machine
Until proving runs, stop and say so, rather than guess from the SQL text. The change can still
be authored and opened as a pull request marked provisional; the gate proves it and posts the
pull request description.

## Not covered
Sequences, triggers, views, synonyms, computed columns, collation changes, and CDC capture
instances have no operation yet. `authoring.md` says how to refuse and route them.

## Two boundaries
Publish only to a disposable copy. Never point a profile at a real environment.
The environments are Dev, QA, UAT and Prod. There is no environment called Test.

## Contributing to this tree
This tree is generated into this repository from the engine repository (the fingerprint is on
the first line of the router). To fix an operation or add a finding, open a pull request there;
a hand edit here is reverted by the next vendoring and fails the pipeline's check.
```

### 7.2 `knowledge/authoring.md` — the one path (150 lines)

The decision tree's nine states, each naming its verb and its exit condition, with intake and
authoring as phases of one session. The draft's skeleton, with the exit conditions verbatim
from the tree where they were right:

```markdown
# Authoring a change

One session, nine steps, one entry point. A compound request (several tables, several operations) is
split first: name each atom, order them (create before reference, add before drop), and run
the steps once per atom; the output is one pull request per release, not per atom.

## S0 · Intake
Hear the request in the developer's words. Name the operation (`ops/`; `INDEX.md` matches
trigger phrases). Capture the object, the operation and the intent. Ask the one business
question only a human can answer (what fills the blanks; delete or reassign the orphan);
never answer it by reading data. Exit: object + operation + intent captured; the one question
posed.

## S1 · Edit
Edit the `CREATE`; never write an `ALTER`. The desired state is the only thing in the model.
Exit: the desired-state `.sql` exists and builds.

## S2 · Measure          `estate measure --tables <t> --target synthetic-copy`
Counts and violating rows: populated or empty; nulls, orphans, duplicates, over-length.
Exit: the counts are in hand.

## S3 · Classify         `estate classify --from <base> --to HEAD --evidence …`
The provisional shape and weight, marked provisional in the first word. Exit: a provisional
pair of findings.

## S4 · Prove            `estate prove --project … --target synthetic-copy` (Strict; then `--permissive` on a block)
The verdict: applied, or blocked with the message verbatim. On a block, the permissive pass
shows what would have happened. Exit: a real verdict from this branch.

## S5 · Ship
From the verdict: one release · one release with a pre-deploy · two releases · scripted ·
refused. The forbidden shape: a single release that carries the model change and the
pre-deploy `ALTER` (it blocks and half-applies). A two-release change opens a row in
`ledgers/in-flight.md`. Exit: a terminal shape.

## S6 · Fork
If proving surfaced a decision only a human can make, pose it in the fork form (the measured
fact; each option with its consequence; one question) and write down the answer with its owner.
Exit: posed and recorded, or answered.

## S7 · Describe         `estate describe …`
The ten sections. A section with nothing to report says so in a sentence. Exit: all ten
present; the verdict and the shipping shape agree.

## S8 · Verify
Read the description against `description.md`. Every sentence denotes; every claim has its receipt;
"Not checked" is not empty and names the Integration Studio refresh per environment as the
step that completes the change. Exit: ready for the gate.

## Refuse and route
Sequences, triggers, views, synonyms, computed columns, collation, CDC capture instances:
say so, name who does it, name what to check. Never guess.
```

### 7.3 `knowledge/reviewing.md` (100 lines)

Reproduce (`estate prove` on the reviewer's own copy), scope (`estate diff --only` and the
dependency closure: foreign keys in and out, procedures, indexes, external consumers, with
row counts), challenge (inject a violating row; play a blocked change forward under
`--permissive`; the seven challenges by operation class), dispose (Approved · Approved with a
named risk · Returned to the author · Escalated with one question). The gate (companion
§10.3) does the reviewer's reproduction for every pull request; this file is for the lead's
own review and for a change the gate could not fully reproduce. The invariant it carries:
no disposition exceeds the scope established; an unscoped cascade cannot be approved.

### 7.4 `knowledge/description.md` — the register and the template on one page (120 lines)

```markdown
# The pull request description

## The register (nine rules)
1 Agentless: no I, we, you. 2 Finding on top, proof beneath. 3 The true verb: drops, deletes,
narrows, blocks, refuses. 4 Findings asserted; interpretation hedged and labelled. 5 Every
claim grounded: the copy it was verified on, the count, the message number, inline. 6 Exact
objects: `dbo.[Order]`, `CustomerId`, the constraint by name. 7 The unverified admitted:
application behaviour, other environments, production scale, in "Not checked". 8 End with
the next action, as a bare imperative aimed at the object. 9 No numbered axes, tool jargon,
or private nicknames (the list is at the end).

## The ten sections
<!-- template:begin -->
# <object>: <plain change> (<one-clause consequence, if any>)
## Verdict           — one line: how it ships, and what the lead weighs
## Intent            — the developer's words, quoted
## What changes      — <object>: <from> → <to>, one line per change; then the explicit negative
## Before promoting  — one imperative per environment
## The data          — counts and offending rows; for a reconcile: rows before, rows after, who approved
## How it ships      — the shape and the mechanics the developer must know
## What proving showed — tried / did / realized, with the message verbatim and the copy's name
## After deploy — check — queries with `-- expect <result>`; the Integration Studio refresh, per environment
## How to roll this back — the reverse steps; what is not auto-reversible, with the recorded originals
## Not checked / still open — never empty
<!-- template:end -->

## Two rules that carry the most weight
The explicit negative is a finding ("No data is remediated." is a complete section). Every
"what proving showed" entry names the disposable copy and the script hash the verdict
carried; a claim with no receipt is an opinion.

## Retired words
The tree's private vocabulary and its plain replacement, one table (Appendix A of this
document's source). A description that uses a retired word fails the register test.
```

The pull-request templates for GitHub and Azure DevOps are generated from the block between
the `template` markers, so the three cannot disagree. `estate describe` renders seven of the ten
sections from the change, the evidence and the verdict, and writes an explicit placeholder into
the three only a person can fill (the intent in the developer's words, the fork's answer with
its owner, and what was not checked beyond what the run could see); the gate refuses a body that
still contains a placeholder, as the tree's ledger gate already does for refusal blocks. A
generated skeleton good enough to ship unedited is how "approved by reading" degrades into
"approved as a form"; the placeholder prevents it.

### 7.5 An operation file (90 lines)

The op skills keep their trigger phrases (what Copilot's skill discovery matches), their flip
conditions, their named trap, their "prove it" steps as verbs, and their verdict paragraph.
They lose the restated proving-loop mechanics, the restated register, the per-op release
boilerplate, and the relative-path citation thickets. The template:

```markdown
---
name: make-mandatory
description: Use when the developer says "make Email required", "tick the Mandatory checkbox",
  "this attribute must be filled" — an existing column NULL → NOT NULL.
---
# Make mandatory (NULL → NOT NULL)

## In the developer's words          — the phrasings; what they mean in SSDT (one line)
## The named trap                     — `BlockOnPossibleDataLoss` fires on table-has-rows, not on column-has-blanks (F7)
## How it flips                       — empty table: in place · populated: two releases, even after the blanks are filled
## Prove it                           — `estate measure --tables …` · `estate prove …` · on a block, `--permissive`
## The verdict, to the developer      — the paragraph, in the description register
## In the description                 — which sections this op fills specially; the sample: samples/make-mandatory.md
## Shared reasoning                   — shared/tightening-class.md
```

Seven headings, one screen. The reasoning lives in `shared/`; the op says only what is
specific to it.

### 7.6 A shared file (120 lines)

`shared/tightening-class.md`, `constraint-is-a-claim.md`, `idempotent-seed.md`,
`identity-and-refactorlog.md`, `multi-phase.md`, `when-to-index.md`, plus `vocabulary.md`
(the OutSystems ↔ SQL Server noun and gesture maps, from `os-vocabulary`) and
`deploy-scripts.md` (where a script goes, what permanence class it is, how it proves
idempotent, when it retires). Each: the members of the class; the mechanism, with the number
of the finding that proves it on DacFx; the ladder (empty · populated · violating); how the per-op
specifics differ; what it is *not* (tightening-class is data-blind on row presence;
constraint-is-a-claim blocks on a violating value; keep them apart).

### 7.7 `knowledge/findings.md`

```markdown
## F7 · 2026-08-21 · `BlockOnPossibleDataLoss` fires on row presence, not on rule violation
A `NULL → NOT NULL` on a populated table blocks even after every NULL is backfilled.
Receipt: `mm_ax`, sqlpackage 170.4.83, Strict. Cited by: ops/make-mandatory, shared/tightening-class.

## ~~F5 · 2026-08-19 · A declarative FK add lands untrusted~~
Overturned by F9 (2026-08-22): DacFx emits WITH NOCHECK ADD then WITH CHECK CHECK, and a clean
add ends trusted on 170.x; on 162.5.57 it read untrusted. Receipt: `fk_trust_2`.
```

Append-only; a test asserts no identifier disappears; an overturned finding is struck and
kept. Every finding names its DacFx version, because the DacFx version decided F5.

### 7.8 `knowledge/samples/` — twelve shapes

`add-optional` (the safest change) · `make-mandatory` (two releases) · `narrow` (the
truncation fork) · `create-fk-orphan` (reconcile, then trusted) · `add-unique` (duplicates
and the filtered-index remedy) · `delete-entity` (the irreversible weigh-line) ·
`rename-attribute` (the refactorlog) · `create-static-seed` (the guarded MERGE) ·
`split-table` phases 1, 2 and 3 · `compound-release` (an entity, a seed, two keys and a
defaulted column as one change). Each is a pull request description, in the register, proven on the golden
project, and the proof lane re-proves it nightly; a sample that no longer matches its verdict
is a red lane, not a stale document.

### 7.9 The ledgers

Eight append-only tables with fixed columns, format-tested. Seven are v2's, unchanged:
`operations.md` (date · op · object · PR · proof · notes; opens empty and stays empty until
the first change ships through the pipeline), `row-tiers.md` (table · tier · measured · source;
per environment), `scale-datapoints.md`, `in-flight.md` (id · change · tables · phase · of ·
next action · window closes · PR; the gate reads `tables` and `window closes`), `refusals.md`,
`reviewers.md` (two rows unfilled today), `toolchain.md` (tool · pinned · source of truth ·
recorded · notes). One is new:

```markdown
# CDC-tracked tables — filled by `estate check cdc --target <env>`, per environment
| environment | table | capture instance | columns captured | measured |
|---|---|---|---|---|
```

`classify` reads it; a table listed here turns a column-list change into an `Error` finding
with the capture-instance step named. The ledgers are owned by the estate repository (§5.3):
the engine vendors them empty once and never overwrites them.

### 7.10 `knowledge/handbook/` — the eight chapters the operations cite

State-based versus migrations · pre- and post-deployment scripts · idempotency · referential
integrity · the refactorlog · deployment safety and `BlockOnPossibleDataLoss` · multi-phase changes · Change
Data Capture. Lifted from v1's `handbook/` (32 chapters, 8,980 lines) and trimmed to what an
operation cites; the other twenty-four chapters archive. Chapters are cited by title, never by
number: v2's skills cite "handbook 16 (= §19)" through a +3 offset in twenty-five places, and
the playbook they also cite is not vendored, so today a Copilot session in the estate cannot
resolve a single handbook citation. The eight chapters are vendored with the tree (§8) and
the citation test (§10) resolves them there. `PORTABILITY.md` (94 lines) moves
here unchanged. `INDEX.md` is generated.

---

## 8. The estate repository's instruction set

Under the two-repository default (the companion's §16, item 9): the team's Azure DevOps
repository holds the SSDT project and receives a generated bundle. If the operator chooses one
repository, the bundle becomes a build output, the ownership table in §8.7 collapses to one
column, and nothing else in this section changes. The
bundle is v2's `copilot-package/` with two personas fewer, verbs instead of shell recipes, and
one more path-scoped instruction. Its shape is kept because it matches what ships in Visual
Studio; its rungs are kept because they state what is verified.

### 8.1 The router — `.github/copilot-instructions.md` (60 lines, generated)

```markdown
<!-- generated by `estate knowledge package` · fingerprint <12 hex> · do not edit -->
# Database schema changes in this repository

This repository's schema is managed with SSDT. A change is an edit to a table definition,
built into a dacpac, deployed through Azure DevOps into Octopus with `BlockOnPossibleDataLoss` on.
The check cannot be relaxed for a single deploy.

## When to use this
A request to change the data model ("make Email required", "add a reference to Customer",
"rename this attribute", "drop that table") is an authoring task: follow `knowledge/authoring.md`.
If this editor offers custom agents, `authoring` is that file; on Visual Studio 2022,
`#prompt:schema-change` is the entry prompt. A request to review a schema pull request is a review
task: `knowledge/reviewing.md`, or the `reviewing` agent.

## The one rule
You cannot tell how a schema change behaves by reading its SQL. Run `estate prove` against
the disposable copy and read the verdict; the verdict is the classification. If proving
cannot run, stop and say so. A guess from the text is the failure this workflow exists to
prevent.

## Loading knowledge
Open `knowledge/INDEX.md`, match the developer's words to an operation, read that file and the
shared file it points to. Follow it; do not re-derive it.

## Writing
Every pull request description, review and disposition follows `knowledge/description.md`.
Teach the developer in conversation; never in the description.

## Two boundaries
Publish only to a disposable copy; never point a profile at a real environment.
Environments are Dev, QA, UAT and Prod. There is no environment called Test.
```

### 8.2 The path-scoped instructions (five files, ≤ 25 lines each, generated)

| File | `applyTo` | The never-rules it carries |
|---|---|---|
| `schema.instructions.md` | `Modules/**/*.sql` (the bundle's own layout; v2's `**/*.sql` fired on every SQL file in the repository, including v1's rowset script, and attached two files at once to a post-deploy script because the script globs were subsets of it) | edit the CREATE, never write an ALTER; a NOT NULL on a populated table blocks even when the blanks are filled (`shared/tightening-class.md`); a rename by typing loses the column's data: use the refactoring, or run `estate emit --refactorlog` |
| `pre-deploy.instructions.md` | `Script.PreDeployment.sql` and `Scripts/PreDeploy/**/*.sql` | a pre-deploy prepares data for a change the model does not yet declare; never put the model change and the pre-deploy ALTER in one release; a data-modifying pre-deploy carries its receipt in the pull request description |
| `post-deploy.instructions.md` | `Script.PostDeployment.sql` and `Data/Seeds/**/*.sql` | seeds are guarded MERGEs with explicit ids; deactivate, never delete; silence on redeploy is the proof |
| `publish-profile.instructions.md` | `**/*.publish.xml` | a profile points only at a disposable copy; `BlockOnPossibleDataLoss` stays on; the two profiles under `estate/profiles/` are the only ones the gate accepts |
| `refactorlog.instructions.md` | `**/*.refactorlog` | new: never remove an entry before every environment has deployed past it; a removed entry re-introduces the drop-and-add |

The fifth file exists because the refactorlog is the highest-leverage artifact after the
eject and had no instruction attached to it.

### 8.3 The agents and the prompt

```markdown
---
name: authoring
description: Author a schema change on this estate. Use for any request to change the data
  model in the developer's words. Follows knowledge/authoring.md: intake, edit, profile,
  classify, prove, ship, fork, describe, verify, with `estate` verbs.
---
Read `knowledge/authoring.md` now and follow it. The operation files are under `knowledge/ops/`;
the description register is `knowledge/description.md`.
```

`reviewing.agent.md` is the same shape over `knowledge/reviewing.md`. Two caveats travel
with the agent files: the custom-agent file format was flagged in the tree as unverified before
the bundle was built and the flag was never cleared, and the agents ship with their tools unset
because Visual Studio's tool names vary by build, so there is no permission mechanism on the
Copilot side (the read-only allowlist in §9.5 exists for Claude Code only). The one prompt,
`schema-change.prompt.md`, is the entry point for editors without agents: its first instruction is the
compound check with an explicit terminal (if more than one operation is needed, stop, produce the
ordered list of pull requests with the reason for the order, and say that the prompt runs once per
pull request), then the router's "one rule" paragraph, then "Follow `knowledge/authoring.md` from
S0." On the 2022 rung the prompt is the only entry point, which is the rung where a compound request most
easily becomes one over-stuffed pull request; the gate backs the prompt by refusing a change with
data-loss steps on more than one table unless `ledgers/in-flight.md` names the program. The four
per-role prompts v2 shipped are gone with the personas.

### 8.4 Skills, index, PR templates, `AGENTS.md`

`.github/skills/<op>/SKILL.md` are the eight-line pointers v2 generates today, over
`knowledge/ops/<op>.md` and `knowledge/shared/<name>.md`. `knowledge/INDEX.md` is the one-hop
table for editors without discovery. The two pull-request templates (GitHub and Azure DevOps)
are generated from `description.md`'s template block. `AGENTS.md` at the estate root is the router's
text under the tool-agnostic name, for any agent that is not Copilot (a Claude Code session
opened on the estate repository reads it and finds the same one rule and the same entry point).

### 8.5 The packager contract

`estate knowledge package` has three modes. `--check` verifies that every generated file in
the current tree equals what the generator would write (the fingerprint over the generator
and its sources is the version). `--apply` regenerates in place (this repository's `.claude/`
and `knowledge/copilot/`). `--vendor <path>` writes the bundle and the vendored `knowledge/`
into an estate checkout, seeding `estate/ledgers/` only if absent and never touching an
estate-owned file (§5.3); the ledgers are excluded from the fingerprint, so an appended row is
never a drift failure. `estate --version` prints the fingerprint, and the router's first line
carries it, so a developer can see in one glance whether the bundle is current.

The lane: on every merge to `main` here, the bake lane runs `--vendor` into a clone of the
estate repository and opens a pull request there when the fingerprint moved, titled with the
fingerprint and listing the changed files. Today's vendor step copies `estate/` unconditionally,
which is harmless only because the ledgers have no rows; the first appended row would be erased
by the next vendoring, so the seed-once rule is a bug fix. The estate's own pipeline runs `--check` on every
pull request and fails with "regenerate" if a generated file was edited by hand, and it
resolves every citation in the vendored tree against the estate checkout, which nothing does
today (the engine's citation gate deliberately skips the bundle because its paths only resolve
after vendoring). Nobody
hand-edits a vendored file; a drifted copy is a failed check. Two Azure DevOps
facts shape the check: a YAML `pr:` trigger does not apply to Azure Repos, so the check runs as a
branch policy a person registers in the portal, and adoption is not complete until it is; and the
prototype of v3's gate (`ssdt-agent-pr-validation.yml`, which restores the bake artifact and
publishes the pull request's combined change on a hosted Windows agent with LocalDB and no Docker)
has never run: its five placeholders are unfilled and its trigger is `none`.

### 8.6 The rungs

Kept from `ADOPTION.md`, which states what is verified: Visual Studio 2026 18.5 and later discovers
skills and agents and attaches the path-scoped instructions; 18.4 loads agents and the index;
2022 (17.14) loads the router, the instructions and the prompt; with agent mode off, the
router and the instructions remain and the tree is read by hand. The bundle's own list of what
is unverified (whether the team's build discovers `.github/skills/`, whether `#prompt:`
attaches on 2022, whether agent mode reads vendored files when the solution is not at the root)
is unchanged and is the pilot's checklist (companion §16, item 2). Nothing in this section
adds a surface until the pilot says which rung the team is on.

### 8.7 What the estate owns, and the engine never writes

`estate/environments.json` (the environments; the writable targets; the local server), the project
format row in `ledgers/toolchain.md` (classic today; `emit` never changes a project's format),
`estate/profiles/*.publish.xml` (mirrored from the pipeline's task by a human), the ledgers,
`estate/evidence.shape.json` and `estate/synthetic-copy.json` (produced by verbs the team runs, committed
by the team), `.editorconfig` and `.gitattributes` (D3), and the SSDT project itself. The
engine reads them; only a verb that appends a row (`check cdc`, `measure --counts`) writes to
them, and it says so in its output.

---

## 9. The agentic interface

The tool an agent calls is the product, and the product is the CLI. Everything in this section
is a consequence of that: the JSON contract, the status line, the optional MCP server, the two
hooks, the permissions, and the session protocols are all views of `estate`.

### 9.1 The CLI is the API

Every verb accepts `--json` and writes one object. The object's shape is the contract:

```json
{ "schema":     "estate.prove/1",
  "estate":     "3.0.12", "dacfx": "162.5.57", "pin": "162.5.57",
  "server":     { "version": "16.0.4135", "compatibilityLevel": 160, "image": "sha256:…" },
  "provenance": { "target": "estate_9f3a1c_41872", "script": "sha256:…", "at": "2026-09-17T14:02:11Z" },
  "verdict":    { "outcome": "blocked", "shape": "two-releases", "message": "Msg 50000 … rows exist …" },
  "findings":   [ { "code": "block-on-possible-data-loss.row-presence", "severity": "error",
                    "subject": "dbo.Customer.Email", "message": "…", "remedy": "…" } ], "exit": 3 }
```

**Versioned:** `schema` names the verb and a major; within a major, fields are only added; a
removal or a rename is a new major and a decision line. **Frozen exit codes:** 0 done · 1 bad
arguments · 2 an input could not be parsed · 3 blocked by `BlockOnPossibleDataLoss` · 4 target
unreachable · 5 divergence found · 6 configuration refused (an unknown key, an inline
credential, a toolchain pin mismatch) · 7 build failed · 9 refused by name (companion §8);
`cli/VERBS.md` lists them and a test asserts the list never shrinks. **Every refusal carries a
remedy:** the `findings[].remedy` field is required for `severity: error` and for exit codes 2,
4, 6 and 9, and the remedy is a verb or a file path, never a paragraph. A checked-in JSON schema
per verb under `cli/schemas/` is what the test checks against, and `--help --json` emits the
same schemas, so the verb reference is generated from the thing the tests check.

### 9.2 `estate doctor` — the status line

One verb answers "can this machine do the work?" and every entry file says to run it first.

```
estate doctor READY | sdk=10.0.4 | dacfx=162.5.57 (pinned) | local-server=localdb (2022) |
  synthetic-copy=9f3a1c (restored 2026-09-16, current) | estate=../estate (environments ok, 2 open windows)
```

`DEGRADED` names what is missing and the remedy (`--install` for the SDK and the local tool;
`estate synthetic-copy up` for the local server; `estate synthetic-copy restore <artifact>`
for a stale synthetic copy). `--json` carries the same fields. The SessionStart hook prints
this line and nothing else; a session quotes it before diagnosing anything (A2). It replaces
431 lines of hook with a verb a human can run.

### 9.3 An MCP server, as an open question

`estate serve --mcp` would expose the verbs as tools over stdio, generated from the same verb
table that produces `--help --json` (one tool per verb, `move` excluded), so that the verdict
becomes a tool call rather than a terminal command. It is about two hundred lines and adds no
surface to `knowledge/`. It is not in the design until the pilot answers one question: whether
the team's Visual Studio, with agent mode gated by the Copilot Business policy `ADOPTION.md`
names, can host an MCP server against an Azure DevOps checkout. Until then the CLI with
`--json` and the frozen exit codes is the tool surface on every rung, including agent mode off.
§12 carries the question.

### 9.4 The hooks

A maintainer surface only: the team does not use Claude Code, so the hooks belong to the engine
repository and are no part of the estate's interface. Two, drafted in §6.4, nine lines between
them. SessionStart installs `dotnet` when it is absent and runs `doctor`. SessionEnd releases
the local server if idle. There is no PreToolUse hook (a verb that needs the local server
refuses with the remedy), no Stop hook (nothing runs after every message), and no hook installs
a daemon or pulls an image (`estate synthetic-copy up` does, and refuses with a reason when it
cannot). A hook that grows past ten lines is a verb that has not been written.

### 9.5 Permissions

For Claude Code in the engine repository; there is no equivalent on the Copilot side, where the
agents' tools are unset by decision (§8.3). The read-only verbs, the build and the tests are
pre-approved. `move`, and any `prove` whose target names an environment rather than the
synthetic copy, always ask. Generated files and the archive are denied to edits and, for the
archive, to reads. The permission file is the mechanism for A6 and for the generated-file rule;
a session that needs the archive says so and is granted it for that task.

### 9.6 Session protocols

**(a) An engineering session in this repository** (Claude Code, a cloud session, a Copilot
session in agent mode). The hook prints the doctor line. Read `AGENTS.md`, `NEXT.md`, and the
README of the package being changed. Work under the laws and the budgets. Before pushing:
`dotnet test --filter Category=fast`; `estate knowledge package --check` if `knowledge/`
changed. Open a pull request with the engine template; CI posts the budget and law numbers.
Rewrite `NEXT.md`. Add at most one line to `DECISIONS.md`. If the work needs a decision only
the operator can make, write it under "Waiting on a person" in `NEXT.md` and stop; do not guess.

**(b) An authoring session in the estate repository** (Copilot in Visual Studio, or any
agent reading `AGENTS.md` there). The router attaches. `authoring.md` S0–S8, each step its
verb. The pull request description is `estate describe`'s output; the agent supplies the
intent, the fork answer and nothing else in its own words. The pull request uses the schema
template. The gate reproduces the proof and diffs the description; the developer refreshes
Integration Studio per environment after each deploy, and the description's after-deploy
section says so.

**(c) A review session.** `reviewing.md`. The gate's `gate.json` is the first evidence; the
reviewer reproduces only when trust is in question, challenges the change in the two ways
`reviewing.md` names, and renders one of four dispositions in the register. A returned change
goes to the author; an escalated one carries one question and the dependency scope to the lead.

**(d) A CI lane.** Three verbs at most. Proof (nightly): `synthetic-copy restore` → `prove` over the
proven changes → publish the results `LAWS.md` reads. Bake (merge to `main`): `profile` (when
the source is real Dev, owner-side) → `synthetic-copy bake` → `knowledge package --vendor` → a pull
request to the estate. Gate (a pull request on the estate): `gate` → post `gate.json` and
`changelog.json`. A lane never approves.

**(e) An authoring session with no `estate` binary.** The estate's laptops are corporate
Windows machines that may lack Docker and will lack the tool until it is installed. The
distribution is a pinned .NET tool (`dotnet tool install --global Estate.Cli --version <pinned>`
from the feed named in `ledgers/toolchain.md`), and the router says what to do without it, in
the bundle's own words: stop and say so, rather than guess from the SQL text. The degraded path
says what it could not do: the session edits the CREATE, writes the intent, and opens the pull
request marked provisional; the gate proves the change and posts the pull request description;
the reviewer reads the gate's description. Nothing is ever classified from the text.

**(f) Reading the archive on purpose.** `archive/INDEX.md` names the file; `git show
v2-final:<path>` reads it without lifting the denial; a session that must read many archived
files asks for the denial to be lifted for that task and says why in the pull request.

### 9.7 One register, two agents

A Claude Code session and a Copilot session produce the same pull request description because
neither writes it: `estate describe` renders it from the verdict, the change and the evidence,
and the agent fills two fields (the intent in the developer's words; the fork answer with its
owner). The register is therefore the verb's property, tested once, and the agents' styles
cannot leak into the artifact a lead approves. The descriptions that ship are written in the
estate repository and would otherwise never meet the lint, so the gate runs the register lint
over the pull request body there, and refuses a body that still carries a placeholder from
`estate describe`. In conversation, the agent is allowed the second person and warmth (v2's
conversation register, kept as one paragraph in `description.md`); the description is not.

---

## 10. How it stays true

Eighteen tests, sixteen in one fast project here and two in the estate's pipeline, `tests/Budgets.Tests`, run on every pull request with no
SQL Server, plus three lanes. Each is named so a red build names the rule.

| # | Test | What it asserts | Values it upholds |
|---|---|---|---|
| 1 | `Manifest` | every `.md` under the tree (excluding `archive/`) is a row in `ci/docs.manifest.json` (tree ⊆ manifest everywhere; both directions for `knowledge/` and the top-level documents); every row has `reader`, `moment`, `kind`, `owner`; hand-written rows are under `budget`; generated rows carry the banner and hand-written rows do not. The failure message is the fix: `docs.manifest.json: add {path, reader, moment, budget, owner} for knowledge/ops/add-check.md` | L3, L4, A4, A10 |
| 2 | `Budgets` | `ci/budgets.json`'s ceilings hold: lines per code package (`wc -l`), tests, docs outside `knowledge/`, `knowledge/`, each hook (≤ 10), the router (≤ 60), `knowledge/README.md` (≤ 150), every operation (≤ 90), and the authoring path sum (≤ 600) | L3, L11, O3 |
| 3 | `Register.Samples` | every file under `knowledge/samples/` passes the register: no first or second person, the ten sections present and in order, a finding sentence under each heading, "Not checked" non-empty, a receipt in "what proving showed" | L1, S3, R2, A3 |
| 4 | `Register.Refusals` | every `Error` and `Finding` message in the CLI passes the register and carries a remedy where required | L1, O4 |
| 5 | `Register.Prose` | every hand-written file in the manifest is free of the retired vocabulary, the antithesis tic and numbered axes; `VALUES.md` first, because a register of one-sentence values is the document most likely to fail the register's own rules | L1, L5 |
| 6 | `Vocabulary` | every term in the glossary appears somewhere in the tree; every retired term (Appendix A) appears nowhere outside `archive/` and Appendix A itself | L5 |
| 7 | `NoRestatedCounts` | no hand-written file states the number of verbs, laws, operations, samples, packages, hooks or files in the tree, or a line count of a tree file (dated historical measurements are allowed) | L2 |
| 8 | `Citations` | every relative link and every `knowledge/…` path in any markdown resolves; every `estate <verb>` cited in `knowledge/` or `ARCHITECTURE.md` exists in `--help --json`; every `F<n>` cited exists in `findings.md` | L2, R2 |
| 9 | `VerbsMatchArchitecture` | the verbs in `ARCHITECTURE.md` and in `--help --json` are the same set, and the generated reference's row count equals the number of verb handlers, so no prose carries a stale count | L2 |
| 10 | `LawsMatchArchitecture` | every law named in `ARCHITECTURE.md` and `VALUES.md` is a row in `LAWS.md`; `LAWS.md` is byte-equal to `ci/laws.sh`'s output | L6 |
| 11 | `PackagerCheck` | `estate knowledge package --check` is clean: pointers, index, bundle and the two schema templates equal the generator's output | principle 5 |
| 12 | `NoSkips` | zero `Skip =` attributes and zero `[Fact(Skip` in `tests/` | L6 |
| 13 | `PackagesAllowlist` | every `PackageReference` across the solution is in `ci/packages.allow` with a licence; `packages.lock.json` is present and CI restores in locked mode | X3 |
| 14 | `Decisions` | every non-header line of `DECISIONS.md` matches the one-line format; no line present on `main` is absent on the branch | A8 |
| 15 | `FindingsAppendOnly` | no `F<n>` heading present on `main` is absent on the branch; every finding names its DacFx version and its receipt | R4, A7 |
| 18 | `DescriptionInPullRequest` (runs in the estate's pipeline, in the gate) | the pull request body passes the register lint and contains no placeholder `estate describe` wrote for a human section | L1, S3, A9 |
| 17 | `VendoredCitations` (runs in the estate's pipeline, not here) | every relative link and bare `.md` name in the vendored `knowledge/` and the bundle resolves against the estate checkout; the engine's citation test cannot see this because the bundle's paths only resolve after vendoring | L2, principle 3 |
| 16 | `ValuesResolve` | every row of `VALUES.md` has a `Where` that names an existing test, verb, analyzer rule, hook or generated file, or the literal "prose only"; the count of "prose only" rows is printed and may not rise without a decision line | principle 8 |

One exclusion is itself tested: `archive/**` is excluded from every count, every lint and the
manifest, and a test asserts the exclusion exists, because on the day the archive lands the docs
budget would otherwise be violated by construction.

The six lanes that exist today map onto three: the three required checks with no timeout
(analyzers, lint, verifiability) and the tree's gates become the fast lane (`Budgets.Tests` and
the analyzers, five minutes, every pull request); the proof lane stays the proof lane (nightly);
the bake lane stays the bake lane (every merge) and gains the vendoring pull request. The PR gate
is new and runs in the estate's pipeline as a branch policy.

Three lanes carry what a fast test cannot: the proof lane re-proves the samples nightly on the
pinned DacFx (a sample whose verdict changed is a red lane); the bake lane regenerates the
bundle and opens the estate pull request; the gate reproduces a schema change's proof and diffs
its pull request description. And one rule with no test, stated as such: `ARCHITECTURE.md` is
edited only to make it true again, one paragraph at a time. Tests 9 and 10 catch the two ways
it most often went false in v2 (a verb list and a law list); the rest is review.

What this replaces, by name: `ssdt-agent-gates.mjs` (370 lines; its five gates are tests 3,
8, 11 and the ledger checks inside `check inflight`), `lint-discipline.sh` (609 lines; its
surviving rules are analyzers), `verifiability-gate.sh` and `matrix-status.sh` (the law list
is generated from tests, not from tags), the perf-gate hook, and `CLAUDE.md` §8's unenforced
rule about restated counts.

---

## 11. Migration of the documents

Aligned to the companion's nine steps (§14.2). Documents move at three of them.

| Step | What moves | From |
|---|---|---|
| 0 | `README.md`, `AGENTS.md`, `CLAUDE.md`, `VALUES.md`, `DECISIONS.md` (opened with the lines in §6.6), `NEXT.md`, `.claude/settings.json` and the two hooks, `.ignore`, `ci/docs.manifest.json`, `ci/budgets.json`, `ci/packages.allow`, `tests/Budgets.Tests`; `archive/INDEX.md` generated once over v1's and v2's documents, and v2's 179 documents archived as one compressed bundle beside it so that search tools stop finding a specification v3 replaced while the code stays buildable in the tree; `V3_ARCHITECTURE.md` and this document stay at the root until `ARCHITECTURE.md` supersedes them at step 9 | root `AGENTS.md` and `architecture-guardrails.md`; `sidecar/projection/CLAUDE.md` §4–§5 and §8; `KICKOFF.md`'s read order; `settings.json` and the four hooks |
| 1 | `LAWS.md` generation with the first kernel laws; `kernel/README.md` | `AXIOMS.md`'s English names; `scripts/matrix-status.sh`'s shape |
| 2–5 | a package README as each package lands; `cli/VERBS.md` and `cli/CONFIG.md` generation when the CLI has verbs; `io/SyntheticCopy/README.md` moved unchanged | `THE_TWIN.md`, `THE_SYNTHETIC_DATA_DESIGN.md`, `CONFIG_REFERENCE.md` |
| 6 | `knowledge/`: `README.md`, `authoring.md`, `reviewing.md`, `description.md`, the 45 operations trimmed, the eight shared files, `findings.md`, twelve samples, the ledgers (with `cdc-tracked.md`), the eight handbook chapters, `PORTABILITY.md`; `INDEX.md` and the pointers generated; the bundle regenerated with two agents, one prompt and five instructions; the first vendoring pull request to the estate | the `ssdt-agent` tree; v1's `handbook/` |
| 7 | the two schema pull-request templates generated from `description.md`; the engine template; the gate's `gate.json` and `changelog.json` posted on pull requests | `pr-template/`, `author-pr` |
| 9 | `ARCHITECTURE.md` written from the companion's §5–§10 and §13; `DOCS.md` generated; `archive/` leaves `main` for the `archive` branch and the `v2-final` tag once its CI is retired; the two design documents move to `archive/design/` | `V3_ARCHITECTURE.md`, this document |

Everything not named above archives at step 0 with its index line: v2's 179 root documents
(the chapters, the handoffs, the vision set, the audits, the cutover set, the principles, the
spine), the `ssdt-agent` program documents, the self-test, the sample gallery beyond the
twelve, and v1's 77,959 lines of prose: `readme.md` (its five-minute quickstart is the ancestor
of the README's), `AGENTS.md` and the five files it tells a session to consult (§5.1),
`docs/`, `notes/`, `ssdt-playbook/` and the twenty-four handbook chapters no operation cites. The index line for each says which v3 file, if any, carries its
knowledge; the test that no retired term appears outside the archive is what keeps the archive
from leaking back.

**Citations gate the migration.** The tree's skills and agents cite what step 6 deletes or
renames, in this many files of the sixty-eight: `sample-prs/` 53, `FINDINGS_AND_CHANGES.md` 21,
`handbook` 20 (25 occurrences, through the +3 offset), `estate/` 11, `CONNECTORS.md` 10,
`proving-ground/` 9, `self-test/` 8, `PROVING_PATH_WINDOWS.md` 2. The bundle's install notes say
to keep all ten targets because the skills cite them; five are deleted here. So the order inside
step 6 is fixed: rewrite the citations first (findings by identifier, samples by shape, handbook
by title, ledgers by their new path), with the citation test green at every commit, and delete
the targets last.

`KICKOFF.md`'s first-read role goes to `AGENTS.md`; `GETTING_STARTED.md`'s stand-up role goes
to `tests/README.md` and `estate doctor --install`; `PROVING_PATH_WINDOWS.md`'s Windows runbook
goes to `knowledge/PORTABILITY.md` and the same verb, with the `.bak` route it described gone
(it put real data on laptops).

The migration does not rewrite v2's documents into the register; they are provenance and are
archived as written. It does not merge `DECISIONS.md`'s 480 entries into the one-line log; the
log opens at step 0 with v3's decisions, and the archive index points at the old file for
anything earlier.

---

## 12. Risks and open questions for this layer

- **The tool has to reach the laptop.** Everything in `authoring.md` assumes `estate` is on
  the developer's PATH, and the estate's laptops are corporate Windows machines that may lack
  Docker and will lack the tool until someone installs it. §9.6(e) gives the distribution (a
  pinned .NET tool from a named feed) and the degraded path (author, mark provisional, let the
  gate prove). The risk is the feed: a corporate machine that cannot reach nuget.org needs an
  internal feed, and standing one up is a decision for the operator alongside the pilot. Until
  the tool is on one champion laptop, the bundle's own rule stands: stop and say so, rather
  than guess from the SQL text.
- **The rung is unverified.** Everything in §8 above the router and the instructions depends on
  which Visual Studio build the team runs and what it discovers. The bundle's own `ADOPTION.md`
  lists the unverified items and this document adds none. The pilot on one champion laptop
  (companion §16, item 2) decides; until then the router, the five instructions and the prompt
  are the surface, and they work on every rung.
- **MCP on the team's build.** §9.3 is an open question: whether Visual Studio's
  agent mode, policy-gated on Copilot Business, can host an MCP server against an Azure DevOps
  checkout. If not, the verbs are the same verbs on the terminal and the loss is convenience,
  not capability. Cost if yes: about two hundred lines generated from the verb table.
- **Two repositories drift.** A vendored file edited by hand in the estate repository is
  caught only if the estate's pipeline runs `--check`, and registering that pipeline is a human
  act in Azure DevOps. Until it is registered, the bake lane's pull request is the only
  reconciliation, and a hand edit survives until the next one. The mitigation is stated in
  `knowledge/README.md`'s first vendored line ("generated; edit in the engine repository") and
  in the pipeline template's README.
- **The ledgers are owned by the estate and seeded by the engine.** A `--vendor` that
  overwrote a ledger would erase the estate's history. The packager never writes an existing
  ledger, and a test proves it against a checkout with populated ledgers.
- **The manifest test as friction.** A session that wants to write prose will find it cannot
  add a file without a manifest row. That is the point, and the exceptions are the
  intended ones: the pull request body (unbounded), `knowledge/` (when the domain changed),
  `NEXT.md` (forty lines). The cheapest way past any such test is to put the prose where the
  test does not look (a `docs/` folder, a comment block at the top of a skill, a wiki), so the
  test's scope is the whole tree minus a short ignore set that is itself in the manifest, and
  the one rule that rewards subtraction (an edit that makes a file shorter is always allowed) is
  stated in `AGENTS.md`, because a budget with no rewarded subtraction is a budget people route
  around. One unintended exception is `findings.md`, which is append-only and
  unbounded; test 15 requires every finding to carry a receipt, and a finding that no
  operation or shared file cites for two releases is a candidate for the archive by decision
  line. Another is code comments; the register test does not read them, and that is accepted.
- **One hop can be one hop too few.** An operation file points to one shared file; a change
  that straddles two classes (a rename and a tightening in one release) needs both. The
  compound sample and `authoring.md`'s first paragraph exist for this; the gate proves the
  release regardless of what the agent read.
- **The register reads as curt to a person.** Agentless, finding-first prose is what a reviewer
  needs and not what a developer wants to hear. v2 solved this with two registers (the pull
  request description; the conversation) and v3 keeps both: the description is the verb's
  output; the conversation is the agent's, second person allowed, warmth allowed, teaching
  allowed. The risk is an agent that puts the conversation in the description; test 3 catches
  it in samples and the gate's description diff catches it in pull requests.
- **The archive is reachable.** An agent asked how tightening works will search, find v2's
  308-line decision table, and reason from a specification v3 replaced. `.ignore` hides
  `archive/` from ripgrep and the permission denies `Read`; a shell `cat` is not covered by
  either; Copilot honours none of it. The archive's exclusion from every count and lint is
  itself a test; `AGENTS.md` says in its first screen that nothing under `archive/` is current;
  and v2's documents are archived as one compressed bundle beside the index, so search tools
  find the index and not the corpus, while v2's code stays buildable until step 9. After step 9
  the archive is a branch and the residual disappears.
- **Generated files edited by hand in this repository.** The permission denies edits to the
  generated paths for Claude Code sessions; a human editor is caught by test 11 on the next
  build. Both are stated in the banner every generated file carries.
- **A compound request through the entry prompt.** The entry point is one conversation; a compound
  request becomes several pull requests, one per release, and the session must keep the order
  straight. `authoring.md` opens with the rule; `decompose`'s ordering rules (create before
  reference, add before drop) are its second paragraph.
- **The values register becomes doctrine.** It is budgeted at 300 lines, every row must
  resolve to a mechanism or say "prose only", and the count of "prose only" rows is printed on
  every build so that it can only go down without a decision. A value that cannot name its
  mechanism after a release is either mechanized or removed.
- **Open questions this document could not settle.** Whether the team's Visual Studio build
  discovers `.github/skills/` (the pilot). Whether Azure DevOps' Copilot surface reads
  `AGENTS.md` (it is generated regardless; it costs nothing). Whether the developers will
  accept the one business question being asked by a tool rather than a person (the pilot's
  conversation cases, judged by the four reviewers). Whether the engine repository and the
  estate repository should merge (companion §16, item 9; this document assumes two).

---

## Appendix A — The retired vocabulary, in two lists

Two lists govern two surfaces, and the lint (§10, tests 5 and 6) has two lists to match. The
first is the pull request description's banned list, from `THE_RECORD.md` §7 and living in
`description.md`; it governs what a developer or a lead reads. The second is the engine's
retired vocabulary; it governs every hand-written file in the engine repository and the
vendored knowledge. Both are banned outside `archive/` and this appendix.

### A.1 The pull request description's banned list (from `THE_RECORD.md` §7)

| Banned in a pull request description or in conversation | Say instead |
|---|---|
| numbered axes as output: `Mechanism 3`, `Tier 2`, `+1 tier` | how it ships; what the lead weighs |
| the tree's private nicknames: `magic line`, `the spine`, `the graduation`, `graduate`, `level up`, `the oracle`, `the flip`, `the corpse`, `proving ground` (in a description), `blast radius`, `naked rename` | the finding, named literally; `a rename with no refactorlog entry` |
| ceremony verbs as surfaced words: `BLESS`, `HAND-BACK`, `REFUSE-ESCALATE` | Approved · Approved with a named risk · Returned to the author · Escalated with one question |
| drama: `destroy(s)`, `fatal`, `catastrophe`, `abort`, `blast`, `veto` as a lead noun | `the deployment is blocked`; `SSDT refused` |
| euphemism: `removes`, `cleans up` for a drop or a delete | the true verb |
| the antithesis tic (`X, not Y` whose negated half adds no fact) | the positive claim alone |
| engine jargon: `Kind`, `OS_KIND_*`, `OSUSR_*`, `SsKey`, `torsor`, `commuting square`, `norm`, `δ` | the table, the column, the foreign key |
| system-shout as a lead: `REFUSED`, `ERROR`, `FAILED` alone | a calm sentence with the code beneath |
| second person in a pull request description | allowed only in conversation |
| cheerleading in conversation: `great question`, `let's dive in`, `happy to help` | usefulness |
| `Test` as an environment name | Dev, QA, UAT, Prod |

The handbook's registered trap names (*Optimistic NOT NULL*, *Ambitious Narrowing*, *Forgotten
FK Check*, *Refactorlog Cleanup*) stay; only *Naked Rename* is retired, for its modifier.

### A.2 The engine's retired vocabulary

| Retired | Say instead |
|---|---|
| pillar, supreme operating discipline | a value in `VALUES.md`, by its name |
| axiom, theorem, A1–A48, T1–T18, bucket A/B/C/D, the ladder (L1/L2/L3), the matrix, the verifiability gate | a law, by its English name in `LAWS.md` |
| chapter open, chapter close, pre-scope, wave map, the presentation contract | the pull request |
| handoff, the letter, "to the next agent" | `NEXT.md` |
| a decision entry (Status / Context / Decision / Reasoning) | one line in `DECISIONS.md`; the reasoning is in the pull request |
| Persona 1, Persona 2, intake, change-author, reviewer (as roles); the change-spec; the review packet | authoring (a phase of one session), reviewing; the pull request description |
| survival rule | a mechanism (a test, a refusal), or a sentence in `knowledge/` |
| `_index`; op-slug; the tree; the skill tree; ssdt-agent | `shared/`; the operation; `knowledge/` |
| `SsKey`, `Kind`, `Attribute`, `Catalog`, `Profile`, `Module` (as types) | `Key`, `Table`, `Column`, `Schema`, `Evidence`; a schema has SQL schemas, not modules |
| project, projection, Π, σ, π (as verbs or symbols in prose) | emit, generate, profile |
| the canary; the quotient (as a noun in prose) | law 2 |
| torsor, commuting square, norm, δ, Kleisli, writer, lineage, episode, ledger (the algebra), seam, binding, capability descent, lane descent, plane, estate board, go board, Voice, View, Watch, the register lint (as a script) | nothing; the things are gone; a `Finding` list, a `Change`, an `Error`, `check environments` |
| skeleton, overlay, pillar 9, operator intent (as a type) | a decision |
| V2-driver, V2-augmented, R6, the dual track, the fallback ladder, cutover+30 (except in the companion's §14.4) | archived with the cutover |
| self-test, the rubric, the golden run, the prompt matrix | the proof lane; the pilot |
| perf gate, `Bench.scope`, `PERF_GATE_RECORD`, the baseline | the scale lane; `ledgers/scale-datapoints.md` |
| `LINT-ALLOW` | nothing; an analyzer suppression carries a justification and is a review comment |
| the proving ground (in the engine), `tests/Golden/proving-ground/` | the golden project, `tests/Golden/project/` |
| F# (as v3's language) | C#; the kernel types are written in F# notation as the specification, and the repository is C# |
| `Ssdt.Walk`, `Walked`, `walk.duplicate-key`, `WalkTests.cs` | `Ssdt.ReadModel`, `ModelObjects`, `model.duplicate-key` (category `model`, exit 2), `ModelElementsTests.cs` |
| `Refusal` (the type), `Result<T>.Refused`, `Result.Refuse`, `Pin.Refuses`, `RefusalExits`; a code's area | `Error`, `Result<T>.Failed`, `Result.Fail`, `Pin.Rejects`, `ExitByCategory`; its category. The verb refuse stays for a refusal by policy, and the frozen exit names stay |
| `Receipt` and its fields `Delta`, `Target`, `DataFacts`, `Engine`, `Profile`, `Where`; `Engine`; `Ssdt.Engine`; the JSON `engine` and the codes `engine.*` | `Provenance` with `Change`, `Schema`, `ExistingData`, `DacFx`, `Server`, `PublishProfile`, `Target`, `At`; `DacFx` (a `DacFxVersion`) and `Server` (product version, compatibility level, image digest); `BuildTargets`; `dacfx`, `server` and `pin`; `server.image-digest`, `toolchain.dacfx-version`, `toolchain.unpinned`. In prose: DacFx, SQL Server, or the tool's name |
| `Branch`, `BranchSite`, `ClaimSite`, `Transfers` (the M2 types); `branch.malformed`, `branch.taken` | `ExistingData`, `PreconditionState`, `Precondition`, `AppliesTo`; `git-branch.malformed`, `git-branch.exists`; git keeps the word branch (decision 2.8) |
| `cohorts` | `readerGroups`; the code `environments.reader-groups` |
| the substrate, `io/Substrate.cs`, the category and doctor item `substrate` | the local server, `io/LocalServer.cs`, `local-server` |
| the Twin, the verb and target `twin`, `twin.not-built`, `io/Twin` | the synthetic copy, `SyntheticCopy`, `synthetic-copy`, `synthetic-copy.not-built`, `io/SyntheticCopy` |
| σ, mint, `Synth`, `Realize.cs` | `SyntheticData.Generate`; a generated set; `GenerateViolatingRow` |
| `SqlServer.Named`, `Database.Where` | `EnvironmentDatabase`, `Database.Target`; `Copy` stays |
| `Seq<T>`, `Seq.Of` | `SortedArray<T>`, `SortedArray.Of` |
| `Delta`, the delta; `Change.Added`, `Removed`, `Changed`; the nested `Altered` | `Change`, the change; `Created`, `Dropped`, `Altered`; `Alteration`; the JSON fields and output lines follow (dropped, as DacFx says) |
| the envelope's `verdict` object (`outcome`, `message`, `kind`) | `outcome`, `message` and `blockedBy` (`block-on-possible-data-loss` or `constraint-violation`) on the envelope; verdict stays for `prove`'s result |
| the severities `block`, `warn` | `error`, `warning`; `note` stays |
| `converged`, the convergence oracle, the law "a published copy converges" | `in-sync`, the empty deploy plan, "a published copy is in sync with its package" |
| the record (the pull request's body), the verb `record`, `Record`, `knowledge/record.md` | the pull request description, `describe`, `PullRequestDescription`, `knowledge/description.md` |
| reference stub | reference assemblies (`Microsoft.NETFramework.ReferenceAssemblies`) |
| `Probe`, the probe executor, `probe.refused`; `Ssdt.Build`'s `probe` parameter | `AggregateQuery`, `SqlServer.Measure`, `aggregate-query.refused`; `run` |
| the germ, the two instruments, lens, directive, the lens register | the lifecycle invariants (`LIFECYCLE_BACKPORT_PROMPT.md`), prediction and proof, the verb table, the question; `ci/review` says review area |
| the guard, `Blocked.Guard`, `profile.guard-off`, `GuardSite` | `BlockOnPossibleDataLoss` (DacFx's option and the check it writes into the deploy script), `BlockedBy.BlockOnPossibleDataLoss`, `profile.data-loss-allowed`, `BlockOnPossibleDataLossSite` |
| archetype, `Archetypes.cs` | sample change, `SampleChanges.cs` |
| `Ssdt.Read`; a read (as a noun) | `ModelElements`; a model |
| the milestone titles Ground, Twin, Record and gate, Front door; the wing; the front door and the one door (in these documents) | Foundation, Synthetic copy, Describe and gate, Agent instructions; the cutover tools; `knowledge/README.md` and the entry prompt |
| `Git.Turn`, `Substrate.Held`, `Patience` | `io/FileLock.cs` with `FileLock.Take(path, timeout)`; `Timeout`; a process run's outcomes `Exited`, `NotFound`, `TimedOut` |

---

## Appendix B — Reading paths

**A new maintainer, day one (about forty minutes to a green build).** `README.md` (five
minutes). `VALUES.md` (ten). `kernel/README.md` (three). `LAWS.md` (two: the names, not the
tests). `estate doctor`, then `dotnet test --filter Category=fast` (fifteen). `NEXT.md` (two).
Then the package README of whatever `NEXT.md` names.

**A developer's first change (one session).** The entry prompt opens `authoring.md`. S0 names
the operation and asks the one question. S1 edits the CREATE. S2–S4 are three verbs. S5 picks
the shape from the verdict. S6 if proving forked. S7 is `estate describe`. S8 reads it against
`description.md`. The pull request carries the description; the gate reproduces it. Total prose
on the path: the router (60) + `authoring.md` (150) + one operation (≤ 90) + one shared file
(≤ 120) + `description.md` (120) = under 600 lines, and test 2 keeps it there.

**A reviewer's first review (fifteen minutes).** `description.md` (five). The pull request body,
findings first. `gate.json` (the reproduced verdict; the description diff). If trust is in question,
`reviewing.md` and one `estate prove` on the reviewer's own copy. One of four dispositions, in
the register.

**The owner's session.** `NEXT.md` ("Waiting on a person" first). The tail of `DECISIONS.md`.
`ARCHITECTURE.md` §16's list, which is where the decisions only the operator can make live
until each is made and becomes a line.

**A cloud or autonomous session.** `CLAUDE.md` imports `AGENTS.md`; the hook prints the
doctor line; `NEXT.md` says what is in flight. The session works, tests, opens a pull request
with the engine template, rewrites `NEXT.md`, and stops. If it needs a decision, the decision
is a line under "Waiting on a person", and the session does not guess. It never reads the
archive unless the task names it, and it never writes a document that is not in the manifest.

---

## Appendix C — Numbers measured for this document

Measured on 2026-09-17 against `4e844fc` with `find`, `wc -l`, and `grep`; corrected where an
ingestion pass said otherwise (`HANDOFF.md` is 3,887 lines; the pass reported 30,911). The
quantities §2.1's table carries are not repeated here.

| Quantity | Value |
|---|---:|
| skills | 45 operations (64–131 lines, median 93.5, 4,961 total) · 6 shared (750) · 4 review (495) · 10 top-level (68–576; `prove-on-dacpac` 576, `talk-to-local-sql` 406) |
| samples | 50 files · 3,755 lines (46 top-level, 4 compound) |
| ledgers | 8 files · 272 lines; `reviewers.md` has two unfilled rows; `operations.md` and `in-flight.md` have no rows |
| skills and agents citing what step 6 removes or renames (of 68 files) | `sample-prs` 53 · `FINDINGS_AND_CHANGES.md` 21 · `handbook` 20 · `estate/` 11 · `CONNECTORS.md` 10 · `proving-ground` 9 · `self-test` 8 · `PROVING_PATH_WINDOWS.md` 2 |
| the Copilot authoring path today | router 60 + largest instruction 23 + largest operation 131 + `THE_RECORD.md` 308 + `THE_RECORD_FORMS.md` 143 = 665 lines; after the trim about 320 |
| ceremony in v2's `src/` (occurrences = lines) | `LINT-ALLOW` 566 in 219 files · `Bench.scope` 279 in 86 · `RequireQualifiedAccess` 619 in 291 · `ValidationError.create` 338 in 88 (`grep -rao … --include=*.fs src \| wc -l`) |
| files with a generated banner | `NORTH_STAR.matrix.generated.md` · `skills/INDEX.md` · the router · 4 instructions · 4 prompts · the PR template · `projection.schema.json` |
| skills that cite v1's handbook or playbook | 1 of 65 (`merge-tables` → `Table-Merge.md`) |
| `global.json` | SDK 9.0.314, `rollForward: disable`, at the root and in the sidecar |
| `.editorconfig` (sidecar) | `end_of_line = lf` · `charset = utf-8` · no BOM in the golden project's `.sql` |
| central package management · lockfiles · `.gitattributes` | none · none · none |
| `<Nullable>enable</Nullable>` · `<TreatWarningsAsErrors>` | 41 · 20 project files |
| v2's stated guarantees | 187 statements across 24 themes (the values digest); the ones with a mechanism are the analyzers, the lints, the property tests and the fixtures |

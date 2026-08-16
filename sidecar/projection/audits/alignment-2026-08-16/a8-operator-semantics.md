# Operator semantics — alignment audit workpaper

Auditor A8 · scope: the operator-surface plane — `Voice.fs`, `CliExit.fs`, `Program.fs` verb grammar,
`Faces/` shared shape, `TtyRenderer`/`View`, `Config.fs` + `ConfigSchema.fs` + `projection.schema.json`.
All paths relative to `/home/user/outsystems-ddl-exporter/sidecar/projection/` unless absolute.

---

## 1 Vocabulary inventory (with file anchors)

**The copy plane (Voice).**
- `Voice.Copy` — code-keyed catalog, 69 entries harvested into `all` (`src/Projection.Cli/Voice.fs:1354-1446`); each `{Code; DocSection; Statement; Substantiation; Action}` over an untyped `Payload = Map<string, objnull>` (`Voice.fs:49`).
- `Voice.ErrorFrame` — closed 12-case DU for the open error-code space; `classifyError` (prefix routing, `Voice.fs:1536-1553`) split from `frameCopy` (total copy, `Voice.fs:1558-1613`).
- `Voice.gateStatement` — total over `Preflight.GateLabel` (13 cases, `Voice.fs:1665-1704`); `migrationStopDetail` total over `MigrationError` (14 arms, `Voice.fs:1740-1755`).
- `stageName` / `followOnAfter` / `followOnHalted` — string-keyed stage maps with identity/default fallbacks (`Voice.fs:99-140`).
- `Surface = {Statement; Substantiation; Action}` (`src/Projection.Cli/Surface.fs:12-21`) — the one statement-first shape; `Surface.render` the one assembly (`Surface.fs:28-34`).
- `View` — 17-case closed render DU + closed `PanelRow` + `TreeNode` (`src/Projection.Cli/View.fs:20-123`); one substrate, pretty/plain/json lenses.
- `TtyRenderer.renderVoicedTo` / `renderVoicedError` — the two doors from code to surface (`src/Projection.Cli/TtyRenderer.fs:489-495, 423-426`); NM-47 `fallbackSurface` for unvoiced codes (`Voice.fs:1473-1485`).

**The exit plane.**
- `CliExit.classifyCode` — string-substring ladder → raw `int` for the artifact/read-only verbs (`src/Projection.Cli/CliExit.fs:43-66`); consumed by Sync/Synthetic/Slice faces (`Faces/Sync.fs:72`, `Faces/Synthetic.fs:113,129,151`, `Faces/Slice.fs:42,85,204`).
- `Preflight.GateLabel` → `labelText` / `exitOf` / `labelOf` — closed DU, total exit map (`src/Projection.Pipeline/Preflight.fs:664-806`).
- The published contract — `Program.usageLines` "Exit codes" hand-prose (`src/Projection.Cli/Program.fs:143-155`).
- Scattered literals — `PlanAction.Refused (1|2|6|9, …)` throughout `MovementSurface.fs` planning arms; inline `6`/`1`/`7`/`4` in `Program.fs:201,425,445,514,654` and faces (`Faces/Canary.fs:33,110`).

**The verb grammar.**
- `Command.parse` (`src/Projection.Pipeline/MovementSurface.fs:2761-2832`): flows-as-verbs (`projection <flow>`), closed secondary verbs single-sourced from `ProjectionConfig.reservedFlowVerbs` (`MovementSurface.fs:2755`, collision-refused at config load `:1034-1039`); `diff` is a declared alias of `explain diff` (`:2765-2769`); `check` subverbs in `planCheck` (`:2218+`) incl. the `("environments" | "estate")` alias (`:2406`).
- `Ref` — the operand algebra `file | json: | @run | live: | ossys: | sink:env[@n]` with the epistemic predicate `espaceSafe` (`src/Projection.Pipeline/Ref.fs:18-88`).
- Flow flags — `FlowRunOpts` 15 fields, contains-based extraction (`MovementSurface.fs:2807-2821`).

**The config plane.**
- `Config.Config` 8 shaping sections (`src/Projection.Pipeline/Config.fs:668-677`); `EmissionSection` ~17 booleans + typed policies (`Config.fs:374-494`); `SinkPolicy` off/auto/pinned with derived `all`/`label`/`parse` (`Config.fs:624-644`); `WriteSignoff.WriteApproval`/`ActBlessing` (`src/Projection.Pipeline/WriteSignoff.fs:62-97`).
- `ConfigSchema.generate` — generated schema, enums derived from `WriteSignoff.allModes`/`TransformGroup.all`/`SinkPolicy.all` (`src/Projection.Pipeline/ConfigSchema.fs:114-212`), byte-compare drift test; scope = shaping namespaces only.
- `RelaxationStore` — the CLI-written `tighteningRelaxations` string array in `projection.json` (`src/Projection.Cli/RelaxationStore.fs:8-28`).

**Faces shared shape.** `Face.run`/`watchInline`/`staged` (`src/Projection.Cli/Faces/Common.fs:29-55`); `Shell.execute` with registers Go/Preview/ReadOnly (`Program.fs:174-180, 226-234`); `nameOf` delegating to `Catalog.displayNameIn` (`Common.fs:17-21`).

## 2 The domain space (independent of current code)

The operator surface must express, for every verb the engine has or will grow:
- **Outcome classes** — proved (matched/unchanged/silent), diverged (with evidence grade: count vs byte vs shape), refused (gate/config/argument/permission/connection), stopped (pre-write vs mid-write vs post-write-unverified), advisory. Each needs exactly one code, one copy, one exit class — and the write-state claim ("nothing was applied") is part of the outcome, not decoration.
- **Consent & rulings** — every irreversible or judgment-bearing act (drop, wipe, delete-scope, correction, re-key, identity correspondence, tightening relaxation, residue adoption) has an operator RULING with identity (who), time, acknowledged evidence, scope, and revocation/reopen. The ruling is a domain object at ONE grain regardless of which plane (transfer, emission, estate, sink) asks for it.
- **Setup state** — configured / unset-optional / unset-required / set-invalid, each a named posture (§14), for every key the config admits; a key the parser accepts is a key some engine behavior consumes (A44 both directions), and a flag the help advertises is a flag the parser reads.
- **Lifecycle** — stages in flight, halted, followed-on; evidence age/provenance leading every verdict that stands on cached or witnessed state.
- **Scale invariance** — constant-size surfaces over growing counts (rollups, capped breadth).

Dimensions: RELATIONAL (verb↔outcome↔code↔copy↔exit is a commuting square), SEMANTIC (one name per concept across planes), STATE (ruling lifecycle: proposed→ruled→reopened; evidence: live→cached→stale→offline), HIERARCHICAL (Core fact → Pipeline classification → CLI copy; internals never surface, copy never sinks into Core).

## 3 Findings — table first

| ID | Class | Dimension | Reification axis | One-line claim | Anchor |
|---|---|---|---|---|---|
| A8-1 | M6+M1+M7 | STATE | ontic/teleological | The operator's RULING is reified at four different grades across five planes; on the sink/estate DECIDE lane it is not expressible at all — copy demands a ruling no primitive can receive | Voice.fs:1322, Estate.fs:2267, RelaxationStore.fs:21-22, WriteSignoff.fs:62-97 |
| A8-2 | M4+M6 | RELATIONAL | ontic | Exit codes are three unreconciled authorities; integers 1, 2, 3 each carry two outcome classes; no closed ExitClass type exists at the CLI grain though the pattern exists one layer down | Program.fs:143-155, CliExit.fs:33-36, Preflight.fs:729-743 |
| A8-3 | M6+M5 | SEMANTIC | epistemic | The copy⇔payload edge of the code⇔copy⇔outcome triangle is unreified: Hero verdicts degrade silently ("Sync ? witnessed") when an emit site's untyped keys drift from the catalog's reads | Voice.fs:49,80-83,1233-1236; VoiceTotalityTests.fs:204 |
| A8-4 | M5 | SEMANTIC | epistemic | `GenericStop`, the total fallback over the open error space, asserts the strongest write claim — "Stopped before any change was applied" — which the classifier cannot know for an arbitrary code | Voice.fs:1611-1613, 1536-1553 |
| A8-5 | M2+M4 | SEMANTIC | teleological | The data-composition policy is spread over three interacting emission booleans whose 8 combinations alias onto 3 outcomes (one combo inert), while the target DU `DataComposition` already exists — the sink's own off/auto/pinned pattern unapplied | Config.fs:374-396,686-689; Hydration.fs:27-30 |
| A8-6 | M5+M1 | STATE | ontic | The generated schema covers only the shaping half of one document — the daily movement plane (flows/environments/readiness) has no schema and unknown keys are silently ignored — and the config still carries an advertised parsed-but-unconsumed key (`overrides.staticData`) | ConfigSchema.fs:26-32; projection.schema.json:4; Config.fs:332; RelaxationStore.fs:17-19 |
| A8-7 | M6+M5 | RELATIONAL | epistemic | The help advertises `--atomic` on the daily flow line but no parser reads it (only `--no-atomic` exists); unknown flow-tail flags fall through silently, so an advertised protection is inert | Program.fs:46,106; MovementSurface.fs:2084,2815 |
| A8-8 | M3+M4 | HIERARCHICAL | ontic | The espace-unsafe advisory is inline face prose, duplicated verbatim across diff/compare, speaking engine internals ("SsKeys are synthesized") on the surface — the exact say-prose-once + leaked-internals defect the Voice migration already fixed elsewhere | Faces/Diff.fs:47,104; Ref.fs:69-88; Voice.fs:823-827 |

---

### A8-1 — The ruling is five vocabularies, and the newest plane has none (M6 + M1 + M7) — DEEPEST

**Evidence.**
- *Write-consent plane* — fully reified: `WriteSignoff.WriteApproval {Mode; Tables; AcknowledgedImpact; ApprovedBy; Date}` and `ActBlessing {Act; Fingerprint; …}` (`WriteSignoff.fs:62-97`) — who, when, on what evidence (the fingerprint reopens the blessing when reality moves, `WriteSignoff.fs:76-84`), revocable by config edit, scope-verified (`THE_CONFIG_CONTROL_PLANE.md` §9).
- *Policy-version plane* — `seal approve <version> --approver <name>` → `ApprovalRecord` (`src/Projection.Core/ApprovalWorkflow.fs:37`; `Program.fs:394`).
- *Tightening-relaxation plane* — a **bare string set**: `tighteningRelaxations: ["Kind.Column", …]` (`RelaxationStore.fs:21-22, 46-64`) — no approver, no date, no evidence, no reopen probe; written by a surgical merge into a file whose own renderer does not know the key (`RelaxationStore.fs:17-19` admits `renderConfig` preservation is "the broader audit-F7 fix").
- *Estate interim plane* — overlay entries carry the reopen probe (`Voice.fs:1103-1106`: "each carries the probe that clears it") but the engine "never applies it — the merge is the operator's" — the ruling itself (who merged, when, why) evaporates into an untracked config edit.
- *Sink-claims plane (this chapter)* — the copy hands the operator a ruling lever that does not exist: `sink.cutoverCorrespondence` says "confirm or reject each on the DECIDE lane (nothing is adopted without the ruling)" (`Voice.fs:1316-1326`) and the finding says "confirm or reject the correspondence" (`src/Projection.Pipeline/Estate.fs:2267`), but no config key, verb, store, or overlay accepts a confirm/reject (grep over `Config.fs` and the CLI: none). For contested/tombstone-only/unclaimed there is at least an out-of-band estate act the next `sync` witnesses; the K9 correspondence is a **pure identity judgment** (two SsKeys are one lineage) with no estate-side act that could express it — the finding refires on every run forever, and a confirmed lineage can never inform a cross-cutover diff.

**Misalignment.** One domain thing — *an operator's ruling on a named subject, at a moment, on acknowledged evidence, revocably* — is named five ways at four grades (M4/M7), is unreified on two planes (M6), and on the correspondence lane the outcome "this ruling, recorded" is inexpressible (M1). K9's own law ("proposed on evidence, NEVER auto-adopted") reifies the proposal and forgets the adoption.

**Candidate primitive.** `Ruling` — `{ Subject : FindingKey (or claim key); Verdict : Adopt | Reject | Defer of probe; ApprovedBy; Date; EvidenceFingerprint }`, persisted (config array or estate store), consumed by the estate board (a ruled finding leaves the DECIDE lane until its fingerprint moves) and by the sink claim assembly (a confirmed correspondence threads lineage into `diff sink:…`). `WriteApproval`/`ActBlessing` become its two existing specializations; `tighteningRelaxations` upgrades to it (legacy bare strings map in — the `model:"<path>"` precedent).

**Outcome-fluency bought.** "What has been ruled, by whom, on what evidence, and what reopens it" becomes one queryable surface across every plane; the DECIDE lane can actually empty; cutover identity judgments survive the operator who made them. **Effort:** M–L. **Risk-of-inaction:** the estate instrument decays into a nag list; the highest-stakes judgments of the eject era (identity correspondences) live only in heads and Slack.

### A8-2 — The exit-code vocabulary: three authorities, aliased integers (M4 + M6)

**Evidence.** The published contract says `1 argv error` / `2 parse error` / `3 execution error (SQL rejected…; connection open…)` (`Program.fs:146-148`). `CliExit` maps `.writeFailed`/`.emitFailed → 1` and documents 1 as "artifact write/emit failed (an output-IO failure)" while claiming "the exit axes match the published contract" (`CliExit.fs:33-35, 53`). `Preflight.exitOf` maps `ReconciliationMismatch → 2` — a *data* mismatch ("The source and the sink do not reconcile", `Voice.fs:1686`) exits on the published "parse error" code — and `UnclassifiedRefusal → 3` (`Preflight.fs:733,743`), so 3 = "execution error" ∪ "any other refusal". Both meanings of each integer fire at runtime: `projection nonsense` → 1 (`Program.fs:654`) and a slice write-IO failure → 1 (`CliExit.fs:53`). Meanwhile the outcome CLASS is a closed DU on the gate plane (`GateLabel`, exemplary) but bare ints on the CLI plane and bare prose on the published plane; `PlanAction.Refused (2|6|9, …)` literals are scattered through 30+ planning arms (`MovementSurface.fs:2186,2191,2226,…`).

**Misalignment.** M4 (one code, two outcomes — a script cannot distinguish "operator typo" from "lost artifact"), M6 (the exit-class concept is reified on one plane only; the published contract is hand-restated prose — the same restatement class CLAUDE.md §0 calls a first-class defect, and it HAS drifted).

**Candidate primitive.** `ExitClass` — one closed DU (≈ `Success | ArgvError | InputParse | ExecutionFailed | DockerUnavailable | FidelityDivergence | ConfigError | GateWithheld | CountDivergence | RefusedFailLoud | ArtifactWriteFailed`), one total `code : ExitClass -> int`, with `Preflight.exitOf`, `CliExit.classifyCode`, and every `Refused` literal projecting through it — and the usage "Exit codes" block **generated** from it (the `ConfigSchema` T-IV move applied to help text).

**Outcome-fluency bought.** `exit N ⇔ outcome class` becomes a law with one owner; CI wrappers and Octopus steps can branch on exits truthfully; the help cannot lie. **Effort:** M. **Risk-of-inaction:** automation built on the published table misroutes the exact pairs (typo vs lost write; refusal vs SQL failure) it exists to separate.

### A8-3 — The triangle's third edge: copy⇔payload is convention (M6 + M5)

**Evidence.** `code ⇔ copy` is superbly total (`VoiceTotalityTests.fs:19,195,272-345` — in-scope⇔voiced both directions, phantom-code detection, call-site literal pinning). But every `Copy` reads `Map<string, objnull>` with per-key defaults (`Voice.fs:49, 75-83`): `syncCompleted` renders **"Sync ? witnessed"** on a missing `syncId` (`Voice.fs:1233-1236`); `estateDiverged` renders zero counts on renamed keys (`Voice.fs:1044-1065`). Emit sites author payload keys independently (`Faces/Diff.fs:27`; `Program.fs:164`); the tests construct their own "representative payload" fixtures (`VoiceTotalityTests.fs:204-206`), so a key rename at a real emit site is invisible to the suite. `Voice.fs:24-29` names typed `toView` (mechanism 1) as the destination — but Hero-grade *verdicts* (`sync.completed`, `estate.*`, `fidelity.rows.*`) now ride the untyped mechanism 2.

**Misalignment.** The type does not carry how the copy's claims are known (EPISTEMIC): the outcome is `(code, payload)` but only `code` is contract-bound — copy is total over codes, partial over the payload half of the outcome (M5), and the key contract per code is knowledge held nowhere (M6).

**Candidate primitive.** Per-code payload contract: either the promised typed `toView` companions for verdict codes, or minimally `RequiredKeys : string list` on `Copy`, asserted by the same call-site-pinning idiom the tests already use for codes — an emit site missing a required key fails the totality suite, and a Hero never renders `?`.

**Outcome-fluency bought.** The verdict line — the single most consequential sentence the instrument speaks — cannot silently degrade. **Effort:** S–M. **Risk-of-inaction:** the strongest claims decay quietly, exactly where "honest without exception" matters.

### A8-4 — The generic frame overclaims the write state (M5)

**Evidence.** `classifyError`'s `else` routes every unrecognized code to `GenericStop`, whose copy is "**Stopped before any change was applied.** The cause is shown below." (`Voice.fs:1553, 1611-1613`). The vocabulary already distinguishes write states elsewhere: `migrate.stopped` says only "did not complete" (`Voice.fs:735`), `PartialWriteUnrecovered` says "some changes remain" (`Voice.fs:1755`), `migrate.verificationFailed` says "were applied" (`Voice.fs:881`). A new face's post-write failure code with an unregistered prefix lands on a false no-write guarantee.

**Misalignment.** A claim true of a *subset* of the domain (pre-flight/parse/config stops) is the copy of the *total* default (M5) — an epistemic overreach: the frame asserts what the classifier does not know, inverting rule 8 (ground every claim in its evidence).

**Candidate primitive.** Split the default: `GenericStop` copy becomes write-state-neutral ("The run stopped. The cause is shown below."); the no-write claim moves onto frames that structurally know it (ConfigProblem, CheckArgument, the parse/argument prefixes) — one line each in `frameCopy`.

**Outcome-fluency bought.** The fallback stays honest at the exact moment a new failure mode appears — which is when the fallback fires. **Effort:** S. **Risk-of-inaction:** one bad incident where "nothing was applied" was false costs the register its earned trust.

### A8-5 — Emission booleans where the domain wants the sink's policy shape (M2 + M4)

**Evidence.** The composition policy is derived from three interacting booleans: `dataCompositionOf` reads `BootstrapAllData` then `StaticSeeds` (`Config.fs:686-689`), while lane-firing reads `StaticSeeds || MigrationDependencies || Bootstrap` — *not* `BootstrapAllData` (`Hydration.fs:27-30`, "replicated here" from `buildPolicyFromConfig` with its own drift note). So `{bootstrapAllData:true}` alone is expressible and inert (composition `AllData`, no lane fires); 8 combinations alias onto ~3 real outcomes; each field's doc comment narrates pairwise interactions (`Config.fs:388-396`). The codomain DU `DataComposition (AllData | AllRemaining | AllExceptStatic)` already exists — the config just cannot say it directly. The house has the correct pattern three times over in the SAME file/schema: `sink.policy` off/auto/pinned (`Config.fs:624-644`), `dataStaging.mode` auto/inline/tempTable (`Config.fs:455-462`), staging `cache` off/auto/pinned (`ConfigSchema.fs:161`).

**Misalignment.** M2: the boolean shape forecloses compositions the DU vocabulary could grow (and invites inert combinations A44's no-inert-keys clause bans); M4: one concept (what the data lanes cover) split across three key names plus a duplicated derivation.

**Candidate primitive.** `emission.dataComposition : "all" | "remaining" | "exceptStatic"` — parsed via a derived `all`/`label` list (the `SinkPolicy` idiom), legacy booleans mapped in the loader (the `model:"<path>"` precedent), `dataCompositionOf` and `emitDataOf` collapsing to one reader.

**Outcome-fluency bought.** The composition becomes sayable in one word, the inert combination unrepresentable, the schema enum derived. **Effort:** S–M. **Risk-of-inaction:** the next data lane doubles the combination space and the pairwise-comment debt.

### A8-6 — Half the control plane has no schema; one key is advertised and dead (M5 + M1-inverse)

**Evidence.** `ConfigSchema` deliberately scopes to the shaping namespaces (`ConfigSchema.fs:26-32`); the shipped schema's own description says "the movement namespaces — environments/flows/readiness — ride the same file" under blanket `additionalProperties: true` (`projection.schema.json:4`). The parser "ignores unknown keys" by contract (`Program.fs:459`), so on the *daily* plane a misspelled `scope` silently falls back to grant-derived, a misspelled `signoff` silently un-declares a standing authorization (fail-closed but mysterious), and no editor validation exists for exactly the vocabulary richest in closed DUs (access/grant/rendition/archetype/scope/shape — all derivable). Meanwhile `overrides.staticData` is advertised in the schema as "reserved — parsed but not consumed" (`ConfigSchema.fs:142`; `Config.fs:332`) — an expressible key reaching nothing, the precise inert-key class NM-03 already *removed* once (`Config.fs:496-507`) and `THE_CONFIG_CONTROL_PLANE.md` §4 bans ("every key is consumed the moment it is expressible — no inert keys"). The tool even WRITES a key (`tighteningRelaxations`) that its own `renderFlow`/`renderConfig` round-trip does not preserve (`RelaxationStore.fs:17-19`).

**Misalignment.** A44 is enforced as a canary for *actions* (expressible ⇔ reachable specs) but not for *keys*: the expressible-side domain is only half-covered by the generated projection (M5), one advertised point maps to nothing (the A44 inverse), and the tool's own output vocabulary sits outside its renderer's domain (M4).

**Candidate primitive.** Extend `ConfigSchema.document` with the movement namespaces, enums derived from the existing closed DUs (the derivation machinery is already built); either consume or delete `staticData` (NM-03 precedent says delete); teach `renderConfig` the relaxation key so `parse ∘ render = id` holds over everything the tool itself writes.

**Outcome-fluency bought.** As-you-type truth on the plane operators actually edit daily; A44 total over keys, not just actions. **Effort:** M. **Risk-of-inaction:** the highest-traffic namespace stays the least validated; silent typo-loss on authorization-adjacent keys.

### A8-7 — An advertised flag no parser reads (M6 + M5)

**Evidence.** The usage flow line advertises `[--atomic]` and describes it ("--atomic wraps the schema deploy in one transaction", `Program.fs:46,106`); the only parsed token is `--no-atomic` (`MovementSurface.fs:2815`) — atomicity is now env-derived (`toEnv.AtomicDeploy … && not opts.NoAtomic`, `MovementSurface.fs:2084`). Grep confirms no `"--atomic"` parse anywhere in `src/`. Flow-tail extraction is contains-based with no leftover-token refusal (`MovementSurface.fs:2807-2821`), so `projection publish --atomic` (or any misspelled flag, e.g. `--resumeable`) is silently inert. The A7 inert-flag note mechanism exists (`Program.fs:165-167`) but covers one module-filter case — the no-silent-drop law is honored by one flag and waived for the rest (M7 flavor: config keys refuse by name, argv flags fall through).

**Misalignment.** The published grammar and the parsed grammar have drifted (M6 — the contract is knowledge held only in prose), and flag handling is a partial function whose misses are silent (M5) on the surface where consent and write-protection flags live.

**Candidate primitive.** A closed flow-flag table (flag literal ⇔ `FlowRunOpts` field), the usage line derived from it, and a `cli.flow.unknownFlag` refusal for leftover `--` tokens (the parser already refuses malformed *values* — `cli.flow.seedInvalid` — so the fail-closed idiom is local).

**Outcome-fluency bought.** What the help says is what the parser accepts, provably; a typo'd protection flag refuses instead of silently not protecting. **Effort:** S. **Risk-of-inaction:** an operator trusts the advertised `--atomic` transaction and gets a non-atomic deploy — the exact outcome `MidWriteNotProtected` (exit 9) exists to prevent.

### A8-8 — The espace advisory: inline, duplicated, internal-voiced (M3 + M4)

**Evidence.** `Faces/Diff.fs:47` and `:104` carry the same ~50-word advisory verbatim (`"…SsKeys are synthesized from physical names…"`) via bare `Console.Error.WriteLine` — engine identity internals (`SsKey`, "PHYSICAL identity") on the statement surface, against §2.2's leaked-internals ban and the lexicon (`THE_VOICE.md` §2.1 keeps `SsKey` off-surface). The underlying fact is already reified in the operand algebra (`Ref.espaceSafe`/`bothLive`, `Ref.fs:69-88`); and the say-prose-once rule was already enforced for exactly this defect class at `transfer.rowsDropped` ("ONE definition — the literal was duplicated verbatim across the transfer and synthetic faces", `Voice.fs:823-827`).

**Misalignment.** M3 — stage-internal vocabulary leaking up past the translation boundary; M4 — one outcome (operands cannot be identity-matched) as two duplicated inline strings outside the catalog, invisible to the register/banned-list tests.

**Candidate primitive.** A voiced code `diff.operandsEspaceUnsafe` (Warn note; statement in operator words — "the same table in two environments cannot be matched by physical name; use ossys: or sink: operands, or projection check shape"), emitted by both faces through `renderVoicedTo`.

**Outcome-fluency bought.** The advisory joins the tested register; the two faces cannot drift. **Effort:** S. **Risk-of-inaction:** register erosion at the surface newcomers hit first when comparing environments.

## 4 Anti-findings (correct specializations)

- **`diff` as a top-level alias of `explain diff`** (`MovementSurface.fs:2765-2769`) and **`environments | estate`** (`:2406`) — two spellings, ONE routing arm each, comment-named as promotion/alias. No behavioral split exists to drift; not M4.
- **Exit 8 vs exit 5 for "the data differs"** (`Program.fs:150-153`) — deliberately evidence-graded: count-divergence and byte-proof-divergence are different findings epistemically, and the usage names the distinction inline. A justified asymmetry, not M7.
- **The two-tier authorization family** — transient per-run flags (`--go`/`--allow-drops`/`--allow-cdc`) vs standing config declarations (`signoff`) is a ruled design (`THE_CONFIG_CONTROL_PLANE.md` §9), and the flags are fail-closed: a missing flag can only refuse, never write.
- **`reservedFlowVerbs` single-sourcing** (`MovementSurface.fs:1034-1039, 2755`) — the flow-name/verb grammar partition is protected structurally: a flow named after a verb is refused at config load, so the namespace collision cannot exist.
- **`scope` decoupled from `grant`** (G1 close) — refusal gate vs move projection kept as two keys is the correct two-concepts-two-names call, not a split vocabulary.
- **Schema-plane copy staying face-side for the load-plan TABLE** (`Voice.fs:788-792`) — structured per-kind disclosure is view-shaped, not copy-shaped; keeping only the verdict lead in the catalog is the right grain.
- **`stageName`'s `| other -> other` passthrough** (`Voice.fs:111`) — a deliberate identity default over the string wire-key, held total by the totality test against the emitted stage set (`VoiceTotalityTests.fs:614-623`); the typed `StageName` (`RunSpine.fs:24-49`) owns validity below the wire. Two representations, one seam, both tested — acceptable, though it is the residual string edge of an otherwise typed spine.

## 5 Already-aligned (exemplary reifications)

- **The Voice totality machinery** — `inScopeCodes ⇔ voicedCodes` both directions, phantom-code sweep, call-site literal pinning, banned-list register tests over filled AND empty payloads, and the NM-47 loud fallback (`VoiceTotalityTests.fs:272-345`; `Voice.fs:1473-1485`). Code⇔copy is a law, not a habit.
- **`Preflight.GateLabel` → `labelText`/`exitOf`/`labelOf`** (`Preflight.fs:664-806`) — the closed outcome-class DU with routing split from exit policy, compiler-total copy (`Voice.gateStatement`), and the gate surface carrying axis + detail + exit as substantiation (`Voice.fs:1711-1723`). This is the shape A8-2 asks the rest of the exit plane to take.
- **`Ref`** (`Ref.fs`) — the operand algebra whose type carries HOW identity is known (`espaceSafe`): epistemics reified as a predicate, consumed for normalization and advisories.
- **`WriteSignoff`** — `allModes` derived everywhere (parser, refusal hint, schema enum; the drifted third list is documented and dead, `WriteSignoff.fs:99-148`); `impactOf` puts the consequence in the operator's hands before the ruling; `ActBlessing` fingerprints make a stale approval unable to rubber-stamp a new reality.
- **`ConfigSchema` generation** (`ConfigSchema.fs:11-24`) — schema-from-code with derived enums and a byte-compare drift test: T-IV operating on the covered half (A8-6 asks for the other half).
- **`SinkPolicy` + `SinkSection.effective`** (`Config.fs:624-666`) — the chapter's "the policy is the lever" worked example: one closed vocabulary, reuse-axis-only semantics documented in the type, per-env refinement total.
- **`View` / `PanelRow`** (`View.fs:20-123`) — one substrate, three lenses; the closed `PanelRow` exists precisely because an open list once let the human and machine lenses diverge — the drift class named and made unrepresentable.
- **`Voice.ErrorFrame`** (`Voice.fs:1517-1613`) — the open sprintf code space routed onto a closed DU with compiler-total copy; A8-4 is one arm's copy, not the mechanism.
- **`CliExit`'s reconciliation** (`CliExit.fs:14-27`) — six drifted per-face ladders collapsed to one table with the divergences named and reconciled onto the gate convention; the repair direction is exactly right (A8-2 asks it to finish the climb from ints to a type).

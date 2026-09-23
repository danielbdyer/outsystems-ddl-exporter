# Decision strategies — alignment audit workpaper

Auditor A4 · 2026-08-16 · scope: `src/Projection.Core/Strategies/*`, `DecisionOverlay.fs`,
`ApprovalWorkflow.fs`, `ActConsent.fs`, `ApprovedDataCorrections.fs`, `DataCorrectionReceipt.fs`,
`ConflictDetector.fs`, plus the tightening/policy decision surfaces they feed
(`Policy.fs` tightening axis, `BridgeRetarget.fs`, `ComposeState.fs`, the tightening passes,
`EstatePosture.fs`, `WriteSignoff.fs`, `SuggestConfigEmitter.fs`, `Lineage.fs` annotation plane).
All paths relative to `/home/user/outsystems-ddl-exporter/sidecar/projection/`.

---

## 1 Vocabulary inventory (with file anchors)

**The five `<Domain>Rules` strategies** (the registered-intervention sub-pattern,
DECISIONS 2026-05-11; registry: `src/Projection.Core/StrategyRegistrations.fs:132-138`):

| Strategy | Outcome DU | Positive evidence | Negative reason | Decision record | Grain |
|---|---|---|---|---|---|
| `NullabilityRules` | `NullabilityOutcome` = EnforceNotNull / KeepNullable / **RequireOperatorApproval** (`Strategies/NullabilityRules.fs:68-72`) | `NullabilityEvidence` 5 cases w/ counts (:8-27) | `KeepNullableReason` 3 cases (:31-42) | `NullabilityDecision` {AttributeKey, Outcome, InterventionId} (:78-82) | attribute |
| `UniqueIndexRules` | `UniqueIndexOutcome` = EnforceUnique / DoNotEnforce — **binary by V1 inheritance** (`Strategies/UniqueIndexRules.fs:49-64`) | `UniqueIndexEvidence` 3 cases (:6-16) | `UniqueIndexKeepReason` 5 cases incl. dead `EvidenceMissing` (:20-46) | `UniqueIndexDecision` (:71-75) | index |
| `ForeignKeyRules` | `ForeignKeyOutcome` = EnforceConstraint / DoNotEnforce (`Strategies/ForeignKeyRules.fs:98-101`) | `ForeignKeyEvidence` 5 cases incl. `DeclaredShapeCarried` abstain (:9-44) | `ForeignKeyKeepReason` 8 cases incl. 2 reserved-unreachable (:47-85) | `ForeignKeyDecision` (:109-113) | reference |
| `CategoricalUniquenessRules` | `CategoricalUniquenessOutcome` = SuggestUnique / DoNotSuggest (`Strategies/CategoricalUniquenessRules.fs:67-70`) | 1 case (:14-21) | 5 cases — the only strategy distinguishing `NoCategoricalEvidence` from `EvidenceMissing` (:28-53) | (:78-82) | attribute |
| `PhysicalClaimRules` | `PhysicalClaimOutcome` = Adopted / **Contested-always** / TombstoneOnly / Unclaimed (`Strategies/PhysicalClaimRules.fs:64-79`) + `CorrespondenceProposal` (:155-168), `proposeCorrespondence` total over outcomes (:176-191) | claim payloads carry `FirstWitnessedSync`, `EntityKey : SsKey option` (:33-52) | — | outcome per `ClaimSet` | physical table |

**Strategy plumbing:** `StrategyEvaluator<'context,'config,'decision>` alias + `Composition.FanOutConfig` +
`fanOut` / `fanOutWithDiagnostics` (`Strategies/Composition.fs:71-179`); observable identity on empty policy.
`CycleResolution` (`Strategies/CycleResolution.fs`): `EdgeStrength` (:43-46), `StrongCycleCertificate`
private-ctor refusal certificate (:77-99), `BreakObjective` w/ named greedy downgrade (:103-112),
`ResolutionReason` (:119-133), `Resolver` (:147-148), `minimalFeedbackStrategy` + `repairCostOf` (:433-534).
`ForeignKeyReadback.Classification` = Reconstructable / `Unreadable of reason: string` (`Strategies/ForeignKeyReadback.fs:32-34`).

**Config levers** (`Policy.fs`): `TighteningDirection` EvidenceDriven / RelaxationOnly (:371-385);
`OverrideAction.KeepNullable` singleton (:400-404) + `ForeignKeyOverrideAction.KeepUntracked` singleton (:499-505);
`NullabilityTighteningConfig` {NullBudget, AllowMandatoryRelaxation, Overrides, Direction} (:412-435);
`UniqueIndexTighteningConfig` {2 toggles, ApplyProfilePromotions} — **no Overrides, no Direction** (:442-461);
`ForeignKeyTighteningConfig` {EnableCreation, AllowCrossSchema, AllowNoCheckCreation, Overrides, Direction} (:516-547);
`TighteningIntervention` closed 4-variant DU w/ ids (:560-581).

**Decision projection:** `DecisionOverlay` {EnforceNotNull, KeepNullable (operator-only), EnforceUnique,
DropFk, NoCheckFk, RetargetFk} (`DecisionOverlay.fs:20-51`); `ofComposeState` (:131-139).
`ComposeState` holds the four tightening decision **sets** but only the bridge-retarget **map** (`ComposeState.fs:20-23,53`).

**Ruling/consent surfaces:** `ApprovalState` Pending/Approved(by,rationale)/Rejected + `ApprovalRecord`
(digest-anchored, `At : DateTimeOffset`) + `ApprovalRegistry.isSuppressed` (`ApprovalWorkflow.fs:17-41,145-190`);
`WriteSignoff.WriteApproval` (mode-class greenlight) vs `ActBlessing` (instance + fingerprint)
(`src/Projection.Pipeline/WriteSignoff.fs:62-104`); `ActConsent.Act` closed 8-act alphabet + `ActFingerprint`
Population/Effect — substrate change re-opens (`ActConsent.fs:33-55,117-124,209-221`);
`ApprovedDataCorrection` {Enabled, Guards, ExpectedCount, ApprovedBy/ApprovedAt : string option, SourceRemediationId}
(`ApprovedDataCorrections.fs:70-88`); `DataCorrectionReceipt` w/ ChangedRows/ExcludedRows enumerated,
EvidenceColumns + EvidenceDigest (`DataCorrectionReceipt.fs:194-224`); `reconcile` replay law
(`ApprovedDataCorrections.fs:483-505`). Estate levers: `EstateLane` Decide/Repair/Relax/Watch +
`EstateLeverForm` Ruling/ReviewBlock/MergeOverlayEntry/NoLever (`EstateFinding.fs:19-69`);
`Relaxation` {Scope=FindingKey, Action, Evidence, ReopenProbe} (`EstateFinding.fs:881-901`,
built `src/Projection.Pipeline/EstatePosture.fs:73-97`; active posture read back as bare key-sets :113-131).
`BridgeRetarget`: `BridgeCheck` 16-check closed taxonomy w/ intrinsic severity + verdict routing
(`BridgeRetarget.fs:60-217`), `BridgeReadiness`, `BridgeRetargetDecision` 3 independent verdicts (:375-392),
fail-closed `unproven` (:343-359), `decide` (:553-559). `ConflictDetector.PolicyConflict` (`ConflictDetector.fs:13-21`).
Audit plane: `AnnotationDetail` typed variants for Nullability/UniqueIndex/ForeignKey/CategoricalUniqueness/
PhysicalClaim decisions + `Label` escape hatch marked "production MUST use typed variants" (`Lineage.fs:123-150`).

## 2 The domain space (independent of current code)

The decision plane's domain is **the full grammar of a governed change**:
`typed trigger → evidence basis → recommendation/proposal → ruling → application → receipt → reopen condition`.

- **Subjects** (grains): attribute, index, reference, physical-table claim set, row set (correction),
  destructive act instance, policy version, retarget plan. Ruling grain is independently **class vs instance**
  (the domain needs both: "replace is approved" vs "this wipe at this population").
- **Epistemic axes** every decision must be able to state: (a) *basis kind* — declared (model), observed
  (profile/probe), derived (adjudication), operator-asserted; (b) *basis quality* — absent / attempted-but-
  unreliable / reliable, a genuine trichotomy since the remedies differ ("run the profiler" vs "your probe
  failed — find out why"); (c) *basis identity + age* — which snapshot, so a ruling given on Tuesday's
  evidence is re-openable when Thursday's differs (the sink chapter's `sink.evidenceAge` and the S8 freshness
  axis prove the house holds this value).
- **Teleological outcome families**, each a value: tighten/adopt · carry-declared (**abstain** — a real
  outcome whenever a direction gate or scope excludes a subject) · relax (operator-only) · lift-to-ruling
  (contest/conflict) · refuse-with-certificate. Recommendation ≠ adoption everywhere: evidence proposes,
  only a ruling applies (S14's law; the DECIDE lane's whole premise).
- **Rulings** are domain objects, not config incidents: who, when, on what basis (anchored), with what
  reopen condition; a rejected proposal is state (don't re-nudge); an adoption without a basis anchor is
  a rubber stamp waiting for drift.
- **Receipts** close the loop: what was applied, to exactly which subjects, reconciled against replay.
- **Unimplemented outcomes** stay named values with named triggers (CrossCatalogBlocked is the house form).

## 3 Findings

| ID | Class | Dimension | Reification axis | One-line claim | Anchor |
|---|---|---|---|---|---|
| A4-1 | M4 (+M1/M6 edges) | HIERARCHICAL | TELEOLOGICAL | The ruling/consent primitive is re-invented in ≥6 shapes; the tightening rungs (overrides, promotions) can't express who/when/on-what-evidence at all, and only 2 of 6 shapes carry drift-reopen anchors | `Policy.fs:391-394`, `WriteSignoff.fs:62-104`, `ApprovalWorkflow.fs:17-41`, `ApprovedDataCorrections.fs:87-88`, `EstatePosture.fs:113-131` |
| A4-2 | M4 (+M3) | SEMANTIC | TELEOLOGICAL | The ABSTAIN outcome (declared-shape-carried) has three spellings: a semantically false `KeepNullable` (nullability), an enforce-disguised `DeclaredShapeCarried` (FK), and `ApplyProfilePromotions=false` (unique index) — no shared carrier | `NullabilityRules.fs:245-249`, `ForeignKeyRules.fs:30-34,302-303,393-397`, `Policy.fs:442-461` |
| A4-3 | M6 (+M4,M5) | SEMANTIC | EPISTEMIC | The absent-vs-unreliable evidence trichotomy is reified in one strategy, collapsed in two, and `UniqueIndexKeepReason.EvidenceMissing` is dead vocabulary with live consumer arms | `UniqueIndexRules.fs:171-177,182-196,251-266` vs `CategoricalUniquenessRules.fs:216-226`; dead arms `UniqueIndexPass.fs:122-124`, `SummaryFormatter.fs:191` |
| A4-4 | M6 | STATE | EPISTEMIC | Advisory outcomes discard the evidence that fed them, and their adoption lever carries no basis anchor — an advised promotion adopted later stands on unexamined different data | `UniqueIndexRules.fs:240-242,46`, `UniqueIndexPass.fs:128-150` vs `ActConsent.fs:209-221`, `ApprovedDataCorrections.fs:357-364` |
| A4-5 | M1 (+M7) | RELATIONAL | TELEOLOGICAL | Per-subject ruling channels exist for attributes and references but not indexes: adopting ONE promotion while refusing another is inexpressible (class-wide boolean only) | `Policy.fs:412-435,516-547` vs `:442-461`; `UniqueIndexPass.fs:147-149` |
| A4-6 | M4 (+M6) | HIERARCHICAL | ONTIC | The bridge-retarget decision is the only decided axis riding the audit trail as prose `Label` — violating `AnnotationDetail`'s own production contract — and the only one whose decision set doesn't ride `ComposeState` | `BridgeRetargetPass.fs:46-48`, `Lineage.fs:143-150`, `ComposeState.fs:53` |
| A4-7 | M5 | STATE | TELEOLOGICAL | Proposals (`SuggestedConfig`) have no identity, so the reject ruling attaches only at whole-policy grain — HORIZON's per-key suppression is inexpressible (gap named in comments) | `SuggestConfigEmitter.fs:115-123`, `ApprovalWorkflow.fs:184-190` |
| A4-8 | M6 | SEMANTIC | EPISTEMIC | `ForeignKeyReadback` computes the four-way which-side-was-lost distinction, then erases it into prose inside a Core DU (`Unreadable of reason: string`) — cause/side aggregation forecloses | `ForeignKeyReadback.fs:32-34,56-70` |

### A4-1 — Six ruling dialects; the weakest rungs carry no provenance at all (M4; deepest)

**Evidence.** The "only a ruling applies" grammar the sink chapter canonized (S14; K9) is operated on
every decide-surface, but each surface minted its own ruling record:

1. `ApprovalState` — Pending/Approved/Rejected DU, `by: string` **required**, `rationale` option,
   `At: DateTimeOffset`, anchored to the policy **content digest** (`ApprovalWorkflow.fs:17-41,60-61`).
2. `WriteSignoff.WriteApproval` — class-grain greenlight; `ApprovedBy: string option`, `Date: string option`,
   `AcknowledgedImpact` (`WriteSignoff.fs:62-75`).
3. `WriteSignoff.ActBlessing` — instance-grain; adds the **fingerprint anchor**; substrate change re-opens
   (`WriteSignoff.fs:78-104`, `ActConsent.fs:117-124`).
4. `ApprovedDataCorrection` — `Enabled: bool` is the ruling switch; `ApprovedBy/ApprovedAt : string option`
   (a **string** timestamp vs #1's DateTimeOffset), no reject state, no rationale; drift protection via
   `ExpectedFindingCount`/`ExpectedCoverage` guards + receipt `EvidenceDigest` (`ApprovedDataCorrections.fs:70-88,357-368`).
5. `TighteningOverride` / `ForeignKeyOverride` — the estate posture's actual relaxation rulings — are
   **bare `(SsKey, Action)` pairs** (`Policy.fs:391-394,511-514`; binder `TighteningBinding.fs:78-90` parses
   only `{attributeRef, action}`). The proposal-side `Relaxation` carries `Scope` (FindingKey), `Evidence`
   (envs), and the `ReopenProbe` (`EstatePosture.fs:73-97`) — all three are **severed at merge**: the active
   posture is recomputed as bare key-sets (`EstatePosture.fs:113-131`). Who relaxed the column, when, on what
   evidence, under which finding — unrepresentable in the type that *is* the ruling.
6. `ApplyProfilePromotions` — a class-wide boolean adoption with no approver, no anchor, no reopen
   (`Policy.fs:442-461`).

**Misalignment.** Not that six surfaces exist (specialization is welcome) but that the ruling's
*invariant core* — subject anchor + basis anchor + by/at/rationale + reopen condition — is present,
partial, or absent per dialect with no principle deciding which. The house already reified the one real
axis of variation (class vs instance consent: `WriteApproval` vs `ActBlessing`), which proves the grammar
is known; rungs 5-6 simply never got it. M1 edge: "an audited relaxation" is inexpressible. M6 edge:
rung 5's evidence basis existed (typed, in `Relaxation`) and is dropped at the exact moment it becomes load-bearing.

**Candidate primitive.** `OperatorRuling<'anchor>` — `{ Subject: 'anchor; Basis: BasisAnchor option;
By: string; At: DateTimeOffset; Rationale: string option; Reopen: ReopenCondition option }` with
`BasisAnchor` = Digest | Fingerprint | FindingKey | EvidenceDigest — adopted rung-by-rung (config schema
gains optional `approvedBy/approvedAt/finding` on overrides first; the binder threads them; nothing behavioral moves).

**Outcome-fluency bought.** Every ruling answers who/when/why/on-what from its own value; rejected-state
becomes expressible off the approval registry's one dialect; reopen stops being an artifact-only contract.
**Effort** M (schema + binder + carriers; no algebra change). **Risk of inaction:** the estate posture —
the mechanism explicitly designed as *interim* with retirement conditions — is the one place the retirement
condition and the consent provenance are not in the record; post-eject (no upstream to re-derive from) the
question "why is this column nullable in the published shape" has no in-system answer.

### A4-2 — Abstention: one domain outcome, three spellings, one of them false (M4+M3)

**Evidence.** Under `Direction = RelaxationOnly`, `NullabilityRules.evaluate` returns
`KeepNullable NoTighteningSignal` for **every** non-overridden attribute — including primary keys and
physically-NOT-NULL columns (`NullabilityRules.fs:245-249`): the trail then records "the decision is to
keep nullable" about columns that stay NOT NULL (harmless to emission — the overlay reads only
EnforceNotNull/OperatorOverride sets, `DecisionOverlay.fs:68-92` — but epistemically false in the lineage,
which is the plane whose whole job is to not lie). `ForeignKeyRules` met the same need honestly-in-substance
but disguised in shape: abstention became an **evidence variant of the positive outcome**
(`EnforceConstraint DeclaredShapeCarried`, `ForeignKeyRules.fs:30-34,302-303`), so `enforces` answers
`true` for a decision that creates nothing (:393-397) and the emitters/overlay special-case it into the
identity path (`ForeignKeyPass.fs:205-209`, `DecisionOverlayTests.fs:512-519`). The unique-index axis
spells the same posture `ApplyProfilePromotions=false` + reasons, having no Direction at all (`Policy.fs:442-461`).

**Misalignment.** "The intervention states no opinion; the declared shape carries" is ONE teleological
outcome the domain owns (every direction gate, scope exclusion, or future axis will need it), currently
misplaced as an evidence modifier (M3) and aliased three ways (M4). PhysicalClaimRules shows the standard:
when the domain has four outcomes, the DU has four cases.

**Candidate primitive.** A per-strategy first-class abstain case — `NullabilityOutcome.DeclaredShapeCarried`
/ `ForeignKeyOutcome.DeclaredShapeCarried of reason: DirectionGate` — or a shared
`Decision<'evidence,'reason> = Apply of 'evidence | Decline of 'reason | CarryDeclared of gate` skeleton
if the house wants one grammar (second-consumer rule already satisfied).

**Outcome-fluency bought.** The trail stops asserting falsehoods; `enforces`-style predicates regain their
stated meaning; a future direction (e.g. `TightenOnly`) lands as data, not as a new disguise.
**Effort** M (outcome DUs are pattern-matched in overlay/passes/formatters; codec surfaces to check).
**Risk:** audit consumers (SummaryFormatter buckets, estate meters) silently mis-bucket abstentions as
active keep/enforce decisions; each new consumer re-learns the disguise or misreads it.

### A4-3 — Absent vs unreliable evidence: one trichotomy, three dialects, one dead token (M6+M4)

**Evidence.** The probe vocabulary distinguishes "no candidate was ever profiled" from "a probe ran and
came back unreliable (FallbackTimeout/Cancelled/AmbiguousMapping)". CategoricalUniqueness reifies both
(`NoCategoricalEvidence` vs `EvidenceMissing`, `CategoricalUniquenessRules.fs:216-226`). UniqueIndex
declares both (`UniqueIndexKeepReason.EvidenceMissing` :30-34, `NoCandidateProfiled` :35-38) and its
docstring promises both (:208-209), but `singleColumnProbe`/`compositeProbe` collapse unreliable to `None`
(`| _ -> None`, :171-177,182-196) so `evaluate` can only ever emit `NoCandidateProfiled` (:251,258,266) —
`EvidenceMissing` is **unproducible** while `UniqueIndexPass.fs:122-124` and
`Targets.OperationalDiagnostics/SummaryFormatter.fs:191` carry live arms and distinct operator copy for it,
and tests construct it directly (`DecisionOverlayTests.fs:105`). ForeignKey collapses the pair the *other*
way — both land on `EvidenceMissing` (`ForeignKeyRules.fs:340-346,379-386`).

**Misalignment.** M6: the epistemic fact "a probe was attempted and failed" is known at the collapse site
and erased. M4: three strategies map one domain distinction three different ways. M5: the doc-promised
function is partial over its declared outcome space. Dead vocabulary with live consumers is the precise
inverse of the house's `NotYetDetected` honesty pattern (a declared-unproduced value must be *declared* as such).

**Candidate primitive.** Return `(ProbeReading = NotProfiled | Unreliable of ProbeStatus | Reliable of 'r)`
from the probe helpers (or minimally: emit `EvidenceMissing` on the `Some unreliable` arm). One shared
reading type would also retire the FK-side fold.

**Outcome-fluency bought.** Operator advice separates "run the profiler" from "your probe failed — investigate";
the dead arm becomes truthful; cross-strategy evidence reasoning (SummaryFormatter buckets) stops depending
on which strategy produced the reason. **Effort** S. **Risk:** misdirected remediation (re-running a profiler
that will time out again); the vocabulary-first token quietly teaches consumers a state that never occurs.

### A4-4 — The advise-only rung drops its evidence at exactly the recommendation boundary (M6)

**Evidence.** `promoteOrAdvise (evidence: UniqueIndexEvidence)` uses the typed evidence when adopting and
**discards it** when advising: `DoNotEnforce PromotionAdvisedNotApplied` is a bare tag
(`UniqueIndexRules.fs:240-242`, case :39-46). The advisory finding therefore cannot state the probe basis
(`UniqueIndexPass.fs:128-150` renders generic copy; the `SuggestedConfig` adoption lever :141-150 carries
Path/Value/Note but no evidence anchor). Contrast the house's own drift discipline on sibling rungs:
a blessing binds to a substrate fingerprint and re-opens on change (`ActConsent.fs:209-221`); a correction
approval carries `ExpectedCount` and refuses on drift (`ApprovedDataCorrections.fs:357-364`); the S14
proposal carries both claims with native keys (`PhysicalClaimRules.fs:155-168`).

**Misalignment.** The recommendation-vs-adoption separation is present (good) but the *evidence basis of
the recommendation* is unreified, so the later adoption (`applyUniquePromotions: true`, possibly weeks
later) applies every candidate against whatever the *then-current* profile says, with no record of what
the operator actually reviewed. The decision type says "advised" without being able to say "on what".

**Candidate primitive.** `PromotionAdvisedNotApplied of evidence: UniqueIndexEvidence` (S); optionally a
basis anchor (profile fingerprint — the estate evidence fingerprint already exists, CLAUDE.md §4.15) on
the suggestion so adoption can name what it was reviewed against (M).

**Outcome-fluency bought.** The advisory finding states its counts; the adopted promotion is auditable
against the reviewed basis; the `no-more-no-less` receipt discipline extends to the one rung missing it.
**Effort** S(-M). **Risk:** rubber-stamp adoptions — the exact failure `ActFingerprint` was built to prevent —
on the schema-tightening plane.

### A4-5 — No per-index ruling channel; adoption is all-or-nothing (M1+M7)

**Evidence.** `NullabilityTighteningConfig.Overrides` (per-attribute) and `ForeignKeyTighteningConfig.Overrides`
(per-reference) give the operator a per-subject ruling; `UniqueIndexTighteningConfig` has neither Overrides
nor Direction (`Policy.fs:442-461` vs :412-435, :516-547). The suggested-config note says it plainly:
"Applying promotes EVERY such candidate on this intervention" (`UniqueIndexPass.fs:147-149`).
"Adopt this index's promotion, refuse that one" — a per-instance ruling every other axis can express —
is inexpressible per-index. The stated justification is V1 inheritance (`UniqueIndexRules.fs:49-56`,
`Policy.fs:438-441`), while the same docstring cites the law "V2 picks based on what serves the algebra,
not by inheritance from one source" (DECISIONS 2026-05-09).

**Misalignment.** M1 (an outcome the operator plausibly needs, unwritable), M7 (the asymmetry's only
justification is provenance, and the house's own precedent — the 2026-07-15 A6 amendment that *added*
Overrides+Direction to two of three configs — shows the channel is wanted; the third was skipped, not ruled out).

**Candidate primitive.** `UniqueIndexOverride = { IndexKey: SsKey; Action: ApplyPromotion | KeepUnenforced }`
+ `Overrides` list on the config, consulted at step 3 of `evaluate` (mirror of the nullability step-1 shape).

**Outcome-fluency bought.** Instance-grain adoption/refusal on the index axis; the estate posture becomes
expressible uniformly across all three tightening axes. **Effort** S-M. **Risk:** operators forced to choose
between adopting unreviewed candidates wholesale and declaring intended uniqueness in the model (which is not
always in their power on an OutSystems estate) — a real workflow foreclosure, though softened by the
model-declaration escape hatch.

### A4-6 — The fifth decided axis rides the trail as prose and ComposeState as a bare map (M4+M6)

**Evidence.** `AnnotationDetail` carries typed variants for all four tightening decision families and the
physical-claim adjudication (`Lineage.fs:123-142`), and its `Label` case is documented "Production pass
drivers MUST use one of the typed variants" (:143-150). `BridgeRetargetPass.outcomeEvent` — a production
pass driver — emits `Annotated (Label (evidenceNarration decision))` (`BridgeRetargetPass.fs:46-48`),
flattening the fully-typed `BridgeRetargetDecision` (check ledger + three verdicts, `BridgeRetarget.fs:384-392`)
into narration a consumer must parse — against the house's own "render via describe, never parse"
(`CycleResolution.fs:137-142`). Likewise `ComposeState` holds the four decision *sets* but only the retarget
*map* (`ComposeState.fs:20-23,53`): downstream of the pass, the retarget's evidence basis is severed from
the applied decision.

**Misalignment.** M4: the audit plane speaks typed for four families, prose for the fifth. M6: the check
ledger (which supplemental evidence cleared, which facts were missing) is knowledge the type already holds,
erased at the trail boundary. The `Label` escape clause ("whose typed annotation shape hasn't yet been
earned") and the MUST sentence are in tension; the earner — a real production consumer with a rich typed
payload — has plainly arrived.

**Candidate primitive.** `AnnotationDetail.BridgeRetargetOutcome of retargetId: string * decision: BridgeRetargetDecision`
(or a compact `retargetId * BridgeReadiness` if payload weight matters), narration kept as the rendering projection.

**Outcome-fluency bought.** Trail consumers count/filter blocked retargets and extract failed checks
structurally; the "annotate-don't-suppress" claim becomes machine-checkable. **Effort** S. **Risk:** the one
signoff-gated schema-rewiring decision in the system is the one whose audit record degrades to prose — the
place a post-incident review will look first.

### A4-7 — Proposals have no identity, so rejection can only be whole-policy (M5; named)

**Evidence.** `SuggestedConfig` = Path/Value/Note (`Diagnostics.fs`); the emitter's design note concedes
per-suggestion suppression "would require tagging each suggestion with the digest of the policy it would
produce, which the diagnostic producers do not model" (`SuggestConfigEmitter.fs:115-123`) — so
`ApprovalRegistry.isSuppressed` gates at the whole-policy-version digest (`ApprovalWorkflow.fs:184-190`),
while the HORIZON contract the module cites reads "SuggestedConfig is suppressed **for this key**"
(`ApprovalWorkflow.fs:132-141`).

**Misalignment.** M5: the ruling function is total over policy versions but the domain's ruling space is
per-proposal; rejecting one nudge while accepting another is inexpressible. Honestly named in comments —
which keeps it a partial dialect rather than a silent one — but the loop-closure claim (pieces 1+2 "shipped")
overstates the grain.

**Candidate primitive.** `ProposalKey` on `SuggestedConfig` (digest of Path+Value, or Path+intervention id);
registry keyed by `PolicyDigest * ProposalKey option`.

**Outcome-fluency bought.** Per-proposal accept/reject state; re-nudge suppression survives policy edits that
don't touch the rejected path. **Effort** S-M. **Risk:** low today (few suggestion kinds), grows linearly with
every new `SuggestedConfig` producer (three already: nullBudget, enableCreation, applyUniquePromotions).

### A4-8 — `Unreadable of reason: string` erases a computed classification (M6; smallest)

**Evidence.** `ForeignKeyReadback.classify` computes the four-way `which` distinction (both endpoints /
parent / referenced / other coordinate, `ForeignKeyReadback.fs:60-65`) and the two likely causes, then
interns them into prose inside a Core DU (`Unreadable of reason: string`, :32-34, :66-70). Recon #20 moved
the classifier *into* Core precisely because it is coordinate logic; its output stayed boundary-shaped.

**Misalignment.** M6: a consumer wanting "N FKs skipped for missing VIEW DEFINITION on schema X" or an
estate finding per unreadable side must parse prose. Contained (one small module, LINT-ALLOW-FILE'd,
tests witness it), but it is now Core vocabulary and the only strategy-window DU whose payload is a sentence.

**Candidate primitive.** `Unreadable of side: LostSide * visible: FkCoordinatesPartial` with the sentence
minted by a `describe` projection (the `ResolutionReason`/`describe` precedent in the same directory).

**Outcome-fluency bought.** Aggregation by cause/side; Voice-conformant copy ownership. **Effort** S.
**Risk:** low — until a second consumer wants the breakdown, at which point it's a parse job.

## 4 Anti-findings (correct specializations)

1. **Binary outcomes on UniqueIndex/FK/Categorical vs ternary Nullability are (mostly) justified.**
   Nullability's `RequireOperatorApproval` exists because NOT NULL has no deferred-validation escape:
   SQL cannot NOCHECK a column constraint, so the model-vs-data conflict must lift. FK resolves the same
   conflict *inside* the outcome space (`ScriptWithNoCheck` / `DataHasOrphans` under the `AllowNoCheckCreation`
   lever, `ForeignKeyRules.fs:349-359`) because WITH NOCHECK exists; declared-unique-with-duplicates is
   deliberately owned by the estate plane (`EstateFindingKind.DataUnique`, DECIDE lane) rather than the
   emission strategy, which trusts the source (`UniqueIndexRules.fs:6-10`). Different planes, each named.
   (The residue that is NOT justified is per-instance adoption — carved out as A4-5.)
2. **`DecisionOverlay` flattening decisions to `Set<SsKey>` is a correct projection, not M6.** The full
   decision sets ride `ComposeState` beside it; the overlay is the A18-safe emitter-facing view, and the
   operator-only `KeepNullable` filter ("evidence never loosens source truth", `DecisionOverlay.fs:26-31,79-92`)
   is a *justified* M7-looking asymmetry with its law stated on the type.
3. **Reserved-unreachable DU variants with named triggers** — `CrossCatalogBlocked` (IR catalog field),
   `DeleteRuleIgnored` (WP-1c), `Act.DeleteScope` (emission lane owns it) — plus the WP-1d **removal** of the
   inert config toggles that once pretended to consult them (`Policy.fs:523-530`) are exactly the
   "unimplemented outcomes as named values with named triggers" standard. Not foreclosure — its opposite.
4. **`ForeignKeyRules.evaluate` keeping `Catalog` as an extra argument** while conforming to
   `StrategyEvaluator` via lambda closure (`Composition.fs:54-63`) — "uniform shape, variable arity context"
   — is specialization done right; the alias names the grammar without Procrusteanizing signatures.
5. **`ConflictDetector`'s two-gate discipline** (only flag axis effects on Selection-removed keys,
   `ConflictDetector.fs:74-102`) correctly refuses to pathologize the success path; `axisOfCode` string-prefix
   routing is the code-namespace contract working, not stringly typing.
6. **`PhysicalClaimPass.nonTrivial`** (sole clean adoption annotates nothing, `PhysicalClaimPass.fs:32-38`)
   is a justified quiet-trail specialization: absence of an entry is itself the named healthy outcome.

## 5 Already-aligned (exemplary reifications)

- **`PhysicalClaimRules`** — the standard the rest should converge to: total ladder over every claim-set
  shape; Contested-always (no silent pick; the ordering *is* the recommendation, adoption is the operator's);
  epistemic honesty in the payload (`FirstWitnessedSync`, `EntityKey: SsKey option` key-basis);
  `proposeCorrespondence` total over outcomes, structurally unable to adopt (no catalog in, no SsKey out),
  with the ruling's future landing named (DerivationReason widens at the ruling — DECISIONS S14).
- **`CycleResolution`** — `StrongCycleCertificate` makes the refusal unforgeable (private ctor: closed cycle,
  zero Weak); `BreakObjective.GreedyAboveThreshold` names the downgrade with its numbers; `ResolutionReason`
  is the cashed 2026-07-07 deferral (string → DU at the second consumer); one `describe` owns the copy;
  `minimalFeedbackStrategy (cost)` is A40 operating (one family, zero-cost degeneration property-tested).
- **`ActConsent` + `WriteSignoff`** — the consent grammar fully reified once: closed act alphabet with
  severity order and per-act impact statements; class-grain `WriteApproval` vs instance-grain `ActBlessing`;
  fingerprints that re-open on substrate change; `actsOf` as THE derivation both the board and the execute
  gate read (blessed set ≡ performed set by construction).
- **`ApprovedDataCorrections` / `DataCorrectionReceipt`** — the complete decision grammar in one lane:
  estate proposes (`SourceRemediationId`) → operator approves (config) → guards refuse by name on drift →
  receipts enumerate exact rows ("no more, no less") with evidence columns + digest → `reconcile` makes the
  replay law executable. The two-class guard semantics (selector vs assertion, promoted by `ExpectedCoverage`)
  is a teleologically precise piece of vocabulary.
- **`Composition.fanOut` / `fanOutWithDiagnostics`** — the strategy driver earned at the fourth consumer,
  with observable-identity-on-empty-policy inherited rather than re-asserted; the five-candidate scorecard
  in the header (four deferred with zero consumers) is the emergent-primitive discipline in writing.
- **`BridgeRetarget`'s check taxonomy** — intrinsic (non-configurable) severity, three independent verdicts
  each aggregating only its informing checks, fail-closed `unproven` default, `BridgeKeyDeclaredUnique` as a
  reified data-vs-declared distinction that prevents the green-gate/red-deploy failure shape. (Its trail/
  ComposeState projection is A4-6; the kernel itself is exemplary.)
- **`NullabilityPass`/`ForeignKeyPass` diagnostic arms** — recommendations that carry their adoption lever
  (`SuggestedConfig` with the computed tightest budget; cardinality-gated enableCreation suggestion), and
  named accepted-divergences (`noCheckWithoutEvidence`, `tightenedWithinBudget`) honoring downgrades-never-silent.

**Drift noted in passing:** `StrategyRegistrations.fs:44` rationale still says "(Cautious / Aggressive /
Disabled)" — V2 collapsed TighteningMode 2026-05-09; `UniqueIndexRules.fs:208-209` docstring promises the
`EvidenceMissing` arm the code cannot produce (A4-3); `ApprovalWorkflow.fs:132-141` loop-closure claim
overstates suppression grain (A4-7).

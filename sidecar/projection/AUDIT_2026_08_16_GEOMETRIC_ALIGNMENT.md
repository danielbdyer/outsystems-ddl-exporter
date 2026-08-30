# AUDIT — Geometric alignment: each stage's vocabulary against its domain space

> **DISPOSITIONED 2026-08-30 — THE ALIGNMENT PROGRAM executed this audit in full.**
> Operator authorization 2026-08-16 ("spare no expense"); three chapters ran
> consecutively on PR #695 under the master plan, every candidate arc landed or was
> deferred by a NAMED trigger — nothing lapsed silently. The finding→slice map:
>
> - **Arcs 0+T (Chapter align-I, 11 slices)** — the fired `OverlayAxis` trigger resolved
>   (`Identity` appended; `axisOfPolicyAxis` total), identity-plane passes reclassified,
>   the `SynthesisConvention` registry + extension-marker lift, sequence-lane convergence,
>   `ChainStep.Requires/Produces` + skeleton honesty, typed `AnnotationDetail`,
>   `FiringSite`, the config-provenance rule (`Supplied<'T>` stays deferred — trigger:
>   the first mis-wiring the rule table catches).
> - **Arcs R+E (Chapter align-II, 14 slices)** — `OperatorRuling` carrier + keyed store,
>   tightening provenance, per-subject index rulings, abstain honesty (the `KeepNullable`
>   lie retired), FindingKey reception + the `rule` verb, `RowsetContract`, typed
>   `BundleErasure`, `AcquisitionScope`, journal read-side decode, typed fingerprint
>   readings, finding pedigree. Auto-application of rulings stays a named deferral.
> - **Arcs S+L+H+X (Chapter align-III, 24 slices)** — typed state (`SyncOrdinal`, typed
>   instants, `ChainAdmission`, `CanaryVerdict`, `EstateHistory.replay` FTC, the dead
>   twin deleted, `DataObservation`, rename isometry); law residency (A44 resident,
>   A45/T17/A47/A48 live, generator honesty, `TriggerProbes`, `L3-Eject` registered);
>   grain (`PhysicalTableRef`/`SchemaBasis`, `Environment.parse`, composite sink labels,
>   typed `TemporalConfig`); lexicon (`ForecastEvidence`, the two-register rule,
>   `WitnessedRow` + the Witness- freeze, `Place`, `RunIndex`) and the a11 voice
>   stage-2 sweep (the lazy plural is dead in src; the ratchet scans the full tree).
> - **Still deferred, each with its named trigger** (the Active-deferrals index owns
>   them): X6 statement-algebra rehome (second non-SSDT consumer);
>   `CanaryVerdict.Aborted` (first started-but-unconcluded canary); append-only ruling
>   HISTORY (first re-ruling consumer; `BasisAnchor.SinkEdition` widens then);
>   `SpineLedger` rename (if the ledger-noun overload still stings); the full
>   Sink→Witness B-family rename (L-effort, operator surface); the consent-surface
>   voice ruling (AWAITING OPERATOR — WriteSignoff/ActConsent measure zero today).
>
> The workpapers (a1–a11) remain the finding-level ground truth; `DECISIONS.md` carries
> every slice's entry. This banner is the disposition; the audit text below is frozen
> as conducted.

> **Conducted 2026-08-16** (operator commission, the session after the data-sink chapter
> closed). Ten parallel read-only auditors, one per stage-plane; their full workpapers are
> independent artifacts at `audits/alignment-2026-08-16/` (a1–a10) — this document is the
> synthesis and the decomposition, not a replacement for them. **Nothing in this audit is
> ruled.** Every finding is a candidate for operator adjudication; every proposed arc is a
> map, not a program. No code changed under this audit.

---

## 1 — The objective, perfected (the commissioning intent, restated)

Each pipeline stage has a **core vocabulary** — its types, DU cases, module names, codes,
keys — and a **domain space**: the full set of situations it must be able to express,
including outcomes never yet exercised. The objective is **alignment**: the vocabulary of
each stage should be a faithful map of its domain space along four dimensions —

- **relational** — how things reference, depend on, compose;
- **semantic** — what things mean and how they are named (one concept per name, one name
  per concept, at the right grain);
- **state** — how things change and how change is known (editions, displacements, ledgers,
  freshness, replay);
- **hierarchical** — how things contain and are contained (the grain tower: environment ⊃
  database ⊃ schema ⊃ module ⊃ kind ⊃ attribute);

and every primitive is judged on three reification axes —

- **ontic**: it names a real domain thing, exactly once, at one grain;
- **epistemic**: the type carries *how it is known* — observed vs derived vs declared vs
  assumed — never comments, strings, or convention;
- **teleological**: it names *what it is for* — whose intent, toward which outcome — so the
  outcome space is enumerable and each outcome is a **value, not an architecture**.

**Fluency is not feature-completeness.** A well-aligned stage can express every outcome in
its domain even when few are implemented; the unimplemented ones are named values with
named triggers (the deferral discipline), never structural impossibilities. Specialization
toward the operator's goals is welcome; **foreclosure** — structure that makes one outcome
the only expressible one — is the defect class. The hunt taxonomy: **M1** inexpressible
outcome, **M2** foreclosing assumption, **M3** misplaced concept, **M4** split/aliased
vocabulary, **M5** partial function over a total domain, **M6** unreified knowledge,
**M7** unjustified asymmetry. Anti-findings — apparent misalignments that are correct
specialization — were mandatory output, so the audit could not manufacture work.

## 2 — The verdict, in one paragraph

The codebase's **ontic and outcome vocabularies are strong and its foreclosure surface is
small and mostly conscious** — SQL-Server saturation is the domain, correctly specialized;
the torsor/RowDiff refusals, the schema-value/data-substrate asymmetry, and the
Notice/DiagnosticEntry distinctions all survived adversarial reading as *correct*
specializations. The systematic debt is **epistemic (M6 dominates every workpaper)**:
knowledge the system demonstrably has — erasure sets, evidence bases, finding pedigrees,
acquisition provenance, trigger conditions, chain preconditions, time and edition — is
carried in prose, strings, comments, and file conventions where the house's own precedents
carry it in types. The residual **teleological** debt concentrates in exactly two places: a
missing center-of-gravity primitive (the operator's **ruling**, independently found
re-invented in five-to-six shapes by two auditors) and one **frozen vocabulary**
(`OverlayAxis`, which stopped moving while the operator outcome space grew — with its
documented collapse trigger already silently fired). Two findings are
**correctness-adjacent today**, not merely stiffness (§4). The one-sentence diagnosis the
workpapers converge on: *the standard the sink chapter set exists and is proven in-house;
the misalignment is that it has not been back-ported to the seams the chapter did not
touch.*

## 3 — The convergence map (what multiple independent auditors hit)

Convergence across independently-scoped auditors is the audit's strongest signal.

| Theme | Independent witnesses | The shared substance |
|---|---|---|
| **The ruling has no carrier** | A4-1, A8-1 (+ S14's own copy) | The operator's ruling — the concept the consent architecture orbits — exists at four grades across five-plus surfaces; the sink/estate DECIDE lane demands confirm/reject that nothing can record; tightening rulings are bare `(SsKey, action)` with no approver, basis, or reopen condition. |
| **Identity honesty, one level up** | A2-1, A9-F1, A1-F5 | `Synthesized.source` is the un-closed sibling of `DerivationReason`: an unregistered convention vocabulary with live drift — three sequence identities across lanes (a silent-wrong-answer, §4), a thrice-open-coded extension marker. The sink fixed key-basis honesty at the claims grain; the core needs the same one level up. |
| **Epistemic carriage flattens at the seams** | A1-F1/F4/F6, A6-F1/F2, A7-1, A4-3/4/8 | The known-lossy `toBundle` is typed lossless while its sibling names every erasure; the journal's domain classification is write-only; finding pedigree mints into `Statement : string`; per-target erasure sets live in a test file's comments; evidence bases sever at merge. |
| **Time and edition are conventions** | A5-F6/F7, A2-4, A9-F5 | The sink edition ordinal is bare `int` in ≥11 files beside a sibling `Version` VO; instants ride raw strings with one decoder defaulting malformed time to `MinValue`; the environment grain is spelled three ways with a CLI-resident parse. |
| **The ledger contract worn as costume** | A5-F1..F4, A7-4, A9-F6 | `Ledger.fs`'s laws are proven on a toy while production grains vacate its arms (tautological `resumeAdmit`, uninstantiated episode grain); `RunLedger` — the R6 cutover gate's evidence — silently splices over torn lines and compares magic strings; `EstateHistory` holds partial sums with no replay. |
| **The law registry's jurisdiction gap** | A10 (F1–F8), A7-5 | The M16 machinery is exemplary but stops one stratum above the finest claims: A44 is a numbered law the registry cannot see; four LIVE laws are machine-indistinguishable from deferrals; triggers are 100% prose even where machine-evaluable; the eject — the outcome that "cannot be partially right" — has neither law nor stub nor deferral row. |
| **The frozen axis vocabulary** | A3-F1/F4/F6, A4-6 | The five-axis `OverlayAxis` DU cannot name the two youngest identity-plane interventions, which ride mislabeled as `Selection` (blinding `ConflictDetector`) or as prose `Label` trail events (invisible to applied/declined egress); `registered ⇔ executed` counts names, not substantive firings. |

## 4 — Correctness-adjacent now (flag first, regardless of arc order)

1. **Mislabeled identity-plane interventions blind conflict detection** — UserMatching and
   BridgeRetarget classified `Selection` against that axis's own contract; `ConflictDetector`
   keys on axes, so their conflicts are structurally invisible; the documented
   Policy↔OverlayAxis collapse trigger has already fired unnoticed (A3-F1; anchors in a3).
2. **Cross-lane sequence identity** — the same sequence mints three different SsKeys per
   reader lane, and sequences diff by SsKey: `diff sink:<env> live:<conn>` misreports every
   sequence as add+remove (A9-F1; both remedies exist in-house).
3. **Skeleton dependents over an empty topology** — the skeleton chain excludes
   `topologicalOrder` at pass grain while keeping its dependents, which silently compute
   over `TopologicalOrder.empty` — the zero-edge-analytics bug the full chain explicitly
   fixed, structurally reconstructed (A3-F2).

## 5 — The reified-primitive candidate map, decomposed into arcs

Each arc is INVEST-able, additive (no behavioral rewrites demanded), and named for the
axis it reifies. Per-candidate contracts, evidence, and effort classes live in the
workpapers; this is the map. **Order within an arc is the workpapers' leverage order;
order ACROSS arcs is the operator's call** (counsel: Arc 0 first; then R and T, whose
absence is actively costing expressiveness the estate's own copy already promises).

**Arc 0 — the correctness-adjacent trio** (S–M): the three §4 items — `OverlayAxis` gains
the identity channel(s) + the total `axisOfPolicyAxis` law; one `SynthesisConvention`
registry (the closed-`DerivationReason` shape) and one sequence identity; `Requires/Produces`
on `ChainStep` (assert-only form) so no chain assembly strands a dependent.

**Arc R — the Ruling** (M): one `OperatorRuling<'anchor>` carrier — subject, basis anchor
(digest | fingerprint | finding-key | evidence-digest), who, when, rationale, reopen
condition — adopted rung-by-rung where five-plus dialects live today; per-subject ruling
channels where whole-class booleans stand (unique-index promotions); first-class ABSTAIN
(`CarryDeclared`-family cases) replacing the three abstain dialects, one falsely spelled.
Discharges A4-1/2/5/7, A8-1; gives S14's correspondence copy a primitive that can receive
the ruling it demands.

**Arc E — epistemic carriage** (M): `toBundle : … -> RowsetBundle * BundleErasure list`
(the closed erasure DU beside the sibling that already does this); per-target A37 erasure
sets as values; the journal's read-side domain restored (`parseLine (renderLine l) = l`);
finding pedigree as a typed basis (evidence, age, firmness, fork witness) instead of
`Statement` prose; `AcquisitionScope = Total | Scoped of ScopeAxis list` carried on the
snapshot itself (the totality gate reads a type, not a value-equality); typed bellwether
readings (`FingerprintMoved` names the axis); the `RowsetContract` value driving the
26-rowset walk. Discharges A1-F1/2/4/6/7, A6-F1, A7-1, A4-3/4/8.

**Arc T — teleology carried to egress** (S–M): typed `AnnotationDetail` variants for the
two `Label`-riding decided axes (payload types exist) + a promotion trigger on `Label`'s
docstring; `FiringSite` on registry metadata (`InChain | AtSeam | OnSinkRead | Dormant of
trigger`) so registered⇔executed gains a per-site leg and dormant capability becomes
registry-visible; the config-provenance rule (`Supplied<'T> = SourceDerived | OperatorDeclared`)
making skeleton-purity sound against mis-wiring rather than sound-by-convention.
Discharges A3-F3/4/5/6, A4-6.

**Arc S — state made honest** (M): the `Ledger` contract instantiated for real (live
`resumeAdmit` arms, the episode grain's instance, `PrevSyncId` chain verification);
`RunLedger` fail-closed with a typed canary verdict (retiring `c = "green"`);
`EstateHistory` replay-from-records; the edition ordinal and instants as VOs (`SyncId`,
event-time vs record-time where the domain distinguishes); `DataObservation` separating
measured-zero from unmeasured. Discharges A5-F1..F8, A7-4/7, A9-F6.

**Arc L — the law registry's jurisdiction extended** (S–M): A44 resident in the registry;
the four Skip-prose LIVE laws flipped to gated witnesses; the matrix generator's grammar
made machine-honest (multiline-aware buckets, validated `@ladder`/axis tags, axis
self-declaration via tags); `TriggerProbes` evaluating the machine-evaluable deferral
subset; `L3-Eject` as at minimum a named Skip-with-trigger. Discharges A10-F1..F8, A7-5.

**Arc H — the grain tower's pinched ends** (M): `PhysicalTableRef` with
`SchemaBasis = Observed | Assumed` for claims/residue/correspondence (retiring fabricated
`dbo`); the environment grain unified (`Environment.parse` in Core; label→digest-SET so an
environment can be a composite of witnessed sources); `TemporalConfig` through `TableId`;
the database grain named when its documented trigger fires. Discharges A1-F3, A2-4/7/8.

**Arc X — seam-name hygiene** (S, blast-radius-managed): the `EvidenceCache` homonym, the
statement algebra's SSDT-named home, `Estate`-vs-`environments`, tri-modal "sink" —
A9's collision list with call-site counts is the worklist. Explicitly LAST: renames buy
fluency only after the vocabulary they name is aligned.

## 6 — Anti-findings honored (specializations the audit certifies)

SQL-Server/SSDT saturation is the domain, not a foreclosure (A6: "no dialect foreclosure
found"). The deliberate asymmetries stand: schema-value vs data-substrate fusion, the
torsor/RowDiff refusals, `Episode`/`CatalogSnapshot` vs `EpisodicLifecycle` as distinct
grains *where consumed* (the dead twin is a separate finding), Notice/DiagnosticEntry,
TableId/TableCoordinate, the nine-surface Verdict register, and the principled
fingerprint-vs-digest split. The audit proposes **no new features, no new stages, no
architectural rework** — every candidate is vocabulary, carriage, or law, in patterns the
codebase already operates.

## 7 — The standard's existing bearers (what the arcs back-port FROM)

`KeyBasis`, `WitnessOutcome`, the freshness `Miss` taxonomy, `PhysicalClaimRules`' ladder,
`DerivationReason`'s closure+codec, `ArtifactByKind` (T11 as a type theorem), typed
capability descent, `CachedProof` (falsifiers inside the artifact), the code⇔copy law, the
M16 citation gate, the one-`ChainStep`-list registry, `WriteSignoff`'s class/instance pair,
and `SinkSection`'s policy-not-flag shape. Every arc above is one of these applied one seam
short of where the domain now needs it — which is the audit's most encouraging result: the
codebase already knows how to do everything it is being asked to do.

## 8 — Where truth lives for this audit

The ten workpapers (`audits/alignment-2026-08-16/a1–a10`) carry the evidence, per-finding
contracts, and effort classes; this synthesis carries the convergence and the arc map; the
DECISIONS entry of this date records that the audit occurred and rules nothing. When a
workpaper and this synthesis disagree, the workpaper's evidence wins; when either disagrees
with the code, the code is the fact. Findings become work only through operator
adjudication — at which point each arc opens in the house rhythm: charter-grade framing,
same-commit laws, per-slice gates, close ritual.

## PERFORMANCE REVIEW SYSTEM

### Complete Unified Specification

-----

# PART I: SHARED LAYER

-----

## I. SHARED REFERENCE DATA

These artifacts are **single source of truth** for both systems. Neither system owns them; both read from them.

### competency_definitions.md

```
## Competencies (×7)

### C1: {Competency Name}
Definition: {official definition}
Archetype: {IC | Manager of ICs | Manager of Managers}

### C2: {Competency Name}
...
```

-----

### anchor_definitions.md

```
## Behavioral Anchors by Competency

### C1: {Competency Name}

#### Below Expectations
- A1.1: {anchor text}
- A1.2: {anchor text}
- A1.3: {anchor text}
- A1.4: {anchor text}

#### Meets Expectations
- A1.5: {anchor text}
- A1.6: {anchor text}
- A1.7: {anchor text}
- A1.8: {anchor text}
- A1.9: {anchor text}

#### Above Expectations
- A1.10: {anchor text}
- A1.11: {anchor text}
- A1.12: {anchor text}
- A1.13: {anchor text}
- A1.14: {anchor text}

### C2: {Competency Name}
...
```

-----

### objective_definitions.md

```
## Objectives (×3-4)

### O1: {Objective Name}
Description: {what success looks like}
Key Results: {measurable outcomes if defined}

### O2: {Objective Name}
...
```

-----

### level_topology.md

```
## Role Levels

### Associate Software Engineer II
- Scope: {typical scope}
- Expectations: {what "meets" looks like at this level}
- Promotion signal: {what "operating at next level" looks like}

### Software Engineer
- Scope: ...
- Expectations: ...
- Promotion signal: ...

### Senior Software Engineer
- Scope: ...
- Expectations: ...
- Promotion signal: ...

### Principal Software Engineer
- Scope: ...
- Expectations: ...
- Promotion signal: ...
```

-----

## II. SHARED SCHEMA: BASE EVIDENCE CLAIM

Both systems extend this base structure:

```
{
  id: string,                          // unique identifier
  
  // STAR decomposition (core to both)
  situation: string | null,            // context/setting
  task: string | null,                 // what was required
  action: string,                      // what was done (required)
  result: string | null,               // outcome/impact
  
  // Classification (core to both)
  competencies: string[],              // 0-7 competency IDs
  objectives: string[],                // 0-4 objective IDs
  valence: "positive" | "negative" | "neutral"
}
```

-----

## III. SHARED PRINCIPLES

### Contract Fidelity

Each prompt-to-prompt handoff has a schema contract. Structured intermediate representations. Prose only at final output.

### Grounding Anchors

Generation bounded by retrieval. Models select and arrange; they don’t fabricate. Every claim traceable to source.

### Artifact Persistence

Each stage produces a markdown artifact. Each artifact reviewed and approved before becoming input to next stage.

### Temperature Discipline

- Analytical work: 0.2-0.3 (boring, repeatable)
- Narrative work: 0.5-0.6 (needs life, still constrained)

-----

# PART II: MANAGER REVIEW SYSTEM

-----

## I. PURPOSE

**Posture:** Evaluation through substantiation
**Fitness function:** Accurate ratings, calibrated judgment, evidence-grounded coaching
**Failure modes to counter:** Visibility bias, anchor inflation, thin evidence, inconsistency across reports

-----

## II. EVIDENCE CLAIM SCHEMA (Manager)

Extends base schema with source attribution and anchor matching:

```
{
  // Base fields
  id: string,
  situation: string | null,
  task: string | null,
  action: string,
  result: string | null,
  competencies: string[],
  objectives: string[],
  valence: "positive" | "negative" | "neutral",
  
  // Manager-system specific
  source: "self" | "peer" | "manager" | "historical",
  raw_text: string,                    // original verbatim from source
  
  // Anchor matching (populated in P4)
  anchor_matches: [
    {
      competency: string,
      anchor_id: string,
      level: "below" | "meets" | "above",
      confidence: "high" | "medium" | "low"
    }
  ]
}
```

-----

## III. INPUT SOURCES

|Source              |Code|Structure                                |Contains                                                          |
|--------------------|----|-----------------------------------------|------------------------------------------------------------------|
|Self-assessment     |S   |Free-form against objectives/competencies|Employee’s claims about own performance                           |
|Peer feedback       |P   |3 buckets × 2-3 items                    |Effective behaviors, competency strengths, competency improvements|
|Manager observations|M   |Unstructured (1:1s, email, ADO)          |Your observations, outcomes, artifacts                            |
|Historical          |H   |Prior review + goals                     |Baseline, trajectory                                              |

-----

## IV. ARTIFACT CHAIN

```
┌─────────────────────────────────────────────────────────────────┐
│  M-A0: context_frame.md                                         │
│  Contents: imports shared definitions + style guide             │
│  Created: once                                                  │
│  Approval: light review                                         │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│  M-A1: {person}_raw_evidence.md                                 │
│  Contents: concatenated raw inputs (self, peer, manager notes)  │
│            organized by source                                  │
│  Created: per person (manual assembly)                          │
│  Approval: completeness check                                   │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ P3: Evidence Extractor
┌─────────────────────────────────────────────────────────────────┐
│  M-A2: {person}_claims.md                                       │
│  Contents: array of evidence claims                             │
│    - STAR-structured                                            │
│    - Source-attributed                                          │
│    - Valence assigned                                           │
│    - NO competency/anchor mapping yet                           │
│  Approval: scan for missed evidence, mis-parsed claims          │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ P4: Semantic Tagger
┌─────────────────────────────────────────────────────────────────┐
│  M-A3: {person}_tagged_claims.md                                │
│  Contents: claims enriched with                                 │
│    - Competency tags                                            │
│    - Objective tags                                             │
│    - Anchor matches with level and confidence                   │
│  Approval: spot-check tagging, watch for anchor inflation       │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ P5: Analyst
┌─────────────────────────────────────────────────────────────────┐
│  M-A4: {person}_analysis.md                                     │
│  Contents:                                                      │
│    - Per-competency analysis (×7)                               │
│    - Per-objective analysis (×3-4)                              │
│    - Perspective alignment matrix                               │
│    - Themes, tensions, gaps                                     │
│  Approval: ⛔ MAJOR STOP - verify ratings, author intent        │
└─────────────────────────────────────────────────────────────────┘
                                │
                                │ You create: {person}_intent.md
                                │
                                ▼ P6: Narrator
┌─────────────────────────────────────────────────────────────────┐
│  M-A5: {person}_draft_review.md                                 │
│  Contents:                                                      │
│    - Objective narratives (×3-4)                                │
│    - Competency narratives (×7)                                 │
│    - Overall summary                                            │
│    - Coaching invitations                                       │
│  Approval: ⛔ MAJOR STOP - bless, edit, finalize                │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ (manual editing)
┌─────────────────────────────────────────────────────────────────┐
│  M-A6: {person}_final_review.md                                 │
└─────────────────────────────────────────────────────────────────┘
```

-----

## V. PROMPT SPECIFICATIONS

### P1: Style Extractor

**Objective:** Extract operational style patterns from your review corpus
**Input:** 5-10 of your prior reviews (anonymized)
**Output:** Style guide section for M-A0

**Output structure:**

```
## Style Guide

### Structural Patterns
- Average paragraph length: {n} sentences
- Section ordering: {pattern}
- Transition style: {description}

### Opening Conventions
{how you typically open a review}

### Closing Conventions
{how you typically close}

### Evidence Citation Style
{inline vs. after claim, specificity level}

### Hedging Vocabulary
- Used: {phrases you use}
- Avoided: {phrases you don't use}

### Characteristic Phrasings
{Danny-isms}

### Coaching Language Patterns
{how you frame invitations, growth edges}
```

**Temperature:** 0.3
**Run:** Once

-----

### P2: Context Frame Assembler

**Objective:** Compress reference materials into reusable context
**Input:** Shared definitions (competencies, anchors, objectives, levels) + P1 style guide
**Output:** M-A0 context_frame.md
**Temperature:** 0.2
**Run:** Once
**Note:** May be partially manual curation

-----

### P3: Evidence Extractor

**Objective:** Parse raw evidence into STAR-structured claims with source attribution
**Input:** M-A0 (context) + M-A1 (raw evidence)
**Output:** M-A2 - array of claims

**Output structure per claim:**

```
### Claim {id}
**Source:** {self | peer | manager | historical}
**Raw Text:** "{verbatim from source}"

**STAR Decomposition:**
- Situation: {extracted or null}
- Task: {extracted or null}
- Action: {extracted}
- Result: {extracted or null}

**Valence:** {positive | negative | neutral}
```

**Temperature:** 0.2

**Validation criteria:**

- Each claim traceable to verbatim source text
- No merged claims that should be separate
- No STAR fields hallucinated beyond source

**Failure signals:**

- Missed evidence
- Claims without source attribution
- STAR fields invented rather than extracted

-----

### P4: Semantic Tagger

**Objective:** Classify claims against competencies/objectives; match to behavioral anchors
**Input:** M-A0 (context, including full anchor definitions) + M-A2 (claims)
**Output:** M-A3 - enriched claims

**Output structure per claim:**

```
### Claim {id}
{... all fields from M-A2 ...}

**Competency Tags:** [C1, C4, C7]
**Objective Tags:** [O2]

**Anchor Matches:**
| Competency | Anchor ID | Level | Confidence | Rationale |
|------------|-----------|-------|------------|-----------|
| C1 | A1.8 | meets | high | {brief justification} |
| C4 | A4.11 | above | medium | {brief justification} |
```

**Temperature:** 0.2

**Validation criteria:**

- Each tag/match has rationale
- No anchor matched without evidence support

**Failure signals:**

- Over-tagging (claim matched to 5+ competencies)
- Anchor inflation (Above anchor on thin evidence)
- Phantom competency attribution

-----

### P5: Analyst

**Objective:** Synthesize tagged claims into structured findings
**Input:** M-A0 (context) + M-A3 (tagged claims)
**Output:** M-A4 - structured analysis

**Output structure:**

```
# Analysis: {Person Name}

## Competency Assessments

### C1: {Competency Name}

#### Evidence Summary
| Claim ID | Source | Summary | Valence |
|----------|--------|---------|---------|
| 3 | peer | {summary} | + |
| 7 | self | {summary} | + |
| 12 | manager | {summary} | - |

#### Anchor Distribution
**Below:** {none | list of matched anchors}
**Meets:** A1.6, A1.8
**Above:** A1.11

#### Rating Inference
**Rating:** Meets
**Confidence:** High
**Evidence Density:** 5 claims

#### Gaps/Flags
{thin coverage areas, conflicting signals, notes}

---

### C2: {Competency Name}
{... same structure ...}

---

## Objective Assessments

### O1: {Objective Name}

#### Evidence Summary
| Claim ID | Source | Summary | Valence |
|----------|--------|---------|---------|
| ... | ... | ... | ... |

#### Rating Inference
**Rating:** Above
**Confidence:** High
**Evidence Density:** 4 claims

#### Gaps/Flags
{notes}

---

## Perspective Alignment Matrix

| Competency | Self View | Peer View | Manager View | Alignment |
|------------|-----------|-----------|--------------|-----------|
| C1 | + | + | neutral | converged |
| C2 | + | mixed | + | partial |
| C3 | silent | + | + | gap (self under-reports) |
| C4 | + | silent | - | diverged |
| ... | | | | |

## Themes

### Strengths
- {theme with supporting claim IDs}

### Growth Edges
- {theme with supporting claim IDs}

### Tensions
- {where evidence conflicts, with claim IDs}

### Notable Patterns
- {anything else worth surfacing}
```

**Temperature:** 0.3

**Validation criteria:**

- Every rating traces to anchor matches
- Gaps explicitly flagged
- Perspective matrix consistent with claim data

**Failure signals:**

- Rating without anchor support
- Themes not grounded in evidence
- Perspective matrix doesn’t match underlying claims

-----

### P6: Narrator

**Objective:** Generate review narrative from analysis + intent
**Input:** M-A0 (context + style guide) + M-A4 (analysis) + {person}_intent.md
**Output:** M-A5 - draft review

**Output structure:**

```
# Performance Review: {Person Name}
## Review Period: {dates}

## Objectives

### O1: {Objective Name}
**Rating:** {Above | Meets | Below}

{Narrative paragraph(s) - evidence-grounded, voice-matched to style guide}

**Forward Focus:** {coaching invitation}

---

### O2: {Objective Name}
{... same structure ...}

---

## Competencies

### C1: {Competency Name}
**Rating:** {Above | Meets | Below}

{Narrative paragraph(s)}

**Coaching Invitation:** {framed as invitation, shaped by intent}

---

### C2: {Competency Name}
{... same structure ...}

---

## Overall Summary

{Developmental arc narrative - where they started, where they are, where they're heading}

{Headline strengths}

{Key growth edges, framed constructively}

{Closing - tone matched to coaching posture}
```

**Temperature:** 0.5-0.6

**Constraints:**

- Voice matches P1 style guide
- Emphasis shaped by editorial intent
- Every claim traceable to M-A4 evidence
- Coaching invitations framed as invitations, not mandates

**Failure signals:**

- Voice drift from style guide
- Invented evidence
- Intent not reflected in emphasis/tone
- Coaching framed as directive rather than invitation

-----

## VI. MANAGER INTENT INPUT

**{person}_intent.md:**

```
## Person: {name}
## Level: {current level}
## Program: {program}

### Stream of Consciousness
{your unfiltered thoughts on this person's year - growth, struggles, 
trajectory, what you want them to hear, what you're proud of, 
what concerns you}

### Editorial Intent
- {what you want this review to accomplish}
- {key message to land}

### Coaching Posture
{stretch | consolidate | redirect | affirm}

### Arc
{emerging | ascending | plateaued | recovering}

### Emphasis
- Foreground: {competencies/themes to highlight}
- Background: {competencies to mention but not dwell on}

### Softening
- {areas where indirection is appropriate}

### Overrides
- {where you disagree with P5 analysis and why}
```

-----

## VII. OPTIONAL: CALIBRATION SWEEP (P7)

**Objective:** Cross-person consistency check
**Input:** All M-A4 analysis files (×11)
**Output:** calibration_report.md

**Output structure:**

```
# Calibration Report

## Rating Distribution by Level

| Level | Person | C1 | C2 | C3 | C4 | C5 | C6 | C7 | O1 | O2 | O3 |
|-------|--------|----|----|----|----|----|----|----|----|----|----|
| Sr | Alex | M | A | M | M | A | M | M | A | M | M |
| Sr | Jordan | A | A | M | M | M | A | M | M | A | M |
| ... | ... | | | | | | | | | | |

## Potential Miscalibrations
- {person} rated Above on {competency} with {n} claims; {other person} rated Meets with {m} claims - verify consistency
- ...

## Visibility Bias Flags
- {person} has thin evidence ({n} total claims) - may be under-observed
- ...

## Cross-Program Patterns
- Program A averaging higher on {competency} - real difference or calibration drift?
- ...
```

**Temperature:** 0.3
**Run:** Once, after all M-A4s complete

-----

## VIII. FAILURE RECOVERY

|Stage|Failure Signal          |Recovery                                                    |
|-----|------------------------|------------------------------------------------------------|
|P3   |Missed evidence         |Re-run with explicit pointer to missed content              |
|P3   |Hallucinated STAR fields|Edit M-A2 manually, proceed                                 |
|P4   |Over-tagging            |Edit M-A3 to remove spurious tags                           |
|P4   |Anchor inflation        |Edit M-A3 to downgrade match level                          |
|P5   |Unsupported rating      |Check M-A3 for tagging error; if correct, override in intent|
|P5   |Perspective matrix wrong|Verify against M-A3 claims, correct manually                |
|P6   |Voice drift             |Re-run with stronger style guide emphasis or edit manually  |
|P6   |Intent not reflected    |Re-run with more explicit intent or edit manually           |

-----

## IX. EXECUTION PROTOCOL

**Setup (once):**

1. Assemble shared reference data
1. Run P1 → produce style guide
1. Run P2 / curate → produce M-A0

**Per person (×11, parallel):**

1. Assemble M-A1 (raw evidence)
1. Run P3 → produce M-A2 → review/approve
1. Run P4 → produce M-A3 → review/approve
1. Run P5 → produce M-A4 → ⛔ **major review**
1. Author {person}_intent.md
1. Run P6 → produce M-A5 → ⛔ **major review**
1. Edit → produce M-A6 (final)

**Post-completion (optional):**
8. Run P7 → produce calibration_report.md → adjust as needed

-----

# PART III: SELF-REVIEW SYSTEM

-----

## I. PURPOSE

**Posture:** Advocacy through substantiation
**Fitness function:** Complete excavation, impact articulation, strategic visibility, confident claim-making
**Failure modes to counter:** Minimization, invisible labor, underselling, attribution diffusion, recency bias

-----

## II. EVIDENCE CLAIM SCHEMA (Self)

Extends base schema with impact framing and visibility tracking:

```
{
  // Base fields
  id: string,
  situation: string,
  task: string,
  action: string,
  result: string,
  competencies: string[],
  objectives: string[],
  valence: "positive" | "negative" | "neutral",
  
  // Self-system specific
  impact_level: "team" | "program" | "org" | "enterprise",
  visibility: "high" | "medium" | "low",
  evidence_type: "artifact" | "outcome" | "feedback" | "recollection",
  citation: string | null,             // link, doc name, ADO item, email thread
  significance: string                 // why this mattered, one sentence
}
```

-----

## III. BIAS COUNTERMEASURES

|Bias               |Failure Mode                         |Counter                           |
|-------------------|-------------------------------------|----------------------------------|
|Recency            |Q3-Q4 dominates                      |Temporal scaffolding in S1        |
|Visibility         |Remember visible, forget quiet impact|Explicit low-visibility prompt    |
|Minimization       |“I just did my job”                  |Impact framing, “so what” required|
|Attribution        |“We delivered” erasing “I did”       |Specific “what did YOU do” prompts|
|Activity vs. Impact|Listing tasks not outcomes           |STAR enforcement, result required |

-----

## IV. ARTIFACT CHAIN

```
┌─────────────────────────────────────────────────────────────────┐
│  S-A0: context_frame.md                                         │
│  Contents: imports shared definitions                           │
│            + level expectations for your role                   │
│  Created: once                                                  │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│  S-A1: evidence_sources.md                                      │
│  Contents: raw material organized by quarter                    │
│    - Q1: memories, artifacts, ADO, emails                       │
│    - Q2: ...                                                    │
│    - Q3: ...                                                    │
│    - Q4: ...                                                    │
│    - Ongoing/cross-cutting                                      │
│  Created: manual assembly with excavation prompts               │
│  Approval: completeness - is the year represented?              │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ PS1: Evidence Excavator
┌─────────────────────────────────────────────────────────────────┐
│  S-A2: raw_claims.md                                            │
│  Contents: array of claims                                      │
│    - STAR-structured                                            │
│    - Impact-framed                                              │
│    - Visibility-tagged                                          │
│    - Citations and significance                                 │
│  Approval: check for gaps, underselling, missing "so what"      │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ PS2: Claim Tagger
┌─────────────────────────────────────────────────────────────────┐
│  S-A3: tagged_claims.md                                         │
│  Contents: claims mapped to competencies and objectives         │
│  Approval: check coverage - any competency/objective orphaned?  │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ PS3: Coverage Analyzer
┌─────────────────────────────────────────────────────────────────┐
│  S-A4: coverage_analysis.md                                     │
│  Contents:                                                      │
│    - Per-objective coverage                                     │
│    - Per-competency coverage                                    │
│    - Gaps and thin areas                                        │
│    - Visibility map                                             │
│    - Impact distribution                                        │
│    - Surfacing recommendations                                  │
│  Approval: ⛔ MAJOR STOP - decide what to surface               │
└─────────────────────────────────────────────────────────────────┘
                                │
                                │ You create: rhetorical_intent.md
                                │
                                ▼ PS4: Narrator
┌─────────────────────────────────────────────────────────────────┐
│  S-A5: draft_self_review.md                                     │
│  Contents:                                                      │
│    - Objective narratives (×3-4)                                │
│    - Competency narratives (×7)                                 │
│    - Growth edges (owned development)                           │
│    - Overall narrative (your year's arc)                        │
│  Approval: ⛔ MAJOR STOP - voice, claim strength, tone          │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼ (manual editing)
┌─────────────────────────────────────────────────────────────────┐
│  S-A6: final_self_review.md                                     │
└─────────────────────────────────────────────────────────────────┘
```

-----

## V. PRE-SYSTEM: EXCAVATION PROMPTS

Before entering the system, use these prompts to populate S-A1:

### Temporal Sweep

```
Q1 (Jan-Mar):
- What shipped?
- What started?
- What was hard?
- What did I specifically do?

Q2 (Apr-Jun):
- What shipped?
- What shifted?
- What did I learn?
- What did I specifically do?

Q3 (Jul-Sep):
- What shipped?
- What scaled?
- What broke and I fixed?
- What did I specifically do?

Q4 (Oct-Dec):
- What shipped?
- What's in flight?
- What am I proud of?
- What did I specifically do?
```

### Visibility Audit

```
- What did I do that nobody saw?
- What did I do that only my team saw?
- What did I do that my skip-level doesn't know about?
- What did I prevent from happening?
```

### Impact Reframe

```
- What would have happened if I wasn't there?
- What decisions did I make that shaped outcomes?
- What did I unblock for others?
- What did I protect the team from?
```

### Attribution Recovery

```
- When I say "we," what did I do?
- What did I architect?
- What did I decide?
- What did I coach others through?
```

-----

## VI. PROMPT SPECIFICATIONS

### PS1: Evidence Excavator

**Objective:** Transform raw evidence sources into STAR-structured claims with impact framing
**Input:** S-A0 (context) + S-A1 (evidence sources)
**Output:** S-A2 - array of claims

**Output structure per claim:**

```
### Claim {id}

**STAR:**
- Situation: {context, stakes, constraints}
- Task: {what was required}
- Action: {what I specifically did}
- Result: {outcome, impact, delta}

**Impact Level:** {team | program | org | enterprise}
**Visibility:** {high | medium | low}
**Evidence Type:** {artifact | outcome | feedback | recollection}
**Citation:** {link/reference or "recollection"}

**Significance:** {why this mattered, one sentence}
```

**Temperature:** 0.3

**Key instructions:**

- Enforce temporal coverage (flag if any quarter underrepresented)
- For each claim: “What did Danny *specifically* do?”
- Require result - no claim without outcome
- Require significance - no claim without “so what”
- Flag low-visibility/high-impact work explicitly
- Mark evidence type honestly

**Failure signals:**

- Claims without results
- “We” language without “I” specificity
- Missing quarters
- No low-visibility work surfaced
- Significance missing or generic

-----

### PS2: Claim Tagger

**Objective:** Map claims to competencies and objectives
**Input:** S-A0 (context) + S-A2 (raw claims)
**Output:** S-A3 - tagged claims

**Output structure per claim:**

```
### Claim {id}
{... all fields from S-A2 ...}

**Competency Tags:** [C2, C5]
**Objective Tags:** [O1, O3]
**Tag Rationale:** {brief}
```

**Temperature:** 0.2

**Key instructions:**

- Allow multiple competency tags where true
- Flag claims that don’t map to any objective (still valuable)
- Don’t force-fit

**Failure signals:**

- Under-tagging (missing valid mappings)
- Force-fitting to objectives
- Tagging without rationale

-----

### PS3: Coverage Analyzer

**Objective:** Assess evidence distribution and identify gaps
**Input:** S-A0 (context) + S-A3 (tagged claims)
**Output:** S-A4 - coverage analysis

**Output structure:**

```
# Coverage Analysis

## Objective Coverage

### O1: {Objective Name}

**Evidence Density:** {count} claims - {strong | adequate | thin}

**Strongest Claims:**
1. Claim {id}: {summary} - {impact level} - {citation}
2. Claim {id}: {summary} - {impact level} - {citation}

**Gaps/Concerns:** {what's missing}

---

### O2: {Objective Name}
{... same structure ...}

---

## Competency Coverage

### C1: {Competency Name}

**Evidence Density:** {count} claims - {strong | adequate | thin}

**Strongest Claims:**
1. Claim {id}: {summary}
2. Claim {id}: {summary}

**Gaps/Concerns:** {what's missing}

---

## Visibility Map

| Claim ID | Summary | Visibility | Impact | Surfacing Risk |
|----------|---------|------------|--------|----------------|
| 3 | SSDT migration architecture | low | org | underseen |
| 7 | Sprint facilitation | high | team | adequate |
| 12 | Contractor mentoring | low | program | underseen |

## Impact Distribution

| Impact Level | Count | % of Claims |
|--------------|-------|-------------|
| Team | X | X% |
| Program | X | X% |
| Org | X | X% |
| Enterprise | X | X% |

## Surfacing Recommendations

**High-impact/low-visibility to foreground:**
- Claim {id}: {summary} - {why this deserves visibility}

**Implicit work to make explicit:**
- {patterns of work that aren't captured in discrete claims}

## Coverage Gaps

**Thin competencies:** {list with notes}
**Thin objectives:** {list with notes}
**Temporal gaps:** {any quarter underrepresented}
```

**Temperature:** 0.3

-----

### PS4: Narrator

**Objective:** Generate self-review narrative from analysis + rhetorical intent
**Input:** S-A0 (context) + S-A4 (coverage analysis) + rhetorical_intent.md
**Output:** S-A5 - draft self-review

**Output structure:**

```
# Self-Review: {Your Name}
## Review Period: {dates}

## Objectives

### O1: {Objective Name}

{Narrative paragraph(s) - impact-led, evidence-grounded, specific}

**Key Contributions:**
- {claim summary with citation}
- {claim summary with citation}

---

### O2: {Objective Name}
{... same structure ...}

---

## Competencies

### C1: {Competency Name}

{Narrative paragraph(s) - confident, specific, impact-framed}

---

### C2: {Competency Name}
{... same structure ...}

---

## Growth Edges

{Areas of active development - framed as investment, not deficit}

- {growth area}: {what you're doing about it, what progress looks like}

---

## Overall

{The arc of your year - where you started, what you accomplished, 
where you're heading}

{Your headline contribution}

{What you're proud of}

{What you're building toward}
```

**Temperature:** 0.5

**Key instructions:**

- Voice: confident but not arrogant, specific not vague
- Lead with impact, follow with how
- Growth edges as active development, not deficits
- Evidence cited naturally, not defensively
- The arc: what story does this year tell?

**Failure signals:**

- Hedging, qualifying, underselling
- Listing without impact
- Growth edges framed as failures
- Generic claims without specifics
- “We” without “I”

-----

## VII. RHETORICAL INTENT INPUT

**rhetorical_intent.md:**

```
## Role: {your title}
## Level: {current level}
## Manager: {who reads this}

### What I Want Seen
- {work that might be invisible}
- {impact that might be underestimated}
- {growth that might not be obvious}

### The Story of My Year
{one paragraph: what arc should this tell?}

### Strategic Emphasis
- Foreground: {objectives/competencies where strongest}
- Adequate coverage: {areas to mention but not belabor}
- Growth edges to own: {where actively developing}

### Tone Calibration
{confident | measured | assertive}

### Promo Relevance
{building a case? what does "operating at next level" look like for you?}

### What I Don't Want
- {underselling patterns to avoid}
- {topics to not overexplain}
```

-----

## VIII. DANNY-SPECIFIC TRAPS

|Trap                  |What It Looks Like                                             |Counter                       |
|----------------------|---------------------------------------------------------------|------------------------------|
|Minimization          |“I just facilitated…”                                          |Reframe: what was the outcome?|
|Team attribution      |“The team delivered…”                                          |Ask: what did YOU do?         |
|Invisible labor       |Not mentioning architecture thinking, coaching, risk mitigation|Explicit unseen work prompt   |
|Complexity as proof   |Over-explaining HOW                                            |Impact-first structure        |
|Growth edges as wounds|Framing development as failure                                 |Active investment framing     |

-----

## IX. FAILURE RECOVERY

|Stage|Failure Signal            |Recovery                                               |
|-----|--------------------------|-------------------------------------------------------|
|PS1  |Thin quarter              |Go back to S-A1, excavate that quarter more            |
|PS1  |No low-visibility work    |Run visibility audit prompts again                     |
|PS1  |“We” language             |Edit to specify your contribution                      |
|PS2  |Orphaned competency       |Check S-A2 for unclaimed evidence or note true gap     |
|PS3  |Underseen work not flagged|Edit recommendations manually                          |
|PS4  |Underselling              |Re-run with stronger rhetorical intent or edit manually|
|PS4  |Generic claims            |Edit to add specifics from S-A3                        |

-----

## X. EXECUTION PROTOCOL

**Setup (once):**

1. Assemble shared reference data (or reuse from manager system)
1. Curate S-A0

**Excavation:**

1. Run excavation prompts (temporal, visibility, impact, attribution)
1. Assemble S-A1 from outputs

**Processing:**
3. Run PS1 → produce S-A2 → review/approve
4. Run PS2 → produce S-A3 → review/approve
5. Run PS3 → produce S-A4 → ⛔ **major review**
6. Author rhetorical_intent.md
7. Run PS4 → produce S-A5 → ⛔ **major review**
8. Edit → produce S-A6 (final)

-----

# PART IV: FILE STRUCTURE

```
/review_system/

  /shared/
    competency_definitions.md
    anchor_definitions.md
    objective_definitions.md
    level_topology.md
    star_schema.md
    
  /manager_review/
    context_frame.md                  # M-A0
    style_guide.md                    # Output of P1
    
    /prompts/
      p1_style_extractor.md
      p2_context_assembler.md
      p3_evidence_extractor.md
      p4_semantic_tagger.md
      p5_analyst.md
      p6_narrator.md
      p7_calibration.md               # Optional
      
    /reviews/
      /person_1/
        raw_evidence.md               # M-A1
        claims.md                     # M-A2
        tagged_claims.md              # M-A3
        analysis.md                   # M-A4
        intent.md                     # Your input
        draft_review.md               # M-A5
        final_review.md               # M-A6
        
      /person_2/
        ...
        
      /person_11/
        ...
        
    calibration_report.md             # Optional P7 output
    
  /self_review/
    context_frame.md                  # S-A0
    
    /prompts/
      ps1_evidence_excavator.md
      ps2_claim_tagger.md
      ps3_coverage_analyzer.md
      ps4_narrator.md
      
    /artifacts/
      evidence_sources.md             # S-A1
      raw_claims.md                   # S-A2
      tagged_claims.md                # S-A3
      coverage_analysis.md            # S-A4
      rhetorical_intent.md            # Your input
      draft_self_review.md            # S-A5
      final_self_review.md            # S-A6
```

-----

# PART V: SYSTEM COMPARISON

|Dimension             |Manager Review            |Self-Review               |
|----------------------|--------------------------|--------------------------|
|**Posture**           |Evaluation                |Advocacy                  |
|**Rating**            |Inferred from anchors     |Not your job              |
|**Evidence direction**|Given to you              |Excavated by you          |
|**Anchor matching**   |Required                  |Not required              |
|**Perspective matrix**|S/P/M triangulation       |N/A                       |
|**Key risk**          |Visibility bias, inflation|Minimization, invisibility|
|**Growth edges**      |Coaching invitations      |Owned development         |
|**Intent input**      |Editorial intent          |Rhetorical intent         |
|**Prompts**           |P1-P7                     |PS1-PS4                   |
|**Artifacts**         |M-A0 through M-A6         |S-A0 through S-A6         |

-----

This is the complete specification.​​​​​​​​​​​​​​​​
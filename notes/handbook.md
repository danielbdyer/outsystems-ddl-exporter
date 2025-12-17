# SSDT Playbook: Table of Contents

## I. ORIENTATION

### 1. Start Here
*Status: Drafted above — needs real environment details*

**1.1 What This Is**
- Purpose statement
- What it is and isn't

**1.2 Who It's For**
- Audience matrix (already drafted)

**1.3 How to Use It**
- Reading paths by role (already drafted)
- *NEW: Progressive disclosure note — "Layer 1/2/3" explained*

**1.4 Your First Week**
- Checklist (already drafted)
- *NEW: Links to Graduation Path (teaser)*

**1.5 Getting Help**
- Channels, escalation (already drafted)

**1.6 Quick Glossary**
- Terms you'll see immediately (already drafted)
- *REFINE: Add OutSystems equivalents inline* — e.g., "Entity (OutSystems) = Table (SQL Server)"

---

### 2. The Big Picture
*Status: Drafted above — needs Integration Studio details*

**2.1 Why We're Here**
- Migration context (already drafted)
- Benefits and costs (already drafted)

**2.2 How Database Changes Flow**
- Flowchart (already drafted)

**2.3 Where SSDT Fits with OutSystems**
- Architecture diagram (already drafted)
- *NEW: Explicit handoff points*

**2.4 The CDC Constraint**
- Summary of implications (already drafted)
- *NEW: Link to CDC table registry*

**2.5 What Success Looks Like**
- Success criteria by stakeholder (already drafted)

---

### 3. The Translation Layer *(NEW SECTION)*
*Status: Not written — HIGH PRIORITY*

This is where the Rosetta Stone, "I Used To / Now I", and "Feels Like / Actually Is" refinements live. It's substantial enough to be its own Orientation section rather than buried in Foundations.

**3.1 OutSystems → SSDT Rosetta Stone**
- Term-by-term mapping table
- Conceptual equivalences
- Where the metaphor breaks down

**3.2 "I Used To... Now I..."**
- Action-by-action translation
- The quick reference cheat sheet

**3.3 Risk Recalibration: "Feels Like / Actually Is"**
- What OutSystems abstracted that you now see
- Danger rerating table
- The mental model shift

**3.4 The Integration Studio Bridge**
- The literal boundary between worlds
- Refresh workflow with screenshots
- Common Integration Studio errors
- When refresh shows unexpected changes

---

## II. FOUNDATIONS

### 4. State-Based Modeling vs. Imperative Migrations *(was 3)*
*Status: Not written — needs drafting*

**4.1 The Mental Model Shift**
- "You declare end state, not transitions"
- *Worked example: What happens when you edit a .sql file*

**4.2 How SSDT Computes the Delta**
- The comparison engine
- What it sees vs. what you intended

**4.3 When the Abstraction Leaks**
- Cases where you must care about generated SQL
- *Link to: Why we review generated scripts*

---

### 5. Anatomy of an SSDT Project *(was 4)*
*Status: Not written — needs drafting*

**5.1 File Structure and Naming**
- Standard layout
- *Worked example: Tour of our actual project*

**5.2 What Lives Where**
- Tables, views, procs, scripts
- The organizational logic

**5.3 The .sqlproj File**
- What it controls
- Key settings

**5.4 Database References**
- Same server
- Cross-database / linked server
- *Gotcha: What happens without them*

**5.5 Build vs. Deploy**
- What happens when (lifecycle)

---

### 6. Pre-Deployment and Post-Deployment Scripts *(was 5)*
*Status: Discussed in thread — needs structuring*

**6.1 When Declarative Isn't Enough**
- The boundary of SSDT's model

**6.2 Pre-Deployment Scripts**
- Purpose: prepare target for schema changes
- When to use
- *Worked example: Backfilling NULLs before NOT NULL constraint*

**6.3 Post-Deployment Scripts**
- Purpose: data migrations, seeding, fixups
- When to use
- *Worked example: Populating lookup table*

**6.4 The Hybrid Approach**
- Permanent vs. transient scripts
- Folder structure: `/Migrations`, `/ReferenceData`, `/OneTime`
- The master script and SQLCMD `:r`

**6.5 Idempotency Principles**
- *Cross-reference to Section 7*

---

### 7. Idempotency 101 *(was 6)*
*Status: Discussed in thread — needs structuring*

**7.1 Why Idempotency Matters**
- Scripts that live forever
- Fresh environment deployments

**7.2 Core Patterns**
- `IF EXISTS` / `IF NOT EXISTS`
- Tracking tables
- *Code templates for each*

**7.3 The Hard Cases**
- One-time corrections
- Data transformations
- *Worked example: Migration tracking table*

**7.4 Testing Idempotency**
- "Can you run this twice safely?"
- Verification approach

---

### 8. Referential Integrity Basics *(was 7)*
*Status: Mentioned — needs drafting*

**8.1 What Foreign Keys Actually Enforce**
- Constraints at insert/update/delete time

**8.2 The Dependency Graph**
- Why order matters for operations
- *Diagram: Example FK web*

**8.3 `WITH NOCHECK` and Trust**
- What the optimizer sees
- Why untrusted constraints are technical debt

**8.4 Orphan Data**
- Finding it (query patterns)
- Fixing it
- Preventing it

---

### 9. The Refactorlog and Rename Discipline *(was 8)*
*Status: Drafted in thread — needs consolidation*

**9.1 What the Refactorlog Is**
- XML tracking of identity-preserving changes

**9.2 The Silent Catastrophe**
- *Anti-pattern: The Naked Rename*
- What happens without refactorlog
- *Worked example: Before/after of SSDT-generated script*

**9.3 How to Rename Correctly**
- GUI approach (preferred)
- Manual refactorlog entry (when necessary)

**9.4 Protecting Refactorlog**
- Branch merge dangers
- *Anti-pattern: The Refactorlog Cleanup*
- CI validation options

---

### 10. SSDT Deployment Safety *(was 9)*
*Status: Drafted in thread — needs consolidation*

**10.1 The Publish Profile**
- What it is
- Environment-specific profiles

**10.2 `BlockOnPossibleDataLoss`**
- Non-negotiable setting
- What triggers it
- How to proceed when it fires

**10.3 `DropObjectsNotInSource`**
- Environment-specific recommendations
- Why prod should be `False`

**10.4 Other Critical Settings**
- `IgnoreColumnOrder`
- `GenerateSmartDefaults`
- `AllowIncompatiblePlatform`
- Full matrix by environment

**10.5 Settings as Last Line of Defense**
- Relationship to tier model
- "The system working"

---

### 11. Multi-Phase Evolution *(was 10)*
*Status: Framework exists — needs pattern expansion*

**11.1 Why Some Changes Can't Be Atomic**
- Data dependencies
- Application coordination
- CDC constraints

**11.2 The Core Pattern**
- Create → Migrate → Deprecate
- *Diagram: Multi-phase timeline*

**11.3 Phase-to-Release Mapping**
- What can share a release
- What must be separate
- Minimum safe intervals

**11.4 Rollback Considerations**
- Rollback options at each phase
- Point of no return

**11.5 Catalog of Multi-Phase Operations**
- Table linking to full patterns in Section 17
- Quick identification guide

---

### 12. CDC and Schema Evolution *(was 11)*
*Status: Drafted in thread — needs consolidation*

**12.1 Why CDC Complicates Everything**
- Capture instances are schema-bound
- The core constraint

**12.2 Operations Requiring Instance Recreation**
- Matrix: what's affected, what's safe

**12.3 Development Strategy**
- Accept gaps, optimize for velocity
- Batch CDC cycles
- Automation sketch

**12.4 UAT Strategy**
- Communicate gaps
- Gap logging
- Client-facing messaging template

**12.5 Production Strategy**
- Dual-instance seamless transitions
- The Option A pattern in detail
- Consumer abstraction layer

**12.6 CDC Table Registry**
- Link to maintained list
- How to check before any change

---

## III. OPERATIONS

### 13. Dimension Framework *(was 12)*
*Status: Drafted in thread — needs polish*

**13.1 The Four Dimensions**
- Data Involvement
- Reversibility
- Dependency Scope
- Application Impact
- *Each with value definitions*

**13.2 Recognition Heuristics**
- Question chains for each dimension
- *Formatted as flowable decision trees*

**13.3 How Dimensions Map to Tiers**
- The "highest risk wins" rule
- Context-based escalation

---

### 14. Ownership Tiers *(was 13)*
*Status: Drafted in thread — needs examples*

**14.1 Tier 1: Self-Service with Review**
- Definition
- Typical operations
- Review expectations

**14.2 Tier 2: Pair-Supported**
- Definition
- Typical operations
- Support model

**14.3 Tier 3: Dev Lead Owned**
- Definition
- Typical operations
- Handoff expectations

**14.4 Tier 4: Principal Escalation**
- Definition
- Typical operations
- Escalation protocol

**14.5 Tier Decision Flowchart**
- Visual quick reference
- *One-page printable*

---

### 15. SSDT Mechanism Axis *(was 14)*
*Status: Drafted in thread — needs examples*

**15.1 Pure Declarative**
- When it applies
- What you do
- What SSDT generates

**15.2 Declarative + Post-Deployment**
- When it applies
- Pattern template

**15.3 Pre-Deployment + Declarative**
- When it applies
- Pattern template

**15.4 Script-Only**
- When SSDT can't help
- Full ownership implications

**15.5 Multi-Phase / Multi-Deployment**
- When it applies
- Coordination requirements

---

### 16. Operation Reference *(was 15)*
*Status: Fully drafted in thread — needs reordering per refinement*

**Structural change:** Reorder by OutSystems developer intent, not technical category.

**16.1 Working with Entities (Tables)**
- Create a new Entity → Create Table
- Rename an Entity → Rename Table + refactorlog
- Delete an Entity → Hard-delete Table (deprecation workflow)
- Archive an Entity → Archive Table

**16.2 Working with Attributes (Columns)**
- Add an Attribute → Create Column
- Make an Attribute required → NULL → NOT NULL
- Make an Attribute optional → NOT NULL → NULL
- Change an Attribute's data type → Change Data Type
- Change an Attribute's length → Widen/Narrow Column
- Rename an Attribute → Rename Column + refactorlog
- Delete an Attribute → Column deprecation workflow

**16.3 Working with Identifiers and References (Keys)**
- Define the Identifier → Create Primary Key
- Create a Reference → Create Foreign Key
- Change cascade behavior → Modify Foreign Key
- Remove a Reference → Drop Foreign Key

**16.4 Working with Indexes**
- Add an Index → Create Index
- Modify an Index → various operations
- Remove an Index → Drop Index

**16.5 Working with Static Entities (Lookup Tables)**
- Create a Lookup Table with Seed Data
- Add/Modify Seed Data
- Extract values to a Lookup Table
- Inline Lookup Table back to parent

**16.6 Constraints and Validation**
- Add a Default Value → Create Default Constraint
- Add a Uniqueness Rule → Create Unique Constraint
- Add a Validation Rule → Create Check Constraint
- Modify/Remove Constraints

**16.7 Structural Changes**
- Split an Entity → Split Table
- Merge Entities → Merge Tables
- Move an Attribute → Move Column
- Move an Entity to another schema → Move Table Between Schemas

**16.8 Views, Synonyms, and Abstraction**
- Create a View
- Use View for backward compatibility
- Create Synonym
- Partition Table
- Indexed/Materialized View

**16.9 Audit and Temporal**
- System-versioned temporal tables
- Manual audit columns
- CDC operations
- Change Tracking

**Each operation entry follows Progressive Disclosure:**
- **Layer 1:** One-liner summary, tier, mechanism
- **Layer 2:** Full card (dimensions, what you do, what SSDT generates)
- **Layer 3:** Gotchas, edge cases, related operations, worked example

---

### 17. Multi-Phase Pattern Templates *(was 16)*
*Status: Flagged in thread — needs full expansion*

**17.1 Pattern: Explicit Conversion Data Type Change**
- Phase sequence
- Release mapping
- Code templates
- Rollback at each phase

**17.2 Pattern: NULL → NOT NULL on Populated Table**
- Phase sequence
- Backfill template
- Verification queries

**17.3 Pattern: Add/Remove IDENTITY**
- Phase sequence
- Table rebuild approach
- FK handling

**17.4 Pattern: Add FK with Orphan Data**
- WITH NOCHECK → Clean → Trust
- Phase sequence
- Verification queries

**17.5 Pattern: Safe Column Removal (4-Phase)**
- Soft-deprecate → Stop writes → Verify → Drop
- Timing recommendations

**17.6 Pattern: Table Split**
- Phase sequence
- Application coordination points

**17.7 Pattern: Table Merge**
- Phase sequence
- Data migration template

**17.8 Pattern: Schema Migration with Backward Compatibility**
- Synonym/View bridge approach
- Phase sequence

**17.9 Pattern: CDC-Enabled Table Schema Change**
- Development approach (accept gaps)
- Production approach (dual instance)
- Consumer abstraction template

---

### 18. Decision Aids *(was 17)*
*Status: Framework exists — needs formatting as usable tools*

**18.1 "What Tier Is This?" Flowchart**
- Visual decision tree
- *One-page printable*

**18.2 "Do I Need Multi-Phase?" Checklist**
- Quick yes/no decision points

**18.3 "Can SSDT Handle This Declaratively?" Quick Reference**
- Operation → Mechanism mapping table

**18.4 Before-You-Start Checklist**
- Universal pre-flight for any change
- *Checkbox format, copy-pasteable*

**18.5 CDC Impact Checker**
- "Is this table CDC-enabled?" lookup
- Implications if yes

---

### 19. Anti-Patterns Gallery *(NEW SECTION)*
*Status: Sketched above — needs full drafting*

**19.1 The Naked Rename**
- What it looks like
- What happens
- The fix

**19.2 The Optimistic NOT NULL**
- What it looks like
- What happens
- The fix

**19.3 The Forgotten FK Check**
- What it looks like
- What happens
- The fix

**19.4 The Ambitious Narrowing**
- What it looks like
- What happens
- The fix

**19.5 The CDC Surprise**
- What it looks like
- What happens
- The fix

**19.6 The Refactorlog Cleanup**
- What it looks like
- What happens
- The fix

**19.7 The SELECT * View**
- What it looks like
- What happens
- The fix

*Each entry: ~1 paragraph setup, code example of the mistake, consequence, correct approach*

---

## IV. PROCESS

### 20. The OutSystems → External Entities Workflow *(was 18)*
*Status: Not written — needs drafting with team input*

**20.1 Context: What External Entities Are**
- Brief explanation for those who need it

**20.2 The Handoff: When a Project Moves to SSDT**
- Trigger criteria
- What happens during migration
- Who's responsible for what

**20.3 What Changes for the OutSystems Developer**
- Before: Entity changes in Service Studio
- After: Schema changes in SSDT, refresh in Integration Studio
- Mental model shift

**20.4 Integration Studio Workflow**
- Refreshing External Entity definitions
- Publishing extensions
- *Screenshots*

---

### 21. Local Development Setup *(was 19)*
*Status: Not written — needs environment-specific details*

**21.1 Prerequisites**
- SQL Server local instance (version requirements)
- Visual Studio + SSDT tooling
- Git setup

**21.2 Cloning and Building**
- Repository location
- Build verification

**21.3 Deploying Locally**
- Publish profile for local
- Connection setup

**21.4 Verifying Changes**
- What to check before PR

---

### 22. The Change/Release Process *(was 20)*
*Status: Not written — needs process definition*

**22.1 Step 1: Classify Your Change**
- Using dimension framework
- Tier determination
- Mechanism identification

**22.2 Step 2: Implement the Change**
- Declarative vs. scripted
- Refactorlog (if rename)

**22.3 Step 3: Build and Test Locally**
- Build verification
- Local deployment
- Generated script review

**22.4 Step 4: Open PR**
- Using PR template
- Reviewer tagging by tier

**22.5 Step 5: Review Process**
- Review criteria by tier
- Common feedback patterns

**22.6 Step 6: Merge and Deploy**
- Pipeline behavior
- Verification post-deploy

**22.7 Environment Promotion**
- Dev → Test → UAT → Prod
- Gates at each stage
- Prod-specific considerations

---

### 23. The PR Template *(was 21)*
*Status: Drafted above — ready*

**23.1 The Template**
- Full template (already drafted)

**23.2 Usage Notes for Authors**
- How to fill it out
- Common mistakes

**23.3 Usage Notes for Reviewers**
- What to check by tier
- Common feedback patterns

---

### 24. Troubleshooting Playbook *(was 22)*
*Status: Not written — needs common issues*

**24.1 Build Failures**
- Common causes
- Resolution patterns

**24.2 Deployment Failures**
- `BlockOnPossibleDataLoss` triggered
- Constraint violations
- Timeout issues

**24.3 Refactorlog Issues**
- Merge conflicts
- Missing entries

**24.4 CDC Issues**
- Stale instances
- Missing columns in capture

**24.5 "It Works Locally But Fails in Pipeline"**
- Common causes
- Debugging approach

**24.6 Error Message Translation** *(from refinement)*
- Common errors mapped to OutSystems equivalents
- What they mean in plain language
- Fixes

**24.7 Rollback Procedures**
- By failure type
- Decision tree for rollback approach

---

### 25. Escalation Paths *(was 23)*
*Status: Not written — needs org-specific details*

**25.1 When to Escalate**
- Tier logic recap
- Judgment call signals

**25.2 How to Escalate**
- Channels (Slack, PR, synchronous)
- What to include

**25.3 Who Owns What**
- Dev lead roster
- Principal coverage areas
- Danny's role

---

### 26. Capability Development *(NEW SECTION — from Graduation Path refinement)*
*Status: Sketched above — needs expansion*

**26.1 The Graduation Path**
- Level 1: Observer
- Level 2: Supported Contributor
- Level 3: Independent Contributor
- Level 4: Trusted Contributor
- Level 5: Dev Lead

**26.2 Progression Expectations**
- Typical timelines
- What demonstrates readiness

**26.3 For Managers: Capability Conversations**
- Assessment criteria
- Development opportunities

---

## V. REFERENCE

### 27. SSDT Standards *(was 24)*
*Status: Sketched — needs finalization*

**27.1 Naming Conventions**
- Tables, columns, constraints, indexes
- Full pattern list

**27.2 Preferred Data Types**
- Recommendations with rationale

**27.3 File Structure**
- Standard layout diagram
- What goes where

**27.4 Readability Standards**
- Formatting expectations
- Comment guidelines

---

### 28. Templates *(was 25)*
*Status: PR template drafted — others needed*

**28.1 New Table Template**
- Boilerplate with standard columns

**28.2 Post-Deployment Migration Block Template**
- Idempotent pattern

**28.3 Idempotent Seed Data Template**
- Lookup table population

**28.4 PR Description Template**
- (Cross-reference to Section 23)

**28.5 Incident Report Template**
- For post-mortems

---

### 29. CDC Table Registry *(NEW — from refinement)*
*Status: Not written — needs current table list*

**29.1 The List**
- All CDC-enabled tables
- Current capture instance names
- Schema version

**29.2 How to Check**
- Query to run
- What to do if your table is on the list

**29.3 Maintenance**
- How this list stays current

---

### 30. Glossary *(was 26)*
*Status: Quick glossary drafted — full glossary needed*

- Full A-Z
- OutSystems equivalents noted where applicable
- Links to relevant sections

---

### 31. Resources *(was 27)*
*Status: Not written*

**31.1 Microsoft Documentation**
- SSDT docs
- SQL Server docs

**31.2 Internal Links**
- Azure DevOps project
- Slack channels
- Team roster

**31.3 Training Materials**
- Recordings (if any)
- This playbook's companion deck (once created)

**31.4 External Resources**
- Blog posts, articles that shaped our approach

---

### 32. Contribution Guidelines *(was 28)*
*Status: Not written*

**32.1 How to Propose Changes**
- PR process for wiki

**32.2 Style Guide**
- Diction, tone, formatting
- Progressive disclosure principle

**32.3 What Belongs Where**
- Wiki vs. code comments vs. ADRs

---

### 33. Changelog *(was 29)*
*Status: Empty — starts when wiki launches*

- Version history
- Significant updates

---

# 1. Start Here

---

## What This Is

This is the SSDT Playbook — our shared knowledge base for managing database schema changes using SQL Server Data Tools.

It exists because:
- We're migrating OutSystems projects to External Entities managed by SSDT
- Database changes can destroy production data if done incorrectly
- SSDT's declarative model requires a mental shift from traditional `ALTER TABLE` thinking
- Our team needs shared vocabulary, clear processes, and graduated ownership

This playbook is **living documentation**. It will evolve as we learn. If something is wrong, unclear, or missing — that's a contribution opportunity, not a failure.

---

## Who It's For

| If you are... | This playbook helps you... |
|---------------|---------------------------|
| New to SSDT | Understand the model, learn the concepts, know what to do |
| Practicing IC | Quickly classify changes, find templates, avoid gotchas |
| Dev Lead | Make judgment calls on edge cases, teach others, review effectively |
| Principal Engineer | Have shared vocabulary for coaching, clear escalation criteria |
| New team member | Onboard with structure, understand why things work this way |

---

## How to Use It

**Don't read this cover-to-cover.** Use it as a reference. Here are the paths:

### "I need to make a database change right now"

1. Go to [17. Decision Aids](#) — classify your change
2. Find your operation in [15. Operation Reference](#)
3. Follow the process in [20. The Change/Release Process](#)

### "I'm new and need to understand the basics"

1. Read this page
2. Read [2. The Big Picture](#)
3. Read [3. State-Based Modeling vs. Imperative Migrations](#)
4. Read [9. SSDT Deployment Safety](#)
5. Shadow a PR, then do one with pairing support

### "I'm reviewing a PR and need to know what to check"

1. Check the PR template for tier classification
2. Reference [13. Ownership Tiers](#) for review criteria
3. For specific operations, check [15. Operation Reference](#) for gotchas

### "Something broke and I need to fix it"

1. Go to [22. Troubleshooting Playbook](#)
2. If not covered, escalate per [23. Escalation Paths](#)
3. After resolution, add what you learned to the playbook

### "I want to improve this documentation"

1. Read [28. Contribution Guidelines](#)
2. Make your change
3. Get it reviewed

---

## Quick Glossary

You'll encounter these terms immediately. Full glossary is in [26. Glossary](#).

| Term | Meaning |
|------|---------|
| **SSDT** | SQL Server Data Tools — Visual Studio tooling for database development |
| **Declarative** | You describe the desired end state; SSDT figures out how to get there |
| **Publish** | Deploy your SSDT project to a target database |
| **dacpac** | Compiled SSDT project — a portable representation of your schema |
| **Refactorlog** | XML file tracking renames so SSDT doesn't interpret them as drop+create |
| **Pre-deployment script** | SQL that runs *before* SSDT applies schema changes |
| **Post-deployment script** | SQL that runs *after* SSDT applies schema changes |
| **Capture instance** | CDC's record of a table's schema at a point in time |
| **Tier** | Ownership level for a change (1-4), based on risk/complexity |
| **Multi-phase** | A change that requires multiple sequential deployments to complete safely |

---

## The Foundational Truths

Everything in this playbook rests on these principles:

1. **SSDT is declarative.** You describe end state, not transitions. The `.sql` file *is* the schema.

2. **Understand what SSDT generates.** The abstraction leaks. Always review generated scripts before production.

3. **Data safety is non-negotiable.** `BlockOnPossibleDataLoss=True` is law. Failed deployments are recoverable. Lost data is not.

4. **Complexity requires explicit ownership.** Tiers distribute risk. Escalation is correct behavior, not weakness.

5. **Multi-phase is discipline, not exception.** Changes touching data or dependencies often require sequenced releases.

6. **CDC is load-bearing.** ~200 tables with CDC means schema changes carry audit continuity obligations.

7. **Documentation is infrastructure.** This playbook evolves. Outdated docs are worse than no docs.

8. **Judgment develops through practice.** Reading doesn't create competence. Pairing and graduated autonomy do.

---

## Your First Week

If you're new, here's what to do:

- [ ] Read Start Here (you're doing it)
- [ ] Read The Big Picture
- [ ] Read State-Based Modeling vs. Imperative Migrations
- [ ] Set up local development environment (see [19. Local Development Setup](#))
- [ ] Build the project locally, deploy to your local SQL Server
- [ ] Shadow a PR from classification through merge
- [ ] Make a Tier 1 change with pairing support
- [ ] Ask questions — better to ask than to guess

---

## Getting Help

| Need | Where to go |
|------|-------------|
| Quick question | #ssdt-questions Slack channel |
| PR review | Tag in PR per tier guidance |
| Escalation | See [23. Escalation Paths](#) |
| Something's broken | [22. Troubleshooting Playbook](#), then escalate |
| Playbook feedback | #ssdt-playbook Slack channel or direct to Danny |

---

# 2. The Big Picture

---

## Why We're Here

We're migrating OutSystems applications to use **External Entities** — database tables managed outside of OutSystems, in SQL Server, using SSDT.

**Before:** OutSystems owned the schema. Changes happened in Service Studio. The database was a black box.

**After:** We own the schema. Changes happen in SSDT. The database is explicitly managed, version-controlled, and deployed through our pipeline.

This gives us:
- Full control over schema design, indexing, constraints
- Ability to support complex data patterns OutSystems can't express
- Change Data Capture for audit history across ~200 tables
- Standard SQL Server tooling and practices

It costs us:
- A new mental model (declarative, not imperative)
- Additional process (PR, review, staged deployment)
- CDC management overhead on schema changes
- Learning curve for the team

This playbook exists to minimize the cost and maximize the benefit.

---

## How Database Changes Flow

```
┌──────────────────────────────────────────────────────────────────────────┐
│                                                                          │
│   Developer identifies need for schema change                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  CLASSIFY                               │                            │
│   │  - What tier? (1-4)                     │                            │
│   │  - What SSDT mechanism?                 │                            │
│   │  - Multi-phase needed?                  │                            │
│   │  - CDC impact?                          │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  IMPLEMENT                              │                            │
│   │  - Edit .sql files (declarative)        │                            │
│   │  - Add pre/post scripts (if needed)     │                            │
│   │  - Update refactorlog (if rename)       │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  TEST LOCALLY                           │                            │
│   │  - Build project                        │                            │
│   │  - Deploy to local SQL Server           │                            │
│   │  - Verify change works as expected      │                            │
│   │  - Review generated script              │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  OPEN PR                                │                            │
│   │  - Use PR template                      │                            │
│   │  - Tag appropriate reviewers (by tier)  │                            │
│   │  - Include classification rationale     │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  REVIEW & MERGE                         │                            │
│   │  - Reviewer validates classification    │                            │
│   │  - Reviewer checks for gotchas          │                            │
│   │  - Merge triggers pipeline              │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  DEPLOY                                 │                            │
│   │  - Pipeline deploys to dev              │                            │
│   │  - Promote to test → UAT → prod         │                            │
│   │  - Verify at each stage                 │                            │
│   └─────────────────────────────────────────┘                            │
│                                                                          │
│   If multi-phase: repeat for subsequent phases                           │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Where SSDT Fits with OutSystems

```
┌─────────────────────────────────────────────────────────────────────────┐
│  OutSystems (Service Studio)                                            │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  Application logic, screens, integrations                          │ │
│  │                                                                    │ │
│  │  External Entities ──────────────────┐                             │ │
│  │  (reference to external tables)      │                             │ │
│  └──────────────────────────────────────│─────────────────────────────┘ │
└─────────────────────────────────────────│───────────────────────────────┘
                                          │
                                          │ References (via Integration Studio)
                                          │
                                          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  SQL Server Database                                                    │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  Tables, Views, Indexes, Constraints                               │ │
│  │  Managed by: SSDT                                                  │ │
│  │                                                                    │ │
│  │  CDC Capture Tables                                                │ │
│  │  (auto-generated, track changes)                                   │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
                                          │
                                          │ Version controlled in
                                          │
                                          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Git Repository                                                         │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  /Tables/*.sql     (declarative schema)                            │ │
│  │  /Views/*.sql                                                      │ │
│  │  /Scripts/         (pre/post deployment)                           │ │
│  │  *.refactorlog     (rename tracking)                               │ │
│  │  *.sqlproj         (project definition)                            │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

**Key insight:** OutSystems *consumes* the schema but doesn't *own* it. When you need a schema change:

1. Make it in SSDT (this playbook)
2. Deploy to the database
3. Refresh the External Entity in Integration Studio
4. Use the updated entity in Service Studio

---

## The Change Data Capture Constraint

Almost 200 tables have CDC enabled. This powers our Change History feature — showing end users who changed what, when.

**Why this matters for schema changes:**

CDC capture instances are schema-bound. When you change a table's structure, you often need to:
- Recreate the capture instance (accepting a history gap), or
- Create a new instance alongside the old one (no gap, more complexity)

In **development**: We accept gaps. Velocity matters more than audit completeness.

In **production**: We cannot accept gaps. Schema changes on CDC-enabled tables require multi-phase treatment.

Every schema change on a CDC-enabled table is at least Tier 2. See [11. CDC and Schema Evolution](#) for full guidance.

---

## What Success Looks Like

**For you (individual contributor):**
- You can classify a change and know whether to proceed or escalate
- You can execute Tier 1-2 changes confidently
- You know where to look when you're uncertain
- You've never caused data loss from a schema change

**For the team:**
- PRs are properly classified and reviewed
- Escalations happen at the right time, not too early or too late
- No one is a bottleneck — knowledge is distributed
- The playbook is improving because people contribute to it

**For the system:**
- Zero data loss incidents from SSDT deployments
- Change History feature has complete audit trails in production
- Deployments are predictable and recoverable
- Schema matches source control — no drift

**For Danny:**
- The team owns this process; Danny is not a bottleneck
- Onboarding new team members is structured, not ad-hoc
- Incidents are rare, and when they happen, they become playbook improvements
- The team's SSDT capability is growing over time

---

## What This Playbook Doesn't Cover

- **OutSystems development** — Service Studio, application logic, etc.
- **General SQL Server administration** — backups, security, performance tuning (beyond indexes)
- **Pipeline/DevOps configuration** — Azure DevOps setup, deployment agents
- **Application-level data access** — ORMs, query patterns in application code

Those are important, but they're not this playbook's scope.

---

# 3. The Translation Layer

---

## Why This Section Exists

You know how to build software. You know OutSystems. You've shipped features, fixed bugs, and made schema changes hundreds of times.

Now we're asking you to do the same work in a different tool, with different mechanics, and different risk profiles. That's disorienting — not because you lack skill, but because your hard-won intuitions don't map cleanly.

This section is your translation guide. It won't make SSDT feel like OutSystems (it isn't), but it will help you find your footing faster by connecting what you know to what you're learning.

---

## 3.1 OutSystems → SSDT Rosetta Stone

### Vocabulary

| OutSystems Term | SSDT / SQL Server Term | Notes |
|-----------------|------------------------|-------|
| Entity | Table | Same concept. In SSDT, you define it in a `.sql` file with a `CREATE TABLE` statement. |
| Entity Attribute | Column | Same concept. Defined inline within the table definition. |
| Entity Identifier | Primary Key | OutSystems auto-generated this. Now you define it explicitly with `PRIMARY KEY`. |
| Auto Number | IDENTITY | `IDENTITY(1,1)` means "start at 1, increment by 1." |
| Reference Attribute | Foreign Key (FK) | The relationship OutSystems drew as a line. Now you write `FOREIGN KEY ... REFERENCES`. |
| Reference (the line in diagrams) | Foreign Key Constraint | Same thing, explicit syntax. |
| Delete Rule (Protect/Delete/Ignore) | ON DELETE (NO ACTION/CASCADE/SET NULL) | You now control cascade behavior explicitly. |
| Index (in Entity properties) | Index | Same concept, more control over type (clustered, non-clustered, filtered, covering). |
| Static Entity | Lookup/Reference Table + Seed Data | Two pieces: the table structure (declarative) and the data (post-deployment script). |
| Entity Record | Row | Same concept. |
| Entity Diagram | No direct equivalent | Your `.sql` files *are* the schema. Use SSMS diagrams or VS schema compare for visualization. |
| Data Type (Integer, Text, Date, etc.) | Data Type (INT, NVARCHAR, DATETIME2, etc.) | Similar but more specific. See data type mapping below. |
| Is Mandatory = Yes | NOT NULL | OutSystems terminology → SQL constraint. |
| Is Mandatory = No | NULL (or omit — NULL is default) | Column allows empty values. |
| Default Value | Default Constraint | Same concept, explicit syntax: `CONSTRAINT DF_Table_Column DEFAULT (value)`. |
| Unique Attribute | Unique Constraint | `CONSTRAINT UQ_Table_Column UNIQUE`. |
| Service Studio | Visual Studio + SSDT | Where you edit. |
| Service Center | SQL Server Management Studio (SSMS) | Where you inspect the running database. |
| Publish (1-Click Publish) | Deploy / Publish | Similar lifecycle, but SSDT deploys via pipeline, not directly from your IDE. |
| eSpace / Module | SSDT Project / .sqlproj | The unit of deployment. |
| Solution | Solution (same term) | Container for multiple projects. |
| Integration Studio | Integration Studio (still used) | The bridge — you refresh External Entity definitions here after SSDT deploys. |
| External Entity | External Entity (same term) | An entity whose schema lives outside OutSystems — in our SSDT-managed database. |

### Data Type Mapping

| OutSystems Type | SQL Server Type | Notes |
|-----------------|-----------------|-------|
| Integer | INT | Exact match. |
| Long Integer | BIGINT | For values > 2.1 billion. |
| Decimal | DECIMAL(p,s) | You specify precision (p) and scale (s). e.g., `DECIMAL(18,2)` for currency. |
| Boolean | BIT | 0 = false, 1 = true. |
| Text | NVARCHAR(n) or NVARCHAR(MAX) | We use NVARCHAR (Unicode) by default. Specify length. |
| Date | DATE | Date only, no time component. |
| Time | TIME | Time only, no date component. |
| Date Time | DATETIME2(7) | Preferred over DATETIME. Higher precision, wider range. |
| Binary Data | VARBINARY(n) or VARBINARY(MAX) | For files, images, etc. |
| Email, Phone Number | NVARCHAR(n) | No special type — just text with appropriate length. |
| Currency | DECIMAL(18,2) | We use DECIMAL, not the MONEY type. |
| Entity Identifier (FK) | INT (or match parent PK type) | Type must match the referenced primary key. |

### Structural Concepts

| OutSystems Concept | SSDT Equivalent | How It Works |
|--------------------|-----------------|--------------|
| Entity relationships shown as lines | Foreign key constraints | You define them explicitly in the table or as separate constraint objects. |
| Cascade delete (Delete Rule = Delete) | ON DELETE CASCADE | Deleting a parent automatically deletes children. |
| Protect (Delete Rule = Protect) | ON DELETE NO ACTION | Delete fails if children exist. This is the default. |
| Computed/calculated attributes | Computed columns | `[FullName] AS ([FirstName] + ' ' + [LastName])` — SQL Server calculates it. |
| Entity versioning / history | System-versioned temporal tables or CDC | OutSystems didn't have this. Now you can track all historical states. |

---

## 3.2 "I Used To... Now I..."

This is your quick-reference for the action translation. When you find yourself thinking "I used to just...", look here.

### Adding and Modifying Structure

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Add an attribute in Service Studio → Publish | Add column to table's `.sql` file → PR → merge → deploy | PR review required. More eyes on changes. |
| Set "Is Mandatory = Yes" on new attribute | Add `NOT NULL` and a `DEFAULT` constraint | If table has data, existing rows need a value. You must provide default or backfill. |
| Set "Is Mandatory = No" | Add column without `NOT NULL` (or explicitly write `NULL`) | Simpler — this is the easier direction. |
| Change attribute data type → Publish | Classify the change first (implicit or explicit conversion?) | Some type changes are safe; others need multi-phase. See Operation Reference. |
| Rename an attribute → Publish | Use SSDT GUI rename (creates refactorlog entry) → PR → deploy | **Critical:** Without refactorlog, SSDT drops the column and recreates it. Data loss. |
| Delete an attribute → Publish | Follow deprecation workflow: soft-deprecate → verify unused → drop | OutSystems could roll back. SQL Server deletion is permanent. Process protects you. |
| Change attribute length (Text 50 → Text 100) | Change length in column definition: `NVARCHAR(50)` → `NVARCHAR(100)` | Widening is safe. Narrowing is dangerous (data truncation). |
| Add an index in Entity properties | Create index in SSDT: `CREATE INDEX IX_Table_Column ON Table(Column)` | Same concept, explicit syntax. Large tables = blocking during creation. |

### Working with Relationships

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Create a Reference Attribute → line appears | Add FK column + foreign key constraint | Two parts: the column itself, then the constraint. |
| Set Delete Rule = Delete (cascade) | Add `ON DELETE CASCADE` to FK definition | Explicit syntax. Be careful — cascades can be surprising. |
| Delete a reference → Publish | Drop the FK constraint (may need to drop column too) | Order matters. Drop constraint before dropping column. |
| Add a Reference to existing data | Verify no orphan data exists first, then add FK | OutSystems checked this for you. Now you run validation queries. |

### Working with Static Entities (Lookup Tables)

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Create Static Entity → add records | Create table (declarative) + seed data (post-deployment script) | Structure and data are separate concerns. |
| Add a record to Static Entity → Publish | Add INSERT to post-deployment script (must be idempotent) | Script must handle "already exists" case gracefully. |
| Rename Static Entity record | Update the record in seed script; update FK references if needed | More manual than OutSystems, but same concept. |

### The Development Workflow

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| Make change → Publish → see it in Service Center | Make change → build locally → deploy to local SQL → PR → merge → pipeline deploys | More steps, but each is a checkpoint. Errors caught earlier. |
| See schema visually in Entity Diagram | Use SSMS database diagrams, VS schema compare, or read `.sql` files | Visualization is available but separate from editing. |
| Trust that Publish wouldn't break things | Review generated script before deploy; trust settings like `BlockOnPossibleDataLoss` | You have more control, which means more responsibility. |
| Rollback by republishing previous version | Rollback varies by change type — some are symmetric, some need restore | No universal "undo." Plan rollback before you deploy. |

### The Integration Studio Bridge

| I used to... | Now I... | Key Difference |
|--------------|----------|----------------|
| (External Entities didn't exist in this form) | Make schema change in SSDT → deploy → refresh External Entity in Integration Studio → publish extension → refresh in Service Studio | This is the new boundary. Schema changes flow through SSDT first. |

---

## 3.3 Risk Recalibration: "Feels Like / Actually Is"

OutSystems abstracted safety. It prevented many mistakes by design. SSDT gives you more power, which means more ways to hurt yourself.

This table recalibrates your risk intuition.

### Operations That Are More Dangerous Than They Feel

| Operation | In OutSystems this felt... | In SSDT it actually is... | Why |
|-----------|---------------------------|---------------------------|-----|
| **Rename attribute** | Safe — just a name change | 🔴 **Dangerous** — data loss without refactorlog | OutSystems tracked identity internally. SSDT tracks by name. A renamed column looks like "old deleted, new created." |
| **Change data type** | Usually safe — OutSystems converted | 🟡 **Variable** — safe to dangerous depending on types | OutSystems did safe conversions silently. SQL Server may fail, truncate, or require explicit conversion. |
| **Delete attribute** | Safe — could rollback | 🔴 **Permanent** — data gone forever | OutSystems versioned everything. SQL Server DROP means gone. Backup restore is your only recovery. |
| **Add NOT NULL attribute** | Safe — OutSystems handled it | 🟡 **Requires thought** — existing rows need values | OutSystems figured out defaults. SQL Server fails if existing rows have no value. |
| **Delete entity with data** | Guarded — OutSystems warned you | 🔴 **Permanent** — `BlockOnPossibleDataLoss` is your only guard | Setting must be True. If False, silent data destruction. |
| **Change attribute length (narrowing)** | Safe — OutSystems checked fit | 🔴 **Dangerous** — truncation or failure | OutSystems validated. SQL Server truncates or errors depending on settings. You must verify data fits. |

### Operations That Are Safer Than They Feel

| Operation | This might feel scary... | But it's actually... | Why |
|-----------|-------------------------|----------------------|-----|
| **Add new entity (table)** | New, unfamiliar syntax | 🟢 **Safe** — purely additive, Tier 1 | Nothing references it yet. Can't break existing code. |
| **Add nullable attribute** | Editing a production table | 🟢 **Safe** — purely additive, Tier 1 | Existing rows get NULL. Existing queries unaffected. |
| **Add an index** | Touching table structure | 🟢 **Mostly safe** — additive for queries | Queries get faster. Only risk is blocking during creation on large tables. |
| **Add default constraint** | Changing column behavior | 🟢 **Safe** — only affects future inserts | Existing data unchanged. New inserts get the default if no value provided. |
| **Create a view** | Adding complexity | 🟢 **Safe** — just a named query | Views are additive. Can be dropped without affecting underlying data. |
| **Widen column (TEXT 50 → 100)** | Changing column definition | 🟢 **Safe** — all existing values still fit | Widening never loses data. Only narrowing is dangerous. |

### The Mental Model Shift

**In OutSystems:** "Publish and see what happens. If it's wrong, publish again."

**In SSDT:** "Classify, plan, verify, deploy. Know your rollback before you go."

This isn't because SSDT is worse — it's because you now have direct access to the database, without the abstraction layer that protected you. That access enables things OutSystems couldn't do (CDC, temporal tables, complex constraints, precise indexing). The cost is that you must provide the judgment OutSystems provided for you.

**The good news:** The playbook encodes that judgment. Follow the process, use the tiers, and you'll develop the intuition over time.

---

## 3.4 The Integration Studio Bridge

This is the literal boundary between OutSystems and SSDT. Every schema change crosses this bridge.

### The Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│  1. SSDT CHANGE                                                         │
│                                                                         │
│     You make a schema change:                                           │
│     - Edit .sql file in Visual Studio                                   │
│     - PR → review → merge                                               │
│     - Pipeline deploys to target database                               │
│                                                                         │
│                              │                                          │
│                              ▼                                          │
├─────────────────────────────────────────────────────────────────────────┤
│  2. DATABASE UPDATED                                                    │
│                                                                         │
│     The SQL Server database now has the new schema.                     │
│     OutSystems doesn't know yet.                                        │
│                                                                         │
│                              │                                          │
│                              ▼                                          │
├─────────────────────────────────────────────────────────────────────────┤
│  3. INTEGRATION STUDIO REFRESH                                          │
│                                                                         │
│     Open Integration Studio:                                            │
│     - Connect to the database                                           │
│     - Select the External Entity                                        │
│     - Click "Refresh" to pull new schema                                │
│     - Review changes (new columns, modified types, etc.)                │
│     - Publish the extension                                             │
│                                                                         │
│                              │                                          │
│                              ▼                                          │
├─────────────────────────────────────────────────────────────────────────┤
│  4. SERVICE STUDIO UPDATE                                               │
│                                                                         │
│     In Service Studio:                                                  │
│     - Refresh references to the extension                               │
│     - Update application logic if needed (new attributes, etc.)         │
│     - Publish the application                                           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Step-by-Step: Refreshing an External Entity

**Prerequisites:**
- SSDT change has been deployed to the target environment
- You have Integration Studio installed and access to the database

**Steps:**

1. **Open Integration Studio**
   - Launch from Windows Start menu or OutSystems Service Studio (Edit → Open Integration Studio)

2. **Open the Extension**
   - File → Open → navigate to your extension (`.xif` file)
   - Or connect to your environment and download the extension

3. **Connect to Database**
   - Go to the Entities folder in your extension
   - Right-click the External Entity you need to refresh
   - Select "Refresh Entity" (or similar — exact wording may vary by version)

4. **Review the Detected Changes**
   - Integration Studio shows what's different between the extension definition and the actual database
   - **Added columns:** Will appear as new attributes
   - **Removed columns:** Will be flagged for removal from entity
   - **Type changes:** May show as modified attributes
   - **Renamed columns:** Will appear as "old removed, new added" — this is expected if you renamed in SSDT

5. **Accept or Adjust**
   - For straightforward changes, accept the detected modifications
   - For renames: you may need to manually map the old attribute to the new one, or delete old and configure new
   - Verify data types mapped correctly (see data type mapping above)

6. **Verify Entity Configuration**
   - Check that Identifier is set correctly
   - Check that Is Mandatory matches your NOT NULL constraints
   - Check that data types are appropriate for OutSystems use

7. **Publish Extension**
   - Click "1-Click Publish" (or similar)
   - Extension is now updated in the environment

8. **Update Service Studio**
   - Open Service Studio
   - Open the module(s) that use this External Entity
   - Right-click the extension reference → Refresh
   - Review any breaking changes (removed attributes, type changes)
   - Update application logic as needed
   - Publish

### Common Integration Studio Scenarios

| You see... | It means... | What to do |
|------------|-------------|------------|
| New attribute detected | SSDT added a column | Accept it. Configure Is Mandatory, data type, etc. |
| Attribute missing | SSDT removed a column | Verify this was intentional. Accept removal. Update app logic if it used this attribute. |
| Attribute type changed | SSDT changed the column's data type | Accept it. Verify OutSystems type mapping is appropriate. May require app logic updates. |
| Entity not found | Table was renamed or dropped in SSDT | If renamed: create new External Entity pointing to new table name. If dropped: this is expected, remove from extension. |
| Unexpected schema differences | Something is out of sync | Verify SSDT deployment completed successfully. Check you're connected to the right database/environment. |
| No changes detected | Database matches extension | Nothing to do — your SSDT change may not have affected this entity, or refresh already happened. |

### Troubleshooting Integration Studio Refresh

| Problem | Likely Cause | Resolution |
|---------|--------------|------------|
| "Cannot connect to database" | Wrong connection string, network issue, permissions | Verify connection details. Check you have access to the database. |
| Refresh shows changes you didn't make | Connected to wrong environment | Check connection — are you pointing at dev when you meant test? |
| Refresh shows no changes but you expected some | Deployment didn't complete, or refreshed already | Verify SSDT pipeline succeeded. Check database directly with SSMS. |
| Type mapping seems wrong | OutSystems chose a different type than expected | You can manually adjust the attribute type in Integration Studio. |
| "Entity has dependencies" warning | OutSystems modules reference this entity | Expected if entity is in use. Proceed, then update dependent modules. |
| Publish fails after refresh | Extension has errors | Check the error log. Common issues: duplicate attribute names, invalid type mappings. |

### When Refresh Shows Unexpected Changes

If Integration Studio shows changes you didn't expect, pause and investigate:

1. **Verify the right environment:** Are you connected to the database you think you are?

2. **Check SSDT deployment status:** Did the pipeline succeed? Check Azure DevOps (or your CI/CD tool).

3. **Check the database directly:** Open SSMS, connect to the database, verify the table structure.

4. **Check for parallel changes:** Did someone else make SSDT changes that merged before yours?

5. **Check for missed migrations:** If this is a fresh environment, did all schema migrations run?

If something looks wrong, **do not accept the changes blindly**. Investigate first. It's easier to diagnose before you publish than after.

### Coordinating SSDT and OutSystems Releases

For changes that affect both schema and application logic:

**Simple case (additive changes):**
- SSDT deploys first (adds column, index, etc.)
- OutSystems deploys second (uses the new column)
- Order matters — OutSystems can't reference what doesn't exist yet

**Complex case (breaking changes):**
- May require multi-phase approach
- Example: Renaming a column
  - Phase 1: SSDT adds new column (old and new coexist)
  - Phase 2: OutSystems migrates to new column
  - Phase 3: SSDT removes old column

**Timing:**
- Allow time between SSDT deploy and OutSystems refresh
- Verify SSDT deployment succeeded before starting Integration Studio work
- In lower environments, this can be minutes apart
- In production, coordinate with release schedule

---

## 3.5 Quick Reference Card

*Print this. Pin it to your wall. Refer to it until you don't need to.*

```
┌─────────────────────────────────────────────────────────────────────────┐
│  SSDT TRANSLATION QUICK REFERENCE                                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  VOCABULARY                                                             │
│  Entity = Table          Attribute = Column       Reference = FK        │
│  Identifier = PK         Static Entity = Lookup Table + Seed Script     │
│  Publish = Deploy        Service Center = SSMS                          │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  DANGER ZONES (more dangerous than they feel)                           │
│  🔴 Rename attribute → Use GUI rename, verify refactorlog               │
│  🔴 Delete attribute → Follow deprecation workflow                      │
│  🔴 Narrow column → Verify all data fits first                          │
│  🟡 Change data type → Classify: implicit safe, explicit = multi-phase  │
│  🟡 Add NOT NULL → Need default or backfill                             │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  SAFE ZONES (safer than they feel)                                      │
│  🟢 Add new table → Purely additive, Tier 1                             │
│  🟢 Add nullable column → Purely additive, Tier 1                       │
│  🟢 Add index → Additive (watch blocking on large tables)               │
│  🟢 Add default constraint → Only affects future inserts                │
│  🟢 Widen column → All existing values still fit                        │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  THE FLOW                                                               │
│  Edit .sql → PR → Merge → Pipeline deploys → Integration Studio         │
│  refresh → Service Studio refresh → App publish                         │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  BEFORE ANY CHANGE                                                      │
│  □ Classified tier?    □ CDC table?    □ Need multi-phase?              │
│  □ Refactorlog needed? □ Reviewers tagged?                              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## What's Next

Now that you have the translation layer:

- **For conceptual foundation:** Read [4. State-Based Modeling vs. Imperative Migrations](#)
- **For your first change:** Read [22. The Change/Release Process](#) and use the [Before-You-Start Checklist](#)
- **For specific operations:** Find your operation in [16. Operation Reference](#)
- **If you're stuck:** Check [24. Troubleshooting Playbook](#) or ask in #ssdt-questions

---

Let me map the optimal sequence based on dependencies and team needs:

**Foundation Layer (enables everything else):**
- Section 4: State-Based Modeling → Section 5: Anatomy of SSDT Project

**Conceptual Consolidation (thread content, needs structuring):**
- Sections 6-12: Pre/Post Scripts, Idempotency, Referential Integrity, Refactorlog, Safety, Multi-Phase, CDC

**Execution Layer (the "how to do it"):**
- Section 17: Multi-Phase Pattern Templates → Section 19: Anti-Patterns Gallery

**Process Layer (the human workflow):**
- Section 20: OutSystems → External Entities Workflow → Section 21: Local Dev Setup → Section 22: Change/Release Process

**Tools Layer (quick reference):**
- Section 18: Decision Aids

**Reference Layer (lookup material):**
- Remaining sections (Standards, Templates, Glossary, etc.)

Let me begin.

---

# 4. State-Based Modeling vs. Imperative Migrations

---

## The Core Idea

In OutSystems, when you changed an entity, you were implicitly saying: *"Here's what I want it to become."* OutSystems figured out how to get there.

SSDT works the same way — but makes it explicit.

**Imperative approach (what you might expect):**
```sql
-- "Do these steps in this order"
ALTER TABLE dbo.Person ADD MiddleName NVARCHAR(50) NULL;
ALTER TABLE dbo.Person ALTER COLUMN Email NVARCHAR(200);
ALTER TABLE dbo.Person DROP COLUMN LegacyId;
```

**Declarative approach (what SSDT actually does):**
```sql
-- "Here's what the table should look like"
CREATE TABLE [dbo].[Person]
(
    PersonId INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(100) NOT NULL,
    MiddleName NVARCHAR(50) NULL,           -- Added
    LastName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(200) NOT NULL,           -- Widened
    -- LegacyId removed
    CONSTRAINT [PK_Person] PRIMARY KEY CLUSTERED (PersonId)
)
```

You write the second version. SSDT compares it to the target database and generates the first version automatically.

---

## Why This Matters

### You Describe End State, Not Transitions

Your `.sql` files aren't scripts to run. They're declarations of what the schema *should be*.

When you edit a table definition:
- You're not writing an ALTER statement
- You're changing the declared end state
- SSDT computes the delta between current and desired
- SSDT generates whatever DDL is needed to close the gap

**Practical implication:** Stop thinking "what command do I run?" Start thinking "what should this table look like when I'm done?"

### The .sql File IS the Schema

In imperative migration systems (like Entity Framework migrations or Flyway), you have:
- Migration files: the steps to get from version N to version N+1
- Maybe a snapshot: what the schema looks like now

In SSDT, you have:
- Table definitions: what each table looks like, period
- The history lives in git, not in the schema itself

**Your `dbo.Person.sql` file is the source of truth.** It represents the current desired state of that table. Git history shows how it evolved.

### SSDT Computes the Delta

When you deploy, SSDT:

1. Reads your project (desired state)
2. Connects to target database (current state)
3. Compares them
4. Generates a deployment script (the delta)
5. Executes that script

```
┌─────────────────┐      ┌─────────────────┐
│  SSDT Project   │      │ Target Database │
│  (desired)      │      │ (current)       │
└────────┬────────┘      └────────┬────────┘
         │                        │
         │    ┌───────────────┐   │
         └───►│   Compare     │◄──┘
              └───────┬───────┘
                      │
                      ▼
              ┌───────────────┐
              │ Generated     │
              │ Deploy Script │
              │ (the delta)   │
              └───────┬───────┘
                      │
                      ▼
              ┌───────────────┐
              │ Execute       │
              │ (database     │
              │  transformed) │
              └───────────────┘
```

**This means:** The same project deployed to different databases generates different scripts. A fresh database gets `CREATE TABLE`. An existing database with the table gets `ALTER TABLE` (or nothing, if it matches).

---

## What You Do vs. What SSDT Generates

| You do this... | SSDT generates this... |
|----------------|------------------------|
| Add a column to table definition | `ALTER TABLE ... ADD ...` |
| Remove a column from table definition | `ALTER TABLE ... DROP COLUMN ...` |
| Change column type in definition | `ALTER TABLE ... ALTER COLUMN ...` |
| Create new table file | `CREATE TABLE ...` |
| Delete table file | `DROP TABLE ...` (if `DropObjectsNotInSource=True`) |
| Rename via refactorlog | `EXEC sp_rename ...` |
| Add constraint to definition | `ALTER TABLE ... ADD CONSTRAINT ...` |

You never write ALTER statements. You edit declarations. SSDT translates.

---

## When the Abstraction Leaks

The declarative model is powerful but not omniscient. SSDT's generated script is *correct* but not always *optimal* or *safe*.

### SSDT Doesn't Know Your Data

SSDT sees schema, not rows. It will happily generate:

```sql
ALTER TABLE dbo.Person ALTER COLUMN Email NVARCHAR(50) NOT NULL
```

It doesn't know that you have 10,000 rows with emails longer than 50 characters, or 500 rows with NULL emails.

**Your job:** Validate data fits before deploying changes that constrain it.

### SSDT Doesn't Know Your Intentions

If you rename a column by editing the file directly:

```sql
-- Before
FirstName NVARCHAR(100)

-- After (you just changed the text)
GivenName NVARCHAR(100)
```

SSDT sees: "FirstName is gone. GivenName is new."

SSDT generates:
```sql
ALTER TABLE dbo.Person DROP COLUMN FirstName
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100)
```

Data in FirstName? Gone.

**Your job:** Use the refactorlog for renames so SSDT knows it's identity-preserving, not drop-and-create.

### SSDT Optimizes for Correctness, Not Performance

SSDT will generate a working script, but not necessarily the fastest one. For example:

- Adding a NOT NULL column with a default might do it in a way that rewrites the whole table
- Changing a clustered index might rebuild everything
- Reordering columns (if `IgnoreColumnOrder=False`) triggers a full table rebuild

**Your job:** Review generated scripts, especially for large tables. Know when to override with manual scripts.

---

## The Review Discipline

Because SSDT generates scripts from your declarations, you must review what it generates before deploying to production.

**For Tier 1 changes:** Skim the generated script. Verify it's doing what you expect.

**For Tier 2+ changes:** Read carefully. Check for:
- Unexpected DROP statements
- Table rebuilds (look for temp table creation)
- Large data movements
- Constraint validations on big tables

**How to see the generated script:**

1. **In Visual Studio:** Right-click project → Schema Compare → compare to target → view script
2. **In pipeline:** Most SSDT pipelines save the generated script as an artifact
3. **Using SqlPackage:** `SqlPackage /Action:Script /SourceFile:project.dacpac /TargetConnectionString:...`

**The rule:** Never deploy to production without reviewing the generated script. The abstraction is not trustworthy without inspection.

---

## Mental Model Summary

| Imperative (migrations) | Declarative (SSDT) |
|-------------------------|---------------------|
| Write migration scripts | Edit table definitions |
| Migrations accumulate forever | Current state is the only truth |
| Order of migrations matters | Order doesn't matter — just the end state |
| Rollback = write reverse migration | Rollback = restore previous definition (SSDT computes reverse) |
| You control exactly what runs | You control the outcome; SSDT controls the path |
| Risk: migrations diverge from reality | Risk: generated script surprises you |

**The shift:** You stop thinking "what steps do I take?" and start thinking "what should exist when I'm done?"

---

## Connecting to What You Know

If OutSystems's 1-Click Publish felt like magic, SSDT is the same magic made visible.

OutSystems compared your model to the database and made changes. You just didn't see the comparison or the generated SQL.

SSDT does the same thing, but:
- You see the model (your `.sql` files)
- You can see the comparison (Schema Compare)
- You can see the generated SQL (deployment script)
- You control whether it runs (publish with review)

More visibility. More control. More responsibility.

---

# 5. Anatomy of an SSDT Project

---

## What You're Looking At

When you open the SSDT project in Visual Studio, you'll see a folder structure that represents your database schema. Every object in the database has a corresponding file.

```
/DatabaseProject.sqlproj          ← Project file (MSBuild, settings)
/DatabaseProject.refactorlog      ← Rename tracking
/DatabaseProject.publish.xml       ← Publish profile(s)

/Security/
    Schemas.sql                   ← Schema definitions (dbo, audit, etc.)
    Roles.sql                     ← Database roles
    Users.sql                     ← Database users

/Tables/
    /dbo/
        dbo.Customer.sql          ← Each table is one file
        dbo.Order.sql
        dbo.OrderLine.sql
        dbo.Product.sql
    /audit/
        audit.ChangeLog.sql

/Views/
    /dbo/
        dbo.vw_ActiveCustomer.sql
        dbo.vw_OrderSummary.sql

/Stored Procedures/
    /dbo/
        dbo.usp_GetCustomerOrders.sql

/Functions/
    /dbo/
        dbo.fn_CalculateTotal.sql

/Indexes/                          ← Optional: can be inline or separate
    IX_Order_CustomerId.sql

/Synonyms/
    dbo.LegacyCustomer.sql

/Scripts/
    /PreDeployment/
        PreDeployment.sql         ← Master pre-deployment script
    /PostDeployment/
        PostDeployment.sql        ← Master post-deployment script
        /Migrations/
            001_BackfillMiddleName.sql
            002_SeedStatusCodes.sql
        /ReferenceData/
            SeedCountries.sql
            SeedProductTypes.sql
        /OneTime/
            Release_2025.02_Fixes.sql

/Snapshots/                        ← Optional: dacpac versions for comparison
    DatabaseProject_v1.0.dacpac
```

---

## File-to-Object Mapping

Every database object gets its own file. One file, one object.

### Tables

```sql
-- /Tables/dbo/dbo.Customer.sql

CREATE TABLE [dbo].[Customer]
(
    [CustomerId] INT IDENTITY(1,1) NOT NULL,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Customer_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT (1),
    
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId]),
    CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email])
)
```

**Note:** Constraints, defaults, and the primary key are defined inline. This keeps everything about the table in one place.

### Foreign Keys

Can be inline or separate. We prefer inline for clarity:

```sql
-- /Tables/dbo/dbo.Order.sql

CREATE TABLE [dbo].[Order]
(
    [OrderId] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [OrderDate] DATE NOT NULL,
    [TotalAmount] DECIMAL(18,2) NOT NULL,
    
    CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED ([OrderId]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customer]([CustomerId])
)
```

### Indexes

Can be inline (in table file) or separate files. Separate is cleaner for complex indexes:

```sql
-- /Indexes/IX_Order_CustomerId.sql

CREATE NONCLUSTERED INDEX [IX_Order_CustomerId]
ON [dbo].[Order]([CustomerId])
INCLUDE ([OrderDate], [TotalAmount])
```

### Views

```sql
-- /Views/dbo/dbo.vw_ActiveCustomer.sql

CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
WHERE IsActive = 1
```

### Stored Procedures

```sql
-- /Stored Procedures/dbo/dbo.usp_GetCustomerOrders.sql

CREATE PROCEDURE [dbo].[usp_GetCustomerOrders]
    @CustomerId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        o.OrderId,
        o.OrderDate,
        o.TotalAmount
    FROM dbo.[Order] o
    WHERE o.CustomerId = @CustomerId
    ORDER BY o.OrderDate DESC;
END
```

---

## The .sqlproj File

This is the MSBuild project file. It defines:

- What files are included in the project
- Target SQL Server version
- Build settings
- Database references

**Key settings you'll see:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="...">
  <PropertyGroup>
    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>  <!-- SQL 2019 -->
    <TargetDatabaseSet>True</TargetDatabaseSet>
    <DefaultCollation>SQL_Latin1_General_CP1_CI_AS</DefaultCollation>
    <DefaultFilegroup>PRIMARY</DefaultFilegroup>
    
    <!-- Build behavior -->
    <TreatTSqlWarningsAsErrors>True</TreatTSqlWarningsAsErrors>
  </PropertyGroup>
  
  <!-- File includes -->
  <ItemGroup>
    <Build Include="Tables\dbo\dbo.Customer.sql" />
    <Build Include="Tables\dbo\dbo.Order.sql" />
    <!-- etc. -->
  </ItemGroup>
  
  <!-- Pre/Post deployment scripts -->
  <ItemGroup>
    <PreDeploy Include="Scripts\PreDeployment\PreDeployment.sql" />
    <PostDeploy Include="Scripts\PostDeployment\PostDeployment.sql" />
  </ItemGroup>
</Project>
```

**You usually don't edit this directly.** Visual Studio maintains it when you add/remove files.

---

## The Publish Profile (.publish.xml)

This defines *how* to deploy and *what settings* to use. You'll have different profiles for different environments.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="...">
  <PropertyGroup>
    <TargetConnectionString>Data Source=localhost;Initial Catalog=MyDatabase;Integrated Security=True</TargetConnectionString>
    
    <!-- Critical safety settings -->
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <DropObjectsNotInSource>False</DropObjectsNotInSource>
    
    <!-- Behavior settings -->
    <IgnoreColumnOrder>True</IgnoreColumnOrder>
    <GenerateSmartDefaults>False</GenerateSmartDefaults>
    <AllowIncompatiblePlatform>False</AllowIncompatiblePlatform>
    <IgnorePermissions>True</IgnorePermissions>
    
    <!-- What to include -->
    <IncludeCompositeObjects>True</IncludeCompositeObjects>
    <IncludeTransactionalScripts>True</IncludeTransactionalScripts>
  </PropertyGroup>
</Project>
```

**Environment-specific profiles:**

| Profile | BlockOnPossibleDataLoss | DropObjectsNotInSource | GenerateSmartDefaults |
|---------|-------------------------|------------------------|-----------------------|
| Local.publish.xml | True | True | True |
| Dev.publish.xml | True | True | True |
| Test.publish.xml | True | True | False |
| UAT.publish.xml | True | False | False |
| Prod.publish.xml | True | False | False |

---

## The Refactorlog

The `.refactorlog` file tracks renames so SSDT knows when you're renaming vs. dropping-and-creating.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Operations Version="1.0" xmlns="...">
  <Operation Name="Rename Refactor" Key="abc123..." ChangeDateTime="2025-01-15T10:30:00">
    <Property Name="ElementName" Value="[dbo].[Customer].[FirstName]" />
    <Property Name="ElementType" Value="SqlSimpleColumn" />
    <Property Name="ParentElementName" Value="[dbo].[Customer]" />
    <Property Name="ParentElementType" Value="SqlTable" />
    <Property Name="NewName" Value="GivenName" />
  </Operation>
</Operations>
```

**Critical rules:**
- Never delete refactorlog entries
- Always use GUI rename (or manually add entries)
- Protect during merges — refactorlog conflicts can cause data loss if resolved incorrectly

---

## Pre-Deployment and Post-Deployment Scripts

These are SQL scripts that run before and after SSDT applies the schema changes.

**PreDeployment.sql:** Runs first. Use for:
- Dropping dependencies that block schema changes
- Preparing data for transformations
- Anything that must happen *before* the schema changes

**PostDeployment.sql:** Runs last. Use for:
- Data migrations
- Seeding reference data
- Backfilling new columns
- Any data work that depends on the new schema existing

**Structure:**

```sql
-- /Scripts/PostDeployment/PostDeployment.sql

/*
Post-Deployment Script
This script runs after schema changes are applied.
Use SQLCMD :r to include other scripts.
*/

PRINT 'Starting post-deployment scripts...'

-- Permanent migrations (idempotent)
:r .\Migrations\001_BackfillMiddleName.sql
:r .\Migrations\002_SeedStatusCodes.sql

-- Reference data (idempotent)
:r .\ReferenceData\SeedCountries.sql
:r .\ReferenceData\SeedProductTypes.sql

-- One-time scripts for this release (remove after prod deploy)
:r .\OneTime\Release_2025.02_Fixes.sql

PRINT 'Post-deployment complete.'
```

The `:r` syntax is SQLCMD — it includes another file inline.

---

## Database References

If your project references objects in other databases (or linked servers), you need database references.

**Same-server reference:**

```xml
<ArtifactReference Include="..\OtherDatabase\OtherDatabase.dacpac">
  <DatabaseVariableLiteralValue>OtherDatabase</DatabaseVariableLiteralValue>
</ArtifactReference>
```

**Linked server / external reference:**

```xml
<ArtifactReference Include="ExternalDB.dacpac">
  <DatabaseVariableLiteralValue>LinkedServer.ExternalDB</DatabaseVariableLiteralValue>
  <SuppressMissingDependenciesErrors>True</SuppressMissingDependenciesErrors>
</ArtifactReference>
```

Without references, SSDT will fail to build if you reference objects it can't find.

---

## Build vs. Deploy

These are different operations:

### Build

**What happens:**
- Compiles the project
- Validates syntax
- Checks referential integrity (do FKs point to real tables?)
- Produces a `.dacpac` file

**When it runs:**
- Every time you build in Visual Studio
- In CI pipeline on every commit

**What it catches:**
- Syntax errors
- Missing references (FK to non-existent table)
- Type mismatches
- Duplicate object names

**What it doesn't catch:**
- Data issues (NULLs in a column you're making NOT NULL)
- Runtime performance
- Blocking behavior

### Deploy (Publish)

**What happens:**
- Takes the `.dacpac` (desired state)
- Connects to target database (current state)
- Computes the delta
- Generates deployment script
- Executes the script (or just generates, depending on settings)

**When it runs:**
- Manually from Visual Studio (Publish)
- In CD pipeline on merge to main
- Via SqlPackage command line

**What it catches:**
- Data violations (constraint failures)
- Permission issues
- Timeout/blocking (if it takes too long)

---

## Navigating the Project: Quick Reference

| I need to... | Go to... |
|--------------|----------|
| See/edit a table structure | `/Tables/{schema}/{schema}.{TableName}.sql` |
| Add a new table | Create file in `/Tables/{schema}/`, add to project |
| Add a column | Edit the table's `.sql` file, add the column |
| Add an index | Create in `/Indexes/` or add inline to table file |
| Add a view | Create file in `/Views/{schema}/` |
| Add seed data | Add to post-deployment script in `/Scripts/PostDeployment/Migrations/` |
| See deployment settings | Open `.publish.xml` files |
| Find rename history | Check `.refactorlog` |
| See what's in the project | Open `.sqlproj` in text editor, or view in Solution Explorer |

---

## Your First Navigation Exercise

Before making any changes, orient yourself:

1. **Open the project in Visual Studio**
2. **Expand the Tables folder** — browse a few table definitions
3. **Find a table with foreign keys** — see how they're defined
4. **Open PostDeployment.sql** — see how it's structured
5. **Open a publish profile** — review the settings
6. **Build the project** — verify it compiles
7. **Do a Schema Compare** — compare your project to your local database

This gives you a mental map before you start making changes.

---

# 6. Pre-Deployment and Post-Deployment Scripts

---

## When Declarative Isn't Enough

SSDT's declarative model handles structure beautifully. But databases have *data*, and data often needs transformation that SSDT can't express declaratively.

**SSDT can:**
- Add a column
- Change a column's type
- Add a constraint

**SSDT cannot:**
- Know what value to put in a new NOT NULL column for existing rows
- Transform existing data from one format to another
- Seed reference data
- Clean up orphan records before adding an FK

That's what pre-deployment and post-deployment scripts are for.

---

## The Execution Order

```
┌─────────────────────────────────────────────────────────────────────────┐
│  1. PRE-DEPLOYMENT SCRIPT                                               │
│     - Runs BEFORE any schema changes                                    │
│     - Database is still in "old" state                                  │
│     - Use for: preparing data, dropping blockers                        │
├─────────────────────────────────────────────────────────────────────────┤
│  2. SCHEMA CHANGES (SSDT-generated)                                     │
│     - All the ALTER TABLE, CREATE INDEX, etc.                           │
│     - Database transitions from old state to new state                  │
├─────────────────────────────────────────────────────────────────────────┤
│  3. POST-DEPLOYMENT SCRIPT                                              │
│     - Runs AFTER schema changes complete                                │
│     - Database is now in "new" state                                    │
│     - Use for: data migration, seeding, backfill                        │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Pre-Deployment Scripts

### When to Use

Pre-deployment scripts run *before* SSDT changes the schema. Use them when:

- **Something blocks the schema change:** A constraint or index prevents the ALTER
- **Data must be cleaned first:** You need to remove violating rows before adding a constraint
- **Dependencies must be dropped:** A view or proc must be dropped before the column it references

### Examples

**Backfill NULLs before adding NOT NULL constraint:**

```sql
-- PreDeployment.sql (or included file)

-- We're about to make MiddleName NOT NULL
-- First, backfill any existing NULLs
PRINT 'Pre-deployment: Backfilling NULL MiddleName values...'

IF EXISTS (SELECT 1 FROM dbo.Person WHERE MiddleName IS NULL)
BEGIN
    UPDATE dbo.Person 
    SET MiddleName = '' 
    WHERE MiddleName IS NULL
    
    PRINT 'Backfilled ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows.'
END
ELSE
BEGIN
    PRINT 'No NULL MiddleName values found — skipping.'
END
GO
```

**Remove orphan data before adding FK:**

```sql
-- We're about to add FK_Order_Customer
-- First, clean up any orphan orders
PRINT 'Pre-deployment: Removing orphan orders...'

DELETE FROM dbo.[Order]
WHERE CustomerId NOT IN (SELECT CustomerId FROM dbo.Customer)

PRINT 'Removed ' + CAST(@@ROWCOUNT AS VARCHAR) + ' orphan orders.'
GO
```

### Structure

Keep pre-deployment scripts lean. They should:
- Do only what's necessary to unblock the schema change
- Be idempotent (safe to run multiple times)
- Print progress for debugging

---

## Post-Deployment Scripts

### When to Use

Post-deployment scripts run *after* SSDT has changed the schema. Use them when:

- **Data needs migration:** Converting data from old format to new
- **New columns need values:** Populating a new column from existing data
- **Reference data needs seeding:** Lookup tables need their initial values
- **One-time fixes:** Corrections that apply to this release only

### The Hybrid Structure

We use a structured approach that balances auditability with cleanliness:

```
/Scripts/PostDeployment/
    PostDeployment.sql              ← Master script (includes others)
    /Migrations/                    ← Permanent, idempotent
        001_BackfillCreatedAt.sql
        002_PopulateStatusLookup.sql
        003_MigrateAddressData.sql
    /ReferenceData/                 ← Permanent, idempotent
        SeedCountries.sql
        SeedStatusCodes.sql
        SeedProductTypes.sql
    /OneTime/                       ← Removed after prod deploy
        Release_2025.02_DataFixes.sql
```

**Master script:**

```sql
-- PostDeployment.sql

/*
Post-Deployment Script
======================
This file runs after schema deployment completes.
Add new migration scripts using :r includes.
All scripts must be idempotent.
*/

PRINT '========================================'
PRINT 'Starting post-deployment scripts'
PRINT '========================================'

-- Permanent migrations (idempotent, cumulative)
PRINT 'Running migrations...'
:r .\Migrations\001_BackfillCreatedAt.sql
:r .\Migrations\002_PopulateStatusLookup.sql
:r .\Migrations\003_MigrateAddressData.sql

-- Reference data (idempotent)
PRINT 'Seeding reference data...'
:r .\ReferenceData\SeedCountries.sql
:r .\ReferenceData\SeedStatusCodes.sql
:r .\ReferenceData\SeedProductTypes.sql

-- One-time scripts for current release
-- Remove these after successful prod deployment
PRINT 'Running one-time release scripts...'
:r .\OneTime\Release_2025.02_DataFixes.sql

PRINT '========================================'
PRINT 'Post-deployment complete'
PRINT '========================================'
GO
```

### Migration Scripts (Permanent)

These stay in the project forever. They must be idempotent — safe to run multiple times.

**Example: Backfill a new column**

```sql
-- /Migrations/001_BackfillCreatedAt.sql

/*
Migration: Backfill CreatedAt column
Ticket: JIRA-1234
Author: Danny
Date: 2025-01-15

This migration populates the new CreatedAt column for existing records.
Uses OrderDate as a proxy where available, otherwise uses a default.
*/

PRINT 'Migration 001: Backfill CreatedAt...'

IF EXISTS (SELECT 1 FROM dbo.[Order] WHERE CreatedAt IS NULL)
BEGIN
    UPDATE dbo.[Order]
    SET CreatedAt = ISNULL(OrderDate, '2020-01-01')
    WHERE CreatedAt IS NULL
    
    PRINT '  Updated ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows.'
END
ELSE
BEGIN
    PRINT '  No NULL CreatedAt values — skipping.'
END
GO
```

**Example: Seed a lookup table**

```sql
-- /ReferenceData/SeedStatusCodes.sql

/*
Reference Data: Order Status codes
*/

PRINT 'Seeding OrderStatus reference data...'

-- Use MERGE for idempotent upsert
MERGE INTO dbo.OrderStatus AS target
USING (VALUES
    (1, 'Pending', 1),
    (2, 'Processing', 2),
    (3, 'Shipped', 3),
    (4, 'Delivered', 4),
    (5, 'Cancelled', 5)
) AS source (StatusId, StatusName, SortOrder)
ON target.StatusId = source.StatusId
WHEN MATCHED THEN
    UPDATE SET StatusName = source.StatusName, SortOrder = source.SortOrder
WHEN NOT MATCHED THEN
    INSERT (StatusId, StatusName, SortOrder)
    VALUES (source.StatusId, source.StatusName, source.SortOrder);

PRINT '  OrderStatus seeded/updated.'
GO
```

### One-Time Scripts (Transient)

These are for release-specific work. After successful production deployment, they're removed (moved to git history only).

```sql
-- /OneTime/Release_2025.02_DataFixes.sql

/*
One-Time Script: Release 2025.02 data corrections
Remove after production deployment.

Ticket: JIRA-1456
Description: Fix incorrectly migrated phone numbers from legacy import
*/

PRINT 'One-time fix: Correcting phone number format...'

UPDATE dbo.Customer
SET PhoneNumber = '+1' + PhoneNumber
WHERE PhoneNumber NOT LIKE '+%'
  AND LEN(PhoneNumber) = 10

PRINT '  Updated ' + CAST(@@ROWCOUNT AS VARCHAR) + ' phone numbers.'
GO
```

---

## Idempotency Patterns

Every permanent script must be safe to run multiple times. Here's how:

### Pattern 1: Check-Before-Act

```sql
-- Only update rows that need it
IF EXISTS (SELECT 1 FROM dbo.Person WHERE MiddleName IS NULL)
BEGIN
    UPDATE dbo.Person SET MiddleName = '' WHERE MiddleName IS NULL
END
```

### Pattern 2: MERGE for Upserts

```sql
-- Insert or update in one statement
MERGE INTO dbo.Country AS target
USING (VALUES ('US', 'United States'), ('CA', 'Canada')) AS source (Code, Name)
ON target.CountryCode = source.Code
WHEN MATCHED THEN UPDATE SET CountryName = source.Name
WHEN NOT MATCHED THEN INSERT (CountryCode, CountryName) VALUES (source.Code, source.Name);
```

### Pattern 3: NOT EXISTS Guard

```sql
-- Only insert if not already there
IF NOT EXISTS (SELECT 1 FROM dbo.Country WHERE CountryCode = 'US')
BEGIN
    INSERT INTO dbo.Country (CountryCode, CountryName) VALUES ('US', 'United States')
END
```

### Pattern 4: Migration Tracking Table

For complex migrations where simple checks aren't enough:

```sql
-- Check if this migration has run
IF NOT EXISTS (SELECT 1 FROM dbo.MigrationHistory WHERE MigrationId = '003_MigrateAddressData')
BEGIN
    -- Do the migration work
    -- ... complex operations ...
    
    -- Mark as complete
    INSERT INTO dbo.MigrationHistory (MigrationId, ExecutedAt, ExecutedBy)
    VALUES ('003_MigrateAddressData', SYSUTCDATETIME(), SYSTEM_USER)
END
```

**The MigrationHistory table:**

```sql
CREATE TABLE [dbo].[MigrationHistory]
(
    MigrationId NVARCHAR(200) NOT NULL,
    ExecutedAt DATETIME2(7) NOT NULL,
    ExecutedBy NVARCHAR(128) NOT NULL,
    CONSTRAINT PK_MigrationHistory PRIMARY KEY (MigrationId)
)
```

### Testing Idempotency

Before committing, ask: "If I run this twice, what happens?"

- **Good:** Second run does nothing (conditions not met)
- **Bad:** Second run fails (duplicate key)
- **Worse:** Second run corrupts data (double-update)

---

## Common Mistakes

| Mistake | What happens | Fix |
|---------|--------------|-----|
| Non-idempotent INSERT | Duplicate key error on second run | Use `IF NOT EXISTS` or `MERGE` |
| UPDATE without WHERE | All rows updated, including already-correct ones | Add condition to skip already-updated rows |
| Assuming column exists | Script fails if run before schema change | Use pre-deployment for schema-dependent work, post-deployment for new-schema work |
| No progress output | Hard to debug when something fails | Add `PRINT` statements |
| Giant single script | Hard to maintain, hard to debug | Break into focused, included files |

---

## Pre-Deployment vs. Post-Deployment Decision Guide

| Scenario | Use | Why |
|----------|-----|-----|
| Backfill NULLs before NOT NULL constraint | Pre-deployment | Must happen before schema change |
| Clean orphans before adding FK | Pre-deployment | Must happen before constraint exists |
| Drop blocking index for column type change | Pre-deployment | Index prevents ALTER |
| Populate new column from existing data | Post-deployment | New column must exist first |
| Seed lookup table | Post-deployment | Table must exist first |
| Transform data to new format | Post-deployment | New structure must exist |
| One-time data fix | Post-deployment | Usually schema-independent |

---

Let me continue with the remaining consolidated Foundations sections.

---

# 7. Idempotency 101

---

## Why Idempotency Matters

An idempotent operation produces the same result whether you run it once or many times.

In the context of deployment scripts:
- **Fresh environment:** Script runs for the first time, does the work
- **Existing environment:** Script runs again, recognizes work is done, does nothing harmful
- **Retry after failure:** Script runs again after a mid-deploy crash, completes without errors

**Without idempotency:**
- Fresh deploys work
- Redeployments fail or corrupt data
- You can't safely retry failed deployments

---

## The Core Patterns

### Pattern 1: Existence Check (Most Common)

Check if the work has already been done.

```sql
-- Adding data
IF NOT EXISTS (SELECT 1 FROM dbo.Country WHERE CountryCode = 'US')
BEGIN
    INSERT INTO dbo.Country (CountryCode, CountryName) VALUES ('US', 'United States')
END

-- Updating data
IF EXISTS (SELECT 1 FROM dbo.Person WHERE MiddleName IS NULL)
BEGIN
    UPDATE dbo.Person SET MiddleName = '' WHERE MiddleName IS NULL
END

-- Deleting data
IF EXISTS (SELECT 1 FROM dbo.TempData WHERE ProcessedDate < '2024-01-01')
BEGIN
    DELETE FROM dbo.TempData WHERE ProcessedDate < '2024-01-01'
END
```

### Pattern 2: MERGE (Upsert)

Insert if missing, update if exists — in one atomic statement.

```sql
MERGE INTO dbo.OrderStatus AS target
USING (VALUES
    (1, 'Pending'),
    (2, 'Active'),
    (3, 'Complete')
) AS source (Id, Name)
ON target.StatusId = source.Id
WHEN MATCHED THEN
    UPDATE SET StatusName = source.Name
WHEN NOT MATCHED THEN
    INSERT (StatusId, StatusName) VALUES (source.Id, source.Name);
```

### Pattern 3: Conditional UPDATE with Filtering

Update only rows that need it — don't touch already-correct rows.

```sql
-- Bad: Updates all rows, even if already correct
UPDATE dbo.Customer SET IsActive = 1

-- Good: Only updates rows that need changing
UPDATE dbo.Customer SET IsActive = 1 WHERE IsActive = 0 OR IsActive IS NULL
```

### Pattern 4: Migration Tracking Table

For complex, multi-step migrations that can't be easily checked.

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.MigrationHistory WHERE MigrationId = 'MIG_2025.01_SplitAddress')
BEGIN
    -- Complex migration logic
    INSERT INTO dbo.Address (CustomerId, Street, City, State, PostalCode)
    SELECT CustomerId, AddressLine1, City, StateCode, ZipCode
    FROM dbo.Customer
    WHERE AddressLine1 IS NOT NULL
    
    -- Mark complete
    INSERT INTO dbo.MigrationHistory (MigrationId, ExecutedAt, ExecutedBy)
    VALUES ('MIG_2025.01_SplitAddress', SYSUTCDATETIME(), SYSTEM_USER)
END
```

---

## Testing Your Idempotency

Before committing any script, run this mental test:

1. **Run it once** — Does it do the intended work?
2. **Run it again immediately** — Does it fail? Does it duplicate data? Does it do nothing?
3. **Run it after partial completion** — If it failed mid-way, does re-running complete the work?

**Automated check:** In your local dev process, deploy twice in a row. Both should succeed without errors.

---

## Common Idempotency Failures

| Failure | Symptom | Fix |
|---------|---------|-----|
| Missing existence check | Duplicate key error | Add `IF NOT EXISTS` |
| UPDATE without condition | Data changed on every run | Add `WHERE` clause filtering already-updated rows |
| IDENTITY_INSERT conflicts | Insert fails on second run | Use existence check or MERGE |
| Hardcoded values + auto-increment | Different IDs in different environments | Use explicit IDs for reference data, or use MERGE on natural key |
| Cumulative operations | Values keep growing | Make the operation set to a specific value, not increment |

---

## Idempotency Checklist

Before committing a pre/post-deployment script:

- [ ] Wrapped in existence check or uses MERGE
- [ ] UPDATEs filter to only rows needing change
- [ ] INSERTs check for existing records
- [ ] DELETEs can safely run when rows already gone
- [ ] Tested by running deploy twice locally
- [ ] Includes PRINT statements for observability

---

# 8. Referential Integrity Basics

---

## What Foreign Keys Actually Enforce

A foreign key constraint says: "Every value in this column must exist in that other table's column."

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

This means:
- **INSERT to Order:** CustomerId must exist in Customer
- **UPDATE Order.CustomerId:** New value must exist in Customer
- **DELETE from Customer:** Fails if Orders reference that Customer (default behavior)

---

## The Dependency Graph

Foreign keys create dependencies. Understanding the graph matters for:
- **Insert order:** Parent must exist before child
- **Delete order:** Children must be removed before parent
- **Drop order:** Can't drop parent table if FKs point to it

```
Customer (parent)
    │
    └──► Order (child)
            │
            └──► OrderLine (grandchild)
```

**Insert:** Customer → Order → OrderLine (parent first)
**Delete:** OrderLine → Order → Customer (children first)

---

## CASCADE Options

You control what happens when a parent row is deleted or updated:

| Option | On DELETE | On UPDATE |
|--------|-----------|-----------|
| `NO ACTION` (default) | Fail if children exist | Fail if children reference old value |
| `CASCADE` | Delete all children automatically | Update all children automatically |
| `SET NULL` | Set FK column to NULL in children | Set FK column to NULL |
| `SET DEFAULT` | Set FK column to default value | Set FK column to default value |

**Be cautious with CASCADE.** It's powerful but can cause surprising mass deletions.

---

## `WITH NOCHECK` and Trust

When you add an FK to a table with existing data, SQL Server validates all rows. If orphans exist, it fails.

You can skip validation:

```sql
ALTER TABLE dbo.[Order] WITH NOCHECK
ADD CONSTRAINT FK_Order_Customer 
FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
```

**But this creates an untrusted constraint:**
- New rows are validated
- Existing rows are not
- Query optimizer ignores untrusted constraints (can't use them for optimization)

**To regain trust:**

```sql
ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer
```

This validates all existing rows and marks the constraint as trusted.

---

## Finding Orphan Data

Before adding an FK, check for orphans:

```sql
-- Find orders with no matching customer
SELECT o.OrderId, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON o.CustomerId = c.CustomerId
WHERE c.CustomerId IS NULL
```

---

## SSDT and Referential Integrity

SSDT validates referential integrity at **build time**. If you define an FK to a table that doesn't exist in your project, build fails.

SSDT generates FK creation at **deploy time**. If orphan data exists, deploy fails (unless you use `WITH NOCHECK` via script).

---

# 9. The Refactorlog and Rename Discipline

*(This section consolidates the refactorlog content from earlier in the thread)*

---

## What the Refactorlog Is

The refactorlog is an XML file that tracks identity-preserving changes — specifically, renames.

When you rename a column in SSDT using the GUI:
- SSDT updates the column name in your `.sql` file
- SSDT adds an entry to the refactorlog

The refactorlog entry says: "This object used to be called X, now it's called Y. They're the same object."

---

## The Silent Catastrophe

Without a refactorlog entry, SSDT interprets a rename as:
- Old column: deleted
- New column: created (fresh, empty)

**Generated script without refactorlog:**
```sql
ALTER TABLE dbo.Person DROP COLUMN FirstName
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100) NULL
-- Data in FirstName? Gone.
```

**Generated script with refactorlog:**
```sql
EXEC sp_rename 'dbo.Person.FirstName', 'GivenName', 'COLUMN'
-- Data preserved.
```

---

## How to Rename Correctly

### Method 1: GUI Rename (Preferred)

1. In Solution Explorer or table designer, right-click the object
2. Select Rename
3. Enter new name
4. SSDT updates the file AND adds refactorlog entry

### Method 2: Manual Refactorlog Entry

If you've already edited the file directly, you can manually add the entry:

```xml
<Operation Name="Rename Refactor" Key="[unique-guid]" ChangeDateTime="2025-01-15T10:30:00">
  <Property Name="ElementName" Value="[dbo].[Person].[FirstName]" />
  <Property Name="ElementType" Value="SqlSimpleColumn" />
  <Property Name="ParentElementName" Value="[dbo].[Person]" />
  <Property Name="ParentElementType" Value="SqlTable" />
  <Property Name="NewName" Value="GivenName" />
</Operation>
```

But this is error-prone. Use the GUI.

---

## Protecting the Refactorlog

### Branch Merges

When two branches rename different objects, both add refactorlog entries. Merge conflicts in XML can be tricky.

**Resolution approach:**
- Keep BOTH entries (they're independent operations)
- Ensure GUIDs are unique
- Validate by building after merge

### Never Delete Entries

Refactorlog entries are needed for fresh environment deployments. Deleting "old" entries means fresh deployments treat those renames as drop-and-create.

**Even if the rename happened a year ago, keep the entry.**

### CI Validation

Consider adding a pipeline check:
- Detect column name changes in `.sql` files
- Verify corresponding refactorlog entry exists
- Fail PR if rename detected without refactorlog

---

# 10. SSDT Deployment Safety

*(This section consolidates the settings discussion from earlier)*

---

## The Publish Profile Settings That Matter

Your publish profile (`.publish.xml`) controls deployment behavior. These settings are your safety net.

---

## `BlockOnPossibleDataLoss`

**What it does:** If SSDT's generated script would drop a column containing data, drop a table with rows, or narrow a column, deployment fails instead of proceeding.

**Setting:** `True` — always, every environment, non-negotiable.

```xml
<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
```

**When it fires:**
- Dropping a column that has data
- Dropping a table that has rows
- Narrowing a column that contains values too long for new size
- Changing type in a way that can lose precision

**What to do when it fires:**
1. Stop. This is the system protecting you.
2. Review the generated script — what's it trying to do?
3. If the data loss is intentional, handle it explicitly in pre-deployment
4. If unintentional, fix your schema change

**Never set this to False for production deploys.** If you need to override, do it consciously via pre-deployment scripting, not by disabling the guard.

---

## `DropObjectsNotInSource`

**What it does:** When `True`, objects in the target database that aren't in your SSDT project are dropped. When `False`, they're ignored.

```xml
<DropObjectsNotInSource>False</DropObjectsNotInSource>
```

**Recommendations:**

| Environment | Setting | Rationale |
|-------------|---------|-----------|
| Dev | True | Keep it clean, catch issues early |
| Test | True | Should match prod process |
| UAT | False | Don't accidentally drop test fixtures |
| Prod | **False** | Never auto-drop in prod |

**For production:** If you want something gone, do it explicitly in the schema (delete the file) so it goes through PR review. Don't rely on SSDT's diff to clean up.

---

## `IgnoreColumnOrder`

**What it does:** When `True`, SSDT ignores differences in column order. When `False`, it will reorder columns to match the definition.

```xml
<IgnoreColumnOrder>True</IgnoreColumnOrder>
```

**Keep this True.** Column order changes can trigger full table rebuilds for cosmetic benefit. Not worth it.

---

## `GenerateSmartDefaults`

**What it does:** When adding a NOT NULL column, SSDT can auto-generate a default value to populate existing rows.

```xml
<GenerateSmartDefaults>False</GenerateSmartDefaults>
```

**Recommendation:**
- `True` for dev (velocity)
- `False` for test/UAT/prod (force explicit handling)

Smart defaults can mask problems. You may not want empty strings or zeros silently backfilled.

---

## Settings Matrix by Environment

| Setting | Dev | Test | UAT | Prod |
|---------|-----|------|-----|------|
| BlockOnPossibleDataLoss | True | True | True | **True** |
| DropObjectsNotInSource | True | True | False | **False** |
| IgnoreColumnOrder | True | True | True | True |
| GenerateSmartDefaults | True | True | False | **False** |
| AllowIncompatiblePlatform | False | False | False | False |
| TreatTSqlWarningsAsErrors | False | True | True | True |

---

## The Settings as Last Line of Defense

These settings are guardrails, not substitutes for good process.

```
Tier system (process) → catches most issues
    ↓
PR review → catches issues process missed
    ↓
Local testing → catches issues review missed
    ↓
Settings (BlockOnPossibleDataLoss, etc.) → catches what everything else missed
```

When a setting blocks you, that's the system working. Investigate, don't bypass.

---

I'll continue with Sections 11-12 (Multi-Phase Evolution and CDC), then move to the Execution Layer (Pattern Templates and Anti-Patterns), then Process.

---

# 11. Multi-Phase Evolution

---

## Why Some Changes Can't Be Atomic

Some schema changes can't safely happen in a single deployment:

- **Data dependencies:** New structure needs data from old structure
- **Application coordination:** Old and new code must coexist during transition
- **Risk management:** Each phase can be validated before proceeding
- **CDC constraints:** Audit continuity requires careful sequencing

**The fundamental pattern:** Create new → Migrate data → Remove old

---

## Phase-to-Release Mapping

Not all phases can share a release. The question is: "Can we safely rollback if something goes wrong after this phase?"

| Phase combination | Same release? | Rationale |
|-------------------|---------------|-----------|
| Create new structure + migrate data | Often yes | Rollback = drop new structure, data still in old |
| Migrate data + drop old structure | **No** | Rollback impossible — old structure is gone |
| Create + migrate | Maybe | Depends on data volume and complexity |
| Anything touching CDC | Often separate | Need to verify capture instances are correct |

**Rule of thumb:** If the phase is irreversible (data loss, structure removal), it should be a separate release with explicit verification before proceeding.

---

## Rollback Considerations

At each phase, know your rollback:

| Phase | Rollback approach |
|-------|-------------------|
| Created new column/table | Drop it |
| Migrated data to new structure | Leave it (or delete if needed) |
| Dropped old column/table | Restore from backup |
| Changed CDC instance | Recreate old instance (with gap) |

**Point of no return:** Once you drop the old structure, rollback requires backup restoration. Make sure you've validated before crossing that line.

---

## Multi-Phase Operations Catalog

These operations typically require multi-phase treatment:

| Operation | Why Multi-Phase | See Pattern |
|-----------|-----------------|-------------|
| Explicit data type conversion | Data must transform; old and new must coexist | 17.1 |
| NULL → NOT NULL on populated table | Existing NULLs need values first | 17.2 |
| Add/remove IDENTITY | Can't ALTER to add IDENTITY; requires table swap | 17.3 |
| Add FK with orphan data | Need to clean data or use NOCHECK→trust sequence | 17.4 |
| Safe column removal | Verify unused before dropping | 17.5 |
| Table split | New structure + data migration + app coordination | 17.6 |
| Table merge | Same as split, reverse direction | 17.7 |
| Rename with compatibility | Old name must keep working during transition | 17.8 |
| CDC-enabled table schema change | Capture instance management | 17.9 |

---

# 12. CDC and Schema Evolution

*(Consolidating from earlier discussion)*

---

## The Core Constraint

CDC capture instances are schema-bound. When you create a capture instance, it records the table's schema at that moment. Changes to the table don't automatically update the capture instance.

**Operations requiring instance recreation:**
- Add column (if you want it tracked)
- Drop column
- Rename column
- Change data type

**Operations that don't affect CDC:**
- Add/modify/drop constraints
- Add/modify/drop indexes
- Changes to non-CDC-enabled tables

---

## Development Strategy: Accept Gaps

In development, velocity matters more than audit completeness.

**Approach:**
1. Batch schema changes
2. Disable CDC on affected tables before deploy
3. Deploy schema changes
4. Re-enable CDC after deploy

**Automation template:**

```sql
-- Pre-deployment: Disable CDC on all tables
DECLARE @sql NVARCHAR(MAX) = ''
SELECT @sql += 'EXEC sys.sp_cdc_disable_table 
    @source_schema = ''' + OBJECT_SCHEMA_NAME(source_object_id) + ''', 
    @source_name = ''' + OBJECT_NAME(source_object_id) + ''', 
    @capture_instance = ''' + capture_instance + ''';'
FROM cdc.change_tables

EXEC sp_executesql @sql

-- [SSDT deployment happens]

-- Post-deployment: Re-enable CDC (from your table list)
-- ... enable scripts ...
```

**Accepted risks:**
- History gaps during development
- Change History feature shows incomplete data in dev/test
- Building habits that need adjustment for production

---

## UAT Strategy: Communicate Gaps

In UAT, clients will see the Change History feature. Set expectations.

**Client messaging template:**

> "The Change History feature tracks all modifications to records. During this testing phase:
> 
> 1. History starts from [date] — changes before that aren't captured.
> 2. During deployments, there may be brief gaps when changes aren't recorded.
> 3. New fields appear in history going forward only.
> 
> In production, we use a process that eliminates gaps."

**Practices:**
- Maintain a gap log
- Notify before deployments
- Smoke test Change History after each deploy

---

## Production Strategy: No Gaps

In production, use the dual-instance pattern.

**Phase sequence:**

```
Release N:
1. Create new capture instance with new schema
2. Apply schema change
3. Both instances active — consumer reads from both

Release N+1 (after retention window):
4. Drop old capture instance
5. Consumer reads only from new instance
```

**Consumer abstraction:**

Your Change History code should query through an abstraction that:
- Unions results from all active instances
- Handles schema differences (missing columns in old instance)
- Manages LSN ranges correctly

---

## CDC Table Registry

Maintain a list of CDC-enabled tables. Check it before any schema change.

**Query to find CDC-enabled tables:**

```sql
SELECT 
    OBJECT_SCHEMA_NAME(source_object_id) AS SchemaName,
    OBJECT_NAME(source_object_id) AS TableName,
    capture_instance AS CaptureInstance,
    create_date AS EnabledDate
FROM cdc.change_tables
ORDER BY SchemaName, TableName
```

**Before any schema change:** "Is this table on the list? If yes, follow CDC protocol."

---

Now let me continue with the Execution Layer — the Multi-Phase Pattern Templates and Anti-Patterns Gallery.

---

# 17. Multi-Phase Pattern Templates

---

## How to Use This Section

Each pattern provides:
- **When to use:** Conditions that trigger this pattern
- **Phase sequence:** The ordered steps across releases
- **Code templates:** Actual SQL for each phase
- **Rollback notes:** How to reverse if needed
- **Verification queries:** How to confirm each phase succeeded

---

## 17.1 Pattern: Explicit Conversion Data Type Change

**When to use:** Changing a column's data type when SQL Server can't implicitly convert (e.g., VARCHAR → DATE, INT → UNIQUEIDENTIFIER)

**Scenario:** Convert `PolicyDate` from `VARCHAR(10)` to `DATE`

### Phase 1 (Release N): Add New Column

```sql
-- Declarative: Add to table definition
[PolicyDateNew] DATE NULL,
```

### Phase 2 (Release N): Migrate Data (Post-Deployment)

```sql
-- PostDeployment script
PRINT 'Migrating PolicyDate to DATE type...'

UPDATE dbo.Policy
SET PolicyDateNew = TRY_CONVERT(DATE, PolicyDate, 101)  -- MM/DD/YYYY format
WHERE PolicyDateNew IS NULL
  AND PolicyDate IS NOT NULL

-- Log failures
INSERT INTO dbo.MigrationLog (TableName, ColumnName, FailedValue, FailureReason)
SELECT 'Policy', 'PolicyDate', PolicyDate, 'Invalid date format'
FROM dbo.Policy
WHERE PolicyDateNew IS NULL 
  AND PolicyDate IS NOT NULL

PRINT 'Migration complete. Check MigrationLog for failures.'
```

### Phase 3 (Release N+1): Application Transition

Application code switches from `PolicyDate` to `PolicyDateNew`. Both columns exist during this phase.

### Phase 4 (Release N+2): Remove Old, Rename New

```sql
-- Pre-deployment: Verify migration complete
IF EXISTS (SELECT 1 FROM dbo.Policy WHERE PolicyDate IS NOT NULL AND PolicyDateNew IS NULL)
BEGIN
    RAISERROR('Migration incomplete — some PolicyDate values not converted', 16, 1)
    RETURN
END
```

```sql
-- Declarative: Remove old column, rename new column (use refactorlog for rename)
-- After this release:
[PolicyDate] DATE NULL,  -- This is the renamed PolicyDateNew
```

**Rollback notes:**
- Phase 1-2: Drop new column, no data loss
- Phase 3: Revert application code
- Phase 4: Requires backup restore (old column is gone)

**Verification:**
```sql
-- After Phase 2: Check conversion success rate
SELECT 
    COUNT(*) AS TotalRows,
    SUM(CASE WHEN PolicyDateNew IS NOT NULL THEN 1 ELSE 0 END) AS Converted,
    SUM(CASE WHEN PolicyDateNew IS NULL AND PolicyDate IS NOT NULL THEN 1 ELSE 0 END) AS Failed
FROM dbo.Policy
```

---

## 17.2 Pattern: NULL → NOT NULL on Populated Table

**When to use:** Making an existing nullable column required

**Scenario:** Make `Customer.Email` NOT NULL

### Phase 1 (Release N): Backfill (Pre-Deployment)

```sql
-- PreDeployment script
PRINT 'Backfilling NULL emails...'

-- Option A: Default value
UPDATE dbo.Customer
SET Email = 'unknown@placeholder.com'
WHERE Email IS NULL

-- Option B: Derive from other data
UPDATE dbo.Customer
SET Email = LOWER(FirstName) + '.' + LOWER(LastName) + '@unknown.com'
WHERE Email IS NULL

PRINT 'Backfill complete.'
```

### Phase 2 (Release N): Apply Constraint (Declarative)

```sql
-- Table definition change
[Email] NVARCHAR(200) NOT NULL,  -- Changed from NULL
```

**If you must do it in one release:** Combine pre-deployment backfill with declarative constraint. SSDT will apply the constraint after pre-deployment runs.

**Rollback notes:**
- Change constraint back to NULL
- Backfilled data remains (but that's usually fine)

**Verification:**
```sql
-- Before Phase 2: Confirm no NULLs remain
SELECT COUNT(*) AS NullEmailCount
FROM dbo.Customer
WHERE Email IS NULL
-- Must be 0
```

---

## 17.3 Pattern: Add/Remove IDENTITY Property

**When to use:** Adding auto-increment to an existing column, or removing it

**Scenario:** Convert `PolicyId INT` to `PolicyId INT IDENTITY(1,1)`

You cannot `ALTER TABLE` to add IDENTITY. This requires a table swap.

### Phase 1 (Release N): Create New Table (Pre-Deployment Script)

```sql
-- PreDeployment script
PRINT 'Creating new Policy table with IDENTITY...'

-- Create new table structure
CREATE TABLE dbo.Policy_New
(
    PolicyId INT IDENTITY(1,1) NOT NULL,
    PolicyNumber NVARCHAR(50) NOT NULL,
    CustomerId INT NOT NULL,
    -- ... all other columns ...
    CONSTRAINT PK_Policy_New PRIMARY KEY CLUSTERED (PolicyId)
)

-- Copy data with IDENTITY_INSERT
SET IDENTITY_INSERT dbo.Policy_New ON

INSERT INTO dbo.Policy_New (PolicyId, PolicyNumber, CustomerId /*, ... */)
SELECT PolicyId, PolicyNumber, CustomerId /*, ... */
FROM dbo.Policy

SET IDENTITY_INSERT dbo.Policy_New OFF

-- Reseed to max + 1
DECLARE @MaxId INT = (SELECT MAX(PolicyId) FROM dbo.Policy_New)
DBCC CHECKIDENT ('dbo.Policy_New', RESEED, @MaxId)

PRINT 'Data migrated to new table.'
```

### Phase 2 (Release N): Swap Tables (Pre-Deployment, continued)

```sql
-- Drop FKs pointing to old table
ALTER TABLE dbo.Claim DROP CONSTRAINT FK_Claim_Policy
-- ... other FKs ...

-- Swap
DROP TABLE dbo.Policy
EXEC sp_rename 'dbo.Policy_New', 'Policy'

-- Recreate FKs
ALTER TABLE dbo.Claim ADD CONSTRAINT FK_Claim_Policy
    FOREIGN KEY (PolicyId) REFERENCES dbo.Policy(PolicyId)

PRINT 'Table swap complete.'
```

### Phase 3: Declarative Definition Matches

Your declarative table definition now shows IDENTITY:

```sql
CREATE TABLE [dbo].[Policy]
(
    [PolicyId] INT IDENTITY(1,1) NOT NULL,
    -- ...
)
```

**Rollback notes:**
- This is largely atomic if done in pre-deployment
- Full rollback = restore from backup
- Test thoroughly in lower environments

**Verification:**
```sql
-- Confirm IDENTITY is set
SELECT 
    COLUMNPROPERTY(OBJECT_ID('dbo.Policy'), 'PolicyId', 'IsIdentity') AS IsIdentity
-- Should be 1

-- Confirm row counts match
SELECT 
    (SELECT COUNT(*) FROM dbo.Policy) AS NewCount
-- Should match original
```

---

## 17.4 Pattern: Add FK with Orphan Data

**When to use:** Adding a foreign key when orphan records exist that you can't immediately delete

**Scenario:** Add `FK_Order_Customer` but some orders have invalid `CustomerId` values

### Phase 1 (Release N): Add FK as Untrusted

```sql
-- PostDeployment script (not declarative — SSDT doesn't support NOCHECK directly)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Order_Customer')
BEGIN
    ALTER TABLE dbo.[Order] WITH NOCHECK
    ADD CONSTRAINT FK_Order_Customer 
        FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
    
    PRINT 'FK created as untrusted.'
END
```

### Phase 2 (Release N or N+1): Clean Orphan Data

```sql
-- PostDeployment script
PRINT 'Cleaning orphan orders...'

-- Option A: Delete orphans
DELETE FROM dbo.[Order]
WHERE CustomerId NOT IN (SELECT CustomerId FROM dbo.Customer)

-- Option B: Create placeholder customer for orphans
IF NOT EXISTS (SELECT 1 FROM dbo.Customer WHERE CustomerId = -1)
BEGIN
    SET IDENTITY_INSERT dbo.Customer ON
    INSERT INTO dbo.Customer (CustomerId, FirstName, LastName, Email)
    VALUES (-1, 'Unknown', 'Customer', 'orphan@placeholder.com')
    SET IDENTITY_INSERT dbo.Customer OFF
END

UPDATE dbo.[Order]
SET CustomerId = -1
WHERE CustomerId NOT IN (SELECT CustomerId FROM dbo.Customer)

PRINT 'Orphans handled.'
```

### Phase 3 (Release N+1 or N+2): Enable Trust

```sql
-- PostDeployment script
PRINT 'Enabling FK trust...'

ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer

PRINT 'FK is now trusted.'
```

### Phase 4: Declarative Definition

Add the FK to your declarative table definition. SSDT will see it already exists and matches.

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

**Verification:**
```sql
-- Check trust status
SELECT name, is_not_trusted
FROM sys.foreign_keys
WHERE name = 'FK_Order_Customer'
-- is_not_trusted should be 0 after Phase 3
```

---

## 17.5 Pattern: Safe Column Removal (4-Phase)

**When to use:** Removing a column safely with full verification

**Scenario:** Remove `Customer.LegacyId` that's no longer used

### Phase 1 (Release N): Soft Deprecate

Document the deprecation. Optionally rename:

```sql
-- Declarative: Rename to signal deprecation
[__deprecated_LegacyId] INT NULL,  -- Was LegacyId, use refactorlog
```

Or just add documentation/comments without schema change.

### Phase 2 (Release N): Stop Writes

Application code change — stop writing to this column. No schema change.

### Phase 3 (Release N+1): Verify Unused

```sql
-- Verification query (run manually, not in deployment)
-- Check for recent writes
SELECT MAX(UpdatedAt) AS LastWrite
FROM dbo.Customer
WHERE LegacyId IS NOT NULL

-- Check for code references (search codebase)
-- Check for report/ETL references (ask stakeholders)
```

Only proceed when confident column is truly unused.

### Phase 4 (Release N+2): Drop Column

```sql
-- Declarative: Remove from table definition
-- Column is simply gone from the CREATE TABLE statement
```

**Rollback notes:**
- Phase 1-3: Fully reversible
- Phase 4: Requires backup restore

---

## 17.6 Pattern: Table Split (Vertical Partitioning)

**When to use:** Extracting columns from one table into a new related table

**Scenario:** Extract address columns from `Customer` into `CustomerAddress`

### Phase 1 (Release N): Create New Table

```sql
-- Declarative: New table file
CREATE TABLE [dbo].[CustomerAddress]
(
    [CustomerAddressId] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [Street] NVARCHAR(200) NULL,
    [City] NVARCHAR(100) NULL,
    [State] NVARCHAR(50) NULL,
    [PostalCode] NVARCHAR(20) NULL,
    
    CONSTRAINT [PK_CustomerAddress] PRIMARY KEY CLUSTERED ([CustomerAddressId]),
    CONSTRAINT [FK_CustomerAddress_Customer] FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customer]([CustomerId])
)
```

### Phase 2 (Release N): Migrate Data (Post-Deployment)

```sql
-- PostDeployment script
PRINT 'Migrating address data...'

INSERT INTO dbo.CustomerAddress (CustomerId, Street, City, State, PostalCode)
SELECT CustomerId, AddressStreet, AddressCity, AddressState, AddressPostalCode
FROM dbo.Customer
WHERE AddressStreet IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.CustomerAddress ca WHERE ca.CustomerId = dbo.Customer.CustomerId)

PRINT 'Address data migrated.'
```

### Phase 3 (Multiple Releases): Application Transition

Application gradually shifts from `Customer.AddressX` to `CustomerAddress.X`. This may take multiple releases.

### Phase 4 (Release N+X): Drop Old Columns

```sql
-- Declarative: Remove address columns from Customer table definition
-- Columns are simply gone
```

**Rollback notes:**
- Phase 1-3: Drop new table, data still in original
- Phase 4: Requires backup restore

---

## 17.9 Pattern: CDC-Enabled Table Schema Change (Production)

**When to use:** Changing schema on a CDC-enabled table without audit gaps

**Scenario:** Add `MiddleName` column to CDC-enabled `Employee` table

### Phase 1 (Release N): Create New Capture Instance

```sql
-- PostDeployment script
PRINT 'Creating new CDC capture instance for Employee...'

-- Create new instance with new schema (after column is added)
EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'Employee',
    @capture_instance = 'dbo_Employee_v2',  -- Versioned name
    @role_name = 'cdc_reader',
    @supports_net_changes = 1

PRINT 'New capture instance created. Both v1 and v2 are now active.'
```

### Phase 2 (Release N): Add Column (Declarative)

```sql
-- Table definition change
[MiddleName] NVARCHAR(50) NULL,
```

The new capture instance (v2) tracks this column. The old instance (v1) doesn't know about it.

### Phase 3 (Release N): Update Consumer Abstraction

Change History code now queries both instances and unions results.

### Phase 4 (Release N+1, after retention): Drop Old Instance

```sql
-- PostDeployment script (after retention period)
PRINT 'Dropping old CDC capture instance...'

EXEC sys.sp_cdc_disable_table
    @source_schema = 'dbo',
    @source_name = 'Employee',
    @capture_instance = 'dbo_Employee_v1'

PRINT 'Old capture instance dropped.'
```

**Rollback notes:**
- Phase 1-3: Drop new instance, keep old
- Phase 4: Cannot restore dropped instance (but data is beyond retention anyway)

---

# 19. Anti-Patterns Gallery

---

## 19.1 The Naked Rename

**What it looks like:**

Developer opens `dbo.Person.sql`, manually changes column name:

```sql
-- Before
[FirstName] NVARCHAR(100) NOT NULL

-- After (just edited the text)
[GivenName] NVARCHAR(100) NOT NULL
```

**What happens:**

SSDT sees: "FirstName is gone. GivenName is new."

Generated script:
```sql
ALTER TABLE dbo.Person DROP COLUMN FirstName
ALTER TABLE dbo.Person ADD GivenName NVARCHAR(100) NULL
```

All data in FirstName is lost.

**The fix:**

Always use SSDT's GUI rename (right-click → Rename). This creates a refactorlog entry that tells SSDT "these are the same column."

**Visual cue:** If your `.refactorlog` file didn't change but a column name did, something is wrong.

---

## 19.2 The Optimistic NOT NULL

**What it looks like:**

Developer adds NOT NULL column without considering existing data:

```sql
-- Adding to table definition
[MiddleName] NVARCHAR(50) NOT NULL,  -- No default!
```

**What happens:**

- Build succeeds (SSDT doesn't know about your data)
- Deploy fails: "Cannot insert NULL into column 'MiddleName'"
- Or, with `GenerateSmartDefaults=True`, SSDT silently backfills empty strings

**The fix:**

Either:
1. Add with a default: `NOT NULL CONSTRAINT DF_Person_MiddleName DEFAULT ('')`
2. Add as NULL, backfill in post-deployment, then alter to NOT NULL in next release

**Rule of thumb:** NOT NULL on existing table = think about existing rows first.

---

## 19.3 The Forgotten FK Check

**What it looks like:**

Developer adds FK without checking for orphan data:

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

**What happens:**

If any `Order.CustomerId` value doesn't exist in `Customer.CustomerId`:
- Deploy fails with constraint violation
- Or, if using `WITH NOCHECK`, constraint is untrusted (optimizer ignores it)

**The fix:**

Before adding FK, run:
```sql
SELECT o.OrderId, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON o.CustomerId = c.CustomerId
WHERE c.CustomerId IS NULL
```

Clean up orphans first, or use the WITH NOCHECK → clean → trust pattern.

---

## 19.4 The Ambitious Narrowing

**What it looks like:**

Developer narrows a column without checking data:

```sql
-- Before
[Email] NVARCHAR(200)

-- After
[Email] NVARCHAR(100)  -- Narrowed!
```

**What happens:**

- If any email is > 100 characters: Deploy fails (BlockOnPossibleDataLoss)
- If BlockOnPossibleDataLoss is off: Data truncation, silent corruption

**The fix:**

Before narrowing, verify:
```sql
SELECT MAX(LEN(Email)) AS MaxLength, COUNT(*) AS OverLimit
FROM dbo.Person
WHERE LEN(Email) > 100  -- New limit
```

If data exceeds new limit, clean it first or reconsider the change.

---

## 19.5 The CDC Surprise

**What it looks like:**

Developer changes schema on CDC-enabled table without considering capture instance:

```sql
-- Just adds a column like normal
[NewColumn] NVARCHAR(50) NULL,
```

**What happens:**

- Column added to table
- Existing capture instance doesn't include it
- Change History won't show changes to NewColumn
- Stale capture instance causes confusion

**The fix:**

Check if table is CDC-enabled first. If yes, follow CDC change protocol:
- Development: Disable/re-enable CDC (accepting gap)
- Production: Create new capture instance, manage dual-instance transition

---

## 19.6 The Refactorlog Cleanup

**What it looks like:**

Developer sees old refactorlog entries:
"We renamed that column two years ago. Why is this still here? Cleaning it up."

Deletes the entry.

**What happens:**

- Existing environments: Fine (column already renamed there)
- Fresh environment deployment: SSDT treats the old rename as drop+create
- Data loss in fresh environments

**The fix:**

Never delete refactorlog entries. They're needed for fresh environment deployments. They're small. Leave them.

---

## 19.7 The SELECT * View

**What it looks like:**

Developer creates view with SELECT *:

```sql
CREATE VIEW dbo.vw_AllCustomers
AS
SELECT * FROM dbo.Customer
```

**What happens:**

- View created with columns that exist *at creation time*
- Later: Column added to Customer
- View doesn't automatically include new column
- Queries against view miss data
- Confusion: "I added the column, why isn't it showing?"

**The fix:**

Always enumerate columns explicitly:
```sql
CREATE VIEW dbo.vw_AllCustomers
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
```

When you add a column, you must update the view too. This is a feature — it forces you to consider whether the view should expose the new column.

---

# 20. The OutSystems → External Entities Workflow

---

## What This Section Covers

This section describes the boundary between OutSystems and SSDT-managed databases. It explains what happens when a project migrates to External Entities, what changes for developers, and how the two systems coordinate.

---

## Context: What External Entities Are

In OutSystems, most entities are "internal" — OutSystems owns the schema, generates the tables, and manages all changes through Service Studio.

**External Entities** are different. They point to tables that exist outside OutSystems — tables we create and manage ourselves in SQL Server using SSDT.

OutSystems can read from and write to External Entities, but it doesn't control their structure. We do.

**Why we use External Entities:**
- Full control over schema design, data types, constraints
- Ability to implement patterns OutSystems doesn't support (temporal tables, complex indexing, partitioning)
- Change Data Capture for audit history
- Standard SQL Server tooling and practices
- Schema versioned in git, deployed through pipelines

---

## The Handoff: When a Project Moves to SSDT

A project migrates from OutSystems-managed entities to External Entities when:
- We need capabilities OutSystems can't provide (CDC, temporal, complex constraints)
- We need tighter control over schema evolution
- The project is designated for the External Entities architecture

**What happens during migration:**

1. **Schema extraction:** Current OutSystems-generated tables are reverse-engineered into SSDT `.sql` files
2. **SSDT project creation:** Tables, indexes, constraints defined declaratively
3. **CDC enablement:** Capture instances created for audit tracking
4. **External Entity creation:** Integration Studio extension created pointing to the tables
5. **OutSystems reconnection:** Applications switch from internal entities to External Entities
6. **Validation:** Data integrity verified, application tested

**After migration:**
- SSDT owns the schema
- OutSystems consumes the schema through External Entities
- All schema changes go through SSDT, then refresh in Integration Studio

---

## What Changes for the OutSystems Developer

### Before (Internal Entities)

| Task | How you did it |
|------|----------------|
| Add an attribute | Service Studio → Edit Entity → Add Attribute → Publish |
| Change data type | Service Studio → Edit Attribute → Change Type → Publish |
| Add an index | Service Studio → Entity Properties → Indexes → Publish |
| See schema changes | Immediate in Service Studio |
| Rollback | Republish previous version |

### After (External Entities)

| Task | How you do it now |
|------|-------------------|
| Add a column | SSDT → Edit table .sql file → PR → Deploy → Integration Studio Refresh → Service Studio Refresh |
| Change data type | SSDT → Classify change → Possibly multi-phase → PR → Deploy → Integration Studio Refresh → Service Studio Refresh |
| Add an index | SSDT → Create index → PR → Deploy (no Integration Studio refresh needed for indexes) |
| See schema changes | Check the SSDT project in git, or query the database directly |
| Rollback | Depends on change type — may require scripted rollback or backup restore |

### The Key Mental Shifts

**Schema changes require process.**
You can't make a quick schema tweak and publish. Changes go through PR review, classification, and staged deployment. This is intentional — it prevents the mistakes that OutSystems's guardrails used to catch for you.

**Two systems must stay synchronized.**
After SSDT deploys, Integration Studio must refresh to see the new schema, then Service Studio must refresh to use it. Forgetting a step creates confusion.

**You own data safety.**
OutSystems validated your changes. Now you must validate them — checking for orphan data, ensuring columns have values, reviewing generated scripts.

---

## The Synchronization Flow

Every schema change follows this flow:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│   ┌─────────────────┐                                                   │
│   │  1. SSDT        │  Developer makes schema change                    │
│   │     Change      │  - Edit .sql files                                │
│   │                 │  - Create PR                                       │
│   └────────┬────────┘  - Merge after review                             │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  2. Pipeline    │  Automated deployment                             │
│   │     Deploy      │  - Build dacpac                                   │
│   │                 │  - Deploy to target environment                   │
│   └────────┬────────┘  - Run pre/post scripts                           │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  3. Database    │  Schema is now updated                            │
│   │     Updated     │  - New columns exist                              │
│   │                 │  - Constraints applied                            │
│   └────────┬────────┘  - Indexes created                                │
│            │                                                            │
│            │         ╔═══════════════════════════════════════════════╗  │
│            │         ║  OutSystems doesn't know yet.                 ║  │
│            │         ║  The External Entity definition is stale.     ║  │
│            │         ╚═══════════════════════════════════════════════╝  │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  4. Integration │  Developer refreshes External Entity              │
│   │     Studio      │  - Connect to database                            │
│   │     Refresh     │  - Refresh entity definition                      │
│   └────────┬────────┘  - Publish extension                              │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  5. Service     │  Developer updates application                    │
│   │     Studio      │  - Refresh extension reference                    │
│   │     Update      │  - Update logic for new attributes                │
│   └────────┬────────┘  - Publish application                            │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  6. Complete    │  Schema change is fully propagated                │
│   │                 │  - Database updated                               │
│   │                 │  - External Entity updated                        │
│   └─────────────────┘  - Application using new schema                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Integration Studio: Step by Step

### Opening the Extension

1. Launch Integration Studio (from Start menu or Service Studio → Edit → Open Integration Studio)
2. Connect to your environment
3. Open the extension containing the External Entities (File → Open, or download from environment)

### Refreshing an External Entity

1. In the extension, navigate to the **Entities** folder
2. Find the External Entity that corresponds to the changed table
3. Right-click → **Refresh**
4. Integration Studio connects to the database and compares schemas

### What the Refresh Shows

| Integration Studio shows... | What happened | What to do |
|-----------------------------|---------------|------------|
| New attribute detected | SSDT added a column | Accept. Configure Is Mandatory, data type. |
| Attribute removed | SSDT dropped a column | Accept. Update application to remove usage. |
| Attribute type changed | SSDT changed column type | Accept. Verify mapping is correct. |
| Multiple changes | Several columns affected | Review each carefully. Accept when understood. |
| No changes detected | Schema matches extension | Nothing to do — already in sync. |

### Accepting Changes

1. Review each detected change
2. For new attributes: Set **Is Mandatory** appropriately (match your NOT NULL constraint)
3. For new attributes: Verify **Data Type** mapping is correct
4. Accept the changes
5. **Save** the extension

### Publishing the Extension

1. Click **1-Click Publish** (or Publish button)
2. Wait for publication to complete
3. Note any warnings or errors

### Returning to Service Studio

1. Open Service Studio
2. Open the module(s) using this External Entity
3. In the module tree, find the extension reference
4. Right-click → **Refresh**
5. Service Studio pulls the updated entity definition
6. Update application logic to use new attributes (if any)
7. Publish the application

---

## Coordinating Releases

### Additive Changes (Safe Order)

When adding columns, indexes, or tables:

```
SSDT deploys first → Database has new structure
                  → Integration Studio can see it
                  → Service Studio can use it
OutSystems deploys second → Application uses new structure
```

OutSystems can't use what doesn't exist yet. SSDT must lead.

### Breaking Changes (Requires Coordination)

When removing or renaming columns:

```
Release N:
  - SSDT: Add new column (if rename) or prepare for removal
  - OutSystems: Migrate to new column / stop using old column
  
Release N+1:
  - SSDT: Remove old column
  - OutSystems: Already not using it
```

You cannot remove a column that OutSystems is still using. The application will break.

### Timing Considerations

| Environment | Coordination needs |
|-------------|-------------------|
| Dev | Minimal — developer does both SSDT and OutSystems |
| Test | Moderate — ensure SSDT deployed before testing |
| UAT | Higher — coordinate with test schedule |
| Prod | Highest — scheduled releases, explicit handoff |

---

## Common Synchronization Issues

| Issue | Symptom | Cause | Resolution |
|-------|---------|-------|------------|
| External Entity missing column | Service Studio doesn't show new attribute | Integration Studio not refreshed | Refresh in Integration Studio, then Service Studio |
| Type mismatch errors | Runtime errors on data access | Integration Studio mapped type differently | Check mapping in Integration Studio, adjust if needed |
| "Column does not exist" at runtime | Application crashes on data access | SSDT deploy didn't complete, or wrong environment | Verify SSDT deployment, check environment connection |
| Phantom columns | Integration Studio shows columns you didn't add | Connected to wrong environment | Check database connection string |
| Refresh shows no changes | Expected to see new column | SSDT deploy didn't run, or you're in wrong branch | Verify pipeline completed, verify correct database |

---

## Checklist: After Every SSDT Deployment

For deployments that change table structure:

- [ ] Verify SSDT pipeline completed successfully
- [ ] Verify changes visible in database (query in SSMS if unsure)
- [ ] Open Integration Studio
- [ ] Connect to correct environment
- [ ] Refresh affected External Entity(ies)
- [ ] Review and accept changes
- [ ] Publish extension
- [ ] Open Service Studio
- [ ] Refresh extension reference
- [ ] Update application logic if needed
- [ ] Publish application (if changes made)
- [ ] Verify application works with new schema

---

# 21. Local Development Setup

---

## What You Need

Before you can work with the SSDT project, you need:

| Component | Purpose | Where to get it |
|-----------|---------|-----------------|
| SQL Server (local instance) | Target database for local deployment | SQL Server Developer Edition (free) or LocalDB |
| Visual Studio | IDE for editing SSDT projects | Visual Studio 2019/2022 |
| SSDT workload | SQL Server tooling for Visual Studio | Visual Studio Installer → Data storage and processing |
| Git | Version control | git-scm.com |
| Repository access | Clone the SSDT project | Azure DevOps (request access if needed) |

---

## Installing SQL Server Locally

### Option A: SQL Server Developer Edition (Recommended)

Full-featured SQL Server, free for development.

1. Download from Microsoft
2. Run installer, choose "Basic" installation
3. Note the instance name (default: `MSSQLSERVER`, connection: `localhost`)
4. Install SQL Server Management Studio (SSMS) for database inspection

### Option B: SQL Server LocalDB

Lightweight, minimal installation.

1. Included with Visual Studio's data workload
2. Instance name typically: `(localdb)\MSSQLLocalDB`
3. Less full-featured but sufficient for basic development

### Verify Installation

Open SSMS, connect to your local instance:
- Server name: `localhost` (or `(localdb)\MSSQLLocalDB` for LocalDB)
- Authentication: Windows Authentication

If you can connect and see system databases, you're ready.

---

## Installing SSDT Tooling

1. Open **Visual Studio Installer**
2. Modify your Visual Studio installation
3. Select the **Data storage and processing** workload
4. Ensure **SQL Server Data Tools** is checked
5. Install

After installation, you should see SQL Server project templates in Visual Studio.

---

## Cloning the Repository

```bash
# Navigate to your projects directory
cd C:\Projects

# Clone the repository
git clone https://your-org@dev.azure.com/your-org/your-project/_git/SSDT-Database

# Navigate into the project
cd SSDT-Database
```

Open the `.sln` file in Visual Studio.

---

## Building the Project

### First Build

1. Open the solution in Visual Studio
2. In Solution Explorer, right-click the database project
3. Select **Build**

**Expected outcome:** Build succeeds, producing a `.dacpac` file in the `bin\Debug` (or `bin\Release`) folder.

### If Build Fails

| Error type | Likely cause | Resolution |
|------------|--------------|------------|
| Missing references | Database references not resolved | Check that referenced `.dacpac` files exist |
| Syntax errors | Invalid SQL in a file | Check the error list, fix the SQL |
| Unresolved objects | FK to table that doesn't exist | Ensure all dependent tables are in the project |
| Target version mismatch | Project targets newer SQL version | Update project properties or local SQL Server |

---

## Creating Your Local Database

### Option A: Publish from Visual Studio

1. Right-click the database project
2. Select **Publish**
3. In the Publish dialog:
   - Click **Edit** next to Target database connection
   - Server name: `localhost` (or your instance)
   - Database name: `YourProject_Local` (or similar)
   - Authentication: Windows Authentication
   - Click **OK**
4. Click **Generate Script** first to review what will run
5. If satisfied, click **Publish**

### Option B: Use a Publish Profile

1. Open your local publish profile (e.g., `Local.publish.xml`)
2. Update connection string if needed
3. Right-click project → **Publish**
4. Select the profile
5. Publish

### Verify the Database

1. Open SSMS
2. Connect to your local instance
3. Expand Databases — you should see your new database
4. Expand Tables — verify tables were created
5. Run a simple query to confirm structure

---

## Making Changes Locally

### The Local Development Cycle

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  1. Edit        │────►│  2. Build       │────►│  3. Publish     │
│  .sql files     │     │  (verify syntax)│     │  to local DB    │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
                                                         ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  6. PR          │◄────│  5. Commit      │◄────│  4. Verify      │
│  when ready     │     │  to branch      │     │  in SSMS        │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

### Reviewing Generated Scripts

Before committing, always review what SSDT will generate:

1. Right-click project → **Schema Compare**
2. Set source: Your project
3. Set target: Your local database
4. Click **Compare**
5. Review the differences
6. Click **View Script** (top toolbar) to see the generated SQL

This is your preview of what deployment will do. If anything looks wrong, stop and investigate.

---

## Useful Local Commands

### Schema Compare (GUI)

Right-click project → Schema Compare

Shows differences between your project and any database. Useful for:
- Verifying your local DB matches the project
- Seeing what a deployment will change
- Debugging "why isn't my change showing up"

### Generate Script Without Deploying

In the Publish dialog, click **Generate Script** instead of **Publish**. This creates the deployment script without running it. Useful for review.

### SqlPackage Command Line

For advanced scenarios:

```bash
# Generate deployment script
SqlPackage /Action:Script /SourceFile:bin\Debug\YourProject.dacpac /TargetConnectionString:"Server=localhost;Database=YourDb;Integrated Security=True" /OutputPath:deploy.sql

# Deploy
SqlPackage /Action:Publish /SourceFile:bin\Debug\YourProject.dacpac /TargetConnectionString:"Server=localhost;Database=YourDb;Integrated Security=True"
```

---

## Checklist: Before Opening a PR

- [ ] Project builds successfully
- [ ] Deployed to local database without errors
- [ ] Verified changes in SSMS (tables, columns, constraints exist as expected)
- [ ] Reviewed generated deployment script (Schema Compare → View Script)
- [ ] If rename: Refactorlog entry exists
- [ ] If NOT NULL on existing table: Default provided or pre-deployment backfill
- [ ] If FK: Checked for orphan data
- [ ] Pre/post deployment scripts are idempotent
- [ ] Change classified correctly (tier, mechanism)

---

# 21. PR Template

*(This would live in your repo as a pull request template, e.g., `.azuredevops/pull_request_template.md` or `.github/pull_request_template.md`)*

---

```markdown
## Summary

_What does this PR do? One or two sentences._

## Classification

### Tier

- [ ] **Tier 1** — Self-Service (schema-only, additive, reversible, self-contained)
- [ ] **Tier 2** — Pair-Supported (data-preserving, effortful reversibility, intra-table scope, contractual impact)
- [ ] **Tier 3** — Dev Lead Owned (data-transforming, inter-table scope, breaking changes, multi-phase)
- [ ] **Tier 4** — Principal Escalation (data-destructive, lossy, cross-boundary, novel pattern)

_If Tier 3+, tag the appropriate dev lead or principal as reviewer._

### SSDT Mechanism

- [ ] Pure Declarative — schema change only, SSDT handles it
- [ ] Declarative + Post-Deployment — schema change + data migration script
- [ ] Pre-Deployment + Declarative — data prep required before schema change
- [ ] Script-Only — SSDT can't handle this; fully scripted
- [ ] Multi-Phase — requires multiple sequential deployments

_If Multi-Phase, list the phases and which release each belongs to._

### Operations Included

_Check all that apply:_

- [ ] Create table
- [ ] Create/modify column
- [ ] Create/modify constraint (FK, check, unique, default)
- [ ] Create/modify index
- [ ] Rename (column, table, or other object)
- [ ] Drop/deprecate object
- [ ] Data type change
- [ ] Nullability change
- [ ] Structural refactoring (split, merge, move)
- [ ] Other: _____________

## CDC Impact

_Does this PR affect any CDC-enabled tables?_

- [ ] No CDC-enabled tables affected
- [ ] Yes — CDC instance recreation required
- [ ] Yes — using dual-instance pattern (no history gap)
- [ ] Yes — accepting history gap (dev/test only)

_If yes, list affected tables:_

| Table | Current Capture Instance | Action |
|-------|--------------------------|--------|
| | | |

## Pre-Deployment Script Changes

- [ ] No pre-deployment changes
- [ ] Pre-deployment script added/modified

_If yes, describe what it does and confirm idempotency:_

## Post-Deployment Script Changes

- [ ] No post-deployment changes
- [ ] Post-deployment script added/modified

_If yes, describe what it does and confirm idempotency:_

## Refactorlog

- [ ] No renames in this PR
- [ ] Rename(s) included — refactorlog entry verified

_If rename, confirm: Did you use the SSDT GUI rename, or manually add the refactorlog entry?_

## Testing

### Local Testing

- [ ] Project builds successfully
- [ ] Deployed to local SQL Server
- [ ] Verified change works as expected
- [ ] Reviewed generated deployment script

### Script Review

_Paste or summarize the key parts of the generated deployment script. Especially important for Tier 2+._

```sql
-- Key operations SSDT will generate:

```

### Data Validation (if applicable)

_For changes affecting existing data, what queries did you run to validate safety?_

```sql
-- Validation queries:

```

## Rollback Plan

_If this deployment fails or causes issues, what's the rollback?_

- [ ] Symmetric rollback (just reverse the change)
- [ ] Requires backup restore
- [ ] Requires scripted rollback (describe below)
- [ ] Multi-phase — rollback is phase-specific

_Rollback notes:_

## Checklist

- [ ] I have classified this PR correctly using the [Dimension Framework](#)
- [ ] I have tagged appropriate reviewers for this tier
- [ ] I have tested locally and reviewed the generated script
- [ ] I have verified CDC impact (or confirmed no CDC tables affected)
- [ ] I have confirmed refactorlog is correct (if any renames)
- [ ] I have confirmed pre/post scripts are idempotent (if any)
- [ ] I have documented the rollback plan

## Reviewer Notes

_Anything specific reviewers should pay attention to?_

---

### For Reviewers

**Tier 1 Review Checklist:**
- [ ] Classification is correct (truly Tier 1)
- [ ] No obvious gotchas in the operation
- [ ] Generated script looks reasonable

**Tier 2 Review Checklist:**
- [ ] Everything from Tier 1
- [ ] Data validation queries are appropriate
- [ ] Pre/post scripts are idempotent
- [ ] CDC impact correctly identified

**Tier 3+ Review Checklist:**
- [ ] Everything from Tier 2
- [ ] Multi-phase sequencing is correct
- [ ] Rollback plan is viable
- [ ] Cross-team coordination identified (if applicable)
- [ ] Consider: should this be walked through synchronously?
```

---

## PR Template Usage Notes

**For authors:**
- Fill out completely. Empty sections signal you haven't thought it through.
- If you're unsure about classification, say so — reviewers can help calibrate.
- The generated script section is not optional for Tier 2+. Paste the relevant parts.
- If your PR is Tier 3+, consider pinging your reviewer before opening the PR to give them a heads-up.

**For reviewers:**
- If the classification seems wrong, say so early. Reclassification might change who should review.
- Check the gotchas in [15. Operation Reference](#) for any operations included.
- For CDC-enabled tables, verify the impact assessment is correct.
- If you're uncomfortable approving, escalate. That's the system working.

**Common feedback patterns:**
- "This is actually Tier 3 because of [X]. Please tag [dev lead]."
- "Missing refactorlog entry for the rename — this will cause data loss."
- "Post-deployment script isn't idempotent. What happens if it runs twice?"
- "CDC instance recreation needed — did you account for the history gap?"
- "Please paste the generated script for the [X] operation."

---


# 22. The Change/Release Process

---

## Overview

Every database change follows a defined process. The process exists to:
- Catch errors before they reach production
- Ensure appropriate review for risk level
- Create audit trail of changes
- Enable safe rollback if needed

The process scales with risk: simple changes move quickly; complex changes get more scrutiny.

---

## Step 1: Classify Your Change

Before touching any code, classify the change.

### 1.1 Identify Affected Tables

- Which tables will this change touch?
- Are any of them CDC-enabled? (Check the CDC Table Registry)

### 1.2 Determine the Dimensions

For each operation in your change, answer:

**Data Involvement:**
- Schema-only (no rows touched)?
- Data-preserving (rows stay, structure changes)?
- Data-transforming (values must convert/move)?
- Data-destructive (information will be lost)?

**Reversibility:**
- Symmetric (trivially reversible)?
- Effortful (requires scripted rollback)?
- Lossy (cannot undo without backup)?

**Dependency Scope:**
- Self-contained (single object)?
- Intra-table (other objects on same table)?
- Inter-table (other tables, views, procs)?
- Cross-boundary (external systems, ETL)?

**Application Impact:**
- Additive (nothing breaks)?
- Contractual (coexistence possible)?
- Breaking (synchronized deployment required)?

### 1.3 Determine the Tier

The tier is the highest risk across any dimension:

| If any dimension lands here... | Floor Tier |
|--------------------------------|------------|
| Schema-only, Symmetric, Self-contained, Additive | Tier 1 |
| Data-preserving, Effortful, Intra-table, Contractual | Tier 2 |
| Data-transforming, Inter-table, Breaking | Tier 3 |
| Data-destructive, Lossy, Cross-boundary | Tier 4 |

**Escalation triggers** (push tier upward regardless of dimensions):
- CDC-enabled table: +1 tier minimum
- Large table (>1M rows): +1 tier for operations that scan/modify data
- Production-critical timing: +1 tier
- Novel or unprecedented pattern: Tier 4

### 1.4 Determine the SSDT Mechanism

- **Pure Declarative:** Just edit the schema files
- **Declarative + Post-Deployment:** Schema change plus data script
- **Pre-Deployment + Declarative:** Prep data, then schema change
- **Script-Only:** SSDT can't handle it; fully scripted
- **Multi-Phase:** Multiple releases required

### 1.5 Document Your Classification

You'll include this in your PR. Write it down now:

```
Change: Add MiddleName column to Customer table
Table: dbo.Customer (CDC-enabled)
Operations: Create column (nullable)
Dimensions: Schema-only, Symmetric, Self-contained, Additive
Base Tier: 1
CDC Impact: Yes — needs instance recreation consideration
Final Tier: 2 (elevated due to CDC)
Mechanism: Declarative + Post-deployment (for CDC re-enable)
```

---

## Step 2: Implement the Change

### 2.1 Create a Branch

```bash
git checkout main
git pull
git checkout -b feature/add-customer-middlename
```

Branch naming convention: `feature/`, `fix/`, or `refactor/` prefix + descriptive name.

### 2.2 Make the Schema Change

For declarative changes, edit the `.sql` file directly:

```sql
-- In /Tables/dbo/dbo.Customer.sql
-- Add the new column in the appropriate position

[MiddleName] NVARCHAR(50) NULL,
```

For renames, use the Visual Studio GUI:
1. In Solution Explorer, navigate to the object
2. Right-click → Rename
3. Enter new name
4. Verify refactorlog was updated

### 2.3 Add Pre/Post Deployment Scripts (If Needed)

If your change requires data work:

```sql
-- /Scripts/PostDeployment/Migrations/XXX_AddCustomerMiddleName.sql

/*
Migration: Add MiddleName column to Customer
Ticket: JIRA-1234
Author: Your Name
Date: 2025-01-15
*/

PRINT 'Migration XXX: Customer.MiddleName setup...'

-- If we needed to backfill (not needed for nullable column, but showing pattern)
-- UPDATE dbo.Customer SET MiddleName = '' WHERE MiddleName IS NULL

PRINT 'Migration XXX complete.'
GO
```

Add the `:r` include to the master PostDeployment.sql if needed.

### 2.4 Handle CDC (If Applicable)

For CDC-enabled tables, add the appropriate CDC management:

```sql
-- For development (accept gaps):
-- Pre-deployment: Disable CDC
-- Post-deployment: Re-enable CDC

-- For production (no gaps):
-- Post-deployment: Create new capture instance
-- Leave old instance until next release
```

---

## Step 3: Build and Test Locally

### 3.1 Build the Project

In Visual Studio: Build → Build Solution (or Ctrl+Shift+B)

**Must succeed with no errors.** Warnings should be reviewed.

### 3.2 Deploy to Local Database

Right-click project → Publish → Select local profile → Publish

**Must succeed.** If it fails:
- Read the error message
- Check if pre-deployment scripts have issues
- Check for data violations (this won't apply to an empty local DB, so also test against a DB with data)

### 3.3 Verify in SSMS

Connect to your local database and verify:
- New objects exist
- Modifications applied correctly
- Constraints are in place
- Indexes created

### 3.4 Review Generated Script

Right-click project → Schema Compare → Compare to local DB → View Script

Read the generated SQL. Ask yourself:
- Is it doing what I expect?
- Are there any DROP statements I didn't anticipate?
- Are there any table rebuilds that seem excessive?
- Does anything look risky?

### 3.5 Test Idempotency

Publish a second time. It should:
- Succeed
- Generate minimal or no changes (if schema matches)
- Not fail on pre/post scripts (they should be idempotent)

---

## Step 4: Open PR

### 4.1 Commit Your Changes

```bash
git add .
git commit -m "Add MiddleName column to Customer table

- Added nullable NVARCHAR(50) column
- CDC impact: requires instance recreation
- Tier 2 change

JIRA-1234"
```

### 4.2 Push and Open PR

```bash
git push -u origin feature/add-customer-middlename
```

Open a PR in Azure DevOps (or your git platform).

### 4.3 Fill Out PR Template

Complete the entire template (see Section 23). Don't skip sections.

Key fields:
- **Classification:** Tier, mechanism, operations
- **CDC Impact:** Yes/no, affected tables
- **Generated Script:** Paste key operations
- **Rollback Plan:** How to reverse if needed

### 4.4 Tag Reviewers

Based on tier:

| Tier | Required Reviewers |
|------|-------------------|
| Tier 1 | Any team member |
| Tier 2 | Dev lead or experienced IC |
| Tier 3 | Dev lead (required) |
| Tier 4 | Principal engineer (required) |

---

## Step 5: Review Process

### For Authors

- Respond to review comments promptly
- If the reviewer questions your classification, discuss — they may see something you missed
- Don't merge until all required approvals are in

### For Reviewers

**Tier 1 Review (5-10 minutes):**
- Classification is correct?
- Change is actually additive/safe?
- No obvious gotchas?
- Generated script looks reasonable?

**Tier 2 Review (15-30 minutes):**
- Everything from Tier 1
- Data validation queries appropriate?
- Pre/post scripts idempotent?
- CDC impact correctly identified?
- Rollback plan viable?

**Tier 3+ Review (30+ minutes):**
- Everything from Tier 2
- Multi-phase sequencing correct?
- Application coordination identified?
- Should this be discussed synchronously before approving?
- Consider: walkthrough call before merge?

### Common Review Feedback

- "This is actually Tier X because..." — Classification adjustment
- "Missing refactorlog entry for the rename" — Critical fix needed
- "Post-deployment script isn't idempotent because..." — Fix required
- "Did you check for orphan data? Show me the query." — Verification request
- "Let's discuss this one synchronously" — Complex enough to warrant a call

---

## Step 6: Merge and Deploy

### 6.1 Merge

After approval:
1. Squash and merge (or your team's preferred merge strategy)
2. Delete the branch

### 6.2 Pipeline Deployment

Merging to main triggers the deployment pipeline:

```
Merge to main
     │
     ▼
┌─────────────────┐
│  Build          │  Compile project, produce dacpac
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Deploy to Dev  │  First environment
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Dev Validation │  Automated tests, manual verification
└────────┬────────┘
         │
         ▼
    (Promote to Test, UAT, Prod — gated)
```

### 6.3 Post-Deployment Verification

After pipeline completes:
1. Verify pipeline succeeded (check Azure DevOps)
2. Spot-check the database (query to confirm change applied)
3. If CDC change: verify capture instance status
4. If OutSystems-impacting: proceed with Integration Studio refresh

---

## Step 7: Environment Promotion

Changes promote through environments:

```
Dev → Test → UAT → Prod
```

### Promotion Gates

| Promotion | Gate |
|-----------|------|
| Dev → Test | Dev validation complete, basic smoke test |
| Test → UAT | QA sign-off, integration tests pass |
| UAT → Prod | UAT sign-off, change window scheduled, rollback plan confirmed |

### Production Deployment

Production deployments have additional requirements:

- [ ] Change scheduled in maintenance window (if needed)
- [ ] Rollback plan documented and reviewed
- [ ] On-call engineer aware
- [ ] Stakeholders notified
- [ ] Post-deployment validation checklist ready

---

## Multi-Phase Coordination

For multi-phase changes, each phase follows this process independently:

**Release N:**
1. Phase 1 changes: PR → Review → Merge → Deploy → Verify
2. Wait for phase 1 to reach production and stabilize

**Release N+1:**
1. Phase 2 changes: PR → Review → Merge → Deploy → Verify
2. Continue until all phases complete

**Document the sequence:**
- PR for phase 1 should reference the overall plan
- PR for phase 2 should reference phase 1's completion
- Each PR is self-contained but part of a documented sequence

---

# 23. The PR Template

*(Full template was provided earlier. Here we add usage guidance.)*

---

## PR Template Location

The template lives in your repository:
- Azure DevOps: `.azuredevops/pull_request_template.md`
- GitHub: `.github/pull_request_template.md`

When you open a PR, the template auto-populates the description field.

---

## Filling Out the Template

### Summary

One to two sentences. What does this change accomplish?

**Good:** "Adds MiddleName column to Customer table to support name display requirements."
**Bad:** "Schema changes" or "Updates"

### Classification Section

Be precise. If you're uncertain, say so — reviewers can help calibrate.

**Tier:** Check exactly one. If you're between tiers, pick the higher one.

**Mechanism:** Check the most accurate description. Multiple phases = Multi-Phase mechanism.

**Operations:** Check all that apply. This helps reviewers know what to look for.

### CDC Impact Section

If you're not sure whether a table has CDC, check before filling this out.

**If no CDC tables affected:** Check the first option, move on.

**If CDC tables affected:** List each table, its current capture instance, and what action you're taking (recreate, dual-instance, accepting gap).

### Script Sections

**Pre-deployment:** If you added pre-deployment work, describe it. Confirm idempotency.

**Post-deployment:** Same. Explain what the script does and why.

**Generated script review:** This is critical for Tier 2+. Paste the significant operations from the generated script. Reviewers can't review what they can't see.

### Rollback Plan

Be specific.

**Good:** "Symmetric rollback: remove the column from the definition, deploy again."
**Good:** "Rollback requires restoring the Customer table from backup taken pre-deployment."
**Bad:** "Revert the PR"

### Testing Section

Don't just check boxes. Describe what you verified.

**Validation queries:** If you ran queries to check data (orphans, NULLs, length), paste them.

### Checklist

The final checklist is your self-review. Don't check items you haven't actually done.

---

## Reviewer Guidance

### What to Check First

1. **Classification accuracy:** Is this really Tier 1? Or does something push it higher?
2. **CDC awareness:** If table is CDC-enabled, is that reflected?
3. **Refactorlog:** Any renames? Is the entry there?

### Red Flags

- Empty or minimal generated script section on Tier 2+ changes
- "Rollback: revert the PR" without specifics
- Classification seems too low for the operations listed
- CDC table affected but no CDC plan documented
- Pre/post scripts present but no idempotency confirmation

### When to Request Changes

- Missing information that you need to review properly
- Classification is clearly wrong
- Safety concern (data loss risk, untested pattern)
- Idempotency issues in scripts

### When to Approve

- Classification is correct
- All checklist items verified
- Generated script reviewed and understood
- Rollback plan is viable
- You would be comfortable if this deployed to production tonight

---

# 24. Troubleshooting Playbook

---

## Build Failures

### "Unresolved reference to object"

**Symptom:** Build error citing an object that doesn't exist.

**Causes:**
- FK references a table not in the project
- View references a column that was removed
- Cross-database reference without database reference configured

**Resolution:**
- Check if the referenced object should exist — add it if missing
- Check if you removed something that's still referenced — fix the reference
- For cross-database: add a database reference to the project

---

### "Syntax error in SQL"

**Symptom:** Build error with SQL syntax issue.

**Causes:**
- Typo in SQL
- Missing comma, parenthesis, or keyword
- Using syntax not supported by target SQL version

**Resolution:**
- Read the error message — it usually points to the line
- Check for missing commas between column definitions
- Verify the syntax is valid for your target SQL Server version

---

### "Duplicate object name"

**Symptom:** Build error saying an object is defined multiple times.

**Causes:**
- Two files define the same object
- Copy/paste error left duplicate definition
- Merge conflict resolved incorrectly

**Resolution:**
- Search the project for the object name
- Remove the duplicate definition
- Check git history to understand how duplication occurred

---

## Deployment Failures

### "BlockOnPossibleDataLoss"

**Symptom:** Deployment fails with message about potential data loss.

**What triggered it:**
- Dropping a column that contains data
- Narrowing a column below current data size
- Dropping a table with rows
- Changing type in a lossy way

**This is the system protecting you.**

**Resolution:**
1. Review the generated script — what's it trying to do?
2. If data loss is intentional: Handle explicitly in pre-deployment script, then proceed
3. If unintentional: Fix your schema change
4. Never set `BlockOnPossibleDataLoss=False` for production

---

### "ALTER TABLE ALTER COLUMN failed because the column is referenced by a constraint"

**Symptom:** Can't modify a column because something depends on it.

**What triggered it:**
- Column is part of an index
- Column has a default constraint
- Column is referenced by a computed column or view

**Resolution:**
1. Identify the dependent object (error message usually says which)
2. In pre-deployment: drop the dependency
3. Let SSDT make the column change
4. In post-deployment: recreate the dependency (or let SSDT do it if declarative)

---

### "The INSERT statement conflicted with the FOREIGN KEY constraint"

**Symptom:** Post-deployment script fails on FK violation.

**What triggered it:**
- Your script is inserting data that references a non-existent parent
- Seed data has bad references

**Resolution:**
1. Check your seed data — do all FK values exist in parent tables?
2. Ensure parent data is seeded before child data
3. Fix the data in your script

---

### "Cannot insert NULL into column"

**Symptom:** Deployment fails on NOT NULL violation.

**What triggered it:**
- Adding NOT NULL column without default to table with existing data
- Post-deployment script inserting incomplete data

**Resolution:**
- Add a default constraint, or
- Make the column nullable initially, backfill, then alter to NOT NULL, or
- Backfill in pre-deployment script

---

### "Timeout expired"

**Symptom:** Deployment times out.

**What triggered it:**
- Large table operation (index build, table rebuild)
- Lock contention
- Long-running pre/post script

**Resolution:**
1. For large tables: Consider deploying during maintenance window
2. For indexes: Consider online index operations (Enterprise Edition)
3. For scripts: Batch large operations
4. Increase timeout in publish profile (but understand why it's slow first)

---

## Refactorlog Issues

### Rename treated as drop+create

**Symptom:** Generated script shows DROP COLUMN + ADD COLUMN instead of sp_rename.

**Cause:** Missing refactorlog entry.

**Resolution:**
1. Do not deploy — this will lose data
2. Either:
   - Use GUI rename to create refactorlog entry, or
   - Manually add refactorlog entry
3. Rebuild, verify generated script now shows rename

---

### Merge conflict in refactorlog

**Symptom:** Git merge conflict in `.refactorlog` file.

**Resolution:**
1. Keep BOTH rename entries (if different objects)
2. If same object renamed differently in each branch, coordinate with other developer
3. Ensure all GUIDs are unique
4. Build after merge to verify

---

## CDC Issues

### Capture instance not tracking new column

**Symptom:** Change History doesn't show changes to a new column.

**Cause:** Old capture instance doesn't know about the new column.

**Resolution:**
- Development: Disable and re-enable CDC on the table
- Production: Create new capture instance, update consumers

---

### "Invalid object name 'cdc.fn_cdc_get_all_changes_...'"

**Symptom:** CDC function doesn't exist.

**Cause:** Capture instance was disabled or never created for this table.

**Resolution:**
1. Check if CDC is enabled: `SELECT * FROM cdc.change_tables`
2. If missing, create capture instance: `EXEC sys.sp_cdc_enable_table ...`

---

## "It Works Locally But Fails in Pipeline"

### Check Environment Differences

| Check | How |
|-------|-----|
| SQL Server version | Compare local version to pipeline target |
| Connection string | Verify pipeline connects to right database |
| Publish profile | Ensure pipeline uses correct profile |
| Pre-existing state | Local DB may have different starting state than target |
| Permissions | Pipeline service account may have different permissions |

### Common Causes

1. **Local DB has manual changes:** Your local DB has objects or data that aren't in the project. Pipeline's target doesn't.

2. **Different starting state:** You created local DB fresh; pipeline targets DB that has history.

3. **Cached dacpac:** Pipeline using old build artifact. Ensure clean build.

4. **Profile mismatch:** Different publish profile settings between local and pipeline.

---

## Error Message Translation

| Error | Plain English | OutSystems Equivalent | Fix |
|-------|--------------|----------------------|-----|
| "Cannot insert NULL into column 'X'" | Column requires a value but none provided | Like when required attribute has no default | Add default or backfill |
| "FOREIGN KEY constraint failed" | Orphan data — parent doesn't exist | Like when reference points to deleted record | Clean orphans or add parent |
| "String or binary data would be truncated" | Value too long for column | Like when text exceeds attribute length | Increase column size or validate data |
| "Cannot drop column because it's referenced by..." | Something depends on this column | Like when attribute is used elsewhere | Drop dependencies first |
| "BlockOnPossibleDataLoss" | SSDT protecting you from destructive change | OutSystems warning on entity delete | Review, handle explicitly if intentional |

---

# 25. Escalation Paths

---

## When to Escalate

Escalation is correct behavior. It's not admitting you can't handle something — it's recognizing when additional experience or authority is appropriate.

### Automatic Escalation (Tier-Based)

| Tier | Required Involvement |
|------|---------------------|
| Tier 1 | Any team member can own; standard review |
| Tier 2 | Pair support available; dev lead review if uncertain |
| Tier 3 | Dev lead owns or directly supervises |
| Tier 4 | Principal engineer involvement required |

### Judgment-Based Escalation

Escalate even if tier seems lower when:

- **You're uncertain about classification.** Better to ask than guess wrong.
- **You've never done this type of change before.** Get support first time.
- **Something unexpected happened.** Errors you don't understand, behavior you didn't expect.
- **Rollback might be needed.** If things went wrong, loop in leads early.
- **Time pressure is high.** When stakes are elevated, get more eyes.
- **Cross-team coordination needed.** Changes affecting other teams need visibility.

### What to Escalate

| Situation | Escalate to |
|-----------|-------------|
| Uncertain about tier/classification | Dev lead |
| First time doing a specific operation type | Dev lead or experienced IC for pairing |
| CDC-related change in production | Dev lead minimum |
| Multi-phase change spanning releases | Dev lead to verify sequencing |
| Deployment failure in test/UAT/prod | Dev lead + on-call if prod |
| Data loss or suspected data corruption | Principal + Danny immediately |
| Novel pattern not covered in playbook | Principal |

---

## How to Escalate

### For PR Review Escalation

1. Tag the appropriate person as a required reviewer
2. In the PR description, note why you're escalating: "Tagging @DevLead — this is my first FK addition to a CDC table, requesting guidance."

### For Real-Time Help

1. Post in #ssdt-questions with:
   - What you're trying to do
   - What happened
   - What you've already tried
   - Specific question

2. If urgent (production issue), escalate to phone/direct message

### For Incident Escalation

If something has gone wrong in production:

1. **Immediately:** Notify dev lead and Danny via Slack/phone
2. **Include:** What happened, what environment, what's the impact
3. **Don't:** Try to fix alone if you're uncertain; more damage can occur
4. **Do:** Preserve evidence (logs, error messages, current state)

---

## Who Owns What

### Dev Lead Coverage Areas

*[Customize this for your team — list dev leads and their areas of responsibility]*

| Dev Lead | Primary Areas |
|----------|--------------|
| [Name 1] | [Modules/tables they know best] |
| [Name 2] | [Modules/tables they know best] |
| [Name 3] | [Modules/tables they know best] |

### Principal Engineer Escalation

Principals should be involved for:
- Tier 4 changes
- Novel patterns requiring architectural judgment
- Cross-system impacts (CDC → Change History → Application)
- Post-incident analysis
- Playbook evolution (new patterns, new gotchas)

### Danny's Role

- Process and playbook owner
- Escalation point for team conflicts or unclear ownership
- Incident communication to stakeholders
- Capability development conversations
- Not a required reviewer for every PR — trust the tier system

---

## After Escalation

### Learning Loop

Every escalation is a learning opportunity:

1. **Document what happened.** What was the question? What was the resolution?
2. **Ask: Should this be in the playbook?** If you escalated because something wasn't documented, document it.
3. **Ask: Did the tier system work?** If you escalated something that should have been obvious from tiers, refine the tier definitions.

### Escalation Isn't Failure

The goal is not to minimize escalations. The goal is to:
- Escalate at the right time (not too late)
- Escalate to the right person
- Learn from escalations so you need fewer for the same situation next time

The worst outcome isn't escalating too much. It's not escalating when you should have.

---

# 26. Capability Development

---

## The Graduation Path

Competence with SSDT develops through practice, not just reading. This path makes progression explicit.

---

### Level 1: Observer

**Duration:** ~1 week

**Activities:**
- Read Start Here, The Big Picture, The Translation Layer
- Shadow PRs — watch changes go through the process
- Build the project locally
- Deploy to your local database
- Browse the schema, understand the structure

**Demonstrate readiness to advance:**
- Can explain what SSDT does (declarative model)
- Can build and deploy locally
- Has observed at least 2-3 PRs through the full cycle

---

### Level 2: Supported Contributor

**Duration:** ~2-4 weeks

**Activities:**
- Make Tier 1 changes with pairing support
- Use the PR template correctly
- Receive PR feedback, incorporate it
- Ask questions freely — this is the learning phase

**Tier 1 changes to practice:**
- Add a nullable column
- Add a default constraint
- Add an index
- Add a new table

**Support model:**
- Pair with an experienced IC or dev lead for first few changes
- Real-time availability for questions
- Detailed PR feedback focused on teaching

**Demonstrate readiness to advance:**
- Has completed 5+ Tier 1 changes successfully
- PR feedback is diminishing (fewer corrections needed)
- Can explain the four dimensions and tier logic
- Understands pre/post deployment script purposes

---

### Level 3: Independent Contributor

**Duration:** ~1-2 months

**Activities:**
- Make Tier 1 changes independently
- Make Tier 2 changes with review (not pairing)
- Begin reviewing others' Tier 1 PRs
- Contribute to troubleshooting (help debug issues)

**Tier 2 changes to practice:**
- Add NOT NULL column with default to populated table
- Add FK to table with verified clean data
- Widen a column
- Add CDC consideration to a change

**Support model:**
- Dev lead available for questions, not actively pairing
- PR review catches issues, with teaching feedback
- Autonomy increasing

**Demonstrate readiness to advance:**
- Has completed 10+ Tier 1-2 changes successfully
- Reviews others' PRs accurately
- Can identify when to escalate (doesn't miss Tier 3+ situations)
- Has handled at least one troubleshooting situation

---

### Level 4: Trusted Contributor

**Duration:** Ongoing

**Activities:**
- Make Tier 1-2 changes independently
- Make Tier 3 changes with dev lead oversight
- Mentor newer team members
- Contribute to playbook improvements
- Participate in incident response

**Tier 3 changes to participate in:**
- Multi-phase data type conversions
- Table structural refactoring
- CDC instance management for production
- Breaking changes requiring coordination

**Support model:**
- Peer relationship with dev leads
- Consulted on complex decisions
- Trusted to escalate appropriately

**Demonstrate readiness to advance:**
- Has participated in multiple Tier 3 changes
- Mentors effectively (others learn from them)
- Contributed playbook improvements
- Trusted by dev leads and principals

---

### Level 5: Dev Lead

**Activities:**
- Own Tier 3 changes
- Escalate Tier 4 appropriately
- Make judgment calls on edge cases
- Review Tier 1-3 PRs for the team
- Evolve team standards
- Mentor and develop others

**Responsibilities:**
- Final reviewer for Tier 2-3 changes in their area
- On-call for escalations
- Participate in incident response
- Contribute to process improvement
- Coordinate with principals on Tier 4 work

---

## Progression Expectations

| Level | Typical Timeline | Not a Failure If |
|-------|-----------------|------------------|
| Observer → Supported | 1 week | Takes longer due to other responsibilities |
| Supported → Independent | 2-4 weeks | Takes longer; everyone learns at different pace |
| Independent → Trusted | 1-2 months | Takes longer; depends on exposure to complex changes |
| Trusted → Dev Lead | Variable | Not everyone becomes a dev lead — Trusted Contributor is a successful end state |

**Key point:** The goal isn't to rush through levels. The goal is to build genuine competence at each level before advancing.

---

## For Managers: Capability Conversations

### Assessing Progression Readiness

**Questions to consider:**
- Is this person consistently successful at their current level?
- When they escalate, is it appropriate? (Not too early, not too late)
- How do they respond to PR feedback? (Defensive vs. learning)
- Can they teach others what they know?
- Do they recognize what they don't know?

### Development Opportunities

| Gap | Opportunity |
|-----|-------------|
| Needs more Tier 2 exposure | Assign Tier 2 changes with support |
| Uncertain about CDC | Pair on CDC-related change |
| Hasn't seen a failure | Include in next incident response |
| Needs to mentor | Pair them with a new team member |
| Ready for Tier 3 but no opportunities | Create or assign appropriate work |

### Warning Signs

- Consistently over-classifies (marks Tier 1 as Tier 3) — may be overly cautious
- Consistently under-classifies (marks Tier 3 as Tier 1) — may be overconfident
- Avoids escalation even when uncertain — risk of incidents
- Escalates everything — may need more direct support to build confidence
- PR feedback same issue repeatedly — learning isn't happening

# 18. Decision Aids

---

## How to Use These Aids

These are quick-reference tools for in-the-moment decisions. They condense the playbook into actionable formats.

**Print them. Pin them. Reference them until you don't need to.**

---

## 18.1 "What Tier Is This?" Flowchart

```
START: You need to make a schema change
           │
           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  QUESTION 1: Will any data be lost or destroyed?                        │
│                                                                         │
│  • Dropping a column that has data?                                     │
│  • Narrowing a column below current max length?                         │
│  • Dropping a table with rows?                                          │
│  • Changing type in a way that loses precision?                         │
└─────────────────────────────────────────────────────────────────────────┘
           │
     ┌─────┴─────┐
     │           │
    YES          NO
     │           │
     ▼           ▼
┌─────────┐    ┌─────────────────────────────────────────────────────────┐
│ TIER 4  │    │  QUESTION 2: Will existing data values change or move?  │
│         │    │                                                         │
│ Stop.   │    │  • Converting data types explicitly?                    │
│ Get     │    │  • Splitting/merging tables?                            │
│ Principal│   │  • Moving columns between tables?                       │
│ involved.│   │  • Backfilling values into existing rows?               │
└─────────┘    └─────────────────────────────────────────────────────────┘
                         │
                   ┌─────┴─────┐
                   │           │
                  YES          NO
                   │           │
                   ▼           ▼
          ┌─────────────┐    ┌─────────────────────────────────────────┐
          │   TIER 3    │    │  QUESTION 3: Do other objects depend    │
          │   minimum   │    │  on what you're changing?               │
          │             │    │                                         │
          │ Dev lead    │    │  • FKs from other tables?               │
          │ owns this.  │    │  • Views referencing this column?       │
          └─────────────┘    │  • Procs, computed columns, indexes?    │
                             │  • External systems (ETL, reports)?     │
                             └─────────────────────────────────────────┘
                                       │
                                 ┌─────┴─────┐
                                 │           │
                            CROSS-TABLE    SAME TABLE
                            OR EXTERNAL    ONLY
                                 │           │
                                 ▼           ▼
                        ┌─────────────┐    ┌─────────────────────────────┐
                        │   TIER 3    │    │  QUESTION 4: Can existing   │
                        │             │    │  app code keep working      │
                        │ Dev lead    │    │  unchanged?                 │
                        │ owns this.  │    │                             │
                        └─────────────┘    │  • Queries still valid?     │
                                           │  • No column removals?      │
                                           │  • No required new inputs?  │
                                           └─────────────────────────────┘
                                                     │
                                               ┌─────┴─────┐
                                               │           │
                                              NO          YES
                                               │           │
                                               ▼           ▼
                                      ┌─────────────┐    ┌───────────────┐
                                      │   TIER 2    │    │   TIER 1      │
                                      │   minimum   │    │               │
                                      │             │    │ Self-service  │
                                      │ Pair support│    │ with review   │
                                      │ recommended │    └───────────────┘
                                      └─────────────┘
```

**After determining base tier, check escalation triggers:**

```
┌─────────────────────────────────────────────────────────────────────────┐
│  ESCALATION TRIGGERS (add +1 tier if any apply)                         │
│                                                                         │
│  □ CDC-enabled table?                          → +1 tier minimum        │
│  □ Table has >1M rows?                         → +1 for data operations │
│  □ Production-critical timing?                 → +1 tier                │
│  □ Pattern you've never done before?           → +1 tier or get support │
│  □ Novel/unprecedented pattern?                → TIER 4 regardless      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 18.2 "Do I Need Multi-Phase?" Checklist

**Check any that apply:**

```
DATA MOVEMENT
□ Existing data must be transformed (type conversion, format change)
□ Data is moving between tables (split, merge, column move)
□ New column needs values derived from existing data

APPLICATION COORDINATION  
□ Old and new code must coexist during transition
□ Column/table being removed is still referenced by app
□ Breaking change requires synchronized app deployment

CDC CONSTRAINTS (Production)
□ CDC-enabled table is changing structure
□ Audit continuity required (no history gaps)

IRREVERSIBILITY
□ Part of the change can't be easily undone
□ Need to verify success before proceeding to next step
□ Rollback complexity justifies separating phases

DEPENDENCIES
□ Must drop something before changing, then recreate after
□ FK relationships must be temporarily disabled
□ Index must be dropped for column change, then rebuilt
```

**Scoring:**
- **0 checked:** Likely single-phase. Proceed normally.
- **1-2 checked:** Consider multi-phase. Review the specific patterns.
- **3+ checked:** Almost certainly multi-phase. See Section 17 for templates.

---

## 18.3 "Can SSDT Handle This Declaratively?" Quick Reference

| Operation | Declarative? | Notes |
|-----------|--------------|-------|
| **CREATION** | | |
| Create table | ✅ Yes | Add .sql file |
| Create column (nullable) | ✅ Yes | Edit table file |
| Create column (NOT NULL) | ✅ Yes | Need default for existing rows |
| Create PK/FK/unique/check | ✅ Yes | Inline or separate file |
| Create index | ✅ Yes | Inline or separate file |
| Create view | ✅ Yes | Add .sql file |
| Create proc/function | ✅ Yes | Add .sql file |
| | | |
| **MODIFICATION** | | |
| Widen column | ✅ Yes | Just change the definition |
| Narrow column | ⚠️ Depends | May need pre-validation; BlockOnPossibleDataLoss guards |
| Change type (implicit) | ✅ Yes | INT→BIGINT, VARCHAR→NVARCHAR |
| Change type (explicit) | ❌ No | Needs multi-phase: add new, migrate, drop old |
| NULL → NOT NULL | ⚠️ Depends | Need default or pre-backfill |
| NOT NULL → NULL | ✅ Yes | Just change the definition |
| Rename column | ✅ Yes | **Must use refactorlog** |
| Rename table | ✅ Yes | **Must use refactorlog** |
| Add/remove IDENTITY | ❌ No | Can't ALTER to add/remove; needs table swap |
| | | |
| **CONSTRAINTS** | | |
| Add default | ✅ Yes | Inline in table definition |
| Modify default | ✅ Yes | Change the value; SSDT drops and recreates |
| Remove default | ✅ Yes | Remove from definition |
| Add FK (clean data) | ✅ Yes | Inline in table definition |
| Add FK (orphan data) | ❌ No | Need WITH NOCHECK via script, then trust |
| Enable/disable constraint | ❌ No | Script-only (operational, not declarative) |
| | | |
| **INDEXES** | | |
| Add/drop index | ✅ Yes | Add/remove definition |
| Change index columns | ✅ Yes | SSDT regenerates |
| Rebuild/reorganize | ❌ No | Maintenance operation, not schema |
| Online index operations | ⚠️ Partial | May need script for WITH (ONLINE=ON) |
| | | |
| **STRUCTURAL** | | |
| Split table | ❌ No | Multi-phase: create, migrate, drop |
| Merge tables | ❌ No | Multi-phase: create, migrate, drop |
| Move column between tables | ❌ No | Multi-phase |
| Move table between schemas | ⚠️ Partial | Declarative with refactorlog, or ALTER SCHEMA TRANSFER |
| | | |
| **CDC** | | |
| Enable/disable CDC | ❌ No | Stored procedure calls, not declarative |
| Create/drop capture instance | ❌ No | Stored procedure calls |

**Legend:**
- ✅ Yes = Pure declarative, just edit the schema files
- ⚠️ Depends/Partial = Declarative with conditions or scripted help
- ❌ No = Script-only or multi-phase required

---

## 18.4 Before-You-Start Checklist

**Copy this for every schema change:**

```
CLASSIFICATION
□ I know which table(s) this change affects
□ I've checked if any affected tables are CDC-enabled
□ I've determined the tier for this change: ___
□ I've identified the SSDT mechanism:
    □ Pure Declarative
    □ Declarative + Post-Deployment
    □ Pre-Deployment + Declarative  
    □ Script-Only
    □ Multi-Phase (releases needed: ___)

PREPARATION
□ I've reviewed the Operation Reference for relevant gotchas
□ I know who needs to review this (based on tier)
□ If rename: I will use GUI rename to create refactorlog entry
□ If NOT NULL on existing table: I have a plan for existing rows
□ If FK: I have verified no orphan data exists
□ If multi-phase: I've mapped all phases to releases

CDC (if applicable)
□ I know which capture instance(s) will be affected
□ I have a plan for instance recreation:
    □ Dev/Test: Accept gap, disable/enable
    □ Production: Dual-instance pattern

IMPLEMENTATION
□ Branch created from latest main
□ Schema changes made (declarative files updated)
□ Pre-deployment scripts added (if needed)
□ Post-deployment scripts added (if needed)
□ Scripts are idempotent

TESTING
□ Project builds successfully
□ Deployed to local database without errors
□ Verified changes in database (SSMS inspection)
□ Reviewed generated deployment script
□ If scripts: Tested idempotency (deployed twice)

READY FOR PR
□ PR template will be filled out completely
□ Appropriate reviewers identified
□ Rollback plan documented
```

---

## 18.5 CDC Impact Checker

### Step 1: Is the Table CDC-Enabled?

**Quick check:**
```sql
SELECT 
    OBJECT_SCHEMA_NAME(source_object_id) AS SchemaName,
    OBJECT_NAME(source_object_id) AS TableName,
    capture_instance
FROM cdc.change_tables
WHERE OBJECT_NAME(source_object_id) = 'YourTableName'
```

**If no results:** Table is not CDC-enabled. No CDC impact.

**If results returned:** Table is CDC-enabled. Continue to Step 2.

---

### Step 2: What Kind of Change?

| Your Change | CDC Impact? | Action Required |
|-------------|-------------|-----------------|
| Add nullable column | Yes (if you want it tracked) | Recreate capture instance |
| Add NOT NULL column | Yes (if you want it tracked) | Recreate capture instance |
| Drop column | Yes | Recreate capture instance |
| Rename column | Yes | Recreate capture instance |
| Change data type | Yes | Recreate capture instance |
| Widen column | No | Capture instance still valid |
| Add/modify constraint | No | Constraints not tracked |
| Add/modify index | No | Indexes not tracked |
| Add/drop FK | No | FKs not tracked |

---

### Step 3: Development or Production?

**Development/Test (Gap Acceptable):**
```
Pre-deployment:
  1. Disable CDC on table
  
[SSDT deploys schema change]

Post-deployment:
  2. Re-enable CDC on table (new capture instance)

⚠️ Changes during deployment window are not captured
```

**Production (No Gap):**
```
Post-deployment (after schema change):
  1. Create NEW capture instance (schema already updated)
     - Name it with version: dbo_TableName_v2
  
[Both v1 and v2 instances now active]
[Consumer code reads from both, unions results]

Next Release:
  2. Drop OLD capture instance (v1)
  3. Consumer code reads only from v2

⚠️ Requires consumer abstraction layer
```

---

### Step 4: Document in PR

```
CDC Impact: Yes

Affected Table(s):
| Table | Current Instance | Action |
|-------|------------------|--------|
| dbo.Customer | dbo_Customer_v1 | Create v2, deprecate v1 next release |

Environment Strategy:
- Dev/Test: Disable/re-enable (accepting gap)
- Production: Dual-instance pattern per Section 12
```

---

## 18.6 Tier Summary Card

**Print this. Keep it visible.**

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           TIER QUICK REFERENCE                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 1: Self-Service                                                   │
│  ─────────────────────                                                  │
│  • Schema-only (no data touched)                                        │
│  • Purely additive (nothing breaks)                                     │
│  • Self-contained (no dependencies)                                     │
│  • Trivially reversible                                                 │
│                                                                         │
│  Examples: Add nullable column, add table, add index, add default       │
│  Review: Any team member                                                │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 2: Pair-Supported                                                 │
│  ──────────────────────                                                 │
│  • Data-preserving (rows stay, structure changes)                       │
│  • Contractual (old/new can coexist)                                    │
│  • Intra-table dependencies                                             │
│  • Reversible with effort                                               │
│                                                                         │
│  Examples: Add NOT NULL with default, add FK (clean data), widen column │
│  Review: Dev lead or experienced IC                                     │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 3: Dev Lead Owned                                                 │
│  ──────────────────────                                                 │
│  • Data-transforming (values change/move)                               │
│  • Inter-table dependencies                                             │
│  • Breaking (synchronized deployment needed)                            │
│  • Multi-phase required                                                 │
│                                                                         │
│  Examples: Type conversion, table split, rename, FK with orphans        │
│  Review: Dev lead required                                              │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  TIER 4: Principal Escalation                                           │
│  ────────────────────────────                                           │
│  • Data-destructive (information lost)                                  │
│  • Cross-boundary (external systems affected)                           │
│  • Lossy (can't undo without backup)                                    │
│  • Novel/unprecedented pattern                                          │
│                                                                         │
│  Examples: Drop table with data, narrow column, major structural change │
│  Review: Principal engineer required                                    │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ESCALATION TRIGGERS: +1 tier for                                       │
│  • CDC-enabled table                                                    │
│  • Table >1M rows (for data operations)                                 │
│  • Production-critical timing                                           │
│  • First time doing this operation type                                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 18.7 Operation Quick Reference

**One-line summaries for common operations:**

| Operation | Tier | Mechanism | Watch For |
|-----------|------|-----------|-----------|
| Add table | 1 | Declarative | Nothing — safest operation |
| Add nullable column | 1 | Declarative | Nothing |
| Add NOT NULL column | 1-2 | Declarative | Need default for existing rows |
| Add index | 1-2 | Declarative | Large table = blocking time |
| Add FK (clean data) | 2 | Declarative | Verify no orphans first |
| Add FK (orphan data) | 3 | Multi-phase | WITH NOCHECK → clean → trust |
| Add default | 1 | Declarative | Nothing |
| Add check constraint | 2 | Declarative | Existing data may violate |
| Add unique constraint | 2 | Declarative | Check for duplicates first |
| Widen column | 2 | Declarative | Index rebuild possible |
| Narrow column | 4 | Pre + Declarative | Validate data fits; BlockOnPossibleDataLoss |
| Change type (implicit) | 2 | Declarative | INT→BIGINT is safe |
| Change type (explicit) | 3-4 | Multi-phase | Add new → migrate → drop old |
| NULL → NOT NULL | 2-3 | Pre + Declarative | Backfill NULLs first |
| NOT NULL → NULL | 1-2 | Declarative | Safe; consider why |
| Rename column | 3 | Declarative + refactorlog | **Without refactorlog = data loss** |
| Rename table | 3 | Declarative + refactorlog | **Without refactorlog = data loss** |
| Drop column | 3-4 | Declarative | Follow deprecation workflow |
| Drop table | 4 | Declarative | Verify truly unused; backup |
| Add/remove IDENTITY | 3-4 | Multi-phase (table swap) | Full table rebuild |
| Split table | 4 | Multi-phase | Multiple releases |
| Merge tables | 4 | Multi-phase | Multiple releases |
| CDC table schema change | +1 tier | Environment-dependent | See CDC protocol |

---

# 27. SSDT Standards

---

## 27.1 Naming Conventions

Consistent naming makes the schema self-documenting and simplifies tooling.

### Tables

| Rule | Convention | Example |
|------|------------|---------|
| Case | PascalCase | `Customer`, `OrderLine` |
| Plurality | Singular | `Customer` (not `Customers`) |
| Prefixes | None for regular tables | `Customer` (not `tblCustomer`) |
| Schema | Always specify | `dbo.Customer`, `audit.ChangeLog` |
| Junction tables | Both entities + relationship | `CustomerProductFavorite` |

### Columns

| Rule | Convention | Example |
|------|------------|---------|
| Case | PascalCase | `FirstName`, `OrderDate` |
| ID columns | TableName + `Id` | `CustomerId`, `OrderId` |
| FK columns | Match parent PK name | `CustomerId` in Order references `CustomerId` in Customer |
| Booleans | Is/Has/Can prefix | `IsActive`, `HasAccess`, `CanEdit` |
| Dates | Descriptive suffix | `CreatedAt`, `UpdatedAt`, `OrderDate` |
| Status fields | Noun | `Status`, `OrderStatus` (not `StatusId` unless FK) |

### Constraints

| Type | Convention | Example |
|------|------------|---------|
| Primary key | `PK_TableName` | `PK_Customer` |
| Foreign key | `FK_ChildTable_ParentTable` | `FK_Order_Customer` |
| Unique | `UQ_TableName_Column(s)` | `UQ_Customer_Email` |
| Check | `CK_TableName_Description` | `CK_Order_PositiveQuantity` |
| Default | `DF_TableName_Column` | `DF_Customer_CreatedAt` |

### Indexes

| Type | Convention | Example |
|------|------------|---------|
| Non-clustered | `IX_TableName_Column(s)` | `IX_Order_CustomerId` |
| Clustered (non-PK) | `CX_TableName_Column(s)` | `CX_Order_OrderDate` |
| Unique | `UX_TableName_Column(s)` | `UX_Customer_Email` |
| Filtered | `IX_TableName_Column(s)_Description` | `IX_Order_Status_Active` |
| Covering | Mention key columns only | `IX_Order_CustomerId` (INCLUDE is implementation) |

### Views

| Rule | Convention | Example |
|------|------------|---------|
| Prefix | `vw_` | `vw_ActiveCustomer` |
| Description | Clear purpose | `vw_OrderSummary`, `vw_CustomerWithAddress` |

### Stored Procedures

| Rule | Convention | Example |
|------|------------|---------|
| Prefix | `usp_` | `usp_GetCustomerOrders` |
| Verb first | Action-oriented | `usp_CreateOrder`, `usp_UpdateCustomerStatus` |
| Avoid `sp_` | Reserved for system | Never `sp_GetCustomer` (conflicts with system procs) |

### Functions

| Rule | Convention | Example |
|------|------------|---------|
| Scalar | `fn_` prefix | `fn_CalculateTax` |
| Table-valued | `fn_` or `tvf_` | `fn_GetCustomerOrders` |

### Synonyms

| Rule | Convention | Example |
|------|------------|---------|
| Match target name when possible | Same as underlying object | `dbo.Customer` → `archive.Customer` |
| Or describe purpose | When bridging systems | `Legacy_Customer` |

---

## 27.2 Preferred Data Types

These are our standards. Deviate only with good reason.

### Strings

| Use Case | Type | Notes |
|----------|------|-------|
| General text | `NVARCHAR(n)` | Unicode support; specify length |
| Very long text | `NVARCHAR(MAX)` | Only when needed (notes, descriptions) |
| Fixed-length codes | `NCHAR(n)` | Rare; e.g., `NCHAR(2)` for country codes |
| ASCII-only, high volume | `VARCHAR(n)` | Exception case; document why |

**Default to NVARCHAR.** Storage is cheap; character encoding bugs are expensive.

### Numbers

| Use Case | Type | Notes |
|----------|------|-------|
| Integer identifiers | `INT` | 2.1 billion max; sufficient for most cases |
| Large identifiers | `BIGINT` | When INT is insufficient |
| Small integers | `TINYINT` or `SMALLINT` | Status codes, flags (save space) |
| Currency/financial | `DECIMAL(18,2)` | Exact precision; never use MONEY |
| Percentages | `DECIMAL(5,2)` | 0.00 to 100.00 |
| High-precision | `DECIMAL(p,s)` | Specify precision and scale |
| Floating point | `FLOAT` | Only for scientific data; imprecise |

**Never use MONEY.** It has hidden rounding behavior and limited precision. Use DECIMAL.

### Dates and Times

| Use Case | Type | Notes |
|----------|------|-------|
| Date and time | `DATETIME2(7)` | Preferred over DATETIME |
| Date only | `DATE` | When time doesn't matter |
| Time only | `TIME` | Rare |
| Legacy compatibility | `DATETIME` | Only for existing systems |

**Use DATETIME2.** Higher precision, larger range, 6-8 bytes (same as DATETIME).

### Other

| Use Case | Type | Notes |
|----------|------|-------|
| Boolean | `BIT` | 0/1; SQL Server has no true boolean |
| Unique identifiers | `UNIQUEIDENTIFIER` | GUIDs; use for external-facing IDs or replication |
| Binary data | `VARBINARY(n)` or `VARBINARY(MAX)` | Files, images |

---

## 27.3 File Structure

### Standard Project Layout

```
/DatabaseProject.sqlproj
/DatabaseProject.refactorlog
/DatabaseProject.publish.xml
/Local.publish.xml
/Dev.publish.xml
/Test.publish.xml
/Prod.publish.xml

/Security/
    Schemas.sql
    Roles.sql

/Tables/
    /dbo/
        dbo.Customer.sql
        dbo.Order.sql
        dbo.OrderLine.sql
    /audit/
        audit.ChangeLog.sql
    /archive/
        archive.OrderHistory.sql

/Views/
    /dbo/
        dbo.vw_ActiveCustomer.sql
        dbo.vw_OrderSummary.sql

/Stored Procedures/
    /dbo/
        dbo.usp_GetCustomerOrders.sql
        dbo.usp_CreateOrder.sql

/Functions/
    /dbo/
        dbo.fn_CalculateOrderTotal.sql

/Indexes/
    IX_Order_CustomerId.sql
    IX_Order_OrderDate.sql

/Synonyms/
    dbo.LegacyCustomer.sql

/Scripts/
    /PreDeployment/
        PreDeployment.sql
    /PostDeployment/
        PostDeployment.sql
        /Migrations/
            001_InitialSeed.sql
            002_BackfillCreatedAt.sql
        /ReferenceData/
            SeedCountries.sql
            SeedStatusCodes.sql
        /OneTime/
            Release_2025.02_Fixes.sql

/Snapshots/
    DatabaseProject_v1.0.dacpac
```

### Rules

| Rule | Rationale |
|------|-----------|
| One object per file | Easy to find, clear git history |
| File name matches object name | `dbo.Customer.sql` contains `dbo.Customer` table |
| Schema folders under each type | `/Tables/dbo/`, `/Tables/audit/` |
| Indexes can be inline or separate | Team choice; be consistent |
| Pre/Post scripts organized | `/Migrations/`, `/ReferenceData/`, `/OneTime/` |

---

## 27.4 Readability Standards

### Formatting

```sql
-- Table definition formatting
CREATE TABLE [dbo].[Customer]
(
    [CustomerId] INT IDENTITY(1,1) NOT NULL,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,
    [Email] NVARCHAR(200) NOT NULL,
    [PhoneNumber] NVARCHAR(20) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT (1),
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_Customer_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [UpdatedAt] DATETIME2(7) NULL,
    
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId]),
    CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email])
)
```

| Element | Standard |
|---------|----------|
| Keywords | UPPERCASE (`CREATE TABLE`, `NOT NULL`) |
| Object names | Bracket-quoted (`[dbo].[Customer]`) |
| Indentation | 4 spaces (not tabs) |
| Columns | One per line |
| Constraints | After columns, separated by blank line or at end |
| Commas | Trailing (at end of line, not beginning) |

### Comments

```sql
-- Single-line comment for brief notes

/*
Multi-line comment for:
- Complex business logic
- Non-obvious constraints
- Historical context
*/

-- For computed columns, explain the logic:
[TotalWithTax] AS ([Subtotal] * (1 + [TaxRate])) PERSISTED,  -- Tax calculated at order time
```

**Comment when:**
- Business logic isn't obvious
- Constraint exists for non-obvious reason
- Historical context helps future maintainers
- Workaround or special case

**Don't comment:**
- The obvious (`-- This is the customer ID`)
- What the code already says

### Views

Always enumerate columns:

```sql
-- Good
CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
WHERE IsActive = 1

-- Bad
CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT *
FROM dbo.Customer
WHERE IsActive = 1
```

---

# 28. Templates

---

## 28.1 New Table Template

```sql
/*
Table: dbo.YourTableName
Description: Brief description of what this table stores
Created: YYYY-MM-DD
Ticket: JIRA-XXXX
*/
CREATE TABLE [dbo].[YourTableName]
(
    -- Primary Key
    [YourTableNameId] INT IDENTITY(1,1) NOT NULL,
    
    -- Foreign Keys
    [RelatedTableId] INT NOT NULL,
    
    -- Business Columns
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_YourTableName_Status] DEFAULT ('Active'),
    
    -- Audit Columns
    [IsActive] BIT NOT NULL CONSTRAINT [DF_YourTableName_IsActive] DEFAULT (1),
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_YourTableName_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] NVARCHAR(128) NOT NULL CONSTRAINT [DF_YourTableName_CreatedBy] DEFAULT (SYSTEM_USER),
    [UpdatedAt] DATETIME2(7) NULL,
    [UpdatedBy] NVARCHAR(128) NULL,
    
    -- Constraints
    CONSTRAINT [PK_YourTableName] PRIMARY KEY CLUSTERED ([YourTableNameId]),
    CONSTRAINT [FK_YourTableName_RelatedTable] FOREIGN KEY ([RelatedTableId]) 
        REFERENCES [dbo].[RelatedTable]([RelatedTableId])
)
GO

-- Indexes
CREATE NONCLUSTERED INDEX [IX_YourTableName_RelatedTableId]
ON [dbo].[YourTableName]([RelatedTableId])
GO
```

---

## 28.2 Post-Deployment Migration Block Template

```sql
/*
Migration: Brief description
Ticket: JIRA-XXXX
Author: Your Name
Date: YYYY-MM-DD

Description:
Explain what this migration does and why.
*/

PRINT 'Migration NNN: Brief description...'

-- Idempotency check
IF EXISTS (SELECT 1 FROM dbo.YourTable WHERE YourCondition)
BEGIN
    -- Perform the migration
    UPDATE dbo.YourTable
    SET YourColumn = 'NewValue'
    WHERE YourCondition
    
    PRINT '  Updated ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows.'
END
ELSE
BEGIN
    PRINT '  No rows to update — skipping.'
END
GO
```

---

## 28.3 Idempotent Seed Data Template

```sql
/*
Reference Data: TableName
Description: Seeds the lookup/reference values for TableName
*/

PRINT 'Seeding TableName reference data...'

MERGE INTO [dbo].[TableName] AS target
USING (VALUES
    (1, 'Value1', 'Description 1', 1),
    (2, 'Value2', 'Description 2', 2),
    (3, 'Value3', 'Description 3', 3)
) AS source ([Id], [Code], [Description], [SortOrder])
ON target.[Id] = source.[Id]
WHEN MATCHED THEN
    UPDATE SET 
        [Code] = source.[Code],
        [Description] = source.[Description],
        [SortOrder] = source.[SortOrder]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Code], [Description], [SortOrder])
    VALUES (source.[Id], source.[Code], source.[Description], source.[SortOrder]);

PRINT '  TableName seeded/updated.'
GO
```

---

## 28.4 Migration Tracking Table

If you need migration tracking for complex multi-step migrations:

```sql
-- Create this table in your schema
CREATE TABLE [dbo].[MigrationHistory]
(
    [MigrationId] NVARCHAR(200) NOT NULL,
    [ExecutedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_MigrationHistory_ExecutedAt] DEFAULT (SYSUTCDATETIME()),
    [ExecutedBy] NVARCHAR(128) NOT NULL CONSTRAINT [DF_MigrationHistory_ExecutedBy] DEFAULT (SYSTEM_USER),
    [Description] NVARCHAR(500) NULL,
    
    CONSTRAINT [PK_MigrationHistory] PRIMARY KEY CLUSTERED ([MigrationId])
)
GO
```

Usage in migration scripts:

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.MigrationHistory WHERE MigrationId = 'MIG_2025.01_YourMigration')
BEGIN
    PRINT 'Running migration: MIG_2025.01_YourMigration'
    
    -- Your migration logic here
    
    INSERT INTO dbo.MigrationHistory (MigrationId, Description)
    VALUES ('MIG_2025.01_YourMigration', 'Brief description of what this did')
    
    PRINT 'Migration complete.'
END
ELSE
BEGIN
    PRINT 'Migration MIG_2025.01_YourMigration already applied — skipping.'
END
GO
```

---

## 28.5 Pre-Deployment Data Validation Template

```sql
/*
Pre-Deployment Validation: Describe what you're validating
Ticket: JIRA-XXXX
*/

PRINT 'Pre-deployment validation...'

-- Check for condition that would cause deployment to fail
DECLARE @ViolationCount INT

SELECT @ViolationCount = COUNT(*)
FROM dbo.YourTable
WHERE YourViolatingCondition

IF @ViolationCount > 0
BEGIN
    PRINT '  ERROR: Found ' + CAST(@ViolationCount AS VARCHAR(10)) + ' rows that violate the new constraint.'
    PRINT '  Deployment cannot proceed. Fix the data first.'
    RAISERROR('Pre-deployment validation failed. See above for details.', 16, 1)
    RETURN
END

PRINT '  Validation passed.'
GO
```

---

## 28.6 CDC Enable/Disable Template

**For Development (accepting gaps):**

```sql
-- Pre-deployment: Disable CDC
PRINT 'Disabling CDC on dbo.YourTable...'

IF EXISTS (SELECT 1 FROM cdc.change_tables WHERE source_object_id = OBJECT_ID('dbo.YourTable'))
BEGIN
    EXEC sys.sp_cdc_disable_table
        @source_schema = 'dbo',
        @source_name = 'YourTable',
        @capture_instance = 'dbo_YourTable'
    
    PRINT '  CDC disabled.'
END
ELSE
BEGIN
    PRINT '  CDC was not enabled — skipping.'
END
GO
```

```sql
-- Post-deployment: Re-enable CDC
PRINT 'Re-enabling CDC on dbo.YourTable...'

IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE source_object_id = OBJECT_ID('dbo.YourTable'))
BEGIN
    EXEC sys.sp_cdc_enable_table
        @source_schema = 'dbo',
        @source_name = 'YourTable',
        @role_name = 'cdc_reader',
        @capture_instance = 'dbo_YourTable',
        @supports_net_changes = 1
    
    PRINT '  CDC enabled.'
END
ELSE
BEGIN
    PRINT '  CDC already enabled — skipping.'
END
GO
```

**For Production (dual-instance):**

```sql
-- Post-deployment: Create new capture instance (after schema change)
PRINT 'Creating new CDC capture instance for dbo.YourTable...'

EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'YourTable',
    @role_name = 'cdc_reader',
    @capture_instance = 'dbo_YourTable_v2',  -- Versioned
    @supports_net_changes = 1

PRINT '  New capture instance dbo_YourTable_v2 created.'
PRINT '  Old instance dbo_YourTable_v1 still active.'
PRINT '  Drop old instance in next release after retention period.'
GO
```

---

## 28.7 Incident Report Template

Use after any incident for blameless post-mortem:

```markdown
# Incident Report: [Brief Title]

## Summary
- **Date/Time:** YYYY-MM-DD HH:MM (timezone)
- **Duration:** X hours/minutes
- **Severity:** Critical / High / Medium / Low
- **Environment:** Dev / Test / UAT / Prod
- **Ticket:** JIRA-XXXX

## What Happened
[One paragraph description of what occurred]

## Impact
- [Who was affected]
- [What functionality was broken]
- [Data impact, if any]

## Timeline
| Time | Event |
|------|-------|
| HH:MM | First indication of problem |
| HH:MM | Investigation began |
| HH:MM | Root cause identified |
| HH:MM | Fix deployed |
| HH:MM | Incident resolved |

## Root Cause
[What actually caused the incident]

## What Went Well
- [Things that helped during response]
- [Process that worked]

## What Could Be Improved
- [Gaps in process]
- [Missing safeguards]
- [Detection delays]

## Action Items
| Action | Owner | Due Date | Status |
|--------|-------|----------|--------|
| [Action] | [Name] | YYYY-MM-DD | Open |

## Playbook Updates
- [ ] New section needed: [Description]
- [ ] Existing section update: [Section + what to add]
- [ ] New anti-pattern: [Description]
- [ ] Decision aid update: [Description]

## Lessons Learned
[Key takeaways for the team]
```

---

# 29. CDC Table Registry

---

## Purpose

This registry tracks all CDC-enabled tables. **Check this before any schema change.**

---

## How to Query the Registry

Run this to see current CDC-enabled tables:

```sql
SELECT 
    OBJECT_SCHEMA_NAME(ct.source_object_id) AS [Schema],
    OBJECT_NAME(ct.source_object_id) AS [Table],
    ct.capture_instance AS [CaptureInstance],
    ct.create_date AS [EnabledDate],
    ct.supports_net_changes AS [NetChanges],
    (
        SELECT STRING_AGG(cc.column_name, ', ') 
        FROM cdc.captured_columns cc 
        WHERE cc.object_id = ct.object_id
    ) AS [TrackedColumns]
FROM cdc.change_tables ct
ORDER BY [Schema], [Table]
```

---

## Current CDC-Enabled Tables

*[This section should be populated with your actual tables. Example format:]*

| Schema | Table | Capture Instance | Enabled Date | Notes |
|--------|-------|------------------|--------------|-------|
| dbo | Customer | dbo_Customer_v1 | 2024-06-15 | Core entity |
| dbo | Order | dbo_Order_v1 | 2024-06-15 | Core entity |
| dbo | Policy | dbo_Policy_v1 | 2024-06-15 | Core entity |
| dbo | Claim | dbo_Claim_v1 | 2024-06-15 | Core entity |
| ... | ... | ... | ... | ... |

**Total CDC-enabled tables:** [XXX]

---

## Before Changing a CDC-Enabled Table

1. **Confirm it's on the list** — Query above or check this page
2. **Classify the change** — Does it require instance recreation? (See 18.5)
3. **Choose your strategy** — Development (gap OK) vs. Production (no gap)
4. **Document in PR** — Use the CDC Impact section of PR template
5. **Follow the protocol** — See Section 12: CDC and Schema Evolution

---

## Maintaining This Registry

**When to update:**
- New table added to CDC
- Table removed from CDC
- Capture instance recreated (version bump)

**How to update:**
- Edit this page directly
- Include in PR description when CDC changes are made
- Keep synchronized with actual database state

---

# 30. Glossary

---

| Term | Definition | OutSystems Equivalent |
|------|------------|----------------------|
| **ALTER TABLE** | SQL command to modify table structure | (Hidden — happens on Publish) |
| **Attribute** | A column in a table | Entity Attribute |
| **Backup** | Point-in-time copy of database | (Platform managed) |
| **BlockOnPossibleDataLoss** | SSDT setting that prevents destructive deployments | (Platform guardrails) |
| **Capture Instance** | CDC's record of a table's schema at a point in time | (N/A) |
| **CASCADE** | Automatic propagation of delete/update to child records | Delete Rule = Delete |
| **CDC (Change Data Capture)** | SQL Server feature tracking row-level changes | (N/A) |
| **CHECK constraint** | Rule validating column values | (N/A — enforced in app logic) |
| **Clustered index** | Index defining physical row order | (Hidden) |
| **Column** | A field in a table | Entity Attribute |
| **Computed column** | Column whose value is calculated from other columns | Calculated Attribute |
| **Constraint** | Rule enforced by the database | (Partially — some in platform) |
| **dacpac** | Compiled SSDT project; portable schema package | (N/A) |
| **DDL** | Data Definition Language (CREATE, ALTER, DROP) | (Hidden) |
| **Declarative** | Describing desired end state, not steps to get there | How Service Studio works |
| **DEFAULT constraint** | Value assigned when none provided | Default Value |
| **DML** | Data Manipulation Language (INSERT, UPDATE, DELETE) | (Hidden) |
| **Entity** | A table (OutSystems terminology) | Entity |
| **External Entity** | OutSystems entity pointing to external table | External Entity |
| **FK (Foreign Key)** | Constraint linking to parent table | Reference Attribute |
| **IDENTITY** | Auto-incrementing column property | Auto Number |
| **Idempotent** | Safe to run multiple times with same result | (N/A) |
| **Index** | Structure speeding up queries | Index (in Entity properties) |
| **Integration Studio** | Tool for managing external integrations | Integration Studio |
| **Is Mandatory** | OutSystems term for required field | NOT NULL |
| **JOIN** | Query combining rows from multiple tables | (Query equivalent) |
| **Lookup table** | Reference/code table with fixed values | Static Entity |
| **Migration** | Script moving data between states | (N/A) |
| **Multi-phase** | Change requiring multiple sequential releases | (N/A — Publish is atomic) |
| **NOT NULL** | Column requires a value | Is Mandatory = Yes |
| **NULL** | Absence of value | Is Mandatory = No |
| **Orphan data** | Child records with no parent | (Platform prevents) |
| **PK (Primary Key)** | Unique identifier for a row | Entity Identifier |
| **Post-deployment script** | SQL running after schema changes | (N/A) |
| **Pre-deployment script** | SQL running before schema changes | (N/A) |
| **Publish** | Deploy changes to database | Publish |
| **Publish profile** | Deployment configuration file | (N/A) |
| **Refactorlog** | XML tracking renames | (N/A — platform handles) |
| **Row** | Single record in a table | Entity Record |
| **Schema** | Namespace for database objects (dbo, audit) | (N/A — all in same space) |
| **Service Studio** | OutSystems IDE for application development | Service Studio |
| **sp_rename** | SQL Server command to rename objects | (Hidden) |
| **SSDT** | SQL Server Data Tools | (N/A) |
| **SSMS** | SQL Server Management Studio | Service Center (sort of) |
| **Stored procedure** | Named SQL routine | Server Action (sort of) |
| **Table** | Structure storing rows of data | Entity |
| **Temporal table** | System-versioned table tracking history | (N/A) |
| **Tier** | Risk classification for changes (1-4) | (N/A) |
| **Trigger** | Code executing on data events | (N/A) |
| **Unique constraint** | Enforces distinct values in column | (N/A — enforced in app) |
| **View** | Saved query appearing as table | (N/A) |
| **WITH NOCHECK** | Add constraint without validating existing data | (N/A) |

---

# 31. Resources

---

## Microsoft Documentation

| Resource | URL | Notes |
|----------|-----|-------|
| SSDT Documentation | [docs.microsoft.com/sql/ssdt](https://docs.microsoft.com/sql/ssdt) | Official SSDT docs |
| SQL Server Documentation | [docs.microsoft.com/sql](https://docs.microsoft.com/sql) | Complete reference |
| T-SQL Reference | [docs.microsoft.com/sql/t-sql](https://docs.microsoft.com/sql/t-sql) | Language reference |
| CDC Documentation | [docs.microsoft.com/sql/relational-databases/track-changes/about-change-data-capture](https://docs.microsoft.com/sql/relational-databases/track-changes/about-change-data-capture-sql-server) | CDC deep dive |

## Internal Links

| Resource | Location | Notes |
|----------|----------|-------|
| SSDT Repository | [Azure DevOps link] | Source of truth for schema |
| Pipeline Dashboard | [Azure DevOps link] | Deployment status |
| #ssdt-questions | Slack | Questions and help |
| #ssdt-playbook | Slack | Playbook feedback |

## Team Contacts

| Role | Name | Contact |
|------|------|---------|
| Playbook Owner | Danny | @danny |
| Dev Leads | [Names] | @team-leads |
| Principals | [Names] | @principals |

## External Resources

*[Curated list of helpful external articles/posts — add as discovered]*

| Title | URL | Why It's Useful |
|-------|-----|-----------------|
| [Example article] | [URL] | [Brief note] |

---

# 32. Contribution Guidelines

---

## This Playbook Is Living Infrastructure

The playbook evolves. When you encounter something undocumented, something wrong, or something confusing — that's a contribution opportunity.

**Contributing isn't optional extra work.** If you find a gap, you're already paying the cost of that gap. Documenting it helps everyone after you.

---

## How to Contribute

### Small Fixes (Typos, Clarifications)

1. Edit the page directly
2. Note the change in the Changelog
3. No PR required for minor fixes

### New Content or Significant Changes

1. Create a branch in the wiki repository (if git-backed) or draft in a doc
2. Write the content following the style guide below
3. Request review from a dev lead or Danny
4. Merge/publish after approval

### Proposing Structural Changes

For changes to the playbook's organization:

1. Open a discussion in #ssdt-playbook
2. Propose the change with rationale
3. Gather feedback
4. Implement if consensus reached

---

## Style Guide

### Voice and Tone

- **Direct and practical** — Get to the point
- **Warm but professional** — Not corporate, not casual
- **Confident but honest** — State what we know; admit what we don't
- **Teaching-oriented** — Explain why, not just what

### Structure

- **Headings** — Clear hierarchy; H2 for major sections, H3 for subsections
- **Lists** — Use for related items; not for prose
- **Tables** — Use for structured comparisons
- **Code blocks** — Always format SQL as code
- **Examples** — Use real schemas where possible; otherwise realistic examples

### Formatting

| Element | Format |
|---------|--------|
| SQL keywords | UPPERCASE in code blocks |
| Object names | Bracket-quoted: `[dbo].[Customer]` |
| File names | Backticks: `PostDeployment.sql` |
| UI elements | Bold: **Right-click → Rename** |
| New terms | Bold on first use, add to Glossary |
| Cross-references | Link to the section |

### Progressive Disclosure

Complex topics should layer:
1. **Summary** — One-liner for quick reference
2. **Details** — Full explanation for those who need it
3. **Deep dive** — Gotchas, edge cases, advanced scenarios

---

## What Belongs in the Playbook

| In Scope | Out of Scope |
|----------|--------------|
| SSDT processes and patterns | General SQL Server administration |
| Our team's standards and conventions | General programming tutorials |
| Decision frameworks for our context | Content not specific to our team |
| Templates for common tasks | One-off scripts (those live in code) |
| Troubleshooting for our environment | Generic troubleshooting |

---

## After You Contribute

1. Note your change in the Changelog (Section 33)
2. If you added a term, add it to the Glossary
3. If you added a pattern, cross-reference it from Operation Reference
4. Announce significant additions in #ssdt-playbook

---

# 33. Changelog

---

## Purpose

Track significant changes to the playbook so team members know what's new and maintainers can understand the document's evolution.

---

## Format

```
## [Date] - [Brief Description]

**What changed:**
- [Specific change 1]
- [Specific change 2]

**Why:**
[Brief rationale]

**Author:** [Name]
```

---

## History

### 2025-01-15 - Initial Playbook Publication

**What changed:**
- Complete playbook created covering all sections
- Orientation: Start Here, Big Picture, Translation Layer
- Foundations: Core concepts, dimensions, tiers
- Operations: Full taxonomy, multi-phase patterns, anti-patterns
- Process: Workflow, setup, change process, PR template, troubleshooting
- Reference: Standards, templates, glossary, resources

**Why:**
Team migrating to SSDT-managed External Entities needs comprehensive guidance.

**Author:** Danny

---

Absolutely. The Operation Reference deserves to be the crown jewel — the place where everything converges. Let me build it with the full weight of what we've developed.

---

# 16. Operation Reference

---

## How to Use This Section

This is your lookup reference for any database operation. It's organized by **what you're trying to do**, mirroring how you thought about changes in OutSystems.

**Each operation has three layers:**

| Layer | What It Gives You | When to Use |
|-------|-------------------|-------------|
| **Layer 1** | One-line summary, tier, mechanism | Quick classification; you've done this before |
| **Layer 2** | Full details: dimensions, steps, SSDT behavior | First time doing this operation; need specifics |
| **Layer 3** | Gotchas, edge cases, anti-patterns, related patterns | Troubleshooting; complex scenarios |

**Start with Layer 1. Go deeper only when you need to.**

---

## 16.1 Working with Entities (Tables)

*In OutSystems, these were your Entities. Now they're tables you define explicitly.*

---

### Create a New Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add a new table to the database | 1 | Pure Declarative | Enable separately if needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | No existing data |
| Reversibility | Symmetric | Delete the file |
| Dependency Scope | Self-contained | Nothing references it yet |
| Application Impact | Additive | Existing code unaffected |

**What you do:**

Create a new `.sql` file in `/Tables/{schema}/`:

```sql
-- /Tables/dbo/dbo.CustomerPreference.sql

CREATE TABLE [dbo].[CustomerPreference]
(
    [CustomerPreferenceId] INT IDENTITY(1,1) NOT NULL,
    [CustomerId] INT NOT NULL,
    [PreferenceKey] NVARCHAR(100) NOT NULL,
    [PreferenceValue] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT [DF_CustomerPreference_CreatedAt] DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] NVARCHAR(128) NOT NULL CONSTRAINT [DF_CustomerPreference_CreatedBy] DEFAULT (SYSTEM_USER),
    
    CONSTRAINT [PK_CustomerPreference] PRIMARY KEY CLUSTERED ([CustomerPreferenceId]),
    CONSTRAINT [FK_CustomerPreference_Customer] FOREIGN KEY ([CustomerId]) 
        REFERENCES [dbo].[Customer]([CustomerId])
)
```

**What SSDT generates:**
```sql
CREATE TABLE [dbo].[CustomerPreference] (...)
```

Verbatim — the table doesn't exist, so SSDT creates it.

**Verification:**
- Build succeeds
- Table appears in local database after publish
- Constraints and FKs are in place

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| FK to non-existent table | If you reference a table not in your project, build fails. Add the parent table first, or add it to the same PR. |
| Missing from project | If you create the file but don't add it to the project, it won't deploy. Verify in Solution Explorer. |
| CDC enablement | New tables aren't CDC-enabled automatically. If this table needs audit tracking, add CDC enablement to post-deployment. |

**Related:**
- Template: [28.1 New Table Template](#281-new-table-template)
- If CDC needed: [12. CDC and Schema Evolution](#12-cdc-and-schema-evolution)

---

### Rename an Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change a table's name while preserving data | 3 | Declarative + Refactorlog | Instance recreation required |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data untouched |
| Reversibility | Symmetric | Rename back |
| Dependency Scope | Cross-boundary | Everything references tables by name |
| Application Impact | Breaking | All callers must update |

**What you do:**

1. In Visual Studio Solution Explorer, right-click the table file
2. Select **Rename**
3. Enter the new name
4. Visual Studio updates the file AND creates a refactorlog entry

**What SSDT generates (with refactorlog):**
```sql
EXEC sp_rename 'dbo.OldTableName', 'NewTableName', 'OBJECT'
```

**What SSDT generates (WITHOUT refactorlog):**
```sql
DROP TABLE [dbo].[OldTableName]
CREATE TABLE [dbo].[NewTableName] (...)
-- ALL DATA LOST
```

**Verification:**
- Check `.refactorlog` file was modified
- Schema Compare shows rename, not drop+create
- Generated script uses `sp_rename`

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Naked Rename** | Editing the file directly without refactorlog causes data loss. See [Anti-Pattern 19.1](#191-the-naked-rename). |
| Dynamic SQL | Any code that constructs table names as strings won't be caught by SSDT's analysis. Search codebase manually. |
| External systems | ETL, reports, and other systems won't update automatically. Coordinate with stakeholders. |
| CDC impact | Capture instance references old table name. Must recreate. |

**Related:**
- Anti-pattern: [19.1 The Naked Rename](#191-the-naked-rename)
- Pattern: [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility)
- Section: [9. The Refactorlog and Rename Discipline](#9-the-refactorlog-and-rename-discipline)

---

### Delete an Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Remove a table and all its data permanently | 4 | Declarative (guarded by BlockOnPossibleDataLoss) | Disable before drop |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | All rows gone |
| Reversibility | Lossy | Backup restore only path back |
| Dependency Scope | Cross-boundary | FKs, views, procs, ETL, reports |
| Application Impact | Breaking | Catastrophic if anything still references |

**What you do:**

1. Follow the deprecation workflow first (soft-deprecate → verify unused → soft-delete)
2. Delete the `.sql` file from the project
3. If `DropObjectsNotInSource=True`, SSDT generates the DROP
4. If `DropObjectsNotInSource=False`, you need a pre-deployment script

**What SSDT generates:**
```sql
DROP TABLE [dbo].[TableName]
```

**Protection:** `BlockOnPossibleDataLoss=True` will halt deployment if table has rows.

**Pre-flight checklist:**
- [ ] Table has been soft-deprecated for defined period
- [ ] Query confirms zero rows or data has been archived
- [ ] `sys.dm_sql_referencing_entities` returns empty
- [ ] Text search across codebase for table name
- [ ] ETL/reporting team confirms no dependencies
- [ ] Backup verified and restoration tested
- [ ] CDC disabled on table (if enabled)

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Foreign keys | If other tables have FKs pointing to this table, drop will fail. Drop those FKs first. |
| DropObjectsNotInSource=False | In production, this setting is usually False. You'll need explicit pre-deployment script to drop. |
| CDC | Disable CDC before dropping table, otherwise CDC objects become orphaned. |
| Views/Procs | Dependent objects will break. SSDT build should catch these if they're in the project. |

**Related:**
- Pattern: [17.5 Safe Column Removal (4-Phase)](#175-pattern-safe-column-removal-4-phase) — same workflow applies to tables

---

### Archive an Entity

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Move old data to archive while preserving it | 3-4 | Multi-Phase | Affects both source and destination |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Data moves between tables |
| Reversibility | Effortful | Can restore, but requires scripted work |
| Dependency Scope | Inter-table to Cross-boundary | FKs must be handled; queries need awareness |
| Application Impact | Breaking for archived data | Active data unaffected if partitioned correctly |

**What you do:**

This is a multi-phase operation. SSDT doesn't have "move data" — you script it.

**Phase 1: Create archive destination**
```sql
-- Declarative: Create archive table (if new)
CREATE TABLE [archive].[Order_Pre2024] (...)
```

**Phase 2: Migrate data (post-deployment)**
```sql
-- Batch to manage transaction log
WHILE 1=1
BEGIN
    DELETE TOP (10000) FROM dbo.[Order]
    OUTPUT DELETED.* INTO archive.Order_Pre2024
    WHERE OrderDate < '2024-01-01'
    
    IF @@ROWCOUNT = 0 BREAK
END
```

**Phase 3: Verify**
```sql
-- Confirm counts match
SELECT 'Source' AS Location, COUNT(*) FROM dbo.[Order] WHERE OrderDate < '2024-01-01'
UNION ALL
SELECT 'Archive', COUNT(*) FROM archive.Order_Pre2024
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Transaction log | Large data movements bloat the log. Batch operations. |
| FKs | Child records must be archived first, or FKs disabled. |
| Cross-database | If archiving to different database, no FK enforcement. Consider different backup/retention policies. |
| CDC | Both tables may need CDC consideration. Archive table typically doesn't need CDC. |

---

## 16.2 Working with Attributes (Columns)

*In OutSystems, these were Entity Attributes. Now they're columns you define in the table's `.sql` file.*

---

### Add an Attribute (Nullable)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add a new column that allows NULL values | 1 | Pure Declarative | Instance recreation if tracking needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Existing rows get NULL |
| Reversibility | Symmetric | Remove from definition |
| Dependency Scope | Self-contained | Nothing references it yet |
| Application Impact | Additive | Existing queries still work |

**What you do:**

Edit the table's `.sql` file, add the column:

```sql
-- Add within the CREATE TABLE statement
[MiddleName] NVARCHAR(50) NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Person] ADD [MiddleName] NVARCHAR(50) NULL;
```

You never write this ALTER. You declare; SSDT transitions.

**Verification:**
- Build succeeds
- Column appears in local database
- Existing rows have NULL for new column

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Position | SSDT may add at end of table. If `IgnoreColumnOrder=False`, could trigger rebuild. Keep `IgnoreColumnOrder=True`. |
| CDC | If table is CDC-enabled and you want this column tracked, you must recreate the capture instance. |

**Related:**
- CDC: [18.5 CDC Impact Checker](#185-cdc-impact-checker)

---

### Add an Attribute (Required / NOT NULL)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add a new column that requires a value | 2 | Declarative (with default) | Instance recreation if tracking needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only (if default provided) | Existing rows get default value |
| Reversibility | Symmetric | Remove from definition |
| Dependency Scope | Self-contained | Nothing references it yet |
| Application Impact | Contractual | New inserts must provide value (or rely on default) |

**What you do:**

Add column with a default constraint:

```sql
[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Customer_Status] DEFAULT ('Active'),
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Customer] ADD [Status] NVARCHAR(20) NOT NULL
    CONSTRAINT [DF_Customer_Status] DEFAULT ('Active');
```

SQL Server applies the default to existing rows automatically.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Optimistic NOT NULL** | Adding NOT NULL without a default to a populated table fails at deploy. See [Anti-Pattern 19.2](#192-the-optimistic-not-null). |
| GenerateSmartDefaults | If `True`, SSDT auto-generates defaults. Don't rely on this in production — be explicit. |
| Large tables | Adding NOT NULL with default may cause table rebuild on older SQL Server versions. Modern versions (2012+) are metadata-only for constants. |

**Related:**
- Anti-pattern: [19.2 The Optimistic NOT NULL](#192-the-optimistic-not-null)

---

### Make an Attribute Required (NULL → NOT NULL)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Make an existing nullable column required | 2-3 | Pre-Deployment + Declarative | No instance recreation needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Existing NULLs must be filled |
| Reversibility | Effortful | Can flip back, but you've altered data |
| Dependency Scope | Intra-table | Constraint is local |
| Application Impact | Breaking | INSERTs/UPDATEs without value will fail |

**What you do:**

**Step 1: Pre-deployment — backfill NULLs**
```sql
PRINT 'Backfilling NULL emails...'

UPDATE dbo.Customer
SET Email = 'unknown@placeholder.com'
WHERE Email IS NULL

PRINT 'Backfilled ' + CAST(@@ROWCOUNT AS VARCHAR) + ' rows.'
```

**Step 2: Declarative — change the definition**
```sql
-- Change NULL to NOT NULL
[Email] NVARCHAR(200) NOT NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(200) NOT NULL;
```

**Verification before deploying:**
```sql
-- Must return 0
SELECT COUNT(*) FROM dbo.Customer WHERE Email IS NULL
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Deploy fails if NULLs exist | SSDT will fail deployment, not build. Pre-validate. |
| Concurrent inserts | If app is inserting NULLs while you deploy, backfill won't catch them. Consider adding default first. |
| Index rebuild | May trigger index rebuild if column is in an index. |

**Related:**
- Pattern: [17.2 NULL → NOT NULL on Populated Table](#172-pattern-null--not-null-on-populated-table)

---

### Make an Attribute Optional (NOT NULL → NULL)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Make an existing required column optional | 1-2 | Pure Declarative | No instance recreation needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | No data changes needed |
| Reversibility | Effortful | Going back requires handling NULLs that may have appeared |
| Dependency Scope | Intra-table | Local constraint |
| Application Impact | Additive | Existing code still works |

**What you do:**

Change the definition:
```sql
-- Change NOT NULL to NULL
[MiddleName] NVARCHAR(50) NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Person] ALTER COLUMN [MiddleName] NVARCHAR(50) NULL;
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Application handling | Will your application handle NULLs correctly? It wasn't expecting them before. |
| Reports/analytics | Downstream systems may not handle NULLs well. |

---

### Change an Attribute's Data Type (Implicit Conversion)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change type when SQL Server can convert automatically | 2 | Pure Declarative | Instance recreation required |

---

**Layer 2**

Implicit conversions are safe widening conversions where no data can be lost:
- `INT` → `BIGINT`
- `VARCHAR(50)` → `VARCHAR(100)`
- `VARCHAR(n)` → `NVARCHAR(n)`
- `DECIMAL(10,2)` → `DECIMAL(18,2)`

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | SQL Server converts without loss |
| Reversibility | Effortful | Reverse may not be implicit |
| Dependency Scope | Inter-table | Views, procs may have type expectations |
| Application Impact | Contractual | Usually works, but app type handling may differ |

**What you do:**

Change the definition:
```sql
-- INT to BIGINT
[CustomerId] BIGINT NOT NULL,
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Order] ALTER COLUMN [CustomerId] BIGINT NOT NULL;
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| VARCHAR → NVARCHAR | Doubles storage. May bloat indexes past limits. |
| Index key limits | Non-clustered index keys can't exceed 1700 bytes (900 in older versions). Widening columns in indexes may fail. |
| CDC | Capture instance has old type. Must recreate. |

---

### Change an Attribute's Data Type (Explicit Conversion)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change type when data must be explicitly converted | 3-4 | Multi-Phase | Instance recreation required |

---

**Layer 2**

Explicit conversions require transformation:
- `VARCHAR` → `DATE`
- `INT` → `UNIQUEIDENTIFIER`
- `DATETIME` → `DATE`
- Any narrowing conversion

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Values must be parsed/converted |
| Reversibility | Effortful to Lossy | Depends on whether round-trip is possible |
| Dependency Scope | Inter-table | Everything referencing this column |
| Application Impact | Breaking | Queries, parameters, application code affected |

**What you do:**

This requires multi-phase. You cannot simply change the type.

**Phase 1:** Add new column with target type
**Phase 2:** Migrate data with conversion logic
**Phase 3:** Application transitions to new column
**Phase 4:** Drop old column, rename new column

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| SSDT may try single-step | SSDT might attempt a direct ALTER that fails. Own this manually. |
| Conversion failures | Not all values may convert. Handle failures explicitly. |
| Multiple releases | This spans at least 2-3 releases typically. |

**Related:**
- Pattern: [17.1 Explicit Conversion Data Type Change](#171-pattern-explicit-conversion-data-type-change)

---

### Change an Attribute's Length (Widen)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Increase column length/precision | 2 | Pure Declarative | No instance recreation needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | All existing values still fit |
| Reversibility | Effortful | Could narrow back, but must verify data fits |
| Dependency Scope | Intra-table | Indexes may rebuild |
| Application Impact | Additive | Existing code continues to work |

**What you do:**

Change the definition:
```sql
-- VARCHAR(50) to VARCHAR(100)
[Email] NVARCHAR(200) NOT NULL,  -- was NVARCHAR(100)
```

**What SSDT generates:**
```sql
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(200) NOT NULL;
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Index key limits | Widening past 900/1700 byte limit fails if column is in index key. |
| Large tables | May trigger metadata operation or rebuild depending on SQL version. |

---

### Change an Attribute's Length (Narrow)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Decrease column length/precision | 4 | Pre-Deployment + Declarative | No instance recreation needed |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | Values exceeding new length will truncate or fail |
| Reversibility | Lossy | Truncated data is gone forever |
| Dependency Scope | Intra-table | Indexes, plus application expectations |
| Application Impact | Breaking | App may attempt values that no longer fit |

**What you do:**

**Step 1: Validate data fits**
```sql
-- Check current max length
SELECT MAX(LEN(Email)) AS MaxLength FROM dbo.Customer

-- Find values that won't fit
SELECT CustomerId, Email, LEN(Email) AS Length
FROM dbo.Customer
WHERE LEN(Email) > 100  -- New limit
```

**Step 2: Handle violations** (pre-deployment or fix data)

**Step 3: Change definition**
```sql
[Email] NVARCHAR(100) NOT NULL,  -- was NVARCHAR(200)
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Ambitious Narrowing** | SSDT will generate the ALTER. SQL Server will fail or truncate. Validate first. See [Anti-Pattern 19.4](#194-the-ambitious-narrowing). |
| BlockOnPossibleDataLoss | This setting should catch it if data exceeds new length. But validate anyway. |

**Related:**
- Anti-pattern: [19.4 The Ambitious Narrowing](#194-the-ambitious-narrowing)

---

### Rename an Attribute

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change a column's name while preserving data | 3 | Declarative + Refactorlog | Instance recreation required |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data untouched |
| Reversibility | Symmetric | Rename back |
| Dependency Scope | Inter-table to Cross-boundary | Views, procs, app code, reports, ETL |
| Application Impact | Breaking | All callers must update |

**What you do:**

1. In Visual Studio, open the table file
2. Right-click on the column name
3. Select **Rename**
4. Enter new name
5. Visual Studio updates the file AND creates refactorlog entry

**What SSDT generates (with refactorlog):**
```sql
EXEC sp_rename 'dbo.Person.FirstName', 'GivenName', 'COLUMN'
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Naked Rename** | Without refactorlog, SSDT drops column and creates new one. Data loss. See [Anti-Pattern 19.1](#191-the-naked-rename). |
| Dynamic SQL | Queries building column names as strings won't be caught. Search codebase. |
| ORM mappings | Application ORMs may have column name assumptions. |
| CDC | Capture instance references old column name. Must recreate. |

**Related:**
- Anti-pattern: [19.1 The Naked Rename](#191-the-naked-rename)
- Section: [9. The Refactorlog and Rename Discipline](#9-the-refactorlog-and-rename-discipline)

---

### Delete an Attribute

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Remove a column and all its data permanently | 3-4 | Declarative (with deprecation workflow) | Instance recreation required |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-destructive | Column values gone |
| Reversibility | Lossy | Cannot recover without backup |
| Dependency Scope | Inter-table to Cross-boundary | Views, procs, reports, ETL may reference |
| Application Impact | Breaking | Anything referencing this column will fail |

**What you do:**

Follow the 4-phase deprecation workflow:

**Phase 1: Soft-deprecate** — Document or rename to signal deprecation
**Phase 2: Stop writes** — Application stops using the column
**Phase 3: Verify unused** — Query confirms no recent writes, no dependencies
**Phase 4: Drop** — Remove from table definition

**Verification before Phase 4:**
```sql
-- Check for dependencies
SELECT 
    referencing_entity_name, 
    referencing_class_desc
FROM sys.dm_sql_referencing_entities('dbo.Customer', 'OBJECT')

-- Check for recent data (if tracking exists)
SELECT MAX(UpdatedAt) FROM dbo.Customer WHERE LegacyColumn IS NOT NULL
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| BlockOnPossibleDataLoss | If column has data, deployment halts. This is protection. |
| Index dependencies | If column is in an index, drop index first (or SSDT will). |
| Computed column dependencies | If column is referenced by computed column, drop that first. |
| CDC | Even dropped columns affect capture instance. Must recreate. |

**Related:**
- Pattern: [17.5 Safe Column Removal (4-Phase)](#175-pattern-safe-column-removal-4-phase)

---

## 16.3 Working with Identifiers and References (Keys)

*In OutSystems, the Identifier was automatic and References were drawn as lines. Now you define them explicitly.*

---

### Define the Identifier (Create Primary Key)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Define the unique identifier for a table | 1 (new table) / 2 (existing) | Pure Declarative | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only (new) / Data-touching (existing — index creation scans rows) | |
| Reversibility | Symmetric | Remove constraint |
| Dependency Scope | Inter-table | FKs from other tables reference this |
| Application Impact | Additive | Enforces uniqueness going forward |

**What you do:**

```sql
-- Inline with table definition
CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([CustomerId])
```

For composite keys:
```sql
CONSTRAINT [PK_OrderLine] PRIMARY KEY CLUSTERED ([OrderId], [LineNumber])
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing table with data | Adding PK builds clustered index. Large table = time and blocking. |
| Duplicate values | If data has duplicates, PK creation fails. Clean first. |
| Identity vs. natural key | IDENTITY columns are auto-incrementing. Natural keys must be managed by application. |

---

### Create a Reference to Another Entity (Foreign Key)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Link a column to a parent table's primary key | 2 (clean data) / 3 (orphans exist) | Declarative / Multi-Phase | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving (validates existing) | SQL Server checks all existing rows |
| Reversibility | Symmetric | Remove constraint |
| Dependency Scope | Inter-table | Creates dependency between tables |
| Application Impact | Contractual | Inserts/updates now validated |

**What you do (clean data):**

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
```

**Pre-flight check:**
```sql
-- Find orphans
SELECT o.OrderId, o.CustomerId
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON o.CustomerId = c.CustomerId
WHERE c.CustomerId IS NULL
-- Must return 0 rows
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The Forgotten FK Check** | If orphans exist, deploy fails. Always check first. See [Anti-Pattern 19.3](#193-the-forgotten-fk-check). |
| WITH NOCHECK | Can add FK without validation, but it's untrusted. See pattern for proper handling. |
| Large tables | FK validation scans the table. May take time. |

**Related:**
- Anti-pattern: [19.3 The Forgotten FK Check](#193-the-forgotten-fk-check)
- Pattern: [17.4 Add FK with Orphan Data](#174-pattern-add-fk-with-orphan-data)

---

### Change Cascade Behavior

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change what happens when parent record is deleted/updated | 3 | Pure Declarative (DROP + ADD) | No impact |

---

**Layer 2**

**Options:**
| Setting | On DELETE | On UPDATE |
|---------|-----------|-----------|
| `NO ACTION` (default) | Fail if children exist | Fail if children reference old value |
| `CASCADE` | Delete all children automatically | Update all children automatically |
| `SET NULL` | Set FK column to NULL | Set FK column to NULL |
| `SET DEFAULT` | Set FK column to default | Set FK column to default |

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Behavior change, not data change |
| Reversibility | Symmetric | Change back |
| Dependency Scope | Inter-table | Affects delete/update behavior across tables |
| Application Impact | Contractual to Breaking | Deletes now cascade — could be surprising |

**What you do:**

```sql
CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) 
    REFERENCES [dbo].[Customer]([CustomerId])
    ON DELETE CASCADE
    ON UPDATE NO ACTION
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| CASCADE danger | Adding CASCADE means deletes propagate silently. A delete that previously failed now removes child records. |
| Audit implications | Cascaded deletes may not be captured the way direct deletes are. |
| Multi-level cascade | CASCADE can chain through multiple tables. Understand the full graph. |

---

### Remove a Reference (Drop Foreign Key)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Remove the link between tables | 2 | Pure Declarative | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Data unchanged |
| Reversibility | Effortful | Adding back requires data validation |
| Dependency Scope | Inter-table | Removes linkage |
| Application Impact | Additive | Less restrictive |

**What you do:**

Remove the constraint from the table definition. SSDT generates:
```sql
ALTER TABLE [dbo].[Order] DROP CONSTRAINT [FK_Order_Customer]
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Why are you dropping? | If it's blocking something (type change, table drop), document that. If permanent, understand the data integrity implications. |
| Query optimizer | Trusted FKs help the optimizer. Dropping may affect query plans. |

---

## 16.4 Working with Indexes

*In OutSystems, indexes were configured in Entity Properties. Now you define them explicitly.*

---

### Add an Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create an index to improve query performance | 1 (small table) / 2 (large table) | Pure Declarative | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-preserving | Scans table to build index |
| Reversibility | Symmetric | Drop the index |
| Dependency Scope | Intra-table | Tied to table structure |
| Application Impact | Additive | Only affects performance |

**What you do:**

```sql
-- Basic non-clustered
CREATE NONCLUSTERED INDEX [IX_Order_CustomerId]
ON [dbo].[Order]([CustomerId])

-- Covering index
CREATE NONCLUSTERED INDEX [IX_Order_CustomerId_Covering]
ON [dbo].[Order]([CustomerId])
INCLUDE ([OrderDate], [TotalAmount])

-- Filtered index
CREATE NONCLUSTERED INDEX [IX_Order_Active]
ON [dbo].[Order]([OrderDate])
WHERE [Status] = 'Active'

-- Unique index
CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Email]
ON [dbo].[Customer]([Email])
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Build time | Large tables take time to index. Consider maintenance window. |
| Blocking | Default index creation is offline (blocks writes). Enterprise Edition supports ONLINE. |
| Filtered index limitations | Only helps queries with matching predicates. Parameterized queries often don't benefit. |
| Too many indexes | Each index adds write overhead. Balance read vs. write performance. |

---

### Modify an Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Change index columns or properties | 2 | Pure Declarative (DROP + CREATE) | No impact |

---

**Layer 2**

**Common modifications:**
- Add/remove columns from key
- Add/remove INCLUDE columns
- Change from non-unique to unique (or vice versa)
- Add/remove filter

**What SSDT generates:** DROP existing index, CREATE new index

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Rebuild time | Modification = full rebuild. Same timing concerns as creation. |
| Unique → non-unique | Safe (less restrictive) |
| Non-unique → unique | May fail if duplicates exist |

---

### Remove an Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Drop an index | 2 | Pure Declarative | No impact |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Schema-only | Index structure removed, data unchanged |
| Reversibility | Effortful | Recreating requires rebuild time |
| Dependency Scope | Intra-table | May affect query performance |
| Application Impact | Additive (structurally) | May cause performance regression |

**What you do:**

Remove the index definition. SSDT generates:
```sql
DROP INDEX [IX_Order_CustomerId] ON [dbo].[Order]
```

**Before dropping, check usage:**
```sql
SELECT 
    i.name AS IndexName,
    s.user_seeks,
    s.user_scans,
    s.user_lookups,
    s.last_user_seek
FROM sys.indexes i
LEFT JOIN sys.dm_db_index_usage_stats s 
    ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE i.object_id = OBJECT_ID('dbo.Order')
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Performance regression | Queries won't break, but may get much slower. Review query plans. |
| Unused indexes | Low usage stats may indicate safe to drop. But beware infrequent but critical queries. |

---

### Rebuild / Reorganize Index

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Maintenance operation for index health | 2-3 | Script-Only (not declarative) | No impact |

---

**Layer 2**

This is **operational maintenance**, not schema change. SSDT doesn't manage it.

**Reorganize (online, less impactful):**
```sql
ALTER INDEX [IX_Order_CustomerId] ON [dbo].[Order] REORGANIZE
```

**Rebuild (offline by default, more effective):**
```sql
ALTER INDEX [IX_Order_CustomerId] ON [dbo].[Order] REBUILD

-- Online (Enterprise Edition)
ALTER INDEX [IX_Order_CustomerId] ON [dbo].[Order] REBUILD WITH (ONLINE = ON)
```

**Rule of thumb:**
- 10-30% fragmentation: REORGANIZE
- >30% fragmentation: REBUILD

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Not declarative | This goes in maintenance jobs, not SSDT project. |
| Offline blocking | Default REBUILD blocks writes. Use ONLINE for production. |
| Transaction log | Rebuilds generate log. Plan accordingly. |

---

## 16.5 Working with Static Entities (Lookup Tables)

*In OutSystems, Static Entities had their data built in. Now you have a table structure (declarative) plus seed data (post-deployment script).*

---

### Create a Lookup Table with Seed Data

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create a reference/code table with fixed values | 1-2 | Declarative (structure) + Post-Deployment (data) | Usually not CDC-enabled |

---

**Layer 2**

**Two pieces:**
1. Table structure (declarative `.sql` file)
2. Seed data (idempotent post-deployment script)

**Structure:**
```sql
-- /Tables/dbo/dbo.OrderStatus.sql
CREATE TABLE [dbo].[OrderStatus]
(
    [StatusId] INT NOT NULL,
    [StatusCode] NVARCHAR(20) NOT NULL,
    [StatusName] NVARCHAR(50) NOT NULL,
    [SortOrder] INT NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_OrderStatus_IsActive] DEFAULT (1),
    
    CONSTRAINT [PK_OrderStatus] PRIMARY KEY CLUSTERED ([StatusId]),
    CONSTRAINT [UQ_OrderStatus_Code] UNIQUE ([StatusCode])
)
```

**Seed data:**
```sql
-- /Scripts/PostDeployment/ReferenceData/SeedOrderStatus.sql

MERGE INTO [dbo].[OrderStatus] AS target
USING (VALUES
    (1, 'PENDING', 'Pending', 1, 1),
    (2, 'PROCESSING', 'Processing', 2, 1),
    (3, 'SHIPPED', 'Shipped', 3, 1),
    (4, 'DELIVERED', 'Delivered', 4, 1),
    (5, 'CANCELLED', 'Cancelled', 5, 1)
) AS source ([StatusId], [StatusCode], [StatusName], [SortOrder], [IsActive])
ON target.[StatusId] = source.[StatusId]
WHEN MATCHED THEN
    UPDATE SET 
        [StatusCode] = source.[StatusCode],
        [StatusName] = source.[StatusName],
        [SortOrder] = source.[SortOrder],
        [IsActive] = source.[IsActive]
WHEN NOT MATCHED THEN
    INSERT ([StatusId], [StatusCode], [StatusName], [SortOrder], [IsActive])
    VALUES (source.[StatusId], source.[StatusCode], source.[StatusName], source.[SortOrder], source.[IsActive]);
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| IDENTITY vs. explicit IDs | For lookup tables, usually use explicit IDs (no IDENTITY) so values are consistent across environments. |
| Idempotency | Use MERGE for upsert. Don't use plain INSERT. |
| FK dependencies | Seed parent tables before child tables. |

**Related:**
- Template: [28.3 Idempotent Seed Data Template](#283-idempotent-seed-data-template)

---

### Add/Modify Seed Data

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add or update values in a lookup table | 1-2 | Post-Deployment Script | Usually not CDC-enabled |

---

**Layer 2**

**What you do:**

Edit the seed data script. Add new values or modify existing:

```sql
-- Add to the VALUES list
    (6, 'RETURNED', 'Returned', 6, 1),  -- New value
```

MERGE handles both insert (new) and update (existing).

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Deleting values | MERGE doesn't delete by default. If you need to deactivate, set `IsActive = 0` rather than deleting. |
| FK references | Can't delete values that are referenced by FKs. |

---

### Extract Values to a Lookup Table

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Convert inline values to a normalized lookup table | 3 | Multi-Phase | May affect both tables |

---

**Layer 2**

**Scenario:** `Order.Status` is `VARCHAR(20)` with values like 'Pending', 'Active'. Extract to `OrderStatus` table.

**Phase 1:** Create lookup table, populate with distinct values
**Phase 2:** Add `Order.StatusId` column
**Phase 3:** Post-deployment: populate `StatusId` from `Status` text
**Phase 4:** Application transitions to FK
**Phase 5:** Next release: drop `Status` column, add FK constraint

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Data quality | What if `Status` has typos or variations? Clean before extracting. |
| Multi-release | This spans multiple releases. Plan the sequence. |

---

## 16.6 Constraints and Validation

*OutSystems enforced some constraints automatically. Now you have explicit control.*

---

### Add a Default Value

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Set a value to use when none is provided | 1 | Pure Declarative | No impact |

---

**Layer 2**

**What you do:**
```sql
[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Order_Status] DEFAULT ('Pending'),
```

Or for existing column:
```sql
-- Add as separate statement
ALTER TABLE [dbo].[Order] ADD CONSTRAINT [DF_Order_Status] DEFAULT ('Pending') FOR [Status]
```

SSDT generates the appropriate ALTER.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Always name constraints | Use `DF_TableName_ColumnName`. Auto-generated names are ugly and vary. |
| Existing default | If changing, SSDT drops old and creates new. Brief window with no default. |

---

### Add a Uniqueness Rule (Unique Constraint)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Enforce distinct values in a column | 2 | Pure Declarative | No impact |

---

**Layer 2**

**What you do:**
```sql
CONSTRAINT [UQ_Customer_Email] UNIQUE ([Email])
```

**Pre-flight check:**
```sql
-- Find duplicates
SELECT Email, COUNT(*) AS Count
FROM dbo.Customer
GROUP BY Email
HAVING COUNT(*) > 1
-- Must return 0 rows
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Duplicates | Deploy fails if duplicates exist. Clean first. |
| NULLs | Standard unique constraint allows one NULL. For multiple NULLs, use filtered unique index. |

---

### Add a Validation Rule (Check Constraint)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Enforce business rules at the database level | 1-2 | Pure Declarative | No impact |

---

**Layer 2**

**What you do:**
```sql
CONSTRAINT [CK_OrderLine_PositiveQuantity] CHECK ([Quantity] > 0)
CONSTRAINT [CK_Order_ValidDates] CHECK ([EndDate] >= [StartDate])
```

**Pre-flight check:**
```sql
-- Find violations
SELECT * FROM dbo.OrderLine WHERE Quantity <= 0
-- Must return 0 rows
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing violations | Deploy fails if existing data violates. Clean or use WITH NOCHECK (not recommended). |
| Complex checks | Very complex checks can impact performance. Keep them simple. |

---

### Enable/Disable Constraint

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Temporarily suspend constraint enforcement | 3 | Script-Only | No impact |

---

**Layer 2**

This is **operational**, not declarative. SSDT manages existence, not enabled state.

**Disable:**
```sql
ALTER TABLE dbo.[Order] NOCHECK CONSTRAINT FK_Order_Customer
```

**Re-enable WITH validation:**
```sql
ALTER TABLE dbo.[Order] WITH CHECK CHECK CONSTRAINT FK_Order_Customer
```

**Note:** `NOCHECK` leaves the constraint untrusted. `WITH CHECK CHECK` validates and restores trust.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Untrusted constraints | Optimizer ignores them. Always restore trust. |
| Use case | Typically for bulk loads or multi-phase migrations. |

---

## 16.7 Structural Changes

*These are significant refactorings that change how data is organized. Almost always multi-phase.*

---

### Split an Entity (Vertical Partitioning)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Extract columns into a new related table | 4 | Multi-Phase | Both tables affected |

---

**Layer 2**

**Dimensions:**
| Dimension | Value | Reasoning |
|-----------|-------|-----------|
| Data Involvement | Data-transforming | Data moves between tables |
| Reversibility | Effortful | Can merge back, but requires scripted work |
| Dependency Scope | Cross-boundary | All queries/procs referencing those columns |
| Application Impact | Breaking | Query patterns must change |

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Application coordination | This is application-level refactoring. SSDT handles each step; you own orchestration. |
| Drop timing | Don't drop columns until application is fully transitioned. |

**Related:**
- Pattern: [17.6 Table Split](#176-pattern-table-split)

---

### Merge Entities (Denormalization)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Combine two tables into one | 4 | Multi-Phase | Both tables affected |

---

**Layer 2**

Reverse of split. Same tier, same concerns.

**Phase sequence:**
1. Add columns to target table
2. Migrate data from source table
3. Application transitions
4. Drop source table

---

### Move an Attribute Between Entities

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Move a column from one table to another | 3-4 | Multi-Phase | Both tables affected |

---

**Layer 2**

**Phase sequence:**
1. Add column to destination table
2. Migrate data
3. Application transitions
4. Drop from source table

---

### Move an Entity Between Schemas

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Move a table to a different schema namespace | 3 | Declarative + Refactorlog OR Script | Instance recreation required |

---

**Layer 2**

**SSDT approach:**

Change the schema in the file:
```sql
CREATE TABLE [archive].[AuditLog]  -- was [dbo].[AuditLog]
```

Use refactorlog to express the move, otherwise SSDT drops and recreates.

**Script approach (preserves object_id):**
```sql
ALTER SCHEMA archive TRANSFER dbo.AuditLog
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Refactorlog | Without it, SSDT interprets as drop + create. |
| ALTER SCHEMA TRANSFER | Single operation, preserves object_id and data. May be preferable. |
| References | All fully-qualified references break. |

**Related:**
- Pattern: [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility)

---

## 16.8 Views, Synonyms, and Abstraction

*These create stable interfaces over changing structures.*

---

### Create a View

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create a named query as a virtual table | 1 | Pure Declarative | N/A |

---

**Layer 2**

**What you do:**
```sql
-- /Views/dbo/dbo.vw_ActiveCustomer.sql
CREATE VIEW [dbo].[vw_ActiveCustomer]
AS
SELECT 
    CustomerId,
    FirstName,
    LastName,
    Email,
    CreatedAt
FROM dbo.Customer
WHERE IsActive = 1
```

**Always enumerate columns explicitly.** Never use `SELECT *`.

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| 🔴 **The SELECT * View** | View schema is fixed at creation. New columns won't appear. See [Anti-Pattern 19.7](#197-the-select--view). |
| Dependency chain | If underlying table changes, view may break. SSDT catches this at build. |

**Related:**
- Anti-pattern: [19.7 The SELECT * View](#197-the-select--view)

---

### Create a View for Backward Compatibility

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Maintain old interface during migration | 2-3 | Part of Multi-Phase pattern | N/A |

---

**Layer 2**

**Scenario:** Renamed table `Employee` to `Staff`. Create view to maintain old name.

```sql
CREATE VIEW [dbo].[Employee]
AS
SELECT 
    StaffId AS EmployeeId,
    FirstName,
    LastName,
    Email
FROM dbo.Staff
```

Old code referencing `dbo.Employee` continues to work.

---

### Create a Synonym

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Create an alias for another object | 1-2 | Pure Declarative | N/A |

---

**Layer 2**

**What you do:**
```sql
-- Same database, different schema
CREATE SYNONYM [dbo].[Customer] FOR [sales].[Customer]

-- Cross-database
CREATE SYNONYM [dbo].[RemoteCustomer] FOR [LinkedServer].[OtherDB].[dbo].[Customer]
```

**Use cases:**
- Schema migration: leave synonym at old location
- Environment abstraction: synonym points to different targets
- Encapsulate linked server complexity

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Runtime resolution | Synonyms resolve at execution, not compile time. If target doesn't exist, runtime error. |
| Cross-database | Requires database reference in SSDT project. |

---

### Create an Indexed View (Materialized)

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Physically store view results for performance | 2-3 | Pure Declarative | N/A |

---

**Layer 2**

**Requirements:**
- `WITH SCHEMABINDING` required
- Deterministic expressions only
- `COUNT_BIG(*)` not `COUNT(*)`
- No OUTER JOIN, subqueries, DISTINCT

```sql
CREATE VIEW [dbo].[vw_CustomerOrderSummary]
WITH SCHEMABINDING
AS
SELECT 
    CustomerId,
    COUNT_BIG(*) AS OrderCount,
    SUM(TotalAmount) AS TotalSpend
FROM dbo.[Order]
GROUP BY CustomerId
GO

CREATE UNIQUE CLUSTERED INDEX [IX_vw_CustomerOrderSummary]
ON [dbo].[vw_CustomerOrderSummary]([CustomerId])
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Write overhead | Every INSERT/UPDATE/DELETE on base tables updates the view. |
| Enterprise Edition | Standard Edition requires `NOEXPAND` hint to use indexed view. |

---

## 16.9 Audit and Temporal

*These patterns track changes over time — the foundation of your Change History feature.*

---

### Add System-Versioned Temporal Table

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Enable automatic history tracking | 2 (new) / 3 (existing) | Pure Declarative (new) / Multi-Phase (existing) | Different mechanism than CDC |

---

**Layer 2**

**For new table:**
```sql
CREATE TABLE [dbo].[Employee]
(
    [EmployeeId] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(100) NOT NULL,
    [Department] NVARCHAR(50) NOT NULL,
    
    [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.EmployeeHistory))
```

**Querying:**
```sql
-- Current state
SELECT * FROM dbo.Employee

-- Point in time
SELECT * FROM dbo.Employee FOR SYSTEM_TIME AS OF '2024-06-15'

-- Full history
SELECT * FROM dbo.Employee FOR SYSTEM_TIME ALL WHERE EmployeeId = 42
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Existing table | Converting existing table to temporal requires multi-phase approach. |
| History table | System-managed; can't directly modify. |
| SSDT table rebuilds | If SSDT needs to rebuild table (column reorder), it disables/re-enables versioning. |

---

### Add Manual Audit Columns

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Add CreatedAt, UpdatedAt, CreatedBy, UpdatedBy columns | 1 (new) / 2 (existing) | Pure Declarative / Post-Deployment for backfill | No CDC impact |

---

**Layer 2**

**Standard audit columns:**
```sql
[CreatedAt] DATETIME2(7) NOT NULL 
    CONSTRAINT [DF_Order_CreatedAt] DEFAULT (SYSUTCDATETIME()),
[CreatedBy] NVARCHAR(128) NOT NULL 
    CONSTRAINT [DF_Order_CreatedBy] DEFAULT (SYSTEM_USER),
[UpdatedAt] DATETIME2(7) NULL,
[UpdatedBy] NVARCHAR(128) NULL
```

**For existing table — backfill:**
```sql
UPDATE dbo.[Order]
SET 
    CreatedAt = ISNULL(OrderDate, '2020-01-01'),
    CreatedBy = 'MIGRATION'
WHERE CreatedAt IS NULL
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| UpdatedAt/UpdatedBy | Database won't auto-populate. Requires trigger or application code. |
| Triggers | Can add trigger for auto-update, but adds overhead. |

---

### Enable Change Data Capture

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Track row-level changes for audit/ETL | 3 | Script-Only | This IS the CDC operation |

---

**Layer 2**

**Enable for database:**
```sql
EXEC sys.sp_cdc_enable_db
```

**Enable for table:**
```sql
EXEC sys.sp_cdc_enable_table
    @source_schema = 'dbo',
    @source_name = 'Customer',
    @role_name = 'cdc_reader',
    @capture_instance = 'dbo_Customer_v1',
    @supports_net_changes = 1
```

**Query changes:**
```sql
DECLARE @from_lsn binary(10) = sys.fn_cdc_get_min_lsn('dbo_Customer_v1')
DECLARE @to_lsn binary(10) = sys.fn_cdc_get_max_lsn()

SELECT * FROM cdc.fn_cdc_get_all_changes_dbo_Customer_v1(@from_lsn, @to_lsn, 'all')
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| Enterprise Edition | CDC requires Enterprise (or Developer/Eval). |
| SQL Agent | Requires SQL Agent running for capture jobs. |
| Schema changes | Any schema change on CDC table requires capture instance management. See [Section 12](#12-cdc-and-schema-evolution). |
| 🔴 **The CDC Surprise** | Schema changes without instance recreation leave history incomplete. See [Anti-Pattern 19.5](#195-the-cdc-surprise). |

**Related:**
- Section: [12. CDC and Schema Evolution](#12-cdc-and-schema-evolution)
- Anti-pattern: [19.5 The CDC Surprise](#195-the-cdc-surprise)
- Pattern: [17.9 CDC-Enabled Table Schema Change](#179-pattern-cdc-enabled-table-schema-change-production)
- Decision aid: [18.5 CDC Impact Checker](#185-cdc-impact-checker)

---

### Enable Change Tracking

**Layer 1**
| Summary | Tier | Mechanism | CDC |
|---------|------|-----------|-----|
| Lightweight tracking of which rows changed | 2 | Declarative (table) / Script (database) | Alternative to CDC |

---

**Layer 2**

**Simpler than CDC:** Tracks *that* rows changed, not *what* changed.

**Enable for database:**
```sql
ALTER DATABASE [YourDb]
SET CHANGE_TRACKING = ON
(CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON)
```

**Enable for table:**
```sql
CREATE TABLE [dbo].[Customer]
(
    -- columns
)
WITH (CHANGE_TRACKING = ON)
```

**Query changes:**
```sql
SELECT 
    ct.CustomerId,
    ct.SYS_CHANGE_OPERATION,  -- I, U, D
    c.*
FROM CHANGETABLE(CHANGES dbo.Customer, @last_sync_version) ct
LEFT JOIN dbo.Customer c ON ct.CustomerId = c.CustomerId
```

---

**Layer 3: Gotchas & Edge Cases**

| Gotcha | Details |
|--------|---------|
| No before/after | Only tells you row changed, not old values. |
| Sync scenarios | Good for cache invalidation, offline sync. |
| All editions | Available in all SQL Server editions. |

**CDC vs Change Tracking:**
| Aspect | CDC | Change Tracking |
|--------|-----|-----------------|
| What's tracked | Full before/after row images | Which rows changed |
| Storage | Separate change tables | Internal tracking |
| Edition | Enterprise | All editions |
| Use case | Audit, ETL | Sync, cache invalidation |
| Overhead | Higher | Lower |

---

## 16.10 Quick Lookup: All Operations by Tier

### Tier 1: Self-Service

| Operation | Mechanism |
|-----------|-----------|
| Create table | Declarative |
| Add nullable column | Declarative |
| Add default constraint | Declarative |
| Add check constraint (new table) | Declarative |
| Add index (small table) | Declarative |
| Create view | Declarative |
| Create synonym | Declarative |
| NOT NULL → NULL | Declarative |

### Tier 2: Pair-Supported

| Operation | Mechanism |
|-----------|-----------|
| Add NOT NULL column (with default) | Declarative |
| Add FK (clean data) | Declarative |
| Add unique constraint | Declarative |
| Add check constraint (existing data) | Declarative |
| Add index (large table) | Declarative |
| Widen column | Declarative |
| Change type (implicit) | Declarative |
| NULL → NOT NULL | Pre-deployment + Declarative |
| Add manual audit columns (existing) | Post-deployment |
| Enable Change Tracking | Script + Declarative |
| Create indexed view | Declarative |

### Tier 3: Dev Lead Owned

| Operation | Mechanism |
|-----------|-----------|
| Rename column | Declarative + Refactorlog |
| Rename table | Declarative + Refactorlog |
| Add FK (orphan data) | Multi-Phase |
| Change cascade behavior | Declarative |
| Drop column (with deprecation) | Multi-Phase |
| Change type (explicit) | Multi-Phase |
| Add/remove IDENTITY | Multi-Phase |
| Move table between schemas | Declarative + Refactorlog |
| Enable CDC | Script-Only |
| CDC table schema change | Multi-Phase |
| Add system-versioned temporal (existing) | Multi-Phase |
| Extract to lookup table | Multi-Phase |

### Tier 4: Principal Escalation

| Operation | Mechanism |
|-----------|-----------|
| Drop table with data | Declarative (guarded) |
| Narrow column | Pre-deployment + Declarative |
| Split table | Multi-Phase |
| Merge tables | Multi-Phase |
| Move column between tables | Multi-Phase |
| Any data-destructive operation | Varies |
| Novel/unprecedented patterns | Case-by-case |

---

## 16.11 Cross-Reference: Anti-Patterns and Patterns

| Operation | Related Anti-Pattern | Related Multi-Phase Pattern |
|-----------|---------------------|----------------------------|
| Rename column/table | [19.1 The Naked Rename](#191-the-naked-rename) | [17.8 Schema Migration with Backward Compatibility](#178-pattern-schema-migration-with-backward-compatibility) |
| Add NOT NULL column | [19.2 The Optimistic NOT NULL](#192-the-optimistic-not-null) | [17.2 NULL → NOT NULL](#172-pattern-null--not-null-on-populated-table) |
| Add FK | [19.3 The Forgotten FK Check](#193-the-forgotten-fk-check) | [17.4 Add FK with Orphan Data](#174-pattern-add-fk-with-orphan-data) |
| Narrow column | [19.4 The Ambitious Narrowing](#194-the-ambitious-narrowing) | — |
| CDC table change | [19.5 The CDC Surprise](#195-the-cdc-surprise) | [17.9 CDC-Enabled Table Schema Change](#179-pattern-cdc-enabled-table-schema-change-production) |
| Refactorlog handling | [19.6 The Refactorlog Cleanup](#196-the-refactorlog-cleanup) | — |
| Create view | [19.7 The SELECT * View](#197-the-select--view) | — |
| Change type (explicit) | — | [17.1 Explicit Conversion Data Type Change](#171-pattern-explicit-conversion-data-type-change) |
| Add/remove IDENTITY | — | [17.3 Add/Remove IDENTITY](#173-pattern-addremove-identity-property) |
| Drop column | — | [17.5 Safe Column Removal](#175-pattern-safe-column-removal-4-phase) |
| Split table | — | [17.6 Table Split](#176-pattern-table-split) |
| Merge tables | — | [17.7 Table Merge](#177-pattern-table-merge) |

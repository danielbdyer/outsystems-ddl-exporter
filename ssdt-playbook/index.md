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


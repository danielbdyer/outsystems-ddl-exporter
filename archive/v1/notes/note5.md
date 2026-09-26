# Release Train, Swim Lanes & SQL Development Standards
## Post-Cutover Operations Guide

---

## 📋 Section 1: Release Train Approach (Dev Environment)

**Purpose**: Establish predictable, coordinated workflow for database changes in development  
**Scope**: Monday forward, Dev environment only (Test/Prod promotion covered separately)  
**Goal**: Prevent chaos, enable fast iteration, maintain quality

---

### The Problem We're Solving

**Without a release train**:
```
Monday morning, 10 developers:
- All making DB changes simultaneously
- Deploying whenever they want
- Integration Studio refreshes random times
- Merge conflicts everywhere
- OutSystems apps breaking randomly
- "Works on my machine" syndrome
- Dev leads firefighting all day
```

**With a release train**:
```
Predictable schedule:
- Clear PR submission windows
- Coordinated deployments
- Scheduled Integration Studio refreshes
- Everyone knows what to expect
- Fewer surprises, faster velocity
```

---

### The Dev Release Train Schedule

#### Daily Rhythm (Monday-Friday)

```
8:00 AM  - Daily standup
         - Dev lead announces: "DB changes from yesterday deployed, Integration Studio refreshed"
         
9:00 AM  - Development window opens
         - Developers work on features
         - Make database changes in projects
         - Create PRs as ready
         
2:00 PM  - PR cutoff for same-day deployment
         - All PRs submitted by 2 PM reviewed by EOD
         - Approved PRs batched for 3 PM deployment
         
3:00 PM  - Daily batch deployment
         - Dev lead (or designated developer) deploys all approved changes
         - One DACPAC deployment with all changes
         - ~15 minute window
         
3:30 PM  - Integration Studio refresh window
         - All developers refresh Integration Studio entities
         - Publish extensions
         - Update Service Studio apps
         - Test their changes
         
5:00 PM  - End of day checkpoint
         - Any emergency fixes go through dev lead
         - Tomorrow's work can be planned
```

**Why these times?**:
- **2 PM cutoff**: Gives 1 hour for review, 30 min buffer before deployment
- **3 PM deployment**: Middle of afternoon, most people available
- **3:30 PM refresh**: Right after deployment, before people wrap up for day
- **Not morning**: Avoids blocking people first thing

---

### PR Submission Guidelines

#### PR Templates

**Location**: Create `.github/pull_request_template.md` or Azure DevOps equivalent

**Template content**:

```markdown
## Database Change PR

### Change Type (check one)
- [ ] Add column(s)
- [ ] Add table(s)
- [ ] Add index(es)
- [ ] Add/modify post-deployment script
- [ ] Refactor (rename/move)
- [ ] Other: ___________

### Description
**What changed?**
[Clear description of schema changes]

**Why?**
[Business requirement, feature ticket, bug fix]

**Feature ticket**: [PROJ-123](link)

### Breaking Changes
- [ ] No breaking changes (safe to deploy)
- [ ] Breaking changes (describe below)

**If breaking, describe**:
- Which OutSystems apps affected: ___________
- Coordination needed: ___________
- Deployment plan: ___________

### Pre-Deployment Checklist
- [ ] Project builds successfully (no errors)
- [ ] Naming conventions followed
- [ ] Data types appropriate (NVARCHAR for text, DATETIME2 for dates)
- [ ] Foreign keys have supporting indexes
- [ ] Post-deployment scripts are idempotent (safe to re-run)
- [ ] Tested locally (published to local database)
- [ ] No sensitive data in scripts (passwords, API keys, PII)

### Post-Deployment Tasks
- [ ] Need to refresh Integration Studio: YES / NO
- [ ] OutSystems apps to update: ___________
- [ ] Testing steps: ___________

### Reviewer
Assigning to: @[dev-lead-name]

### Additional Notes
[Any special considerations, dependencies, or context]
```

**Why this template?**:
- Forces developer to think through implications
- Provides reviewer with needed context
- Creates documentation automatically
- Standardizes communication

---

#### PR Cutoff Windows

**2 PM = Hard Cutoff for Same-Day Deployment**

```
Submitted by 2:00 PM → Deployed at 3:00 PM (same day)
Submitted after 2:00 PM → Deployed next day at 3:00 PM

Exception: Emergency fixes with dev lead approval
```

**Why hard cutoff?**:
- Gives reviewers time (1 hour minimum)
- Prevents rush reviews (quality over speed)
- Allows batching (more efficient)
- Sets expectations (plan ahead)

**What counts as "submitted"?**:
```
✅ PR created in GitHub/Azure DevOps
✅ Template fully filled out
✅ Build passing (green checkmark)
✅ Reviewer assigned

❌ Draft PR
❌ Build failing
❌ Missing description
```

---

#### Lead Time Guidance by Change Type

**Simple changes** (1-2 day lead time):
```
- Add nullable column to existing table
- Add new table (no complex relationships)
- Add index
- Update reference data in post-deployment script

Process:
Day 1, morning: Make change, create PR
Day 1, 2 PM: Submit PR
Day 1, 3 PM: Deployed if approved
Day 1, 3:30 PM: Refresh Integration Studio, test
```

**Medium changes** (2-3 day lead time):
```
- Add multiple related tables
- Add NOT NULL column (needs DEFAULT or migration)
- Refactor (rename table/column)
- Complex post-deployment migration

Process:
Day 1: Design, discuss with dev lead
Day 2, morning: Implement, create PR
Day 2, 2 PM: Submit PR
Day 3, 3 PM: Deployed after thorough review
Day 3, 3:30 PM: Refresh Integration Studio, test carefully
```

**Complex changes** (3-5 day lead time):
```
- Multi-table refactoring
- Data type changes affecting existing data
- Breaking changes requiring OutSystems app updates
- Large data migrations

Process:
Day 1: Design doc, review with team
Day 2: Implement in feature branch
Day 3: PR submitted with detailed plan
Day 4: Review, possibly pair programming
Day 5: Deployment with close monitoring
```

**Rule of thumb**: 
```
If you can't explain the change in 2 sentences → Medium complexity
If multiple people need to coordinate → Complex
If you're unsure → Ask dev lead for classification
```

---

### Integration Studio Refresh Protocol

#### Coordinated Refresh (Daily at 3:30 PM)

**Why coordinated?**:
- Everyone's extensions stay in sync
- Reduces "it works for me" issues
- Batch operation more efficient
- Team learns together (first week)

**Process**:

**3:30 PM: Dev Lead announces in #database-dev**:
```
🔔 Integration Studio Refresh Time

Changes deployed:
- Added PhoneNumber to Customers (@dev1)
- Added OrderStatus table (@dev2)
- Index on Orders.CustomerId (@dev3)

Action required:
1. Save your work in Service Studio
2. Open Integration Studio
3. Refresh affected entities
4. Publish extensions
5. Confirm in thread when done

Affected apps: CustomerApp, OrderApp

Estimated time: 10 minutes
```

**3:30-3:45 PM: Developers refresh**:
```
Each developer:
1. Open Integration Studio
2. Open database extension
3. Right-click affected tables → Refresh
4. Review changes (green = new, yellow = modified)
5. Publish extension
6. Reply in thread: "✅ Done - [YourName]"
```

**3:45 PM: Dev Lead checks**:
```
- All affected developers confirmed?
  - Yes → Proceed
  - No → Ping missing people

- Anyone having issues?
  - Yes → Help troubleshoot
  - No → Green light for testing
```

**After 4:00 PM: Individual refreshes allowed**:
```
If you missed the window:
1. Refresh your extension
2. Post in #database-dev: "Refreshed [Extension] at 4:15 PM"
3. Test your app

Important: Don't assume everyone else refreshed when you did
```

---

#### Ad-Hoc Refresh (Outside scheduled window)

**When needed**:
- Emergency fix deployed
- You're working late
- Testing requires immediate refresh

**Protocol**:
```
1. Post in #database-dev BEFORE refreshing:
   "Planning to refresh [Extension] at [Time] for [Reason]"
   
2. Wait 5 minutes for objections
   (Someone might be mid-testing with old schema)
   
3. If no objections:
   - Refresh your extension
   - Post: "✅ Refreshed [Extension] at [Time]"
   
4. If objections:
   - Coordinate timing
   - Or work in separate extension temporarily
```

**Emergency exception**:
```
Production issue requiring immediate fix:
- Skip protocol
- Refresh as needed
- Communicate after the fact
- Document in #database-emergency
```

---

### Batched Deployments (3 PM Daily)

#### Why Batch vs. Continuous?

**Continuous (every PR merged)**:
```
❌ 10 deployments per day = 10 Integration Studio refreshes
❌ Constant interruptions
❌ Hard to coordinate
❌ Higher error rate
❌ Dev lead overwhelmed
```

**Batched (once daily)**:
```
✅ 1 deployment per day = 1 Integration Studio refresh
✅ Predictable schedule
✅ Easier coordination
✅ Lower error rate
✅ Efficient use of time
```

#### Deployment Process

**3:00 PM: Dev Lead (or designated deployer)**:

**Step 1: Collect approved PRs** (5 min)
```
Query: All PRs merged since yesterday 3 PM
Filter: Only database project PRs
Verify: All have green builds
Result: List of 5-8 changes typically
```

**Step 2: Pre-deployment communication** (2 min)
```
Post in #database-dev:
"🚀 Starting daily deployment - 3:00 PM

Deploying:
- PR #123: Add PhoneNumber to Customers (@dev1)
- PR #124: Add OrderStatus table (@dev2)  
- PR #125: Index on Orders.CustomerId (@dev3)

Duration: ~10 minutes
Integration Studio refresh at 3:30 PM
Stand by for confirmation."
```

**Step 3: Pull latest** (1 min)
```
git checkout main
git pull origin main
```

**Step 4: Build DACPAC** (2 min)
```
Open database project in Visual Studio
Build → Build Solution (Ctrl+Shift+B)
Verify: Build succeeded
Locate: bin\Release\MyDatabase.dacpac
```

**Step 5: Generate deployment script** (2 min)
```
SqlPackage.exe /Action:Script `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"DevServer" `
    /TargetDatabaseName:"MyDatabase" `
    /OutputPath:"deploy-$(Get-Date -Format 'yyyyMMdd-HHmm').sql"
```

**Step 6: Review script** (3 min)
```
Open generated script
Look for:
✅ Expected ALTER TABLE statements
✅ Expected CREATE INDEX statements
✅ No unexpected DROP operations
⚠️ Any DROP operations → Double-check with PR author

If anything unexpected:
- STOP
- Investigate
- Confirm with team
```

**Step 7: Deploy** (3 min)
```
SqlPackage.exe /Action:Publish `
    /SourceFile:"MyDatabase.dacpac" `
    /TargetServerName:"DevServer" `
    /TargetDatabaseName:"MyDatabase" `
    /p:BlockOnPossibleDataLoss=True
```

**Step 8: Verify** (2 min)
```
Open SSMS
Connect to DevServer
Verify:
- New columns exist
- New tables exist
- Indexes created
- Post-deployment scripts ran (check output)
```

**Step 9: Confirm** (1 min)
```
Post in #database-dev:
"✅ Deployment complete - 3:12 PM

Changes applied:
- PhoneNumber added to Customers ✅
- OrderStatus table created ✅
- Index on Orders.CustomerId created ✅

Integration Studio refresh at 3:30 PM
```

**Total time: 15-20 minutes**

---

#### Deployment Failure Protocol

**If deployment fails**:

**Step 1: Stop and assess** (1 min)
```
DO NOT try to "fix" immediately
Read error message completely
Screenshot error
```

**Step 2: Communicate** (1 min)
```
Post in #database-dev:
"⚠️ Deployment failed - 3:08 PM

Error: [paste error]
Screenshot: [attach]

Investigating. Stand by."
```

**Step 3: Triage** (5 min)
```
Error type:
- Syntax error → Code issue
- Timeout → Performance issue
- Data loss blocked → Migration needed
- Constraint violation → Data issue
```

**Step 4: Quick fix or rollback?**
```
Can fix in < 10 minutes?
├─ Yes → Fix, test, redeploy
└─ No → Rollback this PR, deploy rest

Rollback process:
1. Revert problematic PR in Git
2. Rebuild DACPAC (without that change)
3. Deploy
4. Communicate: "Rolled back PR #123, other changes deployed"
```

**Step 5: Post-mortem** (later)
```
After deployment resolved:
- Document what went wrong
- Update checklist to prevent recurrence
- Help PR author fix issue
- Reschedule fixed PR for next day
```

---

### PR Review SLA (Service Level Agreement)

#### Reviewer Commitments

**First response**: Within 2 hours during business hours (9 AM - 5 PM)
```
Even if full review not complete:
"Reviewing this, will have feedback by [time]"
```

**Full review**: Within 4 hours for simple changes
```
Simple change = Add column, add table, add index
Review should be quick: 10-15 minutes
```

**Full review**: Within 1 business day for complex changes
```
Complex change = Refactoring, breaking changes, migrations
Review might need investigation: 30-60 minutes
```

**Exceptions**:
- After-hours PRs: Reviewed next business day
- Friday after 2 PM: Reviewed Monday (unless urgent)
- PTO/OOO: Designate backup reviewer

---

#### Developer Responsibilities

**When submitting PR**:
```
☐ Fill out template completely
☐ Build passes
☐ Self-review your changes
☐ Test locally if possible
☐ Tag reviewer
☐ Respond to review comments within 4 hours
```

**When PR approved**:
```
☐ Merge (or notify dev lead to merge)
☐ Monitor for deployment
☐ Ready to refresh Integration Studio at 3:30 PM
☐ Test your changes after deployment
☐ Report any issues immediately
```

**If PR rejected**:
```
☐ Read feedback carefully
☐ Ask clarifying questions
☐ Make requested changes
☐ Test again
☐ Comment: "Changes made, ready for re-review"
☐ Re-request review
```

---

### Communication Channels

#### Slack Channels (Recommended Setup)

**#database-dev** (primary channel)
```
Purpose: Daily operations, deployments, coordination
Who: All developers working with database
Posts:
- Deployment announcements
- Integration Studio refresh coordination  
- Quick questions
- "I'm working on table X" heads-up

Keep: Focused on operations
Avoid: Long technical debates (take to DM or #database-help)
```

**#database-help** (support channel)
```
Purpose: Questions, troubleshooting, learning
Who: All developers, dev leads, DB experts
Posts:
- "How do I...?"
- Error messages
- Best practice questions
- "Is this the right approach?"

Keep: Supportive, educational
Tag: @dev-leads for visibility
```

**#database-emergency** (incidents only)
```
Purpose: Production issues, critical bugs, outages
Who: On-call, dev leads, management
Posts:
- Production deployment failures
- Data loss incidents
- Critical bugs blocking work
- Security issues

Keep: Signal, not noise
Usage: Rare (1-2 times per month max)
```

---

#### Daily Standup Protocol

**Agenda item: Database changes**

**Dev lead reports** (2 minutes):
```
"Database updates:

Yesterday:
- 5 PRs merged
- Deployed at 3 PM, no issues
- All extensions refreshed
- [highlight any noteworthy changes]

Today:
- 3 PRs in review
- Deployment scheduled 3 PM as usual
- [any heads-up for team]

Blockers: None [or describe]
"
```

**Individual callouts** (as needed):
```
Developer: "I'll be submitting a PR today that refactors 
the Orders table. It might need extra review time."

Dev lead: "Thanks for heads up. Submit by noon if possible 
so I have time for thorough review."
```

---

### Exception Handling

#### After-Hours Emergency Fix

**Criteria for emergency**:
```
✅ Production is down
✅ Critical bug blocking users
✅ Data integrity issue
✅ Security vulnerability

❌ "Would be nice to deploy today"
❌ Developer working late on feature
❌ Tomorrow's work prep
```

**Process**:
```
1. Notify dev lead (phone/text if after 6 PM)
2. Get approval
3. Create PR with "EMERGENCY:" prefix
4. Quick review (15 min max)
5. Deploy
6. Document in #database-emergency
7. Full post-mortem next day
```

---

#### Friday Afternoon Protocol

**After 2 PM Friday**:
```
No deployments except emergencies
Why: Risk of breaking changes going into weekend

PRs submitted Friday after 2 PM:
→ Reviewed Monday morning
→ Deployed Monday 3 PM
```

**Exception**:
```
Non-breaking changes (indexes, reference data) MAY be deployed
if dev lead approves and developer commits to:
- Monitor through end of day
- Available on phone over weekend if issues
```

---

### Metrics & Continuous Improvement

#### Track Weekly (Dev Lead)

```
PR Metrics:
- PRs submitted: ___
- PRs merged: ___
- Average review time: ___
- PRs rejected first time: ___

Deployment Metrics:
- Deployments performed: ___ (should be ~5/week)
- Deployment failures: ___ (target: 0)
- Rollbacks needed: ___ (target: 0)
- Average deployment time: ___

Quality Metrics:
- Post-deployment issues: ___ (target: 0)
- Integration Studio refresh issues: ___ (target: 0)
- Merge conflicts: ___
```

#### Weekly Retrospective (Friday 4 PM)

**15-minute team check-in**:

```
What worked well this week?
- [Team shares wins]

What didn't work?
- [Team shares friction points]

Process adjustments for next week:
- [Agree on 1-2 improvements]

Examples of adjustments:
- "2 PM cutoff too early, move to 2:30 PM"
- "Need better PR template for migrations"
- "Review SLA too aggressive, extend to 6 hours"
```

---

### First Week Special Considerations

#### Monday (Day 1)

```
9:30 AM: Extended standup (30 minutes)
- Dev lead demos workflow
- Q&A
- Walk through PR template

All day: Expect many questions
- Monitor #database-help closely
- Respond within 30 min (not 2 hours)
- Be patient, everyone learning

3:00 PM: First batch deployment
- Dev lead does this personally
- Narrate steps in Slack
- Screenshot each step
- Over-communicate

3:30 PM: First coordinated refresh
- Dev lead facilitates
- Help anyone stuck
- Pair if needed
```

#### Tuesday-Wednesday (Days 2-3)

```
Morning: Shorter standup (15 min)
- Focus on blockers
- Celebrate successes

All day: Still high support mode
- Questions will decrease
- Start seeing patterns in issues
- Document FAQs

Deployment: Dev lead or senior dev
- Still narrate in Slack
- Invite junior devs to observe
```

#### Thursday-Friday (Days 4-5)

```
Morning: Back to normal standup (10 min)
- Most questions answered by now

All day: Normalizing
- Process should feel smoother
- Confidence building

Deployment: Can delegate to senior dev
- Dev lead reviews but doesn't execute
- Building bench strength

Friday 4 PM: First retrospective
- Critical feedback session
- Adjust for Week 2
```

---

### Tools & Templates

#### Deployment Checklist (for deployer)

```
Pre-Deployment:
☐ Git pulled latest
☐ Build succeeded  
☐ All PRs have green builds
☐ No unexpected merge conflicts
☐ Announcement posted in #database-dev

During Deployment:
☐ Script generated and reviewed
☐ No unexpected DROP operations
☐ Deployment executed
☐ No errors in output
☐ Verification queries run

Post-Deployment:
☐ Confirmation posted
☐ Integration Studio refresh scheduled
☐ Any issues documented
☐ PRs closed/updated
```

---

## 📋 Section 2: Swim Lanes (Role Responsibilities)

**Purpose**: Clear role delineation prevents confusion and bottlenecks  
**Goal**: Right person, right task, right level of risk

---

### The Four Swim Lanes

```
┌─────────────────────────────────────────────────┐
│ Junior Developer (0-1 year DB experience)       │
│ Focus: Learn, execute simple tasks, ask         │
├─────────────────────────────────────────────────┤
│ Mid-Level Developer (1-2 years DB experience)   │
│ Focus: Independent simple tasks, guided complex │
├─────────────────────────────────────────────────┤
│ Senior Developer (2-4 years DB experience)      │
│ Focus: Complex tasks, mentor, review            │
├─────────────────────────────────────────────────┤
│ Staff/Lead Developer (4+ years DB experience)   │
│ Focus: Architecture, standards, escalations     │
└─────────────────────────────────────────────────┘
```

---

### Junior Developer Swim Lane

**Profile**:
- New to SSDT/DACPAC workflow
- Basic SQL knowledge (can write SELECT, understand tables)
- First 1-3 months working with database projects
- **This is most of the team starting Monday**

#### Can Do Independently ✅

**Schema Changes**:
```
✅ Add nullable column to existing table
✅ Add new simple table (no complex relationships)
✅ Add index on single column
✅ Modify reference data in post-deployment script

Example tasks:
- "Add PhoneNumber VARCHAR(20) NULL to Customers"
- "Create OrderStatus lookup table with 5 statuses"
- "Add index on Orders.CustomerId"
- "Update OrderStatus to add new 'OnHold' status"
```

**Process Tasks**:
```
✅ Create PR with template filled out
✅ Respond to PR review comments
✅ Build and test locally
✅ Refresh Integration Studio (following instructions)
✅ Update own OutSystems app after schema change
✅ Participate in daily deployment rhythm
```

**Knowledge Building**:
```
✅ Read playbooks independently
✅ Ask questions in #database-help
✅ Watch senior devs during complex tasks
✅ Attend office hours
✅ Practice on sample database
```

---

#### Needs Guidance 🟡

**Schema Changes**:
```
🟡 Add NOT NULL column (needs DEFAULT understanding)
🟡 Add foreign key (needs to add supporting index)
🟡 Add composite index (needs column order guidance)
🟡 Write complex post-deployment migration

Guidance from: Senior dev or dev lead
Time: 15-30 min pairing
```

**Process Tasks**:
```
🟡 First PR review of someone else's work
🟡 Handle merge conflict
🟡 Resolve build error they haven't seen before
🟡 Optimize slow query
🟡 Troubleshoot Integration Studio connection issue

Guidance from: Senior dev or dev lead
Method: Pairing, screen share, or detailed Slack thread
```

---

#### Should Not Do Alone ❌

**High-Risk Changes**:
```
❌ Rename table/column (needs refactorlog understanding)
❌ Drop column or table
❌ Change data type on existing column with data
❌ Multi-table refactoring
❌ Large data migration (millions of rows)
❌ Breaking changes to OutSystems apps

Must: Pair with senior dev or dev lead
Why: Data loss risk, coordination complexity
```

**Process Tasks**:
```
❌ Execute deployment to Dev (first month)
❌ Approve PRs (not yet qualified)
❌ Make architectural decisions
❌ Set team standards
❌ Troubleshoot production issues

Must: Have senior dev or lead present
Why: Need pattern recognition and experience
```

---

#### Learning Path

**Week 1 Goals**:
```
By Friday, junior dev should be able to:
☐ Add simple columns independently
☐ Create PR with complete template
☐ Refresh Integration Studio successfully
☐ Build project without errors
☐ Know when to ask for help
☐ Follow daily release train rhythm
```

**Month 1 Goals**:
```
By end of first month, junior dev should:
☐ Handle 80% of schema changes independently
☐ Write idempotent post-deployment scripts
☐ Create appropriate indexes
☐ Resolve simple build errors alone
☐ Review PRs from other junior devs
☐ Pair less frequently (self-sufficient)
```

**Month 3 Goals**:
```
By end of third month, junior dev should:
☐ Ready to move to mid-level swim lane
☐ Mentor newer junior devs
☐ Handle merge conflicts independently
☐ Optimize basic query performance
☐ Contribute to process improvements
```

---

### Mid-Level Developer Swim Lane

**Profile**:
- 1-2 years database development experience
- Comfortable with SSDT workflow
- Can troubleshoot most issues independently
- Understands data modeling basics

#### Can Do Independently ✅

**All Junior Tasks Plus**:
```
✅ Add NOT NULL columns with DEFAULT
✅ Create tables with foreign keys and indexes
✅ Write multi-step post-deployment migrations
✅ Create composite indexes
✅ Create covering indexes (with INCLUDE)
✅ Handle most merge conflicts
✅ Resolve most build errors
✅ Optimize queries (add indexes, rewrite non-SARGable)
✅ Review junior dev PRs
✅ Execute daily deployment (with checklist)
```

**Example tasks**:
```
"Add IsActive BIT NOT NULL DEFAULT 1 to Orders table"
"Create OrderItems table with FK to Orders and Products, 
 including all supporting indexes"
"Write migration to backfill PhoneNumber column based on 
 Customer.ContactInfo data"
"Create covering index for frequent customer order history query"
```

---

#### Needs Guidance 🟡

**Complex Changes**:
```
🟡 Table refactoring (split/merge)
🟡 Data type changes affecting millions of rows
🟡 Performance tuning for complex queries
🟡 Breaking changes coordination
🟡 Troubleshooting unusual deployment failures

Guidance from: Senior dev or staff
Method: Architecture discussion, review design doc
```

---

#### Should Not Do Alone ❌

**High-Impact Decisions**:
```
❌ Architectural decisions (schema design patterns)
❌ Set team-wide standards
❌ Production deployment (until trained)
❌ Major refactoring affecting multiple apps
❌ Security-sensitive changes

Must: Review with staff/lead
Why: Impact beyond single feature
```

---

#### Responsibilities

**Technical**:
```
- Handle 90%+ of schema changes independently
- Review junior dev PRs daily
- Execute daily Dev deployments (rotate with other mid-levels)
- Mentor junior devs (answer questions, pair occasionally)
- Troubleshoot most Integration Studio issues
```

**Process**:
```
- Participate in retrospectives with feedback
- Suggest process improvements
- Document patterns you discover
- Help maintain playbooks (fix outdated info)
```

---

### Senior Developer Swim Lane

**Profile**:
- 2-4 years database development experience
- Deep SSDT/SQL knowledge
- Can handle any schema change
- Teaches others regularly

#### Can Do Independently ✅

**All Mid-Level Tasks Plus**:
```
✅ Complex refactoring (rename, split, merge tables)
✅ Data type changes with migration strategy
✅ Performance tuning (deep execution plan analysis)
✅ Complex breaking changes with coordination plan
✅ Troubleshoot any deployment issue
✅ Design schema for new features
✅ Review any PR (junior, mid, senior)
✅ Production deployments (after training)
```

**Example tasks**:
```
"Split Customers table into Customers and CustomerDetails 
 (20M rows, vertical partition)"
"Change OrderDate from DATETIME to DATETIME2 across 
 50M row Orders table with zero downtime"
"Redesign OrderItems to support product variants 
 (breaking change, affects 3 apps)"
"Diagnose and fix filtered index not being used despite 
 correct predicate"
```

---

#### Needs Consultation 🟡

**Enterprise Impact**:
```
🟡 Team-wide standard changes
🟡 Major architecture decisions
🟡 Cross-team coordination (affects multiple squads)
🟡 Vendor/external system integration schemas

Consult with: Staff/lead + stakeholders
Why: Ripple effects, long-term implications
```

---

#### Should Not Do Alone ❌

**Organizational Decisions**:
```
❌ Change PR process without team input
❌ Override established standards
❌ Make commitments to other teams without lead

Must: Align with staff/lead first
Why: Team cohesion, strategy alignment
```

---

#### Responsibilities

**Technical Leadership**:
```
- Handle any schema change, any complexity
- Review all PRs (especially complex ones)
- Be final technical decision maker in PR reviews
- Design schema for complex features
- Mentor mid-level devs toward senior level
- Troubleshoot escalated issues
- Conduct "lunch and learns" on advanced topics
```

**Process Leadership**:
```
- Run deployments (daily rotation)
- Lead retrospectives (rotate)
- Update playbooks and documentation
- Propose process improvements
- Interview candidates (DB technical screen)
- Represent team in cross-team meetings
```

**On-Call (Rotation)**:
```
- Be available for production issues
- Make emergency fix decisions
- Coordinate incident response
- Document post-mortems
```

---

### Staff/Lead Developer Swim Lane

**Profile**:
- 4+ years database development experience
- Strategic thinker, not just executor
- Shapes team culture and practices
- Often the most experienced DB person on team

#### Can Do ✅

**Everything in Senior Lane Plus**:
```
✅ Set architectural direction
✅ Define team standards and practices
✅ Make build vs. buy decisions
✅ Negotiate with other teams/departments
✅ Represent team to management
✅ Allocate team resources
✅ Define swim lanes and roles
```

---

#### Responsibilities

**Strategic**:
```
- Define database architecture standards
- Plan for scalability (what breaks at 100M rows?)
- Evaluate new tools/technologies
- Set performance benchmarks
- Make schema design patterns decisions
- Plan technical roadmap (3-6 month horizon)
```

**Team Development**:
```
- Develop career paths for team members
- Identify skill gaps and training needs
- Hire and onboard new team members
- Delegate appropriately across swim lanes
- Build bench strength (succession planning)
- Foster learning culture
```

**Process Excellence**:
```
- Own release train process
- Define PR review standards
- Set SLAs (response time, deployment windows)
- Measure and improve metrics
- Run weekly retrospectives
- Advocate for team needs with management
```

**Risk Management**:
```
- Final approval on risky changes
- Production deployment oversight (not execution)
- Incident response coordination
- Post-mortem facilitation
- Escalation point for team
```

**Communication**:
```
- Daily standup facilitation
- Weekly team updates
- Monthly stakeholder communication
- Cross-team coordination
- Management reporting
```

---

### Role Transition Criteria

#### Junior → Mid-Level

**Criteria**:
```
✅ 3+ months consistent contribution
✅ Completes simple changes independently (no guidance needed)
✅ Builds pass consistently (< 5% failure rate)
✅ PRs approved on first review 80%+ of time
✅ Can review other junior dev PRs
✅ Demonstrates knowledge in 1:1s
✅ Shows initiative (suggests improvements)

Evaluation: By dev lead, quarterly
Process: Informal conversation, then announce to team
```

---

#### Mid-Level → Senior

**Criteria**:
```
✅ 1+ year at mid-level
✅ Handles complex changes successfully
✅ Mentors junior devs regularly
✅ PRs rarely need revisions
✅ Trusted to review all PRs (juniors + mids)
✅ Leads by example in process adherence
✅ Contributes to documentation and standards
✅ Technically strong across all playbooks

Evaluation: By dev lead, with peer input
Process: Formal review, documented promotion
```

---

#### Senior → Staff/Lead

**Criteria**:
```
✅ 1+ year at senior level
✅ Demonstrates strategic thinking
✅ Shapes team culture positively
✅ Trusted by management
✅ Proactively identifies and solves team problems
✅ Strong communication with stakeholders
✅ Develops other senior devs
✅ Impact beyond immediate team

Evaluation: By management, with team input
Process: Formal promotion process, may involve title change
```

---

### Cross-Swim-Lane Collaboration

#### Pairing Sessions

**When to pair**:
```
Junior + Senior: Learning new pattern (1-2 hours)
Mid + Senior: Complex refactoring (2-4 hours)
Senior + Staff: Architecture design (1 hour)
```

**Pairing etiquette**:
```
Experienced person:
- Explain why, not just what
- Let junior person drive (keyboard)
- Ask guiding questions, don't dictate
- Praise good decisions

Junior person:
- Ask "why" questions
- Take notes
- Try first, ask if stuck > 5 min
- Follow up with what you learned
```

---

#### PR Review Across Levels

**Who reviews whom**:
```
Junior PRs: Reviewed by Mid or Senior (or Junior peer + Mid/Senior)
Mid PRs: Reviewed by Senior or Staff
Senior PRs: Reviewed by another Senior or Staff
Staff PRs: Reviewed by Senior (yes, upward review!)

Rule: Never approve your own swim lane's PR without higher review
```

**Review depth by level**:
```
Junior reviewing Junior:
- Checklist items (naming, data types, etc.)
- Learn together
- Flag to senior if unsure

Mid/Senior reviewing Junior:
- Full technical review
- Teaching moments in comments
- Approve or request changes

Senior/Staff reviewing complex:
- Architecture implications
- Performance at scale
- Security and risk
- Strategic alignment
```

---

### Delegation Guidelines for Dev Leads

**Daily Deployment**:
```
Week 1: Dev lead does it (teaching)
Week 2-3: Senior dev with dev lead observing
Week 4+: Rotate among senior and mid-level devs

Never delegate to: Junior devs (first 3 months)
```

**PR Reviews**:
```
Simple changes: Delegate to mid-level or senior
Complex changes: Senior or staff only
First-time complex patterns: Staff/lead personally
```

**Troubleshooting**:
```
Build errors: Junior can try, escalate after 15 min
Deployment failures: Mid-level troubleshoots, senior if > 30 min
Production issues: Senior or staff only
```

**Decision Making**:
```
Operational (which index to add): Senior decides
Tactical (schema design for feature): Senior proposes, staff approves
Strategic (DB architecture direction): Staff decides, team input
```

---

## 📋 Section 3: SQL Development Best Practices & Heuristics

**Purpose**: Codify wisdom, prevent common mistakes, accelerate decisions  
**Scope**: Rules of thumb for daily SQL development work

---

### Philosophy: Good SQL Development

```
Guiding principles:

1. Schema is Code
   - Version controlled
   - Reviewed
   - Tested
   - Documented

2. Data is Sacred
   - Preserve it zealously
   - Backup before changes
   - Validate after changes
   - Never assume

3. Performance Matters Early
   - Index foreign keys always
   - Think about 1M rows, not 100
   - SARGable queries by default
   - Measure, don't guess

4. Explicit Over Implicit
   - Name your constraints
   - List columns in SELECT
   - Specify data types precisely
   - Comment intent, not mechanism

5. Team Over Individual
   - Naming conventions always
   - PR templates always
   - Communication over surprise
   - Document for future you
```

---

### Heuristics by Category

---

#### Data Types

**Heuristic #1: Text Data**
```
Rule: User-entered text = NVARCHAR. System codes = VARCHAR.

Examples:
✅ NVARCHAR(50): FirstName, LastName, City, Country
✅ VARCHAR(20): OrderStatus ('Pending', 'Shipped')
✅ VARCHAR(100): Email (ASCII-only)

Why: NVARCHAR supports Unicode (international names)
Cost: 2x storage of VARCHAR (worth it for UX)
```

**Heuristic #2: Length Sizing**
```
Rule: Be generous but not ridiculous. Think real-world max + 20%.

Common sizes:
- Names: NVARCHAR(50) to NVARCHAR(100)
- Addresses: NVARCHAR(100) street, NVARCHAR(50) city
- Emails: NVARCHAR(100) to NVARCHAR(254)
- Phone: VARCHAR(20) (covers international formats)
- Descriptions: NVARCHAR(500) to NVARCHAR(2000)
- Long text: NVARCHAR(MAX) (avoid if possible)

Why: Too short = truncation. Too long = wasted space.
Red flag: NVARCHAR(MAX) used casually (performance impact)
```

**Heuristic #3: Numbers**
```
Rule: INT unless you know you need BIGINT. DECIMAL for money.

Decision tree:
- Counting items, small quantities: INT (max 2.1B)
- Order IDs, high-volume: BIGINT (max 9 quintillion)
- Money: DECIMAL(10,2) (10 digits total, 2 after decimal)
- Percentages: DECIMAL(5,2) (e.g., 100.50%)
- Scientific: FLOAT (only if precision not critical)

Why: INT is 4 bytes, BIGINT is 8 bytes. Choose appropriately.
Never: FLOAT for money (rounding errors cause penny discrepancies)
```

**Heuristic #4: Dates & Times**
```
Rule: DATETIME2 for everything. DATE if truly no time component.

Examples:
✅ DATETIME2: CreatedDate, ModifiedDate, OrderDate
✅ DATE: BirthDate, StartDate, EndDate
❌ DATETIME: (old type, avoid)

Why: DATETIME2 more precise, better range, SQL Server 2008+ standard
Exception: Legacy systems requiring DATETIME for compatibility
```

**Heuristic #5: Boolean Flags**
```
Rule: BIT for true/false. NOT NULL with DEFAULT.

Template:
[IsActive] BIT NOT NULL DEFAULT 1
[IsDeleted] BIT NOT NULL DEFAULT 0
[IsPublished] BIT NOT NULL DEFAULT 0

Why: BIT = 1 byte efficient. NOT NULL = predictable. DEFAULT = safe.
Never: CHAR(1), TINYINT for booleans (wastes space/clarity)
```

---

#### Naming Conventions

**Heuristic #6: Singular Nouns**
```
Rule: Table name = singular noun. Think "a row represents one X".

Examples:
✅ Customer (a row = a customer)
✅ Order (a row = an order)  
✅ OrderItem (a row = an order item)
❌ Customers, Orders, OrderItems

Why: Consistency, reads naturally in code
Exception: Lookup tables MAY use plural if team decides, but be consistent
```

**Heuristic #7: PascalCase Everywhere**
```
Rule: PascalCase for all identifiers. No spaces, underscores, or hyphens.

Examples:
✅ Customer, OrderItem, FirstName, CreatedDate
❌ customer, order_item, first_name, created_date

Why: Industry standard, OutSystems convention, readability
Exception: None. Be consistent always.
```

**Heuristic #8: Primary Key Pattern**
```
Rule: Primary key = [TableName]Id, INT IDENTITY (or BIGINT).

Examples:
✅ Customer table: CustomerId INT IDENTITY(1,1) NOT NULL PRIMARY KEY
✅ OrderItem table: OrderItemId INT IDENTITY(1,1) NOT NULL PRIMARY KEY
❌ Just "Id" (ambiguous in joins)
❌ CustomerKey, CustomerPK (non-standard)

Why: Unambiguous, joins read naturally (ON CustomerId = CustomerId)
```

**Heuristic #9: Foreign Key Constraints**
```
Rule: FK constraint name = FK_ChildTable_ParentTable

Examples:
✅ FK_Orders_Customers
✅ FK_OrderItems_Orders
✅ FK_OrderItems_Products
❌ FK_CustomerOrders (ambiguous which is parent)

Why: Clear parent-child relationship, easy to understand
```

**Heuristic #10: Index Names**
```
Rule: IX_TableName_Column1_Column2

Examples:
✅ IX_Orders_CustomerId
✅ IX_Orders_OrderDate
✅ IX_Orders_CustomerId_OrderDate
❌ IX_Orders (what column(s)?)
❌ idx_orders_customer (wrong case)

Why: Self-documenting, see what's indexed without querying metadata
```

---

#### NULL Handling

**Heuristic #11: Required Fields**
```
Rule: NOT NULL if value must exist. NULL if value optional or unknown.

Always NOT NULL:
- Primary keys
- Foreign keys (usually - unless relationship truly optional)
- CreatedDate, CreatedBy (audit fields)
- Status fields (use DEFAULT)

Usually NULL:
- Optional contact info (MiddleName, PhoneNumber)
- End dates on ongoing records (EndDate NULL = still active)
- Fields populated later (ShippedDate on pending order)

Red flag: NOT NULL on every column (over-constraining)
```

**Heuristic #12: DEFAULT Values**
```
Rule: NOT NULL requires DEFAULT if table has data. Always provide DEFAULT for flags.

Examples:
✅ [IsActive] BIT NOT NULL DEFAULT 1
✅ [CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE()
✅ [Status] VARCHAR(20) NOT NULL DEFAULT 'Pending'
❌ [IsActive] BIT NOT NULL (no DEFAULT on table with data)

Why: Can't add NOT NULL to existing rows without a value
```

---

#### Indexing

**Heuristic #13: Foreign Keys Get Indexes**
```
Rule: Every FK column gets a non-clustered index. No exceptions.

Example:
-- Define FK
CONSTRAINT FK_Orders_Customers
    FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)

-- Add supporting index
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON Orders(CustomerId);

Why: JOINs use FKs constantly. Without index = table scans = slow.
Performance: 100x+ improvement on large tables
```

**Heuristic #14: Index Columns in ORDER BY**
```
Rule: Frequent ORDER BY column = index candidate, usually DESC for recent items.

Example:
-- Frequent query: Recent orders
SELECT * FROM Orders 
WHERE CustomerId = @id 
ORDER BY OrderDate DESC;

-- Index:
CREATE INDEX IX_Orders_CustomerId_OrderDate
    ON Orders(CustomerId, OrderDate DESC);

Why: Sorts are expensive. Index can provide pre-sorted data.
```

**Heuristic #15: Covering Indexes for Hot Queries**
```
Rule: If query runs 1000+ times/day, consider covering index with INCLUDE.

Example:
-- Hot query:
SELECT OrderId, OrderDate, TotalAmount, Status
FROM Orders
WHERE CustomerId = @id
ORDER BY OrderDate DESC;

-- Covering index:
CREATE INDEX IX_Orders_CustomerId_OrderDate_Covering
    ON Orders(CustomerId, OrderDate DESC)
    INCLUDE (OrderId, TotalAmount, Status);

Why: Eliminates key lookups, can be 10x faster
Cost: More storage, slower writes. Worth it for hot queries.
```

**Heuristic #16: Index Column Order**
```
Rule: Most selective column first in composite index.

Example:
-- Query: WHERE CustomerId = @id AND Status = 'Pending'
-- CustomerId matches 100 rows, Status matches 10,000 rows

✅ CREATE INDEX IX_Orders_CustomerId_Status 
    ON Orders(CustomerId, Status);

❌ CREATE INDEX IX_Orders_Status_CustomerId  
    ON Orders(Status, CustomerId);

Why: Index seeks on CustomerId (100 rows) then filters Status.
Wrong way: Seeks on Status (10,000 rows) then filters CustomerId.
Rule of thumb: Equality columns before range columns, selective before broad.
```

---

#### Query Patterns

**Heuristic #17: SARGable Queries**
```
Rule: Don't wrap indexed columns in functions. Rewrite to direct comparison.

❌ Non-SARGable (can't use index):
WHERE YEAR(OrderDate) = 2024
WHERE UPPER(LastName) = 'SMITH'
WHERE SUBSTRING(Email, 1, 5) = 'admin'

✅ SARGable (can use index):
WHERE OrderDate >= '2024-01-01' AND OrderDate < '2025-01-01'
WHERE LastName = 'Smith' (or use COLLATE if case-insensitive needed)
WHERE Email LIKE 'admin%'

Why: Functions on columns prevent index seeks
Performance: 100x+ difference on large tables
```

**Heuristic #18: Explicit Column Lists**
```
Rule: Never SELECT * in production code. Always list columns.

❌ Wrong:
SELECT * FROM Customers

✅ Right:
SELECT CustomerId, FirstName, LastName, Email
FROM Customers

Why: 
- App breaks when columns added/removed/reordered
- Fetches unneeded data (performance hit)
- Security exposure (might return sensitive columns)
Exception: Ad-hoc queries in SSMS (still bad habit though)
```

**Heuristic #19: Join Explicitly**
```
Rule: Always use explicit JOIN syntax. INNER JOIN is default.

✅ Explicit:
SELECT c.FirstName, o.OrderDate
FROM Customers c
INNER JOIN Orders o ON c.CustomerId = o.CustomerId
WHERE c.IsActive = 1;

❌ Implicit (old style):
SELECT c.FirstName, o.OrderDate
FROM Customers c, Orders o
WHERE c.CustomerId = o.CustomerId
  AND c.IsActive = 1;

Why: Explicit joins clearer intent, easier to maintain
Old style: Prone to accidental cross joins (Cartesian products)
```

---

#### Schema Design

**Heuristic #20: Audit Columns Standard**
```
Rule: Every table gets these 4 audit columns.

Template:
[CreatedDate] DATETIME2 NOT NULL DEFAULT GETDATE(),
[CreatedBy] NVARCHAR(100) NOT NULL DEFAULT SUSER_NAME(),
[ModifiedDate] DATETIME2 NULL,
[ModifiedBy] NVARCHAR(100) NULL

Why: Debugging, compliance, data lineage
Cost: 16 bytes per row (worth it)
Update: Use triggers or application code to set ModifiedDate/By
```

**Heuristic #21: Soft Delete Pattern**
```
Rule: Don't delete data. Flag as deleted instead.

Template:
[IsDeleted] BIT NOT NULL DEFAULT 0,
[DeletedDate] DATETIME2 NULL,
[DeletedBy] NVARCHAR(100) NULL

Then:
-- "Delete"
UPDATE Customers 
SET IsDeleted = 1, DeletedDate = GETDATE(), DeletedBy = SUSER_NAME()
WHERE CustomerId = @id;

-- Queries exclude deleted
SELECT * FROM Customers WHERE IsDeleted = 0;

Why: Recover from mistakes, audit trail, compliance
Consider: Filtered index on IsDeleted = 0 for performance
```

**Heuristic #22: Don't Fear Normalization**
```
Rule: Normalize first (3NF), denormalize only when measured performance problem.

Normalize (start here):
- No repeating groups (use junction table)
- Separate entity = separate table
- No derived/calculated values stored

Denormalize (only if needed):
- Aggregates in reporting tables
- Cached calculations for performance
- Audit data snapshots

Why: Normalization reduces bugs, improves data integrity
Premature denormalization = premature optimization (root of evil)
Measure first, optimize second
```

**Heuristic #23: Surrogate vs Natural Keys**
```
Rule: Surrogate keys (INT IDENTITY) for primary keys. Natural keys as UNIQUE constraints.

Example:
-- Good:
CREATE TABLE Customer (
    CustomerId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,  -- Surrogate
    Email NVARCHAR(100) NOT NULL UNIQUE,                -- Natural key as constraint
    ...
)

Why: Surrogate keys stable (don't change), small (INT), fast (clustered)
Natural keys: Validate with UNIQUE, don't use as PK (can change, often larger)
Exception: Lookup tables with fixed IDs (OrderStatus) can use natural key as PK
```

---

#### Deployment & Operations

**Heuristic #24: Test Locally First**
```
Rule: Build and deploy to local DB before submitting PR.

Process:
1. Make change
2. Build (Ctrl+Shift+B)
3. Fix errors
4. Publish to (localdb)\MSSQLLocalDB
5. Verify in SSMS
6. Test OutSystems connection (if applicable)
7. THEN commit and PR

Why: Catch 80% of issues before they hit team
Time: 5 minutes investment saves hours of team debugging
```

**Heuristic #25: Small PRs Over Large PRs**
```
Rule: Each PR = one logical change. Multiple changes = multiple PRs.

Good PR:
- Add PhoneNumber to Customers (1 table, 1 column)

Bad PR:
- Add PhoneNumber to Customers
- Add OrderStatus table  
- Refactor Orders table
- Optimize 3 queries
(4 unrelated changes)

Why: Easier to review, easier to rollback, easier to understand
Target: < 10 files changed per PR
Exception: Coordinated refactoring that must move together
```

**Heuristic #26: Idempotent Scripts Always**
```
Rule: Post-deployment scripts run every time. Use MERGE or IF NOT EXISTS.

✅ Idempotent:
MERGE INTO OrderStatus ...

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CustomerId = 1)
    INSERT INTO Customers ...

❌ Not Idempotent:
INSERT INTO OrderStatus VALUES (1, 'Pending');  -- Fails second time

Why: Scripts run every deployment, not just first
Test: Deploy twice locally, should succeed both times
```

---

#### Performance

**Heuristic #27: Think at Scale**
```
Rule: Design for 1M rows minimum, even if starting with 1000.

Questions to ask:
- How does this perform with 1M rows?
- Will this query timeout with 10M rows?
- Can we batch this operation?
- Do we need archiving strategy?

Why: Easier to build right from start than retrofit later
Cost: Minimal upfront (add index, think about data types)
Benefit: Massive later (avoid re-architecture)
```

**Heuristic #28: Batch Large Operations**
```
Rule: Operations affecting > 10,000 rows = batch in chunks.

Pattern:
DECLARE @BatchSize INT = 10000;
WHILE EXISTS (SELECT 1 FROM Orders WHERE Processed = 0)
BEGIN
    UPDATE TOP (@BatchSize) Orders
    SET Processed = 1
    WHERE Processed = 0;
    
    WAITFOR DELAY '00:00:02';  -- 2 second pause
END

Why: Smaller transactions, less locking, can be interrupted
Target: < 10 second per batch
```

**Heuristic #29: Measure, Don't Guess**
```
Rule: Use execution plans, not intuition, for performance tuning.

Process:
1. Enable execution plan (SSMS: Ctrl+M)
2. Run query
3. Look for:
   - Table/Index Scans (bad on large tables)
   - Key Lookups (consider covering index)
   - High-cost operators (Sort, Hash Match)
   - Missing index warnings
4. Fix one thing
5. Re-measure
6. Repeat

Why: Intuition often wrong at scale
Tool: Actual execution plan > estimated plan (shows real data)
```

---

#### Safety & Risk

**Heuristic #30: Backup Before DROP**
```
Rule: Dropping column/table with data? Backup first.

Process:
-- Pre-deployment script:
SELECT * INTO #CustomerBackup FROM Customers;

-- (Let DACPAC drop column)

-- Post-deployment: Verify data preserved elsewhere
-- Then DROP TABLE #CustomerBackup after verification

Why: Accidents happen. Give yourself an "undo" button.
Cost: Negligible for < 10M rows
Alternative: Dev only, or snapshot database before deployment
```

**Heuristic #31: Never Assume Empty**
```
Rule: Assume every table has data, even if it "shouldn't".

Check before:
- Dropping columns
- Changing data types
- Adding NOT NULL
- Changing constraints

Query:
SELECT COUNT(*), MIN(CreatedDate), MAX(CreatedDate) 
FROM TableName;

Why: "This table is empty" often wrong
Cost: 1 query, 5 seconds
Benefit: Prevent data loss
```

**Heuristic #32: Read-Only Queries in SSMS**
```
Rule: When exploring production in SSMS, start transaction and rollback.

Pattern:
BEGIN TRANSACTION;  -- Safety!

-- Your exploratory queries
SELECT * FROM Orders WHERE ...;
UPDATE Orders SET ... WHERE ...;  -- Testing logic

ROLLBACK TRANSACTION;  -- Undo everything

Why: Prevent accidental updates in production
Habit: Always wrap modifications in transaction when exploring
Commit: Only when 100% sure
```

---

### Decision Frameworks

#### When to Add an Index?

```
Decision tree:
├─ Column in WHERE clause frequently (>100/day)? → YES
├─ Column in JOIN condition? → YES (especially FKs)
├─ Column in ORDER BY frequently? → YES
├─ Table has < 1000 rows? → NO (overhead not worth it)
├─ Column has 2 distinct values (IsActive)? → NO (not selective)
└─ Write-heavy table (INSERT/UPDATE > SELECT)? → MAYBE (measure)
```

#### When to Denormalize?

```
Decision tree:
├─ Normalized schema has measured performance issue? → MAYBE
├─ Query runs >1000/day AND takes >1 second? → MAYBE
├─ Tried indexes and query optimization first? → REQUIRED
├─ Can accept slight data inconsistency? → CONSIDER
├─ Have plan to keep denormalized data synced? → REQUIRED
└─ Just guessing it'll be slow? → NO (premature optimization)
```

#### When to Use MERGE vs. INSERT?

```
Decision tree:
├─ Script runs every deployment? → MERGE (idempotent)
├─ Data might already exist? → MERGE (handles both cases)
├─ Need to update existing records? → MERGE (handles updates)
├─ One-time migration with version check? → INSERT with IF NOT EXISTS
└─ Creating new data always? → INSERT (simpler)
```

---

### Red Flags (Stop and Ask)

**Schema Design Red Flags**:
```
🚩 Table with > 50 columns (wide table - split?)
🚩 Column names like Column1, Data1, Value1 (poor design)
🚩 JSON/XML column (relational DB - should be tables?)
🚩 Many nullable columns (> 50% NULL - should be separate table?)
🚩 No primary key (always wrong)
🚩 No foreign key constraints (data integrity risk)
```

**Query Red Flags**:
```
🚩 SELECT * (specify columns)
🚩 Function on indexed column in WHERE (non-SARGable)
🚩 Cursor loop (usually replaceable with set-based operation)
🚩 NOLOCK hint (dirty reads, use READ COMMITTED SNAPSHOT instead)
🚩 Implicit joins (use explicit JOIN syntax)
```

**Process Red Flags**:
```
🚩 PR without description (reject)
🚩 Build failing (don't review)
🚩 Renaming without refactorlog (data loss risk)
🚩 Dropping column without migration plan (data loss)
🚩 "Quick fix" to production (process violation)
```

---

### Golden Rules (Never Break)

```
1. Schema is version controlled (always)
2. Every PR reviewed before merge (always)  
3. Build passes before commit (always)
4. Foreign keys have indexes (always)
5. Post-deployment scripts idempotent (always)
6. Use Refactor menu for renames (always)
7. Test locally before PR (always)
8. Naming conventions followed (always)
9. Communication before breaking changes (always)
10. When in doubt, ask (always)
```

---

### Learning Resources by Topic

**Data Types & Design**:
- "Database Design for Mere Mortals" by Michael Hernandez
- [Microsoft: Data Type Guide](https://learn.microsoft.com/en-us/sql/t-sql/data-types/data-types-transact-sql)

**Indexing**:
- "SQL Server Index Design Guide" (Microsoft Learn)
- [Red9: SQL Server Performance Tuning with Indexes](https://red9.com/blog/sql-server-performance-tuning-using-indexes/)

**Query Optimization**:
- "SQL Performance Explained" by Markus Winand (platform-agnostic)
- "SQL Server Query Performance Tuning" by Grant Fritchey

**Best Practices**:
- [MSSQLTips: T-SQL Best Practices](https://www.mssqltips.com/)
- [Brent Ozar: SQL Server Best Practices](https://www.brentozar.com/)

---

## Summary: The Three Pillars

**Release Train**:
- Predictable rhythm (2 PM PR cutoff, 3 PM deployment, 3:30 PM refresh)
- Batched operations (efficiency)
- Clear communication (no surprises)

**Swim Lanes**:
- Right person, right task (safety)
- Clear progression (growth)
- Appropriate delegation (efficiency)

**Best Practices**:
- Heuristics for daily decisions (speed)
- Red flags and golden rules (quality)
- Measure and improve (continuous improvement)

**Together, these enable**:
✅ Fast iteration without chaos
✅ Quality without bottlenecks
✅ Learning without handholding
✅ Scale without heroics

**Starting Monday, we operate this way.**

---

**Last Updated**: [This Weekend]  
**Maintained By**: Database Platform Team & Dev Leads

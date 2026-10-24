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


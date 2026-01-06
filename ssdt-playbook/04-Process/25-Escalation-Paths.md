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


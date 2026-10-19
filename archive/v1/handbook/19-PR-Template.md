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



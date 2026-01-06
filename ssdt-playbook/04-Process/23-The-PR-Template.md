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


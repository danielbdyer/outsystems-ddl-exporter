# 28.7 Incident Report Template

Use after any incident for blameless post-mortem:

```markdown
# Incident Report: [Brief Title]

**Date:** YYYY-MM-DD
**Environment:** [Dev/Test/UAT/Prod]
**Reported By:** [Name]
**Severity:** [1-4]

## Summary

[One paragraph: What happened, when, what was the impact]

## Timeline

| Time (UTC) | Event |
|------------|-------|
| HH:MM | [First indication of problem] |
| HH:MM | [Key events...] |
| HH:MM | [Resolution completed] |

## Impact

- **Duration:** X hours/minutes
- **Users Affected:** [Number or scope]
- **Data Impact:** [None / Rows affected / Description]
- **Functionality Impaired:** [Description]

## Root Cause

[Technical explanation of what went wrong and why]

## What Went Wrong

[Detailed analysis — be specific about the mistake or gap]

## What Went Right

[Things that caught the issue, limited damage, or helped recovery]

## Action Items

| Action | Owner | Due Date | Status |
|--------|-------|----------|--------|
| [Preventative measure] | [Name] | YYYY-MM-DD | [ ] |
| [Monitoring improvement] | [Name] | YYYY-MM-DD | [ ] |
| [Documentation update] | [Name] | YYYY-MM-DD | [ ] |

## Lessons Learned

[What we learned from this incident that should inform future work]

---

**Blameless Note:** This report focuses on systems, processes, and understanding — not individual fault. The goal is learning and improvement.
```

---

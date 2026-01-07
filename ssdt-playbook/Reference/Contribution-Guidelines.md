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


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


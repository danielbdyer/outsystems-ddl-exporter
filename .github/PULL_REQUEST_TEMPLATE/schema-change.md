<!--
  SCHEMA CHANGE PULL REQUEST — the record for a database change.

  Source of truth: sidecar/projection/ssdt-agent/skills/author-pr/SKILL.md (this file mirrors it;
  if they disagree, the skill wins — fix this file in the same commit).

  Use this template for any change to a .sqlproj: table definitions, pre/post-deployment scripts,
  the refactorlog, publish profiles. GitHub: open the PR with ?template=schema-change.md.
  Azure DevOps: copy this file to .azuredevops/pull_request_template/ (default or branch-scoped).

  Ground rules:
  - Every section stays; a section with nothing to report says so in one line ("No data is
    remediated.") — the explicit negative is itself a finding the reviewer relies on.
  - Scripts ship inside the sqlproj. Evidence is summarized here as text. Nothing is attached for
    the reviewer to execute — this body is approvable by reading.
  - Findings first, proof beneath: verbatim errors, row counts, object names. No narration.
-->

# <Table>: <plain summary of the change> (<one-clause consequence, if any>)

## Summary

<!-- 1–3 sentences: what changes, on which table/columns, and the business reason. Name the work item. -->

## Review & release

- <!-- Who must review, and why — one plain finding. E.g.:
     "Any team member can review this: the change is additive and the running application is unaffected."
     "A dev lead must review this: existing data is modified."
     "A principal must review this: data is removed and the removal cannot be undone." -->
- <!-- How it ships — one plain finding. E.g.:
     "Ships as a single schema change, applied in place. No data is read or written."
     "Ships as one release: a pre-deployment script prepares the data, then the schema change lands validated."
     "Ships across N releases so the running application keeps working while the change is in flight." -->
- <!-- Added scrutiny, if any — one line each, or "None.":
     CDC-tracked table (capture instance frozen to current columns) · production row counts may block
     writes or run long (schedule a window) · first time this operation runs on this estate. -->

## Changes

| File | Change |
|---|---|
| <!-- path --> | <!-- what changed, plainly --> |

<!-- Then the honest negative — what a reviewer might fear changed and did not:
     "No renames (refactorlog unchanged). No index, view, or procedure changes." -->

## Data remediation

<!-- If existing data is changed so the schema change can land:
     - the violating rows, by name and count
     - the decision taken, who made it, and the work item
     - the original values, recorded for audit
     If none: "No data is remediated." -->

## Deployment evidence — <environment copy>, <date>, sqlpackage <version>

<!-- Findings with proof beneath:
     - the blocked publish (before remediation), verbatim error + count, if applicable — the block
       is evidence the guard works, not a failure to hide
     - the clean publish (after remediation) and the proven end state (is_not_trusted = 0, counts)
     - what the generated deploy script contained (ADD CONSTRAINT / sp_rename / no rebuild, no drops)
     - the second, no-op publish where idempotency matters -->

## Verification — run in each environment after deployment

```sql
-- expect <the unambiguous result>: <what it proves>

```

## Rollback

<!-- How the change is backed out and whether that is lossless. What is NOT auto-reversible, with
     the recorded originals a manual restore would use. Do not claim reversibility that was not
     exercised. -->

## Not verified

<!-- Mandatory — the standing limits of a disposable-copy publish, specific to THIS change:
     - Application impact: the exact new failure the running app can hit, and who confirms it (@owner)
     - Other environments: what Test/UAT/Prod data this copy cannot know; run the verification query
       before promotion
     - Production scale and timing: blocking or duration the small copy cannot show
     - Reversibility: if the forward publish is all that was proven, say so -->

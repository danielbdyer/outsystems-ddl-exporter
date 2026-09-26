# Reviewers — the one approver class (recalibrated 2026-08-28)

On this estate, every schema change is approved by a **dev lead**, and only by a dev lead.
A developer never approves a schema change — not their own, not another developer's. There is
no level above the dev lead: the principal escalation level is retired. What used to be
routing between levels is now carried **inside the record**: the "what the lead weighs" line
(`THE_RECORD.md` §5) tells the approving lead how heavy this particular approval is, from the
lightest additive look to an irreversible removal named explicitly.

This file names the people who hold the dev-lead approver role right now. The reviewer agent
reads it to address a person; a record never names a person directly (people go on leave and
change roles — this file is easy to update, a record is not).

| approver (dev lead) | name | SSDT experience | available |
|---|---|---|---|
| Dev lead 1 | — fill in at the Dev cutover — | — fill in — | yes |
| Dev lead 2 | — fill in at the Dev cutover — | — fill in — | yes |

Add or remove rows as the role changes hands; at least one available row must exist for the
estate to ship at all. An out-of-date row here is a defect, the same as an out-of-date row in
`operations.md`.

## The standing rules

- **Every change: a dev lead approves.** The pull request carries the weigh-line and the
  proof; the lead approves by reading (and, when warranted, by reproducing — the reviewer
  agent's whole job is making that cheap).
- **No self-approval, at any seniority.** The author of a change — developer or lead — does
  not approve it; another dev lead does.
- **The irreversible-change practice** (the strongest weigh-line: data removed, removal
  cannot be undone): before the change ships, take a backup or snapshot that could restore
  the data and prove the restore works, and record the decision in `refusals.md` with its
  proof. This practice survives the retired principal level because the risk it answers did
  not retire.

## Writing records for this pool

Some approving leads may be new to SSDT. `THE_RECORD.md` §9 sets the standard: write each
record so a reviewer who knows SQL well but is new to SSDT can act on it, and add a
one-clause gloss the first time an SSDT-only term appears. The reviewer agent applies the
same calibration when it writes a disposition (`../agents/reviewer.md` §2).

# Reviewers — who fills each review level, and who stands in when the principal is away

On this estate, four named people review changes. No one else approves a pull request. The team
worked this way for OutSystems changes before the pivot to SSDT, and it continues under SSDT.

`THE_RECORD.md` §5 gives every change a review-level sentence — for example "any team member can
review this", "a dev lead must review this", or "a principal must review this". Those sentences
name a *level*. This file names the *person* at each level right now. The reviewer agent and the
`classify-mechanism` skill both read this file to turn a level into a person. They look it up here
each time, rather than working from memory.

| level slot | name | SSDT experience | available |
|---|---|---|---|
| Senior reviewer 1 | — fill in at the Dev cutover — | new to SSDT; strong SQL; OutSystems-native | yes |
| Senior reviewer 2 | — fill in at the Dev cutover — | new to SSDT; strong SQL; OutSystems-native | yes |
| Senior reviewer 3 | — fill in at the Dev cutover — | new to SSDT; strong SQL; OutSystems-native | yes |
| Principal | — fill in at the Dev cutover — | fluent in SSDT | out of office for the first cutover window — fill in the dates |

## How a review level maps to these people

Until this file is revised:

- **"Any team member can review this"** → any one of the four.
- **"A dev lead or an experienced developer should review this", or "a dev lead must review this"**
  → any one of the three senior reviewers. Attach the proof the change-author produced: the
  reproduced delta, the dependency scope, and the queries to run in each environment.
- **"A principal must review this"** → the principal. While the principal is out of office, use the
  stand-in rule below.

## The stand-in rule (while the principal is away)

A change that needs the principal is the kind that removes data in a way that cannot be undone.
While the principal is out:

1. If the change can wait for the principal to return, hold it.
2. If it cannot wait, two of the senior reviewers review it together.
3. Before the change ships, take a backup or snapshot that could restore the data, and prove the
   restore works.
4. Record the risk in `refusals.md`, with its proof.
5. When the principal returns, they read the record and confirm the decision.

## Writing records for this pool

Three of the four reviewers are new to SSDT. `THE_RECORD.md` §9 sets the standard: write each
record so a reviewer who knows SQL well but is new to SSDT can act on it, and add a one-clause
gloss the first time an SSDT-only term appears. The reviewer agent applies the same calibration
when it writes a disposition (`../agents/reviewer.md` §2).

## Keeping this file true

- A record cites a review *level*, from `THE_RECORD.md` §5. This file maps that level to a person.
  A record never names a person directly: people go on leave and change roles, so a name written
  into a record would go stale. This file is easy to update; a record is not.
- Fill in the names at the Dev cutover. Remove the principal's out-of-office note when the
  principal returns. An out-of-date row here is a defect, the same as an out-of-date row in
  `operations.md`.

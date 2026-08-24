# The review pool — who approves, and the standing absence rule

Review on this estate is a **fixed pool of four named people**, mirroring how OutSystems
changes were reviewed before the pivot — not open roles. The record's review-level sentences
(`../THE_RECORD.md` §5) name a rung; this file owns who that is this season. The reviewer
agent and `classify-mechanism` read this file the way they read the other ledgers — a lookup,
never a recollection.

| slot | name | SSDT depth | availability |
|---|---|---|---|
| Senior reviewer 1 | (record at cutover) | new — strong SQL, OutSystems-native | — |
| Senior reviewer 2 | (record at cutover) | new — strong SQL, OutSystems-native | — |
| Senior reviewer 3 | (record at cutover) | new — strong SQL, OutSystems-native | — |
| Principal | (record at cutover) | fluent | out of office for the initial cutover window (record dates) |

The rung mapping (until revised):

- **"Any team member can approve this"** → any of the four.
- **"A dev lead or an experienced developer" / "a dev lead"** → one of the three senior
  reviewers, with the packet's reproduced proof attached.
- **"A principal must review this"** → the Principal. **During the absence window:** defer the
  change when it can wait; otherwise **two senior reviewers co-review** (the deputized form),
  a pre-change backup/snapshot is proven, the risk is logged in `refusals.md`, and the
  Principal ratifies asynchronously on return.

Register calibration: three of the four are SSDT-new. Records stay finding-first and
agentless, and gloss each SSDT-only construct in one clause on first use (`../agents/reviewer.md`
§2). The no-gloss terse register is reserved for the Principal.

Discipline:

- A review-level sentence in a record cites the rung; this file resolves the rung to people.
  A record never names a person — availability changes faster than records do.
- Update the table at the Dev cutover; sweep the absence row when the Principal returns. A
  stale availability row is a defect with the same standing as a stale operations row.

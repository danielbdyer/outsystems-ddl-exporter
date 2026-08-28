# estate/ — the estate ledger (state gets a home)

The tree's working memory about **the estate itself** — the facts that outlive any one change
and any one session. Before this directory existed, two of the record's standing lines
("this operation has not been performed on this estate before"; "at production row counts…")
were permanently *asserted* because nothing recorded the history they claim, multi-phase
changes had no register to keep Phase 2 from being forgotten, and the refusal ledger the
verdict skill writes had no named file. Each ledger below closes one of those gaps.

Staged here beside the tree; at the eject it moves with the estate repository (the peel — the
formats carry no engine identifiers).

## The ledgers

| File | What it answers | Who writes it, when |
|---|---|---|
| `operations.md` | "Has this operation been performed on this estate before?" — the first-time added-scrutiny line becomes a lookup | one appended row per shipped change, at the production apply |
| `row-tiers.md` | "At production row counts, does this change need a window?" — the scale added-scrutiny line becomes a lookup | refreshed from the estate (or the Twin's evidence) whenever tiers shift an order of magnitude |
| `in-flight.md` | "Which multi-phase changes are mid-flight, what ships next, and by when?" — the forgotten Phase 2 becomes a red gate, and the row's `tables` column is the machine-readable hold `scripts/inflight-check.mjs` enforces against a colliding pull request | a row when phase 1 merges; updated each phase; removed when the final phase ships |
| `refusals.md` | the named home of the verdict skill's refusal ledger — every named risk and escalation, with its proof artifact | appended by the reviewer at disposition time |
| `reviewers.md` | "Which named person fills each review level right now, and who stands in when the principal is away?" — the record's review-level sentence becomes a person | updated at the Dev cutover, and whenever a reviewer's availability changes |
| `handoffs/` | the specified home for captured change-specs and review packets when personas hand off across sessions | written during a change; swept when its pull request merges |

## The discipline (three rules)

1. **The ledger row is part of the change, not an afterthought.** A shipped change whose
   operations row is missing, or a multi-phase change whose in-flight row was not advanced,
   is an incomplete change — the same standing as a missing refactorlog entry.
2. **A scrutiny line contradicting the ledger is a defect** — in the packet if the ledger is
   right, in the ledger if the estate moved. Either way it is fixed in the same change, never
   waved through.
3. **Transient state dies on schedule.** `in-flight.md` rows carry a window date the CI gate
   enforces (a stale row fails the build until it is advanced or consciously re-dated);
   `handoffs/` entries are swept at merge — the pull request is the durable record.

The `estate` face of `sidecar/projection/ssdt-agent/scripts/ssdt-agent-gates.mjs` keeps these ledgers
machine-readable (table structure) and keeps `in-flight.md` honest (no silently expired
windows; every refusal row carries its proof artifact).

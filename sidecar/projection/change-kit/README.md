# change-kit -- prove a model change is safe to ship

You are an OutSystems developer. You used to make a data-model change in Service
Studio -- add an attribute, make one mandatory, add a Reference -- press
Publish, and trust the platform to sort out the existing rows. On a database
that SSDT owns (an External Entity), nobody sorts out the rows for you. A lead
used to be the safety net by hand.

**This kit is the safety net, automated.** You describe the change in your own
words. The kit runs *your literal change* on a throwaway local SQL Server loaded
with real-shaped data, lets SQL Server itself say what breaks, runs the fixed
version, and hands you one verdict:

```
{ what breaks,  the fix,  the proof }
```

The differentiator is **proof, not advice**. It does not predict what might go
wrong. It runs the change on a copy and shows you the corpse and the cure.

---

## One verb, one loop

Everything is one script: `change-kit/prove-safe.sh`. Give it a **scenario** --
three SQL files describing your change -- and it does the rest:

```
change-kit/scenarios/<your-change>/
  00-seed.sql          BEFORE schema + real-shaped rows (drop-create idempotent)
  10-change-naive.sql  the change as you'd first write it   (expected to BREAK)
  20-change-fixed.sql  the remediated change                (expected to SUCCEED)
```

```bash
# from sidecar/projection
bash change-kit/prove-safe.sh change-kit/scenarios/notnull
```

The loop: reuse the disposable SQL Server (`scripts/warm-sql.sh`) -> fresh
per-run database -> seed -> snapshot (row count **and** a value checksum) ->
apply the naive change (SQL Server raises the real error) -> reset -> apply the
fixed change -> snapshot again -> print the verdict -> drop the database. Zero
F#, zero host tooling beyond Docker.

The **value checksum** is what makes the proof real: a naked rename
(`DROP col` + `ADD col`) keeps the row count identical while destroying every
value -- only the checksum catches it.

---

## The operations

Each operation is a **skill** under `.claude/skills/<op>/SKILL.md`. You do not
pick one from a menu -- describe your change in OutSystems terms and the right
skill is selected automatically from its description. A skill speaks your
language, names the one trap, and tells you exactly how to prove it with the loop
above. There is deliberately **no router**: the skill descriptions are the
dispatch surface.

Shipped today:

| You want to... | Skill |
|---|---|
| make an existing attribute mandatory (Is Mandatory: No -> Yes) | `make-column-not-null` |

More operations (add-attribute, add-reference, change-data-type, add-index,
rename, drop-column) are **deferred on purpose** -- see the ledger at the bottom.
The kit ships the one operation that proves the whole design, plus the loop's own
test suite, and grows by demand.

---

## Try it now (the magic moment)

```bash
cd sidecar/projection
bash change-kit/prove-safe.sh change-kit/scenarios/notnull
```

You will see the naive `ALTER ... NOT NULL` break with the real
*"Cannot insert the value NULL into column 'Email'"*, then the backfill-then-
constrain fix succeed, then a checksum proof that all five seeded rows survived.

Run the loop's full self-test (notnull + the rename/narrow/fk regression
scenarios that prove the data oracle catches silent corruption):

```bash
bash change-kit/prove-safe.sh --selftest
```

---

## Authoring a new operation skill (the convention)

A `SKILL.md` is pure SQL-and-prose content. It **never** re-implements the loop;
it composes `prove-safe.sh`. The shape:

```markdown
---
name: <kebab-case, matches the directory name>
description: >
  Use when an OutSystems-native developer wants to <intent in THEIR words> --
  triggers on "<verbatim phrases the dev types>". Proves it is SAFE TO SHIP by
  running it on a throwaway local SQL Server with real-shaped data and returning
  {what breaks, the fix, the proof}.
---

# <operation> -- prove it is safe to ship

## What you said you want (OutSystems -> SSDT)   # one mapping table; orient first
## The one trap this catches                     # cite handbook/16-...  §19.x by FILENAME
## Inputs to collect                             # the minimum, in OutSystems terms
## The SSDT change                               # the .sql edit; phases if multi-phase
## Prove it                                      # the 3 scenario files + the prove-safe.sh call
## Read the verdict                              # BLOCKED vs SAFE; the rollback
## After it ships (the bridge)                   # Integration Studio + Service Studio refresh
```

**Cite the handbook by filename, never by a bare section number.** The handbook's
internal numbering is offset from its filenames (file `14` is internally §17,
file `16` is §19, file `17` is §20), so `§19.2` alone is ambiguous. Write
`handbook/16-Anti-Patterns-Gallery.md §19.2`. The worked example to copy is
`.claude/skills/make-column-not-null/SKILL.md`.

---

## What this CANNOT prove

The kit is honest about its edges. It proves **your literal change, run against
real-SHAPED data, on a real SQL Server engine.** It does **not** prove:

- **What your SSDT/dacpac publish pipeline will do.** Your production change goes
  through a dacpac publish, which can rebuild a table or veto on possible data
  loss where a raw `ALTER` would just run. A `SAFE` verdict here is *"your
  literal change ran on real-shaped data"*, **not** *"your SSDT publish will
  behave the same."* For rename and drop-column especially, the production
  hazard is declarative (SSDT generating `DROP`+`CREATE`, or
  `BlockOnPossibleDataLoss` vetoing) -- those are not reproduced by hand-written
  DDL, which is exactly why those operations are deferred until the declarative
  path is built.
- **Production-scale lock behaviour or timing.** The seed is real-*shaped*, not
  real-*volume*. The "can this go in one release?" call for a large backfill
  depends on row count the kit does not have.
- **Application or SSIS impact.** Whether OutSystems screens/logic or the SSIS
  consumer still work after the change. The kit touches only the database.
- **CDC / Change-History continuity.** A schema change on a CDC-enabled table can
  leave a stale capture instance (`handbook/16-Anti-Patterns-Gallery.md` §19.5).
- **Rollback.** It proves forward, not the reverse. Each skill names its rollback
  from `handbook/11-Multi-Phase-Evolution.md`.

When the verdict is `SAFE`, read it as: *"this change, run on a sample shaped
like your data, executed and every row survived."* Not *"ship to prod
unsupervised."*

---

## Requirements

- Docker Desktop (Windows) -- the only host dependency.
- Git Bash. The script sets `MSYS_NO_PATHCONV=1` and pipes all SQL on stdin, so
  no path-mangling or `docker cp` traps bite you.
- `scripts/warm-sql.sh` (already in this repo) for the disposable SQL Server.

No .NET, no F#, no sqlpackage. The F# Projection engine in this repo is offered
as an *optional* profiling accelerant in one or two skills; it is never imported
and never on the critical path.

---

## What was deliberately left out (and what would earn it back)

Ruthless subtraction. Each deferred item has a named re-earn trigger:

| Deferred | Re-earn trigger |
|---|---|
| The other 6 operation skills (one at a time) | a developer actually asks for operation N **and** the loop already served operation 1 cleanly |
| rename / drop-column on the spine | the declarative (sqlpackage/dacpac) path is built -- their real hazard is declarative, not raw DDL |
| a `translate` / front-door / router skill | developer phrasing routinely fails to match any single operation's description |
| an SSDT `prove-ssdt.sh` (sqlpackage) sibling | a developer has an `.sqlproj`/`.dacpac` in hand **and** the raw-DDL spine has proven insufficient in real use |
| a numeric RISK LABEL / tier engine | the lead's adversarial reviewer persona ships and needs a machine-comparable risk field |
| `seed-static-entity` (run-twice idempotency proof) | requested, **and** the loop gains a second proof mode (it doesn't fit naive->fixed) |
| a synthetic profile-matched seed (F# σ emitter) | the shipped/extracted seeds demonstrably miss a real-data hazard a developer hit in practice |
| the lead's adversarial reviewer persona, dossiers, multi-agent roles | the reviewer is onboarded as a real second persona |

The discipline: ship the smallest thing that is real and runnable, prove it once,
and let demand -- not imagination -- earn the next piece.

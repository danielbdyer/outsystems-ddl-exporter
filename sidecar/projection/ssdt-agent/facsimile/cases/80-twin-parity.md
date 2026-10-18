# Twin parity: every Twin responsibility and fact, kept or retired on the record

*Covers T1 (ARCH §2, row by row), T2 (every `SamplePr*Tests` fact and every loop test, same verdict) and T3/T4 (the team's verbs and the G4 window). IDs: `TWN-1nn` responsibilities · `TWN-2nn` golden facts · `TWN-3nn` workflow. Every case names its ARCH §2 row as `row <n>` in its title: the golden facts are row 24 (the nightly proof lane, kept as the corpus); each loop and workflow case names the row it carries.*
*`tw` is the Twin fixture schema (`SamplePrBaseline`): `Status`(Id, Name; lane rows 1 Open · 2 Closed · 3 Pending) · `Customer`(IDENTITY Id, Name, Email nvarchar(250) NOT NULL, StatusId→Status, CreatedOn) · `Order`(IDENTITY Id, CustomerId→Customer, StatusId→Status, Channel nvarchar(20), Total, PlacedOn) · `OrderLine`(IDENTITY Id, OrderId→Order, Sku nvarchar(64), Quantity int, Note nvarchar(200) NULL); 25 rows per table unless stated; effective options are the Twin's strict posture (`BlockOnPossibleDataLoss` on, `GenerateSmartDefaults` off, `DropObjectsNotInSource` off).*
*A multi-leg Twin fact stays one case with legs `(a)`, `(b)`…, one fixture per leg; a leg the Twin ran as a direct statement is restated as the script it ships as. A Twin test name is the receipt for the verdict the facsimile must reproduce; the facsimile's mechanics inside such a case are `expected`. Proof copies take the merge-base reference data, `up` takes head's; a table plants the maximum of its needs (M2), so a violation witness is also the presence row.*

## Responsibilities (ARCH §2)

### TWN-101 · row 1 · Docker lifecycle → LocalDB: the loop runs where Docker never does
```case
kind:     process
setup:    Windows laptop · no Docker daemon, no `docker` on PATH · LocalDB 2022 (MSSQLLocalDB) · a Sql160 project ·
          a valid facsimile.json
action:   facsimile doctor · facsimile up · facsimile prove Dev
expect:   stdout     DONE doctor · … · .facsimile/out/doctor.json · exit 0 · DONE up · … · .facsimile/out/up.json · exit 0 ·
                     VERDICT … · proofs/<slug>/verdict.Dev.json · exit 0 or 3
          files      .facsimile/ caches · proofs/<slug>/verdict.Dev.json
          exit       0 · 0 · 0 or 3 (a verdict)
laws:     T1 A1 A3 S1
source:   expected · ARCH §2 row 1, §8 · the Twin side: `Runs.up` always calls `TwinContainer.ensureRunning` and connects
          as `sa` on a fixed port (TWIN_SUCCESSION §1)
note:     no verb starts a container, uses `sa`, or binds a fixed port; LocalDB is reached by integrated auth.
```

### TWN-102 · row 1 · no local engine is "machine not ready", not "Docker unreachable"
```case
kind:     process
setup:    no LocalDB installed · no `localdb` server configured
action:   facsimile doctor
expect:   outcome    stop toolchain · "SqlLocalDB not found. <how to get it>" · exit 6
laws:     T1 H3 A3
source:   expected · ARCH §6 toolchain · the Twin side: exit 4 = Docker unavailable (THE_TWIN §6)
note:     exit 4 now means an unreachable environment (A3), so the Twin's "Docker unreachable" 4 becomes the facsimile's 6.
```

### TWN-103 · row 2 · closed config: an unknown key is refused by its JSON path
```case
kind:     pure
action:   Read-FxConfig
input:    { "project": "Estate.sqlproj", "optoins": "facsimile/options.publish.xml" }
expect:   outcome    stop config · "facsimile.json: $.optoins: unknown key" · exit 6
laws:     T1 E4 H3
source:   expected · E4 · Twin anchor (receipt): TwinConfigTests "closed schema: an unknown key refuses, named by its path"
```

### TWN-104 · row 2 · closed config holds at every nesting level, in one pass
```case
kind:     pure
action:   Read-FxConfig
input:    { "project": "Estate.sqlproj",
            "environments": { "Dev": { "server": "dev-sql", "database": "Estate", "serevr": "x" } },
            "up": { "rowsPerTabel": 3 } }
expect:   outcome    stop config · names "$.environments.Dev.serevr" and "$.up.rowsPerTabel", both in one stop · exit 6
laws:     T1 E4 H1
source:   expected · E4 · Twin anchor (receipt): TwinConfigTests "closed schema holds inside nested sections"
```

### TWN-105 · row 2 · every problem in one pass; a missing key and malformed JSON are located; a minimal file parses
```case
kind:     pure
action:   Read-FxConfig
input:    (a) { "environments": {}, "localdb": 7, "extra": 1 }
          (b) { "project": "Estate.sqlproj",        (truncated)
          (c) { "project": "Estate.sqlproj" }
expect:   outcome    (a) one stop config naming $.project (required), $.localdb (type), $.extra (unknown key) · exit 6
                     · (b) stop config · "facsimile.json: <line, column>: malformed JSON" · exit 6
                     · (c) parses; defaults as ARCH §8's example (localdb MSSQLLocalDB, up.rowsPerTable 3)
laws:     T1 E4 H1 H3
source:   expected · E4 · Twin anchors (receipts): TwinConfigTests "errors aggregate: one pass reports every problem",
          "a missing estate section refuses with the expected shape", "malformed JSON refuses with the syntax code",
          "a minimal config parses with documented defaults"
```

### TWN-106 · row 2 · a secret is accepted only as an `env:` or `file:` reference (D9 kept)
```case
kind:     pure
action:   Read-FxConfig
input:    environments.Dev.auth given as (a) "hunter2" · (b) "env:DEV_SQL_PASSWORD" · (c) "file:../secure/pw.txt" ·
          (d) "integrated"
expect:   outcome    (a) stop config · "facsimile.json: $.environments.Dev.auth: inline secret; use env: or file:" · exit 6
                     · (b), (c), (d) parse; only the reference is kept, never the secret it resolves to
laws:     T1 E4 P1
source:   expected · ARCH §2 row 2 and §8 (`auth`: integrated, env:VAR or file:path) · the Twin side: src/Twin.Core/TwinConfig.fs
          (a secret-bearing value must read as an env: or file: reference) · Twin anchors (receipts): TwinConfigTests "D9:
          an inline connection secret refuses at parse", "D9: an inline container password refuses at parse"
note:     INVARIANTS E4 still reads "only as an env: reference"; ARCH §2 row 2 and §8 keep the Twin's file: form, and this
          case follows ARCH (reported).
```

### TWN-107 · row 3 · estate globs → the project: head is what MSBuild builds, not what a glob finds
```case
kind:     process
setup:    classic Estate.sqlproj listing Modules/**/*.sql · a stray Scratch/dbo.Temp.sql (CREATE TABLE) not in the
          project · fx_local converged
action:   facsimile up · edit Scratch/dbo.Temp.sql · facsimile up
expect:   copy       fx_local has no dbo.Temp
          stdout     the second up: DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          exit       0 · 0
laws:     T1 D4 D5
source:   expected · ARCH §2 row 3 · the Twin side: twin.json `estate.tables` glob (`Modules/**/*.sql`,
          proving-ground/twin.json)
note:     an SDK-style project includes every .sql under its folder by default, and there the stray file IS head; what
          counts is MSBuild's item list, never a pattern in facsimile.json.
```

### TWN-108 · row 3 · a project that does not build stops with its first error, file named
```case
kind:     process
setup:    Modules/dbo.Customer.sql holds a CREATE TABLE and a second statement in one batch
action:   facsimile prove Dev
expect:   outcome    stop build-failed · "The project does not build: <first error, file named>" · exit 7
laws:     T1 H3 A3
source:   expected · ARCH §6 build-failed · the Twin side: "a repo that does not model refuses with the file named"
          (THE_TWIN §6); SQL71006, one statement per batch (SamplePrTighteningTests' header)
```

### TWN-109 · row 4 · static lanes kept: reference data lands verbatim before planting, and ancestors reuse it
```case
kind:     sql
env:      tw · Status 0 rows in the schema · referenceData ["Data/StaticSeeds.sql"] (the Status MERGE), unchanged by the
          branch
action:   build the base cache · restore · plant presence(OrderLine)
expect:   copy       Status = {1 Open, 2 Closed, 3 Pending} from the merge-base lane, content hash equal to running that lane
                     on an empty Status · the planted Customer.StatusId and Order.StatusId name lane rows · no Status row
                     is planted
          witnesses  presence(OrderLine) (+ancestors Order, Customer; Status reused)
laws:     T1 F1 M2
source:   expected · ARCH §5 steps 1–2 · Twin anchor (receipt): TwinMintLoopTests "K1 + L1: the estate's reference data stands and every relationship resolves"
```

### TWN-150 · row 4 · a branch that edits a lane: proofs see the merge-base rows, `up` sees head's
```case
kind:     process
setup:    tw · the branch edits Data/StaticSeeds.sql: Pending → On Hold · Dev's Status is the merge-base's {1 Open, 2 Closed,
          3 Pending}
action:   facsimile prove Dev · facsimile up
expect:   copy       the proof copy's base holds Pending (the merge-base lane), as Dev does · fx_local holds On Hold (head's lane)
          files      the base-cache key includes the merge-base lanes' hash, never head's (D5)
laws:     T1 F1 D5 T3
source:   expected · ARCH §2 row 4, §5 step 1, §8 (`referenceData`: the merge-base on every proof copy, head for `up`)
note:     A copy seeded from head's lane would already hold the edit, so the change's own MERGE would look silent there
          (TWN-242).
```

### TWN-110 · row 5 · DacFx publish kept, with one posture: the captured effective options
```case
kind:     process
setup:    facsimile/options.publish.xml captured from the Octopus step (BlockOnPossibleDataLoss=True,
          DropObjectsNotInSource=False, GenerateSmartDefaults=False, plus the step's SQLCMD values) · env pg · Customer
          6 rows · Email NULL×2 · head Customer.Email NULL→NOT NULL
action:   facsimile prove Dev --permissive
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     the effective options are recorded, hash equal to the captured set · the Permissive publish's
                     options differ from Strict in BlockOnPossibleDataLoss alone
laws:     T1 F6 H7
source:   expected · F6 · the Twin side: its own `Runs.up` publishes relaxed (BlockOnPossibleDataLoss=false), so every
          SamplePr fact needed a second posture, `SamplePrPublish.strict` (SamplePrSupport.fs)
```

### TWN-111 · row 6 · fingerprint convergence kept: `up` keys every input, and only inputs
```case
kind:     process
setup:    pg · facsimile up converged
action:   one at a time, each followed by `up`: (i) touch a table file without changing it (ii) edit a table file
          (iii) edit a referenceData lane (iv) change up.rowsPerTable (v) change the captured options (vi) bump the tool
          version (vii) delete the stored fingerprint (viii) edit up.sql
expect:   stdout     (i) DONE up · Nothing to apply · .facsimile/out/up.json · exit 0 · (ii)–(viii) a re-converge, then a second
                     `up` prints DONE up · Nothing to apply
laws:     T1 D4 D5
source:   expected · D4 D5 · Twin anchors (receipts): FingerprintTests "the same inputs produce the same fingerprint",
          "contribution order does not matter", "a content edit changes the fingerprint", "seed, scenario, and tool version each change the fingerprint", "a stored fingerprint round-trips for comparison", "a blank stored fingerprint reads as no materialization"; TwinConfigTests "canonical renderings are deterministic"
note:     seed and scenario were Twin inputs and retire with σ (rows 11, 16); up.sql takes the scenario pins' place (row 17);
          (vii) is the "blank stored fingerprint".
```

### TWN-112 · row 7 · clean slate replaced: nothing is wiped, every proof restores the base
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows · Email NULL×2
change:   head Customer.Email NULL→NOT NULL
action:   prove Dev --permissive (its copy keeps the Permissive residue) · prove Dev
expect:   outcome    both: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3, byte-identical apart from time
          copy       each proof database is a fresh fx_<slug>_Dev_<rand> restored from the base backup; IDENTITY counters
                     stand at their declared seed; no DELETE, TRUNCATE or RESEED appears in the step trace
laws:     T1 S5 S2 D1
source:   expected · S5 · the Twin side: the clean slate nulls deferred FKs, wipes child-first and reseeds (THE_TWIN §6);
          its wipe once failed on a non-updatable view (TwinViewLoopTests "A view in the estate: twin up succeeds, base tables mint, the view stays published and data-free")
note:     a restore cannot meet a view or the virgin-vs-deleted RESEED asymmetry. Dev's identity gaps are reproduced only
          where a Rebuild asks (`gapped(T)`).
```

### TWN-113 · row 8 · retired: the ReadSide read-back and the identity ACL; coordinates are Dev's own names
```case
kind:     process
setup:    the full catalog's corpus cases, run once
action:   run every corpus case · search every artifact (delta, answers, witness plan, witness.sql, verdict, stdout,
          stderr) for `SsKey` and `Reconstructed`
expect:   files      zero hits · every coordinate in answers.<env>.json is `schema.table[.column]`, resolved in the
                     environment by OBJECT_ID / COL_LENGTH (P2)
laws:     T1 P2 S3
source:   expected · retirement, structural: the ACL bridged coordinates to the kernel's SsKey; there is no kernel, and the
          planter reads the copy's own catalog (sql/catalog.sql) · Twin anchors (receipts): TwinIdentityTests "law 2: a defined table coordinate binds to its kind", "law 2: a column coordinate binds to its kind and attribute", "the coordinate of a kind round-trips through binding", "containsTable answers the closed-set membership probe",
          "binding is case-insensitive, the collation's semantics"; EvidenceTests "ofProfile rebinds engine evidence to estate coordinates", "toProfile binds a pack against the twin catalog by coordinate"; CoordinateTests "a table coordinate parses from schema.table", "a column coordinate parses from schema.table.column", "coordinate keys are case-insensitive", "coordinate text round-trips through parse"
note:     binding now follows the environment's catalog collation (a case-sensitive catalog tells dbo.customer from
          dbo.Customer), not a fixed case-insensitive rule.
```

### TWN-114 · row 9 · coordinate totality kept: an object Dev lacks stops missing-object, never reads as empty
```case
kind:     sql
env:      pg
action:   facsimile shape Dev dbo.Customer.Nickname
expect:   outcome    stop missing-object · "Dev has no dbo.Customer.Nickname. …" · exit 5
          files      no count recorded for it, zero or otherwise
laws:     T1 S3 H3
source:   expected · S3 · Twin anchors (receipts): TwinIdentityTests "law 2: an unknown table coordinate refuses by name",
          "law 2: an unknown column coordinate refuses by name"; EvidenceTests "law 2: a pack naming an absent column refuses by name"
```

### TWN-115 · row 9 · a malformed or hostile coordinate validates or stops; it is never executed
```case
kind:     sql
env:      pg
action:   facsimile shape Dev with each of: dbo.Order.Total · Customer.Email · dbo.Customer.Email.x ·
          "dbo.Customer.[Email];DROP TABLE x;--"
expect:   outcome    dbo.Order.Total: done shape · … · exit 0 (bracket-quoted on the way out: [dbo].[Order]) ·
                     Customer.Email and dbo.Customer.Email.x: stop usage · "Unknown verb or argument <coordinate>. Usage:
                     facsimile shape <env> <schema.table.column> [--vocabulary]" · exit 1 · the hostile one: stop
                     missing-object (it is validated only as a literal inside COL_LENGTH, and names nothing) · exit 5
          files      every probe sent is a template instance (P2); nothing else reaches Dev
laws:     T1 P2 S3 A3
source:   expected · ARCH §6 (usage, missing-object); INVARIANTS P2 · Twin anchors (receipts): CoordinateTests "a one-segment table coordinate refuses as malformed",
          "a three-segment table coordinate refuses as malformed", "a two-segment column coordinate refuses as malformed", "a bracketed segment refuses as unsupported"
note:     the Twin refused bracketed segments outright; the facsimile must accept reserved words like Order by quoting
          them. A malformed argument is the agent's to fix (exit 1); a well-formed name that resolves to nothing is S3's.
```

### TWN-116 · row 10 · views kept out: never planted, still published, still identical
```case
kind:     corpus
tags:     edge
env:      tw + view dbo.vCustomerOrders (Order ⋈ Customer) · Customer 25 rows
change:   head Customer + Nickname nvarchar(40) NOT NULL, no default
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer) · none in dbo.vCustomerOrders
          copy       the identity plan is empty, the view included (F2)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     T1 F2 F3 M2
source:   expected · ARCH §2 row 10 · Twin anchor (receipt): TwinViewLoopTests "A view in the estate: twin up succeeds, base tables mint, the view stays published and data-free"
note:     no question is ever asked of a view and the planter never sends DML to one.
```

### TWN-117 · row 11 · retired: σ, the distribution-faithful mint, decides no verdict
```case
kind:     corpus
env:      tw · each of the 47 SamplePr fixtures, as TWN-201…TWN-247 state them
change:   each fixture's own change
action:   prove each with the Twin (σ-minted, 25 rows per table) and with the facsimile (per populated touched table, the
          maximum of its needs: one row by default, two for dups, nulls or a gapped identity)
expect:   outcome    the same verdict class, Msg and object, fixture by fixture; section "Golden facts (T2)" is this
                     case's evidence
laws:     T1 T2 F1 F3 M2
source:   expected · retirement, structural: every question DacFx asks is existence-shaped (the guard is IF EXISTS;
          validation stops at the first violator: F1, F7) and volume never flipped a verdict (F20), so only booleans
          count, and the facsimile measures them in the environment (F1)
note:     the risk ran the other way: a violator rarer than 1/volume can vanish from a mint (TWIN_SUCCESSION §1).
```

### TWN-118 · row 12 · retired: the schema-derived floor; `up` data is plain, valid and fitted to width
```case
kind:     process
setup:    pg
action:   facsimile up
expect:   copy       ordinal literals throughout: strings carry the `w` marker where the width allows it; other values come
                     from the planter's fixed per-class table (P5); no value depends on a column's name (no email, amount
                     or status routing); every value fits its declared width; every NOT NULL is filled;
                     k = up.rowsPerTable rows per table, then up.sql's declared rows
laws:     T1 D3 P5 D1
source:   expected · retirement, structural: no publish verdict reads a value's plausibility, only its class (null,
          misfit, duplicate, orphan) · Twin anchors (receipts): DerivedEvidenceTests "name-routing: email, amount, status, and chronological columns get their shapes", "length-safety: derived values fit the declared width, and a collapsing width derives nothing", "nullability: NOT NULL derives zero nulls; nullable derives a modest rate", "preserve-threshold: every derived vocabulary stays at or under 50 distinct values",
          "surrogates-skipped: a primary key derives nothing", "floor-binds: the derived pack binds back against its own catalog", "captured-wins: the floor yields wherever captured evidence speaks and fills where it does not",
          "determinism: the same catalog derives the same pack"
note:     the floor's one verdict-adjacent service (non-negative numerics satisfy a `>= 0` CHECK) passes to the planter;
          where a literal fails a CHECK the planter stops planter-needs-row, and the row is declared in up.sql (TWN-253).
```

### TWN-119 · row 13 · inverted trust gate: the Twin's forced trust passes a change the environment blocks
```case
kind:     corpus
tags:     edge
env:      tw · FK_OrderLine_Order enabled but untrusted (added WITH NOCHECK) · OrderLine 25 rows · 1 orphan
          (an OrderId with no Order)
change:   head FK_OrderLine_Order gains ON DELETE CASCADE (DacFx: DROP, WITH NOCHECK ADD, WITH CHECK CHECK)
expect:   answers    Trust(OrderLine,FK_OrderLine_Order)=untrusted · Orphan(OrderLine.(OrderId)→Order)=violating
          questions  Orphan(OrderLine.(OrderId)→Order)
          witnesses  orphan(OrderLine.(OrderId)→Order), which also answers presence of OrderLine ·
                     trust(OrderLine, FK_OrderLine_Order, untrusted)
          outcome    verdict blocked constraint · Msg 547 · FK_OrderLine_Order · exit 3
          record     tier T2 · residue {main: FK_OrderLine_Order re-added with CASCADE; validation: failed, untrusted} ·
                     ships reconcile the orphan first (skills/op/create-fk-orphan)
laws:     T1 F9 F1 F3 H6
source:   verify · ARCH §11.1 (does Extract keep the WITH NOCHECK state?) · the Twin arm's wrong clean, by receipt:
          TwinMintLoopTests "trust gate: a violating mint refuses by constraint name; a satisfiable check ends trusted"
          (every constraint forced trusted, every child drawn from a real parent) and SamplePr "change-delete-rule: an FK ON DELETE NO ACTION -> CASCADE is a clean metadata DROP+ADD" (clean on that copy)
note:     the Twin's copy cannot hold this state, so it reports clean where the environment blocks with 547. The planter
          plants the orphan inside its suspend-and-restore envelope and restores the FK untrusted, like Dev (ARCH §5 step 4).
```

### TWN-120 · row 13 · a disabled FK, a system-named default and a column collation survive onto the copy
```case
kind:     corpus
tags:     edge
env:      tw · FK_Order_Customer disabled (NOCHECK CONSTRAINT) · Order 25 rows · 2 orphans · Customer.Email collation
          Latin1_General_CS_AS · Order.Channel default system-named (DF__Order__Channe__<hex>)
change:   head Order + Memo nvarchar(100) NULL
expect:   answers    Trust(Order,FK_Order_Customer)=disabled · Collation(Customer.Email)=Latin1_General_CS_AS
          copy       delta(head → copy) = delta(head → Dev), statement for statement (F9's property)
          outcome    = the oracle's (F3)
laws:     T1 F9 F2 F3
source:   verify · ARCH §11.1 · the Twin side: it publishes the repository's schema and re-validates every check and FK
          (THE_TWIN §9), so none of these states can exist on its copy; red-team #1 (a disabled FK changes the delta)
```

### TWN-121 · row 14 · retired: evidence tiers; the answers are counts and nothing else
```case
kind:     process
setup:    pg stand-in env · Customer 6 rows · Email NULL×2 · head Customer.Email NULL→NOT NULL
action:   facsimile read Dev · prove Dev · then, as a property: set every count n > 0 in answers.Dev.json to 1,
          re-stamp, prove Dev
expect:   files      answers.Dev.json leaves are integers, booleans, identifiers, hashes, timestamps; no percentile,
                     frequency, null rate or value
          outcome    both proofs: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     T1 P1 F1
source:   expected · retirement, structural: every question is a boolean over existence (ARCH §0) · Twin anchors
          (receipts): EvidenceTests "the codec round-trips a full pack", "layer: the richer profile replaces per attribute and unions the rest", "evidencedKinds names exactly the kinds carrying column evidence"; TwinEvidenceLoopTests
          "M3: import → derive → mint re-profiles to the source's shape"
note:     Rows(T) still feeds the scale flag: a flag, not the verdict (TWN-132).
```

### TWN-122 · row 15 · retired: corrections, Faker and classify; no real value reaches the copy
```case
kind:     corpus
env:      pg · Customer 6 rows · Email NULL×2 · one Email canary-7f3a@corp.example · one Name "Zelda Q. Canary"
change:   head Customer.Email NULL→NOT NULL
expect:   files      neither canary appears in the delta, answers, witness plan, verdict, stdout or stderr (P1)
          copy       neither canary appears in any table of the copy (P5)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     T1 P1 P5
source:   expected · retirement, structural: masking hides real values, and P1 lets none leave · the Twin side: three
          masking planes (THE_TWIN §5); TwinEvidenceLoopTests "M3: import → derive → mint re-profiles to the source's shape" (source emails never re-emitted)
```

### TWN-123 · row 16 · retired: scenarios (rows, weights, between, perParent) move the copy away from the env
```case
kind:     corpus
env:      tw · the make-mandatory (TWN-201 (a)) and add-unique (TWN-204 (a)) fixtures
change:   as TWN-201 and TWN-204
action:   prove each fixture with the facsimile, and with the Twin under scenario `default` and under `skewed` (Order 40
          rows · Channel weights Web 8 / Store 2 · PlacedOn between 2026-01-01 and 2026-03-31, late · OrderLine
          perParent 2.0)
expect:   outcome    one facsimile verdict per fixture (Dev's) · the Twin's add-unique verdict can differ between its two
                     scenarios (weighting Channel to two values forces duplicates): the scenario, not Dev, decided it
laws:     T1 F1 F3
source:   expected · retirement, structural: the facsimile's booleans are measured in the environment, so there is no
          overlay to apply · Twin anchors (receipts): TwinMintLoopTests "M4: a scenario shapes the mint — weights, window, ratio, and pins land"; ScenarioCompilerTests "weights compile to a preserved categorical rewrite",
          "weights on a non-text column refuse by name", "a date window compiles to ordered tick percentiles; skews order correctly", "a window on a text column refuses; an inverted window refuses", "a numeric window compiles for a decimal column", "perParent derives the child volume from the parent's resolved rows", "perParent falls back to the parent's default volume", "perParent naming an unrelated parent refuses", "rows and perParent on one table refuse as a conflict", "the chain merges base-first with the nearer scenario winning per field";
          TwinConfigTests "a scenario parses with tables, columns, perParent, and pins", "a column override with both weights and between refuses", "skew without between refuses", "extends naming an undefined scenario refuses",
          "an extends cycle refuses", "resolveScenario: the baseline is optional, a named absence refuses", "an inherited scenario edit changes the descendant's canonical mint", "scenarioChain walks base-first"
```

### TWN-124 · row 17 · scenario pins kept as `witness.sql`: a declared row lands verbatim and must agree with Dev
```case
kind:     corpus
env:      tw · Customer 25 rows · Email all distinct
change:   head + CREATE UNIQUE INDEX UX_Customer_Email ON Customer(Email) · witness.sql (a): `-- plants: dbo.Customer` then
          Customer Id 1000 (IDENTITY_INSERT) Name N'wCanonical Test Customer', Email N'wcanon@example.invalid', StatusId 1
          · `-- plants: dbo.[Order]` then an Order for CustomerId 1000 · (b): (a) plus a second Customer with Email
          N'wcanon@example.invalid'
expect:   answers    Unique(Customer,(Email))={}
          copy       (a) Customer 1000 byte-identical to the declared literals; its Order's FK resolves; the blocks ran in FK
                     order (Customer, then Order)
          outcome    (a) verdict clean · exit 0 · (b) stop acceptance · "The copy answers Unique(Customer,(Email)) yes, but Dev answered no. Fix witness.sql." · exit 5
laws:     T1 F1 A5
source:   expected · ARCH §5 step 5 · Twin anchors (receipts): ScenarioCompilerTests "a valid pin renders per attribute type and exposes its pool key", "applyPins: the pin displaces a colliding minted row and lands first", "a malformed pin value refuses with the expected shape"; TwinMintLoopTests "M4: a scenario shapes the mint — weights, window, ratio, and pins land" (pin Id 1000 lands verbatim and referenceable)
note:     stricter than the Twin: a pin may not create a state Dev does not have (F1). StatusId 1 is a reference-data key,
          repository-defined (P5). `up` takes its pins from up.sql (TWN-317).
```

### TWN-125 · row 17 · a declared row that cannot apply stops with its line named
```case
kind:     sql
env:      tw · planted copy · witness.sql, one variant at a time, each under `-- plants: dbo.Customer`:
          (a) INSERT dbo.Customer (Name, Nickname) VALUES (N'wX', N'wY');   -- no such column
          (b) INSERT dbo.Customer (Name) VALUES (N'wX');                    -- Email NOT NULL
          (c) ALTER TABLE dbo.Customer ADD X int;
action:   apply witness.sql
expect:   outcome    (a) stop witness-failed · "witness.sql line 2 failed with Msg 207. Fix that row." · exit 5 · (b) stop
                     witness-failed · "witness.sql line 2 failed with Msg 515. Fix that row." · exit 5 · (c) stop identity ·
                     "witness.sql changed the schema (dbo.Customer.X). Witness files are DML only." · exit 5
laws:     T1 F2 H3 A5
source:   expected · ARCH §5 step 5, §6 (witness-failed, identity) · Twin anchor (receipt): ScenarioCompilerTests "pin refusals: unknown column, missing key, missing mandatory, null on mandatory"
```

### TWN-126 · row 18 · `twin check` replaced: its proofs, each with a successor inside every verdict
```case
kind:     corpus
env:      pg · Customer 6 rows · Email NULL×2
change:   head Customer.Email NULL→NOT NULL
action:   prove Dev · prove Dev
expect:   record     acceptance ok (re-count = answers, every "no" included) ↔ check's zero orphans · identity plan empty
                     ↔ TablesDefined = TablesLive · the second run byte-identical apart from time ↔ DeterministicRemint ·
                     hashes {dbo.Customer before = after} ↔ the Twin's value-preservation digests, kept
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
laws:     T1 F1 F2 D1 H2
source:   expected · ARCH §2 row 18, §5 steps 6 and 9 · Twin anchor (receipt): TwinMintLoopTests "M5: the proof verb holds on a throwaway database"
note:     π∘σ≈id retires with σ (row 11): nothing is synthesized, so nothing is re-profiled. "Zero orphans" becomes
          "orphans exactly where Dev has them" (F1).
```

### TWN-151 · row 18 · the value-preservation digest kept: a silent trailing-space trim shows only in the hashes
```case
kind:     corpus
env:      pg · Product 5 rows · one Code 'A100' + 12 trailing spaces · no Code over 10 by LEN
change:   head Product.Code nvarchar(50)→nvarchar(10)
action:   prove Dev --permissive
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={overbytes}
          witnesses  misfit(Product.Code, overbytes), which also answers HasRows(Product)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     Permissive: published with no error · hashes {dbo.Product before ≠ after}: the trim is on the record,
                     though no message reports it
laws:     T1 F1 F3 F6
source:   expected · ARCH §2 row 18, §5 step 10 (Permissive records the hashes before and after); red-team #10 (a narrowing
          silently trims trailing spaces ✓); TWIN_SUCCESSION §5 (a lossy result shows only in the hash)
note:     The Twin's widen and retype facts asserted value preservation by digest. The facsimile hashes every touched table
          before and after, so a change that alters values without an error is still seen.
```

### TWN-127 · row 19 · S-stable kept: one column's edit perturbs only that column's planted values
```case
kind:     sql
env:      (a) pg Customer · (b) pg Customer + MiddleName nvarchar(50) NULL after Name · (c) pg Customer without Region ·
          (d) pg Customer with its columns reordered
action:   plant k = 3 rows of Customer, as `up` does, in each
expect:   copy       every column other than the edited one is byte-identical across (a)–(d)
laws:     T1 D3
source:   expected · D3 · the kernel's σ anchors the Twin stood on (Projection.Tests/SyntheticDataTests.fs, not a Twin.Tests
          pool): "S-stable: inserting a column mid-table perturbs only that column — every other column byte-identical",
          "S-stable: removing a column leaves every surviving column byte-identical", "S-stable: reordering a kind's
          columns is byte-identical to the original"
```

### TWN-128 · row 20 · T1 determinism kept: same inputs, byte-identical artifacts apart from the timestamp
```case
kind:     corpus
env:      pg · Customer 6 rows · Email NULL×2 · OrderLine 4 rows
change:   head Customer.Email NULL→NOT NULL
action:   prove Dev on two machines from one commit
expect:   outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3, on both machines
          files      witness plan, planted-row content hash, delta.Dev.sql and verdict.Dev.json identical but for `time`
laws:     T1 D1 H7
source:   expected · D1 · Twin anchors (receipts): TwinMintLoopTests "T1: the same seed mints the same rows; the mint is reproducible"; FingerprintTests "the same inputs produce the same fingerprint"
```

### TWN-129 · row 21 · retired: `twin bake` and `bake.mjs`; nothing is distributed, caches cost only time
```case
kind:     process
setup:    a corpus fixture proven once (caches warm)
action:   facsimile down --all · facsimile prove Dev
expect:   outcome    the identical verdict (D2)
          files      .facsimile/ holds only content-keyed caches, none committed · no .bacpac or image is produced or
                     consumed (no sqlpackage Export or Import in any verb)
laws:     T1 D2 D5
source:   expected · retirement, structural: bake shipped a pre-seeded BEFORE so a machine without the Twin started from
          the same state; the facsimile re-derives its BEFORE from Dev's schema and answers on every run
```

### TWN-130 · row 22 · the verbs kept by meaning: every Twin verb has a successor and a case
```case
kind:     process
setup:    this catalog · the verb list of THE_TWIN §7
action:   tests/run-cases.ps1 --lint
expect:   files      a T3 case exists for each verb of THE_TWIN §7: up → TWN-301, TWN-302, TWN-317 · seed → TWN-303 ·
                     status → TWN-304 · down → TWN-305 · reset → TWN-306 · init → TWN-307 · check → TWN-308 · bake →
                     TWN-309 · evidence → TWN-310 · classify → TWN-311 · removing any one fails the lint
laws:     T1 T3
source:   expected · T1 T3
```

### TWN-131 · row 23 · retired: the scale lane; what the team loses, stated
```case
kind:     manual
setup:    a lead who wrote added-scrutiny lines from estate/scale-datapoints.md
action:   write the scrutiny line for an index build on a 1.05M-row table
expect:   the facsimile measures no wall time. The frozen numbers stay readable at the archive tag: no-op floor 3.8–8.8 s;
          a nonclustered index over 1.05M rows 15.4 s, about 11 s above the floor and roughly linear; CHECK and FK
          re-validation invisible at 1M; the Msg 50000 refusal O(1) at both tiers. They cannot be re-measured without a
          full-size non-prod server (TWIN_SUCCESSION §6b zone 3), which is out of scope. The record carries the `scale`
          flag and the `volume` residual instead.
laws:     T1 H4
source:   receipt · F20 (the measurements; estate/scale-datapoints.md, 2026-08-28)
```

### TWN-132 · row 23 · the replacement: the scale flag from row counts, at the 1M line
```case
kind:     pure
action:   Get-FxTier
input:    questions HasRows(OrderLine) · NotNull(OrderLine.Note)
          answers (a) Rows(OrderLine)=1050000 · HasRows(OrderLine)=yes · NotNull(OrderLine.Note)=clean
                  (b) the same with Rows(OrderLine)=999999
expect:   record     (a) tier T2 · flags {scale} · notChecked ∋ volume
                     · (b) tier T2 · flags {} · notChecked still ∋ volume (H4)
laws:     T1 H4
source:   expected · ARCH §7
note:     Get-FxTier also takes the measures (ARCH §3), so a plain index build, a Scan that asks no question, raises the
          flag too (TWN-209).
```

### TWN-133 · row 23 · volume never flipped a verdict on the scale lane, and the corpus keeps that fact
```case
kind:     corpus
tags:     edge
env:      pg at the scale1m volumes (Customer 30,000 · Order 100,000 · OrderLine 1,050,000 · Product 2,000), and the same
          schema at 6 rows per table
change:   one at a time, all on OrderLine: add-check over clean data · a nonclustered index · add-fk OrderLine→Order over
          a clean child · drop a populated column
expect:   outcome    at both volumes, facsimile = oracle: verdict clean · verdict clean · verdict clean · verdict blocked
                     guard · Msg 50000/127
          record     at scale1m, flags {scale} on the first three (a validation or a Scan on the 1.05M-row OrderLine); the
                     guard-only drop reads one row and is not scale (TIER-15)
laws:     T1 F3 H4
source:   expected · the Twin side, by receipt: F20 (the same four outcomes at 181k and 1.18M rows)
note:     nightly only: the oracle copy is a 1M-row restore.
```

### TWN-134 · row 24 · the nightly proof lane kept as the corpus, nightly and on every PR
```case
kind:     process
setup:    .github/workflows/facsimile-corpus.yml
action:   read the workflow's triggers and job · open a PR touching cases/ · wait for the nightly run
expect:   files      triggers: a nightly schedule, workflow_dispatch, and pull_request on facsimile/**, cases/**, the
                     vocabulary, sample-prs/**, proving-ground/** · it runs every corpus case, TWN-201…TWN-255 included ·
                     a facsimile/oracle disagreement fails the job (F3)
laws:     T1 T2 F3 E1
source:   expected · ARCH §2 rows 24, 28 · the Twin side: .github/workflows/ssdt-agent-proof-lane.yml
          (`SamplePr|TwinViewLoopTests`, cron "17 6 * * *")
note:     that workflow names "the 41 SamplePr facts"; there are 47, plus 8 loop facts, and all 55 are carried here.
```

### TWN-135 · row 25 · the talk-to-local-sql ladder → `doctor`
```case
kind:     process
setup:    (a) LocalDB 2022 · (b) no LocalDB; facsimile.json `localdb` names a reachable SQL Server 2022 (CI, sandbox) ·
          (c) LocalDB 2019 (v15) and a Sql160 project
action:   facsimile doctor
expect:   outcome    (a) done doctor · LocalDB 16 ≥ Sql160 · exit 0 · (b) done doctor · naming the configured server · exit 0 ·
                     (c) stop platform · "LocalDB 15 is older than the project's target Sql160. Install LocalDB 2022 or later, or set allowOlderLocalDb (stamped on every record)." · exit 6
laws:     T1 S4 A3 H3
source:   expected · ARCH §6, S4 · the Twin side: skills/talk-to-local-sql/SKILL.md (twin.json and the twin CLI → the
          Twin; else warm-sql.sh → the warm container; else stop), encoded in `prove.mjs detect`
note:     doctor never probes docker, `twin`, `warm-sql.sh`, or ports 21434 and 11433.
```

### TWN-136 · row 26 · `prove.mjs` absorbed: its classifier fixtures, re-asserted on Get-FxOutcome
```case
kind:     pure
action:   Get-FxOutcome
input:    (a) prove.mjs FIXTURE_BLOCKED ("Msg 50000, Level 16, State 127 … Rows were detected", then SQL72045) with
          FIXTURE_DELTA, whose first batch is the guard on [dbo].[Customer] · (b) FIXTURE_CLEAN ("Successfully published database.") · (c) "something else entirely"
expect:   outcome    (a) verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3 · (b) verdict clean · exit 0 ·
                     (c) stop unclassified · "The publish ended with <message> that no rule classifies. …" · exit 9
laws:     T1 H5 H2 A3
source:   expected · prove.mjs selftest 'blocked fixture classifies blocked', 'blocked fixture carries the Msg line',
          'clean fixture classifies published', 'garbage classifies indeterminate'
note:     prove.mjs's 'connection failure classifies unreachable' has no Get-FxOutcome counterpart: the copy is local, so a
          lost LocalDB is doctor's toolchain stop, and exit 4 is kept for environments.
```

### TWN-137 · row 26 · `prove.mjs`'s delta signals become questions
```case
kind:     pure
action:   Get-FxQuestions
input:    prove.mjs FIXTURE_DELTA (the guard on [dbo].[Customer] · ALTER COLUMN [Email] NVARCHAR (256) NOT NULL ·
          sp_rename [dbo].[Order].[StatusText] → StatusName)
expect:   questions  HasRows(Customer) · NotNull(Customer.Email) · the sp_rename asks nothing · no DROP TABLE inferred
laws:     T1 F4
source:   expected · prove.mjs selftest 'delta signals: data-loss guard', 'delta signals: sp_rename',
          'delta signals: no false DROP TABLE'
note:     prove.mjs's 'shadow-table rebuild (tmp_ms_xx)' signal becomes the Rebuild question; its DROP COLUMN and
          DROP TABLE signals become HasRows through their guards.
```

### TWN-138 · row 27 · `inflight-check.mjs` ported: a delta touching a held table stops in-flight
```case
kind:     process
setup:    estate/in-flight.md open row `CHG-2 | split Order | dbo.[Order] dbo.OrderLine | 2 | 3 | …` · head adds
          OrderLine.Memo NULL, so delta.Dev.sql touches dbo.OrderLine · the branch does not edit the ledger
action:   facsimile prove Dev
expect:   outcome    stop in-flight · "dbo.OrderLine is in an open two-release window (estate/in-flight.md). Hold this change, or make it the window's next release." · exit 5
laws:     T1 H3 A3
source:   expected · ARCH §2 row 27, §6 in-flight · the Twin-era check: inflight-check.mjs selftest 'collision found on held
          table', 'bare name matches any schema', 'row tables parse with brackets', 'no collision on free table' (it exits
          5 too); F17 is the hazard it holds
note:     matching stays bracket- and case-insensitive, and a bare ledger name matches any schema. The tables come from
          each environment's delta, not from the changed files, so drift that puts a held table into one environment's
          delta stops there too (ENV-34).
```

### TWN-139 · row 27 · the phase-advance exemption, the fenced example and the unreadable ledger carry over
```case
kind:     process
setup:    (a) as TWN-138, and the branch also edits estate/in-flight.md · (b) a ledger whose only row is the fenced example ·
          (c) facsimile.json `inFlight` names a ledger that does not exist or cannot be read
action:   facsimile prove Dev
expect:   outcome    (a) no stop: the verdict carries flags {in-flight} and warnings {in-flight: the PR edits the ledger}
                     (inflight-check: WARNING, exit 0) · (b) no stop: a fenced row never parses as live state · (c) stop
                     config · "facsimile.json: $.inFlight: estate/in-flight.md cannot be read." · exit 6 (inflight-check:
                     exit 6)
laws:     T1 T3 E4
source:   expected · ARCH §2 row 27 (the exemption warns; an unreadable ledger is stop config) · inflight-check.mjs (the
          `ledgerEdited` exemption; fenced rows stripped before parsing; a missing ledger exits 6)
```

### TWN-140 · row 28 · `ssdt-agent-proof-lane.yml` → `facsimile-corpus.yml`, with no gap between them
```case
kind:     process
setup:    .github/workflows/ssdt-agent-proof-lane.yml and .github/workflows/facsimile-corpus.yml, G2 through G5
action:   run both lanes at each gate · remove the proof lane at G5
expect:   files      facsimile-corpus.yml runs on a Windows runner (LocalDB; no .NET SDK, no Docker) and on SQL Server
                     2022 (G2) · the proof lane is deleted only after TWN-201…TWN-255 are green (G3) and the G4 window
                     has closed; until then both run
laws:     T1 T2 T4 E2
source:   expected · ARCH §10 G2–G5
```

### TWN-141 · row 29 · retired: `ssdt-agent-bake.yml` has no consumer left
```case
kind:     process
setup:    the repository at G5
action:   search workflows, pipelines and skills; run the corpus without the bake workflow
expect:   files      after G5, no workflow, pipeline or skill references bake.mjs, the `proving-substrate` artifact or
                     bake.manifest.json · no corpus case reads a .bacpac · deleting the workflow changes no verdict
laws:     T1 D2
source:   expected · ARCH §2 row 29 · TWIN_SUCCESSION §7 (bake.mjs leaves the bundle)
```

### TWN-142 · row 30 · the PR pipeline proved into an empty database; the facsimile proves the committed proofs
```case
kind:     process
setup:    an ADO PR making Customer.Email NOT NULL · committed proofs/<slug>/: schema.Dev.dacpac, delta.Dev.sql,
          delta.pr.sql, answers.Dev.json (HasRows(Customer)=yes · NotNull(Customer.Email)=violating)
action:   (old) pipelines/ssdt-agent-pr-validation.yml: bake restore into <base>_<fp>, then
          `prove.mjs verdict --db $(Build.BuildId)_pr` · (new) `facsimile prove Dev` on the agent
expect:   outcome    (old) published clean, exit 0: a false clean, because the publish targets a new, empty database
                     · (new) verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3, the copy built from the committed
                     schema.Dev.dacpac; the check fails
laws:     T1 E3 F1 H2
source:   expected · TWIN_SUCCESSION §8 ("The inverted-verdict template must not be instantiated as written"); ARCH §8
          (schema.<env>.dacpac committed so CI reproduces the proof)
```

### TWN-143 · row 30 · missing or stale proofs refuse; a tooling red has a logged break glass
```case
kind:     process
setup:    (a) a schema PR with no proofs/ · (b) answers keyed to a delta older than the last schema edit · (c) a run that
          exits 6 on the agent · (d) a CI verdict that differs from the author's committed verdict
action:   the PR pipeline
expect:   outcome    (a) fails: a schema PR without proofs (E3) · (b) stop stale-answers · "answers.Dev.json was measured for a different delta or head. Re-run facsimile read Dev." · exit 5 · (c) red; a lead may merge
                     with notChecked ∋ tooling, and the override is logged · (d) reported, not failed
laws:     T1 E3 H3 H4
source:   expected · TWIN_SUCCESSION §8 (PR pipeline steps 1–5; break glass)
```

### TWN-144 · row 31 · `twin-test.sh` and `twin-eject-dryrun.sh` → the runner
```case
kind:     process
setup:    this catalog · tests/run-cases.ps1
action:   tests/run-cases.ps1 (a) kind pure, on a machine with no SQL Server · (b) one case by ID · (c) the full catalog
          on Windows with LocalDB · (d) the vendored facsimile/ folder, from a scratch repository that has never seen
          this monorepo
expect:   outcome    (a) every pure case runs; no connection is attempted (twin-test.sh `fast`) · (b) only that case
                     runs (`focus <pattern>`) · (c) sql, corpus and process cases run; manual cases are listed, not run ·
                     (d) the same results (the eject dry-run's "the binary travels")
laws:     T1 A2 M4
source:   expected · ARCH §3 · cases/README §3
```

### TWN-145 · row 32 · retired: evidence-collision refusal; one environment, one answers file, never a union
```case
kind:     process
setup:    facsimile.json environments Dev and QA naming the same server and database
action:   facsimile read Dev · facsimile read QA · facsimile prove Dev QA
expect:   files      answers.Dev.json and answers.QA.json · equal delta hashes and answers content hashes → one shared proof,
                     cited by both verdicts (F7) · there is no `sources` key, so no table can be claimed twice
laws:     T1 F7
source:   expected · retirement, structural: law 4 guarded the union of evidence packs, and F7 forbids any union · Twin
          anchors (receipts): TwinConfigTests "law 4: a table claimed by two sources refuses naming both", "an unknown rendition refuses with the expected tokens"; EvidenceTests "merge refuses a table claimed by two packs"
```

### TWN-146 · row 33 · shape-tier literal freedom kept and strengthened: answers carry no value; shape returns structure
```case
kind:     sql
env:      pg · Customer 60 rows · Email 60 distinct, including secret7@source.example · Name Alpha×45, Beta×15 ·
          Status.Code 4 values
action:   facsimile read Dev · shape Dev dbo.Customer.Email · shape Dev dbo.Customer.Name ·
          shape Dev dbo.Status.Code --vocabulary
expect:   files      answers.Dev.json validates against the integers/booleans/identifiers/hashes/timestamps schema ·
                     `secret`, `Alpha` and `Beta` appear in no artifact · Email: signatures, lengths, counts, distinct as
                     collated vs binary, no literal, signatures seen fewer than 5 times folded into "other: n rows in m
                     signatures" · Name without --vocabulary: no literal · Status.Code with --vocabulary: its 4 values
laws:     T1 P1 P6
source:   expected · P1 P6 · Twin anchors (receipts): EvidenceTests "law 3: the shape tier carries no captured literal";
          TwinEvidenceLoopTests "M3: import → derive → mint re-profiles to the source's shape" (the derived shape pack
          contains neither "Alpha" nor "secret")
note:     strengthened: there is no rich tier at all, inside the repository or out of it.
```

### TWN-147 · row 34 · retired: the `[twin]` state schema; no state object in any database
```case
kind:     process
setup:    pg
action:   facsimile up · script head against fx_local
expect:   copy       the plan is empty, and fx_local holds no schema, table or extended property the project does not
                     define
          files      `up`'s fingerprint lives under .facsimile/
laws:     T1 F2 D2
source:   expected · retirement, structural: law 5 excluded the Twin's own write from comparison; with no write there is
          nothing to exclude
```

### TWN-148 · row 35 · convergence kept: `up` twice applies nothing; a reference-data edit re-converges
```case
kind:     process
setup:    pg · fx_local converged
action:   facsimile up · edit ReferenceData/Status.sql (one row added) · facsimile up · facsimile up
expect:   stdout     DONE up · Nothing to apply · .facsimile/out/up.json · exit 0 · a re-converge · DONE up · Nothing to apply
          copy       Status holds the added row (head's lane, as `up` takes it)
laws:     T1 D4 T3
source:   expected · D4 · Twin anchor (receipt): TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an estate edit" (its schema-edit leg is TWN-248)
```

### TWN-149 · row 36 · retired: the eject seam; nothing to peel
```case
kind:     process
setup:    the vendored facsimile/ folder in the SSDT repository
action:   lint the vendored facsimile/ folder
expect:   files      no reference to sidecar/projection, Projection.*, Twin.*, *.fs, *.mjs, docker, warm-sql or 11433 ·
                     the only executables invoked are sqlpackage, SqlLocalDB, MSBuild via vswhere, and sqlcmd (ARCH §3)
laws:     T1 M4 A2
source:   expected · retirement, structural: the seam let the Twin leave a 114K-line kernel, and the facsimile has no
          kernel · Twin anchors (receipts): BoundaryTests "boundary: Twin.Core references Projection.Core and nothing else local", "boundary: Twin.Runtime stays inside the kernel manifest", "boundary: no Projection assembly references Twin"; TWIN_SUCCESSION §7 (the 20-line check)
```

## Golden facts (T2)

### TWN-201 · row 24 · make-mandatory · OrderLine.Note blocks populated, applies empty
```case
kind:     corpus
tags:     clean · empty · violating:null
env:      tw · (a) OrderLine 25 rows · Note NULL×0 · (b) OrderLine 0 rows · (c) OrderLine 25 rows · Note NULL×3
change:   head OrderLine.Note NULL→NOT NULL · scripts unchanged
action:   prove Dev (a, b) · prove Dev --permissive (c)
expect:   answers    (a) HasRows(OrderLine)=yes · NotNull(OrderLine.Note)=clean · (b) HasRows(OrderLine)=no
                     · (c) HasRows(OrderLine)=yes · NotNull(OrderLine.Note)=violating
          questions  HasRows(OrderLine) · NotNull(OrderLine.Note)
          witnesses  (a) presence(OrderLine) (+ancestors Order, Customer) · (b) none · (c) null(OrderLine.Note), which also
                     answers HasRows(OrderLine) (+ancestors Order, Customer)
          outcome    (a), (c) verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3 · (b) verdict clean · exit 0
          copy       (c) Permissive: Msg 515 on dbo.OrderLine.Note; OrderLine hash unchanged
          record     tier T2 · ships two releases (F4, F7) · residue {}
laws:     T2 F1 F3 F6 H5
source:   receipt · SamplePr "make OrderLine.Note mandatory: production publish blocks a populated table, applies when empty"
note:     the Twin asserts the strict block carries no Msg 515: zero NULLs, still blocked. Its relaxed `Runs.up` leg
          (BlockOnPossibleDataLoss off, Msg 515 on 3 NULLs) is the facsimile's --permissive.
```

### TWN-202 · row 24 · add-mandatory · a new NOT NULL column with no default blocks populated, applies empty
```case
kind:     corpus
tags:     clean · empty
env:      tw · (a) OrderLine 25 rows · (b) OrderLine 0 rows
change:   head OrderLine + Amount decimal(18,2) NOT NULL, no default
expect:   answers    (a) HasRows(OrderLine)=yes · (b) HasRows(OrderLine)=no
          questions  HasRows(OrderLine)
          witnesses  (a) presence(OrderLine) (+ancestors Order, Customer) · (b) none
          outcome    (a) verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3 · (b) verdict clean · exit 0
          record     tier T1 · warning SQL72015 recorded · ships with a DEFAULT, or backfill then tighten
                     (skills/op/add-mandatory)
laws:     T2 F1 F3 H5
source:   receipt · SamplePr "add-mandatory: a new NOT NULL column with no default blocks a populated table, applies when empty"
note:     the Twin asserts both the SQL72015 warning (no default, no NULLs allowed) and the Msg 50000 guard; the guard is
          what blocks.
```

### TWN-203 · row 24 · narrow · over-length data behind the row-presence guard
```case
kind:     corpus
tags:     violating:overlen · empty
env:      tw · (a) Customer 25 rows · one Email longer than n · (b) Customer, Order, OrderLine 0 rows · (c) as (a)
change:   head Customer.Email nvarchar(250)→nvarchar(n), n below the longest Email; NOT NULL kept
action:   prove Dev (a, b) · prove Dev --permissive (c)
expect:   answers    (a) HasRows(Customer)=yes · Fits(Customer.Email→nvarchar(n))={overlen} · (b) HasRows(Customer)=no
          questions  HasRows(Customer) · Fits(Customer.Email→nvarchar(n))
          witnesses  (a) misfit(Customer.Email, overlen), which also answers HasRows(Customer) · (b) none
          outcome    (a), (c) verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3 · (b) verdict clean · exit 0
          copy       (c) Permissive: Msg 2628 on dbo.Customer.Email; Customer hash unchanged
          record     tier T2 · ships two releases (F1, F4)
laws:     T2 F1 F3 H5
source:   receipt · SamplePr "narrow: over-length data and the row-presence guard block a populated column, applies when empty"
note:     the Twin chose n from its minted emails and emptied OrderLine, Order and Customer child-first for the contrast;
          the fixture fixes n so exactly one value is over-length.
```

### TWN-204 · row 24 · add-unique · a duplicate blocks the index build; a unique column applies
```case
kind:     corpus
tags:     violating:exact · clean
env:      tw · (a) Order 25 rows · Channel N'DUPE'×2 · (b) Customer 25 rows · Email all distinct
change:   (a) head + CREATE UNIQUE INDEX UIX_Order_Channel ON Order(Channel), in its own file · (b) head + CREATE UNIQUE
          INDEX UIX_Customer_Email ON Customer(Email)
expect:   answers    (a) Unique(Order,(Channel))={exact} · (b) Unique(Customer,(Email))={}
          questions  (a) Unique(Order,(Channel)) · (b) Unique(Customer,(Email))
          witnesses  (a) dup(Order,(Channel),exact) (+ancestors Customer) · (b) presence(Customer)
          outcome    (a) verdict blocked unique · Msg 1505 · UIX_Order_Channel · exit 3 · (b) verdict clean · exit 0
          record     tier T2 · (a) residue {} (the index was not built) · (b) effects {adds UIX_Customer_Email}
laws:     T2 F1 F3 H6
source:   receipt · SamplePr "add-unique: duplicate values block the unique index, a unique column applies"
note:     no guard precedes an index build, so the block is the engine's value check. The other classes (case, accent,
          trailing, nulls) belong to the op family.
```

### TWN-205 · row 24 · retype-explicit · a lossy narrowing blocks populated; a widening retype applies
```case
kind:     corpus
tags:     violating:overflow · clean
env:      tw · (a) OrderLine 25 rows · one Quantity = 1000 · (c) as (a) · (d) as (a)
change:   (a), (c) head OrderLine.Quantity int→tinyint · (d) head OrderLine.Quantity int→bigint
action:   prove Dev (a, d) · prove Dev --permissive (c)
expect:   answers    (a) HasRows(OrderLine)=yes · Fits(OrderLine.Quantity→tinyint)={overflow}
                     · (d) Fits(OrderLine.Quantity→bigint)={}
          questions  (a) HasRows(OrderLine) · Fits(OrderLine.Quantity→tinyint) · (d) Fits(OrderLine.Quantity→bigint)
          witnesses  (a) misfit(OrderLine.Quantity, overflow), which also answers HasRows(OrderLine) · (d) presence(OrderLine)
          outcome    (a), (c) verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3 · (d) verdict clean · exit 0
          copy       (c) Permissive: Msg 220 on dbo.OrderLine.Quantity; OrderLine hash unchanged
          record     tier T2
laws:     T2 F1 F3 H5
source:   receipt · SamplePr "retype-explicit: a lossy narrowing type change blocks a populated column, a widening retype applies"
note:     the Twin's relaxed leg records Msg 220 ("Arithmetic overflow error for data type tinyint"), which ARCH §6
          classifies as overflow.
```

### TWN-206 · row 24 · add-check · a violating row blocks the check; conforming data applies trusted
```case
kind:     corpus
tags:     violating:check · clean
env:      tw · (a) OrderLine 25 rows · one Quantity = 0 · (b) OrderLine 25 rows · every Quantity ≥ 1
change:   head + CK_OrderLine_Quantity CHECK (Quantity > 0) · (a′) as (a), with witness.sql
          `-- witness: CheckFalse(OrderLine,CK_OrderLine_Quantity)` and one OrderLine row with Quantity = 0
expect:   answers    (a) CheckFalse(OrderLine,CK_OrderLine_Quantity)=violating · (b) =clean
          questions  CheckFalse(OrderLine,CK_OrderLine_Quantity)
          witnesses  (a) needs a declared row · (a′) presence(OrderLine) · declared(OrderLine Quantity = 0) ·
                     (b) presence(OrderLine)
          outcome    (a) stop check-witness · "Write one row into witness.sql that makes CK_OrderLine_Quantity FALSE, then re-run." · exit 5 · (a′) verdict blocked constraint · Msg 547 · CK_OrderLine_Quantity · exit 3 ·
                     (b) verdict clean · exit 0
          record     tier T2 · (a′) residue {main: CK_OrderLine_Quantity added WITH NOCHECK; validation: failed, untrusted} ·
                     (b) effects {adds CK_OrderLine_Quantity, trusted} · ships reconcile first (F10)
laws:     T2 F1 F3 H6 A5
source:   receipt · SamplePr "add-check: a violating row blocks the check, conforming data applies"
note:     (b)'s presence row must satisfy the new predicate; if the planter's literal does not, the planter stops
          planter-needs-row (PLT-96) and the agent declares a row (ARCH §11.7).
```

### TWN-207 · row 24 · add-optional · a nullable column applies on a populated table
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows
change:   head Customer + MiddleName nvarchar(100) NULL
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Customer.MiddleName}
laws:     T2 F3 H2
source:   receipt · SamplePr "add-optional: a new nullable column applies clean on a populated table"
note:     "rows intact" is the oracle copy's to confirm (F3); T0 publishes head onto the unplanted copy.
```

### TWN-208 · row 24 · add-default · NOT NULL with a default applies, stamping existing rows
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows
change:   head Customer + IsActive bit NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT ((1))
expect:   questions  none: the defaulted NOT NULL backfill is a Scan · measures Rows(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Customer.IsActive, DF_Customer_IsActive}
laws:     T2 F3 F4
source:   receipt · SamplePr "add-default: a NOT NULL column with a default applies clean and backfills existing rows"
note:     the default stamps every existing row as the column lands (F14); the oracle's copy shows all 25 stamped. A Scan
          asks no question; its Rows only feed the scale flag (ARCH §4).
```

### TWN-209 · row 24 · add-index · a non-unique index builds over a populated table
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows
change:   head + CREATE NONCLUSTERED INDEX IX_Customer_Email ON Customer(Email), in its own file
expect:   questions  none: a non-unique index build is a Scan (the key is at most 500 bytes, under the KeyLength limit)
                     · measures Rows(Customer)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds IX_Customer_Email}
laws:     T2 F3 F4
source:   receipt · SamplePr "add-index: a non-unique index builds clean over a populated table"
note:     an index build reads every row but cannot fail on data under the key limit; at 1M rows or more its measured Rows
          raise the scale flag (TWN-132).
```

### TWN-210 · row 24 · widen · enlarging a column applies and keeps every value
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows
change:   head Customer.Email nvarchar(250)→nvarchar(400), NOT NULL kept
expect:   answers    Fits(Customer.Email→nvarchar(400))={}
          questions  Fits(Customer.Email→nvarchar(400))
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · effects {modifies Customer.Email} · hashes {dbo.Customer before = after} · silence ok
laws:     T2 F1 F3 D6
source:   receipt · SamplePr "widen: enlarging a column applies clean and preserves every value"
note:     DacFx restates NOT NULL in the ALTER; nullability is unchanged, so no NotNull question. The Twin's digest
          equality is kept as the before/after hash of every touched table (row 18).
```

### TWN-211 · row 24 · make-optional · relaxing NOT NULL applies on a populated table
```case
kind:     corpus
tags:     clean
env:      tw · OrderLine 25 rows · Sku populated
change:   head OrderLine.Sku nvarchar(64) NOT NULL→NULL
expect:   questions  none (a loosening reads nothing)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {modifies OrderLine.Sku}
laws:     T2 F3
source:   receipt · SamplePr "make-optional: relaxing NOT NULL to NULL applies clean on a populated table"
```

### TWN-212 · row 24 · retype-implicit · a widening type change applies and keeps every value
```case
kind:     corpus
tags:     clean
env:      tw · OrderLine 25 rows
change:   head OrderLine.Quantity int→bigint
expect:   answers    Fits(OrderLine.Quantity→bigint)={}
          questions  Fits(OrderLine.Quantity→bigint)
          witnesses  presence(OrderLine) (+ancestors Order, Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · effects {modifies OrderLine.Quantity} · hashes {dbo.OrderLine before = after} · silence ok
laws:     T2 F1 F3 D6
source:   receipt · SamplePr "retype-implicit: a widening type change applies clean and preserves every value"
note:     hash.sql hashes values, not storage bytes, so a widening that keeps every value keeps the hash (the Twin's
          digest, row 18). SUM, MAX and the probed value are the oracle copy's to confirm.
```

### TWN-213 · row 24 · create-entity · a new table with an identity PK and an FK applies clean
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows
change:   head + dbo.Address (IDENTITY Id PK_Address, CustomerId NOT NULL FK_Address_Customer→Customer, Line1, City,
          PostalCode NULL), in its own file
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Address, PK_Address, FK_Address_Customer (trusted)}
laws:     T2 F3 S3
source:   receipt · SamplePr "create-entity: a new table with an identity PK and an FK to Customer applies clean"
note:     the FK's validation reads only Address, which this delta creates: it is answered from the delta (a new table is
          empty; ARCH §4 coordinates rule 1), never asked of Dev and never missing-object (S3).
```

### TWN-214 · row 24 · audit-columns · NOT NULL stamps with function defaults apply clean
```case
kind:     corpus
tags:     clean
env:      tw · Order 25 rows
change:   head Order + CreatedOn datetime2 NOT NULL DF_Order_CreatedOn (SYSUTCDATETIME()) · CreatedBy nvarchar(100) NOT
          NULL DF_Order_CreatedBy (SUSER_SNAME()) · UpdatedOn datetime2 NOT NULL DF_Order_UpdatedOn (SYSUTCDATETIME())
expect:   questions  none: the defaulted NOT NULL backfills are Scans · measures Rows(Order)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Order.CreatedOn, Order.CreatedBy, Order.UpdatedOn, three named defaults}
laws:     T2 F3
source:   receipt · SamplePr "audit-columns: NOT NULL audit stamps with function defaults apply clean and backfill existing rows"
note:     the function defaults stamp every existing row as the columns land (F14).
```

### TWN-215 · row 24 · temporal-new · a new system-versioned entity applies with its history table
```case
kind:     corpus
tags:     clean
env:      tw
change:   head + dbo.Rate (IDENTITY Id PK, Code, Amount decimal(18,4), ValidFrom and ValidTo GENERATED ALWAYS, PERIOD FOR
          SYSTEM_TIME) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.Rate_History))
expect:   questions  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Rate (system-versioned), Rate_History}
laws:     T2 F3
source:   receipt · SamplePr "temporal-new: a new system-versioned entity applies clean with its history table"
```

### TWN-216 · row 24 · modify-default · a default's value changes; existing rows keep theirs
```case
kind:     corpus
tags:     clean · edge
env:      tw · Order 25 rows · Channel carries DF_Order_Channel DEFAULT (N'Web')
change:   head DF_Order_Channel DEFAULT (N'Web') → DEFAULT (N'Store')
expect:   questions  none (DacFx drops and re-adds the default)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {modifies DF_Order_Channel}
laws:     T2 F3 F9
source:   receipt · SamplePr "modify-default: changing a default's value applies clean and leaves existing rows unchanged"
note:     only later inserts see Store; a default never backfills (F10). Had Dev's default been system-named, its delta
          would differ, so the copy carries Dev's name (F9).
```

### TWN-217 · row 24 · compound additive batch · two tables, two keys and a defaulted NOT NULL column in one publish
```case
kind:     corpus
tags:     clean
env:      tw · Order 25 rows · OrderLine 25 rows
change:   one release: head + ReturnReason (Id PK, Code, IsActive DF 1) · + Return (IDENTITY Id, OrderLineId
          FK_Return_OrderLine→OrderLine, ReturnReasonId FK_Return_ReturnReason→ReturnReason, Quantity DF 1) · Order +
          ReturnsAllowed bit NOT NULL DF_Order_ReturnsAllowed (1)
expect:   questions  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds ReturnReason, Return, FK_Return_OrderLine, FK_Return_ReturnReason,
                     Order.ReturnsAllowed} · ships one publish (F14)
laws:     T2 F3
source:   receipt · SamplePr "compound additive batch: two tables, two keys, and a defaulted NOT NULL column land in one publish"
note:     an all-additive release inherits no guard, DacFx orders the objects, and the default stamps all 25 Order rows.
```

### TWN-218 · row 24 · compound atomic veto · the blocking atom vetoes the release; the innocent atom does not land
```case
kind:     corpus
tags:     clean · edge
env:      tw · Order 25 rows · OrderLine 25 rows · Note NULL×0
change:   one release: head Order + Memo nvarchar(100) NULL · OrderLine.Note NULL→NOT NULL
expect:   answers    HasRows(OrderLine)=yes · NotNull(OrderLine.Note)=clean
          questions  HasRows(OrderLine) · NotNull(OrderLine.Note)
          witnesses  presence(OrderLine) (+ancestors Order, Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     tier T2 · residue {} (Order.Memo was not added; the guard batch precedes every change) · ships the
                     atoms serialized (F13)
laws:     T2 F3 H6
source:   receipt · SamplePr "compound atomic veto: the blocking atom refuses the release and the innocent atom rolls back with it"
```

### TWN-219 · row 24 · drop-default · the add and the drop are both in place
```case
kind:     corpus
tags:     clean
env:      tw · Order 25 rows · (a) Channel without a default · (b) Channel with DF_Order_Channel DEFAULT (N'Web')
change:   (a) head adds DF_Order_Channel · (b) head drops DF_Order_Channel
expect:   questions  none
          outcome    (a), (b) verdict clean · exit 0
          record     tier T0 · effects (a) {adds DF_Order_Channel} · (b) {removes DF_Order_Channel}
laws:     T2 F3
source:   receipt · SamplePr "drop-default: the add and the drop are both in-place; existing rows never move"
note:     the drop's cost (inserts that relied on the default) is application runtime, which no publish witnesses; the
          record names it (skills/op/drop-default).
```

### TWN-220 · row 24 · drop-check · the drop is clean; a row written in the gap refuses the re-add
```case
kind:     corpus
tags:     clean · violating:check
env:      tw · Order 25 rows · (a) CK_Order_Total_NonNegative CHECK (Total >= 0), trusted · (b) no check · one Order with
          Total = -5.00
change:   (a) head drops CK_Order_Total_NonNegative · (b) head re-adds it · (b′) as (b), with witness.sql
          `-- witness: CheckFalse(Order,CK_Order_Total_NonNegative)` and one Order with Total = -5.00
expect:   answers    (b) CheckFalse(Order,CK_Order_Total_NonNegative)=violating
          questions  (a) none · (b) CheckFalse(Order,CK_Order_Total_NonNegative)
          witnesses  (b′) presence(Order) (+ancestors Customer) · declared(Order Total = -5.00)
          outcome    (a) verdict clean · exit 0 · (b) stop check-witness · exit 5 · (b′) verdict blocked constraint ·
                     Msg 547 · CK_Order_Total_NonNegative · exit 3
          record     (a) tier T0 · effects {removes CK_Order_Total_NonNegative} · (b′) tier T2 · residue
                     {validation: CK_Order_Total_NonNegative untrusted}
laws:     T2 F1 F3 A5
source:   receipt · SamplePr "drop-check: the drop is clean; a violating row written in the gap refuses the re-add"
```

### TWN-221 · row 24 · drop-unique · the drop is clean; a duplicate written in the gap refuses the re-add
```case
kind:     corpus
tags:     clean · violating:exact
env:      tw · Customer 25 rows · (a) UX_Customer_Email unique, every Email distinct · (b) no UX_Customer_Email · one
          Email held twice
change:   (a) head without UX_Customer_Email's file · (b) head + CREATE UNIQUE NONCLUSTERED INDEX UX_Customer_Email ON
          Customer(Email)
expect:   answers    (b) Unique(Customer,(Email))={exact}
          questions  (a) none · (b) Unique(Customer,(Email))
          witnesses  (b) dup(Customer,(Email),exact)
          outcome    (a) verdict clean · exit 0 · (b) verdict blocked unique · Msg 1505 · UX_Customer_Email · exit 3
          record     (a) tier T0 · effects {removes UX_Customer_Email} (DropIndexesNotInSource is on by default, apart
                     from DropObjectsNotInSource) · (b) tier T2 · residue {}
laws:     T2 F1 F3
source:   receipt · SamplePr "drop-unique: the drop is clean; a duplicate written in the gap refuses the re-add"
note:     the Twin asserts the re-add's refusal; Msg 1505 is its header's account of it.
```

### TWN-222 · row 24 · drop-pk · referenced refuses at the model build; unreferenced leaves a heap
```case
kind:     corpus
tags:     refuse · clean
env:      tw · Order 25 rows · OrderLine 25 rows
change:   (a) head without PK_Order (FK_OrderLine_Order still references it) · (b) head without PK_OrderLine (unreferenced)
expect:   questions  (a) none: no dacpac is built · (b) none
          outcome    (a) verdict refused-at-build · SQL71516 · dbo.Order (referenced by FK_OrderLine_Order) · exit 3 ·
                     (b) verdict clean · exit 0
          record     (b) tier T0 · effects {removes PK_OrderLine; OrderLine becomes a heap}
laws:     T2 F3 H2
source:   receipt · SamplePr "drop-pk: referenced refuses at the model build; unreferenced drops clean and leaves a heap"
note:     (a) reaches no engine: no read, no copy (F18).
```

### TWN-223 · row 24 · identity-swap · removing IDENTITY from Order.Id is a table rebuild
```case
kind:     corpus
tags:     clean · edge
env:      tw · Order 25 rows, IDENTITY Id 1..25 · OrderLine 25 rows → Order (FK_OrderLine_Order)
change:   head Order.Id INT IDENTITY(1,1) → INT, nothing else
expect:   answers    HasRows(Order)=yes · Fits(moved columns)={} · Gapped(Order)=no · Orphan(OrderLine.(OrderId)→Order)=clean
          questions  Rebuild(Order) · Orphan(OrderLine.(OrderId)→Order)
          witnesses  presence(Order) (+ancestors Customer) · presence(OrderLine), linked to that Order (the Orphan
                     question's link)
          outcome    = the oracle's (F3); expected verdict clean · exit 0
          record     tier T2 · effects {modifies Order (tmp_ms_xx rebuild); FK_OrderLine_Order dropped and re-added, trusted}
laws:     T2 F1 F3 F4
source:   verify · the Twin fact accepts either outcome and asserts the end state only (Id no longer IDENTITY; keys, rows
          and the incoming FK preserved): SamplePr "identity-swap: removing IDENTITY from Order.Id is a table rebuild — the true outcome under the production gate, keys and incoming FK preserved" · sample-prs/identity-swap.md (the
          add-IDENTITY direction, sqlpackage 170.4.83.3) saw the gate allow the rebuild
note:     the first run records the receipt. Keys preserved, 0 orphans and the FK trusted are the oracle copy's checks.
```

### TWN-224 · row 24 · temporal-convert · the existing populated Customer becomes system-versioned
```case
kind:     corpus
tags:     clean · remedy
env:      tw · Customer 25 rows · non-temporal
change:   head Customer + ValidFrom and ValidTo datetime2 GENERATED ALWAYS AS ROW START / ROW END · PERIOD FOR
          SYSTEM_TIME · SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.Customer_History)
expect:   questions  Temporal(Customer) (ADD PERIOD, SET SYSTEM_VERSIONING = ON), with HasRows(Customer) or
                     Rebuild(Customer) as the generated delta asks
          answers    Temporal(Customer)=clean (the period columns are new, so no row holds an invalid period)
          outcome    = the oracle's (F3) · if the declarative form blocks, it ships as the staged program (period columns
                     with a historical ValidFrom default, then versioning), whose authored step first stops
                     authored-script
          record     tier T2, or T3 when staged
laws:     T2 F3 F4
source:   verify · the Twin fact accepts either outcome and asserts the end state only (temporal, history linked, rows
          and business data unchanged): SamplePr "temporal-convert: turning the existing populated Customer system-versioned — the true outcome under the production gate, existing rows preserved" ·
          sample-prs/temporal-convert.md (the staged backfill was not blocked)
note:     the ROW START trap (existing rows dated to the conversion) is a question of meaning the facsimile cannot see;
          the record names it.
```

### TWN-225 · row 24 · create-fk-clean · an FK over clean child data applies in place and lands trusted
```case
kind:     corpus
tags:     clean
env:      tw without FK_OrderLine_Order · OrderLine 25 rows · 0 orphans
change:   head + FK_OrderLine_Order (OrderId → Order)
expect:   answers    Orphan(OrderLine.(OrderId)→Order)=clean
          questions  Orphan(OrderLine.(OrderId)→Order)
          witnesses  presence(OrderLine) (+ancestors Order, Customer: the relationship is head's, carried by the question)
          outcome    verdict clean · exit 0
          record     tier T2 · effects {adds FK_OrderLine_Order, trusted} · ships one release (F8, F9)
laws:     T2 F1 F3
source:   receipt · SamplePr "create-fk-clean: an FK over clean child data applies in place and lands trusted"
note:     Dev's catalog has no FK, so the planter must take the ancestor from the Orphan question; otherwise the planted
          child is an orphan and acceptance stops.
```

### TWN-226 · row 24 · define-pk · a primary key over unique non-NULL keys builds clean
```case
kind:     corpus
tags:     clean
env:      tw · OrderLine a heap (no PK_OrderLine) · 25 rows · Id unique and NOT NULL
change:   head + PK_OrderLine PRIMARY KEY (Id)
expect:   answers    Unique(OrderLine,(Id))={}
          questions  Unique(OrderLine,(Id))
          witnesses  presence(OrderLine) (+ancestors Order, Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · effects {adds PK_OrderLine, clustered}
laws:     T2 F1 F3
source:   receipt · SamplePr "define-pk: adding a primary key over unique non-NULL keys builds the clustered index clean"
note:     the Twin made the heap by dropping the PK from a minted table; here Dev simply is a heap. The nulls class cannot
          occur, since Id is NOT NULL.
```

### TWN-227 · row 24 · junction · a new bridge with two FKs and a composite PK applies clean
```case
kind:     corpus
tags:     clean
env:      tw · Order 25 rows
change:   head + dbo.Tag (Id PK, Name) · + dbo.OrderTag (OrderId FK_OrderTag_Order→Order, TagId FK_OrderTag_Tag→Tag,
          PK (OrderId, TagId))
expect:   questions  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {adds Tag, OrderTag, the composite PK, both FKs trusted}
laws:     T2 F3 S3
source:   receipt · SamplePr "junction: a new bridge with two FKs and a composite PK applies clean, both legs trusted"
note:     the Twin's post-publish probes (valid pairs resolve; a duplicate pair raises Msg 2627) show how the new keys
          behave at runtime. They are not a publish verdict.
```

### TWN-228 · row 24 · create-fk-orphan · an orphan blocks at WITH CHECK CHECK; the reconcile ships it trusted
```case
kind:     corpus
tags:     violating:orphan · remedy
env:      tw without FK_OrderLine_Order · OrderLine 25 rows · 1 orphan (OrderId = MAX(Order.Id) + 100000)
change:   (a) head + FK_OrderLine_Order · (b) (a) + Script.PreDeployment.sql repointing orphans to an existing Order, by
          predicate · witness.sql `-- witness: Authored(pre-deploy over OrderLine.OrderId)` with the orphan
action:   prove Dev (a) · shape Dev dbo.OrderLine.OrderId, then prove Dev (b)
expect:   answers    Orphan(OrderLine.(OrderId)→Order)=violating
          questions  Orphan(OrderLine.(OrderId)→Order) · (b) + Authored(pre-deploy over OrderLine.OrderId)
          witnesses  orphan(OrderLine.(OrderId)→Order), which also answers presence of OrderLine · (b) + declared(orphan
                     OrderLine)
          outcome    (a) verdict blocked constraint · Msg 547 · FK_OrderLine_Order · exit 3 · (b) first stop
                     authored-script · "… Run facsimile shape Dev dbo.OrderLine.OrderId …" · exit 5; then, with the
                     declared witness, verdict clean · exit 0
          record     (a) tier T2 · residue {main: FK_OrderLine_Order added WITH NOCHECK; validation: failed, untrusted} ·
                     (b) tier T3 · effects {adds FK_OrderLine_Order, trusted} · ships one release with the pre-deploy
                     reconcile (F9)
laws:     T2 F1 F3 H6 A5
source:   receipt · SamplePr "create-fk-orphan: an orphan blocks the FK at WITH CHECK CHECK and leaves it untrusted; reconcile then re-validate to trusted"
note:     the Twin ran its remedy as statements (NOCHECK, reconcile, WITH CHECK CHECK: is_not_trusted 1 → 0); restated
          as the pre-deploy it ships as, the declarative add trusts itself (F9).
```

### TWN-229 · row 24 · toggle-trust · WITH CHECK CHECK trusts an untrusted FK over clean data; a violating row keeps it untrusted
```case
kind:     corpus
tags:     clean · violating:orphan · edge
env:      tw · FK_OrderLine_Order present but untrusted (added WITH NOCHECK) · (a) 0 orphans · (b) 1 orphan
change:   Script.PostDeployment.sql: ALTER TABLE [dbo].[OrderLine] WITH CHECK CHECK CONSTRAINT [FK_OrderLine_Order];
          head schema unchanged · witness.sql (a) `-- shapes: none (the script re-validates a key; it reads no value)` ·
          (b) `-- witness: Authored(post-deploy over FK_OrderLine_Order)` with the orphan
expect:   answers    Trust(OrderLine,FK_OrderLine_Order)=untrusted
          questions  Authored(post-deploy over FK_OrderLine_Order)
          witnesses  trust(OrderLine, FK_OrderLine_Order, untrusted) · presence(OrderLine) · (b) declared(orphan OrderLine)
          outcome    first stop authored-script · exit 5; then (a) verdict clean · exit 0 · (b) verdict blocked
                     authored · Msg 547 · post-deploy · exit 3
          record     tier T3 · (a) effects {FK_OrderLine_Order trusted} · (b) residue {main: applied; post-deploy:
                     FK_OrderLine_Order still untrusted}
laws:     T2 F9 F3 H6 H5
source:   receipt · SamplePr "toggle-trust: WITH CHECK CHECK makes an untrusted FK trusted over clean data; a violating row keeps it untrusted"
note:     operational, not declarative (skills/op/toggle-trust). The 547 is raised inside the post-deploy batch, so it is
          blocked authored, whatever its number (H5). It depends on the copy holding Dev's untrusted state (F9; ARCH §11.1).
```

### TWN-230 · row 24 · drop-index · removing an index publishes clean and loses no rows
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows · IX_Customer_Email (Email)
change:   head without IX_Customer_Email's file
expect:   questions  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {removes IX_Customer_Email} (DropIndexesNotInSource)
laws:     T2 F3
source:   receipt · SamplePr "drop-index: removing an index publishes clean and loses no rows; the scripted DROP INDEX is lossless"
note:     the Twin's scripted DROP INDEX reaches the same end state. Whether "not used" is true is workload evidence, a
          residual (skills/op/drop-index).
```

### TWN-231 · row 24 · drop-fk · removing a foreign key publishes clean; an orphan becomes writable
```case
kind:     corpus
tags:     clean
env:      tw · OrderLine 25 rows · FK_OrderLine_Order trusted
change:   head OrderLine without FK_OrderLine_Order
expect:   questions  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {removes FK_OrderLine_Order} (DropConstraintsNotInSource)
laws:     T2 F3
source:   receipt · SamplePr "drop-fk: removing a foreign key publishes clean and loses no rows, but an orphan can now be written"
note:     the cost the Twin proves (an orphan insert refused with 547 before, accepted after) is runtime; the record names it.
```

### TWN-232 · row 24 · delete-attribute · removing a populated column is blocked by the guard
```case
kind:     corpus
tags:     clean · edge
env:      tw · OrderLine 25 rows · Sku populated on every row
change:   head OrderLine without Sku
action:   prove Dev · prove Dev --permissive
expect:   answers    HasRows(OrderLine)=yes
          questions  HasRows(OrderLine)
          witnesses  presence(OrderLine) (+ancestors Order, Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          copy       Permissive: OrderLine.Sku dropped; the OrderLine hash before ≠ after (the irreversible step)
          record     tier T1 · residue {} · ships the 4-phase deprecation (skills/op/delete-attribute)
laws:     T2 F1 F3 F6 H5
source:   receipt · SamplePr "delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the scripted DROP COLUMN is the irreversible step"
```

### TWN-233 · row 24 · delete-entity · removing a table from source is a phantom; the scripted DROP TABLE is the real one
```case
kind:     corpus
tags:     clean · edge
env:      tw · OrderLine 25 rows (a leaf)
change:   (a) head without dbo.OrderLine's file · (b) (a) + Script.PostDeployment.sql: DROP TABLE [dbo].[OrderLine];
          witness.sql `-- shapes: none (the script drops the whole table; it branches on no value)`
expect:   questions  (a) none · (b) Authored(post-deploy over dbo.OrderLine)
          witnesses  (b) presence(OrderLine)
          outcome    (a) verdict clean · exit 0 · (b) first stop authored-script · exit 5; then verdict clean · exit 0
          record     (a) tier T0 · effects {}; Dev keeps dbo.OrderLine and its rows (a phantom removal under
                     DropObjectsNotInSource off; red-team #14) · (b) tier T3 · effects {removes dbo.OrderLine and its
                     rows, irreversibly}
laws:     T2 F3 H4
source:   receipt · SamplePr "delete-entity: removing a whole table from source is a phantom removal under DropObjectsNotInSource=false; the scripted DROP TABLE is the real, destructive one"
note:     the verdict on a destructive authored drop is clean, because it publishes. The record states the loss; the
          verdict does not.
```

### TWN-234 · row 24 · archive-entity · retiring rows to an archive table conserves every row
```case
kind:     corpus
tags:     remedy
env:      tw · OrderLine 25 rows
change:   head + schema archive · + archive.OrderLine (Id int PK, no IDENTITY, the same columns) ·
          Script.PostDeployment.sql: batched DELETE TOP (7) … OUTPUT DELETED.* INTO archive.OrderLine WHERE Id <= @cutoff,
          then RAISERROR(…, 16, 1) unless live + archived = the count before · witness.sql: declared(OrderLine rows on
          both sides of the cutoff)
expect:   questions  Authored(post-deploy over dbo.OrderLine)
          witnesses  presence(OrderLine) · declared(OrderLine rows above and below the cutoff)
          outcome    first stop authored-script · exit 5; then verdict clean · exit 0
          copy       live + archived = the count before · the archived rows byte-identical to the moved ones · no Id in
                     both tables
          record     tier T3 · effects {adds archive.OrderLine; moves rows}
laws:     T2 F3 H5 M2
source:   receipt · SamplePr "archive-entity: retiring rows to an archive table conserves every row (live + archived == original), byte-identical and batched"
note:     the conservation proof is the script's own (the authored-script stop asks for that). The Twin's "two or more batches" needs more than 7 rows below the cutoff; the minimal plant has 1, so batching shows only when
          witness.sql declares 8 or more (volume, row 23).
```

### TWN-235 · row 24 · rename-attribute · a bare column rename is DROP+ADD, blocked; the refactorlog rename keeps every value
```case
kind:     corpus
tags:     clean · edge
env:      tw · Customer 25 rows · Email populated
change:   (a) head renames Customer.Email to EmailAddress in the CREATE, no refactorlog entry · (b) the same, with its
          refactorlog entry
expect:   answers    (a) HasRows(Customer)=yes
          questions  (a) HasRows(Customer) · (b) none
          witnesses  (a) presence(Customer) · (b) none
          outcome    (a) verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3 · (b) verdict clean · exit 0
          record     (a) tier T1 · residue {} · ships with a refactorlog entry (skills/_index/identity-and-refactorlog) ·
                     (b) tier T0 · effects {renames Customer.Email → EmailAddress (sp_rename)}
laws:     T2 F3 H5
source:   receipt · SamplePr "rename-attribute: a bare column rename (no refactorlog) is a DROP+ADD blocked by the data-loss guard; EXEC sp_rename preserves every value"
note:     without the entry DacFx scripts DROP COLUMN Email + ADD EmailAddress; with it, EXECUTE sp_rename …, 'COLUMN',
          which is the Twin's direct sp_rename leg as it ships. Every value kept is the oracle copy's check.
```

### TWN-236 · row 24 · rename-entity · a bare table rename is a phantom; the refactorlog rename keeps object_id and rows
```case
kind:     corpus
tags:     clean · edge
env:      tw · OrderLine 25 rows (a leaf)
change:   (a) head re-heads dbo.OrderLine as dbo.OrderItem (PK_OrderItem, FK_OrderItem_Order), no refactorlog entry ·
          (b) the same, with its refactorlog entry
expect:   questions  (a) none · (b) as the delta asks
          outcome    (a) verdict clean · exit 0 · (b) verdict clean · exit 0
          record     (a) tier T0 · effects {adds dbo.OrderItem, empty}; Dev keeps dbo.OrderLine with its 25 rows (a
                     phantom rename; red-team #14) · (b) effects {renames dbo.OrderLine → dbo.OrderItem; object_id and
                     rows kept} · notChecked ∋ integration-studio (the old name now raises Msg 208)
laws:     T2 F3 H4
source:   receipt · SamplePr "rename-entity: a bare table rename (no refactorlog) is a phantom (new empty table, old survives); EXEC sp_rename preserves object_id and all rows"
note:     verify (b)'s delta: whether DacFx renames the PK and FK through the refactorlog or drops and re-adds them (the
          re-add would ask Orphan(OrderItem.(OrderId)→Order), probed under the old name: ARCH §4 coordinates rule 2).
          The Twin's direct sp_rename kept the stale name FK_OrderLine_Order.
```

### TWN-237 · row 24 · change-delete-rule · NO ACTION → CASCADE is a clean DROP+ADD
```case
kind:     corpus
tags:     clean
env:      tw · OrderLine 25 rows · FK_OrderLine_Order NO ACTION, trusted · 0 orphans
change:   head FK_OrderLine_Order … ON DELETE CASCADE
expect:   answers    Orphan(OrderLine.(OrderId)→Order)=clean
          questions  Orphan(OrderLine.(OrderId)→Order)
          witnesses  presence(OrderLine) (+ancestors Order, Customer)
          outcome    verdict clean · exit 0
          record     tier T2 · effects {modifies FK_OrderLine_Order: DROP + ADD, ON DELETE CASCADE, trusted}
laws:     T2 F1 F3 F9
source:   receipt · SamplePr "change-delete-rule: an FK ON DELETE NO ACTION -> CASCADE is a clean metadata DROP+ADD"
note:     the Twin's rolled-back parent delete (547 before; the cascade removes exactly the children after) is runtime
          blast radius, owned by skills/review dependency-scope. With Dev's FK untrusted over an orphan the verdict
          becomes 547 (TWN-119).
```

### TWN-238 · row 24 · modify-index · an index gains a key column and an INCLUDE by DROP+CREATE
```case
kind:     corpus
tags:     clean
env:      tw · Customer 25 rows · IX_Customer_Email (Email)
change:   head IX_Customer_Email (Email, StatusId) INCLUDE (Name)
expect:   questions  none (the key, at most 504 bytes, is under the limit)
          outcome    verdict clean · exit 0
          record     tier T0 · effects {modifies IX_Customer_Email: DROP + CREATE}
laws:     T2 F3
source:   receipt · SamplePr "modify-index: an existing index gains a key column and an INCLUDE via a clean DROP+CREATE"
```

### TWN-239 · row 24 · move-schema · a header-edit move is a phantom; the refactorlog move is ALTER SCHEMA TRANSFER
```case
kind:     corpus
tags:     clean · edge
env:      tw · OrderLine 25 rows
change:   (a) head + schema sales; dbo.OrderLine re-headed as sales.OrderLine, no refactorlog entry · (b) the same, with
          its refactorlog entry
expect:   questions  none
          outcome    (a) verdict clean · exit 0 · (b) verdict clean · exit 0
          record     (a) tier T0 · effects {adds schema sales, sales.OrderLine (empty)}; Dev keeps dbo.OrderLine with its
                     25 rows (a phantom move) · (b) tier T0 · effects {moves dbo.OrderLine → sales.OrderLine
                     (ALTER SCHEMA TRANSFER); object_id and rows kept}
laws:     T2 F3 H4
source:   receipt · SamplePr "move-schema: a header-edit move without a refactorlog is a phantom move; ALTER SCHEMA TRANSFER is the real one"
```

### TWN-240 · row 24 · rebuild-index · operational maintenance with no declarative delta
```case
kind:     corpus
tags:     refuse
env:      tw · Customer 25 rows · IX_Customer_Email
change:   none is expressible: ALTER INDEX … REBUILD has no declarative form, so head is unchanged
action:   prove Dev · facsimile up · ALTER INDEX [IX_Customer_Email] ON [dbo].[Customer] REBUILD on fx_local · facsimile up
expect:   questions  none
          outcome    verdict clean · exit 0
          stdout     both `up` runs: DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          record     effects {} · routes to the maintenance job (skills/op/rebuild-index)
laws:     T2 D4 F3
source:   receipt · SamplePr "rebuild-index: ALTER INDEX REBUILD is operational maintenance with no declarative delta (NothingToApply)"
```

### TWN-241 · row 24 · create-static-seed · a new lookup and its guarded MERGE seed once, then stay silent
```case
kind:     corpus
tags:     clean
env:      tw · Status 3 rows
change:   head + dbo.Priority (Id int PK, explicit, Name nvarchar(50)) · Script.PostDeployment.sql + its guarded MERGE
          (1 High, 2 Medium, 3 Low)
expect:   questions  Authored(post-deploy over dbo.Priority), answered from the delta: Priority is created by it, so it is
                     empty (ARCH §4 coordinates rule 1) · SeedDrift(Priority) likewise 0
          witnesses  none
          outcome    verdict clean · exit 0 (no authored-script stop: the script reads no existing column)
          copy       Priority = {1 High, 2 Medium, 3 Low}; Id is not IDENTITY · a second publish: the MERGE affects 0 rows
                     and the reference-data hash is unchanged
          stdout     `up` twice: the second prints DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          record     tier T3 · effects {adds Priority; seeds 3 rows} · silence ok
laws:     T2 F3 D4 D6
source:   receipt · SamplePr "create-static-seed: a new lookup + guarded MERGE lane seeds on first converge and is idempotent (0 rows + identical hash) on the second"
note:     ARCH §6's authored-script stop names the existing columns a script reads; a seed into a table this delta creates
          reads none. The silence check (ARCH §5 step 11, D6) is the Twin's "0 rows + identical hash".
```

### TWN-242 · row 24 · edit-seed · the guarded WHEN MATCHED UPDATE changes exactly one row; the re-run is silent
```case
kind:     corpus
tags:     clean
env:      tw · Status {1 Open, 2 Closed, 3 Pending}, as the merge-base lane holds it
change:   the post-deploy Status MERGE: Pending → On Hold · witness.sql `-- shapes: none (Dev's Status equals the
          merge-base lane; SeedDrift counts only the edited row)`
action:   shape Dev dbo.Status.Name --vocabulary · prove Dev
expect:   questions  Authored(post-deploy over dbo.Status) · SeedDrift(Status)
          answers    SeedDrift(Status)=1 (row 3, the value the edit changes), in Dev and on the copy
          witnesses  none generated for Status: the merge-base lane's rows answer it
          outcome    first stop authored-script · exit 5; then verdict clean · exit 0
          copy       the MERGE updates exactly one row (Id 3) · a second publish affects 0 rows, Status hash identical
          record     tier T3 · effects {modifies Status row 3} · silence ok
laws:     T2 F3 P6 D4 D6
source:   receipt · SamplePr "edit-seed: the guarded WHEN MATCHED UPDATE changes exactly one row and the re-run is silent (0 rows + identical hash)"
note:     the copy's Status comes from the merge-base lane (ARCH §5 step 1), so it holds Pending as Dev does and the edit
          is not hidden. Static rows are model data, safe to read whole (TWIN_SUCCESSION §6b; P6 --vocabulary).
```

### TWN-243 · row 24 · extract-to-lookup (phase 1) · the lookup seeds idempotently, every value maps 1:1, the FK lands trusted
```case
kind:     corpus
tags:     clean · remedy
env:      tw · Order 25 rows · Channel ∈ {Web, Store}
change:   head + dbo.Channel (Id int PK, explicit, Code nvarchar(20)) · Order + ChannelId int NULL + FK_Order_Channel ·
          Script.PostDeployment.sql: the guarded Channel MERGE (1 Web, 2 Store), then the backfill join on Code, then
          RAISERROR(…, 16, 1) if any Order is left unmapped · witness.sql `-- witness: Authored(post-deploy over
          Order.Channel)` with Order rows whose Channel is Web and Store
action:   shape Dev dbo.Order.Channel --vocabulary · prove Dev
expect:   questions  Authored(post-deploy over Order.Channel)
          witnesses  presence(Order) · declared(Order Channel = Web, Store)
          outcome    first stop authored-script · exit 5; then verdict clean · exit 0
          copy       0 unmapped values · 0 NULL ChannelId · 0 orphan ChannelId · FK_Order_Channel trusted · Order.Channel
                     retained · a second publish: the MERGE affects 0 rows
          record     tier T3 · ships multi-phase (skills/op/extract-to-lookup) · silence ok
laws:     T2 F3 F8 P6 D6
source:   receipt · SamplePr "extract-to-lookup (phase 1): the lookup is seeded idempotently, every distinct source value maps 1:1, and the FK is trusted"
note:     Web and Store are the seed's own codes, so they are repository-defined values (P5). A third value in Dev (say
          Phone) makes the backfill raise, and the verdict is blocked authored (H5): a shape the Twin's synthetic {Web,
          Store} could not show.
```

### TWN-244 · row 24 · delete-seed-value · the additive seed keeps the row; the explicit delete of a referenced value is blocked
```case
kind:     corpus
tags:     clean · violating:orphan
env:      tw · Status {1 Open, 2 Closed, 3 Pending} · every Customer and Order references Open (1); Pending unreferenced
change:   (a) the post-deploy Status MERGE without Pending (additive: no WHEN NOT MATCHED BY SOURCE arm) · (b) (a) +
          DELETE FROM dbo.Status WHERE Id = 3; · (c) (a) + DELETE FROM dbo.Status WHERE Id = 1; · witness.sql
          `-- witness: Authored(post-deploy over dbo.Status)` with a Customer and an Order referencing Status 1
expect:   questions  Authored(post-deploy over dbo.Status)
          witnesses  presence(Customer), presence(Order), each referencing Status 1
          outcome    first stop authored-script · exit 5; then (a) verdict clean · (b) verdict clean · (c) verdict
                     blocked authored · Msg 547 · post-deploy · exit 3
          copy       (a) Pending persists; the MERGE affects 0 rows · (b) Status 3 → 2 rows; a second publish deletes 0 ·
                     (c) Status intact (the DELETE rolled back); the main script's changes stay applied (F15)
          record     tier T3 · (c) the 547 names FK_Customer_Status or FK_Order_Status (the engine's first) · prefer
                     IsActive = 0 to a hard delete (skills/op/delete-seed-value)
laws:     T2 F3 H6 H5
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the explicit, FK-guarded removal blocks a referenced value (Msg 547)"
```

### TWN-245 · row 24 · split-table (phase 1) · the copy lands 1:1 and trusted; the source-column drop is the guarded later phase
```case
kind:     corpus
tags:     remedy · clean
env:      tw · Customer 25 rows · Email populated
change:   R1: head + dbo.CustomerContact (IDENTITY Id, CustomerId UNIQUE FK_CustomerContact_Customer, Email nvarchar(250)
          NOT NULL) · Script.PostDeployment.sql: INSERT … SELECT every Customer's Email, RAISERROR unless one contact per
          customer · witness.sql: declared(Customer rows per Email shape) · R2: head Customer without Email
action:   shape Dev dbo.Customer.Email · prove Dev --program R1 R2
expect:   answers    R2: HasRows(Customer)=yes
          questions  R1: Authored(post-deploy over Customer.Email) · R2: HasRows(Customer)
          witnesses  presence(Customer) · declared(Customer rows per Email shape)
          outcome    R1: first stop authored-script · exit 5; then verdict clean · R1 replayed: empty plan, silence ok · R2:
                     verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          copy       R1: contacts = customers, 0 lost, 0 duplicated, Email hashes equal · FK_CustomerContact_Customer trusted
          record     R2 names Customer.Email (warning SQL72015) · ships multi-phase (skills/op/split-table)
laws:     T2 F3 F8 H5 D6
source:   receipt · SamplePr "split-table (phase 1): the new CustomerContact is created, every Email copies 1:1 with a matching digest and trusted FK, and the source-column drop is the guarded later phase"
note:     a non-empty R1 replay would be the F17 hazard, recorded not-silent (D6).
```

### TWN-246 · row 24 · merge-tables (phase 1) · cardinality before the copy; a 1:many merge is caught; the absorbed-table drop is later
```case
kind:     corpus
tags:     remedy · clean · edge
env:      tw + dbo.CustomerAddress (IDENTITY Id, CustomerId FK_CustomerAddress_Customer, PostalCode NOT NULL; no UNIQUE on
          CustomerId) · (a) one address per Customer (25) · (b) one Customer with two (26)
change:   head Customer + PostalCode nvarchar(20) NULL · Script.PostDeployment.sql: RAISERROR(…, 16, 1) if absorbed rows ≠
          distinct parents, else copy PostalCode across the join · witness.sql: declared((a) one address per customer ·
          (b) two addresses for one customer) · (c) a later release without CustomerAddress in the project
expect:   questions  Authored(post-deploy over CustomerAddress.CustomerId, CustomerAddress.PostalCode)
          witnesses  presence(Customer) · the declared CustomerAddress rows
          outcome    first stop authored-script · exit 5; then (a) verdict clean · exit 0 · (b) verdict blocked authored ·
                     Msg 50000 · post-deploy · exit 3 (the cardinality check's own RAISERROR, not the guard: H5) ·
                     (c) verdict clean · exit 0
          copy       (a) 0 NULL PostalCode; absorbed and survivor values equal · (c) Dev keeps CustomerAddress: a phantom
                     removal; the real drop is a scripted, reviewed DROP TABLE
          record     tier T3 · ships multi-phase (skills/op/merge-tables)
laws:     T2 F3 H5
source:   receipt · SamplePr "merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves every row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is the later phase"
note:     a naive copy without the check publishes clean on (b) and silently keeps one address: the facsimile cannot see
          meaning, which is why the authored script must check itself.
```

### TWN-247 · row 24 · move-attribute (phase 1) · the join is 1:1, the backfill matches; the source-column drop is the guarded later phase
```case
kind:     corpus
tags:     remedy · clean
env:      tw + dbo.CustomerProfile (IDENTITY Id, CustomerId UNIQUE FK_CustomerProfile_Customer, DisplayName) · one profile
          per Customer
change:   R1: head CustomerProfile + CreatedOn datetime2 NULL · Script.PostDeployment.sql: backfill across the 1:1 join,
          RAISERROR unless every profile is filled · witness.sql: declared(Customer and profile pairs) · R2: head Customer
          without CreatedOn
action:   shape Dev dbo.Customer.CreatedOn · prove Dev --program R1 R2
expect:   answers    R2: HasRows(Customer)=yes
          questions  R1: Authored(post-deploy over Customer.CreatedOn) · R2: HasRows(Customer)
          outcome    R1: first stop authored-script · exit 5; then verdict clean · R1 replayed: empty plan, silence ok · R2:
                     verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          copy       R1: 0 NULL destination values; source and destination hashes equal
          record     R2 names Customer.CreatedOn · ships multi-phase (skills/op/move-attribute)
laws:     T2 F3 F8 H5 D6
source:   receipt · SamplePr "move-attribute (phase 1): the join is proven 1:1, the destination column is backfilled with a matching digest, and the source-column drop is the guarded later phase"
```

### TWN-248 · row 35 · loop · `up` materializes, converges, and re-converges after an estate edit
```case
kind:     process
setup:    the tw repository (Tables/*.sql; referenceData Data/StaticSeeds.sql) · no fx_local
action:   facsimile up · up · status · edit Tables/dbo.Order.sql (+ Note nvarchar(50) NULL) · status · up · up
expect:   stdout     up 1: DONE up (schema published, 1 lane applied, 4 tables, rows > 0) · up 2: DONE up · Nothing to apply
                     · status: DONE status, up current · status after the edit: up stale · up 3: DONE up, re-converged ·
                     up 4: DONE up · Nothing to apply
          copy       after up 3, Order.Note exists in fx_local
          exit       all 0
laws:     T2 T3 D4
source:   receipt · TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an estate edit"
```

### TWN-249 · row 4 · loop · the estate's reference data stands and every relationship resolves
```case
kind:     process
setup:    tw · referenceData ["Data/StaticSeeds.sql"] · up.rowsPerTable 3
action:   facsimile up
expect:   copy       Status = exactly {1 Open, 2 Closed, 3 Pending} in Id order, never regenerated · Customer, Order and
                     OrderLine hold 3 rows each · zero orphans on FK_Customer_Status, FK_Order_Customer, FK_Order_Status
                     and FK_OrderLine_Order · every StatusId names a lane row
laws:     T2 T3 D1
source:   receipt · TwinMintLoopTests "K1 + L1: the estate's reference data stands and every relationship resolves"
note:     the Twin's 25 rows per table are a volume, retired with σ (row 11); its reference data and zero orphans are kept.
```

### TWN-250 · row 20 · loop · the same inputs give the same rows; `up --fresh` is reproducible
```case
kind:     process
setup:    tw
action:   facsimile up --fresh · hash every table · facsimile up --fresh · hash every table
expect:   copy       every table's content hash is identical across the two runs (Order's Id, CustomerId, StatusId and
                     Total in particular)
laws:     T2 T3 D1
source:   receipt · TwinMintLoopTests "T1: the same seed mints the same rows; the mint is reproducible"
```

### TWN-251 · row 16 · loop · retired: a scenario shaping the mint (weights, window, ratio, pins)
```case
kind:     process
setup:    a facsimile.json carrying the Twin fixture's `scenarios.skewed` block verbatim (Order rows 40, Channel weights,
          PlacedOn between, OrderLine perParent, the Customer 1000 pin)
action:   facsimile up
expect:   outcome    stop config · "facsimile.json: $.scenarios: unknown key" · exit 6
laws:     T2 T1 E4
source:   receipt · TwinMintLoopTests "M4: a scenario shapes the mint — weights, window, ratio, and pins land" (the Twin
          side) · retirement, structural: TWN-123; the pin's successors are witness.sql (TWN-124) and up.sql (TWN-317)
note:     a retirement case: weights, windows and ratios land nowhere, and the closed schema turns a copied twin.json habit
          into a located refusal, not a silent ignore. The pin moves to up.sql (row 17).
```

### TWN-252 · row 18 · loop · the proof verb holds on a throwaway database
```case
kind:     corpus
env:      tw · 25 rows per table · Status from the lane · 0 orphans
change:   none (head = Dev)
action:   prove Dev · prove Dev
expect:   questions  none
          outcome    verdict clean · exit 0
          record     acceptance ok · identity plan empty (4 tables, the lane's rows) · silence ok · the second run
                     byte-identical but for time
laws:     T2 F1 F2 D1 H2 D6
source:   receipt · TwinMintLoopTests "M5: the proof verb holds on a throwaway database"
note:     the Twin's TablesDefined = TablesLive, LanesApplied = 1, OrphanRows = 0, DeterministicRemint and no findings map
          to identity, the lane, acceptance (Dev has no orphan) and D1; the re-publish's empty plan is D6's silence.
```

### TWN-253 · row 13 · loop · the trust gate: an unsatisfiable check stops by name; a satisfiable one stays trusted
```case
kind:     corpus
tags:     edge
env:      tw + CK_OrderLine_Quantity, trusted · (a) CHECK (Quantity >= 100000), every Dev row satisfying it ·
          (b) CHECK (Quantity >= 0)
change:   head OrderLine.Note NULL→NOT NULL · (a′) as (a), with witness.sql `-- plants: dbo.OrderLine` and one valid row,
          Quantity = 100000
expect:   answers    HasRows(OrderLine)=yes · NotNull(OrderLine.Note)=clean · Trust(OrderLine,CK_OrderLine_Quantity)=trusted
          witnesses  (a) presence(OrderLine) cannot be made · (a′) declared(OrderLine Quantity = 100000) ·
                     (b) presence(OrderLine)
          outcome    (a) stop planter-needs-row · "Cannot make a valid row for dbo.OrderLine (CK_OrderLine_Quantity). Write one valid INSERT for dbo.OrderLine into witness.sql under `-- plants: dbo.OrderLine`." · exit 5 ·
                     (a′), (b) verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     CK_OrderLine_Quantity is trusted on the copy because it is trusted in Dev (F9), not because a gate
                     forced it
laws:     T2 F9 A5 H3
source:   receipt · TwinMintLoopTests "trust gate: a violating mint refuses by constraint name; a satisfiable check ends trusted"
note:     the Twin's refusal named the constraint (twin.mint.constraintViolation); the stop names it and the remedy. On
          `up` the same literal fails, and the declared row goes into up.sql under `-- plants: dbo.OrderLine`.
```

### TWN-254 · row 14 · loop · retired: evidence import → derive → mint re-profiling to the source's shape
```case
kind:     corpus
env:      tw · Customer 60 rows · Name Alpha×45, Beta×15 · Email 60 distinct secret<n>@source.example
change:   head Customer.Name nvarchar(100)→nvarchar(60)
action:   facsimile read Dev · prove Dev
expect:   answers    HasRows(Customer)=yes · Fits(Customer.Name→nvarchar(60))={}
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          files      no `secret`, `@source.example`, `Alpha` or `Beta` in any artifact
          copy       one Customer row, not 60, with no Alpha/Beta skew and no source email
laws:     T2 T1 P1 P5 F1
source:   receipt · TwinEvidenceLoopTests "M3: import → derive → mint re-profiles to the source's shape" (the Twin side) ·
          retirement, structural: TWN-121 (no verdict depends on volume or vocabulary skew)
note:     a retirement case: import, derive, the re-profiled mint and the coverage board (Customer rich, Order none) retire.
          The privacy half (no source value re-emitted, no literal in a committed file) is kept and strengthened (P1,
          P5, P6).
```

### TWN-255 · row 10 · loop · a view in the estate: `up` succeeds, base tables fill, the view stays published and data-free
```case
kind:     process
setup:    tw + Tables/dbo.vCustomerOrders.sql (CREATE VIEW over Order ⋈ Customer)
action:   facsimile up · facsimile up --fresh
expect:   copy       Customer holds rows · sys.views has vCustomerOrders · SELECT COUNT(*) FROM dbo.vCustomerOrders
                     succeeds · no DML ever targets the view · up --fresh gives the identical content hash
          exit       0 · 0
laws:     T2 T3 D1 F2
source:   receipt · TwinViewLoopTests "A view in the estate: twin up succeeds, base tables mint, the view stays published and data-free"
```

## Workflow continuity (T3) and side by side (T4)

### TWN-301 · row 22 · `twin up` → `facsimile up`, from nothing
```case
kind:     process
setup:    a laptop with LocalDB 2022 and the estate repository · no .facsimile/ · no fx_local
action:   facsimile up
expect:   stdout     DONE up · … · .facsimile/out/up.json · exit 0
          copy       fx_local holds head's schema, head's referenceData lanes verbatim, up.rowsPerTable rows per table with
                     their FK ancestors, then up.sql's declared rows
          files      the fingerprint under .facsimile/
          exit       0
laws:     T3 D4 S1 A1
source:   expected · THE_TWIN §7 ("container present → schema current → data present"); ARCH §8 (`up`)
note:     the honest difference: the data is k plain rows, not 100 distribution-faithful ones (TWIN_SUCCESSION §9:
          realistic local data is consciously dropped).
```

### TWN-302 · row 22 · `twin up` when current → "Nothing to apply.", fast
```case
kind:     process
setup:    fx_local converged
action:   facsimile up
expect:   stdout     DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          exit       0
laws:     T3 D4 A1
source:   verify · the Twin answered in about a second (THE_TWIN §7); whether the facsimile's no-op stays that fast
          depends on fingerprinting head without a full MSBuild run (ARCH §11.4, §11.6)
note:     no publish and no plant appear in the trace.
```

### TWN-303 · row 22 · `twin seed` → `facsimile up --fresh`: force, reproducibly
```case
kind:     process
setup:    fx_local converged · one row a developer inserted by hand
action:   facsimile up --fresh · facsimile up --fresh
expect:   copy       each run rebuilds fx_local from the cache · the hand-inserted row is gone, as after `twin seed` · the
                     two runs' content hashes are identical
          stdout     DONE up · … · .facsimile/out/up.json · exit 0, twice
          exit       0 · 0
laws:     T3 D1 S5
source:   expected · THE_TWIN §7 ("force a fresh mint, reproducibly")
note:     a hand-written row meant to survive belongs in up.sql, which `up --fresh` re-applies.
```

### TWN-304 · row 22 · `twin status` → `facsimile status`
```case
kind:     process
setup:    fx_local converged · proofs/<slug>/answers.Dev.json measured for an older delta · a cached schema.QA.dacpac 9
          days old
action:   facsimile status · edit a table file · facsimile status
expect:   stdout     DONE status · … · .facsimile/out/status.json · exit 0, each time
          files      the JSON lists the fx_ databases · up current, then stale after the edit · cached schemas with their
                     age (QA, 9 days) · answers.Dev.json stale (delta hash differs)
          exit       0 · 0
laws:     T3 A1 E3
source:   expected · ARCH §8 status · Twin parity, by receipt: TwinSchemaLoopTests "law 1: up materializes, converges, and re-converges after an estate edit" (status current, then stale after the edit)
```

### TWN-305 · row 22 · `twin down` → `facsimile down`: the proof copies go; fx_local and its hand-written rows stay
```case
kind:     process
setup:    fx_local with a row inserted by hand · two registered fx_<slug> proof databases · a person's own database fx_mine,
          absent from the registry · an unrelated database Scratch on the same instance
action:   facsimile down · facsimile up
expect:   copy       after down: no registered proof copy remains · fx_local still holds the hand-inserted row, as `twin down`
                     kept its container's data · fx_mine and Scratch untouched (S6) · the next up converges without a
                     rebuild
          stdout     DONE down · … · .facsimile/out/down.json · exit 0 · DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
          exit       0 · 0
laws:     T3 S6 D2
source:   expected · ARCH §2 row 22 and §8 (`down` keeps fx_local and the caches) · THE_TWIN §7 ("stop (state kept)")
```

### TWN-306 · row 22 · `twin reset` → `facsimile down --all`
```case
kind:     process
setup:    fx_local converged, its content hash recorded · a person's own database fx_mine, absent from the registry
action:   facsimile down --all · facsimile up
expect:   files      .facsimile/'s caches cleared after down --all
          copy       fx_local is gone after down --all; fx_mine survives (S6) · the next up builds from nothing; its content
                     hash equals the recorded one (D1)
          stdout     DONE down · … · .facsimile/out/down.json · exit 0
          exit       0 · 0
laws:     T3 S6 D1 D2
source:   expected · ARCH §2 row 22 · THE_TWIN §7 ("remove (data gone)")
```

### TWN-307 · row 22 · `twin init` → `facsimile init`
```case
kind:     process
setup:    (a) no facsimile.json · (b) an existing facsimile.json
action:   facsimile init
expect:   files      (a) a starter facsimile.json that Read-FxConfig accepts with no stop · (b) nothing written
          stdout     (a) DONE init · … · facsimile.json · exit 0 · (b) DONE init · facsimile.json is already present; nothing
                     was written · facsimile.json · exit 0
          exit       0 · 0
laws:     T3 E4
source:   expected · Twin parity: `twin init` writes a starter twin.json and, when one exists, prints "twin.json is already present; nothing was written." (src/Twin.Cli/Program.fs runInit, Render.initExists)
```

### TWN-308 · row 18 · `twin check` → acceptance, identity and determinism inside every verdict
```case
kind:     process
setup:    a team member who ran `twin check` before trusting a proof
action:   facsimile prove Dev · read verdict.Dev.json · prove Dev again
expect:   files      the verdict records acceptance ok, an empty identity plan, the touched tables' before and after hashes,
                     and the stamps that make a re-run comparable (H7) · the re-run is byte-identical but for time (D1) ·
                     there is no separate check verb; CI runs prove
laws:     T3 F1 F2 D1 H7
source:   expected · ARCH §2 row 18 · Twin anchor (receipt): TwinMintLoopTests "M5: the proof verb holds on a throwaway database"
```

### TWN-309 · row 21 · `twin bake` → nothing to bake
```case
kind:     process
setup:    a fresh machine
action:   facsimile prove Dev · facsimile prove Dev · facsimile bake
expect:   outcome    identical verdicts · the first run builds the base cache itself and the second restores it · there
                     is no artifact to download · `facsimile bake`, typed from habit: stop usage · "Unknown verb or argument
                     bake. Usage: <one line>." · exit 1
laws:     T3 D2 D5 A3
source:   expected · ARCH §2 row 21, §6 usage
note:     the Twin and prove.mjs also used exit 1 for an argument error.
```

### TWN-310 · row 14 · `twin evidence import|derive|verify` → `read`, `shape`, `status`
```case
kind:     process
setup:    pg stand-in environment Dev · a valid facsimile.json · a branch with a schema change
action:   facsimile read Dev · facsimile shape Dev dbo.Customer.Email · facsimile status
expect:   files      read: schema.Dev.dacpac, both deltas and answers.Dev.json (counts), in two touches (M3), as import's
                     successor · shape: the value-free profile of one column, as derive's successor for discovery ·
                     status: answers staleness per environment, as verify's coverage board · no rich pack anywhere
          stdout     DONE read · … · exit 0 · DONE shape · … · exit 0 · DONE status · … · exit 0
laws:     T3 M3 P1 P6
source:   expected · ARCH §8 · the Twin side: TwinEvidenceLoopTests "M3: import → derive → mint re-profiles to the source's shape"
```

### TWN-311 · row 15 · `twin classify` → nothing to classify
```case
kind:     process
setup:    a facsimile.json carrying a `corrections` key copied from twin.json
action:   facsimile doctor
expect:   outcome    stop config · "facsimile.json: $.corrections: unknown key" · exit 6 · without the key, done doctor · exit 0
laws:     T3 E4 P1 P5
source:   expected · ARCH §2 row 15
note:     no PII classification exists to maintain, because no environment value reaches the copy (P1, P5).
```

### TWN-312 · row 30 · G4: every schema PR records both verdicts
```case
kind:     process
setup:    the G4 window · a schema PR whose description carries only the facsimile's verdict line
action:   tests/run-cases.ps1 --lint on the PR
expect:   outcome    refused: a G4 schema PR carries both the Twin's and the facsimile's verdict lines · with both, it
                     passes
laws:     T4 E3
source:   expected · T4
```

### TWN-313 · row 13 · G4: a disagreement is a defect, adjudicated by the engine, never averaged
```case
kind:     manual
setup:    a G4 PR against a Dev whose FK_OrderLine_Order is untrusted over an orphan (TWN-119's shape); the Twin records
          clean, the facsimile blocked constraint Msg 547
action:   open a defect · publish head onto a full copy of Dev (the oracle), or read the environment's own Octopus result
expect:   the oracle blocks with 547 · the defect is filed against the Twin (forced trust, row 13) · the PR records the
          facsimile's verdict and links the defect · neither verdict is averaged or outvoted
laws:     T4 F3 F9
source:   expected · T4, F3
```

### TWN-314 · row 1 · G4: the laptop check; `twin up` users move to `facsimile up` on the corporate machine
```case
kind:     manual
setup:    corporate Windows laptop · VS 2022 MSBuild · LocalDB as installed · no Docker · ConstrainedLanguage mode
action:   facsimile doctor · up · up · prove Dev on one real change
expect:   doctor ends done, or names a stop (platform when LocalDB is older than the DSP) · up converges, then prints DONE up · Nothing to apply · prove reaches a verdict · every verb runs under ConstrainedLanguage (A2) · timings are recorded
          against ARCH §11.4 and §11.6
laws:     T3 T4 A2 S4
source:   verify · ARCH §11.4–11.6, §11.8 · TWIN_SUCCESSION Appendix C
```

### TWN-315 · row 25 · G4/G5: the skills switch after the cases are green, never before
```case
kind:     process
setup:    the ssdt-agent tree and the vendored bundle across G3, G4 and G5
action:   tests/run-cases.ps1 --lint · the bundle's text check
expect:   files      skills/talk-to-local-sql and skills/prove-on-dacpac change only in a PR where every TWN case is green
                     (G3) and the G4 window is open · after G5 no vendored file matches
                     `twin|docker|warm-sql|11433|\.fs\b|\.mjs` · THE_TWIN.md and TWIN_README.md carry the deprecation
                     banner · every retirement case states its reason, so nothing retires silently
laws:     T3 T1 T4
source:   expected · ARCH §10 · TWIN_SUCCESSION §7
```

### TWN-316 · row 22 · the exit codes the team knows map to codes that say who acts
```case
kind:     process
setup:    a valid repository and facsimile.json, then five faults introduced one at a time
action:   one run each: an unknown verb · a config typo · an unreachable environment · a project that does not build · an
          unknown statement
expect:   exit       1 (Twin: 1 argv) · 6 (Twin: 6) · 4 (Twin's 4 meant Docker; now an environment) · 7 · 9 (Twin's 9
                     "refused" becomes "needs the principal")
          stdout     one STOP line per run, naming its next step (A1, H3)
laws:     T3 A3 A1 H3
source:   expected · A3 · the Twin side: THE_TWIN §6 (0 done · 1 argv · 4 docker unavailable · 6 config · 9 refused)
```

### TWN-317 · row 17 · `up.sql` carries the pins `twin.json` scenarios carried
```case
kind:     process
setup:    tw · fx_local converged · up.sql: `-- plants: dbo.Customer` then Customer Id 1000 (IDENTITY_INSERT) Name
          N'wCanonical Test Customer', Email N'wcanon@example.invalid', StatusId 1
action:   facsimile up · facsimile up · edit up.sql (Name N'wRenamed') · facsimile up
expect:   copy       fx_local's Customer is exactly the declared row, Id 1000, byte-identical · the k planted Order rows point
                     at it (children spread over the existing parents) · its StatusId names a lane row
          stdout     DONE up · … · exit 0 · DONE up · Nothing to apply · .facsimile/out/up.json · exit 0 · then a re-converge
                     carrying the renamed pin
laws:     T3 D4 P5
source:   expected · ARCH §2 rows 17 and 22, §8 (`up`: then up.sql, declared rows and pins, DML only) · Twin anchor
          (receipt): TwinMintLoopTests "M4: a scenario shapes the mint — weights, window, ratio, and pins land" (the pin
          lands verbatim and referenceable)
note:     up.sql is DML only, like witness.sql, and is an input to `up`'s fingerprint, so an edit re-converges (D4).
```

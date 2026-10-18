# 42 · Operations on tables, seeds, structure and multi-phase programs

*Op cases for the 15 table-grain operations, in `README.md`'s grammar and standard tags; one `##` heading per operation, naming its skill, its record and its Twin test class.*
*Unless `env` says otherwise, the environment proven is Dev (QA, UAT and Prod unread), the effective options are the pipeline's (BlockOnPossibleDataLoss=true, GenerateSmartDefaults=false, DropObjectsNotInSource=false, IncludeTransactionalScripts=true), and reference rows come from the `referenceData` lane as of base.*
*`notChecked` tokens: unread environment names, `volume`, `cdc`, `workload`, `platform`, `over-time`, `engine`, `is-refresh` (the Integration Studio refresh). `laws` cite INVARIANTS; bare `F<n>` in `source` cites FINDINGS_AND_CHANGES.md.*

---

## create-entity

`skills/op/create-entity` · `sample-prs/create-entity.md` · Twin `SamplePrCleanApply2Tests`

### OP-create-entity-01 · a standalone new table
```case
kind:     corpus
tags:     empty
env:      pg · Customer 6 rows
change:   head + Modules/CustomerPreference.sql: Id int IDENTITY PK · PrefKey nvarchar(50) NOT NULL ·
          PrefValue nvarchar(200) NULL · scripts unchanged
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {adds CustomerPreference}
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F4
source:   expected · skills/op/create-entity ("emitted verbatim, and no existing data is read or written")
note:     clean, violating and remedy cannot occur: the table exists in no environment, so no existing row can
          be read. The delta is one CREATE TABLE, and the tier is read off it.
```

### OP-create-entity-02 · a foreign key to a populated parent (Twin parity)
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   head + CustomerPreference (Id IDENTITY PK · CustomerId int NOT NULL · PrefKey · PrefValue)
          with FK_CustomerPreference_Customer → Customer(Id)
expect:   questions  none: the delta's WITH NOCHECK ADD + WITH CHECK CHECK validate CustomerPreference, which the
                     same delta creates, so no existing row is read and Customer is not touched
          witnesses  none
          outcome    verdict clean · exit 0
          copy       CustomerPreference 0 rows · FK_CustomerPreference_Customer is_not_trusted=0
          record     tier T0 · ships one release, in place
                     · effects {adds CustomerPreference, FK_CustomerPreference_Customer}
                     · notChecked {QA, UAT, Prod, engine, is-refresh}
laws:     F3 F4 T2
source:   receipt · SamplePr "create-entity: a new table with an identity PK and an FK to Customer applies clean"
note:     The CHECK CHECK matches the `Orphan` shape but names a table the environment lacks; it must yield no
          question, not S3's missing-object stop. `engine`: trust on a declarative add is DacFx-dependent (FINDINGS F19).
```

### OP-create-entity-03 · a unique index on the new table
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   head + CustomerPreference (as -02) + Indexes/UX_CustomerPreference_Key.sql:
          CREATE UNIQUE INDEX UX_CustomerPreference_Key ON dbo.CustomerPreference (CustomerId, PrefKey)
expect:   questions  none: the unique build reads only CustomerPreference, which this delta creates
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place
                     · effects {adds CustomerPreference, UX_CustomerPreference_Key}
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     F4
source:   expected · ARCH §4 (`Unique` row) and §7 (T0: no statement reads an existing row)
note:     Declared in its own file, the index is a separate `CREATE UNIQUE INDEX` batch after the CREATE TABLE, so
          the vocabulary sees the `Unique` shape and must place it on a delta-created table.
```

### OP-create-entity-04 · a foreign key to a parent the project does not have
```case
kind:     corpus
tags:     edge
env:      pg
change:   head + CustomerPreference with FK_CustomerPreference_Member → dbo.Member(Id); no project file declares
          dbo.Member
expect:   outcome    stop build-failed · "The project does not build: SQL71501 (unresolved reference [dbo].[Member])" · exit 7
laws:     H3 A3
source:   expected · skills/op/create-entity (named trap: "the build fails — not the deploy")
note:     No environment is read: the missing parent is a dependency fault caught before any delta exists.
```

### OP-create-entity-05 · the new file is not part of the build
```case
kind:     corpus
tags:     edge
env:      pg
change:   Modules/CustomerPreference.sql added to the folder but not included by SampleCatalog.sqlproj
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · effects {} · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3
source:   expected · skills/op/create-entity ("a file the project glob misses silently never deploys")
note:     The facsimile proves the delta it is given. An empty `effects` on a pull request that says "add an
          entity" is the reviewer's cue; it is not a stop.
```

### OP-create-entity-06 · a table of the same name already exists in one environment
```case
kind:     corpus
tags:     edge
env:      Dev: pg · QA: pg + hand-made CustomerPreference (Id, CustomerId, PrefKey, PrefValue, Notes nvarchar(max) NULL),
          4 rows, every CustomerId a real Customer
change:   head + CustomerPreference with FK_CustomerPreference_Customer (as -02); no Notes column
expect:   answers    QA: HasRows(CustomerPreference)=yes · Orphan(CustomerPreference.(CustomerId)→Customer)=clean
          questions  Dev: none · QA: HasRows(CustomerPreference) (the guard above DROP COLUMN Notes)
                     · Orphan(CustomerPreference.(CustomerId)→Customer)
          witnesses  Dev: none · QA: presence(CustomerPreference) (+ancestor Customer)
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked guard · Msg 50000/127 · dbo.CustomerPreference · exit 3
          record     Dev tier T0 · QA tier T2 · two proofs (the delta hashes differ)
                     · notChecked {UAT, Prod, over-time, is-refresh}
laws:     F7 F1 H5
source:   expected · red-team #1 (proving against main misses the delta each environment receives, drift included)
note:     A column is not an "object not in source", so QA's DROP COLUMN is generated whatever DropObjectsNotInSource
          says. Only QA's own extracted schema shows it.
```

---

## delete-entity

`skills/op/delete-entity` · `sample-prs/delete-entity.md` · Twin `SamplePrRemovalTests`

### OP-delete-entity-01 · populated, removed from source: a phantom removal (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · OrderLine 8 rows · options DropObjectsNotInSource=false
change:   head − Modules/OrderLine.sql · scripts unchanged
expect:   questions  none: the delta carries no statement for OrderLine
          witnesses  none
          outcome    verdict clean · exit 0
          copy       OrderLine still present, object_id unchanged
          record     tier T0 · ships a phantom removal: the publish is green and OrderLine survives with its rows
                     · effects {} · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F6 T2
source:   receipt · SamplePr "delete-entity: removing a whole table from source is a phantom removal under
          DropObjectsNotInSource=false; the scripted DROP TABLE is the real, destructive one"
note:     The same with OrderLine empty. Head no longer declares the table while `effects` is empty: that gap is the
          record's signal that nothing was removed.
```

### OP-delete-entity-02 · populated, drop-enabled options: the guard blocks
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · options DropObjectsNotInSource=true
change:   head − Modules/OrderLine.sql
expect:   answers    HasRows(OrderLine)=yes
          questions  HasRows(OrderLine)
          witnesses  presence(OrderLine)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     tier T1 · ships one release, scripted (no declarative drop passes the locked gate over rows)
                     · residue {} · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F1 F6 H5 H6
source:   expected · skills/op/delete-entity (diagnostic posture: "a Strict publish must block"); recorded live in
          sample-prs/delete-entity.md (SQL72015, then Msg 50000), which is not a README §4 receipt form
note:     The effective options decide (INVARIANTS F6): under the pipeline's options the same head is -01's phantom.
          A drop asks only HasRows, so no `violating` class exists for this operation.
```

### OP-delete-entity-03 · empty, drop-enabled options: the table drops
```case
kind:     corpus
tags:     empty
env:      pg · OrderLine 0 rows · options DropObjectsNotInSource=true
change:   head − Modules/OrderLine.sql
expect:   answers    HasRows(OrderLine)=no
          questions  HasRows(OrderLine)
          witnesses  none
          outcome    verdict clean · exit 0
          copy       OrderLine absent
          record     tier T1 · ships one release, in place · effects {removes OrderLine}
                     · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F1 F6 M2
source:   expected · ARCH §4 (`HasRows`; a "no" plants nothing); skills/op/delete-entity ("table empty … a clean drop")
note:     Dev's "no" says nothing of Prod: the unread environments and `over-time` stay on the record, and the
          approval still weighs an irreversible drop.
```

### OP-delete-entity-04 · remedy: a scripted, idempotent pre-deploy DROP TABLE, the file removed in the same release
```case
kind:     corpus
tags:     remedy
env:      pg · OrderLine 8 rows · options DropObjectsNotInSource=false
change:   head − Modules/OrderLine.sql · Script.PreDeployment.sql +
          `IF OBJECT_ID(N'dbo.OrderLine', N'U') IS NOT NULL DROP TABLE dbo.OrderLine;` · Data/Seed.sql − its OrderLine MERGE
expect:   questions  Authored(Script.PreDeployment.sql) · Authored(Data/Seed.sql)
          witnesses  run 2: declared(presence(OrderLine)) under `-- plants: OrderLine`
          outcome    stop authored-script · "the change runs authored SQL over dbo.OrderLine; declare what it handles" · exit 5
                     then, with witness.sql: verdict clean · exit 0
          copy       OrderLine absent · a second publish: plan empty, and the pre-deploy's guard skips the drop
          record     tier T3 · ships one release, scripted · effects {removes OrderLine}
                     · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     H3 A5 F2 M2
source:   expected · FINDINGS Part 4 (delete-entity row, proven 2026-08-21); that the scripted drop removes the
          populated table is receipt in SamplePr "delete-entity: removing a whole table from source is a phantom removal
          under DropObjectsNotInSource=false; the scripted DROP TABLE is the real, destructive one"
note:     The script reads no column, so `shape` has nothing to profile and the declaration is presence alone. The
          data-loss gate never governs raw T-SQL: the drop's safety rests on the reference check and the approval.
```

### OP-delete-entity-05 · edge: the drop while the model still declares the table
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows
change:   program R1: Modules/OrderLine.sql kept · pre-deploy guarded DROP TABLE dbo.OrderLine (as -04)
expect:   questions  Authored(Script.PreDeployment.sql)
          witnesses  declared(presence(OrderLine))
          outcome    after the authored stop: R1 verdict clean · exit 0 (OrderLine dropped)
                     R1 replayed: plan non-empty, CREATE TABLE [dbo].[OrderLine] (0 rows, a new object_id): the F17 hazard
          record     tier T3 · ships the drop and the .sql removal in one release, never apart
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     F8 F3
source:   expected · FINDINGS Part 4 ("a model still holding the table re-creates it empty on the next publish"),
          recorded live in sample-prs/delete-entity.md (new object_id 1253579504)
note:     The lag that lets a narrow ship as two releases does not transfer to a table drop; the replay shows why.
```

### OP-delete-entity-06 · edge: an inbound foreign key not dropped first
```case
kind:     corpus
tags:     edge
env:      pg + Return (Id IDENTITY PK, OrderLineId int NOT NULL, FK_Return_OrderLine → OrderLine) · OrderLine 8 rows
          · Return 2 rows
change:   head − Modules/OrderLine.sql − FK_Return_OrderLine · pre-deploy guarded DROP TABLE dbo.OrderLine with no
          foreign-key drop before it
expect:   questions  Authored(Script.PreDeployment.sql)
          witnesses  declared(none: the failure does not depend on rows)
          outcome    after the authored stop: stop unclassified · "the publish ended with Msg 3726, which no rule classifies" · exit 9
laws:     H5 H2 E1
source:   expected · skills/op/delete-entity ("the drop fails until they are dropped first"); ARCH §6 lists no rule for 3726
note:     The plan drops FK_Return_OrderLine in the main script, after the pre-deploy has already failed. The ordered
          script (the key, then the table) is clean; until a 3726 rule lands with this case, the closed list stops.
```

### OP-delete-entity-07 · edge: an environment-only table dropped silently under drop-enabled options
```case
kind:     corpus
tags:     edge
env:      QA: pg + LegacyAudit (declared by no project file) 0 rows · options DropObjectsNotInSource=true
change:   head + Customer.MiddleName nvarchar(50) NULL; nothing else
expect:   answers    HasRows(LegacyAudit)=no
          questions  HasRows(LegacyAudit)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T1 · effects {adds Customer.MiddleName, removes LegacyAudit}
                     · notChecked {UAT, Prod, over-time, is-refresh}
laws:     F7 F6 F3
source:   receipt · red-team #14 ("environment-only objects dropped silently ✓")
note:     The drop is listed though the pull request never names LegacyAudit. Populated, the same QA blocks on a table
          nobody touched. Both faces exist only in QA's own delta.
```

---

## rename-entity

`skills/op/rename-entity` · `skills/_index/identity-and-refactorlog` · `sample-prs/rename-entity.md` · Twin `SamplePrRenameTests`

### OP-rename-entity-01 · populated, with its refactorlog entry: sp_rename
```case
kind:     corpus
tags:     clean
env:      pg · OrderLine 8 rows
change:   OrderLine.sql re-headed [dbo].[OrderItem] · SampleCatalog.refactorlog + Rename Refactor
          [dbo].[OrderLine] → [OrderItem] · constraint names unchanged
expect:   questions  none: `EXECUTE sp_rename … @objtype = N'OBJECT'` reads no row
          witnesses  none
          outcome    verdict clean · exit 0
          copy       OrderItem present with OrderLine's object_id · OrderLine absent
          record     tier T0 · ships one release, in place · effects {renames OrderLine→OrderItem}
                     · notChecked {QA, UAT, Prod, cdc, is-refresh}
laws:     F3 F4
source:   expected · skills/_index/identity-and-refactorlog; recorded live in sample-prs/rename-entity.md (sp_rename,
          object_id 1061578820 kept)
note:     This entry is also the remedy for -02 and -03. A rename asks the data nothing, so no `violating` class
          exists. `cdc` stays named: a CDC-tracked table refuses sp_rename (-06).
```

### OP-rename-entity-02 · no entry, drops off: a phantom rename (Twin parity)
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows
change:   OrderLine.sql re-headed [dbo].[OrderItem] · no refactorlog entry
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          copy       OrderItem 0 rows · OrderLine present, object_id unchanged
          record     tier T0 · ships a phantom rename: OrderItem created empty, OrderLine stranded with its rows
                     · effects {adds OrderItem} · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F6 T2
source:   receipt · SamplePr "rename-entity: a bare table rename (no refactorlog) is a phantom (new empty table, old
          survives); EXEC sp_rename preserves object_id and all rows"; red-team #14
note:     A green publish with `renames` empty on a change meant as a rename is what the record shows; the stranded
          data is red-team #14's phantom.
```

### OP-rename-entity-03 · no entry, drop-enabled options: the guard blocks
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · options DropObjectsNotInSource=true
change:   as -02
expect:   answers    HasRows(OrderLine)=yes
          questions  HasRows(OrderLine) (the guard above DROP TABLE [dbo].[OrderLine], before CREATE TABLE [dbo].[OrderItem])
          witnesses  presence(OrderLine)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     tier T1 · residue {} (OrderItem never created) · notChecked {QA, UAT, Prod, is-refresh}
laws:     F1 F6 H5 H6
source:   expected · skills/_index/identity-and-refactorlog (DROP TABLE + CREATE TABLE without the entry); recorded live
          in sample-prs/rename-entity.md ("OrderLine survived with 8 rows; OrderItem was never created")
```

### OP-rename-entity-04 · no entry: empty in Dev passes, populated in QA blocks
```case
kind:     corpus
tags:     empty
env:      Dev: pg · OrderLine 0 rows · QA: pg · OrderLine 8 rows · options DropObjectsNotInSource=true
change:   as -02
expect:   answers    Dev: HasRows(OrderLine)=no · QA: HasRows(OrderLine)=yes
          questions  HasRows(OrderLine)
          witnesses  Dev: none · QA: presence(OrderLine)
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     Dev effects {removes OrderLine, adds OrderItem} · two proofs (the answers hashes differ)
laws:     F7 F1
source:   expected · ARCH §4 (`HasRows`); skills/_index/identity-and-refactorlog
note:     Dev's green verdict records a drop and a create, never a rename: `renames` is empty even where nothing
          blocked, which is the reviewer's cue that the entry is missing.
```

### OP-rename-entity-05 · constraint names renamed too, without entries of their own: a rebuild
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · Id IDENTITY 1–8
change:   as -01, plus PK_OrderLine_Id → PK_OrderItem_Id and DF_OrderLine_Amount → DF_OrderItem_Amount in the CREATE,
          with no refactorlog entries for them
expect:   answers    HasRows(OrderLine)=yes · Fits {} for every moved column · Gapped(OrderLine)=no
          questions  Rebuild(OrderItem), resolved to the environment's OrderLine through the delta's own sp_rename
          witnesses  presence(OrderLine)
          outcome    verdict clean · exit 0
          copy       OrderItem holds the planted row · its object_id differs from OrderLine's before
          record     tier T2 · ships one release · effects {renames OrderLine→OrderItem, modifies OrderItem (rebuilt)}
                     · notChecked {QA, UAT, Prod, volume, is-refresh}
laws:     F1 F4 S3
source:   expected · recorded live in sample-prs/rename-entity.md ("tmp_ms_xx_OrderItem … the 8 rows still survived,
          but the object_id changed to 1253579504")
note:     The rebuild names OrderItem, which the environment knows as OrderLine. Unless questions resolve through a
          rename earlier in the same delta, S3 stops missing-object; ARCH §4 does not yet state that rule.
```

### OP-rename-entity-06 · edge: a CDC-tracked table
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · Cdc(OrderLine)=yes
change:   as -01
expect:   answers    Cdc(OrderLine)=yes
          outcome    stop cdc · "OrderLine is tracked by CDC, and LocalDB has none. This change is principal-led." · exit 9
laws:     H2 H3
source:   expected · ARCH §6 (`cdc` stop); the engine fact it guards, sp_rename failing Msg 4928 on a CDC-tracked
          table, is red-team #6 ✓
note:     The copy has no CDC, so publishing would report a false clean.
```

### OP-rename-entity-07 · edge: a deleted refactorlog entry, proven per environment
```case
kind:     corpus
tags:     edge
env:      Dev: pg with OrderItem 8 rows (the rename already applied) · UAT: pg with OrderLine 8 rows (not yet received)
change:   head declares [dbo].[OrderItem]; its Rename Refactor entry has been deleted from SampleCatalog.refactorlog
expect:   questions  none
          outcome    Dev: verdict clean · exit 0 (plan empty)
                     UAT: verdict clean · exit 0 (OrderItem created empty, OrderLine stranded)
          record     Dev effects {} · UAT effects {adds OrderItem} · two proofs
laws:     F7 F3
source:   expected · skills/_index/identity-and-refactorlog (Refactorlog Cleanup: "a deleted entry re-becomes a drop+create")
note:     Proving Dev alone (or main) reports nothing; only UAT's own delta shows the stranded table.
```

### OP-rename-entity-08 · the offline delta under a refactorlog
```case
kind:     corpus
tags:     edge
env:      Dev: pg · OrderLine 8 rows · QA: pg · OrderItem 8 rows, `__RefactorLog` already holding the entry's key
change:   as -01
expect:   files      delta.Dev.sql carries the sp_rename · delta.QA.sql carries no rename statement · each equals the
                     live Script of head against that environment, after normalization
          outcome    Dev: verdict clean · exit 0 · QA: verdict clean · exit 0
laws:     F5 F7
source:   verify · whether Script(head, extracted schema) consults the target's applied refactorlog keys as the live
          Script does: an Extract carries schema, not `__RefactorLog` rows, and F5's receipt (TWIN_SUCCESSION §0.1)
          covered no refactorlog
```

---

## move-schema

`skills/op/move-schema` · `skills/_index/identity-and-refactorlog` · `sample-prs/move-schema.md` · Twin `SamplePrSchemaChangeTests`

### OP-move-schema-01 · header edit, no entry, drops off: a phantom move (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · OrderLine 8 rows · no schema `sales`
change:   head + CREATE SCHEMA sales · OrderLine.sql re-headed [sales].[OrderLine] · no refactorlog entry
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          copy       sales.OrderLine 0 rows · dbo.OrderLine present, object_id unchanged
          record     tier T0 · ships a phantom move: sales.OrderLine created empty, dbo.OrderLine stranded
                     · effects {adds sales, sales.OrderLine} · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F6 T2
source:   receipt · SamplePr "move-schema: a header-edit move without a refactorlog is a phantom move; ALTER SCHEMA
          TRANSFER is the real one"
note:     A move asks the data nothing, so no `violating` class exists; the remedies are -03 and -04.
```

### OP-move-schema-02 · header edit, no entry, drop-enabled options: the guard blocks
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · options DropObjectsNotInSource=true
change:   as -01
expect:   answers    HasRows(OrderLine)=yes
          questions  HasRows(OrderLine) (the guard above DROP TABLE [dbo].[OrderLine])
          witnesses  presence(OrderLine)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     tier T1 · residue {} · notChecked {QA, UAT, Prod, is-refresh}
laws:     F1 F6 H5
source:   expected · skills/_index/identity-and-refactorlog ("for a schema move it is the same shape on the two-part name")
```

### OP-move-schema-03 · the refactorlog entry carries the move
```case
kind:     corpus
tags:     remedy
env:      pg · Category 3 rows (lane)
change:   head + CREATE SCHEMA archive · Category.sql re-headed [archive].[Category] · a refactorlog move entry for
          [dbo].[Category] → schema [archive] · extract style: no scripts
expect:   questions  none: the move reads no row
          witnesses  none
          outcome    verdict clean · exit 0
          copy       archive.Category present with dbo.Category's object_id · dbo.Category absent
          record     tier T0 · ships one release, in place · effects {adds archive, renames dbo.Category→archive.Category}
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F4
source:   verify · which statement DacFx emits for a refactorlog schema move: the skills name both a schema-qualified
          sp_rename and `ALTER SCHEMA … TRANSFER`, and the live proof (sample-prs/move-schema.md) ran the transfer by
          hand, not through a publish
note:     The record must call this a rename (identity kept), not a removal plus an addition, whichever statement
          the first run shows.
```

### OP-move-schema-04 · the scripted transfer in the same release as the header edit
```case
kind:     corpus
tags:     remedy
env:      pg · Category 3 rows (lane)
change:   Category.sql re-headed [archive].[Category] · no refactorlog entry · pre-deploy:
          `IF SCHEMA_ID(N'archive') IS NULL EXEC(N'CREATE SCHEMA archive');` then
          `IF OBJECT_ID(N'dbo.Category') IS NOT NULL ALTER SCHEMA archive TRANSFER dbo.Category;`
expect:   questions  Authored(Script.PreDeployment.sql)
          witnesses  declared(none: the transfer moves whatever rows exist)
          outcome    after the authored stop: the plan, computed before the pre-deploy ran, still creates the schema
                     and the table; the main script fails Msg 2714 (already exists) → stop unclassified · exit 9
laws:     H5 E1 F4
source:   verify · F2 is the receipt that the plan is computed before the pre-deploy runs; the failing statement and
          its number (2714 on CREATE SCHEMA or on CREATE TABLE) are unproven
note:     Read with -03: the single-release shape is the refactorlog entry. A scripted transfer belongs outside this
          release's model edit (an out-of-DACPAC step, then the model), never beside it.
```

### OP-move-schema-05 · empty, no entry, drop-enabled options: drop and create pass
```case
kind:     corpus
tags:     empty
env:      pg · Category 0 rows · options DropObjectsNotInSource=true
change:   as -03 without the refactorlog entry
expect:   answers    HasRows(Category)=no
          questions  HasRows(Category)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T1 · effects {adds archive, archive.Category, removes dbo.Category}
                     · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F1 F7
source:   expected · skills/op/move-schema; ARCH §4 (`HasRows`)
note:     Nothing is lost in Dev only because Dev is empty; `renames` stays empty, and a populated environment
          blocks (-02) or strands its rows (-01).
```

### OP-move-schema-06 · edge: the unchanged seed still names the old two-part name
```case
kind:     corpus
tags:     edge
env:      pg · Category 3 rows (lane) · project style, Data/Seed.sql MERGEs dbo.Category
change:   as -03 (the refactorlog move) · Data/Seed.sql unchanged, still `MERGE dbo.Category …`
expect:   questions  none: the move reads no row, and the seed did not change, so it is not `Authored`
          witnesses  none
          outcome    the main script moves Category; the post-deploy fails Msg 208 on dbo.Category
                     → stop unclassified · "the publish ended with Msg 208, which no rule classifies" · exit 9
laws:     H5 F4 E1
source:   expected · F15 (the seed's claims bind every atom in the release: the rename face failed until the seed's
          column references were renamed); this is the table-grain face, and ARCH §6 lists 207 but not 208
note:     A post-deploy runs in full on every publish, so a script that did not change can still fail against a
          schema that did; tier T0 does not mean nothing can fail.
```

### OP-move-schema-07 · edge: a question on the moved table names its new address
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows
change:   a refactorlog move dbo.OrderLine → sales.OrderLine and OrderLine.Amount decimal(18,2) → decimal(9,2), one release
expect:   answers    HasRows(OrderLine)=yes · Fits(OrderLine.Amount→decimal(9,2))={}
          questions  HasRows(sales.OrderLine) · Fits(sales.OrderLine.Amount→decimal(9,2)), each resolved to the
                     environment's dbo.OrderLine through the delta's own move
          witnesses  presence(OrderLine)
          outcome    verdict blocked guard · Msg 50000/127 · sales.OrderLine · exit 3
          record     tier T2 · residue {} (the move rolls back with the guard)
laws:     S3 F4 F1
source:   verify · whether DacFx orders the move before the guarded ALTER (so the guard names [sales].[OrderLine]);
          ARCH §4 and S3 give no rule for a question whose object this same delta renames or moves
```

---

## junction

`skills/op/junction` · `sample-prs/junction.md` · Twin `SamplePrReferenceIntegrityTests`

### OP-junction-01 · a new bridge and parent, both legs trusted (Twin parity)
```case
kind:     corpus
tags:     empty
env:      pg · Order 6 rows
change:   head + Tag (Id IDENTITY PK, Name nvarchar(50) NOT NULL) + OrderTag (OrderId, TagId,
          PK_OrderTag (OrderId, TagId), FK_OrderTag_Order → Order, FK_OrderTag_Tag → Tag)
expect:   questions  none: both WITH CHECK CHECK validate OrderTag, which this delta creates
          witnesses  none
          outcome    verdict clean · exit 0
          copy       OrderTag 0 rows · PK over 2 columns · both FKs is_not_trusted=0
          record     tier T0 · ships one release, in place · effects {adds Tag, OrderTag}
                     · notChecked {QA, UAT, Prod, engine, is-refresh}
laws:     F3 F4 T2
source:   receipt · SamplePr "junction: a new bridge with two FKs and a composite PK applies clean, both legs trusted"
note:     `clean` cannot occur for a bridge no environment has (drift aside, -06). A parent over 1M rows raises no
          `scale` flag: the validation reads only the empty bridge, so the skill's ">1M rows" line is the lead's.
```

### OP-junction-02 · remedy: seed pairs whose parents all exist
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 6 rows (Id 1–6) · Product 5 rows (Id 1–5)
change:   head + CustomerProduct (CustomerId, ProductId, composite PK, FKs to Customer and Product) ·
          Data/Seed.sql + guarded MERGE of pairs (1,1), (1,2), (2,1)
expect:   questions  Authored(Data/Seed.sql)
          witnesses  run 2: declared(Customer Id 1, 2 and Product Id 1, 2 present) under `-- plants: Customer`,
                     `-- plants: Product`
          outcome    stop authored-script · "the seed names Customer and Product ids; declare which exist" · exit 5
                     then, with witness.sql: verdict clean · exit 0
          copy       CustomerProduct 3 rows · a second publish touches 0 rows
          record     tier T3 · ships one release (schema, then the post-deploy seed)
                     · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     H3 A5 M2 F2
source:   expected · skills/op/junction ("every pair needs a real row on both sides, or the create is blocked")
note:     No vocabulary question touches the parents, so their rows are declared, beyond M2's minimum. The planter's
          ordinal ids need not equal the seed's constants, which is why they cannot stand in.
```

### OP-junction-03 · violating: a seed pair names a parent the environment lacks
```case
kind:     corpus
tags:     violating:orphan
env:      pg · Customer 6 rows · Product 5 rows, no Id 999
change:   as -02, plus the pair (1, 999)
expect:   questions  Authored(Data/Seed.sql)
          witnesses  declared(Customer 1 present; Product 999 absent)
          outcome    after the authored stop: verdict blocked constraint · Msg 547 · FK_CustomerProduct_Product · exit 3
          record     tier T3 · residue {main: CustomerProduct, PK, both FKs applied and trusted}
                     · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     H6 F1 H5
source:   expected · skills/op/junction (an orphan pair is rejected Msg 547); that the main script has committed when
          the post-deploy fails is F15's (a failed seed on a half-applied release)
```

### OP-junction-04 · per environment: a seed's parent id exists in Dev, not in QA
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Product Id 1–5 · QA: pg · Product Id 1, 3, 4, 5, 6 (Id 2 deleted long ago)
change:   as -02
expect:   witnesses  Dev: declared(Product 2 present) · QA: declared(Product 2 absent)
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked constraint · Msg 547 · FK_CustomerProduct_Product · exit 3
          record     two proofs (the witness.sql hashes differ)
laws:     F7 F1
source:   expected · skills/op/junction ("QA, UAT, and Prod may hold parent rows this copy cannot see")
note:     Business-row ids are per environment; a seed that names them is proven per environment or not at all.
```

### OP-junction-05 · edge: a duplicate pair in the seed
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows · Product 5 rows
change:   as -02, the seed's VALUES listing (1,1) twice
expect:   witnesses  declared(Customer 1 and Product 1 present)
          outcome    after the authored stop: verdict blocked unique · Msg 2627 · PK_CustomerProduct · exit 3
          record     tier T3 · residue {main: CustomerProduct, PK, both FKs applied}
laws:     H5 H6
source:   expected · ARCH §6 (2627 on insert); that the composite PK rejects a duplicate pair with Msg 2627 is receipt
          in SamplePr "junction: a new bridge with two FKs and a composite PK applies clean, both legs trusted"
```

### OP-junction-06 · edge: QA already holds a hand-made bridge with an orphan pair
```case
kind:     corpus
tags:     edge
env:      Dev: pg · QA: pg + hand-made CustomerProduct (CustomerId int NOT NULL, ProductId int NOT NULL; no keys)
          3 rows, one pair on Product 999
change:   head + CustomerProduct with its composite PK and both FKs (as -02, no seed)
expect:   answers    QA: Unique(CustomerProduct,(CustomerId, ProductId))={}
                     · Orphan(CustomerProduct.(CustomerId)→Customer)=clean
                     · Orphan(CustomerProduct.(ProductId)→Product)=violating
          questions  Dev: none · QA: Unique(CustomerProduct,(CustomerId, ProductId)) · both Orphan legs
          witnesses  QA: presence(CustomerProduct) (+ancestors Customer, Product) · orphan(CustomerProduct.(ProductId)→Product)
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked constraint · Msg 547 · FK_CustomerProduct_Product · exit 3
          record     QA tier T2 · residue {main: PK built, FK_CustomerProduct_Customer trusted,
                     FK_CustomerProduct_Product present with is_not_trusted=1}
laws:     F7 F1 H6
source:   expected · red-team #1 (drift); the untrusted half-applied key after a 547 at WITH CHECK CHECK is F9
          (db_orphD) and red-team #4 ✓
```

---

## temporal-new

`skills/op/temporal-new` · `sample-prs/temporal-new.md` · Twin `SamplePrCleanApply2Tests`

### OP-temporal-new-01 · a new system-versioned table (Twin parity)
```case
kind:     corpus
tags:     empty
env:      pg · Customer 6 rows
change:   head + Rate (Id int PK · Amount decimal(18,4) · ValidFrom/ValidTo datetime2 GENERATED ALWAYS AS ROW
          START/END · PERIOD FOR SYSTEM_TIME · SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.Rate_History))
expect:   questions  none: the system-versioned CREATE reads no existing row
          witnesses  none
          outcome    verdict clean · exit 0
          copy       Rate temporal_type=2 · Rate_History temporal_type=1, linked · 2 GENERATED ALWAYS columns
          record     tier T0 · ships one release, in place · effects {adds Rate, Rate_History}
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F4 T2
source:   receipt · SamplePr "temporal-new: a new system-versioned entity applies clean with its history table"
note:     clean, violating and remedy cannot occur for a table no environment has; an existing table is
          temporal-convert.
```

### OP-temporal-new-02 · on LocalDB, the laptop's engine
```case
kind:     manual
tags:     edge
setup:    a team laptop: LocalDB 2022 (16.x) · the project's DSP Sql160 · `facsimile doctor` green
action:   facsimile prove Dev, over -01's change
expect:   outcome    verdict clean · exit 0
          record     stamped with the LocalDB version and edition · no `older-localdb` flag
laws:     S4 H7
source:   expected · Microsoft documentation: system-versioned temporal tables are available in every edition from
          SQL Server 2016, Express and LocalDB included
```

### OP-temporal-new-03 · per environment: QA already has a plain table of that name
```case
kind:     corpus
tags:     edge
env:      Dev: pg · QA: pg + hand-made Rate (Id int PK, Amount decimal(18,4)), 3 rows, not system-versioned
change:   as -01
expect:   questions  Dev: none · QA: the conversion of a populated table (see OP-temporal-convert-01)
          outcome    Dev: verdict clean · exit 0
                     QA: as OP-temporal-convert-01 · stop unknown-statement · exit 9
laws:     F7 F4 H2
source:   verify · as OP-temporal-convert-01 (the conversion delta's shape is unrecorded)
note:     The same head is a create in Dev and a conversion in QA; only QA's extracted schema says which.
```

### OP-temporal-new-04 · a later change on the populated temporal table
```case
kind:     corpus
tags:     edge
env:      pg + Rate (system-versioned, as -01) 4 rows
change:   Rate.Amount decimal(18,4) → decimal(9,2)
expect:   answers    HasRows(Rate)=yes · Fits(Rate.Amount→decimal(9,2))={}
          questions  HasRows(Rate) · Fits(Rate.Amount→decimal(9,2))
          witnesses  presence(Rate), its period columns left to the engine
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Rate · exit 3
laws:     F1 F4
source:   verify · whether DacFx alters a system-versioned column in place (history table alongside) or rebuilds
          behind SYSTEM_VERSIONING = OFF; and that a presence row never names a GENERATED ALWAYS column (Msg 13536)
```

---

## temporal-convert

`skills/op/temporal-convert` · `skills/_index/multi-phase` · `sample-prs/temporal-convert.md` · Twin `SamplePrRebuildTests`

### OP-temporal-convert-01 · populated: the declarative conversion in one release
```case
kind:     corpus
tags:     clean
env:      pg · Customer 6 rows
change:   Customer.sql + ValidFrom/ValidTo datetime2 GENERATED ALWAYS AS ROW START/END NOT NULL (no defaults) ·
          PERIOD FOR SYSTEM_TIME · SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.Customer_History)
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer) (the guard above the NOT NULL period columns) · the versioning switch
                     over existing rows (`ADD PERIOD` / `SET (SYSTEM_VERSIONING = ON)`) matches no vocabulary row
          outcome    stop unknown-statement · "Delta line <n> reads rows, but no vocabulary row matches it" · exit 9
laws:     F4 H2 E1
source:   verify · the delta is unrecorded: SamplePr "temporal-convert: turning the existing populated Customer
          system-versioned — the true outcome under the production gate, existing rows preserved" passes on either
          path and records no verdict, while sample-prs/README says "single publish blocks (no-default period columns)"
note:     The first run decides the vocabulary row this needs: a guard plus in-place ALTERs (then the expected verdict
          is blocked guard · dbo.Customer), or a tmp_ms_xx rebuild, which `Rebuild` already matches. No value class
          makes a conversion refuse a row; what a row means (its ROW START) is -05's.
```

### OP-temporal-convert-02 · an empty table converts in place
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   as -01
expect:   answers    HasRows(Customer)=no
          questions  as -01
          outcome    stop unknown-statement · exit 9 until the vocabulary row of -01 lands; then verdict clean · exit 0
          record     (after the row) tier T1 or T2 by that row · effects {modifies Customer, adds Customer_History}
laws:     F4 E1
source:   verify · as -01; skills/op/temporal-convert ("existing empty table → collapses to a single schema change")
note:     INVARIANTS F4 does not depend on data: an unmatched row-reading statement stops even over an empty table.
```

### OP-temporal-convert-03 · remedy: the staged conversion with historical defaults
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 6 rows
change:   program R1: model unchanged; pre-deploy, guarded by `COL_LENGTH('dbo.Customer','ValidFrom') IS NULL`, adds
          ValidFrom DEFAULT '2020-01-01' and ValidTo DEFAULT '9999-12-31 23:59:59.9999999' (GENERATED ALWAYS, named
          DF_Customer_ValidFrom / DF_Customer_ValidTo) with PERIOD FOR SYSTEM_TIME, then SET SYSTEM_VERSIONING = ON
          (HISTORY_TABLE = dbo.Customer_History) · R2: the model declares the same, the pre-deploy retired
expect:   questions  from R2's target (F8): HasRows(Customer) · Authored(Script.PreDeployment.sql)
          witnesses  presence(Customer) · declared(none beyond presence: the script writes only the new columns)
          outcome    stop authored-script · exit 5, then R1 verdict clean · exit 0
                     R1 replayed: plan non-empty (the lagging model undoes the conversion): the F17 hazard
                     R2: plan empty · verdict clean · exit 0
          copy       Customer temporal_type=2 · Customer_History linked · MIN(ValidFrom) = 2020-01-01 · hash of
                     (Id, Name, Email, ContactPhone, Region, AccountId) equal before and after
          record     tier T3 · ships two releases (R1 converts with the model lagging; R2 catches up) · flags {in-flight}
                     between R1 and R2 · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     F8 A5 H3
source:   verify · the staged R1 is receipt-backed only on the path where the gate blocked (SamplePr "temporal-convert:
          turning the existing populated Customer system-versioned — the true outcome under the production gate,
          existing rows preserved" runs it then: business digest equal, history linked); that R1's replay is non-empty
          and R2's plan is empty is unproven
```

### OP-temporal-convert-04 · edge: system-named defaults in R1 leave R2 a non-empty plan
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   as -03, but R1's pre-deploy adds the period columns with unnamed DEFAULTs while R2's model names them
expect:   outcome    R1 verdict clean (after the authored stop)
                     R2: plan non-empty (drop the system-named defaults, add the named ones) · verdict clean · exit 0
          record     R2 effects {modifies Customer defaults}: R2 is not the no-op the program promised
laws:     F9 F8
source:   verify · whether DacFx treats a system-named default as different from a named one with the same
          definition; INVARIANTS F9 names default-constraint names as metadata fidelity
```

### OP-temporal-convert-05 · edge: the ROW START source dates every row to the conversion
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   as -03, but ValidFrom DEFAULT SYSUTCDATETIME()
expect:   outcome    after the authored stop: R1 verdict clean · exit 0
          copy       every Customer row's ValidFrom ≥ the publish time · the business-column hash unchanged
          record     tier T3 · the ROW START source is the script's declaration, carried to the record
laws:     F3
source:   expected · skills/op/temporal-convert (named trap: "every existing row claims to have begun at conversion time")
note:     The engine accepts any ROW START, so the facsimile reproduces this faithfully and cannot judge it. A per-row
          business date (a CreatedOn) is a third source, discovered with `shape`; the after-deploy check is the PR's.
```

### OP-temporal-convert-06 · edge: ValidTo not the datetime2 maximum
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   as -03, but ValidTo DEFAULT '2099-12-31'
expect:   outcome    after the authored stop: the pre-deploy fails on the period or versioning statement (open rows
                     must end at the maximum) → stop unclassified · exit 9
laws:     H5 E1
source:   verify · the message (13575 or a neighbour) and which statement raises it; the Twin test's remedy notes
          "ValidTo must be the datetime2 max for every current row or the versioning switch's consistency check fails"
note:     The pre-deploy's earlier statements stay applied (FINDINGS F6); the record after a classifying rule lists them.
```

---

## create-static-seed

`skills/op/create-static-seed` · `skills/_index/idempotent-seed` · `sample-prs/create-static-seed.md` · Twin `SamplePrSeedTests`

### OP-create-static-seed-01 · a new lookup and its guarded MERGE; the second publish is silent (Twin parity)
```case
kind:     corpus
tags:     empty
env:      pg (no Priority)
change:   head + Priority (Id int PK, explicit, no IDENTITY · Name nvarchar(50) NOT NULL) · Data/Seed.sql + guarded
          MERGE (1 High, 2 Medium, 3 Low): WHEN MATCHED AND t.Name <> s.Name · WHEN NOT MATCHED BY TARGET
expect:   questions  Authored(Data/Seed.sql); the CREATE reads no row
          witnesses  run 2: declared(none: the MERGE touches only Priority, which this delta creates)
          outcome    stop authored-script · exit 5, then, with witness.sql: verdict clean · exit 0
          copy       Priority 3 rows · Id not IDENTITY · a second publish: plan empty, 0 rows touched, Priority's
                     hash identical
          record     tier T3 · ships one release (schema, then the post-deploy seed) · effects {adds Priority}
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     A5 H3 F3 T2
source:   receipt · SamplePr "create-static-seed: a new lookup + guarded MERGE lane seeds on first converge and is
          idempotent (0 rows + identical hash) on the second"; F12 (the guarded Category MERGE touches 0 rows on re-run)
note:     The stop names no column to shape, because the script reads only an object this delta creates; declaring
          "none" follows it literally and clears it. `clean` and `violating` cannot occur for a new lookup; an
          environment that already holds one is edit-seed's (OP-edit-seed-03, -05).
```

### OP-create-static-seed-02 · edge: an unguarded WHEN MATCHED rewrites every row with the same values
```case
kind:     corpus
tags:     edge
env:      pg (no Priority)
change:   as -01, but `WHEN MATCHED THEN UPDATE SET Name = s.Name` (no comparison)
expect:   outcome    after the authored stop: verdict clean · exit 0
          copy       the second publish touches 3 rows · Priority's hash identical
          record     the redeploy is reported not silent (3 rows rewritten), though nothing failed
laws:     F3
source:   verify · whether a post-deploy's rows-affected is observable from sqlpackage's output; F12 records the
          unguarded MERGE touching 3 rows over unchanged values, and ARCH §5.9's silence check names only the plan
          and the reference-data hash, which a same-value rewrite leaves identical
```

### OP-create-static-seed-03 · edge: an IDENTITY lookup key seeded with explicit ids
```case
kind:     corpus
tags:     edge
env:      pg (no Priority)
change:   as -01, but Priority.Id int IDENTITY(1,1); the seed inserts explicit ids with no SET IDENTITY_INSERT
expect:   witnesses  declared(none)
          outcome    after the authored stop: verdict blocked seed · Msg 544 · dbo.Priority · exit 3
          record     tier T3 · residue {main: Priority created, empty}
laws:     H5 H6
source:   receipt · F12 (identity-swap: "the post-deploy seed's explicit-id insert fails Msg 544 until bracketed with
          SET IDENTITY_INSERT"); F15 (the main script has committed when the post-deploy seed fails)
note:     Explicit ids are the model's (idempotent-seed: "a constant must mean the same row across environments");
          the fix is the explicit-id key, not the bracket.
```

### OP-create-static-seed-04 · edge: a child's new NOT NULL reference to the lookup validates before the seed runs
```case
kind:     corpus
tags:     edge
env:      pg · Order 6 rows
change:   as -01 · Order + PriorityId int NOT NULL DEFAULT 1 + FK_Order_Priority → Priority
expect:   answers    HasRows(Order)=yes
          questions  HasRows(Order): the FK's WITH CHECK CHECK validates a column this delta adds, so every row
                     holds the default 1 while Priority is still empty · Authored(Data/Seed.sql)
          witnesses  presence(Order) · declared(none)
          outcome    after the authored stop: verdict blocked constraint · Msg 547 · FK_Order_Priority · exit 3
          record     tier T3 · residue {main: Priority created, Order.PriorityId added with its default,
                     FK_Order_Priority present with is_not_trusted=1}
laws:     F4 F1 H6
source:   expected · skills/op/create-static-seed ("a missing parent row makes a child's foreign key block the deploy");
          the untrusted residue after a 547 at WITH CHECK CHECK is F9 and red-team #4 ✓
note:     The rule "a constraint over a column the delta adds asks HasRows of its table" is not yet in ARCH §4. Parents
          come first across releases: seed the lookup a release earlier, or add the reference nullable.
```

### OP-create-static-seed-05 · edge: a bare INSERT seed duplicate-keys on the second publish
```case
kind:     corpus
tags:     edge
env:      pg (no Priority)
change:   as -01, but the seed is `INSERT dbo.Priority (Id, Name) VALUES (1, N'High'), (2, N'Medium'), (3, N'Low');`
expect:   outcome    after the authored stop: the first publish is clean; the silence publish fails
                     → verdict blocked unique · Msg 2627 · PK_Priority · exit 3
laws:     H5
source:   expected · skills/_index/idempotent-seed ("Never a bare INSERT (duplicate-keys on the second deploy)")
note:     The second, silence publish is the one every later deploy is; it is where a non-idempotent seed fails.
```

---

## edit-seed

`skills/op/edit-seed` · `skills/_index/idempotent-seed` · `sample-prs/edit-seed.md` · Twin `SamplePrSeedTests`

### OP-edit-seed-01 · add a value
```case
kind:     corpus
tags:     clean
env:      pg · Category 3 rows (1 Hardware, 2 Software, 3 Service)
change:   Data/Seed.sql: the Category MERGE gains (4, N'Refunded', 1)
expect:   questions  Authored(Data/Seed.sql)
          witnesses  run 2: declared(none) after `facsimile shape Dev dbo.Category.Code --vocabulary` returns the
                     lane's 3 codes, so the lane reproduces Dev
          outcome    stop authored-script · "run shape on dbo.Category.Code; declare the shapes the seed handles" · exit 5
                     then, with witness.sql: verdict clean · exit 0
          copy       Category 4 rows · the first publish touches 1 row, the second 0 with the hash identical
          record     tier T3 · ships one release (post-deploy seed) · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     A5 H3 P6
source:   receipt · F12 (edit-seed: "an inserted Refunded row touches 1, a re-run 0")
note:     A 3-value lookup is within P6's `--vocabulary` bound, and a seed is model data. `empty` (a lookup never seeded
          in an environment) is create-static-seed's first publish.
```

### OP-edit-seed-02 · a label change touches exactly one row (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · Status 3 rows (1 Pending, 2 Shipped, 3 Cancelled)
change:   Data/Seed.sql: Status Id 3 'Cancelled' → 'Canceled' through the guarded WHEN MATCHED
expect:   questions  Authored(Data/Seed.sql)
          witnesses  declared(none) after `shape Dev dbo.Status.Code --vocabulary`
          outcome    after the authored stop: verdict clean · exit 0
          copy       exactly 1 row updated; Ids 1 and 2 untouched · the second publish 0 rows, hash identical
          record     tier T3 · ships one release (post-deploy seed)
laws:     A5 T2
source:   receipt · SamplePr "edit-seed: the guarded WHEN MATCHED UPDATE changes exactly one row and the re-run is
          silent (0 rows + identical hash)"; F12 ("a label change (Hardware → Hardware Pro) exactly 1")
```

### OP-edit-seed-03 · violating: the new Code already exists under another Id in QA
```case
kind:     corpus
tags:     violating:dup-code
env:      Dev: pg + UX_Category_Code (unique index on Code) · Category 3 rows (lane) · QA: as Dev, plus (5, 'Refunded')
          added by hand
change:   as -01, the seed adding (4, N'Refunded', 1)
expect:   witnesses  QA: declared(Category (5, 'Refunded')), its Code taken from the seed itself, after `shape QA
                     dbo.Category.Code --vocabulary` returns 4 codes
          outcome    Dev: verdict clean · exit 0
                     QA: verdict blocked unique · Msg 2601 · UX_Category_Code · exit 3
          record     two proofs · QA residue {} (the post-deploy MERGE failed; the schema is unchanged)
laws:     F7 F1 P5
source:   expected · red-team #15 (seed ids differ per environment and collide; not marked verified there); 2601 for
          a unique index, 2627 for a unique constraint
note:     The witness must equal a seed Code to collide, so it cannot carry P5's `w` prefix; it is model data, not an
          environment value. No templated probe reads the Id↔Code pairing. The remedy is a developer decision (adopt
          QA's id everywhere, or reconcile QA's row in a pre-deploy), asked through ask-the-developer.
```

### OP-edit-seed-04 · edge: a MERGE source naming one Id twice
```case
kind:     corpus
tags:     edge
env:      pg · Status 3 rows (lane)
change:   the Status seed's VALUES carries (3, N'Canceled', 1) and (3, N'Void', 1), and its guarded WHEN MATCHED fires
expect:   witnesses  declared(none)
          outcome    after the authored stop: verdict blocked seed · Msg 8672 · dbo.Status · exit 3
          record     tier T3 · residue {}
laws:     H5
source:   expected · ARCH §6 (8672, "the MERGE statement attempted to UPDATE or DELETE the same row more than once")
```

### OP-edit-seed-05 · edge: the Id is taken in QA by another Code, and the relabel is green
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Category 3 rows (lane) · QA: Category 4 rows incl. (4, 'Backorder') added by hand · Product 2 rows
          with CategoryId 4
change:   as -01, the seed adding (4, N'Refunded', 1)
expect:   witnesses  QA: declared(Category (4, 'wBackorder'); 2 Product rows with CategoryId 4)
          outcome    Dev: verdict clean · exit 0 · QA: verdict clean · exit 0
          copy       Dev: Category Id 4 inserted · QA: Category Id 4 relabelled to 'Refunded', its 2 Products now
                     mean Refunded
          record     two proofs: the same seed inserts in Dev and updates in QA
laws:     F7 F3
source:   expected · red-team #15; skills/_index/idempotent-seed (explicit ids: "a constant must mean the same row
          across environments")
note:     Nothing blocks: the relabel is green and wrong. Only QA's own proof, with its row declared, shows an update
          where Dev shows an insert.
```

### OP-edit-seed-06 · edge: a guard that is not null-safe never applies a NULL → '' change
```case
kind:     corpus
tags:     edge
env:      pg + Category.Label nvarchar(50) NULL · Category 3 rows, Label NULL×3
change:   the seed sets Label N'' for Id 1 with the guard `t.Label <> s.Label`
expect:   witnesses  declared(Category Label NULL ×3, matching Dev)
          outcome    after the authored stop: verdict clean · exit 0
          copy       Category 1's Label stays NULL: `NULL <> ''` is UNKNOWN and the guard never fires; the hash keeps
                     NULL distinct from '', so a null-safe guard instead changes 1 row once, then 0
laws:     F3 F1
source:   expected · skills/_index/idempotent-seed ("Null-safe comparison — treat NULL and '' as distinct")
note:     Silence passes and the seed's claim never lands; the engine accepts both, so this shows only in content.
```

---

## delete-seed-value

`skills/op/delete-seed-value` · `skills/_index/idempotent-seed` · `sample-prs/delete-seed-value.md` · Twin `SamplePrSeedTests`

### OP-delete-seed-value-01 · deactivate, don't delete
```case
kind:     corpus
tags:     clean
env:      pg · Category 3 rows (lane) · Product 5 rows, CategoryId 1 ×2
change:   Data/Seed.sql: Category Id 1 IsActive 1 → 0 through the guarded WHEN MATCHED
expect:   questions  Authored(Data/Seed.sql)
          witnesses  declared(none: no foreign key, so the referencing rows cannot change the outcome)
          outcome    after the authored stop: verdict clean · exit 0
          copy       Category 1 IsActive=0 and still present · the second publish 0 rows, hash identical
          record     tier T3 · ships one release (post-deploy seed) · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     A5 M2
source:   expected · skills/op/delete-seed-value; recorded live in sample-prs/delete-seed-value.md (pg_move)
note:     Deactivation is this operation's remedy for -03 and -04. `empty` cannot occur: there is no row to retire.
```

### OP-delete-seed-value-02 · removing the value from the seed deletes nothing (Twin parity)
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Status (Order.StatusId → Status) · Status 3 rows · Order 6 rows, StatusId 1 ×6
change:   Data/Seed.sql: the Status MERGE's VALUES drops (3, N'Cancelled', 1); no WHEN NOT MATCHED BY SOURCE arm
expect:   witnesses  declared(none)
          outcome    after the authored stop: verdict clean · exit 0
          copy       Status still 3 rows, Id 3 present · the MERGE touched 0 rows
          record     tier T3 · the change's intent ("delete the value") did not happen: the copy is unchanged
laws:     T2 F3
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the
          explicit, FK-guarded removal blocks a referenced value (Msg 547)"
```

### OP-delete-seed-value-03 · violating: an explicit DELETE of a referenced value under a declared FK
```case
kind:     corpus
tags:     violating:referenced
env:      pg + FK_Order_Status · Status 3 rows · Order 6 rows, StatusId 1 ×6
change:   post-deploy + `DELETE dbo.Status WHERE Id = 1;`
expect:   witnesses  declared(one Order row with StatusId 1) under `-- plants: Order`
          outcome    after the authored stop: verdict blocked constraint · Msg 547 · FK_Order_Status · exit 3
          copy       Status 1 still present
          record     tier T3 · residue {}
laws:     H5 F1 T2
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the
          explicit, FK-guarded removal blocks a referenced value (Msg 547)"
note:     No vocabulary question plants Order, so the referencing row is declared; without it the copy deletes cleanly
          and disagrees with Dev.
```

### OP-delete-seed-value-04 · violating: an explicit DELETE of a referenced value with no FK, a silent orphan
```case
kind:     corpus
tags:     violating:referenced
env:      pg (no FK) · Category 3 rows · Product 5 rows, CategoryId 1 ×2
change:   post-deploy + `DELETE dbo.Category WHERE Id = 1;`
expect:   witnesses  declared(2 Product rows with CategoryId 1)
          outcome    after the authored stop: verdict clean · exit 0
          copy       Category 1 absent · 2 Product rows point at no Category
          record     tier T3 · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F3
source:   expected · skills/op/delete-seed-value ("not refused unless a foreign key is declared"); recorded live in
          sample-prs/delete-seed-value.md (pg_move: the delete succeeded and orphaned 2 Product rows)
note:     Green and harmful: the engine has nothing to refuse. The PR's after-deploy LEFT JOIN catches the orphans;
          deactivation (-01) is the shipped shape.
```

### OP-delete-seed-value-05 · an explicit DELETE of an unreferenced value, idempotent
```case
kind:     corpus
tags:     clean
env:      pg + FK_Order_Status · Status 3 rows · Order 6 rows, StatusId 1 ×6 (Status 3 unreferenced)
change:   Data/Seed.sql: the VALUES drops (3, …) · post-deploy + `DELETE dbo.Status WHERE Id = 3;` after the MERGE
expect:   witnesses  declared(Order rows referencing only Status 1)
          outcome    after the authored stop: verdict clean · exit 0
          copy       Status 2 rows · the second publish deletes 0 rows
laws:     F3 A5
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the
          explicit, FK-guarded removal blocks a referenced value (Msg 547)" (the explicit DELETE of the unused value
          succeeds; a second identical DELETE affects 0 rows)
note:     Leave (3, …) in the VALUES and every publish re-inserts then deletes it: never silent, though the hash holds.
```

### OP-delete-seed-value-06 · edge: a WHEN NOT MATCHED BY SOURCE arm meets QA's hand-added, referenced value
```case
kind:     corpus
tags:     edge
env:      Dev: pg + FK_Order_Status · Status 3 rows · QA: as Dev, plus Status (4, 'Backorder') referenced by 2 Orders
change:   the Status seed gains `WHEN NOT MATCHED BY SOURCE THEN DELETE`
expect:   witnesses  Dev: declared(none) · QA: declared(Status (4, 'wBackorder'); 2 Orders with StatusId 4)
          outcome    Dev: verdict clean · exit 0 (0 rows deleted)
                     QA: verdict blocked constraint · Msg 547 · FK_Order_Status · exit 3
          record     two proofs
laws:     F7 F1
source:   expected · skills/_index/idempotent-seed ("WHEN NOT MATCHED BY SOURCE THEN DELETE — only with care and a
          scoped source; it deletes rows the seed simply didn't list")
note:     Without the FK, QA's run is green and orphans 2 Orders. Either way only QA's own proof sees it.
```

---

## extract-to-lookup

`skills/op/extract-to-lookup` · `skills/_index/multi-phase` · `sample-prs/extract-to-lookup.md` · Twin `SamplePrSeedTests` · the whole program is CMP-17

### OP-extract-to-lookup-01 · phase 1: the lookup created, seeded, referenced and backfilled; the mapping total (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg + Order.Channel nvarchar(20) NOT NULL · Order 6 rows · Channel {Web ×3, Store ×3}
change:   head + Channel (Id int PK explicit · Code nvarchar(20) NOT NULL) · Order + ChannelId int NULL +
          FK_Order_Channel → Channel · Data/Seed.sql + guarded Channel MERGE (1 Web, 2 Store) · post-deploy backfill
          `UPDATE o SET ChannelId = c.Id FROM dbo.[Order] o JOIN dbo.Channel c ON c.Code = o.Channel WHERE o.ChannelId IS NULL`
expect:   questions  Authored(Data/Seed.sql, the backfill); the FK asks nothing: its column is added by this delta,
                     NULL in every row
          witnesses  run 2: declared(an Order row per Channel shape, Web and Store) after
                     `facsimile shape Dev dbo.[Order].Channel --vocabulary` returns 2 values
          outcome    stop authored-script · "run shape on dbo.[Order].Channel; declare a witness per shape" · exit 5
                     then, with witness.sql: verdict clean · exit 0
          copy       0 unmapped distinct Channel · Order.ChannelId NULL = 0 · FK_Order_Channel is_not_trusted=0 ·
                     Order.Channel retained · the second publish: Channel MERGE 0 rows, hash identical
          record     tier T3 · ships phase 1 of 3 (create, seed and backfill; the drop is later)
                     · effects {adds Channel, Order.ChannelId, FK_Order_Channel}
                     · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     P6 A5 H3 T2
source:   receipt · SamplePr "extract-to-lookup (phase 1): the lookup is seeded idempotently, every distinct source
          value maps 1:1, and the FK is trusted"
```

### OP-extract-to-lookup-02 · violating:unmapped, and the backfill checks itself
```case
kind:     corpus
tags:     violating:unmapped
env:      as -01 but Channel {Web ×3, Store ×2, Phone ×1}
change:   as -01, the backfill opening with `IF EXISTS (SELECT 1 FROM dbo.[Order] o WHERE NOT EXISTS (SELECT 1 FROM
          dbo.Channel c WHERE c.Code = o.Channel)) RAISERROR(N'unmapped Channel', 16, 1);`
expect:   witnesses  declared(Order rows Web, Store, and 'wPhone', a value no seeded Code matches)
          outcome    after the authored stop: verdict blocked authored · Msg 50000/1 · Script.PostDeployment.sql · exit 3
          record     tier T3 · residue {main: Channel, Order.ChannelId, FK_Order_Channel applied}
laws:     H5 H6 F1 P5
source:   expected · TWIN_SUCCESSION §6b (the script "THROWs if it finds a shape it does not handle"); F12 (an injected
          'Backordered' fires the non-total negative)
note:     H5 attributes a non-guard 50000 to its script, but ARCH §6 lists no verdict reason for it; `authored` is
          this case's proposal. The fix is to seed the value or reconcile it before the backfill.
```

### OP-extract-to-lookup-03 · violating:unmapped, and a join backfill leaves a silent NULL
```case
kind:     corpus
tags:     violating:unmapped
env:      as -02
change:   as -01 (no self-check)
expect:   witnesses  as -02
          outcome    after the authored stop: verdict clean · exit 0
          copy       1 Order with ChannelId NULL (the 'wPhone' row)
          record     tier T3 · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F3 F1
source:   expected · skills/op/extract-to-lookup (named trap: "any source text with no seeded lookup row becomes NULL");
          F12 (the total-mapping query)
note:     The engine has nothing to refuse; that is why §6b asks scripts to check themselves. The value is lost for
          good at the phase-3 drop.
```

### OP-extract-to-lookup-04 · violating:case, a case twin under a case-sensitive column
```case
kind:     corpus
tags:     violating:case
env:      pg + Order.Channel nvarchar(20) COLLATE Latin1_General_CS_AS · Order 6 rows · Channel {Web ×3, Store ×2, web ×1}
change:   as -02 (self-checking backfill); Channel.Code declared with the same collation
expect:   answers    Collation(Order.Channel)=Latin1_General_CS_AS
          witnesses  declared(Order rows Web, Store, and 'web', the case twin of a seeded Code) after `shape` reports
                     3 distinct as collated and 3 binary
          outcome    after the authored stop: verdict blocked authored · Msg 50000/1 · Script.PostDeployment.sql · exit 3
laws:     F9 F1 P5 P6
source:   expected · INVARIANTS F9 (column collation is metadata fidelity); TWIN_SUCCESSION §6b (the shape profile found
          a case twin: 4 distinct as collated vs 5 binary)
note:     Under a case-insensitive collation 'web' maps to Web and nothing is lost; the copy must carry Dev's
          collation for the verdict to agree (INVARIANTS F9).
```

### OP-extract-to-lookup-05 · violating:trailing, a padded twin maps and a leading-space twin does not
```case
kind:     corpus
tags:     violating:trailing
env:      as -01 but Channel {Web ×2, Store ×2, 'Web ' ×1, ' Web' ×1}
change:   as -01, the backfill written `UPDATE dbo.[Order] SET ChannelId = CASE Channel WHEN N'Web' THEN 1
          WHEN N'Store' THEN 2 ELSE 0 END WHERE ChannelId IS NULL` (no lookup row 0)
expect:   witnesses  declared(Order rows 'Web ' and ' Web', twins of a seeded Code) after `shape` reports more
                     distinct binary values than distinct as collated
          outcome    after the authored stop: verdict blocked constraint · Msg 547 · FK_Order_Channel · exit 3
          record     tier T3 · residue {main: Channel, Order.ChannelId, FK_Order_Channel applied}
laws:     F1 H5 H6
source:   expected · ARCH §4 (`trailing` class); skills/op/extract-to-lookup ("becomes NULL, or blocks the FK")
note:     `=` pads trailing spaces, so 'Web ' maps to 1; ' Web' falls to ELSE 0 and the FK refuses it. A join backfill
          would have left ' Web' NULL instead (-03).
```

### OP-extract-to-lookup-06 · violating:empty, '' and NULL in the source
```case
kind:     corpus
tags:     violating:empty
env:      pg + Order.Channel nvarchar(20) NULL · Order 6 rows · Channel {Web ×2, Store ×2, NULL ×1, '' ×1}
change:   as -02, the self-check excluding NULL (`WHERE o.Channel IS NOT NULL AND NOT EXISTS …`)
expect:   witnesses  declared(Order rows NULL and '', alongside Web and Store)
          outcome    after the authored stop: verdict blocked authored · Msg 50000/1 · Script.PostDeployment.sql · exit 3
          copy       the NULL row would map to NULL legitimately; the '' row is the unmapped one
laws:     F1 P6
source:   expected · skills/_index/idempotent-seed (NULL distinct from ''); TWIN_SUCCESSION §6b ("Email: one NULL, one ''
          … pass NOT NULL while still meaning blank")
note:     Without the self-check both rows end ChannelId NULL and are indistinguishable afterwards; `shape` counts
          NULL and '' apart, which is how the agent knows to declare both.
```

### OP-extract-to-lookup-07 · contract, naive: the free-text column dropped declaratively
```case
kind:     corpus
tags:     clean
env:      pg + Order.Channel · phase 1 landed (Channel seeded, ChannelId backfilled, 0 unmapped) · Order 6 rows
change:   head − Order.Channel
expect:   answers    HasRows(Order)=yes
          questions  HasRows(Order) (the guard above DROP COLUMN Channel)
          witnesses  presence(Order)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Order · exit 3
          record     tier T1 · ships the contract as two releases (C1 drops in pre-deploy with the model lagging; C2
                     catches up) · residue {}
laws:     F1 H5
source:   receipt · SamplePr "delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the
          scripted DROP COLUMN is the irreversible step"; F12 (a phase-3 drop's block is data-blind)
note:     The total-mapping proof clears the reviewer's doubt, never the gate.
```

### OP-extract-to-lookup-08 · an empty source collapses the program to one release
```case
kind:     corpus
tags:     empty
env:      pg + Order.Channel · Order 0 rows
change:   -01's additions and − Order.Channel, all in one release
expect:   answers    HasRows(Order)=no
          questions  HasRows(Order) (the guard above DROP COLUMN Channel) · Authored(Data/Seed.sql, the backfill)
          witnesses  declared(none: no Order row to map)
          outcome    after the authored stop: verdict clean · exit 0
          record     tier T3 · ships one release · effects {adds Channel, Order.ChannelId, FK_Order_Channel,
                     removes Order.Channel} · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F1 M2
source:   expected · skills/_index/multi-phase ("if the source is empty … the whole staged shape collapses")
note:     The collapse is Dev's; a populated QA needs the three-phase program, proven against QA's own answers.
```

### OP-extract-to-lookup-09 · remedy: the unmapped value given a home before the backfill
```case
kind:     corpus
tags:     remedy
env:      as -02 (Channel {Web ×3, Store ×2, Phone ×1})
change:   as -02, the developer's answer recorded in the seed: the Channel MERGE gains (3, N'Phone')
expect:   questions  Authored(Data/Seed.sql, the backfill)
          witnesses  declared(Order rows Web, Store and Phone — 'Phone' now the seed's own Code)
          outcome    stop authored-script · exit 5
                     then, with witness.sql: verdict clean · exit 0
          copy       0 unmapped · Order.ChannelId NULL = 0 · FK_Order_Channel is_not_trusted=0
          record     tier T3 · ships phase 1 of 3 · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     A5 F1 H3
source:   expected · skills/op/extract-to-lookup ("a pre-backfill reconcile is required so nothing maps to NULL");
          F15 (a) (the seed, not the pre-deploy, carries the mapping decision)
note:     -02's self-check now passes because the seed answers it. Folding the value into an existing Code instead is a
          reconcile in the same release, recorded in the seed the same way.
```

---

## split-table

`skills/op/split-table` · `skills/_index/multi-phase` · `sample-prs/split-table.md` · Twin `SamplePrStructuralTests`

### OP-split-table-01 · phase 1: the new table and its copy, 1:1, hash-equal (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · ContactPhone populated ×5
change:   head + CustomerContact (Id IDENTITY PK · CustomerId int NOT NULL UNIQUE · Phone nvarchar(40) NULL ·
          FK_CustomerContact_Customer → Customer) · post-deploy copy, guarded by NOT EXISTS:
          `INSERT dbo.CustomerContact (CustomerId, Phone) SELECT c.Id, c.ContactPhone FROM dbo.Customer c WHERE …`
expect:   questions  Authored(the post-deploy copy); the FK and unique checks read only CustomerContact, which this
                     delta creates
          witnesses  run 2: declared(presence(Customer)) after `shape Dev dbo.Customer.ContactPhone`
          outcome    stop authored-script · exit 5, then, with witness.sql: verdict clean · exit 0
          copy       CustomerContact rows = Customer rows · lost 0 · duplicated 0 · FK trusted ·
                     hash(Customer: Id AS k, ContactPhone AS v) = hash(CustomerContact: CustomerId AS k, Phone AS v)
          record     tier T3 · ships phase 1 of 3 · effects {adds CustomerContact}
                     · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     A5 H3 M2 T2
source:   receipt · F12 (split-table, pg_split: "additive publish clean, 5 rows copied, hash-equal (aliased)");
          SamplePr "split-table (phase 1): the new CustomerContact is created, every Email copies 1:1 with a matching
          digest and trusted FK, and the source-column drop is the guarded later phase"
note:     Both projections alias to the same names: FOR XML RAW encodes column names, so un-aliased hashes differ over
          identical data (FINDINGS F12's alias law). ARCH §5 has no conservation step; the hash runs on the copy afterwards.
```

### OP-split-table-02 · an empty source collapses to one additive release
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head + CustomerContact (as -01) − Customer.ContactPhone, one release, no copy
expect:   answers    HasRows(Customer)=no
          questions  HasRows(Customer) (the guard above DROP COLUMN ContactPhone)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T1 · ships one release · effects {adds CustomerContact, removes Customer.ContactPhone}
                     · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     F1 M2
source:   expected · skills/op/split-table ("an empty source collapses to a single additive release")
```

### OP-split-table-03 · phase 3, naive: the source-column drop is the guarded later phase (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · phase 1 landed (CustomerContact holds every phone)
change:   head − Customer.ContactPhone
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · ships the drop as two releases (delete-attribute's shape) · residue {}
laws:     F1 H5 T2
source:   receipt · SamplePr "split-table (phase 1): the new CustomerContact is created, every Email copies 1:1 with a
          matching digest and trusted FK, and the source-column drop is the guarded later phase"
```

### OP-split-table-04 · violating:overlen, the destination narrower than a source value
```case
kind:     corpus
tags:     violating:overlen
env:      pg · Customer 5 rows · ContactPhone LEN max 35
change:   as -01, but Phone nvarchar(20)
expect:   witnesses  declared(a Customer whose ContactPhone is a 21-character `w`-prefixed value) after `shape Dev
                     dbo.Customer.ContactPhone` reports lengths over 20
          outcome    after the authored stop: verdict blocked truncation · Msg 2628 · dbo.CustomerContact · exit 3
          record     tier T3 · residue {main: CustomerContact created}
laws:     F1 H5 H6 P6
source:   expected · ARCH §6 (2628, or 8152 below compatibility level 150); skills/_index/multi-phase
note:     `shape` returns lengths without values (P6), which is all this witness needs.
```

### OP-split-table-05 · violating:unicode, a varchar destination substitutes characters silently
```case
kind:     corpus
tags:     violating:unicode
env:      pg · Customer 5 rows · one ContactPhone holding a character outside the destination's code page
change:   as -01, but Phone varchar(40)
expect:   witnesses  declared(a Customer whose ContactPhone is 'w電話')
          outcome    after the authored stop: verdict clean · exit 0
          copy       the aliased hashes differ: the copy wrote '?' where the source held the character
laws:     F3 F1
source:   expected · skills/_index/multi-phase (the BEFORE/AFTER content hash licenses the drop)
note:     No message reports the loss; only the conservation hash sees it, before phase 3 makes it permanent.
```

### OP-split-table-06 · violating:empty, NULL and '' in the moving column
```case
kind:     corpus
tags:     violating:empty
env:      pg · Customer 6 rows · ContactPhone NULL ×1 · '' ×1
change:   as -01, but the copy filters `WHERE c.ContactPhone <> ''`
expect:   witnesses  declared(Customer rows with ContactPhone NULL and '')
          outcome    after the authored stop: verdict clean · exit 0
          copy       CustomerContact 4 rows for 6 Customers: lost 2 · an unfiltered copy keeps both and the aliased
                     hashes match, NULL and '' kept apart
laws:     F1 F3
source:   expected · skills/_index/multi-phase; ARCH §3 (`sql/hash.sql`: NULL distinct from '')
```

### OP-split-table-07 · edge: create and drop in one release, proven per environment
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Customer 0 rows · QA: pg · Customer 5 rows
change:   head + CustomerContact − Customer.ContactPhone, with -01's post-deploy copy, all in one release
expect:   answers    Dev: HasRows(Customer)=no · QA: HasRows(Customer)=yes
          questions  HasRows(Customer) · Authored(the copy)
          outcome    after the authored stop:
                     Dev: the column drops, then the copy names it
                     → verdict blocked seed · Msg 207 · Script.PostDeployment.sql · exit 3
                     QA: verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     Dev residue {main: CustomerContact created, Customer.ContactPhone dropped} · QA residue {}
laws:     F7 H5 H6
source:   expected · skills/op/split-table (named trap: "treating it as one PR"); F15 (a post-deploy naming a dropped
          column fails Msg 207 on a half-applied release)
note:     Two environments, two different blocks from one head: only per-environment proof shows both.
```

### OP-split-table-08 · remedy: phase 3 as two releases
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 5 rows · phase 1 landed, the copy proven hash-equal
change:   program R1: model still declares ContactPhone · pre-deploy
          `IF COL_LENGTH('dbo.Customer','ContactPhone') IS NOT NULL ALTER TABLE dbo.Customer DROP COLUMN ContactPhone;`
          · R2: the model drops ContactPhone, the pre-deploy retired
expect:   answers    HasRows(Customer)=yes
          questions  from R2's target (F8): HasRows(Customer) · Authored(Script.PreDeployment.sql)
          witnesses  presence(Customer) · declared(none beyond presence)
          outcome    stop authored-script · exit 5, then:
                     R1: verdict clean · exit 0 (ContactPhone gone)
                     R1 replayed: plan non-empty, ContactPhone re-added empty: the F17 hazard
                     R2: plan empty · verdict clean · exit 0
          record     tier T3 · ships two releases, R2 promptly after R1 · flags {in-flight} between R1 and R2
                     · effects {removes Customer.ContactPhone} · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     F8 A5
source:   expected · FINDINGS Part 4 (delete-attribute row: R1 drops in pre-deploy with the model lagging, R2 catches up;
          "Re-publishing R1 re-adds the column"); F17 (the replay re-creates a dropped column on a green publish)
note:     The conservation hash licenses the reviewer's decision to drop; the two releases are what pass the gate.
```

---

## merge-tables

`skills/op/merge-tables` · `skills/_index/multi-phase` · `sample-prs/merge-tables.md` · Twin `SamplePrStructuralTests`

### OP-merge-tables-01 · phase 1: cardinality 1:1, the copy conserves (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · CustomerAddress 5 rows, one per Customer
change:   Customer + Line1 nvarchar(120) NULL, City nvarchar(80) NULL, PostalCode nvarchar(20) NULL · post-deploy copy
          opening with `IF (SELECT COUNT(*) FROM dbo.CustomerAddress) <> (SELECT COUNT(DISTINCT CustomerId) FROM
          dbo.CustomerAddress) RAISERROR(N'not 1:1', 16, 1);` then `UPDATE c SET … FROM dbo.Customer c JOIN …`
expect:   questions  Authored(the post-deploy copy); the column adds read no row
          witnesses  run 2: declared(a Customer and its one CustomerAddress)
          outcome    stop authored-script · exit 5, then, with witness.sql: verdict clean · exit 0
          copy       absorbed rows = distinct parents · hash(CustomerAddress: CustomerId AS k, Line1 AS a, City AS b,
                     PostalCode AS c) = hash(Customer: Id AS k, … WHERE Line1 IS NOT NULL)
          record     tier T3 · ships phase 1 of 3 · effects {modifies Customer (+3 columns)}
                     · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     A5 M2 T2
source:   receipt · F12 (merge-tables, pg_merge: "absorbed = 5 == distinct_parents = 5"); SamplePr "merge-tables
          (phase 1): cardinality is proven 1:1 before the copy, the copy preserves every row and value, a 1:many
          merge is caught before it silently drops rows, and the absorbed-table drop is the later phase"
```

### OP-merge-tables-02 · violating:cardinality, the self-checking copy refuses 1:many
```case
kind:     corpus
tags:     violating:cardinality
env:      pg · Customer 5 rows · CustomerAddress 6 rows, Customer 1 holding two
change:   as -01
expect:   witnesses  declared(two CustomerAddress rows on one Customer)
          outcome    after the authored stop: verdict blocked authored · Msg 50000/1 · Script.PostDeployment.sql · exit 3
          record     tier T3 · residue {main: Customer.Line1, City, PostalCode added}
laws:     H5 H6 M2 F1
source:   expected · F12 ("a 2nd address for one Customer → 6 != 5, the 1:many refusal"); the attribution is H5's
note:     The 1:many shape is two rows sharing a parent: beyond M2's one presence row, so it is declared in witness.sql.
```

### OP-merge-tables-03 · violating:cardinality, a naive copy drops a row and the value hash looks complete
```case
kind:     corpus
tags:     violating:cardinality
env:      as -02
change:   as -01, without the cardinality check
expect:   witnesses  as -02
          outcome    after the authored stop: verdict clean · exit 0
          copy       5 survivors hold an address for 6 absorbed rows · a survivor hash matches the hash of whichever
                     5 absorbed rows won: only the count catches the loss
laws:     F3 M2
source:   receipt · SamplePr "merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves every
          row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is the later
          phase" (the naive copy kept 25 of 26)
```

### OP-merge-tables-04 · edge: an absorbed row with no parent
```case
kind:     corpus
tags:     edge
env:      pg (no FK) · Customer 5 rows · CustomerAddress 5 rows: 4 on real Customers, 1 on CustomerId 99
change:   as -01
expect:   witnesses  declared(an address on a real Customer, and one on CustomerId 99)
          outcome    after the authored stop: verdict clean · exit 0 (5 rows, 5 distinct parents: the count passes)
          copy       4 addresses reach Customer · the absorbed and survivor hashes differ: the conservation hash
                     catches what the count misses
laws:     F1 F3
source:   expected · TWIN_SUCCESSION §5 (merge witnesses: "an absorbed row with no parent")
```

### OP-merge-tables-05 · survivors without an absorbed row, and NULL vs '' absorbed values
```case
kind:     corpus
tags:     violating:empty
env:      pg · Customer 6 rows · CustomerAddress 5 rows (Customer 6 has none) · City NULL ×1 · '' ×1
change:   as -01
expect:   witnesses  declared(a Customer with no address; addresses with City NULL and '')
          outcome    after the authored stop: verdict clean · exit 0
          copy       Customer 6 keeps NULL address columns (1:0..1 is allowed) · the filtered survivor hash equals
                     the absorbed hash because NULL and '' stay apart on both sides
laws:     F1
source:   expected · skills/op/merge-tables ("1:1 or 1:0..1"); ARCH §3 (`sql/hash.sql`: NULL distinct from '')
```

### OP-merge-tables-06 · phase 3: the absorbed-table drop is a phantom under the production options (Twin parity)
```case
kind:     corpus
tags:     edge
env:      pg · CustomerAddress 5 rows · phases 1 and 2 landed · options DropObjectsNotInSource=false
change:   head − Modules/CustomerAddress.sql
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          copy       CustomerAddress still present
          record     tier T0 · ships a phantom removal: the real phase-3 drop is the scripted DROP TABLE of
                     OP-delete-entity-04 · effects {}
laws:     F3 F6 T2
source:   receipt · SamplePr "merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves every
          row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is the later
          phase" (removing CustomerAddress under the production publish is a phantom; the scripted DROP TABLE removes it)
```

### OP-merge-tables-07 · an empty absorbed table: one release
```case
kind:     corpus
tags:     empty
env:      pg · CustomerAddress 0 rows · options DropObjectsNotInSource=false
change:   Customer + the absorbing columns · Modules/CustomerAddress.sql removed · pre-deploy guarded
          DROP TABLE dbo.CustomerAddress
expect:   questions  Authored(Script.PreDeployment.sql)
          witnesses  declared(none: CustomerAddress is empty)
          outcome    after the authored stop: verdict clean · exit 0
          record     tier T3 · ships one release · effects {modifies Customer (+3 columns), removes CustomerAddress}
                     · notChecked {QA, UAT, Prod, over-time, is-refresh}
laws:     A5 F7
source:   expected · skills/op/merge-tables ("absorbed entity empty → … in a single release")
note:     Empty is Dev's answer; a QA with rows needs its own proof and the copy-first program.
```

### OP-merge-tables-08 · remedy: phase 3, the scripted drop of the absorbed table
```case
kind:     corpus
tags:     remedy
env:      pg · CustomerAddress 5 rows · phases 1 and 2 landed, cardinality and hash proven · options
          DropObjectsNotInSource=false
change:   head − Modules/CustomerAddress.sql · pre-deploy
          `IF OBJECT_ID(N'dbo.CustomerAddress', N'U') IS NOT NULL DROP TABLE dbo.CustomerAddress;`
expect:   questions  Authored(Script.PreDeployment.sql)
          witnesses  declared(presence(CustomerAddress))
          outcome    stop authored-script · exit 5
                     then, with witness.sql: verdict clean · exit 0
          copy       CustomerAddress absent · a second publish: plan empty
          record     tier T3 · ships one release, scripted · effects {removes CustomerAddress}
                     · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     A5 H3
source:   expected · skills/op/merge-tables (phase 3 "ships as the one-release scripted DROP TABLE (delete-entity)");
          the shape is OP-delete-entity-04's
```

---

## move-attribute

`skills/op/move-attribute` · `skills/_index/multi-phase` · `sample-prs/move-attribute.md` · Twin `SamplePrStructuralTests`

### OP-move-attribute-01 · phase 1: the destination column, a copy keyed by a 1:1 join (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg − Account.Region · Account 3 rows · Customer 5 rows · AccountId {1, 2, 3, NULL, NULL} · Region ×5
change:   Account + Region nvarchar(50) NULL · post-deploy copy opening with a 1:1 check that excludes NULL keys
          (`WHERE AccountId IS NOT NULL GROUP BY AccountId HAVING COUNT(*) > 1` → RAISERROR), then
          `UPDATE a SET Region = c.Region FROM dbo.Account a JOIN dbo.Customer c ON c.AccountId = a.Id`
expect:   questions  Authored(the post-deploy copy); the column add reads no row
          witnesses  run 2: declared(Customers on Accounts 1–3, and two with AccountId NULL)
          outcome    stop authored-script · exit 5, then, with witness.sql: verdict clean · exit 0
          copy       hash(Customer: AccountId AS k, Region AS v WHERE AccountId IS NOT NULL) =
                     hash(Account: Id AS k, Region AS v WHERE Region IS NOT NULL) · 2 Customers' Regions have no
                     destination
          record     tier T3 · ships phase 1 of 3 · effects {adds Account.Region}
                     · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     A5 M2 T2
source:   receipt · F12 (move-attribute, pg_move: 1:1 holds excluding NULLs, hash-equal aliased, "2 of 5 customers have
          a NULL AccountId so their Region strands"); SamplePr "move-attribute (phase 1): the join is proven 1:1, the
          destination column is backfilled with a matching digest, and the source-column drop is the guarded later phase"
note:     The stranded Regions are a fork for the developer, named on the record; the facsimile reproduces them and
          does not settle them.
```

### OP-move-attribute-02 · violating:cardinality, two Customers share an Account with different Regions
```case
kind:     corpus
tags:     violating:cardinality
env:      as -01 but AccountId {1, 1, 3, NULL, NULL}, the two on Account 1 holding West and East
change:   as -01
expect:   witnesses  declared(two Customers on Account 1 with different Regions)
          outcome    after the authored stop: verdict blocked authored · Msg 50000/1 · Script.PostDeployment.sql · exit 3
          record     tier T3 · residue {main: Account.Region added}
laws:     H5 H6 M2
source:   expected · skills/op/move-attribute ("relationship not 1:1 → STOP: the value is ambiguous")
```

### OP-move-attribute-03 · edge: a 1:1 check that forgets NULL keys refuses clean data
```case
kind:     corpus
tags:     edge
env:      as -01
change:   as -01, but the check groups without `WHERE AccountId IS NOT NULL`
expect:   witnesses  declared(as -01, both NULL-key Customers included)
          outcome    after the authored stop: verdict blocked authored · Msg 50000/1 · Script.PostDeployment.sql · exit 3
laws:     F1 H5 M2
source:   expected · F12 ("1:1 holds (excluding NULLs — else the 2 unmapped rows false-positive as 1:many)")
note:     A false refusal: the two NULL keys group together. With only M2's one presence row the copy would never
          show it; the NULL-key rows must be declared.
```

### OP-move-attribute-04 · phase 3, naive: the source-column drop is guarded (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · Account.Region backfilled
change:   head − Customer.Region
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · ships the drop as two releases · residue {}
laws:     F1 H5 T2
source:   receipt · SamplePr "move-attribute (phase 1): the join is proven 1:1, the destination column is backfilled with
          a matching digest, and the source-column drop is the guarded later phase"
```

### OP-move-attribute-05 · empty source column: an all-NULL column's drop still trips the guard
```case
kind:     corpus
tags:     empty
env:      pg · Customer 6 rows · Region NULL ×6
change:   head − Customer.Region; nothing to move
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · ships with a pre-deploy step that clears the guard (two releases) · residue {}
laws:     F1 H5
source:   expected · skills/op/move-attribute ("even an all-NULL column drop is blocked under BlockOnPossibleDataLoss")
note:     The guard is table-has-rows (FINDINGS F1). The planted row gives Region a value though Dev holds only NULLs;
          INVARIANTS F1 still holds, because the only question is HasRows.
```

### OP-move-attribute-06 · edge: the "move" written as a cross-table rename
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows · Region ×5
change:   head − Customer.Region + Account.Region nvarchar(50) NULL, one release, no copy
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · effects would be {adds Account.Region, removes Customer.Region}, never a rename
laws:     F1 H5
source:   expected · skills/_index/identity-and-refactorlog ("Cross-table MOVE is NOT a rename")
note:     Over an empty Customer the same head publishes clean and moves nothing; no refactorlog entry can carry a
          column between tables.
```

### OP-move-attribute-07 · violating:overlen, the destination narrower
```case
kind:     corpus
tags:     violating:overlen
env:      as -01, one Region 30 characters long
change:   as -01, but Account.Region nvarchar(20)
expect:   witnesses  declared(as -01, one Customer on an Account with a 30-character `w`-prefixed Region)
          outcome    after the authored stop: verdict blocked truncation · Msg 2628 · dbo.Account · exit 3
          record     tier T3 · residue {main: Account.Region added}
laws:     F1 H5 H6 P6
source:   expected · ARCH §6 (2628, or 8152 below compatibility level 150)
note:     Which of the two numbers the copy raises follows the compatibility level, one of INVARIANTS F9's five metadata items.
```

### OP-move-attribute-08 · remedy: phase 3 as two releases
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 5 rows · phase 1 landed, the copy proven hash-equal, the two NULL-key Regions settled
change:   program R1: model still declares Customer.Region · pre-deploy guarded DROP COLUMN Region · R2: the model
          drops Region, the pre-deploy retired
expect:   answers    HasRows(Customer)=yes
          questions  from R2's target (F8): HasRows(Customer) · Authored(Script.PreDeployment.sql)
          witnesses  presence(Customer) · declared(none beyond presence)
          outcome    stop authored-script · exit 5, then:
                     R1: verdict clean · exit 0 · R1 replayed: Region re-added empty, the F17 hazard
                     R2: plan empty · verdict clean · exit 0
          record     tier T3 · ships two releases · flags {in-flight} between R1 and R2
                     · effects {removes Customer.Region}
laws:     F8 A5
source:   expected · as OP-split-table-08 (FINDINGS Part 4, delete-attribute row; F17)
note:     The stranded Regions (-01) must be settled before R1: once it lands, their values live nowhere.
```

---

## archive-entity

`skills/op/archive-entity` · `skills/_index/multi-phase` · `sample-prs/archive-entity.md` · Twin `SamplePrRemovalTests`

### OP-archive-entity-01 · release 1: the archive table, additive
```case
kind:     corpus
tags:     clean
env:      pg · Order 4 rows
change:   head + CREATE SCHEMA archive + archive.OrderArchive (Order's columns, Id int NOT NULL PK, no IDENTITY)
expect:   questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships release 1 of 2 · effects {adds archive, archive.OrderArchive}
                     · notChecked {QA, UAT, Prod, is-refresh}
laws:     F3 F4
source:   expected · skills/op/archive-entity ("the archive table is added (additive, one declarative release)")
note:     `empty`: with no row past the cutoff there is nothing to move, and this release is the whole change.
```

### OP-archive-entity-02 · release 2: a batched move conserves every row, over more than one batch (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg + archive.OrderArchive · Order 12 rows, 8 with StatusId 3 · Status lane
change:   post-deploy: `WHILE 1 = 1 BEGIN DELETE TOP (5) o OUTPUT DELETED.* INTO archive.OrderArchive FROM dbo.[Order] o
          WHERE o.StatusId = 3; IF @@ROWCOUNT = 0 BREAK; END`
expect:   questions  Authored(Script.PostDeployment.sql)
          witnesses  run 2: declared(8 Order rows with StatusId 3 and 4 without) under `-- plants: Order`
          outcome    stop authored-script · exit 5, then, with witness.sql: verdict clean · exit 0
          copy       live + archived = 12 · archived 8 · no Id in both · the archived hash equals the moved rows'
                     hash before · at least 2 batches committed
          record     tier T3 · ships release 2 of 2 · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     M2 A5 H4 T2
source:   receipt · SamplePr "archive-entity: retiring rows to an archive table conserves every row (live + archived
          == original), byte-identical and batched"
note:     M2's minimum (one presence row) never reaches a second batch; the row count is a declared choice in
          witness.sql. `volume` stays: log growth, timing and CommandTimeout belong to the real table.
```

### OP-archive-entity-03 · edge: the cutoff, before, on and after
```case
kind:     corpus
tags:     edge
env:      pg + archive.OrderArchive · Order 6 rows, Id 1–6
change:   post-deploy batched move `WHERE o.Id <= 3`
expect:   witnesses  declared(Order Ids 2, 3 and 4: just before, on, and just after the cutoff)
          outcome    after the authored stop: verdict clean · exit 0
          copy       Id 3 moves under `<=` and would stay under `<` · live + archived = 6 either way
          record     tier T3 · the cutoff's comparison is the script's declaration
laws:     F1 F3
source:   expected · skills/op/archive-entity; skills/_index/multi-phase (source + archive == original)
note:     Conservation cannot tell `<` from `<=`; the boundary witness is how the record shows which one ran. A cutoff
          after every row empties the live table, and later changes then answer HasRows(Order)=no here (`over-time`).
```

### OP-archive-entity-04 · violating:referenced, archiving parents that children still reference
```case
kind:     corpus
tags:     violating:referenced
env:      pg + archive.OrderArchive + FK_OrderLine_Order · Order 6 rows, 2 with StatusId 3 · OrderLine 4 rows, 1 on a
          retired Order
change:   -02's batched move
expect:   witnesses  declared(a retired Order and an OrderLine on it)
          outcome    after the authored stop: verdict blocked constraint · Msg 547 · FK_OrderLine_Order · exit 3
          copy       live + archived still = the original: the failing DELETE … OUTPUT rolled back as one statement
          record     tier T3 · residue {}
laws:     H5 F1
source:   expected · skills/op/archive-entity (named trap: "child rows with FKs must move … before parents")
```

### OP-archive-entity-05 · violating:referenced, no FK, the move orphans children silently
```case
kind:     corpus
tags:     violating:referenced
env:      pg (no FK) + archive.OrderArchive · Order 6 rows, 2 with StatusId 3 · OrderLine 4 rows, 1 on a retired Order
change:   -02's batched move
expect:   witnesses  as -04
          outcome    after the authored stop: verdict clean · exit 0
          copy       the OrderLine's OrderId resolves to no live Order; its parent is in the archive
laws:     F3
source:   expected · skills/op/archive-entity; the no-FK face of OP-delete-seed-value-04
```

### OP-archive-entity-06 · edge: an archive table that copied the source's IDENTITY
```case
kind:     corpus
tags:     edge
env:      pg · Order 12 rows, 8 with StatusId 3
change:   as -01 and -02 in one release, but archive.OrderArchive.Id int IDENTITY(1,1)
expect:   witnesses  declared(as -02)
          outcome    after the authored stop: verdict blocked seed · Msg 544 · archive.OrderArchive · exit 3
          record     tier T3 · residue {main: archive, archive.OrderArchive created}
laws:     H5 H6
source:   expected · F12 records Msg 544 for an explicit-id insert into an IDENTITY column; the Twin test's archive is
          "a passive store — no IDENTITY, so the original Ids are preserved"
```

### OP-archive-entity-07 · edge: an archive table with a foreign key cannot be an OUTPUT INTO target
```case
kind:     corpus
tags:     edge
env:      pg + archive.OrderArchive with FK_OrderArchive_Status → Status · Order 12 rows, 8 with StatusId 3
change:   -02's batched move
expect:   witnesses  declared(as -02)
          outcome    after the authored stop: the move fails Msg 332 (an OUTPUT INTO target may not sit on either side
                     of a FOREIGN KEY) → stop unclassified · exit 9
laws:     H5 E1
source:   expected · the documented OUTPUT INTO restriction (no FOREIGN KEY, enabled trigger or CHECK on the target);
          ARCH §6 lists no rule for 332
```

### OP-archive-entity-08 · volume: an archive over a million rows raises no scale flag
```case
kind:     pure
tags:     edge
input:    delta: CREATE SCHEMA [archive]; CREATE TABLE [archive].[OrderArchive] (…); and a post-deploy changed
          against base: the batched DELETE … OUTPUT over [dbo].[Order]
          answers: HasRows(Order)=yes · Rows(Order)=3200000
action:   Get-FxTier, then Get-FxRecord
expect:   record     tier T3 · flags {} · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     H4
source:   expected · ARCH §7 (`scale` covers index builds, rebuilds, column alters and constraint validation in the
          delta, not authored DML); TWIN_SUCCESSION §6 (volume: timing, log size, CommandTimeout at >1M rows)
note:     The skill's ">1M rows: schedule a window" is the lead's line, carried as the `volume` residual; the flag
          does not fire for a move it cannot see in the delta.
```

### OP-archive-entity-09 · remedy: children move before their parents
```case
kind:     corpus
tags:     remedy
env:      as -04, plus archive.OrderLineArchive (OrderLine's columns, no IDENTITY, no FK)
change:   post-deploy: first the batched move of OrderLines whose Order is retired into archive.OrderLineArchive, then
          -02's batched move of the retired Orders
expect:   witnesses  declared(as -04)
          outcome    after the authored stop: verdict clean · exit 0
          copy       live + archived = original for Order and for OrderLine · no live OrderLine on an archived Order
          record     tier T3 · ships release 2 of 2 · notChecked {QA, UAT, Prod, volume, over-time, is-refresh}
laws:     A5 F1
source:   expected · skills/op/archive-entity ("child rows with FKs must move (or their FKs be disabled) before parents")
note:     Conservation is per table; an archive with no foreign keys has given up enforcement for the archived rows,
          which the record names.
```

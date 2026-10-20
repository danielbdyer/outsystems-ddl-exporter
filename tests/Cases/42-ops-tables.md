# 42 · Operations on tables, seeds, structure and multi-phase programs

*Op cases for the table-grain operations (`90-traceability.md` lists them), in `README.md`'s grammar and standard tags; one `##` heading per operation, naming its skill, its record and its Twin test class.*
*Unless `env` says otherwise, the environment proven is Dev (QA, UAT and Prod unread), the effective options are the pipeline's (BlockOnPossibleDataLoss=true, GenerateSmartDefaults=false, DropObjectsNotInSource=false, IncludeTransactionalScripts=true), and reference rows come from the `referenceData` lane as of base.*
*`notChecked` uses only INVARIANTS H4's closed tokens (`unread:<env>`, `volume`, `cdc`, `workload`, `platform:<what>`, `drift-since-measure`, `engine-version`, `integration-studio`, `external-consumers`, `app-behaviour`, `declared-not-measured`, …). A case that first meets the `authored-script` stop and then proves with witness.sql writes `leg1` (the stop) and `leg2` (the verdict); a case proven per environment writes one leg per `prove <env>`. `laws` cite INVARIANTS; bare `F<n>` in `source` cites FINDINGS_AND_CHANGES.md.*

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
          record     tier T0 · ships one release, in place · effects {adds CustomerPreference} · silence ok
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     F3 F4 D6
source:   expected · skills/op/create-entity ("emitted verbatim, and no existing data is read or written")
note:     The delta is one CREATE TABLE, and the tier is read off it.
          n/a clean — the table exists in no environment, so no populated state precedes the change.
          n/a violating — a table this delta creates is empty (ARCH §4, coordinates), so nothing can violate.
          n/a remedy — a new table has nothing to reconcile.
```

### OP-create-entity-02 · a foreign key to a populated parent (Twin parity)
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   head + CustomerPreference (Id IDENTITY PK · CustomerId int NOT NULL · PrefKey · PrefValue)
          with FK_CustomerPreference_Customer → Customer(Id)
expect:   questions  none: the delta's WITH NOCHECK ADD + WITH CHECK CHECK validate CustomerPreference, which the
                     same delta creates, so the Orphan question is answered from the delta (a new table is empty)
          witnesses  none
          outcome    verdict clean · exit 0
          copy       CustomerPreference 0 rows · FK_CustomerPreference_Customer is_not_trusted=0
          record     tier T0 · ships one release, in place
                     · effects {adds CustomerPreference, FK_CustomerPreference_Customer} · silence ok
                     · notChecked {unread:QA, unread:UAT, unread:Prod, engine-version, integration-studio}
laws:     F3 F4 S3 D6 T2
source:   receipt · SamplePr "create-entity: a new table with an identity PK and an FK to Customer applies clean"
note:     The CHECK CHECK matches the `Orphan` shape on a table this delta creates: ARCH §4's coordinate rule 1 answers
          it from the delta, never S3's missing-object stop. `engine-version`: trust on a declarative add is
          DacFx-dependent (FINDINGS F19).
```

### OP-create-entity-03 · a unique index on the new table
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   head + CustomerPreference (as -02) + Indexes/UX_CustomerPreference_Key.sql:
          CREATE UNIQUE INDEX UX_CustomerPreference_Key ON dbo.CustomerPreference (CustomerId, PrefKey)
expect:   questions  none: the unique build reads only CustomerPreference, which this delta creates, so the Unique
                     question is answered from the delta (empty: no duplicate)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place
                     · effects {adds CustomerPreference, UX_CustomerPreference_Key} · silence ok
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     F4 S3 D6
source:   expected · ARCH §4 (`Unique` row; coordinates: created by this delta, answered from the delta) and §7 (T0)
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
          record     tier T0 · effects {} · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
                     · silence ok
laws:     F3 D6
source:   expected · skills/op/create-entity ("a file the project glob misses silently never deploys")
note:     The facsimile proves the delta it is given. An empty `effects` on a pull request that says "add an
          entity" is the reviewer's cue; it is not a stop.
```

### OP-create-entity-06 · a table of the same name already exists in one environment
```case
kind:     corpus
tags:     edge
env:      Dev: pg · QA: pg + hand-made CustomerPreference (Id, CustomerId, PrefKey, PrefValue,
          Notes nvarchar(max) NULL),
          4 rows, every CustomerId a real Customer
change:   head + CustomerPreference with FK_CustomerPreference_Customer (as -02); no Notes column · proved per
          environment: leg1 `prove Dev`, leg2 `prove QA`
expect:   leg1       questions  none
                     witnesses  none
                     outcome    verdict clean · exit 0
                     record     tier T0 · silence ok · notChecked {unread:UAT, unread:Prod, integration-studio}
          leg2       answers    HasRows(CustomerPreference)=yes · Orphan(CustomerPreference.(CustomerId)→Customer)=clean
                     questions  HasRows(CustomerPreference) (the guard above DROP COLUMN Notes)
                                · Orphan(CustomerPreference.(CustomerId)→Customer)
                     witnesses  presence(CustomerPreference) (CustomerId linked to a planted Customer ancestor)
                     outcome    verdict blocked guard · Msg 50000/127 · dbo.CustomerPreference · exit 3
                     record     tier T2 · residue {} · flags {lag:QA, drift:QA} · drift:QA {CustomerPreference.Notes}
                                · notChecked {unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     F7 F1 H5 D6
source:   expected · red-team #1 (proving against main misses the delta each environment receives, drift included)
note:     A column is not an "object not in source", so QA's DROP COLUMN is generated whatever DropObjectsNotInSource
          says. Only QA's own extracted schema shows it; two proofs, because the delta hashes differ (F7).
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
                     · effects {} · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio} · silence ok
laws:     F3 F6 T2 D6
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
                     · residue {} · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                     integration-studio}
laws:     F1 F6 H5 H6
source:   receipt · sample-prs/delete-entity.md, What proving showed (DropObjectsNotInSource = true: SQL72015, then
          Msg 50000; OrderLine survived with 8 rows) · skills/op/delete-entity (diagnostic posture: "a Strict publish
          must block")
note:     The effective options decide (INVARIANTS F6): under the pipeline's options the same head is -01's phantom.
          n/a violating — a drop asks only HasRows, which has no value class.
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
                     · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
                     · silence ok
laws:     F1 F6 M2 D6
source:   expected · ARCH §4 (`HasRows`; a "no" plants nothing); skills/op/delete-entity ("table empty … a clean drop")
note:     Dev's "no" says nothing of Prod: the unread environments and `drift-since-measure` stay on the record, and the
          approval still weighs an irreversible drop.
```

### OP-delete-entity-04 · remedy: a scripted, idempotent pre-deploy DROP TABLE, the file removed in the same release
```case
kind:     corpus
tags:     remedy
env:      pg · OrderLine 8 rows · options DropObjectsNotInSource=false
change:   head − Modules/OrderLine.sql · Script.PreDeployment.sql +
          `IF OBJECT_ID(N'dbo.OrderLine', N'U') IS NOT NULL DROP TABLE dbo.OrderLine;`
          · Data/Seed.sql − its OrderLine MERGE
expect:   questions  Authored(Script.PreDeployment.sql) · Authored(Data/Seed.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.OrderLine:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(presence(OrderLine)) under `-- plants: OrderLine`
                     outcome    verdict clean · exit 0
                     copy       OrderLine absent · a second publish: plan empty,
                                and the pre-deploy's guard skips the drop
                     record     tier T3 · ships one release, scripted · effects {removes OrderLine} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     H3 A5 F2 M2 D6
source:   receipt · sample-prs/delete-entity.md, What proving showed (the file removed and an idempotent pre-deploy
          DROP TABLE under DropObjectsNotInSource = false: the table gone, a second publish a clean no-op) · SamplePr
          "delete-entity: removing a whole table from source is a phantom removal under DropObjectsNotInSource=false;
          the scripted DROP TABLE is the real, destructive one" · FINDINGS Part 4 (delete-entity row)
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.OrderLine:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(presence(OrderLine))
                     outcome    verdict clean · exit 0
                     copy       OrderLine dropped by R1
                     record     tier T3 · silence not-silent(R1 replay plan: CREATE TABLE [dbo].[OrderLine],
                                0 rows and a
                                new object_id: the F17 hazard) · ships the drop and the .sql removal in one release,
                                never apart · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     F8 F3 D6
source:   receipt · sample-prs/delete-entity.md, What proving showed (a pre-deploy DROP TABLE while the model still
          declares OrderLine: the next publish re-creates it, 0 rows, new object_id 1253579504) · FINDINGS Part 4 ("a
          model still holding the table re-creates it empty on the next publish")
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.OrderLine:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  `-- shapes: none (the failure does not depend on rows)`
                     outcome    verdict blocked authored · Msg 3726 · pre-deploy · exit 3
                     record     tier T3 · residue {pre-deploy: the DROP TABLE refused; nothing applied} · notChecked
                                {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     H5 H6 E1
source:   expected · skills/op/delete-entity ("the drop fails until they are dropped first") · ARCH §6 (a script's 3726
          is `blocked authored`)
note:     The plan drops FK_Return_OrderLine in the main script, after the pre-deploy has already failed. The ordered
          script (the key, then the table) is clean.
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
          record     tier T1 · effects {adds Customer.MiddleName, removes LegacyAudit} · flags {drift:QA}
                     · drift:QA {LegacyAudit} · silence ok · notChecked {unread:Dev, unread:UAT, unread:Prod,
                     drift-since-measure, integration-studio}
laws:     F7 F6 F3 D6
source:   receipt · red-team #14 ("environment-only objects dropped silently ✓")
note:     The drop is listed though the pull request never names LegacyAudit: Compare-FxDeltas finds it in
          delta.QA.sql and not in delta.pr.sql. Populated, the same QA blocks on a table nobody touched. Both faces
          exist only in QA's own delta.
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
          record     tier T0 · ships one release, in place · effects {renames OrderLine→OrderItem} · silence ok
                     · notChecked {unread:QA, unread:UAT, unread:Prod, cdc, integration-studio}
laws:     F3 F4 D6
source:   receipt · sample-prs/rename-entity.md, What proving showed (with the entry the delta is the sp_rename;
          OrderItem holds all 8 rows under object_id 1061578820; a second publish a clean no-op) ·
          skills/_index/identity-and-refactorlog
note:     This entry is the remedy for -02 and -03. `cdc` stays named: a CDC-tracked table refuses sp_rename (-06).
          n/a violating — a rename asks the data nothing.
          n/a remedy — the remedy for a missing entry is the entry itself, a declarative model change proven here; no
          authored script is involved.
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
                     · effects {adds OrderItem} · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
                     · silence ok
laws:     F3 F6 T2 D6
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
          questions  HasRows(OrderLine) (the guard above DROP TABLE [dbo].[OrderLine],
                     before CREATE TABLE [dbo].[OrderItem])
          witnesses  presence(OrderLine)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          record     tier T1 · residue {} (OrderItem never created) · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
laws:     F1 F6 H5 H6
source:   receipt · sample-prs/rename-entity.md, What proving showed (no entry: DROP TABLE then CREATE TABLE, SQL72015
          then Msg 50000; "OrderLine survived with 8 rows; OrderItem was never created") ·
          skills/_index/identity-and-refactorlog
```

### OP-rename-entity-04 · no entry: empty in Dev passes, populated in QA blocks
```case
kind:     corpus
tags:     empty
env:      Dev: pg · OrderLine 0 rows · QA: pg · OrderLine 8 rows · options DropObjectsNotInSource=true
change:   as -02 · proved per environment: leg1 `prove Dev`, leg2 `prove QA`
expect:   questions  HasRows(OrderLine)
          leg1       answers    HasRows(OrderLine)=no
                     witnesses  none
                     outcome    verdict clean · exit 0
                     record     tier T1 · effects {removes OrderLine, adds OrderItem} · silence ok · notChecked
                                {unread:UAT, unread:Prod, drift-since-measure, integration-studio}
          leg2       answers    HasRows(OrderLine)=yes
                     witnesses  presence(OrderLine)
                     outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
                     record     tier T1 · residue {} · notChecked {unread:UAT, unread:Prod, integration-studio}
laws:     F7 F1 D6
source:   expected · ARCH §4 (`HasRows`); skills/_index/identity-and-refactorlog
note:     Two proofs, because the answers hashes differ (F7). Dev's green verdict records a drop and a create, never a
          rename: `renames` is empty even where nothing blocked, which is the reviewer's cue that the entry is missing.
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
                     · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod, volume, integration-studio}
laws:     F1 F4 S3 D6
source:   receipt · sample-prs/rename-entity.md, What proving showed ("tmp_ms_xx_OrderItem … the 8 rows still
          survived, but the object_id changed to 1253579504")
note:     The rebuild names OrderItem, which the environment knows as OrderLine: ARCH §4's coordinate rule 2 maps it
          through the sp_rename earlier in the same delta and probes the old name, so S3 never stops missing-object.
```

### OP-rename-entity-06 · edge: a CDC-tracked table
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · Cdc(OrderLine)=yes
change:   as -01
expect:   answers    Cdc(OrderLine)=yes
          outcome    stop cdc · "OrderLine is tracked by CDC, and LocalDB has none. This change is principal-led."
                     · exit 9
          flags      {cdc}
laws:     H2 H3
source:   expected · ARCH §6 (`cdc`: the flag, and the stop on LocalDB); the engine fact it guards, sp_rename failing
          Msg 4928 on a CDC-tracked table, is red-team #6 ✓
note:     The copy has no CDC, so publishing would report a false clean. A designed stop (INVARIANTS F3 scope): the
          oracle's verdict is recorded for information only.
```

### OP-rename-entity-07 · edge: a deleted refactorlog entry, proven per environment
```case
kind:     corpus
tags:     edge
env:      Dev: pg with OrderItem 8 rows (the rename already applied) · UAT: pg with OrderLine 8 rows (not yet received)
change:   head declares [dbo].[OrderItem]; its Rename Refactor entry has been deleted from SampleCatalog.refactorlog ·
          proved per environment: leg1 `prove Dev`, leg2 `prove UAT`
expect:   questions  none
          leg1       outcome    verdict clean · exit 0
                     record     tier T0 · effects {} (plan empty) · silence ok
          leg2       outcome    verdict clean · exit 0
                     copy       OrderItem created empty, OrderLine stranded with its 8 rows
                     record     tier T0 · effects {adds OrderItem} · flags {lag:UAT} · lag:UAT {CREATE TABLE
                                [dbo].[OrderItem]} · silence ok
laws:     F7 F3 D6
source:   expected · skills/_index/identity-and-refactorlog (Refactorlog Cleanup: "a deleted entry re-becomes a drop+create")
note:     Proving Dev alone (or main) reports nothing: delta.pr.sql is empty, and only UAT's own delta shows the
          stranded table, which is what `lag:UAT` records.
```

### OP-rename-entity-08 · the offline delta under a refactorlog
```case
kind:     corpus
tags:     edge
env:      Dev: pg · OrderLine 8 rows · QA: pg · OrderItem 8 rows, `__RefactorLog` already holding the entry's key
change:   as -01
expect:   files      delta.Dev.sql carries the sp_rename · delta.QA.sql carries no rename statement · each equals the
                     live Script of head against that environment, after normalization · verdicts.json indexes both
          outcome    verdict clean · exit 0 (both environments; stdout names verdicts.json)
          record     silence ok
laws:     F5 F7 D6
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
                     · effects {adds sales, sales.OrderLine} · silence ok · notChecked {unread:QA, unread:UAT,
                     unread:Prod, integration-studio}
laws:     F3 F6 D6 T2
source:   receipt · SamplePr "move-schema: a header-edit move without a refactorlog is a phantom move; ALTER SCHEMA
          TRANSFER is the real one"
note:     The remedies are -03 and -04.
          n/a violating — a move asks the data nothing.
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
          record     tier T1 · residue {} · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
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
          record     tier T0 · ships one release, in place · effects {adds archive,
                     renames dbo.Category→archive.Category}
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio} · silence ok
laws:     F3 F4 D6
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none: the transfer moves whatever rows exist)
                     outcome    stop unclassified · "The publish ended with Msg 2714 that no rule classifies. Add a
                                rule with its case." · exit 9
laws:     H5 E1 F4
source:   verify · F2 is the receipt that the plan is computed before the pre-deploy runs; the failing statement and
          its number (2714 on CREATE SCHEMA or on CREATE TABLE in the generated main script) are unproven
note:     The pre-deploy succeeds; the main script then fails creating what the pre-deploy already made. H5 classifies
          an error by its phase: ARCH §6's authored 2714 covers a pre- or post-deploy batch, while a number in the
          generated main phase needs its own rule, so this stops unclassified. Read with -03: the single-release shape
          is the refactorlog entry. A scripted transfer belongs outside this release's model edit (an out-of-DACPAC
          step, then the model), never beside it.
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
                     · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
                     · silence ok
laws:     F1 F7 D6
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
          outcome    verdict blocked authored · Msg 208 · post-deploy · exit 3
          record     tier T0 · residue {main: Category moved to archive · post-deploy: the seed failed on dbo.Category}
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     H5 H6 F4
source:   expected · F15 (the seed's claims bind every atom in the release: the rename face failed until the seed's
          column references were renamed); this is the table-grain face, and ARCH §6 classes a script's 208 as
          `blocked authored`
note:     A post-deploy runs in full on every publish, so a script that did not change can still fail against a
          schema that did; tier T0 does not mean nothing can fail.
```

### OP-move-schema-07 · edge: a question on the moved table names its new address
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows
change:   a refactorlog move dbo.OrderLine → sales.OrderLine and OrderLine.Amount decimal(18,2) → decimal(9,2),
          one release
expect:   answers    HasRows(OrderLine)=yes · Fits(OrderLine.Amount→decimal(9,2))={}
          questions  HasRows(sales.OrderLine) · Fits(sales.OrderLine.Amount→decimal(9,2)), each resolved to the
                     environment's dbo.OrderLine through the delta's own move and probed under the old name
          witnesses  presence(OrderLine)
          outcome    verdict blocked guard · Msg 50000/127 · sales.OrderLine · exit 3
          record     tier T2 · residue {} (the move rolls back with the guard)
laws:     S3 F4 F1
source:   verify · whether DacFx orders the move before the guarded ALTER (so the guard names [sales].[OrderLine]);
          the resolution itself is ARCH §4's coordinate rule 2 (moved by this delta → probed under the old name)
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
expect:   questions  none: both WITH CHECK CHECK validate OrderTag, which this delta creates, so both Orphan
                     questions are answered from the delta (a new table is empty)
          witnesses  none
          outcome    verdict clean · exit 0
          copy       OrderTag 0 rows · PK over 2 columns · both FKs is_not_trusted=0
          record     tier T0 · ships one release, in place · effects {adds Tag, OrderTag} · silence ok
                     · notChecked {unread:QA, unread:UAT, unread:Prod, engine-version, integration-studio}
laws:     F3 F4 S3 D6 T2
source:   receipt · SamplePr "junction: a new bridge with two FKs and a composite PK applies clean, both legs trusted"
note:     A parent over 1M rows raises no `scale` flag: the validation reads only the empty bridge, so the skill's ">1M
          rows" line is the lead's.
          n/a clean — no environment holds the bridge before the change (a hand-made one is drift, -06).
```

### OP-junction-02 · remedy: seed pairs whose parents all exist
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 6 rows (Id 1–6) · Product 5 rows (Id 1–5)
change:   head + CustomerProduct (CustomerId, ProductId, composite PK, FKs to Customer and Product) ·
          Data/Seed.sql + guarded MERGE of pairs (1,1), (1,2), (2,1)
expect:   questions  Authored(Data/Seed.sql): the seed fills the new bridge before validation runs, so both Orphan
                     questions fold into it (ARCH §4)
          leg1       outcome    stop authored-script · "the seed names Customer and Product ids; declare which exist" ·
                                exit 5
          leg2       witnesses  declared(Customer Id 1, 2 and Product Id 1, 2 present) under `-- plants: Customer`,
                                `-- plants: Product`
                     outcome    verdict clean · exit 0
                     copy       CustomerProduct 3 rows · both FKs is_not_trusted=0 · a second publish touches 0 rows
                     record     tier T3 · ships one release (schema, then the post-deploy seed) · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                                integration-studio}
laws:     H3 A5 M2 F2 D6
source:   expected · skills/op/junction ("every pair needs a real row on both sides, or the create is blocked")
note:     No vocabulary question touches the parents, so their rows are declared, beyond M2's minimum. The planter's
          ordinal ids need not equal the seed's constants, which is why they cannot stand in; the seed's ids are
          repository-defined values (P5).
```

### OP-junction-03 · violating: a seed pair names a parent the environment lacks
```case
kind:     corpus
tags:     violating:orphan
env:      pg · Customer 6 rows · Product 5 rows, no Id 999
change:   as -02, plus the pair (1, 999)
expect:   questions  Authored(Data/Seed.sql)
          leg1       outcome    stop authored-script · "the seed names Customer and Product ids; declare which exist" ·
                                exit 5
          leg2       witnesses  declared(Customer 1 present; Product 999 absent)
                     outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
                     record     tier T3 · residue {main: CustomerProduct, PK, both FKs added WITH NOCHECK · post-deploy:
                                the seed's insert refused · validation: not reached, both FKs untrusted}
                                · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                                integration-studio}
laws:     H6 F1 H5
source:   expected · skills/op/junction (an orphan pair is rejected Msg 547); that the main script has committed when
          the post-deploy fails is F15's (a failed seed on a half-applied release)
note:     The enabled (untrusted) key refuses the seed's insert inside the post-deploy batch, so the 547 is `blocked
          authored` (H5). The residue's untrusted keys rest on validation running after the post-deploy (ARCH §11.9).
```

### OP-junction-04 · per environment: a seed's parent id exists in Dev, not in QA
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Product Id 1–5 · QA: pg · Product Id 1, 3, 4, 5, 6 (Id 2 deleted long ago)
change:   as -02 · proved per environment: leg1 `prove Dev`, leg2 `prove QA`, each after the authored stop
expect:   questions  Authored(Data/Seed.sql)
          leg1       witnesses  declared(Product 2 present)
                     outcome    verdict clean · exit 0
                     record     tier T3 · silence ok
          leg2       witnesses  declared(Product 2 absent)
                     outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
                     record     tier T3 · residue {main: CustomerProduct, PK, both FKs added WITH NOCHECK · post-deploy:
                                the seed's insert of (1, 2) refused}
laws:     F7 F1 H5
source:   expected · skills/op/junction ("QA, UAT, and Prod may hold parent rows this copy cannot see")
note:     Business-row ids are per environment; a seed that names them is proven per environment or not at all. The
          two proofs differ in their declarations, which needs witness.sql blocks scoped per environment: ARCH §8
          commits one witness.sql per change (reported).
```

### OP-junction-05 · edge: a duplicate pair in the seed
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows · Product 5 rows
change:   as -02, the seed's VALUES listing (1,1) twice
expect:   questions  Authored(Data/Seed.sql)
          leg1       outcome    stop authored-script · "the seed names Customer and Product ids; declare which exist" ·
                                exit 5
          leg2       witnesses  declared(Customer 1 and Product 1 present)
                     outcome    verdict blocked authored · Msg 2627 · post-deploy · exit 3
                     record     tier T3 · residue {main: CustomerProduct, PK, both FKs added WITH NOCHECK · post-deploy:
                                the MERGE's duplicate insert refused} · notChecked {unread:QA, unread:UAT, unread:Prod}
laws:     H5 H6
source:   expected · ARCH §6 (a seed's 2627 is `blocked authored`); that the composite PK rejects a duplicate pair with
          Msg 2627 is receipt in SamplePr "junction: a new bridge with two FKs and a composite PK applies clean, both
          legs trusted"
```

### OP-junction-06 · edge: QA already holds a hand-made bridge with an orphan pair
```case
kind:     corpus
tags:     edge
env:      Dev: pg · QA: pg + hand-made CustomerProduct (CustomerId int NOT NULL, ProductId int NOT NULL; no keys)
          3 rows, one pair on Product 999
change:   head + CustomerProduct with its composite PK and both FKs (as -02, no seed) · proved per environment: leg1
          `prove Dev`, leg2 `prove QA`
expect:   leg1       questions  none
                     outcome    verdict clean · exit 0
                     record     tier T0 · silence ok
          leg2       answers    Unique(CustomerProduct,(CustomerId, ProductId))={}
                                · Orphan(CustomerProduct.(CustomerId)→Customer)=clean
                                · Orphan(CustomerProduct.(ProductId)→Product)=violating
                     questions  Unique(CustomerProduct,(CustomerId, ProductId)) · both Orphan legs
                     witnesses  presence(CustomerProduct) (+ancestors Customer, Product: both columns linked) ·
                                orphan(CustomerProduct.(ProductId)→Product)
                     outcome    verdict blocked constraint · Msg 547 · FK_CustomerProduct_Product · exit 3
                     record     tier T2 · flags {lag:QA} · residue {main: PK built, both FKs added WITH NOCHECK ·
                                validation: FK_CustomerProduct_Customer trusted, FK_CustomerProduct_Product present with
                                is_not_trusted=1}
laws:     F7 F1 H6
source:   expected · red-team #1 (drift); the untrusted half-applied key after a 547 at WITH CHECK CHECK is F9
          (db_orphD) and red-team #4 ✓
note:     The 547 is raised at validation, outside any authored batch, so it is `blocked constraint` (H5). `lag:QA`:
          QA receives ALTERs on an existing table where delta.pr creates it.
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
          record     tier T0 · ships one release, in place · effects {adds Rate, Rate_History} · silence ok
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     F3 F4 D6 T2
source:   receipt · SamplePr "temporal-new: a new system-versioned entity applies clean with its history table"
note:     An existing table is temporal-convert.
          n/a clean — the table exists in no environment, so no populated state precedes it.
          n/a violating — a table this delta creates is empty; its new history table holds no period to be invalid.
          n/a remedy — a new table has nothing to reconcile.
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
change:   as -01 · proved per environment: leg1 `prove Dev`, leg2 `prove QA`
expect:   leg1       questions  none
                     outcome    verdict clean · exit 0
          leg2       answers    HasRows(Rate)=yes · Temporal(Rate)=clean (the period columns are new: answered from
                                the delta)
                     questions  HasRows(Rate) · Temporal(Rate): the conversion of a populated table (OP-temporal-convert-01)
                     witnesses  presence(Rate)
                     outcome    verdict blocked guard · Msg 50000/127 · dbo.Rate · exit 3
                     record     tier T2 · flags {lag:QA} · residue {}
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
expect:   answers    HasRows(Customer)=yes · Temporal(Customer)=clean (the period columns are new: answered from the
                     delta)
          questions  HasRows(Customer) (the guard above the NOT NULL period columns)
                     · Temporal(Customer) (`ADD PERIOD` /
                     `SET (SYSTEM_VERSIONING = ON)`)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · residue {} · ships staged (-03) · notChecked {unread:QA, unread:UAT, unread:Prod,
                     drift-since-measure, integration-studio}
laws:     F4 F1 H5
source:   verify · the delta is unrecorded: SamplePr "temporal-convert: turning the existing populated Customer
          system-versioned — the true outcome under the production gate, existing rows preserved" passes on either
          path and records no verdict, while sample-prs/README says "single publish blocks (no-default period
          columns)". Whether DacFx guards the NOT NULL period columns (this verdict) or rebuilds through tmp_ms_xx
          (then `Rebuild` matches too) is what the first run decides
note:     ARCH §4's `Temporal` row classifies the versioning switch, so the conversion no longer stops
          unknown-statement. No value class makes a conversion refuse a row; what a row means (its ROW START) is -05's.
```

### OP-temporal-convert-02 · an empty table converts in place
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   as -01
expect:   answers    HasRows(Customer)=no · Temporal(Customer)=clean
          questions  HasRows(Customer) · Temporal(Customer)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · effects {modifies Customer, adds Customer_History}
                     · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                     integration-studio}
laws:     F4 F3 D6
source:   verify · as -01 (the delta's shape is unrecorded); skills/op/temporal-convert ("existing empty table → collapses
          to a single schema change")
note:     The tier is T2 over an empty table: it comes from the delta's `Temporal` question, never from the rows.
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
expect:   questions  from R2's target (F8): HasRows(Customer) · Temporal(Customer) · Authored(Script.PreDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Customer) · declared(none beyond presence: the script writes only the new
                                columns)
                     outcome    verdict clean · exit 0
                     copy       Customer temporal_type=2 · Customer_History linked · MIN(ValidFrom) = 2020-01-01 · hash
                                of (Id, Name, Email, ContactPhone, Region, AccountId) equal before and after
                     record     tier T3 · ships two releases (R1 converts with the model lagging; R2 catches up) · R1
                                clean · R2 clean, empty plan · silence R1 not-silent(replay plan: the lagging model
                                undoes the conversion, the F17 hazard) · R2 ok · flags {in-flight} between R1 and R2
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     F8 A5 H3 D6
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
expect:   questions  as -03
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as -03
                     outcome    verdict clean · exit 0
                     record     tier T3 · R1 clean · R2 clean, but its plan non-empty (drop the system-named defaults,
                                add the named ones): R2 effects {modifies Customer defaults}, not the no-op the
                                program promised
laws:     F9 F8
source:   verify · ARCH §11.10 (whether DacFx treats a system-named default as different from a named one with the same
          definition); INVARIANTS F9 names default-constraint names as metadata fidelity
```

### OP-temporal-convert-05 · edge: the ROW START source dates every row to the conversion
```case
kind:     corpus
tags:     edge
env:      pg · Customer 6 rows
change:   as -03, but ValidFrom DEFAULT SYSUTCDATETIME()
expect:   questions  as -03
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as -03
                     outcome    verdict clean · exit 0
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
expect:   questions  as -03
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as -03
                     outcome    verdict blocked authored · Msg 13575 · pre-deploy · exit 3
                     record     tier T3 · residue {pre-deploy: the period columns added; the versioning switch refused}
laws:     H5 H6
source:   verify · the message (13575 or a neighbour) and which statement raises it; the Twin test's remedy notes
          "ValidTo must be the datetime2 max for every current row or the versioning switch's consistency check fails"
note:     Whatever the number, an error raised inside the pre-deploy batch is `blocked authored` (H5). The pre-deploy's
          earlier statements stay applied (FINDINGS F6), and the residue lists them.
```

### OP-temporal-convert-07 · violating: re-enabling versioning over a history row whose period ends before it starts
```case
kind:     corpus
tags:     violating:invalid
env:      pg + Customer with ValidFrom/ValidTo datetime2 GENERATED ALWAYS AS ROW START/END and PERIOD FOR SYSTEM_TIME,
          SYSTEM_VERSIONING turned OFF for maintenance · Customer 6 rows · Customer_History 3 rows, one whose ValidTo
          is earlier than its ValidFrom
change:   head declares SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.Customer_History)
expect:   answers    HasRows(Customer)=yes · Temporal(Customer)=violating
          questions  Temporal(Customer)
          witnesses  presence(Customer) · misfit(Customer.period, invalid): a history row planted while versioning is
                     OFF, its end before its start
          outcome    verdict blocked temporal · Msg 13575 · dbo.Customer · exit 3
          record     tier T2 · residue {} · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure}
laws:     F1 F3 F4 H5
source:   verify · ARCH §6 (`blocked temporal`: the consistency check's failure, raised by a generated `Temporal`
          statement) · ARCH §11.13 (whether SET SYSTEM_VERSIONING = ON runs its consistency check on LocalDB as on SQL
          Server 2022, and which message a failed check raises: 13575 is provisional)
note:     A number raised by the generated main phase is classified by its rule; the same failure inside a pre-deploy
          is `blocked authored` (-06).
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
expect:   answers    SeedDrift(Priority)=clean (answered from the delta: a new table holds no row to disagree)
          questions  Authored(Data/Seed.sql) · SeedDrift(Priority); the CREATE reads no row
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none: the MERGE touches only Priority, which this delta creates)
                     outcome    verdict clean · exit 0
                     copy       Priority 3 rows · Id not IDENTITY · a second publish: plan empty, 0 rows touched,
                                Priority's hash identical
                     record     tier T3 · ships one release (schema, then the post-deploy seed) · effects {adds
                                Priority} · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                                integration-studio}
laws:     A5 H3 F3 D6 T2
source:   receipt · SamplePr "create-static-seed: a new lookup + guarded MERGE lane seeds on first converge and is
          idempotent (0 rows + identical hash) on the second"; F12 (the guarded Category MERGE touches 0 rows on re-run)
note:     The stop names no column to shape, because the script reads only an object this delta creates; declaring
          "none" follows it literally and clears it. An environment that already holds the lookup is edit-seed's
          (OP-edit-seed-03, -05).
          n/a clean — the lookup exists in no environment, so no populated-but-clean state precedes it.
```

### OP-create-static-seed-02 · edge: an unguarded WHEN MATCHED rewrites every row with the same values
```case
kind:     corpus
tags:     edge
env:      pg (no Priority)
change:   as -01, but `WHEN MATCHED THEN UPDATE SET Name = s.Name` (no comparison)
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Priority), answered from the delta
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none: the MERGE touches only Priority, which this delta creates)
                     outcome    verdict clean · exit 0
                     copy       the second publish touches 3 rows · Priority's hash identical
                     record     tier T3 · silence not-silent(the second publish rewrote 3 rows over unchanged values;
                                the hash identical) · notChecked {unread:QA, unread:UAT, unread:Prod,
                                integration-studio}
laws:     F3 D6
source:   expected · INVARIANTS D6 (an unguarded MERGE and a same-value rewrite, caught by rows affected, not by the
          hash, are not-silent) · F12 (the unguarded Category MERGE touches 3 rows over unchanged rows)
note:     Nothing fails, so the verdict is clean; the record's silence line is where the churn shows.
```

### OP-create-static-seed-03 · edge: an IDENTITY lookup key seeded with explicit ids
```case
kind:     corpus
tags:     edge
env:      pg (no Priority)
change:   as -01, but Priority.Id int IDENTITY(1,1); the seed inserts explicit ids with no SET IDENTITY_INSERT
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Priority), answered from the delta
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none)
                     outcome    verdict blocked authored · Msg 544 · post-deploy · exit 3
                     record     tier T3 · residue {main: Priority created, empty · post-deploy: the seed's explicit-id
                                insert refused}
laws:     H5 H6
source:   receipt · F12 (identity-swap: "the post-deploy seed's explicit-id insert fails Msg 544 until bracketed with
          SET IDENTITY_INSERT"); F15 (the main script has committed when the post-deploy seed fails)
note:     Explicit ids are the model's (idempotent-seed: "a constant must mean the same row across environments");
          the fix is the explicit-id key, not the bracket. A 544 inside the seed is `blocked authored` (H5).
```

### OP-create-static-seed-04 · edge: a child's new NOT NULL reference to the lookup, satisfied by the post-deploy seed
```case
kind:     corpus
tags:     edge
env:      pg · Order 6 rows
change:   as -01 · Order + PriorityId int NOT NULL DEFAULT 1 + FK_Order_Priority → Priority
expect:   answers    HasRows(Order)=yes
          questions  measures Rows(Order) (Scan: the defaulted NOT NULL add) · Orphan(Order.(PriorityId)→Priority),
                     answered from the delta: every Order holds the default 1 and Priority is new, so it is an orphan
                     unless a script fills the parent; the post-deploy seed does, so the question becomes
                     Authored(Data/Seed.sql) (ARCH §4)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) · declared(none: the seed writes only Priority)
                     outcome    verdict clean · exit 0
                     copy       every Order's PriorityId is 1 · FK_Order_Priority is_not_trusted=0
                     record     tier T3 · ships one release (the lookup, its seed and the reference together) · silence
                                ok · notChecked {unread:QA, unread:UAT, unread:Prod, volume, integration-studio}
laws:     F4 F1 S3 D6
source:   verify · ARCH §4 (coordinates: an FK to a new parent is answered from the delta, and a seeded lookup counts) ·
          ARCH §11.9: constraint validation ran after the post-deploy on sqlpackage 170.5.96 (observed 2026-10-18, not a
          receipt); whether it does on 162.x decides this verdict, else the WITH CHECK CHECK meets an empty Priority and
          blocks Msg 547
note:     The default must be a value the seed carries: -06 is the case where it is not, and -07 its remedy. Seeding
          the lookup a release earlier stays the safe shape wherever the pinned DacFx validates before the post-deploy.
```

### OP-create-static-seed-05 · edge: a bare INSERT seed duplicate-keys on the second publish
```case
kind:     corpus
tags:     edge
env:      pg (no Priority)
change:   as -01, but the seed is `INSERT dbo.Priority (Id, Name) VALUES (1, N'High'), (2, N'Medium'), (3, N'Low');`
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Priority), answered from the delta
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none)
                     outcome    verdict blocked authored · Msg 2627 · post-deploy · exit 3
                     record     tier T3 · silence not-silent(the replay publish failed Msg 2627 inside the post-deploy) ·
                                residue {post-deploy: the replay's INSERT refused; Priority holds the first publish's 3
                                rows}
laws:     H5 D6
source:   verify · skills/_index/idempotent-seed ("Never a bare INSERT (duplicate-keys on the second deploy)") · ARCH §5
          step 11 runs the replay but does not say how an error inside it is classified; this case proposes that the
          replay's failure is the verdict (reported)
note:     The first publish is clean; the second, silence publish is the one every later deploy is, and it is where a
          non-idempotent seed fails.
```

### OP-create-static-seed-06 · violating:orphan, the reference's default is a value the seed does not carry
```case
kind:     corpus
tags:     violating:orphan
env:      pg · Order 6 rows
change:   as -04, but Order.PriorityId DEFAULT 0, and the seed carries only 1 High, 2 Medium, 3 Low
expect:   answers    HasRows(Order)=yes
          questions  measures Rows(Order) (Scan) · Orphan(Order.(PriorityId)→Priority), folded into
                     Authored(Data/Seed.sql) because the seed fills the new parent (ARCH §4)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) · declared(none: the seed writes only Priority)
                     outcome    verdict blocked constraint · Msg 547 · FK_Order_Priority · exit 3
                     record     tier T3 · residue {main: Priority created, Order.PriorityId added with its default 0,
                                FK_Order_Priority added WITH NOCHECK · post-deploy: Priority seeded 1–3 · validation:
                                FK_Order_Priority present, untrusted (is_not_trusted=1)} · notChecked {unread:QA,
                                unread:UAT, unread:Prod}
laws:     F1 F4 H5 H6 S3
source:   verify · as -04 (ARCH §11.9: validation after the post-deploy); the untrusted residue after a 547 at WITH
          CHECK CHECK is F9 and red-team #4 ✓
note:     The 547 is raised at validation, outside any authored batch, so it is `blocked constraint` (H5), with the
          post-deploy already applied in the residue (H6's phase order: pre-deploy, main, post-deploy, validation).
```

### OP-create-static-seed-07 · remedy: the seed carries the reference's default value
```case
kind:     corpus
tags:     remedy
env:      pg · Order 6 rows
change:   as -06, the developer's answer recorded in the seed: the Priority MERGE gains (0, N'Unassigned')
expect:   answers    HasRows(Order)=yes
          questions  measures Rows(Order) (Scan) · Authored(Data/Seed.sql), the Orphan question folded into it (ARCH §4)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Priority:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Order) · declared(none: the seed writes only Priority)
                     outcome    verdict clean · exit 0
                     copy       Priority 4 rows · every Order's PriorityId is 0 · FK_Order_Priority is_not_trusted=0
                     record     tier T3 · ships one release · effects {adds Priority, Order.PriorityId,
                                FK_Order_Priority} · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                                volume, integration-studio}
laws:     A5 F1 D6
source:   verify · as -04 (ARCH §11.9) · skills/_index/idempotent-seed (explicit ids, a constant meaning the same row in
          every environment)
note:     Changing the default to a seeded value is the same remedy as a model edit; either way the value lives in the
          repository, not in an environment.
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
expect:   answers    HasRows(Category)=yes · SeedDrift(Category)=clean
          questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none: SeedDrift(Category)=clean, so the merge-base lane reproduces Dev)
                     outcome    verdict clean · exit 0
                     copy       Category 4 rows · the first publish touches 1 row, the second 0 with the hash identical
                     record     tier T3 · ships one release (post-deploy seed) · silence ok · notChecked {unread:QA,
                                unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     A5 H3 D6
source:   receipt · F12 (edit-seed: "an inserted Refunded row touches 1, a re-run 0")
note:     SeedDrift counts the rows whose (Id, Code) disagrees with the seed's VALUES (repository values are sent; only
          an integer returns). `shape --vocabulary` would return nothing here: each code is seen once, under k = 5 (P6).
```

### OP-edit-seed-02 · a label change touches exactly one row (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg · Status 3 rows (1 Pending, 2 Shipped, 3 Cancelled)
change:   Data/Seed.sql: Status Id 3 'Cancelled' → 'Canceled' through the guarded WHEN MATCHED
expect:   answers    SeedDrift(Status)=violating (1 row: Id 3 holds 'Cancelled', the seed's Code is 'Canceled')
          questions  Authored(Data/Seed.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none: the merge-base lane's own (3, 'Cancelled') already disagrees on the copy)
                     outcome    verdict clean · exit 0
                     copy       exactly 1 row updated; Ids 1 and 2 untouched · the second publish 0 rows, hash identical
                     record     tier T3 · ships one release (post-deploy seed) · silence ok · notChecked {unread:QA,
                                unread:UAT, unread:Prod, integration-studio}
laws:     A5 D6 T2
source:   receipt · SamplePr "edit-seed: the guarded WHEN MATCHED UPDATE changes exactly one row and the re-run is
          silent (0 rows + identical hash)"; F12 ("a label change (Hardware → Hardware Pro) exactly 1")
note:     A relabel is a disagreement by construction: SeedDrift counts the one row the seed means to change, and the
          copy's merge-base lane row counts the same 1, so acceptance holds with nothing declared (a declared Id 3 would
          collide with the lane row). A count above 1 would mean Dev disagrees with the seed elsewhere too.
```

### OP-edit-seed-03 · violating: the new Code already exists under another Id in QA
```case
kind:     corpus
tags:     violating:dup-code
env:      Dev: pg + UX_Category_Code (unique index on Code) · Category 3 rows (lane) · QA: as Dev, plus (5, 'Refunded')
          added by hand
change:   as -01, the seed adding (4, N'Refunded', 1) · proved per environment: leg2 `prove Dev`, leg3 `prove QA`
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       answers    SeedDrift(Category)=clean
                     witnesses  declared(none)
                     outcome    verdict clean · exit 0
                     record     tier T3 · silence ok
          leg3       answers    SeedDrift(Category)=violating: the seed's Code 'Refunded' sits under Id 5 in QA
                     witnesses  declared(seed drift row: Category (5, 'Refunded'), its Code taken from the seed itself)
                     outcome    verdict blocked authored · Msg 2601 · post-deploy · exit 3
                     record     tier T3 · residue {post-deploy: the MERGE's insert of (4, 'Refunded') refused; the
                                schema is unchanged} · notChecked {unread:UAT, unread:Prod, drift-since-measure}
laws:     F7 F1 P5 H5
source:   expected · red-team #15 (seed ids differ per environment and collide; not marked verified there); 2601 for
          a unique index, 2627 for a unique constraint
note:     The witness must equal a seed Code to collide, so it cannot carry P5's `w` prefix; it is a repository-defined
          value, not an environment value. SeedDrift is the templated probe that reads the Id↔Code pairing, and the
          2601 is raised inside the seed, so it is `blocked authored` (H5). The remedy is a developer decision (adopt
          QA's id everywhere, -08), asked through ask-the-developer.
```

### OP-edit-seed-04 · edge: a MERGE source naming one Id twice
```case
kind:     corpus
tags:     edge
env:      pg · Status 3 rows (lane)
change:   the Status seed's VALUES carries (3, N'Canceled', 1) and (3, N'Void', 1), and its guarded WHEN MATCHED fires
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none)
                     outcome    verdict blocked authored · Msg 8672 · post-deploy · exit 3
                     record     tier T3 · residue {post-deploy: the MERGE refused as one statement}
laws:     H5
source:   expected · ARCH §6 (a seed's 8672, "the MERGE statement attempted to UPDATE or DELETE the same row more than
          once", is `blocked authored`)
```

### OP-edit-seed-05 · edge: the Id is taken in QA by another Code, and the relabel is green
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Category 3 rows (lane) · QA: Category 4 rows incl. (4, 'Backorder') added by hand · Product 2 rows
          with CategoryId 4
change:   as -01, the seed adding (4, N'Refunded', 1) · proved per environment: leg2 `prove Dev`, leg3 `prove QA`
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       answers    SeedDrift(Category)=clean
                     witnesses  declared(none)
                     outcome    verdict clean · exit 0
                     copy       Category Id 4 inserted
          leg3       answers    SeedDrift(Category)=violating: Id 4 holds a Code other than the seed's
                     witnesses  declared(seed drift row: Category (4, 'wBackorder'); 2 Product rows with CategoryId 4)
                     outcome    verdict clean · exit 0
                     copy       Category Id 4 relabelled to 'Refunded', its 2 Products now mean Refunded
                     record     tier T3 · the same seed inserts in Dev and updates in QA
laws:     F7 F3
source:   expected · red-team #15; skills/_index/idempotent-seed (explicit ids: "a constant must mean the same row
          across environments")
note:     Nothing blocks: the relabel is green and wrong. QA's SeedDrift answer is what puts the disagreement on QA's
          record; its declared row is what makes the copy show an update where Dev shows an insert.
```

### OP-edit-seed-06 · edge: a guard that is not null-safe never applies a NULL → '' change
```case
kind:     corpus
tags:     edge
env:      pg + Category.Label nvarchar(50) NULL · Category 3 rows, Label NULL×3
change:   the seed sets Label N'' for Id 1 with the guard `t.Label <> s.Label`
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Category Label NULL ×3, matching Dev)
                     outcome    verdict clean · exit 0
                     copy       Category 1's Label stays NULL: `NULL <> ''` is UNKNOWN and the guard never fires; the hash
                                keeps NULL distinct from '', so a null-safe guard instead changes 1 row once, then 0
                     record     tier T3 · silence ok
laws:     F3 F1 D6
source:   expected · skills/_index/idempotent-seed ("Null-safe comparison — treat NULL and '' as distinct")
note:     Silence passes and the seed's claim never lands; the engine accepts both, so this shows only in content.
          SeedDrift compares (Id, Code), not Label, so it reads clean here.
```

### OP-edit-seed-07 · empty: the lane never ran in the environment, and the copy's lookup comes from the merge-base
```case
kind:     corpus
tags:     empty
env:      pg · Category 0 rows (the lane never ran in Dev)
change:   Data/Seed.sql: the Category MERGE gains (4, N'Refunded', 1)
expect:   answers    HasRows(Category)=no · SeedDrift(Category)=clean
          questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  `-- shapes: none (Dev's Category is empty)`
                     outcome    verdict clean · exit 0
                     copy       Category 4 rows after the publish: the merge-base lane's 3, then the seed's insert of 4
                     record     tier T3 · ships one release (post-deploy seed) · silence ok · notChecked {unread:QA,
                                unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     F3 D6 A5
source:   expected · ARCH §5 step 1 (the copy runs the merge-base referenceData lanes) · skills/_index/idempotent-seed
          (the guarded MERGE inserts what is missing)
note:     Dev's own publish would insert all four rows where the copy inserts one: the verdict agrees (F3), the rows
          affected do not. SeedDrift counts disagreeing rows, and an empty lookup has none.
```

### OP-edit-seed-08 · remedy: the seed adopts the id the code already holds in QA
```case
kind:     corpus
tags:     remedy
env:      as OP-edit-seed-03 (QA holds (5, 'Refunded') added by hand; Dev holds the lane's 3 rows)
change:   the developer's answer (ask-the-developer: adopt QA's id everywhere) recorded in the seed: the Category MERGE
          gains (5, N'Refunded', 1) instead of (4, …) · proved per environment: leg2 `prove Dev`, leg3 `prove QA`
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       answers    SeedDrift(Category)=clean
                     witnesses  declared(none)
                     outcome    verdict clean · exit 0
                     copy       Category Id 5 'Refunded' inserted
                     record     tier T3 · silence ok
          leg3       answers    SeedDrift(Category)=clean: QA's (5, 'Refunded') now agrees with the seed
                     witnesses  declared(Category (5, 'Refunded'), the seed's own row)
                     outcome    verdict clean · exit 0
                     copy       the MERGE matches Id 5 and touches 0 rows
                     record     tier T3 · silence ok · notChecked {unread:UAT, unread:Prod, drift-since-measure,
                                integration-studio}
laws:     F7 A5 D6
source:   expected · red-team #15 (seed ids differ per environment and collide) · skills/_index/idempotent-seed
          (explicit ids: "a constant must mean the same row across environments")
note:     The fix is a repository decision, not a per-environment reconcile: the same constant now means the same row
          in Dev and in QA.
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
expect:   answers    SeedDrift(Category)=clean
          questions  Authored(Data/Seed.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none: no foreign key, so the referencing rows cannot change the outcome)
                     outcome    verdict clean · exit 0
                     copy       Category 1 IsActive=0 and still present · the second publish 0 rows, hash identical
                     record     tier T3 · ships one release (post-deploy seed) · silence ok · notChecked {unread:QA,
                                unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     A5 M2 D6
source:   receipt · sample-prs/delete-seed-value.md, What proving showed (pg_move: set IsActive = 0 instead; the row and
          its references stay intact, and the redeploy is silent)
note:     Deactivation is this operation's remedy for -03 and -04 (-07).
          n/a empty — a value never seeded in an environment cannot be retired there; the MERGE would insert it inactive,
          which is create-static-seed's first publish (OP-create-static-seed-01).
```

### OP-delete-seed-value-02 · removing the value from the seed deletes nothing (Twin parity)
```case
kind:     corpus
tags:     edge
env:      pg + FK_Order_Status (Order.StatusId → Status) · Status 3 rows · Order 6 rows, StatusId 1 ×6
change:   Data/Seed.sql: the Status MERGE's VALUES drops (3, N'Cancelled', 1); no WHEN NOT MATCHED BY SOURCE arm
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none)
                     outcome    verdict clean · exit 0
                     copy       Status still 3 rows, Id 3 present · the MERGE touched 0 rows
                     record     tier T3 · the change's intent ("delete the value") did not happen: the copy is
                                unchanged · silence ok
laws:     T2 F3 D6
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the
          explicit, FK-guarded removal blocks a referenced value (Msg 547)"
```

### OP-delete-seed-value-03 · violating: an explicit DELETE of a referenced value under a declared FK
```case
kind:     corpus
tags:     violating:referenced
env:      pg + FK_Order_Status · Status 3 rows · Order 6 rows, StatusId 1 ×6
change:   post-deploy + `DELETE dbo.Status WHERE Id = 1;`
expect:   questions  Authored(Script.PostDeployment.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(one Order row with StatusId 1) under `-- plants: Order`
                     outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
                     copy       Status 1 still present
                     record     tier T3 · residue {post-deploy: the DELETE refused as one statement}
laws:     H5 F1 T2
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the
          explicit, FK-guarded removal blocks a referenced value (Msg 547)"
note:     No vocabulary question plants Order, so the referencing row is declared; without it the copy deletes cleanly
          and disagrees with Dev. The 547 is raised inside the post-deploy, so it is `blocked authored` (H5).
```

### OP-delete-seed-value-04 · violating: an explicit DELETE of a referenced value with no FK, a silent orphan
```case
kind:     corpus
tags:     violating:referenced
env:      pg (no FK) · Category 3 rows · Product 5 rows, CategoryId 1 ×2
change:   post-deploy + `DELETE dbo.Category WHERE Id = 1;`
expect:   questions  Authored(Script.PostDeployment.sql) · SeedDrift(Category)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Category:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(2 Product rows with CategoryId 1)
                     outcome    verdict clean · exit 0
                     copy       Category 1 absent · 2 Product rows point at no Category
                     record     tier T3 · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                                drift-since-measure, integration-studio}
laws:     F3 D6
source:   receipt · sample-prs/delete-seed-value.md, What proving showed (pg_move: the hard DELETE succeeded and
          orphaned 2 Product rows) · skills/op/delete-seed-value ("not refused unless a foreign key is declared")
note:     Green and harmful: the engine has nothing to refuse. The PR's after-deploy LEFT JOIN catches the orphans;
          deactivation (-01, -07) is the shipped shape.
```

### OP-delete-seed-value-05 · an explicit DELETE of an unreferenced value, idempotent
```case
kind:     corpus
tags:     clean
env:      pg + FK_Order_Status · Status 3 rows · Order 6 rows, StatusId 1 ×6 (Status 3 unreferenced)
change:   Data/Seed.sql: the VALUES drops (3, …) · post-deploy + `DELETE dbo.Status WHERE Id = 3;` after the MERGE
expect:   questions  Authored(Data/Seed.sql, Script.PostDeployment.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order rows referencing only Status 1)
                     outcome    verdict clean · exit 0
                     copy       Status 2 rows · the second publish deletes 0 rows
                     record     tier T3 · silence ok
laws:     F3 A5 D6
source:   receipt · SamplePr "delete-seed-value: removing a value from the additive seed does NOT delete the row; the
          explicit, FK-guarded removal blocks a referenced value (Msg 547)" (the explicit DELETE of the unused value
          succeeds; a second identical DELETE affects 0 rows)
note:     Leave (3, …) in the VALUES and every publish re-inserts then deletes it: not-silent by rows affected, though
          the hash holds (D6).
```

### OP-delete-seed-value-06 · edge: a WHEN NOT MATCHED BY SOURCE arm meets QA's hand-added, referenced value
```case
kind:     corpus
tags:     edge
env:      Dev: pg + FK_Order_Status · Status 3 rows · QA: as Dev, plus Status (4, 'Backorder') referenced by 2 Orders
change:   the Status seed gains `WHEN NOT MATCHED BY SOURCE THEN DELETE` · proved per environment: leg2 `prove Dev`,
          leg3 `prove QA`
expect:   questions  Authored(Data/Seed.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(none)
                     outcome    verdict clean · exit 0 (0 rows deleted)
                     record     tier T3 · silence ok
          leg3       witnesses  declared(Status (4, 'wBackorder'); 2 Orders with StatusId 4)
                     outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
                     record     tier T3 · residue {post-deploy: the MERGE refused as one statement}
laws:     F7 F1 H5
source:   expected · skills/_index/idempotent-seed ("WHEN NOT MATCHED BY SOURCE THEN DELETE — only with care and a
          scoped source; it deletes rows the seed simply didn't list")
note:     Without the FK, QA's run is green and orphans 2 Orders. Either way only QA's own proof sees it.
```

### OP-delete-seed-value-07 · remedy: the referenced value deactivated, not deleted
```case
kind:     corpus
tags:     remedy
env:      as OP-delete-seed-value-03 (FK_Order_Status · Status 3 rows · Order 6 rows, StatusId 1 ×6)
change:   the post-deploy DELETE removed · Data/Seed.sql: Status Id 1 IsActive 1 → 0 through the guarded WHEN MATCHED
expect:   answers    SeedDrift(Status)=clean
          questions  Authored(Data/Seed.sql) · SeedDrift(Status)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Status:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(one Order row with StatusId 1) under `-- plants: Order`
                     outcome    verdict clean · exit 0
                     copy       Status 1 IsActive=0 and still present · every Order still resolves · the second publish
                                0 rows, hash identical
                     record     tier T3 · ships one release (post-deploy seed) · silence ok · notChecked {unread:QA,
                                unread:UAT, unread:Prod, drift-since-measure, app-behaviour}
laws:     A5 F1 D6
source:   expected · sample-prs/delete-seed-value.md, What proving showed (set IsActive = 0 instead: the row and its
          references stay intact, and the redeploy is silent; recorded without a foreign key) ·
          skills/_index/idempotent-seed (deactivate, don't delete)
note:     `app-behaviour`: screens and inserts must stop offering the inactive value; the deploy cannot show that.
```

---

## extract-to-lookup

`skills/op/extract-to-lookup` · `skills/_index/multi-phase` · `sample-prs/extract-to-lookup.md` · Twin `SamplePrSeedTests` · the whole program is CMP-17

### OP-extract-to-lookup-01 · phase 1: the lookup created, seeded, referenced and backfilled; the mapping total (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg + Order.Channel nvarchar(20) NOT NULL · Order 12 rows · Channel {Web ×6, Store ×6}
change:   head + Channel (Id int PK explicit · Code nvarchar(20) NOT NULL) · Order + ChannelId int NULL +
          FK_Order_Channel → Channel · Data/Seed.sql + guarded Channel MERGE (1 Web, 2 Store) · post-deploy backfill
          `UPDATE o SET ChannelId = c.Id FROM dbo.[Order] o JOIN dbo.Channel c ON c.Code = o.Channel WHERE o.ChannelId IS NULL`
expect:   answers    SeedDrift(Channel)=clean (answered from the delta: a new table)
          questions  Authored(Data/Seed.sql, the backfill) · SeedDrift(Channel); the FK's Orphan question folds into
                     Authored: ChannelId is new (NULL in every row) and the post-deploy fills both the child column and
                     the parent before validation runs (ARCH §4)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(an Order row per Channel shape, Web and Store) after `facsimile shape Dev
                                dbo.[Order].Channel --vocabulary` returns 2 values, each seen 6 times
                     outcome    verdict clean · exit 0
                     copy       0 unmapped distinct Channel · Order.ChannelId NULL = 0
                                · FK_Order_Channel is_not_trusted=0
                                · Order.Channel retained · the second publish: Channel MERGE 0 rows, hash identical
                     record     tier T3 · ships phase 1 of 3 (create, seed and backfill; the drop is later)
                                · effects {adds Channel, Order.ChannelId, FK_Order_Channel} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     P6 A5 H3 D6 T2
source:   receipt · SamplePr "extract-to-lookup (phase 1): the lookup is seeded idempotently, every distinct source
          value maps 1:1, and the FK is trusted"
note:     Each value is seen at least k = 5 times, so `--vocabulary` may return it (P6); with fewer, the values fold
          into "other" and the agent declares the seed's own Codes, which are repository-defined (P5).
```

### OP-extract-to-lookup-02 · violating:unmapped, and the backfill checks itself
```case
kind:     corpus
tags:     violating:unmapped
env:      as -01 but Channel {Web ×3, Store ×2, Phone ×1}
change:   as -01, the backfill opening with `IF EXISTS (SELECT 1 FROM dbo.[Order] o WHERE NOT EXISTS (SELECT 1 FROM
          dbo.Channel c WHERE c.Code = o.Channel)) RAISERROR(N'unmapped Channel', 16, 1);`
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order rows Web, Store, and 'wPhone', a value no seeded Code matches)
                     outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
                     record     tier T3 · residue {main: Channel, Order.ChannelId, FK_Order_Channel added WITH NOCHECK ·
                                post-deploy: the Channel seed applied, the backfill refused before writing ·
                                validation: not reached, FK_Order_Channel untrusted}
laws:     H5 H6 F1 P5
source:   expected · TWIN_SUCCESSION §6b (the script "THROWs if it finds a shape it does not handle"); F12 (an injected
          'Backordered' fires the non-total negative)
note:     ARCH §6: a remedy's own RAISERROR inside the post-deploy is `blocked authored` (H5). 'Phone' is seen once, so
          `shape` folds it into "other" (P6); the agent declares a generated 'wPhone' for it. The fix is to seed the
          value or reconcile it before the backfill (-09).
```

### OP-extract-to-lookup-03 · violating:unmapped, and a join backfill leaves a silent NULL
```case
kind:     corpus
tags:     violating:unmapped
env:      as -02
change:   as -01 (no self-check)
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as -02
                     outcome    verdict clean · exit 0
                     copy       1 Order with ChannelId NULL (the 'wPhone' row)
                     record     tier T3 · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                                drift-since-measure, integration-studio}
laws:     F3 F1 D6
source:   expected · skills/op/extract-to-lookup (named trap: "any source text with no seeded lookup row becomes NULL");
          F12 (the total-mapping query)
note:     The engine has nothing to refuse; that is why §6b asks scripts to check themselves. The value is lost for
          good at the phase-3 drop.
```

### OP-extract-to-lookup-04 · violating:case, a case twin under a case-sensitive column
```case
kind:     corpus
tags:     violating:case
env:      pg + Order.Channel nvarchar(20) COLLATE Latin1_General_CS_AS · Order 6 rows · Channel {Web ×3, Store ×2,
          web ×1}
change:   as -02 (self-checking backfill); Channel.Code declared with the same collation
expect:   answers    Collation(Order.Channel)=Latin1_General_CS_AS
          questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order rows Web, Store, and 'web', the case twin of a seeded Code) after `shape`
                                reports 3 distinct as collated and 3 binary
                     outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
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
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order rows 'Web ' and ' Web',
                                twins of a seeded Code) after `shape` reports more
                                distinct binary values than distinct as collated
                     outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
                     record     tier T3 · residue {main: Channel, Order.ChannelId, FK_Order_Channel added WITH NOCHECK ·
                                post-deploy: the Channel seed applied, the backfill UPDATE refused as one statement}
laws:     F1 H5 H6
source:   expected · ARCH §4 (`trailing` class); skills/op/extract-to-lookup ("becomes NULL, or blocks the FK")
note:     `=` pads trailing spaces, so 'Web ' maps to 1; ' Web' falls to ELSE 0, and the key, enabled since the main
          script added it WITH NOCHECK, refuses the UPDATE inside the post-deploy: `blocked authored` (H5), not
          `blocked constraint`. A join backfill would have left ' Web' NULL instead (-03).
```

### OP-extract-to-lookup-06 · violating:empty, '' and NULL in the source
```case
kind:     corpus
tags:     violating:empty
env:      pg + Order.Channel nvarchar(20) NULL · Order 6 rows · Channel {Web ×2, Store ×2, NULL ×1, '' ×1}
change:   as -02, the self-check excluding NULL (`WHERE o.Channel IS NOT NULL AND NOT EXISTS …`)
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order rows NULL and '', alongside Web and Store)
                     outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
                     copy       the NULL row would map to NULL legitimately; the '' row is the unmapped one
laws:     F1 P6 H5
source:   expected · skills/_index/idempotent-seed (NULL distinct from ''); TWIN_SUCCESSION §6b ("Email: one NULL, one
          '' … pass NOT NULL while still meaning blank")
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
expect:   answers    HasRows(Order)=no · SeedDrift(Channel)=clean (answered from the delta)
          questions  HasRows(Order) (the guard above DROP COLUMN Channel) · Authored(Data/Seed.sql, the backfill) ·
                     SeedDrift(Channel)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  `-- shapes: none (no Order row to map)`
                     outcome    verdict clean · exit 0
                     record     tier T3 · ships one release · effects {adds Channel, Order.ChannelId, FK_Order_Channel,
                                removes Order.Channel} · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                                drift-since-measure, integration-studio}
laws:     F1 M2 D6
source:   expected · skills/_index/multi-phase ("if the source is empty … the whole staged shape collapses")
note:     The collapse is Dev's; a populated QA needs the three-phase program, proven against QA's own answers.
```

### OP-extract-to-lookup-09 · remedy: the unmapped value given a home before the backfill
```case
kind:     corpus
tags:     remedy
env:      as -02 (Channel {Web ×3, Store ×2, Phone ×1})
change:   as -02, the developer's answer recorded in the seed: the Channel MERGE gains (3, N'Phone')
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order].Channel:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order rows Web, Store and Phone — 'Phone' now the seed's own Code)
                     outcome    verdict clean · exit 0
                     copy       0 unmapped · Order.ChannelId NULL = 0 · FK_Order_Channel is_not_trusted=0
                     record     tier T3 · ships phase 1 of 3 · silence ok · notChecked {unread:QA, unread:UAT,
                                unread:Prod, volume, drift-since-measure, integration-studio}
laws:     A5 F1 H3 D6
source:   expected · skills/op/extract-to-lookup ("a pre-backfill reconcile is required so nothing maps to NULL");
          F15 (a) (the seed, not the pre-deploy, carries the mapping decision)
note:     -02's self-check now passes because the seed answers it. 'Phone' is repository-defined once the seed carries it
          (P5). Folding the value into an existing Code instead is a reconcile in the same release, recorded in the
          seed the same way.
```

### OP-extract-to-lookup-10 · edge: the source table is tracked by CDC
```case
kind:     corpus
tags:     edge
env:      pg + Order.Channel nvarchar(20) NOT NULL · Order 6 rows · Cdc(Order)=yes
change:   as -01
expect:   answers    HasRows(Order)=yes · Cdc(Order)=yes
          outcome    stop cdc · "dbo.Order is tracked by CDC, and LocalDB has none. This change is principal-led."
                     · exit 9
          flags      {cdc}
laws:     H2 H3 A3 F3
source:   expected · ARCH §6 (`cdc`: the flag, and the stop on LocalDB) · red-team #6 (a silently stale capture
          instance ✓)
note:     On a CDC server phase 1 publishes clean while Order's capture instance silently omits ChannelId, and every
          consumer of the change feed keeps reading the free text. A designed stop (INVARIANTS F3 scope): the oracle's
          verdict is recorded for information only.
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
expect:   questions  Authored(the post-deploy copy); the FK and unique checks are on CustomerContact, which this delta
                     creates and the copy fills before validation runs, so they fold into Authored (ARCH §4)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.ContactPhone:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(presence(Customer)) after `shape Dev dbo.Customer.ContactPhone`
                     outcome    verdict clean · exit 0
                     copy       CustomerContact rows = Customer rows · lost 0 · duplicated 0 · FK trusted ·
                                hash(Customer: Id AS k, ContactPhone AS v) = hash(CustomerContact: CustomerId AS k,
                                Phone
                                AS v)
                     record     tier T3 · ships phase 1 of 3 · effects {adds CustomerContact} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     A5 H3 M2 D6 T2
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
                     · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
                     · silence ok
laws:     F1 M2 D6
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
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.ContactPhone:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(a Customer whose ContactPhone is a 21-character `w`-prefixed value) after `shape
                                Dev dbo.Customer.ContactPhone` reports lengths over 20
                     outcome    verdict blocked authored · Msg 2628 · post-deploy · exit 3
                     record     tier T3 · residue {main: CustomerContact created · post-deploy: the copy refused as one
                                statement}
laws:     F1 H5 H6 P6
source:   expected · ARCH §6 (any error inside an authored batch is `blocked authored`, here 2628, or 8152 below
          compatibility level 150); skills/_index/multi-phase
note:     `shape` returns lengths without values (P6), which is all this witness needs.
```

### OP-split-table-05 · violating:unicode, a varchar destination substitutes characters silently
```case
kind:     corpus
tags:     violating:unicode
env:      pg · Customer 5 rows · one ContactPhone holding a character outside the destination's code page
change:   as -01, but Phone varchar(40)
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.ContactPhone:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(a Customer whose ContactPhone is 'w電話')
                     outcome    verdict clean · exit 0
                     copy       the aliased hashes differ: the copy wrote '?' where the source held the character
                     record     tier T3 · silence ok
laws:     F3 F1 D6
source:   expected · skills/_index/multi-phase (the BEFORE/AFTER content hash licenses the drop)
note:     No message reports the loss; only the conservation hash sees it, before phase 3 makes it permanent. `shape`
          shows the character only as the class symbol `x` (P6).
```

### OP-split-table-06 · violating:empty, NULL and '' in the moving column
```case
kind:     corpus
tags:     violating:empty
env:      pg · Customer 6 rows · ContactPhone NULL ×1 · '' ×1
change:   as -01, but the copy filters `WHERE c.ContactPhone <> ''`
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.ContactPhone:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Customer rows with ContactPhone NULL and '')
                     outcome    verdict clean · exit 0
                     copy       CustomerContact 4 rows for 6 Customers: lost 2 · an unfiltered copy keeps both and the
                                aliased hashes match, NULL and '' kept apart
                     record     tier T3 · silence ok
laws:     F1 F3 D6
source:   expected · skills/_index/multi-phase; ARCH §3 (`sql/hash.sql`: NULL distinct from '')
```

### OP-split-table-07 · edge: create and drop in one release, proven per environment
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Customer 0 rows · QA: pg · Customer 5 rows
change:   head + CustomerContact − Customer.ContactPhone, with -01's post-deploy copy, all in one release · proved per
          environment after the authored stop: leg2 `prove Dev`, leg3 `prove QA`
expect:   questions  HasRows(Customer) · Authored(the copy)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.ContactPhone:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       answers    HasRows(Customer)=no
                     outcome    verdict blocked authored · Msg 207 · post-deploy · exit 3
                     record     tier T3 · residue {main: CustomerContact created, Customer.ContactPhone dropped ·
                                post-deploy: the copy failed on ContactPhone}
          leg3       answers    HasRows(Customer)=yes
                     outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
                     record     tier T3 · residue {}
laws:     F7 H5 H6
source:   expected · skills/op/split-table (named trap: "treating it as one PR"); F15 (a post-deploy naming a dropped
          column fails Msg 207 on a half-applied release)
note:     Two environments, two different blocks from one head: only per-environment proof shows both. In Dev the
          column drops, then the copy names it.
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.ContactPhone:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Customer) · `-- shapes: none (the script reads no value)`
                     outcome    verdict clean · exit 0
                     record     tier T3 · ships two releases, R2 promptly after R1 · R1 clean (ContactPhone gone) · R2
                                clean, empty plan · silence R1 not-silent(replay plan: ContactPhone re-added empty, the
                                F17 hazard) · R2 ok · flags {in-flight} between R1 and R2 · effects {removes
                                Customer.ContactPhone} · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                                drift-since-measure, integration-studio}
laws:     F8 A5 D6
source:   expected · FINDINGS Part 4 (delete-attribute row: R1 drops in pre-deploy with the model lagging, R2 catches
          up; "Re-publishing R1 re-adds the column"); F17 (the replay re-creates a dropped column on a green publish)
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(a Customer and its one CustomerAddress)
                     outcome    verdict clean · exit 0
                     copy       absorbed rows = distinct parents · hash(CustomerAddress: CustomerId AS k, Line1 AS a,
                                City
                                AS b, PostalCode AS c) = hash(Customer: Id AS k, … WHERE Line1 IS NOT NULL)
                     record     tier T3 · ships phase 1 of 3 · effects {modifies Customer (+3 columns)} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     A5 M2 D6 T2
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
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(two CustomerAddress rows on one Customer)
                     outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
                     record     tier T3 · residue {main: Customer.Line1, City, PostalCode added · post-deploy: the copy
                                refused before writing}
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
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as -02
                     outcome    verdict clean · exit 0
                     copy       5 survivors hold an address for 6 absorbed rows · a survivor hash matches the hash of
                                whichever 5 absorbed rows won: only the count catches the loss
                     record     tier T3 · silence ok
laws:     F3 M2 D6
source:   receipt · SamplePr "merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves
          every row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is
          the later phase" (the naive copy kept 25 of 26)
```

### OP-merge-tables-04 · edge: an absorbed row with no parent
```case
kind:     corpus
tags:     edge
env:      pg (no FK) · Customer 5 rows · CustomerAddress 5 rows: 4 on real Customers, 1 on CustomerId 99
change:   as -01
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(an address on a real Customer, and one on CustomerId 99)
                     outcome    verdict clean · exit 0 (5 rows, 5 distinct parents: the count passes)
                     copy       4 addresses reach Customer
                                · the absorbed and survivor hashes differ: the conservation hash
                                catches what the count misses
                     record     tier T3 · silence ok
laws:     F1 F3 D6
source:   expected · TWIN_SUCCESSION §5 (merge witnesses: "an absorbed row with no parent")
```

### OP-merge-tables-05 · survivors without an absorbed row, and NULL vs '' absorbed values
```case
kind:     corpus
tags:     violating:empty
env:      pg · Customer 6 rows · CustomerAddress 5 rows (Customer 6 has none) · City NULL ×1 · '' ×1
change:   as -01
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(a Customer with no address; addresses with City NULL and '')
                     outcome    verdict clean · exit 0
                     copy       Customer 6 keeps NULL address columns (1:0..1 is allowed) · the filtered survivor hash
                                equals the absorbed hash because NULL and '' stay apart on both sides
                     record     tier T3 · silence ok
laws:     F1 D6
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
                     OP-delete-entity-04 · effects {} · silence ok
laws:     F3 F6 T2 D6
source:   receipt · SamplePr "merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves
          every row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is
          the later phase" (removing CustomerAddress under the production publish is a phantom; the scripted DROP
          TABLE removes it)
```

### OP-merge-tables-07 · an empty absorbed table: one release
```case
kind:     corpus
tags:     empty
env:      pg · CustomerAddress 0 rows · options DropObjectsNotInSource=false
change:   Customer + the absorbing columns · Modules/CustomerAddress.sql removed · pre-deploy guarded
          DROP TABLE dbo.CustomerAddress
expect:   questions  Authored(Script.PreDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  `-- shapes: none (CustomerAddress is empty)`
                     outcome    verdict clean · exit 0
                     record     tier T3 · ships one release · effects {modifies Customer (+3 columns), removes
                                CustomerAddress} · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                                drift-since-measure, integration-studio}
laws:     A5 F7 D6
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.CustomerAddress:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(presence(CustomerAddress))
                     outcome    verdict clean · exit 0
                     copy       CustomerAddress absent · a second publish: plan empty
                     record     tier T3 · ships one release, scripted · effects {removes CustomerAddress} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     A5 H3 D6
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.AccountId, dbo.Customer.Region:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Customers on Accounts 1–3, and two with AccountId NULL)
                     outcome    verdict clean · exit 0
                     copy       hash(Customer: AccountId AS k, Region AS v WHERE AccountId IS NOT NULL) =
                                hash(Account: Id AS k, Region AS v WHERE Region IS NOT NULL) · 2 Customers' Regions have
                                no destination
                     record     tier T3 · ships phase 1 of 3 · effects {adds Account.Region} · silence ok
                                · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                                integration-studio}
laws:     A5 M2 D6 T2
source:   receipt · F12 (move-attribute, pg_move: 1:1 holds excluding NULLs, hash-equal aliased, "2 of 5 customers
          have a NULL AccountId so their Region strands"); SamplePr "move-attribute (phase 1): the join is proven 1:1,
          the destination column is backfilled with a matching digest, and the source-column drop is the guarded later
          phase"
note:     The stranded Regions are a fork for the developer, named on the record; the facsimile reproduces them and
          does not settle them.
```

### OP-move-attribute-02 · violating:cardinality, two Customers share an Account with different Regions
```case
kind:     corpus
tags:     violating:cardinality
env:      as -01 but AccountId {1, 1, 3, NULL, NULL}, the two on Account 1 holding West and East
change:   as -01
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.AccountId, dbo.Customer.Region:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(two Customers on Account 1 with different Regions)
                     outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
                     record     tier T3 · residue {main: Account.Region added · post-deploy: the copy refused before
                                writing}
laws:     H5 H6 M2
source:   expected · skills/op/move-attribute ("relationship not 1:1 → STOP: the value is ambiguous")
```

### OP-move-attribute-03 · edge: a 1:1 check that forgets NULL keys refuses clean data
```case
kind:     corpus
tags:     edge
env:      as -01
change:   as -01, but the check groups without `WHERE AccountId IS NOT NULL`
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.AccountId, dbo.Customer.Region:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(as -01, both NULL-key Customers included)
                     outcome    verdict blocked authored · Msg 50000 · post-deploy · exit 3
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
source:   receipt · SamplePr "move-attribute (phase 1): the join is proven 1:1, the destination column is backfilled
          with a matching digest, and the source-column drop is the guarded later phase"
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
expect:   questions  as -01
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.AccountId, dbo.Customer.Region:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(as -01, one Customer on an Account with a 30-character `w`-prefixed Region)
                     outcome    verdict blocked authored · Msg 2628 · post-deploy · exit 3
                     record     tier T3 · residue {main: Account.Region added · post-deploy: the copy refused as one
                                statement}
laws:     F1 H5 H6 P6
source:   expected · ARCH §6 (any error inside an authored batch is `blocked authored`: 2628, or 8152 below
          compatibility level 150)
note:     Which of the two numbers the copy raises follows the compatibility level, one of INVARIANTS F9's five metadata
          items; the verdict's reason does not.
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
          leg1       outcome    stop authored-script · "authored SQL reads dbo.Customer.Region:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  presence(Customer) · `-- shapes: none (the script reads no value)`
                     outcome    verdict clean · exit 0
                     record     tier T3 · ships two releases · R1 clean · R2 clean, empty plan · silence R1
                                not-silent(replay plan: Region re-added empty, the F17 hazard) · R2 ok · flags
                                {in-flight} between R1 and R2 · effects {removes Customer.Region}
                                · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure,
                                integration-studio}
laws:     F8 A5 D6
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
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio} · silence ok
laws:     F3 F4 D6
source:   expected · skills/op/archive-entity ("the archive table is added (additive, one declarative release)")
note:     With no row past the cutoff there is nothing to move, and this release is the whole change; an empty Order is
          -10.
```

### OP-archive-entity-02 · release 2: a batched move conserves every row, over more than one batch (Twin parity)
```case
kind:     corpus
tags:     clean
env:      pg + archive.OrderArchive · Order 12 rows, 8 with StatusId 3 · Status lane
change:   post-deploy: `WHILE 1 = 1 BEGIN DELETE TOP (5) o OUTPUT DELETED.* INTO archive.OrderArchive FROM dbo.[Order] o
          WHERE o.StatusId = 3; IF @@ROWCOUNT = 0 BREAK; END`
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(8 Order rows with StatusId 3 and 4 without) under `-- plants: Order`
                     outcome    verdict clean · exit 0
                     copy       live + archived = 12 · archived 8 · no Id in both · the archived hash equals the moved
                                rows' hash before · at least 2 batches committed
                     record     tier T3 · ships release 2 of 2 · silence ok · notChecked {unread:QA, unread:UAT,
                                unread:Prod, volume, drift-since-measure, integration-studio}
laws:     M2 A5 H4 D6 T2
source:   receipt · SamplePr "archive-entity: retiring rows to an archive table conserves every row (live + archived
          == original), byte-identical and batched"
note:     M2's minimum (one presence row) never reaches a second batch; the row count is a declared choice in
          witness.sql, counted apart from the generated rows. `volume` stays: log growth, timing and CommandTimeout
          belong to the real table. The replay moves nothing (0 rows: StatusId 3 is gone from the live table).
```

### OP-archive-entity-03 · edge: the cutoff, before, on and after
```case
kind:     corpus
tags:     edge
env:      pg + archive.OrderArchive · Order 6 rows, Id 1–6
change:   post-deploy batched move `WHERE o.Id <= 3`
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(Order Ids 2, 3 and 4: just before, on, and just after the cutoff)
                     outcome    verdict clean · exit 0
                     copy       Id 3 moves under `<=` and would stay under `<` · live + archived = 6 either way
                     record     tier T3 · the cutoff's comparison is the script's declaration · silence ok
laws:     F1 F3 D6
source:   expected · skills/op/archive-entity; skills/_index/multi-phase (source + archive == original)
note:     Conservation cannot tell `<` from `<=`; the boundary witness is how the record shows which one ran. A cutoff
          after every row empties the live table, and later changes then answer HasRows(Order)=no here
          (`drift-since-measure`).
```

### OP-archive-entity-04 · violating:referenced, archiving parents that children still reference
```case
kind:     corpus
tags:     violating:referenced
env:      pg + archive.OrderArchive + FK_OrderLine_Order · Order 6 rows, 2 with StatusId 3 · OrderLine 4 rows, 1 on a
          retired Order
change:   -02's batched move
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(a retired Order and an OrderLine on it)
                     outcome    verdict blocked authored · Msg 547 · post-deploy · exit 3
                     copy       live + archived still = the original: the failing DELETE … OUTPUT rolled back as one
                                statement
                     record     tier T3 · residue {post-deploy: the move's first batch refused as one statement}
laws:     H5 F1
source:   expected · skills/op/archive-entity (named trap: "child rows with FKs must move … before parents")
note:     The 547 is raised inside the post-deploy batch, so it is `blocked authored` (H5), not `blocked constraint`.
```

### OP-archive-entity-05 · violating:referenced, no FK, the move orphans children silently
```case
kind:     corpus
tags:     violating:referenced
env:      pg (no FK) + archive.OrderArchive · Order 6 rows, 2 with StatusId 3 · OrderLine 4 rows, 1 on a retired Order
change:   -02's batched move
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  as -04
                     outcome    verdict clean · exit 0
                     copy       the OrderLine's OrderId resolves to no live Order; its parent is in the archive
                     record     tier T3 · silence ok
laws:     F3 D6
source:   expected · skills/op/archive-entity; the no-FK face of OP-delete-seed-value-04
```

### OP-archive-entity-06 · edge: an archive table that copied the source's IDENTITY
```case
kind:     corpus
tags:     edge
env:      pg · Order 12 rows, 8 with StatusId 3
change:   as -01 and -02 in one release, but archive.OrderArchive.Id int IDENTITY(1,1)
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(as -02)
                     outcome    verdict blocked authored · Msg 544 · post-deploy · exit 3
                     record     tier T3 · residue {main: archive, archive.OrderArchive created · post-deploy: the move's
                                first batch refused}
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
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(as -02)
                     outcome    verdict blocked authored · Msg 332 · post-deploy · exit 3
                     record     tier T3 · residue {post-deploy: the move refused before any row moved}
laws:     H5
source:   expected · the documented OUTPUT INTO restriction (no FOREIGN KEY, enabled trigger or CHECK on the target,
          Msg 332); ARCH §6 (any error inside an authored batch is `blocked authored`)
```

### OP-archive-entity-08 · volume: an archive over a million rows raises the scale flag
```case
kind:     pure
tags:     edge
input:    delta: CREATE SCHEMA [archive]; CREATE TABLE [archive].[OrderArchive] (…); and a post-deploy changed
          against base: the batched DELETE … OUTPUT over [dbo].[Order]
          questions: Authored(Script.PostDeployment.sql) · measures Rows(Order)
          answers: HasRows(Order)=yes · Rows(Order)=3200000
action:   Get-FxTier, then Get-FxRecord
expect:   record     tier T3 · flags {scale} · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     drift-since-measure, integration-studio}
laws:     H4
source:   expected · ARCH §7 (`scale`: Rows ≥ 1,000,000 under a data-sensitive, Scan, Rebuild or authored-DML
          statement on that table); TWIN_SUCCESSION §6 (volume: timing, log size, CommandTimeout at >1M rows)
note:     The skill's ">1M rows: schedule a window" is the lead's line: the flag names the scrutiny, and `volume` names
          what the copy cannot time.
```

### OP-archive-entity-09 · remedy: children move before their parents
```case
kind:     corpus
tags:     remedy
env:      as -04, plus archive.OrderLineArchive (OrderLine's columns, no IDENTITY, no FK)
change:   post-deploy: first the batched move of OrderLines whose Order is retired into archive.OrderLineArchive, then
          -02's batched move of the retired Orders
expect:   questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order], dbo.OrderLine:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  declared(as -04)
                     outcome    verdict clean · exit 0
                     copy       live + archived = original for Order and for OrderLine · no live OrderLine on an
                                archived Order
                     record     tier T3 · ships release 2 of 2 · silence ok · notChecked {unread:QA, unread:UAT,
                                unread:Prod, volume, drift-since-measure, integration-studio}
laws:     A5 F1 D6
source:   expected · skills/op/archive-entity ("child rows with FKs must move (or their FKs be disabled) before parents")
note:     Conservation is per table; an archive with no foreign keys has given up enforcement for the archived rows,
          which the record names.
```

### OP-archive-entity-10 · empty: no Order in Dev, the move touches nothing
```case
kind:     corpus
tags:     empty
env:      pg + archive.OrderArchive · Order 0 rows
change:   -02's batched move
expect:   answers    HasRows(Order)=no
          questions  Authored(Script.PostDeployment.sql)
          leg1       outcome    stop authored-script · "authored SQL reads dbo.[Order]:
                                declare its shapes in witness.sql, or `-- shapes: none (<reason>)`" · exit 5
          leg2       witnesses  `-- shapes: none (Order is empty in Dev)`
                     outcome    verdict clean · exit 0
                     copy       live 0 + archived 0 = 0 · the first batch affects 0 rows and breaks
                     record     tier T3 · ships release 2 of 2 · silence ok · notChecked {unread:QA, unread:UAT,
                                unread:Prod, drift-since-measure}
laws:     F1 F3 D6
source:   expected · skills/op/archive-entity · skills/_index/multi-phase (source + archive == original holds at zero)
note:     Empty in Dev proves nothing of QA's rows: each populated environment needs its own answers (F7), and rows
          written before promotion are `drift-since-measure`.
```

# 40 · Column operations

*Family 40 (`OP-<slug>-nn`): the column operations (`90-traceability.md` lists them), each with its standard set (README §5), one `##` apiece.
Fixtures are relative to `pg`; the effective options are pg's Pipeline profile unless a case says otherwise.
`copy` records what the copy showed (`permissive:` for the `--permissive` arm); a stop outcome is a `process` case.*

## add-optional

### OP-add-optional-01 · empty table: the nullable add touches nothing
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer + MiddleName nvarchar(100) NULL · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {adds Customer.MiddleName} · notChecked
                     {unread:QA, unread:UAT, unread:Prod, integration-studio} · silence ok
laws:     F3 H2 H4 D6
source:   expected · skills/op/add-optional (any table state ships in place)
```

### OP-add-optional-02 · populated: every existing row takes NULL
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · Email NULL×2
change:   head Customer + MiddleName nvarchar(100) NULL · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       MiddleName NULL on the presence row · every other Customer column hash-equal before/after
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 M2 T2 D6
source:   receipt · SamplePr "add-optional: a new nullable column applies clean on a populated table"
note:     Presence is still planted for a populated touched table (M2), which is what lets the copy show that no stored
          value moved.
          n/a violating — the new column holds NULL in every existing row (answered from the delta); nothing can fail.
          n/a remedy — a nullable add has nothing to reconcile.
```

### OP-add-optional-03 · IgnoreColumnOrder off: a mid-table add becomes a rebuild
```case
kind:     corpus
tags:     edge
env:      pg · Customer 4 rows · Id 1, 2, 4, 9
change:   head Customer + MiddleName nvarchar(100) NULL between Name and Email · scripts unchanged · effective
          options: Pipeline profile with IgnoreColumnOrder=False
expect:   answers    HasRows(Customer)=yes · Gapped(Customer)=yes
          questions  Rebuild(Customer): HasRows(Customer) · Gapped(Customer)
          witnesses  presence(Customer) · gapped(Customer)
          outcome    verdict clean · exit 0
          copy       Customer Id set hash-equal before/after · IDENT_CURRENT(Customer) ≥ MAX(Id)
          record     tier T2 · ships one release (a table rebuild) · notChecked {unread:QA, unread:UAT, unread:Prod,
                     volume, integration-studio}
                     · silence ok
laws:     F6 F4 F1 D6
source:   expected · skills/op/add-optional (named edge: IgnoreColumnOrder=False turns a mid-table insert into a
          rebuild)
note:     The same edit is T0 under the captured options (-02) and T2 here: the tier comes from the delta the
          effective options produce, never from the operation's name.
```

### OP-add-optional-04 · CDC-tracked table: principal-led
```case
kind:     process
tags:     refuse
env:      pg · Customer 5 rows · Cdc(Customer)=yes
change:   head Customer + MiddleName nvarchar(100) NULL · scripts unchanged
setup:    SQL Server 2022 stand-in for Dev with CDC enabled on dbo.Customer · LocalDB for the copy
action:   facsimile read Dev · facsimile prove Dev
expect:   answers    HasRows(Customer)=yes · Cdc(Customer)=yes
          outcome    stop cdc · "dbo.Customer is tracked by CDC, and LocalDB has none. This change is
                     principal-led." · exit 9
          flags      {cdc}
laws:     H2 H3 A3
source:   expected · ARCH §6 (cdc: the flag, and the stop on LocalDB) · the premise is red-team #6 (a silently stale
          capture instance ✓)
note:     On a CDC server the add publishes clean while the capture instance silently omits MiddleName. The copy
          cannot show that, so the run stops rather than report clean.
```

## add-mandatory

### OP-add-mandatory-01 · empty table: NOT NULL with no default applies
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer + Segment nvarchar(20) NOT NULL, no default · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  HasRows(Customer)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T1 · ships one release, in place · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio} · silence ok
laws:     F3 H2 T2 D6
source:   receipt · SamplePr "add-mandatory: a new NOT NULL column with no default blocks a populated table, applies
          when empty"
```

### OP-add-mandatory-02 · populated, with a DEFAULT: every existing row is stamped
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows
change:   head Customer + Segment nvarchar(20) NOT NULL CONSTRAINT DF_Customer_Segment DEFAULT (N'Standard') ·
          scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none (Scan: the defaulted NOT NULL add backfills every row and can refuse none) · measures
                     Rows(Customer)
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       Segment = 'Standard' on the presence row · is_nullable 0
          record     tier T0 · ships one release, in place · note "the default stamps every existing row with
                     'Standard': confirm it is right for them" · silence ok · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, platform:edition, integration-studio}
laws:     F3 F4 H2 D6 T2
source:   receipt · SamplePr "add-default: a NOT NULL column with a default applies clean and backfills existing
          rows" · F14
note:     The default backfills here because the column is new. On an existing column it never does
          (OP-add-default-02).
```

### OP-add-mandatory-03 · the delta asks presence only
```case
kind:     pure
tags:     edge
input:    /* The column [dbo].[Customer].[Segment] … has no default value and does not allow NULL values. … */
          IF EXISTS (select top 1 1 from [dbo].[Customer])
              RAISERROR (N'Rows were detected. …', 16, 127) WITH NOWAIT;
          GO
          ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) NOT NULL;
action:   Get-FxQuestions, then Get-FxTier
expect:   questions  HasRows(Customer), from the guard batch · nothing from the ADD: a new column holds no value to
                     test
          record     tier T1
laws:     F4 H5
source:   expected · ARCH §4 (HasRows; NotNull matches ALTER COLUMN, never ADD) · the warning text as SamplePr
          "add-mandatory: a new NOT NULL column with no default blocks a populated table, applies when empty"
          asserts it
note:     The SQL72015 comment above the guard is neither a question nor an outcome.
```

### OP-add-mandatory-04 · populated, no default: the Optimistic NOT NULL block
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows
change:   head Customer + Segment nvarchar(20) NOT NULL, no default · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · residue {} · ships one release once the column carries a DEFAULT (-02), else the
                     three-release fallback (-06) · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure}
laws:     F1 F3 H5 T2
source:   receipt · SamplePr "add-mandatory: a new NOT NULL column with no default blocks a populated table, applies
          when empty"
note:     Unlike the tightening class, a DEFAULT cures it (-02).
          n/a violating — HasRows has no value class; a populated table is the only blocking state.
```

### OP-add-mandatory-05 · the fallback program without declared shapes: the authored-script stop
```case
kind:     process
tags:     remedy
env:      pg · Customer 5 rows · AccountId NULL×2
change:   program R0 → R1 → R2, because no DEFAULT is acceptable for new rows · R0: head Customer + Segment
          nvarchar(20) NULL · R1: model unchanged · pre-deploy sets Segment where NULL ('Retail' when AccountId is
          NULL, else 'Account'), then ALTER COLUMN Segment nvarchar(20) NOT NULL, both idempotent · R2: head Segment
          NOT NULL, pre-deploy removed · witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev --program R0 R1 R2
expect:   questions  from the R2 target: HasRows(Customer) · Authored(R1 pre-deploy)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Customer.AccountId. Run facsimile
                     shape Dev dbo.Customer.AccountId, declare a witness in witness.sql for every shape the script
                     handles, and make the script check its own preconditions." · exit 5
          record     tier T3
laws:     F8 H3 A3
source:   expected · skills/op/add-mandatory (fallback: add nullable, backfill, tighten with the model lagging) ·
          ARCH §6
note:     Segment does not exist in Dev yet, so the stop names only the column Dev has that the script reads. Asking
          to profile Segment would lead to missing-object (S3) instead of progress (A5).
```

### OP-add-mandatory-06 · the fallback program with declared shapes: R0, R1, R1 replayed, R2
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 5 rows · AccountId NULL×2
change:   as OP-add-mandatory-05 · witness.sql declares the two shapes `shape` showed for AccountId (NULL, set); the
          script THROWs before any write if Segment already holds a value outside {'Retail', 'Account'}
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer) · Authored(R1 pre-deploy)
          witnesses  presence(Customer) · declared(AccountId NULL) · declared(AccountId set)
          outcome    verdict clean · exit 0
          record     tier T3 · ships three releases · R0 clean · R1 clean · R2 clean, empty plan · silence R1
                     not-silent(replay plan: ALTER COLUMN [Segment] … NULL, the F17 hazard; hold other publishes) ·
                     R2 ok · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                     integration-studio}
laws:     F8 A5 H2 D6
source:   expected · skills/op/add-mandatory (fallback) · the R1/R2 legs are F7's two releases; the replay revert is
          F3's
```

### OP-add-mandatory-07 · GenerateSmartDefaults in the effective options
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows
change:   head Customer + Segment nvarchar(20) NOT NULL, no default · scripts unchanged · effective options:
          Pipeline profile with GenerateSmartDefaults=True
expect:   answers    HasRows(Customer)=yes
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       Segment = '' on the presence row
          record     tier T0 · note "GenerateSmartDefaults stamped '' on every existing row; no one chose that
                     value" · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio} · silence ok
laws:     F6 F3 D6
source:   verify · skills/op/add-mandatory says a smart default silently stamps a value (e.g. ''). Uncertain:
          whether DacFx then omits the guard, and what it stamps into nvarchar. The first run decides.
note:     The verdict follows the captured options, never a hand-written profile (F6). With GenerateSmartDefaults
          off, the same edit is -04's block.
```

### OP-add-mandatory-08 · more than a million rows: the defaulted add stamps every row
```case
kind:     pure
tags:     edge
input:    delta ALTER TABLE [dbo].[Customer] ADD [Segment] NVARCHAR (20) CONSTRAINT [DF_Customer_Segment]
          DEFAULT (N'Standard') NOT NULL; · questions none (Scan) · measures Rows(Customer) · answers
          HasRows(Customer)=yes · Rows(Customer)=2400000
action:   Get-FxTier
expect:   record     tier T0 · flags {scale}
laws:     H4 F4
source:   expected · ARCH §4 (Scan: an ADD … NOT NULL DEFAULT backfill) · ARCH §7 (scale: Rows ≥ 1,000,000 under a
          Scan) · skills/op/add-mandatory (>1M rows: the add may run long or block writes)
note:     Off Enterprise edition the defaulted add writes every row. The add asks no question, but it is a Scan, so
          its table's Rows is measured and the flag is raised.
```

### OP-add-mandatory-09 · Prod unread: both worlds, and the read to run before promoting
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Customer 5 rows (read) · Prod: pg · Customer 9 rows (read: none)
change:   head Customer + Segment nvarchar(20) NOT NULL, no default · scripts unchanged · proved with `prove Prod
          --assume Prod=worst`
expect:   questions  HasRows(Customer), from Dev's schema (the nearest read environment)
          witnesses  presence(Customer) in both worlds
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · flags {unread:Prod, assumed:Prod} · Prod line "both worlds block Msg 50000/127 at
                     dbo.Customer; before promoting, read HasRows(dbo.Customer) on Prod" · notChecked {assumed:Prod,
                     drift-since-measure}
laws:     H4 H7 F3
source:   expected · ARCH §8 (--assume) · TWIN_SUCCESSION §6b (a VIEW DEFINITION-only login reads every presence
          guard)
note:     HasRows needs only VIEW DEFINITION (sys.partitions), so for a T1 change one such Prod login would turn
          this assumption into a measured answer.
```

## make-mandatory

### OP-make-mandatory-01 · empty table: the tightening applies in place
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
expect:   answers    HasRows(Customer)=no · NotNull(Customer.Email)=clean
          questions  HasRows(Customer) · NotNull(Customer.Email)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio} · silence ok
laws:     F1 F3 H2 T2 D6
source:   receipt · SamplePr "make OrderLine.Note mandatory: production publish blocks a populated table, applies
          when empty"
note:     An empty table cannot be violating. The tier is still T2, because it comes from the delta, not the data
          (§7).
```

### OP-make-mandatory-02 · populated, zero NULLs, '' and 'N/A' present: still blocked
```case
kind:     corpus
tags:     clean
env:      pg · Customer 20 rows · Email NULL×0 · ''×1 · 'N/A'×5
change:   head Customer.Email NULL→NOT NULL · scripts unchanged
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=clean
          questions  HasRows(Customer) · NotNull(Customer.Email)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · residue {} · ships two releases (F4, F7) · note "'' and placeholder values pass NOT
                     NULL while meaning blank: run facsimile shape Dev dbo.Customer.Email before writing R1's
                     backfill" · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                     integration-studio}
laws:     F1 F3 H5 T2
source:   receipt · F7 · SamplePr "make OrderLine.Note mandatory: production publish blocks a populated table,
          applies when empty" (zero NULLs, still refused) · TWIN_SUCCESSION §6b ('' and an 'A/A' placeholder pass
          NOT NULL)
note:     The guard reads row presence: a clean NULL count is necessary for R1 and never sufficient for one release.
          The record line is fixed for every NotNull question, because no count tells a placeholder from a value.
```

### OP-make-mandatory-03 · populated, NULLs present
```case
kind:     corpus
tags:     violating:null
env:      pg · Customer 5 rows · Email NULL×2
change:   head Customer.Email NULL→NOT NULL · scripts unchanged · prove --permissive
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          questions  HasRows(Customer) · NotNull(Customer.Email)
          witnesses  presence(Customer) · null(Customer.Email)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          copy       permissive: Msg 515 · Customer.Email · the column stays nullable
          record     tier T2 · residue {} · ships two releases (F4, F7) · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F1 F3 F6 H5 M2
source:   receipt · F7 · SamplePr "make OrderLine.Note mandatory: production publish blocks a populated table,
          applies when empty" (its relaxed publish meets Msg 515 over NULL rows)
```

### OP-make-mandatory-04 · R1 → R2 without declared shapes: the authored-script stop
```case
kind:     process
tags:     remedy
env:      pg · Customer 20 rows · Email NULL×2 · ''×1 · 'N/A'×5
change:   program R1 → R2 · R1: model unchanged (Email NULL) · pre-deploy fills NULL Email, then ALTER COLUMN Email
          nvarchar(256) NOT NULL, both idempotent · the seed stops writing NULL Email · R2: head Email NOT NULL,
          pre-deploy removed · witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev --program R1 R2
expect:   questions  from the R2 target: HasRows(Customer) · NotNull(Customer.Email) · Authored(R1 pre-deploy, seed)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Customer.Email. Run facsimile
                     shape Dev dbo.Customer.Email, declare a witness in witness.sql for every shape the script
                     handles, and make the script check its own preconditions." · exit 5
          record     tier T3
laws:     F8 H3 A3
source:   expected · skills/op/make-mandatory (the R1/R2 shape, F7) · ARCH §6 (authored-script)
```

### OP-make-mandatory-05 · R1, R1 replayed, R2 with declared shapes
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 20 rows · Email NULL×2 · ''×1 · 'N/A'×5
change:   as OP-make-mandatory-04 · witness.sql declares what `shape` showed beyond NULL: the summary's empty=1 and
          the A/A signature ×5. The script fills NULL, '' and A/A alike, and THROWs before any write if it finds
          another whitespace-only or placeholder value
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          questions  HasRows(Customer) · NotNull(Customer.Email) · Authored(R1 pre-deploy, seed)
          witnesses  presence(Customer) · null(Customer.Email) · declared(Email '') · declared(Email 'N/A')
          outcome    verdict clean · exit 0
          record     tier T3 · ships two releases · R1 clean · R2 clean, empty plan · silence R1
                     not-silent(replay plan: ALTER COLUMN [Email] … NULL, the F17 hazard; hold other publishes until
                     R2) · R2 ok · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                     integration-studio}
laws:     F8 F1 A5 H2 D6
source:   receipt · F7 (mm_2r: R1 lands NOT NULL, R2 is a no-op) · F3 (an R1 replay reverts the column, narrow face)
          · F17 (the lag-window revert)
note:     R1's own model diff is empty, yet null(Customer.Email) is planted, because the R2 target asks NotNull
          (F8).
```

### OP-make-mandatory-06 · R1 with the seed still writing NULL
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows (the pg seed rows) · Email NULL×2 (Ids 3, 5)
change:   program R1 → R2 as OP-make-mandatory-04, but Data/Seed.sql still writes Email NULL for Ids 3 and 5 ·
          witness.sql declares the NULL shape
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          witnesses  presence(Customer) · null(Customer.Email)
          outcome    verdict blocked authored · Msg 515 · post-deploy · exit 3
          record     tier T3 · residue {pre-deploy: Email filled and NOT NULL · post-deploy: the seed's NULL write
                     failed} · note "the corrected seed is part of R1" · notChecked {unread:QA, unread:UAT, unread:Prod}
laws:     H5 H6 F3
source:   receipt · F6 (pre-deploy work survives a failed deploy) · FINDINGS Part 5 (pg_mm: the seed must ride with
          the change, Msg 515)
note:     A Msg 515 raised inside the post-deploy batch is the seed's, never the guard and never `blocked null`: any
          error inside an authored batch is `blocked authored` (H5).
```

### OP-make-mandatory-07 · backfill in the same release as the model change: blocked with zero NULLs left
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows · Email NULL×2
change:   head Customer.Email NULL→NOT NULL · pre-deploy fills NULL Email (no ALTER) · seed corrected · witness.sql
          declares the NULL shape
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          questions  HasRows(Customer) · NotNull(Customer.Email) · Authored(pre-deploy, seed)
          witnesses  presence(Customer) · null(Customer.Email)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T3 · residue {pre-deploy: Email NULL×2 filled} · ships two releases (F4, F7)
laws:     H5 H6 F3
source:   expected · F2 (the plan is computed before the pre-deploy runs) · F6 · F7 · skills/_index/tightening-class
          (records this disproven recipe: a pre-deploy cleared every NULL and Strict still blocked)
note:     The backfill leaves zero NULLs and the guard still fires, because it reads row presence.
```

### OP-make-mandatory-08 · Prod unread: both worlds
```case
kind:     corpus
tags:     edge
env:      Dev: pg · Customer 5 rows · Email NULL×2 (read) · Prod: pg · Customer 9 rows · Email NULL×1 (read: none)
change:   head Customer.Email NULL→NOT NULL · scripts unchanged · proved with `prove Prod --assume Prod=worst`
expect:   questions  HasRows(Customer) · NotNull(Customer.Email), from Dev's schema
          witnesses  clean world: presence(Customer) · violating world: presence(Customer) · null(Customer.Email)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · flags {unread:Prod, assumed:Prod} · Prod line "both worlds block Msg 50000/127 at
                     dbo.Customer; before R1 reaches Prod, count Email IS NULL there: R1's backfill must cover every
                     one" · notChecked {assumed:Prod, volume, drift-since-measure}
laws:     H4 H7 F3
source:   expected · ARCH §8 (--assume) · TWIN_SUCCESSION §3 step 7 (prove both worlds; record the probe to run)
```

### OP-make-mandatory-09 · an unrelated publish inside the R1 → R2 window
```case
kind:     process
tags:     edge
env:      pg · Customer 5 rows · Email already NOT NULL (R1 landed in Dev; R2 not merged)
change:   another branch adds Customer.MiddleName nvarchar(100) NULL; its model still declares Email NULL, so Dev's
          delta also carries ALTER COLUMN [Email] … NULL
setup:    estate/in-flight.md lists dbo.Customer (make-mandatory, R1 landed in Dev, window open) · Dev read
action:   facsimile prove Dev
expect:   outcome    stop in-flight · "dbo.Customer is in an open two-release window (estate/in-flight.md). Hold
                     this change, or make it the window's next release." · exit 5
          flags      {in-flight, lag:Dev}
laws:     H3 A3
source:   expected · ARCH §6 (in-flight) · the hazard it holds back is F17 (a lagging-model publish reverts on a
          green publish)
note:     Without the stop this publish is clean and quietly makes Email nullable again. The ALTER COLUMN [Email] … NULL
          is in delta.Dev.sql but not in delta.pr.sql, which is what `lag:Dev` records (Compare-FxDeltas).
```

## make-optional

### OP-make-optional-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · CustomerAddress 0 rows
change:   head CustomerAddress.Line1 NOT NULL→NULL · scripts unchanged
expect:   answers    HasRows(CustomerAddress)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/make-optional (a loosening is never refused)
```

### OP-make-optional-02 · populated: every value kept, the risk is downstream
```case
kind:     corpus
tags:     clean
env:      pg · CustomerAddress 5 rows · Line1 populated ×5
change:   head CustomerAddress.Line1 NOT NULL→NULL · scripts unchanged
expect:   answers    HasRows(CustomerAddress)=yes
          questions  none
          witnesses  presence(CustomerAddress)
          outcome    verdict clean · exit 0
          copy       Line1 hash-equal before/after · is_nullable 1
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio, external-consumers, app-behaviour}
                     · silence ok
laws:     F3 H2 H4 T2 D6
source:   receipt · SamplePr "make-optional: relaxing NOT NULL to NULL applies clean on a populated table"
note:     The risk is a consumer that assumed Line1 is never NULL: the Integration Studio refresh, SSIS and other
          readers, and the application's own paths, each a named residual (H4).
          n/a violating — a loosening reads no value that could fail.
          n/a remedy — a loosening has nothing to reconcile.
```

### OP-make-optional-03 · `NULL` is not `NOT NULL`, and an unchanged type asks no Fits
```case
kind:     pure
tags:     edge
input:    ALTER TABLE [dbo].[CustomerAddress] ALTER COLUMN [Line1] NVARCHAR (120) NULL;
action:   Get-FxQuestions, then Get-FxTier
expect:   questions  none (NotNull matches only an ALTER COLUMN ending NOT NULL; no data-loss comment names Line1)
          record     tier T0
laws:     F4
source:   expected · ARCH §4 · the delta is the single statement skills/op/make-optional records
note:     Shares the discriminator OP-widen-03 verifies: the data-loss comment is the only place the delta names a
          column's old type.
```

### OP-make-optional-04 · QA already nullable: its own, empty delta
```case
kind:     corpus
tags:     edge
env:      QA stand-in: pg · CustomerAddress 5 rows · Line1 already NULL-able (a hotfix only QA received)
change:   head CustomerAddress.Line1 NOT NULL→NULL · scripts unchanged
expect:   answers    HasRows(CustomerAddress)=yes
          questions  none (QA's delta has no statement for Line1)
          witnesses  none (QA's delta touches no table)
          outcome    verdict clean · exit 0
          record     tier T0 · note "QA's delta is empty for this change" · notChecked {unread:Dev, unread:UAT,
                     unread:Prod}
                     · silence ok
laws:     F7 F5 D6
source:   expected · red-team #1 (proving against main misses the delta each environment receives; drift)
note:     QA's delta hash differs from Dev's, so QA gets its own proof (F7). Dev's verdict is never copied across.
```

## widen

### OP-widen-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer.Email nvarchar(256)→nvarchar(400) · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/widen
```

### OP-widen-02 · populated: every value preserved
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · Email NULL×2 · longest Email 24 characters
change:   head Customer.Email nvarchar(256)→nvarchar(400) · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       Email hash-equal before/after · max_length 800
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 M2 T2 D6
source:   receipt · SamplePr "widen: enlarging a column applies clean and preserves every value"
note:     n/a violating — a widening cannot misfit; it reads no value that could fail.
          n/a remedy — a widening has nothing to reconcile.
```

### OP-widen-03 · a widening raises no Fits question
```case
kind:     pure
tags:     edge
input:    ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (400) NULL;
action:   Get-FxQuestions, then Get-FxTier
expect:   questions  none (no guard and no data-loss comment name Email)
          record     tier T0
laws:     F4
source:   verify · uncertain: whether DacFx 162.x and 170.x name every lossy ALTER COLUMN in the data-loss comment
          above the guard, and never a widening. The delta carries a column's old type nowhere else, so this is the
          only way Get-FxQuestions can tell a widening from a narrowing. The corpus deltas decide (ARCH §11.3).
```

### OP-widen-04 · the widened column sits in an index: warning 1945 now, Msg 1946 later
```case
kind:     corpus
tags:     edge
env:      pg + IX_Customer_Email (nonclustered on Customer(Email)) · Customer 5 rows · longest Email 24 characters
change:   head Customer.Email nvarchar(256)→nvarchar(1000) (2000 bytes, past the 1700-byte key limit) · scripts
          unchanged
expect:   answers    HasRows(Customer)=yes · KeyLength(Customer, IX_Customer_Email)=clean
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          record     warnings {1945: IX_Customer_Email's key can exceed 1700 bytes} · note "an insert or update with
                     an Email over 850 characters will fail Msg 1946" · notChecked {unread:QA, unread:UAT,
                     unread:Prod, app-behaviour}
                     · silence ok
laws:     F3 H2 H5 D6
source:   verify · red-team #16 records 1945 warning now and 1946 failing later (✓). Uncertain: whether DacFx widens
          the indexed column in place (SQL Server allows a same-type widening under an index) or drops and
          re-creates IX_Customer_Email, which adds a KeyLength question and makes the tier T2. Either way the
          warning reaches the record's `warnings`, never the verdict (ARCH §6).
```

### OP-widen-05 · more than a million rows: Rows is read even when nothing is asked
```case
kind:     pure
tags:     edge
input:    delta ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR (400) NULL; · questions none · measures
          Rows(Customer) · answers HasRows(Customer)=yes · Rows(Customer)=1300000
action:   Get-FxTier
expect:   record     tier T0 · flags {scale}
laws:     H4 F4
source:   verify · ARCH §7 raises scale only under a data-sensitive, Scan, Rebuild or authored-DML statement, and ARCH
          §4's Scan row does not list a widening ALTER COLUMN, while QST-19 measures it. Uncertain: whether
          Get-FxQuestions classes a widening as Scan (it may rewrite every row: skills/op/widen says an older server
          may rebuild rather than alter metadata) or as non-reading (then no flag). The vocabulary decision settles it.
note:     The tier is T0 either way: the tier comes from the questions, the flag from the statement class and Rows.
```

### OP-widen-06 · QA was already wider: there, this widen is a narrowing
```case
kind:     corpus
tags:     edge
env:      QA stand-in: pg · Customer 5 rows · Email nvarchar(500) (a hotfix only QA received) · longest Email 24
          chars
change:   head Customer.Email nvarchar(256)→nvarchar(400) · scripts unchanged
expect:   answers    HasRows(Customer)=yes · Fits(Customer.Email→nvarchar(400))={}
          questions  HasRows(Customer) · Fits(Customer.Email→nvarchar(400))
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · residue {} · flags {lag:QA} · lag:QA {the guarded ALTER COLUMN [Email] NVARCHAR (400):
                     a narrowing of QA's 500, where delta.pr widens 256} · notChecked {unread:Dev, unread:UAT,
                     unread:Prod, drift-since-measure}
laws:     F7 F5 F3 H5
source:   expected · red-team #1 (each environment receives its own delta; drift) · F1 (a populated narrowing
          blocks)
note:     Dev proves clean (OP-widen-02) and QA blocks: one verdict per environment, never one for all (F7).
```

## narrow

### OP-narrow-01 · empty table: the narrowing applies in place
```case
kind:     corpus
tags:     empty
env:      pg · Product 0 rows
change:   head Product.Code nvarchar(50)→nvarchar(10) · scripts unchanged
expect:   answers    HasRows(Product)=no · Fits(Product.Code→nvarchar(10))={}
          questions  HasRows(Product) · Fits(Product.Code→nvarchar(10))
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio} · silence ok
laws:     F1 F3 H2 T2 D6
source:   receipt · SamplePr "narrow: over-length data and the row-presence guard block a populated column, applies
          when empty"
```

### OP-narrow-02 · populated, every value fits: still blocked
```case
kind:     corpus
tags:     clean
env:      pg · Product 5 rows · every Code 10 characters or fewer, no trailing spaces
change:   head Product.Code nvarchar(50)→nvarchar(10) · scripts unchanged
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={}
          questions  HasRows(Product) · Fits(Product.Code→nvarchar(10))
          witnesses  presence(Product)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     tier T2 · residue {} · ships two releases (F4) · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F1 F3 H5
source:   receipt · F1 (the guard is table-has-rows, not does-the-data-fit) · FINDINGS Part 5 (a populated
          narrow-that-fits, NVARCHAR(50)→NVARCHAR(16), still blocked)
```

### OP-narrow-03 · violating: a value longer than the new width
```case
kind:     corpus
tags:     violating:overlen
env:      pg · Product 5 rows · Code 'STANDARD-SKU-001' (16 characters)×1
change:   head Product.Code nvarchar(50)→nvarchar(10) · scripts unchanged · prove --permissive
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={overlen}
          questions  HasRows(Product) · Fits(Product.Code→nvarchar(10))
          witnesses  presence(Product) · misfit(Product.Code, overlen)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          copy       permissive: Msg 2628 · Product.Code · the column stays nvarchar(50)
          record     tier T2 · residue {} · ships two releases (F4); R1 must decide what the over-length codes
                     become · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                     integration-studio}
laws:     F1 F3 F6 H5 T2
source:   receipt · F1 · SamplePr "narrow: over-length data and the row-presence guard block a populated column,
          applies when empty" (its relaxed publish meets Msg 2628)
```

### OP-narrow-04 · violating: over the width by trailing spaces only
```case
kind:     corpus
tags:     violating:overbytes
env:      pg · Product 5 rows · Code 'ABCDEFGHIJ' + 3 trailing spaces (LEN 10, 13 characters stored)×1
change:   head Product.Code nvarchar(50)→nvarchar(10) · scripts unchanged · prove --permissive
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={overbytes}
          questions  HasRows(Product) · Fits(Product.Code→nvarchar(10))
          witnesses  presence(Product) · misfit(Product.Code, overbytes): a `w`-prefixed 10-character value with
                     trailing spaces
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          copy       permissive: published with no error · Product.Code hash differs before/after (the spaces
                     trimmed)
          record     tier T2 · residue {} · ships two releases (F4); R1's ALTER trims these silently, so R1 must say
                     whether that is wanted · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     drift-since-measure, integration-studio}
laws:     F1 F3 F6 P5
source:   receipt · red-team #10 (a narrowing silently trims trailing spaces ✓)
note:     LEN ignores trailing spaces, so an overlen count reads 0 here. Only the overbytes class and the permissive
          hash show the loss.
```

### OP-narrow-05 · violating: a value over the new precision
```case
kind:     corpus
tags:     violating:overflow
env:      pg · Order 4 rows · Total 1234567.89×1
change:   head Order.Total decimal(18,2)→decimal(8,2) · scripts unchanged · prove --permissive
expect:   answers    HasRows(Order)=yes · Fits(Order.Total→decimal(8,2))={overflow}
          questions  HasRows(Order) · Fits(Order.Total→decimal(8,2))
          witnesses  presence(Order) · misfit(Order.Total, overflow)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Order · exit 3
          copy       permissive: Msg 8115 · Order.Total · the column stays decimal(18,2)
          record     tier T2 · residue {} · ships two releases (F4) · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F1 F3 F6 H5
source:   expected · skills/op/narrow ("reduce the precision") · ARCH §6 (overflow, 8115)
```

### OP-narrow-06 · violating: a scale cut that rounds silently
```case
kind:     corpus
tags:     violating:roundtrip
env:      pg · OrderLine 8 rows · Amount 12.25×1
change:   head OrderLine.Amount decimal(18,2)→decimal(18,1) · scripts unchanged · prove --permissive
expect:   answers    HasRows(OrderLine)=yes · Fits(OrderLine.Amount→decimal(18,1))={roundtrip}
          questions  HasRows(OrderLine) · Fits(OrderLine.Amount→decimal(18,1))
          witnesses  presence(OrderLine) · misfit(OrderLine.Amount, roundtrip)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          copy       permissive: published with no error · Amount hash differs before/after (12.25 became 12.3)
          record     tier T2 · residue {} · ships two releases (F4); R1 must state the rounding rule · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F1 F3 F6
source:   expected · skills/op/narrow · ARCH §4 (roundtrip: lossy but convertible)
note:     Nothing fails, so no message reports it; only the roundtrip count and the permissive hash do.
          nonconvertible and empty cannot occur in a same-type narrowing.
```

### OP-narrow-07 · R1 → R2 without declared shapes: the authored-script stop
```case
kind:     process
tags:     remedy
env:      pg · Product 5 rows · Code 'STANDARD-SKU-001'×1
change:   program R1 → R2 · R1: model unchanged (Code nvarchar(50)) · pre-deploy shortens Code where LEN(Code) > 10,
          then ALTER COLUMN Code nvarchar(10) NOT NULL, both idempotent · the seed reconciled · R2: head Code
          nvarchar(10), pre-deploy removed · witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev --program R1 R2
expect:   questions  from the R2 target: HasRows(Product) · Fits(Product.Code→nvarchar(10)) · Authored(R1
                     pre-deploy, seed)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Product.Code. Run facsimile shape
                     Dev dbo.Product.Code, declare a witness in witness.sql for every shape the script handles, and
                     make the script check its own preconditions." · exit 5
          record     tier T3
laws:     F8 H3 A3
source:   expected · skills/op/narrow (R1/R2) · ARCH §6 (authored-script)
```

### OP-narrow-08 · R1, R1 replayed, R2 with declared shapes
```case
kind:     corpus
tags:     remedy
env:      pg · Product 5 rows · Code 'STANDARD-SKU-001'×1
change:   as OP-narrow-07 · witness.sql declares what `shape` showed: max_len 16, edge_spaces 0 (the script cuts to
          10 characters)
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={overlen}
          questions  HasRows(Product) · Fits(Product.Code→nvarchar(10)) · Authored(R1 pre-deploy, seed)
          witnesses  presence(Product) · misfit(Product.Code, overlen) · declared(Code over 10 characters)
          outcome    verdict clean · exit 0
          record     tier T3 · ships two releases · R1 clean · R2 clean, empty plan · silence R1
                     not-silent(replay plan: ALTER COLUMN [Code] NVARCHAR (50) NOT NULL, the F17 hazard) · R2 ok ·
                     notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F8 F1 A5 H2 D6
source:   receipt · F4 (prove_2rel: R1 narrows with the model lagging, R2 is a no-op, stable) · F3 (prove_nvC: a
          publish of R1's model widened the column back)
note:     The replay is F3's drift seen through the program: R1's pre-deploy lands, and any publish still carrying
          R1's model reverts it.
```

### OP-narrow-09 · the model narrows and the pre-deploy narrows in one release
```case
kind:     corpus
tags:     edge
env:      pg · Product 5 rows · Code 'STANDARD-SKU-001'×1
change:   head Product.Code nvarchar(50)→nvarchar(10) · pre-deploy shortens over-length codes and runs ALTER COLUMN
          Code nvarchar(10) NOT NULL in the same release · seed reconciled · witness.sql declares the over-length
          shape
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→nvarchar(10))={overlen}
          questions  HasRows(Product) · Fits(Product.Code→nvarchar(10)) · Authored(pre-deploy, seed)
          witnesses  presence(Product) · misfit(Product.Code, overlen) · declared(Code over 10 characters)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     tier T3 · residue {pre-deploy: Code shortened, the column already nvarchar(10)} · note "the
                     model change and a pre-deploy ALTER never share a release"
laws:     H5 H6 F3
source:   receipt · F2 (prove_nvB: refused, and the column was already 10 afterward) · F6
note:     Worse than no change: a failed deploy with a half-applied schema, read back by scripting head against the
          copy.
```

### OP-narrow-10 · R1 with the seed still writing the long code
```case
kind:     corpus
tags:     edge
env:      pg · Product 5 rows (the pg seed rows) · Code 'STANDARD-SKU-001'×1 (Id 3)
change:   program R1 → R2 as OP-narrow-08, but Data/Seed.sql still writes 'STANDARD-SKU-001' for Id 3
expect:   witnesses  presence(Product) · misfit(Product.Code, overlen) · declared(Code over 10 characters)
          outcome    verdict blocked authored · Msg 2628 · post-deploy · exit 3
          record     tier T3 · residue {pre-deploy: Code shortened, the column nvarchar(10) · post-deploy: the seed's
                     write failed} · note "the seed that feeds the column is part of R1" · notChecked {unread:QA,
                     unread:UAT, unread:Prod}
laws:     H5 H6 F3
source:   receipt · sample-prs/narrow.md, What proving showed (the post-deploy seed still writing the 16-character
          `STANDARD-SKU-001` failed Msg 2628; the pre-deploy's narrowing had already committed)
note:     A 2628 raised inside the post-deploy batch is the seed's: `blocked authored`, never `blocked truncation` (H5).
```

## retype-implicit

### OP-retype-implicit-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · OrderLine 0 rows
change:   head OrderLine.LineNumber int→bigint · scripts unchanged
expect:   answers    HasRows(OrderLine)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/retype-implicit
```

### OP-retype-implicit-02 · populated: every value preserved
```case
kind:     corpus
tags:     clean
env:      pg · OrderLine 8 rows · MAX(LineNumber) 3
change:   head OrderLine.LineNumber int→bigint · scripts unchanged
expect:   answers    HasRows(OrderLine)=yes
          questions  none
          witnesses  presence(OrderLine)
          outcome    verdict clean · exit 0
          copy       LineNumber hash-equal before/after · type bigint
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 T2 D6
source:   receipt · SamplePr "retype-implicit: a widening type change applies clean and preserves every value" ·
          SamplePr "retype-explicit: a lossy narrowing type change blocks a populated column, a widening retype
          applies" (its widening leg)
note:     n/a violating — every int fits bigint; no value can fail the widening.
          n/a remedy — an implicit widening has nothing to reconcile.
```

### OP-retype-implicit-03 · filed as implicit, but the delta narrows
```case
kind:     pure
tags:     edge
input:    /* The type for column LineNumber in table [dbo].[OrderLine] is currently INT NOT NULL
          but is being changed to SMALLINT NOT NULL. Data loss could occur … */
          IF EXISTS (select top 1 1 from [dbo].[OrderLine])
              RAISERROR (N'Rows were detected. …', 16, 127) WITH NOWAIT;
          GO
          ALTER TABLE [dbo].[OrderLine] ALTER COLUMN [LineNumber] SMALLINT NOT NULL;
action:   Get-FxQuestions, then Get-FxTier
expect:   questions  HasRows(OrderLine) · Fits(OrderLine.LineNumber→smallint)
          record     tier T2
laws:     F4
source:   expected · ARCH §7 (the tier comes from the delta, never from the operation's name) ·
          skills/op/retype-implicit (a narrowing direction is the wrong op) · the comment's form as F12 quotes it
          for Order.Total
```

### OP-retype-implicit-04 · varchar → nvarchar drops a case-sensitive collation under a unique index
```case
kind:     corpus
tags:     edge
env:      pg, with Product.Code varchar(50) COLLATE Latin1_General_CS_AS NOT NULL and UX_Product_Code unique on
          Product(Code) · Product 3 rows · Code 'ab1', 'AB1', 'C3'
change:   head Product.Code nvarchar(50) NOT NULL (the database default collation, case-insensitive) · scripts
          unchanged · effective options: Pipeline profile with IncludeTransactionalScripts=False
expect:   answers    HasRows(Product)=yes · Unique(Product,(Code))={case}, counted under head's collation
          questions  Unique(Product,(Code))
          witnesses  presence(Product) · dup(Product,(Code), case)
          outcome    verdict blocked unique · Msg 1505 · UX_Product_Code · exit 3
          record     tier T2 · residue {main: UX_Product_Code dropped, Product.Code altered to nvarchar(50)} ·
                     notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure}
laws:     F9 F1 F3 H6
source:   expected · red-team #3 (a collation change on a unique column is data-reading DDL with no guard ✓) ·
          red-team #4 (a blocked unique build leaves the index dropped and the column altered ✓)
note:     The copy must carry the environment's column collation (F9), and the duplicate count must use the
          collation the index is rebuilt under, not the one the column has today.
```

### OP-retype-implicit-05 · widening a primary-key IDENTITY column
```case
kind:     corpus
tags:     edge
env:      pg · OrderLine 8 rows · Id 1–8
change:   head OrderLine.Id int IDENTITY→bigint IDENTITY · scripts unchanged
expect:   answers    HasRows(OrderLine)=yes
          witnesses  presence(OrderLine)
          outcome    verdict clean · exit 0
          copy       OrderLine Id set hash-equal before/after · type bigint · is_identity 1
          record     tier T2 · ships one release · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     integration-studio}
                     · silence ok
laws:     F4 F3 H2 D6
source:   verify · uncertain: which shape DacFx emits for a type change on a clustered primary-key IDENTITY column:
          a drop and re-add of PK_OrderLine_Id (which asks Unique) or a tmp_ms_xx rebuild (which asks Rebuild).
          Either way the delta asks a question, so the tier is T2, not the T0 the operation's name suggests.
```

## retype-explicit

### OP-retype-explicit-01 · empty table: the bare retype applies
```case
kind:     corpus
tags:     empty
env:      pg · Product 0 rows
change:   head Product.Code nvarchar(50)→int · scripts unchanged
expect:   answers    HasRows(Product)=no · Fits(Product.Code→int)={}
          questions  HasRows(Product) · Fits(Product.Code→int)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T2 · ships one release, in place · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio} · silence ok
laws:     F1 F3 H2 D6
source:   expected · skills/_index/tightening-class (the empty table is the only one-release leg)
```

### OP-retype-explicit-02 · populated, every value converts exactly: still blocked
```case
kind:     corpus
tags:     clean
env:      pg + Product.Weight nvarchar(20) NULL · Product 5 rows · Weight '12'×2, '40'×2, NULL×1
change:   head Product.Weight nvarchar(20)→int NULL · scripts unchanged
expect:   answers    HasRows(Product)=yes · Fits(Product.Weight→int)={}
          questions  HasRows(Product) · Fits(Product.Weight→int)
          witnesses  presence(Product)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     tier T2 · residue {} · ships multi-phase (add the new column · convert · drop the old in two
                     releases) · notChecked {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure,
                     integration-studio}
laws:     F1 F3 H5
source:   expected · skills/op/retype-explicit · skills/_index/tightening-class (the guard is data-blind)
```

### OP-retype-explicit-03 · violating: values that do not convert
```case
kind:     corpus
tags:     violating:nonconvertible
env:      pg · Product 5 rows · Code 'A100', 'B200', 'STANDARD-SKU-001', 'DUPE'×2
change:   head Product.Code nvarchar(50)→int · scripts unchanged · prove --permissive
expect:   answers    HasRows(Product)=yes · Fits(Product.Code→int)={nonconvertible}
          questions  HasRows(Product) · Fits(Product.Code→int)
          witnesses  presence(Product) · misfit(Product.Code, nonconvertible)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          copy       permissive: Msg 245 · Product.Code · the column stays nvarchar(50)
          record     tier T2 · residue {} · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure}
laws:     F1 F3 F6 H5
source:   expected · F12 (pg_retype: TRY_CONVERT(INT, Product.Code) is NULL for all 5; pg_retype2: a bare populated
          retype is refused, SQL72015 + Msg 50000) · ARCH §6 (conversion, 245)
```

### OP-retype-explicit-04 · violating: values that convert but lose their cents
```case
kind:     corpus
tags:     violating:roundtrip
env:      pg · Order 4 rows · Total 120.00, 540.50, 75.25, 300.00
change:   head Order.Total decimal(18,2)→int · scripts unchanged · prove --permissive
expect:   answers    HasRows(Order)=yes · Fits(Order.Total→int)={roundtrip}
          questions  HasRows(Order) · Fits(Order.Total→int)
          witnesses  presence(Order) · misfit(Order.Total, roundtrip)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Order · exit 3
          copy       permissive: published with no error · Total hash differs before/after (the cents gone)
          record     tier T2 · residue {} · ships multi-phase; the rounding rule is the data owner's · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F1 F3 F6
source:   expected · F12 (pg_retype2: SQL72015 + Msg 50000; every total converts and 2 of 4 lose their cents)
note:     TRY_CONVERT succeeds on 540.50, so a nonconvertible count reads 0. Only the round-trip comparison and the
          permissive hash see the loss.
```

### OP-retype-explicit-05 · violating: '' to a number becomes 0
```case
kind:     corpus
tags:     violating:empty
env:      pg + Product.Weight nvarchar(20) NULL · Product 5 rows · Weight '12'×3, ''×1, NULL×1
change:   head Product.Weight nvarchar(20)→int NULL · scripts unchanged · prove --permissive
expect:   answers    HasRows(Product)=yes · Fits(Product.Weight→int)={empty}
          questions  HasRows(Product) · Fits(Product.Weight→int)
          witnesses  presence(Product) · misfit(Product.Weight, empty)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          copy       permissive: published with no error · the '' row reads 0
          record     tier T2 · residue {} · ships multi-phase; the conversion must say what '' becomes · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, drift-since-measure, integration-studio}
laws:     F1 F3 F6
source:   expected · red-team #10 (TRY_CONVERT('') gives 0 for int and 1900-01-01 for date, NULL for decimal and
          uniqueidentifier ✓)
note:     TRY_CONVERT(int, '') is 0, not NULL, so the nonconvertible count misses it. Into date '' becomes
          1900-01-01 just as silently; into decimal or uniqueidentifier it counts as nonconvertible and the
          permissive arm meets Msg 8114 or Msg 8169, both `conversion` in ARCH §6.
```

### OP-retype-explicit-06 · violating: a value over the target range
```case
kind:     corpus
tags:     violating:overflow
env:      pg · OrderLine 8 rows · LineNumber 1000×1
change:   head OrderLine.LineNumber int→tinyint · scripts unchanged · prove --permissive
expect:   answers    HasRows(OrderLine)=yes · Fits(OrderLine.LineNumber→tinyint)={overflow}
          questions  HasRows(OrderLine) · Fits(OrderLine.LineNumber→tinyint)
          witnesses  presence(OrderLine) · misfit(OrderLine.LineNumber, overflow)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.OrderLine · exit 3
          copy       permissive: Msg 220 · OrderLine.LineNumber · the column stays int
          record     tier T2 · residue {} · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     drift-since-measure, integration-studio}
laws:     F1 F3 F6 H5 T2
source:   receipt · SamplePr "retype-explicit: a lossy narrowing type change blocks a populated column, a widening
          retype applies" (Strict: Msg 50000; its relaxed publish: Msg 220)
note:     The receipt's number is 220, which ARCH §6 maps to overflow alongside 8115 (and 248). The widening leg of the
          same fact is OP-retype-implicit-02.
```

### OP-retype-explicit-07 · the conversion release without declared shapes: the authored-script stop
```case
kind:     process
tags:     remedy
env:      pg + Customer.BirthText nvarchar(10) NULL + Customer.BirthDate date NULL (release A landed) · Customer 15
          rows · BirthText 99/99/9999-shaped ×10 (3 with a first part over 12) · 9999-99-99-shaped ×5
change:   release B: model unchanged · post-deploy converts BirthText into BirthDate where BirthDate IS NULL (ISO
          dates with style 23, slash dates with style 103) · witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev
expect:   questions  Authored(post-deploy over Customer.BirthText, Customer.BirthDate)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Customer.BirthText,
                     dbo.Customer.BirthDate. Run facsimile shape Dev <column> for each, declare a witness in
                     witness.sql for every shape the script handles, and make the script check its own
                     preconditions." · exit 5
          record     tier T3
laws:     H3 A3
source:   expected · skills/op/retype-explicit · TWIN_SUCCESSION §6b (an explicit retype always needs discovery)
```

### OP-retype-explicit-08 · the conversion release with declared shapes
```case
kind:     corpus
tags:     remedy
env:      as OP-retype-explicit-07
change:   as OP-retype-explicit-07 · the data owner says slash dates are day-first · witness.sql declares what
          `shape` and a first-part-over-12 count showed: a slash date with a first part over 12, an ambiguous slash
          date (both parts 12 or less), an ISO date. The script THROWs before any write if a non-NULL BirthText
          matches neither signature
expect:   answers    HasRows(Customer)=yes
          questions  Authored(post-deploy)
          witnesses  presence(Customer) · declared(slash, first part > 12) · declared(slash, ambiguous) ·
                     declared(ISO)
          outcome    verdict clean · exit 0
          copy       the ambiguous witness lands day-first, as declared · the ISO witness converts exactly
          record     tier T3 · ships multi-phase: A adds BirthDate · B converts · C drops BirthText in two releases
                     (OP-delete-attribute-05) · Prod line "the script's precondition check, run before any write,
                     stands in for unread Prod" · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     drift-since-measure, integration-studio}
                     · silence ok
laws:     A5 H2 D6
source:   expected · skills/op/retype-explicit · TWIN_SUCCESSION §6b (a slash-date column mixed conventions; a count
          of first parts over 12, not a failure count, found it)
note:     No Fits class can count this. An ambiguous slash date converts either way, so roundtrip and nonconvertible
          both read 0 for it; only discovery, then confirmation, finds the mixed convention.
```

### OP-retype-explicit-09 · edge: a text date's conversion depends on the session's language and date format
```case
kind:     corpus
tags:     edge
env:      pg + Customer.BirthText nvarchar(10) NULL · Customer 6 rows · BirthText 99/99/9999-shaped ×6, two with a
          first part over 12
change:   head Customer.BirthText nvarchar(10)→date NULL · scripts unchanged · the captured options carry no language or
          date format for the Octopus login
expect:   answers    HasRows(Customer)=yes · Fits(Customer.BirthText→date)={nonconvertible}, counted under SET LANGUAGE
                     us_english; SET DATEFORMAT ymd
          questions  HasRows(Customer) · Fits(Customer.BirthText→date)
          witnesses  presence(Customer) · misfit(Customer.BirthText, nonconvertible): a day-first date from the
                     planter's fixed table (P5)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T2 · residue {} · notChecked {unread:QA, unread:UAT, unread:Prod, platform:dateformat,
                     drift-since-measure}
laws:     F1 F3 H4 P5
source:   verify · ARCH §11.12 (which language and date format the Octopus login runs under) · ARCH §5 step 2 (probes
          and the planter run under us_english and ymd). Uncertain: how many of the slash dates count nonconvertible
          under ymd, and whether the deploying session would read them the same way. The guard blocks a populated
          retype whatever the count (F12), so only the answer's class and the residual depend on the first run.
note:     The verdict is data-blind here; the record is not. Until the Octopus login's language and date format are in
          the captured options, every Fits question over a text date carries `platform:dateformat`.
```

## audit-columns

### OP-audit-columns-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer + CreatedBy nvarchar(256) NOT NULL DEFAULT (SUSER_SNAME()) · CreatedOn datetime2(3) NOT NULL
          DEFAULT (SYSUTCDATETIME()) · ModifiedBy, ModifiedOn likewise · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/audit-columns
```

### OP-audit-columns-02 · populated, nullable stamps: purely additive
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows
change:   head Customer + CreatedBy, CreatedOn, ModifiedBy, ModifiedOn, each NULL with no default · scripts
          unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       the four stamps NULL on the presence row · every other column hash-equal before/after
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod,
                     integration-studio}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/audit-columns (nullable stamps add in a single release)
note:     n/a violating — the new stamps hold their default or NULL in every existing row, answered from the delta
          (ARCH §4); no existing value is tested, and a NOT NULL stamp with no default asks only HasRows (-04).
```

### OP-audit-columns-03 · populated, NOT NULL with function defaults: every row stamped
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows
change:   head Customer + the four stamps NOT NULL with DEFAULT (SUSER_SNAME()) / DEFAULT (SYSUTCDATETIME()) ·
          scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none (Scan: the defaulted NOT NULL adds backfill every row) · measures Rows(Customer)
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       no stamp NULL on the presence row · CreatedBy is the deploying login
          record     tier T0 · ships one release, in place · note "existing rows carry the deploy time and the
                     deploying login, not their real history" · silence ok · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, platform:edition, integration-studio}
laws:     F3 F4 H2 D1 D6 T2
source:   receipt · SamplePr "audit-columns: NOT NULL audit stamps with function defaults apply clean and backfill
          existing rows"
note:     The verdict must not embed the stamped values: they differ on every run, and D1 compares verdicts byte for
          byte apart from the timestamp field.
```

### OP-audit-columns-04 · populated, NOT NULL with no default: refused
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows
change:   head Customer + CreatedOn datetime2(3) NOT NULL, no default · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · residue {} · ships one release once the stamps carry defaults (-03) · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure}
laws:     F3 H5
source:   expected · the same ADD … NOT NULL with no default that SamplePr "add-mandatory: a new NOT NULL column
          with no default blocks a populated table, applies when empty" records
note:     The Optimistic NOT NULL trap; a DEFAULT cures it, which is what keeps it out of the tightening class.
```

### OP-audit-columns-05 · the one-release staging backfill the skill describes
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 5 rows
change:   head Customer + CreatedOn datetime2(3) NOT NULL, no default · pre-deploy adds CreatedOn NULL if absent,
          then sets it to SYSUTCDATETIME() where NULL · witness.sql: `-- shapes: none (the script fills a column it
          creates and reads no existing value)`
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer) · Authored(pre-deploy)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T3 · residue {pre-deploy: CreatedOn added NULL and filled} · ships in one release with a
                     DEFAULT (-03), or as add-mandatory's three releases (OP-add-mandatory-06)
laws:     H5 H6
source:   verify · skills/op/audit-columns says NOT NULL with no default ships in one release behind a pre-deploy
          backfill. By F2 the plan is computed before the pre-deploy runs, so the guard should still fire and leave
          the pre-deploy's column behind (F6). The first run decides which, and the case keeps that receipt.
note:     The first run without the declaration stops authored-script, as OP-delete-attribute-04 shows for a script
          that reads no value.
```

## add-default

### OP-add-default-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer.Region + CONSTRAINT DF_Customer_Region DEFAULT (N'Unassigned') · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · notChecked {unread:QA, unread:UAT, unread:Prod}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/add-default
```

### OP-add-default-02 · populated: no existing row is touched
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · Region NULL×1
change:   head Customer.Region + CONSTRAINT DF_Customer_Region DEFAULT (N'Unassigned') · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       Region hash-equal before/after (no UPDATE ran)
          record     tier T0 · ships one release, in place · note "existing blanks stay blank: a default fills only
                     new rows" · notChecked {unread:QA, unread:UAT, unread:Prod} · silence ok
laws:     F3 H2 D6
source:   receipt · F10 (an existing NULL stayed NULL; only a fresh insert got the default) · SamplePr
          "drop-default: the add and the drop are both in-place; existing rows never move" (its add leg)
note:     A default never backfills an existing column; only a new NOT NULL column with a default stamps rows
          (OP-add-mandatory-02).
          n/a violating — adding a default reads no existing value.
```

### OP-add-default-03 · a DEFAULT constraint is not a claim to check
```case
kind:     pure
tags:     edge
input:    ALTER TABLE [dbo].[Customer] ADD CONSTRAINT [DF_Customer_Region] DEFAULT (N'Unassigned') FOR [Region];
action:   Get-FxQuestions, then Get-FxTier
expect:   questions  none (CheckFalse and Orphan key on WITH CHECK CHECK CONSTRAINT, which a default add never
                     emits)
          record     tier T0
laws:     F4
source:   expected · ARCH §4 · skills/op/add-default (the delta is a clean ADD CONSTRAINT with no UPDATE)
```

### OP-add-default-04 · the environment holds a system-named default on the column
```case
kind:     corpus
tags:     edge
env:      pg + an unnamed DEFAULT (N'N/A') on Customer.Region, created by an ad-hoc script (system name
          DF__Customer__Region__<hash>) · Customer 5 rows
change:   head Customer.Region + CONSTRAINT DF_Customer_Region DEFAULT (N'Unassigned') · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       one default on Region, named DF_Customer_Region · Region hash-equal before/after
          record     tier T0 · flags {drift:Dev} · drift:Dev {DF__Customer__Region__<hash>} · note "the system-named
                     default is dropped and replaced" · notChecked {unread:QA, unread:UAT, unread:Prod} · silence ok
laws:     F9 F2 F3 D6
source:   verify · ARCH §11.10: whether Extract keeps a system-generated default name, and whether the copy built
          from it carries that name (F9) or a fresh one. If a fresh one, the identity check (F2) and the delta's DROP
          must still agree with the environment's (F5). The first run decides.
```

### OP-add-default-05 · existing blanks wanted filled: the authored-script stop
```case
kind:     process
tags:     remedy
env:      pg · Customer 5 rows · Region NULL×1 · ''×1
change:   head as OP-add-default-02 · post-deploy sets Region = 'Unassigned' where Region IS NULL (idempotent) ·
          witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev
expect:   questions  Authored(post-deploy over Customer.Region)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Customer.Region. Run facsimile
                     shape Dev dbo.Customer.Region, declare a witness in witness.sql for every shape the script
                     handles, and make the script check its own preconditions." · exit 5
          record     tier T3
laws:     H3 A3
source:   expected · skills/op/add-default (filling existing rows is a separate step) · ARCH §6
```

### OP-add-default-06 · existing blanks filled with declared shapes, silent on the second publish
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 5 rows · Region NULL×1 · ''×1
change:   as OP-add-default-05 · witness.sql declares the shapes the summary showed, a NULL and an '' · the
          developer decides '' stays as stored, and the record says so
expect:   answers    HasRows(Customer)=yes
          questions  Authored(post-deploy)
          witnesses  presence(Customer) · declared(Region NULL) · declared(Region '')
          outcome    verdict clean · exit 0
          copy       the NULL witness reads 'Unassigned' · the '' witness keeps '' · second publish: empty plan, the
                     backfill touches 0 rows, Customer hash unchanged
          record     tier T3 · ships one release (the constraint, then an idempotent post-deploy) · note "'' was
                     left as stored by decision" · silence ok · notChecked {unread:QA, unread:UAT, unread:Prod,
                     drift-since-measure}
laws:     A5 H2 D6
source:   expected · skills/op/add-default · skills/deploy-scripts (silence is the proof: 0 rows and an identical
          hash on a redeploy) · INVARIANTS D6
note:     D6 hashes every touched table, not only reference data, and counts rows affected, so a backfill missing its
          `WHERE Region IS NULL` would read not-silent here.
```

## modify-default

### OP-modify-default-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · Order 0 rows
change:   head DF_Order_StatusText DEFAULT (N'Pending')→DEFAULT (N'Shipped') · scripts unchanged
expect:   answers    HasRows(Order)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {modifies DF_Order_StatusText} · notChecked
                     {unread:QA, unread:UAT, unread:Prod} · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/modify-default (a DROP then ADD of the named constraint)
```

### OP-modify-default-02 · populated: rows written under the old default keep it
```case
kind:     corpus
tags:     clean
env:      pg · Order 4 rows · StatusText 'Pending'×2, 'Shipped'×1, 'Cancelled'×1
change:   head DF_Order_StatusText DEFAULT (N'Pending')→DEFAULT (N'Shipped') · scripts unchanged
expect:   answers    HasRows(Order)=yes
          questions  none
          witnesses  presence(Order)
          outcome    verdict clean · exit 0
          copy       StatusText hash-equal before/after (no UPDATE ran)
          record     tier T0 · ships one release, in place · note "rows written under the old default keep
                     'Pending'" · notChecked {unread:QA, unread:UAT, unread:Prod} · silence ok
laws:     F3 H2 T2 D6
source:   receipt · SamplePr "modify-default: changing a default's value applies clean and leaves existing rows
          unchanged" · F10
note:     n/a violating — a DROP and ADD of a default reads no existing value.
```

### OP-modify-default-03 · Dev's default is named, QA's is system-named: two proofs
```case
kind:     process
tags:     edge
env:      Dev: pg · Order 4 rows · QA: pg with StatusText's default unnamed (DF__Order__StatusTe__<hash>), created
          by an ad-hoc script · Order 4 rows
change:   head DF_Order_StatusText DEFAULT (N'Pending')→DEFAULT (N'Shipped') · scripts unchanged
setup:    Dev and QA stand-ins · LocalDB 2022
action:   facsimile read Dev · facsimile read QA · facsimile prove Dev QA
expect:   files      delta.Dev.sql drops DF_Order_StatusText · delta.QA.sql drops the system-named default ·
                     verdict.Dev.json and verdict.QA.json, each clean, with different delta hashes · verdicts.json
                     indexes both
          outcome    verdict clean · exit 0
          stdout     one line, `VERDICT clean · … · verdicts.json · exit 0` (the most severe of the two)
          flags      {drift:QA} in verdict.QA.json (the system-named default exists only in QA)
laws:     F7 F9 A1
source:   expected · skills/op/modify-default (an unnamed default differs per environment)
note:     Equal answers do not make one proof: the delta hashes differ, so each environment is proven on its own
          (F7). Whether the QA copy can carry the system name is OP-add-default-04's question.
```

### OP-modify-default-04 · old rows wanted re-stamped: the authored-script stop
```case
kind:     process
tags:     remedy
env:      pg · Order 4 rows · StatusText 'Pending'×2
change:   head as OP-modify-default-02 · post-deploy sets StatusText = 'Shipped' where StatusText = 'Pending' ·
          witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev
expect:   questions  Authored(post-deploy over Order.StatusText)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Order.StatusText. Run facsimile
                     shape Dev dbo.Order.StatusText, declare a witness in witness.sql for every shape the script
                     handles, and make the script check its own preconditions." · exit 5
          record     tier T3
laws:     H3 A3
source:   expected · skills/op/modify-default (a re-stamp is a separate backfill) · ARCH §6
note:     The script cannot tell a row the old default stamped from one a user set to 'Pending'. That is the
          developer's call, recorded. After the declarations, the verdict takes OP-add-default-06's form.
```

## drop-default

### OP-drop-default-01 · empty table
```case
kind:     corpus
tags:     empty
env:      pg · Product 0 rows
change:   head Product.LegacyCode without DF_Product_LegacyCode · scripts unchanged
expect:   answers    HasRows(Product)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {removes DF_Product_LegacyCode} · notChecked
                     {unread:QA, unread:UAT, unread:Prod} · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/drop-default
```

### OP-drop-default-02 · populated NOT NULL column: rows never move, the risk is the next insert
```case
kind:     corpus
tags:     clean
env:      pg · Product 5 rows · LegacyCode populated ×5
change:   head Product.LegacyCode without DF_Product_LegacyCode (still NOT NULL) · scripts unchanged
expect:   answers    HasRows(Product)=yes
          questions  none
          witnesses  presence(Product)
          outcome    verdict clean · exit 0
          copy       LegacyCode hash-equal before/after · no default on LegacyCode
          record     tier T0 · ships one release, in place · note "an insert that omits LegacyCode now fails Msg 515
                     at run time" · notChecked {unread:QA, unread:UAT, unread:Prod, app-behaviour} · silence ok
laws:     F3 H2 T2 D6
source:   receipt · SamplePr "drop-default: the add and the drop are both in-place; existing rows never move"
note:     The deploy cannot show the damage: it lives in the application's insert paths, the `app-behaviour` residual.
          n/a violating — a drop reads no value that could fail.
```

### OP-drop-default-03 · nullable column: omitted values become NULL, nothing fails
```case
kind:     corpus
tags:     edge
env:      pg + DF_Customer_Region DEFAULT (N'Unassigned') on Customer.Region · Customer 5 rows
change:   head Customer.Region without its default (still NULL) · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       Region hash-equal before/after
          record     tier T0 · ships one release, in place · note "an insert that omits Region now writes NULL, not
                     'Unassigned'" · notChecked {unread:QA, unread:UAT, unread:Prod, app-behaviour} · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/drop-default (the nullable branch: the meaning of "omitted" changes)
```

### OP-drop-default-04 · a seed row added in the same change relies on the dropped default
```case
kind:     corpus
tags:     edge
env:      pg · Product 5 rows (the pg seed rows)
change:   head Product.LegacyCode without DF_Product_LegacyCode · Data/Seed.sql gains Product Id 6 with no
          LegacyCode · witness.sql declares the seed's Product rows (without it the first run stops authored-script)
expect:   answers    HasRows(Product)=yes
          questions  Authored(post-deploy seed over Product)
          witnesses  presence(Product) · declared(seed Product rows)
          outcome    verdict blocked authored · Msg 515 · post-deploy · exit 3
          record     tier T3 · residue {main: DF_Product_LegacyCode dropped · post-deploy: the seed's insert of Product
                     6 failed} · note "the seed is part of the change set" · notChecked {unread:QA, unread:UAT,
                     unread:Prod}
laws:     H5 H6 F3
source:   expected · skills/op/drop-default (named trap: a seed that omitted the column fails on its next new row)
note:     The 515 is raised inside the post-deploy batch, so it is the seed's (`blocked authored`, H5); the remedy is
          -05.
```

### OP-drop-default-05 · remedy: the seed's new row names the column the dropped default used to fill
```case
kind:     corpus
tags:     remedy
env:      pg · Product 5 rows (the pg seed rows)
change:   as OP-drop-default-04, but Data/Seed.sql's Product 6 sets LegacyCode explicitly (a repository-defined value)
expect:   answers    HasRows(Product)=yes
          questions  Authored(post-deploy seed over Product)
          leg1       outcome    stop authored-script · "The change runs authored SQL over dbo.Product. Declare in
                                witness.sql the shapes the seed handles, or `-- shapes: none (<reason>)`." · exit 5
          leg2       witnesses  presence(Product) · declared(seed Product rows)
                     outcome    verdict clean · exit 0
                     copy       Product 6 holds the seed's LegacyCode · Products 1–5 hash-equal before/after
                     record     tier T3 · ships one release (the drop and the corrected seed together) · effects
                                {removes DF_Product_LegacyCode} · silence ok · notChecked {unread:QA, unread:UAT,
                                unread:Prod, app-behaviour}
laws:     A5 H2 H3 D6
source:   expected · skills/op/drop-default (the seed that relied on the default is part of the change set) · F15 (the
          seed's claims bind every atom in the release)
note:     The application's own inserts that omitted LegacyCode are not in the change set; they stay `app-behaviour`.
```

## delete-attribute

### OP-delete-attribute-01 · empty table: the column drops in place
```case
kind:     corpus
tags:     empty
env:      pg · Product 0 rows
change:   head Product without LegacyCode and DF_Product_LegacyCode · scripts unchanged
expect:   answers    HasRows(Product)=no
          questions  HasRows(Product)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T1 · ships one release, in place · effects
                     {removes Product.LegacyCode, DF_Product_LegacyCode} · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio} · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/delete-attribute (an empty column drops in place; the approval is never the lightest)
```

### OP-delete-attribute-02 · populated: the drop is refused
```case
kind:     corpus
tags:     clean
env:      pg · Product 5 rows · LegacyCode populated ×5
change:   head Product without LegacyCode and DF_Product_LegacyCode · scripts unchanged
expect:   answers    HasRows(Product)=yes
          questions  HasRows(Product)
          witnesses  presence(Product)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Product · exit 3
          record     tier T1 · residue {} · ships two releases after the application stops using the column ·
                     notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
laws:     F1 F3 H5 T2
source:   receipt · SamplePr "delete-attribute: removing a populated column is BLOCKED by the data-loss guard; the
          scripted DROP COLUMN is the irreversible step"
note:     n/a violating — a drop has no value class: a populated table is the only blocking state.
```

### OP-delete-attribute-03 · DROP COLUMN asks presence only
```case
kind:     pure
tags:     edge
input:    /* The column [dbo].[Product].[LegacyCode] is being dropped, data loss could occur. */
          IF EXISTS (select top 1 1 from [dbo].[Product])
              RAISERROR (N'Rows were detected. …', 16, 127) WITH NOWAIT;
          GO
          ALTER TABLE [dbo].[Product] DROP CONSTRAINT [DF_Product_LegacyCode];
          GO
          ALTER TABLE [dbo].[Product] DROP COLUMN [LegacyCode];
action:   Get-FxQuestions, then Get-FxRecord
expect:   questions  HasRows(Product) · nothing from DROP COLUMN or DROP CONSTRAINT
          record     tier T1 · effects {removes Product.LegacyCode, DF_Product_LegacyCode}
laws:     F4 H5
source:   expected · ARCH §4 · the warning text as sample-prs/delete-attribute.md records it (SQL72015)
```

### OP-delete-attribute-04 · R1 → R2 without a declaration: the stop, for a script that reads no value
```case
kind:     process
tags:     remedy
env:      pg · Product 5 rows · LegacyCode populated ×5
change:   program R1 → R2 · R1: model unchanged · pre-deploy drops DF_Product_LegacyCode, then LegacyCode, each only
          if it still exists · Data/Seed.sql stops writing LegacyCode · R2: head drops LegacyCode and its default,
          pre-deploy removed · witness.sql absent
setup:    LocalDB 2022 · Dev read, answers current
action:   facsimile prove Dev --program R1 R2
expect:   questions  from the R2 target: HasRows(Product) · Authored(R1 pre-deploy, seed)
          outcome    stop authored-script · "The change runs authored SQL over dbo.Product.LegacyCode. Run facsimile
                     shape Dev dbo.Product.LegacyCode, declare a witness in witness.sql for every shape the script
                     handles, and make the script check its own preconditions." · exit 5
          record     tier T3
laws:     F8 H3 A3
source:   expected · skills/op/delete-attribute (R1/R2) · ARCH §6 (authored-script)
note:     The pre-deploy is DDL and reads no value, so the answer to this stop is `-- shapes: none (<reason>)` in
          witness.sql (ARCH §6, authored-script).
```

### OP-delete-attribute-05 · the drop's R1, R1 replayed, R2
```case
kind:     corpus
tags:     remedy
env:      pg · Product 5 rows · LegacyCode populated ×5
change:   as OP-delete-attribute-04 · witness.sql: `-- shapes: none (the R1 script reads no value)`
expect:   answers    HasRows(Product)=yes
          questions  HasRows(Product) · Authored(R1 pre-deploy, seed)
          witnesses  presence(Product)
          outcome    verdict clean · exit 0
          record     tier T3 · ships two releases · R1 clean · R2 clean, empty plan · silence R1 not-silent(replay
                     plan: ADD [LegacyCode] … DEFAULT (N'LEGACY') NOT NULL, every row restamped and the original
                     values gone: the F17 hazard) · R2 ok · effects {removes Product.LegacyCode,
                     DF_Product_LegacyCode} · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     drift-since-measure, integration-studio}
laws:     F8 A5 H2 D6
source:   receipt · F17 (a lagging-model publish re-created the dropped column, backfilled from its default) ·
          FINDINGS Part 4 (delete-attribute: two releases, proven 2026-08-21)
note:     The 4-phase deprecation's first three phases (soft-deprecate, stop writes, verify unused) are
          application-side; the copy proves only the drop's two releases. After an R1 replay, R2 itself blocks on
          the re-added column.
```

### OP-delete-attribute-06 · R1 with the seed still naming the dropped column
```case
kind:     corpus
tags:     edge
env:      pg · Product 5 rows (the pg seed rows) · LegacyCode populated ×5
change:   program R1 → R2 as OP-delete-attribute-05, but Data/Seed.sql still writes LegacyCode
expect:   witnesses  presence(Product)
          outcome    verdict blocked authored · Msg 207 · post-deploy · exit 3
          record     tier T3 · residue {pre-deploy: DF_Product_LegacyCode and LegacyCode dropped · post-deploy: the
                     seed failed on LegacyCode} · note "the corrected seed is part of R1" · notChecked {unread:QA,
                     unread:UAT, unread:Prod}
laws:     H5 H6 F3
source:   receipt · F15 (b: after a pre-deploy column drop, the seed still naming the column failed with Msg 207 on
          a half-applied release)
note:     The values are already gone when the publish reports failure; the residue is read by scripting head
          against the copy.
```

### OP-delete-attribute-07 · CDC-tracked table: principal-led
```case
kind:     process
tags:     refuse
env:      pg · Product 5 rows · Cdc(Product)=yes
change:   head Product without LegacyCode and DF_Product_LegacyCode · scripts unchanged
setup:    SQL Server 2022 stand-in for Dev, CDC enabled on dbo.Product · LocalDB for the copy
action:   facsimile read Dev · facsimile prove Dev
expect:   answers    HasRows(Product)=yes · Cdc(Product)=yes
          outcome    stop cdc · "dbo.Product is tracked by CDC, and LocalDB has none. This change is principal-led."
                     · exit 9
          flags      {cdc}
laws:     H2 H3 A3
source:   expected · ARCH §6 (cdc) · the premise is red-team #6 (Msg 4928 on sp_rename ✓; a silently stale capture
          instance ✓)
note:     On a CDC server the R1 drop may fail (Msg 4928 is recorded for sp_rename) or leave the capture instance
          stale. LocalDB can show neither.
```

### OP-delete-attribute-08 · a project view still reads the column
```case
kind:     process
tags:     edge
env:      pg + view dbo.vProductLegacy (SELECT Id, LegacyCode FROM dbo.Product) in the project · Product 5 rows
change:   head Product without LegacyCode and DF_Product_LegacyCode · the view left unchanged
setup:    MSBuild available · Dev read
action:   facsimile prove Dev
expect:   outcome    stop build-failed · "The project does not build: SQL71501: … unresolved reference to
                     [dbo].[Product].[LegacyCode]." · exit 7
laws:     H3 A3
source:   expected · skills/op/delete-attribute (named trap: a view still referencing the column)
note:     Dependents are dropped first, in the same change. A dependent that lives only in an environment is not in
          the project and is not caught by the build.
```

## rename-attribute

### OP-rename-attribute-01 · empty table, refactorlog entry present
```case
kind:     corpus
tags:     empty
env:      pg · Customer 0 rows
change:   head Customer.ContactPhone → MobileNumber · SampleCatalog.refactorlog + Rename Refactor
          [dbo].[Customer].[ContactPhone] → [MobileNumber] · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  none
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T0 · ships one release, in place · effects {renames Customer.ContactPhone → MobileNumber}
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio} · silence ok
laws:     F3 H2 D6
source:   expected · skills/_index/identity-and-refactorlog (with the entry the delta is sp_rename)
```

### OP-rename-attribute-02 · populated, refactorlog entry present: every value kept
```case
kind:     corpus
tags:     clean
env:      pg · Customer 5 rows · ContactPhone populated ×5
change:   as OP-rename-attribute-01
expect:   answers    HasRows(Customer)=yes
          questions  none (sp_rename reads no row)
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
          copy       MobileNumber hash-equal to ContactPhone before · Customer's object_id unchanged
          record     tier T0 · ships one release, in place · effects {renames Customer.ContactPhone → MobileNumber}
                     · notChecked {unread:QA, unread:UAT, unread:Prod, integration-studio, external-consumers}
                     · silence ok
laws:     F3 H2 D6
source:   expected · F13 (a refactorlog entry makes the delta sp_rename) · SamplePr "rename-attribute: a bare column
          rename (no refactorlog) is a DROP+ADD blocked by the data-loss guard; EXEC sp_rename preserves every
          value" (sp_rename keeps every value)
note:     Every caller of the old name must move; callers outside the database are the `external-consumers` residual.
          n/a violating — sp_rename reads no row, so no value can fail it.
```

### OP-rename-attribute-03 · the record reads rename from the delta
```case
kind:     pure
tags:     edge
input:    EXECUTE sp_rename @objname = N'[dbo].[Customer].[ContactPhone]', @newname = N'MobileNumber',
              @objtype = N'COLUMN';
action:   Get-FxQuestions, then Get-FxRecord
expect:   questions  none
          record     tier T0 · effects {renames Customer.ContactPhone → MobileNumber}
laws:     F4
source:   expected · skills/_index/identity-and-refactorlog (read the delta: sp_rename or DROP+ADD) · the statement
          as skills/op/rename-attribute records it
note:     The same edit without the entry is DROP COLUMN + ADD, and the record must say removes and adds, never
          renames (-04, -05).
```

### OP-rename-attribute-04 · populated, no refactorlog entry: a drop and an add, refused
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows · ContactPhone populated ×5
change:   head Customer.ContactPhone → MobileNumber · refactorlog unchanged · scripts unchanged
expect:   answers    HasRows(Customer)=yes
          questions  HasRows(Customer)
          witnesses  presence(Customer)
          outcome    verdict blocked guard · Msg 50000/127 · dbo.Customer · exit 3
          record     tier T1 · residue {} · effects {removes Customer.ContactPhone, adds Customer.MobileNumber} ·
                     note "not a rename: add the Rename Refactor entry" · notChecked
                     {unread:QA, unread:UAT, unread:Prod, drift-since-measure}
laws:     F1 F3 H5 T2
source:   receipt · SamplePr "rename-attribute: a bare column rename (no refactorlog) is a DROP+ADD blocked by the
          data-loss guard; EXEC sp_rename preserves every value"
note:     The remedy is the refactorlog entry, a model change: no authored script, so no authored-script stop.
```

### OP-rename-attribute-05 · empty, no refactorlog entry: clean, and still not a rename
```case
kind:     corpus
tags:     edge
env:      pg · Customer 0 rows
change:   head Customer.ContactPhone → MobileNumber · refactorlog unchanged · scripts unchanged
expect:   answers    HasRows(Customer)=no
          questions  HasRows(Customer)
          witnesses  none
          outcome    verdict clean · exit 0
          record     tier T1 · effects {removes Customer.ContactPhone, adds Customer.MobileNumber} · note "not a
                     rename: a populated environment blocks, and without the guard it loses every value" ·
                     notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
                     · silence ok
laws:     F3 F7 H2 D6
source:   expected · skills/_index/identity-and-refactorlog (a missing entry is DROP+ADD whatever the data)
note:     A clean Dev verdict on an empty table says nothing about QA: each environment gets its own verdict (F7).
```

### OP-rename-attribute-06 · the seed still names the old column
```case
kind:     corpus
tags:     edge
env:      pg · Customer 5 rows (the pg seed rows) · ContactPhone populated ×5
change:   as OP-rename-attribute-01, but Data/Seed.sql still writes ContactPhone
expect:   answers    HasRows(Customer)=yes
          questions  none
          witnesses  presence(Customer)
          outcome    verdict blocked authored · Msg 207 · post-deploy · exit 3
          record     tier T0 · residue {main: sp_rename applied, MobileNumber holds every value · post-deploy: the
                     seed failed on ContactPhone} · note "rename the seed's column references in the same change
                     (-08)" · notChecked {unread:QA, unread:UAT, unread:Prod}
laws:     H5 H6 F3
source:   receipt · F15 (b, the rename face: the seed failed identically until its column references were renamed)
note:     The tier is T0 because the delta asks nothing; the block comes from an unchanged post-deploy script.
```

### OP-rename-attribute-07 · CDC-tracked table: principal-led
```case
kind:     process
tags:     refuse
env:      pg · Customer 5 rows · Cdc(Customer)=yes
change:   as OP-rename-attribute-01
setup:    SQL Server 2022 stand-in for Dev, CDC enabled on dbo.Customer · LocalDB for the copy
action:   facsimile read Dev · facsimile prove Dev
expect:   answers    HasRows(Customer)=yes · Cdc(Customer)=yes
          outcome    stop cdc · "dbo.Customer is tracked by CDC, and LocalDB has none. This change is
                     principal-led." · exit 9
          flags      {cdc}
laws:     H2 H3 A3
source:   expected · ARCH §6 (cdc) · the premise is red-team #6 (sp_rename of a CDC-tracked column fails Msg 4928 ✓)
note:     On the real server this clean-looking T0 rename fails with Msg 4928, while the copy would report clean.
```

### OP-rename-attribute-08 · remedy: the seed's column references renamed in the same change
```case
kind:     corpus
tags:     remedy
env:      pg · Customer 5 rows (the pg seed rows) · ContactPhone populated ×5
change:   as OP-rename-attribute-01 · Data/Seed.sql's ContactPhone references renamed to MobileNumber in the same change
expect:   answers    HasRows(Customer)=yes
          questions  Authored(post-deploy seed over Customer); the sp_rename reads no row
          leg1       outcome    stop authored-script · "The change runs authored SQL over dbo.Customer. Declare in
                                witness.sql the shapes the seed handles, or `-- shapes: none (<reason>)`." · exit 5
          leg2       witnesses  presence(Customer) · declared(the seed's Customer rows)
                     outcome    verdict clean · exit 0
                     copy       MobileNumber holds every value · Customer's object_id unchanged
                     record     tier T3 · ships one release (the rename and the corrected seed together) · effects
                                {renames Customer.ContactPhone → MobileNumber} · silence ok · notChecked
                                {unread:QA, unread:UAT, unread:Prod, integration-studio, external-consumers}
laws:     A5 H2 D6 F3
source:   receipt · F15 (b, the rename face: it failed until the seed's six column references were renamed with it) ·
          sample-prs/compound/rename-then-tighten.md, What proving showed (the seed's references renamed, Release 1
          published: the single sp_rename, 5 of 5 values preserved)
note:     The refactorlog renames the schema, not raw column references inside deployment scripts (-06).
```

## identity-swap

### OP-identity-swap-01 · empty table: the rebuild moves nothing
```case
kind:     corpus
tags:     empty
env:      pg · Account 0 rows
change:   head Account.Id int IDENTITY(1,1)→int (IDENTITY removed) · scripts unchanged
expect:   answers    HasRows(Account)=no · Gapped(Account)=no
          questions  Rebuild(Account): HasRows(Account) · Gapped(Account)
          witnesses  none
          outcome    verdict clean · exit 0
          copy       Account.Id is_identity 0
          record     tier T2 · ships one release (a table rebuild) · effects {modifies Account.Id: IDENTITY removed}
                     · notChecked {unread:QA, unread:UAT, unread:Prod, drift-since-measure, integration-studio}
                     · silence ok
laws:     F3 H2 D6
source:   expected · skills/op/identity-swap (empty, no keys: the rebuild copies nothing; confirm it is a rebuild)
```

### OP-identity-swap-02 · populated, gapped ids, no incoming key: ids preserved
```case
kind:     corpus
tags:     clean
env:      pg · Category 4 rows · Id 1, 2, 3, 7 (Id 7 added outside the seed)
change:   head Category.Id int→int IDENTITY(1,1) · scripts unchanged
expect:   answers    HasRows(Category)=yes · Gapped(Category)=yes
          questions  Rebuild(Category): HasRows(Category) · Gapped(Category)
          witnesses  presence(Category) · gapped(Category)
          outcome    verdict clean · exit 0
          copy       acceptance reads Gapped(Category)=yes · Id set hash-equal before/after · is_identity 1 ·
                     IDENT_CURRENT(Category) ≥ MAX(Id)
          record     tier T2 · ships one release (a table rebuild, no incoming key) · effects
                     {modifies Category.Id: IDENTITY added} · notChecked {unread:QA, unread:UAT, unread:Prod, volume,
                     integration-studio}
                     · silence ok
laws:     F1 F3 M2 H2 D6
source:   expected · F12 (pg_idsw_before: the gate allows the rebuild; ids preserved; IDENT_CURRENT = 3), recorded on
          contiguous ids 1, 2, 3 only; the gapped fixture here is not yet witnessed live
note:     F12's ids were 1, 2, 3, which a re-minting rebuild would also have produced. A contiguous-from-1 witness
          is vacuous, so a gapped environment gets a gapped witness (two rows, which also serve as presence: M2), and
          acceptance stops if the copy reads Gapped=no (F1).
          n/a violating — the rebuild copies every value unchanged (no moved column changes type), so no value can
          refuse it; gaps are reproduced, never violating.
```

### OP-identity-swap-03 · the rebuild's IF EXISTS is not a guard
```case
kind:     pure
tags:     edge
input:    CREATE TABLE [dbo].[tmp_ms_xx_Category] ([Id] INT IDENTITY (1, 1) NOT NULL,
              [Code] NVARCHAR (30) NOT NULL, …);
          IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Category])
              BEGIN
                  SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] ON;
                  INSERT INTO [dbo].[tmp_ms_xx_Category] ([Id], [Code], [IsActive])
                  SELECT [Id], [Code], [IsActive] FROM [dbo].[Category] ORDER BY [Id] ASC;
                  SET IDENTITY_INSERT [dbo].[tmp_ms_xx_Category] OFF;
              END
          DROP TABLE [dbo].[Category];
          EXECUTE sp_rename N'[dbo].[tmp_ms_xx_Category]', N'Category';
action:   Get-FxQuestions, then Get-FxTier
expect:   questions  Rebuild(Category): HasRows(Category) · Gapped(Category) · no Fits (no moved column is named
                     lossy)
          record     tier T2
laws:     F4 H5
source:   expected · ARCH §4 (Rebuild) · H5 (a guard batch carries RAISERROR … 16, 127) · the shape as F12 records
          it
note:     No RAISERROR follows this IF EXISTS, so no block here can ever be attributed to the guard.
```

### OP-identity-swap-04 · the seed inserts explicit ids into the new IDENTITY column
```case
kind:     corpus
tags:     edge
env:      pg · Category 3 rows · Id 1, 2, 3 (the pg seed)
change:   head Category.Id int→int IDENTITY(1,1) · Data/Seed.sql unchanged: its Category MERGE inserts explicit Ids
          without SET IDENTITY_INSERT
expect:   answers    HasRows(Category)=yes · Gapped(Category)=no
          questions  Rebuild(Category): HasRows(Category) · Gapped(Category)
          witnesses  presence(Category)
          outcome    verdict blocked authored · Msg 544 · post-deploy · exit 3
          record     tier T2 · residue {main: Category rebuilt, is_identity 1, ids preserved · post-deploy: the seed's
                     explicit-id insert failed} · notChecked {unread:QA, unread:UAT, unread:Prod}
laws:     H5 H6 F3
source:   receipt · F12 (the seed's explicit-id insert fails Msg 544 until bracketed with SET IDENTITY_INSERT)
```

### OP-identity-swap-05 · the seed bracketed, with declared rows
```case
kind:     corpus
tags:     remedy
env:      pg · Category 3 rows · Id 1, 2, 3 (the pg seed)
change:   head Category.Id int→int IDENTITY(1,1) · the seed's Category MERGE bracketed with SET IDENTITY_INSERT
          dbo.Category ON … OFF · witness.sql declares Dev's Category rows (static rows are model data, safe to
          read)
expect:   answers    HasRows(Category)=yes · Gapped(Category)=no
          questions  Rebuild(Category): HasRows(Category) · Gapped(Category) · Authored(post-deploy seed)
          witnesses  presence(Category) · declared(Category Ids 1–3)
          outcome    verdict clean · exit 0
          copy       is_identity 1 · ids 1, 2, 3 · IDENT_CURRENT(Category) ≥ MAX(Id) · second publish: empty plan,
                     the seed silent
          record     tier T3 · ships one release (the rebuild and the seed fix together) · silence ok · notChecked
                     {unread:QA, unread:UAT, unread:Prod, integration-studio}
laws:     A5 H2 F3 D6
source:   receipt · F12 (the seed fix ships in the same change set; end state is_identity = 1, ids preserved,
          IDENT_CURRENT = 3)
note:     The first run without the declaration stops authored-script, because the seed changed.
```

### OP-identity-swap-06 · an incoming foreign key around the rebuild
```case
kind:     corpus
tags:     edge
env:      pg + FK_OrderLine_Order (OrderLine.OrderId → Order.Id, trusted) · Order 4 rows · Id 2, 3, 5, 11 ·
          OrderLine 8 rows, every OrderId resolves
change:   head Order.Id int IDENTITY(1,1)→int (IDENTITY removed) · scripts unchanged
expect:   answers    HasRows(Order)=yes · HasRows(OrderLine)=yes · Gapped(Order)=yes ·
                     Orphan(OrderLine.(OrderId)→Order)=clean · Trust(OrderLine, FK_OrderLine_Order)=trusted
          questions  Rebuild(Order): HasRows(Order) · Gapped(Order) · Orphan(OrderLine.(OrderId)→Order), from the
                     re-added key's WITH CHECK CHECK
          witnesses  presence(OrderLine) (+ancestors Order) · gapped(Order)
          outcome    verdict clean · exit 0
          copy       Order Id set and whole-row hash equal before/after · 0 orphans · FK_OrderLine_Order present and
                     trusted · is_identity 0
          record     tier T2 · ships one release (a rebuild, the incoming key dropped and re-added) · notChecked
                     {unread:QA, unread:UAT, unread:Prod, volume, engine-version, integration-studio} · silence ok
laws:     F9 F1 F3 T2 D6
source:   verify · SamplePr "identity-swap: removing IDENTITY from Order.Id is a table rebuild — the true outcome
          under the production gate, keys and incoming FK preserved" proves this end state but accepts either path
          to it. Uncertain: whether the Strict publish performs the rebuild with an incoming key (F12's keyless
          rebuild was allowed) or blocks it. The first run decides.
note:     skills/op/identity-swap says a table with incoming keys ships across releases; only the keyless case is
          proven. If the publish is clean, the record says one release.
```

### OP-identity-swap-07 · more than a million rows: the rebuild copies every row
```case
kind:     pure
tags:     edge
input:    questions Rebuild(Order): HasRows(Order) · Gapped(Order) · answers HasRows(Order)=yes · Gapped(Order)=yes
          · Rows(Order)=1500000
action:   Get-FxTier
expect:   record     tier T2 · flags {scale}
laws:     H4
source:   expected · ARCH §7 (scale: a rebuild of a table of 1M rows or more) · skills/op/identity-swap (the copy is
          the expensive part)
```

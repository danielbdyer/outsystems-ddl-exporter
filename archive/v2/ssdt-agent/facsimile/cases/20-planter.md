# The planter (`PLT`)

*`Get-FxWitnessPlan` (pure) picks the witnesses; `sql/plant.sql` makes the rows from the copy's catalog. Every planted value is
generated or repository-defined, never an environment value (P5). A generated value is a pure function of (table, column,
ordinal, type, width, class) (D3): strings `w<n>` fitted to width, binary `0x77…`, dates 2000-01-01 + n days. Where the `w`
marker cannot fit, the value comes from the planter's fixed per-class table (`''`, a day-first date, a numeric overflow, a
`char(1)`). Keys resolved from the copy, period columns and identity values are outside D3 but inside D1.
A table plants the maximum of its needs, not their sum (M2): a violation row also serves as presence, classes on different
columns share a row, classes on one column take separate rows, and `dups`, `nulls` and a gapped identity take two rows. Every
Orphan question links the child to a planted or reference parent, even before the FK exists. Every probe and the planter run
under `SET LANGUAGE us_english; SET DATEFORMAT ymd`. The planter may suspend a constraint to plant and restores the
environment's trust state afterwards; `witness.sql` (DML only, in blocks headed `-- plants: <T>` or `-- witness: <question key>`,
run in FK order) runs inside that envelope. `decision` marks a choice ARCH leaves open.*

## Types

### PLT-01 · int, smallint, bigint: the row's ordinal
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a int NOT NULL · b smallint NULL · c bigint NOT NULL) · T 3 rows
action:   plant · questions HasRows(T)
expect:   answers    HasRows(T)=yes
          plan       presence(T)
          copy       T 1 row · Id 1 (engine) · a=1 · b=1 · c=1
laws:     D3 F1
source:   expected · ARCH §5 step 2
note:     Nullable b gets a value, not NULL (ARCH §5 step 2). There is no distribution: the value is the ordinal n at
          every integer width (the Twin's σ mint is retired, ARCH §2 row 11).
```

### PLT-02 · tinyint: the ordinal, wrapping at 256
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c tinyint NOT NULL) · T 2 rows
action:   plant · questions HasRows(T)
expect:   plan       presence(T)
          copy       T 1 row · c=1
laws:     D3
source:   expected · ARCH §5 step 2
note:     decision: row n gets n mod 256, so a non-unique column never overflows in `up`. A unique tinyint
          column holds at most 256 planted rows (PLT-114 for the narrow-unique stop).
```

### PLT-03 · bit: 1, then 0
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c bit NOT NULL · d bit NULL · Note nvarchar(20) NULL) · T 4 rows · Note NULL×2
action:   plant · questions HasRows(T) · Unique(T,(Note)) (head adds a plain unique index on Note)
expect:   answers    HasRows(T)=yes · Unique(T,(Note))={nulls}
          witnesses  dup(T,(Note),nulls): two rows, which also answer HasRows(T)
          copy       T 2 rows · row 1 c=1 · d=1 · Note NULL · row 2 c=0 · d=0 · Note NULL
laws:     D3 F1 M2
source:   expected · ARCH §5 steps 2–3
note:     Row n gets n mod 2, so the first row reads 1. The nulls class needs two rows (M2), which is how a second row
          arises in prove. A unique bit column holds two planted rows at most (PLT-114).
```

### PLT-04 · decimal(1,0) and numeric(1,0): one digit
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c decimal(1,0) NOT NULL · d numeric(1,0) NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · c=1 · d=1
laws:     D3
source:   expected · ARCH §5 step 2
note:     decision: row n gets n mod 10^(p−s), so ten values exist and a unique (1,0) column holds ten rows.
          `numeric` is `decimal` throughout.
```

### PLT-05 · decimal(38,10), and the general (p,s) rule
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a decimal(38,10) NOT NULL · b decimal(18,2) NULL · c decimal(2,2) NOT NULL) · T 2 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a=1.0000000000 · b=1.00 · c=0.01
laws:     D3
source:   expected · ARCH §5 step 2
note:     The integer part is n mod 10^(p−s) and every scale digit is zero. A p = s column has no integer
          part and gets n × 10^−s instead.
```

### PLT-06 · money, smallmoney, float, real: the ordinal, exactly
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a money NOT NULL · b smallmoney NULL · c float NOT NULL · d real NULL) · T 3 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a=1.0000 · b=1.0000 · c=1E0 · d=1E0
laws:     D3 D1
source:   expected · ARCH §5 step 2
note:     Integers below 2^24 are exact in `real`, so equality probes and dup witnesses compare exactly and
          every run writes identical bytes.
```

### PLT-07 · date: base plus n days
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c date NOT NULL · d date NULL) · T 2 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · c='2000-01-02' · d='2000-01-02'
laws:     D3 D1
source:   expected · ARCH §5 step 2
note:     A date cannot carry the `w` marker, so it comes from the planter's fixed base (P5), which keeps D1. Nothing is
          drawn from a window (the Twin's date distributions are retired, ARCH §2 row 16).
```

### PLT-08 · time(n): n seconds, no fraction
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a time(0) NOT NULL · b time(7) NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a='00:00:01' · b='00:00:01.0000000'
laws:     D3
source:   expected · ARCH §5 step 2
note:     The fractional digits are zero, so the same value is valid at every precision and no rounding
          happens. Row n > 86399 wraps modulo a day.
```

### PLT-09 · datetime and smalldatetime: base plus n days, at midnight, under the pinned date format
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a datetime NOT NULL · b smalldatetime NULL) · T 2 rows · the copy's login
          defaults to LANGUAGE british (dmy)
action:   plant · questions HasRows(T)
expect:   trace      the plant session begins SET LANGUAGE us_english; SET DATEFORMAT ymd, whatever the login's default
          copy       T 1 row · a='2000-01-02T00:00:00' · b='2000-01-02T00:00:00' (2 January, not 1 February)
laws:     D3 D1
source:   expected · ARCH §5 step 2 (the language and date-format pin); Microsoft doc "datetime (Transact-SQL)" (the
          yyyy-mm-dd form is DATEFORMAT-dependent for datetime)
note:     The planter writes the `T` form, which no DATEFORMAT reinterprets, and pins the session too, so a class witness
          that depends on the format (PLT-65) reads the same in plant.sql and probes.sql. Midnight avoids datetime's
          1/300 s rounding and smalldatetime's minute rounding.
```

### PLT-10 · datetime2(n): base plus n days, at every precision
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a datetime2(0) NOT NULL · b datetime2(7) NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a='2000-01-02 00:00:00' · b='2000-01-02 00:00:00.0000000'
laws:     D3
source:   expected · ARCH §5 step 2
```

### PLT-11 · datetimeoffset(n): UTC
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c datetimeoffset(3) NOT NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · c='2000-01-02 00:00:00.000 +00:00'
laws:     D3 D1
source:   expected · ARCH §5 step 2
note:     The offset is always +00:00, so the value never depends on the planting machine's time zone.
```

### PLT-12 · varchar(n) and nvarchar(n): `w<n>`, fitted to width
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a varchar(10) NOT NULL · b nvarchar(256) NULL · c nvarchar(3) NOT NULL)
          T 2 rows · a holds the canary 'zz-canary-a'
action:   plant · questions HasRows(T) · then plant row n = 100 in isolation
expect:   copy       row 1 a='w1' · b=N'w1' · c=N'w1' · row 100 c=N'w2s' (`w` + base-36 100, since 'w100' exceeds 3)
          answers    the canary is absent from every copy table
laws:     D3 P5 F1
source:   expected · F20 (the Twin's width-blind token failed a bulk copy; values must fit the declared width)
note:     The fit is `w` + decimal n if it fits, else `w` + lower-case base-36 n, else the left `width` characters.
          A truncation that collides in a unique column is caught at insert (PLT-42).
```

### PLT-13 · char(n) and nchar(n): fixed width, padded by the engine
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a char(10) NOT NULL · b nchar(4) NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a='w1' + 8 spaces · b=N'w1' + 2 spaces (DATALENGTH 10 and 8)
laws:     D3
source:   expected · ARCH §5 step 2
note:     The planter writes 'w1' and the engine pads it. Equality ignores the padding, and the overbytes
          class (trailing spaces only) cannot arise from a fixed-width source.
```

### PLT-14 · char(1) and nvarchar(1): the bare marker
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a char(1) NOT NULL · b nvarchar(1) NULL · Note nvarchar(20) NULL) · T 5 rows · Note NULL×1
action:   plant · questions HasRows(T) · NotNull(T.Note)
expect:   witnesses  null(T.Note), which also answers HasRows(T)
          copy       T 1 row · a='w' · b=N'w' · Note NULL
laws:     D3 P5 M2
source:   expected · INVARIANTS P5 (the per-class table's `char(1)` entry)
note:     A width-1 string has room for the marker and nothing else, so every planted row is equal in it. That is
          harmless unless the column is unique (PLT-42 in prove, PLT-114 in `up`).
```

### PLT-15 · varchar(max), nvarchar(max), text, ntext
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a varchar(max) NOT NULL · b nvarchar(max) NULL · c text NULL · d ntext NOT NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a='w1' · b=N'w1' · c='w1' · d=N'w1'
laws:     D3
source:   expected · ARCH §5 step 2
note:     text and ntext are deprecated and cannot be compared or indexed, so they never carry a Unique or
          KeyLength question. The planter treats them as (max) types.
```

### PLT-16 · no name routing: an email-like column gets the same literal as any string
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Email nvarchar(256) NULL · WorkEmail varchar(30) NOT NULL
          · EmailFlag bit NOT NULL · Mail nvarchar(10) NULL) · T 2 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · Email=N'w1' · WorkEmail='w1' · EmailFlag=1 · Mail=N'w1'
          plan       no planted value depends on a column's name
laws:     P5 D3
source:   expected · INVARIANTS P5 (no name routing); ARCH §2 row 12 (the schema-derived floor's name routing is retired)
note:     No publish verdict reads whether a value looks like an email address. Renaming a column therefore never changes
          a planted value.
```

### PLT-17 · binary(n): 0x77, then the ordinal
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a binary(4) NOT NULL · b binary(1) NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a=0x77000001 · b=0x01
laws:     D3 P5
source:   expected · ARCH §5 step 2
note:     decision: 0x77 (`w`) followed by n big-endian in the remaining bytes. A width too small for both
          gets the ordinal bytes alone, so binary(1) still distinguishes 256 rows.
```

### PLT-18 · varbinary(n), varbinary(max), image
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a varbinary(16) NOT NULL · b varbinary(max) NULL · c image NULL · d varbinary(1) NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a=0x7701 · b=0x7701 · c=0x7701 · d=0x01
laws:     D3
source:   expected · ARCH §5 step 2
note:     A variable-width value carries the minimal big-endian bytes of n after the prefix. image is deprecated
          and is treated as varbinary(max).
```

### PLT-19 · uniqueidentifier: a reserved, deterministic pattern
```case
kind:     sql
env:      pg + T (Id uniqueidentifier NOT NULL PK · Ref uniqueidentifier NULL) · T 2 rows
action:   plant · questions HasRows(T) · twice, on two fresh restores
expect:   copy       T 1 row · Id='77777777-0000-4000-8000-000000000001' · Ref the same value · identical in both runs
laws:     D3 D1 P5
source:   expected · INVARIANTS D1, P5
note:     The marker cannot fit a GUID, so the pattern is the planter's fixed, documented one: the last 12 hex digits are
          n, and the planter never calls NEWID(). Orphan witnesses use a separate reserved pattern (PLT-76).
```

### PLT-20 · xml, untyped
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Doc xml NOT NULL · Extra xml NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · Doc=N'<w n="1"/>' · Extra=N'<w n="1"/>'
laws:     D3 P5
source:   expected · ARCH §5 step 2
```

### PLT-21 · xml typed by a schema collection, NOT NULL: needs a declared row
```case
kind:     sql
env:      pg + XML SCHEMA COLLECTION dbo.OrderSchema + T (Id int IDENTITY PK · Doc xml(CONTENT dbo.OrderSchema) NOT NULL) · T 3 rows
action:   plant · questions HasRows(T)
expect:   plan       presence(T)
          outcome    stop planter-needs-row · "dbo.T (Doc is xml typed by dbo.OrderSchema): write one valid INSERT for
                     dbo.T into witness.sql under -- plants: dbo.T" · exit 5
laws:     H3 F1
source:   expected · ARCH §6 planter-needs-row
note:     The planter does not read schema collections. A valid document is a domain artefact, so an agent
          writes it.
```

### PLT-22 · typed xml or CLR UDT that is nullable and named by no question: NULL
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Doc xml(dbo.OrderSchema) NULL · Shape dbo.Polygon NULL (user CLR type)
          · Note nvarchar(20) NULL) · T 3 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · Doc NULL · Shape NULL · Note=N'w1'
laws:     F1 D3
source:   expected · ARCH §5 step 2
note:     decision: this is the one exception to "nullable gets a value". A NULL in a column that no question
          names changes no answer. If a question names the column, PLT-21's stop applies.
```

### PLT-23 · sql_variant: the string literal inside
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c sql_variant NOT NULL) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · c=N'w1' · SQL_VARIANT_PROPERTY(c,'BaseType')='nvarchar'
laws:     D3 P5
source:   expected · ARCH §5 step 2
```

### PLT-24 · hierarchyid: one level per row
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Node hierarchyid NOT NULL) · T 4 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · Node=hierarchyid::Parse('/1/')
laws:     D3
source:   expected · ARCH §5 step 2
```

### PLT-25 · geography and geometry: a point per row
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Place geography NOT NULL · Spot geometry NULL) · T 2 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · Place=geography::Point(0, 1, 4326) · Spot=geometry::Point(1, 0, 0)
laws:     D3
source:   expected · ARCH §5 step 2
note:     The geography longitude is n mod 180, so it stays in range. hierarchyid, geography and geometry are
          system CLR types (is_user_defined=0), so they are planted; only user CLR types stop (PLT-28).
```

### PLT-26 · rowversion and timestamp: skipped
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c nvarchar(20) NOT NULL · Ver rowversion) · T 3 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · c=N'w1' · Ver assigned by the engine · Ver absent from the INSERT column list
laws:     F1
source:   expected · ARCH §5 step 2
```

### PLT-27 · an alias type resolves to its base type and width
```case
kind:     sql
env:      pg + TYPE dbo.ShortCode FROM nvarchar(6) NOT NULL + T (Id int IDENTITY PK · c dbo.ShortCode) · T 1 row
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · c=N'w1' (fitted to 6, from the alias)
laws:     D3
source:   expected · ARCH §5 step 2
note:     catalog.sql resolves an alias (is_user_defined=1, is_assembly_type=0) through system_type_id and
          the alias's max_length, precision and scale.
```

### PLT-28 · a user CLR type, NOT NULL: needs a declared row
```case
kind:     sql
env:      pg + ASSEMBLY Geo + TYPE dbo.Polygon EXTERNAL NAME Geo.Polygon + T (Id int IDENTITY PK · Shape dbo.Polygon NOT NULL) · T 2 rows
action:   plant · questions HasRows(T)
expect:   outcome    stop planter-needs-row · "dbo.T (Shape is CLR type dbo.Polygon): write one valid INSERT under
                     -- plants: dbo.T" · exit 5
laws:     H3
source:   expected · ARCH §6 planter-needs-row
note:     Whether a user assembly publishes to LocalDB at all (clr strict security) is decided earlier, at the
          base publish. If it does not publish, that is base-unpublishable, not this stop.
```

## Structure

### PLT-29 · IDENTITY with a non-default seed and increment: the engine numbers the row
```case
kind:     sql
env:      pg + T (Id int IDENTITY(1000,-5) PK · c int NOT NULL) · T 7 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · Id=1000 (engine) · c=1 · no SET IDENTITY_INSERT in the plant trace
laws:     F1 D1
source:   expected · ARCH §5 step 2
note:     Presence never sets an identity value. Only the gapped witness does (PLT-82). A fresh restore makes
          the engine's value the same in every run.
```

### PLT-30 · computed columns, persisted or not: skipped
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a int NOT NULL · b AS (a * 2) · c AS (a + 1) PERSISTED) · T 2 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a=1 · b=2 · c=2 (engine) · b and c absent from the INSERT column list
laws:     F1
source:   expected · ARCH §5 step 2
```

### PLT-31 · a persisted computed column that fails on the literal: needs a declared row
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Code nvarchar(10) NOT NULL · CodeNum AS CAST(Code AS int) PERSISTED) · T 3 rows
action:   plant · questions HasRows(T)
expect:   outcome    stop planter-needs-row · "dbo.T (computed CodeNum fails on the planted literal, Msg 245): write
                     one valid INSERT under -- plants: dbo.T" · exit 5
laws:     H3 F1
source:   expected · ARCH §6 planter-needs-row
note:     This differs from the computed-witness stop, which is for a computed column the delta adds
          (ARCH §4 Computed). Here the column already exists, and the planter's own row breaks it.
```

### PLT-32 · literal and function defaults: the planter's literal is written
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Status nvarchar(20) NOT NULL DF N'' · Created datetime2 NOT NULL DF SYSUTCDATETIME()
          · Ref uniqueidentifier NOT NULL DF NEWSEQUENTIALID() · Flag bit NOT NULL DF 1) · T 2 rows
action:   plant · questions HasRows(T) · twice, on two fresh restores
expect:   copy       T 1 row · Status=N'w1' · Created='2000-01-02 00:00:00' · Ref='77777777-0000-4000-8000-000000000001'
                     · Flag=1 · identical in both runs
laws:     D1 D3 F1
source:   expected · INVARIANTS D1
note:     decision: every writable column gets its literal. A function default would break D1, and a literal
          default can be `''`, which a Fits class reads (PLT-94). Sequence defaults are the exception (PLT-33).
```

### PLT-33 · NEXT VALUE FOR a sequence: omitted, so the default fires
```case
kind:     sql
env:      pg + SEQUENCE dbo.OrderNo START WITH 5000 + T (Id int IDENTITY PK · No int NOT NULL DF NEXT VALUE FOR dbo.OrderNo
          · c int NOT NULL) · T 2 rows
action:   plant · questions HasRows(T) · twice, on two fresh restores
expect:   copy       T 1 row · No=5000 (the restored sequence's next value) · c=1 · identical in both runs
laws:     D1 F1
source:   expected · ARCH §5 step 1 (every proof restores the same base)
note:     decision: if No were planted as a literal, it could later collide with a value the sequence gives a
          post-deploy insert during the proof. The environment would never see that 2627.
```

### PLT-34 · a temporal table's current rows: period columns omitted
```case
kind:     sql
env:      pg + T (Id int PK · c nvarchar(20) NOT NULL · ValidFrom datetime2 GENERATED ALWAYS AS ROW START
          · ValidTo datetime2 GENERATED ALWAYS AS ROW END · PERIOD FOR SYSTEM_TIME) SYSTEM_VERSIONING = ON
          (HISTORY_TABLE dbo.T_History) · T 4 rows · T_History 0 rows
action:   plant · questions HasRows(T)
expect:   answers    HasRows(T)=yes
          copy       T 1 row · Id=1 · c=N'w1' · ValidFrom engine-assigned · ValidTo 9999-12-31 23:59:59.9999999
                     · T_History 0 rows
laws:     F1 D1
source:   expected · skills/op/temporal-new (GENERATED ALWAYS period columns are engine-maintained)
note:     Period columns are outside D3 and inside D1 (INVARIANTS D3). ValidFrom is the clock at planting, so D1 can only
          compare it as it compares the timestamp field; D3 does not say how (reported).
```

### PLT-35 · temporal history is planted by UPDATE, never by INSERT
```case
kind:     sql
env:      as PLT-34 · T_History 9 rows · Fits(T.c→nvarchar(5))={overlen} in T_History only
action:   plant · questions HasRows(T) · HasRows(T_History) · Fits(T_History.c→nvarchar(5))
expect:   copy       T row 1 inserted as c=N'w1wwww' (the misfit), then UPDATE T SET c=N'w1' → T_History holds
                     the misfit version, T holds the clean one · T 1 row · T_History 1 row
          answers    the copy reads HasRows(T_History)=yes and the misfit in T_History, not in T
laws:     F1 M2
source:   verify · Does an UPDATE in the same transaction as the INSERT write a history row (start = end) that
          the probes count? If it does not, the two statements commit separately. The first run decides.
note:     A history table refuses direct INSERT while SYSTEM_VERSIONING is ON. Switching versioning off would be
          DDL, which the identity check would have to see restored.
```

### PLT-36 · sparse columns and a column set: write the sparse columns by name
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a nvarchar(20) SPARSE NULL · b int SPARSE NULL
          · Props xml COLUMN_SET FOR ALL_SPARSE_COLUMNS) · T 2 rows
action:   plant · questions HasRows(T)
expect:   copy       T 1 row · a=N'w1' · b=1 · Props not in the INSERT column list (it shows <a>w1</a><b>1</b>)
laws:     F1 F2
source:   expected · Microsoft doc "Use column sets" (insert either the column set or its sparse columns, never both)
note:     catalog.sql reads is_column_set and is_sparse. Sparse columns are nullable, so they get values like
          any nullable column.
```

### PLT-37 · a case-sensitive collation column keeps its collation and its literal
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Code nvarchar(20) COLLATE Latin1_General_CS_AS NOT NULL) · T 3 rows
          · database default SQL_Latin1_General_CP1_CI_AS
action:   plant · questions HasRows(T) · then identity
expect:   copy       T 1 row · Code=N'w1' · Code's collation Latin1_General_CS_AS · identity plan empty
laws:     F9 F2
source:   expected · INVARIANTS F9 (column collation)
note:     The literal is lower-case. The only upper-case value the planter writes is a case-class witness
          (PLT-70).
```

### PLT-38 · non-dbo schemas and names that need quoting
```case
kind:     sql
env:      pg + SCHEMA [sales] + [sales].[Order Line] (Id int IDENTITY PK · [Unit]]Price] decimal(18,2) NOT NULL
          · OrderId int NOT NULL FK → [sales].[Order].Id) · [sales].[Order Line] 4 rows
action:   plant · questions HasRows([sales].[Order Line])
expect:   copy       [sales].[Order] 1 row (ancestor) · [sales].[Order Line] 1 row · [Unit]]Price]=1.00
          plan       every identifier in the plant trace passes through QUOTENAME; no name is unqualified
laws:     F1
source:   expected · ARCH §5 step 2
```

### PLT-39 · views are never planted, and stay published
```case
kind:     sql
env:      pg + VIEW dbo.vCustomer AS SELECT Id, Name FROM dbo.Customer + VIEW dbo.vOrderTotals WITH SCHEMABINDING
          + its clustered index · Customer 5 rows · Order 4 rows
action:   plant · questions HasRows(Customer) · HasRows(Order) · then identity
expect:   copy       Customer 1 row · Order 1 row · no INSERT targets a view · vOrderTotals holds 1 row (engine-maintained)
                     · identity plan empty, both views present
laws:     F2 T1
source:   expected · THE_TWIN.md §6 (views excluded from the mint but still published; ARCH §2 row 10)
```

### PLT-40 · unique keys on columns no question names: the ordinal keeps witness rows distinct
```case
kind:     sql
env:      pg + UNIQUE INDEX UX_Product_Name ON Product(Name) · UNIQUE INDEX UX_Product_Code_Filt ON Product(Code)
          WHERE Code IS NOT NULL · Product 5 rows · one Code over 10 by LEN · one over 10 by trailing spaces only
action:   plant · questions HasRows(Product) · Fits(Product.Code→nvarchar(10))
expect:   answers    Fits(Product.Code→nvarchar(10))={overlen, overbytes}
          copy       Product 2 rows · row 1 Name=N'w1' · Code=N'w1wwwwwwwww' · row 2 Name=N'w2' · Code=N'w2' + 9 spaces
                     · no 2601 or 2627 while planting
laws:     F1 M2 D3
source:   expected · ARCH §5 steps 2–3
note:     Two classes on one column need two rows (M2). Each row varies only its own class cell and keeps its ordinal
          everywhere else, so every other unique key stays distinct.
```

### PLT-41 · a non-IDENTITY key in a table that already holds rows: MAX(key) + n
```case
kind:     sql
env:      pg + dbo.Kind (Id int PK, no IDENTITY · Label nvarchar(20) NULL) · the merge-base referenceData lane seeds
          Kind 1–3 · Kind in env 3 rows · Label NULL×1
action:   plant · questions HasRows(Kind) · NotNull(Kind.Label)
expect:   plan       null(Kind.Label) · presence already answered by the lane rows
          copy       Kind 4 rows · lane rows 1–3 unchanged · witness row Id=4 (MAX 3 + n=1) · Label NULL
laws:     F1 M2 D1
source:   expected · ARCH §5 step 2 (reuse existing rows); INVARIANTS D3 (keys resolved from the copy)
note:     A key resolved from the copy is outside D3 but inside D1: the same restore gives the same MAX, so every run plants
          the same Id. Without it, Id=1 would collide with lane row 1 (Msg 2627).
```

### PLT-42 · a unique column too narrow for the planted rows: needs a declared row
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Flag char(1) NOT NULL UNIQUE · Note nvarchar(50) NULL) · T 3 rows · Note NULL×2
action:   plant · questions HasRows(T) · Unique(T,(Note)) (head adds a plain unique index on Note)
expect:   plan       dup(T,(Note),nulls): two rows
          outcome    stop planter-needs-row · "Cannot make a valid row for dbo.T (Flag char(1) is unique and has room for one
                     planted value). Write the rows into witness.sql under -- plants: dbo.T." · exit 5
laws:     H3 M2
source:   expected · ARCH §6 planter-needs-row
note:     The second row's Flag is also 'w', so the insert fails 2627, which the planter turns into this stop, naming the
          constraint. bit has room for two planted values, tinyint 256 and decimal(1,0) ten.
```

### PLT-43 · a CHECK the literal satisfies
```case
kind:     sql
env:      pg + CK_Order_Total CHECK (Total >= 0) + CK_Order_StatusText CHECK (LEN(StatusText) > 0) · Order 4 rows
action:   plant · questions HasRows(Order) · then identity
expect:   copy       Order 1 row · Total=1.00 · StatusText=N'w1' · both CKs enabled and trusted after planting
                     · identity plan empty
laws:     F1 F9
source:   expected · THE_TWIN.md §9 (non-negative numerics satisfy the common >= 0 class)
note:     Positive ordinals and non-empty strings satisfy the most common predicates. How often the estate's
          real CHECKs stop the planter is PLT-122 (ARCH §11.7).
```

### PLT-44 · a CHECK the literal violates: needs a declared row naming the CK
```case
kind:     sql
env:      pg + CK_Product_Code CHECK (Code LIKE 'ABC%') · Product 5 rows (every Code starts ABC)
action:   plant · questions HasRows(Product)
expect:   outcome    stop planter-needs-row · "dbo.Product (CK_Product_Code rejects the planted literal): write one
                     valid INSERT under -- plants: dbo.Product" · exit 5
laws:     H3 F1
source:   expected · ARCH §6 planter-needs-row
note:     decision: the planter never parses a predicate. It inserts, turns a Msg 547 naming a CHECK into this
          stop, and never disables a CHECK to plant a presence row, because that row must be valid.
```

### PLT-45 · triggers, AFTER or INSTEAD OF, do not fire while planting
```case
kind:     sql
env:      pg + T with AFTER INSERT trg_T_Audit (writes dbo.AuditLog) and trg_T_Old (disabled in env)
          + U with INSTEAD OF INSERT trg_U_Route · T 3 rows · U 2 rows · AuditLog 0 rows
action:   plant · questions HasRows(T) · HasRows(U) · HasRows(AuditLog) · then identity
expect:   answers    HasRows(T)=yes · HasRows(U)=yes · HasRows(AuditLog)=no
          copy       T 1 row · U 1 row · AuditLog 0 rows · trg_T_Audit and trg_U_Route enabled again · trg_T_Old still
                     disabled · identity plan empty
laws:     F1 F2 F9
source:   expected · ARCH §5 step 2 (decision)
note:     decision: a planted row stands for a row that already exists, so the side effects of its past insert
          are measured through the answers of the tables they wrote to. witness.sql runs as written, with triggers firing.
```

### PLT-46 · a memory-optimized table on LocalDB
```case
kind:     sql
env:      pg + MEMORY_OPTIMIZED_DATA filegroup + T (Id int IDENTITY PK NONCLUSTERED · c nvarchar(20) NOT NULL)
          WITH (MEMORY_OPTIMIZED = ON, DURABILITY = SCHEMA_AND_DATA) · T 3 rows
action:   base publish to LocalDB 2022 · plant · questions HasRows(T)
expect:   copy       T 1 row · c=N'w1'
          outcome    or, if LocalDB refuses the filegroup: stop base-unpublishable · "(memory-optimized tables): record
                     Not checked" · exit 9
laws:     F1 F2
source:   verify · Does LocalDB host In-Memory OLTP through a publish? The documentation does not settle it. The
          first Windows CI run decides which of the two outcomes holds.
```

### PLT-47 · a columnstore index and a partitioned table on LocalDB
```case
kind:     sql
env:      pg + T (…) with a nonclustered columnstore index + U partitioned RANGE RIGHT on a date column
          (partition scheme ALL TO ([PRIMARY])) · T 2 rows · U 2 rows
action:   base publish to LocalDB 2022 · plant · questions HasRows(T) · HasRows(U)
expect:   copy       T 1 row · U 1 row, in the partition that '2000-01-02' selects · identity plan empty
laws:     F1 F2
source:   verify · Both features are in every edition since SQL Server 2016 SP1. Untested: LocalDB through a
          publish, and a scheme spread over several filegroups, which a LocalDB publish must create.
```

### PLT-48 · FILESTREAM: the base does not publish
```case
kind:     sql
env:      pg + FILESTREAM filegroup + T (Id uniqueidentifier ROWGUIDCOL NOT NULL UNIQUE · Doc varbinary(max) FILESTREAM NULL) · T 2 rows
action:   prove
expect:   outcome    stop base-unpublishable · "<env>'s schema does not publish to LocalDB (FILESTREAM): record it as
                     Not checked" · exit 9
laws:     H3 H1
source:   expected · Microsoft doc "SQL Server Express LocalDB" (LocalDB does not support FILESTREAM)
note:     The stop comes at the base publish (ARCH §5 step 1), before the planter runs.
```

## Ancestors and cycles

### PLT-49 · single level: a NOT NULL FK gets one ancestor
```case
kind:     sql
env:      pg + FK_CustomerAddress_Customer (CustomerId NOT NULL → Customer.Id) · CustomerAddress 5 rows · Customer 5 rows
action:   plant · questions HasRows(CustomerAddress)
expect:   witnesses  presence(CustomerAddress) (+ancestors Customer)
          copy       Customer 1 row (ordinal 1) · CustomerAddress 1 row · CustomerId = that Customer's Id, read back
laws:     F1 M2
source:   expected · ARCH §5 step 2
note:     The child's key value comes from the inserted parent (OUTPUT), never from n, so an IDENTITY parent
          with any seed resolves.
```

### PLT-50 · a multi-level chain, with a lookup reused at the top
```case
kind:     sql
env:      pg + FKs OrderLine.OrderId → Order.Id · Order.CustomerId → Customer.Id · Order.StatusId → Status.Id (all NOT NULL)
          · OrderLine 9 rows · Status 1–3 from the referenceData lane
action:   plant · questions HasRows(OrderLine)
expect:   witnesses  presence(OrderLine) (+ancestors Order, Customer; Status 1 reused)
          copy       Customer 1 · Order 1 (CustomerId → it · StatusId=1) · OrderLine 1 · Status still its 3 lane rows
laws:     F1 M2
source:   expected · ARCH §5 step 2
note:     Ancestors are resolved transitively, parents first, until a table already holds a row. The lowest
          existing key is the one reused, so the choice is deterministic.
```

### PLT-51 · a composite FK: both columns from one parent row
```case
kind:     sql
env:      pg + Shipment (OrderId int · LineNumber int · PK (OrderId, LineNumber)) + ShipmentNote (Id int IDENTITY PK
          · OrderId int NOT NULL · LineNumber int NOT NULL · FK (OrderId, LineNumber) → Shipment) · ShipmentNote 3 rows
action:   plant · questions HasRows(ShipmentNote)
expect:   copy       Shipment 1 row (1, 1) · ShipmentNote 1 row (OrderId 1, LineNumber 1)
laws:     F1 M2
source:   expected · ARCH §5 step 2
note:     The FK's columns are copied together from one parent row. They are never minted independently, which
          could pair keys that exist separately but not as a pair.
```

### PLT-52 · a nullable FK: NULL, and no ancestor
```case
kind:     sql
env:      pg + FK_Customer_Account (AccountId NULL → Account.Id) · Customer 5 rows · Account 3 rows
action:   plant · questions HasRows(Customer)
expect:   copy       Customer 1 row · AccountId NULL · Account 0 rows
laws:     F1 M2
source:   expected · ARCH §5 step 2 (nullable FK columns are the exception and get NULL)
note:     A NULL passes MATCH SIMPLE, so it needs no parent. A nullable FK column is the only nullable column
          the planter leaves NULL (with PLT-22's unmintable types).
```

### PLT-125 · a nullable FK column with an Orphan question is linked, not left NULL
```case
kind:     corpus
env:      pg · Customer 5 rows, every AccountId resolves · Account 3 rows
change:   head adds FK_Customer_Account (Customer.AccountId → Account.Id)
expect:   answers    Orphan(Customer.(AccountId)→Account)=clean · Rows(Customer)=5
          witnesses  presence(Customer) (+link Account: a planted parent row)
          copy       Account 1 row · Customer 1 row · AccountId = that Account's Id, not NULL
          outcome    verdict clean · exit 0
laws:     F1 F3 M2
source:   expected · ARCH §5 step 2 (every Orphan question links the child, even before the FK exists; nullable FK columns
          with no Orphan question get NULL); F9 (a declarative FK add over clean data lands trusted)
note:     A NULL would pass MATCH SIMPLE as well, but then the copy's WITH CHECK CHECK would validate no real child. A
          populated, linked child is the state Dev has. Without an Orphan question the column stays NULL (PLT-52).
```

### PLT-53 · an FK to a reference-data lookup: reuse, never plant over
```case
kind:     sql
env:      pg with Product.CategoryId NOT NULL + FK_Product_Category → Category.Id · Category 1–3 from the
          referenceData lane · Product 5 rows
action:   plant · questions HasRows(Product) · hash Category before and after
expect:   copy       Product 1 row · CategoryId=1 · Category exactly its 3 lane rows · Category hash unchanged
laws:     F1 M2
source:   expected · ARCH §5 step 2 (an ancestor reuses an existing row before planting a new one)
note:     The planter never UPDATEs or DELETEs a row it did not insert.
```

### PLT-54 · a lookup the lanes leave empty: the ancestor is planted
```case
kind:     sql
env:      as PLT-53 · facsimile.json has no referenceData lane for Category
action:   plant · questions HasRows(Product)
expect:   witnesses  presence(Product) (+ancestors Category)
          copy       Category 1 row (Id=1 · Code=N'w1' · IsActive=1) · Product 1 row · CategoryId=1
laws:     F1 M2
source:   expected · ARCH §5 step 2
```

### PLT-55 · a parent that is touched too: one row serves both
```case
kind:     sql
env:      pg + FK_Order_Customer (CustomerId NOT NULL) · Order 4 rows · Customer 5 rows
action:   plant · questions HasRows(Order) · HasRows(Customer)
expect:   witnesses  presence(Customer) · presence(Order)
          copy       Customer 1 row · Order 1 row pointing at it · 2 rows in all, not 3
laws:     M2 F1
source:   expected · INVARIANTS M2
```

### PLT-56 · an FK the change adds is linked like an existing one
```case
kind:     corpus
env:      pg (no FKs) · Order 4 rows, every CustomerId resolves · Customer 5 rows
change:   head adds FK_Order_Customer (Order.CustomerId → Customer.Id)
expect:   answers    Orphan(Order.(CustomerId)→Customer)=clean · Rows(Order)=4
          witnesses  presence(Order) (+link Customer: a planted parent row)
          outcome    verdict clean · exit 0
laws:     F1 F3 M2
source:   expected · ARCH §5 step 2 (every Orphan question links the child, even though the FK does not exist yet in the
          base); F9 (a declarative FK add is WITH NOCHECK ADD + WITH CHECK CHECK, and lands trusted over clean data)
note:     The copy has no FK yet, so its catalog alone would leave Order.CustomerId=1 pointing nowhere: an accidental orphan
          the environment does not have. The plan takes the link from the Orphan question instead.
```

### PLT-57 · a NOT NULL self-reference: needs a declared row
```case
kind:     sql
env:      pg + Employee (Id int IDENTITY PK · ManagerId int NOT NULL FK_Employee_Manager → Employee.Id) · Employee 12 rows
action:   plant · questions HasRows(Employee)
expect:   outcome    stop planter-needs-row · "dbo.Employee (NOT NULL self-reference FK_Employee_Manager): write one
                     valid INSERT under -- plants: dbo.Employee" · exit 5
laws:     H3
source:   expected · ARCH §6 planter-needs-row
note:     decision: a row that points at itself is a domain claim (the root of the tree), so the planter does
          not invent it. One IDENTITY_INSERT row with Id=1 and ManagerId=1 clears the stop (FKs are checked per statement).
```

### PLT-58 · a two-table NOT NULL cycle: needs declared rows, and the stop names the cycle
```case
kind:     sql
env:      pg + Dept (Id int PK · HeadId int NOT NULL FK → Person.Id) + Person (Id int PK · DeptId int NOT NULL FK → Dept.Id)
          · Dept 3 rows · Person 8 rows
action:   plant · questions HasRows(Person)
expect:   outcome    stop planter-needs-row · "Cannot make a valid row for dbo.Person (NOT NULL FK cycle dbo.Person → dbo.Dept
                     → dbo.Person). Write the rows into witness.sql under -- plants: dbo.Dept and -- plants: dbo.Person." · exit 5
laws:     H3 A5
source:   expected · ARCH §6 planter-needs-row
note:     A cycle through a nullable column is not a cycle here, because that edge gets NULL. DML alone cannot insert a NOT
          NULL cycle, so the declared blocks run inside the planter's suspend-and-restore envelope (PLT-85).
```

### PLT-59 · a parent the environment answered empty, under an untrusted FK: the child is planted as an orphan
```case
kind:     sql
env:      pg + FK_Order_Customer (NOT NULL) enabled, is_not_trusted=1 · Order 4 rows, every CustomerId absent · Customer 0 rows
action:   plant · questions HasRows(Order) · HasRows(Customer) · then identity
expect:   answers    HasRows(Order)=yes · HasRows(Customer)=no · Trust(Order,FK_Order_Customer)=untrusted
          copy       Customer 0 rows · Order 1 row · CustomerId=1000 (empty parent: MAX 0 + 1000) · FK restored enabled and
                     untrusted · identity plan empty
laws:     F1 F9
source:   expected · ARCH §5 step 4 (suspend to plant, then restore the environment's trust state)
note:     The answers win over the ancestor rule. An enforced-but-untrusted FK lets the environment hold this state, so the
          planter suspends the FK, inserts, and restores it without WITH CHECK.
```

### PLT-60 · a parent answered empty under a trusted FK: the answers contradict the schema
```case
kind:     sql
env:      pg + FK_Order_Customer (NOT NULL) trusted · answers HasRows(Order)=yes · HasRows(Customer)=no
action:   plant · questions HasRows(Order) · HasRows(Customer)
expect:   outcome    stop answers-invalid · "answers.<env>.json does not validate (HasRows(dbo.Customer)=no contradicts trusted
                     FK_Order_Customer while dbo.Order holds rows). Re-run facsimile read <env>." · exit 5
          copy       nothing planted
laws:     F1 H3 A5
source:   expected · ARCH §6 answers-invalid (decision); ARCH §11.2 (sys.partitions counts are approximate)
note:     decision: a trusted FK makes this state impossible, so one count is wrong, most likely a stale sys.partitions row
          count. Planting the ancestor would end in an acceptance stop whose next step (fix witness.sql) cannot help; a
          re-read can (A5).
```

## Violation witnesses by class

### PLT-61 · null: the witness is a full valid row with one NULL, and it is the presence row too
```case
kind:     sql
env:      pg · Customer 5 rows · Email NULL×2
action:   plant · questions HasRows(Customer) · NotNull(Customer.Email)
expect:   answers    HasRows(Customer)=yes · NotNull(Customer.Email)=violating
          witnesses  null(Customer.Email), which also answers HasRows(Customer)
          copy       Customer 1 row · Email NULL · Name=N'w1' · ContactPhone=N'w1' · Region=N'w1' · AccountId=1
laws:     F1 M2 P5 D3
source:   expected · ARCH §4 NotNull; ARCH §5 step 2 (a violation row also counts as presence)
note:     A witness row is a full valid row at its own ordinal, except for the violating cell. pg has no FKs, so AccountId
          is an ordinary nullable int here. Deleting the row breaks both answers (PLT-107).
```

### PLT-62 · misfit overlen: one character past the target, by LEN
```case
kind:     sql
env:      pg · Product 5 rows · Code max LEN 16
action:   plant · questions HasRows(Product) · Fits(Product.Code→nvarchar(10))
expect:   answers    Fits(Product.Code→nvarchar(10))={overlen}
          witnesses  misfit(Product.Code, overlen), which also answers HasRows(Product)
          copy       Product 1 row · Code=N'w1wwwwwwwww' (LEN 11, no trailing space)
laws:     F1 M2
source:   expected · F1 (the narrowing on a populated table: the guard fires first under Strict)
note:     The value is `w<n>` padded with `w` to the target width + 1. Under Strict the guard decides. The misfit decides
          Permissive (2628) and the R1 of a program (INVARIANTS F8).
```

### PLT-63 · misfit overbytes: over by trailing spaces only
```case
kind:     sql
env:      pg · Product 5 rows · one Code is 'A100' + 12 trailing spaces
action:   plant · questions HasRows(Product) · Fits(Product.Code→nvarchar(10))
expect:   answers    Fits(Product.Code→nvarchar(10))={overbytes}
          copy       Product 1 row · Code=N'w1' + 9 spaces (11 characters, LEN 2)
laws:     F1 M2
source:   expected · red-team #10 (a narrowing silently trims trailing spaces, verified)
note:     LEN ignores trailing spaces, so this row is not overlen. The narrowing trims it without an error, and only the
          before/after hashes show the change (TWN-151).
```

### PLT-64 · misfit nonconvertible
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · c nvarchar(20) NOT NULL) · T 4 rows · one c is 'n/a'
action:   plant · questions HasRows(T) · Fits(T.c→int)
expect:   answers    Fits(T.c→int)={nonconvertible}
          copy       T 1 row · c=N'w1' (TRY_CONVERT(int, N'w1') IS NULL), which also answers HasRows(T)
laws:     F1 P5 M2
source:   expected · ARCH §4 Fits
```

### PLT-65 · misfit roundtrip: a day-first date that converts month-first under the pinned date format
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Born varchar(10) NOT NULL) · T 6 rows · formats 99/99/9999 and 9999-99-99
action:   plant · questions HasRows(T) · Fits(T.Born→date)
expect:   answers    Fits(T.Born→date)={roundtrip}
          trace      probes.sql and plant.sql both run under SET LANGUAGE us_english; SET DATEFORMAT ymd
          copy       T 1 row · Born='02/01/2000' (meant 2 January; under ymd the four-digit part is the year and the other
                     two read month-first, so it converts to 2000-02-01)
laws:     F1 P5
source:   expected · TWIN_SUCCESSION §6b (a slash date converting "successfully" month-first); Microsoft doc "datetime
          (Transact-SQL)" (in a numeric date, a four-digit part is the year)
note:     The witness must convert, so the marker cannot apply: the value is the per-class table's day-first date (P5). P5's
          example 13/04/2001 reads nonconvertible under ymd (month 13), so this class needs a day of 12 or less (reported).
```

### PLT-123 · the date-format pin: probes and planter agree whatever the logins' defaults
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Born varchar(10) NOT NULL) · T 6 rows · one Born '02/01/2000' · the environment's
          reader defaults to us_english · the copy's login defaults to LANGUAGE british (dmy)
action:   read (the Fits(T.Born→date) probe) · plant · accept
expect:   answers    the environment and the copy both read Fits(T.Born→date)={roundtrip}
          trace      every probe batch and the plant session begin SET LANGUAGE us_english; SET DATEFORMAT ymd
          record     notChecked ∋ platform:dateformat when the captured options do not state the Octopus login's language
                     and date format
laws:     F1 H4 D1
source:   expected · ARCH §5 step 2 (the pin, and the platform:dateformat residual)
note:     Without the pin, the copy's british session would read '02/01/2000' as 2 January while the environment's probe read
          1 February, and acceptance would compare two different questions.
```

### PLT-66 · misfit roundtrip: scale beyond the target
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Rate decimal(18,4) NOT NULL) · T 5 rows · one Rate 2.3456
action:   plant · questions HasRows(T) · Fits(T.Rate→decimal(18,2))
expect:   answers    Fits(T.Rate→decimal(18,2))={roundtrip}
          copy       T 1 row · Rate=1.0001 (rounds to 1.00 with no error)
laws:     F1
source:   expected · TWIN_SUCCESSION §5 (a lossy result shows only in the hash)
note:     The witness is n plus one unit in the last source scale digit, which the target scale cannot hold.
```

### PLT-67 · misfit empty: `''` toward a non-string type
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Qty nvarchar(10) NOT NULL) · T 4 rows · Qty ''×1
action:   plant · questions HasRows(T) · Fits(T.Qty→int)
expect:   answers    Fits(T.Qty→int)={empty}
          copy       T 1 row · Qty=N''
laws:     F1 P5
source:   expected · red-team #10 (TRY_CONVERT('') gives 0 for int and 1900-01-01 for date, NULL for decimal and GUID; verified)
note:     The witness is `''` whatever the target type, the per-class table's entry for this class (P5). The class exists
          because the outcome differs by target.
```

### PLT-68 · misfit overflow: the smallest value past the target
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Total decimal(18,2) NOT NULL · Qty int NOT NULL) · T 3 rows
          · one Total 12000.00 · one Qty 40000
action:   plant · questions HasRows(T) · Fits(T.Total→decimal(5,2)) · Fits(T.Qty→smallint)
expect:   answers    Fits(T.Total→decimal(5,2))={overflow} · Fits(T.Qty→smallint)={overflow}
          copy       T 1 row · Total=1000.00 · Qty=32768
laws:     F1 M2
source:   expected · ARCH §4 Fits; INVARIANTS M2
note:     The value is 10^(p−s) for a decimal target and the target's maximum + 1 for an integer target. The two classes
          sit on different columns, so one row carries both (M2: the maximum of the needs).
```

### PLT-69 · dup exact: two rows equal on the key
```case
kind:     sql
env:      pg · Product 5 rows · Code 'DUPE'×2
action:   plant · questions HasRows(Product) · Unique(Product,(Code))
expect:   answers    Unique(Product,(Code))={exact}
          witnesses  dup(Product,(Code),exact): two rows, which also answer HasRows(Product)
          copy       Product 2 rows · row 1 Code=N'w1' · Name=N'w1' · row 2 Code=N'w1' · Name=N'w2' · LegacyCode=N'w2'
laws:     F1 M2
source:   expected · ARCH §4 Unique
note:     For a composite key, every key column is equal across the pair. The pair is the presence, so no third row is
          planted (M2).
```

### PLT-70 · dup case: legal under the old collation, a duplicate under the new one
```case
kind:     corpus
env:      pg + T (Id int IDENTITY PK · Code nvarchar(20) COLLATE Latin1_General_CS_AS NOT NULL) + UNIQUE INDEX UX_T_Code
          · T 4 rows · Code 'abc' and 'ABC'
change:   head changes Code's collation to Latin1_General_CI_AS (UX_T_Code is dropped and rebuilt)
expect:   answers    Unique(T,(Code))={case}, counted under the head collation Latin1_General_CI_AS
          witnesses  dup(T,(Code),case) → the copy holds N'w1' and N'W1', legal under CS, two rows that also answer HasRows(T)
          outcome    verdict blocked unique · Msg 1505 · dbo.T.UX_T_Code · exit 3
laws:     F1 F3 F9
source:   expected · red-team #3 (a collation change on a unique column reads rows with no guard, verified); ARCH §4 Unique
          (duplicates counted under the collation the index will be built with)
note:     The case class can be violating only under a case-insensitive target collation. The copy keeps the
          environment's CS collation until head's publish changes it (F9).
```

### PLT-71 · dup accent: a circumflexed `w`
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · Name nvarchar(50) COLLATE Latin1_General_CI_AI NOT NULL) · T 3 rows · 'Jose' and 'José'
action:   plant · questions HasRows(T) · Unique(T,(Name))
expect:   answers    Unique(T,(Name))={accent}
          copy       row 1 Name=N'w1' · row 2 Name=N'ŵ1' (U+0175) · the copy's probe counts one accent group
laws:     F1 P5
source:   verify · Do N'ŵ1' and N'w1' compare equal under Latin1_General_CI_AI and SQL_Latin1_General_CP1_CI_AI?
          The first run decides. A varchar column has no accented `w` in code page 1252, so that variant stops planter-needs-row.
```

### PLT-72 · dup trailing: equal under every collation, different in bytes
```case
kind:     sql
env:      pg · Product 5 rows · 'B200' and 'B200 '
action:   plant · questions HasRows(Product) · Unique(Product,(Code))
expect:   answers    Unique(Product,(Code))={trailing}
          copy       row 1 Code=N'w1' · row 2 Code=N'w1 ' (one trailing space)
laws:     F1
source:   expected · ARCH §4 Unique
note:     Comparison pads with spaces, so the two values are equal even under BIN2. They are distinct in
          bytes, which keeps this class separate from exact.
```

### PLT-73 · dup nulls: two NULLs in a plain unique index
```case
kind:     corpus
env:      pg · Customer 5 rows · Email NULL×2
change:   head adds CREATE UNIQUE INDEX UX_Customer_Email ON Customer(Email)
expect:   answers    Unique(Customer,(Email))={nulls}
          witnesses  dup(Customer,(Email),nulls)
          plan       Customer 2 rows, both Email NULL; they also answer HasRows(Customer), so no separate presence row
          outcome    verdict blocked unique · Msg 1505 · dbo.Customer.UX_Customer_Email · exit 3
laws:     F1 F3 M2
source:   expected · F10 (a second NULL blocks a unique index with Msg 1505); INVARIANTS M2 (nulls needs two rows)
note:     Two NULLs are the duplicate, so the class needs two rows. A presence row with a value would add nothing.
```

### PLT-74 · a filtered unique index: NULLs must not count
```case
kind:     corpus
env:      pg · Customer 5 rows · Email NULL×2
change:   head adds CREATE UNIQUE INDEX UX_Customer_Email ON Customer(Email) WHERE Email IS NOT NULL
expect:   answers    Unique(Customer,(Email),Email IS NOT NULL)={}
          witnesses  presence(Customer)
          outcome    verdict clean · exit 0
laws:     F1 F3 M2
source:   expected · F10, skills/_index/constraint-is-a-claim (the filtered index is the remedy)
note:     The planter plants no NULL rows, and an extra NULL would change nothing here. M2 forbids it anyway.
```

### PLT-75 · orphan, integer key: MAX + 1000
```case
kind:     sql
env:      pg · Order 4 rows · CustomerId 999 has no Customer · Customer 5 rows
action:   plant · questions HasRows(Order) · Orphan(Order.(CustomerId)→Customer)
expect:   answers    Orphan(Order.(CustomerId)→Customer)=violating
          witnesses  orphan(Order.(CustomerId)→Customer), which also answers HasRows(Order)
          copy       Order 1 row · CustomerId=1000 (the copy's MAX(Customer.Id) 0 + 1000), checked absent · no Customer row
                     planted
laws:     F1 M2 D1
source:   expected · ARCH §4 Orphan; INVARIANTS D3 (orphan keys beyond the parent's maximum are resolved from the copy)
note:     The headroom stops a parent inserted by a post-deploy seed during the proof from adopting the orphan. If MAX + 1000
          overflows, the type's maximum is used when absent, else needs-row. The orphan row needs no parent, and Customer
          is not touched, so nothing is planted there.
```

### PLT-76 · orphan, uniqueidentifier key: a reserved absent value
```case
kind:     sql
env:      pg + P (Id uniqueidentifier PK DF NEWID()) + C (Id int IDENTITY PK · PRef uniqueidentifier NOT NULL) · C 3 rows, one orphan
action:   plant · questions HasRows(C) · Orphan(C.(PRef)→P) · twice, on two fresh restores
expect:   copy       C 1 row · PRef='FFFFFFFF-FFFF-4FFF-BFFF-000000000001', checked absent from P · identical in both runs
laws:     F1 D1
source:   expected · INVARIANTS D1
note:     "Fresh" means absent and deterministic. The orphan pattern differs from the presence pattern (PLT-19), so a
          planted parent can never satisfy it.
```

### PLT-77 · orphan, composite key: every column past the parents
```case
kind:     sql
env:      as PLT-51 with the FK not yet in env · ShipmentNote 3 rows, one pair absent from Shipment
action:   plant · questions HasRows(ShipmentNote) · Orphan(ShipmentNote.(OrderId, LineNumber)→Shipment)
expect:   copy       ShipmentNote 1 row (OrderId 1000, LineNumber 1000), both non-NULL · Shipment 0 rows
laws:     F1 M2
source:   expected · ARCH §4 Orphan (MATCH SIMPLE: a NULL in any FK column passes)
note:     A half-NULL row is never an orphan witness, because MATCH SIMPLE lets it pass. The orphan row also answers
          HasRows(ShipmentNote).
```

### PLT-78 · CheckFalse: always a declared row, so the plan stops first
```case
kind:     pure
input:    questions HasRows(Order) · CheckFalse(Order,CK_Order_Total) · answers HasRows(Order)=yes
          · CheckFalse(Order,CK_Order_Total)=violating · witness.sql absent
action:   Get-FxWitnessPlan, then the plant step's needs check
expect:   plan       presence(Order) · needs check(Order, CK_Order_Total)
          outcome    stop check-witness · "Write one row into witness.sql that makes CK_Order_Total FALSE, then
                     re-run." · exit 5
laws:     F1 H3 A5
source:   expected · ARCH §4 CheckFalse
```

### PLT-79 · CheckFalse discharged by a declared row
```case
kind:     corpus
env:      pg · Order 4 rows · one Total -5
change:   head adds CK_Order_Total CHECK (Total > 0) · witness.sql `-- witness: CheckFalse(Order,CK_Order_Total)` then
          INSERT dbo.[Order] (CustomerId, StatusId, Total, StatusText) VALUES (1, 1, -1, N'w9')
expect:   answers    CheckFalse(Order,CK_Order_Total)=violating · Rows(Order)=4
          witnesses  presence(Order) · declared(Order Total = -1), counted apart from the generated rows
          outcome    verdict blocked constraint · Msg 547 · CK_Order_Total · exit 3
laws:     F1 F3 A5 M2
source:   expected · F10 (add-check over Total = -5 blocks with Msg 547); ARCH §5 step 5 (a `-- witness:` block discharges
          the need it names)
note:     Acceptance then confirms that the copy's own probe reads violating, as the environment's did. For an existing
          untrusted CK, see PLT-89.
```

### PLT-80 · longkey, nonclustered: just over 1700 bytes
```case
kind:     corpus
env:      pg + Product.Notes nvarchar(1000) NULL · Product 5 rows · one Notes of 900 characters
change:   head adds CREATE INDEX IX_Product_Notes ON Product(Notes)
expect:   answers    KeyLength(Product,IX_Product_Notes)=violating
          witnesses  longkey(Product, IX_Product_Notes), which also answers HasRows(Product)
          plan       Product 1 row · Notes=N'w1' + 849 × 'w' (851 characters, 1702 bytes)
          outcome    verdict blocked keylength · Msg 1946 · IX_Product_Notes · exit 3
laws:     F1 F3 M2
source:   expected · red-team #16 (Msg 1945 warns at create time; 1946 fails on an over-limit key, verified)
note:     The value is the smallest key past the limit. For a multi-column key, the first column wide enough
          carries the excess, and the others keep their ordinal values.
```

### PLT-81 · longkey, clustered: just over 900 bytes
```case
kind:     corpus
env:      pg + T (Id int NOT NULL · Code varchar(1000) NOT NULL), a heap · T 3 rows · one Code of 950 characters
change:   head adds CREATE CLUSTERED INDEX CX_T_Code ON T(Code)
expect:   answers    KeyLength(T,CX_T_Code)=violating
          witnesses  longkey(T, CX_T_Code), which also answers HasRows(T)
          plan       T 1 row · Code='w1' + 899 × 'w' (901 bytes)
          outcome    verdict blocked keylength · Msg 1946 · CX_T_Code · exit 3
laws:     F1 F3
source:   expected · Microsoft doc "Maximum capacity specifications for SQL Server" (900-byte clustered key)
```

### PLT-82 · gapped identity: ids 2 and 5, not 1 and 2
```case
kind:     sql
env:      pg · Customer 3 rows · Ids 3, 7, 12 · change forces a tmp_ms_xx_Customer rebuild
action:   plant · questions HasRows(Customer) · Gapped(Customer) (from the Rebuild row)
expect:   answers    HasRows(Customer)=yes · Gapped(Customer)=yes
          witnesses  gapped(Customer)
          copy       Customer 2 rows · Id 2 and Id 5 (seed + 1×increment, seed + 4×increment, under IDENTITY_INSERT)
                     · no separate presence row · IDENT_CURRENT=5
laws:     F1 M2
source:   expected · TWIN_SUCCESSION §5 (identity-swap witnesses: gapped ids, not starting at 1); F12 (the rebuild keeps ids)
note:     If Gapped=no, presence alone is planted (Id 1). A contiguous 1..n witness cannot tell a renumbering
          rebuild from one that keeps ids, so "ids preserved" passes vacuously. The record says so.
```

### PLT-83 · declared blocks run in FK order, after the generated parents they reference
```case
kind:     sql
env:      pg + FK_Order_Customer (CustomerId NOT NULL) · Order 4 rows · Customer 5 rows · witness.sql `-- plants: dbo.[Order]`
          then INSERT dbo.[Order] (CustomerId, StatusId, Total, StatusText) VALUES (1, 2, 5.00, N'w-pinned')
action:   plant · questions HasRows(Order) · HasRows(Customer)
expect:   plan       trace: restore · suspend · presence(Customer) · the `-- plants: dbo.[Order]` block · restore trust state
                     · accept
          copy       Customer 1 row (Id 1) · Order exactly the declared row · Customer's values identical to the run
                     without witness.sql
laws:     F1 D3 D1 M2
source:   expected · ARCH §5 step 5 (the blocks run in FK order)
note:     The block owns Order's rows, so no generated Order row is planted, and the declared row must satisfy every answer
          for Order. Its CustomerId 1 names the generated parent, which is why it runs after Customer. Pins carry over the
          Twin's scenario pins (ARCH §2 row 17).
```

### PLT-84 · `-- plants: T` takes a table over entirely
```case
kind:     sql
env:      pg + CK_Product_Code CHECK (Code LIKE 'ABC%') · Product 5 rows · Name NULL×1
          · witness.sql: `-- plants: dbo.Product` then INSERT (Code, Name, LegacyCode)
          VALUES (N'ABCw1', N'w1', N'w1'), (N'ABCw2', NULL, N'w2')
action:   plant · questions HasRows(Product) · NotNull(Product.Name)
expect:   plan       presence(Product) and null(Product.Name) declared · nothing generated for Product
          copy       Product exactly the 2 declared rows · acceptance passes
laws:     F1 A5 M2 P5
source:   expected · ARCH §5 step 5 (the `-- plants:` header owns T's rows); ARCH §6 planter-needs-row
note:     The declared rows must satisfy every answer for Product, because acceptance checks them as it checks generated
          rows. `ABC` is copied from CK_Product_Code's own predicate and the rest carries the marker; decision:
          witness-not-synthetic accepts such a value (reported). This case clears PLT-44's stop.
```

### PLT-85 · a NOT NULL cycle's declared blocks run inside the envelope, in FK order
```case
kind:     sql
env:      as PLT-58 · witness.sql: `-- plants: dbo.Dept` then INSERT Dept (1, 1) · `-- plants: dbo.Person` then INSERT
          Person (1, 1) · + Badge (Id int IDENTITY PK · PersonId int NOT NULL FK → Person.Id) · Badge 6 rows
action:   plant · questions HasRows(Person) · HasRows(Badge) · then identity
expect:   copy       Dept 1 · Person 1 · Badge 1 pointing at Person 1 (the declared row is reused as its ancestor)
                     · both cycle FKs restored to their environment state (enabled, re-validated, trusted) · identity plan empty
laws:     F1 F2 F9
source:   expected · ARCH §5 step 5 (the envelope admits a NOT NULL cycle; the blocks run in FK order)
note:     The cycle has no FK order of its own: the envelope suspends both FKs, runs the two blocks, and restores them.
          Badge's generated row runs after the block that owns its parent.
```

### PLT-86 · several classes on one table: the maximum of the needs, in a canonical order
```case
kind:     sql
env:      pg · Product 5 rows · Name NULL×1 · one Code over 10 by LEN · one Code over 10 by trailing spaces only
action:   plant · questions HasRows(Product) · NotNull(Product.Name) · Fits(Product.Code→nvarchar(10))
expect:   answers    NotNull(Product.Name)=violating · Fits(Product.Code→nvarchar(10))={overlen, overbytes}
          copy       Product 2 rows · row 1 Name NULL and Code overlen · row 2 Code overbytes · no separate presence row
laws:     M2 D1 F1
source:   expected · INVARIANTS M2 (the maximum of a table's needs, not their sum)
note:     Classes on different columns share a row; two classes on one column cannot, so Code sets the count at 2. Row k
          takes the k-th class of each column in ARCH §4's row order, then table, column and class order, so ordinals
          never depend on how the questions arrived (PLT-104).
```

## Metadata witnesses

### PLT-87 · an untrusted FK: does Extract carry it, and does the copy keep it?
```case
kind:     sql
env:      pg + FK_Order_Customer added WITH NOCHECK (enabled, is_not_trusted=1) · Order 4 rows · one orphan
action:   read (Extract) · base publish · plant · questions HasRows(Order) · then identity
expect:   answers    Trust(Order,FK_Order_Customer)=untrusted
          copy       FK_Order_Customer enabled, is_not_trusted=1 after planting · identity plan empty
laws:     F9 F2
source:   verify · ARCH §11.1. If Extract drops WITH NOCHECK, the base publish makes the key trusted, and the planter
          adds trust(Order, FK_Order_Customer, untrusted): NOCHECK, then CHECK CONSTRAINT without WITH CHECK.
note:     The Twin's post-mint gate forced every constraint trusted (THE_TWIN.md §9). The copy reproduces the
          environment's state instead (ARCH §2 row 13). IgnoreWithNocheckOnForeignKeys suggests DacFx models it.
```

### PLT-88 · a disabled FK: does Extract carry it, and does the copy keep it?
```case
kind:     sql
env:      pg + FK_Order_Customer, then NOCHECK CONSTRAINT (is_disabled=1) · Order 4 rows
action:   read (Extract) · base publish · plant · questions HasRows(Order) · then identity
expect:   answers    Trust(Order,FK_Order_Customer)=disabled
          copy       FK_Order_Customer disabled after planting · identity plan empty
laws:     F9 F2
source:   verify · ARCH §11.1. Red-team #1 shows a disabled FK changes the delta an environment receives
          (verified), but not that Extract preserves it. If Extract does not, the planter adds
          trust(Order, FK_Order_Customer, disabled).
```

### PLT-89 · a witness under an existing enforced constraint: suspend, plant, restore the environment's state
```case
kind:     sql
env:      pg + FK_Order_Customer enabled, is_not_trusted=1 · CK_Order_Total CHECK (Total >= 0) enabled, is_not_trusted=1
          · Order 4 rows · one orphan · one Total -5
action:   plant · questions HasRows(Order) · Orphan(Order.(CustomerId)→Customer) · CheckFalse(Order,CK_Order_Total)
          · witness.sql `-- witness: CheckFalse(Order,CK_Order_Total)` then INSERT Order Total = -1 · then identity
expect:   copy       the orphan row (also the presence row) and the declared row land · both constraints end enabled,
                     untrusted · identity plan empty
          answers    the copy's Orphan and CheckFalse read violating, as env
laws:     F1 F2 F9
source:   expected · ARCH §5 steps 4–5 (the suspend-and-restore envelope); sample-prs/toggle-trust.md (WITH CHECK CHECK over
          a violating row fails 547 and stays untrusted)
note:     The generated rows and witness.sql both run inside the envelope: each constraint they would trip is suspended,
          then restored to its environment state. DML alone could not insert these rows under an enforced constraint.
```

### PLT-90 · a system-named default: is `DF__…` preserved?
```case
kind:     sql
env:      pg + an unnamed inline default on Customer.Region (N'West') → DF__Customer__Regio__3B75D760, is_system_named=1
action:   read (Extract) · base publish · plant · questions HasRows(Customer) · then identity
expect:   copy       Customer.Region has a default with is_system_named=1 · identity plan empty
          copy       a modify-default on Region scripts the same delta against the copy as against env (F9)
laws:     F9 F2
source:   verify · DacFx models a system-named constraint as unnamed, so a publish mints a new DF__ name. F9 asks
          for equal names "or stop". Does sp_rename clear is_system_named? Does the delta address the default by name?
note:     If names must match and cannot be reproduced, the outcome is a stop, and ARCH §6 lists no stop for it
          (reported).
```

## Acceptance (F1)

### PLT-91 · every question is re-counted, including every "no"
```case
kind:     sql
env:      pg · Customer 5 rows · Email NULL×2 · Product 5 rows · Code 'DUPE'×2 · Order 4 rows, every Total ≥ 0
action:   plant · accept · questions HasRows ×3 · NotNull(Customer.Email) · Unique(Product,(Code))
          · CheckFalse(Order,CK_Order_Total: Total >= 0)
expect:   answers    env and copy agree cell by cell: NotNull violating · Unique {exact}, so case, accent, trailing and nulls
                     are all no · CheckFalse clean
          witnesses  null(Customer.Email) (also presence) · dup(Product,(Code),exact) (two rows, also presence) · presence(Order)
laws:     F1 M2
source:   expected · INVARIANTS F1
note:     A cell that reads "no" in env must read "no" in the copy. The copy must agree on what is absent as well as on
          what is present.
```

### PLT-92 · a missing witness stops acceptance
```case
kind:     sql
env:      as PLT-91
setup:    the plan's dup(Product,(Code),exact) item is deleted (the M1 mutation)
action:   plant · accept
expect:   outcome    stop acceptance · "The copy answers Unique(Product,(Code)) exact no, but <env> answered yes.
                     Fix witness.sql." · exit 5
laws:     F1 H2 H3 M1
source:   expected · ARCH §6 acceptance
```

### PLT-93 · an extra violating row stops acceptance
```case
kind:     sql
env:      pg · Customer 5 rows · no NULL Email
action:   plant · accept · questions HasRows(Customer) · NotNull(Customer.Email)
          · witness.sql `-- plants: dbo.Customer` then INSERT dbo.Customer (Name, Email) VALUES (N'w9', NULL)
expect:   answers    env NotNull(Customer.Email)=clean
          outcome    stop acceptance · "The copy answers NotNull(Customer.Email) yes, but <env> answered no. Fix
                     witness.sql." · exit 5
laws:     F1 H2 H3
source:   expected · ARCH §6 acceptance
```

### PLT-94 · a `''` default never becomes an empty-class witness
```case
kind:     corpus
env:      pg + T (Id int IDENTITY PK · Qty nvarchar(10) NOT NULL DF N'') · T 4 rows · every Qty a digit string
change:   head retypes Qty to int
expect:   answers    HasRows(T)=yes · Fits(T.Qty→int)={}
          witnesses  presence(T), with Qty=N'1' (neither '' from the default nor 'w1')
          outcome    verdict blocked guard · Msg 50000/127 · dbo.T · exit 3
laws:     F1 F3
source:   expected · F1 (a retype on a populated table is refused by the guard; cf. F12 retype-explicit)
note:     If the default had fired, `''` would read empty-class violating (it converts to 0). The guard decides
          Strict either way. Permissive and R1 would then wrongly show the conversion.
```

### PLT-95 · the presence literal fits every Fits target on its column
```case
kind:     sql
env:      pg + T (Id int IDENTITY PK · a nvarchar(20) NOT NULL · b nvarchar(20) NOT NULL · c nvarchar(40) NOT NULL
          · d nvarchar(50) NOT NULL) · T 3 rows, all clean
action:   plant · accept · questions HasRows(T) · Fits(T.a→int) · Fits(T.b→date) · Fits(T.c→uniqueidentifier)
          · Fits(T.d→nvarchar(1))
expect:   answers    every Fits={} in env and in the copy
          copy       a=N'1' · b=N'2000-01-02' · c=N'77777777-0000-4000-8000-000000000001' · d=N'w'
laws:     F1 D3 P5
source:   expected · INVARIANTS F1, P5 (the marker applies where the type, the width and the class allow it)
note:     A presence row under a Fits question must convert, so its class does not allow the marker: the column gets the
          target type's literal as text, fitted to the narrower width. D3's signature reads type as the current type
          plus every target (reported).
```

### PLT-96 · a presence row that a CK being added would make FALSE: needs a declared row naming the CK
```case
kind:     sql
env:      pg · Product 5 rows, every Code starts ABC
action:   plant · accept · questions HasRows(Product) · CheckFalse(Product,CK_Product_Code: Code LIKE 'ABC%')
expect:   answers    env CheckFalse(Product,CK_Product_Code)=clean
          outcome    stop planter-needs-row · "dbo.Product (CK_Product_Code, added by the change, is FALSE for the planted
                     literal): write one valid INSERT under -- plants: dbo.Product" · exit 5
laws:     F1 H3 A5
source:   expected · ARCH §6 planter-needs-row (decision)
note:     The CK is not in the copy yet, so the insert succeeds. Acceptance then finds only generated rows
          violating, and reports needs-row because the remedy is a valid row, not a fix to witness.sql.
```

### PLT-97 · rows already in the copy can answer a question: that witness is skipped
```case
kind:     sql
env:      pg · the merge-base Status lane includes N'Cancelled' (9 characters) · env Status the same 3 rows
action:   plant · accept · questions HasRows(Status) · Fits(Status.Code→nvarchar(8))
expect:   answers    Fits(Status.Code→nvarchar(8))={overlen} in env and in the copy
          plan       presence(Status) and misfit(Status.Code, overlen) · both reported "answered by existing rows"
          copy       Status 3 rows, unchanged · nothing planted
laws:     F1 M2
source:   expected · INVARIANTS M2; ARCH §5 step 1 (merge-base lanes on every proof copy)
note:     plant.sql probes the copy before each item, so the pure plan predicts items but not the rows planted (reported:
          Get-FxWitnessPlan cannot see reference rows).
```

### PLT-98 · rows already in the copy that disagree with env: stop acceptance
```case
kind:     sql
env:      pg · env Status holds 3 rows whose Codes are all ≤ 8 characters; the copy's merge-base lane seeds N'Cancelled'
action:   plant · accept · questions HasRows(Status) · Fits(Status.Code→nvarchar(8))
expect:   answers    env Fits={} · copy Fits={overlen}
          outcome    stop acceptance · "The copy answers Fits(Status.Code→nvarchar(8)) overlen yes, but <env>
                     answered no. Fix witness.sql." · exit 5
laws:     F1 H3
source:   expected · ARCH §6 acceptance
note:     The planter never edits reference rows. An agent can, because witness.sql is DML, and the stop says so. The record
          then shows that env and the merge-base lane disagree.
```

## Identity (F2)

### PLT-99 · DDL in witness.sql stops identity
```case
kind:     sql
env:      pg · Customer 5 rows
action:   plant · witness.sql `-- plants: dbo.Customer` with one valid INSERT, then CREATE INDEX IX_w ON dbo.Customer(Region)
          · then identity
expect:   outcome    stop identity · "witness.sql changed the schema (dbo.Customer.IX_w). Witness files are DML
                     only." · exit 5
laws:     F2 H3 H2
source:   expected · red-team #11 (witness.sql can change the schema); ARCH §6 identity
```

### PLT-100 · DML only passes
```case
kind:     sql
env:      pg · Customer 5 rows · Product 5 rows
action:   plant · witness.sql with INSERT, UPDATE, DELETE, MERGE and SET IDENTITY_INSERT ON/OFF, under `-- plants:` and
          `-- witness:` headers · then identity
expect:   plan       identity plan empty
          outcome    no stop from identity
laws:     F2
source:   expected · INVARIANTS F2
note:     SET IDENTITY_INSERT is a session option, not schema, and the IDENT_CURRENT it moves is not in the model.
```

### PLT-101 · the plan is empty after planting, the planter's own temporary changes included
```case
kind:     sql
env:      PLT-45's triggers + PLT-89's untrusted constraints + PLT-82's gapped identity + PLT-33's sequence
action:   plant · then identity · then compare sys.triggers, sys.foreign_keys and sys.check_constraints with the
          base, before and after
expect:   plan       identity plan empty
          copy       every trigger, FK and CHECK has its base is_disabled and is_not_trusted
laws:     F2 F9
source:   verify · Does DacFx's model carry trigger enabled state, and sequence and identity current values? If it
          does not, identity is blind to a failed restore, so plant.sql must assert the catalog comparison itself.
```

## Determinism and S-stable

### PLT-102 · two runs plant byte-identical rows (D1)
```case
kind:     sql
env:      pg · Customer 5 rows · Email NULL×2 · Order 4 rows · one orphan · Product 'DUPE'×2
action:   prove the same inputs twice, on two fresh restores · hash.sql over every table after planting
expect:   copy       identical hashes table by table · identical plant traces · identical plant reports
laws:     D1 S5 D2
source:   expected · INVARIANTS D1
note:     Keys resolved from the copy and identity values are outside D3 but inside D1, so a fresh restore makes them
          identical too. Period values are the exception D1 compares as its timestamp field (PLT-34). Run B starts after
          run A leaves a blocked, half-applied copy, and still matches it (S5).
```

### PLT-103 · adding a column changes only that column (D3)
```case
kind:     sql
env:      schema A = pg · schema B = pg + Customer.Nickname nvarchar(30) NULL · same data shapes · Email NULL×2
action:   plant under A and under B · questions HasRows(Customer) · NotNull(Customer.Email)
expect:   copy       every value in every column except Nickname is identical between A and B · B's one row (the null
                     witness, also the presence row) has Nickname N'w1'
laws:     D3 D1
source:   expected · INVARIANTS D3 (the Twin's S-stable, kept: ARCH §2 row 19)
note:     Each value is keyed by column name and ordinal, never by column position or by a stream shared across columns,
          so a new column cannot shift another column's values.
```

### PLT-104 · the plan is a pure function, independent of question order
```case
kind:     pure
input:    questions Q = [Fits(Product.Code→nvarchar(10)), HasRows(Product), NotNull(Product.Name), HasRows(Customer)]
          · answers all yes or violating {overlen} · and the same set in reverse order
action:   Get-FxWitnessPlan on both orders
expect:   plan       byte-identical plan text · Product: row 1 null(Name) and misfit(Code, overlen) · Customer: row 1 presence
laws:     D1 D3 M2
source:   expected · INVARIANTS D1, M2
```

### PLT-105 · retyping, dropping or reordering a column changes only that column
```case
kind:     sql
env:      schema A = pg · B1 = Region nvarchar(80) · B2 = Region int · B3 = ContactPhone dropped · B4 = Customer's
          columns in a different order · Customer 5 rows
action:   plant under A, B1, B2, B3, B4 · questions HasRows(Customer)
expect:   copy       B1 equals A everywhere (N'w1' still fits) · B2 differs only in Region (1) · B3 differs only by the
                     missing column · B4 equals A column by column
laws:     D3
source:   expected · INVARIANTS D3
```

### PLT-106 · M2: the planted count equals the predicted count
```case
kind:     sql
env:      pg + FK_Order_Customer (NOT NULL) · Customer 5 rows · Email NULL×2 · Order 4 rows · Product 'DUPE'×2
action:   plant · questions HasRows(Customer) · HasRows(Order) · HasRows(Product) · NotNull(Customer.Email)
          · Unique(Product,(Code))
expect:   plan       predicted 4: Customer 1 (null(Email), also presence) · Order 1 (its ancestor is that Customer row)
                     · Product 2 (dup exact, also presence)
          copy       4 rows planted · the report states predicted 4, planted 4, skipped 0 · removing the null row or either
                     dup row breaks F1
laws:     M2 F1
source:   expected · INVARIANTS M2
note:     The prediction needs the copy's FK graph and reference rows, which plant.sql knows and Get-FxWitnessPlan does not
          (PLT-97, reported).
```

### PLT-107 · M2's deletion law: every generated row is necessary
```case
kind:     sql
env:      pg · Customer 5 rows · Email NULL×2 · Product 5 rows · Code 'DUPE'×2
action:   plant · questions HasRows(Customer) · NotNull(Customer.Email) · HasRows(Product) · Unique(Product,(Code))
          · then delete each planted row in turn and accept
expect:   copy       3 rows planted (Customer: the null witness · Product: the dup pair) · deleting any one of them breaks F1
laws:     M2 F1
source:   expected · INVARIANTS M2 (a violation row also serves as presence)
note:     Under the earlier sum rule a separate presence row survived its own deletion. Under the maximum rule no
          generated row is redundant.
```

## `up` mode

### PLT-108 · k rows in every base table
```case
kind:     process
setup:    facsimile.json `up.rowsPerTable` 3 · the pg project · no fx_local · up.sql absent
action:   facsimile up --fresh
expect:   copy       fx_local: Account, Customer, CustomerAddress, Order, OrderLine and Product hold 3 rows each (ordinals
                     1–3) · Status and Category hold the head version of their lane rows only
          stdout     DONE up · … · .facsimile/out/up.json · exit 0
laws:     T3 S1 A1
source:   expected · ARCH §8 (`up`: repository schema, reference data, k presence rows per table, then up.sql)
note:     `up` plants every base table, touched or not, with no questions or answers involved. The data is plain but valid
          (ARCH §2 row 12). The reference data is head's, where a proof copy's is the merge-base's.
```

### PLT-109 · FK-consistent: children spread over the existing parents
```case
kind:     process
setup:    the pg project + NOT NULL FKs OrderLine→Order, Order→Customer, Order→Status · nullable Product.CategoryId FK · k 3
action:   facsimile up --fresh
expect:   copy       Order rows 1–3 → CustomerId 1, 2, 3 · StatusId 1, 2, 3 (lane rows) · OrderLine 1–3 → OrderId 1, 2, 3
                     · Product.CategoryId NULL · zero orphans · every FK trusted
laws:     T3 D1
source:   expected · ARCH §5 step 2 (rules shared with prove)
note:     Child row n points at parent row ((n−1) mod p)+1 in key order, where p is the parent's row count.
          Nullable FKs stay NULL as in prove (the Twin's per-parent fan-out is retired, ARCH §2 row 16).
```

### PLT-110 · views are skipped in `up`
```case
kind:     process
setup:    the pg project + VIEW dbo.vCustomer · k 3
action:   facsimile up --fresh
expect:   copy       vCustomer published and returns 3 rows (from Customer) · no INSERT targets a view
laws:     T1 T3
source:   expected · THE_TWIN.md §6 (views not minted, still published)
```

### PLT-111 · reference data is untouched in `up`
```case
kind:     process
setup:    the pg project · referenceData lanes seed Status 1–3 and Category 1–3 (head version) · k 3
action:   facsimile up --fresh · hash Status and Category after the lanes and after planting
expect:   copy       Status and Category hashes unchanged by planting · no planted row in either table
laws:     T3 F1
source:   expected · THE_TWIN.md §6 (the row-carrying kinds are the lane-seeded set, by observation)
note:     A table that holds rows after the lanes is reference data by observation. No config names it, as in the Twin.
```

### PLT-112 · `up` is deterministic and convergent
```case
kind:     process
setup:    the pg project · k 3
action:   facsimile up --fresh · hash every table · facsimile down --all · facsimile up --fresh · hash · facsimile up
expect:   copy       the two hash sets are identical
          stdout     the final `up`: DONE up · Nothing to apply · .facsimile/out/up.json · exit 0
laws:     D1 D4 T3
source:   expected · INVARIANTS D4; THE_TWIN.md §3 law 1 (convergence, kept)
note:     `down --all` drops fx_local and the caches, so the second `up --fresh` starts from nothing; plain `down` would keep
          fx_local (S6).
```

### PLT-113 · `up` re-converges by restore and plant, never by publishing over planted rows (S2)
```case
kind:     process
setup:    fx_local from PLT-108 · then the project adds Customer.Nickname nvarchar(30) NULL
action:   facsimile up · step trace
expect:   plan       trace: build · cache miss → publish the repository schema · head referenceData lanes · backup · restore
                     fx_local · plant · up.sql · never a publish after a plant
          copy       Customer.Nickname N'w1'..N'w3' · every other value identical to before (D3)
laws:     S2 D4 D3
source:   expected · red-team #9 (re-publishing the base after planting erases witnesses, verified)
```

### PLT-114 · `up` with a unique column too narrow for k: needs a declared row in up.sql
```case
kind:     process
setup:    the pg project + T (Id int IDENTITY PK · IsPrimary bit NOT NULL UNIQUE)
          + U (Id int IDENTITY PK · Grade char(1) NOT NULL UNIQUE) · k 3
action:   facsimile up --fresh
expect:   outcome    stop planter-needs-row · "Cannot make a valid row for dbo.T (IsPrimary bit is unique and has room for two
                     planted values at k 3). Write one valid INSERT for dbo.T into up.sql under -- plants: dbo.T." · JSON
                     also lists dbo.U (Grade char(1) is unique and has room for one) · exit 5
laws:     H3 A3
source:   expected · ARCH §6 planter-needs-row ("into witness.sql (or up.sql)"); ARCH §8 (`up` applies up.sql)
note:     With the marker, char(1) fails at k = 2, not k > 2.
```

### PLT-115 · prove never publishes the base after a plant (S2)
```case
kind:     process
setup:    PLT-102's fixture
action:   facsimile prove Dev · step trace
expect:   plan       trace: restore · plant · witness.sql · accept · identity (a Script, not a publish) · publish head · never
                     a publish of schema.Dev.dacpac after "plant"
laws:     S2 S5
source:   expected · red-team #9 (re-publishing the base erases witnesses, verified); ARCH §5
```

## Refusals (planter-needs-row)

### PLT-116 · the stop names the table, the reason and the declared-row header (H3)
```case
kind:     sql
env:      as PLT-44
action:   prove
expect:   stdout     STOP planter-needs-row · Write one valid INSERT for dbo.Product into witness.sql under -- plants:
                     dbo.Product · proofs/<slug>/verdict.Dev.json · exit 5
          files      verdict.Dev.json: stop planter-needs-row · table dbo.Product · reason "CK_Product_Code rejects the
                     planted literal" · constraint CK_Product_Code · next "-- plants: dbo.Product"
laws:     H3 H1 A1 A3
source:   expected · ARCH §6 planter-needs-row
```

### PLT-117 · following the stop clears it (A5)
```case
kind:     sql
env:      as PLT-44 · then witness.sql gains `-- plants: dbo.Product` with one valid row whose Code is N'ABCw1'
action:   prove · apply the instruction · prove again
expect:   outcome    run 1 stop planter-needs-row · run 2 reaches a verdict or a different stop, never the same one
laws:     A5 H3
source:   expected · INVARIANTS A5
```

### PLT-118 · every needs-row in one pass
```case
kind:     sql
env:      PLT-21's T + PLT-44's Product + PLT-57's Employee, all populated
action:   plant · questions HasRows(T) · HasRows(Product) · HasRows(Employee)
expect:   outcome    stop planter-needs-row · stdout names the first table in plan order · the JSON lists all three tables
                     with their reasons · exit 5
laws:     H1 H3 A5
source:   expected · ARCH §6 planter-needs-row (decision)
note:     decision: one stop that lists every unplantable table. Otherwise an agent would need one run per table.
          This mirrors E4's "all problems in one pass".
```

### PLT-119 · a declared row that fails to insert stops witness-failed
```case
kind:     sql
env:      as PLT-44 · witness.sql line 1 `-- plants: dbo.Product` · line 2 INSERT dbo.Product (Code, Name, LegacyCode)
          VALUES (N'w1', N'w1', N'w1'), which violates CK_Product_Code
action:   plant
expect:   outcome    stop witness-failed · "witness.sql line 2 failed with Msg 547. Fix that row." · exit 5
          files      the stop records Msg 547 and CK_Product_Code, never the server's text
laws:     H3 A5 P3
source:   expected · ARCH §5 step 5 (a failing row stops witness-failed); ARCH §6
note:     The row carries the marker, so witness-not-synthetic does not fire first (PLT-124). The server text would quote the
          agent's own value, which is harmless here, but one rule applies to every error.
```

### PLT-124 · a literal neither marked nor repository-defined stops witness-not-synthetic
```case
kind:     sql
env:      pg · the merge-base Status lane includes N'Cancelled' · Customer 5 rows · Order 4 rows · witness.sql, one variant at
          a time: (a) `-- plants: dbo.Customer` then INSERT (Name, Email) VALUES (N'Joe Bloggs', N'joe@corp.example')
          · (b) the same with N'wJoe' and N'w-joe' · (c) `-- plants: dbo.[Order]` then INSERT (CustomerId, StatusId, Total,
          StatusText) VALUES (1, 3, 5.00, N'Cancelled')
action:   plant, once per variant
expect:   outcome    (a) stop witness-not-synthetic · "witness.sql line 2 carries a literal not marked synthetic. Use a
                     w-prefixed value or a repository-defined value." · exit 5 · (b) no stop · (c) no stop: N'Cancelled'
                     is a seed code the repository defines
          files      (a) the stop names the line, never the literal
laws:     P5 P1 H3 A5
source:   expected · ARCH §5 step 5; INVARIANTS P5 (repository-defined values are allowed where equality with one matters)
note:     decision: the check applies to string and binary literals; a number, a date or a bit carries no marker (reported).
          witness.sql is committed, so a copied environment value would leave through it.
```

### PLT-120 · needs the plan knows of stop before any row is planted
```case
kind:     pure
input:    questions HasRows(Order) · CheckFalse(Order,CK_Order_Total) · Computed(Order.TotalCents) · answers violating
          · witness.sql absent
action:   Get-FxWitnessPlan
expect:   plan       needs check(Order, CK_Order_Total) · needs computed(Order.TotalCents)
          outcome    stop check-witness · "Write one row into witness.sql that makes CK_Order_Total FALSE, then re-run."
                     · the JSON also lists computed-witness for Order.TotalCents · exit 5
laws:     H1 A5
source:   expected · ARCH §4 (needs: check, computed, authored)
note:     Plan-level needs are known without SQL and stop before planting. planter-needs-row can only surface
          once they are met, so A5 always moves forward.
```

### PLT-127 · a computed column the delta adds: stop computed-witness, then the declared row decides
```case
kind:     corpus
env:      pg · Order 4 rows · one Total 30000000.00
change:   head adds Order.TotalCents AS CAST(Total * 100 AS int) PERSISTED
expect:   leg1       no witness.sql
                     questions  Computed(Order.TotalCents)
                     answers    Computed(Order.TotalCents)=unmeasured · Rows(Order)=4
                     outcome    stop computed-witness · "Write one row into witness.sql whose TotalCents expression fails,
                                then re-run." · exit 5
          leg2       witness.sql `-- witness: Computed(Order.TotalCents)` then one Order with Total = 30000000.00
                     witnesses  presence(Order) · declared(Order Total = 30000000.00)
                     outcome    verdict blocked overflow · Msg 8115 · dbo.Order · exit 3
                     record     tier T2 · notChecked ∋ declared-not-measured
laws:     H2 A5 F3 H4
source:   verify · whether DacFx adds a PERSISTED column in place (computed over every existing row) or rebuilds the
          table, and whether the failure is Msg 8115. The stop is ARCH §4 Computed and ARCH §6 computed-witness
note:     leg1 is a designed stop (F3 scope). In leg2 the declared row matches the shape Dev holds, so the oracle agrees; the
          record says the question was declared, not measured (H2).
```

### PLT-121 · a needs-row is never downgraded
```case
kind:     sql
env:      PLT-21's T, PLT-42's T and PLT-44's Product, one at a time
action:   plant
expect:   outcome    each stops planter-needs-row · the planter never writes NULL into a NOT NULL column, never relies on a
                     disabled CHECK to admit a presence row (PLT-96 re-checks), and never skips a populated table
laws:     H2 F1
source:   expected · INVARIANTS H2 (no silent clean)
note:     Skipping a populated table would make HasRows read no in the copy, turning a blocked guard into a
          false clean. The only skip allowed is a witness already answered by existing rows (PLT-97).
```

### PLT-122 · how often the real estate needs a declared row (ARCH §11.7)
```case
kind:     manual
setup:    a team laptop · LocalDB 2022 · schema.Dev.dacpac of the real estate
action:   facsimile up --fresh against the estate's project, then count the tables that stop planter-needs-row, by reason
expect:   files      a table of reason → table count (CHECK, CLR or typed xml, self-reference or cycle, narrow unique, computed)
                     recorded in this case as its receipt
laws:     F1 M2
source:   verify · ARCH §11.7. Do ordinal literals satisfy the estate's CHECKs often enough? A high CHECK count
          argues for using a column's literal default, which D3's signature excludes today.
```

module Projection.Tests.SchemaMigrationEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// 6.A.12 — the implied emission differential. `SchemaMigrationEmitter.emit`
// turns a `CatalogDiff` into minimum-viable-touch DDL: ALTER TABLE … ADD /
// ALTER COLUMN, not a full CREATE. Renames are the RefactorLog channel
// (tested in RefactorLogEmitterTests); destructive / unsupported changes are
// refused fail-loud. Clean-room: `between` (comparison) ≠ `emit` (emission).

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> Assert.Fail(sprintf "%A" e); Unchecked.defaultof<'a>

let private nm (s: string) : Name = Name.create s |> Result.value

let private catalogWithCustomer (customer': Kind) : Catalog =
    let m = { salesModule with Kinds = [ customer'; order; country ] }
    Catalog.create [ m ] [] |> Result.value

let private withCustomerName (f: Attribute -> Attribute) : Catalog =
    let c' =
        { customer with
            Attributes =
                customer.Attributes
                |> List.map (fun a -> if a.SsKey = customerNameKey then f a else a) }
    catalogWithCustomer c'

/// Emit the migration of `sampleCatalog → target`.
let private migrationOf (target: Catalog) : Statement list * DiagnosticEntry list =
    let diff = CatalogDiff.between sampleCatalog target
    let m = SchemaMigrationEmitter.emit diff
    m.Value, m.Entries

let private isCreateTable = function Statement.CreateTable _ -> true | _ -> false
let private alterColumns (stmts: Statement list) =
    stmts |> List.choose (function Statement.AlterTableAlterColumn (_, c) -> Some c | _ -> None)
let private addColumns (stmts: Statement list) =
    stmts |> List.choose (function Statement.AlterTableAddColumn (_, c) -> Some c | _ -> None)

[<Fact>]
let ``migration: a column type change emits an ALTER, not a CREATE`` () =
    let target = withCustomerName (fun a -> { a with Type = Integer })
    let stmts, _ = migrationOf target
    // The minimum-viable-touch is an ALTER COLUMN — never a full CREATE.
    Assert.False(stmts |> List.exists isCreateTable)
    let alters = alterColumns stmts
    Assert.Equal(1, List.length alters)
    Assert.Equal("NAME", (List.head alters).Name)

[<Fact>]
let ``migration: ALTER COLUMN renders as ALTER TABLE ... ALTER COLUMN via ScriptDom`` () =
    let target = withCustomerName (fun a -> { a with Type = Integer })
    let stmts, _ = migrationOf target
    let sb = System.Text.StringBuilder()
    stmts |> List.iter (Render.toSql sb)
    let sql = sb.ToString()
    Assert.Contains("ALTER TABLE", sql)
    Assert.Contains("ALTER COLUMN", sql)
    Assert.DoesNotContain("CREATE TABLE", sql)

[<Fact>]
let ``migration: a new attribute emits ADD COLUMN`` () =
    let newKey = attrKey ["Customer"; "Loyalty"]
    let c' =
        { customer with
            Attributes =
                customer.Attributes
                @ [ { Attribute.create newKey (nm "Loyalty") Integer with
                        Column = ColumnRealization.create ("LOYALTY") (true) |> Result.value } ] }
    let stmts, _ = migrationOf (catalogWithCustomer c')
    let adds = addColumns stmts
    Assert.Equal(1, List.length adds)
    Assert.Equal("LOYALTY", (List.head adds).Name)
    Assert.False(stmts |> List.exists isCreateTable)

[<Fact>]
let ``migration: a dropped column is refused fail-loud (no DROP emitted)`` () =
    let c' =
        { customer with
            Attributes = customer.Attributes |> List.filter (fun a -> a.SsKey <> customerTenantKey) }
    let stmts, entries = migrationOf (catalogWithCustomer c')
    // Nothing emitted for a pure drop — refused, not silently dropped.
    Assert.Empty(stmts)
    Assert.Contains(entries, fun e ->
        e.Code = "migration.destructiveColumnDrop" && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``migration: a DEFAULT-constraint facet change is refused fail-loud`` () =
    // Customer.Name gains a DEFAULT — a DefaultValue facet ALTER COLUMN can't
    // express in one statement; refused (no partial/misleading ALTER).
    let lit = SqlLiteral.ofRaw Text "unknown"
    let target = withCustomerName (fun a -> { a with DefaultValue = Some lit })
    let stmts, entries = migrationOf target
    Assert.Empty(alterColumns stmts)
    Assert.Contains(entries, fun e ->
        e.Code = "migration.unsupportedFacetChange" && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``migration: an identity facet change is refused fail-loud`` () =
    let target = withCustomerName (fun a -> { a with IsIdentity = true })
    let _, entries = migrationOf target
    Assert.Contains(entries, fun e ->
        e.Code = "migration.unsupportedFacetChange" && e.Severity = DiagnosticSeverity.Error)

// AC-G8/S11 re-target: `NULL to NOT NULL tightening` no longer "emits the ALTER
// with a narrowing Warning" — narrowing is now a declared-loss, refused
// fail-loud unless --allow-drops. The re-targeted test (`... refuses fail-loud
// unless declared`) lives at the end of this file alongside the length- and
// precision/scale-narrowing tests.

[<Fact>]
let ``migration: an identical diff emits no statements and no diagnostics`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog
    let m = SchemaMigrationEmitter.emit diff
    Assert.Empty(m.Value)
    Assert.Empty(m.Entries)

[<Fact>]
let ``migration: a rename alone emits no ALTER (renames are the RefactorLog channel)`` () =
    // Customer.Name's logical name changes (Name → FullName); the column
    // SHAPE is unchanged, so the migration emitter emits nothing — the rename
    // rides the RefactorLog channel, disjoint from the ALTER channel.
    let target = withCustomerName (fun a -> { a with Name = nm "FullName" })
    let stmts, entries = migrationOf target
    Assert.Empty(alterColumns stmts)
    Assert.Empty(addColumns stmts)
    Assert.Empty(entries)

// ---------------------------------------------------------------------------
// S6.3 (rename ⊥ reshape — the adversarial simultaneous case). One attribute
// is BOTH renamed (logical Name change → RefactorLog channel) AND reshaped (a
// Length shape facet → ALTER COLUMN channel) in the same diff. The two
// emission channels are disjoint by axis (RefactorLogEmitter reads
// `AttributeDiff.Renamed`; SchemaMigrationEmitter reads `AttributeDiff.Reshaped`),
// so the rename must ride the RefactorLog ONLY and the reshape the ALTER ONLY —
// neither channel carries the other's content. A "skip the ALTER if the column
// is also renamed" (or vice-versa) coupling would FAIL this test (the
// medium-high-risk co-wrongness the regrade flagged).
// ---------------------------------------------------------------------------

[<Fact>]
let ``S6.3: a simultaneously renamed AND reshaped attribute splits cleanly across the two channels`` () =
    // Customer.Name: logical Name changes (Name → FullName) AND declared length
    // is set (None → Some 256). One attribute, two orthogonal moves, one diff.
    let target =
        withCustomerName (fun a -> { a with Name = nm "FullName"; Length = Some 256 })
    let diff = CatalogDiff.between sampleCatalog target

    // The diff itself records BOTH moves on the same attribute key.
    let ad = CatalogDiff.attributeDiffOf customerKey diff |> Option.get
    Assert.True(ad.Renamed |> Map.containsKey customerNameKey)
    Assert.True(ad.Reshaped |> List.exists (fun c ->
        c.AttributeKey = customerNameKey && Set.contains AttributeFacet.Length c.Facets))

    // SchemaMigration channel: exactly the ALTER COLUMN for the shape change,
    // and NOTHING else — no ADD/DROP, no statement carrying the rename.
    let m = SchemaMigrationEmitter.emit diff
    let alters = alterColumns m.Value
    Assert.Equal(1, List.length alters)
    Assert.Equal("NAME", (List.head alters).Name)   // physical column unchanged by a logical rename
    Assert.Empty(addColumns m.Value)
    Assert.False(m.Value |> List.exists (function Statement.AlterTableDropColumn _ -> true | _ -> false))
    Assert.Equal(1, List.length m.Value)             // the ALTER is the whole emission
    Assert.False(m.Value |> List.exists isCreateTable)
    // The migration channel never renders the new logical name (that's RefactorLog's).
    let migrationSql = ScriptDomGenerate.toText (Seq.ofList m.Value)
    Assert.DoesNotContain("FullName", migrationSql)

    // RefactorLog channel: exactly the rename entry (old → new), and NO ALTER /
    // shape content — the refactor entry carries only the rename evidence.
    let refactor = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.tryFind customerKey refactor |> Option.defaultValue []
    Assert.Equal(1, List.length entries)
    let entry = List.head entries
    Assert.Equal(RenameRefactor, entry.OperationKind)
    Assert.Equal(SqlSimpleColumn, entry.ElementType)
    Assert.Equal("FullName", entry.NewName)
    Assert.Contains("Name", entry.ElementName)       // old logical name on the element reference
    // No OTHER kind's refactor slice picked up a rename (the rename is local).
    let allEntries =
        ArtifactByKind.toMap refactor |> Map.toList |> List.collect snd
    Assert.Equal(1, List.length allEntries)

// ---------------------------------------------------------------------------
// S10.3 / S10.4 — the non-alterable facets (PrimaryKey / Computed) refuse
// fail-loud. `ALTER TABLE … ALTER COLUMN` cannot express a PK toggle or a
// computed-column conversion in one statement; the emitter refuses with a
// named Error and emits NO ALTER. A body that silently emitted a naive ALTER
// for these facets would produce a non-empty statement list — these tests
// discriminate it.
// ---------------------------------------------------------------------------

[<Fact>]
let ``S10.3: a PrimaryKey facet change is refused fail-loud (no ALTER emitted)`` () =
    // Customer.Name gains PK membership (false → true) — a PrimaryKey facet a
    // single ALTER COLUMN cannot express; refused, no statement.
    let target = withCustomerName (fun a -> { a with IsPrimaryKey = true })
    let stmts, entries = migrationOf target
    Assert.Empty(alterColumns stmts)
    Assert.Empty(stmts)
    Assert.Contains(entries, fun e ->
        e.Code = "migration.unsupportedFacetChange" && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``S10.4: a Computed facet change is refused fail-loud (no ALTER emitted)`` () =
    // Customer.Name becomes a computed column — a Computed facet a single
    // ALTER COLUMN cannot express; refused, no statement.
    let computed = ComputedColumnConfig.create "UPPER([NAME])" false |> Result.value
    let target = withCustomerName (fun a -> { a with Computed = Some computed })
    let stmts, entries = migrationOf target
    Assert.Empty(alterColumns stmts)
    Assert.Empty(stmts)
    Assert.Contains(entries, fun e ->
        e.Code = "migration.unsupportedFacetChange" && e.Severity = DiagnosticSeverity.Error)

// ---------------------------------------------------------------------------
// C1 emitter follow-on — reference / index / sequence channel emission. Added
// FK/index/sequence emit their minimum-viable DDL; a Trust-only FK change
// emits the WITH NOCHECK two-step; destructive/unsupported changes refuse
// fail-loud (named Error). Same discipline as the attribute channel above.
// ---------------------------------------------------------------------------

let private migrationBetween (source: Catalog) (target: Catalog) : Statement list * DiagnosticEntry list =
    let m = SchemaMigrationEmitter.emit (CatalogDiff.between source target)
    m.Value, m.Entries

let private catalogOf (kinds: Kind list) (seqs: Sequence list) : Catalog =
    Catalog.create [ { salesModule with Kinds = kinds } ] seqs |> Result.value

let private orderNoRef : Kind = { order with References = [] }
let private seqKey (s: string) : SsKey = kindKey [ "Seq"; s ]
let private orderNumberSeq : Sequence =
    Sequence.create (seqKey "OrderNumber") (nm "OrderNumber") "dbo" "bigint"
        (Some 1m) (Some 1m) (Some 1m) (Some 9999999999m) false SequenceCacheMode.Unspecified None
    |> Result.value
let private customerNameIdx : Index =
    { Index.create (idxKey [ "Customer"; "UX_Name" ]) (nm "UX_Customer_Name")
        (IndexColumn.ascendingList [ customerNameKey ]) with Uniqueness = Unique }

let private hasError (code: string) (entries: DiagnosticEntry list) =
    entries |> List.exists (fun e -> e.Code = code && e.Severity = DiagnosticSeverity.Error)

[<Fact>]
let ``C1 emit: an added FK emits ALTER TABLE ADD CONSTRAINT FOREIGN KEY (not a CREATE)`` () =
    let stmts, entries = migrationBetween (catalogOf [ customer; orderNoRef; country ] []) (catalogOf [ customer; order; country ] [])
    Assert.False(stmts |> List.exists isCreateTable)
    Assert.Equal(1, stmts |> List.filter (function Statement.AlterTableAddForeignKey _ -> true | _ -> false) |> List.length)
    Assert.False(entries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))
    // The new variant renders to real T-SQL through ScriptDom (the full path).
    let sql = ScriptDomGenerate.toText (Seq.ofList stmts)
    Assert.Contains("ALTER TABLE", sql)
    Assert.Contains("FOREIGN KEY", sql)
    Assert.Contains("REFERENCES", sql)

[<Fact>]
let ``C1 emit: an added UNIQUE index emits CREATE INDEX`` () =
    let target = catalogOf [ { customer with Indexes = [ customerNameIdx ] }; order; country ] []
    let stmts, entries = migrationBetween (catalogOf [ customer; order; country ] []) target
    Assert.Equal(1, stmts |> List.filter (function Statement.CreateIndex _ -> true | _ -> false) |> List.length)
    Assert.False(entries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))

[<Fact>]
let ``C1 emit: an added sequence emits CREATE SEQUENCE`` () =
    let stmts, entries = migrationBetween (catalogOf [ customer; order; country ] []) (catalogOf [ customer; order; country ] [ orderNumberSeq ])
    Assert.Equal(1, stmts |> List.filter (function Statement.CreateSequence _ -> true | _ -> false) |> List.length)
    Assert.False(entries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))

[<Fact>]
let ``C1 emit: an FK trust change (trusted -> NOCHECK) emits the disable + nocheck two-step`` () =
    // NM-12 — represent the WITH NOCHECK FK as a real untrusted constraint (both
    // sides legal via withConstraintState); the trust facet still flips.
    let orderTrusted =
        { order with References = order.References |> List.map (Reference.withConstraintState true true) }
    let orderUntrusted =
        { order with References = order.References |> List.map (Reference.withConstraintState true false) }
    let stmts, _ = migrationBetween (catalogOf [ customer; orderTrusted; country ] []) (catalogOf [ customer; orderUntrusted; country ] [])
    Assert.True(stmts |> List.exists (function Statement.AlterTableDisableConstraint _ -> true | _ -> false))
    Assert.True(stmts |> List.exists (function Statement.AlterTableNoCheckConstraint _ -> true | _ -> false))

[<Fact>]
let ``C1 emit: a dropped FK refuses fail-loud (migration.destructiveReferenceDrop)`` () =
    let _, entries = migrationBetween (catalogOf [ customer; order; country ] []) (catalogOf [ customer; orderNoRef; country ] [])
    Assert.True(hasError "migration.destructiveReferenceDrop" entries)

[<Fact>]
let ``C1 emit: a changed index refuses fail-loud (migration.unsupportedIndexChange)`` () =
    let a = catalogOf [ { customer with Indexes = [ { customerNameIdx with Uniqueness = NotUnique } ] }; order; country ] []
    let b = catalogOf [ { customer with Indexes = [ customerNameIdx ] }; order; country ] []
    let _, entries = migrationBetween a b
    Assert.True(hasError "migration.unsupportedIndexChange" entries)

[<Fact>]
let ``C1 emit: a dropped sequence refuses fail-loud (migration.destructiveSequenceDrop)`` () =
    let _, entries = migrationBetween (catalogOf [ customer; order; country ] [ orderNumberSeq ]) (catalogOf [ customer; order; country ] [])
    Assert.True(hasError "migration.destructiveSequenceDrop" entries)

// ---------------------------------------------------------------------------
// C1 destructive follow-on — under --allow-drops the emitter emits the
// destructive DDL (DROP COLUMN/CONSTRAINT/INDEX/SEQUENCE + DROP-then-recreate
// for FK/index/sequence reshapes) instead of refusing. Without the flag the
// existing refusals stand (covered above).
// ---------------------------------------------------------------------------

let private dropsBetween (source: Catalog) (target: Catalog) : Statement list * DiagnosticEntry list =
    let m = SchemaMigrationEmitter.emitWith true (CatalogDiff.between source target)
    m.Value, m.Entries

let private noError (entries: DiagnosticEntry list) =
    Assert.False(entries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))

[<Fact>]
let ``C1 drops: a removed FK emits ALTER TABLE DROP CONSTRAINT (allow-drops)`` () =
    let stmts, entries = dropsBetween (catalogOf [ customer; order; country ] []) (catalogOf [ customer; orderNoRef; country ] [])
    Assert.True(stmts |> List.exists (function Statement.AlterTableDropConstraint _ -> true | _ -> false))
    noError entries
    let sql = ScriptDomGenerate.toText (Seq.ofList stmts)
    Assert.Contains("DROP CONSTRAINT", sql)

[<Fact>]
let ``C1 drops: a removed index emits DROP INDEX (allow-drops)`` () =
    let a = catalogOf [ { customer with Indexes = [ customerNameIdx ] }; order; country ] []
    let b = catalogOf [ customer; order; country ] []
    let stmts, entries = dropsBetween a b
    Assert.True(stmts |> List.exists (function Statement.DropIndex _ -> true | _ -> false))
    noError entries
    let sql = ScriptDomGenerate.toText (Seq.ofList stmts)
    Assert.Contains("DROP INDEX", sql)

[<Fact>]
let ``C1 drops: a removed sequence emits DROP SEQUENCE (allow-drops)`` () =
    let stmts, entries = dropsBetween (catalogOf [ customer; order; country ] [ orderNumberSeq ]) (catalogOf [ customer; order; country ] [])
    Assert.True(stmts |> List.exists (function Statement.DropSequence _ -> true | _ -> false))
    noError entries
    let sql = ScriptDomGenerate.toText (Seq.ofList stmts)
    Assert.Contains("DROP SEQUENCE", sql)

[<Fact>]
let ``C1 drops: a dropped column emits ALTER TABLE DROP COLUMN (allow-drops)`` () =
    let customerNoTenant =
        { customer with Attributes = customer.Attributes |> List.filter (fun a -> a.SsKey <> customerTenantKey) }
    let stmts, entries = dropsBetween (catalogOf [ customer; order; country ] []) (catalogOf [ customerNoTenant; order; country ] [])
    Assert.True(stmts |> List.exists (function Statement.AlterTableDropColumn _ -> true | _ -> false))
    noError entries
    let sql = ScriptDomGenerate.toText (Seq.ofList stmts)
    Assert.Contains("DROP COLUMN", sql)

[<Fact>]
let ``C1 drops: an index reshape emits DROP INDEX + CREATE INDEX (allow-drops)`` () =
    let a = catalogOf [ { customer with Indexes = [ { customerNameIdx with Uniqueness = NotUnique } ] }; order; country ] []
    let b = catalogOf [ { customer with Indexes = [ customerNameIdx ] }; order; country ] []
    let stmts, _ = dropsBetween a b
    Assert.True(stmts |> List.exists (function Statement.DropIndex _ -> true | _ -> false))
    Assert.True(stmts |> List.exists (function Statement.CreateIndex _ -> true | _ -> false))

[<Fact>]
let ``C1 drops: a sequence reshape emits DROP SEQUENCE + CREATE SEQUENCE (allow-drops)`` () =
    let a = catalogOf [ customer; order; country ] [ orderNumberSeq ]
    let b = catalogOf [ customer; order; country ] [ { orderNumberSeq with Increment = Some 10m } ]
    let stmts, _ = dropsBetween a b
    Assert.True(stmts |> List.exists (function Statement.DropSequence _ -> true | _ -> false))
    Assert.True(stmts |> List.exists (function Statement.CreateSequence _ -> true | _ -> false))

[<Fact>]
let ``C1 drops: without --allow-drops a removed FK still refuses (the gate holds)`` () =
    let _, entries = migrationBetween (catalogOf [ customer; order; country ] []) (catalogOf [ customer; orderNoRef; country ] [])
    Assert.True(hasError "migration.destructiveReferenceDrop" entries)

// ---------------------------------------------------------------------------
// AC-G8 / AC-S11 — column NARROWING is a declared-loss. NULL→NOT NULL
// tightening, length shrink, and precision/scale shrink each refuse fail-loud
// (migration.narrowingColumn, NO ALTER) under the safe default and emit the
// ALTER only under the same allow-drops declaration as a DROP. Each test
// DISCRIMINATES: a warn-and-proceed impl emits the ALTER + a Warning under the
// default, failing the `isAlterCol = false` + `narrowingError = true` asserts.
// ---------------------------------------------------------------------------

let private hasNarrowingError (entries: DiagnosticEntry list) =
    hasError "migration.narrowingColumn" entries

let private isAlterCol (stmts: Statement list) =
    stmts |> List.exists (function Statement.AlterTableAlterColumn _ -> true | _ -> false)

[<Fact>]
let ``migration: NULL to NOT NULL tightening refuses fail-loud unless declared`` () =
    // Build source (nullable) → target (not null) directly; the fixture
    // columns are all NOT NULL, so construct the nullable source explicitly.
    let mk (nullable: bool) =
        let c' =
            { customer with
                Attributes =
                    customer.Attributes
                    |> List.map (fun a ->
                        if a.SsKey = customerNameKey then { a with Column = { a.Column with IsNullable = nullable } } else a) }
        catalogWithCustomer c'
    let source, target = mk true, mk false
    // Safe default — REFUSE: a migration.narrowingColumn Error and NO ALTER.
    // (A warn-and-proceed impl would emit the ALTER and fail these two asserts.)
    let stmts, entries = migrationBetween source target
    Assert.False(isAlterCol stmts)
    Assert.True(hasNarrowingError entries)
    // Declared (allow-drops) — the ALTER COLUMN emits, no Error.
    let dStmts, dEntries = dropsBetween source target
    Assert.True(isAlterCol dStmts)
    Assert.False(dEntries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))

[<Fact>]
let ``migration: a length narrowing (NVARCHAR(256) -> (50)) refuses fail-loud unless declared`` () =
    // S11.2 — declared-length shrink is truncation-destructive. Source 256,
    // target 50. Refuse under the default; emit under allow-drops.
    let mk (len: int) =
        withCustomerName (fun a -> { a with Length = Some len })
    let source, target = mk 256, mk 50
    let stmts, entries = migrationBetween source target
    Assert.False(isAlterCol stmts)
    Assert.True(hasNarrowingError entries)
    let dStmts, dEntries = dropsBetween source target
    Assert.True(isAlterCol dStmts)
    Assert.False(dEntries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))

[<Fact>]
let ``migration: a precision/scale narrowing (DECIMAL(18,4) -> (9,2)) refuses fail-loud unless declared`` () =
    // Precision shrink (18 -> 9) AND scale shrink (4 -> 2) — both lose data.
    let mk (p: int) (s: int) =
        withCustomerName (fun a -> { a with Precision = Some p; Scale = Some s })
    let source, target = mk 18 4, mk 9 2
    let stmts, entries = migrationBetween source target
    Assert.False(isAlterCol stmts)
    Assert.True(hasNarrowingError entries)
    let dStmts, dEntries = dropsBetween source target
    Assert.True(isAlterCol dStmts)
    Assert.False(dEntries |> List.exists (fun e -> e.Severity = DiagnosticSeverity.Error))

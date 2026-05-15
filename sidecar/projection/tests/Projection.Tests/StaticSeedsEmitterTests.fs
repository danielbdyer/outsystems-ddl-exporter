module Projection.Tests.StaticSeedsEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.Data

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice α — StaticSeedsEmitter v0 (V1-shape MERGE).
//
// Per `CHAPTER_4_1_B_OPEN.md` strategic frame axis 8: idempotence + topo-
// order + (slice γ) CDC-silence. This test file covers the slice-α
// surface: MERGE shape parity with V1 + idempotence under repeat
// invocation + T11 keyset coverage. CDC-silence (the chapter signature)
// lands at slice γ canary.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

/// Static-entity fixture with explicit Id values (so VALUES rows are
/// deployable). Three rows × three columns (Id INT PK + Code TEXT +
/// Label TEXT).
let private mkCountryKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Country"]
    let idKey = mkKey ["TestModule"; "Country"; "Id"]
    let codeKey = mkKey ["TestModule"; "Country"; "Code"]
    let labelKey = mkKey ["TestModule"; "Country"; "Label"]
    let row code label =
        { Identifier = mkKey ["TestModule"; "Country"; "Row"; code]
          Values =
              Map.ofList
                  [ mkName "Id",    code  // simulate Id-as-Code for test simplicity
                    mkName "Code",  code
                    mkName "Label", label ] }
    {
        SsKey    = kindKey
        Name     = mkName "Country"
        Origin   = OsNative
        Modality = [ Static [ row "US" "United States"
                              row "CA" "Canada" ] ]
        Physical = { Schema = "dbo"; Table = "OSUSR_TEST_COUNTRY" }
        Attributes =
            [
                { SsKey = idKey;    Name = mkName "Id";    Type = Integer
                  Column = { ColumnName = "ID";    IsNullable = false }
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
                { SsKey = codeKey;  Name = mkName "Code";  Type = Text
                  Column = { ColumnName = "CODE";  IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
                { SsKey = labelKey; Name = mkName "Label"; Type = Text
                  Column = { ColumnName = "LABEL"; IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
    }

/// Non-static kind (no `Modality.Static` mark); should produce a no-op
/// DataInsertScript per T11 strict-equality keyset.
let private mkRegularKind () : Kind =
    let kindKey = mkKey ["TestModule"; "Customer"]
    let idKey = mkKey ["TestModule"; "Customer"; "Id"]
    let nameKey = mkKey ["TestModule"; "Customer"; "Name"]
    {
        SsKey    = kindKey
        Name     = mkName "Customer"
        Origin   = OsNative
        Modality = []  // not static
        Physical = { Schema = "dbo"; Table = "OSUSR_TEST_CUSTOMER" }
        Attributes =
            [
                { SsKey = idKey;   Name = mkName "Id";   Type = Integer
                  Column = { ColumnName = "ID";   IsNullable = false }
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = true; Description = None; IsActive = true  }
                { SsKey = nameKey; Name = mkName "Name"; Type = Text
                  Column = { ColumnName = "NAME"; IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
    }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds; IsActive = true  }
    { Modules = [ m ] }

/// Whitespace-normalize a rendered SQL string so substring assertions
/// match across formatter variations. ScriptDom's `Sql160ScriptGenerator`
/// emits canonical formatting (line breaks before `AS`, `SET`, etc.) that
/// differs from V1's hand-rolled spacing; the structural-content
/// property (every load-bearing token in expected order) is what these
/// tests check, not byte-exact whitespace.
let private normWs (s: string) : string =
    System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim()

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

[<Fact>]
let ``StaticSeedsEmitter.emit produces one DataInsertScript per kind (T11 keyset)`` () =
    let catalog = mkCatalog [ mkCountryKind (); mkRegularKind () ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.Equal (2, Map.count map)

[<Fact>]
let ``StaticSeedsEmitter.emit produces empty Phase1Merges for non-static kinds`` () =
    let regular = mkRegularKind ()
    let catalog = mkCatalog [ regular ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find regular.SsKey
    Assert.Empty script.Phase1Merges
    Assert.Equal<string> ("", script.Rendered)

[<Fact>]
let ``StaticSeedsEmitter.emit populates Phase1Merges for Modality.Static kinds`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Equal (2, List.length script.Phase1Merges)

[<Fact>]
let ``StaticSeedsEmitter.emit Phase1Merges carry KindKey + Identifier from StaticRow`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    for row in script.Phase1Merges do
        Assert.Equal<SsKey> (country.SsKey, row.KindKey)

[<Fact>]
let ``StaticSeedsEmitter.emit Phase2Updates is empty at slice α (no cycle-breaking yet)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``StaticSeedsEmitter.emit Rendered MERGE shape contains V1-required clauses`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // V1's six load-bearing MERGE clauses (per `StaticSeedSqlBuilder.cs:211-260`).
    // Per Tier-1 #1 (typed-AST MERGE via ScriptDom): formatting is the
    // canonical Sql160ScriptGenerator output (different whitespace
    // wrapping than V1's hand-rolled). Assertions normalize whitespace
    // so the structural-content property is what's checked, not exact
    // formatting.
    let r = normWs script.Rendered
    Assert.Contains ("MERGE INTO [dbo].[OSUSR_TEST_COUNTRY] AS [Target]", r)
    Assert.Contains ("USING", r)
    Assert.Contains ("VALUES", r)
    Assert.Contains ("AS [Source]([ID], [CODE], [LABEL])", r)
    Assert.Contains ("ON [Target].[ID] = [Source].[ID]", r)
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", r)
    Assert.Contains ("WHEN NOT MATCHED THEN INSERT", r)
    Assert.EndsWith ("GO", r)

[<Fact>]
let ``StaticSeedsEmitter.emit Rendered does NOT carry change-detection predicate (slice α; CDC awareness lands at slice β)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Slice α emits V1's WHEN MATCHED THEN UPDATE SET unconditional; the
    // change-detection-predicate (Target.col <> Source.col + null-aware
    // OR conditions) is the slice-β contribution that closes CDC-noise.
    let r = normWs script.Rendered
    Assert.DoesNotContain ("WHEN MATCHED AND", r)
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", r)

[<Fact>]
let ``T1: StaticSeedsEmitter.emit is byte-deterministic across repeat invocations`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let r1 = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let r2 = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)
    Assert.Equal<DataInsertRow list> (s1.Phase1Merges, s2.Phase1Merges)

[<Fact>]
let ``StaticSeedsEmitter.emit formats Text values with N-prefix + single-quote escaping`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Text columns get `N'...'` prefix per `Render.formatSqlLiteral`.
    Assert.Contains ("N'United States'", script.Rendered)
    Assert.Contains ("N'Canada'", script.Rendered)

[<Fact>]
let ``StaticSeedsEmitter.emit formats Integer values without quotes`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Integer values are bare digits per `Render.formatSqlLiteral`.
    // Note: this fixture uses Code-as-Id ("US"/"CA") so the Id column
    // gets formatted via Integer's identity branch — raw value stays
    // bare. Real production fixtures will carry numeric Ids.
    Assert.Contains ("US,", script.Rendered)
    Assert.Contains ("CA,", script.Rendered)

[<Fact>]
let ``T11: StaticSeedsEmitter.emit covers every catalog kind`` () =
    let country = mkCountryKind ()
    let regular = mkRegularKind ()
    let catalog = mkCatalog [ country; regular ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.True (Map.containsKey country.SsKey map)
    Assert.True (Map.containsKey regular.SsKey map)

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice β — CdcAwareness dispatch + change-detection MERGE
// predicate. The load-bearing semantic addition that closes CDC-silence
// on idempotent redeploys (per V2_DRIVER.md highest-stakes claim).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice β: CdcAwareness.empty has no CDC-enabled kinds`` () =
    Assert.True (Set.isEmpty CdcAwareness.empty.CdcEnabled)
    Assert.True (Map.isEmpty CdcAwareness.empty.CdcInstance)

[<Fact>]
let ``Slice β: CdcAwareness.isEnabled true iff key in enabled set`` () =
    let country = mkCountryKind ()
    let regular = mkRegularKind ()
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    Assert.True  (CdcAwareness.isEnabled country.SsKey cdc)
    Assert.False (CdcAwareness.isEnabled regular.SsKey cdc)

[<Fact>]
let ``Slice β: CdcAwareness.captureInstance returns Some when registered`` () =
    let country = mkCountryKind ()
    let cdc =
        CdcAwareness.create
            (Set.ofList [ country.SsKey ])
            (Map.ofList [ country.SsKey, "dbo_OSUSR_TEST_COUNTRY" ])
    Assert.Equal<string option>
        (Some "dbo_OSUSR_TEST_COUNTRY",
         CdcAwareness.captureInstance country.SsKey cdc)

[<Fact>]
let ``Slice β: StaticSeedsEmitter without CDC keeps V1 unconditional WHEN MATCHED`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // CdcAwareness.empty (no kinds enabled) → V1-shape MERGE.
    let r = normWs script.Rendered
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", r)
    Assert.DoesNotContain ("WHEN MATCHED AND", r)

[<Fact>]
let ``Slice β: StaticSeedsEmitter with CDC enabled emits change-detection predicate`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // CDC-enabled → WHEN MATCHED AND ( ... ) THEN UPDATE SET.
    let r = normWs script.Rendered
    Assert.Contains ("WHEN MATCHED AND (", r)
    Assert.Contains (") THEN UPDATE SET", r)
    Assert.DoesNotContain ("WHEN MATCHED THEN UPDATE SET", r)

[<Fact>]
let ``Slice β: change-detection predicate is nullable-aware (NULL-asymmetry both ways)`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    // Per pre-scope §6: NULL ≠ NULL in SQL; the predicate covers both
    // null-asymmetry directions (target NULL vs source NOT NULL, and
    // target NOT NULL vs source NULL). Verify on at least one non-key
    // column.
    let r = normWs script.Rendered
    // ScriptDom emits aliases bracketed (`[Target]` / `[Source]`).
    Assert.Contains ("[Target].[CODE] <> [Source].[CODE]", r)
    Assert.Contains ("[Target].[CODE] IS NULL AND [Source].[CODE] IS NOT NULL", r)
    Assert.Contains ("[Target].[CODE] IS NOT NULL AND [Source].[CODE] IS NULL", r)

[<Fact>]
let ``Slice β: change-detection predicate covers every non-key column`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit catalog profile |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    let r = normWs script.Rendered
    // Both non-key columns (CODE + LABEL) appear in the predicate.
    // ScriptDom emits aliases bracketed (`[Target]` / `[Source]`)
    // because they round-trip through `Identifier.EncodeIdentifier`.
    Assert.Contains ("[Target].[CODE] <> [Source].[CODE]", r)
    Assert.Contains ("[Target].[LABEL] <> [Source].[LABEL]", r)
    // ID is the PK; it should NOT appear in the change-detection
    // predicate (the predicate gates UPDATEs of NON-KEY columns).
    Assert.DoesNotContain ("[Target].[ID] <> [Source].[ID]", r)

[<Fact>]
let ``Slice β: per-kind dispatch — only CDC-enabled kinds get the predicate`` () =
    // Two static kinds in one catalog; only one is CDC-enabled.
    let country = mkCountryKind ()
    // Build a second static kind (Region) with same shape.
    let regionKey = mkKey ["TestModule"; "Region"]
    let region : Kind =
        { country with
            SsKey    = regionKey
            Name     = mkName "Region"
            Physical = { Schema = "dbo"; Table = "OSUSR_TEST_REGION" } }
    let catalog = mkCatalog [ country; region ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let artifact = StaticSeedsEmitter.emit catalog profile |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    let countryScript = Map.find country.SsKey map
    let regionScript = Map.find region.SsKey map
    let cR = normWs countryScript.Rendered
    let rR = normWs regionScript.Rendered
    // Country: CDC-enabled → predicate.
    Assert.Contains ("WHEN MATCHED AND (", cR)
    // Region: not CDC-enabled → V1-shape MERGE.
    Assert.DoesNotContain ("WHEN MATCHED AND (", rR)
    Assert.Contains ("WHEN MATCHED THEN UPDATE SET", rR)

[<Fact>]
let ``Slice β: T1 byte-determinism holds under CDC dispatch`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let cdc = CdcAwareness.create (Set.ofList [ country.SsKey ]) Map.empty
    let profile = { Profile.empty with CdcAwareness = cdc }
    let r1 = StaticSeedsEmitter.emit catalog profile |> mustOkEmit
    let r2 = StaticSeedsEmitter.emit catalog profile |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find country.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find country.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice δ — DataInsertScript.Phase2Updates + DeferredFkSet
// (two-phase insertion / cycle-breaking).
//
// V1 reference: `Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:88-148`
// (the empirical foundation V2 inherits) + `IdentifyNullableFKColumns:150`
// (the deferral predicate). V2 algebra: cycle-membership comes from
// `TopologicalOrderPass.Cycles`; nullable cycle-FK columns are NULLed
// in Phase-1 MERGE VALUES and populated in Phase-2 per-row UPDATEs;
// non-cycle / non-nullable FKs are not deferred.
// ---------------------------------------------------------------------------

/// Self-referencing kind: Tree (Id INT PK, Label TEXT, ParentId INT FK
/// nullable → Tree.Id). One Modality.Static row. The self-FK forms a
/// 1-node SCC; per `TopologicalOrderPass.SelfLoopPolicy = TreatAsCycle`
/// the kind appears in `Cycles`. Phase-1 MERGE NULLs ParentId; Phase-2
/// UPDATE re-populates it.
let private mkTreeKind () : Kind =
    let kindKey   = mkKey ["TestModule"; "Tree"]
    let idKey     = mkKey ["TestModule"; "Tree"; "Id"]
    let labelKey  = mkKey ["TestModule"; "Tree"; "Label"]
    let parentKey = mkKey ["TestModule"; "Tree"; "ParentId"]
    let refKey    = mkKey ["TestModule"; "Tree"; "RefParent"]
    let row =
        { Identifier = mkKey ["TestModule"; "Tree"; "Row"; "ROOT"]
          Values =
              Map.ofList
                  [ mkName "Id",       "1"
                    mkName "Label",    "root"
                    mkName "ParentId", "1" ] }
    {
        SsKey    = kindKey
        Name     = mkName "Tree"
        Origin   = OsNative
        Modality = [ Static [ row ] ]
        Physical = { Schema = "dbo"; Table = "OSUSR_TEST_TREE" }
        Attributes =
            [
                { SsKey = idKey;     Name = mkName "Id";       Type = Integer
                  Column = { ColumnName = "ID";       IsNullable = false }
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
                { SsKey = labelKey;  Name = mkName "Label";    Type = Text
                  Column = { ColumnName = "LABEL";    IsNullable = false }
                  IsPrimaryKey = false; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
                { SsKey = parentKey; Name = mkName "ParentId"; Type = Integer
                  Column = { ColumnName = "PARENTID"; IsNullable = true }     // nullable → deferrable
                  IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
            ]
        References =
            [
                { SsKey = refKey; Name = mkName "RefParent"
                  SourceAttribute = parentKey; TargetKind = kindKey
                  OnDelete = NoAction; IsUserFk = false }
            ]
        Indexes    = []
        Description = None
        IsActive = true
    }

/// Self-referencing kind whose FK column is NOT NULL. Same shape as
/// `mkTreeKind` but `ParentId` is non-nullable. The cycle still
/// surfaces via `TopologicalOrderPass.Cycles`, but slice δ MUST NOT
/// defer the column (NULLing would violate the constraint; V1's
/// `IdentifyNullableFKColumns:184` skips these too).
let private mkRigidTreeKind () : Kind =
    let tree = mkTreeKind ()
    let attrs =
        tree.Attributes
        |> List.map (fun a ->
            if a.Name = mkName "ParentId" then
                { a with Column = { a.Column with IsNullable = false }
                         IsMandatory = true }
            else a)
    { tree with Attributes = attrs }

[<Fact>]
let ``Slice δ: acyclic catalog produces empty DeferredFkSet on every Phase1 row`` () =
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    for row in script.Phase1Merges do
        Assert.True (Set.isEmpty row.DeferredFkSet)
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``Slice δ: self-referencing nullable FK populates DeferredFkSet`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    Assert.Equal (1, List.length script.Phase1Merges)
    let phase1 = List.head script.Phase1Merges
    // The nullable self-FK attribute (ParentId) is the deferred column.
    Assert.True (Set.contains (mkName "ParentId") phase1.DeferredFkSet)
    // Other columns NOT deferred (Id PK; Label non-FK).
    Assert.False (Set.contains (mkName "Id") phase1.DeferredFkSet)
    Assert.False (Set.contains (mkName "Label") phase1.DeferredFkSet)

[<Fact>]
let ``Slice δ: self-FK kind produces one Phase2Updates row per Phase1 row`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    Assert.Equal (List.length script.Phase1Merges, List.length script.Phase2Updates)
    // Phase2 rows carry the same (KindKey, Identifier, DeferredFkSet)
    // as their Phase1 counterparts — same logical row, two phases.
    let p1Identities = script.Phase1Merges |> List.map (fun r -> r.KindKey, r.Identifier, r.DeferredFkSet)
    let p2Identities = script.Phase2Updates |> List.map (fun r -> r.KindKey, r.Identifier, r.DeferredFkSet)
    Assert.Equal<(SsKey * SsKey * Set<Name>) list> (p1Identities, p2Identities)

[<Fact>]
let ``Slice δ: NOT NULL FK in cycle is NOT deferred`` () =
    let rigid = mkRigidTreeKind ()
    let catalog = mkCatalog [ rigid ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find rigid.SsKey
    // Cycle membership holds (TopologicalOrderPass sees the self-edge
    // under TreatAsCycle), but the FK column is non-nullable so V1's
    // deferral predicate (and V2's mirror) refuses to defer.
    let phase1 = List.head script.Phase1Merges
    Assert.True (Set.isEmpty phase1.DeferredFkSet)
    Assert.Empty script.Phase2Updates

[<Fact>]
let ``Slice δ: Phase1 MERGE renders deferred column as NULL in VALUES`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    let r = normWs script.Rendered
    // The MERGE's USING (VALUES (...)) row should carry NULL in the
    // PARENTID slot rather than the row's raw value (1). The Source
    // column list still names PARENTID — only the literal in VALUES
    // changes. Per ScriptDom's Sql160ScriptGenerator NullLiteral output
    // = `NULL`. Look for the row tuple: (1, N'root', NULL).
    Assert.Contains ("(1, N'root', NULL)", r)
    // And the Source column list still names PARENTID structurally.
    Assert.Contains ("AS [Source]([ID], [LABEL], [PARENTID])", r)

[<Fact>]
let ``Slice δ: Phase2 UPDATE references PK in WHERE + deferred column in SET`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find tree.SsKey
    let r = normWs script.Rendered
    Assert.Contains ("UPDATE [dbo].[OSUSR_TEST_TREE]", r)
    // SET clause carries the original (deferred) value — 1.
    Assert.Contains ("SET [PARENTID] = 1", r)
    // WHERE-clause scopes to the row's PK (Id = 1).
    Assert.Contains ("WHERE [ID] = 1", r)

[<Fact>]
let ``Slice δ: 2-cycle with both FKs nullable defers FK column on each kind`` () =
    // Two Static kinds A ↔ B both nullable FK forms a 2-member SCC.
    // Both kinds should receive Phase-2 deferral for their cross-FK.
    let aKey  = mkKey ["TestModule"; "A"]
    let bKey  = mkKey ["TestModule"; "B"]
    let aIdK  = mkKey ["TestModule"; "A"; "Id"]
    let aFkK  = mkKey ["TestModule"; "A"; "BId"]
    let bIdK  = mkKey ["TestModule"; "B"; "Id"]
    let bFkK  = mkKey ["TestModule"; "B"; "AId"]
    let aRefK = mkKey ["TestModule"; "A"; "ToB"]
    let bRefK = mkKey ["TestModule"; "B"; "ToA"]
    let aRow =
        { Identifier = mkKey ["TestModule"; "A"; "Row"; "1"]
          Values = Map.ofList [ mkName "Id", "1"; mkName "BId", "1" ] }
    let bRow =
        { Identifier = mkKey ["TestModule"; "B"; "Row"; "1"]
          Values = Map.ofList [ mkName "Id", "1"; mkName "AId", "1" ] }
    let mkAttr ssk name typ col isPk isNull =
        { SsKey = ssk; Name = mkName name; Type = typ
          Column = { ColumnName = col; IsNullable = isNull }
          IsPrimaryKey = isPk; IsMandatory = not isNull
          Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true  }
    let mkRef ssk name srcAttr tgt =
        { SsKey = ssk; Name = mkName name
          SourceAttribute = srcAttr; TargetKind = tgt; OnDelete = NoAction; IsUserFk = false }
    let aKind : Kind =
        { SsKey = aKey; Name = mkName "A"; Origin = OsNative
          Modality = [ Static [ aRow ] ]
          Physical = { Schema = "dbo"; Table = "OSUSR_A" }
          Attributes = [ mkAttr aIdK "Id"  Integer "ID"  true false
                         mkAttr aFkK "BId" Integer "BID" false true ]
          References = [ mkRef aRefK "ToB" aFkK bKey ]
          Indexes    = []
          Description = None; IsActive = true  }
    let bKind : Kind =
        { SsKey = bKey; Name = mkName "B"; Origin = OsNative
          Modality = [ Static [ bRow ] ]
          Physical = { Schema = "dbo"; Table = "OSUSR_B" }
          Attributes = [ mkAttr bIdK "Id"  Integer "ID"  true false
                         mkAttr bFkK "AId" Integer "AID" false true ]
          References = [ mkRef bRefK "ToA" bFkK aKey ]
          Indexes    = []
          Description = None; IsActive = true  }
    let catalog = mkCatalog [ aKind; bKind ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let m = ArtifactByKind.toMap artifact
    let aScript = Map.find aKey m
    let bScript = Map.find bKey m
    // Each kind defers its own FK to the other.
    Assert.True (Set.contains (mkName "BId") (List.head aScript.Phase1Merges).DeferredFkSet)
    Assert.True (Set.contains (mkName "AId") (List.head bScript.Phase1Merges).DeferredFkSet)
    // And each kind has Phase2Updates populated.
    Assert.NotEmpty aScript.Phase2Updates
    Assert.NotEmpty bScript.Phase2Updates

[<Fact>]
let ``T1 (slice δ): byte-determinism holds across repeat invocations under cycle-breaking`` () =
    let tree = mkTreeKind ()
    let catalog = mkCatalog [ tree ]
    let r1 = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let r2 = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1 |> Map.find tree.SsKey
    let s2 = ArtifactByKind.toMap r2 |> Map.find tree.SsKey
    Assert.Equal<string> (s1.Rendered, s2.Rendered)
    Assert.Equal<DataInsertRow list> (s1.Phase1Merges, s2.Phase1Merges)
    Assert.Equal<DataInsertRow list> (s1.Phase2Updates, s2.Phase2Updates)

[<Fact>]
let ``Slice δ: slice α/β rows pre-existing carry empty DeferredFkSet (acyclic invariant)`` () =
    // Country (slice α/β fixture; no FKs at all) MUST have empty
    // DeferredFkSet on every Phase1 row + empty Phase2Updates. Catches
    // the case where slice δ accidentally populates the field on
    // non-cycle kinds.
    let country = mkCountryKind ()
    let catalog = mkCatalog [ country ]
    let artifact = StaticSeedsEmitter.emit catalog Profile.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find country.SsKey
    Assert.All (script.Phase1Merges, fun row -> Assert.Empty row.DeferredFkSet)
    Assert.Empty script.Phase2Updates

module Projection.Tests.ActConsentTests

// THE ACT CONSENT ALPHABET (2026-07-10, the transfer-manifest program, slice
// 4a): pure witnesses over the closed act DU, the deterministic fingerprints,
// and THE act derivation (`ActConsent.actsOf` — the one derivation the board's
// consent axis narrates and the 4b execute gate enforces). The laws under
// test: the canonical value renderer alone; the effect hash re-opens on ANY
// substrate drift (a changed pair, a new unmatched value, a target duplicate,
// a re-toggled resolution) and on nothing else; fingerprint text round-trips
// totally; the relocated wipe / identity-insert derivations stay byte-
// identical to their pre-relocation delegating names.

open Xunit
open Projection.Core
open Projection.Pipeline

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "AC_KIND" [ s ] |> Result.value
let private aKey (k: string) (a: string) : SsKey = SsKey.synthesizedComposite "AC_ATTR" [ k; a ] |> Result.value
let private rKey (k: string) (r: string) : SsKey = SsKey.synthesizedComposite "AC_REF" [ k; r ] |> Result.value

let private idPk (kind: string) : Attribute =
    { Attribute.create (aKey kind "Id") (nm "Id") Integer with
        Column = ColumnRealization.create "ID" false |> Result.value
        IsPrimaryKey = true; IsIdentity = true; IsMandatory = true }

let private fkCol (kind: string) (logical: string) : Attribute =
    { Attribute.create (aKey kind logical) (nm logical) Integer with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        IsMandatory = true }

/// Parent ← Child (Child.ParentId → Parent); both identity-PK kinds.
let private catalog : Catalog =
    let parent =
        Kind.create (kKey "Parent") (nm "Parent") (TableId.create "dbo" "OSUSR_AC_PARENT" |> Result.value)
            [ idPk "Parent" ]
    let child =
        { Kind.create (kKey "Child") (nm "Child") (TableId.create "dbo" "OSUSR_AC_CHILD" |> Result.value)
            [ idPk "Child"; fkCol "Child" "ParentId" ] with
            References = [ Reference.create (rKey "Child" "Parent") (nm "ParentId") (aKey "Child" "ParentId") (kKey "Parent") ] }
    let m =
        Module.create (SsKey.synthesizedComposite "AC_MOD" [ "Acts" ] |> Result.value)
            (nm "Acts") [ parent; child ] true []
        |> Result.value
    Catalog.create [ m ] [] |> Result.value

let private row (kind: string) (values: (string * string) list) : StaticRow =
    { Identifier = kKey kind; Values = values |> List.map (fun (c, v) -> nm c, v) |> Map.ofList }

let private load (kind: string) (disposition: IdentityDisposition) (rows: StaticRow list) : DataLoadKind =
    { Kind = kKey kind; Disposition = disposition; DeferredFkColumns = Set.empty; Rows = rows }

let private planOf (loads: DataLoadKind list) (skipped: (SsKey * UnresolvedReference) list) : DataLoadPlan =
    { Loads = loads; UnbreakableCycleFks = []; SkippedReferences = skipped; DroppedRows = [] }

let private topoOf (order: SsKey list) : TopologicalOrder =
    { Mode = Topological; Order = order; Edges = []; MissingEdges = []; Cycles = []; Diagnostics = [] }

let private label (k: SsKey) : string = SsKey.rootOriginal k

let private substrate : ActConsent.EffectSubstrate =
    { Token = "match:Parent"
      Resolution = "reconcile:Email"
      MatchedPairs = [ "alice@x", "71"; "bob@x", "72" ]
      UnmatchedValues = [ "carol@x" ]
      SinkTotal = 2L
      SinkDistinct = 2L
      PlannedCount = 2 }

let private hexOf (fp: ActConsent.ActFingerprint) : string =
    match fp with
    | ActConsent.ActFingerprint.Effect h -> h
    | other -> failwithf "expected an effect fingerprint, got %A" other

// -- the canonical value renderer, alone --------------------------------------

[<Fact>]
let ``canonicalValue is the identity on every real string — no case folding, no trimming, no culture`` () =
    for s in [ ""; " "; "  padded  "; "MiXeD"; "1,5"; "1.5"; "\t"; "ünïcode✓"; "" ] do
        Assert.Equal<string>(s, ActConsent.canonicalValue s)

[<Fact>]
let ``the absent marker aliases no canonicalValue output of a SQL-read string`` () =
    // The readers render SQL NULL as "" and no SQL string carries NUL, so a
    // marker embedding NUL is out of band by construction.
    Assert.Contains('\u0000', ActConsent.absent)
    for s in [ ""; "null"; "NULL"; " " ] do
        Assert.NotEqual<string>(ActConsent.absent, ActConsent.canonicalValue s)

// -- the effect hash: deterministic, order-free, drift-sensitive ---------------

[<Fact>]
let ``effectFingerprint is deterministic and 64 lowercase hex`` () =
    let a = hexOf (ActConsent.effectFingerprint substrate)
    let b = hexOf (ActConsent.effectFingerprint substrate)
    Assert.Equal<string>(a, b)
    Assert.Equal(64, a.Length)
    Assert.Equal<string>(a, a.ToLowerInvariant())

[<Fact>]
let ``effectFingerprint does not depend on the order the matched pairs arrive in — records sort ordinally`` () =
    let shuffled = { substrate with MatchedPairs = List.rev substrate.MatchedPairs }
    Assert.Equal<string>(hexOf (ActConsent.effectFingerprint substrate), hexOf (ActConsent.effectFingerprint shuffled))

[<Fact>]
let ``a business-key edit re-opens the act at constant pair count and constant sink counts`` () =
    let edited = { substrate with MatchedPairs = [ "alice@x", "71"; "bob@y", "72" ] }
    Assert.NotEqual<string>(hexOf (ActConsent.effectFingerprint substrate), hexOf (ActConsent.effectFingerprint edited))

[<Fact>]
let ``a re-pointed target identity re-opens the act at constant business keys`` () =
    let repointed = { substrate with MatchedPairs = [ "alice@x", "71"; "bob@x", "99" ] }
    Assert.NotEqual<string>(hexOf (ActConsent.effectFingerprint substrate), hexOf (ActConsent.effectFingerprint repointed))

[<Fact>]
let ``a duplicate appearing on the target re-opens the act — the trailer carries the exact sink counts`` () =
    let duplicated = { substrate with SinkTotal = 3L; SinkDistinct = 2L }
    Assert.NotEqual<string>(hexOf (ActConsent.effectFingerprint substrate), hexOf (ActConsent.effectFingerprint duplicated))

[<Fact>]
let ``a re-toggled resolution re-opens the act at constant population — Resolve A then B is two different consents`` () =
    let retoggled = { substrate with Resolution = "reconcile:Name" }
    Assert.NotEqual<string>(hexOf (ActConsent.effectFingerprint substrate), hexOf (ActConsent.effectFingerprint retoggled))

[<Fact>]
let ``a new unmatched value re-opens the act — an unresolved reference is part of the effect`` () =
    let grown = { substrate with UnmatchedValues = "carol@x" :: [ "dave@x" ] }
    Assert.NotEqual<string>(hexOf (ActConsent.effectFingerprint substrate), hexOf (ActConsent.effectFingerprint grown))

[<Fact>]
let ``an unmatched value cannot masquerade as a matched pair — the absent marker is out of band`` () =
    // Same flat value multiset, different meaning: matched ("x" -> "") vs unmatched "x".
    let asMatched = { substrate with MatchedPairs = [ "x", "" ]; UnmatchedValues = [] }
    let asUnmatched = { substrate with MatchedPairs = []; UnmatchedValues = [ "x" ] }
    Assert.NotEqual<string>(hexOf (ActConsent.effectFingerprint asMatched), hexOf (ActConsent.effectFingerprint asUnmatched))

// -- fingerprint text: total round-trip ----------------------------------------

[<Fact>]
let ``fingerprint text round-trips for both shapes, including hostile population keys`` () =
    let effect = ActConsent.effectFingerprint substrate
    let hostile = ActConsent.populationFingerprint "a:b%c" "d.e:f" 42
    for fp in [ effect; hostile; ActConsent.populationFingerprint "" "" 0 ] do
        Assert.Equal<ActConsent.ActFingerprint option>(Some fp, ActConsent.parseFingerprint (ActConsent.fingerprintText fp))

[<Fact>]
let ``parseFingerprint is total: junk, uppercase hex, and short hex are None, never a throw`` () =
    for s in [ ""; "effect:"; "effect:ABC"; "effect:" + String.replicate 64 "G"
               "effect:" + (String.replicate 64 "a").ToUpperInvariant().Substring(0, 63) + "!"
               "population:1:2"; "population:1:2:notanumber"; "population:1:2:3:4"; "sha:deadbeef" ] do
        Assert.Equal<ActConsent.ActFingerprint option>(None, ActConsent.parseFingerprint s)
    // uppercase hex is refused — a blessing is copied, never re-cased.
    let upper = "effect:" + (hexOf (ActConsent.effectFingerprint substrate)).ToUpperInvariant()
    Assert.Equal<ActConsent.ActFingerprint option>(None, ActConsent.parseFingerprint upper)

// -- the act derivation ----------------------------------------------------------

let private parentRows = [ row "Parent" [ "Id", "1" ]; row "Parent" [ "Id", "2" ] ]
let private childRows = [ row "Child" [ "Id", "10"; "ParentId", "1" ] ]

[<Fact>]
let ``actsOf emits wipes only under WipeAndLoad, child-first, never for a reconciled kind`` () =
    let plan =
        planOf
            [ load "Parent" IdentityDisposition.ReconciledByRule []
              load "Child" IdentityDisposition.AssignedBySink childRows ]
            []
    let topo = topoOf [ kKey "Parent"; kKey "Child" ]
    let wipeActs (emission: EmissionMode) =
        ActConsent.actsOf label catalog plan topo None (Set.ofList [ kKey "Parent" ]) emission
        |> List.choose (function ActConsent.Act.Wipe t -> Some t | _ -> None)
    Assert.Equal<SsKey list>([ kKey "Child" ], wipeActs EmissionMode.WipeAndLoad)
    Assert.Empty(wipeActs EmissionMode.Incremental)

[<Fact>]
let ``the relocated wipeTargets stays byte-identical through its delegating name`` () =
    let plan =
        planOf
            [ load "Parent" IdentityDisposition.AssignedBySink parentRows
              load "Child" IdentityDisposition.AssignedBySink childRows ]
            []
    let topo = topoOf [ kKey "Parent"; kKey "Child" ]
    for loadSet in [ None; Some (Set.ofList [ kKey "Child" ]); Some Set.empty ] do
        Assert.Equal<SsKey list>(ActConsent.wipeTargets plan topo loadSet, TransferResume.wipeTargets plan topo loadSet)

[<Fact>]
let ``the relocated identityInsertTables stays byte-identical through its delegating name`` () =
    let plan =
        planOf
            [ load "Parent" IdentityDisposition.PreservedFromSource parentRows
              load "Child" IdentityDisposition.AssignedBySink childRows ]
            []
    Assert.Equal<string list>(ActConsent.identityInsertTables catalog plan, Transfer.identityInsertTables catalog plan)
    Assert.Equal<string list>([ "Parent" ], ActConsent.identityInsertTables catalog plan)

[<Fact>]
let ``actsOf derives mint, re-key, match, and drop from the plan — sorted by token, distinct`` () =
    let dropped : UnresolvedReference =
        { Column = nm "ParentId"; Target = kKey "Parent"; UnresolvedSource = SourceKey.ofString "7" }
    let plan =
        planOf
            [ load "Parent" IdentityDisposition.AssignedBySink parentRows
              load "Child" IdentityDisposition.AssignedBySink childRows ]
            [ kKey "Child", dropped; kKey "Child", dropped ]
    let topo = topoOf [ kKey "Parent"; kKey "Child" ]
    let acts = ActConsent.actsOf label catalog plan topo None (Set.ofList [ kKey "Realm" ]) EmissionMode.Incremental
    let tokens = acts |> List.map (ActConsent.tokenOf label)
    // sorted + distinct (the duplicate skipped reference folds to one Drop act)
    Assert.Equal<string list>(tokens |> List.distinct |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)), tokens)
    let tok (a: ActConsent.Act) = ActConsent.tokenOf label a
    Assert.Contains(tok (ActConsent.Act.Mint (kKey "Parent")), tokens)
    Assert.Contains(tok (ActConsent.Act.Mint (kKey "Child")), tokens)
    Assert.Contains(tok (ActConsent.Act.Rekey (kKey "Child", nm "ParentId")), tokens)
    Assert.Contains(tok (ActConsent.Act.Match (kKey "Realm")), tokens)
    Assert.Contains(tok (ActConsent.Act.Drop (kKey "Child", nm "ParentId")), tokens)
    Assert.Equal(1, tokens |> List.filter ((=) (tok (ActConsent.Act.Drop (kKey "Child", nm "ParentId")))) |> List.length)

[<Fact>]
let ``every act carries a complete operator statement and a distinct canonical token`` () =
    let acts =
        [ ActConsent.Act.Wipe (kKey "Parent")
          ActConsent.Act.IdentityInsert (kKey "Parent")
          ActConsent.Act.DeleteScope (kKey "Parent")
          ActConsent.Act.Mint (kKey "Parent")
          ActConsent.Act.Rekey (kKey "Child", nm "ParentId")
          ActConsent.Act.Match (kKey "Parent")
          ActConsent.Act.Resolve (kKey "Parent", "reconcile:Email")
          ActConsent.Act.Drop (kKey "Child", nm "ParentId") ]
    let tokens = acts |> List.map (ActConsent.tokenOf label)
    Assert.Equal(tokens.Length, (List.distinct tokens).Length)
    for act in acts do
        let statement = ActConsent.describe label act
        Assert.False(System.String.IsNullOrWhiteSpace statement)
        Assert.EndsWith(".", statement)

// -- the fingerprint derivation (ActEvidence) ------------------------------------

[<Fact>]
let ``fingerprintsOf pins a wipe to the probed sink population and a mint to the plan's own rows`` () =
    let plan =
        planOf
            [ load "Parent" IdentityDisposition.AssignedBySink parentRows
              load "Child" IdentityDisposition.AssignedBySink childRows ]
            []
    let topo = topoOf [ kKey "Parent"; kKey "Child" ]
    let acts = ActConsent.actsOf label catalog plan topo None Set.empty EmissionMode.WipeAndLoad
    let probes = Map.ofList [ kKey "Parent", ({ FirstKey = "1"; LastKey = "9"; RowCount = 9 } : ActEvidence.PopulationProbe) ]
    let fps = ActEvidence.fingerprintsOf label catalog plan None Map.empty probes acts
    let tok (a: ActConsent.Act) = ActConsent.tokenOf label a
    Assert.Equal<ActConsent.ActFingerprint option>(
        Some (ActConsent.populationFingerprint "1" "9" 9), fps |> Map.tryFind (tok (ActConsent.Act.Wipe (kKey "Parent"))))
    Assert.Equal<ActConsent.ActFingerprint option>(
        Some (ActConsent.populationFingerprint "1" "2" 2), fps |> Map.tryFind (tok (ActConsent.Act.Mint (kKey "Parent"))))
    // an unprobed wipe has NO fingerprint — absence is named, never invented.
    Assert.Equal<ActConsent.ActFingerprint option>(None, fps |> Map.tryFind (tok (ActConsent.Act.Wipe (kKey "Child"))))

[<Fact>]
let ``a source row edit re-opens the re-key act — the fingerprint pins the (row, reference) correspondence`` () =
    let planA =
        planOf [ load "Parent" IdentityDisposition.AssignedBySink parentRows
                 load "Child" IdentityDisposition.AssignedBySink childRows ] []
    let planB =
        planOf [ load "Parent" IdentityDisposition.AssignedBySink parentRows
                 load "Child" IdentityDisposition.AssignedBySink [ row "Child" [ "Id", "10"; "ParentId", "2" ] ] ] []
    let topo = topoOf [ kKey "Parent"; kKey "Child" ]
    let fpOf plan =
        let acts = ActConsent.actsOf label catalog plan topo None Set.empty EmissionMode.Incremental
        ActEvidence.fingerprintsOf label catalog plan None Map.empty Map.empty acts
        |> Map.tryFind (ActConsent.tokenOf label (ActConsent.Act.Rekey (kKey "Child", nm "ParentId")))
    Assert.True((fpOf planA).IsSome)
    Assert.NotEqual<ActConsent.ActFingerprint option>(fpOf planA, fpOf planB)

[<Fact>]
let ``the match fingerprint reads the SAME evidence-cache products the workbench forecast reads`` () =
    let cache : EvidenceCache.Cache =
        { SourceRows = Map.ofList [ kKey "Parent", [ row "Parent" [ "Id", "1"; "Email", "alice@x" ] ] ]
          SinkRows   = Map.ofList [ kKey "Parent", [ row "Parent" [ "Id", "71"; "Email", "alice@x" ] ] ]
          References = Map.empty
          Uniqueness = Map.ofList [ (kKey "Parent", nm "Email"), (1L, 1L) ] }
    let plan = planOf [] []
    let acts = [ ActConsent.Act.Match (kKey "Parent") ]
    let strategies = Map.ofList [ kKey "Parent", ReconciliationStrategy.MatchByColumn (nm "Email") ]
    let fps = ActEvidence.fingerprintsOf label catalog plan (Some cache) strategies Map.empty acts
    let pairs, unmatched, counts = EvidenceCache.matchProducts cache catalog (kKey "Parent") (nm "Email")
    let token = ActConsent.tokenOf label (ActConsent.Act.Match (kKey "Parent"))
    let expected =
        ActConsent.effectFingerprint
            { Token = token; Resolution = "reconcile:Email"
              MatchedPairs = pairs; UnmatchedValues = unmatched
              SinkTotal = 1L; SinkDistinct = 1L; PlannedCount = List.length pairs }
    Assert.Equal<ActConsent.ActFingerprint option>(Some expected, fps |> Map.tryFind token)
    Assert.Equal<string list>([], unmatched)
    Assert.Equal(1, List.length pairs)
    ignore counts

// -- slice 4b: severity, the gate's exit class, and the bless-all bridge -------

[<Fact>]
let ``severity orders the alphabet by consequence — the wipe leads, the match trails, total over the closed DU`` () =
    let acts =
        [ ActConsent.Act.Wipe (kKey "Parent")
          ActConsent.Act.IdentityInsert (kKey "Parent")
          ActConsent.Act.DeleteScope (kKey "Parent")
          ActConsent.Act.Drop (kKey "Child", nm "ParentId")
          ActConsent.Act.Mint (kKey "Parent")
          ActConsent.Act.Rekey (kKey "Child", nm "ParentId")
          ActConsent.Act.Resolve (kKey "Parent", "reconcile:Email")
          ActConsent.Act.Match (kKey "Parent") ]
    let ranks = acts |> List.map ActConsent.severity
    Assert.Equal<int list>(List.sort ranks, ranks)                       // listed most- to least-severe
    Assert.Equal(ranks.Length, (List.distinct ranks).Length)             // a total order, no ties
    Assert.Equal(0, ActConsent.severity (ActConsent.Act.Wipe (kKey "X")))

[<Fact>]
let ``the write-consent codes classify onto the destructive exit (9), never the unclassified 3`` () =
    Assert.Equal((9, Preflight.ConsentWithheld), Preflight.classify "transfer.writeSignoff.actUnblessed")
    Assert.Equal((9, Preflight.ConsentWithheld), Preflight.classify "transfer.writeSignoff.ungreenlit")

[<Fact>]
let ``the bless-all bridge re-blesses exactly the fingerprints an actUnblessed refusal names — unread acts stay unblessed`` () =
    let fp = ActConsent.effectFingerprint substrate
    let refusal =
        ValidationError.createWithMetadata
            "transfer.writeSignoff.actUnblessed"
            "2 act(s) this run performs are not blessed at their current fingerprint."
            (Map.ofList
                [ "match:Parent", Some (sprintf "%s — No Parent rows are written." (ActConsent.fingerprintText fp))
                  "wipe:Parent", Some (sprintf "%s — Every row of Parent is deleted." (ActConsent.fingerprintText (ActConsent.populationFingerprint "1" "9" 9)))
                  "mint:Ghost", Some "unread — The target mints a new primary key." ])
    let blessings = TransferActs.blessingsOf refusal
    Assert.Equal(2, blessings.Length)
    Assert.Contains(blessings, fun b -> b.Act = "match:Parent" && b.Fingerprint = fp)
    Assert.Contains(blessings, fun b -> b.Act = "wipe:Parent" && b.Fingerprint = ActConsent.populationFingerprint "1" "9" 9)
    Assert.DoesNotContain(blessings, fun b -> b.Act = "mint:Ghost")

[<Fact>]
let ``a pinned owner's match fingerprint derives from the authored key map alone — pure config, no read needed`` () =
    let plan = planOf [] []
    let acts = [ ActConsent.Act.Match (kKey "Parent") ]
    let pin = Map.ofList [ SourceKey.ofString "1", AssignedKey.ofString "71"; SourceKey.ofString "2", AssignedKey.ofString "71" ]
    let strategies = Map.ofList [ kKey "Parent", ReconciliationStrategy.ManualOverride pin ]
    let fps = ActEvidence.fingerprintsOf label catalog plan None strategies Map.empty acts
    let token = ActConsent.tokenOf label (ActConsent.Act.Match (kKey "Parent"))
    let expected =
        ActConsent.effectFingerprint
            { Token = token; Resolution = "pinned"
              MatchedPairs = [ "1", "71"; "2", "71" ]; UnmatchedValues = []
              SinkTotal = 0L; SinkDistinct = 0L; PlannedCount = 2 }
    Assert.Equal<ActConsent.ActFingerprint option>(Some expected, fps |> Map.tryFind token)
    // re-pointing the pin re-opens the act: a different map, a different hash.
    let repointed = Map.ofList [ kKey "Parent", ReconciliationStrategy.ManualOverride (Map.ofList [ SourceKey.ofString "1", AssignedKey.ofString "72"; SourceKey.ofString "2", AssignedKey.ofString "72" ]) ]
    let fps2 = ActEvidence.fingerprintsOf label catalog plan None repointed Map.empty acts
    Assert.NotEqual<ActConsent.ActFingerprint option>(fps |> Map.tryFind token, fps2 |> Map.tryFind token)

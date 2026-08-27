namespace Twin.Core

open System.Text.Json
open Projection.Core

/// THE TWIN — evidence (Twin.Core, pure).
///
/// The durable, coordinate-keyed distribution evidence the mint rides
/// on. Two tiers of one shape:
///
///   **rich** — everything the profiler captured, including literal
///     values (categorical frequencies, numeric percentiles). Lives
///     OUT of the repository.
///   **shape** — the committed tier: counts, null rates, distinct
///     counts, lengths, truncation flags, fan-out shapes — and **no
///     captured literal of any kind** (law 3, property-tested).
///
/// The pack's wire format carries coordinates and scalars only — no
/// SsKey, no engine type — so it survives ejection unchanged and can be
/// reviewed line by line in the repository.
///
/// Rebinding is the identity ACL applied to evidence:
///   `ofProfile`  — capture-side: engine Profile → coordinate-keyed pack
///     (the capture catalog's names are the map);
///   `toProfile`  — mint-side: pack → engine Profile against the twin
///     catalog (law 2 — an unbound coordinate refuses by name);
///   `layer`      — precedence: a later profile's evidence replaces the
///     earlier per attribute (rich over shape; the scenario overlay
///     rides above both, in the compiler).
type NumericShape = {
    Min : decimal; P25 : decimal; P50 : decimal; P75 : decimal
    P95 : decimal; P99 : decimal; Max : decimal
}

/// The string-plane realities the deploy engine actually trips on —
/// COUNTS ONLY, never values, so both tiers keep the whole record
/// (masked by construction). Captured by the twin-side reality probe
/// during import; absent for non-text columns and for packs captured
/// before this axis existed.
type TextShape = {
    /// Rows holding the EMPTY STRING — distinct from NULL (a NOT NULL
    /// flip passes over `''` while the application treats it as missing;
    /// the row-fidelity digest already separates the two bytes).
    EmptyCount         : int64
    /// Rows whose value carries a trailing space (unique indexes compare
    /// under ANSI padding, so `'x '` and `'x'` collide there while `=`
    /// folds them — the pad-fold trap, witnessed synthetically).
    TrailingSpaceCount : int64
    /// Distinct values that fold together under UPPER — the pairs a
    /// CI-collation unique add refuses. Computed only for bounded-length
    /// columns (the indexable ones); 0 elsewhere.
    CaseCollisions     : int64
    /// The length distribution beyond the max: LEN's median and 90th
    /// percentile over non-null rows (advisory fidelity margins).
    LengthP50          : int option
    LengthP90          : int option
}

type ColumnEvidence = {
    Column        : string
    RowCount      : int64
    NullCount     : int64
    MaxLength     : int option
    DistinctCount : int64 option
    Truncated     : bool
    /// Deployed evidence: at least one value appears in two or more
    /// rows. A boolean carries no captured literal, so the shape tier
    /// keeps it.
    HasDuplicates : bool
    /// Rich tier only; `[]` in the shape tier.
    Frequencies   : (string * int64) list
    /// Rich tier only; `None` in the shape tier.
    Numeric       : NumericShape option
    /// String-plane counts (both tiers — literal-free by construction);
    /// `None` for non-text columns and pre-F1 packs.
    Text          : TextShape option
}

type TableEvidence = {
    Table    : string
    RowCount : int64
    Columns  : ColumnEvidence list
}

/// Child-per-parent fan-out for one relationship, addressed by the
/// child table + FK column (+ the parent, to disambiguate a column
/// carrying several relationships is impossible in SQL — one FK column,
/// one target — but the parent names the edge for the reader).
type FanOutEvidence = {
    ChildTable  : string
    ChildColumn : string
    ParentTable : string
    Shape       : NumericShape
}

/// Referential-integrity reality for one edge: how many child rows name
/// a parent that does not exist. Emitted only when the capture observed
/// at least one orphan. A count is literal-free, so the shape tier keeps
/// it. The edge may have NO enforcing reference in the estate — that is
/// the FK-add case this axis exists for — so mint-side binding treats an
/// absent reference as carried-for-the-witness, never as a refusal.
type OrphanEvidence = {
    ChildTable  : string
    ChildColumn : string
    ParentTable : string
    OrphanCount : int64
}

/// Foreign-key selectivity as counts by rank: how many child rows the
/// most-referenced parent holds, then the second-most, and so on. The
/// captured parent-key VALUES are dropped at capture — the mint consumes
/// counts by rank only — so this axis is literal-free in both tiers by
/// construction.
type SelectivityEvidence = {
    ChildTable    : string
    ChildColumn   : string
    ParentTable   : string
    DistinctCount : int64
    /// Per-rank child counts, descending.
    Counts        : int64 list
}

/// Multi-FK joint co-occurrence for one table: which FK-value tuples
/// occur together, and how often. Tuple keys carry real parent-key
/// values, so this axis is RICH ONLY — `deriveShape` drops it.
type JointEvidence = {
    Table         : string
    /// The participating FK column names, in tuple order.
    Columns       : string list
    DistinctCount : int64
    Frequencies   : (string * int64) list
}

type EvidenceTier =
    | ShapeTier
    | RichTier

type EvidencePack = {
    Tier          : EvidenceTier
    /// Provenance labels — the source names that contributed.
    Sources       : string list
    Tables        : TableEvidence list
    FanOuts       : FanOutEvidence list
    Orphans       : OrphanEvidence list
    Selectivities : SelectivityEvidence list
    /// Rich tier only; `[]` in the shape tier.
    Joints        : JointEvidence list
}

[<RequireQualifiedAccess>]
module Evidence =

    let emptyPack (tier: EvidenceTier) : EvidencePack =
        { Tier = tier; Sources = []; Tables = []; FanOuts = []
          Orphans = []; Selectivities = []; Joints = [] }

    // ------------------------------------------------------------------
    // Capture-side rebinding: Profile → pack (rich).
    // ------------------------------------------------------------------

    /// The capture-side name map: engine keys → coordinate texts. Built
    /// from the capture catalog, filtered to the closed table set.
    type private CaptureMap = {
        KindCoord   : Map<SsKey, string>
        AttrCoord   : Map<SsKey, string * string>          // key → (table, column)
        RefCoord    : Map<SsKey, string * string * string> // key → (childTable, childColumn, parentTable)
    }

    let private captureMapOf (catalog: Catalog) (keep: Kind -> string option) : CaptureMap =
        let kinds =
            Catalog.allKinds catalog
            |> List.choose (fun k -> keep k |> Option.map (fun coord -> k, coord))
        let kindCoord = kinds |> List.map (fun (k, c) -> k.SsKey, c) |> Map.ofList
        let attrCoord =
            kinds
            |> List.collect (fun (k, coord) ->
                k.Attributes |> List.map (fun a -> a.SsKey, (coord, ColumnRealization.columnNameText a.Column)))
            |> Map.ofList
        let refCoord =
            kinds
            |> List.collect (fun (k, coord) ->
                k.References
                |> List.choose (fun r ->
                    match Map.tryFind r.TargetKind kindCoord with
                    | None -> None
                    | Some parentCoord ->
                        let column =
                            k.Attributes
                            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                            |> Option.map (fun a -> ColumnRealization.columnNameText a.Column)
                        column |> Option.map (fun col -> r.SsKey, (coord, col, parentCoord))))
            |> Map.ofList
        { KindCoord = kindCoord; AttrCoord = attrCoord; RefCoord = refCoord }

    let private shapeOf (n: NumericDistribution) : NumericShape =
        { Min = n.Min; P25 = n.P25; P50 = n.P50; P75 = n.P75; P95 = n.P95; P99 = n.P99; Max = n.Max }

    /// Rebind a captured engine Profile to a coordinate-keyed RICH pack.
    /// `keep` maps a capture-side kind to its estate coordinate text —
    /// the rendition seam: a logical source keeps its physical
    /// `schema.table`; a physical (OutSystems cloud) source maps through
    /// its logical entity name. Kinds mapped to `None` are outside the
    /// closed set and contribute nothing.
    let ofProfile
        (sourceName: string)
        (catalog: Catalog)
        (keep: Kind -> string option)
        (profile: Profile)
        : EvidencePack =
        let map = captureMapOf catalog keep
        let categoricalByAttr =
            profile.Distributions
            |> List.choose (function AttributeDistribution.Categorical c -> Some (c.AttributeKey, c) | _ -> None)
            |> Map.ofList
        let numericByAttr =
            profile.Distributions
            |> List.choose (function AttributeDistribution.Numeric n -> Some (n.AttributeKey, n) | _ -> None)
            |> Map.ofList
        // Duplicate reality is measured on two engine axes; either one is
        // evidence of a duplicate value in the column.
        let duplicateByAttr =
            let fromReality =
                profile.AttributeRealities
                |> List.filter (fun r -> r.HasDuplicates)
                |> List.map (fun r -> r.AttributeKey)
            let fromUnique =
                profile.UniqueCandidates
                |> List.filter (fun u -> u.HasDuplicate)
                |> List.map (fun u -> u.AttributeKey)
            Set.ofList (fromReality @ fromUnique)
        let columns =
            profile.Columns
            |> List.choose (fun c ->
                Map.tryFind c.AttributeKey map.AttrCoord
                |> Option.map (fun (table, column) ->
                    table,
                    { Column = column
                      RowCount = c.RowCount
                      NullCount = c.NullCount
                      MaxLength = c.MaxObservedLength
                      DistinctCount =
                          Map.tryFind c.AttributeKey categoricalByAttr |> Option.map (fun cat -> cat.DistinctCount)
                      Truncated =
                          Map.tryFind c.AttributeKey categoricalByAttr |> Option.map (fun cat -> cat.IsTruncated) |> Option.defaultValue false
                      HasDuplicates = Set.contains c.AttributeKey duplicateByAttr
                      Frequencies =
                          Map.tryFind c.AttributeKey categoricalByAttr |> Option.map (fun cat -> cat.Frequencies) |> Option.defaultValue []
                      Numeric =
                          Map.tryFind c.AttributeKey numericByAttr |> Option.map shapeOf
                      // The string-plane counts arrive from the twin-side
                      // reality probe (Twin.Runtime), never the kernel.
                      Text = None }))
        let tables =
            columns
            |> List.groupBy fst
            |> List.map (fun (table, cols) ->
                let columnEvidence = cols |> List.map snd |> List.sortBy (fun c -> c.Column.ToLowerInvariant())
                { Table = table
                  RowCount = columnEvidence |> List.map (fun c -> c.RowCount) |> function [] -> 0L | xs -> List.max xs
                  Columns = columnEvidence })
            |> List.sortBy (fun t -> t.Table.ToLowerInvariant())
        let fanOuts =
            profile.ForeignKeyCardinalities
            |> List.choose (fun f ->
                Map.tryFind f.ReferenceKey map.RefCoord
                |> Option.map (fun (child, column, parent) ->
                    { ChildTable = child; ChildColumn = column; ParentTable = parent
                      Shape = shapeOf f.ChildCountDistribution }))
            |> List.sortBy (fun f -> f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant())
        let orphans =
            profile.ForeignKeys
            |> List.filter (fun r -> r.HasOrphan)
            |> List.choose (fun r ->
                Map.tryFind r.ReferenceKey map.RefCoord
                |> Option.map (fun (child, column, parent) ->
                    { ChildTable = child; ChildColumn = column; ParentTable = parent
                      OrphanCount = r.OrphanCount }))
            |> List.sortBy (fun o -> o.ChildTable.ToLowerInvariant(), o.ChildColumn.ToLowerInvariant())
        let selectivities =
            profile.ForeignKeySelectivities
            |> List.filter (fun s -> not (List.isEmpty s.Frequencies))
            |> List.choose (fun s ->
                Map.tryFind s.ReferenceKey map.RefCoord
                |> Option.map (fun (child, column, parent) ->
                    { ChildTable = child; ChildColumn = column; ParentTable = parent
                      DistinctCount = s.DistinctCount
                      // The captured parent-key values are dropped here, at the
                      // capture boundary — the mint draws by rank, so only the
                      // count vector travels.
                      Counts = s.Frequencies |> List.map snd }))
            |> List.sortBy (fun s -> s.ChildTable.ToLowerInvariant(), s.ChildColumn.ToLowerInvariant())
        let joints =
            profile.JointDistributions
            |> List.choose (fun j ->
                match Map.tryFind j.KindKey map.KindCoord with
                | None -> None
                | Some table ->
                    let columnNames =
                        j.AttributeKeys
                        |> List.map (fun k -> Map.tryFind k map.AttrCoord |> Option.map snd)
                    if columnNames |> List.exists Option.isNone then None
                    else
                        Some
                            { Table = table
                              Columns = columnNames |> List.map Option.get
                              DistinctCount = j.DistinctCount
                              Frequencies = j.Frequencies })
            |> List.sortBy (fun j -> j.Table.ToLowerInvariant(), String.concat "|" j.Columns)  // LINT-ALLOW: deterministic composite sort key over the joint's column list; the joined text is the ordering key, never emitted
        { Tier = RichTier; Sources = [ sourceName ]; Tables = tables; FanOuts = fanOuts
          Orphans = orphans; Selectivities = selectivities; Joints = joints }

    // ------------------------------------------------------------------
    // The tier projection (law 3) and the merge (law 4's backstop).
    // ------------------------------------------------------------------

    /// Rich → shape: every captured literal dropped; structure, counts,
    /// and shapes kept. Fan-out shapes, orphan counts, duplicate flags,
    /// and selectivity count vectors carry counts, never values, so they
    /// survive. Joint tuples carry real parent-key values, so they drop.
    let deriveShape (pack: EvidencePack) : EvidencePack =
        { pack with
            Tier = ShapeTier
            Joints = []
            Tables =
                pack.Tables
                |> List.map (fun t ->
                    { t with
                        Columns =
                            t.Columns
                            |> List.map (fun c -> { c with Frequencies = []; Numeric = None }) }) }

    let private mergeCollision (table: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.mergeCollision"
            "Two evidence packs carry the same table. Each table belongs to exactly one source."
            (Map.ofList [ "table", Some table ])

    /// Union packs with disjoint table sets (the artifact-level backstop
    /// of the config's collision law).
    let merge (packs: EvidencePack list) : Result<EvidencePack> =
        match packs with
        | [] -> Result.success (emptyPack RichTier)
        | first :: _ ->
            let collisions =
                packs
                |> List.collect (fun p -> p.Tables |> List.map (fun t -> t.Table.ToLowerInvariant()))
                |> List.groupBy id
                |> List.filter (fun (_, g) -> List.length g > 1)
                |> List.map (fst >> mergeCollision)
            if not (List.isEmpty collisions) then Result.failure collisions
            else
                Result.success
                    { Tier = first.Tier
                      Sources = packs |> List.collect (fun p -> p.Sources) |> List.distinct
                      Tables = packs |> List.collect (fun p -> p.Tables) |> List.sortBy (fun t -> t.Table.ToLowerInvariant())
                      FanOuts = packs |> List.collect (fun p -> p.FanOuts) |> List.sortBy (fun f -> f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant())
                      Orphans = packs |> List.collect (fun p -> p.Orphans) |> List.sortBy (fun o -> o.ChildTable.ToLowerInvariant(), o.ChildColumn.ToLowerInvariant())
                      Selectivities = packs |> List.collect (fun p -> p.Selectivities) |> List.sortBy (fun s -> s.ChildTable.ToLowerInvariant(), s.ChildColumn.ToLowerInvariant())
                      Joints = packs |> List.collect (fun p -> p.Joints) |> List.sortBy (fun j -> j.Table.ToLowerInvariant(), String.concat "|" j.Columns) }  // LINT-ALLOW: deterministic composite sort key over the joint's column list; the joined text is the ordering key, never emitted

    // ------------------------------------------------------------------
    // Mint-side rebinding: pack → Profile against the twin catalog.
    // ------------------------------------------------------------------

    let private numericOf (attrKey: SsKey) (rows: int64) (s: NumericShape) : Result<NumericDistribution> =
        NumericDistribution.create
            attrKey s.Min s.P25 s.P50 s.P75 s.P95 s.P99 s.Max
            (max rows NumericDistribution.sampleSizeFloor)
            (ProbeStatus.observed (max rows NumericDistribution.sampleSizeFloor))

    /// Resolve an evidence edge (child table, FK column, parent table)
    /// against the twin catalog. The three COORDINATES must bind — law 2
    /// holds for names. The REFERENCE between them is allowed to be
    /// absent: that is the FK-add case the orphan and selectivity axes
    /// exist for, so an edge without an enforcing reference resolves to
    /// `Ok None` rather than refusing. Callers decide what an absent
    /// reference means for their axis.
    let resolveEdge
        (index: CatalogIndex)
        (childTable: string)
        (childColumn: string)
        (parentTable: string)
        : Result<Reference option> =
        match TableCoordinate.parse childTable, TableCoordinate.parse parentTable with
        | Ok childCoord, Ok parentCoord ->
            CatalogIndex.bindKind index childCoord
            |> Result.bind (fun childKind ->
                CatalogIndex.bindKind index parentCoord
                |> Result.bind (fun parentKind ->
                    ColumnCoordinate.create childCoord childColumn
                    |> Result.bind (CatalogIndex.bindColumn index)
                    |> Result.map (fun _ ->
                        childKind.References
                        |> List.tryFind (fun r ->
                            r.TargetKind = parentKind.SsKey
                            && (childKind.Attributes
                                |> List.exists (fun a ->
                                    a.SsKey = r.SourceAttribute
                                    && System.String.Equals(
                                        ColumnRealization.columnNameText a.Column, childColumn,
                                        System.StringComparison.OrdinalIgnoreCase)))))))
        | cR, pR -> Result.failure (Result.errors cR @ Result.errors pR)

    /// Bind a pack to the twin catalog as an engine Profile. Law 2: every
    /// evidenced coordinate must exist — an unbound table or column is a
    /// named refusal, never a silent skip (the estate moved ahead of the
    /// evidence; `twin evidence verify` is the drift answer). One
    /// deliberate asymmetry: an orphan or selectivity entry whose
    /// COORDINATES bind but whose REFERENCE the estate does not carry is
    /// skipped here and stays in the pack for the witness pass — the
    /// FK-add case is why those axes are captured at all.
    let toProfile (index: CatalogIndex) (pack: EvidencePack) : Result<Profile> =
        let rowsByTable =
            pack.Tables |> List.map (fun t -> t.Table.ToLowerInvariant(), t.RowCount) |> Map.ofList
        let sampleFor (table: string) (floor: int64) : int64 =
            Map.tryFind (table.ToLowerInvariant()) rowsByTable
            |> Option.defaultValue floor
            |> max floor
        let tableResults =
            pack.Tables
            |> List.map (fun t ->
                match TableCoordinate.parse t.Table with
                | Error es -> Result.failure es
                | Ok coord ->
                    CatalogIndex.bindKind index coord
                    |> Result.bind (fun kind ->
                        t.Columns
                        |> List.map (fun c ->
                            ColumnCoordinate.create coord c.Column
                            |> Result.bind (CatalogIndex.bindColumn index)
                            |> Result.bind (fun (_, attr) ->
                                let columnProfile =
                                    ColumnProfile.create attr.SsKey c.RowCount c.NullCount (ProbeStatus.observed c.RowCount)
                                    |> Result.map (fun cp ->
                                        match c.MaxLength with
                                        | Some len -> ColumnProfile.withMaxObservedLength len cp
                                        | None -> cp)
                                let categorical =
                                    match c.Frequencies with
                                    | [] -> Result.success None
                                    | freqs ->
                                        let distinct = defaultArg c.DistinctCount (int64 (List.length freqs))
                                        CategoricalDistribution.create attr.SsKey freqs distinct c.Truncated (ProbeStatus.observed c.RowCount)
                                        |> Result.map Some
                                let numeric =
                                    match c.Numeric with
                                    | None -> Result.success None
                                    | Some s -> numericOf attr.SsKey c.RowCount s |> Result.map Some
                                let duplicate =
                                    if not c.HasDuplicates then None
                                    else
                                        Some
                                            ({ AttributeKey         = attr.SsKey
                                               IsNullableInDatabase = false
                                               HasNulls             = c.NullCount > 0L
                                               HasDuplicates        = true
                                               HasOrphans           = false
                                               IsPresentButInactive = false },
                                             { AttributeKey = attr.SsKey
                                               HasDuplicate = true
                                               ProbeStatus  = ProbeStatus.observed c.RowCount })
                                match columnProfile, categorical, numeric with
                                | Ok cp, Ok cat, Ok num -> Result.success (cp, cat, num, duplicate)
                                | cpR, catR, numR ->
                                    Result.failure (Result.errors cpR @ Result.errors catR @ Result.errors numR)))
                        |> Result.aggregate
                        |> Result.map (fun cols -> kind, cols)))
            |> Result.aggregate
        // A fan-out binds when the estate carries the reference. An edge
        // whose coordinates bind but whose reference is absent is the
        // FK-add case (an environment's own reference recorded ahead of
        // the trunk — the same edge the orphan evidence rides): σ has no
        // relationship to attach the cardinality to, so the shape stays
        // pack-side and never reaches the Profile. Unparseable
        // coordinates still refuse inside resolveEdge.
        let fanOutResults =
            pack.FanOuts
            |> List.map (fun f ->
                resolveEdge index f.ChildTable f.ChildColumn f.ParentTable
                |> Result.bind (fun reference ->
                    match reference with
                    | None -> Result.success None
                    | Some r ->
                        numericOf r.SsKey (int64 NumericDistribution.sampleSizeFloor) f.Shape
                        |> Result.map (fun dist ->
                            Some (ForeignKeyCardinality.create r.SsKey dist))))
            |> Result.aggregate
            |> Result.map (List.choose id)
        // Orphan reality binds when the estate carries the reference; an
        // edge without one is the FK-add case and stays pack-side for the
        // witness pass.
        let orphanResults =
            pack.Orphans
            |> List.map (fun o ->
                resolveEdge index o.ChildTable o.ChildColumn o.ParentTable
                |> Result.map (fun reference ->
                    reference
                    |> Option.map (fun r ->
                        { ReferenceKey = r.SsKey
                          HasOrphan    = true
                          OrphanCount  = o.OrphanCount
                          IsNoCheck    = false
                          ProbeStatus  = ProbeStatus.observed (sampleFor o.ChildTable (max o.OrphanCount 1L)) })))
            |> Result.aggregate
            |> Result.map (List.choose id)
        // Selectivity binds by rank: the pack carries counts only, so the
        // rebind fabricates rank labels — the mint draws by rank and never
        // reads the labels. Same absent-reference asymmetry as orphans.
        let selectivityResults =
            pack.Selectivities
            |> List.map (fun s ->
                resolveEdge index s.ChildTable s.ChildColumn s.ParentTable
                |> Result.bind (fun reference ->
                    match reference with
                    | None -> Result.success None
                    | Some r ->
                        let ranked = s.Counts |> List.mapi (fun i c -> sprintf "#%d" (i + 1), c)
                        let truncated = s.DistinctCount <> int64 (List.length s.Counts)
                        ForeignKeySelectivity.create
                            r.SsKey ranked s.DistinctCount truncated
                            (ProbeStatus.observed (max (s.Counts |> List.sum) 1L))
                        |> Result.map Some))
            |> Result.aggregate
            |> Result.map (List.choose id)
        let jointResults =
            pack.Joints
            |> List.filter (fun j -> not (List.isEmpty j.Frequencies))
            |> List.map (fun j ->
                match TableCoordinate.parse j.Table with
                | Error es -> Result.failure es
                | Ok coord ->
                    CatalogIndex.bindKind index coord
                    |> Result.bind (fun kind ->
                        j.Columns
                        |> List.map (fun col ->
                            ColumnCoordinate.create coord col
                            |> Result.bind (CatalogIndex.bindColumn index)
                            |> Result.map (fun (_, attr) -> attr.SsKey))
                        |> Result.aggregate
                        |> Result.bind (fun attrKeys ->
                            let truncated = j.DistinctCount <> int64 (List.length j.Frequencies)
                            JointDistribution.create
                                kind.SsKey attrKeys j.Frequencies j.DistinctCount truncated
                                (ProbeStatus.observed (max (j.Frequencies |> List.sumBy snd) 1L)))))
            |> Result.aggregate
        match tableResults, fanOutResults, orphanResults, selectivityResults, jointResults with
        | Ok tables, Ok fanOuts, Ok orphans, Ok selectivities, Ok joints ->
            let cols = tables |> List.collect (fun (_, cols) -> cols)
            let duplicates = cols |> List.choose (fun (_, _, _, dup) -> dup)
            Result.success
                { Profile.empty with
                    Columns = cols |> List.map (fun (cp, _, _, _) -> cp)
                    Distributions =
                        (cols |> List.choose (fun (_, cat, _, _) -> cat |> Option.map AttributeDistribution.Categorical))
                        @ (cols |> List.choose (fun (_, _, num, _) -> num |> Option.map AttributeDistribution.Numeric))
                    ForeignKeyCardinalities = fanOuts
                    ForeignKeys = orphans
                    ForeignKeySelectivities = selectivities
                    JointDistributions = joints
                    AttributeRealities = duplicates |> List.map fst
                    UniqueCandidates = duplicates |> List.map snd }
        | tR, fR, oR, sR, jR ->
            Result.failure
                (Result.errors tR @ Result.errors fR @ Result.errors oR
                 @ Result.errors sR @ Result.errors jR)

    /// Precedence layering: `over`'s evidence replaces `base`'s per
    /// attribute/reference key; everything else unions. Every axis the
    /// pack carries layers the same way — the reality axes included, so
    /// a rich pack's orphans and selectivities are never lost under a
    /// shape base.
    let layer (baseProfile: Profile) (over: Profile) : Profile =
        let replaceBy (key: 'a -> 'k) (baseXs: 'a list) (overXs: 'a list) : 'a list =
            let overKeys = overXs |> List.map key |> Set.ofList
            (baseXs |> List.filter (fun x -> not (Set.contains (key x) overKeys))) @ overXs
        let distKey (d: AttributeDistribution) =
            match d with
            | AttributeDistribution.Categorical c -> c.AttributeKey
            | AttributeDistribution.Numeric n -> n.AttributeKey
        { baseProfile with
            Columns = replaceBy (fun (c: ColumnProfile) -> c.AttributeKey) baseProfile.Columns over.Columns
            Distributions = replaceBy distKey baseProfile.Distributions over.Distributions
            ForeignKeyCardinalities =
                replaceBy (fun (f: ForeignKeyCardinality) -> f.ReferenceKey)
                    baseProfile.ForeignKeyCardinalities over.ForeignKeyCardinalities
            ForeignKeys =
                replaceBy (fun (r: ForeignKeyReality) -> r.ReferenceKey)
                    baseProfile.ForeignKeys over.ForeignKeys
            ForeignKeySelectivities =
                replaceBy (fun (s: ForeignKeySelectivity) -> s.ReferenceKey)
                    baseProfile.ForeignKeySelectivities over.ForeignKeySelectivities
            JointDistributions =
                replaceBy (fun (j: JointDistribution) -> j.KindKey, j.AttributeKeys)
                    baseProfile.JointDistributions over.JointDistributions
            AttributeRealities =
                replaceBy (fun (r: AttributeReality) -> r.AttributeKey)
                    baseProfile.AttributeRealities over.AttributeRealities
            UniqueCandidates =
                replaceBy (fun (u: UniqueCandidateProfile) -> u.AttributeKey)
                    baseProfile.UniqueCandidates over.UniqueCandidates }

    /// The kinds a layered profile carries column evidence for — the
    /// volume seam: an evidenced kind rides observed × scale; an
    /// unevidenced kind gets the default volume.
    let evidencedKinds (index: CatalogIndex) (profile: Profile) : Set<SsKey> =
        let byAttr =
            CatalogIndex.kinds index
            |> List.collect (fun (_, k) -> k.Attributes |> List.map (fun a -> a.SsKey, k.SsKey))
            |> Map.ofList
        profile.Columns
        |> List.choose (fun c -> Map.tryFind c.AttributeKey byAttr)
        |> Set.ofList

    // ------------------------------------------------------------------
    // The codec — deterministic, total, round-tripping.
    // ------------------------------------------------------------------

    let private tierText (t: EvidenceTier) : string =
        match t with ShapeTier -> "shape" | RichTier -> "rich"

    let serialize (pack: EvidencePack) : string =
        let options = JsonWriterOptions(Indented = true)
        use stream = new System.IO.MemoryStream()
        (fun () ->
            use writer = new Utf8JsonWriter(stream, options)
            writer.WriteStartObject()
            writer.WriteString("tier", tierText pack.Tier)
            writer.WriteStartArray "sources"
            for s in pack.Sources |> List.sort do writer.WriteStringValue s
            writer.WriteEndArray()
            writer.WriteStartArray "tables"
            for t in pack.Tables |> List.sortBy (fun t -> t.Table.ToLowerInvariant()) do
                writer.WriteStartObject()
                writer.WriteString("table", t.Table)
                writer.WriteNumber("rowCount", t.RowCount)
                writer.WriteStartArray "columns"
                for c in t.Columns |> List.sortBy (fun c -> c.Column.ToLowerInvariant()) do
                    writer.WriteStartObject()
                    writer.WriteString("column", c.Column)
                    writer.WriteNumber("rowCount", c.RowCount)
                    writer.WriteNumber("nullCount", c.NullCount)
                    match c.MaxLength with
                    | Some l -> writer.WriteNumber("maxLength", l)
                    | None -> ()
                    match c.DistinctCount with
                    | Some d -> writer.WriteNumber("distinctCount", d)
                    | None -> ()
                    if c.Truncated then writer.WriteBoolean("truncated", true)
                    if c.HasDuplicates then writer.WriteBoolean("hasDuplicates", true)
                    match c.Frequencies with
                    | [] -> ()
                    | freqs ->
                        writer.WriteStartArray "frequencies"
                        for (v, n) in freqs do
                            writer.WriteStartObject()
                            writer.WriteString("value", v)
                            writer.WriteNumber("count", n)
                            writer.WriteEndObject()
                        writer.WriteEndArray()
                    match c.Numeric with
                    | None -> ()
                    | Some s ->
                        writer.WriteStartObject "numeric"
                        writer.WriteNumber("min", s.Min); writer.WriteNumber("p25", s.P25)
                        writer.WriteNumber("p50", s.P50); writer.WriteNumber("p75", s.P75)
                        writer.WriteNumber("p95", s.P95); writer.WriteNumber("p99", s.P99)
                        writer.WriteNumber("max", s.Max)
                        writer.WriteEndObject()
                    match c.Text with
                    | None -> ()
                    | Some ts ->
                        writer.WriteStartObject "text"
                        if ts.EmptyCount > 0L then writer.WriteNumber("empty", ts.EmptyCount)
                        if ts.TrailingSpaceCount > 0L then writer.WriteNumber("trailingSpace", ts.TrailingSpaceCount)
                        if ts.CaseCollisions > 0L then writer.WriteNumber("caseCollisions", ts.CaseCollisions)
                        (match ts.LengthP50 with Some v -> writer.WriteNumber("lengthP50", v) | None -> ())
                        (match ts.LengthP90 with Some v -> writer.WriteNumber("lengthP90", v) | None -> ())
                        writer.WriteEndObject()
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WriteStartArray "fanOuts"
            for f in pack.FanOuts |> List.sortBy (fun f -> f.ChildTable.ToLowerInvariant(), f.ChildColumn.ToLowerInvariant()) do
                writer.WriteStartObject()
                writer.WriteString("child", f.ChildTable)
                writer.WriteString("column", f.ChildColumn)
                writer.WriteString("parent", f.ParentTable)
                writer.WriteStartObject "shape"
                writer.WriteNumber("min", f.Shape.Min); writer.WriteNumber("p25", f.Shape.P25)
                writer.WriteNumber("p50", f.Shape.P50); writer.WriteNumber("p75", f.Shape.P75)
                writer.WriteNumber("p95", f.Shape.P95); writer.WriteNumber("p99", f.Shape.P99)
                writer.WriteNumber("max", f.Shape.Max)
                writer.WriteEndObject()
                writer.WriteEndObject()
            writer.WriteEndArray()
            // The reality axes are additive: each array is omitted when
            // empty, so a pack carrying none serializes byte-identically
            // to the pre-axis wire format.
            match pack.Orphans with
            | [] -> ()
            | orphans ->
                writer.WriteStartArray "orphans"
                for o in orphans |> List.sortBy (fun o -> o.ChildTable.ToLowerInvariant(), o.ChildColumn.ToLowerInvariant()) do
                    writer.WriteStartObject()
                    writer.WriteString("child", o.ChildTable)
                    writer.WriteString("column", o.ChildColumn)
                    writer.WriteString("parent", o.ParentTable)
                    writer.WriteNumber("count", o.OrphanCount)
                    writer.WriteEndObject()
                writer.WriteEndArray()
            match pack.Selectivities with
            | [] -> ()
            | selectivities ->
                writer.WriteStartArray "selectivities"
                for s in selectivities |> List.sortBy (fun s -> s.ChildTable.ToLowerInvariant(), s.ChildColumn.ToLowerInvariant()) do
                    writer.WriteStartObject()
                    writer.WriteString("child", s.ChildTable)
                    writer.WriteString("column", s.ChildColumn)
                    writer.WriteString("parent", s.ParentTable)
                    writer.WriteNumber("distinctCount", s.DistinctCount)
                    writer.WriteStartArray "counts"
                    for c in s.Counts do writer.WriteNumberValue c
                    writer.WriteEndArray()
                    writer.WriteEndObject()
                writer.WriteEndArray()
            match pack.Joints with
            | [] -> ()
            | joints ->
                writer.WriteStartArray "joints"
                for j in joints |> List.sortBy (fun j -> j.Table.ToLowerInvariant(), String.concat "|" j.Columns) do  // LINT-ALLOW: deterministic composite sort key over the joint's column list; the joined text is the ordering key, never emitted
                    writer.WriteStartObject()
                    writer.WriteString("table", j.Table)
                    writer.WriteStartArray "columns"
                    for col in j.Columns do writer.WriteStringValue col
                    writer.WriteEndArray()
                    writer.WriteNumber("distinctCount", j.DistinctCount)
                    writer.WriteStartArray "frequencies"
                    for (v, n) in j.Frequencies do
                        writer.WriteStartObject()
                        writer.WriteString("value", v)
                        writer.WriteNumber("count", n)
                        writer.WriteEndObject()
                    writer.WriteEndArray()
                    writer.WriteEndObject()
                writer.WriteEndArray()
            writer.WriteEndObject()) ()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let private codecError (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.codec"
            "The evidence pack did not parse."
            (Map.ofList [ "detail", Some detail ])

    let deserialize (json: string) : Result<EvidencePack> =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement
            let getStr (el: JsonElement) (name: string) : string =
                match el.TryGetProperty name with
                | true, v ->
                    (match v.GetString() with null -> "" | s -> s)
                | _ -> ""
            let tier =
                match getStr root "tier" with
                | "shape" -> ShapeTier
                | _ -> RichTier
            let sources =
                match root.TryGetProperty "sources" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for s in arr.EnumerateArray() -> match s.GetString() with null -> "" | v -> v ]
                | _ -> []
            let shapeOfEl (el: JsonElement) : NumericShape =
                let d (name: string) = el.GetProperty(name).GetDecimal()
                { Min = d "min"; P25 = d "p25"; P50 = d "p50"; P75 = d "p75"; P95 = d "p95"; P99 = d "p99"; Max = d "max" }
            let tables =
                match root.TryGetProperty "tables" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for t in arr.EnumerateArray() ->
                        { Table = getStr t "table"
                          RowCount = t.GetProperty("rowCount").GetInt64()
                          Columns =
                              match t.TryGetProperty "columns" with
                              | true, cols when cols.ValueKind = JsonValueKind.Array ->
                                  [ for c in cols.EnumerateArray() ->
                                      { Column = getStr c "column"
                                        RowCount = c.GetProperty("rowCount").GetInt64()
                                        NullCount = c.GetProperty("nullCount").GetInt64()
                                        MaxLength =
                                            match c.TryGetProperty "maxLength" with
                                            | true, v -> Some (v.GetInt32())
                                            | _ -> None
                                        DistinctCount =
                                            match c.TryGetProperty "distinctCount" with
                                            | true, v -> Some (v.GetInt64())
                                            | _ -> None
                                        Truncated =
                                            match c.TryGetProperty "truncated" with
                                            | true, v -> v.GetBoolean()
                                            | _ -> false
                                        HasDuplicates =
                                            match c.TryGetProperty "hasDuplicates" with
                                            | true, v -> v.GetBoolean()
                                            | _ -> false
                                        Frequencies =
                                            match c.TryGetProperty "frequencies" with
                                            | true, freqs when freqs.ValueKind = JsonValueKind.Array ->
                                                [ for f in freqs.EnumerateArray() ->
                                                    getStr f "value", f.GetProperty("count").GetInt64() ]
                                            | _ -> []
                                        Numeric =
                                            match c.TryGetProperty "numeric" with
                                            | true, n when n.ValueKind = JsonValueKind.Object -> Some (shapeOfEl n)
                                            | _ -> None
                                        Text =
                                            match c.TryGetProperty "text" with
                                            | true, ts when ts.ValueKind = JsonValueKind.Object ->
                                                Some
                                                    { EmptyCount =
                                                          (match ts.TryGetProperty "empty" with true, v -> v.GetInt64() | _ -> 0L)
                                                      TrailingSpaceCount =
                                                          (match ts.TryGetProperty "trailingSpace" with true, v -> v.GetInt64() | _ -> 0L)
                                                      CaseCollisions =
                                                          (match ts.TryGetProperty "caseCollisions" with true, v -> v.GetInt64() | _ -> 0L)
                                                      LengthP50 =
                                                          (match ts.TryGetProperty "lengthP50" with true, v -> Some (v.GetInt32()) | _ -> None)
                                                      LengthP90 =
                                                          (match ts.TryGetProperty "lengthP90" with true, v -> Some (v.GetInt32()) | _ -> None) }
                                            | _ -> None } ]
                              | _ -> [] } ]
                | _ -> []
            let fanOuts =
                match root.TryGetProperty "fanOuts" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for f in arr.EnumerateArray() ->
                        { ChildTable = getStr f "child"
                          ChildColumn = getStr f "column"
                          ParentTable = getStr f "parent"
                          Shape = shapeOfEl (f.GetProperty "shape") } ]
                | _ -> []
            let orphans =
                match root.TryGetProperty "orphans" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for o in arr.EnumerateArray() ->
                        { ChildTable = getStr o "child"
                          ChildColumn = getStr o "column"
                          ParentTable = getStr o "parent"
                          OrphanCount = o.GetProperty("count").GetInt64() } ]
                | _ -> []
            let selectivities =
                match root.TryGetProperty "selectivities" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for s in arr.EnumerateArray() ->
                        { ChildTable = getStr s "child"
                          ChildColumn = getStr s "column"
                          ParentTable = getStr s "parent"
                          DistinctCount = s.GetProperty("distinctCount").GetInt64()
                          Counts =
                              match s.TryGetProperty "counts" with
                              | true, cs when cs.ValueKind = JsonValueKind.Array ->
                                  [ for c in cs.EnumerateArray() -> c.GetInt64() ]
                              | _ -> [] } ]
                | _ -> []
            let joints =
                match root.TryGetProperty "joints" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    [ for j in arr.EnumerateArray() ->
                        { Table = getStr j "table"
                          Columns =
                              match j.TryGetProperty "columns" with
                              | true, cs when cs.ValueKind = JsonValueKind.Array ->
                                  [ for c in cs.EnumerateArray() -> match c.GetString() with null -> "" | v -> v ]
                              | _ -> []
                          DistinctCount = j.GetProperty("distinctCount").GetInt64()
                          Frequencies =
                              match j.TryGetProperty "frequencies" with
                              | true, fs when fs.ValueKind = JsonValueKind.Array ->
                                  [ for f in fs.EnumerateArray() ->
                                      getStr f "value", f.GetProperty("count").GetInt64() ]
                              | _ -> [] } ]
                | _ -> []
            Result.success
                { Tier = tier; Sources = sources; Tables = tables; FanOuts = fanOuts
                  Orphans = orphans; Selectivities = selectivities; Joints = joints }
        with ex ->
            Result.failureOf (codecError ex.Message)

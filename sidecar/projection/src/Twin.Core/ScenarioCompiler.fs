namespace Twin.Core

open Projection.Core

/// THE TWIN — the scenario compiler (Twin.Core, pure).
///
/// A scenario **only rewrites evidence, volumes, corrections, and pins —
/// it never generates.** Compilation realizes that law: every scenario
/// field lowers to an existing engine input, totally — each override
/// binds to a coordinate and a legal shape, or the compile refuses with
/// the scenario, coordinate, and expected shape named.
///
///   rows                → `VolumeTarget.Absolute`
///   perParent (child ⇒ parent, mean) → the child's volume, derived as
///                         round(mean × parent volume) — uniform draws
///                         then land that fan-out on average; skewed
///                         fan-out remains the evidence plane's
///                         (`ForeignKeySelectivity`, F5a)
///   weights             → a rewritten `CategoricalDistribution`, with
///                         the column forced to Preserve (the weighted
///                         values ARE the wanted values; a correction's
///                         Synthesize classification is displaced,
///                         explicitly)
///   between/skew        → a rewritten `NumericDistribution` — decimal
///                         for numeric columns, tick-encoded for
///                         chronological columns (K2)
///   pins                → operator-authored `StaticRow`s, rendered per
///                         attribute type, merged after realization so
///                         Faker never rewrites them; their keys join
///                         the FK-draw pools (K1b)
type CompiledPin = {
    Kind     : SsKey
    PkName   : Name
    Rows     : StaticRow list
    PoolKeys : string list
}

type CompiledScenario = {
    /// Per-kind volume overrides (explicit rows + perParent derivations).
    Volumes       : Map<SsKey, VolumeTarget>
    /// The evidence rewrite: replaces the affected attributes'
    /// distributions, leaves everything else untouched.
    Overlay       : Profile -> Profile
    /// Columns whose scenario weighting forces value preservation.
    ForcePreserve : Set<string>
    /// Columns the weighting must displace from a Synthesize
    /// classification (scenario wins over correction, explicitly).
    UnSynthesize  : Set<string>
    Pins          : CompiledPin list
}

[<RequireQualifiedAccess>]
module ScenarioCompiler =

    let empty : CompiledScenario =
        { Volumes = Map.empty; Overlay = id; ForcePreserve = Set.empty; UnSynthesize = Set.empty; Pins = [] }

    // ------------------------------------------------------------------
    // Refusals — every one names the scenario and the coordinate.
    // ------------------------------------------------------------------

    let private refuse (code: string) (message: string) (meta: (string * string) list) : ValidationError =
        ValidationError.createWithMetadata code message (meta |> List.map (fun (k, v) -> k, Some v) |> Map.ofList)

    let private volumeConflict (scenario: string) (coord: TableCoordinate) : ValidationError =
        refuse "twin.scenario.volumeConflict"
            "A table carries both an explicit row count and a per-parent ratio. Pick one — the ratio derives the row count."
            [ "scenario", scenario; "table", TableCoordinate.text coord ]

    let private noReference (scenario: string) (child: TableCoordinate) (parent: TableCoordinate) : ValidationError =
        refuse "twin.scenario.perParentNoReference"
            "A per-parent ratio names a parent the child carries no relationship to."
            [ "scenario", scenario; "table", TableCoordinate.text child; "parent", TableCoordinate.text parent ]

    let private weightsOnNonText (scenario: string) (c: ColumnCoordinate) : ValidationError =
        refuse "twin.scenario.weightsOnNonText"
            "Weights reshape a text column's vocabulary. This column is not text — use between for a numeric or date window."
            [ "scenario", scenario; "coordinate", ColumnCoordinate.text c ]

    let private windowUnsupported (scenario: string) (c: ColumnCoordinate) : ValidationError =
        refuse "twin.scenario.windowOnUnsupported"
            "A between window applies to a numeric or date column."
            [ "scenario", scenario; "coordinate", ColumnCoordinate.text c ]

    let private windowMalformed (scenario: string) (c: ColumnCoordinate) (expected: string) : ValidationError =
        refuse "twin.scenario.windowMalformed"
            "A between bound did not parse for this column's type."
            [ "scenario", scenario; "coordinate", ColumnCoordinate.text c; "expected", expected ]

    let private windowInverted (scenario: string) (c: ColumnCoordinate) : ValidationError =
        refuse "twin.scenario.windowInverted"
            "The between window's lower bound is not below its upper bound."
            [ "scenario", scenario; "coordinate", ColumnCoordinate.text c ]

    let private pinColumnUnknown (scenario: string) (coord: TableCoordinate) (column: string) : ValidationError =
        refuse "twin.scenario.pinColumnUnknown"
            "A pinned row names a column the table does not carry."
            [ "scenario", scenario; "table", TableCoordinate.text coord; "column", column ]

    let private pinNeedsKey (scenario: string) (coord: TableCoordinate) : ValidationError =
        refuse "twin.scenario.pinNeedsKey"
            "A pinned row must carry the table's key column — the pin's identity and its referenceable value."
            [ "scenario", scenario; "table", TableCoordinate.text coord ]

    let private pinMissingMandatory (scenario: string) (coord: TableCoordinate) (column: string) : ValidationError =
        refuse "twin.scenario.pinMissingMandatory"
            "A pinned row leaves a non-nullable column unset. Set it, or make the column nullable in the estate."
            [ "scenario", scenario; "table", TableCoordinate.text coord; "column", column ]

    let private pinNullOnMandatory (scenario: string) (coord: TableCoordinate) (column: string) : ValidationError =
        refuse "twin.scenario.pinNullOnMandatory"
            "A pinned row sets NULL on a non-nullable column."
            [ "scenario", scenario; "table", TableCoordinate.text coord; "column", column ]

    let private pinValueMalformed (scenario: string) (coord: TableCoordinate) (column: string) (expected: string) : ValidationError =
        refuse "twin.scenario.pinValueMalformed"
            "A pinned value did not parse for this column's type."
            [ "scenario", scenario; "table", TableCoordinate.text coord; "column", column; "expected", expected ]

    // ------------------------------------------------------------------
    // Chain merge (base-first; the nearer scenario wins field-wise).
    // ------------------------------------------------------------------

    let private mergeColumns
        (baseColumns: (string * ColumnOverride) list)
        (overlay: (string * ColumnOverride) list)
        : (string * ColumnOverride) list =
        let overlayKeys = overlay |> List.map (fun (c, _) -> c.ToLowerInvariant()) |> Set.ofList
        (baseColumns |> List.filter (fun (c, _) -> not (Set.contains (c.ToLowerInvariant()) overlayKeys)))
        @ overlay

    let private mergePerParent
        (baseRatios: (TableCoordinate * decimal) list)
        (overlay: (TableCoordinate * decimal) list)
        : (TableCoordinate * decimal) list =
        let overlayKeys = overlay |> List.map (fun (p, _) -> TableCoordinate.key p) |> Set.ofList
        (baseRatios |> List.filter (fun (p, _) -> not (Set.contains (TableCoordinate.key p) overlayKeys)))
        @ overlay

    let private mergeTable (baseO: TableOverride) (overlay: TableOverride) : TableOverride =
        { Rows      = (match overlay.Rows with Some r -> Some r | None -> baseO.Rows)
          Columns   = mergeColumns baseO.Columns overlay.Columns
          PerParent = mergePerParent baseO.PerParent overlay.PerParent }

    /// Collapse the inheritance chain (base-first) into one effective
    /// scenario body.
    let mergeChain (chain: ScenarioIr list) : (TableCoordinate * TableOverride) list * Pin list =
        let tables =
            chain
            |> List.fold
                (fun (acc: (string * (TableCoordinate * TableOverride)) list) s ->
                    s.Tables
                    |> List.fold
                        (fun acc (coord, o) ->
                            let key = TableCoordinate.key coord
                            match acc |> List.tryFind (fun (k, _) -> k = key) with
                            | Some (_, (_, baseO)) ->
                                acc
                                |> List.map (fun (k, v) -> if k = key then k, (coord, mergeTable baseO o) else k, v)
                            | None -> acc @ [ key, (coord, o) ])
                        acc)
                []
            |> List.map snd
        let pins = chain |> List.collect (fun s -> s.Pins)
        tables, pins

    // ------------------------------------------------------------------
    // Value parsing for windows and pins.
    // ------------------------------------------------------------------

    let private tryParseDate (text: string) : System.DateTime option =
        let styles = System.Globalization.DateTimeStyles.None
        let formats = [| "yyyy-MM-dd"; "yyyy-MM-dd HH:mm:ss"; "yyyy-MM-dd HH:mm:ss.fffffff" |]
        match System.DateTime.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture, styles) with
        | true, v -> Some v
        | _ -> None

    let private tryParseDecimal (text: string) : decimal option =
        match System.Decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    /// The skewed percentile position: where the q-quantile of the window
    /// sits. Uniform is linear; early concentrates mass near the lower
    /// bound (percentiles rise slowly); late concentrates near the upper.
    let private position (skew: DateSkew) (q: decimal) : decimal =
        match skew with
        | SkewUniform -> q
        | SkewEarly -> q * q
        | SkewLate -> 1.0m - (1.0m - q) * (1.0m - q)

    let private windowDistribution
        (attrKey: SsKey)
        (lo: decimal)
        (hi: decimal)
        (skew: DateSkew)
        (sampleSize: int64)
        : Result<NumericDistribution> =
        let at (q: decimal) = lo + (hi - lo) * position skew q
        NumericDistribution.create
            attrKey lo (at 0.25m) (at 0.50m) (at 0.75m) (at 0.95m) (at 0.99m) hi
            (max sampleSize NumericDistribution.sampleSizeFloor)
            (ProbeStatus.observed (max sampleSize NumericDistribution.sampleSizeFloor))

    // ------------------------------------------------------------------
    // Pin rendering.
    // ------------------------------------------------------------------

    let private renderPinValue
        (scenario: string)
        (coord: TableCoordinate)
        (attr: Attribute)
        (value: PinValue)
        : Result<string option> =
        let columnText = ColumnRealization.columnNameText attr.Column
        let malformed (expected: string) = Result.failureOf (pinValueMalformed scenario coord columnText expected)
        match value with
        | PinNull ->
            if attr.Column.IsNullable then Result.success None
            else Result.failureOf (pinNullOnMandatory scenario coord columnText)
        | PinBool b ->
            match attr.Type with
            | PrimitiveType.Boolean -> Result.success (Some (RawValueCodec.formatBoolean b))
            | _ -> malformed "a value matching the column's type"
        | PinNumber n ->
            match attr.Type with
            | PrimitiveType.Integer | PrimitiveType.Decimal ->
                match tryParseDecimal n with
                | Some _ -> Result.success (Some n)
                | None -> malformed "a number"
            | _ -> malformed "a value matching the column's type"
        | PinText t ->
            match attr.Type with
            | PrimitiveType.Text -> Result.success (Some t)
            | PrimitiveType.Integer | PrimitiveType.Decimal ->
                match tryParseDecimal t with
                | Some _ -> Result.success (Some t)
                | None -> malformed "a number"
            | PrimitiveType.Boolean ->
                match t.ToLowerInvariant() with
                | "true" | "1" -> Result.success (Some (RawValueCodec.formatBoolean true))
                | "false" | "0" -> Result.success (Some (RawValueCodec.formatBoolean false))
                | _ -> malformed "true or false"
            | PrimitiveType.DateTime ->
                match tryParseDate t with
                | Some d -> Result.success (Some (RawValueCodec.formatDateTime d))
                | None -> malformed "an ISO date (yyyy-MM-dd, optional time)"
            | PrimitiveType.Date ->
                match tryParseDate t with
                | Some d -> Result.success (Some (RawValueCodec.formatDate d))
                | None -> malformed "an ISO date (yyyy-MM-dd)"
            | PrimitiveType.Guid ->
                match System.Guid.TryParse t with
                | true, g -> Result.success (Some (RawValueCodec.formatGuid g))
                | _ -> malformed "a GUID"
            | _ -> malformed "a value matching the column's type"

    let private compilePin
        (index: CatalogIndex)
        (scenario: string)
        (pin: Pin)
        : Result<CompiledPin> =
        CatalogIndex.bindKind index pin.Table
        |> Result.bind (fun kind ->
            match kind.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey) with
            | None -> Result.failureOf (pinNeedsKey scenario pin.Table)
            | Some pk ->
                let pkColumnKey = (ColumnRealization.columnNameText pk.Column).ToLowerInvariant()
                let attrsByColumn =
                    kind.Attributes
                    |> List.map (fun a -> (ColumnRealization.columnNameText a.Column).ToLowerInvariant(), a)
                    |> Map.ofList
                pin.Rows
                |> List.mapi (fun i row ->
                    let bound =
                        row
                        |> List.map (fun (column, value) ->
                            match Map.tryFind (column.ToLowerInvariant()) attrsByColumn with
                            | None -> Result.failureOf (pinColumnUnknown scenario pin.Table column)
                            | Some attr ->
                                renderPinValue scenario pin.Table attr value
                                |> Result.map (fun raw -> attr, raw))
                        |> Result.aggregate
                    bound
                    |> Result.bind (fun cells ->
                        let byKey = cells |> List.map (fun (a, raw) -> (ColumnRealization.columnNameText a.Column).ToLowerInvariant(), raw) |> Map.ofList
                        match Map.tryFind pkColumnKey byKey |> Option.flatten with
                        | None -> Result.failureOf (pinNeedsKey scenario pin.Table)
                        | Some pkRaw ->
                            let missingMandatory =
                                kind.Attributes
                                |> List.filter (fun a ->
                                    not a.Column.IsNullable
                                    && not a.IsIdentity
                                    && not (Map.containsKey ((ColumnRealization.columnNameText a.Column).ToLowerInvariant()) byKey))
                                // An identity PK is minted by the sink; a
                                // pinned PK arrives explicitly either way.
                                |> List.filter (fun a -> not a.IsPrimaryKey)
                            match missingMandatory with
                            | a :: _ ->
                                Result.failureOf (pinMissingMandatory scenario pin.Table (ColumnRealization.columnNameText a.Column))
                            | [] ->
                                let identifier =
                                    SsKey.synthesizedComposite "TWIN_PIN" [ TableCoordinate.key pin.Table; pkRaw; string i ]
                                    |> function Ok k -> k | Error _ -> kind.SsKey
                                Result.success
                                    (pkRaw,
                                     { StaticRow.Identifier = identifier
                                       StaticRow.Values = cells |> List.map (fun (a, raw) -> a.Name, raw) |> Map.ofList })))
                |> Result.aggregate
                |> Result.map (fun rows ->
                    { Kind = kind.SsKey
                      PkName = pk.Name
                      Rows = rows |> List.map snd
                      PoolKeys = rows |> List.map fst }))

    // ------------------------------------------------------------------
    // The compile.
    // ------------------------------------------------------------------

    /// Compile the effective scenario chain against the twin catalog.
    /// `defaultVolumeOf` supplies the resolved default row count for a
    /// kind (flat or centrality-weighted) — the parent side of a
    /// per-parent derivation when the parent carries no explicit rows.
    let compile
        (index: CatalogIndex)
        (defaultVolumeOf: SsKey -> int)
        (chain: ScenarioIr list)
        : Result<CompiledScenario> =
        match chain with
        | [] -> Result.success empty
        | _ ->
            let scenarioName = (List.last chain).Name
            let tables, pins = mergeChain chain

            // Explicit row volumes first — the parent side of ratios.
            let explicitVolumes =
                tables
                |> List.choose (fun (coord, o) -> o.Rows |> Option.map (fun rows -> coord, o, rows))
                |> List.map (fun (coord, _, rows) ->
                    CatalogIndex.bindKind index coord
                    |> Result.map (fun kind -> TableCoordinate.key coord, (kind.SsKey, rows)))
                |> Result.aggregate
                |> Result.map Map.ofList

            match explicitVolumes with
            | Error es -> Result.failure es
            | Ok explicits ->
                let volumeOfParent (parentCoord: TableCoordinate) (parentKind: Kind) : int =
                    match Map.tryFind (TableCoordinate.key parentCoord) explicits with
                    | Some (_, rows) -> rows
                    | None -> defaultVolumeOf parentKind.SsKey

                let derivedVolumes =
                    tables
                    |> List.collect (fun (coord, o) ->
                        match o.PerParent, o.Rows with
                        | [], _ -> []
                        | _ :: _, Some _ -> [ Result.failure [ volumeConflict scenarioName coord ] ]
                        | ratios, None ->
                            [ CatalogIndex.bindKind index coord
                              |> Result.bind (fun childKind ->
                                  ratios
                                  |> List.map (fun (parentCoord, mean) ->
                                      CatalogIndex.bindKind index parentCoord
                                      |> Result.bind (fun parentKind ->
                                          let hasReference =
                                              childKind.References
                                              |> List.exists (fun r -> r.TargetKind = parentKind.SsKey)
                                          if not hasReference then
                                              Result.failureOf (noReference scenarioName coord parentCoord)
                                          else
                                              let parentRows = volumeOfParent parentCoord parentKind
                                              Result.success (decimal parentRows * mean)))
                                  |> Result.aggregate
                                  |> Result.map (fun derived ->
                                      // Multiple ratios on one child: the
                                      // largest requirement wins (the others
                                      // hold as at-least averages).
                                      let rows = derived |> List.map (fun d -> int (System.Decimal.Round d)) |> List.max
                                      childKind.SsKey, VolumeTarget.Absolute (max 0 rows))) ])
                    |> Result.aggregate

                let columnCompiles =
                    tables
                    |> List.collect (fun (coord, o) ->
                        o.Columns
                        |> List.map (fun (column, over) ->
                            ColumnCoordinate.create coord column
                            |> Result.bind (CatalogIndex.bindColumn index)
                            |> Result.bind (fun (kind, attr) ->
                                let columnCoord = TwinIdentity.coordinateOfColumn kind attr
                                let sampleSize =
                                    match Map.tryFind (TableCoordinate.key coord) explicits with
                                    | Some (_, rows) -> int64 rows
                                    | None -> int64 (defaultVolumeOf kind.SsKey)
                                match over with
                                | Weights weights ->
                                    match attr.Type with
                                    | PrimitiveType.Text ->
                                        let frequencies = weights |> List.map (fun (v, ratio) -> v, int64 ratio)
                                        CategoricalDistribution.create attr.SsKey frequencies (int64 (List.length weights)) false (ProbeStatus.observed sampleSize)
                                        |> Result.map (fun dist ->
                                            AttributeDistribution.Categorical dist, attr.SsKey, Some (Name.value attr.Name))
                                    | _ -> Result.failureOf (weightsOnNonText scenarioName columnCoord)
                                | Between (loText, hiText, skew) ->
                                    let bounds =
                                        match attr.Type with
                                        | PrimitiveType.DateTime | PrimitiveType.Date ->
                                            match tryParseDate loText, tryParseDate hiText with
                                            | Some lo, Some hi -> Result.success (decimal lo.Ticks, decimal hi.Ticks)
                                            | _ -> Result.failureOf (windowMalformed scenarioName columnCoord "an ISO date (yyyy-MM-dd)")
                                        | PrimitiveType.Integer | PrimitiveType.Decimal ->
                                            match tryParseDecimal loText, tryParseDecimal hiText with
                                            | Some lo, Some hi -> Result.success (lo, hi)
                                            | _ -> Result.failureOf (windowMalformed scenarioName columnCoord "a number")
                                        | _ -> Result.failureOf (windowUnsupported scenarioName columnCoord)
                                    bounds
                                    |> Result.bind (fun (lo, hi) ->
                                        if lo >= hi then Result.failureOf (windowInverted scenarioName columnCoord)
                                        else
                                            windowDistribution attr.SsKey lo hi skew sampleSize
                                            |> Result.map (fun dist ->
                                                AttributeDistribution.Numeric dist, attr.SsKey, None)))))
                    |> Result.aggregate

                let compiledPins =
                    pins |> List.map (compilePin index scenarioName) |> Result.aggregate

                match derivedVolumes, columnCompiles, compiledPins with
                | Ok derived, Ok columns, Ok pinList ->
                    let volumes =
                        (explicits |> Map.toList |> List.map (fun (_, (key, rows)) -> key, VolumeTarget.Absolute rows))
                        @ derived
                        |> Map.ofList
                    let overlayDists = columns |> List.map (fun (d, _, _) -> d)
                    let overlayKeys = columns |> List.map (fun (_, k, _) -> k) |> Set.ofList
                    let preserve =
                        columns |> List.choose (fun (_, _, p) -> p) |> Set.ofList
                    let overlay (profile: Profile) : Profile =
                        { profile with
                            Distributions =
                                (profile.Distributions
                                 |> List.filter (fun d ->
                                     let key =
                                         match d with
                                         | AttributeDistribution.Categorical c -> c.AttributeKey
                                         | AttributeDistribution.Numeric n -> n.AttributeKey
                                     not (Set.contains key overlayKeys)))
                                @ overlayDists }
                    Result.success
                        { Volumes = volumes
                          Overlay = overlay
                          ForcePreserve = preserve
                          UnSynthesize = preserve
                          Pins = pinList }
                | dR, cR, pR ->
                    Result.failure (Result.errors dR @ Result.errors cR @ Result.errors pR)

    /// Merge pinned rows into a realized dataset: a minted row whose key
    /// collides with a pin gives way (the pin wins, deterministically);
    /// pins land first in each kind's list. Applied AFTER Faker
    /// realization so pinned values stay verbatim.
    let applyPins (pins: CompiledPin list) (dataset: Map<SsKey, StaticRow list>) : Map<SsKey, StaticRow list> =
        pins
        |> List.fold
            (fun (acc: Map<SsKey, StaticRow list>) pin ->
                let existing = Map.tryFind pin.Kind acc |> Option.defaultValue []
                let pinnedKeys = Set.ofList pin.PoolKeys
                let kept =
                    existing
                    |> List.filter (fun row ->
                        match Map.tryFind pin.PkName row.Values |> Option.flatten with
                        | Some pk -> not (Set.contains pk pinnedKeys)
                        | None -> true)
                Map.add pin.Kind (pin.Rows @ kept) acc)
            dataset

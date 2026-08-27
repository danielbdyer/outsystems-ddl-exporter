namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Twin.Core

/// THE TWIN — the volume shell (F4): magnitude realism, discovered
/// rather than configured.
///
/// σ materializes rows in memory, so a production-magnitude table mints
/// at a capped volume (`scale` below one); every witness window and
/// absolute count, though, was budgeted against the EVIDENCE volume.
/// The shell closes the gap after the mint, in the engine where volume
/// is cheap: each evidence-riding kind whose minted rows fall short of
/// its recorded row count is amplified by deterministic
/// doubling — `INSERT … SELECT` over its own rows, ordered by key —
/// until the recorded volume (or the fixed global budget) is reached.
/// The σ-minted core keeps the fidelity (vocabularies, sectors, joints,
/// envelopes); the shell multiplies its distributions proportionally:
/// copied FK values keep referencing the core's parents, so validity
/// and fan-out skew survive by construction, and the witness pass runs
/// AFTER amplification, restoring every exact count (null rates, empty
/// floors, planted extremes) on the full-magnitude landscape.
///
/// Legality is planned per kind and named: the primary key must be a
/// single column that is IDENTITY (the engine mints shell keys) or a
/// positive INTEGER (offset by `startMax · 2^(round−1)`, collision-free
/// under partial rounds by induction); every other unique index must
/// have a text member wide enough to carry the key-stamped suffix
/// (declared length ≥ 25 — the mutation is
/// `LEFT(value, L−24) + '~' + key + '~' + round`, NULLs preserved);
/// computed columns are omitted (the engine computes). A kind that
/// cannot be amplified lawfully is a NAMED skip, and a budget-bounded
/// shortfall is reported, never silent. At scale one the shell is inert
/// by construction: minted equals recorded, nothing amplifies.
[<RequireQualifiedAccess>]
module VolumeShell =

    /// The fixed global budget: extra shell rows one seed may add across
    /// all kinds. Bounds bake time on production-magnitude estates; the
    /// remainder is a reported shortfall.
    let shellRowBudget : int64 = 2_000_000L

    /// Characters reserved for the uniqueness suffix: '~' + a 20-digit
    /// key + '~' + a 2-digit round.
    let private suffixReserve : int = 24

    type ShellSkip = {
        Table  : string
        Reason : string
    }

    type ShellReport = {
        /// Rows the shell added across all kinds.
        AddedRows : int64
        /// Evidence rows the budget could not reach.
        Shortfall : int64
        Skips     : ShellSkip list
    }

    let emptyReport : ShellReport = { AddedRows = 0L; Shortfall = 0L; Skips = [] }

    // ------------------------------------------------------------------
    // Planning — pure, per kind.
    // ------------------------------------------------------------------

    type private ColumnRole =
        /// Omitted from the insert entirely (IDENTITY, computed).
        | Omitted
        /// Copied verbatim.
        | Copied of column: string
        /// The non-identity integer key, offset per round.
        | OffsetKey of column: string
        /// A unique text member, key-stamped per round.
        | Stamped of column: string * declaredLength: int

    type ShellDecision =
        | Amplifiable
        | Skip of reason: string

    let private primaryKey (kind: Kind) : Attribute option =
        match kind.Attributes |> List.filter (fun a -> a.IsPrimaryKey) with
        | [ pk ] -> Some pk
        | _ -> None

    let private uniqueIndexesBeyondKey (kind: Kind) =
        kind.Indexes
        |> List.filter (fun i ->
            match i.Uniqueness with
            | IndexUniqueness.Unique -> true
            | IndexUniqueness.PrimaryKey | IndexUniqueness.NotUnique -> false)

    let private stampCapable (kind: Kind) (attrKey: SsKey) : bool =
        kind.Attributes
        |> List.exists (fun a ->
            a.SsKey = attrKey
            && a.Type = PrimitiveType.Text
            && not a.IsPrimaryKey
            && (match a.Length with Some l -> l >= suffixReserve + 1 | None -> false))

    /// Can this kind lawfully carry a shell? Pure and named.
    let decide (kind: Kind) : ShellDecision =
        match primaryKey kind with
        | None -> Skip "keyUnsupported"
        | Some pk ->
            if not pk.IsIdentity && pk.Type <> PrimitiveType.Integer then Skip "keyUnsupported"
            else
                let incapable =
                    uniqueIndexesBeyondKey kind
                    |> List.exists (fun i ->
                        not (i.Columns |> List.exists (fun c -> stampCapable kind c.Attribute)))
                if incapable then Skip "uniqueUnsupported"
                else Amplifiable

    let private rolesOf (kind: Kind) : ColumnRole list =
        let stampable =
            uniqueIndexesBeyondKey kind
            |> List.collect (fun i -> i.Columns |> List.map (fun c -> c.Attribute))
            |> List.filter (stampCapable kind)
            |> Set.ofList
        kind.Attributes
        |> List.map (fun a ->
            let column = ColumnRealization.columnNameText a.Column
            if a.IsIdentity || a.Computed.IsSome then Omitted
            elif a.IsPrimaryKey then OffsetKey column
            elif Set.contains a.SsKey stampable then Stamped (column, a.Length |> Option.defaultValue 0)
            else Copied column)

    // ------------------------------------------------------------------
    // Emission — deterministic T-SQL, one INSERT per doubling round.
    // ------------------------------------------------------------------

    let private quote (s: string) : string = Projection.Targets.SSDT.Render.quote s

    let private n (v: int64) : string =
        v.ToString(System.Globalization.CultureInfo.InvariantCulture)

    /// One amplification round: copy `take` of the current rows (by key
    /// order — the identity assignment and the partial round are then
    /// deterministic), keys offset past everything the induction has
    /// produced, stamped columns re-keyed, everything else verbatim.
    let emitRound
        (kind: Kind)
        (keyColumn: string)
        (offset: int64)
        (round: int64)
        (take: int64)
        : string =
        let table = Projection.Targets.SSDT.Render.tableQualified kind.Physical
        let roles = rolesOf kind
        let targets =
            roles
            |> List.choose (fun r ->
                match r with
                | Omitted -> None
                | Copied c | OffsetKey c | Stamped (c, _) -> Some (quote c))
        let sources =
            roles
            |> List.choose (fun r ->
                match r with
                | Omitted -> None
                | Copied c -> Some (System.String.Concat("src.", quote c))  // LINT-ALLOW: terminal shell SQL text; identifiers pass through the SSDT renderer's quoting
                | OffsetKey c -> Some (System.String.Concat("src.", quote c, " + ", n offset))  // LINT-ALLOW: terminal shell SQL text; identifiers pass through the SSDT renderer's quoting
                | Stamped (c, declared) ->
                    Some
                        (System.String.Concat(  // LINT-ALLOW: terminal shell SQL text; identifiers pass through the SSDT renderer's quoting
                            "CASE WHEN src.", quote c, " IS NULL THEN NULL ELSE CONCAT(LEFT(src.", quote c,
                            ", ", string (declared - suffixReserve), "), N'~', src.", quote keyColumn,
                            ", N'~', ", n round, ") END")))
        System.String.Concat(  // LINT-ALLOW: terminal shell SQL text; identifiers pass through the SSDT renderer's quoting
            "INSERT INTO ", table, " (", String.concat ", " targets, ") SELECT TOP (", n take, ") ",  // LINT-ALLOW: comma-joined identifier lists inside the terminal shell SQL; every identifier passed through the renderer's quoting
            String.concat ", " sources, " FROM ", table, " AS src ORDER BY src.", quote keyColumn, ";")  // LINT-ALLOW: comma-joined expression list inside the terminal shell SQL; same renderer-quoted identifiers

    // ------------------------------------------------------------------
    // The driver.
    // ------------------------------------------------------------------

    let private scalar (cnn: SqlConnection) (sql: string) : Task<int64> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql  // LINT-ALLOW: terminal shell count/max probe at the command boundary
            let! v = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt64 v
        }

    /// Observed row count for a kind from the layered evidence — the
    /// same reading the mint's volume resolution takes.
    let private observedRows (profile: Profile) (kind: Kind) : int64 =
        kind.Attributes
        |> List.choose (fun a -> Profile.tryFindColumn a.SsKey profile |> Option.map (fun c -> c.RowCount))
        |> function [] -> 0L | xs -> List.max xs

    // Amplify one kind toward its target under the remaining budget.
    // Hoisted per the FS3511 survival rule; returns (added, shortfall).
    let private amplifyKind
        (cnn: SqlConnection)
        (kind: Kind)
        (keyColumn: string)
        (target: int64)
        (budget: int64)
        : Task<int64 * int64> =
        task {
            let table = Projection.Targets.SSDT.Render.tableQualified kind.Physical
            let! current = scalar cnn (System.String.Concat("SELECT COUNT_BIG(*) FROM ", table, ";"))  // LINT-ALLOW: terminal shell count probe; the identifier passes through the SSDT renderer's quoting
            if current <= 0L || current >= target then return 0L, 0L
            else
                let reachable = min target (current + budget)
                let shortfall = target - reachable
                let! startMax =
                    scalar cnn (System.String.Concat("SELECT ISNULL(MAX(", quote keyColumn, "), 0) FROM ", table, ";"))  // LINT-ALLOW: terminal shell key-max probe; identifiers pass through the SSDT renderer's quoting
                let mutable have = current  // LINT-ALLOW: the doubling walk's cursor — the round count depends on live row counts; confined to this loop
                let mutable round = 1L  // LINT-ALLOW: the doubling walk's round counter; same confinement
                while have < reachable do
                    let take = min have (reachable - have)
                    let offset = startMax * (1L <<< int (min 62L (round - 1L)))
                    do! Deploy.executeBatch cnn (emitRound kind keyColumn offset round take)
                    have <- have + take  // LINT-ALLOW: the doubling walk's cursor advance; same confinement
                    round <- round + 1L  // LINT-ALLOW: the doubling walk's round advance; same confinement
                return (have - current), shortfall
        }

    /// The pass: every evidence-riding kind (no explicit volume, no
    /// provided pool) whose minted rows fall short of its recorded rows
    /// is amplified toward the record, under the global budget. Copied
    /// FK values reference the σ core, so order is free and validity
    /// holds by construction.
    let amplify
        (cnn: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (pools: Map<SsKey, string list>)
        : Task<Result<ShellReport>> =
        task {
            let candidates =
                Catalog.allKinds catalog
                |> List.filter (fun k ->
                    not (Map.containsKey k.SsKey config.VolumeByKind)
                    && not (Map.containsKey k.SsKey pools))
            let skips = System.Collections.Generic.List<ShellSkip>()
            let mutable added = 0L  // LINT-ALLOW: the while-walk's accumulator — FS3511 forces this shape (a `for` with an await in the body over rich elements does not compile in Release); confined to this loop
            let mutable shortfall = 0L  // LINT-ALLOW: the while-walk's accumulator; same FS3511 confinement
            let mutable remaining = candidates  // LINT-ALLOW: the while-walk's cursor; same FS3511 confinement
            try
                while not (List.isEmpty remaining) do
                    let kind = List.head remaining
                    remaining <- List.tail remaining  // LINT-ALLOW: the while-walk's cursor advance; same FS3511 confinement
                    let target = observedRows profile kind
                    if target > 0L then
                        match decide kind with
                        | Skip reason ->
                            // A skip is only a finding when a shell was
                            // actually owed.
                            let table = TableCoordinate.text (TwinIdentity.coordinateOfKind kind)
                            let! current = scalar cnn (System.String.Concat("SELECT COUNT_BIG(*) FROM ", Projection.Targets.SSDT.Render.tableQualified kind.Physical, ";"))  // LINT-ALLOW: terminal shell count probe; the identifier passes through the SSDT renderer's quoting
                            if current > 0L && current < target then
                                skips.Add { Table = table; Reason = reason }
                                shortfall <- shortfall + (target - current)  // LINT-ALLOW: the while-walk's accumulator advance; same FS3511 confinement
                        | Amplifiable ->
                            match primaryKey kind with
                            | None -> ()
                            | Some pk ->
                                let keyColumn = ColumnRealization.columnNameText pk.Column
                                let budget = max 0L (shellRowBudget - added)
                                let! kindAdded = amplifyKind cnn kind keyColumn target budget
                                let (a, s) = kindAdded
                                added <- added + a  // LINT-ALLOW: the while-walk's accumulator advance; same FS3511 confinement
                                shortfall <- shortfall + s  // LINT-ALLOW: the while-walk's accumulator advance; same FS3511 confinement
                return Result.success { AddedRows = added; Shortfall = shortfall; Skips = List.ofSeq skips }
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.createWithMetadata
                            "twin.shell.failed"
                            "The volume shell did not complete."
                            (Map.ofList [ "detail", Some ex.Message ]))
        }

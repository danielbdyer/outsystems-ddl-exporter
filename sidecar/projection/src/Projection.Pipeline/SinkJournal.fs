// LINT-ALLOW-FILE-MUTATION: journal persistence boundary (the data-sink
//   chapter S4b; the CaptureJournal precedent) — NDJSON via System.Text.Json
//   (the typed-AST library for the format) over an append-only file with an
//   fsync'd FileStream; the displacement algebra, the line codec, and the
//   LedgerSpec instance are pure — the I/O surface (fsync append /
//   fail-closed load) is the thin boundary the witness and the sync verb
//   call.
namespace Projection.Pipeline

open System
open System.IO
open System.Text.Json
open Projection.Core
open Projection.Adapters.OssysSql
open FsToolkit.ErrorHandling

/// The sink's displacement journal (S4b) — the acquisition grain's ledger,
/// the FOURTH `Ledger.LedgerSpec` instance (after the capture journal's
/// chunk grain, the episode store's episode grain, and the G10 marker
/// grain). Append-only NDJSON, one line per displacement; fixed filename
/// (`journal.ndjson` — the digest directory is the key, so no digest
/// filename); fsync on append (the CaptureJournal durability posture); a
/// torn TRAILING line is tolerated, an interior corrupt line refuses by
/// name (`RunLedger`'s skip-malformed posture is exactly the silent
/// forgetting a metadata ledger must never do); a regressing syncId refuses
/// on the `Ledger.resumeAdmit` drift channel (`sink.journal.syncRegression`).
/// No intent records: snapshots are idempotently re-derivable, so the
/// orphan-snapshot window repairs by re-differencing at the next witness
/// (`sink.journal.reconciledOrphanSnapshot`).
[<RequireQualifiedAccess>]
module SinkJournal =

    [<Literal>]
    let fileName = "journal.ndjson"

    /// One journal line: a displacement stamped with its sync coordinates
    /// (typed at align-III.1 — the ordinal VO makes a zero/negative sync
    /// unwritable; the wire stays a raw int and re-mints fail-closed).
    type JournalLine =
        {
            SyncId        : SyncOrdinal
            PrevSyncId    : SyncOrdinal option
            CapturedAtUtc : DateTimeOffset
            Displacement  : SinkDisplacement.Displacement
        }

    /// The LedgerSpec instance: state is the witnessed snapshot, ⊕ is
    /// `SinkDisplacement.applyOne`, genesis is the empty snapshot, and the
    /// resume discipline is `Linkage` (align-III.2 — retiring the tautology
    /// that made `FingerprintOf = SyncId` compare against itself). The
    /// linkage reads each line's SyncId as its group identity and PrevSyncId
    /// as the predecessor it claims; `Ledger.admitChain` verifies the sync
    /// chain for real — a group that does not link to the one before refuses
    /// by name. The write witness is the fsync'd append itself.
    let spec : LedgerSpec<MetadataSnapshotRunner.MetadataSnapshot, JournalLine, SyncOrdinal> =
        {
            Genesis = SinkDisplacement.emptySnapshot
            Apply = fun state line -> SinkDisplacement.applyOne state line.Displacement
            Admission = ChainAdmission.Linkage ((fun line -> line.SyncId), (fun line -> line.PrevSyncId))
        }

    // ------------------------------------------------------------------
    // Line codec (compact single-line JSON; row images via the snapshot
    // codec's row-grain surface — one encoding for bodies and lines).
    // ------------------------------------------------------------------

    let private writeKeyBasis (jw: Utf8JsonWriter) (basis: SinkDisplacement.KeyBasis) =
        jw.WritePropertyName "keyBasis"
        jw.WriteStartObject()
        match basis with
        | SinkDisplacement.KeyBasis.Native g ->
            jw.WriteString("kind", "native")
            jw.WriteString("guid", g.ToString("D"))
        | SinkDisplacement.KeyBasis.Positional n ->
            jw.WriteString("kind", "positional")
            jw.WriteNumber("id", n)
        | SinkDisplacement.KeyBasis.Composite parts ->
            jw.WriteString("kind", "composite")
            jw.WritePropertyName "parts"
            jw.WriteStartArray()
            for p in parts do jw.WriteStringValue p
            jw.WriteEndArray()
        jw.WriteEndObject()

    let private writeRowImage (jw: Utf8JsonWriter) (name: string) (row: SinkDisplacement.WitnessedRow option) =
        jw.WritePropertyName name
        match row with
        | None -> jw.WriteNullValue()
        | Some r ->
            jw.WriteStartObject()
            match r with
            | SinkDisplacement.WitnessedRow.Module x -> MetadataSnapshotCodec.Rows.writeModule jw x
            | SinkDisplacement.WitnessedRow.Entity x -> MetadataSnapshotCodec.Rows.writeEntity jw x
            | SinkDisplacement.WitnessedRow.Attribute x -> MetadataSnapshotCodec.Rows.writeAttribute jw x
            | SinkDisplacement.WitnessedRow.Reference x -> MetadataSnapshotCodec.Rows.writeReference jw x
            | SinkDisplacement.WitnessedRow.PhysicalTable x -> MetadataSnapshotCodec.Rows.writePhysicalTable jw x
            | SinkDisplacement.WitnessedRow.ColumnReality x -> MetadataSnapshotCodec.Rows.writeColumnReality jw x
            | SinkDisplacement.WitnessedRow.ColumnCheck x -> MetadataSnapshotCodec.Rows.writeColumnCheck jw x
            | SinkDisplacement.WitnessedRow.PhysColsPresent x -> MetadataSnapshotCodec.Rows.writePhysColsPresent jw x
            | SinkDisplacement.WitnessedRow.Index x -> MetadataSnapshotCodec.Rows.writeIndex jw x
            | SinkDisplacement.WitnessedRow.IndexColumn x -> MetadataSnapshotCodec.Rows.writeIndexColumn jw x
            | SinkDisplacement.WitnessedRow.FkReality x -> MetadataSnapshotCodec.Rows.writeFkReality jw x
            | SinkDisplacement.WitnessedRow.FkColumn x -> MetadataSnapshotCodec.Rows.writeFkColumn jw x
            | SinkDisplacement.WitnessedRow.Trigger x -> MetadataSnapshotCodec.Rows.writeTrigger jw x
            | SinkDisplacement.WitnessedRow.Sequence x -> MetadataSnapshotCodec.Rows.writeSequence jw x
            | SinkDisplacement.WitnessedRow.Temporal x -> MetadataSnapshotCodec.Rows.writeTemporal jw x
            | SinkDisplacement.WitnessedRow.Capability x -> MetadataSnapshotCodec.Rows.writeCapability jw x
            jw.WriteEndObject()

    let private domainToken (t: SinkDisplacement.DomainTransition) : string =
        match t with
        | SinkDisplacement.DomainTransition.EntityDeactivated -> "entityDeactivated"
        | SinkDisplacement.DomainTransition.EntityReactivated -> "entityReactivated"
        | SinkDisplacement.DomainTransition.EntityRehomed _ -> "entityRehomed"
        | SinkDisplacement.DomainTransition.EntityRegisteredExternal -> "entityRegisteredExternal"
        | SinkDisplacement.DomainTransition.PhysicalTableClaimChanged _ -> "physicalTableClaimChanged"
        | SinkDisplacement.DomainTransition.PhysicalTableSuperseded _ -> "physicalTableSuperseded"
        | SinkDisplacement.DomainTransition.AttributeRetired -> "attributeRetired"
        | SinkDisplacement.DomainTransition.AttributeReactivated -> "attributeReactivated"
        | SinkDisplacement.DomainTransition.AttributeRetyped _ -> "attributeRetyped"
        | SinkDisplacement.DomainTransition.ModuleRetired -> "moduleRetired"
        | SinkDisplacement.DomainTransition.ModuleReactivated -> "moduleReactivated"
        | SinkDisplacement.DomainTransition.ShapeChanged -> "shapeChanged"

    let private writeDomain (jw: Utf8JsonWriter) (domain: SinkDisplacement.DomainTransition option) =
        jw.WritePropertyName "domain"
        match domain with
        | None -> jw.WriteNullValue()
        | Some t ->
            jw.WriteStartObject()
            jw.WriteString("token", domainToken t)
            match t with
            | SinkDisplacement.DomainTransition.EntityRehomed (fromId, toId) ->
                jw.WriteNumber("fromEspaceId", fromId)
                jw.WriteNumber("toEspaceId", toId)
            | SinkDisplacement.DomainTransition.PhysicalTableClaimChanged (fromTable, toTable) ->
                jw.WriteString("fromTable", fromTable)
                jw.WriteString("toTable", toTable)
            | SinkDisplacement.DomainTransition.PhysicalTableSuperseded table ->
                jw.WriteString("table", table)
            | SinkDisplacement.DomainTransition.AttributeRetyped facets ->
                jw.WritePropertyName "facets"
                jw.WriteStartArray()
                for f in facets do jw.WriteStringValue (string f)
                jw.WriteEndArray()
            | _ -> ()
            jw.WriteEndObject()

    /// Render one journal line as compact single-line JSON (no newline).
    let renderLine (line: JournalLine) : string =
        let node =
            JsonWriting.writeToNode (fun jw ->
                jw.WriteStartObject()
                jw.WriteNumber("syncId", SyncOrdinal.value line.SyncId)
                (match line.PrevSyncId with
                 | Some p -> jw.WriteNumber("prevSyncId", SyncOrdinal.value p)
                 | None -> jw.WriteNull("prevSyncId"))
                jw.WriteString("capturedAtUtc", line.CapturedAtUtc.ToString("O", Globalization.CultureInfo.InvariantCulture))
                jw.WriteString("table", SinkDisplacement.SinkTable.token line.Displacement.Table)
                jw.WriteString("key", line.Displacement.KeyText)
                writeKeyBasis jw line.Displacement.KeyBasis
                writeDomain jw line.Displacement.Domain
                writeRowImage jw "before" line.Displacement.Before
                writeRowImage jw "after" line.Displacement.After
                jw.WriteEndObject())
        node.ToJsonString()

    // ------------------------------------------------------------------
    // Line decode (fail-closed; the journal read path).
    // ------------------------------------------------------------------

    let private fail (code: string) (msg: string) : Result<'a> =
        Result.failureOf (ValidationError.create code msg)

    let private tableOfToken (token: string) : Result<SinkDisplacement.SinkTable> =
        SinkDisplacement.SinkTable.all
        |> List.tryFind (fun t -> SinkDisplacement.SinkTable.token t = token)
        |> function
           | Some t -> Ok t
           | None -> fail "sink.journal.unknownTable" (sprintf "unknown journal table token '%s'" token)

    let private readRowImage (table: SinkDisplacement.SinkTable) (el: JsonElement) : Result<SinkDisplacement.WitnessedRow option> =
        if el.ValueKind = JsonValueKind.Null then Ok None
        else
            let lift (r: Result<'a>) (wrap: 'a -> SinkDisplacement.WitnessedRow) = r |> Result.map (wrap >> Some)
            match table with
            | SinkDisplacement.SinkTable.Modules -> lift (MetadataSnapshotCodec.Rows.readModule el) SinkDisplacement.WitnessedRow.Module
            | SinkDisplacement.SinkTable.Entities -> lift (MetadataSnapshotCodec.Rows.readEntity el) SinkDisplacement.WitnessedRow.Entity
            | SinkDisplacement.SinkTable.Attributes -> lift (MetadataSnapshotCodec.Rows.readAttribute el) SinkDisplacement.WitnessedRow.Attribute
            | SinkDisplacement.SinkTable.References -> lift (MetadataSnapshotCodec.Rows.readReference el) SinkDisplacement.WitnessedRow.Reference
            | SinkDisplacement.SinkTable.PhysicalTables -> lift (MetadataSnapshotCodec.Rows.readPhysicalTable el) SinkDisplacement.WitnessedRow.PhysicalTable
            | SinkDisplacement.SinkTable.ColumnReality -> lift (MetadataSnapshotCodec.Rows.readColumnReality el) SinkDisplacement.WitnessedRow.ColumnReality
            | SinkDisplacement.SinkTable.ColumnChecks -> lift (MetadataSnapshotCodec.Rows.readColumnCheck el) SinkDisplacement.WitnessedRow.ColumnCheck
            | SinkDisplacement.SinkTable.PhysColsPresent -> lift (MetadataSnapshotCodec.Rows.readPhysColsPresent el) SinkDisplacement.WitnessedRow.PhysColsPresent
            | SinkDisplacement.SinkTable.Indexes -> lift (MetadataSnapshotCodec.Rows.readIndex el) SinkDisplacement.WitnessedRow.Index
            | SinkDisplacement.SinkTable.IndexColumns -> lift (MetadataSnapshotCodec.Rows.readIndexColumn el) SinkDisplacement.WitnessedRow.IndexColumn
            | SinkDisplacement.SinkTable.ForeignKeysReality -> lift (MetadataSnapshotCodec.Rows.readFkReality el) SinkDisplacement.WitnessedRow.FkReality
            | SinkDisplacement.SinkTable.ForeignKeyColumns -> lift (MetadataSnapshotCodec.Rows.readFkColumn el) SinkDisplacement.WitnessedRow.FkColumn
            | SinkDisplacement.SinkTable.Triggers -> lift (MetadataSnapshotCodec.Rows.readTrigger el) SinkDisplacement.WitnessedRow.Trigger
            | SinkDisplacement.SinkTable.Sequences -> lift (MetadataSnapshotCodec.Rows.readSequence el) SinkDisplacement.WitnessedRow.Sequence
            | SinkDisplacement.SinkTable.Temporal -> lift (MetadataSnapshotCodec.Rows.readTemporal el) SinkDisplacement.WitnessedRow.Temporal
            | SinkDisplacement.SinkTable.Capabilities -> lift (MetadataSnapshotCodec.Rows.readCapability el) SinkDisplacement.WitnessedRow.Capability

    let private asNonNullString (code: string) (context: string) (el: JsonElement) : Result<string> =
        match el.GetString() with
        | null -> fail code (sprintf "%s: string element returned null" context)
        | s -> Ok s

    let private readKeyBasis (el: JsonElement) : Result<SinkDisplacement.KeyBasis> =
        match el.TryGetProperty "kind" with
        | true, k when k.ValueKind = JsonValueKind.String ->
            asNonNullString "sink.journal.corruptLine" "keyBasis kind" k
            |> Result.bind (fun kind ->
                match kind with
                | "native" ->
                    match el.TryGetProperty "guid" with
                    | true, g when g.ValueKind = JsonValueKind.String ->
                        asNonNullString "sink.journal.corruptLine" "keyBasis guid" g
                        |> Result.bind (fun gs ->
                            match Guid.TryParseExact(gs, "D") with
                            | true, guid -> Ok (SinkDisplacement.KeyBasis.Native guid)
                            | _ -> fail "sink.journal.corruptLine" "native keyBasis carries a non-Guid")
                    | _ -> fail "sink.journal.corruptLine" "native keyBasis missing guid"
                | "positional" ->
                    match el.TryGetProperty "id" with
                    | true, n when n.ValueKind = JsonValueKind.Number -> Ok (SinkDisplacement.KeyBasis.Positional (n.GetInt32()))
                    | _ -> fail "sink.journal.corruptLine" "positional keyBasis missing id"
                | "composite" ->
                    match el.TryGetProperty "parts" with
                    | true, parts when parts.ValueKind = JsonValueKind.Array ->
                        parts.EnumerateArray()
                        |> Seq.map (fun p ->
                            if p.ValueKind = JsonValueKind.String then
                                asNonNullString "sink.journal.corruptLine" "keyBasis part" p
                            else fail "sink.journal.corruptLine" "composite keyBasis part is not a string")
                        |> Seq.toList
                        |> List.fold (fun acc item ->
                            match acc, item with
                            | Ok xs, Ok x -> Ok (x :: xs)
                            | Error e, _ -> Error e
                            | _, Error e -> Error e) (Ok [])
                        |> Result.map (List.rev >> SinkDisplacement.KeyBasis.Composite)
                    | _ -> fail "sink.journal.corruptLine" "composite keyBasis missing parts"
                | other -> fail "sink.journal.corruptLine" (sprintf "unknown keyBasis kind '%s'" other))
        | _ -> fail "sink.journal.corruptLine" "keyBasis missing kind"

    let private facetOfToken (t: string) : Result<AttributeFacet> =
        match t with
        | "DataType" -> Ok AttributeFacet.DataType
        | "Nullability" -> Ok AttributeFacet.Nullability
        | "PrimaryKey" -> Ok AttributeFacet.PrimaryKey
        | "Length" -> Ok AttributeFacet.Length
        | "Precision" -> Ok AttributeFacet.Precision
        | "Scale" -> Ok AttributeFacet.Scale
        | "Identity" -> Ok AttributeFacet.Identity
        | "DefaultValue" -> Ok AttributeFacet.DefaultValue
        | "Computed" -> Ok AttributeFacet.Computed
        | other -> fail "sink.journal.corruptLine" (sprintf "unknown attribute facet token '%s'" other)

    /// align-II.10 (E3) — decode the domain classification the writer
    /// records. Payload-bearing tokens restore their payloads; an unknown
    /// token or a malformed payload fail-closes (a corrupt classification
    /// never silently reads as unclassified).
    let private readDomain (el: JsonElement) : Result<SinkDisplacement.DomainTransition option> =
        match el.TryGetProperty "domain" with
        | false, _ -> fail "sink.journal.corruptLine" "line missing domain (null is a written value, never an omission)"
        | true, v when v.ValueKind = JsonValueKind.Null -> Ok None
        | true, v when v.ValueKind = JsonValueKind.Object ->
            match v.TryGetProperty "token" with
            | true, t when t.ValueKind = JsonValueKind.String ->
                asNonNullString "sink.journal.corruptLine" "domain token" t
                |> Result.bind (fun token ->
                    let str (name: string) : Result<string> =
                        match v.TryGetProperty name with
                        | true, x when x.ValueKind = JsonValueKind.String ->
                            asNonNullString "sink.journal.corruptLine" (sprintf "domain %s" name) x
                        | _ -> fail "sink.journal.corruptLine" (sprintf "domain '%s' missing '%s'" token name)
                    let num (name: string) : Result<int> =
                        match v.TryGetProperty name with
                        | true, x when x.ValueKind = JsonValueKind.Number -> Ok (x.GetInt32())
                        | _ -> fail "sink.journal.corruptLine" (sprintf "domain '%s' missing '%s'" token name)
                    match token with
                    | "entityDeactivated" -> Ok (Some SinkDisplacement.DomainTransition.EntityDeactivated)
                    | "entityReactivated" -> Ok (Some SinkDisplacement.DomainTransition.EntityReactivated)
                    | "entityRegisteredExternal" -> Ok (Some SinkDisplacement.DomainTransition.EntityRegisteredExternal)
                    | "attributeRetired" -> Ok (Some SinkDisplacement.DomainTransition.AttributeRetired)
                    | "attributeReactivated" -> Ok (Some SinkDisplacement.DomainTransition.AttributeReactivated)
                    | "moduleRetired" -> Ok (Some SinkDisplacement.DomainTransition.ModuleRetired)
                    | "moduleReactivated" -> Ok (Some SinkDisplacement.DomainTransition.ModuleReactivated)
                    | "shapeChanged" -> Ok (Some SinkDisplacement.DomainTransition.ShapeChanged)
                    | "entityRehomed" ->
                        num "fromEspaceId"
                        |> Result.bind (fun fromId ->
                            num "toEspaceId"
                            |> Result.map (fun toId -> Some (SinkDisplacement.DomainTransition.EntityRehomed (fromId, toId))))
                    | "physicalTableClaimChanged" ->
                        str "fromTable"
                        |> Result.bind (fun fromTable ->
                            str "toTable"
                            |> Result.map (fun toTable -> Some (SinkDisplacement.DomainTransition.PhysicalTableClaimChanged (fromTable, toTable))))
                    | "physicalTableSuperseded" ->
                        str "table"
                        |> Result.map (fun table -> Some (SinkDisplacement.DomainTransition.PhysicalTableSuperseded table))
                    | "attributeRetyped" ->
                        match v.TryGetProperty "facets" with
                        | true, arr when arr.ValueKind = JsonValueKind.Array ->
                            arr.EnumerateArray()
                            |> Seq.map (fun f ->
                                if f.ValueKind = JsonValueKind.String then
                                    asNonNullString "sink.journal.corruptLine" "facet" f |> Result.bind facetOfToken
                                else fail "sink.journal.corruptLine" "facet is not a string")
                            |> Seq.toList
                            |> List.fold (fun acc item ->
                                match acc, item with
                                | Ok xs, Ok x -> Ok (x :: xs)
                                | Error e, _ -> Error e
                                | _, Error e -> Error e) (Ok [])
                            |> Result.map (List.rev >> SinkDisplacement.DomainTransition.AttributeRetyped >> Some)
                        | _ -> fail "sink.journal.corruptLine" "domain 'attributeRetyped' missing 'facets'"
                    | other -> fail "sink.journal.corruptLine" (sprintf "unknown domain token '%s'" other))
            | _ -> fail "sink.journal.corruptLine" "domain object missing token"
        | _ -> fail "sink.journal.corruptLine" "malformed 'domain' (expected object or null)"

    /// Parse one journal line — the full inverse of `renderLine`
    /// (align-II.10: the domain classification DECODES; `parseLine ∘
    /// renderLine = id` is a live law beside T19, so a journal read sees
    /// exactly the classification the witness recorded).
    let parseLine (text: string) : Result<JournalLine> =
        let parsed =
            try Ok (JsonDocument.Parse text)
            with ex -> fail "sink.journal.corruptLine" (sprintf "journal line did not parse: %s" ex.Message)
        parsed
        |> Result.bind (fun doc ->
            use doc = doc
            let root = doc.RootElement
            result {
                let! syncId =
                    match root.TryGetProperty "syncId" with
                    | true, v when v.ValueKind = JsonValueKind.Number ->
                        // align-III.1 — the ordinal re-mints fail-closed: a
                        // stored 0/negative is a corrupt line by name, never
                        // a readable "edition".
                        match SyncOrdinal.create (v.GetInt32()) with
                        | Ok o -> Ok o
                        | Error m -> fail "sink.journal.corruptLine" (sprintf "line syncId: %s" m)
                    | _ -> fail "sink.journal.corruptLine" "line missing syncId"
                let! prevSyncId =
                    match root.TryGetProperty "prevSyncId" with
                    | true, v when v.ValueKind = JsonValueKind.Number ->
                        match SyncOrdinal.create (v.GetInt32()) with
                        | Ok o -> Ok (Some o)
                        | Error m -> fail "sink.journal.corruptLine" (sprintf "line prevSyncId: %s" m)
                    | _ -> Ok None
                let! capturedAt =
                    match root.TryGetProperty "capturedAtUtc" with
                    | true, v when v.ValueKind = JsonValueKind.String ->
                        asNonNullString "sink.journal.corruptLine" "capturedAtUtc" v
                        |> Result.bind (fun text ->
                            match DateTimeOffset.TryParse(text, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind) with
                            | true, dto -> Ok dto
                            | _ -> fail "sink.journal.corruptLine" "capturedAtUtc is not round-trippable")
                    | _ -> fail "sink.journal.corruptLine" "line missing capturedAtUtc"
                let! table =
                    match root.TryGetProperty "table" with
                    | true, v when v.ValueKind = JsonValueKind.String ->
                        asNonNullString "sink.journal.corruptLine" "table" v
                        |> Result.bind tableOfToken
                    | _ -> fail "sink.journal.corruptLine" "line missing table"
                let! key =
                    match root.TryGetProperty "key" with
                    | true, v when v.ValueKind = JsonValueKind.String ->
                        asNonNullString "sink.journal.corruptLine" "key" v
                    | _ -> fail "sink.journal.corruptLine" "line missing key"
                let! basis =
                    match root.TryGetProperty "keyBasis" with
                    | true, v when v.ValueKind = JsonValueKind.Object -> readKeyBasis v
                    | _ -> fail "sink.journal.corruptLine" "line missing keyBasis"
                let! before =
                    match root.TryGetProperty "before" with
                    | true, v -> readRowImage table v
                    | _ -> fail "sink.journal.corruptLine" "line missing before image"
                let! after =
                    match root.TryGetProperty "after" with
                    | true, v -> readRowImage table v
                    | _ -> fail "sink.journal.corruptLine" "line missing after image"
                let! domain = readDomain root
                return
                    { SyncId = syncId
                      PrevSyncId = prevSyncId
                      CapturedAtUtc = capturedAt
                      Displacement =
                        { Table = table
                          KeyText = key
                          KeyBasis = basis
                          Before = before
                          After = after
                          Domain = domain } }
            })

    // ------------------------------------------------------------------
    // The file boundary.
    // ------------------------------------------------------------------

    /// Append lines with fsync (the CaptureJournal durability posture):
    /// the append is the write witness — a line the caller sees appended
    /// has reached the platters, so a crash leaves no half-claimed ledger.
    let append (path: string) (lines: JournalLine list) : Result<unit> =
        try
            (match Path.GetDirectoryName path with
             | null -> ()
             | "" -> ()
             | dir -> Directory.CreateDirectory dir |> ignore)
            use stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)
            use writer = new StreamWriter(stream, Text.UTF8Encoding(false))
            for line in lines do
                writer.WriteLine(renderLine line)
            writer.Flush()
            stream.Flush(true)
            Ok ()
        with ex ->
            fail "sink.writeFailed" (sprintf "journal append failed: %s" ex.Message)

    /// Load the whole journal (the metadata plane is small — whole-file is
    /// honest). A torn TRAILING line (no final newline) is tolerated as
    /// the named at-least-once window; an interior corrupt line refuses.
    let load (path: string) : Result<JournalLine list> =
        if not (File.Exists path) then Ok []
        else
            try
                let text = File.ReadAllText path
                let endsWithNewline = text.EndsWith "\n"
                let rawLines =
                    text.Split('\n')
                    |> Array.toList
                    |> List.filter (fun l -> l.Trim() <> "")
                let parsedAll =
                    rawLines |> List.map parseLine
                let lastIndex = List.length parsedAll - 1
                parsedAll
                |> List.mapi (fun i r -> i, r)
                |> List.fold (fun acc (i, r) ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | Ok xs, Ok line -> Ok (line :: xs)
                    | Ok xs, Error _ when i = lastIndex && not endsWithNewline ->
                        // The torn trailing line: tolerated, named by absence.
                        Ok xs
                    | Ok _, Error e -> Error e) (Ok [])
                |> Result.map List.rev
            with ex ->
                fail "sink.journal.unreadable" (sprintf "journal read failed: %s" ex.Message)

    /// align-II.10 (E3) — the journal read as a NAMED reading: the lines,
    /// or the located cause of an unreadable ledger. The two claim-assembly
    /// consumers thread it so an unreadable journal degrades NAMED (the
    /// estate face says so; the sink-served model read logs it) instead of
    /// silently assembling claims over an empty list.
    [<RequireQualifiedAccess>]
    type JournalReading =
        | Read of lines: JournalLine list
        | Unreadable of cause: string

    [<RequireQualifiedAccess>]
    module JournalReading =

        /// Classify a load result. The cause is the primary error's
        /// located message.
        let ofLoad (r: Result<JournalLine list>) : JournalReading =
            match r with
            | Ok lines -> JournalReading.Read lines
            | Error errors ->
                let cause =
                    match errors with
                    | e :: _ -> e.Message
                    | [] -> "the journal read returned no cause"
                JournalReading.Unreadable cause

        /// The lines a reading carries — empty on Unreadable (the consumer
        /// has already surfaced the degradation by name).
        let lines (r: JournalReading) : JournalLine list =
            match r with
            | JournalReading.Read lines -> lines
            | JournalReading.Unreadable _ -> []

    /// The chain admission (align-III.2): the sink journal is a `Linkage`
    /// grain, so admission runs through the shared `Ledger.admitChain`
    /// substrate — no hand-rolled fold, no tautological self-compare. The
    /// substrate verifies BOTH that sync groups strictly increase and that
    /// each group names the one before as its `PrevSyncId`; a regression
    /// refuses `sink.journal.syncRegression`, a broken predecessor link
    /// refuses `sink.journal.brokenChain`. Neither is a silent re-run.
    let admitChain (lines: JournalLine list) : Result<Verified<JournalLine> list> =
        let renderOrd (o: SyncOrdinal option) : string =
            match o with Some s -> SyncOrdinal.text s | None -> "genesis (no predecessor)"
        match Ledger.admitChain spec lines with
        | Ok verified -> Result.success verified
        | Error (ChainRefusal.OrdinalRegression (pos, ordinal, prior)) ->
            fail "sink.journal.syncRegression"
                (sprintf "journal syncId regressed at line %d: sync %s after sync %s" pos (SyncOrdinal.text ordinal) (SyncOrdinal.text prior))
        | Error (ChainRefusal.BrokenLink (pos, claimed, expected)) ->
            fail "sink.journal.brokenChain"
                (sprintf "journal chain broke at line %d: the sync names predecessor %s, but the chain reached %s" pos (renderOrd claimed) (renderOrd expected))
        | Error ChainRefusal.RecomputeRequiresSource ->
            // Unreachable — the sink journal is a Linkage grain, never a
            // recompute one — but named rather than silently dropped.
            fail "sink.journal.brokenChain"
                "the sink journal admits by predecessor linkage; a source recompute was requested against a linkage chain (internal invariant)"

    /// The FTC at the acquisition grain: fold ⊕ over the verified chain
    /// from the genesis (empty) snapshot — `Ledger.replay`, not a bespoke
    /// fold. With the witness writing canonical snapshots, `replay` of a
    /// journal reproduces the latest witnessed snapshot exactly (T19).
    let replay (verified: Verified<JournalLine> list) : MetadataSnapshotRunner.MetadataSnapshot =
        Ledger.replay spec verified

    /// The latest sync ordinal recorded, when any.
    let lastSyncId (lines: JournalLine list) : SyncOrdinal option =
        lines |> List.map (fun l -> l.SyncId) |> function [] -> None | ids -> Some (List.max ids)

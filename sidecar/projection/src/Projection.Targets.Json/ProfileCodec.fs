namespace Projection.Targets.Json

open System.Globalization
open System.Text.Json
open Projection.Core

/// Round-trippable `Profile ↔ JSON` codec — the durable artifact between
/// capture and synthesis (THE_SYNTHETIC_DATA_DESIGN §2.2 / §10.2). The
/// capture step (`projection profile <env> --out`) writes it; the synthetic
/// flow (`from: synthetic, profile: file:<path>`) reads it; an operator may
/// inspect / tweak it by hand in between. Sibling to `CatalogCodec`; the
/// `Catalog × Profile` evidence pair both persist by the same realize/ingest
/// idiom.
///
/// **Total** — every field and DU variant reachable from `Profile` is encoded
/// (the inventory is the totality contract; a missed axis would be a silent
/// drop, forbidden). **Deterministic (T1)** — `JsonOptions.indented`, fixed
/// write order, decimals via InvariantCulture, SsKeys serialized via
/// `SsKey.serialize`, `DateTimeOffset` via round-trip `"O"`. **Re-validating
/// (A39)** — decode rebuilds each leaf through its smart constructor
/// (`ProbeStatus.create`, `ColumnProfile.create`, `CategoricalDistribution
/// .create`, `NumericDistribution.create`, …) so a decoded profile re-proves
/// its empirical invariants and surfaces a `Result<Profile>`.
///
/// **The universal law** (`∀ p. deserialize (serialize p) = Ok p`) is the
/// codec discipline's keystone, tested over a constructed-valid generator.
[<RequireQualifiedAccess>]
module ProfileCodec =

    [<Literal>]
    let version : int = 1

    let private inv (d: decimal) : string = JsonCodecKernel.inv d

    // ======================================================================
    // ENCODE
    // ======================================================================

    // Structural write helpers — the shared kernel primitives under this
    // codec's local names (mirrors the `inv` alias above; the write semantics
    // live once in `JsonCodecKernel`).
    let private wField (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (v: 'a) : unit =
        JsonCodecKernel.wField jw name write v

    let private wOpt (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (v: 'a option) : unit =
        JsonCodecKernel.wOpt jw name write v

    let private wList (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (xs: 'a list) : unit =
        JsonCodecKernel.wList jw name write xs

    let private wSsKeyVal (jw: Utf8JsonWriter) (k: SsKey) : unit = jw.WriteStringValue (SsKey.serialize k)

    let private wProbeOutcome (jw: Utf8JsonWriter) (o: ProbeOutcome) : unit =
        jw.WriteStringValue (
            match o with
            | Succeeded         -> "Succeeded"
            | FallbackTimeout   -> "FallbackTimeout"
            | Cancelled         -> "Cancelled"
            | TrustedConstraint -> "TrustedConstraint"
            | AmbiguousMapping  -> "AmbiguousMapping")

    let private wProbeStatus (jw: Utf8JsonWriter) (p: ProbeStatus) : unit =
        jw.WriteStartObject()
        jw.WriteString("capturedAtUtc", p.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture))
        jw.WriteNumber("sampleSize", p.SampleSize)
        wField jw "outcome" wProbeOutcome p.Outcome
        jw.WriteEndObject()

    let private wFrequency (jw: Utf8JsonWriter) ((value, count): string * int64) : unit =
        jw.WriteStartObject()
        jw.WriteString("value", value)
        jw.WriteNumber("count", count)
        jw.WriteEndObject()

    let private wColumnProfile (jw: Utf8JsonWriter) (c: ColumnProfile) : unit =
        jw.WriteStartObject()
        wField jw "attributeKey" wSsKeyVal c.AttributeKey
        jw.WriteNumber("rowCount", c.RowCount)
        jw.WriteNumber("nullCount", c.NullCount)
        // The max-observed-length axis is optional evidence — written only when
        // the profiler probed it, so a profile without it round-trips to `None`
        // (the no-evidence identity) rather than a spurious `Some 0`.
        match c.MaxObservedLength with
        | Some len -> jw.WriteNumber("maxObservedLength", len)
        | None     -> ()
        wField jw "probeStatus" wProbeStatus c.NullCountProbeStatus
        jw.WriteEndObject()

    let private wUniqueCandidate (jw: Utf8JsonWriter) (u: UniqueCandidateProfile) : unit =
        jw.WriteStartObject()
        wField jw "attributeKey" wSsKeyVal u.AttributeKey
        jw.WriteBoolean("hasDuplicate", u.HasDuplicate)
        wField jw "probeStatus" wProbeStatus u.ProbeStatus
        jw.WriteEndObject()

    let private wCompositeUnique (jw: Utf8JsonWriter) (u: CompositeUniqueCandidateProfile) : unit =
        jw.WriteStartObject()
        wField jw "kindKey" wSsKeyVal u.KindKey
        wList jw "attributeKeys" wSsKeyVal u.AttributeKeys
        jw.WriteBoolean("hasDuplicate", u.HasDuplicate)
        wField jw "probeStatus" wProbeStatus u.ProbeStatus
        jw.WriteEndObject()

    let private wForeignKeyReality (jw: Utf8JsonWriter) (r: ForeignKeyReality) : unit =
        jw.WriteStartObject()
        wField jw "referenceKey" wSsKeyVal r.ReferenceKey
        jw.WriteBoolean("hasOrphan", r.HasOrphan)
        jw.WriteNumber("orphanCount", r.OrphanCount)
        jw.WriteBoolean("isNoCheck", r.IsNoCheck)
        wField jw "probeStatus" wProbeStatus r.ProbeStatus
        jw.WriteEndObject()

    let private wMoments (jw: Utf8JsonWriter) (m: StatisticalMoments) : unit =
        jw.WriteStartObject()
        jw.WriteString("mean", inv m.Mean)
        jw.WriteString("stdDev", inv m.StdDev)
        jw.WriteEndObject()

    let private wNumeric (jw: Utf8JsonWriter) (n: NumericDistribution) : unit =
        jw.WriteStartObject()
        wField jw "attributeKey" wSsKeyVal n.AttributeKey
        jw.WriteString("min", inv n.Min)
        jw.WriteString("p25", inv n.P25)
        jw.WriteString("p50", inv n.P50)
        jw.WriteString("p75", inv n.P75)
        jw.WriteString("p95", inv n.P95)
        jw.WriteString("p99", inv n.P99)
        jw.WriteString("max", inv n.Max)
        jw.WriteNumber("sampleSize", n.SampleSize)
        wOpt jw "moments" wMoments n.Moments
        wField jw "probeStatus" wProbeStatus n.ProbeStatus
        jw.WriteEndObject()

    let private wCategorical (jw: Utf8JsonWriter) (c: CategoricalDistribution) : unit =
        jw.WriteStartObject()
        wField jw "attributeKey" wSsKeyVal c.AttributeKey
        wList jw "frequencies" wFrequency c.Frequencies
        jw.WriteNumber("distinctCount", c.DistinctCount)
        jw.WriteBoolean("isTruncated", c.IsTruncated)
        wField jw "probeStatus" wProbeStatus c.ProbeStatus
        jw.WriteEndObject()

    let private wDistribution (jw: Utf8JsonWriter) (d: AttributeDistribution) : unit =
        jw.WriteStartObject()
        match d with
        | AttributeDistribution.Categorical c ->
            jw.WriteString("kind", "categorical")
            wField jw "categorical" wCategorical c
        | AttributeDistribution.Numeric n ->
            jw.WriteString("kind", "numeric")
            wField jw "numeric" wNumeric n
        jw.WriteEndObject()

    let private wAttributeReality (jw: Utf8JsonWriter) (a: AttributeReality) : unit =
        jw.WriteStartObject()
        wField jw "attributeKey" wSsKeyVal a.AttributeKey
        jw.WriteBoolean("isNullableInDatabase", a.IsNullableInDatabase)
        jw.WriteBoolean("hasNulls", a.HasNulls)
        jw.WriteBoolean("hasDuplicates", a.HasDuplicates)
        jw.WriteBoolean("hasOrphans", a.HasOrphans)
        jw.WriteBoolean("isPresentButInactive", a.IsPresentButInactive)
        jw.WriteEndObject()

    let private wFkCardinality (jw: Utf8JsonWriter) (c: ForeignKeyCardinality) : unit =
        jw.WriteStartObject()
        wField jw "referenceKey" wSsKeyVal c.ReferenceKey
        wField jw "childCountDistribution" wNumeric c.ChildCountDistribution
        jw.WriteEndObject()

    let private wFkSelectivity (jw: Utf8JsonWriter) (s: ForeignKeySelectivity) : unit =
        jw.WriteStartObject()
        wField jw "referenceKey" wSsKeyVal s.ReferenceKey
        wList jw "frequencies" wFrequency s.Frequencies
        jw.WriteNumber("distinctCount", s.DistinctCount)
        jw.WriteBoolean("isTruncated", s.IsTruncated)
        wField jw "probeStatus" wProbeStatus s.ProbeStatus
        jw.WriteEndObject()

    let private wJoint (jw: Utf8JsonWriter) (j: JointDistribution) : unit =
        jw.WriteStartObject()
        wField jw "kindKey" wSsKeyVal j.KindKey
        wList jw "attributeKeys" wSsKeyVal j.AttributeKeys
        wList jw "frequencies" wFrequency j.Frequencies
        jw.WriteNumber("distinctCount", j.DistinctCount)
        jw.WriteBoolean("isTruncated", j.IsTruncated)
        wField jw "probeStatus" wProbeStatus j.ProbeStatus
        jw.WriteEndObject()

    let private wCdc (jw: Utf8JsonWriter) (c: CdcAwareness) : unit =
        jw.WriteStartObject()
        // Sorted for T1 determinism (Set/Map enumeration order is stable but
        // serialize by sorted serialized-key regardless). PL-6 (S32): the
        // serialized form is computed ONCE per element and is both the sort
        // key and the written value — `SsKey.serialize` is injective and
        // `List.sort` on the strings is the same ordinal comparison
        // `List.sortBy SsKey.serialize` used, so the emitted order (and
        // bytes) are unchanged.
        let serializedEnabled =
            c.CdcEnabled |> Set.toList |> List.map SsKey.serialize |> List.sort
        wList jw "enabled" (fun jw (s: string) -> jw.WriteStringValue s) serializedEnabled
        jw.WritePropertyName "instances"
        jw.WriteStartArray()
        let serializedInstances =
            c.CdcInstance
            |> Map.toList
            |> List.map (fun (k, instance) -> SsKey.serialize k, instance)
            |> List.sortBy fst
        for (serializedKey, instance) in serializedInstances do
            jw.WriteStartObject()
            jw.WriteString("key", serializedKey)
            jw.WriteString("instance", instance)
            jw.WriteEndObject()
        jw.WriteEndArray()
        jw.WriteEndObject()

    let private wUser (idValue: 'id -> int) (jw: Utf8JsonWriter) (u: UserAttributes<'id>) : unit =
        jw.WriteStartObject()
        jw.WriteNumber("id", idValue u.Id)
        wField jw "ssKey" wSsKeyVal u.SsKey
        wOpt jw "email" (fun jw (Email e) -> jw.WriteStringValue e) u.Email
        jw.WriteEndObject()

    let private wUserPopulation (idValue: 'id -> int) (jw: Utf8JsonWriter) (p: UserPopulation<'id>) : unit =
        jw.WriteStartObject()
        wList jw "users" (wUser idValue) p.Users
        jw.WriteEndObject()

    let private wProfile (jw: Utf8JsonWriter) (p: Profile) : unit =
        jw.WriteStartObject()
        jw.WriteNumber("version", version)
        wList jw "columns" wColumnProfile p.Columns
        wList jw "uniqueCandidates" wUniqueCandidate p.UniqueCandidates
        wList jw "compositeUniqueCandidates" wCompositeUnique p.CompositeUniqueCandidates
        wList jw "foreignKeys" wForeignKeyReality p.ForeignKeys
        wList jw "distributions" wDistribution p.Distributions
        wList jw "attributeRealities" wAttributeReality p.AttributeRealities
        wList jw "foreignKeyCardinalities" wFkCardinality p.ForeignKeyCardinalities
        wList jw "foreignKeySelectivities" wFkSelectivity p.ForeignKeySelectivities
        wList jw "jointDistributions" wJoint p.JointDistributions
        wField jw "cdcAwareness" wCdc p.CdcAwareness
        wField jw "sourceUsers" (wUserPopulation SourceUserId.value) p.SourceUsers
        wField jw "targetUsers" (wUserPopulation TargetUserId.value) p.TargetUsers
        jw.WriteEndObject()

    /// `Profile → JSON`. Deterministic (T1).
    let serialize (profile: Profile) : string =
        JsonWriting.writeToString (fun jw -> wProfile jw profile)

    // ======================================================================
    // DECODE — leaves rebuilt through smart constructors (A39).
    // ======================================================================

    // Decode kernel — thin delegations to the shared `JsonCodecKernel` (prefix
    // `"profileCodec"`), so the emitted error codes stay byte-identical.
    let private fail (code: string) (msg: string) : Result<'a> = JsonCodecKernel.fail code msg
    let private prop (el: JsonElement) (name: string) : Result<JsonElement> = JsonCodecKernel.prop "profileCodec" el name
    let private asString (el: JsonElement) : Result<string> = JsonCodecKernel.asString "profileCodec" el
    let private asBool (el: JsonElement) : Result<bool> = JsonCodecKernel.asBool "profileCodec" el
    let private asInt64 (el: JsonElement) : Result<int64> = JsonCodecKernel.asInt64 "profileCodec" el
    let private asInt (el: JsonElement) : Result<int> = JsonCodecKernel.asInt "profileCodec" el
    let private asDecimal (el: JsonElement) : Result<decimal> = JsonCodecKernel.asDecimal "profileCodec" el
    let private field (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> = JsonCodecKernel.field "profileCodec" el name read
    let private optField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a option> = JsonCodecKernel.optField el name read
    let private listField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a list> = JsonCodecKernel.listField "profileCodec" el name read

    let private readSsKey (el: JsonElement) : Result<SsKey> =
        asString el |> Result.bind SsKey.deserialize

    let private readDateTimeOffset (el: JsonElement) : Result<System.DateTimeOffset> =
        asString el
        |> Result.bind (fun s ->
            match System.DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) with
            | true, d -> Ok d
            | _ -> fail "profileCodec.expectedDateTimeOffset" (sprintf "not a round-trip DateTimeOffset: '%s'" s))

    let private readProbeOutcome (el: JsonElement) : Result<ProbeOutcome> =
        asString el
        |> Result.bind (function
            | "Succeeded"         -> Ok Succeeded
            | "FallbackTimeout"   -> Ok FallbackTimeout
            | "Cancelled"         -> Ok Cancelled
            | "TrustedConstraint" -> Ok TrustedConstraint
            | "AmbiguousMapping"  -> Ok AmbiguousMapping
            | o -> fail "profileCodec.probeOutcome.unknown" (sprintf "unknown ProbeOutcome '%s'" o))

    let private readProbeStatus (el: JsonElement) : Result<ProbeStatus> =
        field el "capturedAtUtc" readDateTimeOffset |> Result.bind (fun at ->
        field el "sampleSize" asInt64 |> Result.bind (fun ss ->
        field el "outcome" readProbeOutcome |> Result.bind (fun oc ->
        ProbeStatus.create at ss oc)))

    let private readFrequency (el: JsonElement) : Result<string * int64> =
        field el "value" asString |> Result.bind (fun v ->
        field el "count" asInt64 |> Result.map (fun c -> (v, c)))

    let private readColumnProfile (el: JsonElement) : Result<ColumnProfile> =
        field el "attributeKey" readSsKey |> Result.bind (fun key ->
        field el "rowCount" asInt64 |> Result.bind (fun rc ->
        field el "nullCount" asInt64 |> Result.bind (fun nc ->
        optField el "maxObservedLength" asInt |> Result.bind (fun maxLen ->
        field el "probeStatus" readProbeStatus |> Result.bind (fun ps ->
        ColumnProfile.create key rc nc ps
        |> Result.map (fun cp ->
            match maxLen with
            | Some len -> ColumnProfile.withMaxObservedLength len cp
            | None     -> cp))))))

    let private readUniqueCandidate (el: JsonElement) : Result<UniqueCandidateProfile> =
        field el "attributeKey" readSsKey |> Result.bind (fun key ->
        field el "hasDuplicate" asBool |> Result.bind (fun dup ->
        field el "probeStatus" readProbeStatus |> Result.map (fun ps ->
        { UniqueCandidateProfile.create key with HasDuplicate = dup; ProbeStatus = ps })))

    let private readCompositeUnique (el: JsonElement) : Result<CompositeUniqueCandidateProfile> =
        field el "kindKey" readSsKey |> Result.bind (fun key ->
        listField el "attributeKeys" readSsKey |> Result.bind (fun keys ->
        field el "hasDuplicate" asBool |> Result.bind (fun dup ->
        field el "probeStatus" readProbeStatus |> Result.map (fun ps ->
        { CompositeUniqueCandidateProfile.create key keys with HasDuplicate = dup; ProbeStatus = ps }))))

    let private readForeignKeyReality (el: JsonElement) : Result<ForeignKeyReality> =
        field el "referenceKey" readSsKey |> Result.bind (fun key ->
        field el "hasOrphan" asBool |> Result.bind (fun orphan ->
        field el "orphanCount" asInt64 |> Result.bind (fun oc ->
        field el "isNoCheck" asBool |> Result.bind (fun nochk ->
        field el "probeStatus" readProbeStatus |> Result.map (fun ps ->
        { ForeignKeyReality.create key with
            HasOrphan = orphan; OrphanCount = oc; IsNoCheck = nochk; ProbeStatus = ps })))))

    let private readMoments (el: JsonElement) : Result<StatisticalMoments> =
        field el "mean" asDecimal |> Result.bind (fun m ->
        field el "stdDev" asDecimal |> Result.bind (fun sd ->
        StatisticalMoments.create m sd))

    let private readNumeric (el: JsonElement) : Result<NumericDistribution> =
        field el "attributeKey" readSsKey |> Result.bind (fun key ->
        field el "min" asDecimal |> Result.bind (fun mn ->
        field el "p25" asDecimal |> Result.bind (fun p25 ->
        field el "p50" asDecimal |> Result.bind (fun p50 ->
        field el "p75" asDecimal |> Result.bind (fun p75 ->
        field el "p95" asDecimal |> Result.bind (fun p95 ->
        field el "p99" asDecimal |> Result.bind (fun p99 ->
        field el "max" asDecimal |> Result.bind (fun mx ->
        field el "sampleSize" asInt64 |> Result.bind (fun ss ->
        optField el "moments" readMoments |> Result.bind (fun moments ->
        field el "probeStatus" readProbeStatus |> Result.bind (fun ps ->
        NumericDistribution.create key mn p25 p50 p75 p95 p99 mx ss ps
        |> Result.bind (fun nd ->
            // NM-13: re-prove the moment-range invariant (Min ≤ Mean ≤ Max)
            // through the sanctioned `withMoments`, never a raw record-`with`.
            match moments with
            | Some m -> NumericDistribution.withMoments m nd
            | None   -> Result.success nd))))))))))))

    let private readCategorical (el: JsonElement) : Result<CategoricalDistribution> =
        field el "attributeKey" readSsKey |> Result.bind (fun key ->
        listField el "frequencies" readFrequency |> Result.bind (fun freqs ->
        field el "distinctCount" asInt64 |> Result.bind (fun dc ->
        field el "isTruncated" asBool |> Result.bind (fun trunc ->
        field el "probeStatus" readProbeStatus |> Result.bind (fun ps ->
        CategoricalDistribution.create key freqs dc trunc ps)))))

    let private readDistribution (el: JsonElement) : Result<AttributeDistribution> =
        field el "kind" asString |> Result.bind (function
            | "categorical" -> field el "categorical" readCategorical |> Result.map AttributeDistribution.Categorical
            | "numeric"     -> field el "numeric" readNumeric |> Result.map AttributeDistribution.Numeric
            | o -> fail "profileCodec.distribution.unknown" (sprintf "unknown distribution kind '%s'" o))

    let private readAttributeReality (el: JsonElement) : Result<AttributeReality> =
        field el "attributeKey" readSsKey |> Result.bind (fun key ->
        field el "isNullableInDatabase" asBool |> Result.bind (fun nul ->
        field el "hasNulls" asBool |> Result.bind (fun hn ->
        field el "hasDuplicates" asBool |> Result.bind (fun hd ->
        field el "hasOrphans" asBool |> Result.bind (fun ho ->
        field el "isPresentButInactive" asBool |> Result.map (fun inact ->
        { AttributeReality.create key with
            IsNullableInDatabase = nul; HasNulls = hn; HasDuplicates = hd
            HasOrphans = ho; IsPresentButInactive = inact }))))))

    let private readFkCardinality (el: JsonElement) : Result<ForeignKeyCardinality> =
        field el "referenceKey" readSsKey |> Result.bind (fun key ->
        field el "childCountDistribution" readNumeric |> Result.map (fun nd ->
        ForeignKeyCardinality.create key nd))

    let private readFkSelectivity (el: JsonElement) : Result<ForeignKeySelectivity> =
        field el "referenceKey" readSsKey |> Result.bind (fun key ->
        listField el "frequencies" readFrequency |> Result.bind (fun freqs ->
        field el "distinctCount" asInt64 |> Result.bind (fun dc ->
        field el "isTruncated" asBool |> Result.bind (fun trunc ->
        field el "probeStatus" readProbeStatus |> Result.bind (fun ps ->
        ForeignKeySelectivity.create key freqs dc trunc ps)))))

    let private readJoint (el: JsonElement) : Result<JointDistribution> =
        field el "kindKey" readSsKey |> Result.bind (fun key ->
        listField el "attributeKeys" readSsKey |> Result.bind (fun keys ->
        listField el "frequencies" readFrequency |> Result.bind (fun freqs ->
        field el "distinctCount" asInt64 |> Result.bind (fun dc ->
        field el "isTruncated" asBool |> Result.bind (fun trunc ->
        field el "probeStatus" readProbeStatus |> Result.bind (fun ps ->
        JointDistribution.create key keys freqs dc trunc ps))))))

    let private readCdcInstance (el: JsonElement) : Result<SsKey * string> =
        field el "key" readSsKey |> Result.bind (fun k ->
        field el "instance" asString |> Result.map (fun i -> (k, i)))

    let private readCdc (el: JsonElement) : Result<CdcAwareness> =
        listField el "enabled" readSsKey |> Result.bind (fun enabled ->
        listField el "instances" readCdcInstance |> Result.map (fun instances ->
        CdcAwareness.create (Set.ofList enabled) (Map.ofList instances)))

    let private readEmail (el: JsonElement) : Result<Email> =
        asString el |> Result.bind Email.create

    let private readUser (ofInt: int -> 'id) (el: JsonElement) : Result<UserAttributes<'id>> =
        field el "id" asInt |> Result.bind (fun id ->
        field el "ssKey" readSsKey |> Result.bind (fun key ->
        optField el "email" readEmail |> Result.map (fun email ->
        UserAttributes.create (ofInt id) key email)))

    let private readUserPopulation (ofInt: int -> 'id) (el: JsonElement) : Result<UserPopulation<'id>> =
        listField el "users" (readUser ofInt) |> Result.map UserPopulation.create

    let private readProfile (el: JsonElement) : Result<Profile> =
        listField el "columns" readColumnProfile |> Result.bind (fun columns ->
        listField el "uniqueCandidates" readUniqueCandidate |> Result.bind (fun uniques ->
        listField el "compositeUniqueCandidates" readCompositeUnique |> Result.bind (fun composites ->
        listField el "foreignKeys" readForeignKeyReality |> Result.bind (fun fks ->
        listField el "distributions" readDistribution |> Result.bind (fun dists ->
        listField el "attributeRealities" readAttributeReality |> Result.bind (fun realities ->
        listField el "foreignKeyCardinalities" readFkCardinality |> Result.bind (fun cards ->
        listField el "foreignKeySelectivities" readFkSelectivity |> Result.bind (fun sels ->
        listField el "jointDistributions" readJoint |> Result.bind (fun joints ->
        field el "cdcAwareness" readCdc |> Result.bind (fun cdc ->
        field el "sourceUsers" (readUserPopulation SourceUserId.ofInt) |> Result.bind (fun srcUsers ->
        field el "targetUsers" (readUserPopulation TargetUserId.ofInt) |> Result.map (fun tgtUsers ->
        { Columns                   = columns
          UniqueCandidates          = uniques
          CompositeUniqueCandidates = composites
          ForeignKeys               = fks
          Distributions             = dists
          AttributeRealities        = realities
          ForeignKeyCardinalities   = cards
          ForeignKeySelectivities   = sels
          JointDistributions        = joints
          CdcAwareness              = cdc
          SourceUsers               = srcUsers
          TargetUsers               = tgtUsers }))))))))))))

    /// `JSON → Profile`. Total parser; re-proves every leaf invariant.
    let deserialize (json: string) : Result<Profile> =
        let parsed = try Ok (JsonDocument.Parse json) with ex -> fail "profileCodec.parse" ex.Message
        parsed |> Result.bind (fun doc ->
            use doc = doc
            readProfile doc.RootElement)

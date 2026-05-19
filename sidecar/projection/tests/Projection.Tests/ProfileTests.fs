module Projection.Tests.ProfileTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.ProfileFixtures

// ---------------------------------------------------------------------------
// ProbeStatus construction
// ---------------------------------------------------------------------------

[<Fact>]
let ``ProbeStatus.create rejects negative sample size`` () =
    let r = ProbeStatus.create DateTimeOffset.UtcNow -1L Succeeded
    Assert.True(Result.isFailure r)

[<Fact>]
let ``ProbeStatus.create accepts zero sample size`` () =
    let r = ProbeStatus.create DateTimeOffset.UtcNow 0L Succeeded
    Assert.True(Result.isSuccess r)

[<Fact>]
let ``ProbeStatus.isReliable distinguishes Succeeded from other outcomes`` () =
    let mk outcome =
        ProbeStatus.create DateTimeOffset.UtcNow 1L outcome |> Result.value
    Assert.True(ProbeStatus.isReliable (mk Succeeded))
    Assert.False(ProbeStatus.isReliable (mk FallbackTimeout))
    Assert.False(ProbeStatus.isReliable (mk Cancelled))
    Assert.False(ProbeStatus.isReliable (mk TrustedConstraint))
    Assert.False(ProbeStatus.isReliable (mk AmbiguousMapping))

// ---------------------------------------------------------------------------
// ColumnProfile derived measures
// ---------------------------------------------------------------------------

[<Fact>]
let ``ColumnProfile.nullPercentage handles zero rows`` () =
    let cp = { customerNameProfile with RowCount = 0L; NullCount = 0L }
    Assert.Equal(None, ColumnProfile.nullPercentage cp)

[<Fact>]
let ``ColumnProfile.nullPercentage computes the observed ratio`` () =
    Assert.Equal(Some 0.02m, ColumnProfile.nullPercentage customerNameProfile)
    Assert.Equal(Some 0m,    ColumnProfile.nullPercentage customerIdProfile)

[<Fact>]
let ``ColumnProfile.isAllNull and isZeroNull are mutually consistent`` () =
    let allNull   = { customerNameProfile with NullCount = customerNameProfile.RowCount }
    let zeroNull  = { customerNameProfile with NullCount = 0L }
    Assert.True (ColumnProfile.isAllNull allNull)
    Assert.False(ColumnProfile.isAllNull zeroNull)
    Assert.True (ColumnProfile.isZeroNull zeroNull)
    Assert.False(ColumnProfile.isZeroNull allNull)

[<Fact>]
let ``ColumnProfile.isAllNull is false on an empty table (row count zero)`` () =
    let empty = { customerNameProfile with RowCount = 0L; NullCount = 0L }
    Assert.False(ColumnProfile.isAllNull empty)
    Assert.True (ColumnProfile.isZeroNull empty)

// ---------------------------------------------------------------------------
// Profile.empty is structurally complete (A34: independent of Catalog and
// Policy). The fact that this constructs without referencing any Catalog
// or Policy type is the test — any Profile field that secretly carried a
// back-reference would break the type-only construction below.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A34: Profile.empty is empty across all four collections`` () =
    let p = Profile.empty
    Assert.Empty(p.Columns)
    Assert.Empty(p.UniqueCandidates)
    Assert.Empty(p.CompositeUniqueCandidates)
    Assert.Empty(p.ForeignKeys)
    Assert.True(Profile.isEmpty p)

[<Fact>]
let ``A34: Profile.empty satisfies isEmpty`` () =
    Assert.True(Profile.isEmpty Profile.empty)

[<Fact>]
let ``Profile.isEmpty is false for any populated profile`` () =
    Assert.False(Profile.isEmpty sampleProfile)
    let onlyColumns = { Profile.empty with Columns = [ customerIdProfile ] }
    let onlyUnique  = { Profile.empty with UniqueCandidates = [ countryCodeUniqueCandidate ] }
    let onlyFk      = { Profile.empty with ForeignKeys = [ orderCustomerFkReality ] }
    Assert.False(Profile.isEmpty onlyColumns)
    Assert.False(Profile.isEmpty onlyUnique)
    Assert.False(Profile.isEmpty onlyFk)

// ---------------------------------------------------------------------------
// Identity-keyed lookups (A4): tryFindColumn / tryFindForeignKey by SsKey.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4: Profile.tryFindColumn locates a column by AttributeKey`` () =
    let found = Profile.tryFindColumn customerNameKey sampleProfile
    Assert.True(Option.isSome found)
    Assert.Equal(customerNameKey, (Option.get found).AttributeKey)

[<Fact>]
let ``A4: Profile.tryFindColumn returns None for an unknown key`` () =
    let unknown = attrKey ["Unknown"]
    Assert.Equal(None, Profile.tryFindColumn unknown sampleProfile)

[<Fact>]
let ``A4: Profile.tryFindForeignKey locates an FK by ReferenceKey`` () =
    let found = Profile.tryFindForeignKey orderRefToCustomer sampleProfile
    Assert.True(Option.isSome found)
    Assert.Equal(orderRefToCustomer, (Option.get found).ReferenceKey)

[<Fact>]
let ``A4: Profile.tryFindForeignKey returns None for an unknown key`` () =
    let unknown = refKey ["Unknown"]
    Assert.Equal(None, Profile.tryFindForeignKey unknown sampleProfile)

[<Fact>]
let ``A4: Profile.tryFindUnique locates a candidate by AttributeKey`` () =
    let found = Profile.tryFindUnique countryCodeKey sampleProfile
    Assert.True(Option.isSome found)

// ---------------------------------------------------------------------------
// Independence (A34): structurally, Profile and Catalog can be perturbed
// independently. Changing the Profile does not change any Catalog field;
// changing the Catalog does not retroactively affect Profile. The lookup
// is by SsKey, which is the shared identity primitive — that is allowed by
// A34 because SsKey is opaque to both aggregates.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A34: a Profile change does not require any Catalog change`` () =
    let perturbed = { sampleProfile with Columns = sampleProfile.Columns |> List.tail }
    // The catalog reference (sampleCatalog from Fixtures) is unchanged by
    // the profile mutation. The fact that this expression type-checks
    // without touching Catalog is the structural test.
    Assert.NotEqual<Profile>(sampleProfile, perturbed)
    Assert.Equal(3, (Catalog.allKinds sampleCatalog).Length)

[<Fact>]
let ``A34: a Catalog rename does not invalidate a Profile keyed by SsKey`` () =
    // Rename Customer in the catalog; the profile's column key
    // (customerNameKey) still resolves because it was keyed by SsKey, not
    // by name.
    let renamed =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.map (fun k ->
                                if k.SsKey = customerKey then
                                    { k with Name = Name.create "Renamed" |> Result.value }
                                else k) }) }
    Assert.True(Option.isSome (Catalog.tryFindKind customerKey renamed))
    Assert.True(Option.isSome (Profile.tryFindColumn customerNameKey sampleProfile))

// ---------------------------------------------------------------------------
// Fixture sanity — confirms every key used in the synthetic profile resolves
// to a real attribute or reference in the synthetic catalog.
// ---------------------------------------------------------------------------

let private allAttributeKeys : Set<SsKey> =
    sampleCatalog
    |> Catalog.allKinds
    |> List.collect (fun k -> k.Attributes |> List.map (fun a -> a.SsKey))
    |> Set.ofList

let private allReferenceKeys : Set<SsKey> =
    sampleCatalog
    |> Catalog.allKinds
    |> List.collect (fun k -> k.References |> List.map (fun r -> r.SsKey))
    |> Set.ofList

[<Fact>]
let ``fixture: every column profile's AttributeKey resolves to a catalog attribute`` () =
    for cp in sampleProfile.Columns do
        Assert.Contains(cp.AttributeKey, allAttributeKeys)

[<Fact>]
let ``fixture: every FK profile's ReferenceKey resolves to a catalog reference`` () =
    for fk in sampleProfile.ForeignKeys do
        Assert.Contains(fk.ReferenceKey, allReferenceKeys)

[<Fact>]
let ``fixture: profile covers every attribute in the synthetic catalog`` () =
    let profileKeys =
        sampleProfile.Columns
        |> List.map (fun c -> c.AttributeKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(allAttributeKeys, profileKeys)

// ---------------------------------------------------------------------------
// Property: ColumnProfile.nullPercentage is a function — same input always
// produces the same output. (Catches accidental dependence on time / random.)
// ---------------------------------------------------------------------------

[<Property>]
let ``ColumnProfile.nullPercentage is deterministic`` (rowCount: int64) (nullCount: int64) =
    // Pure function check: same input twice -> same output. Acts as a
    // canary against accidental impurity (DateTime.Now, Random, etc.)
    // creeping into ColumnProfile derivations. Input validity is not part
    // of this property; we sample the full int64 domain.
    let probe =
        { CapturedAtUtc = DateTimeOffset.UnixEpoch
          SampleSize    = 0L
          Outcome       = Succeeded }
    let cp =
        { AttributeKey         = customerIdAttrKey
          RowCount             = rowCount
          NullCount            = nullCount
          NullCountProbeStatus = probe }
    ColumnProfile.nullPercentage cp = ColumnProfile.nullPercentage cp

// ---------------------------------------------------------------------------
// Slice B.3.7 — Profile.merge multi-environment aggregation.
// FsCheck property tests verify the algebraic laws (commutative,
// associative, identity) hold for the worst-case-aggregation semantics.
// ---------------------------------------------------------------------------

let private mkProbe (sample: int64) : ProbeStatus =
    { CapturedAtUtc = DateTimeOffset.UnixEpoch
      SampleSize    = sample
      Outcome       = Succeeded }

let private profileWithColumn (key: SsKey) (rowCount: int64) (nullCount: int64) : Profile =
    { Profile.empty with
        Columns = [
            { AttributeKey         = key
              RowCount             = max 0L rowCount
              NullCount             = max 0L (min nullCount rowCount)
              NullCountProbeStatus = mkProbe (max 0L rowCount) }
        ] }

let private profileWithAttributeReality (key: SsKey) (hasNulls: bool) (hasDup: bool) : Profile =
    { Profile.empty with
        AttributeRealities = [
            { AttributeReality.create key with
                HasNulls      = hasNulls
                HasDuplicates = hasDup }
        ] }

[<Fact>]
let ``B.3.7: Profile.merge with empty profile is identity`` () =
    let p = profileWithColumn customerIdAttrKey 100L 5L
    Assert.Equal<ColumnProfile list>(p.Columns, (Profile.merge p Profile.empty).Columns)
    Assert.Equal<ColumnProfile list>(p.Columns, (Profile.merge Profile.empty p).Columns)

[<Fact>]
let ``B.3.7: Profile.merge takes worst-case MAX of RowCount + NullCount per attribute`` () =
    let a = profileWithColumn customerIdAttrKey 100L 5L
    let b = profileWithColumn customerIdAttrKey 200L 30L
    let merged = Profile.merge a b
    Assert.Equal(1, List.length merged.Columns)
    let col = merged.Columns.Head
    Assert.Equal(200L, col.RowCount)
    Assert.Equal(30L, col.NullCount)

[<Fact>]
let ``B.3.7: Profile.merge unions columns for disjoint attribute keys`` () =
    let a = profileWithColumn customerIdAttrKey 100L 5L
    let b = profileWithColumn customerNameKey 200L 0L
    let merged = Profile.merge a b
    Assert.Equal(2, List.length merged.Columns)

[<Fact>]
let ``B.3.7: Profile.merge ORs over AttributeReality booleans`` () =
    let a = profileWithAttributeReality customerIdAttrKey true false
    let b = profileWithAttributeReality customerIdAttrKey false true
    let merged = Profile.merge a b
    Assert.Equal(1, List.length merged.AttributeRealities)
    let r = merged.AttributeRealities.Head
    Assert.True(r.HasNulls)
    Assert.True(r.HasDuplicates)

[<Property(MaxTest = 50)>]
let ``B.3.7: Profile.merge is commutative on ColumnProfile axis`` (rA: int64) (nA: int64) (rB: int64) (nB: int64) =
    // Sample int64 values; nullCount clamped ≤ rowCount inside profileWithColumn.
    let a = profileWithColumn customerIdAttrKey rA nA
    let b = profileWithColumn customerIdAttrKey rB nB
    let ab = (Profile.merge a b).Columns
    let ba = (Profile.merge b a).Columns
    // List-equality on Columns; merge contributes single entry per attribute.
    ab = ba

[<Property(MaxTest = 50)>]
let ``B.3.7: Profile.merge is associative on AttributeReality axis``
    (h1A: bool) (h2A: bool) (h1B: bool) (h2B: bool) (h1C: bool) (h2C: bool) =
    let a = profileWithAttributeReality customerIdAttrKey h1A h2A
    let b = profileWithAttributeReality customerIdAttrKey h1B h2B
    let c = profileWithAttributeReality customerIdAttrKey h1C h2C
    let lhs = Profile.merge (Profile.merge a b) c
    let rhs = Profile.merge a (Profile.merge b c)
    lhs.AttributeRealities = rhs.AttributeRealities

[<Property(MaxTest = 30)>]
let ``B.3.7: Profile.merge is commutative on AttributeReality booleans``
    (h1A: bool) (h2A: bool) (h1B: bool) (h2B: bool) =
    let a = profileWithAttributeReality customerIdAttrKey h1A h2A
    let b = profileWithAttributeReality customerIdAttrKey h1B h2B
    (Profile.merge a b).AttributeRealities = (Profile.merge b a).AttributeRealities

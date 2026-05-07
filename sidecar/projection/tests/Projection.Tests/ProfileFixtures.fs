module Projection.Tests.ProfileFixtures

open System
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Synthetic profile aligned with the 3-kind catalog (Customer / Order /
// Country) defined in Fixtures.fs. The numbers are realistic-but-tiny — the
// goal is texture for downstream passes' tests, not data scale.
//
// All evidence keys (AttributeKey, ReferenceKey, etc.) are SsKeys taken
// directly from Fixtures.fs, so passes that consume Profile can join it
// against the catalog by identity without an intermediate lookup.
// ---------------------------------------------------------------------------

let private utcNow : DateTimeOffset =
    DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero)

let private mustOk r =
    match r with
    | Success v -> v
    | Failure es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp $"Profile fixture construction failed: {codes}"

let private succeededProbe (sampleSize: int64) : ProbeStatus =
    ProbeStatus.create utcNow sampleSize Succeeded |> mustOk

// ---------------------------------------------------------------------------
// Customer column profile — 100 rows total, 2 nulls in Name (a tiny null
// budget that an Aggressive nullability policy would tolerate).
// ---------------------------------------------------------------------------

let customerIdProfile : ColumnProfile = {
    AttributeKey         = customerIdAttrKey
    RowCount             = 100L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 100L
}

let customerNameProfile : ColumnProfile = {
    AttributeKey         = customerNameKey
    RowCount             = 100L
    NullCount            = 2L
    NullCountProbeStatus = succeededProbe 100L
}

let customerTenantProfile : ColumnProfile = {
    AttributeKey         = customerTenantKey
    RowCount             = 100L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 100L
}

// ---------------------------------------------------------------------------
// Order column profiles plus a clean FK reality on the Customer reference.
// ---------------------------------------------------------------------------

let orderIdProfile : ColumnProfile = {
    AttributeKey         = orderIdAttrKey
    RowCount             = 250L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 250L
}

let orderCustomerFkProfile : ColumnProfile = {
    AttributeKey         = orderCustomerFkKey
    RowCount             = 250L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 250L
}

let orderCustomerFkReality : ForeignKeyReality = {
    ReferenceKey = orderRefToCustomer
    HasOrphan    = false
    OrphanCount  = 0L
    IsNoCheck    = false
    ProbeStatus  = succeededProbe 250L
}

// ---------------------------------------------------------------------------
// Country (static) column profiles — small, perfectly populated, as static
// kinds tend to be.
// ---------------------------------------------------------------------------

let countryIdProfile : ColumnProfile = {
    AttributeKey         = countryIdAttrKey
    RowCount             = 3L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 3L
}

let countryCodeProfile : ColumnProfile = {
    AttributeKey         = countryCodeKey
    RowCount             = 3L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 3L
}

let countryLabelProfile : ColumnProfile = {
    AttributeKey         = countryLabelKey
    RowCount             = 3L
    NullCount            = 0L
    NullCountProbeStatus = succeededProbe 3L
}

// ---------------------------------------------------------------------------
// Country.Code as a unique-candidate (the static dictionary's natural key).
// ---------------------------------------------------------------------------

let countryCodeUniqueCandidate : UniqueCandidateProfile = {
    AttributeKey = countryCodeKey
    HasDuplicate = false
    ProbeStatus  = succeededProbe 3L
}

// ---------------------------------------------------------------------------
// The full sample profile, aligned with sampleCatalog.
// ---------------------------------------------------------------------------

let sampleProfile : Profile = {
    Columns = [
        customerIdProfile;     customerNameProfile;     customerTenantProfile
        orderIdProfile;        orderCustomerFkProfile
        countryIdProfile;      countryCodeProfile;      countryLabelProfile
    ]
    UniqueCandidates          = [ countryCodeUniqueCandidate ]
    CompositeUniqueCandidates = []
    ForeignKeys               = [ orderCustomerFkReality ]
    Distributions             = []
}

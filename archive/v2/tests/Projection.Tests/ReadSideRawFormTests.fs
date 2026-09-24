module Projection.Tests.ReadSideRawFormTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql

// ---------------------------------------------------------------------------
// `ReadSide.formatRawValue` on the Text lane — the `sql_variant` carriage
// contract. A variant column (semantic Text) surfaces its UNDERLYING boxed
// base-type value — int, decimal, DateTime, byte[], Guid, … — and a
// culture-sensitive `ToString()` there would make the raw plane
// machine-dependent. The formatter must canonicalize every base type through
// the same invariant forms the dedicated categories use, and pass genuine
// strings (every non-variant text storage type) through unchanged.
// ---------------------------------------------------------------------------

let private inv = System.Globalization.CultureInfo.InvariantCulture

[<Fact>]
let ``a variant cell's boxed base values land in canonical raw form`` () =
    let f (v: obj | null) = ReadSide.formatRawValue Text v
    // The identity pass-through every genuine text column takes.
    Assert.Equal ("plain", f (box "plain"))
    // The variant-surfaced base types, each in its canonical form.
    Assert.Equal ("42", f (box 42))
    Assert.Equal ("42", f (box 42L))
    Assert.Equal ("3.14", f (box 3.14M))
    Assert.Equal ((1.5).ToString("G17", inv), f (box 1.5))
    Assert.Equal (RawValueCodec.formatBoolean true, f (box true))
    let dt = System.DateTime(2026, 7, 16, 12, 30, 0)
    Assert.Equal (RawValueCodec.formatDateTime dt, f (box dt))
    let dto = System.DateTimeOffset(2026, 7, 16, 12, 30, 0, System.TimeSpan.FromHours -3.0)
    Assert.Equal (RawValueCodec.formatDateTimeOffset dto, f (box dto))
    let g = System.Guid.Parse "00000000-0000-0000-0000-000000000001"
    Assert.Equal (RawValueCodec.formatGuid g, f (box g))
    Assert.Equal ("CAFEBABE", f (box [| 0xCAuy; 0xFEuy; 0xBAuy; 0xBEuy |]))

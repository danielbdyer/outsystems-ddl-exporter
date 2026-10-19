module Projection.Tests.BulkTests

open Xunit
open Projection.Core
open Projection.Pipeline

// ---------------------------------------------------------------------------
// WP-17(a/b) (DECISIONS 2026-07-16) — the bulk parse plane dispatches on the
// raw shape: float/real G17/G9 raws with an exponent parse as the exact IEEE
// double (Decimal.Parse OVERFLOWED above ≈7.9E28 — the S1 write loss);
// offset-bearing datetime raws parse back to the exact DateTimeOffset
// (DateTime.ParseExact REFUSED them — the S2 write half).
// ---------------------------------------------------------------------------

[<Fact>]
let ``WP-17a: an E-notation Decimal raw parses as the exact IEEE double (no overflow)`` () =
    match Bulk.parseRaw Decimal (Some "1.7976931348623157E+308") with
    | :? double as d -> Assert.Equal(System.Double.MaxValue, d)
    | other -> failwithf "expected a boxed double; got %A" other

[<Fact>]
let ``WP-17a: a plain-digit Decimal raw keeps the exact decimal parse (decimal-family columns)`` () =
    match Bulk.parseRaw Decimal (Some "123.4500") with
    | :? decimal as d -> Assert.Equal(123.4500m, d)
    | other -> failwithf "expected a boxed decimal; got %A" other

[<Fact>]
let ``WP-17b: an offset-bearing DateTime raw parses as the exact DateTimeOffset (offset intact)`` () =
    match Bulk.parseRaw DateTime (Some "2026-07-16 12:30:00.0000000 -03:00") with
    | :? System.DateTimeOffset as dto ->
        Assert.Equal(System.TimeSpan.FromHours -3.0, dto.Offset)
        Assert.Equal(System.DateTime(2026, 7, 16, 12, 30, 0), dto.DateTime)
    | other -> failwithf "expected a boxed DateTimeOffset; got %A" other

[<Fact>]
let ``WP-17b: the offset-less canonical DateTime raw still parses as DateTime (unchanged)`` () =
    match Bulk.parseRaw DateTime (Some "2026-07-16 12:30:00.0000000") with
    | :? System.DateTime as dt -> Assert.Equal(System.DateTime(2026, 7, 16, 12, 30, 0), dt)
    | other -> failwithf "expected a boxed DateTime; got %A" other

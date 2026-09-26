module Projection.Tests.UuidV5Tests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// RFC 4122 §4.3 UUIDv5 — name-based UUID derivation. Verified against
// canonical published vectors and exercised via property tests for
// determinism and namespace-sensitivity.
//
// Used by chapter 3.5's `RefactorLogEmitter` to derive `OperationKey`
// per rename evidence, so two emit runs against the same `CatalogDiff`
// produce byte-identical `.refactorlog` XML (T1).
// ---------------------------------------------------------------------------

// RFC 4122 §C.2 reserved namespaces.
let private dnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8")
let private urlNamespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8")

// ---------------------------------------------------------------------------
// Worked examples — published RFC vectors and known-good values.
// `python3 -c "import uuid; print(uuid.uuid5(uuid.NAMESPACE_DNS, 'www.example.org'))"`
// → `74738ff5-5367-5958-9aee-98fffdcd1876`. Same vector confirms the
// implementation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UuidV5: DNS namespace + "www.example.org" matches RFC 4122 vector`` () =
    let actual = UuidV5.create dnsNamespace "www.example.org"
    Assert.Equal(Guid.Parse("74738ff5-5367-5958-9aee-98fffdcd1876"), actual)

[<Fact>]
let ``UuidV5: DNS namespace + "python.org" matches canonical vector`` () =
    // python3 -c "import uuid; print(uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org'))"
    let actual = UuidV5.create dnsNamespace "python.org"
    Assert.Equal(Guid.Parse("886313e1-3b8a-5372-9b90-0c9aee199e5d"), actual)

[<Fact>]
let ``UuidV5: URL namespace + "https://example.com" derives a stable v5 Guid`` () =
    // Cross-checked against `python3 -c "import uuid; print(
    // uuid.uuid5(uuid.NAMESPACE_URL, 'https://example.com'))"`. Pinning
    // the actual value as a regression guard — this Guid is a load-
    // bearing chapter-3.5 invariant: any change here breaks T1
    // byte-determinism on the rendered .refactorlog XML.
    let actual = UuidV5.create urlNamespace "https://example.com"
    Assert.Equal(Guid.Parse("4fd35a71-71ef-5a55-a9d9-aa75c889a6d0"), actual)

// ---------------------------------------------------------------------------
// Version + variant bits — RFC 4122 §4.3 places `0101` in the high
// nibble of byte[6] and `10` in the high two bits of byte[8] of the
// big-endian-ordered UUID. .NET's `Guid.ToString "D"` puts byte[6]
// at index 14-15 of the dashed form (`xxxxxxxx-xxxx-Mxxx-Nxxx-xxxxxxxxxxxx`)
// where M is the version digit (must be `5`) and N is the variant
// digit (must start with `8`/`9`/`a`/`b`).
// ---------------------------------------------------------------------------

[<Fact>]
let ``UuidV5: version digit is 5`` () =
    let guid = UuidV5.create dnsNamespace "anything"
    let dashedForm = guid.ToString("D")
    // Position 14 in the dashed form (`xxxxxxxx-xxxx-5xxx-...`)
    // is the version digit.
    Assert.Equal('5', dashedForm.[14])

[<Fact>]
let ``UuidV5: variant high nibble is 8 9 a or b`` () =
    let guid = UuidV5.create dnsNamespace "anything"
    let dashedForm = guid.ToString("D")
    // Position 19 is the variant digit.
    let v = dashedForm.[19]
    Assert.Contains(v, "89ab")

// ---------------------------------------------------------------------------
// Determinism — same inputs produce same Guid.
// ---------------------------------------------------------------------------

// FsCheck's runtime generators can produce null `string` even though
// F# 9's `Nullable=enable` declares `string` non-nullable; FsCheck
// doesn't see the F# type-system signal. `NonNull<string | null>` filters
// to the type's non-null inhabitants, matching the F#/Core contract
// (Core treats null at boundaries; UuidV5 only sees non-null inputs).

[<Property(MaxTest = 50)>]
let ``UuidV5 is deterministic across repeat invocations`` (name: NonNull<string | null>) =
    let s : string = nonNull name.Get
    let a = UuidV5.create dnsNamespace s
    let b = UuidV5.create dnsNamespace s
    a = b

[<Property(MaxTest = 50)>]
let ``UuidV5 is namespace-sensitive`` (name: NonNull<string | null>) =
    let s : string = nonNull name.Get
    let viaDns = UuidV5.create dnsNamespace s
    let viaUrl = UuidV5.create urlNamespace s
    viaDns <> viaUrl

[<Property(MaxTest = 50)>]
let ``UuidV5 is name-sensitive`` (a: NonNull<string | null>) (b: NonNull<string | null>) =
    let sa : string = nonNull a.Get
    let sb : string = nonNull b.Get
    if sa = sb then true
    else UuidV5.create dnsNamespace sa <> UuidV5.create dnsNamespace sb

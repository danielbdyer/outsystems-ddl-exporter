namespace Projection.Core

// LINT-ALLOW-FILE-MUTATION: RFC 4122 §4.3 byte-level hash → bit-set → endian-roundtrip on a fresh
//   local Array (the input Guid's ToByteArray() returns a copy). Mutation
//   observation-free outside the function.

open System
open System.Security.Cryptography
open System.Text

/// RFC 4122 §4.3 name-based UUID (version 5) derivation. Produces a
/// deterministic `Guid` from `(namespace, name)` via SHA-1. Used by
/// chapter 3.5's `RefactorLogEmitter` to derive `OperationKey` per
/// rename evidence — the SSDT `<Operation>` element's `Key`
/// attribute that DacFx's incremental deploy planner reads to
/// detect rename refactors. Two emit runs against the same
/// `CatalogDiff` produce the same `OperationKey`; T1 byte-
/// determinism on the rendered `.refactorlog` rests on this
/// derivation.
///
/// .NET 9 ships `Guid.CreateVersion7` (time-based) but not a
/// `CreateVersion5` (name-based). The implementation here is the
/// canonical RFC 4122 §4.3 algorithm:
///
/// 1. Convert `namespaceGuid` to network byte order (big-endian).
/// 2. Concatenate `namespace_bytes (16) || name_bytes (UTF-8)`.
/// 3. SHA-1 hash; take the first 16 bytes.
/// 4. Set the version bits (high 4 bits of byte[6] to `0101`).
/// 5. Set the variant bits (high 2 bits of byte[8] to `10`).
/// 6. Convert back to .NET's mixed-endian `Guid` layout.
///
/// Verified against the RFC 4122 §4.3 worked example:
///   uuidv5(DNS_NS = `6ba7b810-9dad-11d1-80b4-00c04fd430c8`,
///          name   = "www.example.org")
///   = `74738ff5-5367-5958-9aee-98fffdcd1876` (V).
[<RequireQualifiedAccess>]
module UuidV5 =

    /// Convert a `Guid` from .NET's mixed-endian layout (per
    /// `ToByteArray`) to RFC 4122 network byte order (big-endian
    /// throughout). The first three groups (4, 2, 2 bytes) reverse;
    /// the last 8 bytes are already in network order.
    let private toBigEndian (g: Guid) : byte[] =
        let b = g.ToByteArray()
        Array.Reverse(b, 0, 4)
        Array.Reverse(b, 4, 2)
        Array.Reverse(b, 6, 2)
        b

    /// Inverse of `toBigEndian`. Mutates a copy of the input array.
    let private fromBigEndian (bytes: byte[]) : Guid =
        let b = Array.copy bytes
        Array.Reverse(b, 0, 4)
        Array.Reverse(b, 4, 2)
        Array.Reverse(b, 6, 2)
        Guid(b)

    /// Apply the RFC 4122 §4.3 version-5 + variant bit-set to a
    /// fresh 16-byte buffer derived from a SHA-1 digest. Mutation
    /// is local to the function (the buffer is a private copy);
    /// the returned `Guid` is immutable.
    let private finalizeUuidV5 (sha1Hash: byte[]) : Guid =
        let bytes = Array.sub sha1Hash 0 16
        // Version 5: high 4 bits of byte[6] = 0101.
        bytes.[6] <- (bytes.[6] &&& 0x0Fuy) ||| 0x50uy
        // RFC 4122 variant: high 2 bits of byte[8] = 10.
        bytes.[8] <- (bytes.[8] &&& 0x3Fuy) ||| 0x80uy
        fromBigEndian bytes

    /// Total. RFC 4122 §4.3 name-based UUID (version 5). Pure;
    /// deterministic across runs and platforms. The string `name`
    /// is encoded as UTF-8 bytes; for typed component-list inputs
    /// (chapter 3.5 deep audit), prefer `createFromSegments` to
    /// avoid the intermediate concatenated string.
    let create (namespaceGuid: Guid) (name: string) : Guid =
        let nsBytes = toBigEndian namespaceGuid
        let nameBytes = Encoding.UTF8.GetBytes(name)
        let combined = Array.append nsBytes nameBytes
        finalizeUuidV5 (SHA1.HashData(combined))

    /// Total. RFC 4122 §4.3 UUIDv5 over a *typed byte-segment list*
    /// joined by a single separator byte. Per chapter 3.5 deep audit
    /// (2026-05-09) — the data-structure-oriented form: typed list
    /// of UTF-8-byte segments flows in via BCL `SHA1.TransformBlock`
    /// incremental hashing; no intermediate concatenated string is
    /// allocated. Byte-equivalent to `create namespaceGuid (String
    /// .concat <sep> segments)` (UTF-8 of `"a:b"` is identical to
    /// UTF-8(`"a"`) + 0x3A + UTF-8(`"b"`)), so consumers migrating
    /// from string-form get the same output Guid.
    ///
    /// LINT-ALLOW-FILE-MUTATION already covers the
    /// `SHA1.TransformBlock` mutation surface.
    let createFromSegments
        (namespaceGuid: Guid)
        (separator: byte)
        (segments: byte[] list)
        : Guid =
        let nsBytes = toBigEndian namespaceGuid
        use sha1 = SHA1.Create()
        sha1.TransformBlock(nsBytes, 0, nsBytes.Length, null, 0) |> ignore
        let sepBuffer = [| separator |]
        let mutable first = true
        for seg in segments do
            if not first then
                sha1.TransformBlock(sepBuffer, 0, 1, null, 0) |> ignore
            first <- false
            sha1.TransformBlock(seg, 0, seg.Length, null, 0) |> ignore
        sha1.TransformFinalBlock([||], 0, 0) |> ignore
        // BCL `SHA1.Hash` returns `byte[] | null` per the F# 9
        // nullness annotation, but it's non-null after a
        // `TransformFinalBlock` call. Coerce via `nonNull`.
        finalizeUuidV5 (nonNull sha1.Hash)

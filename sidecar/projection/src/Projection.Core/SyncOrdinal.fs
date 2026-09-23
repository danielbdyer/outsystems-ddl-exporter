namespace Projection.Core

/// The sink ledger's edition ordinal (align-III.1; audit a5's S-VO charge).
/// 1-based and dense by the witness's own law — sync 1 IS the genesis witness
/// (the first edition's diff from the empty state), and every later edition is
/// its predecessor's successor. The VO makes the bare int's two lies
/// unrepresentable: a zero/negative "sync" cannot be constructed (stored wires
/// stay raw ints and re-mint through `create`, fail-closed), and "nothing
/// witnessed yet" is `SyncOrdinal option` — never a magic 0.
type SyncOrdinal = private SyncOrdinal of int

[<RequireQualifiedAccess>]
module SyncOrdinal =

    /// The first witnessed edition — the genesis diff from the empty state.
    let genesis : SyncOrdinal = SyncOrdinal 1

    /// Mint from a parsed/stored int. Fail-closed below 1: a wire carrying
    /// 0 or a negative is a torn or foreign record, never an edition. The
    /// message is static (Core purity — no sprintf); the boundary that
    /// parsed the wire adds WHERE, and the store file itself shows WHAT.
    let create (n: int) : Result<SyncOrdinal, string> =
        if n >= 1 then Ok (SyncOrdinal n)
        else Error "a sync ordinal is 1-based; zero or below names no witnessed edition"

    /// The raw ordinal (the wire form — journals and manifests persist ints).
    let value (SyncOrdinal n) : int = n

    /// The ordinal the next witness mints: genesis when nothing is witnessed
    /// yet, else the predecessor's successor (density is the witness's law).
    let next (previous: SyncOrdinal option) : SyncOrdinal =
        match previous with
        | None -> genesis
        | Some (SyncOrdinal n) -> SyncOrdinal (n + 1)

    /// The rendered ordinal — the `@<n>` display/pin form.
    let text (SyncOrdinal n) : string = string n

/// One witnessed edition of one sink source: WHICH source (its
/// credential-rotation-invariant connection digest) at WHICH ordinal — the
/// coordinate a `sink:<env>@<n>` pin resolves to, a witness outcome names,
/// and (align-III.2) a ruling basis anchors to. The pair travels together
/// because neither half addresses an edition alone.
type SinkEdition =
    { ConnDigest : string
      Ordinal    : SyncOrdinal }

[<RequireQualifiedAccess>]
module SinkEdition =

    /// The display form: `<digest16>@<n>`.
    let text (e: SinkEdition) : string =
        e.ConnDigest + "@" + SyncOrdinal.text e.Ordinal // LINT-ALLOW: terminal edition-identity tag (digest@ordinal); the value IS a string identity at the boundary

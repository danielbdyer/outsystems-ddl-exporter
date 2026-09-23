namespace Projection.Core

/// align-II.1 (A53; audit a4) — the RULING carrier. The system collects
/// operator judgment in six dialects (approvals, signoffs, consents,
/// overrides, suggested-config responses, estate levers) but had no
/// carrier for a RULING: a keyed, anchored, durable "the operator
/// confirmed/rejected THIS subject on THIS evidence." This file is that
/// carrier, pure; the durable keyed store is `RulingStore` (Pipeline).

/// What a ruling anchors TO — the evidence identity the operator judged.
/// An anchored ruling is judgment about the subject AS WITNESSED: a
/// reopen probe compares the anchor against current evidence to decide
/// staleness. `None` basis is judgment about the subject in general.
[<RequireQualifiedAccess>]
type BasisAnchor =
    /// A content digest (registry digest, catalog digest, …).
    | Digest of value: string
    /// An estate evidence fingerprint token.
    | Fingerprint of value: string
    /// The finding key the ruling adjudicates (the estate DECIDE lane's
    /// anchor — II.5 reception renders rulings on their findings).
    | FindingKey of key: FindingKey
    /// An evidence-pack digest (the A4-4 adoption-lever anchor).
    | EvidenceDigest of value: string
    /// A witnessed sink edition (align-III.2 — the II.1-deferred widen, its
    /// trigger fired now that `SinkEdition` is a carrier and the ledger's
    /// admission is honest). A ruling anchored to an edition is judgment
    /// about a sink finding AS OF that witnessed state; a reopen probe can
    /// compare the current edition against the ruled one — the substrate an
    /// append-only ruling history stands on.
    | SinkEdition of edition: SinkEdition

/// The operator's verdict. No `Pending` — an unruled subject simply has
/// no ruling in the keyed store (absence is the pending state, and it is
/// structurally distinct from a recorded rejection).
[<RequireQualifiedAccess>]
type RulingVerdict =
    | Confirmed
    | Rejected

/// When a recorded ruling stops binding and the subject re-opens for
/// judgment. Minimal closed vocabulary; expands under evidence.
[<RequireQualifiedAccess>]
type ReopenCondition =
    /// Reopen when the anchored evidence no longer matches the subject's
    /// current evidence (requires a `Basis` to compare against).
    | OnEvidenceChange
    /// Reopen at/after the named instant (boundary-compared).
    | After of at: System.DateTimeOffset

/// One operator ruling on one subject. Generic in the subject so the
/// estate lane rules `FindingKey`s while future lanes can rule other
/// identities; the BasisAnchor vocabulary stays closed either way.
/// `At` is boundary-supplied (Core reads no clock).
type OperatorRuling<'subject> = {
    Subject   : 'subject
    Verdict   : RulingVerdict
    Basis     : BasisAnchor option
    By        : string
    At        : System.DateTimeOffset
    Rationale : string option
    Reopen    : ReopenCondition option
}

[<RequireQualifiedAccess>]
module RulingVerdict =

    /// Wire token (the store's closed vocabulary).
    let token (v: RulingVerdict) : string =
        match v with
        | RulingVerdict.Confirmed -> "confirmed"
        | RulingVerdict.Rejected  -> "rejected"

    let tryParse (s: string) : RulingVerdict option =
        match s with
        | "confirmed" -> Some RulingVerdict.Confirmed
        | "rejected"  -> Some RulingVerdict.Rejected
        | _ -> None

[<RequireQualifiedAccess>]
module OperatorRuling =

    let private byBlank =
        ValidationError.create "ruling.by.blank"
            "A ruling must name who ruled — 'by' cannot be blank."

    let private rationaleBlank =
        ValidationError.create "ruling.rationale.blank"
            "A supplied rationale cannot be blank — omit it instead."

    /// Construct a ruling, validating the free-text fields. `At` comes
    /// from the boundary (the verb's clock); Core only carries it.
    let create
        (subject: 'subject)
        (verdict: RulingVerdict)
        (basis: BasisAnchor option)
        (by: string)
        (at: System.DateTimeOffset)
        (rationale: string option)
        (reopen: ReopenCondition option)
        : Result<OperatorRuling<'subject>> =
        if System.String.IsNullOrWhiteSpace by then
            Result.failureOf byBlank
        elif rationale |> Option.exists System.String.IsNullOrWhiteSpace then
            Result.failureOf rationaleBlank
        else
            Result.success
                { Subject = subject
                  Verdict = verdict
                  Basis = basis
                  By = by
                  At = at
                  Rationale = rationale
                  Reopen = reopen }

namespace Projection.Core

// LINT-ALLOW-FILE: terminal version-string + policy-digest projection. `%d.%d.%d`
//   semantic-version rendering and the explicit per-field policy token projection
//   (M5 — `appendField` / `appendLP`, replacing the former `%A` representation)
//   feed the content hash at the SHA256 boundary; the typed version + policy
//   records remain the structure. Per `DECISIONS 2026-05-09 — Built-in obligation`
//   and the M5 determinism-by-construction move (THE VECTOR §6 Kind II).

open System
open System.Security.Cryptography
open System.Text

/// Semantic version triple for policy snapshots (H-085).
///
/// The component layout follows SemVer 2.0.0 conventions:
///   - `Major` — bumps when a previous policy axis is removed or restricted
///     (an established surface is taken away or narrowed). Example: turning
///     a `TighteningPolicy.Interventions` list from N entries down to N-1.
///   - `Minor` — bumps when a new axis surface is added (something that
///     was previously open is now constrained, OR a new intervention
///     was added). Example: adding an `IncludeOnly` restriction to a
///     previously `IncludeAll` policy is a *restriction* in selection
///     semantics but an *addition* of operator intent; per HORIZON it is
///     a minor bump because the change adds an axis.
///   - `Patch` — bumps when only the rationale / cosmetic content
///     changes; the policy's structural shape is unchanged.
///
/// Composers `next*` produce the next version from a previous version
/// according to the three bump rules (SemVer 2.0.0: a major bump resets
/// minor and patch to 0; a minor bump resets patch to 0).
type SemVer = {
    Major : int
    Minor : int
    Patch : int
}


/// Classification of the structural delta between two `Policy` values
/// (H-085). Encodes the SemVer bump kind without committing to a
/// specific previous version.
type SemVerBump =
    /// No structural change — policies are content-equal. No bump
    /// required (current version stands).
    | NoBump
    /// Rationale or non-structural change only (preserved for future
    /// use; the current `Policy` shape has no rationale fields, so this
    /// branch fires for content-equal policies whose serialized
    /// representations nonetheless differ — e.g., across runtime
    /// versions). Reset patch+1.
    | PatchBump
    /// New axis surface added (a previously-default axis took on a
    /// non-default value, or a Tightening intervention was added).
    /// Minor+1; reset patch to 0.
    | MinorBump
    /// Existing axis surface removed or restricted (a non-default axis
    /// returned to its default, or an intervention was removed, or an
    /// existing axis value was narrowed). Major+1; reset minor and
    /// patch to 0.
    | MajorBump


/// A `Policy` stamped with a content-derived version identifier (H-085).
///
/// Carries both a stable content digest and a `SemVer` version. The digest
/// (hex SHA-256) is the snapshot identity — equal digest ⇒ structurally
/// equal policy. The SemVer captures the relationship to a *previous*
/// snapshot; on construction without a predecessor the version defaults
/// to `1.0.0` (the first version in a chain). `evolve` produces the next
/// `VersionedPolicy` from a predecessor + new policy + changelog, applying
/// the correct bump.
type VersionedPolicy = {
    /// Hex SHA-256 of the canonical policy representation. Stable for
    /// structurally-equal policies within a runtime version.
    Digest    : string
    /// SemVer version. `1.0.0` for the first snapshot in a chain;
    /// computed from the predecessor's version + the bump on `evolve`.
    Version   : SemVer
    /// Snapshot timestamp.
    At        : DateTimeOffset
    /// The policy value.
    Policy    : Policy
    /// Optional human-readable description of what changed relative to
    /// the prior version.
    ChangeLog : string option
}


/// SemVer projection helpers (H-085).
[<RequireQualifiedAccess>]
module SemVer =

    /// The genesis version `1.0.0` — the first snapshot in a chain.
    let genesis : SemVer = { Major = 1; Minor = 0; Patch = 0 }

    /// Apply a `SemVerBump` to a `SemVer`. Major resets minor and patch
    /// to 0; minor resets patch to 0; patch increments only patch;
    /// `NoBump` returns the input unchanged.
    let applyBump (bump: SemVerBump) (v: SemVer) : SemVer =
        match bump with
        | NoBump    -> v
        | PatchBump -> { v with Patch = v.Patch + 1 }
        | MinorBump -> { v with Minor = v.Minor + 1; Patch = 0 }
        | MajorBump -> { Major = v.Major + 1; Minor = 0; Patch = 0 }

    /// Compact textual form `"M.m.p"`. Round-trips via `tryParse` (TBD).
    let toString (v: SemVer) : string =
        sprintf "%d.%d.%d" v.Major v.Minor v.Patch


/// Construction and inspection for `VersionedPolicy` (H-085).
[<RequireQualifiedAccess>]
module VersionedPolicy =

    // -----------------------------------------------------------------
    // M5 (THE VECTOR §6 Kind II) — the canonical policy projection.
    //
    // The digest's canonical representation was `sprintf "%A" policy` —
    // determinism-by-LUCK: the F# structural printer's shape depends on the
    // compiler version, exactly the failure the neighbouring
    // `TransformRegistry.digest` forbids (NM-60). This is determinism-by-
    // CONSTRUCTION: every field projected explicitly, every closed DU to a
    // fixed token, every free-text / identity field LENGTH-PREFIXED
    // (`<utf8-byte-count>:value`) so the encoding is injective — no crafted
    // name / id / value can forge a different policy that serializes to the
    // same buffer. Sets are sorted by their serialized identity (order-
    // independent); the intervention + delete-scope lists preserve operator
    // order (registration order is itself information). This CHANGES the
    // digest VALUE versus the prior `%A` form; no digest is pinned cross-
    // process (the docstring already warned "snapshot ID, not a cross-version
    // stability guarantee"; `GoldenEmissionTests` excludes the manifest's
    // VersionedPolicy stamp — verified), so the change is internal.
    // -----------------------------------------------------------------

    /// Append a variable-length field LENGTH-PREFIXED (`<utf8-byte-count>:value`)
    /// — the injective free-text encoding `TransformRegistry.appendLenPrefixed`
    /// established (NM-60). The byte count (not char count) defeats multibyte
    /// confusion; the prefix makes the structural delimiters unforgeable.
    let private appendLP (sb: StringBuilder) (value: string) : unit =
        sb.Append(Encoding.UTF8.GetByteCount value) |> ignore
        sb.Append(':') |> ignore
        sb.Append(value) |> ignore

    /// Append a `|tag=token` segment whose token is drawn from a fixed set
    /// (a closed-DU case name or a bool) — no length prefix needed.
    let private appendField (sb: StringBuilder) (tag: string) (token: string) : unit =
        sb.Append('|') |> ignore
        sb.Append(tag) |> ignore
        sb.Append('=') |> ignore
        sb.Append(token) |> ignore

    let private boolTok (b: bool) : string = if b then "T" else "F"

    /// A sorted, counted, length-prefixed projection of an SsKey set — order-
    /// independent (a set has no order) via the sorted serialized identities.
    let private appendSsKeySet (sb: StringBuilder) (keys: SsKey Set) : unit =
        let sorted = keys |> Set.toList |> List.map SsKey.serialize |> List.sort
        sb.Append(List.length sorted) |> ignore
        for k in sorted do
            sb.Append(';') |> ignore
            appendLP sb k

    let private appendSelection (sb: StringBuilder) (s: SelectionPolicy) : unit =
        match s with
        | IncludeAll       -> appendField sb "sel" "All"
        | IncludeOnly keys -> appendField sb "sel" "Only"; appendSsKeySet sb keys
        | ExcludeOnly keys -> appendField sb "sel" "Excl"; appendSsKeySet sb keys

    let private dataCompositionTok (c: DataComposition) : string =
        match c with
        | AllRemaining    -> "AllRemaining"
        | AllExceptStatic -> "AllExceptStatic"
        | AllData         -> "AllData"

    let private dataVerificationTok (v: DataVerification) : string =
        match v with
        | DataVerification.Standard            -> "Standard"
        | DataVerification.ValidateBeforeApply -> "ValidateBeforeApply"

    let private appendEmission (sb: StringBuilder) (e: EmissionPolicy) : unit =
        appendField sb "emSchema" (boolTok e.EmitSchema)
        appendField sb "emData"   (boolTok e.EmitData)
        appendField sb "emDiag"   (boolTok e.EmitDiagnostics)
        appendField sb "dataComp" (dataCompositionTok e.DataComposition)
        appendField sb "platAuto" (boolTok e.IncludePlatformAutoIndexes)
        (match e.DeleteScope with
         | None -> appendField sb "delScope" "None"
         | Some ds ->
             appendField sb "delScope" "Some"
             sb.Append(List.length ds.Terms) |> ignore
             for t in ds.Terms do
                 sb.Append(';') |> ignore
                 appendLP sb (ColumnName.value t.Column)
                 appendLP sb t.Value)
        appendField sb "elegant"   (boolTok e.RenderConstraintsElegant)
        appendField sb "identAnn"  (boolTok e.EmitIdentityAnnotations)
        appendField sb "dataVerif" (dataVerificationTok e.DataVerification)

    let private insertionTok (i: InsertionPolicy) : string =
        // Qualified cases — `Merge` otherwise resolves to `PolicyExpr.Merge`.
        match i with
        | InsertionPolicy.SchemaOnly        -> "SchemaOnly"
        | InsertionPolicy.InsertNew         -> "InsertNew"
        | InsertionPolicy.Merge             -> "Merge"
        | InsertionPolicy.TruncateAndInsert -> "TruncateAndInsert"

    let private overrideActionTok (a: OverrideAction) : string =
        match a with
        | KeepNullable -> "KeepNullable"

    let private fkOverrideActionTok (a: ForeignKeyOverrideAction) : string =
        match a with
        | KeepUntracked -> "KeepUntracked"

    let private directionTok (d: TighteningDirection) : string =
        match d with
        | TighteningDirection.EvidenceDriven -> "EvidenceDriven"
        | TighteningDirection.RelaxationOnly -> "RelaxationOnly"

    let private appendIntervention (sb: StringBuilder) (i: TighteningIntervention) : unit =
        match i with
        | Nullability (id, cfg) ->
            appendField sb "tv" "Nullability"
            appendLP sb id
            sb.Append(";nb=")  |> ignore
            appendLP sb (cfg.NullBudget.ToString(System.Globalization.CultureInfo.InvariantCulture))
            sb.Append(";amr=") |> ignore; sb.Append(boolTok cfg.AllowMandatoryRelaxation) |> ignore
            sb.Append(";dir=") |> ignore; sb.Append(directionTok cfg.Direction) |> ignore
            sb.Append(";ov=")  |> ignore; sb.Append(List.length cfg.Overrides) |> ignore
            for o in cfg.Overrides do
                sb.Append('{') |> ignore
                appendLP sb (SsKey.serialize o.AttributeKey)
                sb.Append(',') |> ignore
                sb.Append(overrideActionTok o.Action) |> ignore
                sb.Append('}') |> ignore
        | UniqueIndex (id, cfg) ->
            appendField sb "tv" "UniqueIndex"
            appendLP sb id
            sb.Append(";scu=") |> ignore; sb.Append(boolTok cfg.EnforceSingleColumnUnique) |> ignore
            sb.Append(";mcu=") |> ignore; sb.Append(boolTok cfg.EnforceMultiColumnUnique) |> ignore
        | ForeignKey (id, cfg) ->
            appendField sb "tv" "ForeignKey"
            appendLP sb id
            sb.Append(";ec=")  |> ignore; sb.Append(boolTok cfg.EnableCreation) |> ignore
            sb.Append(";acs=") |> ignore; sb.Append(boolTok cfg.AllowCrossSchema) |> ignore
            // WP-1d: the inert `acc=`/`tmd=` tokens were removed with the
            // `AllowCrossCatalog` / `TreatMissingDeleteRuleAsIgnore` fields.
            sb.Append(";anc=") |> ignore; sb.Append(boolTok cfg.AllowNoCheckCreation) |> ignore
            sb.Append(";dir=") |> ignore; sb.Append(directionTok cfg.Direction) |> ignore
            sb.Append(";rov=") |> ignore; sb.Append(List.length cfg.Overrides) |> ignore
            for o in cfg.Overrides do
                sb.Append('{') |> ignore
                appendLP sb (SsKey.serialize o.ReferenceKey)
                sb.Append(',') |> ignore
                sb.Append(fkOverrideActionTok o.Action) |> ignore
                sb.Append('}') |> ignore
        | CategoricalUniqueness (id, cfg) ->
            appendField sb "tv" "CategoricalUniqueness"
            appendLP sb id
            sb.Append(";md=") |> ignore; sb.Append(string cfg.MinDistinctCountForUniqueness) |> ignore

    let private appendTightening (sb: StringBuilder) (t: TighteningPolicy) : unit =
        appendField sb "tight" (string (List.length t.Interventions))
        for i in t.Interventions do
            sb.Append('[') |> ignore
            appendIntervention sb i
            sb.Append(']') |> ignore

    let rec private appendUserMatching (sb: StringBuilder) (u: UserMatchingStrategy) : unit =
        match u with
        | ByEmail -> appendField sb "um" "ByEmail"
        | BySsKey -> appendField sb "um" "BySsKey"
        | ManualOverride m ->
            appendField sb "um" "ManualOverride"
            let pairs =
                m |> Map.toList
                |> List.map (fun (s, t) -> SourceUserId.value s, TargetUserId.value t)
                |> List.sortBy fst
            sb.Append(List.length pairs) |> ignore
            for (s, t) in pairs do
                sb.Append(';') |> ignore; sb.Append(s) |> ignore; sb.Append("->") |> ignore; sb.Append(t) |> ignore
        | FallbackToSystemUser (fallback, primary) ->
            appendField sb "um" "Fallback"
            sb.Append(";fb=") |> ignore; sb.Append(TargetUserId.value fallback) |> ignore
            sb.Append(";primary=(") |> ignore
            appendUserMatching sb primary
            sb.Append(')') |> ignore

    /// The canonical, injective string projection of a whole `Policy` — the
    /// five axes, each field explicit. The digest hashes THIS.
    let private canonicalToken (policy: Policy) : string =
        // Instance-local StringBuilder mutation only, sealed at this function's
        // exit; mirrors `TransformRegistry.digest`'s accumulator. No consumer
        // reads the buffer — only the resulting bytes feed SHA256.
        let sb = StringBuilder()
        appendSelection sb policy.Selection
        appendEmission sb policy.Emission
        appendField sb "ins" (insertionTok policy.Insertion)
        appendTightening sb policy.Tightening
        appendUserMatching sb policy.UserMatching
        sb.ToString()

    /// Compute the hex SHA-256 content digest for a given policy. The canonical
    /// representation is the explicit `canonicalToken` projection (M5);
    /// structurally-equal policies hash equal, by construction rather than by
    /// the F# printer's luck. Consumers comparing digests across runtime
    /// upgrades should still treat inequality as "possibly changed" rather than
    /// "definitely changed" — the digest is a snapshot ID; cross-version
    /// stability now rests on the projection (stable) rather than on `%A`.
    let digestOf (policy: Policy) : string =
        use _ = Bench.scope "ir.policy.digestOf"
        let bytes = Encoding.UTF8.GetBytes (canonicalToken policy)
        let hash = SHA256.HashData bytes
        System.Convert.ToHexString(hash).ToLowerInvariant()

    // -----------------------------------------------------------------
    // Bump classification (H-085 SemVer semantics)
    // -----------------------------------------------------------------

    /// True iff the selection went from a wider to a narrower set of kinds.
    /// `IncludeAll` is the widest; `IncludeOnly s` narrows to `s`;
    /// `ExcludeOnly s` narrows by removing `s`.
    let private selectionRestricted
        (before: SelectionPolicy)
        (after: SelectionPolicy)
        : bool =
        match before, after with
        | IncludeAll, IncludeOnly _ -> true
        | IncludeAll, ExcludeOnly s -> not (Set.isEmpty s)
        | IncludeOnly a, IncludeOnly b ->
            Set.isProperSubset b a
        | ExcludeOnly a, ExcludeOnly b ->
            Set.isProperSubset a b
        | _ -> false

    /// True iff the selection went from a narrower to a wider set of kinds.
    let private selectionWidened
        (before: SelectionPolicy)
        (after: SelectionPolicy)
        : bool =
        selectionRestricted after before

    /// Classify the structural delta between two policies as a SemVer
    /// bump. The HORIZON contract is:
    ///   - **Major**: removal or restriction of an existing axis
    ///     (e.g., `IncludeAll` → `IncludeOnly` narrows selection;
    ///     `Tightening.Interventions` shrinks; a non-default axis
    ///     returns to its default).
    ///   - **Minor**: addition of an axis surface (a default value
    ///     takes on a non-default value, or a Tightening intervention
    ///     is added).
    ///   - **Patch**: structural equality but representational
    ///     difference (e.g., the F# structural printer disagrees but
    ///     `before = after`). Reserved for cross-runtime stability.
    ///   - **NoBump**: structural equality AND identical digest.
    let bumpKind (before: Policy) (after: Policy) : SemVerBump =
        if before = after then NoBump
        else
            let selRestrict = selectionRestricted before.Selection after.Selection
            let selWiden    = selectionWidened    before.Selection after.Selection
            let beforeIds =
                before.Tightening.Interventions
                |> List.map TighteningIntervention.id |> Set.ofList
            let afterIds  =
                after.Tightening.Interventions
                |> List.map TighteningIntervention.id |> Set.ofList
            let interventionsRemoved = not (Set.isSubset beforeIds afterIds)
            let interventionsAdded   = not (Set.isSubset afterIds beforeIds)
            let isMajor =
                selRestrict
                || interventionsRemoved
                || (before.Emission     <> after.Emission
                    && after.Emission     = Policy.empty.Emission)
                || (before.Insertion    <> after.Insertion
                    && after.Insertion    = Policy.empty.Insertion)
                || (before.UserMatching <> after.UserMatching
                    && after.UserMatching = Policy.empty.UserMatching)
            let isMinor =
                selWiden
                || interventionsAdded
                || (before.Emission     <> after.Emission
                    && before.Emission    = Policy.empty.Emission)
                || (before.Insertion    <> after.Insertion
                    && before.Insertion   = Policy.empty.Insertion)
                || (before.UserMatching <> after.UserMatching
                    && before.UserMatching = Policy.empty.UserMatching)
            let interventionsContentChanged =
                // Same ID set but different config (e.g. a NullBudget or
                // EnableCreation flip) is a MATERIAL change of operator intent,
                // not cosmetic (NM-58). Sort by id so a pure reordering of the
                // intervention list does not count. The intervention carries
                // only (id, config) — there is no rationale field to exclude.
                (before.Tightening.Interventions |> List.sortBy TighteningIntervention.id)
                <> (after.Tightening.Interventions |> List.sortBy TighteningIntervention.id)
            let isStructural =
                before.Selection <> after.Selection
                || before.Emission <> after.Emission
                || before.Insertion <> after.Insertion
                || before.UserMatching <> after.UserMatching
                || beforeIds <> afterIds
                || interventionsContentChanged
            if isMajor then MajorBump
            elif isMinor then MinorBump
            elif isStructural then
                // Non-default → non-default change on an axis, or a same-id
                // intervention whose config changed: the axis surface is
                // changing rather than appearing/disappearing. Counts as Minor
                // (a new operator intent appeared).
                MinorBump
            else
                // Identical intervention content in a different order (a pure
                // reorder). Patch bump preserves the chain without claiming a
                // structural change.
                PatchBump

    // -----------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------

    /// Construct a genesis `VersionedPolicy` (`1.0.0`) at the given
    /// timestamp. Used for the first snapshot in a version chain.
    let create
        (at: DateTimeOffset)
        (policy: Policy)
        (changeLog: string option)
        : VersionedPolicy =
        { Digest    = digestOf policy
          Version   = SemVer.genesis
          At        = at
          Policy    = policy
          ChangeLog = changeLog }

    /// Evolve a `VersionedPolicy` to a new policy, computing the next
    /// SemVer from the predecessor's version and the structural delta.
    /// `at` records the new snapshot's timestamp.
    let evolve
        (predecessor: VersionedPolicy)
        (at: DateTimeOffset)
        (newPolicy: Policy)
        (changeLog: string option)
        : VersionedPolicy =
        let bump = bumpKind predecessor.Policy newPolicy
        let nextVersion = SemVer.applyBump bump predecessor.Version
        { Digest    = digestOf newPolicy
          Version   = nextVersion
          At        = at
          Policy    = newPolicy
          ChangeLog = changeLog }

    // Slice 0 (2026-06-02): `VersionedPolicy.now` and `evolveNow` retired.
    // They captured `DateTimeOffset.UtcNow` inside Core (analyzer gap pre-Slice-0).
    // The principled shape mirrors `Episode.fs` and `ApprovalRecord.At`:
    // Core takes `at` as a boundary-supplied parameter; CLI and Pipeline
    // supply `DateTimeOffset.UtcNow` at the call site. Tests use a
    // per-file fixed `testTime` constant for determinism.

    /// True iff two versioned policies share the same content digest
    /// (structurally equal policies, regardless of version / timestamp).
    let sameContent (a: VersionedPolicy) (b: VersionedPolicy) : bool =
        a.Digest = b.Digest

    /// True iff `after`'s digest differs from `before`'s — i.e. the
    /// policy changed between the two snapshots.
    let changed (before: VersionedPolicy) (after: VersionedPolicy) : bool =
        not (sameContent before after)

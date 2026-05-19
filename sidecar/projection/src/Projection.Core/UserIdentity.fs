namespace Projection.Core

/// User-identity value objects + cross-environment user-population
/// shapes (chapter 4.2 slices α + β).
///
/// **Compile-order positioning** (per `Projection.Core.fsproj`): this
/// file compiles BEFORE `Profile.fs` so Profile can carry typed
/// `UserPopulation<SourceUserId>` + `UserPopulation<TargetUserId>`
/// fields (slice β). `Policy.fs` (which compiles after Profile.fs) is
/// where `UserMatchingStrategy` lives — the strategy DU references
/// the identity types from THIS file.
///
/// **Why a separate file** (per pillar 8 concept-shaped naming +
/// `Policy.fs` already at 600+ lines): user identity is its own
/// bounded context (per pre-scope §3 — "user identity is the bridge
/// across environments"). Colocating identity types + populations
/// here gives slice δ's `UserFkReflowPass.discover` one import
/// surface for everything user-shaped that isn't strategy.

/// Underlying user identifier (per pre-scope §3 — V1's `UserId`
/// shape is `int`; V2 wraps for newtype safety). Boundary adapters
/// parse from V1's `osm_model.json` user-population evidence into
/// this shape; Core sees only the typed value.
type UserId = UserId of int

/// Source-environment user identifier. The orientation marker
/// prevents passing a SourceUserId where TargetUserId is expected
/// (or vice versa) — a class of cutover-time bug ruled out by the
/// type system.
type SourceUserId = SourceUserId of UserId

/// Target-environment user identifier. Sibling to SourceUserId.
type TargetUserId = TargetUserId of UserId

/// User email — per pre-scope §3, the value carries V1's
/// `OrdinalIgnoreCase + Trim` normalization at construction.
/// Smart constructor on `Email.create` enforces non-blank.
type Email = Email of string


[<RequireQualifiedAccess>]
module UserId =

    /// Project the underlying integer. The newtype prevents
    /// accidental orientation confusion at type-check time;
    /// projection happens only at the boundary (CSV adapters,
    /// V1 differential).
    let value (UserId v) : int = v


[<RequireQualifiedAccess>]
module SourceUserId =

    /// Construct a SourceUserId from a raw integer. Boundary-side
    /// projection; trusted callers (adapters parsing V1's
    /// `osm_model.json` user-population evidence) wrap here.
    let ofInt (i: int) : SourceUserId = SourceUserId (UserId i)

    /// Project to the underlying integer. Used at the SQL-emission
    /// boundary when rendering FK column values.
    let value (SourceUserId (UserId v)) : int = v


[<RequireQualifiedAccess>]
module TargetUserId =

    /// Construct a TargetUserId from a raw integer. Sibling to
    /// SourceUserId.ofInt.
    let ofInt (i: int) : TargetUserId = TargetUserId (UserId i)

    /// Project to the underlying integer.
    let value (TargetUserId (UserId v)) : int = v


[<RequireQualifiedAccess>]
module Email =

    let private emailEmpty =
        ValidationError.create
            "email.empty"
            "An email cannot be blank."

    /// Construct an Email value. Validates non-blank input and
    /// normalizes via `Trim()` (V1 parity per `UserMatchingEngine.cs:
    /// 84-86, 97`). Case sensitivity is NOT normalized at construction
    /// — preserved for downstream display + audit trail; the matching
    /// strategy applies `OrdinalIgnoreCase` comparison at lookup time.
    let create (raw: string) : Result<Email> =
        if System.String.IsNullOrWhiteSpace raw then
            Result.failureOf emailEmpty
        else
            Result.success (Email (raw.Trim()))

    /// Project the underlying string. Used at the SQL-emission
    /// boundary when rendering or for ordinal-ignore-case
    /// comparison at the matching seam.
    let value (Email v) : string = v


// ---------------------------------------------------------------------------
// User-population shapes (chapter 4.2 slice β).
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4: `UserPopulation` lives
// in Profile (sibling field on `Profile.SourceUsers` / `Profile.
// TargetUsers`); user populations are *empirical* evidence (what users
// actually exist in each environment), not structural (the user kind
// exists once in Catalog).
//
// **Parameterized over the id type** (refinement on pre-scope §4's
// loose "UserId : SourceUserId  // or TargetUserId, contextually"
// shape): `UserAttributes<'id>` + `UserPopulation<'id>` are
// polymorphic so `Profile.SourceUsers : UserPopulation<SourceUserId>`
// and `Profile.TargetUsers : UserPopulation<TargetUserId>` are
// distinct typed values. Slice α's identity-type orientation safety
// extends to the population level: code that handles source users
// and target users cannot accidentally cross the orientation
// boundary at compile time.
// ---------------------------------------------------------------------------

/// Per-user attributes carried in a population. Generic over the id
/// type so the same shape serves source and target populations
/// without losing orientation-typing.
///
/// Slice β scope: three fields (per pre-scope §4) — Id, SsKey, Email.
/// Future strategies that demand richer attributes (phone, external
/// ID) extend the record under IR-grows-under-evidence; the smart
/// constructor `UserAttributes.create` is the natural extension
/// point.
type UserAttributes<'id> =
    {
        /// Environment-specific user identifier. Typed `'id` so
        /// `UserAttributes<SourceUserId>` and `UserAttributes<
        /// TargetUserId>` are distinct.
        Id    : 'id
        /// V2 IR identity (per A4). For OSSYS-native users this is
        /// the `OssysOriginal` SsKey carrying the platform GUID;
        /// for synthesized / derived users it carries the `Synthesized`
        /// or `Derived` shape. The `BySsKey` matching strategy
        /// (slice ε) uses this for identity-stable cross-environment
        /// matching when both environments inherit from a shared
        /// OSSYS origin.
        SsKey : SsKey
        /// User email. `None` for users with no email registered;
        /// the `ByEmail` matching strategy emits a `NoEmail`
        /// `RemapDiagnostic` (slice γ) for source users without an
        /// email.
        Email : Email option
    }


/// Per-environment user population. Generic over the id type so
/// `UserPopulation<SourceUserId>` and `UserPopulation<TargetUserId>`
/// are distinct typed values. The `Users` list is the empirical
/// evidence for one environment.
///
/// **Slice β scope**: minimal shape (Users list only). Indexing
/// helpers (`byEmail`, `bySsKey`) for the `UserFkReflowPass.
/// discover` performance path (per pre-scope §11 — the pass must
/// build a dictionary-keyed index once per discover-call, not per
/// source user) land at slice δ when the discovery pass arrives.
type UserPopulation<'id> =
    {
        Users : UserAttributes<'id> list
    }


[<RequireQualifiedAccess>]
module UserAttributes =

    /// Construct a `UserAttributes<'id>` value. No validation
    /// required at slice β — every field is already typed
    /// (the orientation guarantees come from the `'id` newtype;
    /// `Email.create` validates at the field level if the
    /// caller supplies `Some email`). Future strategies that
    /// demand cross-field invariants (e.g., "email must match a
    /// regex per environment") extend the smart constructor here.
    let create (id: 'id) (ssKey: SsKey) (email: Email option) : UserAttributes<'id> =
        { Id = id; SsKey = ssKey; Email = email }


[<RequireQualifiedAccess>]
module UserPopulation =

    /// The empty population. Equivalent to "no users registered for
    /// this environment" — `UserFkReflowPass.discover` against an
    /// empty target population produces every source user as
    /// `Unmatched` with the appropriate `RemapDiagnostic`.
    let empty<'id> : UserPopulation<'id> = { Users = [] }

    /// True iff the population has zero users.
    let isEmpty (p: UserPopulation<'id>) : bool =
        List.isEmpty p.Users

    /// Construct a population from a list of user attributes.
    /// Trivial wrapper preserved for symmetry with `empty` and to
    /// give callers a smart-constructor surface for future
    /// invariants (e.g., reject duplicate Ids; per pre-scope §11
    /// risks: argued against because environment data legitimately
    /// carries duplicate emails — but duplicate Ids would be a
    /// boundary-data bug worth catching here when consumer
    /// pressure surfaces).
    let create (users: UserAttributes<'id> list) : UserPopulation<'id> =
        { Users = users }

    /// Multi-env merge — left-biased union by `Id`. Per slice B.3.7
    /// `Profile.merge` discipline: commutative for the identity-set
    /// (the two populations share the same `Id` ⇒ same user); the
    /// left-bias ensures associativity when conflicting attribute
    /// records share an `Id` (the first observation's attributes
    /// stay; later observations only add new ids). Same SourceUserId
    /// or TargetUserId across environments means "same user" by
    /// construction (the `'id` newtype carries environment-scoped
    /// identity).
    let union (a: UserPopulation<'id>) (b: UserPopulation<'id>) : UserPopulation<'id> =
        let aIds = a.Users |> List.map (fun u -> u.Id) |> Set.ofList
        let additional = b.Users |> List.filter (fun u -> not (Set.contains u.Id aIds))
        { Users = a.Users @ additional }

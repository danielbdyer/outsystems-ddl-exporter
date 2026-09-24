module Projection.Tests.ArchetypeTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Slice A witness (DATABASE_ARCHETYPES.md §1/§4; REVERSE_LEG_WORK_PLAN Slice A).
/// The archetype is the FOUNDATION — a closed capability class that subsumes
/// `Grant`, expands to a `CapabilityProfile` at ONE site, and defaults from the
/// existing grant. Nothing branches on it yet (Slices B/C/S are the consumers),
/// so the only claims to pin are: the derive/infer round-trip, the expansion
/// matches the confirmed verdicts, the lazy default, and byte-identical render.

let private allArchetypes = [ Archetype.FullRights; Archetype.ManagedDml ]
let private allGrants     = [ Grant.SchemaAndData; Grant.DataOnly ]

// --- the (infer ∘ derive-grant) round-trip — the work plan's named witness ---

[<Fact>]
let ``Archetype — ofGrant ∘ grant = id on every archetype`` () =
    for a in allArchetypes do
        Assert.Equal<Archetype>(a, Archetype.ofGrant (Archetype.grant a))

[<Fact>]
let ``Archetype — grant ∘ ofGrant = id on every grant`` () =
    for g in allGrants do
        Assert.Equal<Grant>(g, Archetype.grant (Archetype.ofGrant g))

// --- the expansion matches the confirmed verdicts (§1 catalog) ---------------

[<Fact>]
let ``CapabilityProfile.of FullRights — the verified on-prem bundle (DDL + IDENTITY_INSERT + sink-resident + TRUNCATE)`` () =
    let p = CapabilityProfile.``of`` Archetype.FullRights
    Assert.Equal<Grant>(Grant.SchemaAndData, p.Grant)
    Assert.Equal<IdentityDisposition>(IdentityDisposition.PreservedFromSource, p.IdentityDefault)
    Assert.True(p.DdlPermitted)
    Assert.True(p.IdentityInsert)
    Assert.True(p.ConstraintBypass)
    Assert.Equal<ResumeKind>(ResumeKind.SinkResidentTable, p.ResumeCheckpoint)
    Assert.Equal<WipeKind>(WipeKind.Truncate, p.WipeStrategy)

[<Fact>]
let ``CapabilityProfile.of ManagedDml — the J5 cloud bundle (no DDL / IDENTITY_INSERT; sink-mints; client journal; child-first DELETE)`` () =
    let p = CapabilityProfile.``of`` Archetype.ManagedDml
    Assert.Equal<Grant>(Grant.DataOnly, p.Grant)
    Assert.Equal<IdentityDisposition>(IdentityDisposition.AssignedBySink, p.IdentityDefault)
    Assert.False(p.DdlPermitted)
    Assert.False(p.IdentityInsert)
    Assert.False(p.ConstraintBypass)
    Assert.Equal<ResumeKind>(ResumeKind.ClientJournal, p.ResumeCheckpoint)
    Assert.Equal<WipeKind>(WipeKind.ChildFirstDelete, p.WipeStrategy)

[<Fact>]
let ``CapabilityProfile — the derived Grant is a projection of the archetype (subsumes Grant)`` () =
    for a in allArchetypes do
        Assert.Equal<Grant>(Archetype.grant a, (CapabilityProfile.``of`` a).Grant)

// --- the lazy default: declared wins, else inferred from grant, else None -----

let private envWith (grant: Grant option) (archetype: Archetype option) : Environment =
    { Name = "e"; Access = Access.Direct (ConnectionRef.EnvVar "E_CONN")
      Grant = grant; Store = None; Rendition = None; Archetype = archetype; AtomicDeploy = None; Revert = None }

[<Fact>]
let ``effectiveArchetype — a declared archetype wins over the grant inference`` () =
    // grant DataOnly would infer ManagedDml, but the explicit declaration is FullRights.
    Assert.Equal<Archetype option>(
        Some Archetype.FullRights,
        Environment.effectiveArchetype (envWith (Some Grant.DataOnly) (Some Archetype.FullRights)))

[<Fact>]
let ``effectiveArchetype — an undeclared archetype is inferred from the grant`` () =
    Assert.Equal<Archetype option>(Some Archetype.FullRights, Environment.effectiveArchetype (envWith (Some Grant.SchemaAndData) None))
    Assert.Equal<Archetype option>(Some Archetype.ManagedDml, Environment.effectiveArchetype (envWith (Some Grant.DataOnly) None))

[<Fact>]
let ``effectiveArchetype — no grant and no declared archetype is no class (None → None)`` () =
    Assert.Equal<Archetype option>(None, Environment.effectiveArchetype (envWith None None))

// --- byte-identical render: an undeclared archetype emits no field ------------

[<Fact>]
let ``render — an undeclared archetype omits the field (existing configs byte-identical); a declared one emits the canonical token`` () =
    let bare = { ProjectionConfig.empty with Environments = Map.ofList [ "e", envWith (Some Grant.SchemaAndData) None ] }
    Assert.DoesNotContain("archetype", ProjectionConfig.render bare)
    let declared = { ProjectionConfig.empty with Environments = Map.ofList [ "e", envWith (Some Grant.SchemaAndData) (Some Archetype.ManagedDml) ] }
    Assert.Contains("managed-dml", ProjectionConfig.render declared)

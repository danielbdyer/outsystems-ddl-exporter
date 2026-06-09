module Projection.Tests.CapabilitySurveyTests

open Xunit
open Projection.Pipeline

/// The capability survey (prototype) — the pure core: the required-capability
/// catalog derived from the declared grant facet, and the declared-vs-actual
/// reconciliation. The parallel live probe is witnessed in the Docker pool.

[<Fact>]
let ``requiredFor: schema+data demands the schema activities; data-only just the DML`` () =
    Assert.Equal<Set<Preflight.WriteAction>>(
        set [ Preflight.Insert; Preflight.Delete; Preflight.Alter; Preflight.CreateTable ],
        CapabilitySurvey.requiredFor Grant.SchemaAndData)
    Assert.Equal<Set<Preflight.WriteAction>>(
        set [ Preflight.Insert; Preflight.Delete ],
        CapabilitySurvey.requiredFor Grant.DataOnly)

[<Fact>]
let ``reconcile: a data-only grant against a schema+data promise misses the schema activities`` () =
    let evidence : Preflight.GrantEvidence = { Granted = set [ ("", "INSERT"); ("", "DELETE") ] }
    let missing = CapabilitySurvey.reconcile Grant.SchemaAndData evidence
    Assert.Contains(Preflight.Alter, missing)
    Assert.Contains(Preflight.CreateTable, missing)
    Assert.DoesNotContain(Preflight.Insert, missing)   // INSERT is actually granted

[<Fact>]
let ``reconcile: a full grant covers the schema+data promise — nothing missing`` () =
    let evidence : Preflight.GrantEvidence =
        { Granted = set [ ("", "INSERT"); ("", "DELETE"); ("", "ALTER"); ("", "CREATE TABLE") ] }
    Assert.Empty(CapabilitySurvey.reconcile Grant.SchemaAndData evidence)

[<Fact>]
let ``reconcile: a data-only promise is covered by the DML grants alone`` () =
    let evidence : Preflight.GrantEvidence = { Granted = set [ ("", "INSERT"); ("", "DELETE") ] }
    Assert.Empty(CapabilitySurvey.reconcile Grant.DataOnly evidence)

module Projection.Tests.CapabilityRefusalTests

open Xunit
open Projection.Pipeline

// Recon #4 — the closed (SQL error number → Capability) registry the two reverse-leg
// descent sites recognize capability refusals through (replacing two hand-rolled
// `ex.Number = …` predicates the code itself flagged as siblings). A data error —
// anything NOT in the registry — is `None` and PROPAGATES; degrading on it would
// mask corruption.

[<Fact>]
let ``ofErrorNumber: 334 names the OUTPUT-without-INTO-on-triggered-target capability`` () =
    Assert.Equal(Some Capability.OutputWithoutIntoOnTriggeredTarget, CapabilityRefusal.ofErrorNumber 334)

[<Fact>]
let ``ofErrorNumber: 1088 / 4902 / 229 all name the ALTER-constraint-trust capability`` () =
    Assert.Equal(Some Capability.AlterConstraintTrust, CapabilityRefusal.ofErrorNumber 1088)
    Assert.Equal(Some Capability.AlterConstraintTrust, CapabilityRefusal.ofErrorNumber 4902)
    Assert.Equal(Some Capability.AlterConstraintTrust, CapabilityRefusal.ofErrorNumber 229)

[<Fact>]
let ``ofErrorNumber: a data error is NOT a capability refusal (propagates)`` () =
    Assert.Equal(None, CapabilityRefusal.ofErrorNumber 547)  // constraint conflict — the loud fidelity signal
    Assert.Equal(None, CapabilityRefusal.ofErrorNumber 0)

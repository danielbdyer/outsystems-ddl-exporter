module Projection.Tests.ApprovalStoreTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline

// Wave-3 slice 3.2 — ApprovalStore JSON round-trip. The ApprovalRegistry
// algebra lives in Core (pure); this verifies its durable JSON home so R6
// operator sign-off is recorded and consultable across runs (instead of being
// constructed and discarded). A missing store is the empty registry (safe
// first-run default); a malformed store is an error (never silently empty —
// that would lose recorded approvals).

/// Slice 0 (2026-06-02): Core retired the `*Now` wrappers; tests use a fixed
/// `testTime` for determinism. Per the Episode.fs "boundary-supplied at"
/// pattern — wall-clock impurity stays at the boundary.
let private testTime : System.DateTimeOffset =
    System.DateTimeOffset(2026, 1, 1, 0, 0, 0, System.TimeSpan.Zero)

let private withTempFile (f: string -> 'a) : 'a =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "approval-%s.json" (Guid.NewGuid().ToString("N")))
    try f path
    finally (try System.IO.File.Delete path with _ -> ())

let private at = DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero)

let private approvedRecord (digest: string) (approver: string) (rationale: string option) : ApprovalRecord =
    ApprovalWorkflow.pending testTime digest
    |> ApprovalWorkflow.approve approver rationale at

[<Fact>]
let ``3.2: an approval record round-trips through save -> load -> tryFind`` () =
    withTempFile (fun path ->
        let registry =
            ApprovalRegistry.empty
            |> ApprovalRegistry.record (approvedRecord "digestA" "alice" (Some "looks good"))
        match ApprovalStore.save path registry with
        | Error e -> Assert.Fail(sprintf "save: %A" e)
        | Ok () ->
            match ApprovalStore.load path with
            | Error e -> Assert.Fail(sprintf "load: %A" e)
            | Ok loaded ->
                match ApprovalRegistry.tryFind "digestA" loaded with
                | Some r ->
                    Assert.Equal(Approved, r.Decision)
                    Assert.Equal(Some "alice", r.ApprovedBy)
                    Assert.Equal(Some "looks good", r.Rationale)
                    Assert.Equal("digestA", r.PolicyVersion)
                    Assert.Equal(at, r.At)
                | None -> Assert.Fail("digestA not found after round-trip"))

[<Fact>]
let ``3.2: None fields (pending record, no approver / rationale) round-trip`` () =
    withTempFile (fun path ->
        let registry = ApprovalRegistry.empty |> ApprovalRegistry.record (ApprovalWorkflow.pending testTime "digestP")
        ApprovalStore.save path registry |> ignore
        match ApprovalStore.load path with
        | Ok loaded ->
            match ApprovalRegistry.tryFind "digestP" loaded with
            | Some r ->
                Assert.Equal(Pending, r.Decision)
                Assert.Equal(None, r.ApprovedBy)
                Assert.Equal(None, r.Rationale)
            | None -> Assert.Fail("digestP not found")
        | Error e -> Assert.Fail(sprintf "%A" e))

[<Fact>]
let ``3.2: a missing store loads as the empty registry (safe first-run default)`` () =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), sprintf "approval-missing-%s.json" (Guid.NewGuid().ToString("N")))
    match ApprovalStore.load path with
    | Ok r -> Assert.Equal<ApprovalRegistry>(ApprovalRegistry.empty, r)
    | Error e -> Assert.Fail(sprintf "expected Ok empty, got %A" e)

[<Fact>]
let ``3.2: a malformed store is a ParseFailure (never silently empty)`` () =
    withTempFile (fun path ->
        System.IO.File.WriteAllText(path, "{ this is not valid json ][")
        match ApprovalStore.load path with
        | Error (ParseFailure _) -> ()
        | Error e -> Assert.Fail(sprintf "expected ParseFailure, got %A" e)
        | Ok _ -> Assert.Fail("malformed store must not load as empty (would lose approvals)"))

[<Fact>]
let ``3.2: re-saving an unchanged registry is byte-stable (T1 determinism)`` () =
    withTempFile (fun path1 ->
        withTempFile (fun path2 ->
            let registry =
                ApprovalRegistry.empty
                |> ApprovalRegistry.record (approvedRecord "dB" "bob" None)
                |> ApprovalRegistry.record (approvedRecord "dA" "alice" (Some "ok"))
            ApprovalStore.save path1 registry |> ignore
            ApprovalStore.save path2 registry |> ignore
            Assert.Equal(System.IO.File.ReadAllText path1, System.IO.File.ReadAllText path2)))

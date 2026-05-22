module Projection.Tests.PolicyStateMachineTests

// H-098 (HORIZON Cluster F): model-based testing for the policy system.
//
// **Two surfaces:**
//   1. **Hand-rolled trace property** (original Cluster F shipping) —
//      direct FsCheck.Xunit `[<Property>]` over a `runTrace` function.
//      Simple; no Machine ceremony.
//   2. **FsCheck.Experimental.Machine** (Cluster F follow-up) — the
//      object-oriented state-machine API the HORIZON sketch named. The
//      operational advantage of the Machine variant is **shrinking**:
//      when the agreement property fails, FsCheck shrinks the failing
//      trace to a minimum reproducer.
//
// Both surfaces cover the same property — at every step, the model's
// "touched axes" projection equals the real Policy's "axes differing
// from Policy.empty" projection.
//
// **Model.** A `Map<ModelKey, bool>` tracks which axes have been
// touched (i.e., set to a non-default value) at any point in the
// trace.
//
// **Pillar 9 / A12 connection.** This is the executable witness of
// the orthogonality axiom: changing axis X must not perturb other
// axes' touched state.

open Xunit
open FsCheck
open FsCheck.Experimental
open FsCheck.Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Closed DU of operations the model exercises.
// ---------------------------------------------------------------------------

type private PolicyOp =
    | SetSelection of SelectionPolicy
    | SetEmission of EmissionPolicy
    | SetInsertion of InsertionPolicy
    | AddTightening of TighteningIntervention
    | SetUserMatching of UserMatchingStrategy
    | Reset

// ---------------------------------------------------------------------------
// Apply an operation to the real Policy.
// ---------------------------------------------------------------------------

let private apply (op: PolicyOp) (p: Policy) : Policy =
    match op with
    | SetSelection s    -> { p with Selection = s }
    | SetEmission e     -> { p with Emission = e }
    | SetInsertion i    -> { p with Insertion = i }
    | AddTightening t   -> { p with Tightening = { Interventions = p.Tightening.Interventions @ [ t ] } }
    | SetUserMatching u -> { p with UserMatching = u }
    | Reset             -> Policy.empty

// ---------------------------------------------------------------------------
// Apply an operation to the model (Map<OverlayAxis, bool>).
// `Reset` clears all touched axes. Every Set* operation marks its
// axis touched; UserMatching is its own axis but lives under
// "OverlayAxis ⊃ Policy axes" — we track it via a synthetic key
// since OverlayAxis doesn't currently name UserMatching.
// ---------------------------------------------------------------------------

type private ModelKey =
    | Axis of OverlayAxis
    | UserMatchingKey  // Policy.UserMatching has no OverlayAxis counterpart

let private applyModel (op: PolicyOp) (m: Map<ModelKey, bool>) : Map<ModelKey, bool> =
    match op with
    | SetSelection _    -> Map.add (Axis Selection) true m
    | SetEmission _     -> Map.add (Axis Emission) true m
    | SetInsertion _    -> Map.add (Axis Insertion) true m
    | AddTightening _   -> Map.add (Axis Tightening) true m
    | SetUserMatching _ -> Map.add UserMatchingKey true m
    | Reset             -> Map.empty

// ---------------------------------------------------------------------------
// Project the real Policy to its "touched" model.
// ---------------------------------------------------------------------------

let private touchedAxes (p: Policy) : Set<ModelKey> =
    let isTouched (key: ModelKey) (touched: bool) : ModelKey option =
        if touched then Some key else None
    [ isTouched (Axis Selection)     (p.Selection <> Policy.empty.Selection)
      isTouched (Axis Emission)      (p.Emission <> Policy.empty.Emission)
      isTouched (Axis Insertion)     (p.Insertion <> Policy.empty.Insertion)
      isTouched (Axis Tightening)    (p.Tightening <> Policy.empty.Tightening)
      isTouched UserMatchingKey      (p.UserMatching <> Policy.empty.UserMatching) ]
    |> List.choose id
    |> Set.ofList

let private modelTouched (m: Map<ModelKey, bool>) : Set<ModelKey> =
    m
    |> Map.toSeq
    |> Seq.choose (fun (k, v) -> if v then Some k else None)
    |> Set.ofSeq

// ---------------------------------------------------------------------------
// Per-file fixtures for op generation
// ---------------------------------------------------------------------------

let private nullabilityCfg (id: string) : TighteningIntervention =
    let cfg = NullabilityTighteningConfig.create 0.1m false [] |> Result.value
    Nullability (id, cfg)

/// Bounded set of operations for the property sweep. Each operation
/// targets one axis with a non-default value (so applying it MUST
/// flip the touched state for that axis).
let private opOfIndex (i: int) : PolicyOp =
    match abs i % 6 with
    | 0 -> SetSelection (IncludeOnly (Set.singleton customerKey))
    | 1 -> SetEmission EmissionPolicy.dataOnly
    | 2 -> SetInsertion InsertNew
    | 3 -> AddTightening (nullabilityCfg (sprintf "op-%d" (abs i)))
    | 4 -> SetUserMatching BySsKey
    | _ -> Reset

// ---------------------------------------------------------------------------
// Step-by-step trace: at every step, the model and the real Policy
// must agree on which axes are touched.
// ---------------------------------------------------------------------------

let private runTrace (indices: int list) : bool =
    let rec loop (policy: Policy) (model: Map<ModelKey, bool>) (ops: int list) : bool =
        match ops with
        | [] -> touchedAxes policy = modelTouched model
        | i :: rest ->
            let op = opOfIndex i
            let policy' = apply op policy
            let model'  = applyModel op model
            if touchedAxes policy' <> modelTouched model' then false
            else loop policy' model' rest
    loop Policy.empty Map.empty indices

// ---------------------------------------------------------------------------
// H-098 Law 1: the model and the real Policy agree on touched axes
// across any sequence of operations.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-098 model-impl agreement: trace-step touched axes equal at every step`` (indices: int list) =
    runTrace indices

// ---------------------------------------------------------------------------
// H-098 Law 2 (worked examples): single-axis transitions touch only
// the expected axis.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-098 single-op: SetSelection touches only Selection`` () =
    let policy' = apply (SetSelection (IncludeOnly (Set.singleton customerKey))) Policy.empty
    Assert.Equal<Set<ModelKey>>(Set.singleton (Axis Selection), touchedAxes policy')

[<Fact>]
let ``H-098 single-op: SetEmission touches only Emission`` () =
    let policy' = apply (SetEmission EmissionPolicy.dataOnly) Policy.empty
    Assert.Equal<Set<ModelKey>>(Set.singleton (Axis Emission), touchedAxes policy')

[<Fact>]
let ``H-098 single-op: SetInsertion touches only Insertion`` () =
    let policy' = apply (SetInsertion InsertNew) Policy.empty
    Assert.Equal<Set<ModelKey>>(Set.singleton (Axis Insertion), touchedAxes policy')

[<Fact>]
let ``H-098 single-op: AddTightening touches only Tightening`` () =
    let policy' = apply (AddTightening (nullabilityCfg "x")) Policy.empty
    Assert.Equal<Set<ModelKey>>(Set.singleton (Axis Tightening), touchedAxes policy')

[<Fact>]
let ``H-098 single-op: SetUserMatching touches only UserMatchingKey`` () =
    let policy' = apply (SetUserMatching BySsKey) Policy.empty
    Assert.Equal<Set<ModelKey>>(Set.singleton UserMatchingKey, touchedAxes policy')

// ---------------------------------------------------------------------------
// H-098 Law 3: Reset returns to Policy.empty.
// ---------------------------------------------------------------------------

[<Fact>]
let ``H-098 reset: Reset after any single op returns Policy.empty`` () =
    let p1 = apply (SetSelection (IncludeOnly (Set.singleton customerKey))) Policy.empty
    let p2 = apply Reset p1
    Assert.Equal(Policy.empty, p2)

[<Property>]
let ``H-098 reset (property): Reset clears the touched axis set`` (indices: int list) =
    let final =
        indices
        |> List.fold (fun p i -> apply (opOfIndex i) p) Policy.empty
        |> apply Reset
    touchedAxes final = Set.empty

// ---------------------------------------------------------------------------
// H-098 Law 4: monotone growth on touched axes between Reset
// operations. Without a Reset, the touched set can only grow.
// ---------------------------------------------------------------------------

[<Property>]
let ``H-098 monotone growth: touched axes grow across non-Reset sequences`` (indices: int list) =
    let nonReset =
        indices
        |> List.map opOfIndex
        |> List.filter (fun op -> match op with Reset -> false | _ -> true)
    let rec loop (policy: Policy) (prev: Set<ModelKey>) (ops: PolicyOp list) : bool =
        match ops with
        | [] -> true
        | op :: rest ->
            let next = apply op policy
            let nextTouched = touchedAxes next
            if Set.isSubset prev nextTouched then loop next nextTouched rest
            else false
    loop Policy.empty Set.empty nonReset

// ---------------------------------------------------------------------------
// H-098 FsCheck.Experimental.Machine variant (Cluster F follow-up).
//
// Object-oriented state-machine API. Same agreement property as the
// hand-rolled variant above; the operational advantage is automatic
// shrinking — when a generated trace fails, FsCheck searches for a
// minimum reproducer (fewest operations + simplest parameters).
//
// Actual: Policy (the production type under test).
// Model:  Map<ModelKey, bool> (the touched-axis bookkeeper).
//
// Each operation: extends both actual and model; the agreement
// post-condition (touchedAxes actual = modelTouched model) is the
// invariant checked after every step.
// ---------------------------------------------------------------------------

/// Mutable holder for the actual `Policy` value across operation
/// invocations. FsCheck.Experimental.Machine's `Check` method receives
/// the actual by reference; for immutable record types like `Policy`,
/// we wrap the value in a class with mutable state so each operation
/// can advance the actual forward by calling `Apply`.
///
/// This is the canonical FsCheck.Experimental shape for immutable
/// types: actual = stateful wrapper; model = immutable map updated by
/// `Run`. Without this wrapper, every `Check` invocation receives the
/// Setup-value actual and the property fails on any multi-step trace.
type private PolicyHolder() =
    let mutable value : Policy = Policy.empty
    member _.Value = value
    member _.Apply (op: PolicyOp) : unit = value <- apply op value
    member _.Reset () : unit = value <- Policy.empty

let private agreementProp (actual: Policy) (model: Map<ModelKey, bool>) : FsCheck.Property =
    let touchedActual = touchedAxes actual
    let touchedModel = modelTouched model
    (touchedActual = touchedModel)
    |@ sprintf "expected touched-axes %A (model), got %A (actual)" touchedModel touchedActual

type private SetSelectionOp() =
    inherit Operation<PolicyHolder, Map<ModelKey, bool>>()
    override _.Run model = Map.add (Axis Selection) true model
    override _.Check (holder, model) : FsCheck.Property =
        holder.Apply (SetSelection (IncludeOnly (Set.singleton customerKey)))
        agreementProp holder.Value model
    override _.ToString() = "SetSelection"

type private SetEmissionOp() =
    inherit Operation<PolicyHolder, Map<ModelKey, bool>>()
    override _.Run model = Map.add (Axis Emission) true model
    override _.Check (holder, model) : FsCheck.Property =
        holder.Apply (SetEmission EmissionPolicy.dataOnly)
        agreementProp holder.Value model
    override _.ToString() = "SetEmission"

type private SetInsertionOp() =
    inherit Operation<PolicyHolder, Map<ModelKey, bool>>()
    override _.Run model = Map.add (Axis Insertion) true model
    override _.Check (holder, model) : FsCheck.Property =
        holder.Apply (SetInsertion InsertNew)
        agreementProp holder.Value model
    override _.ToString() = "SetInsertion"

type private AddTighteningOp(label: string) =
    inherit Operation<PolicyHolder, Map<ModelKey, bool>>()
    override _.Run model = Map.add (Axis Tightening) true model
    override _.Check (holder, model) : FsCheck.Property =
        holder.Apply (AddTightening (nullabilityCfg label))
        agreementProp holder.Value model
    override _.ToString() = sprintf "AddTightening(%s)" label

type private SetUserMatchingOp() =
    inherit Operation<PolicyHolder, Map<ModelKey, bool>>()
    override _.Run model = Map.add UserMatchingKey true model
    override _.Check (holder, model) : FsCheck.Property =
        holder.Apply (SetUserMatching BySsKey)
        agreementProp holder.Value model
    override _.ToString() = "SetUserMatching"

type private ResetOp() =
    inherit Operation<PolicyHolder, Map<ModelKey, bool>>()
    override _.Run _ = Map.empty
    override _.Check (holder, model) : FsCheck.Property =
        holder.Reset()
        agreementProp holder.Value model
    override _.ToString() = "Reset"

let private opGen : Gen<Operation<PolicyHolder, Map<ModelKey, bool>>> =
    Gen.frequency
        [ 2, Gen.constant (SetSelectionOp()    :> Operation<_, _>)
          2, Gen.constant (SetEmissionOp()     :> Operation<_, _>)
          2, Gen.constant (SetInsertionOp()    :> Operation<_, _>)
          2, Gen.map (fun n -> AddTighteningOp(sprintf "fc-%d" n) :> Operation<_, _>) (Gen.choose (0, 1000))
          2, Gen.constant (SetUserMatchingOp() :> Operation<_, _>)
          1, Gen.constant (ResetOp()           :> Operation<_, _>) ]

type private PolicyMachine() =
    inherit Machine<PolicyHolder, Map<ModelKey, bool>>()
    override _.Setup =
        { new Setup<PolicyHolder, Map<ModelKey, bool>>() with
            override _.Actual() = PolicyHolder()
            override _.Model() = Map.empty }
        |> Gen.constant
        |> Arb.fromGen
    override _.Next _model = opGen

[<Property>]
let ``H-098 FsCheck.Experimental.Machine: model-impl agreement under generated traces (shrinks on failure)`` () =
    StateMachine.toProperty (PolicyMachine())

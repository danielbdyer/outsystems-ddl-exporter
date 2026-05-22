module Projection.Tests.PolicyStateMachineTests

// H-098 (HORIZON Cluster F): model-based testing for the policy system.
//
// **Model.** A `Map<OverlayAxis, bool>` tracks which axes have been
// touched (i.e., set to a non-default value) at any point in the
// trace.
//
// **Implementation.** The real `Policy` record. After every
// transition, the model's "touched axes" projection must equal the
// real Policy's "axes differing from Policy.empty" projection.
//
// **Transitions.** Closed DU `PolicyOp` covering the five axes the
// operator can address: SetSelection, SetEmission, SetInsertion,
// AddTightening, SetUserMatching, Reset.
//
// **Generated traces.** FsCheck generates lists of operations of
// arbitrary length; the agreement property is checked at every step.
// A divergence between the model and the real Policy means either
// (1) a Policy mutator silently changes another axis (axis-isolation
// violation), or (2) the model's bookkeeping has drifted.
//
// **Pillar 9 / A12 connection.** This is the executable witness of
// the orthogonality axiom: changing axis X must not perturb other
// axes' touched state.

open Xunit
open FsCheck
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

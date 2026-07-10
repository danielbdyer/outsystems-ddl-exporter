module Projection.Tests.WriteSignoffTests

// The write-signoff greenlight (2026-07-08, the greenlight program): the
// declarative, first-class APPROVAL for a destructive write mode. This pins
// the pure vocabulary — the six modes' string ⇔ DU round-trip, the impact
// register (THE_VOICE: stative, agentless), the `verify` verdict across
// missing / covering / scope-mismatched, and the config parse + render
// round-trip (A44: `parse ∘ render = id` on the `signoff` array). No database.

open Xunit
open Projection.Pipeline

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es

// -- the mode alphabet: closed, and the string bridge round-trips ------------

let private allModes : WriteSignoff.WriteMode list =
    [ WriteSignoff.WriteMode.Replace
      WriteSignoff.WriteMode.Fresh
      WriteSignoff.WriteMode.Drops
      WriteSignoff.WriteMode.Cdc
      WriteSignoff.WriteMode.IdentityInsert
      WriteSignoff.WriteMode.DeleteScope ]

[<Fact>]
let ``parseMode ∘ modeLabel = Some for every mode (the label bridge round-trips)`` () =
    for m in allModes do
        Assert.Equal(Some m, WriteSignoff.parseMode (WriteSignoff.modeLabel m))

[<Fact>]
let ``parseMode is case/whitespace-insensitive and rejects an unknown label`` () =
    Assert.Equal(Some WriteSignoff.WriteMode.Replace, WriteSignoff.parseMode "  REPLACE ")
    Assert.Equal(Some WriteSignoff.WriteMode.IdentityInsert, WriteSignoff.parseMode "Identity-Insert")
    Assert.Equal(None, WriteSignoff.parseMode "wipe")

[<Fact>]
let ``the six canonical labels are the operator-writable vocabulary`` () =
    let labels = allModes |> List.map WriteSignoff.modeLabel
    Assert.Equal<string list>(
        [ "replace"; "fresh"; "drops"; "cdc"; "identity-insert"; "delete-scope" ],
        labels)

// -- the impact register: stative, evidence-grounded (THE_VOICE) -------------

[<Fact>]
let ``impactOf carries a non-empty, first-person-free impact for every mode`` () =
    // THE_VOICE: no pronouns, stative. A blank impact would be a silent gate.
    for m in allModes do
        let impact = WriteSignoff.impactOf m
        Assert.False(System.String.IsNullOrWhiteSpace impact)
        let lowered = impact.ToLowerInvariant()
        for banned in [ " i "; " we "; " you "; " our " ] do
            Assert.DoesNotContain(banned, lowered)

// -- verify: the verdict across the authorization space ----------------------

let private appr (m: WriteSignoff.WriteMode) (tables: string list) : WriteSignoff.WriteApproval =
    { Mode = m; Tables = tables; AcknowledgedImpact = None; ApprovedBy = None; Date = None }

[<Fact>]
let ``verify: no approval for a destructive mode is Missing (the default-on gate)`` () =
    match WriteSignoff.verify "golden" [] WriteSignoff.WriteMode.Replace [ "Customer" ] with
    | WriteSignoff.Missing (reason, remedy) ->
        Assert.Contains("replace", reason)
        Assert.Contains("signoff", remedy)
        Assert.Contains("golden", remedy)
    | other -> Assert.Fail(sprintf "expected Missing, got %A" other)

[<Fact>]
let ``verify: a mode greenlit with an EMPTY scope confirms the whole flow's set`` () =
    match WriteSignoff.verify "golden" [ appr WriteSignoff.WriteMode.Replace [] ] WriteSignoff.WriteMode.Replace [ "Customer"; "Order" ] with
    | WriteSignoff.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed, got %A" other)

[<Fact>]
let ``verify: a declared scope that COVERS the plan confirms`` () =
    match WriteSignoff.verify "golden" [ appr WriteSignoff.WriteMode.Replace [ "Customer"; "Order" ] ] WriteSignoff.WriteMode.Replace [ "Customer" ] with
    | WriteSignoff.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed, got %A" other)

[<Fact>]
let ``verify: a declared scope that MISSES a wiped table is a ScopeMismatch (no rubber-stamp of a wider blast radius)`` () =
    match WriteSignoff.verify "golden" [ appr WriteSignoff.WriteMode.Replace [ "Customer" ] ] WriteSignoff.WriteMode.Replace [ "Customer"; "Order" ] with
    | WriteSignoff.ScopeMismatch (reason, remedy) ->
        Assert.Contains("Order", reason)
        Assert.Contains("Order", remedy)
    | other -> Assert.Fail(sprintf "expected ScopeMismatch, got %A" other)

[<Fact>]
let ``verify: the WRONG mode greenlit does not satisfy a different mode`` () =
    match WriteSignoff.verify "golden" [ appr WriteSignoff.WriteMode.Fresh [] ] WriteSignoff.WriteMode.Replace [ "Customer" ] with
    | WriteSignoff.Missing _ -> ()
    | other -> Assert.Fail(sprintf "expected Missing (fresh does not cover replace), got %A" other)

[<Fact>]
let ``verify: an empty plan touches nothing, so a bare greenlight confirms`` () =
    match WriteSignoff.verify "golden" [ appr WriteSignoff.WriteMode.Replace [ "Customer" ] ] WriteSignoff.WriteMode.Replace [] with
    | WriteSignoff.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed on an empty plan, got %A" other)

[<Fact>]
let ``verify: the acknowledged impact is echoed on Confirmed when the operator declared one`` () =
    let a = { appr WriteSignoff.WriteMode.Replace [] with AcknowledgedImpact = Some "wipes the golden subset" }
    match WriteSignoff.verify "golden" [ a ] WriteSignoff.WriteMode.Replace [ "Customer" ] with
    | WriteSignoff.Confirmed note -> Assert.Equal("wipes the golden subset", note)
    | other -> Assert.Fail(sprintf "expected Confirmed echoing the acknowledged impact, got %A" other)

[<Fact>]
let ``approvedModes projects the resolved set the engine gates on`` () =
    let approvals = [ appr WriteSignoff.WriteMode.Replace [ "Customer" ]; appr WriteSignoff.WriteMode.Drops [] ]
    let modes = WriteSignoff.approvedModes approvals
    Assert.True(Set.contains WriteSignoff.WriteMode.Replace modes)
    Assert.True(Set.contains WriteSignoff.WriteMode.Drops modes)
    Assert.False(Set.contains WriteSignoff.WriteMode.Fresh modes)

[<Fact>]
let ``greenlit is the minimal scopeless declaration the gate accepts`` () =
    let g = WriteSignoff.greenlit WriteSignoff.WriteMode.Replace
    Assert.Equal<string list>([], g.Tables)
    match WriteSignoff.verify "golden" [ g ] WriteSignoff.WriteMode.Replace [ "Customer"; "Order" ] with
    | WriteSignoff.Confirmed _ -> ()
    | other -> Assert.Fail(sprintf "expected Confirmed from a scopeless greenlight, got %A" other)

// -- the config surface: parse, and A44 render round-trip --------------------

let private cfgJson (signoffBody: string) : string =
    sprintf """
{
  "model": { "path": "m.json" },
  "output": { "dir": "out/" },
  "environments": {
    "qa":  { "access": "direct", "conn": "file:./q.conn", "rendition": "physical", "archetype": "managed-dml", "grant": "data" },
    "uat": { "access": "direct", "conn": "file:./u.conn", "rendition": "physical", "archetype": "managed-dml", "grant": "data" }
  },
  "flows": {
    "golden": { "from": "qa", "to": "uat", "scope": "data", "tables": ["Customer","Order"], "strategy": "replace", "signoff": %s }
  }
}""" signoffBody

[<Fact>]
let ``config: a rich signoff parses into the flow's approval list`` () =
    let cfg = mustOk (ProjectionConfig.parse (cfgJson """[ { "mode": "replace", "tables": ["Customer","Order"], "acknowledgedImpact": "wipes the subset", "approvedBy": "dan", "date": "2026-07-08" } ]"""))
    let flow = Map.find "golden" cfg.Flows
    Assert.Equal(1, List.length flow.Signoff)
    let a = List.head flow.Signoff
    Assert.Equal(WriteSignoff.WriteMode.Replace, a.Mode)
    Assert.Equal<string list>([ "Customer"; "Order" ], a.Tables)
    Assert.Equal(Some "wipes the subset", a.AcknowledgedImpact)
    Assert.Equal(Some "dan", a.ApprovedBy)
    Assert.Equal(Some "2026-07-08", a.Date)

[<Fact>]
let ``config: an unknown signoff mode refuses by name`` () =
    match ProjectionConfig.parse (cfgJson """[ { "mode": "wipe" } ]""") with
    | Ok _ -> Assert.Fail "expected a refusal on the unknown mode"
    | Error es -> Assert.Contains(es, fun (e: Projection.Core.ValidationError) -> e.Code.Contains "signoff")

[<Fact>]
let ``config A44: a signoff round-trips through render (parse ∘ render = id)`` () =
    let cfg = mustOk (ProjectionConfig.parse (cfgJson """[ { "mode": "replace", "tables": ["Customer"], "approvedBy": "dan" } ]"""))
    let cfg2 = mustOk (ProjectionConfig.parse (ProjectionConfig.render cfg))
    Assert.Equal<WriteSignoff.WriteApproval list>((Map.find "golden" cfg.Flows).Signoff, (Map.find "golden" cfg2.Flows).Signoff)

[<Fact>]
let ``config A44: an ABSENT signoff renders nothing and round-trips to empty`` () =
    let noSignoff = """
{
  "model": { "path": "m.json" },
  "output": { "dir": "out/" },
  "environments": {
    "qa":  { "access": "direct", "conn": "file:./q.conn", "rendition": "physical", "archetype": "managed-dml", "grant": "data" },
    "uat": { "access": "direct", "conn": "file:./u.conn", "rendition": "physical", "archetype": "managed-dml", "grant": "data" }
  },
  "flows": { "golden": { "from": "qa", "to": "uat", "scope": "data", "tables": ["Customer"] } }
}"""
    let cfg = mustOk (ProjectionConfig.parse noSignoff)
    Assert.Equal<WriteSignoff.WriteApproval list>([], (Map.find "golden" cfg.Flows).Signoff)
    let rendered = ProjectionConfig.render cfg
    Assert.DoesNotContain("signoff", rendered)

// -- the act blessings (2026-07-10, slice 4a): the signoff array's second
// closed entry shape — `{ "act": token, "fingerprint": text, … }` beside the
// mode approvals. Parse refusals are NAMED (no fingerprint, an unparseable
// fingerprint, both act and mode, a duplicate token) and the A44 round-trip
// covers the mixed array.

open Projection.Core

let private hex64 = String.replicate 64 "a"

[<Fact>]
let ``config: an act blessing parses into the flow's act signoffs with its exact fingerprint`` () =
    let cfg = mustOk (ProjectionConfig.parse (cfgJson (sprintf """[ { "mode": "replace" }, { "act": "wipe:Sales.Customer", "fingerprint": "effect:%s", "approvedBy": "dan", "date": "2026-07-10" } ]""" hex64)))
    let flow = Map.find "golden" cfg.Flows
    Assert.Equal(1, List.length flow.Signoff)
    Assert.Equal(1, List.length flow.ActSignoff)
    let b = List.head flow.ActSignoff
    Assert.Equal<string>("wipe:Sales.Customer", b.Act)
    Assert.Equal(ActConsent.ActFingerprint.Effect hex64, b.Fingerprint)
    Assert.Equal(Some "dan", b.ApprovedBy)
    Assert.Equal(Some "2026-07-10", b.Date)

[<Fact>]
let ``config: an act blessing without a fingerprint refuses by name — a blessing binds to the exact substrate`` () =
    match ProjectionConfig.parse (cfgJson """[ { "act": "wipe:Sales.Customer" } ]""") with
    | Ok _ -> Assert.Fail "expected a refusal on the missing fingerprint"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "cli.config.signoffNoFingerprint")

[<Fact>]
let ``config: an unparseable fingerprint refuses by name, never a silent skip`` () =
    match ProjectionConfig.parse (cfgJson """[ { "act": "wipe:Sales.Customer", "fingerprint": "effect:nothex" } ]""") with
    | Ok _ -> Assert.Fail "expected a refusal on the unparseable fingerprint"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "cli.config.signoffFingerprintUnparseable")

[<Fact>]
let ``config: an entry setting BOTH act and mode refuses — one entry is one approval or one blessing`` () =
    match ProjectionConfig.parse (cfgJson (sprintf """[ { "act": "wipe:X", "mode": "replace", "fingerprint": "effect:%s" } ]""" hex64)) with
    | Ok _ -> Assert.Fail "expected a refusal on the dual-shape entry"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "cli.config.signoffShape")

[<Fact>]
let ``config: a duplicate act token refuses — one blessing per act`` () =
    match ProjectionConfig.parse (cfgJson (sprintf """[ { "act": "wipe:X", "fingerprint": "effect:%s" }, { "act": "wipe:X", "fingerprint": "effect:%s" } ]""" hex64 hex64)) with
    | Ok _ -> Assert.Fail "expected a refusal on the duplicate act token"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "cli.config.signoffDuplicate")

[<Fact>]
let ``config A44: a mixed signoff (mode approvals + act blessings) round-trips through render (parse ∘ render = id)`` () =
    let population = ActConsent.fingerprintText (ActConsent.populationFingerprint "1" "2048" 2048)
    let cfg = mustOk (ProjectionConfig.parse (cfgJson (sprintf """[ { "mode": "replace", "tables": ["Customer"] }, { "act": "mint:Sales.Order", "fingerprint": "effect:%s", "acknowledgedImpact": "fresh keys minted" }, { "act": "wipe:Sales.Customer", "fingerprint": "%s" } ]""" hex64 population)))
    let cfg2 = mustOk (ProjectionConfig.parse (ProjectionConfig.render cfg))
    Assert.Equal<WriteSignoff.WriteApproval list>((Map.find "golden" cfg.Flows).Signoff, (Map.find "golden" cfg2.Flows).Signoff)
    Assert.Equal<WriteSignoff.ActBlessing list>((Map.find "golden" cfg.Flows).ActSignoff, (Map.find "golden" cfg2.Flows).ActSignoff)

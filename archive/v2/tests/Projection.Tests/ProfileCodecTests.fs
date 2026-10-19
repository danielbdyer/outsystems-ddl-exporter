module Projection.Tests.ProfileCodecTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Targets.Json

// THE_SYNTHETIC_DATA_DESIGN §10.2 — the durable Profile artifact's round-trip
// law. `∀ p. deserialize (serialize p) = Ok p` (the codec discipline's
// universal law over a constructed-valid generator) plus a totality example
// that populates every one of the 12 Profile axes, so a missed axis fails the
// round trip rather than dropping silently.

let private value (r: Result<'a>) : 'a = Result.value r
let private aKey (s: string) : SsKey = SsKey.synthesizedComposite "PC_ATTR" [ s ] |> value
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "PC_KIND" [ s ] |> value
let private rKey (s: string) : SsKey = SsKey.synthesizedComposite "PC_REF" [ s ] |> value

let private probe (n: int64) : ProbeStatus =
    ProbeStatus.create (DateTimeOffset(2026, 6, 8, 12, 30, 0, TimeSpan.Zero)) n Succeeded |> value

let private roundTrips (p: Profile) : bool =
    match ProfileCodec.deserialize (ProfileCodec.serialize p) with
    | Ok p' -> p' = p
    | Error _ -> false

// ---------------------------------------------------------------------------
// Example tests — concrete anchors that pinpoint which axis regressed.
// ---------------------------------------------------------------------------

[<Fact>]
let ``round-trip: the empty profile`` () =
    Assert.True(roundTrips Profile.empty)

[<Fact>]
let ``round-trip: every axis populated (totality)`` () =
    let p : Profile =
        { Columns =
            // c1 carries the max-observed-length axis (so the round trip exercises
            // it); c2 leaves it `None` (so the absent-axis case round-trips too).
            [ ColumnProfile.create (aKey "c1") 100L 5L (probe 100L) |> value
              |> ColumnProfile.withMaxObservedLength 128
              ColumnProfile.create (aKey "c2") 100L 0L (probe 100L) |> value ]
          UniqueCandidates =
            [ { UniqueCandidateProfile.create (aKey "c2") with HasDuplicate = true; ProbeStatus = probe 100L } ]
          CompositeUniqueCandidates =
            [ { (CompositeUniqueCandidateProfile.create (kKey "k1") [ aKey "c1"; aKey "c2" ]) with
                  HasDuplicate = false; ProbeStatus = probe 100L } ]
          ForeignKeys =
            [ { ForeignKeyReality.create (rKey "r1") with
                  HasOrphan = true; OrphanCount = 3L; IsNoCheck = true; ProbeStatus = probe 250L } ]
          Distributions =
            [ AttributeDistribution.Categorical
                (CategoricalDistribution.create (aKey "c2")
                    [ "Active", 70L; "Inactive", 30L ] 2L false (probe 100L) |> value)
              AttributeDistribution.Numeric
                ({ (NumericDistribution.create (aKey "c1") 0M 25M 50M 75M 95M 99M 100M 100L (probe 100L) |> value) with
                     Moments = Some (StatisticalMoments.create 48.5M 12.25M |> value) }) ]
          AttributeRealities =
            [ { AttributeReality.create (aKey "c1") with
                  IsNullableInDatabase = true; HasNulls = true; HasDuplicates = false
                  HasOrphans = false; IsPresentButInactive = true } ]
          ForeignKeyCardinalities =
            [ ForeignKeyCardinality.create (rKey "r1")
                (NumericDistribution.create (aKey "c1") 1M 1M 2M 4M 8M 9M 10M 50L (probe 50L) |> value) ]
          ForeignKeySelectivities =
            [ ForeignKeySelectivity.create (rKey "r1")
                [ "10", 40L; "11", 20L ] 2L false (probe 60L) |> value ]
          JointDistributions =
            [ JointDistribution.create (kKey "k1") [ aKey "c1"; aKey "c2" ]
                [ "10|Active", 25L; "11|Inactive", 15L ] 2L false (probe 40L) |> value ]
          CdcAwareness =
            CdcAwareness.create (Set.ofList [ kKey "k1" ]) (Map.ofList [ kKey "k1", "dbo_K1" ])
          SourceUsers =
            UserPopulation.create
              [ UserAttributes.create (SourceUserId.ofInt 7) (aKey "u1") (Some (Email.create "a@x" |> value)) ]
          TargetUsers =
            UserPopulation.create
              [ UserAttributes.create (TargetUserId.ofInt 18) (aKey "u1") None ] }
    match ProfileCodec.deserialize (ProfileCodec.serialize p) with
    | Ok p' -> Assert.Equal<Profile>(p, p')
    | Error es -> Assert.True(false, sprintf "round-trip failed: %A" es)

// ---------------------------------------------------------------------------
// The universal law over a constructed-valid generator (random nesting).
// ---------------------------------------------------------------------------

let rec private seqGen (gs: Gen<'a> list) : Gen<'a list> =
    match gs with
    | [] -> Gen.constant []
    | g :: rest -> gen { let! x = g in let! xs = seqGen rest in return x :: xs }

/// A constructed-valid `Profile` — validity is built in (null ≤ rows; numeric
/// percentiles monotonic by construction; categorical distinct-count agrees
/// with the frequency length), never generate-and-filtered.
let private genProfile : Gen<Profile> =
    gen {
        let! n = Gen.choose (0, 6)
        let keys = [ for i in 1 .. n -> aKey (string i) ]
        let! columns =
            keys
            |> List.map (fun k ->
                gen {
                    let! rc = Gen.choose (0, 5000)
                    let! nc = Gen.choose (0, rc)
                    return ColumnProfile.create k (int64 rc) (int64 nc) (probe (int64 rc)) |> value
                })
            |> seqGen
        let! dists =
            keys
            |> List.map (fun k ->
                Gen.oneof
                    [ // categorical (not truncated ⇒ distinctCount = length)
                      gen {
                          let! m = Gen.choose (0, 5)
                          let freqs = [ for j in 1 .. m -> (sprintf "v%d" j, int64 (j * 3)) ]
                          return
                              AttributeDistribution.Categorical
                                  (CategoricalDistribution.create k freqs (int64 m) false (probe 100L) |> value)
                      }
                      // numeric (percentiles monotonic by construction)
                      gen {
                          let! baseV = Gen.choose (0, 100)
                          let! step = Gen.choose (0, 20)
                          let d i = decimal (baseV + step * i)
                          return
                              AttributeDistribution.Numeric
                                  (NumericDistribution.create k (d 0) (d 1) (d 2) (d 3) (d 4) (d 5) (d 6) 100L (probe 100L)
                                   |> value)
                      } ])
            |> seqGen
        return { Profile.empty with Columns = columns; Distributions = dists }
    }

type ProfileArb =
    static member Profile() : Arbitrary<Profile> = Arb.fromGen genProfile

[<Property(Arbitrary = [| typeof<ProfileArb> |])>]
let ``law: deserialize (serialize p) = Ok p`` (p: Profile) : bool =
    roundTrips p

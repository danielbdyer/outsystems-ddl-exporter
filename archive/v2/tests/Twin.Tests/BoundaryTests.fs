module Twin.Tests.BoundaryTests

open Xunit

// ---------------------------------------------------------------------------
// THE_TWIN.md §architecture — the context boundary, executable.
//
// The Twin's reference arrows are one-way: Twin.Core touches only
// Projection.Core; Twin.Runtime touches only the named kernel projects;
// nothing in Projection.* references Twin.*. The kernel manifest in
// THE_TWIN.md restates these arrows in prose — this test is what keeps the
// prose honest.
// ---------------------------------------------------------------------------

let private referencedLocalNames (t: System.Type) : Set<string> =
    t.Assembly.GetReferencedAssemblies()
    |> Array.map (fun a -> a.Name |> Option.ofObj |> Option.defaultValue "")
    |> Array.filter (fun n -> n.StartsWith "Projection" || n.StartsWith "Twin")
    |> Set.ofArray

[<Fact>]
let ``boundary: Twin.Core references Projection.Core and nothing else local`` () =
    let refs = referencedLocalNames typeof<Twin.Core.TableCoordinate>
    Assert.Equal<Set<string>>(Set.ofList [ "Projection.Core" ], refs)

[<Fact>]
let ``boundary: Twin.Runtime stays inside the kernel manifest`` () =
    let allowed =
        Set.ofList
            [ "Twin.Core"
              "Projection.Core"
              "Projection.Adapters.Sql"
              "Projection.Pipeline"
              "Projection.Targets.SSDT"
              "Projection.Targets.Json"
              "Projection.Targets.Data" ]
    let refs = referencedLocalNames typeof<Twin.Runtime.TwinContainer.ContainerState>
    Assert.True(Set.isSubset refs allowed,
                sprintf "Twin.Runtime references outside the kernel manifest: %A" (Set.difference refs allowed))

[<Fact>]
let ``boundary: no Projection assembly references Twin`` () =
    // Every kernel assembly the Twin consumes, checked at its own
    // reference list: none may point back at Twin.*. The set under test
    // is exactly Twin.Runtime's kernel references, loaded by name.
    let kernelNames =
        typeof<Twin.Runtime.TwinContainer.ContainerState>.Assembly.GetReferencedAssemblies()
        |> Array.filter (fun a -> (a.Name |> Option.ofObj |> Option.defaultValue "").StartsWith "Projection")
    Assert.NotEmpty kernelNames
    for name in kernelNames do
        let assembly = System.Reflection.Assembly.Load name
        let back =
            assembly.GetReferencedAssemblies()
            |> Array.filter (fun a -> (a.Name |> Option.ofObj |> Option.defaultValue "").StartsWith "Twin")
        // A kernel assembly referencing Twin.* would be a boundary defect.
        Assert.Empty back

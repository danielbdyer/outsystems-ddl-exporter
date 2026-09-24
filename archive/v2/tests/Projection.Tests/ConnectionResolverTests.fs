module Projection.Tests.ConnectionResolverTests

open System
open System.IO
open Xunit
open Projection.Core
open Projection.Adapters.Sql

// D9 resolution of a Source/Sink `ConnectionRef` to a concrete connection
// string: read the named env var or file at the operator-controlled
// boundary; surface missing / empty content as typed `ValidationError`s
// so the `transfer` CLI verb maps them to its connection-config exit code.

let private withEnv (name: string) (value: string option) (work: unit -> unit) : unit =
    let prior = Environment.GetEnvironmentVariable name
    try
        match value with
        | Some v -> Environment.SetEnvironmentVariable(name, v)
        | None   -> Environment.SetEnvironmentVariable(name, null)
        work ()
    finally
        Environment.SetEnvironmentVariable(name, prior)

let private withTempFile (contents: string) (work: string -> unit) : unit =
    let path = Path.Combine(Path.GetTempPath(), "projection-conn-test-" + Guid.NewGuid().ToString("N") + ".txt")
    try
        File.WriteAllText(path, contents)
        work path
    finally
        if File.Exists path then File.Delete path

let private uniqueEnvName () =
    "PROJECTION_TEST_CONN_VAR_" + Guid.NewGuid().ToString("N")

[<Fact>]
let ``ConnectionResolver.resolve EnvVar reads the named environment variable`` () =
    let name = uniqueEnvName ()
    withEnv name (Some "Server=localhost;Database=X") (fun () ->
        match ConnectionResolver.resolve "Test" (ConnectionRef.EnvVar name) with
        | Ok s    -> Assert.Equal("Server=localhost;Database=X", s)
        | Error es -> Assert.Fail(sprintf "expected Ok; got %A" es))

[<Fact>]
let ``ConnectionResolver.resolve EnvVar surfaces refMissing when the variable is unset`` () =
    let name = uniqueEnvName ()
    match ConnectionResolver.resolve "Test" (ConnectionRef.EnvVar name) with
    | Ok s    -> Assert.Fail(sprintf "expected Error; got Ok %s" s)
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.refMissing")

[<Fact>]
let ``ConnectionResolver.resolve EnvVar surfaces refEmpty when the variable is blank`` () =
    let name = uniqueEnvName ()
    withEnv name (Some "   ") (fun () ->
        match ConnectionResolver.resolve "Test" (ConnectionRef.EnvVar name) with
        | Ok s    -> Assert.Fail(sprintf "expected Error; got Ok %s" s)
        | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.refEmpty"))

[<Fact>]
let ``ConnectionResolver.resolve File reads the file content (trimmed)`` () =
    withTempFile "  Server=localhost;Database=X  \n" (fun path ->
        match ConnectionResolver.resolve "Test" (ConnectionRef.File path) with
        | Ok s    -> Assert.Equal("Server=localhost;Database=X", s)
        | Error es -> Assert.Fail(sprintf "expected Ok; got %A" es))

[<Fact>]
let ``ConnectionResolver.resolve File surfaces refMissing when the path doesn't exist`` () =
    let path = Path.Combine(Path.GetTempPath(), "projection-conn-test-missing-" + Guid.NewGuid().ToString("N") + ".txt")
    match ConnectionResolver.resolve "Test" (ConnectionRef.File path) with
    | Ok s    -> Assert.Fail(sprintf "expected Error; got Ok %s" s)
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.refMissing")

[<Fact>]
let ``ConnectionResolver.resolve File surfaces refEmpty when the file is blank`` () =
    withTempFile "   \n  " (fun path ->
        match ConnectionResolver.resolve "Test" (ConnectionRef.File path) with
        | Ok s    -> Assert.Fail(sprintf "expected Error; got Ok %s" s)
        | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.refEmpty"))

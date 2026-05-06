module Projection.Tests.ResultTests

open System
open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.ResultOperators

// ---------------------------------------------------------------------------
// ValidationError construction
// ---------------------------------------------------------------------------

[<Fact>]
let ``ValidationError.create requires non-blank code`` () =
    Assert.Throws<ArgumentException>(fun () ->
        ValidationError.create "" "msg" |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () ->
        ValidationError.create "   " "msg" |> ignore) |> ignore

[<Fact>]
let ``ValidationError.create requires non-blank message`` () =
    Assert.Throws<ArgumentException>(fun () ->
        ValidationError.create "code" "" |> ignore) |> ignore

[<Fact>]
let ``ValidationError.withMetadata adds an entry`` () =
    let e =
        ValidationError.create "k.x" "msg"
        |> ValidationError.withMetadata "key" (Some "value")
    Assert.Equal(Some (Some "value"), Map.tryFind "key" e.Metadata)

[<Fact>]
let ``ValidationError.withMetadata with blank key is a no-op`` () =
    let original = ValidationError.create "k.x" "msg"
    let mutated = original |> ValidationError.withMetadata "" (Some "v")
    Assert.Equal(original, mutated)

// ---------------------------------------------------------------------------
// Result construction
// ---------------------------------------------------------------------------

[<Fact>]
let ``Result.failure rejects empty error list`` () =
    Assert.Throws<ArgumentException>(fun () ->
        Result.failure [] |> ignore) |> ignore

[<Fact>]
let ``Result.success and Result.failureOf are inverses of isSuccess`` () =
    Assert.True(Result.isSuccess (Result.success 7))
    Assert.True(Result.isFailure (Result.failureOf (ValidationError.create "x.y" "msg")))

// ---------------------------------------------------------------------------
// Monad laws
//
// For any (presumed-pure) function f and value x:
//   left identity   : bind f (Success x)  =  f x
//   right identity  : bind Success r      =  r
//   associativity   : bind g (bind f r)   =  bind (fun x -> bind g (f x)) r
//
// FsCheck samples ints; the laws hold for any type because Result is the
// Either monad.
// ---------------------------------------------------------------------------

let private sampleErr =
    ValidationError.create "monad.law.fail" "test failure"

[<Property>]
let ``Result monad: left identity`` (x: int) =
    let f y = Result.success (y + 1)
    Result.bind f (Result.success x) = f x

[<Property>]
let ``Result monad: right identity (Success path)`` (x: int) =
    Result.bind Result.success (Result.success x) = Result.success x

[<Property>]
let ``Result monad: right identity (Failure path)`` () =
    let r : Result<int> = Result.failureOf sampleErr
    Result.bind Result.success r = r

[<Property>]
let ``Result monad: associativity`` (x: int) =
    let f y = Result.success (y + 1)
    let g y = Result.success (y * 2)
    Result.bind g (Result.bind f (Result.success x))
        = Result.bind (fun y -> Result.bind g (f y)) (Result.success x)

// ---------------------------------------------------------------------------
// Functor laws
//   identity     : map id r = r
//   composition  : map (g << f) r = map g (map f r)
// ---------------------------------------------------------------------------

[<Property>]
let ``Result functor: identity`` (x: int) =
    Result.map id (Result.success x) = Result.success x

[<Property>]
let ``Result functor: composition (Success)`` (x: int) =
    let f (y: int) = y + 3
    let g (y: int) = y * 5
    Result.map (g << f) (Result.success x) = Result.map g (Result.map f (Result.success x))

[<Property>]
let ``Result functor: composition (Failure)`` () =
    let r : Result<int> = Result.failureOf sampleErr
    let f (y: int) = y + 3
    let g (y: int) = y * 5
    Result.map (g << f) r = Result.map g (Result.map f r)

// ---------------------------------------------------------------------------
// Short-circuit semantics — failures pass through bind/map untouched.
// ---------------------------------------------------------------------------

[<Fact>]
let ``bind on Failure does not invoke the continuation`` () =
    let mutable invoked = false
    let f x =
        invoked <- true
        Result.success (x + 1)
    let r : Result<int> = Result.failureOf sampleErr
    let _ = Result.bind f r
    Assert.False(invoked)

[<Fact>]
let ``map on Failure does not invoke the function`` () =
    let mutable invoked = false
    let f x =
        invoked <- true
        x + 1
    let r : Result<int> = Result.failureOf sampleErr
    let _ = Result.map f r
    Assert.False(invoked)

[<Fact>]
let ``bind preserves the original error list`` () =
    let e1 = ValidationError.create "a.b" "first"
    let e2 = ValidationError.create "c.d" "second"
    let r : Result<int> = Result.failure [e1; e2]
    match Result.bind (fun x -> Result.success (x + 1)) r with
    | Failure es -> Assert.Equal<ValidationError list>([e1; e2], es)
    | Success _  -> Assert.Fail("Expected Failure to pass through")

// ---------------------------------------------------------------------------
// Ensure
// ---------------------------------------------------------------------------

[<Fact>]
let ``ensure on Success+true is a passthrough`` () =
    let r = Result.success 5
    let result = r |> Result.ensure (fun x -> x > 0) sampleErr
    Assert.Equal(Result.success 5, result)

[<Fact>]
let ``ensure on Success+false converts to Failure`` () =
    let r = Result.success 5
    let result = r |> Result.ensure (fun x -> x < 0) sampleErr
    Assert.Equal<Result<int>>(Result.failureOf sampleErr, result)

[<Fact>]
let ``ensure on Failure is a passthrough`` () =
    let original : Result<int> = Result.failureOf sampleErr
    let result = original |> Result.ensure (fun x -> x > 0) sampleErr
    Assert.Equal<Result<int>>(original, result)

// ---------------------------------------------------------------------------
// Collect — short-circuits on first failure (matches trunk Collect).
// ---------------------------------------------------------------------------

[<Fact>]
let ``collect of all-Success returns Success of list in order`` () =
    let inputs = [ Result.success 1; Result.success 2; Result.success 3 ]
    Assert.Equal<Result<int list>>(Result.success [1; 2; 3], Result.collect inputs)

[<Fact>]
let ``collect short-circuits on first failure`` () =
    let e = ValidationError.create "boom" "first failure"
    let inputs =
        [ Result.success 1
          Result.failureOf e
          Result.success 2 ]
    Assert.Equal<Result<int list>>(Result.failureOf e, Result.collect inputs)

[<Fact>]
let ``collect of empty input is Success of empty list`` () =
    Assert.Equal<Result<int list>>(Result.success [], Result.collect Seq.empty)

// ---------------------------------------------------------------------------
// Operator surface — sanity check that >>= matches Result.bind.
// ---------------------------------------------------------------------------

[<Property>]
let ``operator >>= equals Result.bind`` (x: int) =
    let f y = Result.success (y + 10)
    (Result.success x >>= f) = Result.bind f (Result.success x)

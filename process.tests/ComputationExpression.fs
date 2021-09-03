module Process.Tests.ComputationExpression

open System
open Xunit
open FsUnit
open FsUnit.Xunit
open Process

[<Fact>]
let ``CE can return`` () =
  let p =
    proc {
      return 1
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = 1, []

  actual |> should equal expected

[<Fact>]
let ``CE can be nested`` () =
  let p =
    proc {
      yield "a"

      let! x =
        proc {
          yield "b"
          return 1
        }

      yield "c"

      return x + 1
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = 2, [ "a"; "b"; "c" ]

  actual |> should equal expected

[<Fact>]
let ``CE supports if ... then`` () =
  let p =
    proc {
      if true
      then
        yield "a"

      yield "b"

      if false
      then
        yield "nope"

      yield "c"
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = (), [ "a"; "b"; "c" ]

  actual |> should equal expected

[<Fact>]
let ``CE supports for loops`` () =
  let p =
    proc {
      for x in [ "a"; "b"; "c" ] do
        yield x
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = (), [ "a"; "b"; "c" ]

  actual |> should equal expected

[<Fact>]
let ``CE implements zero`` () =
  let p =
    proc {
      ()
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = (), []

  actual |> should equal expected

[<Fact>]
let ``CE supports while loops`` () =
  let p =
    proc {
      let mutable i = 0

      while i < 2 do
        yield i
        i <- i + 1
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = (), [ 0; 1; 2 ]

  actual |> should equal expected

[<Fact>]
let ``CE supports use`` () =
  let mutable isDisposed = 0

  let p =
    proc {
      use r =
        {
          new IDisposable with
            member this.Dispose() =
              isDisposed <- isDisposed + 1
        }

      yield "a"

      return 1
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = 1, [ "a" ]

  actual |> should equal expected
  isDisposed |> should equal 1

[<Fact>]
let ``CE supports use in case of exceptions`` () =
  let mutable isDisposed = 0

  let p =
    proc {
      use r =
        {
          new IDisposable with
            member this.Dispose() =
              isDisposed <- isDisposed + 1
        }

      failwith "nope"

      return 1
    }

  try
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously
    |> ignore
  with _ ->
    ()

  isDisposed |> should equal 1

[<Fact>]
let ``CE supports try ... with`` () =
  let p =
    proc {
      yield "a"

      try
        yield "b"
        failwith "nope"
      with _ ->
        yield "c"

      return 1
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = 1, [ "a"; "b"; "c" ]

  actual |> should equal expected

[<Fact>]
let ``CE supports try ... finally`` () =
  let mutable hitFinally = 0

  let p =
    proc {
      yield "a"

      try
        yield "b"
        failwith "nope"
      finally
        hitFinally <- hitFinally + 1
        ()

      yield "c"

      return 1
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = 1, [ "a"; "b"; "c" ]

  actual |> should equal expected
  hitFinally |> should equal 1

[<Fact>]
let ``CE supports let! ... and! ...`` () =
  let p =
    proc {
      yield "a"

      let! x =
        proc {
          yield "b"

          return 4
        }
      and! y =
        proc {
          yield "c"

          return 3
        }

      yield "d"

      return x - y
    }

  let actual =
    p
    |> Process.toAsyncWithProgress
    |> Async.RunSynchronously

  let expected = 1, [ "a"; "b"; "c"; "d" ]

  actual |> should equal expected

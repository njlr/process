namespace Process

open System

type IProcess<'result, 'progress> =
  abstract member Start : (('result -> unit) * ('progress -> unit) * (Exception -> unit) * (OperationCanceledException -> unit)) -> unit

module Process =

  let fromContinuations (continuations : (('result -> unit) * ('progress -> unit) * (Exception -> unit) * (OperationCanceledException -> unit)) -> unit) =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let resolve, update, reject, cancel = args

          try
            continuations (resolve, update, reject, cancel)
          with exn ->
            reject exn
    }

  let just x =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let resolve, _, _, _ = args
          resolve x
    }

  let progess p =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let resolve, update, _, _ = args
          update p
          resolve ()
    }

  let error exn =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let _, _, reject, _ = args
          reject exn
    }

  let bind (f : 't -> IProcess<'u, _>) (m : IProcess<'t, _>) =
    fromContinuations
      (fun args ->
        let resolve, update, reject, cancel = args

        let mResolve =
          fun x ->
            let n = f x
            n.Start(resolve, update, reject, cancel)

        m.Start(mResolve, update, reject, cancel))

  let map (f : 't -> 'u) (m : IProcess<'t, _>) : IProcess<'u, _> =
    fromContinuations
      (fun args ->
        let resolve, update, reject, cancel = args
        m.Start(f >> resolve, update, reject, cancel))

  let zip (a : IProcess<'t, 'progress>) (b : IProcess<'u, 'progress>) : IProcess<'t * 'u, 'progress> =
    fromContinuations
      (fun args ->
        let resolve, update, reject, cancel = args

        let mutable resultA = ValueNone
        let mutable resultB = ValueNone

        let mutable isRejected = false
        let mutable isCancelled = false

        let resolveA =
          (fun x ->
            if resultA = ValueNone
            then
              resultA <- ValueSome x

              match resultB with
              | ValueSome y ->
                resolve (x, y)
              | ValueNone -> ())

        let resolveB =
          (fun y ->
            if resultB = ValueNone
            then
              resultB <- ValueSome y

              match resultA with
              | ValueSome x ->
                resolve (x, y)
              | ValueNone -> ())

        let cancel =
          (fun exn ->
            if not isCancelled && not isRejected
            then
              isCancelled <- true
              cancel exn)

        let reject =
          (fun exn ->
            if not isCancelled && not isRejected
            then
              isRejected <- true
              reject exn)

        a.Start(resolveA, update, reject, cancel)
        b.Start(resolveB, update, reject, cancel))

  let toAsync (p : IProcess<'t, _>) =
    Async.FromContinuations (fun (resolve, reject, cancel) ->
      p.Start(resolve, ignore, reject, cancel))

  let toAsyncWithProgress (p : IProcess<'t, _>) =
    Async.FromContinuations (fun (resolve, reject, cancel) ->
      let progress = System.Collections.Generic.List<_>()

      let onProgress x =
        progress.Add(x)

      let onDone x =
        resolve (x, Seq.toList progress)

      p.Start(onDone, onProgress, reject, cancel))

  let ofAsync x =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let resolve, update, reject, cancel = args

          Async.StartWithContinuations(x, resolve, reject, cancel)
    }

  let ofAsyncWithProgress f =
    fromContinuations (fun (resolve, update, reject, cancel) ->
      let w = f update
      Async.StartWithContinuations(w, resolve, reject, cancel))

  let ignoreProgress (m : IProcess<'t, 'u>) =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let resolve, _, reject, cancel = args

          m.Start(resolve, ignore, reject, cancel)
    }

  let cancel () =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let _, _, _, cancel = args

          cancel (OperationCanceledException())
    }

  open System.Threading

  let runSynchronously update (m : IProcess<'result, 'update>) =
    use w = new ManualResetEvent(false)

    let mutable outcome = Unchecked.defaultof<_>

    let resolve =
      (fun x ->
        outcome <- Ok x
        w.Set() |> ignore)

    let reject =
      (fun exn ->
        outcome <- Error exn
        w.Set() |> ignore)

    let cancel =
      (fun exn ->
        outcome <- Error exn
        w.Set() |> ignore)

    m.Start(resolve, update, reject, cancel)

    w.WaitOne() |> ignore

    match outcome with
    | Ok x -> x
    | Error exn -> raise exn

  let ignore (m : IProcess<'t, 'u>) =
    {
      new IProcess<_, _> with
        member this.Start(args) =
          let resolve, update, reject, cancel = args

          m.Start((fun _ -> resolve ()), update, reject, cancel)
    }

[<AutoOpen>]
module ComputationExpression =

  let private tryWith (f : unit -> IProcess<_, _>) (handler : Exception -> IProcess<_, _>) : IProcess<_, _> =
    Process.fromContinuations
      (fun (resolve, update, reject, cancel) ->
        let m = f ()

        let mReject =
          (fun exn ->
            let n = handler exn

            n.Start(resolve, update, reject, cancel))

        m.Start(resolve, update, mReject, cancel))

  let private tryFinally (f : unit -> IProcess<_, _>) (compensation : unit -> unit) : IProcess<_, _> =
    let handler =
      Process.fromContinuations (fun (resolve, _, _, _) ->
        compensation ()
        resolve ())

    tryWith f (fun _ -> handler)

  let private using (resource : 't) (expr : 't -> IProcess<'u, _>) : IProcess<'u, _> when 't :> IDisposable =
    Process.fromContinuations
      (fun (resolve, update, reject, cancel) ->
        use resource = resource // Take ownership

        let m = expr resource

        m.Start(resolve, update, reject, cancel))

  type ProcessBuilder() =
    member this.Bind(m, f) =
      Process.bind f m

    member this.BindReturn(m : IProcess<'u, 'progress>, f) : IProcess<'t, _> =
      Process.map f m

    member this.MergeSources(a, b) =
      Process.zip a b

    member this.Delay(f) : unit -> IProcess<_, _> =
      f

    member this.Run(f) : IProcess<_, _> =
      f ()

    member this.Combine(a, b) =
      Process.bind (fun () -> b) a

    member this.Combine(a, f : unit -> _) =
      Process.bind f a

    member this.For(xs, f) =
      xs
      |> Seq.map f
      |> Seq.fold
        (fun acc x -> Process.bind (fun () -> x) acc)
        (Process.just ())

    member this.While(guard, body) =
      let rec loop m =
        if guard ()
        then
          Process.bind (fun () -> loop (body ())) m
        else
          m

      loop (Process.just ())

    // Delayed<'T> * (exn -> M<'T>) -> M<'T>
    member this.TryWith(f : unit -> IProcess<_, _>, handler : Exception -> IProcess<_, _>) : IProcess<_, _> =
      tryWith f handler

    // Delayed<'T> * (unit -> unit) -> M<'T>
    member this.TryFinally(f, compensation : unit -> unit) : IProcess<_, _> =
      tryFinally f compensation

    // 'T * ('T -> M<'U>) -> M<'U> when 'T :> IDisposable
    member this.Using(resource : 't, expr : 't -> IProcess<'u, _>) : IProcess<'u, _> when 't :> IDisposable =
      using resource expr

    member this.Yield(x) =
      Process.progess x

    member this.Return(x) =
      Process.just x

    member this.ReturnFrom(m) : IProcess<_, _> =
      m

    member this.Zero() =
      Process.just ()

  let proc = ProcessBuilder()

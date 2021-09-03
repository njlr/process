open System
open Process

let sleep seconds =
  Process.ofAsync (Async.Sleep (TimeSpan.FromSeconds (float seconds)))

[<EntryPoint>]
let main argv =

  proc {
    for i in 0..9 do
      yield i
      do! sleep 1
  }
  |> Process.runSynchronously (printfn "%A")

  0

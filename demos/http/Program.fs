open System
open System.Net
open Process

let download (url : string) =
  Process.fromContinuations (fun (resolve, update, reject, cancel) ->
    use webClient = new WebClient()

    webClient.DownloadProgressChanged.Add(update)

    webClient.DownloadStringCompleted.Add
      (fun result ->
        if result.Cancelled
        then
          let exn =
            if isNull result.Error
            then
              OperationCanceledException()
            else
              OperationCanceledException("String download cancelled", result.Error)

          cancel exn
        else
          if isNull result.Error
          then
            resolve result.Result
          else
            reject result.Error)

    webClient.DownloadStringAsync(Uri url))

[<EntryPoint>]
let main argv =

  download "https://example.org"
  |> Process.runSynchronously (fun e -> printfn $"{e.ProgressPercentage}%%")
  |> printfn "%A"

  0

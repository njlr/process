# process

A _process_ is an observable computation that reports progress updates and then a result value.

Some examples of processes:

 * File transfer with number of bytes transfered
 * Bash script execution with lines of standard output

Most CLI apps can be modelled using processes.

To construct a process that results in `1`, do:

```fsharp
Process.just 1
```

To run a process, do:

```fsharp
Process.runSyncronously (fun progress -> ()) p
```

To construct a process from continuation functions, do:

```fsharp
Process.fromContinuations (fun (resolve, update, reject, cancel) -> (
  // Send progress updates
  update 0.1
  update 0.3
  update 0.7
  update 1.0

  // Complete the process
  resolve "success"
))
```

You can combine processes using the `proc` computation expression:

```fsharp
proc {
  // Report progress with yield
  yield "a"

  // Bind results from sub processes
  let! x = proc {
    // Internal updates are passed through
    yield "b"

    return 1
  }

  yield "c"

  return x + 1
}
|> Process.runSyncronously ignore
```

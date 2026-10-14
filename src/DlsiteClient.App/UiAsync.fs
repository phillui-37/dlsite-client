module DlsiteClient.App.UiAsync

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Threading

/// Marshal an action onto the Avalonia UI thread.
let postUi (action: unit -> unit) =
    if Dispatcher.UIThread.CheckAccess() then action ()
    else Dispatcher.UIThread.Post(action)

/// Run synchronous work on a thread-pool thread; deliver result/error on the UI thread.
let run (work: unit -> 'T) (onOk: 'T -> unit) (onErr: exn -> unit) : unit =
    ignore (
        Task.Run(fun () ->
            try
                let r = work ()
                postUi (fun () -> onOk r)
            with ex ->
                postUi (fun () -> onErr ex)))

/// Run an async Task off the caller's context; deliver result/error on the UI thread.
/// Prefer this over `.GetResult()` / `.Wait()` from views.
let runAsync (work: unit -> Task<'T>) (onOk: 'T -> unit) (onErr: exn -> unit) : unit =
    ignore (
        task {
            try
                let! r = work ()
                postUi (fun () -> onOk r)
            with ex ->
                postUi (fun () -> onErr ex)
        })

/// Fire-and-forget Task with UI-thread error handler.
let runAsyncUnit (work: unit -> Task) (onOk: unit -> unit) (onErr: exn -> unit) : unit =
    ignore (
        task {
            try
                do! work ()
                postUi onOk
            with ex ->
                postUi (fun () -> onErr ex)
        })

/// Like runAsyncUnit but forces the body onto a thread-pool thread (safe for sync/disk/network).
/// Do **not** use for WebAuthenticationBroker / UI-owned APIs.
let runPoolAsyncUnit (work: unit -> Task) (onOk: unit -> unit) (onErr: exn -> unit) : unit =
    ignore (
        Task.Run(fun () ->
            try
                work().GetAwaiter().GetResult()
                postUi onOk
            with ex ->
                postUi (fun () -> onErr ex)))

/// Debounce UI→settings writes (e.g. text boxes) without blocking.
let debounce (ms: int) =
    let mutable cts: CancellationTokenSource option = None
    fun (action: unit -> unit) ->
        match cts with
        | Some c -> c.Cancel(); c.Dispose()
        | None -> ()
        let n = new CancellationTokenSource()
        cts <- Some n
        ignore (
            task {
                try
                    do! Task.Delay(ms, n.Token)
                    postUi action
                with :? OperationCanceledException -> ()
            })

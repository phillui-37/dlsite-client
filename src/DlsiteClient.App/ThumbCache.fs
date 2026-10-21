module DlsiteClient.App.ThumbCache

open System
open System.Collections.Concurrent
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.App.UiAsync

type ThumbStatus =
    | Idle
    | Loading
    | Ready of Bitmap
    | Failed

let private cache = ConcurrentDictionary<string, ThumbStatus>(StringComparer.Ordinal)
let private inFlight = ConcurrentDictionary<string, byte>(StringComparer.Ordinal)
let private http =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds 12.0
    c

/// Subscribers notified on the UI thread when any thumb transitions.
let private listeners = ConcurrentDictionary<int, Action>()
let private nextId = ref 0

let subscribe (handler: Action) : IDisposable =
    let id = Interlocked.Increment nextId
    listeners[id] <- handler
    { new IDisposable with
        member _.Dispose() = listeners.TryRemove(id) |> ignore }

let private notify () =
    postUi (fun () ->
        for kv in listeners do
            try kv.Value.Invoke() with _ -> ())

let statusOf (url: string) =
    if String.IsNullOrWhiteSpace url then Failed
    else
        match cache.TryGetValue url with
        | true, s -> s
        | false, _ -> Idle

/// Non-blocking peek — never starts network IO.
let tryPeek (url: string) : Bitmap option =
    match statusOf url with
    | Ready bmp -> Some bmp
    | _ -> None

let private download (url: string) =
    task {
        try
            use! stream = http.GetStreamAsync(url)
            use ms = new MemoryStream()
            do! stream.CopyToAsync(ms)
            ms.Position <- 0L
            let bmp = new Bitmap(ms)
            cache[url] <- Ready bmp
        with _ ->
            cache[url] <- Failed
        inFlight.TryRemove(url) |> ignore
        notify ()
    }

/// Drop a cached failure so the next <see cref="request"/> fetches again.
let invalidate (url: string) =
    if not (String.IsNullOrWhiteSpace url) then cache.TryRemove url |> ignore

/// Request a thumb; starts background fetch at most once. Safe on UI thread.
let request (url: string) : unit =
    if String.IsNullOrWhiteSpace url then ()
    else
        match cache.TryGetValue url with
        | true, Ready _
        | true, Failed
        | true, Loading -> ()
        | _ ->
            if inFlight.TryAdd(url, 0uy) then
                cache[url] <- Loading
                ignore (download url)

let private skeleton (w: float) (h: float) : IView =
    Border.create [
        Border.width w
        Border.height h
        Border.background "#E5E7EB"
        Border.cornerRadius 4.0
        Border.child (
            TextBlock.create [
                TextBlock.text "…"
                TextBlock.foreground "#9CA3AF"
                TextBlock.horizontalAlignment HorizontalAlignment.Center
                TextBlock.verticalAlignment VerticalAlignment.Center
            ])
    ]
    :> IView

/// Placeholder / skeleton / image. Always calls <see cref="request"/> for misses.
let thumbView (url: string) (w: float) (h: float) : IView =
    request url
    match statusOf url with
    | Ready bmp ->
        Image.create [
            Image.source bmp
            Image.width w
            Image.height h
            Image.stretch Stretch.UniformToFill
        ]
        :> IView
    | Loading
    | Idle -> skeleton w h
    | Failed ->
        Border.create [
            Border.width w
            Border.height h
            Border.background "#F3F4F6"
            Border.cornerRadius 4.0
            Border.onPointerPressed (fun e ->
                e.Handled <- true
                invalidate url)
            Border.child (
                TextBlock.create [
                    TextBlock.text "!"
                    TextBlock.foreground "#9CA3AF"
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ])
        ]
        :> IView

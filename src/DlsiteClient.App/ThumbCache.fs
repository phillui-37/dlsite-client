module DlsiteClient.App.ThumbCache

open System
open System.Collections.Concurrent
open System.IO
open System.Net.Http
open Avalonia.Media.Imaging

/// Best-effort URL → Bitmap cache (UI may call sync; failures become None).
let private cache = ConcurrentDictionary<string, Bitmap option>()
let private http =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds 8.0
    c

let tryGet (url: string) : Bitmap option =
    if String.IsNullOrWhiteSpace url then None
    else
        match cache.TryGetValue url with
        | true, bmp -> bmp
        | false, _ ->
            try
                use stream = http.GetStreamAsync(url).GetAwaiter().GetResult()
                use ms = new MemoryStream()
                stream.CopyTo ms
                ms.Position <- 0L
                let bmp = new Bitmap(ms)
                cache[url] <- Some bmp
                Some bmp
            with _ ->
                cache[url] <- None
                None

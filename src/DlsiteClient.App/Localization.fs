module DlsiteClient.App.Localization

open System
open System.Collections.Generic
open System.IO
open System.Text.Json

type I18n() =
    let mutable map = Dictionary<string, string>(StringComparer.Ordinal)
    let mutable locale = "ja"

    member _.Locale = locale

    member _.Load(localeCode: string, localesDir: string) =
        locale <- localeCode
        let file =
            match localeCode with
            | "zh-TW" | "zh_TW" | "zh-Hant" -> "zh-TW.json"
            | _ -> "ja.json"
        let path = Path.Combine(localesDir, file)
        map.Clear()
        if File.Exists path then
            use doc = JsonDocument.Parse(File.ReadAllText path)
            for prop in doc.RootElement.EnumerateObject() do
                let v = prop.Value.GetString()
                map[prop.Name] <- if isNull v then "" else v
        else
            System.Console.Error.WriteLine("Locale file missing: " + path)

    member _.T(key: string) =
        match map.TryGetValue key with
        | true, v -> v
        | _ -> key

    member this.Tf(key: string, [<ParamArray>] args: obj[]) =
        let fmt = this.T key
        try String.Format(fmt, args) with _ -> fmt

let mutable globalI18n = I18n()

let t key = globalI18n.T key
let errorText (ex: exn) =
    match ex.Message with
    | "secret-service-unavailable" -> t "auth.secretServiceUnavailable"
    | "steam-runtime-missing" -> t "launcher.steamRuntimeMissing"
    | message -> message
let tf key ([<ParamArray>] args) = globalI18n.Tf(key, args)

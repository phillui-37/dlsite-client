module DlsiteClient.App.OAuthLogin

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open DlsiteClient.Core.Api
open DlsiteClient.App.Services

let private mainWindow () : Window option =
    match Application.Current with
    | null -> None
    | app ->
        match app.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            match desktop.MainWindow with
            | null -> None
            | w -> Some w
        | _ -> None

/// Nest OIDC via Avalonia WebAuthenticationBroker (MIT WebView >= 11.4.0).
/// Nest redirect is https://www.dlsite.com/home/login (fragment) — needs NativeWebDialog / WebView2, not Browser loopback.
/// Caller must not block the UI thread; Program.main must be STA for WebView2.
let loginAsync (svc: AppServices) : Task<string> =
    task {
        match mainWindow () with
        | None -> return "no main window"
        | Some owner ->
            let lang =
                let ui = svc.Settings.Current.UiLocale
                if ui = "zh-TW" then "zh_TW" else "ja"
            let request = NestOidc.BuildAuthorizeUri(lang)
            let options =
                WebAuthenticatorOptions(
                    request,
                    NestOidc.RedirectUri,
                    Mode = WebAuthenticatorMode.NativeWebDialog)
            try
                let! result = WebAuthenticationBroker.AuthenticateAsync(owner, options)
                match Option.ofObj result.CallbackUri with
                | None -> return "cancelled"
                | Some callback ->
                    let mutable idToken = ""
                    let mutable state = ""
                    let mutable sessionState = ""
                    if NestOidc.TryParseCallback(callback, &idToken, &state, &sessionState) then
                        do! svc.Auth.LoginWithOidcAsync(idToken, state, sessionState)
                        return "ok"
                    else
                        return "bad callback"
            with ex ->
                return Localization.errorText ex
    }

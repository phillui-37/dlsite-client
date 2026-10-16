namespace DlsiteClient.App

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI.Hosts
open Material.Colors
open Material.Styles.Themes
open Material.Styles.Themes.Base
open DlsiteClient.App.Localization
#if WINDOWS
open DlsiteClient.App.NestImportPrompt
#endif
open DlsiteClient.App.Services
open DlsiteClient.App.Views

type MainWindow() as this =
    inherit HostWindow()
    do
        base.Title <- t "app.title"
        base.Width <- 1440.0
        base.MinWidth <- 960.0
        base.MinHeight <- 640.0
        base.Height <- 800.0
        this.Content <- MainView.view ()

type App() =
    inherit Application()

    override this.Initialize() =
        let theme = MaterialTheme(null)
        theme.BaseTheme <- BaseThemeMode.Light
        theme.PrimaryColor <- PrimaryColor.Blue
        theme.SecondaryColor <- SecondaryColor.Teal
        this.Styles.Add(theme)
        this.RequestedThemeVariant <- Styling.ThemeVariant.Light

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow()
#if WINDOWS
            maybeAsk (get ())
#endif
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

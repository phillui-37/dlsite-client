namespace DlsiteClient.App

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open DlsiteClient.App.Localization
open DlsiteClient.App.Views

type MainWindow() as this =
    inherit HostWindow()
    do
        base.Title <- t "app.title"
        base.Width <- 1200.0
        base.Height <- 800.0
        this.Content <- MainView.view ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Light

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow()
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

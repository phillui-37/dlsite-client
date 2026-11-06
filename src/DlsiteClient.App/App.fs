namespace DlsiteClient.App

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI.Hosts
open Avalonia.Themes.Simple
open Avalonia.Media
open System
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
        this.Styles.Add(SimpleTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Light
        for key, color in [
            "PaperBrush", Palette.Paper; "SurfaceBrush", Palette.Surface
            "InkBrush", Palette.Ink; "InkMutedBrush", Palette.InkMuted
            "CharcoalBrush", Palette.Charcoal; "CharcoalSelectedBrush", Palette.CharcoalSelected
            "RailTextBrush", Palette.RailText; "ClayBrush", Palette.Clay
            "ClayHoverBrush", Palette.ClayHover; "ClayContainerBrush", Palette.ClayContainer
            "MossBrush", Palette.Moss; "MossContainerBrush", Palette.MossContainer
            "ControlOutlineBrush", Palette.OutlineStrong; "FocusBrush", Palette.Focus
            "HoverBrush", Palette.Hover; "PressedBrush", Palette.Pressed
            "ThemeBackgroundBrush", Palette.Paper; "ThemeForegroundBrush", Palette.Ink
            "ThemeBorderLowBrush", Palette.OutlineStrong; "ThemeBorderMidBrush", Palette.Clay
            "ThemeBorderHighBrush", Palette.Focus; "ThemeControlLowBrush", Palette.Surface
            "ThemeControlMidBrush", Palette.Surface; "ThemeControlHighBrush", Palette.Pressed
            "ThemeControlVeryHighBrush", Palette.ClayContainer
            "ThemeAccentBrush", Palette.Clay; "ThemeAccentBrush2", Palette.Moss
            "HighlightBrush", Palette.Clay; "HighlightBrush2", Palette.ClayContainer
            "ThemeControlHighlightLowBrush", Palette.MossContainer
            "ThemeControlHighlightMidBrush", Palette.ClayContainer
            "ThemeControlHighlightHighBrush", Palette.Clay
        ] do
            this.Resources.[key] <- SolidColorBrush(Color.Parse color)
        this.Styles.Add WabiSabi.styles

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow()
#if WINDOWS
            maybeAsk (get ())
#endif
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

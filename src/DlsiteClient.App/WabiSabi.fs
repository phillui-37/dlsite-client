namespace DlsiteClient.App

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Presenters
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Input
open Avalonia.Media
open Avalonia.Styling

/// The application's WabiSabi control styles, expressed as native Avalonia styles.
module WabiSabi =
    let private brush (color: string) = SolidColorBrush(Color.Parse color)

    let private ofType<'T when 'T :> StyledElement> () = Selectors.OfType<'T>(null)
    let private classSelector selector name = Selectors.Class(selector, name)
    let private controlState<'T when 'T :> StyledElement> (className: string option) pseudoClass =
        let selector = ofType<'T> ()
        let selector = className |> Option.fold classSelector selector
        classSelector selector pseudoClass

    let private templatePart<'T when 'T :> StyledElement> className pseudoClass =
        controlState<'T> className pseudoClass
        |> Selectors.Template
        |> Selectors.OfType<ContentPresenter>
        |> fun selector -> Selectors.Name(selector, "PART_ContentPresenter")

    let private style selector setters =
        let result = Style()
        result.Selector <- selector
        for setter in setters do
            result.Setters.Add(setter)
        result

    let private background: AvaloniaProperty = TemplatedControl.BackgroundProperty
    let private foreground: AvaloniaProperty = TemplatedControl.ForegroundProperty
    let private borderBrush: AvaloniaProperty = TemplatedControl.BorderBrushProperty
    let private borderThickness: AvaloniaProperty = TemplatedControl.BorderThicknessProperty
    let private cornerRadius: AvaloniaProperty = TemplatedControl.CornerRadiusProperty
    let private padding: AvaloniaProperty = TemplatedControl.PaddingProperty
    let private minHeight: AvaloniaProperty = Layoutable.MinHeightProperty
    let private margin: AvaloniaProperty = Layoutable.MarginProperty
    let private boxShadow: AvaloniaProperty = ContentPresenter.BoxShadowProperty

    let styles =
        let result = Styles()
        [ style (ofType<Window> ()) [ Setter(background, box (brush Palette.Paper)); Setter(foreground, box (brush Palette.Ink)); Setter(TemplatedControl.FontSizeProperty, box 14.0) ]
          style (ofType<TextBlock> ()) [ Setter(TextBlock.ForegroundProperty, box (brush Palette.Ink)) ]
          style (ofType<Button> ()) [ Setter(background, box (brush Palette.Surface)); Setter(foreground, box (brush Palette.Ink)); Setter(borderBrush, box (brush Palette.OutlineStrong)); Setter(borderThickness, box (Thickness(1.0))); Setter(cornerRadius, box (CornerRadius(3.0))); Setter(padding, box (Thickness(12.0, 8.0))); Setter(minHeight, box 36.0); Setter(TemplatedControl.CursorProperty, box (Cursor(StandardCursorType.Hand))) ]
          style (templatePart<Button> None ":pointerover") [ Setter(ContentPresenter.BackgroundProperty, box (brush Palette.Hover)); Setter(ContentPresenter.BorderBrushProperty, box (brush Palette.Clay)); Setter(ContentPresenter.ForegroundProperty, box (brush Palette.Ink)) ]
          style (templatePart<Button> None ":pressed") [ Setter(ContentPresenter.BackgroundProperty, box (brush Palette.Pressed)); Setter(ContentPresenter.BorderBrushProperty, box (brush Palette.Clay)); Setter(ContentPresenter.ForegroundProperty, box (brush Palette.Ink)) ]
          style (classSelector (ofType<Button> ()) "table-heading") [ Setter(borderThickness, box (Thickness(0.0))) ]
          style (classSelector (ofType<Button> ()) "primary") [ Setter(background, box (brush Palette.Clay)); Setter(foreground, box (brush Palette.Surface)); Setter(borderBrush, box (brush Palette.Clay)) ]
          style (templatePart<Button> (Some "primary") ":pointerover") [ Setter(ContentPresenter.BackgroundProperty, box (brush Palette.ClayHover)); Setter(ContentPresenter.ForegroundProperty, box (brush Palette.Surface)) ]
          style (templatePart<Button> (Some "primary") ":pressed") [ Setter(ContentPresenter.BackgroundProperty, box (brush Palette.Charcoal)); Setter(ContentPresenter.ForegroundProperty, box (brush Palette.Surface)) ]
          style (templatePart<Button> (Some "nav") ":pointerover") [ Setter(ContentPresenter.BackgroundProperty, box (brush Palette.CharcoalSelected)); Setter(ContentPresenter.ForegroundProperty, box (brush Palette.RailText)); Setter(ContentPresenter.BorderBrushProperty, box (brush Palette.RailText)) ]
          style (templatePart<Button> (Some "nav") ":pressed") [ Setter(ContentPresenter.BackgroundProperty, box (brush Palette.Charcoal)); Setter(ContentPresenter.ForegroundProperty, box (brush Palette.RailText)) ]
          style (templatePart<Button> None ":focus-visible") [ Setter(ContentPresenter.BorderBrushProperty, box (brush Palette.Focus)); Setter(boxShadow, box (BoxShadows.Parse("0 0 0 2 #65472F"))) ]
          style (templatePart<Button> (Some "nav") ":focus-visible") [ Setter(ContentPresenter.BorderBrushProperty, box (brush Palette.RailText)); Setter(boxShadow, box (BoxShadows.Parse("0 0 0 2 #F5F1E8"))) ]
          style (Selectors.OfType<Button>(Selectors.Child(classSelector (ofType<WrapPanel> ()) "actions"))) [ Setter(margin, box (Thickness(0.0, 0.0, 8.0, 8.0))) ]
          style (ofType<TextBox> ()) [ Setter(background, box (brush Palette.Surface)); Setter(foreground, box (brush Palette.Ink)); Setter(borderBrush, box (brush Palette.OutlineStrong)); Setter(cornerRadius, box (CornerRadius(3.0))); Setter(padding, box (Thickness(10.0, 8.0))); Setter(minHeight, box 38.0); Setter(TextBox.SelectionBrushProperty, box (brush Palette.ClayContainer)); Setter(TextBox.SelectionForegroundBrushProperty, box (brush Palette.Ink)) ]
          style (controlState<TextBox> None ":focus") [ Setter(borderBrush, box (brush Palette.Focus)) ]
          style (ofType<ComboBox> ()) [ Setter(background, box (brush Palette.Surface)); Setter(foreground, box (brush Palette.Ink)); Setter(borderBrush, box (brush Palette.OutlineStrong)); Setter(cornerRadius, box (CornerRadius(3.0))); Setter(padding, box (Thickness(10.0, 7.0))); Setter(minHeight, box 36.0) ]
          style (ofType<CheckBox> ()) [ Setter(foreground, box (brush Palette.Ink)); Setter(minHeight, box 36.0); Setter(margin, box (Thickness(0.0, 0.0, 12.0, 4.0))) ]
          style (ofType<ProgressBar> ()) [ Setter(foreground, box (brush Palette.Moss)); Setter(background, box (brush Palette.MossContainer)) ]
          style (controlState<Border> (Some "work") ":focus-visible") [ Setter(Border.BorderBrushProperty, box (brush Palette.Focus)); Setter(Border.BoxShadowProperty, box (BoxShadows.Parse("0 0 0 2 #65472F"))) ] ]
        |> List.iter result.Add
        result

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Takao.Host.MewUI;

/// <summary>
/// Application-wide Fluent glass control styles.  Page surfaces are composed
/// from the same tokens in <see cref="ShellComponents"/>; this sheet supplies
/// the missing glass chrome for controls created by every page and dialog.
/// </summary>
internal static class GlassTheme
{
    public static StyleSheet CreateStyleSheet()
    {
        var sheet = new StyleSheet();
        sheet.With<Button>(ControlStyle<Button>());
        sheet.With<TextBox>(ControlStyle<TextBox>());
        sheet.With<ComboBox>(ControlStyle<ComboBox>());
        sheet.With<CheckBox>(ControlStyle<CheckBox>());
        // ComboBox creates its ListBox in a popup and assigns the built-in
        // named style, so override that name in the same application sheet.
        sheet.With(BuiltInStyles.ComboBoxPopup, () => Style.DeriveFromDefault<ListBox>(
            setters:
            [
                Setter.Create(Control.BackgroundProperty, (Theme theme) => GlassMaterial.Surface(theme, ShellTokens.SurfaceAlpha)),
                Setter.Create(Control.BorderBrushProperty, (Theme theme) => Glass(theme.Palette.ControlBorder, ShellTokens.BorderAlpha)),
                Setter.Create(Control.BorderThicknessProperty, 1d),
                Setter.Create(Control.CornerRadiusProperty, ShellTokens.PanelRadius),
            ]));
        sheet.With<ContextMenu>(ControlStyle<ContextMenu>());
        sheet.Freeze();
        return sheet;
    }

    private static Style ControlStyle<T>() where T : Control
        => Style.DeriveFromDefault<T>(
            setters:
            [
                Setter.Create(Control.BackgroundProperty, (Theme theme) => GlassMaterial.Surface(theme, ShellTokens.ControlAlpha)),
                Setter.Create(Control.BorderBrushProperty, (Theme theme) => Glass(theme.Palette.ControlBorder, ShellTokens.BorderAlpha)),
                Setter.Create(Control.BorderThicknessProperty, 1d),
                Setter.Create(Control.CornerRadiusProperty, ShellTokens.ControlRadius),
                Setter.Create(Control.PaddingProperty, new Thickness(10, 6)),
            ],
            triggers:
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, (Theme theme) => GlassMaterial.Surface(theme, ShellTokens.ControlHotAlpha)),
                        Setter.Create(Control.BorderBrushProperty, (Theme theme) => Glass(theme.Palette.Accent, 220)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, (Theme theme) => theme.IsDark
                            ? Glass(theme.Palette.Accent, ShellTokens.ControlPressedAlpha)
                            : Color.FromArgb(ShellTokens.LightControlPressedAlpha, 230, 247, 255)),
                        Setter.Create(Control.BorderBrushProperty, (Theme theme) => Glass(theme.Palette.Accent, 235)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters =
                    [Setter.Create(Control.BorderBrushProperty, (Theme theme) => Glass(theme.Palette.Accent, 235))],
                },
                new StateTrigger
                {
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, (Theme theme) => GlassMaterial.Surface(theme, ShellTokens.ControlDisabledAlpha)),
                        Setter.Create(UIElement.OpacityProperty, 0.68),
                    ],
                },
            ]);

    private static Color Glass(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);
}

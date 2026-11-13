using Aprillz.MewUI;

namespace Takao.Host.MewUI;

/// <summary>
/// Fluent acrylic colors used by the shell and every page surface. Light mode
/// uses a translucent white lens over a cool ambient tint; dark mode keeps the
/// platform palette while still allowing the DWM backdrop through.
/// </summary>
internal static class GlassMaterial
{
    public static Color Surface(Theme theme, byte darkAlpha)
    {
        if (theme.IsDark)
            return theme.Palette.ControlBackground.WithAlpha(darkAlpha);

        return Color.FromArgb(LightAlpha(darkAlpha), 250, 252, 255);
    }

    public static Color SelectedSurface(Theme theme, byte darkAlpha)
        => theme.IsDark
            ? theme.Palette.ControlBackground.WithAlpha(darkAlpha)
            : Color.FromArgb(ShellTokens.LightSelectedSurfaceAlpha, 255, 255, 255);

    public static Color Title(Theme theme)
        => theme.IsDark
            ? Color.FromArgb(ShellTokens.GlassTitleAlpha, 30, 34, 42)
            : Color.FromArgb(ShellTokens.LightTitleAlpha, 220, 237, 246);

    public static Color Sidebar(Theme theme)
        => theme.IsDark
            ? Color.FromArgb(ShellTokens.GlassSidebarAlpha, 30, 34, 42)
            : Color.FromArgb(ShellTokens.LightSidebarAlpha, 203, 228, 242);

    public static Color ContentTint(Theme theme)
        => theme.IsDark
            ? Color.FromArgb(18, 7, 12, 20)
            : Color.FromArgb(64, 192, 223, 239);

    public static Color AmbientPrimary(Theme theme)
        => theme.IsDark
            ? Color.FromArgb(18, 35, 89, 122)
            : Color.FromArgb(24, 119, 195, 232);

    public static Color AmbientSecondary(Theme theme)
        => theme.IsDark
            ? Color.FromArgb(16, 92, 53, 122)
            : Color.FromArgb(18, 157, 214, 191);

    public static Color Border(Theme theme)
        => theme.IsDark
            ? theme.Palette.ControlBorder.WithAlpha(ShellTokens.BorderAlpha)
            : Color.FromArgb(170, 255, 255, 255);

    private static byte LightAlpha(byte darkAlpha)
        => darkAlpha switch
        {
            ShellTokens.SurfaceAlpha => ShellTokens.LightSurfaceAlpha,
            ShellTokens.SelectedSurfaceAlpha => ShellTokens.LightSelectedSurfaceAlpha,
            ShellTokens.ControlAlpha => ShellTokens.LightControlAlpha,
            ShellTokens.ControlHotAlpha => ShellTokens.LightControlHotAlpha,
            ShellTokens.ControlPressedAlpha => ShellTokens.LightControlPressedAlpha,
            _ => (byte)Math.Clamp(darkAlpha - 28, 64, 220),
        };
}

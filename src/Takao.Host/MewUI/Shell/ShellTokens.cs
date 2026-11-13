namespace Takao.Host.MewUI;

/// <summary>
/// Shared geometry for the native task-manager shell. Page content should use
/// these values through <see cref="ShellComponents"/> instead of introducing
/// another shell spacing scale.
/// </summary>
internal static class ShellTokens
{
    public const double TitleBarHeight = 48;
    public const double SidebarWidth = 204;
    public const double SidebarCompactWidth = 64;
    public const double NavigationItemHeight = 44;
    public const double CommandHeight = 44;
    public const double TitleBarSummaryGap = 12;
    public const double PanelRadius = 8;
    public const double ControlRadius = 4;

    // Shared Fluent glass geometry.  Keep all pages on the same rhythm so
    // reference screenshots do not drift as page-specific content changes.
    public const double PagePadding = 16;
    public const double ContentGap = 16;
    public const double CardGap = 12;
    // Acrylic surfaces should let the DWM backdrop read through.  Keep the
    // content layer below 70% opacity so cards, controls and the sidebar share
    // one material instead of rendering as opaque dark rectangles.
    public const byte GlassTitleAlpha = 188;
    public const byte GlassSidebarAlpha = 170;
    public const byte SurfaceAlpha = 166;
    public const byte SelectedSurfaceAlpha = 210;
    public const byte ControlAlpha = 142;
    public const byte ControlHotAlpha = 182;
    public const byte ControlPressedAlpha = 204;
    public const byte ControlDisabledAlpha = 84;
    public const byte BorderAlpha = 178;

    // Light acrylic needs a lower white overlay than dark acrylic. These
    // values keep the backdrop tint visible through every page surface.
    public const byte LightSurfaceAlpha = 132;
    public const byte LightSelectedSurfaceAlpha = 178;
    public const byte LightControlAlpha = 116;
    public const byte LightControlHotAlpha = 152;
    public const byte LightControlPressedAlpha = 180;
    public const byte LightTitleAlpha = 156;
    public const byte LightSidebarAlpha = 138;
}

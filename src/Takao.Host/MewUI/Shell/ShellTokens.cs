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
    public const double PageHeaderHeight = 56;
    public const double PanelRadius = 8;
    public const double ControlRadius = 4;
}

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Takao.Host.MewUI;

/// <summary>
/// Shared native shell primitives. These helpers only build layout surfaces;
/// page data, actions and state remain owned by <see cref="TaskManagerView"/>.
/// </summary>
internal static class ShellComponents
{
    public static Grid WindowShell(FrameworkElement titleBar, FrameworkElement sidebar, FrameworkElement content)
    {
        var root = new Grid()
            .Rows($"{ShellTokens.TitleBarHeight}, *")
            .Columns($"{ShellTokens.SidebarWidth}, *");
        root.Children(titleBar.ColumnSpan(2), sidebar.Row(1), content.Row(1).Column(1));
        return root;
    }

    public static FrameworkElement TitleBar(
        FrameworkElement brand,
        FrameworkElement search,
        FrameworkElement summary,
        FrameworkElement dragRegion,
        FrameworkElement windowButtons)
    {
        return new Border()
            // Keep the content inset on the left while letting the custom
            // window controls meet the native window edge like Win11 chrome.
            .Padding(new Thickness(16, 0, 0, 0))
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .WithTheme((theme, border) =>
            {
                border.Background = Color.FromArgb(ShellTokens.GlassTitleAlpha,
                    theme.IsDark ? (byte)30 : (byte)224,
                    theme.IsDark ? (byte)34 : (byte)241,
                    theme.IsDark ? (byte)42 : (byte)252);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new Grid().Columns("204, *, Auto, Auto").Children(
                brand,
                // Keep the drag target beside the input instead of spanning
                // over it. TextBox hit testing and keyboard focus then remain
                // completely independent from window dragging.
                new Grid().Columns("Auto, *").Children(
                    search,
                    dragRegion.Column(1)).Column(1),
                summary.Column(2),
                windowButtons.Column(3)));
    }

    public static FrameworkElement Sidebar(FrameworkElement navigation, FrameworkElement footer)
    {
        return new Border()
            .Padding(8)
            .BorderThickness(new Thickness(0, 0, 1, 0))
            .WithTheme((theme, border) =>
            {
                border.Background = Color.FromArgb(ShellTokens.GlassSidebarAlpha,
                    theme.IsDark ? (byte)30 : (byte)219,
                    theme.IsDark ? (byte)34 : (byte)237,
                    theme.IsDark ? (byte)42 : (byte)250);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new DockPanel().Spacing(8).Children(footer.DockBottom(), navigation));
    }

    public static FrameworkElement PageHeader(FrameworkElement title)
    {
        return new Border()
            .Padding(16, 0)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .WithTheme((theme, border) => border.BorderBrush = theme.Palette.ControlBorder)
            .Child(title);
    }

    public static FrameworkElement DataSurface(FrameworkElement content)
    {
        return new Border()
            .BorderThickness(1)
            .CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                // Keep data surfaces translucent so the window backdrop remains
                // visible while preserving a readable contrast layer.
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(content);
    }

    public static FrameworkElement CommandBar(params FrameworkElement[] commands)
    {
        return new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(commands);
    }
}

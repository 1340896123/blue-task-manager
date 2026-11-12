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
            .Padding(16, 0)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .WithTheme((theme, border) =>
            {
                border.Background = Color.FromArgb(theme.IsDark ? (byte)222 : (byte)232,
                    theme.Palette.WindowBackground.R,
                    theme.Palette.WindowBackground.G,
                    theme.Palette.WindowBackground.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new Grid().Columns("204, Auto, Auto, *, Auto").Children(
                brand,
                search.Column(1),
                summary.Column(2),
                dragRegion.Column(3),
                windowButtons.Column(4)));
    }

    public static FrameworkElement Sidebar(FrameworkElement navigation, FrameworkElement footer)
    {
        return new Border()
            .Padding(8)
            .BorderThickness(new Thickness(0, 0, 1, 0))
            .WithTheme((theme, border) =>
            {
                border.Background = Color.FromArgb(theme.IsDark ? (byte)222 : (byte)232,
                    theme.Palette.WindowBackground.R,
                    theme.Palette.WindowBackground.G,
                    theme.Palette.WindowBackground.B);
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
                border.Background = theme.Palette.ControlBackground;
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

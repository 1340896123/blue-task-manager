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

        // Put two low-alpha color blooms behind the shell. They are ambient
        // light sources rather than decorative content, so translucent cards
        // keep a visible acrylic read even over a flat desktop wallpaper.
        var ambientPrimary = new Ellipse { Width = 1400, Height = 900, IsHitTestVisible = false };
        var ambientSecondary = new Ellipse { Width = 1200, Height = 900, IsHitTestVisible = false };
        Canvas.SetLeft(ambientPrimary, -230);
        Canvas.SetTop(ambientPrimary, -330);
        Canvas.SetLeft(ambientSecondary, -100);
        Canvas.SetTop(ambientSecondary, 250);
        var ambient = new Canvas { IsHitTestVisible = false }
            .Children(ambientPrimary, ambientSecondary)
            .WithTheme((theme, _) =>
            {
                ambientPrimary.Fill = GlassMaterial.AmbientPrimary(theme);
                ambientSecondary.Fill = GlassMaterial.AmbientSecondary(theme);
            });

        return new Grid().Children(ambient, root);
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
                border.Background = GlassMaterial.Title(theme);
                border.BorderBrush = GlassMaterial.Border(theme);
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
                border.Background = GlassMaterial.Sidebar(theme);
                border.BorderBrush = GlassMaterial.Border(theme);
            })
            .Child(new DockPanel().Spacing(8).Children(footer.DockBottom(), navigation));
    }

    public static FrameworkElement DataSurface(FrameworkElement content)
    {
        var surface = new Border()
            .BorderThickness(1)
            .CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                // Keep data surfaces translucent so the window backdrop remains
                // visible while preserving a readable contrast layer.
                border.Background = GlassMaterial.Surface(theme, ShellTokens.SurfaceAlpha);
                border.BorderBrush = GlassMaterial.Border(theme);
            })
            .Child(content);
        return Shadowed(surface);
    }

    public static FrameworkElement Shadowed(FrameworkElement content, double radius = ShellTokens.PanelRadius)
        => new ShadowDecorator()
            .BlurRadius(8)
            .OffsetY(2)
            .ShadowColor(Color.FromArgb(34, 24, 46, 64))
            .CornerRadius(radius)
            .Child(content);

    public static FrameworkElement CommandBar(params FrameworkElement[] commands)
    {
        return new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(commands);
    }
}

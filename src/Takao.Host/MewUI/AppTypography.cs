using Aprillz.MewUI;

namespace Takao.Host.MewUI;

/// <summary>
/// Loads the bundled HarmonyOS Sans SC face before the first MewUI theme is
/// created. The font stays app-local; it is never installed into Windows.
/// </summary>
internal static class AppTypography
{
    internal const string FontRelativePath = "fonts/HarmonyOS_Sans_SC_Regular.ttf";

    internal static FontResource Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FontRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Takao 字体文件缺失：{FontRelativePath}", path);

        using var stream = File.OpenRead(path);
        return FontResources.Register(stream, ".ttf", "HarmonyOS Sans SC");
    }

    internal static void Apply(FontResource font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ThemeManager.DefaultMetrics = ThemeManager.DefaultMetrics with { FontFamily = font.FontFamily };
    }
}

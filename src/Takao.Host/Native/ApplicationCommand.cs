using Windows.Win32;

namespace Takao.Host.Native;

internal static class ApplicationCommand
{
    // Resolve only an observed Windows command; never execute it. Quoted paths and the
    // documented CreateProcess unquoted-prefix order are both used by Run/SCM values.
    internal static string? Executable(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.Length is 0 or > 32768) return null;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? Existing(text[1..end]) : null;
        }
        var candidates = 0;
        for (var end = 0; end <= text.Length && candidates < 32; end++)
        {
            if (end != text.Length && !char.IsWhiteSpace(text[end])) continue;
            if (end == 0) continue;
            candidates++;
            var found = Existing(text[..end]);
            if (found is not null) return found;
        }
        return null;
    }

    private static string? Existing(string value)
    {
        if (value.Length is 0 or > 32768) return null;
        // SCM uses the Win32 \SystemRoot convention as well as environment expansion.
        if (value.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), value[12..]);
        if (value.StartsWith(@"\??\", StringComparison.Ordinal)) value = value[4..];
        if (Path.IsPathFullyQualified(value))
        {
            if (File.Exists(value)) return Path.GetFullPath(value);
            if (!Path.HasExtension(value) && File.Exists(value + ".exe")) return Path.GetFullPath(value + ".exe");
            return null;
        }
        // SearchPath follows the normal Windows executable search, without a shell.
        // Candidates with embedded whitespace cannot be a bare executable name.
        if (value.Any(char.IsWhiteSpace) || value.Contains('"') || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar)) return null;
        var path = new char[32768];
        var count = PInvoke.SearchPath(null, value, ".exe", path, out _);
        return count > 0 && count < path.Length && File.Exists(new string(path, 0, (int)count)) ? new string(path, 0, (int)count) : null;
    }
}

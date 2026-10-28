using Takao.Host.Models;

namespace Takao.Host.Native;

internal static class StartupLocationResolver
{
    internal static StartupDto Describe(StartupDto entry)
    {
        string? executable = null;
        try
        {
            if (Path.IsPathFullyQualified(entry.Id) && File.Exists(entry.Id)) executable = Path.GetFullPath(entry.Id);
            else
            {
                var command = Environment.ExpandEnvironmentVariables(entry.Command.Trim());
                if (command.StartsWith('"')) { var end = command.IndexOf('"', 1); if (end > 1) executable = command[1..end]; }
                else { var end = command.IndexOfAny([' ', '\t']); executable = end < 0 ? command : command[..end]; }
                if (executable is null || !Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable)) executable = null;
                else executable = Path.GetFullPath(executable);
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { executable = null; }
        return entry with { LocationPath = executable, CanOpenLocation = executable is not null, LocationReason = executable is null ? "无法可靠定位为现存绝对exe路径或Startup来源文件；请复制命令与注册表来源核查，命令不会被执行。" : null };
    }
}

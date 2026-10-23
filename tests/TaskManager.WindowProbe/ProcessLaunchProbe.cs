using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Windows.Win32;
using Windows.Win32.System.Threading;

internal static class ProcessLaunchProbe
{
    internal static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "process-launch") return false;
        unsafe
        {
            Span<char> command = (args[2] + '\0').ToCharArray();
            var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
            var success = PInvoke.CreateProcess(args[1] == "-" ? null : args[1], ref command, null, null, false,
                PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT, null, null, startup, out var process);
            var error = success ? 0 : Marshal.GetLastPInvokeError();
            if (!success) { Console.WriteLine(JsonConvert.SerializeObject(new { success = false, nativeErrorCode = error })); return true; }
            try
            {
                using var handle = new SafeFileHandle((nint)process.hProcess.Value, ownsHandle: false);
                if (!PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _)) throw new InvalidOperationException("Launched process identity unavailable.");
                var fileTime = (((ulong)(uint)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime).ToString();
                var exited = PInvoke.WaitForSingleObject(handle, 15000) == Windows.Win32.Foundation.WAIT_EVENT.WAIT_OBJECT_0;
                if (!PInvoke.GetExitCodeProcess(handle, out var exitCode)) throw new InvalidOperationException("Launched process exit code unavailable.");
                Console.WriteLine(JsonConvert.SerializeObject(new { success = true, pid = process.dwProcessId, fileTime, exited, exitCode }));
            }
            finally { PInvoke.CloseHandle(process.hThread); PInvoke.CloseHandle(process.hProcess); }
        }
        return true;
    }
}

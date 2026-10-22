using System.Collections.Concurrent;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TaskManager.Host.Native;

// Shell extensions require a COM STA and a message pump. No Shell work runs on the sampler.
public sealed class ShellDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> work = new(128);
    private readonly Thread thread;
    private int initializationError;
    public ShellDispatcher()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "TaskManager Shell STA" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    public Task<T> InvokeAsync<T>(Func<T> callback)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!work.TryAdd(() => { try { if (initializationError != 0) throw new ApiException(503, "shellUnavailable", "Windows Shell COM初始化失败。", initializationError); completion.TrySetResult(callback()); } catch (Exception ex) { completion.TrySetException(ex); } })) throw new ApiException(503, "shellBusy", "Windows Shell请求过多，请稍后重试。");
        }
        catch (InvalidOperationException) { throw new ApiException(503, "shellClosed", "应用正在关闭，Shell操作不可用。"); }
        return completion.Task;
    }
    private unsafe void Run()
    {
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);
        if (initialized.Failed) initializationError = initialized.Value;
        try
        {
            while (!work.IsCompleted)
            {
                if (work.TryTake(out var action, 25)) action();
                while (PInvoke.PeekMessage(out var message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE)) { PInvoke.TranslateMessage(message); PInvoke.DispatchMessage(message); }
            }
        }
        finally { if (initialized.Succeeded) PInvoke.CoUninitialize(); }
    }
    public void Dispose() { work.CompleteAdding(); }
}

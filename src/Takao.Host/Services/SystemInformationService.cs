using System.Diagnostics;
using Takao.Host.Models;
using Takao.Host.Native;

namespace Takao.Host.Services;

/// <summary>Independent, on-demand hardware cache. Never enters the real-time sampler lock.</summary>
public sealed class SystemInformationService(IHostApplicationLifetime lifetime)
{
    private readonly object gate = new();
    private Task<SystemInformationDto>? pending;
    private SystemInformationDto? current;
    private long captured;

    public async Task<SystemInformationDto> ReadAsync(bool refresh, CancellationToken requestAborted)
    {
        Task<SystemInformationDto> reading;
        lock (gate)
        {
            if (!refresh && current is not null && Stopwatch.GetElapsedTime(captured) < TimeSpan.FromSeconds(60)) return current;
            reading = pending ??= Task.Run(Collect, CancellationToken.None);
        }
        try { return await reading.WaitAsync(TimeSpan.FromSeconds(8), requestAborted); }
        catch (TimeoutException) { return Empty("loading", new("warmingUp", "硬件查询仍在后台进行，请稍后刷新；实时采样继续。")); }
    }
    private SystemInformationDto Collect()
    {
        SystemInformationDto result;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        try { result = SystemInformationCollector.Read(cancellation.Token); }
        catch (OperationCanceledException) { result = Empty("unavailable", new("timeout", "硬件读取已超时或应用正在关闭。")); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { result = Empty("unavailable", new("error", $"硬件读取失败（0x{exception.HResult:X8}），可刷新重试。", exception.HResult)); }
        lock (gate) { current = result; captured = Stopwatch.GetTimestamp(); pending = null; }
        return result;
    }
    private static SystemInformationDto Empty(string status, Unavailable reason)
    {
        Dictionary<string, Unavailable> Missing(params string[] fields) => fields.ToDictionary(field => field, _ => reason);
        return new(status, null, Missing("source"), [], [], new(null, null, null, Missing("manufacturer", "product", "version")),
            new(null, null, [], Missing("installedBytes", "moduleCount", "modules")), [], [],
            new(null, null, null, null, Missing("caption", "displayVersion", "build", "architecture")));
    }
}

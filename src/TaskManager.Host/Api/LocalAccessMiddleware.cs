using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TaskManager.Host.Models;
using TaskManager.Host.Services;

namespace TaskManager.Host.Api;

public sealed class LocalAccessGuard
{
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal bool ValidToken(string candidate)
    {
        var bytes = Encoding.UTF8.GetBytes(candidate);
        var expected = Encoding.UTF8.GetBytes(Token);
        return bytes.Length == expected.Length && CryptographicOperations.FixedTimeEquals(bytes, expected);
    }
}

public sealed class LocalAccessMiddleware(RequestDelegate next, ILogger<LocalAccessMiddleware> logger)
{
    private static readonly JsonSerializerSettings JsonSettings = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
    public async Task InvokeAsync(HttpContext context, LocalAccessGuard guard)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        try
        {
            var host = context.Request.Host;
            if ((host.Host != "127.0.0.1" && host.Host != "localhost") || host.Port != context.Connection.LocalPort ||
                context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote))
                throw new ApiException(403, "invalidHost", "只允许当前本机回环地址与端口。");
            if (context.Request.Headers.TryGetValue("Origin", out var origin) && origin.ToString() != $"{context.Request.Scheme}://{host}")
                throw new ApiException(403, "invalidOrigin", "请求来源与本机应用不一致。");
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
                !guard.ValidToken(context.Request.Headers["X-TaskManager-Token"].ToString()))
                throw new ApiException(403, "invalidToken", "本次应用操作令牌缺失或无效，请重新连接。");
            await next(context);
        }
        catch (ApiException exception) { await WriteError(context, exception.Status, new(exception.Code, exception.Message, exception.NativeErrorCode)); }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "本地请求失败 {Path}", context.Request.Path);
            await WriteError(context, 500, new("internalError", "本地服务处理失败，请重试。"));
        }
    }
    private static async Task WriteError(HttpContext context, int status, ErrorBody error)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonConvert.SerializeObject(new ErrorEnvelope(error), JsonSettings));
    }
}

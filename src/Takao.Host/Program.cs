using Microsoft.AspNetCore.Server.Kestrel.Core;
using Newtonsoft.Json.Serialization;
using Takao.Host.Api;
using Takao.Host.Services;
using Takao.Host.Native;

try
{
    var entry = TaskManagerEntry.Prepare(args);
    if (entry.MaintenanceRequested)
    {
        Console.WriteLine(new TaskManagerIntegrationService().RemoveOwnedForUninstall().Message);
        return;
    }
    if (entry.Forwarded) { Console.WriteLine(entry.Message); return; }
    args = entry.Arguments;
}
catch (StartupPrivilegeException error) { Console.Error.WriteLine($"{error.Code} ({error.ExitCode}): {error.Message}"); Environment.ExitCode = error.ExitCode; return; }
catch (ApiException error) { Console.Error.WriteLine($"{error.Code}: {error.Message} 卸载维护失败，必须保留程序文件。"); Environment.ExitCode = 1; return; }
catch (ArgumentException error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 2; return; }

var desktopMode = !args.Contains("--headless", StringComparer.OrdinalIgnoreCase);
var updates = new ApplicationUpdateService(desktopMode);
var normalDesktopStartup = desktopMode && !args.Any(argument => argument.StartsWith("--port", StringComparison.OrdinalIgnoreCase))
    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKMANAGER_PORT")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKMANAGER_DATA_DIR"))
    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKMANAGER_QT_TEST_1080P"));
if (normalDesktopStartup && await updates.TryApplyScheduledAtStartupAsync()) { updates.Dispose(); return; }
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var portArgument = args.FirstOrDefault(argument => argument.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))?[7..];
var port = int.TryParse(portArgument ?? Environment.GetEnvironmentVariable("TASKMANAGER_PORT"), out var configuredPort) ? configuredPort : 5188;
if (port is < 1024 or > 65535) throw new InvalidOperationException("TASKMANAGER_PORT must be between 1024 and 65535.");
var preferences = new PreferencesService();
var desktop = new DesktopWindowService(preferences);
builder.Services.AddSingleton(desktop);
builder.Services.AddSingleton(updates);
builder.Services.AddHostedService(provider => provider.GetRequiredService<ApplicationUpdateService>());
builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1));
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
        new Takao.Host.Models.ErrorEnvelope(new("invalidRequest", "请求字段缺失或格式无效，请检查输入。")));
}).AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
    options.SerializerSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
    options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Include;
});
builder.Services.AddSingleton<LocalAccessGuard>();
builder.Services.AddSingleton(preferences);
builder.Services.AddSingleton<ShellDispatcher>();
builder.Services.AddKeyedSingleton<ShellDispatcher>("icons");
builder.Services.AddSingleton<ProcessIconService>();
builder.Services.AddSingleton<ProcessConnections>();
builder.Services.AddSingleton<ProcessQueryService>();
builder.Services.AddSingleton<ProcessManagementService>();
builder.Services.AddSingleton<ShellActionService>();
builder.Services.AddSingleton<ServiceActionService>();
builder.Services.AddSingleton<TaskManagerIntegrationService>();
builder.Services.AddResponseCompression(options => options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>());
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SnapshotService>());
builder.Services.AddSingleton<ProcessActionService>();
var app = builder.Build();
updates.ConfigureDesktopShutdown(async () => { await desktop.ExecuteAsync("close"); app.Lifetime.StopApplication(); });
app.UseMiddleware<LocalAccessMiddleware>();
app.UseResponseCompression();
app.MapControllers();
try
{
    // Start listening before Qt requests /api/bootstrap. Headless callers use
    // the same real services and middleware without a rendering fallback.
    await app.StartAsync();
    if (desktopMode) await desktop.OpenAsync($"http://127.0.0.1:{port}", app.Lifetime);
    await app.WaitForShutdownAsync();
}
catch (Exception error)
{
    Environment.ExitCode = 1;
    var code = error is ApiException api ? api.Code : "desktopStartupFailed";
    app.Logger.LogError(error, "{Code}: Qt/.NET desktop startup failed", code);
    try
    {
        var directory = Environment.GetEnvironmentVariable("TASKMANAGER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueDomainTaskManager");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "startup-error.log"), $"{DateTime.UtcNow:O} {code}: {error.Message}");
    }
    catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"{code}: {error.Message}"); }
}
finally
{
    await desktop.DisposeAsync();
    await app.StopAsync();
    await app.DisposeAsync();
}

public partial class Program;

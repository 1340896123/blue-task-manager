using Microsoft.AspNetCore.Server.Kestrel.Core;
using Newtonsoft.Json.Serialization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Takao.Host.MewUI;
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
builder.Services.AddSingleton<SystemInformationService>();
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
var shutdownCompleted = false;
FontResource? appFont = null;
try
{
    // Start listening before the native MewUI window is shown. Headless callers
    // use the same real services without a rendering fallback.
    await app.StartAsync();
    if (desktopMode)
    {
        Win32Platform.Register();
        Direct2DBackend.Register();
        appFont = AppTypography.Load();
        AppTypography.Apply(appFont);
        var window = new TaskManagerWindow(app.Services.GetRequiredService<SnapshotService>(), preferences,
            app.Services.GetRequiredService<ProcessActionService>(), app.Services.GetRequiredService<SystemInformationService>(),
            app.Services.GetRequiredService<ShellActionService>(), app.Services.GetRequiredService<ServiceActionService>(),
            desktop, app.Services.GetRequiredService<TaskManagerIntegrationService>(),
            app.Services.GetRequiredService<ProcessIconService>());
        desktop.Attach(window);
        window.OnClosed(() =>
        {
            desktop.Detach(window);
            app.Lifetime.StopApplication();
        });
        // Resolve the persisted theme before the first frame. MewUI owns the
        // palette and native focus semantics; the shell applies the same mode
        // again when the user changes it from Settings.
        var theme = preferences.Theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.System,
        };
        Application.Run(window, () =>
        {
            // Install one application-level sheet before the first frame so
            // buttons, inputs, selectors and dialogs on every page use the
            // same Fluent glass material.
            Application.Current.StyleSheet = GlassTheme.CreateStyleSheet();
            Application.Current.SetTheme(theme);
        });
        if (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
            app.Lifetime.StopApplication();
        await app.StopAsync();
    }
    else
    {
        await app.WaitForShutdownAsync();
    }
    // WaitForShutdownAsync already stops hosted services. Native collectors
    // release their cancellation sources during that stop.
    shutdownCompleted = true;
}
catch (Exception error)
{
    Environment.ExitCode = 1;
    var code = error is ApiException api ? api.Code : "desktopStartupFailed";
    app.Logger.LogError(error, "{Code}: MewUI/.NET desktop startup failed", code);
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
    appFont?.Dispose();
    await desktop.DisposeAsync();
    if (!shutdownCompleted) await app.StopAsync();
    await app.DisposeAsync();
}

public partial class Program;

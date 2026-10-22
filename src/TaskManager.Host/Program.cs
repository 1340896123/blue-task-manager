using Microsoft.AspNetCore.Server.Kestrel.Core;
using Newtonsoft.Json.Serialization;
using TaskManager.Host.Api;
using TaskManager.Host.Services;
using ElectronNET.API;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var portArgument = args.FirstOrDefault(argument => argument.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))?[7..];
var port = int.TryParse(portArgument ?? Environment.GetEnvironmentVariable("TASKMANAGER_PORT"), out var configuredPort) ? configuredPort : 5188;
if (port is < 1024 or > 65535) throw new InvalidOperationException("TASKMANAGER_PORT must be between 1024 and 65535.");
var desktop = new DesktopWindowService();
if (!args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
{
    Environment.SetEnvironmentVariable("TASKMANAGER_ORIGIN", $"http://127.0.0.1:{port}");
    builder.UseElectron(args, async (IServiceProvider services) => await desktop.OpenAsync($"http://127.0.0.1:{port}", services.GetRequiredService<IHostApplicationLifetime>()));
}
builder.Services.AddSingleton(desktop);
builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1));
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
        new TaskManager.Host.Models.ErrorEnvelope(new("invalidRequest", "请求字段缺失或格式无效，请检查输入。")));
}).AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
    options.SerializerSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
    options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Include;
});
builder.Services.AddSingleton<LocalAccessGuard>();
builder.Services.AddSingleton<PreferencesService>();
builder.Services.AddResponseCompression(options => options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>());
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SnapshotService>());
builder.Services.AddSingleton<ProcessActionService>();
var app = builder.Build();
app.UseMiddleware<LocalAccessMiddleware>();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;

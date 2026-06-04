using Liveolator.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

ServerConfig config;
try
{
    config = ServerConfig.Parse(args);
}
catch (ArgumentException ex)
{
    // Config errors are fatal but must be legible (stderr keeps stdout clean for the stdio protocol).
    await Console.Error.WriteLineAsync($"liveolator-mcp: {ex.Message}");
    return 1;
}

if (config.Mode == ServerMode.Http)
    await RunHttpAsync(config);
else
    await RunStdioAsync(config);

return 0;

// stdio transport: for a locally-launched agent. All logs go to stderr so stdout carries only protocol.
static async Task RunStdioAsync(ServerConfig config)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddLiveolatorMusicServices(config);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

// HTTP/SSE transport on loopback: for remote/already-running agents.
static async Task RunHttpAsync(ServerConfig config)
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls($"http://127.0.0.1:{config.Port}");

    builder.Services.AddLiveolatorMusicServices(config);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    WebApplication app = builder.Build();
    app.MapMcp();
    await app.RunAsync();
}

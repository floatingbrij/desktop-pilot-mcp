using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// CRITICAL: All logging must go to stderr to keep stdout clean for MCP JSON-RPC protocol
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddDebug();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

IHost app = builder.Build();
await app.RunAsync();

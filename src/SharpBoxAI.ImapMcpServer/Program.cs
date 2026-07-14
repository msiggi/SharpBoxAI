using SharpBoxAI.ImapMcpServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ContentRoot auf das Programmverzeichnis setzen, damit appsettings.json auch dann
// gefunden wird, wenn der Server von einem MCP-Client (z.B. Claude Desktop) mit
// beliebigem Arbeitsverzeichnis gestartet wird.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Logging MUSS auf stderr, da stdout für das MCP-Protokoll reserviert ist
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Credentials (Imap:User / Imap:Password) kommen aus den User Secrets,
// nicht aus der eingecheckten appsettings.json
builder.Configuration.AddUserSecrets<ImapSettings>(optional: true);

// Umgebungsvariablen als optionaler Override (höchste Priorität)
var envOverrides = new Dictionary<string, string>
{
    ["IMAP_HOST"] = "Imap:Host",
    ["IMAP_USER"] = "Imap:User",
    ["IMAP_PASSWORD"] = "Imap:Password",
};

foreach (var (envVar, configKey) in envOverrides)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(value))
    {
        builder.Configuration[configKey] = value;
    }
}

builder.Services.Configure<ImapSettings>(builder.Configuration.GetSection(ImapSettings.SectionName));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

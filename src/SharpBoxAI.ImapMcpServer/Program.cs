using SharpBoxAI.ImapMcpServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Transport per Startargument: ohne Argument stdio (wie bisher, für Claude Desktop),
// mit --http als Streamable-HTTP-Server auf Port 5100 (für Clients im Heimnetz).
var httpModus = args.Contains("--http");
var hostArgs = args.Where(a => a != "--http").ToArray();

if (httpModus)
{
    // ContentRoot auf das Programmverzeichnis setzen, damit appsettings.json auch dann
    // gefunden wird, wenn der Server aus einem beliebigen Arbeitsverzeichnis startet.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = hostArgs,
        ContentRootPath = AppContext.BaseDirectory,
    });

    ImapKonfigurationAnwenden(builder.Configuration, builder.Services);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    // MCP-Endpunkt liegt auf der Root-URL, z.B. http://<host>:5100
    app.MapMcp();

    await app.RunAsync("http://0.0.0.0:5100");
}
else
{
    // ContentRoot auf das Programmverzeichnis setzen, damit appsettings.json auch dann
    // gefunden wird, wenn der Server von einem MCP-Client (z.B. Claude Desktop) mit
    // beliebigem Arbeitsverzeichnis gestartet wird.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = hostArgs,
        ContentRootPath = AppContext.BaseDirectory,
    });

    // Logging MUSS auf stderr, da stdout für das MCP-Protokoll reserviert ist
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    ImapKonfigurationAnwenden(builder.Configuration, builder.Services);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

// Für beide Transport-Varianten identisch: User Secrets und Env-Variablen-Overrides
// über der eingecheckten appsettings.json, danach die ImapSettings registrieren.
static void ImapKonfigurationAnwenden(ConfigurationManager configuration, IServiceCollection services)
{
    // Credentials (Imap:User / Imap:Password) kommen aus den User Secrets,
    // nicht aus der eingecheckten appsettings.json
    configuration.AddUserSecrets<ImapSettings>(optional: true);

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
            configuration[configKey] = value;
        }
    }

    services.Configure<ImapSettings>(configuration.GetSection(ImapSettings.SectionName));
}

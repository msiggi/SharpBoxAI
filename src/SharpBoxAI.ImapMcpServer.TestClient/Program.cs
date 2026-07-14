using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// Repo-Root über die Solution-Datei finden, damit der Client aus jedem
// Arbeitsverzeichnis heraus funktioniert (dotnet run, VS, Explorer).
var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SharpBoxAI.slnx")))
{
    dir = dir.Parent;
}

if (dir is null)
{
    Console.Error.WriteLine("SharpBoxAI.slnx nicht gefunden — bitte aus dem Repository heraus starten.");
    return 1;
}

var serverProject = Path.Combine(dir.FullName, "src", "SharpBoxAI.ImapMcpServer");

Console.WriteLine($"Starte MCP-Server: {serverProject}");
Console.WriteLine("(vorher \"dotnet build\" ausführen — der Server wird mit --no-build gestartet)");
Console.WriteLine();

var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "SharpBoxAI.ImapMcpServer",
    Command = "dotnet",
    Arguments = ["run", "--project", serverProject, "--no-build"],
});

await using var client = await McpClient.CreateAsync(transport);

Console.WriteLine($"Verbunden: {client.ServerInfo.Name} {client.ServerInfo.Version}");
Console.WriteLine();

Console.WriteLine("Verfügbare Tools:");
var tools = await client.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"  - {tool.Name}: {tool.Description}");
}

Console.WriteLine();
Console.WriteLine("Rufe list_unread_emails auf ...");
Console.WriteLine(new string('=', 60));

var result = await client.CallToolAsync("list_unread_emails");

foreach (var block in result.Content)
{
    if (block is TextContentBlock text)
    {
        Console.WriteLine(text.Text);
    }
}

Console.WriteLine(new string('=', 60));

if (result.IsError == true)
{
    Console.Error.WriteLine("Tool-Aufruf hat einen Fehler gemeldet.");
    return 1;
}

return 0;

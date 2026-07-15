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

// Nur die lesenden Tools werden automatisch aufgerufen — move_email,
// delete_email, mark_as_spam usw. verändern das Postfach und bleiben
// bewusst dem MCP-Client (z.B. Claude) überlassen.
var exitCode = 0;

async Task<string> CallAndPrintAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
{
    Console.WriteLine();
    Console.WriteLine($"Rufe {toolName} auf ...");
    Console.WriteLine(new string('=', 60));

    var result = await client.CallToolAsync(toolName, arguments);
    var output = new System.Text.StringBuilder();

    foreach (var block in result.Content)
    {
        if (block is TextContentBlock text)
        {
            Console.WriteLine(text.Text);
            output.AppendLine(text.Text);
        }
    }

    Console.WriteLine(new string('=', 60));

    if (result.IsError == true)
    {
        Console.Error.WriteLine($"{toolName} hat einen Fehler gemeldet.");
        exitCode = 1;
    }

    return output.ToString();
}

var unreadOutput = await CallAndPrintAsync("list_unread_emails");
await CallAndPrintAsync("list_read_emails", new Dictionary<string, object?> { ["maxCount"] = 5 });
await CallAndPrintAsync("list_folders");

// get_email mit der ersten gefundenen UID testen (rein lesend)
var uidMatch = System.Text.RegularExpressions.Regex.Match(unreadOutput, @"UID: (\d+)");
if (uidMatch.Success)
{
    var uid = uint.Parse(uidMatch.Groups[1].Value);
    await CallAndPrintAsync("get_email", new Dictionary<string, object?> { ["uid"] = uid });
}

return exitCode;

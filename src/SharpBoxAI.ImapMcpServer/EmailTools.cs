using System.ComponentModel;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace SharpBoxAI.ImapMcpServer;

[McpServerToolType]
public sealed class EmailTools
{
    private readonly ImapSettings _settings;

    public EmailTools(IOptions<ImapSettings> options)
    {
        _settings = options.Value;
    }

    [McpServerTool, Description("Listet ungelesene E-Mails im Posteingang auf (Betreff, Absender, Datum).")]
    public async Task<string> ListUnreadEmails(CancellationToken cancellationToken)
    {
        // Credentials kommen aus den User Secrets bzw. Umgebungsvariablen — nie aus dem Code
        var user = _settings.User;
        var pass = _settings.Password;

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            return "Konfigurationsfehler: Imap:User und Imap:Password müssen als User Secrets "
                + "(dotnet user-secrets set) oder über die Umgebungsvariablen IMAP_USER/IMAP_PASSWORD gesetzt sein.";
        }

        using var client = new ImapClient();

        var socketOptions = _settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Verbindungsfehler zu {_settings.Host}:{_settings.Port}: {ex.Message}";
        }

        try
        {
            await client.AuthenticateAsync(user, pass, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Anmeldung als {user} fehlgeschlagen: {ex.Message}";
        }

        var inbox = client.Inbox;

        // ReadOnly: das Postfach darf durch dieses Tool nicht verändert werden
        // (kein "als gelesen markieren", kein Verschieben, kein Löschen)
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);

        if (uids.Count == 0)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return "Keine ungelesenen Mails.";
        }

        // Nur die Envelopes abrufen statt kompletter Nachrichten inkl. Anhängen
        var summaries = await inbox.FetchAsync(uids, MessageSummaryItems.Envelope, cancellationToken);

        var results = new List<string>();

        foreach (var summary in summaries)
        {
            if (summary.Envelope is not { } envelope)
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Von: {envelope.From}");
            sb.AppendLine($"Betreff: {envelope.Subject}");
            sb.AppendLine($"Datum: {envelope.Date:yyyy-MM-dd HH:mm zzz}");
            results.Add(sb.ToString());
        }

        await client.DisconnectAsync(true, cancellationToken);

        return string.Join("\n---\n", results);
    }
}

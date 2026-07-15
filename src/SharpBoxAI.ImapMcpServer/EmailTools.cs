using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

    [McpServerTool, Description("Listet ungelesene E-Mails im Posteingang auf (UID, Betreff, Absender, Datum). Die UID wird für move_email, delete_email, mark_as_spam usw. benötigt.")]
    public Task<string> ListUnreadEmails(CancellationToken cancellationToken)
        => ListEmailsAsync(SearchQuery.NotSeen, "Keine ungelesenen Mails.", maxCount: null, cancellationToken);

    [McpServerTool, Description("Listet bereits gelesene E-Mails im Posteingang auf (UID, Betreff, Absender, Datum), neueste zuerst. Die UID wird für get_email, move_email, delete_email usw. benötigt.")]
    public Task<string> ListReadEmails(
        CancellationToken cancellationToken,
        [Description("Maximale Anzahl der Mails (neueste zuerst), Standard 20")] int maxCount = 20)
        => ListEmailsAsync(SearchQuery.Seen, "Keine gelesenen Mails.", maxCount <= 0 ? 20 : maxCount, cancellationToken);

    /// <summary>
    /// Gemeinsame Auflistung für gelesene/ungelesene Mails. Mit <paramref name="maxCount"/>
    /// werden nur die neuesten Treffer geliefert (höchste UIDs zuerst), damit die
    /// Antwort bei vollen Postfächern überschaubar bleibt.
    /// </summary>
    private async Task<string> ListEmailsAsync(SearchQuery query, string emptyMessage, int? maxCount, CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var inbox = client.Inbox;

            // Nur lesen: das Auflisten darf keine Flags verändern
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uids = await inbox.SearchAsync(query, cancellationToken);

            if (uids.Count == 0)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return emptyMessage;
            }

            var totalCount = uids.Count;
            IList<UniqueId> selected = uids;
            if (maxCount is { } limit && uids.Count > limit)
            {
                // Höhere UIDs = neuere Mails; nur die neuesten abrufen
                selected = uids.OrderByDescending(u => u.Id).Take(limit).ToList();
            }

            // Nur die Envelopes abrufen statt kompletter Nachrichten inkl. Anhängen
            var summaries = await inbox.FetchAsync(selected, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId, cancellationToken);

            var results = new List<string>();

            foreach (var summary in summaries.OrderByDescending(s => s.UniqueId.Id))
            {
                if (summary.Envelope is not { } envelope)
                {
                    continue;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"UID: {summary.UniqueId}");
                sb.AppendLine($"Von: {envelope.From}");
                sb.AppendLine($"Betreff: {envelope.Subject}");
                sb.AppendLine($"Datum: {envelope.Date:yyyy-MM-dd HH:mm zzz}");
                results.Add(sb.ToString());
            }

            await client.DisconnectAsync(true, cancellationToken);

            var header = selected.Count < totalCount
                ? $"{totalCount} Treffer, die neuesten {selected.Count} werden angezeigt:\n\n"
                : string.Empty;

            return header + string.Join("\n---\n", results);
        }
    }

    [McpServerTool, Description("Liest eine E-Mail anhand ihrer UID aus dem Posteingang: Header, Text-Inhalt und Anhang-Namen. Verändert die Mail nicht (auch das Gelesen-Flag bleibt unberührt).")]
    public async Task<string> GetEmail(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var inbox = client.Inbox;

            // ReadOnly: das Abrufen darf keine Flags setzen (kein automatisches \Seen)
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uniqueId = new UniqueId(uid);
            var summaries = await inbox.FetchAsync([uniqueId], MessageSummaryItems.Envelope, cancellationToken);
            if (summaries.Count == 0)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return $"Keine Mail mit UID {uid} im Posteingang gefunden.";
            }

            var message = await inbox.GetMessageAsync(uniqueId, cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine($"UID: {uid}");
            sb.AppendLine($"Von: {message.From}");
            sb.AppendLine($"An: {message.To}");
            sb.AppendLine($"Betreff: {message.Subject}");
            sb.AppendLine($"Datum: {message.Date:yyyy-MM-dd HH:mm zzz}");

            var attachments = message.Attachments
                .Select(a => a.ContentDisposition?.FileName ?? a.ContentType.Name ?? "(unbenannt)")
                .ToList();

            if (attachments.Count > 0)
            {
                sb.AppendLine($"Anhänge: {string.Join(", ", attachments)}");
            }

            sb.AppendLine();

            // Text-Body bevorzugen; reine HTML-Mails werden grob zu Text konvertiert
            var body = message.TextBody;
            if (string.IsNullOrWhiteSpace(body) && message.HtmlBody is { } html)
            {
                body = HtmlToPlainText(html);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                sb.AppendLine("(kein Textinhalt)");
            }
            else
            {
                const int maxBodyLength = 10_000;
                if (body.Length > maxBodyLength)
                {
                    sb.AppendLine(body[..maxBodyLength]);
                    sb.AppendLine($"[... gekürzt, insgesamt {body.Length} Zeichen]");
                }
                else
                {
                    sb.AppendLine(body);
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
            return sb.ToString();
        }
    }

    [McpServerTool, Description("Listet alle Ordner des Postfachs auf (z.B. als Ziel für move_email).")]
    public async Task<string> ListFolders(CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], subscribedOnly: false, cancellationToken);

            var lines = folders
                .Select(f => f.FullName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await client.DisconnectAsync(true, cancellationToken);

            return lines.Count == 0
                ? "Keine Ordner gefunden."
                : string.Join("\n", lines);
        }
    }

    [McpServerTool, Description("Verschiebt eine E-Mail anhand ihrer UID aus dem Posteingang in einen anderen Ordner. Ordnernamen liefert list_folders.")]
    public async Task<string> MoveEmail(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        [Description("Vollständiger Name des Zielordners (aus list_folders)")] string targetFolder,
        CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var target = await FindFolderAsync(client, targetFolder, cancellationToken);
            if (target is null)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return $"Zielordner \"{targetFolder}\" nicht gefunden. Verfügbare Ordner liefert list_folders.";
            }

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var result = await MoveAsync(inbox, new UniqueId(uid), target, cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);
            return result;
        }
    }

    [McpServerTool, Description("Verschiebt eine E-Mail anhand ihrer UID aus dem Posteingang in den Papierkorb. Es wird nichts endgültig gelöscht.")]
    public async Task<string> DeleteEmail(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var trash = await FindSpecialFolderAsync(client, SpecialFolder.Trash,
                ["Trash", "Papierkorb", "Gelöschte Elemente", "Gelöschte Objekte", "Deleted Items", "INBOX.Trash"],
                cancellationToken);

            if (trash is null)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return "Kein Papierkorb-Ordner gefunden — Mail wurde NICHT gelöscht. "
                    + "Bitte mit list_folders prüfen, wie der Papierkorb heißt, und move_email verwenden.";
            }

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var result = await MoveAsync(inbox, new UniqueId(uid), trash, cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);
            return result;
        }
    }

    [McpServerTool, Description("Markiert eine E-Mail als Spam, indem sie anhand ihrer UID aus dem Posteingang in den Spam-/Junk-Ordner verschoben wird.")]
    public async Task<string> MarkAsSpam(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var junk = await FindSpecialFolderAsync(client, SpecialFolder.Junk,
                ["Spam", "Junk", "Junk-E-Mail", "Junk E-Mail", "INBOX.Spam", "INBOX.Junk"],
                cancellationToken);

            if (junk is null)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return "Kein Spam-/Junk-Ordner gefunden. "
                    + "Bitte mit list_folders prüfen, wie der Ordner heißt, und move_email verwenden.";
            }

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var result = await MoveAsync(inbox, new UniqueId(uid), junk, cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);
            return result;
        }
    }

    [McpServerTool, Description("Markiert eine E-Mail im Posteingang anhand ihrer UID als gelesen.")]
    public Task<string> MarkAsRead(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
        => SetFlagAsync(uid, MessageFlags.Seen, add: true, "als gelesen markiert", cancellationToken);

    [McpServerTool, Description("Markiert eine E-Mail im Posteingang anhand ihrer UID als ungelesen.")]
    public Task<string> MarkAsUnread(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
        => SetFlagAsync(uid, MessageFlags.Seen, add: false, "als ungelesen markiert", cancellationToken);

    [McpServerTool, Description("Versieht eine E-Mail im Posteingang anhand ihrer UID mit einer Flagge (\\Flagged, z.B. für wichtig/Nachverfolgung).")]
    public Task<string> FlagEmail(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
        => SetFlagAsync(uid, MessageFlags.Flagged, add: true, "mit Flagge markiert", cancellationToken);

    [McpServerTool, Description("Entfernt die Flagge (\\Flagged) einer E-Mail im Posteingang anhand ihrer UID.")]
    public Task<string> UnflagEmail(
        [Description("UID der Mail im Posteingang (aus list_unread_emails)")] uint uid,
        CancellationToken cancellationToken)
        => SetFlagAsync(uid, MessageFlags.Flagged, add: false, "Flagge entfernt", cancellationToken);

    private async Task<string> SetFlagAsync(uint uid, MessageFlags flag, bool add, string doneText, CancellationToken cancellationToken)
    {
        var (client, error) = await ConnectAsync(cancellationToken);
        if (client is null)
        {
            return error!;
        }

        using (client)
        {
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var uniqueId = new UniqueId(uid);
            var subject = await DescribeAsync(inbox, uniqueId, cancellationToken);
            if (subject is null)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return $"Keine Mail mit UID {uid} im Posteingang gefunden.";
            }

            if (add)
            {
                await inbox.AddFlagsAsync(uniqueId, flag, silent: true, cancellationToken);
            }
            else
            {
                await inbox.RemoveFlagsAsync(uniqueId, flag, silent: true, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);
            return $"Mail \"{subject}\" (UID {uid}) {doneText}.";
        }
    }

    /// <summary>
    /// Verbindet und authentifiziert einen neuen ImapClient. Bei Fehlern wird
    /// (null, Fehlermeldung) zurückgegeben — die Tools geben Fehler als Text aus,
    /// damit der MCP-Client eine verständliche Meldung erhält.
    /// </summary>
    private async Task<(ImapClient? Client, string? Error)> ConnectAsync(CancellationToken cancellationToken)
    {
        // Credentials kommen aus den User Secrets bzw. Umgebungsvariablen — nie aus dem Code
        var user = _settings.User;
        var pass = _settings.Password;

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            return (null, "Konfigurationsfehler: Imap:User und Imap:Password müssen als User Secrets "
                + "(dotnet user-secrets set) oder über die Umgebungsvariablen IMAP_USER/IMAP_PASSWORD gesetzt sein.");
        }

        var client = new ImapClient();

        var socketOptions = _settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            return (null, $"Verbindungsfehler zu {_settings.Host}:{_settings.Port}: {ex.Message}");
        }

        try
        {
            await client.AuthenticateAsync(user, pass, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            return (null, $"Anmeldung als {user} fehlgeschlagen: {ex.Message}");
        }

        return (client, null);
    }

    /// <summary>
    /// Sucht einen Ordner zuerst über seinen vollständigen Pfad, dann per
    /// Namensvergleich (case-insensitive) über alle persönlichen Ordner.
    /// </summary>
    private static async Task<IMailFolder?> FindFolderAsync(ImapClient client, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetFolderAsync(name, cancellationToken);
        }
        catch (FolderNotFoundException)
        {
            // weiter unten per Namensvergleich suchen
        }

        var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], subscribedOnly: false, cancellationToken);

        return folders.FirstOrDefault(f =>
            string.Equals(f.FullName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ermittelt einen Spezialordner (Papierkorb, Spam) bevorzugt über die
    /// IMAP-Erweiterung SPECIAL-USE; Fallback über gängige Ordnernamen, da
    /// nicht jeder Server (z.B. Strato) SPECIAL-USE unterstützt.
    /// </summary>
    private static async Task<IMailFolder?> FindSpecialFolderAsync(
        ImapClient client,
        SpecialFolder specialFolder,
        string[] fallbackNames,
        CancellationToken cancellationToken)
    {
        try
        {
            var folder = client.GetFolder(specialFolder);
            if (folder is not null)
            {
                return folder;
            }
        }
        catch (NotSupportedException)
        {
            // Server unterstützt weder SPECIAL-USE noch XLIST
        }

        foreach (var name in fallbackNames)
        {
            var folder = await FindFolderAsync(client, name, cancellationToken);
            if (folder is not null)
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// Verschiebt eine einzelne Mail und liefert eine Text-Rückmeldung mit Betreff,
    /// damit der Aufrufer prüfen kann, dass die richtige Mail getroffen wurde.
    /// </summary>
    private static async Task<string> MoveAsync(IMailFolder source, UniqueId uid, IMailFolder target, CancellationToken cancellationToken)
    {
        var subject = await DescribeAsync(source, uid, cancellationToken);
        if (subject is null)
        {
            return $"Keine Mail mit UID {uid} im Posteingang gefunden.";
        }

        await source.MoveToAsync(uid, target, cancellationToken);

        return $"Mail \"{subject}\" (UID {uid}) nach \"{target.FullName}\" verschoben.";
    }

    /// <summary>
    /// Grobe HTML-zu-Text-Konvertierung für Mails ohne Text-Body. Kein vollwertiger
    /// Parser — reicht aber, damit der MCP-Client den Inhalt lesen kann.
    /// </summary>
    private static string HtmlToPlainText(string html)
    {
        var text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>|</p>|</div>|</tr>|</li>|</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*(\n\s*)+", "\n\n");
        return text.Trim();
    }

    /// <summary>
    /// Liefert den Betreff einer Mail oder null, wenn die UID nicht (mehr) existiert.
    /// </summary>
    private static async Task<string?> DescribeAsync(IMailFolder folder, UniqueId uid, CancellationToken cancellationToken)
    {
        var summaries = await folder.FetchAsync([uid], MessageSummaryItems.Envelope, cancellationToken);
        var summary = summaries.FirstOrDefault();

        return summary?.Envelope is { } envelope
            ? envelope.Subject ?? "(kein Betreff)"
            : null;
    }
}

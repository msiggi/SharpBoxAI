namespace SharpBoxAI.ImapMcpServer;

public sealed class ImapSettings
{
    public const string SectionName = "Imap";

    public string Host { get; set; } = "imap.strato.de";

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    // Kommen aus den User Secrets (Imap:User / Imap:Password) oder aus den
    // Umgebungsvariablen IMAP_USER / IMAP_PASSWORD — nie aus appsettings.json.
    public string? User { get; set; }

    public string? Password { get; set; }
}

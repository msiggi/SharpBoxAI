# SharpBoxAI

**Ein MCP-Server in C#/.NET, der Dein E-Mail-Postfach per IMAP für KI-Assistenten wie Claude zugänglich macht.**

SharpBoxAI implementiert das [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) und stellt einem MCP-Client (z. B. Claude Desktop) eine Reihe von E-Mail-Tools zur Verfügung. Damit kann der Assistent auf natürliche Sprache hin ungelesene Mails zusammenfassen, Newsletter aussortieren, Spam wegräumen oder das Postfach aufräumen — ohne dass Zugangsdaten jemals den eigenen Rechner verlassen: Der Server läuft lokal und spricht direkt mit dem IMAP-Server des eigenen Mail-Providers.

## Features

- **Postfach lesen**: ungelesene und gelesene Mails auflisten, einzelne Mails inkl. Text-Inhalt und Anhang-Namen abrufen — rein lesend, ohne das Gelesen-Flag zu verändern
- **Postfach organisieren**: Mails in Ordner verschieben, in den Papierkorb legen (nichts wird endgültig gelöscht), als Spam markieren
- **Flags verwalten**: gelesen/ungelesen setzen, Mails flaggen/entflaggen
- **Provider-unabhängig**: funktioniert mit jedem IMAP-Server; Spezialordner (Papierkorb, Spam) werden per IMAP-`SPECIAL-USE` erkannt, mit Fallback auf gängige deutsche und englische Ordnernamen für Server ohne diese Erweiterung
- **Sichere Konfiguration**: Zugangsdaten kommen aus .NET User Secrets oder Umgebungsvariablen — nie aus eingecheckten Dateien

### Verfügbare MCP-Tools

| Tool | Beschreibung |
|---|---|
| `list_unread_emails` | Listet ungelesene Mails im Posteingang (UID, Betreff, Absender, Datum) |
| `list_read_emails` | Listet gelesene Mails, neueste zuerst (Anzahl begrenzbar, Standard 20) |
| `get_email` | Liest eine Mail anhand ihrer UID: Header, Text-Inhalt, Anhang-Namen |
| `list_folders` | Listet alle Ordner des Postfachs |
| `move_email` | Verschiebt eine Mail in einen anderen Ordner |
| `delete_email` | Verschiebt eine Mail in den Papierkorb (kein endgültiges Löschen) |
| `mark_as_spam` | Verschiebt eine Mail in den Spam-/Junk-Ordner |
| `mark_as_read` / `mark_as_unread` | Setzt bzw. entfernt das Gelesen-Flag |
| `flag_email` / `unflag_email` | Setzt bzw. entfernt die Flagge (`\Flagged`) |

## Projektstruktur

```
src/
├── SharpBoxAI.ImapMcpServer/            # Der MCP-Server (stdio-Transport)
│   ├── Program.cs                       # Host-Setup, Konfiguration, MCP-Registrierung
│   ├── EmailTools.cs                    # Alle MCP-Tools (MailKit/IMAP)
│   ├── ImapSettings.cs                  # Konfigurationsmodell
│   └── appsettings.json                 # Host/Port/SSL (ohne Zugangsdaten)
├── SharpBoxAI.ImapMcpServer.TestClient/ # Konsolen-Client zum Testen der lesenden Tools
└── SharpBoxAI/                          # Platzhalter für zukünftige Erweiterungen
```

Verwendete Pakete: [MailKit](https://github.com/jstedfast/MailKit) für IMAP, das offizielle [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) für MCP.

## Voraussetzungen

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Ein E-Mail-Konto mit IMAP-Zugang

## Konfiguration

Host, Port und SSL werden in `src/SharpBoxAI.ImapMcpServer/appsettings.json` konfiguriert:

```json
{
  "Imap": {
    "Host": "imap.example.com",
    "Port": 993,
    "UseSsl": true
  }
}
```

Die Zugangsdaten gehören **nicht** in die `appsettings.json`, sondern in die User Secrets:

```bash
cd src/SharpBoxAI.ImapMcpServer
dotnet user-secrets set "Imap:User" "user@example.com"
dotnet user-secrets set "Imap:Password" "geheim"
```

Alternativ (mit höchster Priorität) per Umgebungsvariablen: `IMAP_HOST`, `IMAP_USER`, `IMAP_PASSWORD`.

## Verwendung

### Bauen

```bash
dotnet build
```

### Mit Claude Desktop verbinden

In der Claude-Desktop-Konfiguration (`claude_desktop_config.json`) den Server als MCP-Server eintragen:

```json
{
  "mcpServers": {
    "sharpboxai": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/pfad/zum/repo/src/SharpBoxAI.ImapMcpServer",
        "--no-build"
      ]
    }
  }
}
```

Danach Claude Desktop neu starten — die E-Mail-Tools stehen dann im Chat zur Verfügung, z. B.:

> „Fasse meine ungelesenen Mails zusammen und verschiebe alle Newsletter in den Ordner ‚Newsletter‘."

### Test-Client

Der mitgelieferte Test-Client startet den Server, listet die verfügbaren Tools auf und ruft die **rein lesenden** Tools automatisch auf (`list_unread_emails`, `list_read_emails`, `list_folders`, `get_email`). Verändernde Tools wie `move_email` oder `delete_email` werden bewusst nicht automatisch ausgeführt.

```bash
dotnet build
dotnet run --project src/SharpBoxAI.ImapMcpServer.TestClient
```

## Sicherheit & Design-Entscheidungen

- **Keine Zugangsdaten im Repository**: Benutzer und Passwort kommen ausschließlich aus User Secrets oder Umgebungsvariablen.
- **Kein endgültiges Löschen**: `delete_email` verschiebt nur in den Papierkorb. Wird kein Papierkorb gefunden, passiert nichts.
- **Lesen ist wirklich nur Lesen**: Auflisten und Abrufen von Mails öffnet den Posteingang read-only und verändert keine Flags.
- **Kein Senden von Mails**: Der Server kann ausschließlich lesen und organisieren — SMTP ist bewusst nicht angebunden.
- **Begrenzte Ausgaben**: Mail-Inhalte werden auf 10.000 Zeichen gekürzt, Listen auf die neuesten Treffer begrenzt, damit der Kontext des MCP-Clients nicht überläuft.
- **Logging auf stderr**: stdout ist für das MCP-Protokoll reserviert.

## Lizenz

Noch nicht festgelegt.

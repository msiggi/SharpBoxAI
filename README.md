# SharpBoxAI

**An MCP server written in C#/.NET that makes your e-mail inbox accessible to AI assistants like Claude via IMAP.**

SharpBoxAI implements the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) and provides an MCP client (e.g. Claude Desktop) with a set of e-mail tools. This lets the assistant respond to natural-language requests: summarize unread mail, sort out newsletters, clean up spam, or tidy the inbox — without your credentials ever leaving your machine. The server runs locally and talks directly to your mail provider's IMAP server.

## Features

- **Read the inbox**: list unread and read messages, fetch individual messages including text content and attachment names — strictly read-only, without touching the seen flag
- **Organize the inbox**: move messages to folders, put them in the trash (nothing is deleted permanently), mark them as spam
- **Manage flags**: mark as read/unread, flag/unflag messages
- **Provider-independent**: works with any IMAP server; special folders (trash, spam) are detected via the IMAP `SPECIAL-USE` extension, with a fallback to common German and English folder names for servers that don't support it
- **Secure configuration**: credentials come from .NET user secrets or environment variables — never from files checked into the repository

### Available MCP tools

| Tool | Description |
|---|---|
| `list_unread_emails` | Lists unread messages in the inbox (UID, subject, sender, date) |
| `list_read_emails` | Lists read messages, newest first (count is limitable, default 20) |
| `get_email` | Reads a message by its UID: headers, text content, attachment names |
| `list_folders` | Lists all folders of the mailbox |
| `move_email` | Moves a message to another folder |
| `delete_email` | Moves a message to the trash (no permanent deletion) |
| `mark_as_spam` | Moves a message to the spam/junk folder |
| `mark_as_read` / `mark_as_unread` | Sets or clears the seen flag |
| `flag_email` / `unflag_email` | Sets or clears the flag (`\Flagged`) |

## Project structure

```
src/
├── SharpBoxAI.ImapMcpServer/            # The MCP server (stdio transport)
│   ├── Program.cs                       # Host setup, configuration, MCP registration
│   ├── EmailTools.cs                    # All MCP tools (MailKit/IMAP)
│   ├── ImapSettings.cs                  # Configuration model
│   └── appsettings.json                 # Host/port/SSL (no credentials)
├── SharpBoxAI.ImapMcpServer.TestClient/ # Console client for testing the read-only tools
└── SharpBoxAI/                          # Placeholder for future extensions
```

Packages used: [MailKit](https://github.com/jstedfast/MailKit) for IMAP, the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) for MCP.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An e-mail account with IMAP access

## Configuration

Host, port, and SSL are configured in `src/SharpBoxAI.ImapMcpServer/appsettings.json`:

```json
{
  "Imap": {
    "Host": "imap.example.com",
    "Port": 993,
    "UseSsl": true
  }
}
```

Credentials do **not** belong in `appsettings.json`; put them in the user secrets instead:

```bash
cd src/SharpBoxAI.ImapMcpServer
dotnet user-secrets set "Imap:User" "user@example.com"
dotnet user-secrets set "Imap:Password" "secret"
```

Alternatively (with highest priority), use the environment variables `IMAP_HOST`, `IMAP_USER`, `IMAP_PASSWORD`.

## Usage

### Build

```bash
dotnet build
```

### Connect to Claude Desktop

Register the server as an MCP server in the Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "sharpboxai": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/repo/src/SharpBoxAI.ImapMcpServer",
        "--no-build"
      ]
    }
  }
}
```

Then restart Claude Desktop — the e-mail tools are now available in the chat, for example:

> "Summarize my unread mail and move all newsletters to the 'Newsletter' folder."

### Test client

The included test client starts the server, lists the available tools, and automatically calls the **read-only** tools (`list_unread_emails`, `list_read_emails`, `list_folders`, `get_email`). Tools that modify the mailbox, such as `move_email` or `delete_email`, are deliberately not executed automatically.

```bash
dotnet build
dotnet run --project src/SharpBoxAI.ImapMcpServer.TestClient
```

## Security & design decisions

- **No credentials in the repository**: username and password come exclusively from user secrets or environment variables.
- **No permanent deletion**: `delete_email` only moves messages to the trash. If no trash folder is found, nothing happens.
- **Reading really is just reading**: listing and fetching messages opens the inbox read-only and does not modify any flags.
- **No sending of mail**: the server can only read and organize — SMTP is deliberately not included.
- **Bounded output**: message bodies are truncated at 10,000 characters and listings are limited to the newest matches, so the MCP client's context does not overflow.
- **Logging goes to stderr**: stdout is reserved for the MCP protocol.

## License

Not yet decided.

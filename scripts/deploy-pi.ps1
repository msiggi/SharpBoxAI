<#
.SYNOPSIS
    Baut den MCP-Server für linux-arm64 und deployt ihn per SSH auf einen Raspberry Pi.

.DESCRIPTION
    Ablauf: dotnet publish (self-contained, damit auf dem Pi kein .NET SDK nötig ist)
    -> Tarball packen -> per scp auf den Pi -> Service stoppen, entpacken, Service starten.

    Voraussetzungen auf dem Pi (einmalig, siehe -PrintSetup):
      - systemd-Unit sharpboxai.service
      - /etc/sharpboxai.env mit IMAP_HOST / IMAP_USER / IMAP_PASSWORD
      - sudo ohne Passwortabfrage für den Deploy-Benutzer (auf Raspberry Pi OS Standard)

    Credentials werden bewusst nicht mitdeployt - sie leben nur auf dem Pi.

.EXAMPLE
    ./scripts/deploy-pi.ps1
    Deployt mit den Standardwerten (msiggi@192.168.121.220:/opt/sharpboxai).

.EXAMPLE
    ./scripts/deploy-pi.ps1 -PiHost 192.168.1.42 -PiUser andererbenutzer
    Deployt auf einen anderen Host/Benutzer.

.EXAMPLE
    ./scripts/deploy-pi.ps1 -PrintSetup
    Gibt die einmaligen Einrichtungsbefehle für den Pi aus, ohne zu deployen.
#>
[CmdletBinding()]
param(
    [string]$PiHost = '192.168.121.220',
    [string]$PiUser = 'msiggi',
    [string]$RemotePath = '/opt/sharpboxai',
    [string]$ServiceName = 'sharpboxai',
    [string]$Runtime = 'linux-arm64',
    # Nur bauen und packen, nicht übertragen - z.B. zum Prüfen des Publish-Ergebnisses.
    [switch]$BuildOnly,
    # Einmalige Einrichtungsbefehle für den Pi ausgeben und beenden.
    [switch]$PrintSetup
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\SharpBoxAI.ImapMcpServer'
$publishDir = Join-Path $repoRoot 'artifacts\pi'
$tarball = Join-Path $repoRoot 'artifacts\sharpboxai-pi.tar.gz'
$target = "$PiUser@$PiHost"

if ($PrintSetup) {
    # Achtung: die Bloecke duerfen NICHT eingerueckt werden - bash erkennt ein
    # eingeruecktes EOF nicht als Ende des Heredocs und wartet endlos weiter.
    @"
Einmalige Einrichtung auf dem Pi - per 'ssh $target' verbinden und
die folgenden drei Bloecke dort ausfuehren.

--- 1) Zugangsdaten hinterlegen (Werte anpassen!) ---

sudo tee /etc/$ServiceName.env > /dev/null <<'EOF'
IMAP_HOST=imap.deinprovider.de
IMAP_USER=du@example.com
IMAP_PASSWORD=dein-app-passwort
EOF
sudo chmod 600 /etc/$ServiceName.env

--- 2) Zielverzeichnis anlegen ---

sudo mkdir -p $RemotePath

--- 3) systemd-Unit anlegen und aktivieren ---

sudo tee /etc/systemd/system/$ServiceName.service > /dev/null <<'EOF'
[Unit]
Description=SharpBoxAI IMAP MCP Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$RemotePath
ExecStart=$RemotePath/SharpBoxAI.ImapMcpServer --http
EnvironmentFile=/etc/$ServiceName.env
Restart=always
RestartSec=5
User=$PiUser

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable $ServiceName

Danach genuegt fuer jedes Update: ./scripts/deploy-pi.ps1
"@
    exit 0
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name fehlgeschlagen (Exit-Code $LASTEXITCODE)."
    }
}

# --- 1. Publish -------------------------------------------------------------
# Altes Ergebnis entfernen, damit entfallene Dateien nicht mitwandern.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $tarball) { Remove-Item $tarball -Force }

Invoke-Step "Publish ($Runtime, self-contained)" {
    dotnet publish $projectPath -c Release -r $Runtime --self-contained -o $publishDir
}

# --- 2. Packen --------------------------------------------------------------
# Ein Tarball statt "scp -r": deutlich schneller als hunderte Einzeldateien und
# erhält das Executable-Bit, das scp von Windows aus nicht überträgt.
Invoke-Step 'Tarball packen' {
    tar -czf $tarball -C $publishDir .
}

$sizeMb = [math]::Round((Get-Item $tarball).Length / 1MB, 1)
Write-Host "    $tarball ($sizeMb MB)" -ForegroundColor DarkGray

if ($BuildOnly) {
    Write-Host "BuildOnly gesetzt - nicht übertragen." -ForegroundColor Yellow
    exit 0
}

# --- 3. Übertragen ----------------------------------------------------------
$remoteTarball = "/tmp/sharpboxai-pi.tar.gz"

Invoke-Step "Übertragen nach $target" {
    scp $tarball "${target}:$remoteTarball"
}

# --- 4. Installieren --------------------------------------------------------
# Der Service wird vor dem Entpacken gestoppt, weil die laufende Binary sonst
# gesperrt sein kann; --no-block vermeidet Hänger, falls er schon tot ist.
$remoteScript = @"
set -e
sudo systemctl stop $ServiceName 2>/dev/null || true
sudo mkdir -p $RemotePath
sudo tar -xzf $remoteTarball -C $RemotePath
sudo chmod +x $RemotePath/SharpBoxAI.ImapMcpServer
rm -f $remoteTarball
sudo systemctl start $ServiceName
sleep 2
sudo systemctl is-active --quiet $ServiceName && echo "Service laeuft." || (echo "Service NICHT aktiv:"; sudo journalctl -u $ServiceName -n 30 --no-pager; exit 1)
"@

Invoke-Step 'Auf dem Pi installieren und Service neu starten' {
    # Skript über stdin schicken, damit kein Quoting-Chaos entsteht.
    $remoteScript | ssh $target 'bash -s'
}

Write-Host ''
Write-Host "Deployment abgeschlossen: http://${PiHost}:5100" -ForegroundColor Green
Write-Host "Logs:  ssh $target 'journalctl -u $ServiceName -f'" -ForegroundColor DarkGray

# Multi-Stage-Build: SDK-Image baut, schlankes ASP.NET-Runtime-Image führt aus.
# Beide Images gibt es für linux/arm64, der Build läuft also direkt auf dem Pi 5.
#
# Der Build-Stage läuft bewusst immer auf der Architektur des Buildhosts
# (--platform=$BUILDPLATFORM) und cross-compiliert per "dotnet publish -a" auf die
# Zielarchitektur. So kommt ein Multi-Arch-Build (amd64 + arm64) in der CI ohne
# QEMU-Emulation aus. BuildKit setzt BUILDPLATFORM und TARGETARCH automatisch.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Erst nur die Projektdatei kopieren, damit der Restore-Layer im Cache bleibt,
# solange sich die Paketreferenzen nicht ändern.
COPY src/SharpBoxAI.ImapMcpServer/SharpBoxAI.ImapMcpServer.csproj src/SharpBoxAI.ImapMcpServer/
RUN dotnet restore src/SharpBoxAI.ImapMcpServer/SharpBoxAI.ImapMcpServer.csproj -a $TARGETARCH

COPY src/SharpBoxAI.ImapMcpServer/ src/SharpBoxAI.ImapMcpServer/
# Nicht self-contained: die .NET-Runtime kommt aus dem Runtime-Image darunter.
RUN dotnet publish src/SharpBoxAI.ImapMcpServer/SharpBoxAI.ImapMcpServer.csproj \
    -c Release -a $TARGETARCH --no-restore --no-self-contained -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Der Server lauscht im HTTP-Modus fest auf 0.0.0.0:5100 (siehe Program.cs).
EXPOSE 5100

# Nicht als root laufen - der Benutzer "app" ist in den .NET-Runtime-Images vorhanden.
USER app

# Credentials kommen als Umgebungsvariablen (IMAP_HOST / IMAP_USER / IMAP_PASSWORD)
# in den Container, nicht ins Image.
ENTRYPOINT ["dotnet", "SharpBoxAI.ImapMcpServer.dll", "--http"]

# PowerShell equivalent of init-migrations.sh
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK 8 is required (https://dotnet.microsoft.com/download)"
}
try { dotnet ef --version | Out-Null } catch {
    Write-Host "Installing dotnet-ef…"
    dotnet tool install -g dotnet-ef --version 8.*
    $env:PATH = "$env:PATH;$HOME\.dotnet\tools"
}

dotnet ef migrations add Initial --project src/Aethernet.Data --startup-project src/Aethernet.Server
dotnet ef database update         --project src/Aethernet.Data --startup-project src/Aethernet.Server

Write-Host "`nDone. Schema applied."

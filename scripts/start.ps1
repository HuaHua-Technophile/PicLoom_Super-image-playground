#Requires -Version 7.0
<#
.SYNOPSIS
  Starts (or restarts) the local PicLoom API and waits until it answers.

.DESCRIPTION
  The API is a console app; if its process is terminated the port simply stops
  listening and the browser shows "can't reach this page". This script gives a
  single, repeatable entry point:

    ./scripts/start.ps1            # start if not running, otherwise report status
    ./scripts/start.ps1 -Restart   # kill whatever owns the port, then start
    ./scripts/start.ps1 -Stop      # stop the server
    ./scripts/start.ps1 -Build     # build first (npm + dotnet), then start

.PARAMETER Port
  Port to listen on. Defaults to 5278.

.PARAMETER NoBrowser
  Do not open the default browser. Useful for tests and headless runs.
#>
[CmdletBinding()]
param(
    [switch]$Restart,
    [switch]$Stop,
    [switch]$Build,
    [int]$Port = 5278,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root 'src/PicLoom.Api/bin/Debug/net10.0/PicLoom.Api.dll'
$apiDir = Join-Path $root 'src/PicLoom.Api/bin/Debug/net10.0'
$runDir = Join-Path $root 'artifacts/run'
$baseUrl = "http://127.0.0.1:$Port/"

function Get-PortOwner {
    param([int]$LocalPort)
    $connection = Get-NetTCPConnection -LocalPort $LocalPort -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $connection) { return 0 }
    return [int]$connection.OwningProcess
}

function Test-Server {
    param([string]$Url)
    try {
        $response = Invoke-WebRequest -Uri $Url -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Stop-Server {
    $owner = Get-PortOwner -LocalPort $Port
    if ($owner -eq 0) {
        Write-Host "No process is listening on port $Port."
        return
    }
    Stop-Process -Id $owner -Force -ErrorAction Stop
    Write-Host "Stopped PID $owner on port $Port."
}

if ($Stop) {
    Stop-Server
    return
}

if ($Restart) {
    Stop-Server
    Start-Sleep -Milliseconds 800
}
elseif (Test-Server -Url $baseUrl) {
    Write-Host "PicLoom is already running at $baseUrl (PID $(Get-PortOwner -LocalPort $Port))."
    Write-Host 'Use -Restart to restart it.'
    return
}

if ($Build) {
    Write-Host 'Building web assets...'
    & npm run build --prefix (Join-Path $root 'src/PicLoom.Web')
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }

    Write-Host 'Building the solution...'
    & dotnet build (Join-Path $root 'PicLoom.sln') -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

if (-not (Test-Path $dll)) {
    throw "API binary not found at $dll. Run with -Build first."
}

New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$arguments = @("`"$dll`"")
if (-not $NoBrowser) { $arguments += '--open-browser' }

$process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -WorkingDirectory $apiDir `
    -RedirectStandardOutput (Join-Path $runDir 'api.out.log') `
    -RedirectStandardError (Join-Path $runDir 'api.err.log') `
    -PassThru

Set-Content -Path (Join-Path $runDir 'api.pid') -Value $process.Id

$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    if ($process.HasExited) {
        $stderr = Get-Content (Join-Path $runDir 'api.err.log') -ErrorAction SilentlyContinue | Out-String
        throw "PicLoom exited immediately (code $($process.ExitCode)). stderr: $stderr"
    }
    if (Test-Server -Url $baseUrl) {
        Write-Host "PicLoom is up at $baseUrl (PID $($process.Id))."
        Write-Host "Logs: artifacts/run/api.out.log / api.err.log"
        return
    }
    Start-Sleep -Milliseconds 400
}

throw "Timed out waiting for $baseUrl. Check artifacts/run/api.err.log."

# Usage: ./scripts/logs.ps1 [-Lines 80] [-TaskId <guid>] [-Stream out|err]   -- read the UTF-8 PicLoom log, optionally for one task
#Requires -Version 7.0
<#
.SYNOPSIS
  Prints the tail of the PicLoom API log written by scripts/start.ps1.

.DESCRIPTION
  Every structured event is one line, so a failure can be read straight from the log:
    provider.dispatch  taskId=… transport=json|multipart path=/v1/images/generations size=…
    provider.success   taskId=… status=200 elapsedMs=… images=… bytes=…
    provider.failure   taskId=… status=… elapsedMs=… errorKind=… errorSnippet=…
    task.transition    phase=… taskId=… from=… to=… errorCode=…
  The API key is never logged, and the prompt is truncated to 60 characters.

.PARAMETER Lines
  How many trailing lines to show. Defaults to 80. Ignored when -TaskId is used.

.PARAMETER TaskId
  Filter the whole log down to the lines that mention this task id.

.PARAMETER Stream
  'out' (default) reads artifacts/run/api.out.log, 'err' reads api.err.log.

.EXAMPLE
  ./scripts/logs.ps1

.EXAMPLE
  ./scripts/logs.ps1 -TaskId 3f2504e0-4f89-11d3-9a0c-0305e82c3301

.EXAMPLE
  ./scripts/logs.ps1 -Lines 200 -Stream err
#>
[CmdletBinding()]
param(
    [int]$Lines = 80,
    [guid]$TaskId,
    [ValidateSet('out', 'err')]
    [string]$Stream = 'out'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root = Split-Path $PSScriptRoot -Parent
$logFile = Join-Path $root "artifacts/run/api.$Stream.log"

if (-not (Test-Path $logFile)) {
    Write-Host "No log file at $logFile."
    Write-Host 'Start the API first: ./scripts/start.ps1 -Build -NoBrowser'
    return
}

if ($PSBoundParameters.ContainsKey('TaskId') -and $TaskId -ne [guid]::Empty) {
    $hyphenated = $TaskId.ToString()
    $compact = $TaskId.ToString('N')
    $taskLines = Get-Content -Path $logFile -Encoding utf8 | Where-Object {
        $_ -match [regex]::Escape($hyphenated) -or $_ -match [regex]::Escape($compact)
    }

    if (-not $taskLines) {
        Write-Host "No log lines mention task $hyphenated in $logFile."
        return
    }

    Write-Host "# $($taskLines.Count) line(s) for task $hyphenated in $logFile"
    $taskLines
    return
}

if ($Lines -lt 1) { $Lines = 80 }
$tail = Get-Content -Path $logFile -Tail $Lines -Encoding utf8
Write-Host "# last $Lines line(s) of $logFile"
$tail

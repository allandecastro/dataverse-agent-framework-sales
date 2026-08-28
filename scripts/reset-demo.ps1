<#
.SYNOPSIS
    Resets the demo to its canonical state so every run reads identical data.

.DESCRIPTION
    The Operator now writes real opportunity edits (stage, close date) once approved, and those
    mutate the very fields the Watcher reads on the next run. Run this before each demo or
    rehearsal to keep the "same result every run" gate true:

      1. seed.ps1                - restores every opportunity (stage, close date, and the rest)
                                   to data/dataset.json, idempotently by deterministic id.
      2. cleanup-demo-writes.ps1 - deletes the tasks and appointments the agents created.

    Uses the signed-in az identity - a maintenance identity, never the agent application users
    (the reader and writer). Run `az login` first.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL. Defaults to the demo environment.
#>
param(
    [string] $EnvironmentUrl = 'https://dv-mafdemo-dev.crm4.dynamics.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "Resetting the demo fixture in $EnvironmentUrl"
Write-Host "1/2 Restoring opportunities from dataset.json..."
& (Join-Path $PSScriptRoot 'seed.ps1') -EnvironmentUrl $EnvironmentUrl
Write-Host "2/2 Removing the agent-created tasks and appointments..."
& (Join-Path $PSScriptRoot 'cleanup-demo-writes.ps1') -EnvironmentUrl $EnvironmentUrl
Write-Host "Reset complete. Every run now starts from the same state."

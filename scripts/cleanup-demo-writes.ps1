<#
.SYNOPSIS
    Removes the tasks and appointments the demo created, so a rehearsal starts clean.

.DESCRIPTION
    Demo writes run under the writer application user (Zava zava-operator), so everything
    it created is owned by that user. This deletes every task and appointment owned by it.

    It does not revert opportunity stage or close-date changes - those are updates, not
    creates. Re-run scripts/seed.ps1 to reset the opportunities if needed.

    Idempotent and read-then-delete: prints what it will remove and the running count.
    Uses the signed-in az identity for the Dataverse token, so run `az login` first.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL, e.g. https://dv-mafdemo-dev.crm4.dynamics.com

.PARAMETER WhatIf
    List what would be deleted without deleting anything.
#>
param(
    [Parameter(Mandatory)] [string] $EnvironmentUrl,
    [string] $ApiVersion = 'v9.2',
    [string] $WriterUserLastName = 'zava-operator',
    [switch] $WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resource = $EnvironmentUrl.TrimEnd('/')
$baseUrl = "$resource/api/data/$ApiVersion"

# Shared Web API plumbing (token, headers, Invoke-Dataverse).
. (Join-Path $PSScriptRoot '_dataverse.ps1')
Connect-Dataverse -Resource $resource

# Resolve the writer application user by name.
$userQuery = "$baseUrl/systemusers?`$select=systemuserid,fullname&`$filter=lastname eq '$WriterUserLastName'"
$writer = (Invoke-Dataverse -Method Get -Uri $userQuery).value | Select-Object -First 1
if (-not $writer) { throw "Writer application user (lastname '$WriterUserLastName') not found. Provision first." }
Write-Host "Writer user: $($writer.fullname) ($($writer.systemuserid))"

$removed = 0
foreach ($entity in @('tasks', 'appointments')) {
    $query = "$baseUrl/$entity`?`$select=activityid,subject&`$filter=_ownerid_value eq $($writer.systemuserid)"
    $items = (Invoke-Dataverse -Method Get -Uri $query).value
    Write-Host "$entity owned by the writer: $($items.Count)"
    foreach ($item in $items) {
        Write-Host "  - $($item.subject)"
        if (-not $WhatIf) {
            Invoke-Dataverse -Method Delete -Uri "$baseUrl/$entity($($item.activityid))" | Out-Null
            $removed++
        }
    }
}

if ($WhatIf) { Write-Host "WhatIf: nothing deleted." }
else { Write-Host "Deleted $removed activities." }

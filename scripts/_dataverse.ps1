#Requires -Version 7.0
<#
.SYNOPSIS
    Shared Dataverse Web API helpers, dot-sourced by seed.ps1, provision.ps1 and
    cleanup-demo-writes.ps1.

.DESCRIPTION
    The token acquisition, the request headers, and the REST call were identical in three
    scripts; they live here now so there is one place to fix. Everything else stays in the
    script that needs it - Get-DeterministicGuid and the seed record helpers are seeding
    concerns, not shared plumbing, so they remain in seed.ps1.

    Dot-sourced, not a module: `. (Join-Path $PSScriptRoot '_dataverse.ps1')`. The functions
    then run in the caller's scope, so Connect-Dataverse sets $script:headers where the
    caller's own Invoke-Dataverse calls read it.
#>

# Acquire an interactive Azure CLI token for the environment and store the request headers
# in the caller's script scope. Run `az login` first.
function Connect-Dataverse {
    param([Parameter(Mandatory)] [string] $Resource)
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI (az) not found. Install it and run 'az login' first."
    }
    $token = az account get-access-token --resource $Resource --query accessToken -o tsv
    if (-not $token) { throw "Could not acquire a token for $Resource. Run 'az login' first." }
    $script:headers = @{
        Authorization      = "Bearer $token"
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Accept             = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
    }
}

# One REST call against the Web API, using the headers Connect-Dataverse set. Serialises the
# body to JSON and surfaces Dataverse's own error detail on failure.
function Invoke-Dataverse {
    param([string] $Method, [string] $Uri, $Body)
    # TimeoutSec so a request that never comes back fails fast instead of hanging the whole seed.
    $params = @{ Method = $Method; Uri = $Uri; Headers = $script:headers; TimeoutSec = 100 }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 12) }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        throw "Dataverse $Method $Uri failed: $($_.Exception.Message)`n$($_.ErrorDetails.Message)"
    }
}

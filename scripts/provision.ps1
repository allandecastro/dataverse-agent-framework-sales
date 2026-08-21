#Requires -Version 7.0
<#
.SYNOPSIS
    Provisions the two agent identities and their Dataverse security roles. Idempotent.

.DESCRIPTION
    Creates, for the identity boundary of phase 4:
      - Two Entra app registrations, zava-watcher and zava-operator, each with a client
        secret written to user secrets (never the repo).
      - Two Dataverse application users bound to those apps.
      - Two security roles with least-privilege scope:
          Zava Agent Reader - read only on account, opportunity, activitypointer,
            annotation, contact.
          Zava Agent Writer - create on task and appointment, write on opportunity.

    Naming convention (consistent, versioned):
      - Entra app registrations: zava-watcher, zava-operator (lowercase, hyphenated).
      - Dataverse publisher: display "Zava", customization prefix "zava".
      - Security roles: "Zava Agent Reader", "Zava Agent Writer".

    Modern alternative: under Microsoft Agent 365 / Entra Agent ID (preview), each agent
    would be registered as an agent identity rather than a classic app registration, and
    named with an Agent 365 display name. That path is tenant-eligibility gated, so this
    script uses classic app registrations as the reliable route. See docs/identity.md.

    ALLAN owns this: validate the role scopes below before running, run it yourself with
    tenant admin, and never commit the secrets it prints.

    Prerequisites: az login (tenant admin), and az account get-access-token access to the
    Dataverse environment.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL, for example https://org.crm.dynamics.com.

.PARAMETER TenantId
    The Entra tenant id. Defaults to the current az login tenant.

.EXAMPLE
    ./scripts/provision.ps1 -EnvironmentUrl https://dv-mafdemo-dev.crm4.dynamics.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EnvironmentUrl,
    [string] $TenantId,
    [string] $ApiVersion = 'v9.2'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Naming (edit here, one place) --------------------------------------------

$Agents = @(
    @{ App = 'zava-watcher';  Role = 'Zava Agent Reader'; Kind = 'reader' }
    @{ App = 'zava-operator'; Role = 'Zava Agent Writer'; Kind = 'writer' }
)
$PublisherPrefix = 'zava'

# Least-privilege role scopes. VALIDATE THESE before running. Depth: Basic, Local, Deep,
# Global (user, business unit, parent-child, organization).
$ReaderPrivileges = @(
    @{ Name = 'prvReadAccount';         Depth = 'Global' }
    @{ Name = 'prvReadOpportunity';     Depth = 'Global' }
    @{ Name = 'prvReadActivity';        Depth = 'Global' }  # activitypointer
    @{ Name = 'prvReadNote';            Depth = 'Global' }  # annotation
    @{ Name = 'prvReadContact';         Depth = 'Global' }
)
$WriterPrivileges = @(
    @{ Name = 'prvCreateTask';          Depth = 'Global' }
    @{ Name = 'prvCreateAppointment';   Depth = 'Global' }
    @{ Name = 'prvWriteOpportunity';    Depth = 'Global' }  # field scope below
    @{ Name = 'prvReadOpportunity';     Depth = 'Global' }  # needed to update
)

# --- Setup --------------------------------------------------------------------

$resource = $EnvironmentUrl.TrimEnd('/')
$baseUrl = "$resource/api/data/$ApiVersion"

if (-not $TenantId) { $TenantId = az account show --query tenantId -o tsv }
Write-Host "Tenant $TenantId, environment $resource"

$token = az account get-access-token --resource $resource --query accessToken -o tsv
if (-not $token) { throw "Could not get a Dataverse token. Run 'az login' first." }
$script:headers = @{
    Authorization      = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Accept             = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
}

function Invoke-Dataverse {
    param([string] $Method, [string] $Uri, $Body)
    $params = @{ Method = $Method; Uri = $Uri; Headers = $script:headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }
    try { return Invoke-RestMethod @params }
    catch { throw "Dataverse $Method $Uri failed: $($_.Exception.Message)`n$($_.ErrorDetails.Message)" }
}

$who = Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/WhoAmI"
$businessUnitId = $who.BusinessUnitId

# Privilege ids by name, resolved once.
function Get-PrivilegeId {
    param([string] $Name)
    $result = Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/privileges?`$select=privilegeid&`$filter=name eq '$Name'"
    if (-not $result.value) { throw "Privilege '$Name' not found." }
    return $result.value[0].privilegeid
}

$depthMap = @{ Basic = 'Basic'; Local = 'Local'; Deep = 'Deep'; Global = 'Global' }

# --- Provision each agent -----------------------------------------------------

foreach ($agent in $Agents) {
    Write-Host ""
    Write-Host "=== $($agent.App) ==="

    # 1. Entra app registration (idempotent by displayName).
    $appId = az ad app list --filter "displayName eq '$($agent.App)'" --query "[0].appId" -o tsv
    if (-not $appId) {
        $appId = az ad app create --display-name $agent.App --sign-in-audience AzureADMyOrg --query appId -o tsv
        Write-Host "  created app registration, appId $appId"
    }
    else { Write-Host "  app registration exists, appId $appId" }

    # Client secret, printed for user secrets. Never commit it.
    $secret = az ad app credential reset --id $appId --display-name 'zava' --query password -o tsv
    Write-Host "  client secret (store in user secrets, do not commit):"
    Write-Host "    $($agent.Kind) clientId=$appId  secret=$secret"

    # 2. Dataverse application user bound to the app.
    $existing = Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/systemusers?`$select=systemuserid&`$filter=applicationid eq $appId"
    if (-not $existing.value) {
        $user = @{
            'applicationid'                          = $appId
            'businessunitid@odata.bind'              = "/businessunits($businessUnitId)"
            'firstname'                              = 'Zava'
            'lastname'                               = $agent.App
        }
        Invoke-Dataverse -Method 'Post' -Uri "$baseUrl/systemusers" -Body $user | Out-Null
        Write-Host "  created application user"
    }
    else { Write-Host "  application user exists" }

    # 3. Security role with least-privilege scope (idempotent by name).
    $roleResult = Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/roles?`$select=roleid&`$filter=name eq '$($agent.Role)'"
    if ($roleResult.value) {
        $roleId = $roleResult.value[0].roleid
        Write-Host "  role '$($agent.Role)' exists"
    }
    else {
        $created = Invoke-Dataverse -Method 'Post' -Uri "$baseUrl/roles" -Body @{
            name                        = $agent.Role
            'businessunitid@odata.bind' = "/businessunits($businessUnitId)"
        }
        # Post returns no body by default; re-query for the id.
        $roleId = (Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/roles?`$select=roleid&`$filter=name eq '$($agent.Role)'").value[0].roleid
        Write-Host "  created role '$($agent.Role)'"
    }

    $privileges = if ($agent.Kind -eq 'reader') { $ReaderPrivileges } else { $WriterPrivileges }
    $rolePrivileges = @($privileges | ForEach-Object {
        @{ PrivilegeId = (Get-PrivilegeId $_.Name); Depth = $depthMap[$_.Depth] }
    })
    Invoke-Dataverse -Method 'Post' -Uri "$baseUrl/roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" `
        -Body @{ Privileges = $rolePrivileges } | Out-Null
    Write-Host "  assigned $($privileges.Count) privileges"

    # Assign the role to the application user.
    $userId = (Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/systemusers?`$select=systemuserid&`$filter=applicationid eq $appId").value[0].systemuserid
    Invoke-Dataverse -Method 'Post' -Uri "$baseUrl/systemusers($userId)/systemuserroles_association/`$ref" `
        -Body @{ '@odata.id' = "$baseUrl/roles($roleId)" } | Out-Null
    Write-Host "  assigned role to the application user"
}

Write-Host ""
Write-Host "Done. Store the printed client ids and secrets in user secrets:"
Write-Host "  ZAVA_TENANT_ID, ZAVA_READER_CLIENT_ID/SECRET, ZAVA_WRITER_CLIENT_ID/SECRET"
Write-Host ""
Write-Host "NOTE: the writer role grants entity-level write on opportunity. Restricting it"
Write-Host "to the stage and estimated close date fields needs a Field Security Profile on"
Write-Host "those two secured fields - configure that in the maker portal and validate it."

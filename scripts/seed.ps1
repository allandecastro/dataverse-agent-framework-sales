#Requires -Version 7.0
<#
.SYNOPSIS
    Seeds the phase 2 demo dataset into a Dataverse environment. Idempotent.

.DESCRIPTION
    Reads data/dataset.json and upserts accounts, contacts, opportunities,
    activities and notes into Dataverse. Every record gets a deterministic GUID
    derived from its symbolic key, so ids are stable across runs and the Thinker
    can cite ids that exist. Relative date offsets are resolved to absolute dates
    at seed time, so the signals stay true whenever the dataset is re-seeded.

    Idempotency: records are written with PATCH to a known id, which upserts.
    Re-running updates in place, it never duplicates.

    Authentication uses your interactive Azure CLI login (application users come
    later, in phase 4). The signed-in identity must have write access to the
    environment. Prerequisite: az login.

    Not seeded:
      - The expected.archetype oracle. The model must derive the archetype.
      - closeDateHistory. The "close date moved twice" Slipping signal depends on
        audit history, which is environment dependent. The account still resolves
        to Slipping through the named competitor and the deactivated champion, so
        the classification does not rely on it.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL, for example https://org.crm.dynamics.com.

.PARAMETER DatasetPath
    Path to dataset.json. Defaults to ../data/dataset.json.

.PARAMETER OwnerId
    Optional systemuserid to own the seeded records. Defaults to the caller.

.EXAMPLE
    ./scripts/seed.ps1 -EnvironmentUrl https://org.crm.dynamics.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EnvironmentUrl,
    [string] $DatasetPath = (Join-Path $PSScriptRoot '..' 'data' 'dataset.json'),
    [string] $ApiVersion = 'v9.2',
    [string] $OwnerId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Configuration ------------------------------------------------------------

$resource = $EnvironmentUrl.TrimEnd('/')
$baseUrl = "$resource/api/data/$ApiVersion"
$anchor = [DateTime]::UtcNow.Date  # all offsets are days from here

$stageMap = @{ Qualify = 0; Develop = 1; Propose = 2; Close = 3 }
$activityMap = @{
    phonecall   = @{ set = 'phonecalls';   regard = 'regardingobjectid_account_phonecall' }
    email       = @{ set = 'emails';       regard = 'regardingobjectid_account_email' }
    appointment = @{ set = 'appointments'; regard = 'regardingobjectid_account_appointment' }
    task        = @{ set = 'tasks';        regard = 'regardingobjectid_account_task' }
}

# --- Helpers ------------------------------------------------------------------

function Get-DataverseToken {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI (az) not found. Install it and run 'az login' first."
    }
    $token = az account get-access-token --resource $resource --query accessToken -o tsv
    if (-not $token) { throw "Could not acquire a token for $resource. Run 'az login' first." }
    return $token
}

function Get-DeterministicGuid {
    param([string] $Key)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Key))
        return [Guid]::new($bytes)
    }
    finally { $md5.Dispose() }
}

function Invoke-Dataverse {
    param([string] $Method, [string] $Uri, $Body)
    $params = @{ Method = $Method; Uri = $Uri; Headers = $script:headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 12) }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        $detail = $_.ErrorDetails.Message
        throw "Dataverse $Method $Uri failed: $($_.Exception.Message)`n$detail"
    }
}

function Set-Record {
    param([string] $EntitySet, [Guid] $Id, [hashtable] $Body)
    Invoke-Dataverse -Method 'Patch' -Uri "$baseUrl/$EntitySet($($Id.ToString()))" -Body $Body | Out-Null
}

function Set-State {
    param([string] $EntitySet, [Guid] $Id, [int] $StateCode, [int] $StatusCode)
    Set-Record -EntitySet $EntitySet -Id $Id -Body @{ statecode = $StateCode; statuscode = $StatusCode }
}

function Resolve-DateTime {
    param([int] $Offset)
    return $anchor.AddDays($Offset).AddHours(9)  # 09:00 UTC, the hour is not a signal
}

function Format-DateOnly { param([DateTime] $Value) return $Value.ToString('yyyy-MM-dd') }
function Format-DateTime { param([DateTime] $Value) return $Value.ToString('yyyy-MM-ddTHH:mm:ssZ') }

function Split-PersonName {
    param([string] $FullName)
    $parts = $FullName.Trim() -split '\s+', 2
    if ($parts.Count -eq 2) { return @{ First = $parts[0]; Last = $parts[1] } }
    return @{ First = $FullName; Last = '' }
}

# --- Connect ------------------------------------------------------------------

$token = Get-DataverseToken
$script:headers = @{
    Authorization      = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Accept             = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
}

$who = Invoke-Dataverse -Method 'Get' -Uri "$baseUrl/WhoAmI"
$owner = if ($OwnerId) { $OwnerId } else { $who.UserId }
$ownerBind = "/systemusers($owner)"
Write-Host "Connected to $resource as owner $owner"

# --- Seed ---------------------------------------------------------------------

$dataset = Get-Content -Raw -Path $DatasetPath | ConvertFrom-Json
$counts = @{ accounts = 0; contacts = 0; opportunities = 0; activities = 0; notes = 0 }

foreach ($account in $dataset.accounts) {
    $accountId = Get-DeterministicGuid $account.key
    Set-Record -EntitySet 'accounts' -Id $accountId -Body @{
        name              = $account.name
        accountnumber     = $account.key
        description       = $account.industry
        'ownerid@odata.bind' = $ownerBind
    }
    $counts.accounts++
    $accountBind = "/accounts($($accountId.ToString()))"

    foreach ($contact in $account.contacts) {
        $contactId = Get-DeterministicGuid $contact.key
        $name = Split-PersonName $contact.fullName
        Set-Record -EntitySet 'contacts' -Id $contactId -Body @{
            firstname = $name.First
            lastname  = $name.Last
            jobtitle  = $contact.jobTitle
            'parentcustomerid_account@odata.bind' = $accountBind
            'ownerid@odata.bind'                  = $ownerBind
        }
        # Deactivated contacts carry the Slipping and AtRisk signals. Set state
        # after the upsert, since a record cannot be created already inactive.
        if (-not $contact.isActive) {
            Set-State -EntitySet 'contacts' -Id $contactId -StateCode 1 -StatusCode 2
        }
        $counts.contacts++
    }

    foreach ($opportunity in $account.opportunities) {
        $opportunityId = Get-DeterministicGuid $opportunity.key
        $closeDate = $anchor.AddDays([int]$opportunity.closeDateOffsetDays)
        Set-Record -EntitySet 'opportunities' -Id $opportunityId -Body @{
            name               = $opportunity.name
            estimatedvalue     = [decimal]$opportunity.amount
            estimatedclosedate = (Format-DateOnly $closeDate)
            salesstage         = $stageMap[$opportunity.stage]
            stepname           = $opportunity.stage
            'customerid_account@odata.bind' = $accountBind
            'ownerid@odata.bind'            = $ownerBind
        }
        $counts.opportunities++
    }

    foreach ($activity in $account.activities) {
        $map = $activityMap[$activity.type]
        if (-not $map) { throw "Unknown activity type '$($activity.type)' on $($account.key)." }
        $activityId = Get-DeterministicGuid $activity.key
        $start = Resolve-DateTime ([int]$activity.dateOffsetDays)
        $end = $start.AddMinutes(30)
        # Scheduled and actual are both set so whichever field the Watcher reads
        # carries the real date. The temporal signals live here, not in createdon.
        Set-Record -EntitySet $map.set -Id $activityId -Body @{
            subject       = $activity.subject
            description   = $activity.description
            scheduledstart = (Format-DateTime $start)
            scheduledend   = (Format-DateTime $end)
            actualstart    = (Format-DateTime $start)
            actualend      = (Format-DateTime $end)
            "$($map.regard)@odata.bind" = $accountBind
            'ownerid@odata.bind'        = $ownerBind
        }
        $counts.activities++
    }

    foreach ($note in $account.notes) {
        $noteId = Get-DeterministicGuid $note.key
        $isOpportunity = $note.regardingKey -like 'opp-*'
        $regardKey = if ($isOpportunity) { Get-DeterministicGuid $note.regardingKey } else { $accountId }
        $regardBind = if ($isOpportunity) { "objectid_opportunity@odata.bind" } else { "objectid_account@odata.bind" }
        $regardTarget = if ($isOpportunity) { "/opportunities($($regardKey.ToString()))" } else { $accountBind }
        Set-Record -EntitySet 'annotations' -Id $noteId -Body @{
            subject       = $note.subject
            notetext      = $note.body
            $regardBind   = $regardTarget
        }
        $counts.notes++
    }

    Write-Host "  seeded $($account.key) $($account.name)"
}

Write-Host ""
Write-Host "Done. Accounts $($counts.accounts), contacts $($counts.contacts), opportunities $($counts.opportunities), activities $($counts.activities), notes $($counts.notes)."

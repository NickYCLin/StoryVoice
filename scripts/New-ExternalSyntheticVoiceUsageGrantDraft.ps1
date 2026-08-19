[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ConsumerKeyId,
    [Parameter(Mandatory = $true)] [string] $SyntheticVoiceAuthorizationPath,
    [Parameter(Mandatory = $true)] [string] $ProjectId,
    [Parameter(Mandatory = $true)] [string] $ConsumerFamilyId,
    [Parameter(Mandatory = $true)] [string] $TerritoryCountryCode,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $EffectiveAtUtc,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $ExpiresAtUtc,
    [Parameter(Mandatory = $true)] [ValidateSet('confirm')] [string] $CommercialScopeConfirmation,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SyntheticVoiceArtifactCommon.ps1')

$authorization = & (Join-Path $PSScriptRoot 'Test-SyntheticVoiceAuthorization.ps1') `
    -Path $SyntheticVoiceAuthorizationPath

if ($ConsumerKeyId -cnotmatch '^[a-z0-9][a-z0-9_-]{7,63}$') {
    throw 'ConsumerKeyId must be 8-64 lowercase ASCII characters without wildcards.'
}
Assert-SvCanonicalId -Name 'ProjectId' -Value $ProjectId
Assert-SvCanonicalId -Name 'ConsumerFamilyId' -Value $ConsumerFamilyId
Assert-SvTerritoryValue -Mode 'country-list' -CountryCodes @($TerritoryCountryCode)
if ($authorization.AllowedConsumerFamilies -cnotcontains $ConsumerFamilyId) {
    throw 'ConsumerFamilyId is not included in the active synthetic voice authorization.'
}
if ($authorization.TerritoryMode -ceq 'country-list' -and
    $authorization.TerritoryCountryCodes -cnotcontains $TerritoryCountryCode) {
    throw 'TerritoryCountryCode is not included in the active synthetic voice authorization.'
}

$effectiveUtc = $EffectiveAtUtc.ToUniversalTime()
$expiresUtc = $ExpiresAtUtc.ToUniversalTime()
if ($expiresUtc -le $effectiveUtc) {
    throw 'ExpiresAtUtc must be later than EffectiveAtUtc.'
}
if ($effectiveUtc -le [DateTimeOffset]::UtcNow) {
    throw 'EffectiveAtUtc must be in the future so a later authenticated owner action can activate the draft.'
}
if ($effectiveUtc -lt $authorization.EffectiveAtUtc -or
    $expiresUtc -gt $authorization.ExpiresAtUtc) {
    throw 'The usage-grant time range must be contained by the active synthetic voice authorization.'
}

$grantId = 'svg_' + [Guid]::NewGuid().ToString('N')
$artifact = [ordered]@{
    schema = 'voice-api-synthetic-usage-grant/v1'
    grantId = $grantId
    consumerKeyId = $ConsumerKeyId
    ownerId = $authorization.OwnerId
    voiceAlias = $authorization.VoiceAlias
    characterProfileId = $authorization.CharacterProfileId
    syntheticVoiceAuthorizationSha256 = $authorization.AuthorizationSha256
    projectId = $ProjectId
    consumerFamilyId = $ConsumerFamilyId
    territoryCountryCode = $TerritoryCountryCode
    effectiveAtUtc = $effectiveUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    expiresAtUtc = $expiresUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    revokedAtUtc = $null
    activation = [ordered]@{
        state = 'draft'
        accountSubjectId = $null
        auditEventId = $null
        issuedAtUtc = $null
    }
}

$fullPath = Write-SvCreateNewJson -Artifact $artifact -OutputPath $OutputPath
[pscustomobject]@{
    Status = 'draft'
    GrantId = $grantId
    OutputPath = $fullPath
    DraftSha256 = Get-SvFileSha256 -Path $fullPath
}

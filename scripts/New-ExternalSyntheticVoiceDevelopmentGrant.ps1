[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ConsumerKeyId,
    [Parameter(Mandatory = $true)] [string] $ProjectId,
    [Parameter(Mandatory = $true)] [Guid] $OwnerId,
    [Parameter(Mandatory = $true)] [string] $VoiceAlias,
    [Parameter(Mandatory = $true)] [Guid] $CharacterProfileId,
    [Parameter(Mandatory = $true)] [string] $ReferenceAudioPath,
    [Parameter(Mandatory = $true)] [string] $ExpectedTranscriptCanonicalPath,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $EffectiveAtUtc,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $ExpiresAtUtc,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SyntheticVoiceArtifactCommon.ps1')

if ($ConsumerKeyId -cnotmatch '^[a-z0-9][a-z0-9_-]{7,63}$') {
    throw 'ConsumerKeyId must be 8-64 lowercase ASCII characters without wildcards.'
}
Assert-SvCanonicalId -Name 'ProjectId' -Value $ProjectId
Assert-SvVoiceAlias -Name 'VoiceAlias' -Value $VoiceAlias
$ownerIdValue = ConvertTo-SvGuidString -Value $OwnerId -Name 'OwnerId'
$characterProfileIdValue = ConvertTo-SvGuidString `
    -Value $CharacterProfileId `
    -Name 'CharacterProfileId'

$referenceAudioFullPath = [IO.Path]::GetFullPath($ReferenceAudioPath)
if ([IO.Path]::GetExtension($referenceAudioFullPath) -cne '.wav') {
    throw 'ReferenceAudioPath must point to a .wav file.'
}
$referenceAudioSha256 = Get-SvFileSha256 `
    -Path $referenceAudioFullPath `
    -MaximumBytes (10 * 1024 * 1024)
$transcriptCanonicalSha256 = Get-SvCanonicalTranscriptSha256 `
    -Path $ExpectedTranscriptCanonicalPath

$effectiveUtc = $EffectiveAtUtc.ToUniversalTime()
$expiresUtc = $ExpiresAtUtc.ToUniversalTime()
if ($expiresUtc -le $effectiveUtc) {
    throw 'ExpiresAtUtc must be later than EffectiveAtUtc.'
}
if ($expiresUtc -le [DateTimeOffset]::UtcNow) {
    throw 'ExpiresAtUtc must be in the future.'
}
if ($expiresUtc - $effectiveUtc -gt [TimeSpan]::FromDays(30)) {
    throw 'A private development grant cannot exceed 30 days.'
}

$grantId = 'svd_' + [Guid]::NewGuid().ToString('N')
$artifact = [ordered]@{
    schema = 'voice-api-synthetic-development-grant/v1'
    grantId = $grantId
    consumerKeyId = $ConsumerKeyId
    ownerId = $ownerIdValue
    voiceAlias = $VoiceAlias
    characterProfileId = $characterProfileIdValue
    referenceAudioSha256 = $referenceAudioSha256
    expectedTranscriptCanonicalSha256 = $transcriptCanonicalSha256
    projectId = $ProjectId
    effectiveAtUtc = $effectiveUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    expiresAtUtc = $expiresUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    revokedAtUtc = $null
    origin = 'owner-created-synthetic-no-human-source-no-identifiable-imitation'
}

$fullPath = Write-SvCreateNewJson -Artifact $artifact -OutputPath $OutputPath
[pscustomobject]@{
    AccessTier = 'private-development'
    GrantId = $grantId
    OutputPath = $fullPath
    GrantSha256 = Get-SvFileSha256 -Path $fullPath
}

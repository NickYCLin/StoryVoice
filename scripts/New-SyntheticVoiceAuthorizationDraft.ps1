[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [Guid] $OwnerId,
    [Parameter(Mandatory = $true)] [string] $VoiceAlias,
    [Parameter(Mandatory = $true)] [Guid] $CharacterProfileId,
    [Parameter(Mandatory = $true)] [string] $ProviderId,
    [Parameter(Mandatory = $true)] [string] $ToolId,
    [Parameter(Mandatory = $true)] [string] $ModelId,
    [Parameter(Mandatory = $true)] [string] $ModelRevision,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $CreatedAtUtc,
    [Parameter(Mandatory = $true)] [string] $GenerationManifestPath,
    [Parameter(Mandatory = $true)] [string] $LicenseIdentifier,
    [Parameter(Mandatory = $true)] [string] $TermsUri,
    [Parameter(Mandatory = $true)] [string] $TermsSnapshotPath,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $TermsAcceptedAtUtc,
    [Parameter(Mandatory = $true)] [string] $ReferenceAudioPath,
    [Parameter(Mandatory = $true)] [string] $ExpectedTranscriptCanonicalPath,
    [Parameter(Mandatory = $true)] [ValidateSet('confirm')] [string] $SourceClaimsConfirmation,
    [Parameter(Mandatory = $true)] [string] $DisplayName,
    [AllowNull()] [AllowEmptyString()] [string] $AttributionText = $null,
    [Parameter(Mandatory = $true)] [ValidateSet('confirm')] [string] $AttributionDisplayConfirmation,
    [Parameter(Mandatory = $true)] [string[]] $Styles,
    [Parameter(Mandatory = $true)] [string[]] $UseCases,
    [Parameter(Mandatory = $true)] [string] $FixedDemoPath,
    [Parameter(Mandatory = $true)] [ValidateSet('confirm')] [string] $PermissionsConfirmation,
    [Parameter(Mandatory = $true)] [string[]] $AllowedConsumerFamilies,
    [Parameter(Mandatory = $true)] [ValidateSet('country-list', 'worldwide')] [string] $TerritoryMode,
    [string[]] $TerritoryCountryCodes = @(),
    [Parameter(Mandatory = $true)] [DateTimeOffset] $EffectiveAtUtc,
    [Parameter(Mandatory = $true)] [DateTimeOffset] $ExpiresAtUtc,
    [Parameter(Mandatory = $true)] [string] $RevocationContact,
    [Parameter(Mandatory = $true)] [string] $RevocationProcess,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SyntheticVoiceArtifactCommon.ps1')

Assert-SvCanonicalId -Name 'ProviderId' -Value $ProviderId -MaximumLength 128
$ownerId = ConvertTo-SvGuidString -Value $OwnerId -Name 'OwnerId'
$characterProfileId = ConvertTo-SvGuidString -Value $CharacterProfileId -Name 'CharacterProfileId'
Assert-SvVoiceAlias -Name 'VoiceAlias' -Value $VoiceAlias
foreach ($field in @{
    ToolId = $ToolId
    ModelId = $ModelId
    ModelRevision = $ModelRevision
    LicenseIdentifier = $LicenseIdentifier
}.GetEnumerator()) {
    Assert-SvPrintableString -Name $field.Key -Value $field.Value -MaximumLength 200
}
Assert-SvHttpsUri -Name 'TermsUri' -Value $TermsUri
Assert-SvPrintableString -Name 'DisplayName' -Value $DisplayName -MaximumLength 120
Assert-SvNoUnsupportedOfficialClaim -Name 'DisplayName' -Value $DisplayName
Assert-SvTextArrayValue -Name 'Styles' -Values $Styles -MaximumCount 8 -MaximumLength 40
Assert-SvTextArrayValue -Name 'UseCases' -Values $UseCases -MaximumCount 8 -MaximumLength 40
foreach ($label in @($Styles) + @($UseCases)) {
    Assert-SvNoUnsupportedOfficialClaim -Name 'Styles/UseCases' -Value $label
}
Assert-SvIdentifierArrayValue -Name 'AllowedConsumerFamilies' -Values $AllowedConsumerFamilies
Assert-SvTerritoryValue -Mode $TerritoryMode -CountryCodes $TerritoryCountryCodes
Assert-SvContact -Name 'RevocationContact' -Value $RevocationContact
Assert-SvPrintableString -Name 'RevocationProcess' -Value $RevocationProcess -MinimumLength 20 -MaximumLength 2000

$normalizedAttributionText = $AttributionText
if ([string]::IsNullOrEmpty($normalizedAttributionText)) {
    $normalizedAttributionText = $null
}
else {
    Assert-SvPrintableString -Name 'AttributionText' -Value $normalizedAttributionText -MaximumLength 500
    Assert-SvNoUnsupportedOfficialClaim -Name 'AttributionText' -Value $normalizedAttributionText
}

$generationManifestSha256 = Get-SvFileSha256 -Path $GenerationManifestPath -MaximumBytes 131072
$termsSnapshotSha256 = Get-SvFileSha256 -Path $TermsSnapshotPath -MaximumBytes 1048576
if ([IO.Path]::GetExtension($ReferenceAudioPath) -ine '.wav' -or
    [IO.Path]::GetExtension($FixedDemoPath) -ine '.wav') {
    throw 'ReferenceAudioPath and FixedDemoPath must use the .wav extension.'
}
$referenceAudioSha256 = Get-SvFileSha256 -Path $ReferenceAudioPath -MaximumBytes 10485760
$transcriptSha256 = Get-SvCanonicalTranscriptSha256 -Path $ExpectedTranscriptCanonicalPath
$fixedDemoSha256 = Get-SvFileSha256 -Path $FixedDemoPath -MaximumBytes 3145728

$createdUtc = $CreatedAtUtc.ToUniversalTime()
$termsAcceptedUtc = $TermsAcceptedAtUtc.ToUniversalTime()
$effectiveUtc = $EffectiveAtUtc.ToUniversalTime()
$expiresUtc = $ExpiresAtUtc.ToUniversalTime()
$now = [DateTimeOffset]::UtcNow
if ($createdUtc -gt $now -or $termsAcceptedUtc -gt $now) {
    throw 'CreatedAtUtc and TermsAcceptedAtUtc cannot be in the future.'
}
if ($termsAcceptedUtc -gt $createdUtc) {
    throw 'TermsAcceptedAtUtc cannot be later than CreatedAtUtc.'
}
if ($createdUtc -gt $effectiveUtc -or $expiresUtc -le $effectiveUtc) {
    throw 'Authorization timestamps must satisfy CreatedAtUtc <= EffectiveAtUtc < ExpiresAtUtc.'
}
if ($effectiveUtc -le $now) {
    throw 'EffectiveAtUtc must be in the future so a later authenticated owner action can activate the draft.'
}

$authorizationId = 'sva_' + [Guid]::NewGuid().ToString('N')
$artifact = [ordered]@{
    schema = 'storyvoice-synthetic-voice-authorization/v1'
    authorizationId = $authorizationId
    ownerId = $ownerId
    voice = [ordered]@{
        alias = $VoiceAlias
        characterProfileId = $characterProfileId
        displayName = $DisplayName
        attributionText = $normalizedAttributionText
        attributionDisplayAllowed = $true
        aiDisclosureRequired = $true
        styles = @($Styles)
        useCases = @($UseCases)
        fixedDemoSha256 = $fixedDemoSha256
        fixedDemoMediaType = 'audio/wav'
    }
    creation = [ordered]@{
        providerId = $ProviderId
        toolId = $ToolId
        modelId = $ModelId
        modelRevision = $ModelRevision
        createdAtUtc = ConvertTo-SvCanonicalUtc $createdUtc
        generationManifestSha256 = $generationManifestSha256
        licenseIdentifier = $LicenseIdentifier
        termsUri = $TermsUri
        termsSnapshotSha256 = $termsSnapshotSha256
        termsAcceptedAtUtc = ConvertTo-SvCanonicalUtc $termsAcceptedUtc
    }
    assetBindings = [ordered]@{
        referenceAudioSha256 = $referenceAudioSha256
        expectedTranscriptCanonicalSha256 = $transcriptSha256
    }
    sourceClaims = [ordered]@{
        allGenerationInputsOwnedOrLicensed = $true
        noHumanVoiceInputProvided = $true
        noHumanBiometricTemplateProvided = $true
        noIdentifiablePersonImitationRequested = $true
        noKnownIdentifiablePersonImitated = $true
        noThirdPartyCharacterOrBrandClaimed = $true
    }
    providerRights = [ordered]@{
        commercialOutputUseAllowed = $false
        publicOutputDistributionAllowed = $false
        apiServiceUseAllowed = $false
        voiceModelDerivationAllowed = $false
    }
    permissions = [ordered]@{
        catalogDisplay = $true
        demoPlayback = $true
        crossProjectApi = $true
        subscriptionOffering = $true
        commercialUse = $true
        publicDistribution = $true
    }
    allowedConsumerFamilies = @($AllowedConsumerFamilies)
    territory = [ordered]@{
        mode = $TerritoryMode
        countryCodes = @($TerritoryCountryCodes)
    }
    externalProviderPolicy = [ordered]@{
        mode = 'prohibited'
        allowedProviderIds = @()
    }
    effectiveAtUtc = ConvertTo-SvCanonicalUtc $effectiveUtc
    expiresAtUtc = ConvertTo-SvCanonicalUtc $expiresUtc
    revocation = [ordered]@{
        scope = 'all-authorized-uses'
        contact = $RevocationContact
        process = $RevocationProcess
        requestedAtUtc = $null
        effectiveAtUtc = $null
    }
    attestation = [ordered]@{
        state = 'draft'
        method = $null
        accountSubjectId = $null
        auditEventId = $null
        attestedAtUtc = $null
        issuedAtUtc = $null
    }
}

$fullPath = Write-SvCreateNewJson -Artifact $artifact -OutputPath $OutputPath
[pscustomobject]@{
    Status = 'draft'
    AuthorizationId = $authorizationId
    OutputPath = $fullPath
    DraftSha256 = Get-SvFileSha256 -Path $fullPath
}

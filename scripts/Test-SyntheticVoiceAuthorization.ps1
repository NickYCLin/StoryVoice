[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [DateTimeOffset] $AtUtc = [DateTimeOffset]::UtcNow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SyntheticVoiceArtifactCommon.ps1')

$opened = $null
try {
    $opened = Open-SvJsonDocument -Path $Path
    $root = $opened.Document.RootElement
    Assert-SvExactObject -Element $root -JsonPath '$' -ExpectedProperties @(
        'schema', 'authorizationId', 'ownerId', 'voice', 'creation', 'assetBindings',
        'sourceClaims', 'providerRights', 'permissions', 'allowedConsumerFamilies',
        'territory', 'externalProviderPolicy',
        'effectiveAtUtc', 'expiresAtUtc', 'revocation', 'attestation')

    $schema = Read-SvString -Element (Get-SvProperty $root 'schema') -JsonPath '$.schema' -MaximumLength 64
    if ($schema -cne 'storyvoice-synthetic-voice-authorization/v1') {
        throw '$.schema must be storyvoice-synthetic-voice-authorization/v1.'
    }
    $authorizationId = Read-SvString -Element (Get-SvProperty $root 'authorizationId') -JsonPath '$.authorizationId' -MaximumLength 128
    Assert-SvGrantId -Name '$.authorizationId' -Value $authorizationId
    $ownerId = Read-SvString -Element (Get-SvProperty $root 'ownerId') -JsonPath '$.ownerId' -MaximumLength 36
    Assert-SvCanonicalGuidString -Name '$.ownerId' -Value $ownerId

    $voice = Get-SvProperty $root 'voice'
    Assert-SvExactObject -Element $voice -JsonPath '$.voice' -ExpectedProperties @(
        'alias', 'characterProfileId', 'displayName',
        'attributionText', 'attributionDisplayAllowed', 'aiDisclosureRequired',
        'styles', 'useCases', 'fixedDemoSha256', 'fixedDemoMediaType')
    $voiceAlias = Read-SvString -Element (Get-SvProperty $voice 'alias') -JsonPath '$.voice.alias' -MaximumLength 64
    Assert-SvVoiceAlias -Name '$.voice.alias' -Value $voiceAlias
    $characterProfileId = Read-SvString -Element (Get-SvProperty $voice 'characterProfileId') -JsonPath '$.voice.characterProfileId' -MaximumLength 36
    Assert-SvCanonicalGuidString -Name '$.voice.characterProfileId' -Value $characterProfileId
    $displayName = Read-SvString -Element (Get-SvProperty $voice 'displayName') -JsonPath '$.voice.displayName' -MaximumLength 120
    Assert-SvNoUnsupportedOfficialClaim -Name '$.voice.displayName' -Value $displayName
    $attributionText = Read-SvNullableString -Element (Get-SvProperty $voice 'attributionText') -JsonPath '$.voice.attributionText' -MaximumLength 500
    if ($null -ne $attributionText) {
        Assert-SvNoUnsupportedOfficialClaim -Name '$.voice.attributionText' -Value $attributionText
    }
    if (-not (Read-SvBoolean -Element (Get-SvProperty $voice 'attributionDisplayAllowed') -JsonPath '$.voice.attributionDisplayAllowed')) {
        throw '$.voice.attributionDisplayAllowed must be true.'
    }
    if (-not (Read-SvBoolean -Element (Get-SvProperty $voice 'aiDisclosureRequired') -JsonPath '$.voice.aiDisclosureRequired')) {
        throw '$.voice.aiDisclosureRequired must be true.'
    }
    $styles = Read-SvTextArray -Element (Get-SvProperty $voice 'styles') -JsonPath '$.voice.styles'
    $useCases = Read-SvTextArray -Element (Get-SvProperty $voice 'useCases') -JsonPath '$.voice.useCases'
    foreach ($label in @($styles) + @($useCases)) {
        Assert-SvNoUnsupportedOfficialClaim -Name '$.voice.styles/useCases' -Value $label
    }
    $fixedDemoSha256 = Read-SvString -Element (Get-SvProperty $voice 'fixedDemoSha256') -JsonPath '$.voice.fixedDemoSha256' -MaximumLength 64
    Assert-SvHashString -Name '$.voice.fixedDemoSha256' -Value $fixedDemoSha256
    $fixedDemoMediaType = Read-SvString -Element (Get-SvProperty $voice 'fixedDemoMediaType') -JsonPath '$.voice.fixedDemoMediaType' -MaximumLength 64
    if ($fixedDemoMediaType -cne 'audio/wav') {
        throw '$.voice.fixedDemoMediaType must be audio/wav.'
    }

    $creation = Get-SvProperty $root 'creation'
    Assert-SvExactObject -Element $creation -JsonPath '$.creation' -ExpectedProperties @(
        'providerId', 'toolId', 'modelId', 'modelRevision', 'createdAtUtc',
        'generationManifestSha256', 'licenseIdentifier', 'termsUri',
        'termsSnapshotSha256', 'termsAcceptedAtUtc')
    $providerId = Read-SvString -Element (Get-SvProperty $creation 'providerId') -JsonPath '$.creation.providerId' -MaximumLength 128
    Assert-SvCanonicalId -Name '$.creation.providerId' -Value $providerId
    foreach ($field in @('toolId', 'modelId', 'modelRevision', 'licenseIdentifier')) {
        $null = Read-SvString -Element (Get-SvProperty $creation $field) -JsonPath "$.creation.$field" -MaximumLength 200
    }
    $createdAtUtc = Read-SvCanonicalUtc -Element (Get-SvProperty $creation 'createdAtUtc') -JsonPath '$.creation.createdAtUtc'
    $generationManifestSha256 = Read-SvString -Element (Get-SvProperty $creation 'generationManifestSha256') -JsonPath '$.creation.generationManifestSha256' -MaximumLength 64
    Assert-SvHashString -Name '$.creation.generationManifestSha256' -Value $generationManifestSha256
    $termsUri = Read-SvString -Element (Get-SvProperty $creation 'termsUri') -JsonPath '$.creation.termsUri' -MaximumLength 2048
    Assert-SvHttpsUri -Name '$.creation.termsUri' -Value $termsUri
    $termsSnapshotSha256 = Read-SvString -Element (Get-SvProperty $creation 'termsSnapshotSha256') -JsonPath '$.creation.termsSnapshotSha256' -MaximumLength 64
    Assert-SvHashString -Name '$.creation.termsSnapshotSha256' -Value $termsSnapshotSha256
    $termsAcceptedAtUtc = Read-SvCanonicalUtc -Element (Get-SvProperty $creation 'termsAcceptedAtUtc') -JsonPath '$.creation.termsAcceptedAtUtc'
    if ($termsAcceptedAtUtc -gt $createdAtUtc) {
        throw '$.creation.termsAcceptedAtUtc cannot be later than createdAtUtc.'
    }

    $assetBindings = Get-SvProperty $root 'assetBindings'
    $assetKeys = @('referenceAudioSha256', 'expectedTranscriptCanonicalSha256')
    Assert-SvExactObject -Element $assetBindings -JsonPath '$.assetBindings' -ExpectedProperties $assetKeys
    $assetHashes = @{}
    foreach ($field in $assetKeys) {
        $value = Read-SvString -Element (Get-SvProperty $assetBindings $field) -JsonPath "$.assetBindings.$field" -MaximumLength 64
        Assert-SvHashString -Name "$.assetBindings.$field" -Value $value
        $assetHashes[$field] = $value
    }

    $sourceClaims = Get-SvProperty $root 'sourceClaims'
    $claimKeys = @(
        'allGenerationInputsOwnedOrLicensed', 'noHumanVoiceInputProvided',
        'noHumanBiometricTemplateProvided', 'noIdentifiablePersonImitationRequested',
        'noKnownIdentifiablePersonImitated', 'noThirdPartyCharacterOrBrandClaimed')
    Assert-SvExactObject -Element $sourceClaims -JsonPath '$.sourceClaims' -ExpectedProperties $claimKeys
    foreach ($field in $claimKeys) {
        if (-not (Read-SvBoolean -Element (Get-SvProperty $sourceClaims $field) -JsonPath "$.sourceClaims.$field")) {
            throw "$.sourceClaims.$field must be true."
        }
    }

    $providerRights = Get-SvProperty $root 'providerRights'
    $providerRightKeys = @(
        'commercialOutputUseAllowed', 'publicOutputDistributionAllowed',
        'apiServiceUseAllowed', 'voiceModelDerivationAllowed')
    Assert-SvExactObject -Element $providerRights -JsonPath '$.providerRights' -ExpectedProperties $providerRightKeys
    foreach ($field in $providerRightKeys) {
        if (-not (Read-SvBoolean -Element (Get-SvProperty $providerRights $field) -JsonPath "$.providerRights.$field")) {
            throw "$.providerRights.$field must be true."
        }
    }

    $permissions = Get-SvProperty $root 'permissions'
    $permissionKeys = @(
        'catalogDisplay', 'demoPlayback', 'crossProjectApi',
        'subscriptionOffering', 'commercialUse', 'publicDistribution')
    Assert-SvExactObject -Element $permissions -JsonPath '$.permissions' -ExpectedProperties $permissionKeys
    foreach ($field in $permissionKeys) {
        if (-not (Read-SvBoolean -Element (Get-SvProperty $permissions $field) -JsonPath "$.permissions.$field")) {
            throw "$.permissions.$field must be true."
        }
    }

    $allowedConsumerFamilies = Read-SvIdentifierArray -Element (Get-SvProperty $root 'allowedConsumerFamilies') -JsonPath '$.allowedConsumerFamilies'

    $territory = Get-SvProperty $root 'territory'
    Assert-SvExactObject -Element $territory -JsonPath '$.territory' -ExpectedProperties @('mode', 'countryCodes')
    $territoryMode = Read-SvString -Element (Get-SvProperty $territory 'mode') -JsonPath '$.territory.mode' -MaximumLength 32
    $countryElement = Get-SvProperty $territory 'countryCodes'
    if ($countryElement.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
        throw '$.territory.countryCodes must be an array.'
    }
    $countryCodes = [System.Collections.Generic.List[string]]::new()
    foreach ($country in $countryElement.EnumerateArray()) {
        $countryCodes.Add((Read-SvString -Element $country -JsonPath '$.territory.countryCodes[]' -MaximumLength 2))
    }
    Assert-SvTerritoryValue -Mode $territoryMode -CountryCodes $countryCodes.ToArray()

    $providerPolicy = Get-SvProperty $root 'externalProviderPolicy'
    Assert-SvExactObject -Element $providerPolicy -JsonPath '$.externalProviderPolicy' -ExpectedProperties @('mode', 'allowedProviderIds')
    $providerMode = Read-SvString -Element (Get-SvProperty $providerPolicy 'mode') -JsonPath '$.externalProviderPolicy.mode' -MaximumLength 32
    $providerIds = Get-SvProperty $providerPolicy 'allowedProviderIds'
    if ($providerMode -cne 'prohibited' -or
        $providerIds.ValueKind -ne [Text.Json.JsonValueKind]::Array -or
        $providerIds.GetArrayLength() -ne 0) {
        throw '$.externalProviderPolicy must prohibit external providers with an empty allowlist.'
    }

    $effectiveAtUtc = Read-SvCanonicalUtc -Element (Get-SvProperty $root 'effectiveAtUtc') -JsonPath '$.effectiveAtUtc'
    $expiresAtUtc = Read-SvCanonicalUtc -Element (Get-SvProperty $root 'expiresAtUtc') -JsonPath '$.expiresAtUtc'
    if ($expiresAtUtc -le $effectiveAtUtc) {
        throw '$.expiresAtUtc must be later than $.effectiveAtUtc.'
    }
    $verificationTime = $AtUtc.ToUniversalTime()
    if ($verificationTime -lt $effectiveAtUtc) {
        throw 'The synthetic voice authorization is not yet effective.'
    }
    if ($verificationTime -ge $expiresAtUtc) {
        throw 'The synthetic voice authorization has expired.'
    }

    $revocation = Get-SvProperty $root 'revocation'
    Assert-SvExactObject -Element $revocation -JsonPath '$.revocation' -ExpectedProperties @(
        'scope', 'contact', 'process', 'requestedAtUtc', 'effectiveAtUtc')
    $revocationScope = Read-SvString -Element (Get-SvProperty $revocation 'scope') -JsonPath '$.revocation.scope' -MaximumLength 64
    if ($revocationScope -cne 'all-authorized-uses') {
        throw '$.revocation.scope must be all-authorized-uses.'
    }
    $revocationContact = Read-SvString -Element (Get-SvProperty $revocation 'contact') -JsonPath '$.revocation.contact' -MaximumLength 320
    Assert-SvContact -Name '$.revocation.contact' -Value $revocationContact
    $null = Read-SvString -Element (Get-SvProperty $revocation 'process') -JsonPath '$.revocation.process' -MinimumLength 20 -MaximumLength 2000
    $requestedAtUtc = Read-SvNullableCanonicalUtc -Element (Get-SvProperty $revocation 'requestedAtUtc') -JsonPath '$.revocation.requestedAtUtc'
    $revokedAtUtc = Read-SvNullableCanonicalUtc -Element (Get-SvProperty $revocation 'effectiveAtUtc') -JsonPath '$.revocation.effectiveAtUtc'
    if ($null -ne $requestedAtUtc -or $null -ne $revokedAtUtc) {
        throw 'A synthetic voice authorization with any revocation timestamp is not active.'
    }

    $attestation = Get-SvProperty $root 'attestation'
    Assert-SvExactObject -Element $attestation -JsonPath '$.attestation' -ExpectedProperties @(
        'state', 'method', 'accountSubjectId', 'auditEventId',
        'attestedAtUtc', 'issuedAtUtc')
    $state = Read-SvString -Element (Get-SvProperty $attestation 'state') -JsonPath '$.attestation.state' -MaximumLength 32
    if ($state -cne 'active') {
        throw '$.attestation.state is not active. A draft is never active.'
    }
    $method = Read-SvString -Element (Get-SvProperty $attestation 'method') -JsonPath '$.attestation.method' -MaximumLength 64
    if ($method -cne 'authenticated-owner-action') {
        throw '$.attestation.method must be authenticated-owner-action.'
    }
    $accountSubjectId = Read-SvString -Element (Get-SvProperty $attestation 'accountSubjectId') -JsonPath '$.attestation.accountSubjectId' -MaximumLength 128
    Assert-SvCanonicalId -Name '$.attestation.accountSubjectId' -Value $accountSubjectId
    $auditEventId = Read-SvString -Element (Get-SvProperty $attestation 'auditEventId') -JsonPath '$.attestation.auditEventId' -MaximumLength 128
    Assert-SvCanonicalId -Name '$.attestation.auditEventId' -Value $auditEventId
    $attestedAtUtc = Read-SvCanonicalUtc -Element (Get-SvProperty $attestation 'attestedAtUtc') -JsonPath '$.attestation.attestedAtUtc'
    $issuedAtUtc = Read-SvCanonicalUtc -Element (Get-SvProperty $attestation 'issuedAtUtc') -JsonPath '$.attestation.issuedAtUtc'
    if ($createdAtUtc -gt $attestedAtUtc -or $termsAcceptedAtUtc -gt $attestedAtUtc -or
        $attestedAtUtc -gt $issuedAtUtc -or $issuedAtUtc -gt $effectiveAtUtc) {
        throw 'Timestamps must satisfy terms <= creation <= attestation <= issuance <= effective.'
    }
    if ($createdAtUtc -gt $verificationTime -or $termsAcceptedAtUtc -gt $verificationTime) {
        throw 'Creation and provider-terms timestamps cannot be in the future.'
    }

    [pscustomobject]@{
        Valid = $true
        Schema = $schema
        AuthorizationId = $authorizationId
        AuthorizationSha256 = Get-SvFileSha256 -Path $opened.FullPath
        OwnerId = $ownerId
        VoiceAlias = $voiceAlias
        CharacterProfileId = $characterProfileId
        FixedDemoSha256 = $fixedDemoSha256
        ReferenceAudioSha256 = $assetHashes.referenceAudioSha256
        ExpectedTranscriptCanonicalSha256 = $assetHashes.expectedTranscriptCanonicalSha256
        AllowedConsumerFamilies = @($allowedConsumerFamilies)
        TerritoryMode = $territoryMode
        TerritoryCountryCodes = @($countryCodes.ToArray())
        EffectiveAtUtc = $effectiveAtUtc
        ExpiresAtUtc = $expiresAtUtc
    }
}
finally {
    if ($null -ne $opened) {
        $opened.Document.Dispose()
        [Array]::Clear($opened.Bytes, 0, $opened.Bytes.Length)
    }
}

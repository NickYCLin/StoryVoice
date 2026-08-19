Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$authorizationGenerator = Join-Path $PSScriptRoot 'New-SyntheticVoiceAuthorizationDraft.ps1'
$authorizationVerifier = Join-Path $PSScriptRoot 'Test-SyntheticVoiceAuthorization.ps1'
$usageGenerator = Join-Path $PSScriptRoot 'New-ExternalSyntheticVoiceUsageGrantDraft.ps1'
$developmentGrantGenerator = Join-Path $PSScriptRoot 'New-ExternalSyntheticVoiceDevelopmentGrant.ps1'
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$temporaryRoot = [IO.Path]::GetFullPath(
    (Join-Path $temporaryBase ("storyvoice-synthetic-authorization-tests-" + [Guid]::NewGuid().ToString('N'))))
$expectedPrefix = $temporaryBase + [IO.Path]::DirectorySeparatorChar

if (-not $temporaryRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The test temporary directory escaped the operating-system temporary root.'
}

function ConvertTo-TestUtcString {
    param([Parameter(Mandatory = $true)] [DateTimeOffset] $Value)
    return $Value.ToUniversalTime().ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture)
}

function Write-TestText {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $Value
    )
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Write-TestBytes {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [byte[]] $Value
    )
    [IO.File]::WriteAllBytes($Path, $Value)
}

function Write-TestJson {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [Parameter(Mandatory = $true)] [string] $Path
    )
    Write-TestText -Path $Path -Value (($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine)
}

function Get-TestSha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)] [scriptblock] $Action,
        [Parameter(Mandatory = $true)] [string] $MessagePattern
    )
    $rejected = $false
    try {
        & $Action
    }
    catch {
        $rejected = $true
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "Expected '$MessagePattern', received: $($_.Exception.Message)"
        }
    }
    if (-not $rejected) {
        throw "Expected rejection matching '$MessagePattern', but the action succeeded."
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [Parameter(Mandatory = $true)] [string[]] $Expected,
        [Parameter(Mandatory = $true)] [string] $Name
    )
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if ([string]::Join([Environment]::NewLine, $actual) -cne
        [string]::Join([Environment]::NewLine, $wanted)) {
        throw "$Name did not have the exact expected properties."
    }
}

function New-MutatedAuthorization {
    param(
        [Parameter(Mandatory = $true)] [string] $SourcePath,
        [Parameter(Mandatory = $true)] [string] $OutputPath,
        [Parameter(Mandatory = $true)] [scriptblock] $Mutator
    )
    $value = Get-Content -LiteralPath $SourcePath -Raw | ConvertFrom-Json
    & $Mutator $value
    Write-TestJson -Value $value -Path $OutputPath
}

[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    $now = [DateTimeOffset]::UtcNow
    $termsAcceptedAt = $now.AddDays(-3)
    $createdAt = $now.AddDays(-2)
    $effectiveAt = $now.AddHours(1)
    $expiresAt = $now.AddDays(30)
    $ownerId = [Guid]::NewGuid().ToString('D')
    $characterProfileId = [Guid]::NewGuid().ToString('D')
    $generationManifestPath = Join-Path $temporaryRoot 'generation-manifest.json'
    $termsSnapshotPath = Join-Path $temporaryRoot 'provider-terms.html'
    $referenceAudioPath = Join-Path $temporaryRoot 'reference.wav'
    $transcriptPath = Join-Path $temporaryRoot 'transcript.txt'
    $canonicalTranscriptFixturePath = Join-Path $temporaryRoot 'canonical-transcript-fixture.txt'
    $demoPath = Join-Path $temporaryRoot 'demo.wav'

    Write-TestText -Path $generationManifestPath -Value '{"fixture":"synthetic-generation-manifest"}'
    Write-TestText -Path $termsSnapshotPath -Value '<html><body>Provider terms fixture.</body></html>'
    Write-TestBytes -Path $referenceAudioPath -Value ([byte[]](0x52, 0x49, 0x46, 0x46, 0x01))
    Write-TestText -Path $transcriptPath -Value (([char]0x3000) + '完全校對的測試逐字稿。' + [Environment]::NewLine)
    Write-TestText -Path $canonicalTranscriptFixturePath -Value '完全校對的測試逐字稿。'
    Write-TestBytes -Path $demoPath -Value ([byte[]](0x52, 0x49, 0x46, 0x46, 0x02))

    $draftPath = Join-Path $temporaryRoot 'authorization-draft.json'
    $generatorParameters = @{
        OwnerId = $ownerId
        VoiceAlias = 'fixture-voice'
        CharacterProfileId = $characterProfileId
        ProviderId = 'fixture.provider'
        ToolId = 'Fixture Tool'
        ModelId = 'fixture-model'
        ModelRevision = 'fixture-revision'
        CreatedAtUtc = $createdAt
        GenerationManifestPath = $generationManifestPath
        LicenseIdentifier = 'fixture-commercial-license'
        TermsUri = 'https://provider.example/terms'
        TermsSnapshotPath = $termsSnapshotPath
        TermsAcceptedAtUtc = $termsAcceptedAt
        ReferenceAudioPath = $referenceAudioPath
        ExpectedTranscriptCanonicalPath = $transcriptPath
        SourceClaimsConfirmation = 'confirm'
        DisplayName = 'Fixture Synthetic Voice'
        AttributionText = 'AI 合成聲線，由建立者管理'
        AttributionDisplayConfirmation = 'confirm'
        Styles = @('calm', 'narration')
        UseCases = @('catalog', 'subscription')
        FixedDemoPath = $demoPath
        PermissionsConfirmation = 'confirm'
        AllowedConsumerFamilies = @('storyvoice-partners')
        TerritoryMode = 'country-list'
        TerritoryCountryCodes = @('TW')
        EffectiveAtUtc = $effectiveAt
        ExpiresAtUtc = $expiresAt
        RevocationContact = 'rights@example.test'
        RevocationProcess = 'Disable the catalog entry and all dependent API grants.'
        OutputPath = $draftPath
    }

    $generatorCommand = Get-Command $authorizationGenerator
    foreach ($removedParameter in @(
        'AllowedProjects',
        'GenerationInputPath',
        'GenerationOutputPath',
        'ProviderRightsConfirmation')) {
        if ($generatorCommand.Parameters.ContainsKey($removedParameter)) {
            throw "Authorization generator must not expose $removedParameter."
        }
    }

    $draftResult = & $authorizationGenerator @generatorParameters
    $draft = Get-Content -LiteralPath $draftPath -Raw | ConvertFrom-Json
    if ($draftResult.Status -cne 'draft' -or
        $draft.schema -cne 'storyvoice-synthetic-voice-authorization/v1' -or
        $draft.authorizationId -cnotmatch '^sva_[0-9a-f]{32}$' -or
        $draft.ownerId -cne $ownerId -or
        $draft.voice.alias -cne 'fixture-voice' -or
        $draft.voice.characterProfileId -cne $characterProfileId -or
        $draft.voice.fixedDemoSha256 -cne (Get-TestSha256 $demoPath) -or
        $draft.creation.generationManifestSha256 -cne (Get-TestSha256 $generationManifestPath) -or
        $draft.creation.termsSnapshotSha256 -cne (Get-TestSha256 $termsSnapshotPath) -or
        $draft.assetBindings.referenceAudioSha256 -cne (Get-TestSha256 $referenceAudioPath) -or
        $draft.assetBindings.expectedTranscriptCanonicalSha256 -cne (Get-TestSha256 $canonicalTranscriptFixturePath) -or
        $draft.attestation.state -cne 'draft') {
        throw 'The authorization draft did not derive its fixed fields and hashes correctly.'
    }
    foreach ($value in $draft.providerRights.PSObject.Properties.Value) {
        if ($value -ne $false) {
            throw 'A draft must leave provider rights false for controlled terms review.'
        }
    }
    foreach ($value in $draft.sourceClaims.PSObject.Properties.Value) {
        if ($value -ne $true) {
            throw 'Confirmed source claims must be true.'
        }
    }
    foreach ($value in $draft.permissions.PSObject.Properties.Value) {
        if ($value -ne $true) {
            throw 'Confirmed public and commercial permissions must be true.'
        }
    }
    Assert-ExactProperties -Value $draft -Name 'authorization root' -Expected @(
        'schema', 'authorizationId', 'ownerId', 'voice', 'creation', 'assetBindings',
        'sourceClaims', 'providerRights', 'permissions', 'allowedConsumerFamilies',
        'territory', 'externalProviderPolicy', 'effectiveAtUtc', 'expiresAtUtc',
        'revocation', 'attestation')
    Assert-ExactProperties -Value $draft.voice -Name 'voice' -Expected @(
        'alias', 'characterProfileId', 'displayName', 'attributionText',
        'attributionDisplayAllowed', 'aiDisclosureRequired', 'styles', 'useCases',
        'fixedDemoSha256', 'fixedDemoMediaType')
    Assert-ExactProperties -Value $draft.assetBindings -Name 'assetBindings' -Expected @(
        'referenceAudioSha256', 'expectedTranscriptCanonicalSha256')
    Assert-ExactProperties -Value $draft.attestation -Name 'attestation' -Expected @(
        'state', 'method', 'accountSubjectId', 'auditEventId', 'attestedAtUtc',
        'issuedAtUtc')

    Assert-Throws -MessagePattern 'never overwrites' -Action {
        $null = & $authorizationGenerator @generatorParameters
    }
    Assert-Throws -MessagePattern 'must be true|not active|draft' -Action {
        $null = & $authorizationVerifier -Path $draftPath -AtUtc $now
    }

    $missingManifestParameters = $generatorParameters.Clone()
    $missingManifestParameters.GenerationManifestPath = Join-Path $temporaryRoot 'missing-manifest.json'
    $missingManifestParameters.OutputPath = Join-Path $temporaryRoot 'missing-manifest-draft.json'
    Assert-Throws -MessagePattern 'does not exist' -Action {
        $null = & $authorizationGenerator @missingManifestParameters
    }

    $invalidAliasParameters = $generatorParameters.Clone()
    $invalidAliasParameters.VoiceAlias = 'Invalid_Alias'
    $invalidAliasParameters.OutputPath = Join-Path $temporaryRoot 'invalid-alias-draft.json'
    Assert-Throws -MessagePattern 'canonical voice alias' -Action {
        $null = & $authorizationGenerator @invalidAliasParameters
    }

    $officialClaimParameters = $generatorParameters.Clone()
    $officialClaimParameters.DisplayName = 'Official Fixture Voice'
    $officialClaimParameters.OutputPath = Join-Path $temporaryRoot 'official-claim-draft.json'
    Assert-Throws -MessagePattern 'official status' -Action {
        $null = & $authorizationGenerator @officialClaimParameters
    }

    $embeddedControlTranscriptPath = Join-Path $temporaryRoot 'embedded-control-transcript.txt'
    Write-TestText -Path $embeddedControlTranscriptPath -Value (
        'first line' + [Environment]::NewLine + 'second line')
    $embeddedControlParameters = $generatorParameters.Clone()
    $embeddedControlParameters.ExpectedTranscriptCanonicalPath = $embeddedControlTranscriptPath
    $embeddedControlParameters.OutputPath = Join-Path $temporaryRoot 'embedded-control-draft.json'
    Assert-Throws -MessagePattern 'embedded control' -Action {
        $null = & $authorizationGenerator @embeddedControlParameters
    }

    $pastEffectiveParameters = $generatorParameters.Clone()
    $pastEffectiveParameters.EffectiveAtUtc = $now.AddMinutes(-1)
    $pastEffectiveParameters.OutputPath = Join-Path $temporaryRoot 'past-effective-draft.json'
    Assert-Throws -MessagePattern 'must be in the future' -Action {
        $null = & $authorizationGenerator @pastEffectiveParameters
    }

    $activePath = Join-Path $temporaryRoot 'authorization-active.json'
    $active = Get-Content -LiteralPath $draftPath -Raw | ConvertFrom-Json
    $activeEffectiveAt = $now.AddMinutes(-10)
    $active.effectiveAtUtc = ConvertTo-TestUtcString $activeEffectiveAt
    foreach ($property in $active.providerRights.PSObject.Properties) {
        $property.Value = $true
    }
    $active.attestation.state = 'active'
    $active.attestation.method = 'authenticated-owner-action'
    $active.attestation.accountSubjectId = 'account.fixture-owner'
    $active.attestation.auditEventId = 'audit.fixture-authorization'
    $active.attestation.attestedAtUtc = ConvertTo-TestUtcString $activeEffectiveAt.AddMinutes(-2)
    $active.attestation.issuedAtUtc = ConvertTo-TestUtcString $activeEffectiveAt.AddMinutes(-1)
    Write-TestJson -Value $active -Path $activePath

    $verified = & $authorizationVerifier -Path $activePath -AtUtc $now
    if (-not $verified.Valid -or
        $verified.OwnerId -cne $ownerId -or
        $verified.VoiceAlias -cne 'fixture-voice' -or
        $verified.CharacterProfileId -cne $characterProfileId -or
        $verified.AuthorizationSha256 -cne (Get-TestSha256 $activePath)) {
        throw 'The active authorization verifier did not return the expected binding.'
    }

    $falseClaimPath = Join-Path $temporaryRoot 'false-claim.json'
    New-MutatedAuthorization -SourcePath $activePath -OutputPath $falseClaimPath -Mutator {
        param($value)
        $value.sourceClaims.noKnownIdentifiablePersonImitated = $false
    }
    Assert-Throws -MessagePattern 'must be true' -Action {
        $null = & $authorizationVerifier -Path $falseClaimPath -AtUtc $now
    }

    $falseProviderRightPath = Join-Path $temporaryRoot 'false-provider-right.json'
    New-MutatedAuthorization -SourcePath $activePath -OutputPath $falseProviderRightPath -Mutator {
        param($value)
        $value.providerRights.apiServiceUseAllowed = $false
    }
    Assert-Throws -MessagePattern 'must be true' -Action {
        $null = & $authorizationVerifier -Path $falseProviderRightPath -AtUtc $now
    }

    $falsePermissionPath = Join-Path $temporaryRoot 'false-permission.json'
    New-MutatedAuthorization -SourcePath $activePath -OutputPath $falsePermissionPath -Mutator {
        param($value)
        $value.permissions.subscriptionOffering = $false
    }
    Assert-Throws -MessagePattern 'must be true' -Action {
        $null = & $authorizationVerifier -Path $falsePermissionPath -AtUtc $now
    }

    $externalProviderPath = Join-Path $temporaryRoot 'external-provider.json'
    New-MutatedAuthorization -SourcePath $activePath -OutputPath $externalProviderPath -Mutator {
        param($value)
        $value.externalProviderPolicy.mode = 'allowlist'
        $value.externalProviderPolicy.allowedProviderIds = @('external.provider')
    }
    Assert-Throws -MessagePattern 'must prohibit external providers' -Action {
        $null = & $authorizationVerifier -Path $externalProviderPath -AtUtc $now
    }

    $revokedPath = Join-Path $temporaryRoot 'revoked.json'
    New-MutatedAuthorization -SourcePath $activePath -OutputPath $revokedPath -Mutator {
        param($value)
        $value.revocation.requestedAtUtc = $value.effectiveAtUtc
    }
    Assert-Throws -MessagePattern 'revocation timestamp' -Action {
        $null = & $authorizationVerifier -Path $revokedPath -AtUtc $now
    }

    $unknownPropertyPath = Join-Path $temporaryRoot 'unknown-property.json'
    New-MutatedAuthorization -SourcePath $activePath -OutputPath $unknownPropertyPath -Mutator {
        param($value)
        $value | Add-Member -NotePropertyName unexpected -NotePropertyValue $true
    }
    Assert-Throws -MessagePattern 'unknown property' -Action {
        $null = & $authorizationVerifier -Path $unknownPropertyPath -AtUtc $now
    }

    $duplicatePropertyPath = Join-Path $temporaryRoot 'duplicate-property.json'
    $activeJson = Get-Content -LiteralPath $activePath -Raw
    $duplicateJson = [regex]::Replace(
        $activeJson,
        '"schema"\s*:\s*"storyvoice-synthetic-voice-authorization/v1"',
        '"schema":"storyvoice-synthetic-voice-authorization/v1","schema":"storyvoice-synthetic-voice-authorization/v1"',
        1)
    Write-TestText -Path $duplicatePropertyPath -Value $duplicateJson
    Assert-Throws -MessagePattern 'duplicate property' -Action {
        $null = & $authorizationVerifier -Path $duplicatePropertyPath -AtUtc $now
    }

    Assert-Throws -MessagePattern 'expired' -Action {
        $null = & $authorizationVerifier -Path $activePath -AtUtc $expiresAt.AddSeconds(1)
    }

    $usageGeneratorCommand = Get-Command $usageGenerator
    if ($usageGeneratorCommand.Parameters.ContainsKey('Purpose') -or
        $usageGeneratorCommand.Parameters.ContainsKey('PublicationSelfAuthorizationPath') -or
        $usageGeneratorCommand.Parameters.ContainsKey('SyntheticOriginPath')) {
        throw 'Usage generator must expose only the fixed synthetic commercial flow.'
    }

    $usagePath = Join-Path $temporaryRoot 'usage-grant.json'
    $usageParameters = @{
        ConsumerKeyId = 'consumer_fixture_01'
        SyntheticVoiceAuthorizationPath = $activePath
        ProjectId = 'project.fixture'
        ConsumerFamilyId = 'storyvoice-partners'
        TerritoryCountryCode = 'TW'
        EffectiveAtUtc = $now.AddMinutes(5)
        ExpiresAtUtc = $now.AddDays(1)
        CommercialScopeConfirmation = 'confirm'
        OutputPath = $usagePath
    }
    $usageResult = & $usageGenerator @usageParameters
    $usage = Get-Content -LiteralPath $usagePath -Raw | ConvertFrom-Json
    if ($usageResult.Status -cne 'draft' -or
        $usage.schema -cne 'voice-api-synthetic-usage-grant/v1' -or
        $usage.grantId -cnotmatch '^svg_[0-9a-f]{32}$' -or
        $usage.syntheticVoiceAuthorizationSha256 -cne $verified.AuthorizationSha256 -or
        $usage.projectId -cne 'project.fixture' -or
        $usage.consumerFamilyId -cne 'storyvoice-partners' -or
        $usage.territoryCountryCode -cne 'TW' -or
        $usage.activation.state -cne 'draft' -or
        $null -ne $usage.activation.accountSubjectId -or
        $null -ne $usage.activation.auditEventId -or
        $null -ne $usage.activation.issuedAtUtc) {
        throw 'The usage generator did not emit the fixed synthetic commercial artifact.'
    }
    Assert-ExactProperties -Value $usage -Name 'usage artifact' -Expected @(
        'schema', 'grantId', 'consumerKeyId', 'ownerId', 'voiceAlias', 'characterProfileId',
        'syntheticVoiceAuthorizationSha256', 'projectId', 'consumerFamilyId',
        'territoryCountryCode', 'effectiveAtUtc', 'expiresAtUtc', 'revokedAtUtc',
        'activation')
    Assert-ExactProperties -Value $usage.activation -Name 'usage activation' -Expected @(
        'state', 'accountSubjectId', 'auditEventId', 'issuedAtUtc')
    Assert-Throws -MessagePattern 'never overwrites' -Action {
        $null = & $usageGenerator @usageParameters
    }

    $badFamilyParameters = $usageParameters.Clone()
    $badFamilyParameters.ConsumerFamilyId = 'unauthorized-family'
    $badFamilyParameters.OutputPath = Join-Path $temporaryRoot 'bad-family-usage.json'
    Assert-Throws -MessagePattern 'not included' -Action {
        $null = & $usageGenerator @badFamilyParameters
    }

    $badTerritoryParameters = $usageParameters.Clone()
    $badTerritoryParameters.TerritoryCountryCode = 'US'
    $badTerritoryParameters.OutputPath = Join-Path $temporaryRoot 'bad-territory-usage.json'
    Assert-Throws -MessagePattern 'not included' -Action {
        $null = & $usageGenerator @badTerritoryParameters
    }

    $badWindowParameters = $usageParameters.Clone()
    $badWindowParameters.ExpiresAtUtc = $expiresAt.AddSeconds(1)
    $badWindowParameters.OutputPath = Join-Path $temporaryRoot 'bad-window-usage.json'
    Assert-Throws -MessagePattern 'contained' -Action {
        $null = & $usageGenerator @badWindowParameters
    }

    $developmentGrantPath = Join-Path $temporaryRoot 'development-grant.json'
    $developmentGrantParameters = @{
        ConsumerKeyId = 'owner-project-dev'
        ProjectId = 'owner-project-dev'
        OwnerId = [Guid]$ownerId
        VoiceAlias = 'fixture-voice'
        CharacterProfileId = [Guid]$characterProfileId
        ReferenceAudioPath = $referenceAudioPath
        ExpectedTranscriptCanonicalPath = $transcriptPath
        EffectiveAtUtc = $now.AddMinutes(-1)
        ExpiresAtUtc = $now.AddDays(7)
        OutputPath = $developmentGrantPath
    }
    $developmentResult = & $developmentGrantGenerator @developmentGrantParameters
    $developmentGrant = Get-Content -LiteralPath $developmentGrantPath -Raw | ConvertFrom-Json
    if ($developmentResult.AccessTier -cne 'private-development' -or
        $developmentGrant.schema -cne 'voice-api-synthetic-development-grant/v1' -or
        $developmentGrant.grantId -cnotmatch '^svd_[0-9a-f]{32}$' -or
        $developmentGrant.consumerKeyId -cne 'owner-project-dev' -or
        $developmentGrant.projectId -cne 'owner-project-dev' -or
        $developmentGrant.ownerId -cne $ownerId -or
        $developmentGrant.voiceAlias -cne 'fixture-voice' -or
        $developmentGrant.characterProfileId -cne $characterProfileId -or
        $developmentGrant.referenceAudioSha256 -cne (Get-TestSha256 $referenceAudioPath) -or
        $developmentGrant.expectedTranscriptCanonicalSha256 -cne
            (Get-TestSha256 $canonicalTranscriptFixturePath) -or
        $developmentGrant.origin -cne
            'owner-created-synthetic-no-human-source-no-identifiable-imitation' -or
        $null -ne $developmentGrant.revokedAtUtc -or
        $developmentGrant.PSObject.Properties.Name -contains 'activation') {
        throw 'The private development grant helper emitted an invalid artifact.'
    }
    Assert-ExactProperties -Value $developmentGrant -Name 'development grant' -Expected @(
        'schema', 'grantId', 'consumerKeyId', 'ownerId', 'voiceAlias',
        'characterProfileId', 'referenceAudioSha256',
        'expectedTranscriptCanonicalSha256', 'projectId', 'effectiveAtUtc',
        'expiresAtUtc', 'revokedAtUtc', 'origin')
    Assert-Throws -MessagePattern 'never overwrites' -Action {
        $null = & $developmentGrantGenerator @developmentGrantParameters
    }

    $overlongDevelopmentParameters = $developmentGrantParameters.Clone()
    $overlongDevelopmentParameters.OutputPath = Join-Path $temporaryRoot 'overlong-development.json'
    $overlongDevelopmentParameters.ExpiresAtUtc = $now.AddDays(31)
    Assert-Throws -MessagePattern 'cannot exceed 30 days' -Action {
        $null = & $developmentGrantGenerator @overlongDevelopmentParameters
    }

    Write-Output 'Synthetic voice authorization tooling tests passed.'
}
finally {
    if ([IO.Directory]::Exists($temporaryRoot) -and
        $temporaryRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($temporaryRoot, $true)
    }
}

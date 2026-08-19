Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$generator = Join-Path $PSScriptRoot 'New-ExternalVoiceApiCredential.ps1'
foreach ($tier in @('private-development', 'subscription-commercial')) {
    $credential = & $generator -KeyId 'consumer_test' -AccessTier $tier
    $expectedPrefix = if ($tier -ceq 'private-development') { 'svd1' } else { 'svv1' }

    if ($credential.KeyId -cne 'consumer_test' -or $credential.AccessTier -cne $tier) {
        throw 'The positive credential test returned the wrong binding.'
    }

    if ($credential.BearerToken -cnotmatch "^$expectedPrefix\.consumer_test\.[A-Za-z0-9_-]{43}$") {
        throw 'The positive credential test returned a non-canonical bearer token.'
    }

    $tokenBytes = [System.Text.Encoding]::UTF8.GetBytes($credential.BearerToken)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $expectedHash = ([BitConverter]::ToString($sha256.ComputeHash($tokenBytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
        [Array]::Clear($tokenBytes, 0, $tokenBytes.Length)
    }

    if ($credential.TokenSha256 -cne $expectedHash) {
        throw 'The positive credential test returned the wrong SHA-256.'
    }
}

$invalidKeyIds = @(
    'short',
    'UPPERCASE',
    '-leadingdash',
    'contains.dot'
)

foreach ($invalidKeyId in $invalidKeyIds) {
    $wasRejected = $false

    try {
        $null = & $generator `
            -KeyId $invalidKeyId `
            -AccessTier 'private-development' `
            2>$null
    }
    catch {
        $wasRejected = $true
    }

    if (-not $wasRejected) {
        throw "The negative credential test accepted invalid key ID '$invalidKeyId'."
    }
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryDirectory = Join-Path $temporaryRoot ('storyvoice-credential-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
try {
    $outputPath = Join-Path $temporaryDirectory 'private-development-credential.json'
    $safeResult = & $generator `
        -KeyId 'safe_test_01' `
        -AccessTier 'private-development' `
        -OutputPath $outputPath
    $serializedResult = $safeResult | ConvertTo-Json -Compress
    $storedCredential = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json

    if ($safeResult.PSObject.Properties.Name -contains 'BearerToken' -or
        $serializedResult.Contains('BearerToken', [StringComparison]::Ordinal) -or
        $serializedResult.Contains($storedCredential.BearerToken, [StringComparison]::Ordinal)) {
        throw 'Safe OutputPath mode leaked the bearer token through its result.'
    }
    if ($storedCredential.BearerToken -cnotmatch '^svd1\.safe_test_01\.[A-Za-z0-9_-]{43}$' -or
        $safeResult.CredentialPath -cne [IO.Path]::GetFullPath($outputPath) -or
        $safeResult.TokenSha256 -cne $storedCredential.TokenSha256) {
        throw 'Safe OutputPath mode wrote an invalid credential.'
    }

    if ($IsWindows) {
        $acl = Get-Acl -LiteralPath $outputPath
        if (-not $acl.AreAccessRulesProtected) {
            throw 'Safe OutputPath mode left Windows ACL inheritance enabled.'
        }
        $allowedSids = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        $null = $allowedSids.Add([Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
        $null = $allowedSids.Add('S-1-5-18')
        $null = $allowedSids.Add('S-1-5-32-544')
        foreach ($rule in $acl.Access) {
            $sid = $rule.IdentityReference.Translate(
                [Security.Principal.SecurityIdentifier]).Value
            if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
                -not $allowedSids.Contains($sid)) {
                throw "Safe OutputPath mode granted access to unexpected SID '$sid'."
            }
        }
    }
    elseif ([IO.File]::GetUnixFileMode($outputPath) -ne
        ([IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)) {
        throw 'Safe OutputPath mode did not set Unix mode 0600.'
    }

    $createNewRejected = $false
    try {
        $null = & $generator `
            -KeyId 'safe_test_01' `
            -AccessTier 'private-development' `
            -OutputPath $outputPath `
            2>$null
    }
    catch {
        $createNewRejected = $true
    }
    if (-not $createNewRejected) {
        throw 'Safe OutputPath mode overwrote an existing credential.'
    }
}
finally {
    $resolvedTemporaryDirectory = [IO.Path]::GetFullPath($temporaryDirectory)
    if (-not $resolvedTemporaryDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a credential test directory outside the system temp root.'
    }
    if ([IO.Directory]::Exists($resolvedTemporaryDirectory)) {
        [IO.Directory]::Delete($resolvedTemporaryDirectory, $true)
    }
}

Write-Output 'External voice credential helper tests passed.'

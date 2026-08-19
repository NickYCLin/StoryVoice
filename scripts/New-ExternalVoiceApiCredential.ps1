[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('(?-i)^[a-z0-9][a-z0-9_-]{7,63}$')]
    [string] $KeyId,

    [Parameter(Mandatory = $true)]
    [ValidateSet('private-development', 'subscription-commercial')]
    [string] $AccessTier,

    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$secretBytes = [byte[]]::new(32)
$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()

try {
    $random.GetBytes($secretBytes)
}
finally {
    $random.Dispose()
}

$secret = [Convert]::ToBase64String($secretBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$tokenPrefix = if ($AccessTier -ceq 'private-development') { 'svd1' } else { 'svv1' }
$token = "$tokenPrefix.$KeyId.$secret"

if ($secret.Length -ne 43 -or
    $token -cnotmatch "^$tokenPrefix\.$([Regex]::Escape($KeyId))\.[A-Za-z0-9_-]{43}$") {
    throw 'Generated credential failed the canonical token self-check.'
}

$tokenBytes = [System.Text.Encoding]::UTF8.GetBytes($token)
$sha256 = [System.Security.Cryptography.SHA256]::Create()

try {
    $hash = ([BitConverter]::ToString($sha256.ComputeHash($tokenBytes))).Replace('-', '').ToLowerInvariant()
}
finally {
    $sha256.Dispose()
    [Array]::Clear($secretBytes, 0, $secretBytes.Length)
    [Array]::Clear($tokenBytes, 0, $tokenBytes.Length)
}

if ($hash -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Generated credential failed the SHA-256 self-check.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    # Interactive mode intentionally emits the bearer token exactly once.
    [pscustomobject]@{
        KeyId = $KeyId
        AccessTier = $AccessTier
        TokenSha256 = $hash
        BearerToken = $token
    }
    return
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$parentDirectory = [IO.Path]::GetDirectoryName($fullOutputPath)
if ([string]::IsNullOrWhiteSpace($parentDirectory) -or
    -not [IO.Directory]::Exists($parentDirectory)) {
    throw 'OutputPath parent directory must already exist.'
}

$credential = [ordered]@{
    KeyId = $KeyId
    AccessTier = $AccessTier
    TokenSha256 = $hash
    BearerToken = $token
}
$credentialBytes = [Text.UTF8Encoding]::new($false, $true).GetBytes(
    (($credential | ConvertTo-Json -Depth 3) + [Environment]::NewLine))
$stream = $null
$created = $false
try {
    $stream = [IO.File]::Open(
        $fullOutputPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    $created = $true

    if ($IsWindows) {
        $acl = Get-Acl -LiteralPath $fullOutputPath
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) {
            $null = $acl.RemoveAccessRuleAll($rule)
        }

        $allowedSids = @(
            [Security.Principal.WindowsIdentity]::GetCurrent().User,
            [Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
            [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
        )
        foreach ($sid in $allowedSids) {
            $rule = [Security.AccessControl.FileSystemAccessRule]::new(
                $sid,
                [Security.AccessControl.FileSystemRights]::FullControl,
                [Security.AccessControl.AccessControlType]::Allow)
            $null = $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $fullOutputPath -AclObject $acl
    }
    else {
        [IO.File]::SetUnixFileMode(
            $fullOutputPath,
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
    }

    $stream.Write($credentialBytes, 0, $credentialBytes.Length)
    $stream.Flush($true)
}
catch {
    if ($null -ne $stream) {
        $stream.Dispose()
        $stream = $null
    }
    if ($created -and [IO.File]::Exists($fullOutputPath)) {
        [IO.File]::Delete($fullOutputPath)
    }
    throw
}
finally {
    if ($null -ne $stream) {
        $stream.Dispose()
    }
    [Array]::Clear($credentialBytes, 0, $credentialBytes.Length)
}

# Safe file mode never returns the raw bearer token to the pipeline/stdout.
[pscustomobject]@{
    KeyId = $KeyId
    AccessTier = $AccessTier
    TokenSha256 = $hash
    CredentialPath = $fullOutputPath
}

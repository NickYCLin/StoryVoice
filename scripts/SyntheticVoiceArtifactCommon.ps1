Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SvIso3166Alpha2 = [System.Collections.Generic.HashSet[string]]::new(
    ((
        'AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ ' +
        'CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR ' +
        'GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM ' +
        'JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ ' +
        'MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE ' +
        'RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT ' +
        'TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW') -split ' '),
    [StringComparer]::Ordinal)

function Assert-SvPrintableString {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value,
        [int] $MinimumLength = 1,
        [int] $MaximumLength = 2000
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value.Length -lt $MinimumLength -or
        $Value.Length -gt $MaximumLength -or
        $Value -cne $Value.Trim()) {
        throw "$Name has an invalid length, is blank, or has surrounding whitespace."
    }

    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsControl($character) -or
            [char]::GetUnicodeCategory($character) -eq [Globalization.UnicodeCategory]::Format) {
            throw "$Name contains a control or format character."
        }
    }
}

function Assert-SvNoUnsupportedOfficialClaim {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    if ($Value.Contains('官方', [StringComparison]::Ordinal) -or
        $Value.Contains('official', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must not claim official status."
    }
}

function Assert-SvCanonicalId {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value,
        [int] $MinimumLength = 1,
        [int] $MaximumLength = 128
    )

    Assert-SvPrintableString -Name $Name -Value $Value -MinimumLength $MinimumLength -MaximumLength $MaximumLength
    if ($Value -cnotmatch '^[a-z0-9][a-z0-9._:-]*$' -or $Value.Contains('*', [StringComparison]::Ordinal)) {
        throw "$Name must be a canonical lowercase identifier without wildcards."
    }
}

function Assert-SvGrantId {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    if ($Value -cnotmatch '^[a-z0-9][a-z0-9._:-]{7,127}$') {
        throw "$Name must be a canonical grant identifier."
    }
}

function Assert-SvVoiceAlias {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    if ($Value -cnotmatch '^(?:[a-z0-9]|[a-z0-9][a-z0-9-]{0,62}[a-z0-9])$') {
        throw "$Name must be a canonical voice alias."
    }
}

function ConvertTo-SvGuidString {
    param(
        [Parameter(Mandatory = $true)] [Guid] $Value,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    if ($Value -eq [Guid]::Empty) {
        throw "$Name must be a non-empty GUID."
    }

    return $Value.ToString('D').ToLowerInvariant()
}

function Assert-SvCanonicalGuidString {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParseExact($Value, 'D', [ref] $parsed) -or
        $parsed -eq [Guid]::Empty -or
        $Value -cne $parsed.ToString('D').ToLowerInvariant()) {
        throw "$Name must be a canonical lowercase non-empty GUID-D."
    }
}

function Assert-SvHashString {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Name must be a lowercase SHA-256 value."
    }
}

function ConvertTo-SvCanonicalUtc {
    param([Parameter(Mandatory = $true)] [DateTimeOffset] $Value)

    return $Value.ToUniversalTime().ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture)
}

function Assert-SvContact {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    Assert-SvPrintableString -Name $Name -Value $Value -MinimumLength 3 -MaximumLength 320
    $uri = $null
    $isHttps = [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri) -and
        $uri.Scheme -ceq [Uri]::UriSchemeHttps -and
        [string]::IsNullOrEmpty($uri.UserInfo)
    $mail = $null
    $isEmail = [Net.Mail.MailAddress]::TryCreate($Value, [ref] $mail) -and
        $mail.Address -ceq $Value
    if (-not $isHttps -and -not $isEmail) {
        throw "$Name must be an email address or an HTTPS URL."
    }
}

function Assert-SvHttpsUri {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Value
    )

    Assert-SvPrintableString -Name $Name -Value $Value -MaximumLength 2048
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri) -or
        $uri.Scheme -cne [Uri]::UriSchemeHttps -or
        -not [string]::IsNullOrEmpty($uri.UserInfo)) {
        throw "$Name must be an HTTPS URL without user information."
    }
}

function Assert-SvIdentifierArrayValue {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string[]] $Values,
        [int] $MaximumCount = 50
    )

    if ($Values.Count -lt 1 -or $Values.Count -gt $MaximumCount) {
        throw "$Name must contain between 1 and $MaximumCount entries."
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Values) {
        Assert-SvCanonicalId -Name $Name -Value $value
        if (-not $seen.Add($value)) {
            throw "$Name contains a duplicate entry."
        }
    }
}

function Assert-SvTextArrayValue {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string[]] $Values,
        [int] $MaximumCount = 8,
        [int] $MaximumLength = 40
    )

    if ($Values.Count -lt 1 -or $Values.Count -gt $MaximumCount) {
        throw "$Name must contain between 1 and $MaximumCount entries."
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Values) {
        Assert-SvPrintableString -Name $Name -Value $value -MaximumLength $MaximumLength
        if (-not $seen.Add($value)) {
            throw "$Name contains a duplicate entry."
        }
    }
}

function Assert-SvTerritoryValue {
    param(
        [Parameter(Mandatory = $true)] [string] $Mode,
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [string[]] $CountryCodes
    )

    if ($Mode -cnotin @('country-list', 'worldwide')) {
        throw 'Territory mode must be country-list or worldwide.'
    }

    if ($Mode -ceq 'worldwide') {
        if ($CountryCodes.Count -ne 0) {
            throw 'Territory country codes must be empty for worldwide mode.'
        }
        return
    }

    if ($CountryCodes.Count -lt 1 -or $CountryCodes.Count -gt 249) {
        throw 'Territory country codes must contain between 1 and 249 entries.'
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($code in $CountryCodes) {
        if (-not $script:SvIso3166Alpha2.Contains($code) -or -not $seen.Add($code)) {
            throw 'Territory country codes must be unique uppercase ISO 3166-1 alpha-2 codes.'
        }
    }
}

function Get-SvFileSha256 {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [long] $MaximumBytes = [long]::MaxValue
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($fullPath)) {
        throw "Artifact does not exist: $fullPath"
    }
    $stream = [IO.File]::Open($fullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        if ($stream.Length -lt 1 -or $stream.Length -gt $MaximumBytes) {
            throw "Artifact must contain between 1 and $MaximumBytes bytes: $fullPath"
        }
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Get-SvCanonicalTranscriptSha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($fullPath)) {
        throw "Artifact does not exist: $fullPath"
    }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    $canonicalBytes = $null
    $sha = $null
    try {
        if ($bytes.Length -lt 1 -or $bytes.Length -gt 32768) {
            throw "Canonical transcript input must contain between 1 and 32768 bytes: $fullPath"
        }
        $value = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $canonical = $value.Normalize([Text.NormalizationForm]::FormKC).Trim()
        if ([string]::IsNullOrWhiteSpace($canonical) -or $canonical.Length -gt 2000) {
            throw 'Canonical transcript must contain visible content and at most 2000 UTF-16 code units.'
        }

        $hasVisibleContent = $false
        foreach ($character in $canonical.ToCharArray()) {
            $category = [char]::GetUnicodeCategory($character)
            if ([char]::IsControl($character) -or
                $category -eq [Globalization.UnicodeCategory]::Format) {
                throw 'Canonical transcript cannot contain an embedded control or format character.'
            }
            if (-not [char]::IsWhiteSpace($character) -and
                $category -cnotin @(
                    [Globalization.UnicodeCategory]::NonSpacingMark,
                    [Globalization.UnicodeCategory]::SpacingCombiningMark,
                    [Globalization.UnicodeCategory]::EnclosingMark)) {
                $hasVisibleContent = $true
            }
        }
        if (-not $hasVisibleContent) {
            throw 'Canonical transcript must contain visible content.'
        }

        $canonicalBytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($canonical)
        $sha = [Security.Cryptography.SHA256]::Create()
        return ([BitConverter]::ToString($sha.ComputeHash($canonicalBytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        if ($null -ne $sha) {
            $sha.Dispose()
        }
        if ($null -ne $canonicalBytes) {
            [Array]::Clear($canonicalBytes, 0, $canonicalBytes.Length)
        }
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Write-SvCreateNewJson {
    param(
        [Parameter(Mandatory = $true)] $Artifact,
        [Parameter(Mandatory = $true)] [string] $OutputPath
    )

    $fullPath = [IO.Path]::GetFullPath($OutputPath)
    $directory = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($directory) -or -not [IO.Directory]::Exists($directory)) {
        throw 'The OutputPath parent directory must already exist.'
    }
    if ([IO.File]::Exists($fullPath)) {
        throw 'OutputPath already exists. The generator never overwrites an artifact.'
    }

    $json = $Artifact | ConvertTo-Json -Depth 16
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($json + [Environment]::NewLine)
    if ($bytes.Length -gt 131072) {
        throw 'The generated artifact exceeds 128 KiB.'
    }

    $stream = $null
    $created = $false
    try {
        $stream = [IO.File]::Open(
            $fullPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        $created = $true
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    catch {
        if ($created -and $null -ne $stream) {
            $stream.Dispose()
            $stream = $null
        }
        if ($created -and [IO.File]::Exists($fullPath)) {
            [IO.File]::Delete($fullPath)
        }
        throw
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        [Array]::Clear($bytes, 0, $bytes.Length)
    }

    return $fullPath
}

function Assert-SvExactObject {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath,
        [Parameter(Mandatory = $true)] [string[]] $ExpectedProperties
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
        throw "$JsonPath must be an object."
    }

    $remaining = [System.Collections.Generic.HashSet[string]]::new($ExpectedProperties, [StringComparer]::Ordinal)
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
        if (-not $seen.Add($property.Name)) {
            throw "$JsonPath contains a duplicate property '$($property.Name)'."
        }
        if (-not $remaining.Remove($property.Name)) {
            throw "$JsonPath contains an unknown property '$($property.Name)'."
        }
    }
    if ($remaining.Count -ne 0) {
        throw "$JsonPath is missing required property '$([Linq.Enumerable]::First($remaining))'."
    }
}

function Get-SvProperty {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    return $Element.GetProperty($Name)
}

function Read-SvString {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath,
        [int] $MinimumLength = 1,
        [int] $MaximumLength = 2000
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::String) {
        throw "$JsonPath must be a string."
    }
    $value = $Element.GetString()
    Assert-SvPrintableString -Name $JsonPath -Value $value -MinimumLength $MinimumLength -MaximumLength $MaximumLength
    return $value
}

function Read-SvNullableString {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath,
        [int] $MaximumLength = 2000
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Null) {
        return $null
    }
    return Read-SvString -Element $Element -JsonPath $JsonPath -MaximumLength $MaximumLength
}

function Read-SvBoolean {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath
    )

    if ($Element.ValueKind -cnotin @([Text.Json.JsonValueKind]::True, [Text.Json.JsonValueKind]::False)) {
        throw "$JsonPath must be a boolean."
    }
    return $Element.GetBoolean()
}

function Read-SvCanonicalUtc {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath
    )

    $value = Read-SvString -Element $Element -JsonPath $JsonPath -MaximumLength 20
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
        $value,
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal,
        [ref] $parsed) -or
        $value -cne (ConvertTo-SvCanonicalUtc $parsed)) {
        throw "$JsonPath must be canonical UTC yyyy-MM-ddTHH:mm:ssZ."
    }
    return $parsed
}

function Read-SvNullableCanonicalUtc {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Null) {
        return $null
    }
    return Read-SvCanonicalUtc -Element $Element -JsonPath $JsonPath
}

function Assert-SvJsonNull {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Null) {
        throw "$JsonPath must be null."
    }
}

function Read-SvIdentifierArray {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath,
        [int] $MinimumCount = 1,
        [int] $MaximumCount = 50
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Array -or
        $Element.GetArrayLength() -lt $MinimumCount -or
        $Element.GetArrayLength() -gt $MaximumCount) {
        throw "$JsonPath must contain between $MinimumCount and $MaximumCount identifiers."
    }
    $values = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $Element.EnumerateArray()) {
        $value = Read-SvString -Element $item -JsonPath "$JsonPath[]" -MaximumLength 128
        Assert-SvCanonicalId -Name "$JsonPath[]" -Value $value
        if (-not $seen.Add($value)) {
            throw "$JsonPath contains a duplicate identifier."
        }
        $values.Add($value)
    }
    return ,$values.ToArray()
}

function Read-SvTextArray {
    param(
        [Parameter(Mandatory = $true)] [Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $JsonPath,
        [int] $MaximumCount = 8,
        [int] $MaximumLength = 40
    )

    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Array -or
        $Element.GetArrayLength() -lt 1 -or
        $Element.GetArrayLength() -gt $MaximumCount) {
        throw "$JsonPath must contain between 1 and $MaximumCount entries."
    }
    $values = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $Element.EnumerateArray()) {
        $value = Read-SvString -Element $item -JsonPath "$JsonPath[]" -MaximumLength $MaximumLength
        if (-not $seen.Add($value)) {
            throw "$JsonPath contains a duplicate entry."
        }
        $values.Add($value)
    }
    return ,$values.ToArray()
}

function Open-SvJsonDocument {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($fullPath)) {
        throw "Artifact does not exist: $fullPath"
    }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    if ($bytes.Length -eq 0 -or $bytes.Length -gt 131072) {
        throw 'Artifact must contain between 1 byte and 128 KiB.'
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw 'Artifact must be UTF-8 without BOM.'
    }
    $null = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 16
    $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]$bytes, $options)
    return [pscustomobject]@{
        FullPath = $fullPath
        Bytes = $bytes
        Document = $document
    }
}

Set-StrictMode -Version Latest

$script:NovaGameAdminAccessEnvironmentKeys = @(
    "NovaGame__AdminAccess__Jwt__SigningKey",
    "NovaGame__AdminAccess__Seed__UserName",
    "NovaGame__AdminAccess__Seed__Password",
    "NovaGame__AdminAccess__Seed__DisplayName"
)

function Resolve-NovaGameAdminAccessEnvFile {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$ExplicitPath
    )

    $candidate = $ExplicitPath
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = [Environment]::GetEnvironmentVariable("NOVAGAME_ADMIN_ACCESS_ENV_FILE")
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = Join-Path $RepositoryRoot "server\.env.local"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepositoryRoot $candidate
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function New-NovaGameAdminAccessSigningKey {
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Assert-NovaGameAdminAccessEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Values)

    $missingKeys = @($script:NovaGameAdminAccessEnvironmentKeys | Where-Object {
        -not $Values.Contains($_)
    })
    if ($missingKeys.Count -gt 0) {
        throw "Administrator access environment is missing required key(s): $($missingKeys -join ', ')."
    }

    $signingKey = [string]$Values["NovaGame__AdminAccess__Jwt__SigningKey"]
    if ([string]::IsNullOrWhiteSpace($signingKey) -or
        $signingKey -notmatch "^[A-Za-z0-9_-]+$") {
        throw "NovaGame__AdminAccess__Jwt__SigningKey must be a base64url value containing at least 32 bytes."
    }

    try {
        $base64 = $signingKey.Replace("-", "+").Replace("_", "/")
        $paddingLength = (4 - ($base64.Length % 4)) % 4
        if ($paddingLength -gt 0) {
            $base64 += [string]::new([char]"=", $paddingLength)
        }
        [byte[]]$keyBytes = [Convert]::FromBase64String($base64)
    }
    catch [FormatException] {
        throw "NovaGame__AdminAccess__Jwt__SigningKey must be a base64url value containing at least 32 bytes."
    }

    if ($keyBytes.Length -lt 32) {
        throw "NovaGame__AdminAccess__Jwt__SigningKey must be a base64url value containing at least 32 bytes."
    }

    $userName = ([string]$Values["NovaGame__AdminAccess__Seed__UserName"]).Trim()
    $password = [string]$Values["NovaGame__AdminAccess__Seed__Password"]
    $hasUserName = -not [string]::IsNullOrWhiteSpace($userName)
    $hasPassword = -not [string]::IsNullOrWhiteSpace($password)
    if ($hasUserName -ne $hasPassword) {
        throw "Administrator seed configuration requires both a user name and a password, or neither."
    }

    if ($hasUserName) {
        $containsControlCharacter = @($userName.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0
        if ($userName.Length -lt 3 -or $userName.Length -gt 64 -or
            $userName -match "\s" -or $containsControlCharacter) {
            throw "Administrator seed user names must contain 3 to 64 non-control characters without whitespace."
        }
        if ($password.Length -lt 12 -or $password.Length -gt 256) {
            throw "Administrator seed passwords must contain 12 to 256 characters."
        }
    }

    $displayName = ([string]$Values["NovaGame__AdminAccess__Seed__DisplayName"]).Trim()
    if ([string]::IsNullOrWhiteSpace($displayName) -or $displayName.Length -gt 128) {
        throw "NovaGame__AdminAccess__Seed__DisplayName must contain 1 to 128 characters."
    }
}

function Read-NovaGameAdminAccessEnvironment {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Administrator access environment file was not found at '$Path'. Run scripts/bootstrap.ps1 -Scope Server first."
    }

    $values = [ordered]@{}
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        $lineNumber++
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=", [StringComparison]::Ordinal)
        if ($separatorIndex -le 0) {
            throw "Invalid dotenv entry at '$Path' line $lineNumber."
        }

        $key = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        if ($key -notin $script:NovaGameAdminAccessEnvironmentKeys) {
            throw "Unknown administrator access environment key '$key' at '$Path' line $lineNumber."
        }
        if ($values.Contains($key)) {
            throw "Duplicate administrator access environment key '$key' at '$Path' line $lineNumber."
        }
        if ($value.StartsWith('"') -or $value.StartsWith("'")) {
            throw "Quoted dotenv values are not supported in '$Path' line $lineNumber."
        }

        $values[$key] = $value
    }

    Assert-NovaGameAdminAccessEnvironment -Values $values
    return $values
}

function Get-NovaGameAdminAccessEffectiveEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$FileValues)

    $effectiveValues = [ordered]@{}
    foreach ($key in $script:NovaGameAdminAccessEnvironmentKeys) {
        $processValue = [Environment]::GetEnvironmentVariable($key, "Process")
        $effectiveValues[$key] = if ($null -ne $processValue) {
            $processValue
        }
        else {
            [string]$FileValues[$key]
        }
    }

    Assert-NovaGameAdminAccessEnvironment -Values $effectiveValues
    return $effectiveValues
}

function Write-NovaGameAdminAccessEnvironmentFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$SigningKey,
        [string]$SeedUserName = "",
        [string]$SeedPassword = "",
        [string]$SeedDisplayName = "Administrator"
    )

    if ([string]::IsNullOrWhiteSpace($SigningKey)) {
        $SigningKey = New-NovaGameAdminAccessSigningKey
    }

    $values = [ordered]@{
        NovaGame__AdminAccess__Jwt__SigningKey = $SigningKey
        NovaGame__AdminAccess__Seed__UserName = $SeedUserName
        NovaGame__AdminAccess__Seed__Password = $SeedPassword
        NovaGame__AdminAccess__Seed__DisplayName = $SeedDisplayName
    }
    Assert-NovaGameAdminAccessEnvironment -Values $values

    if (Test-Path -LiteralPath $Path) {
        throw "Refusing to overwrite existing administrator access environment file '$Path'."
    }

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $lines = @($script:NovaGameAdminAccessEnvironmentKeys | ForEach-Object { "$_=$($values[$_])" })
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))
    try {
        foreach ($line in $lines) {
            $writer.WriteLine($line)
        }
    }
    finally {
        $writer.Dispose()
    }
}

function Initialize-NovaGameAdminAccessEnvironmentFile {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $null = Read-NovaGameAdminAccessEnvironment -Path $Path
        return "existing"
    }

    Write-NovaGameAdminAccessEnvironmentFile -Path $Path
    $null = Read-NovaGameAdminAccessEnvironment -Path $Path
    return "created"
}

function Set-NovaGameAdminAccessProcessEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Values)

    $previousValues = [ordered]@{}
    foreach ($key in $script:NovaGameAdminAccessEnvironmentKeys) {
        $previousValues[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        [Environment]::SetEnvironmentVariable($key, [string]$Values[$key], "Process")
    }

    return $previousValues
}

function Restore-NovaGameAdminAccessProcessEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$PreviousValues)

    foreach ($key in $script:NovaGameAdminAccessEnvironmentKeys) {
        [Environment]::SetEnvironmentVariable($key, $PreviousValues[$key], "Process")
    }
}

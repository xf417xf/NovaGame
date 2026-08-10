Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "process-environment.ps1")

$script:NovaGameDatabaseEnvironmentKeys = @(
    "NOVAGAME_MYSQL_DATABASE",
    "NOVAGAME_MYSQL_USER",
    "NOVAGAME_MYSQL_PASSWORD",
    "NOVAGAME_MYSQL_ROOT_PASSWORD",
    "NOVAGAME_MYSQL_BIND_ADDRESS",
    "NOVAGAME_MYSQL_PORT"
)

function Resolve-NovaGameDatabaseEnvFile {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$ExplicitPath
    )

    $candidate = $ExplicitPath
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = [Environment]::GetEnvironmentVariable("NOVAGAME_DATABASE_ENV_FILE")
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = Join-Path $RepositoryRoot "deploy\.env"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepositoryRoot $candidate
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function New-NovaGameBase64UrlSecret {
    param([ValidateRange(24, 128)][int]$ByteCount = 32)

    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes($ByteCount)
    return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Assert-NovaGameDatabaseEnvironment {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Values,
        [switch]$AllowEphemeralPort
    )

    $missingKeys = @($script:NovaGameDatabaseEnvironmentKeys | Where-Object {
        -not $Values.Contains($_) -or [string]::IsNullOrWhiteSpace([string]$Values[$_])
    })
    if ($missingKeys.Count -gt 0) {
        throw "Database environment is missing required value(s): $($missingKeys -join ', ')."
    }

    $database = [string]$Values["NOVAGAME_MYSQL_DATABASE"]
    if ($database -notmatch "^[A-Za-z][A-Za-z0-9_]{0,63}$") {
        throw "NOVAGAME_MYSQL_DATABASE must be a 1-64 character MySQL identifier."
    }

    $user = [string]$Values["NOVAGAME_MYSQL_USER"]
    if ($user -notmatch "^[A-Za-z][A-Za-z0-9_]{0,31}$" -or $user -eq "root") {
        throw "NOVAGAME_MYSQL_USER must be a non-root 1-32 character MySQL identifier."
    }

    foreach ($passwordKey in @("NOVAGAME_MYSQL_PASSWORD", "NOVAGAME_MYSQL_ROOT_PASSWORD")) {
        $password = [string]$Values[$passwordKey]
        if ($password -notmatch "^[A-Za-z0-9_-]{32,171}$") {
            throw "$passwordKey must be a 32-171 character base64url value."
        }
    }

    if ([string]$Values["NOVAGAME_MYSQL_BIND_ADDRESS"] -ne "127.0.0.1") {
        throw "NOVAGAME_MYSQL_BIND_ADDRESS must remain 127.0.0.1 for the local M0.5 runtime."
    }

    $port = 0
    if (-not [int]::TryParse([string]$Values["NOVAGAME_MYSQL_PORT"], [ref]$port)) {
        throw "NOVAGAME_MYSQL_PORT must be an integer."
    }

    $minimumPort = if ($AllowEphemeralPort) { 0 } else { 1 }
    if ($port -lt $minimumPort -or $port -gt 65535) {
        throw "NOVAGAME_MYSQL_PORT must be between $minimumPort and 65535."
    }
}

function Read-NovaGameDatabaseEnvironment {
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowEphemeralPort
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Database environment file was not found at '$Path'. Run scripts/bootstrap.ps1 -Scope Database first."
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
        if ($key -notin $script:NovaGameDatabaseEnvironmentKeys) {
            throw "Unknown database environment key '$key' at '$Path' line $lineNumber."
        }
        if ($values.Contains($key)) {
            throw "Duplicate database environment key '$key' at '$Path' line $lineNumber."
        }
        if ($value.StartsWith('"') -or $value.StartsWith("'")) {
            throw "Quoted dotenv values are not supported in '$Path' line $lineNumber."
        }

        $values[$key] = $value
    }

    Assert-NovaGameDatabaseEnvironment -Values $values -AllowEphemeralPort:$AllowEphemeralPort
    return $values
}

function Write-NovaGameDatabaseEnvironmentFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Database = "novagame",
        [string]$User = "novagame",
        [string]$Password,
        [string]$RootPassword,
        [string]$BindAddress = "127.0.0.1",
        [ValidateRange(0, 65535)][int]$Port = 3306,
        [switch]$AllowEphemeralPort
    )

    if ([string]::IsNullOrWhiteSpace($Password)) {
        $Password = New-NovaGameBase64UrlSecret
    }
    if ([string]::IsNullOrWhiteSpace($RootPassword)) {
        $RootPassword = New-NovaGameBase64UrlSecret
    }

    $values = [ordered]@{
        NOVAGAME_MYSQL_DATABASE = $Database
        NOVAGAME_MYSQL_USER = $User
        NOVAGAME_MYSQL_PASSWORD = $Password
        NOVAGAME_MYSQL_ROOT_PASSWORD = $RootPassword
        NOVAGAME_MYSQL_BIND_ADDRESS = $BindAddress
        NOVAGAME_MYSQL_PORT = [string]$Port
    }
    Assert-NovaGameDatabaseEnvironment -Values $values -AllowEphemeralPort:$AllowEphemeralPort

    if (Test-Path -LiteralPath $Path) {
        throw "Refusing to overwrite existing database environment file '$Path'."
    }

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $lines = @($script:NovaGameDatabaseEnvironmentKeys | ForEach-Object { "$_=$($values[$_])" })
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

function Initialize-NovaGameDatabaseEnvironmentFile {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $null = Read-NovaGameDatabaseEnvironment -Path $Path
        return "existing"
    }

    Write-NovaGameDatabaseEnvironmentFile -Path $Path
    $null = Read-NovaGameDatabaseEnvironment -Path $Path
    return "created"
}

function Set-NovaGameDatabaseProcessEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Values)

    $previousValues = Get-NovaGameProcessEnvironmentSnapshot `
        -Names $script:NovaGameDatabaseEnvironmentKeys
    foreach ($key in $script:NovaGameDatabaseEnvironmentKeys) {
        [Environment]::SetEnvironmentVariable($key, [string]$Values[$key], "Process")
    }

    return $previousValues
}

function Restore-NovaGameDatabaseProcessEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$PreviousValues)

    Restore-NovaGameProcessEnvironmentSnapshot -Snapshot $PreviousValues
}

function Get-NovaGameComposeArguments {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$EnvironmentFile,
        [string]$ProjectName
    )

    $deployRoot = Join-Path $RepositoryRoot "deploy"
    $composeFile = Join-Path $deployRoot "docker-compose.dev.yml"
    if (-not (Test-Path -LiteralPath $composeFile -PathType Leaf)) {
        throw "Docker Compose file was not found at '$composeFile'."
    }

    $arguments = @(
        "compose",
        "--project-directory", $deployRoot,
        "--env-file", $EnvironmentFile
    )
    if (-not [string]::IsNullOrWhiteSpace($ProjectName)) {
        $arguments += @("--project-name", $ProjectName)
    }
    $arguments += @("--file", $composeFile)
    return $arguments
}

function New-NovaGameMySqlConnectionString {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Environment,
        [string]$Server = "127.0.0.1",
        [Nullable[int]]$Port
    )

    $resolvedPort = if ($null -ne $Port) {
        [int]$Port
    }
    else {
        [int]$Environment["NOVAGAME_MYSQL_PORT"]
    }

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder["Server"] = $Server
    $builder["Port"] = $resolvedPort
    $builder["Database"] = [string]$Environment["NOVAGAME_MYSQL_DATABASE"]
    $builder["User ID"] = [string]$Environment["NOVAGAME_MYSQL_USER"]
    $builder["Password"] = [string]$Environment["NOVAGAME_MYSQL_PASSWORD"]
    $builder["SslMode"] = "None"
    $builder["AllowPublicKeyRetrieval"] = $true
    $builder["Connection Timeout"] = 15
    $builder["DateTimeKind"] = "Utc"
    return $builder.ConnectionString
}

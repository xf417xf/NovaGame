[CmdletBinding()]
param(
    [ValidateSet("Database", "Migrations", "Server", "Admin", "All")]
    [string]$Scope = "Server",

    [string]$DatabaseEnvFile,

    [string]$AdminAccessEnvFile,

    [string]$ApiUrl = "http://127.0.0.1:5000",

    [ValidateRange(1, 65535)]
    [int]$AdminPort = 5777,

    [string]$VbenTemplateRoot,

    [string]$NodePath,

    [string]$PnpmBootstrapPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot
. (Join-Path $PSScriptRoot "database-environment.ps1")
. (Join-Path $PSScriptRoot "admin-access-environment.ps1")

function Require-Command {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Required command '$Name' was not found on PATH."
    }

    return $command.Source
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Initialize-AdminToolchain {
    $arguments = @(
        "-NoProfile",
        "-File", (Join-Path $PSScriptRoot "bootstrap.ps1"),
        "-Scope", "Admin"
    )
    if (-not [string]::IsNullOrWhiteSpace($VbenTemplateRoot)) {
        $arguments += @("-VbenTemplateRoot", $VbenTemplateRoot)
    }
    if (-not [string]::IsNullOrWhiteSpace($NodePath)) {
        $arguments += @("-NodePath", $NodePath)
    }
    if (-not [string]::IsNullOrWhiteSpace($PnpmBootstrapPath)) {
        $arguments += @("-PnpmBootstrapPath", $PnpmBootstrapPath)
    }

    & pwsh @arguments | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "Admin bootstrap failed with exit code $LASTEXITCODE."
    }

    $manifestPath = Join-Path $repositoryRoot ".local\admin-toolchain.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Admin toolchain manifest is missing after bootstrap."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1) {
        throw "Unsupported admin toolchain manifest schema."
    }
    foreach ($requiredFile in @(
        [string]$manifest.nodePath,
        [string]$manifest.pnpmBootstrapPath
    )) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Admin toolchain file is missing at '$requiredFile'."
        }
    }
    if (-not (Test-Path -LiteralPath ([string]$manifest.adminRoot) -PathType Container)) {
        throw "Admin root is missing at '$($manifest.adminRoot)'."
    }

    return $manifest
}

function Invoke-AdminPnpm {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [string[]]$ArgumentList = @()
    )

    $previousPath = $env:PATH
    $previousStorePath = $env:pnpm_config_store_dir
    $env:PATH = "$(Split-Path -Parent ([string]$Toolchain.nodePath))$([System.IO.Path]::PathSeparator)$previousPath"
    $env:pnpm_config_store_dir = [string]$Toolchain.storePath
    Push-Location -LiteralPath ([string]$Toolchain.adminRoot)
    try {
        $pnpmArguments = @(
            [string]$Toolchain.pnpmBootstrapPath,
            "dlx",
            "pnpm@$($Toolchain.pnpmVersion)"
        ) + $ArgumentList
        Invoke-CheckedCommand ([string]$Toolchain.nodePath) $pnpmArguments
    }
    finally {
        Pop-Location
        $env:PATH = $previousPath
        $env:pnpm_config_store_dir = $previousStorePath
    }
}

function Wait-HttpEndpoint {
    param(
        [Parameter(Mandatory)][Uri]$Uri,
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$StandardOutputPath,
        [Parameter(Mandatory)][string]$StandardErrorPath,
        [System.Collections.IDictionary]$Headers = @{},
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "API process exited with code $($Process.ExitCode). Logs: '$StandardOutputPath', '$StandardErrorPath'."
        }

        try {
            $requestArguments = @{
                Uri = $Uri
                Method = "Get"
                Headers = $Headers
                TimeoutSec = 2
            }
            if ($Uri.Scheme -eq "https") {
                $requestArguments.SkipCertificateCheck = $true
            }
            $response = Invoke-WebRequest @requestArguments
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "API did not become healthy at '$Uri' within $TimeoutSeconds seconds. Logs: '$StandardOutputPath', '$StandardErrorPath'."
}

function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }
    $Process.Refresh()
    if ($Process.HasExited) {
        return
    }

    $taskkill = Get-Command "taskkill.exe" -ErrorAction SilentlyContinue
    if ($taskkill) {
        & $taskkill.Source "/PID" ([string]$Process.Id) "/T" "/F" | Out-Null
    }
    else {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
}

[Uri]$parsedApiUrl = $null
if (-not [Uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$parsedApiUrl) -or
    $parsedApiUrl.Scheme -notin @("http", "https")) {
    throw "ApiUrl must be an absolute HTTP or HTTPS URL."
}

$needsDatabase = $Scope -in @("Database", "Migrations", "Server", "All")
$needsServer = $Scope -in @("Migrations", "Server", "All")
$needsAdmin = $Scope -in @("Admin", "All")

$adminToolchain = $null
if ($needsAdmin) {
    $adminToolchain = Initialize-AdminToolchain
}

$previousDatabaseEnvironment = $null
$previousConnectionString = $env:ConnectionStrings__Default
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$previousAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDotNetEnvironment = $env:DOTNET_ENVIRONMENT
$previousForwardedHeadersEnabled = $env:NovaCore__Web__ForwardedHeaders__IsEnabled
$previousKnownProxy = $env:NovaCore__Web__ForwardedHeaders__KnownProxies__0
$previousAdminAccessEnvironment = $null
$previousViteApiUrl = $env:VITE_GLOB_API_URL
$previousViteApiTarget = $env:VITE_NOVAGAME_API_URL
$previousViteMock = $env:VITE_NITRO_MOCK
$previousViteHttps = $env:VITE_HTTPS
$previousVitePort = $env:VITE_PORT
$previousAdminPort = $env:NOVAGAME_ADMIN_PORT
$previousNovaGameApiUrl = $env:NOVAGAME_API_URL
$apiProcess = $null

try {
    if ($needsDatabase) {
        $docker = Require-Command "docker"
        $resolvedDatabaseEnvFile = Resolve-NovaGameDatabaseEnvFile `
            -RepositoryRoot $repositoryRoot `
            -ExplicitPath $DatabaseEnvFile
        $databaseEnvironment = Read-NovaGameDatabaseEnvironment -Path $resolvedDatabaseEnvFile
        $composeArguments = Get-NovaGameComposeArguments `
            -RepositoryRoot $repositoryRoot `
            -EnvironmentFile $resolvedDatabaseEnvFile `
            -ProjectName "novagame-dev"
        $previousDatabaseEnvironment = Set-NovaGameDatabaseProcessEnvironment `
            -Values $databaseEnvironment

        Invoke-CheckedCommand $docker @("compose", "version")
        Invoke-CheckedCommand $docker @("info", "--format", "{{.ServerVersion}}")
        Invoke-CheckedCommand $docker ($composeArguments + @("config", "--quiet"))
        Invoke-CheckedCommand $docker ($composeArguments + @(
            "up", "--detach", "--wait", "--wait-timeout", "180", "mysql"
        ))
        Invoke-CheckedCommand $docker ($composeArguments + @("ps", "mysql"))
    }

    if ($Scope -eq "Database") {
        Write-Output "MySQL is healthy and listening on the configured loopback port."
        return
    }

    if ($needsServer) {
        $dotnet = Require-Command "dotnet"
        $connectionString = New-NovaGameMySqlConnectionString -Environment $databaseEnvironment
        $resolvedAdminAccessEnvFile = Resolve-NovaGameAdminAccessEnvFile `
            -RepositoryRoot $repositoryRoot `
            -ExplicitPath $AdminAccessEnvFile
        $adminAccessFileEnvironment = Read-NovaGameAdminAccessEnvironment `
            -Path $resolvedAdminAccessEnvFile
        $adminAccessEnvironment = Get-NovaGameAdminAccessEffectiveEnvironment `
            -FileValues $adminAccessFileEnvironment
        $previousAdminAccessEnvironment = Set-NovaGameAdminAccessProcessEnvironment `
            -Values $adminAccessEnvironment
        $env:ConnectionStrings__Default = $connectionString
        $env:ASPNETCORE_URLS = $ApiUrl
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        $env:DOTNET_ENVIRONMENT = "Development"
        $env:NovaCore__Web__ForwardedHeaders__IsEnabled = "true"
        $env:NovaCore__Web__ForwardedHeaders__KnownProxies__0 = "127.0.0.1"

        Invoke-CheckedCommand $dotnet @(
            "run",
            "--project", "server/src/NovaGame.DbMigrator/NovaGame.DbMigrator.csproj",
            "--no-launch-profile"
        )

        if ($Scope -eq "Migrations") {
            Write-Output "NovaGame database migrations and seed contributors completed successfully."
            return
        }

        if ($Scope -eq "Server") {
            Invoke-CheckedCommand $dotnet @(
                "run",
                "--project", "server/src/NovaGame.Api/NovaGame.Api.csproj",
                "--no-launch-profile"
            )
            return
        }

        $logRoot = Join-Path $repositoryRoot ".local\logs"
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
        $runId = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
        $apiStandardOutputPath = Join-Path $logRoot "api-$runId.stdout.log"
        $apiStandardErrorPath = Join-Path $logRoot "api-$runId.stderr.log"
        $apiProcess = Start-Process `
            -FilePath $dotnet `
            -ArgumentList @(
                "run",
                "--project", "server/src/NovaGame.Api/NovaGame.Api.csproj",
                "--no-launch-profile"
            ) `
            -WorkingDirectory $repositoryRoot `
            -WindowStyle Hidden `
            -RedirectStandardOutput $apiStandardOutputPath `
            -RedirectStandardError $apiStandardErrorPath `
            -PassThru

        $healthUri = [Uri]("$($ApiUrl.TrimEnd('/'))/health/live")
        Wait-HttpEndpoint `
            -Uri $healthUri `
            -Process $apiProcess `
            -StandardOutputPath $apiStandardOutputPath `
            -StandardErrorPath $apiStandardErrorPath `
            -Headers @{
                "X-Forwarded-For" = "127.0.0.1"
                "X-Forwarded-Proto" = "https"
                "X-Forwarded-Host" = "localhost:$AdminPort"
            }
        Write-Output "NovaGame API is healthy at '$ApiUrl'."
        Write-Output "API logs: '$apiStandardOutputPath', '$apiStandardErrorPath'."
    }

    if ($needsAdmin) {
        $env:VITE_GLOB_API_URL = "/api"
        $env:VITE_NOVAGAME_API_URL = $ApiUrl
        $env:VITE_NITRO_MOCK = "false"
        $env:VITE_HTTPS = "true"
        $env:VITE_PORT = [string]$AdminPort
        $env:NOVAGAME_ADMIN_PORT = [string]$AdminPort
        $env:NOVAGAME_API_URL = $ApiUrl
        Write-Output "NovaGame Admin is starting at 'https://localhost:$AdminPort'."
        Write-Output "Admin API proxy target: '$ApiUrl'."
        Invoke-AdminPnpm -Toolchain $adminToolchain -ArgumentList @("run", "dev:ele")
    }
}
finally {
    Stop-ProcessTree -Process $apiProcess
    if ($null -ne $previousDatabaseEnvironment) {
        Restore-NovaGameDatabaseProcessEnvironment -PreviousValues $previousDatabaseEnvironment
    }
    $env:ConnectionStrings__Default = $previousConnectionString
    $env:ASPNETCORE_URLS = $previousAspNetCoreUrls
    $env:ASPNETCORE_ENVIRONMENT = $previousAspNetCoreEnvironment
    $env:DOTNET_ENVIRONMENT = $previousDotNetEnvironment
    $env:NovaCore__Web__ForwardedHeaders__IsEnabled = $previousForwardedHeadersEnabled
    $env:NovaCore__Web__ForwardedHeaders__KnownProxies__0 = $previousKnownProxy
    $env:VITE_GLOB_API_URL = $previousViteApiUrl
    $env:VITE_NOVAGAME_API_URL = $previousViteApiTarget
    $env:VITE_NITRO_MOCK = $previousViteMock
    $env:VITE_HTTPS = $previousViteHttps
    $env:VITE_PORT = $previousVitePort
    $env:NOVAGAME_ADMIN_PORT = $previousAdminPort
    $env:NOVAGAME_API_URL = $previousNovaGameApiUrl
    if ($null -ne $previousAdminAccessEnvironment) {
        Restore-NovaGameAdminAccessProcessEnvironment `
            -PreviousValues $previousAdminAccessEnvironment
    }
}

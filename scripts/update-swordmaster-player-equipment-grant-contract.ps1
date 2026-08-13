[CmdletBinding()]
param(
    [string]$NodePath,
    [string]$PnpmBootstrapPath,
    [switch]$SimulateInvalidGenerationInput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sampleRoot = Join-Path $repositoryRoot "samples\SwordMaster"
$adminRoot = Join-Path $sampleRoot "admin"
$integrationProject = Join-Path `
    $sampleRoot `
    "server\tests\SwordMaster.Integration.Tests\SwordMaster.Integration.Tests.csproj"
$trackedContractPath = Join-Path `
    $adminRoot `
    "contracts\player-equipment-grants.openapi.json"
$trackedTypesPath = Join-Path `
    $adminRoot `
    "src\generated\player-equipment-grants\types.gen.ts"
$playerContractPath = Join-Path `
    $sampleRoot `
    "contracts\player-assets.openapi.json"
$playerGeneratedTypesPath = Join-Path `
    $sampleRoot `
    "src\generated\player-assets\types.gen.ts"
$temporaryRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "novagame-player-equipment-grant-contract-$([Guid]::NewGuid().ToString('N'))"
$capturedContractPath = Join-Path `
    $temporaryRoot `
    "capture\player-equipment-grants.openapi.json"
$temporaryGeneratedTypesRoot = Join-Path `
    $temporaryRoot `
    "generated\player-equipment-grants"
$temporaryGeneratedTypesPath = Join-Path `
    $temporaryGeneratedTypesRoot `
    "types.gen.ts"
$temporaryBackupRoot = Join-Path $temporaryRoot "tracked-backup"

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

function Assert-PathWithin {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ParentPath
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith(
            $resolvedParent,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$resolvedPath' must remain under '$resolvedParent'."
    }
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-ProcessEnvironmentVariableSnapshot {
    param([Parameter(Mandatory)][string]$Name)

    return [pscustomobject]@{
        Exists = Test-Path -LiteralPath "Env:$Name"
        Value = [Environment]::GetEnvironmentVariable(
            $Name,
            [EnvironmentVariableTarget]::Process)
    }
}

function Restore-ProcessEnvironmentVariable {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][psobject]$Snapshot
    )

    if ($Snapshot.Exists) {
        [Environment]::SetEnvironmentVariable(
            $Name,
            [string]$Snapshot.Value,
            [EnvironmentVariableTarget]::Process)
        return
    }

    Remove-Item -LiteralPath "Env:$Name" -ErrorAction SilentlyContinue
}

function Assert-LegacyPlayerArtifactsUnchanged {
    param(
        [Parameter(Mandatory)][string]$ExpectedContractHash,
        [Parameter(Mandatory)][string]$ExpectedTypesHash
    )

    $errors = [System.Collections.Generic.List[Exception]]::new()
    try {
        $currentHash = Get-FileSha256 -Path $playerContractPath
        if ($currentHash -ne $ExpectedContractHash) {
            throw "The existing PlayerAssets contract changed during equipment-grant generation."
        }
    }
    catch {
        $errors.Add($_.Exception)
    }

    try {
        $currentHash = Get-FileSha256 -Path $playerGeneratedTypesPath
        if ($currentHash -ne $ExpectedTypesHash) {
            throw "The existing PlayerAssets generated types changed during equipment-grant generation."
        }
    }
    catch {
        $errors.Add($_.Exception)
    }

    if ($errors.Count -eq 1) {
        throw $errors[0]
    }
    if ($errors.Count -gt 1) {
        throw [AggregateException]::new(
            "Both legacy PlayerAssets artifact checks failed.",
            $errors)
    }
}

function Assert-TemporaryOperatorArtifacts {
    param([Parameter(Mandatory)][string]$GenerationInputPath)

    if (-not (Test-Path -LiteralPath $capturedContractPath -PathType Leaf)) {
        throw "The real Program contract capture did not create '$capturedContractPath'."
    }

    $contractBytes = [System.IO.File]::ReadAllBytes($capturedContractPath)
    $hasUtf8Bom = $contractBytes.Length -ge 3 -and
        $contractBytes[0] -eq 0xEF -and
        $contractBytes[1] -eq 0xBB -and
        $contractBytes[2] -eq 0xBF
    if ($contractBytes.Length -eq 0 -or $hasUtf8Bom) {
        throw "The captured equipment-grant contract must be non-empty UTF-8 without a BOM."
    }
    $contractText = [System.Text.Encoding]::UTF8.GetString($contractBytes)
    if (-not $contractText.EndsWith("`n", [StringComparison]::Ordinal) -or
        $contractText.Contains("`r", [StringComparison]::Ordinal)) {
        throw "The captured equipment-grant contract must use canonical LF line endings."
    }

    $contract = $contractText | ConvertFrom-Json -AsHashtable
    $expectedPaths = @(
        "/api/swordmaster/operations/player-assets/{playerId}/equipment-grants",
        "/api/swordmaster/operations/player-assets/{playerId}/equipment-grants/{grantId}/rollback"
    )
    if (@(Compare-Object $expectedPaths @($contract.paths.Keys)).Count -ne 0) {
        throw "The captured equipment-grant contract contains an unapproved path set."
    }

    $httpMethods = @(
        "delete", "get", "head", "options", "patch", "post", "put", "trace")
    $operationCount = 0
    foreach ($path in $contract.paths.Values) {
        foreach ($method in $httpMethods) {
            if (-not $path.ContainsKey($method)) {
                continue
            }

            $operationCount++
            $security = @($path[$method].security)
            if ($security.Count -ne 1 -or
                @($security[0].Keys).Count -ne 1 -or
                -not $security[0].ContainsKey("Bearer")) {
                throw "Every captured equipment-grant operation must require only Bearer security."
            }
        }
    }
    if ($operationCount -ne 3) {
        throw "The captured equipment-grant contract must contain exactly three operations."
    }

    $securitySchemes = @($contract.components.securitySchemes.Keys)
    if ($securitySchemes.Count -ne 1 -or $securitySchemes[0] -ne "Bearer") {
        throw "The captured equipment-grant contract must contain only the Bearer security scheme."
    }
    foreach ($excludedValue in @(
            "SwordMasterPlayer",
            "/api/player-assets/characters/",
            "/api/auth/",
            "/api/game-config/")) {
        if ($contractText.Contains($excludedValue, [StringComparison]::Ordinal)) {
            throw "The captured equipment-grant contract contains excluded value '$excludedValue'."
        }
    }

    if (-not (Test-Path `
            -LiteralPath $temporaryGeneratedTypesRoot `
            -PathType Container)) {
        throw "Equipment-grant type generation for '$GenerationInputPath' did not produce its temporary output directory."
    }

    $generatedFiles = @(
        Get-ChildItem `
            -LiteralPath $temporaryGeneratedTypesRoot `
            -File `
            -Recurse)
    if ($generatedFiles.Count -ne 1 -or
        $generatedFiles[0].FullName -ne $temporaryGeneratedTypesPath) {
        throw "Equipment-grant generation must produce only 'types.gen.ts'."
    }
    $generatedTypes = Get-Content `
        -LiteralPath $temporaryGeneratedTypesPath `
        -Raw
    if ($generatedTypes -match '(?m)^export\s+(?!type\b)') {
        throw "Equipment-grant generation must expose types only."
    }
    if ($generatedTypes -notmatch
        '(?ms)export type GrantPlayerEquipmentRequest = \{\s*configuredItemId: string;\s*reason: string;\s*\};' -or
        $generatedTypes -notmatch
        '(?ms)export type RollbackPlayerEquipmentGrantRequest = \{\s*reason: string;\s*\};') {
        throw "Equipment-grant request types must keep their frozen required non-null inputs."
    }
}

function Publish-TrackedOperatorArtifacts {
    $artifacts = @(
        [pscustomobject]@{
            Staged = $capturedContractPath
            Destination = $trackedContractPath
            Backup = Join-Path $temporaryBackupRoot "player-equipment-grants.openapi.json"
            Existed = $false
        },
        [pscustomobject]@{
            Staged = $temporaryGeneratedTypesPath
            Destination = $trackedTypesPath
            Backup = Join-Path $temporaryBackupRoot "types.gen.ts"
            Existed = $false
        }
    )

    foreach ($artifact in $artifacts) {
        if ((Test-Path -LiteralPath $artifact.Destination) -and
            -not (Test-Path -LiteralPath $artifact.Destination -PathType Leaf)) {
            throw "Tracked operator artifact '$($artifact.Destination)' is not a file."
        }
    }

    New-Item -ItemType Directory -Path $temporaryBackupRoot -Force |
        Out-Null
    foreach ($artifact in $artifacts) {
        $artifact.Existed = Test-Path `
            -LiteralPath $artifact.Destination `
            -PathType Leaf
        if ($artifact.Existed) {
            Copy-Item `
                -LiteralPath $artifact.Destination `
                -Destination $artifact.Backup
        }
    }

    $publishError = $null
    try {
        foreach ($artifact in $artifacts) {
            $destinationDirectory = Split-Path -Parent $artifact.Destination
            New-Item `
                -ItemType Directory `
                -Path $destinationDirectory `
                -Force | Out-Null
            Copy-Item `
                -LiteralPath $artifact.Staged `
                -Destination $artifact.Destination `
                -Force
        }

        foreach ($artifact in $artifacts) {
            if ((Get-FileSha256 -Path $artifact.Destination) -ne
                (Get-FileSha256 -Path $artifact.Staged)) {
                throw "Published operator artifact '$($artifact.Destination)' does not match its validated staging file."
            }
        }
    }
    catch {
        $publishError = $_.Exception
    }

    if ($null -eq $publishError) {
        return
    }

    $rollbackErrors = [System.Collections.Generic.List[Exception]]::new()
    foreach ($artifact in $artifacts) {
        try {
            if ($artifact.Existed) {
                Copy-Item `
                    -LiteralPath $artifact.Backup `
                    -Destination $artifact.Destination `
                    -Force
            }
            elseif (Test-Path -LiteralPath $artifact.Destination) {
                Remove-Item -LiteralPath $artifact.Destination -Force
            }
        }
        catch {
            $rollbackErrors.Add($_.Exception)
        }
    }

    if ($rollbackErrors.Count -eq 0) {
        throw $publishError
    }

    $publishAndRollbackErrors =
        [System.Collections.Generic.List[Exception]]::new()
    $publishAndRollbackErrors.Add($publishError)
    $publishAndRollbackErrors.AddRange($rollbackErrors)
    throw [AggregateException]::new(
        "Publishing the operator artifacts failed and rollback was incomplete.",
        $publishAndRollbackErrors)
}

Assert-PathWithin `
    -Path $temporaryRoot `
    -ParentPath ([System.IO.Path]::GetTempPath())

if (-not (Test-Path -LiteralPath $playerContractPath -PathType Leaf)) {
    throw "The existing PlayerAssets contract is missing at '$playerContractPath'."
}
if (-not (Test-Path -LiteralPath $playerGeneratedTypesPath -PathType Leaf)) {
    throw "The existing PlayerAssets generated types are missing at '$playerGeneratedTypesPath'."
}

$playerContractHash = Get-FileSha256 -Path $playerContractPath
$playerGeneratedTypesHash = Get-FileSha256 -Path $playerGeneratedTypesPath
$captureEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "SWORDMASTER_PLAYER_EQUIPMENT_GRANT_OPENAPI_CAPTURE_PATH"
$openApiInputEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT"
$typesOutputEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT"
$pathEnvironment = Get-ProcessEnvironmentVariableSnapshot -Name "PATH"
$storeEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "pnpm_config_store_dir"
$workflowError = $null

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    $env:SWORDMASTER_PLAYER_EQUIPMENT_GRANT_OPENAPI_CAPTURE_PATH = `
        $capturedContractPath
    Invoke-CheckedCommand "dotnet" @(
        "test",
        $integrationProject,
        "--configuration", "Release",
        "--filter", "FullyQualifiedName~PlayerEquipmentGrantOpenApiContractTests",
        "--verbosity", "minimal",
        "-p:NuGetAudit=false"
    )
    if (-not (Test-Path -LiteralPath $capturedContractPath -PathType Leaf)) {
        throw "The real Program contract capture did not create '$capturedContractPath'."
    }

    $bootstrapArguments = @(
        "-NoProfile",
        "-File", (Join-Path $PSScriptRoot "bootstrap.ps1"),
        "-Scope", "SwordMaster"
    )
    if (-not [string]::IsNullOrWhiteSpace($NodePath)) {
        $bootstrapArguments += @("-NodePath", $NodePath)
    }
    if (-not [string]::IsNullOrWhiteSpace($PnpmBootstrapPath)) {
        $bootstrapArguments += @(
            "-PnpmBootstrapPath",
            $PnpmBootstrapPath)
    }
    Invoke-CheckedCommand "pwsh" $bootstrapArguments

    $toolchainManifestPath = Join-Path `
        $repositoryRoot `
        ".local\swordmaster-toolchain.json"
    if (-not (Test-Path -LiteralPath $toolchainManifestPath -PathType Leaf)) {
        throw "SwordMaster bootstrap did not create '$toolchainManifestPath'."
    }
    $toolchain = Get-Content `
        -LiteralPath $toolchainManifestPath `
        -Raw | ConvertFrom-Json

    $generationInputPath = if ($SimulateInvalidGenerationInput) {
        Join-Path $temporaryRoot "missing-generation-input.openapi.json"
    }
    else {
        $capturedContractPath
    }
    Assert-PathWithin `
        -Path $generationInputPath `
        -ParentPath ([System.IO.Path]::GetTempPath())

    $env:PATH = "$(Split-Path -Parent ([string]$toolchain.nodePath))$([System.IO.Path]::PathSeparator)$([string]$pathEnvironment.Value)"
    $env:pnpm_config_store_dir = [string]$toolchain.storePath
    $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT = $generationInputPath
    $env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT = `
        $temporaryGeneratedTypesRoot
    Push-Location -LiteralPath $sampleRoot
    try {
        Invoke-CheckedCommand ([string]$toolchain.nodePath) @(
            [string]$toolchain.pnpmBootstrapPath,
            "dlx",
            "pnpm@$($toolchain.pnpmVersion)",
            "run",
            "contract:generate",
            "--no-log-file"
        )
    }
    finally {
        Pop-Location
    }

    Assert-TemporaryOperatorArtifacts `
        -GenerationInputPath $generationInputPath
    Publish-TrackedOperatorArtifacts
}
catch {
    $workflowError = $_.Exception
}

$finalizationErrors = [System.Collections.Generic.List[Exception]]::new()
try {
    Assert-LegacyPlayerArtifactsUnchanged `
        -ExpectedContractHash $playerContractHash `
        -ExpectedTypesHash $playerGeneratedTypesHash
}
catch {
    $finalizationErrors.Add($_.Exception)
}

$environmentRestorations = [ordered]@{
    SWORDMASTER_PLAYER_EQUIPMENT_GRANT_OPENAPI_CAPTURE_PATH = `
        $captureEnvironment
    SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT = $openApiInputEnvironment
    SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT = $typesOutputEnvironment
    PATH = $pathEnvironment
    pnpm_config_store_dir = $storeEnvironment
}
foreach ($environmentRestoration in $environmentRestorations.GetEnumerator()) {
    try {
        Restore-ProcessEnvironmentVariable `
            -Name $environmentRestoration.Key `
            -Snapshot $environmentRestoration.Value
    }
    catch {
        $finalizationErrors.Add($_.Exception)
    }
}

try {
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        Assert-PathWithin `
            -Path $temporaryRoot `
            -ParentPath ([System.IO.Path]::GetTempPath())
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
catch {
    $finalizationErrors.Add($_.Exception)
}

$allErrors = [System.Collections.Generic.List[Exception]]::new()
if ($null -ne $workflowError) {
    $allErrors.Add($workflowError)
}
$allErrors.AddRange($finalizationErrors)
if ($allErrors.Count -eq 1) {
    throw $allErrors[0]
}
if ($allErrors.Count -gt 1) {
    throw [AggregateException]::new(
        "Equipment-grant contract update and final validation reported multiple failures.",
        $allErrors)
}

Write-Output "SwordMaster player-equipment-grant contract and TypeScript types updated."

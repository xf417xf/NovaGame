[CmdletBinding()]
param(
    [string]$NodePath,
    [string]$PnpmBootstrapPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sampleRoot = Join-Path $repositoryRoot "samples\SwordMaster"
$integrationProject = Join-Path `
    $sampleRoot `
    "server\tests\SwordMaster.Integration.Tests\SwordMaster.Integration.Tests.csproj"
$trackedContractPath = Join-Path `
    $sampleRoot `
    "contracts\player-assets.openapi.json"
$temporaryRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "novagame-player-assets-contract-$([Guid]::NewGuid().ToString('N'))"
$capturedContractPath = Join-Path $temporaryRoot "player-assets.openapi.json"

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

Assert-PathWithin `
    -Path $temporaryRoot `
    -ParentPath ([System.IO.Path]::GetTempPath())

$previousCapturePath = `
    $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_CAPTURE_PATH
$previousPath = $env:PATH
$previousStorePath = $env:pnpm_config_store_dir

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_CAPTURE_PATH = `
        $capturedContractPath
    Invoke-CheckedCommand "dotnet" @(
        "test",
        $integrationProject,
        "--configuration", "Release",
        "--filter", "FullyQualifiedName~PlayerAssetsOpenApiContractTests",
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

    $contractDirectory = Split-Path -Parent $trackedContractPath
    New-Item -ItemType Directory -Path $contractDirectory -Force | Out-Null
    Copy-Item `
        -LiteralPath $capturedContractPath `
        -Destination $trackedContractPath `
        -Force

    $env:PATH = "$(Split-Path -Parent ([string]$toolchain.nodePath))$([System.IO.Path]::PathSeparator)$previousPath"
    $env:pnpm_config_store_dir = [string]$toolchain.storePath
    Push-Location -LiteralPath $sampleRoot
    try {
        Invoke-CheckedCommand ([string]$toolchain.nodePath) @(
            [string]$toolchain.pnpmBootstrapPath,
            "dlx",
            "pnpm@$($toolchain.pnpmVersion)",
            "run",
            "contract:generate"
        )
    }
    finally {
        Pop-Location
    }

    $generatedFiles = @(
        Get-ChildItem `
            -LiteralPath (Join-Path $sampleRoot "src\generated\player-assets") `
            -File `
            -Recurse)
    if ($generatedFiles.Count -ne 1 -or
        $generatedFiles[0].Name -ne "types.gen.ts") {
        throw "PlayerAssets generation must produce only 'types.gen.ts'."
    }

    Write-Output "SwordMaster PlayerAssets contract and TypeScript types updated."
}
finally {
    $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_CAPTURE_PATH = `
        $previousCapturePath
    $env:PATH = $previousPath
    $env:pnpm_config_store_dir = $previousStorePath

    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        Assert-PathWithin `
            -Path $temporaryRoot `
            -ParentPath ([System.IO.Path]::GetTempPath())
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

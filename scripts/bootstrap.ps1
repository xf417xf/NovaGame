[CmdletBinding()]
param(
    [ValidateSet("Repository", "Server", "Database", "Admin", "Client", "SwordMaster", "All")]
    [string]$Scope = "All",

    [string]$NovaCoreRoot,

    [string]$NovaCoreRef,

    [string]$NuGetPackagesRoot,

    [string]$VbenTemplateRoot,

    [string]$NodePath,

    [string]$PnpmBootstrapPath,

    [string]$DatabaseEnvFile,

    [string]$AdminAccessEnvFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot
. (Join-Path $PSScriptRoot "database-environment.ps1")
. (Join-Path $PSScriptRoot "admin-access-environment.ps1")

$localRoot = Join-Path $repositoryRoot ".local"
$novaCoreLocalRoot = Join-Path $repositoryRoot ".novacore"
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $localRoot "dotnet"
}

if (-not [string]::IsNullOrWhiteSpace($NuGetPackagesRoot)) {
    $env:NUGET_PACKAGES = $NuGetPackagesRoot
}
elseif (-not [string]::IsNullOrWhiteSpace($env:NOVAGAME_NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES = $env:NOVAGAME_NUGET_PACKAGES
}
if (-not $env:NUGET_PACKAGES) {
    $userProfile = if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $env:USERPROFILE
    }
    else {
        [Environment]::GetFolderPath("UserProfile")
    }
    $defaultGlobalPackages = Join-Path $userProfile ".nuget\packages"
    $env:NUGET_PACKAGES = if (Test-Path -LiteralPath $defaultGlobalPackages -PathType Container) {
        $defaultGlobalPackages
    }
    else {
        Join-Path $localRoot "nuget"
    }
}
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

foreach ($path in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Require-Command {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Required command '$Name' was not found on PATH."
    }

    return $command.Source
}

function Resolve-ConfiguredPath {
    param(
        [string]$ExplicitValue,
        [string]$EnvironmentName,
        [string]$Fallback,
        [string]$Description
    )

    $candidate = $ExplicitValue
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = [Environment]::GetEnvironmentVariable($EnvironmentName)
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = $Fallback
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw "$Description is required. Supply an explicit path or set $EnvironmentName."
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
        throw "$Description was not found at '$candidate'."
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

function Resolve-ExecutablePath {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Description
    )

    if (Test-Path -LiteralPath $Value -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Value).Path
    }

    $command = Get-Command $Value -ErrorAction SilentlyContinue
    if ($command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    throw "$Description was not found at '$Value' or on PATH."
}

function Get-NodeVersion {
    param([Parameter(Mandatory)][string]$Path)

    $output = @(& $Path "--version")
    if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0) {
        throw "Unable to read the Node.js version from '$Path'."
    }

    $versionText = ([string]$output[-1]).Trim().TrimStart("v")
    [Version]$version = $null
    if (-not [Version]::TryParse($versionText, [ref]$version)) {
        throw "Node.js at '$Path' returned an invalid version '$versionText'."
    }

    return $version
}

function Test-CompatibleNodeVersion {
    param([Parameter(Mandatory)][Version]$Version)

    return (($Version.Major -eq 22 -and $Version.Minor -ge 18) -or
        $Version.Major -eq 24)
}

function Resolve-CompatibleNode {
    param(
        [string]$ExplicitPath,
        [Version]$RequiredVersion
    )

    $versionRequirement = if ($null -eq $RequiredVersion) {
        "^22.18.0 or ^24.0.0"
    }
    else {
        $RequiredVersion.ToString()
    }

    $configuredPath = $ExplicitPath
    if ([string]::IsNullOrWhiteSpace($configuredPath)) {
        $configuredPath = $env:NOVAGAME_NODE_PATH
    }
    if (-not [string]::IsNullOrWhiteSpace($configuredPath)) {
        $resolvedPath = Resolve-ExecutablePath `
            -Value $configuredPath `
            -Description "Configured NovaGame Node.js executable"
        $version = Get-NodeVersion -Path $resolvedPath
        if (-not (Test-CompatibleNodeVersion -Version $version) -or
            ($null -ne $RequiredVersion -and $version -ne $RequiredVersion)) {
            throw "Configured Node.js '$resolvedPath' is version '$version'; this workspace requires $versionRequirement."
        }

        return [ordered]@{ Path = $resolvedPath; Version = $version.ToString() }
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    @(Get-Command node -All -ErrorAction SilentlyContinue) | ForEach-Object {
        if (-not [string]::IsNullOrWhiteSpace($_.Source)) {
            $null = $candidates.Add($_.Source)
        }
    }

    $userProfile = if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $env:USERPROFILE
    }
    else {
        [Environment]::GetFolderPath("UserProfile")
    }
    $null = $candidates.Add((Join-Path $userProfile ".cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"))

    $checked = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        try {
            $resolvedPath = (Resolve-Path -LiteralPath $candidate).Path
            $version = Get-NodeVersion -Path $resolvedPath
            $null = $checked.Add("$resolvedPath ($version)")
            if ((Test-CompatibleNodeVersion -Version $version) -and
                ($null -eq $RequiredVersion -or $version -eq $RequiredVersion)) {
                return [ordered]@{ Path = $resolvedPath; Version = $version.ToString() }
            }
        }
        catch {
            $null = $checked.Add("$candidate (unusable)")
        }
    }

    $checkedText = if ($checked.Count -eq 0) { "none" } else { $checked -join ", " }
    throw "No compatible NovaGame Node.js runtime was found for requirement $versionRequirement. Checked: $checkedText. Set NOVAGAME_NODE_PATH or pass -NodePath."
}

function Test-PnpmBootstrap {
    param(
        [Parameter(Mandatory)][string]$NodeExecutable,
        [Parameter(Mandatory)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $null = @(& $NodeExecutable $Path "--version")
    return $LASTEXITCODE -eq 0
}

function Resolve-PnpmBootstrap {
    param(
        [Parameter(Mandatory)][string]$NodeExecutable,
        [string]$ExplicitPath
    )

    $configuredPath = $ExplicitPath
    if ([string]::IsNullOrWhiteSpace($configuredPath)) {
        $configuredPath = $env:NOVAGAME_PNPM_BOOTSTRAP_PATH
    }
    if (-not [string]::IsNullOrWhiteSpace($configuredPath)) {
        $resolvedPath = Resolve-ExecutablePath `
            -Value $configuredPath `
            -Description "Configured pnpm bootstrap script"
        if (-not (Test-PnpmBootstrap -NodeExecutable $NodeExecutable -Path $resolvedPath)) {
            throw "Configured pnpm bootstrap script '$resolvedPath' cannot run under '$NodeExecutable'."
        }

        return $resolvedPath
    }

    $userProfile = if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $env:USERPROFILE
    }
    else {
        [Environment]::GetFolderPath("UserProfile")
    }
    $candidates = [System.Collections.Generic.List[string]]::new()
    $null = $candidates.Add((Join-Path $userProfile ".cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules\pnpm\bin\pnpm.cjs"))
    $null = $candidates.Add((Join-Path $userProfile "AppData\Roaming\npm\node_modules\pnpm\bin\pnpm.cjs"))

    @(Get-Command pnpm.cmd -All -ErrorAction SilentlyContinue) | ForEach-Object {
        if (-not [string]::IsNullOrWhiteSpace($_.Source)) {
            $commandRoot = Split-Path -Parent $_.Source
            $null = $candidates.Add((Join-Path $commandRoot "node_modules\pnpm\bin\pnpm.cjs"))
        }
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (Test-PnpmBootstrap -NodeExecutable $NodeExecutable -Path $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "No pnpm JavaScript bootstrap was found for the compatible Node.js runtime. Set NOVAGAME_PNPM_BOOTSTRAP_PATH or pass -PnpmBootstrapPath."
}

function Invoke-PinnedPnpm {
    param(
        [Parameter(Mandatory)][string]$NodeExecutable,
        [Parameter(Mandatory)][string]$BootstrapPath,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$StorePath,
        [string[]]$ArgumentList = @()
    )

    $previousPath = $env:PATH
    $previousStorePath = $env:pnpm_config_store_dir
    $env:PATH = "$(Split-Path -Parent $NodeExecutable)$([System.IO.Path]::PathSeparator)$previousPath"
    $env:pnpm_config_store_dir = $StorePath
    try {
        & $NodeExecutable $BootstrapPath "dlx" "pnpm@$Version" @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "pnpm@$Version failed with exit code ${LASTEXITCODE}: $($ArgumentList -join ' ')"
        }
    }
    finally {
        $env:PATH = $previousPath
        $env:pnpm_config_store_dir = $previousStorePath
    }
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

function Assert-PathWithin {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ParentPath
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$resolvedPath' must remain under '$resolvedParent'."
    }
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Publish-CleanDirectory {
    param(
        [Parameter(Mandatory)][string]$StagingPath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$ParentPath
    )

    if (-not (Test-Path -LiteralPath $StagingPath -PathType Container)) {
        throw "Staging directory '$StagingPath' does not exist."
    }
    if ((Test-Path -LiteralPath $DestinationPath) -and
        -not (Test-Path -LiteralPath $DestinationPath -PathType Container)) {
        throw "Destination '$DestinationPath' exists and is not a directory."
    }

    $backupPath = "$DestinationPath.backup-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    Assert-PathWithin -Path $StagingPath -ParentPath $ParentPath
    Assert-PathWithin -Path $DestinationPath -ParentPath $ParentPath
    Assert-PathWithin -Path $backupPath -ParentPath $ParentPath

    try {
        if (Test-Path -LiteralPath $DestinationPath -PathType Container) {
            Move-Item -LiteralPath $DestinationPath -Destination $backupPath
        }
        Move-Item -LiteralPath $StagingPath -Destination $DestinationPath
    }
    catch {
        if (-not (Test-Path -LiteralPath $DestinationPath) -and
            (Test-Path -LiteralPath $backupPath -PathType Container)) {
            Move-Item -LiteralPath $backupPath -Destination $DestinationPath
        }
        throw
    }

    if (Test-Path -LiteralPath $backupPath -PathType Container) {
        Remove-Item -LiteralPath $backupPath -Recurse -Force
    }
}

function Clear-NovaCorePackageCache {
    param(
        [Parameter(Mandatory)][string]$CacheRoot,
        [Parameter(Mandatory)][object[]]$PackageRecords
    )

    $resolvedCacheRoot = [System.IO.Path]::GetFullPath($CacheRoot)
    $resolvedRepositoryRoot = `
        [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
    if (-not $resolvedCacheRoot.StartsWith(
            $resolvedRepositoryRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning `
            "NovaCore package cache '$resolvedCacheRoot' is outside the repository; stale entries must be cleared by the caller."
        return
    }

    foreach ($packageRecord in $PackageRecords) {
        $packageCachePath = Join-Path `
            $resolvedCacheRoot `
            (([string]$packageRecord.name).ToLowerInvariant() +
                "\$([string]$packageRecord.version)")
        Assert-PathWithin -Path $packageCachePath -ParentPath $resolvedCacheRoot
        if (Test-Path -LiteralPath $packageCachePath -PathType Container) {
            Remove-Item -LiteralPath $packageCachePath -Recurse -Force
        }
    }
}

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $version = @($project.Project.PropertyGroup.Version) |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$version)) {
        throw "Project '$ProjectPath' does not declare a package version."
    }

    return [string]$version
}

function Get-NuGetPackageMetadata {
    param([Parameter(Mandatory)][string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith(".nuspec") })
        if ($nuspecEntries.Count -ne 1) {
            throw "Package '$PackagePath' must contain exactly one nuspec file."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            $nuspecContent = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    [xml]$nuspec = $nuspecContent
    return [ordered]@{
        Id = [string]$nuspec.package.metadata.id
        Version = [string]$nuspec.package.metadata.version
        RepositoryCommit = [string]$nuspec.package.metadata.repository.commit
    }
}

function Resolve-NovaCoreSnapshot {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$RequestedRef,
        [Parameter(Mandatory)][string]$ExpectedCommit,
        [Parameter(Mandatory)][string]$SnapshotParent
    )

    $commitOutput = @(& git -C $SourceRoot rev-parse --verify "$RequestedRef`^{commit}")
    if ($LASTEXITCODE -ne 0 -or $commitOutput.Count -eq 0) {
        throw "NovaCore ref '$RequestedRef' could not be resolved from '$SourceRoot'."
    }

    $resolvedCommit = ([string]$commitOutput[-1]).Trim()
    if ($resolvedCommit -ne $ExpectedCommit) {
        throw "NovaCore ref '$RequestedRef' resolved to '$resolvedCommit', but server/novacore.lock.json requires '$ExpectedCommit'."
    }

    $snapshotPath = Join-Path $SnapshotParent $resolvedCommit
    New-Item -ItemType Directory -Path $SnapshotParent -Force | Out-Null
    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $stagingPath = Join-Path $SnapshotParent "$resolvedCommit.staging-$suffix"
    $archivePath = Join-Path $SnapshotParent "$resolvedCommit-$suffix.zip"
    Assert-PathWithin -Path $stagingPath -ParentPath $SnapshotParent
    Assert-PathWithin -Path $archivePath -ParentPath $SnapshotParent

    try {
        Invoke-CheckedCommand "git" @(
            "-C", $SourceRoot,
            "archive",
            "--format=zip",
            "--output", $archivePath,
            $resolvedCommit
        )
        $archiveSha256 = Get-FileSha256 -Path $archivePath
        Expand-Archive -LiteralPath $archivePath -DestinationPath $stagingPath
        $exportedAtUtc = [DateTime]::UtcNow.ToString("O")
        [System.IO.File]::WriteAllText(
            (Join-Path $stagingPath ".novagame-source.json"),
            ([ordered]@{
                schemaVersion = 1
                commit = $resolvedCommit
                requestedRef = $RequestedRef
                sourceRoot = $SourceRoot
                archiveSha256 = $archiveSha256
                exportedAtUtc = $exportedAtUtc
            } | ConvertTo-Json))
        Publish-CleanDirectory `
            -StagingPath $stagingPath `
            -DestinationPath $snapshotPath `
            -ParentPath $SnapshotParent
    }
    finally {
        if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
            Remove-Item -LiteralPath $archivePath -Force
        }
        if (Test-Path -LiteralPath $stagingPath -PathType Container) {
            Remove-Item -LiteralPath $stagingPath -Recurse -Force
        }
    }

    return [ordered]@{
        Path = $snapshotPath
        Commit = $resolvedCommit
        ArchiveSha256 = $archiveSha256
        ExportedAtUtc = $exportedAtUtc
    }
}

$resolved = [ordered]@{
    Scope = $Scope
    RepositoryRoot = $repositoryRoot
    Git = Require-Command "git"
    PowerShell = Require-Command "pwsh"
    DotNet = Require-Command "dotnet"
    DotNetCliHome = $env:DOTNET_CLI_HOME
    NuGetPackagesRoot = $env:NUGET_PACKAGES
}

if ($Scope -in @("Server", "Database", "All")) {
    $resolvedNovaCoreRoot = Resolve-ConfiguredPath `
        -ExplicitValue $NovaCoreRoot `
        -EnvironmentName "NOVACORE_ROOT" `
        -Fallback "D:\Programs\nova-core" `
        -Description "NovaCore source root"

    $gitDirectoryOutput = @(& $resolved.Git -C $resolvedNovaCoreRoot rev-parse --git-dir)
    if ($LASTEXITCODE -ne 0 -or $gitDirectoryOutput.Count -eq 0) {
        throw "NovaCore source root '$resolvedNovaCoreRoot' is not a Git repository."
    }

    $lockPath = Join-Path $repositoryRoot "server\novacore.lock.json"
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "NovaCore lock file is missing at '$lockPath'."
    }

    $novaCoreLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $novaCoreVersion = ([string]$novaCoreLock.version).Trim()
    $lockedNovaCoreCommit = ([string]$novaCoreLock.commit).Trim().ToLowerInvariant()
    $lockedNovaCoreRef = ([string]$novaCoreLock.ref).Trim()
    $expectedPackageNames = @($novaCoreLock.requiredPackages | ForEach-Object { ([string]$_).Trim() })
    if ([string]::IsNullOrWhiteSpace($novaCoreVersion)) {
        throw "NovaCore lock version must not be empty."
    }
    if ($lockedNovaCoreCommit -notmatch '^[0-9a-f]{40}$') {
        throw "NovaCore lock commit must be a full 40-character Git commit hash."
    }
    if ([string]::IsNullOrWhiteSpace($lockedNovaCoreRef)) {
        throw "NovaCore lock ref must not be empty."
    }
    if ($expectedPackageNames.Count -ne 10) {
        throw "NovaCore lock must contain exactly 10 generated-application packages."
    }
    if (@($expectedPackageNames | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -ne 0 -or
        @($expectedPackageNames | Sort-Object -Unique).Count -ne $expectedPackageNames.Count) {
        throw "NovaCore lock package names must be non-empty and unique."
    }

    $lockSha256 = Get-FileSha256 -Path $lockPath
    $requestedNovaCoreRef = if (-not [string]::IsNullOrWhiteSpace($NovaCoreRef)) {
        $NovaCoreRef
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:NOVACORE_REF)) {
        $env:NOVACORE_REF
    }
    else {
        $lockedNovaCoreCommit
    }
    $snapshot = Resolve-NovaCoreSnapshot `
        -SourceRoot $resolvedNovaCoreRoot `
        -RequestedRef $requestedNovaCoreRef `
        -ExpectedCommit $lockedNovaCoreCommit `
        -SnapshotParent (Join-Path $novaCoreLocalRoot "sources")
    $novaCoreSourceRoot = [string]$snapshot.Path
    $novaCoreCommit = [string]$snapshot.Commit

    foreach ($requiredPath in @(
        "NovaCore.sln",
        "tools\NovaCore.Cli\NovaCore.Cli.csproj",
        "src\framework\framework-package-manifest.json"
    )) {
        $fullPath = Join-Path $novaCoreSourceRoot $requiredPath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Pinned NovaCore snapshot is missing required file '$fullPath'."
        }
    }

    $env:DOTNET_CLI_HOME = Join-Path $novaCoreLocalRoot "dotnet\$novaCoreCommit"
    $nugetPackagesBase = if (-not [string]::IsNullOrWhiteSpace($NuGetPackagesRoot)) {
        [System.IO.Path]::GetFullPath($NuGetPackagesRoot)
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:NOVAGAME_NUGET_PACKAGES)) {
        [System.IO.Path]::GetFullPath($env:NOVAGAME_NUGET_PACKAGES)
    }
    else {
        Join-Path $localRoot "nuget"
    }
    $env:NUGET_PACKAGES = Join-Path $nugetPackagesBase $novaCoreCommit
    New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES -Force | Out-Null
    $resolved.DotNetCliHome = $env:DOTNET_CLI_HOME
    $resolved.NuGetPackagesRoot = $env:NUGET_PACKAGES

    $manifestPath = Join-Path $novaCoreSourceRoot "src\framework\framework-package-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "NovaCore framework package manifest is missing at '$manifestPath'."
    }

    $manifestSha256 = Get-FileSha256 -Path $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $frameworkPackages = @($manifest.packages | Where-Object { $_.requiredByGeneratedApps -eq $true })
    $manifestPackageNames = @($frameworkPackages | ForEach-Object { ([string]$_.name).Trim() })
    if ($frameworkPackages.Count -ne 10 -or
        @($manifestPackageNames | Sort-Object -Unique).Count -ne $frameworkPackages.Count) {
        throw "NovaCore generated-app package manifest must contain exactly 10 unique packages."
    }
    if ((@($expectedPackageNames | Sort-Object) -join "`n") -ne
        (@($manifestPackageNames | Sort-Object) -join "`n")) {
        throw "NovaCore generated-app package manifest does not match server/novacore.lock.json."
    }

    $artifactsPath = Join-Path $novaCoreLocalRoot "artifacts\$novaCoreCommit"
    $feedParent = Join-Path $novaCoreLocalRoot "feeds"
    $stagingPackageSource = Join-Path $feedParent "$novaCoreCommit-staging-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    New-Item -ItemType Directory -Path $artifactsPath, $stagingPackageSource -Force | Out-Null
    Assert-PathWithin -Path $stagingPackageSource -ParentPath $feedParent

    $compatibilityStagingSource = ""
    try {
        $packageRecords = [System.Collections.Generic.List[object]]::new()
        foreach ($package in $frameworkPackages) {
            $packageName = [string]$package.name
            $projectPath = Join-Path $novaCoreSourceRoot ([string]$package.project)
            Assert-PathWithin -Path $projectPath -ParentPath $novaCoreSourceRoot
            if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
                throw "NovaCore package project '$projectPath' does not exist in the pinned snapshot."
            }
            $projectVersion = Get-ProjectVersion -ProjectPath $projectPath
            if ($projectVersion -ne $novaCoreVersion) {
                throw "NovaCore package '$packageName' has version '$projectVersion'; expected '$novaCoreVersion'."
            }

            Invoke-CheckedCommand "dotnet" @(
                "restore",
                $projectPath,
                "--ignore-failed-sources",
                "--artifacts-path", $artifactsPath,
                "--disable-parallel",
                "--verbosity", "minimal",
                "-p:NuGetAudit=false"
            )

            Invoke-CheckedCommand "dotnet" @(
                "pack",
                $projectPath,
                "--configuration", "Release",
                "--output", $stagingPackageSource,
                "--artifacts-path", $artifactsPath,
                "--no-restore",
                "--disable-build-servers",
                "--verbosity", "minimal",
                "-p:NuGetAudit=false",
                "-p:RepositoryType=git",
                "-p:RepositoryCommit=$novaCoreCommit",
                "-p:SourceRevisionId=$novaCoreCommit",
                "-p:ContinuousIntegrationBuild=true"
            )

            $packageFile = Join-Path $stagingPackageSource "$packageName.$projectVersion.nupkg"
            if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
                throw "Expected NovaCore package '$packageName' was not produced in '$stagingPackageSource'."
            }

            $packageMetadata = Get-NuGetPackageMetadata -PackagePath $packageFile
            if ($packageMetadata.Id -ne $packageName -or
                $packageMetadata.Version -ne $projectVersion -or
                $packageMetadata.RepositoryCommit -ne $novaCoreCommit) {
                throw "Package '$packageFile' metadata does not match its locked identity, version, and source commit."
            }

            $packageRecords.Add([ordered]@{
                name = $packageName
                version = $projectVersion
                project = [string]$package.project
                file = [System.IO.Path]::GetFileName($packageFile)
                sha256 = Get-FileSha256 -Path $packageFile
            })
        }

        $actualPackageFiles = @(Get-ChildItem -LiteralPath $stagingPackageSource -Filter "*.nupkg" -File)
        $expectedPackageFiles = @($packageRecords | ForEach-Object { [string]$_.file } | Sort-Object)
        $actualPackageFileNames = @($actualPackageFiles | ForEach-Object { $_.Name } | Sort-Object)
        if (($expectedPackageFiles -join "`n") -ne ($actualPackageFileNames -join "`n")) {
            throw "The NovaCore package feed does not contain exactly the locked package files."
        }

        $packageSource = Join-Path $feedParent $novaCoreCommit
        Publish-CleanDirectory `
            -StagingPath $stagingPackageSource `
            -DestinationPath $packageSource `
            -ParentPath $feedParent

        $compatibilityPackageSource = Join-Path $novaCoreLocalRoot "packages"
        $compatibilityStagingSource = Join-Path $novaCoreLocalRoot "packages.staging-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
        New-Item -ItemType Directory -Path $compatibilityStagingSource -Force | Out-Null
        foreach ($packageRecord in $packageRecords) {
            Copy-Item `
                -LiteralPath (Join-Path $packageSource ([string]$packageRecord.file)) `
                -Destination $compatibilityStagingSource
        }
        Publish-CleanDirectory `
            -StagingPath $compatibilityStagingSource `
            -DestinationPath $compatibilityPackageSource `
            -ParentPath $novaCoreLocalRoot
        Clear-NovaCorePackageCache `
            -CacheRoot $env:NUGET_PACKAGES `
            -PackageRecords @($packageRecords)
    }
    finally {
        foreach ($temporaryPath in @($stagingPackageSource, $compatibilityStagingSource)) {
            if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and
                (Test-Path -LiteralPath $temporaryPath -PathType Container)) {
                Remove-Item -LiteralPath $temporaryPath -Recurse -Force
            }
        }
    }

    $provenancePath = Join-Path $novaCoreLocalRoot "provenance.json"
    $provenanceStagingPath = "$provenancePath.staging-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    Assert-PathWithin -Path $provenanceStagingPath -ParentPath $novaCoreLocalRoot
    $provenance = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        lock = [ordered]@{
            path = "server/novacore.lock.json"
            sha256 = $lockSha256
            version = $novaCoreVersion
            commit = $novaCoreCommit
            ref = $lockedNovaCoreRef
        }
        source = [ordered]@{
            root = $resolvedNovaCoreRoot
            requestedRef = $requestedNovaCoreRef
            resolvedCommit = $novaCoreCommit
            snapshotRoot = $novaCoreSourceRoot
            archiveSha256 = [string]$snapshot.ArchiveSha256
            exportedAtUtc = [string]$snapshot.ExportedAtUtc
        }
        manifest = [ordered]@{
            path = "src/framework/framework-package-manifest.json"
            sha256 = $manifestSha256
        }
        feed = [ordered]@{
            root = $packageSource
            compatibilityRoot = $compatibilityPackageSource
            packageCount = $packageRecords.Count
        }
        packages = @($packageRecords)
    }
    try {
        [System.IO.File]::WriteAllText(
            $provenanceStagingPath,
            ($provenance | ConvertTo-Json -Depth 7))
        [System.IO.File]::Move($provenanceStagingPath, $provenancePath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $provenanceStagingPath -PathType Leaf) {
            Remove-Item -LiteralPath $provenanceStagingPath -Force
        }
    }

    $resolved.NovaCoreRoot = $resolvedNovaCoreRoot
    $resolved.NovaCoreSnapshotRoot = $novaCoreSourceRoot
    $resolved.NovaCoreCommit = $novaCoreCommit
    $resolved.NovaCoreVersion = $novaCoreVersion
    $resolved.NovaCorePackageSource = $packageSource
    $resolved.NovaCoreCompatibilityPackageSource = $compatibilityPackageSource
    $resolved.NovaCoreArtifacts = $artifactsPath
    $resolved.NovaCorePackages = $manifestPackageNames
    $resolved.NovaCoreProvenance = $provenancePath

    $resolvedAdminAccessEnvFile = Resolve-NovaGameAdminAccessEnvFile `
        -RepositoryRoot $repositoryRoot `
        -ExplicitPath $AdminAccessEnvFile
    $defaultAdminAccessEnvFile = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "server\.env.local"))
    if (-not (Test-Path -LiteralPath $resolvedAdminAccessEnvFile -PathType Leaf) -and
        $resolvedAdminAccessEnvFile -ne $defaultAdminAccessEnvFile) {
        throw "The overridden administrator access environment file does not exist at '$resolvedAdminAccessEnvFile'."
    }
    $adminAccessEnvironmentFileStatus = Initialize-NovaGameAdminAccessEnvironmentFile `
        -Path $resolvedAdminAccessEnvFile

    if ($resolvedAdminAccessEnvFile -eq $defaultAdminAccessEnvFile) {
        & git check-ignore --quiet -- "server/.env.local"
        if ($LASTEXITCODE -ne 0) {
            throw "Generated administrator access environment file 'server/.env.local' is not ignored by Git."
        }
    }

    $resolved.AdminAccessEnvironmentFile = $resolvedAdminAccessEnvFile
    $resolved.AdminAccessEnvironmentFileStatus = $adminAccessEnvironmentFileStatus
}

if ($Scope -in @("Database", "All")) {
    $resolved.Docker = Require-Command "docker"

    $resolvedDatabaseEnvFile = Resolve-NovaGameDatabaseEnvFile `
        -RepositoryRoot $repositoryRoot `
        -ExplicitPath $DatabaseEnvFile
    $defaultDatabaseEnvFile = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "deploy\.env"))
    if (-not (Test-Path -LiteralPath $resolvedDatabaseEnvFile -PathType Leaf) -and
        $resolvedDatabaseEnvFile -ne $defaultDatabaseEnvFile) {
        throw "The overridden database environment file does not exist at '$resolvedDatabaseEnvFile'."
    }
    $databaseEnvStatus = Initialize-NovaGameDatabaseEnvironmentFile `
        -Path $resolvedDatabaseEnvFile
    $databaseEnvironment = Read-NovaGameDatabaseEnvironment -Path $resolvedDatabaseEnvFile
    $composeArguments = Get-NovaGameComposeArguments `
        -RepositoryRoot $repositoryRoot `
        -EnvironmentFile $resolvedDatabaseEnvFile `
        -ProjectName "novagame-dev"

    if ($resolvedDatabaseEnvFile -eq $defaultDatabaseEnvFile) {
        & git check-ignore --quiet -- "deploy/.env"
        if ($LASTEXITCODE -ne 0) {
            throw "Generated database environment file 'deploy/.env' is not ignored by Git."
        }
    }

    $previousDatabaseEnvironment = Set-NovaGameDatabaseProcessEnvironment `
        -Values $databaseEnvironment
    try {
        Invoke-CheckedCommand "docker" @("compose", "version")
        Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")
        Invoke-CheckedCommand "docker" ($composeArguments + @("config", "--quiet"))
    }
    finally {
        Restore-NovaGameDatabaseProcessEnvironment -PreviousValues $previousDatabaseEnvironment
    }

    $resolved.DatabaseEnvironmentFile = $resolvedDatabaseEnvFile
    $resolved.DatabaseEnvironmentFileStatus = $databaseEnvStatus
    $resolved.DatabaseComposeFile = Join-Path $repositoryRoot "deploy\docker-compose.dev.yml"
}

if ($Scope -in @("Admin", "All")) {
    $adminRoot = Join-Path $repositoryRoot "admin"
    $adminPackageJsonPath = Join-Path $adminRoot "package.json"
    $adminLockPath = Join-Path $adminRoot "pnpm-lock.yaml"
    $adminWorkspacePath = Join-Path $adminRoot "pnpm-workspace.yaml"
    $adminApplicationPath = Join-Path $adminRoot "apps\web-ele\package.json"
    foreach ($requiredPath in @(
        $adminPackageJsonPath,
        $adminLockPath,
        $adminWorkspacePath,
        $adminApplicationPath,
        (Join-Path $adminRoot ".node-version"),
        (Join-Path $adminRoot "LICENSE")
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "The imported Vben web-ele admin is incomplete; missing '$requiredPath'."
        }
    }

    $resolvedVbenRoot = Resolve-ConfiguredPath `
        -ExplicitValue $VbenTemplateRoot `
        -EnvironmentName "VBEN_TEMPLATE_ROOT" `
        -Fallback (Join-Path $localRoot "vben-template-5.7.0") `
        -Description "Vben Admin 5 web-ele template root"

    $templatePackageJsonPath = Join-Path $resolvedVbenRoot "package.json"
    $templateLicensePath = Join-Path $resolvedVbenRoot "LICENSE"
    $templateApplicationPath = Join-Path $resolvedVbenRoot "apps\web-ele\package.json"
    $templateNodeVersionPath = Join-Path $resolvedVbenRoot ".node-version"
    foreach ($requiredPath in @(
        $templatePackageJsonPath,
        $templateLicensePath,
        $templateApplicationPath,
        $templateNodeVersionPath
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Vben template root is missing '$requiredPath'."
        }
    }

    $adminPackage = Get-Content -LiteralPath $adminPackageJsonPath -Raw | ConvertFrom-Json
    $templatePackage = Get-Content -LiteralPath $templatePackageJsonPath -Raw | ConvertFrom-Json
    if ([string]$adminPackage.name -ne "vben-admin-monorepo" -or
        [string]$templatePackage.name -ne [string]$adminPackage.name -or
        [string]$templatePackage.version -ne [string]$adminPackage.version) {
        throw "The imported admin package identity does not match the fixed Vben template."
    }
    if ([string]$adminPackage.packageManager -ne [string]$templatePackage.packageManager -or
        [string]$adminPackage.engines.node -ne [string]$templatePackage.engines.node) {
        throw "The imported admin must preserve the Vben template package-manager and Node.js constraints."
    }
    if ([string]$adminPackage.engines.node -ne "^22.18.0 || ^24.0.0") {
        throw "Unsupported Vben Node.js engine constraint '$($adminPackage.engines.node)'."
    }
    if ((Get-FileSha256 -Path (Join-Path $adminRoot "LICENSE")) -ne
        (Get-FileSha256 -Path $templateLicensePath)) {
        throw "The imported admin must preserve the fixed Vben template license."
    }
    if ((Get-Content -LiteralPath (Join-Path $adminRoot ".node-version") -Raw).Trim() -ne
        (Get-Content -LiteralPath $templateNodeVersionPath -Raw).Trim()) {
        throw "The imported admin must preserve the fixed Vben .node-version declaration."
    }

    $packageManagerMatch = [regex]::Match(
        [string]$adminPackage.packageManager,
        '^pnpm@(?<version>\d+\.\d+\.\d+)$')
    if (-not $packageManagerMatch.Success) {
        throw "Admin packageManager must pin an exact pnpm version."
    }
    $pnpmVersion = $packageManagerMatch.Groups["version"].Value
    foreach ($requiredScript in @(
        "build:ele", "check:type", "dev:ele", "format:check", "lint", "test:e2e", "test:unit"
    )) {
        if ($null -eq $adminPackage.scripts.$requiredScript) {
            throw "Admin package.json is missing required script '$requiredScript'."
        }
    }

    $adminNode = Resolve-CompatibleNode -ExplicitPath $NodePath
    $resolvedPnpmBootstrap = Resolve-PnpmBootstrap `
        -NodeExecutable $adminNode.Path `
        -ExplicitPath $PnpmBootstrapPath
    $pnpmStorePath = Join-Path $localRoot "pnpm-store"
    New-Item -ItemType Directory -Path $pnpmStorePath -Force | Out-Null

    Push-Location -LiteralPath $adminRoot
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-PinnedPnpm `
            -NodeExecutable $adminNode.Path `
            -BootstrapPath $resolvedPnpmBootstrap `
            -Version $pnpmVersion `
            -StorePath $pnpmStorePath `
            -ArgumentList @("install", "--frozen-lockfile")
    }
    finally {
        $env:CI = $previousCi
        Pop-Location
    }

    $toolchainManifestPath = Join-Path $localRoot "admin-toolchain.json"
    $toolchainManifestStagingPath = "$toolchainManifestPath.staging-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $toolchainManifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        adminRoot = $adminRoot
        templateRoot = $resolvedVbenRoot
        nodePath = [string]$adminNode.Path
        nodeVersion = [string]$adminNode.Version
        nodeEngine = [string]$adminPackage.engines.node
        packageManager = [string]$adminPackage.packageManager
        pnpmBootstrapPath = $resolvedPnpmBootstrap
        pnpmVersion = $pnpmVersion
        storePath = $pnpmStorePath
    }
    try {
        [System.IO.File]::WriteAllText(
            $toolchainManifestStagingPath,
            ($toolchainManifest | ConvertTo-Json -Depth 4))
        [System.IO.File]::Move($toolchainManifestStagingPath, $toolchainManifestPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $toolchainManifestStagingPath -PathType Leaf) {
            Remove-Item -LiteralPath $toolchainManifestStagingPath -Force
        }
    }

    $resolved.Node = [string]$adminNode.Path
    $resolved.NodeVersion = [string]$adminNode.Version
    $resolved.VbenTemplateRoot = $resolvedVbenRoot
    $resolved.AdminRoot = $adminRoot
    $resolved.AdminPackageManager = [string]$adminPackage.packageManager
    $resolved.AdminPnpmBootstrapPath = $resolvedPnpmBootstrap
    $resolved.AdminPnpmStorePath = $pnpmStorePath
    $resolved.AdminToolchainManifest = $toolchainManifestPath
}

if ($Scope -in @("Client", "SwordMaster", "All")) {
    $clientRoot = Join-Path $repositoryRoot "clients\cocos"
    $clientPackageJsonPath = Join-Path $clientRoot "package.json"
    $clientLockPath = Join-Path $clientRoot "pnpm-lock.yaml"
    $clientWorkspacePath = Join-Path $clientRoot "pnpm-workspace.yaml"
    $clientNodeVersionPath = Join-Path $clientRoot ".node-version"
    $gameConfigClientPackagePath = Join-Path $clientRoot "packages\game-config-client\package.json"
    $cocosCreatorAdapterPackagePath = Join-Path $clientRoot "adapters\cocos-creator\package.json"
    foreach ($requiredPath in @(
        $clientPackageJsonPath,
        $clientLockPath,
        $clientWorkspacePath,
        $clientNodeVersionPath,
        $gameConfigClientPackagePath,
        $cocosCreatorAdapterPackagePath
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "The Cocos client workspace is incomplete; missing '$requiredPath'."
        }
    }

    $clientPackage = Get-Content -LiteralPath $clientPackageJsonPath -Raw | ConvertFrom-Json
    if ([string]$clientPackage.packageManager -ne "pnpm@10.33.4") {
        throw "The Cocos client workspace must pin packageManager to pnpm@10.33.4."
    }
    if ([string]$clientPackage.engines.node -ne "^22.18.0 || ^24.0.0") {
        throw "The Cocos client workspace must require Node.js ^22.18.0 or ^24.0.0."
    }
    $declaredClientNodeVersion = (Get-Content -LiteralPath $clientNodeVersionPath -Raw).Trim()
    if ($declaredClientNodeVersion -ne "24.14.0") {
        throw "The Cocos client .node-version must remain pinned to 24.14.0."
    }
    foreach ($requiredScript in @("format:check", "lint", "typecheck", "test", "build")) {
        if ($null -eq $clientPackage.scripts.PSObject.Properties[$requiredScript]) {
            throw "The Cocos client package.json is missing required script '$requiredScript'."
        }
    }

    $clientPackageManagerMatch = [regex]::Match(
        [string]$clientPackage.packageManager,
        '^pnpm@(?<version>\d+\.\d+\.\d+)$')
    if (-not $clientPackageManagerMatch.Success) {
        throw "The Cocos client packageManager must pin an exact pnpm version."
    }
    $clientPnpmVersion = $clientPackageManagerMatch.Groups["version"].Value
    $clientNode = Resolve-CompatibleNode `
        -ExplicitPath $NodePath `
        -RequiredVersion ([Version]$declaredClientNodeVersion)
    $resolvedClientPnpmBootstrap = Resolve-PnpmBootstrap `
        -NodeExecutable $clientNode.Path `
        -ExplicitPath $PnpmBootstrapPath
    $clientPnpmStorePath = Join-Path $localRoot "pnpm-store"
    New-Item -ItemType Directory -Path $clientPnpmStorePath -Force | Out-Null

    Push-Location -LiteralPath $clientRoot
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-PinnedPnpm `
            -NodeExecutable $clientNode.Path `
            -BootstrapPath $resolvedClientPnpmBootstrap `
            -Version $clientPnpmVersion `
            -StorePath $clientPnpmStorePath `
            -ArgumentList @("install", "--frozen-lockfile")
    }
    finally {
        $env:CI = $previousCi
        Pop-Location
    }

    $clientToolchainManifestPath = Join-Path $localRoot "client-toolchain.json"
    $clientToolchainManifestStagingPath = "$clientToolchainManifestPath.staging-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $clientToolchainManifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        clientRoot = $clientRoot
        nodePath = [string]$clientNode.Path
        nodeVersion = [string]$clientNode.Version
        declaredNodeVersion = $declaredClientNodeVersion
        nodeEngine = [string]$clientPackage.engines.node
        packageManager = [string]$clientPackage.packageManager
        pnpmBootstrapPath = $resolvedClientPnpmBootstrap
        pnpmVersion = $clientPnpmVersion
        storePath = $clientPnpmStorePath
    }
    try {
        [System.IO.File]::WriteAllText(
            $clientToolchainManifestStagingPath,
            ($clientToolchainManifest | ConvertTo-Json -Depth 4))
        [System.IO.File]::Move(
            $clientToolchainManifestStagingPath,
            $clientToolchainManifestPath,
            $true)
    }
    finally {
        if (Test-Path -LiteralPath $clientToolchainManifestStagingPath -PathType Leaf) {
            Remove-Item -LiteralPath $clientToolchainManifestStagingPath -Force
        }
    }

    $resolved.Node = [string]$clientNode.Path
    $resolved.NodeVersion = [string]$clientNode.Version
    $resolved.ClientRoot = $clientRoot
    $resolved.ClientPackageManager = [string]$clientPackage.packageManager
    $resolved.ClientPnpmBootstrapPath = $resolvedClientPnpmBootstrap
    $resolved.ClientPnpmStorePath = $clientPnpmStorePath
    $resolved.ClientToolchainManifest = $clientToolchainManifestPath
}

if ($Scope -in @("SwordMaster", "All")) {
    $gameConfigClientPackagePath = Join-Path `
        $clientRoot `
        "packages\game-config-client\package.json"
    $cocosCreatorAdapterPackagePath = Join-Path `
        $clientRoot `
        "adapters\cocos-creator\package.json"
    $gameConfigClientPackage = Get-Content `
        -LiteralPath $gameConfigClientPackagePath `
        -Raw | ConvertFrom-Json
    $cocosCreatorAdapterPackage = Get-Content `
        -LiteralPath $cocosCreatorAdapterPackagePath `
        -Raw | ConvertFrom-Json
    if ([string]$gameConfigClientPackage.name -ne "@novagame/game-config-client") {
        throw "The public GameConfig client package name must remain '@novagame/game-config-client'."
    }
    if ([string]$cocosCreatorAdapterPackage.name -ne "@novagame/cocos-creator-game-config") {
        throw "The public Cocos adapter package name must remain '@novagame/cocos-creator-game-config'."
    }

    Push-Location -LiteralPath $clientRoot
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-PinnedPnpm `
            -NodeExecutable $clientNode.Path `
            -BootstrapPath $resolvedClientPnpmBootstrap `
            -Version $clientPnpmVersion `
            -StorePath $clientPnpmStorePath `
            -ArgumentList @("run", "build")
    }
    finally {
        $env:CI = $previousCi
        Pop-Location
    }

    foreach ($publicEntryPoint in @(
        (Join-Path $clientRoot "packages\game-config-client\dist\index.js"),
        (Join-Path $clientRoot "packages\game-config-client\dist\index.d.ts"),
        (Join-Path $clientRoot "adapters\cocos-creator\dist\index.js"),
        (Join-Path $clientRoot "adapters\cocos-creator\dist\index.d.ts")
    )) {
        if (-not (Test-Path -LiteralPath $publicEntryPoint -PathType Leaf)) {
            throw "The public NovaGame client package build is incomplete; missing '$publicEntryPoint'."
        }
    }

    $swordMasterRoot = Join-Path $repositoryRoot "samples\SwordMaster"
    $swordMasterPackagePath = Join-Path $swordMasterRoot "package.json"
    $swordMasterLockPath = Join-Path $swordMasterRoot "pnpm-lock.yaml"
    $swordMasterNodeVersionPath = Join-Path $swordMasterRoot ".node-version"
    $swordMasterSnapshotPath = Join-Path `
        $swordMasterRoot `
        "config\game-config.snapshot.json"
    $swordMasterSnapshotManifestPath = Join-Path `
        $swordMasterRoot `
        "config\game-config.snapshot.manifest.json"
    foreach ($requiredPath in @(
        $swordMasterPackagePath,
        $swordMasterLockPath,
        $swordMasterNodeVersionPath,
        $swordMasterSnapshotPath,
        $swordMasterSnapshotManifestPath
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "The SwordMaster smoke consumer is incomplete; missing '$requiredPath'."
        }
    }
    if (Test-Path -LiteralPath (Join-Path $swordMasterRoot "pnpm-workspace.yaml")) {
        throw "SwordMaster must remain a standalone consumer, not a nested pnpm workspace."
    }

    $swordMasterPackage = Get-Content `
        -LiteralPath $swordMasterPackagePath `
        -Raw | ConvertFrom-Json
    if ([string]$swordMasterPackage.packageManager -ne "pnpm@10.33.4") {
        throw "SwordMaster must pin packageManager to pnpm@10.33.4."
    }
    if ([string]$swordMasterPackage.engines.node -ne "^22.18.0 || ^24.0.0") {
        throw "SwordMaster must require Node.js ^22.18.0 or ^24.0.0."
    }
    $declaredSwordMasterNodeVersion = (
        Get-Content -LiteralPath $swordMasterNodeVersionPath -Raw).Trim()
    if ($declaredSwordMasterNodeVersion -ne "24.14.0") {
        throw "The SwordMaster .node-version must remain pinned to 24.14.0."
    }
    foreach ($requiredScript in @(
        "contract:generate", "format:check", "lint", "typecheck", "test",
        "test:real-api", "build"
    )) {
        if ($null -eq $swordMasterPackage.scripts.PSObject.Properties[$requiredScript]) {
            throw "The SwordMaster package.json is missing required script '$requiredScript'."
        }
    }

    $swordMasterDependenciesProperty = `
        $swordMasterPackage.PSObject.Properties["dependencies"]
    $swordMasterDependencies = if ($null -eq $swordMasterDependenciesProperty) {
        @()
    }
    else {
        @($swordMasterDependenciesProperty.Value.PSObject.Properties)
    }
    if ($swordMasterDependencies.Count -ne 2) {
        throw "SwordMaster runtime dependencies must contain exactly the two public NovaGame client packages."
    }
    $expectedSwordMasterLinks = [ordered]@{
        "@novagame/game-config-client" = `
            "link:../../clients/cocos/packages/game-config-client"
        "@novagame/cocos-creator-game-config" = `
            "link:../../clients/cocos/adapters/cocos-creator"
    }
    foreach ($dependency in $expectedSwordMasterLinks.GetEnumerator()) {
        $property = $swordMasterDependenciesProperty.Value.PSObject.Properties[
            [string]$dependency.Key]
        if ($null -eq $property -or
            [string]$property.Value -ne [string]$dependency.Value) {
            throw "SwordMaster must consume '$($dependency.Key)' through '$($dependency.Value)'."
        }
    }

    Push-Location -LiteralPath $swordMasterRoot
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-PinnedPnpm `
            -NodeExecutable $clientNode.Path `
            -BootstrapPath $resolvedClientPnpmBootstrap `
            -Version $clientPnpmVersion `
            -StorePath $clientPnpmStorePath `
            -ArgumentList @("install", "--frozen-lockfile")
    }
    finally {
        $env:CI = $previousCi
        Pop-Location
    }

    $swordMasterToolchainManifestPath = Join-Path `
        $localRoot `
        "swordmaster-toolchain.json"
    $swordMasterToolchainManifestStagingPath = `
        "$swordMasterToolchainManifestPath.staging-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $swordMasterToolchainManifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        swordMasterRoot = $swordMasterRoot
        clientRoot = $clientRoot
        nodePath = [string]$clientNode.Path
        nodeVersion = [string]$clientNode.Version
        declaredNodeVersion = $declaredSwordMasterNodeVersion
        nodeEngine = [string]$swordMasterPackage.engines.node
        packageManager = [string]$swordMasterPackage.packageManager
        pnpmBootstrapPath = $resolvedClientPnpmBootstrap
        pnpmVersion = $clientPnpmVersion
        storePath = $clientPnpmStorePath
        gameConfigClientRoot = Join-Path `
            $clientRoot `
            "packages\game-config-client"
        cocosCreatorAdapterRoot = Join-Path `
            $clientRoot `
            "adapters\cocos-creator"
    }
    try {
        [System.IO.File]::WriteAllText(
            $swordMasterToolchainManifestStagingPath,
            ($swordMasterToolchainManifest | ConvertTo-Json -Depth 4))
        [System.IO.File]::Move(
            $swordMasterToolchainManifestStagingPath,
            $swordMasterToolchainManifestPath,
            $true)
    }
    finally {
        if (Test-Path `
                -LiteralPath $swordMasterToolchainManifestStagingPath `
                -PathType Leaf) {
            Remove-Item `
                -LiteralPath $swordMasterToolchainManifestStagingPath `
                -Force
        }
    }

    $resolved.SwordMasterRoot = $swordMasterRoot
    $resolved.SwordMasterPackageManager = [string]$swordMasterPackage.packageManager
    $resolved.SwordMasterToolchainManifest = $swordMasterToolchainManifestPath
}

$resolved | ConvertTo-Json -Depth 4

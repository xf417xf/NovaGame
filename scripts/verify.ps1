[CmdletBinding()]
param(
    [ValidateSet("Documentation", "Repository", "Server", "Database", "Authentication", "GameConfig", "AdminE2E", "Admin", "Client", "SwordMaster", "All")]
    [string]$Scope = "All",

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

$localRoot = Join-Path $repositoryRoot ".local"
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $localRoot "dotnet"
}
if (-not $env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES = if (-not [string]::IsNullOrWhiteSpace($env:NOVAGAME_NUGET_PACKAGES)) {
        $env:NOVAGAME_NUGET_PACKAGES
    }
    else {
        $novaCoreCacheKey = "unlocked"
        $novaCoreLockPath = Join-Path $repositoryRoot "server\novacore.lock.json"
        if (Test-Path -LiteralPath $novaCoreLockPath -PathType Leaf) {
            $novaCoreLockMetadata = Get-Content `
                -LiteralPath $novaCoreLockPath `
                -Raw | ConvertFrom-Json
            $lockedCommit = [string]$novaCoreLockMetadata.commit
            if ($lockedCommit -match '^[0-9a-f]{40}$') {
                $novaCoreCacheKey = $lockedCommit
            }
        }

        Join-Path $localRoot "nuget\$novaCoreCacheKey"
    }
}
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

foreach ($path in @($env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
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

function Invoke-CheckedCommandCapture {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    $output = @(& $FilePath @ArgumentList)
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }

    return $output
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

    $manifestPath = Join-Path $localRoot "admin-toolchain.json"
    Assert-Condition `
        (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
        "Admin toolchain manifest is missing after bootstrap."
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Condition ([int]$manifest.schemaVersion -eq 1) "Unsupported admin toolchain manifest schema."

    $expectedAdminRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "admin"))
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.adminRoot) -eq $expectedAdminRoot) `
        "Admin toolchain manifest targets a different admin root."
    foreach ($requiredFile in @(
        [string]$manifest.nodePath,
        [string]$manifest.pnpmBootstrapPath
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
            "Admin toolchain file is missing at '$requiredFile'."
    }
    Assert-Condition `
        (Test-Path -LiteralPath ([string]$manifest.storePath) -PathType Container) `
        "Admin pnpm store is missing at '$($manifest.storePath)'."

    $package = Get-Content -LiteralPath "admin/package.json" -Raw | ConvertFrom-Json
    Assert-Condition `
        ([string]$package.packageManager -eq [string]$manifest.packageManager) `
        "Admin toolchain package-manager identity is stale."
    Assert-Condition `
        ([string]$manifest.packageManager -eq "pnpm@$($manifest.pnpmVersion)") `
        "Admin toolchain must execute the package.json-pinned pnpm version."

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

function Initialize-ClientToolchain {
    $arguments = @(
        "-NoProfile",
        "-File", (Join-Path $PSScriptRoot "bootstrap.ps1"),
        "-Scope", "Client"
    )
    if (-not [string]::IsNullOrWhiteSpace($NodePath)) {
        $arguments += @("-NodePath", $NodePath)
    }
    if (-not [string]::IsNullOrWhiteSpace($PnpmBootstrapPath)) {
        $arguments += @("-PnpmBootstrapPath", $PnpmBootstrapPath)
    }

    & pwsh @arguments | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "Client bootstrap failed with exit code $LASTEXITCODE."
    }

    $manifestPath = Join-Path $localRoot "client-toolchain.json"
    Assert-Condition `
        (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
        "Client toolchain manifest is missing after bootstrap."
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Condition ([int]$manifest.schemaVersion -eq 1) "Unsupported client toolchain manifest schema."

    $expectedClientRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "clients\cocos"))
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.clientRoot) -eq $expectedClientRoot) `
        "Client toolchain manifest targets a different client root."
    foreach ($requiredFile in @(
        [string]$manifest.nodePath,
        [string]$manifest.pnpmBootstrapPath
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
            "Client toolchain file is missing at '$requiredFile'."
    }

    $expectedStorePath = [System.IO.Path]::GetFullPath(
        (Join-Path $localRoot "pnpm-store"))
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.storePath) -eq $expectedStorePath) `
        "Client pnpm store must remain repository-local."
    Assert-Condition `
        (Test-Path -LiteralPath $expectedStorePath -PathType Container) `
        "Client pnpm store is missing at '$expectedStorePath'."

    $package = Get-Content -LiteralPath "clients/cocos/package.json" -Raw | ConvertFrom-Json
    $declaredNodeVersion = (Get-Content -LiteralPath "clients/cocos/.node-version" -Raw).Trim()
    Assert-Condition `
        ([string]$package.packageManager -eq [string]$manifest.packageManager) `
        "Client toolchain package-manager identity is stale."
    Assert-Condition `
        ([string]$package.engines.node -eq [string]$manifest.nodeEngine) `
        "Client toolchain Node.js engine identity is stale."
    Assert-Condition `
        ($declaredNodeVersion -eq "24.14.0") `
        "The Cocos client .node-version must remain pinned to 24.14.0."
    Assert-Condition `
        ([string]$manifest.declaredNodeVersion -eq $declaredNodeVersion) `
        "Client toolchain declared Node.js version is stale."
    Assert-Condition `
        ([string]$manifest.nodeVersion -eq $declaredNodeVersion) `
        "Client toolchain must execute the .node-version runtime."
    Assert-Condition `
        ([string]$manifest.packageManager -eq "pnpm@$($manifest.pnpmVersion)") `
        "Client toolchain must execute the package.json-pinned pnpm version."

    return $manifest
}

function Invoke-ClientPnpm {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [string[]]$ArgumentList = @()
    )

    $previousPath = $env:PATH
    $previousStorePath = $env:pnpm_config_store_dir
    $env:PATH = "$(Split-Path -Parent ([string]$Toolchain.nodePath))$([System.IO.Path]::PathSeparator)$previousPath"
    $env:pnpm_config_store_dir = [string]$Toolchain.storePath
    Push-Location -LiteralPath ([string]$Toolchain.clientRoot)
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

function Initialize-SwordMasterToolchain {
    $arguments = @(
        "-NoProfile",
        "-File", (Join-Path $PSScriptRoot "bootstrap.ps1"),
        "-Scope", "SwordMaster"
    )
    if (-not [string]::IsNullOrWhiteSpace($NodePath)) {
        $arguments += @("-NodePath", $NodePath)
    }
    if (-not [string]::IsNullOrWhiteSpace($PnpmBootstrapPath)) {
        $arguments += @("-PnpmBootstrapPath", $PnpmBootstrapPath)
    }

    & pwsh @arguments | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "SwordMaster bootstrap failed with exit code $LASTEXITCODE."
    }

    $manifestPath = Join-Path $localRoot "swordmaster-toolchain.json"
    Assert-Condition `
        (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
        "SwordMaster toolchain manifest is missing after bootstrap."
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Condition `
        ([int]$manifest.schemaVersion -eq 1) `
        "Unsupported SwordMaster toolchain manifest schema."

    $expectedSwordMasterRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "samples\SwordMaster"))
    $expectedClientRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "clients\cocos"))
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.swordMasterRoot) -eq `
            $expectedSwordMasterRoot) `
        "SwordMaster toolchain manifest targets a different sample root."
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.clientRoot) -eq `
            $expectedClientRoot) `
        "SwordMaster toolchain manifest targets a different client root."
    foreach ($requiredFile in @(
        [string]$manifest.nodePath,
        [string]$manifest.pnpmBootstrapPath
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
            "SwordMaster toolchain file is missing at '$requiredFile'."
    }

    $expectedStorePath = [System.IO.Path]::GetFullPath(
        (Join-Path $localRoot "pnpm-store"))
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.storePath) -eq `
            $expectedStorePath) `
        "SwordMaster pnpm store must remain repository-local."
    Assert-Condition `
        (Test-Path -LiteralPath $expectedStorePath -PathType Container) `
        "SwordMaster pnpm store is missing at '$expectedStorePath'."

    $package = Get-Content `
        -LiteralPath "samples/SwordMaster/package.json" `
        -Raw | ConvertFrom-Json
    $declaredNodeVersion = (
        Get-Content -LiteralPath "samples/SwordMaster/.node-version" -Raw).Trim()
    Assert-Condition `
        ([string]$package.packageManager -eq [string]$manifest.packageManager) `
        "SwordMaster toolchain package-manager identity is stale."
    Assert-Condition `
        ([string]$package.engines.node -eq [string]$manifest.nodeEngine) `
        "SwordMaster toolchain Node.js engine identity is stale."
    Assert-Condition `
        ($declaredNodeVersion -eq "24.14.0") `
        "The SwordMaster .node-version must remain pinned to 24.14.0."
    Assert-Condition `
        ([string]$manifest.declaredNodeVersion -eq $declaredNodeVersion) `
        "SwordMaster toolchain declared Node.js version is stale."
    Assert-Condition `
        ([string]$manifest.nodeVersion -eq $declaredNodeVersion) `
        "SwordMaster toolchain must execute the .node-version runtime."
    Assert-Condition `
        ([string]$manifest.packageManager -eq "pnpm@$($manifest.pnpmVersion)") `
        "SwordMaster toolchain must execute the package.json-pinned pnpm version."

    $expectedGameConfigClientRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $expectedClientRoot "packages\game-config-client"))
    $expectedCocosCreatorAdapterRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $expectedClientRoot "adapters\cocos-creator"))
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.gameConfigClientRoot) -eq `
            $expectedGameConfigClientRoot) `
        "SwordMaster toolchain manifest targets a different GameConfig client package."
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$manifest.cocosCreatorAdapterRoot) -eq `
            $expectedCocosCreatorAdapterRoot) `
        "SwordMaster toolchain manifest targets a different Cocos Creator adapter package."

    return $manifest
}

function Invoke-SwordMasterPnpm {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [string[]]$ArgumentList = @()
    )

    $previousPath = $env:PATH
    $previousStorePath = $env:pnpm_config_store_dir
    $env:PATH = "$(Split-Path -Parent ([string]$Toolchain.nodePath))$([System.IO.Path]::PathSeparator)$previousPath"
    $env:pnpm_config_store_dir = [string]$Toolchain.storePath
    Push-Location -LiteralPath ([string]$Toolchain.swordMasterRoot)
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

function Invoke-SwordMasterPnpmIsolated {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][System.Collections.IDictionary]$Environment
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [string]$Toolchain.nodePath
    $startInfo.WorkingDirectory = [string]$Toolchain.swordMasterRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment.Clear()

    foreach ($name in @(
        "APPDATA",
        "ComSpec",
        "HOMEDRIVE",
        "HOMEPATH",
        "LOCALAPPDATA",
        "NUMBER_OF_PROCESSORS",
        "OS",
        "PATHEXT",
        "PROCESSOR_ARCHITECTURE",
        "ProgramData",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "SystemDrive",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR"
    )) {
        $value = [Environment]::GetEnvironmentVariable($name, "Process")
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $startInfo.Environment[$name] = $value
        }
    }

    $nodeDirectory = Split-Path -Parent ([string]$Toolchain.nodePath)
    $processPath = [Environment]::GetEnvironmentVariable("PATH", "Process")
    $startInfo.Environment["PATH"] = if ([string]::IsNullOrWhiteSpace($processPath)) {
        $nodeDirectory
    }
    else {
        "$nodeDirectory$([System.IO.Path]::PathSeparator)$processPath"
    }
    $startInfo.Environment["CI"] = "true"
    $startInfo.Environment["NO_COLOR"] = "1"
    $startInfo.Environment["pnpm_config_store_dir"] = [string]$Toolchain.storePath

    foreach ($entry in $Environment.GetEnumerator()) {
        $name = [string]$entry.Key
        Assert-Condition `
            ($name -match '^SWORDMASTER_ACCEPTANCE_[A-Z0-9_]+$') `
            "Isolated SwordMaster acceptance variables must use the SWORDMASTER_ACCEPTANCE_ prefix."
        $startInfo.Environment[$name] = [string]$entry.Value
    }

    $null = $startInfo.ArgumentList.Add([string]$Toolchain.pnpmBootstrapPath)
    foreach ($argument in $ArgumentList) {
        $null = $startInfo.ArgumentList.Add([string]$argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        Assert-Condition $process.Start() "Unable to start the isolated SwordMaster acceptance process."
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "The isolated SwordMaster acceptance process exceeded 60 seconds."
        }
        $null = $standardOutput.GetAwaiter().GetResult()
        $null = $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "The isolated SwordMaster acceptance process failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-AvailableLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
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
            $response = Invoke-WebRequest `
                -Uri $Uri `
                -Method Get `
                -Headers $Headers `
                -TimeoutSec 2
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

    $processId = $Process.Id
    try {
        $Process.Refresh()
        if (-not $Process.HasExited) {
            $taskkill = Get-Command "taskkill.exe" -ErrorAction SilentlyContinue
            if ($taskkill) {
                & $taskkill.Source "/PID" ([string]$processId) "/T" "/F" | Out-Null
                $taskkillExitCode = $LASTEXITCODE
                $Process.Refresh()
                if ($taskkillExitCode -ne 0 -and -not $Process.HasExited) {
                    throw "taskkill failed for process '$processId' with exit code '$taskkillExitCode'."
                }
            }
            else {
                Stop-Process -Id $processId -Force -ErrorAction Stop
            }
        }

        if (-not $Process.WaitForExit(10000)) {
            throw "Process '$processId' did not exit within 10 seconds."
        }
    }
    finally {
        $Process.Dispose()
    }
}

function Resolve-E2EBrowserChannel {
    $configuredChannel = [Environment]::GetEnvironmentVariable(
        "NOVAGAME_E2E_BROWSER_CHANNEL",
        "Process")
    if (-not [string]::IsNullOrWhiteSpace($configuredChannel)) {
        return $configuredChannel.Trim()
    }

    $edgeCandidates = [System.Collections.Generic.List[string]]::new()
    $edgeCommand = Get-Command "msedge.exe" -ErrorAction SilentlyContinue
    if ($edgeCommand -and -not [string]::IsNullOrWhiteSpace($edgeCommand.Source)) {
        $null = $edgeCandidates.Add($edgeCommand.Source)
    }
    foreach ($programRoot in @(
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles,
        $env:LOCALAPPDATA
    )) {
        if (-not [string]::IsNullOrWhiteSpace($programRoot)) {
            $null = $edgeCandidates.Add(
                (Join-Path $programRoot "Microsoft\Edge\Application\msedge.exe"))
        }
    }

    if (-not (@($edgeCandidates | Where-Object {
                    Test-Path -LiteralPath $_ -PathType Leaf
                }).Count -ge 1)) {
        throw "Microsoft Edge was not found. Install Edge or set NOVAGAME_E2E_BROWSER_CHANNEL to an available Playwright channel."
    }

    return "msedge"
}

function Test-Documentation {
    $requiredFiles = @(
        "README.md",
        "AGENTS.md",
        "server/AGENTS.md",
        "admin/AGENTS.md",
        "clients/cocos/AGENTS.md",
        "docs/README.md",
        "docs/architecture.md",
        "docs/workflows/development.md",
        "docs/workflows/extraction.md",
        "docs/workflows/configuration-release.md",
        "docs/roadmap.md",
        "docs/goals/m0.5.md",
        "docs/goals/m1.md",
        "docs/goals/m2.md",
        "samples/SwordMaster/docs/equipment-attribute-effect.md",
        "samples/SwordMaster/docs/player-assets-m1-06-evidence.md",
        "samples/SwordMaster/docs/player-assets-m1-07-reuse-evidence.md"
    )

    foreach ($requiredFile in $requiredFiles) {
        Assert-Condition (Test-Path -LiteralPath $requiredFile -PathType Leaf) "Missing required document '$requiredFile'."
    }

    $linkErrors = [System.Collections.Generic.List[string]]::new()
    $nonAsciiFiles = [System.Collections.Generic.List[string]]::new()

    $markdownPaths = @(
        git ls-files --cached --others --exclude-standard -- "*.md" |
            Where-Object {
                $_ -notlike "admin/*" -or $_ -eq "admin/AGENTS.md"
            }
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate repository Markdown files through Git."
    }

    $markdownPaths | ForEach-Object {
        $file = Get-Item -LiteralPath (Join-Path $repositoryRoot $_)
        $content = Get-Content -LiteralPath $file.FullName -Raw

        if ($content -match "[^\x00-\x7F]") {
            $nonAsciiFiles.Add($file.FullName)
        }

        foreach ($match in [regex]::Matches($content, "(?<!\!)\[[^\]]+\]\(([^)]+)\)")) {
            $target = $match.Groups[1].Value.Trim()
            if ($target -match "^(https?:|mailto:|#)" -or [string]::IsNullOrWhiteSpace($target)) {
                continue
            }

            $pathOnly = ($target -split "#")[0]
            if ([string]::IsNullOrWhiteSpace($pathOnly)) {
                continue
            }

            $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $pathOnly))
            if (-not (Test-Path -LiteralPath $resolvedTarget)) {
                $linkErrors.Add("$($file.FullName): $target")
            }
        }
    }

    Assert-Condition ($linkErrors.Count -eq 0) "Broken Markdown links:`n$($linkErrors -join "`n")"
    Assert-Condition ($nonAsciiFiles.Count -eq 0) "Documentation must remain ASCII. Non-ASCII files:`n$($nonAsciiFiles -join "`n")"
    Assert-Condition (-not (Test-Path -LiteralPath "samples/SwordMaster/AGENTS.md")) "SwordMaster must not have a scoped AGENTS.md."

    $roadmap = Get-Content -LiteralPath "docs/roadmap.md" -Raw
    $goalDefinitions = @(
        [pscustomobject]@{
            Milestone = "M0.5"
            Path = "docs/goals/m0.5.md"
            ExpectedId = "novagame-m0.5"
            StageIdPrefix = "M05"
            FirstStageNumber = 0
            FinalStageId = "M05-09"
            StagePattern = '^## M05-\d{2} - '
            ExpectedStageCount = 10
            ExitSection = "## M0.5 Exit Criteria"
            RequiredDependency = $null
        },
        [pscustomobject]@{
            Milestone = "M1"
            Path = "docs/goals/m1.md"
            ExpectedId = "novagame-m1"
            StageIdPrefix = "M1"
            FirstStageNumber = 1
            FinalStageId = "M1-08"
            StagePattern = '^## M1-\d{2} - '
            ExpectedStageCount = 8
            ExitSection = "## M1 Exit Criteria"
            RequiredDependency = "novagame-m0.5-complete"
        },
        [pscustomobject]@{
            Milestone = "M2"
            Path = "docs/goals/m2.md"
            ExpectedId = "novagame-m2"
            StageIdPrefix = "M2"
            FirstStageNumber = 1
            FinalStageId = "M2-08"
            StagePattern = '^## M2-\d{2} - '
            ExpectedStageCount = 8
            ExitSection = "## M2 Exit Criteria"
            RequiredDependency = "novagame-m1-complete"
        }
    )
    $allowedGoalStatuses = @(
        "drafting", "ready", "in_progress", "blocked", "complete")
    $activeGoalStatuses = @("in_progress", "blocked")
    $goalRecords = [ordered]@{}
    $activeGoalMilestones = [System.Collections.Generic.List[string]]::new()

    foreach ($definition in $goalDefinitions) {
        $goalContent = Get-Content -LiteralPath $definition.Path -Raw
        $frontMatterMatch = [regex]::Match(
            $goalContent,
            '\A---\r?\n(?<metadata>.*?)\r?\n---(?:\r?\n|\z)',
            [System.Text.RegularExpressions.RegexOptions]::Singleline)
        Assert-Condition `
            $frontMatterMatch.Success `
            "$($definition.Milestone) goal is missing YAML front matter."
        $metadata = $frontMatterMatch.Groups["metadata"].Value

        foreach ($requiredMetadataField in @(
            "id",
            "title",
            "status",
            "active_stage",
            "execution_authorized",
            "dependencies",
            "last_verification_status",
            "last_verification_at",
            "last_verification_command",
            "last_verification_result",
            "updated_at"
        )) {
            $metadataFieldPattern = '(?m)^' +
                [regex]::Escape($requiredMetadataField) + ':.*$'
            Assert-Condition `
                ([regex]::IsMatch($metadata, $metadataFieldPattern)) `
                "$($definition.Milestone) goal metadata is missing '$requiredMetadataField'."
        }

        $idMatch = [regex]::Match(
            $metadata,
            '(?m)^id:\s*(?<value>[^\r\n]+?)\s*$')
        Assert-Condition `
            ($idMatch.Success -and
                $idMatch.Groups["value"].Value -eq $definition.ExpectedId) `
            "$($definition.Milestone) goal id must be '$($definition.ExpectedId)'."
        if ($null -ne $definition.RequiredDependency) {
            $dependencyPattern = '(?m)^\s*-\s+' +
                [regex]::Escape($definition.RequiredDependency) + '\s*$'
            Assert-Condition `
                ([regex]::IsMatch($metadata, $dependencyPattern)) `
                "$($definition.Milestone) goal must depend on '$($definition.RequiredDependency)'."
        }

        $statusMatch = [regex]::Match(
            $metadata,
            '(?m)^status:\s*(?<value>[a-z_]+)\s*$')
        $activeStageMatch = [regex]::Match(
            $metadata,
            '(?m)^active_stage:\s*(?<value>[A-Za-z0-9.-]+)\s*$')
        $authorizationMatch = [regex]::Match(
            $metadata,
            '(?m)^execution_authorized:\s*(?<value>true|false)\s*$')
        $lastVerificationStatusMatch = [regex]::Match(
            $metadata,
            '(?m)^last_verification_status:\s*(?<value>[a-z_]+)\s*$')
        $lastVerificationAtMatch = [regex]::Match(
            $metadata,
            '(?m)^last_verification_at:\s*(?<value>[^\r\n]+?)\s*$')
        $lastVerificationCommandMatch = [regex]::Match(
            $metadata,
            '(?m)^last_verification_command:\s*(?<value>[^\r\n]+?)\s*$')
        $lastVerificationResultMatch = [regex]::Match(
            $metadata,
            '(?m)^last_verification_result:\s*(?<value>[^\r\n]+?)\s*$')
        Assert-Condition `
            $statusMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid status."
        Assert-Condition `
            $activeStageMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid active_stage."
        Assert-Condition `
            $authorizationMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid execution_authorized value."
        Assert-Condition `
            $lastVerificationStatusMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid last_verification_status."
        Assert-Condition `
            $lastVerificationAtMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid last_verification_at."
        Assert-Condition `
            $lastVerificationCommandMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid last_verification_command."
        Assert-Condition `
            $lastVerificationResultMatch.Success `
            "$($definition.Milestone) goal metadata has an invalid last_verification_result."

        $goalStatus = $statusMatch.Groups["value"].Value
        $activeStage = $activeStageMatch.Groups["value"].Value
        $executionAuthorized = `
            $authorizationMatch.Groups["value"].Value -eq "true"
        $lastVerificationAt = $lastVerificationAtMatch.Groups["value"].Value
        $lastVerificationCommand =
            $lastVerificationCommandMatch.Groups["value"].Value
        $lastVerificationResult =
            $lastVerificationResultMatch.Groups["value"].Value
        Assert-Condition `
            ($goalStatus -in $allowedGoalStatuses) `
            "$($definition.Milestone) goal has unsupported status '$goalStatus'."
        if ($lastVerificationStatusMatch.Groups["value"].Value -eq "passed") {
            Assert-Condition `
                ([regex]::IsMatch(
                    $lastVerificationResult,
                    '^Passed(?:[ :.-].*)?$')) `
                "$($definition.Milestone) passed verification metadata must record a Passed result."
        }
        if ($goalStatus -eq "complete") {
            Assert-Condition `
                ($lastVerificationStatusMatch.Groups["value"].Value -eq "passed") `
                "$($definition.Milestone) completed goal must record a passed last verification."
            Assert-Condition `
                ([regex]::IsMatch($lastVerificationAt, '^\d{4}-\d{2}-\d{2}$')) `
                "$($definition.Milestone) completed goal must record a verification date."
            Assert-Condition `
                ($lastVerificationCommand -notin @(
                    "pending", "never", "none", "null", "not_run")) `
                "$($definition.Milestone) completed goal must record the verification command."
            Assert-Condition `
                ($lastVerificationResult -notin @(
                    "pending", "never", "none", "null", "not_run")) `
                "$($definition.Milestone) completed goal must record the verification result."
        }

        $hasActiveStatus = $goalStatus -in $activeGoalStatuses
        Assert-Condition `
            ($executionAuthorized -eq $hasActiveStatus) `
            "$($definition.Milestone) execution_authorized must be true exactly while status is in_progress or blocked."
        if ($hasActiveStatus) {
            Assert-Condition `
                ($activeStage -ne "none") `
                "$($definition.Milestone) active goal must name an active stage."
            Assert-Condition `
                ([regex]::IsMatch(
                    $goalContent,
                    '^## ' + [regex]::Escape($activeStage) + ' - ',
                    [System.Text.RegularExpressions.RegexOptions]::Multiline)) `
                "$($definition.Milestone) active stage '$activeStage' has no stage section."
            $activeGoalMilestones.Add($definition.Milestone)
        }
        else {
            Assert-Condition `
                ($activeStage -eq "none") `
                "$($definition.Milestone) inactive goal must use active_stage: none."
        }

        $roadmapSectionPattern = '(?ms)^##\s+' +
            [regex]::Escape($definition.Milestone) +
            '\s+-[^\r\n]*\r?\n(?<body>.*?)(?=^##\s+|\z)'
        $roadmapSectionMatch = [regex]::Match(
            $roadmap,
            $roadmapSectionPattern)
        Assert-Condition `
            $roadmapSectionMatch.Success `
            "The roadmap is missing the $($definition.Milestone) milestone section."
        $roadmapStatusMatch = [regex]::Match(
            $roadmapSectionMatch.Groups["body"].Value,
            '(?m)^\*\*Status:\*\*[ \t]*(?<value>[A-Za-z]+)[ \t]*$')
        Assert-Condition `
            $roadmapStatusMatch.Success `
            "The roadmap is missing the $($definition.Milestone) status."
        $roadmapStatus = $roadmapStatusMatch.Groups["value"].Value
        $expectedRoadmapStatus = switch ($goalStatus) {
            "complete" { "Complete" }
            "in_progress" { "Active" }
            "blocked" { "Active" }
            default { "Planned" }
        }
        Assert-Condition `
            ($roadmapStatus -eq $expectedRoadmapStatus) `
            "$($definition.Milestone) roadmap status must be '$expectedRoadmapStatus' while the goal is '$goalStatus'."

        foreach ($requiredSection in @(
            "## Execution Protocol",
            "## Stage Status",
            $definition.ExitSection,
            "## Non-Goals",
            "## Blocker Log",
            "## Verification Log"
        )) {
            Assert-Condition `
                $goalContent.Contains($requiredSection) `
                "$($definition.Milestone) goal is missing required section '$requiredSection'."
        }
        $actualStageCount = [regex]::Matches(
            $goalContent,
            $definition.StagePattern,
            [System.Text.RegularExpressions.RegexOptions]::Multiline).Count
        Assert-Condition `
            ($actualStageCount -eq $definition.ExpectedStageCount) `
            "$($definition.Milestone) goal must define $($definition.ExpectedStageCount) ordered stages; found $actualStageCount."

        $expectedStageIds = @(
            for ($stageIndex = 0;
                $stageIndex -lt $definition.ExpectedStageCount;
                $stageIndex++) {
                "{0}-{1:D2}" -f `
                    $definition.StageIdPrefix, `
                    ($definition.FirstStageNumber + $stageIndex)
            }
        )
        $stageHeadingPattern = '(?m)^## (?<id>' +
            [regex]::Escape($definition.StageIdPrefix) +
            '-\d{2}) - '
        $actualStageHeadingIds = @(
            [regex]::Matches($goalContent, $stageHeadingPattern) |
                ForEach-Object { $_.Groups["id"].Value })
        Assert-Condition `
            (($actualStageHeadingIds -join ',') -eq
                ($expectedStageIds -join ',')) `
            "$($definition.Milestone) detailed stage sections must use the exact ordered stage IDs."

        $stageRowPattern = '(?m)^\|[ \t]*(?<id>' +
            [regex]::Escape($definition.StageIdPrefix) +
            '-\d{2})[ \t]*\|[^|\r\n]*\|[ \t]*' +
            '(?<status>pending|in_progress|blocked|complete)[ \t]*\|'
        $stageRows = [regex]::Matches($goalContent, $stageRowPattern)
        $actualStageIds = @(
            $stageRows | ForEach-Object { $_.Groups["id"].Value })
        Assert-Condition `
            (($actualStageIds -join ',') -eq ($expectedStageIds -join ',')) `
            "$($definition.Milestone) Stage Status table must contain each ordered stage exactly once."

        $stageStatuses = [ordered]@{}
        foreach ($stageRow in $stageRows) {
            $stageStatuses[$stageRow.Groups["id"].Value] =
                $stageRow.Groups["status"].Value
        }

        if ($goalStatus -in @("drafting", "ready")) {
            $nonPendingStageIds = @(
                $expectedStageIds |
                    Where-Object { $stageStatuses[$_] -ne "pending" })
            Assert-Condition `
                ($nonPendingStageIds.Count -eq 0) `
                "$($definition.Milestone) drafting or ready goal must keep every stage pending."
        }
        elseif ($goalStatus -eq "complete") {
            $incompleteStageIds = @(
                $expectedStageIds |
                    Where-Object { $stageStatuses[$_] -ne "complete" })
            Assert-Condition `
                ($incompleteStageIds.Count -eq 0) `
                "$($definition.Milestone) completed goal must mark every stage complete."

            $verificationLogIndex = $goalContent.IndexOf(
                "## Verification Log",
                [StringComparison]::Ordinal)
            Assert-Condition `
                ($verificationLogIndex -ge 0) `
                "$($definition.Milestone) completed goal is missing its Verification Log."
            $verificationLog = $goalContent.Substring($verificationLogIndex)
            $verificationLogEntryCount = [regex]::Matches(
                $verificationLog,
                '(?m)^\|[ \t]*\d{4}-\d{2}-\d{2}[ \t]*\|').Count
            Assert-Condition `
                ($verificationLogEntryCount -ge $definition.ExpectedStageCount) `
                "$($definition.Milestone) completed goal must record verification entries for every stage."
            foreach ($expectedStageId in $expectedStageIds) {
                $passedStagePattern = '(?m)^\|[ \t]*\d{4}-\d{2}-\d{2}' +
                    '[ \t]*\|[ \t]*' +
                    [regex]::Escape($expectedStageId) +
                    '[ \t]*\|[^\r\n]*\|[ \t]*Passed' +
                    '(?:[ :.-][^|\r\n]*)?[ \t]*\|[ \t]*$'
                Assert-Condition `
                    ([regex]::IsMatch(
                        $verificationLog,
                        $passedStagePattern)) `
                    "$($definition.Milestone) completed goal must include a passed verification entry for stage '$expectedStageId'."
            }
            $finalVerificationPattern = '(?m)^\|[ \t]*\d{4}-\d{2}-\d{2}' +
                '[ \t]*\|[ \t]*' +
                [regex]::Escape($definition.FinalStageId) +
                '[ \t]*\|[ \t]*`' +
                [regex]::Escape($lastVerificationCommand) +
                '`[^|\r\n]*\|[ \t]*Passed' +
                '(?:[ :.-][^|\r\n]*)?[ \t]*\|[ \t]*$'
            Assert-Condition `
                ([regex]::IsMatch(
                    $verificationLog,
                    $finalVerificationPattern)) `
                "$($definition.Milestone) completed goal must log its final-stage last verification command."
        }
        else {
            $activeStageIndex = [array]::IndexOf(
                [string[]]$expectedStageIds,
                $activeStage)
            Assert-Condition `
                ($activeStageIndex -ge 0) `
                "$($definition.Milestone) active_stage '$activeStage' is not in the Stage Status table."
            for ($stageIndex = 0;
                $stageIndex -lt $expectedStageIds.Count;
                $stageIndex++) {
                $expectedStageStatus = if ($stageIndex -lt $activeStageIndex) {
                    "complete"
                }
                elseif ($stageIndex -eq $activeStageIndex) {
                    $goalStatus
                }
                else {
                    "pending"
                }
                $stageId = $expectedStageIds[$stageIndex]
                Assert-Condition `
                    ($stageStatuses[$stageId] -eq $expectedStageStatus) `
                    "$($definition.Milestone) stage '$stageId' must be '$expectedStageStatus' while '$activeStage' is active."
            }
        }

        $goalRecords[$definition.Milestone] = [pscustomobject]@{
            Content = $goalContent
            Metadata = $metadata
            Status = $goalStatus
            ActiveStage = $activeStage
            ExecutionAuthorized = $executionAuthorized
            StageStatuses = $stageStatuses
        }
    }

    Assert-Condition `
        ($activeGoalMilestones.Count -le 1) `
        "At most one repository goal may be active; found $($activeGoalMilestones -join ', ')."
    $activeRoadmapMilestoneCount = [regex]::Matches(
        $roadmap,
        '(?m)^\*\*Status:\*\*[ \t]*Active[ \t]*$').Count
    Assert-Condition `
        ($activeRoadmapMilestoneCount -le 1) `
        "At most one roadmap milestone may be Active; found $activeRoadmapMilestoneCount."
    Assert-Condition `
        ($activeRoadmapMilestoneCount -eq $activeGoalMilestones.Count) `
        "The active roadmap milestone count must match the active repository goal count."

    $m1Goal = $goalRecords["M1"]
    $m2Goal = $goalRecords["M2"]

    $m1DecisionRecordMatch = [regex]::Match(
        $m1Goal.Content,
        '(?ms)^## Capability Decision Record\r?\n(?<body>.*?)(?=^##\s+|\z)')
    Assert-Condition `
        $m1DecisionRecordMatch.Success `
        "M1 goal is missing the Capability Decision Record section."
    $m1DecisionRecord = $m1DecisionRecordMatch.Groups["body"].Value
    $expectedM1DecisionFields = @(
        "Selection status",
        "Requirement source",
        "Player-visible outcome",
        "Owning SwordMaster domain",
        "Concrete rules and invariants",
        "Expected failure behavior",
        "Required server surface",
        "Required client interaction",
        "Required configuration, API, or persistence",
        "Explicit non-goals",
        "Capability-specific verification commands",
        "User approval evidence"
    )
    $actualM1DecisionFields = @(
        [regex]::Matches(
            $m1DecisionRecord,
            '(?m)^\|[ \t]*(?<field>[^|\r\n]+?)[ \t]*\|[^\r\n]*$') |
            ForEach-Object { $_.Groups["field"].Value.Trim() } |
            Where-Object { $_ -notin @("Field", "---") })
    Assert-Condition `
        (($actualM1DecisionFields -join ',') -eq
            ($expectedM1DecisionFields -join ',')) `
        "M1 Capability Decision Record must contain the exact ordered fields."

    $m1TaskRegisterMatch = [regex]::Match(
        $m1Goal.Content,
        '(?ms)^## Active Task Register\r?\n(?<body>.*?)(?=^##\s+|\z)')
    Assert-Condition `
        $m1TaskRegisterMatch.Success `
        "M1 goal is missing the Active Task Register section."
    $m1TaskRegister = $m1TaskRegisterMatch.Groups["body"].Value
    Assert-Condition `
        $m1TaskRegister.Contains(
            "| ID | Stage | Ownership | Deliverable | Dependencies | Verification | Status |") `
        "M1 Active Task Register must contain the exact ordered columns."
    $m1TaskRowPattern = '(?m)^\|[ \t]*(?<id>M1-\d{2}-T\d{2})[ \t]*' +
        '\|[ \t]*(?<stage>M1-\d{2})[ \t]*' +
        '\|[^|\r\n]+\|[^|\r\n]+\|[^|\r\n]+\|[^|\r\n]+' +
        '\|[ \t]*(?<status>pending|in_progress|blocked|complete)[ \t]*\|[ \t]*$'
    $m1TaskRows = [regex]::Matches(
        $m1TaskRegister,
        $m1TaskRowPattern)
    Assert-Condition `
        ($m1TaskRows.Count -gt 0) `
        "M1 Active Task Register must contain at least one task."
    $m1TaskIds = @(
        $m1TaskRows | ForEach-Object { $_.Groups["id"].Value })
    $uniqueM1TaskIds = @($m1TaskIds | Sort-Object -Unique)
    Assert-Condition `
        ($uniqueM1TaskIds.Count -eq $m1TaskIds.Count) `
        "M1 Active Task Register task IDs must be unique."
    $m1TaskStatuses = [ordered]@{}
    foreach ($m1TaskRow in $m1TaskRows) {
        $taskId = $m1TaskRow.Groups["id"].Value
        $taskStage = $m1TaskRow.Groups["stage"].Value
        Assert-Condition `
            ($m1Goal.StageStatuses.Keys -contains $taskStage) `
            "M1 task '$taskId' references unknown stage '$taskStage'."
        $m1TaskStatuses[$taskId] = $m1TaskRow.Groups["status"].Value
    }
    if ($m1Goal.ExecutionAuthorized) {
        $activeM1TaskRows = @(
            $m1TaskRows |
                Where-Object {
                    $_.Groups["status"].Value -in @("in_progress", "blocked")
                })
        Assert-Condition `
            ($activeM1TaskRows.Count -eq 1) `
            "An active M1 goal must have exactly one in-progress or blocked task."
        Assert-Condition `
            ($activeM1TaskRows[0].Groups["status"].Value -eq $m1Goal.Status) `
            "The active M1 task status must match the M1 goal status."
        Assert-Condition `
            ($activeM1TaskRows[0].Groups["stage"].Value -eq $m1Goal.ActiveStage) `
            "The active M1 task must belong to the active M1 stage."

        $orderedM1StageIds = @($m1Goal.StageStatuses.Keys)
        $activeM1StageIndex = [array]::IndexOf(
            [string[]]$orderedM1StageIds,
            $m1Goal.ActiveStage)
        foreach ($m1TaskRow in $m1TaskRows) {
            $taskStageIndex = [array]::IndexOf(
                [string[]]$orderedM1StageIds,
                $m1TaskRow.Groups["stage"].Value)
            Assert-Condition `
                ($taskStageIndex -le $activeM1StageIndex) `
                "M1 task '$($m1TaskRow.Groups["id"].Value)' must not predefine work for a future stage."
        }
    }
    elseif ($m1Goal.Status -eq "complete") {
        $incompleteM1TaskRows = @(
            $m1TaskRows |
                Where-Object { $_.Groups["status"].Value -ne "complete" })
        Assert-Condition `
            ($incompleteM1TaskRows.Count -eq 0) `
            "A completed M1 goal must mark every registered task complete."
    }

    $requiredM106TaskIds = @(
        "M1-06-T01",
        "M1-06-T02",
        "M1-06-T03",
        "M1-06-T04"
    )
    foreach ($requiredTaskId in $requiredM106TaskIds) {
        Assert-Condition `
            ($m1TaskStatuses.Keys -contains $requiredTaskId) `
            "M1 Active Task Register is missing required task '$requiredTaskId'."
    }
    $m106StageStatus = $m1Goal.StageStatuses["M1-06"]
    if ($m106StageStatus -eq "complete") {
        foreach ($requiredTaskId in $requiredM106TaskIds) {
            Assert-Condition `
                ($m1TaskStatuses[$requiredTaskId] -eq "complete") `
                "Completed M1-06 must mark '$requiredTaskId' complete."
        }
    }
    elseif ($m1Goal.ActiveStage -eq "M1-06") {
        $firstIncompleteIndex = -1
        for ($taskIndex = 0;
            $taskIndex -lt $requiredM106TaskIds.Count;
            $taskIndex++) {
            if ($m1TaskStatuses[$requiredM106TaskIds[$taskIndex]] -ne "complete") {
                $firstIncompleteIndex = $taskIndex
                break
            }
        }
        Assert-Condition `
            ($firstIncompleteIndex -ge 0) `
            "Active M1-06 must retain one incomplete ordered task."
        for ($taskIndex = 0;
            $taskIndex -lt $requiredM106TaskIds.Count;
            $taskIndex++) {
            $requiredTaskId = $requiredM106TaskIds[$taskIndex]
            $expectedTaskStatus = if ($taskIndex -lt $firstIncompleteIndex) {
                "complete"
            }
            elseif ($taskIndex -eq $firstIncompleteIndex) {
                $m1Goal.Status
            }
            else {
                "pending"
            }
            Assert-Condition `
                ($m1TaskStatuses[$requiredTaskId] -eq $expectedTaskStatus) `
                "M1-06 task '$requiredTaskId' must be '$expectedTaskStatus' in ordered execution."
        }
    }

    $requiredM107TaskIds = @(
        "M1-07-T01",
        "M1-07-T02",
        "M1-07-T03",
        "M1-07-T04"
    )
    foreach ($requiredTaskId in $requiredM107TaskIds) {
        Assert-Condition `
            ($m1TaskStatuses.Keys -contains $requiredTaskId) `
            "M1 Active Task Register is missing required task '$requiredTaskId'."
    }
    $m107StageStatus = $m1Goal.StageStatuses["M1-07"]
    if ($m107StageStatus -in @("in_progress", "blocked", "complete")) {
        Assert-Condition `
            ($m1Goal.StageStatuses["M1-06"] -eq "complete") `
            "M1-07 requires M1-06 to be complete."
        Assert-Condition `
            ($m1TaskStatuses["M1-06-T04"] -eq "complete") `
            "M1-07 requires M1-06-T04 to be complete."
    }
    if ($m107StageStatus -eq "complete") {
        foreach ($requiredTaskId in $requiredM107TaskIds) {
            Assert-Condition `
                ($m1TaskStatuses[$requiredTaskId] -eq "complete") `
                "Completed M1-07 must mark '$requiredTaskId' complete."
        }
    }
    elseif ($m1Goal.ActiveStage -eq "M1-07") {
        $firstIncompleteIndex = -1
        for ($taskIndex = 0;
            $taskIndex -lt $requiredM107TaskIds.Count;
            $taskIndex++) {
            if ($m1TaskStatuses[$requiredM107TaskIds[$taskIndex]] -ne "complete") {
                $firstIncompleteIndex = $taskIndex
                break
            }
        }
        Assert-Condition `
            ($firstIncompleteIndex -ge 0) `
            "Active M1-07 must retain one incomplete ordered task."
        for ($taskIndex = 0;
            $taskIndex -lt $requiredM107TaskIds.Count;
            $taskIndex++) {
            $requiredTaskId = $requiredM107TaskIds[$taskIndex]
            $expectedTaskStatus = if ($taskIndex -lt $firstIncompleteIndex) {
                "complete"
            }
            elseif ($taskIndex -eq $firstIncompleteIndex) {
                $m1Goal.Status
            }
            else {
                "pending"
            }
            Assert-Condition `
                ($m1TaskStatuses[$requiredTaskId] -eq $expectedTaskStatus) `
                "M1-07 task '$requiredTaskId' must be '$expectedTaskStatus' in ordered execution."
        }
    }

    $requiredM108TaskIds = @(
        "M1-08-T01",
        "M1-08-T02",
        "M1-08-T03"
    )
    foreach ($requiredTaskId in $requiredM108TaskIds) {
        Assert-Condition `
            ($m1TaskStatuses.Keys -contains $requiredTaskId) `
            "M1 Active Task Register is missing required task '$requiredTaskId'."
    }
    $m108StageStatus = $m1Goal.StageStatuses["M1-08"]
    if ($m108StageStatus -in @("in_progress", "blocked", "complete")) {
        Assert-Condition `
            ($m1Goal.StageStatuses["M1-07"] -eq "complete") `
            "M1-08 requires M1-07 to be complete."
        Assert-Condition `
            ($m1TaskStatuses["M1-07-T04"] -eq "complete") `
            "M1-08 requires M1-07-T04 to be complete."
    }
    if ($m108StageStatus -eq "complete") {
        foreach ($requiredTaskId in $requiredM108TaskIds) {
            Assert-Condition `
                ($m1TaskStatuses[$requiredTaskId] -eq "complete") `
                "Completed M1-08 must mark '$requiredTaskId' complete."
        }
    }
    elseif ($m1Goal.ActiveStage -eq "M1-08") {
        $firstIncompleteIndex = -1
        for ($taskIndex = 0;
            $taskIndex -lt $requiredM108TaskIds.Count;
            $taskIndex++) {
            if ($m1TaskStatuses[$requiredM108TaskIds[$taskIndex]] -ne "complete") {
                $firstIncompleteIndex = $taskIndex
                break
            }
        }
        Assert-Condition `
            ($firstIncompleteIndex -ge 0) `
            "Active M1-08 must retain one incomplete ordered task."
        for ($taskIndex = 0;
            $taskIndex -lt $requiredM108TaskIds.Count;
            $taskIndex++) {
            $requiredTaskId = $requiredM108TaskIds[$taskIndex]
            $expectedTaskStatus = if ($taskIndex -lt $firstIncompleteIndex) {
                "complete"
            }
            elseif ($taskIndex -eq $firstIncompleteIndex) {
                $m1Goal.Status
            }
            else {
                "pending"
            }
            Assert-Condition `
                ($m1TaskStatuses[$requiredTaskId] -eq $expectedTaskStatus) `
                "M1-08 task '$requiredTaskId' must be '$expectedTaskStatus' in ordered execution."
        }
    }
    if ($m1Goal.Status -ne "complete") {
        Assert-Condition `
            ($m2Goal.Status -eq "drafting" -and
                -not $m2Goal.ExecutionAuthorized -and
                $m2Goal.ActiveStage -eq "none") `
            "M2 must remain inactive and drafting until M1 completes."
    }

    $m1Design = Get-Content -LiteralPath `
        "samples/SwordMaster/docs/equipment-attribute-effect.md" -Raw
    foreach ($requiredM1DesignText in @(
        '**Approval reference:** `user-approval-2026-08-01-equipment-attribute-effect`',
        "samples/SwordMaster/server/SwordMaster.Server.sln",
        "## Physical Backend Layout",
        "## Dependency Direction",
        "## Runtime Composition",
        "## Persistence Boundary",
        "## Domain Ownership",
        "## Configuration Policy",
        "## Player Identity And Authorization",
        "## HTTP Contracts",
        "## Transactions, Concurrency, And Retry Safety",
        "## NovaGame Boundary Review",
        "## Exact Source References",
        "## Verification Commands",
        "PlayerAssetsProfile",
        "SwordMasterPlayer"
    )) {
        Assert-Condition `
            $m1Design.Contains($requiredM1DesignText) `
            "SwordMaster M1 design is missing required text '$requiredM1DesignText'."
    }
    Assert-Condition `
        (-not $m1Design.Contains("**Status:** In progress")) `
        "SwordMaster M1 design must leave draft status before M1-02 verification."

    $m1AcceptanceEvidence = Get-Content -LiteralPath `
        "samples/SwordMaster/docs/player-assets-m1-06-evidence.md" -Raw
    foreach ($requiredM1EvidenceText in @(
        "# SwordMaster PlayerAssets M1-06 Acceptance Evidence",
        "**Milestone:** M1",
        "**Stage:** M1-06 - Verify Behavior End To End",
        '**Capability:** `equipment-attribute-effect`',
        '**Approval reference:** `user-approval-2026-08-01-equipment-attribute-effect`',
        "**Status:** Frozen",
        "**Frozen at:** 2026-08-02",
        "**Completed at:** 2026-08-02",
        "## Scope And Authority",
        "## Evidence Rules",
        "## Requirement-To-Evidence Matrix",
        "## Recorded Failure Coverage",
        "## Named Gaps",
        "## Authorized Follow-Up Work",
        "## Exact Verification Commands",
        "## Explicit Exclusions",
        "| ID | Acceptance Requirement | Existing Evidence | Verification Command | Gap | Owning Task |",
        "No mock may substitute for a missing cross-boundary acceptance path.",
        "NovaGame receives no new business capability during M1-06.",
        "Browser automation, an admin UI, production Cocos scenes, extraction, and new gameplay are excluded.",
        "happy path",
        "authorization",
        "invalid input",
        "concurrency conflict",
        "idempotency conflict",
        "retry and recovery",
        "persisted state",
        "command receipts",
        "audit evidence",
        "pinned GameConfig version",
        "client-visible results",
        "SwordMaster.PlayerAssets.InvalidRequest",
        "SwordMaster.PlayerAssets.SlotNotEquipped"
    )) {
        Assert-Condition `
            $m1AcceptanceEvidence.Contains($requiredM1EvidenceText) `
            "SwordMaster M1-06 acceptance evidence is missing required text '$requiredM1EvidenceText'."
    }

    $m1ReuseEvidence = Get-Content -LiteralPath `
        "samples/SwordMaster/docs/player-assets-m1-07-reuse-evidence.md" -Raw
    foreach ($requiredM1ReuseEvidenceText in @(
        "# SwordMaster PlayerAssets M1-07 Reuse Evidence",
        "**Stage:** M1-07 - Record Reuse Evidence",
        '**Capability:** `equipment-attribute-effect`',
        "## Scope And Authority",
        "## Concrete Reuse Evidence",
        "## T02 Candidate For Decision",
        "## T02 Conclusion",
        "## T03 Decision",
        '`game-config-snapshot-consumer-validation`',
        "The complete PlayerAssets or equipment system remains SwordMaster-owned.",
        "Validated for possible future M2 consideration",
        "Extraction authorization | Not granted",
        "At M1-07 completion, M2 remained drafting and inactive",
        "does not authorize extraction"
    )) {
        Assert-Condition `
            $m1ReuseEvidence.Contains($requiredM1ReuseEvidenceText) `
            "SwordMaster M1-07 reuse evidence is missing required text '$requiredM1ReuseEvidenceText'."
    }

    $m2EvidenceRecordMatch = [regex]::Match(
        $m2Goal.Content,
        '(?ms)^## Extraction Evidence Record\r?\n(?<body>.*?)(?=^##\s+|\z)')
    Assert-Condition `
        $m2EvidenceRecordMatch.Success `
        "M2 goal is missing the Extraction Evidence Record section."
    $m2EvidenceRecord = $m2EvidenceRecordMatch.Groups["body"].Value
    $expectedM2EvidenceFields = @(
        "Candidate status",
        "Candidate identifier",
        "Source M1 capability",
        "Source repository state",
        "Proven behavior and tests",
        "Reuse pressure or variation",
        "Reusable mechanism",
        "SwordMaster policy retained",
        "Owning NovaGame domain",
        "Proposed experimental contract",
        "Migration and duplicate-removal plan",
        "Candidate-specific verification commands",
        "Explicit user authorization"
    )
    $actualM2EvidenceFields = @(
        [regex]::Matches(
            $m2EvidenceRecord,
            '(?m)^\|[ \t]*(?<field>[^|\r\n]+?)[ \t]*\|[^\r\n]*$') |
            ForEach-Object { $_.Groups["field"].Value.Trim() } |
            Where-Object { $_ -notin @("Field", "---") })
    Assert-Condition `
        (($actualM2EvidenceFields -join ',') -eq
            ($expectedM2EvidenceFields -join ',')) `
        "M2 Extraction Evidence Record must contain the exact ordered fields."

    if ($m2Goal.Status -ne "drafting") {
        Assert-Condition `
            ($m1Goal.Status -eq "complete") `
            "M2 cannot leave drafting before M1 is complete."
    }

    $selectedCapabilityMatch = [regex]::Match(
        $m1Goal.Metadata,
        '(?m)^selected_capability:\s*(?<value>none|[a-z0-9][a-z0-9._-]*)\s*$')
    $capabilityApprovalStatusMatch = [regex]::Match(
        $m1Goal.Metadata,
        '(?m)^capability_approval_status:\s*(?<value>pending|approved)\s*$')
    $capabilityApprovalReferenceMatch = [regex]::Match(
        $m1Goal.Metadata,
        '(?m)^capability_approval_reference:\s*(?<value>[^\r\n]+?)\s*$')
    $validatedExtractionCandidateMatch = [regex]::Match(
        $m1Goal.Metadata,
        '(?m)^validated_extraction_candidate:\s*(?<value>none|[a-z0-9][a-z0-9._-]*)\s*$')
    $validatedExtractionCandidateReferenceMatch = [regex]::Match(
        $m1Goal.Metadata,
        '(?m)^validated_extraction_candidate_reference:\s*(?<value>[^\r\n]+?)\s*$')
    Assert-Condition `
        $selectedCapabilityMatch.Success `
        "M1 goal metadata has an invalid selected_capability."
    Assert-Condition `
        $capabilityApprovalStatusMatch.Success `
        "M1 goal metadata has an invalid capability_approval_status."
    Assert-Condition `
        $capabilityApprovalReferenceMatch.Success `
        "M1 goal metadata is missing capability_approval_reference."
    Assert-Condition `
        $validatedExtractionCandidateMatch.Success `
        "M1 goal metadata has an invalid validated_extraction_candidate."
    Assert-Condition `
        $validatedExtractionCandidateReferenceMatch.Success `
        "M1 goal metadata is missing validated_extraction_candidate_reference."

    $capabilityApprovalStatus =
        $capabilityApprovalStatusMatch.Groups["value"].Value
    $capabilityApprovalReference =
        $capabilityApprovalReferenceMatch.Groups["value"].Value
    if ($capabilityApprovalStatus -eq "pending") {
        Assert-Condition `
            ($capabilityApprovalReference -eq "none") `
            "M1 pending capability approval must use capability_approval_reference: none."
    }
    else {
        Assert-Condition `
            ($capabilityApprovalReference -notin @(
                "none", "'none'", '"none"')) `
            "M1 approved capability must include an approval reference."
    }
    if ($m1Goal.Status -eq "ready") {
        Assert-Condition `
            ($selectedCapabilityMatch.Groups["value"].Value -eq "none") `
            "M1 ready definition must not preselect a capability."
    }

    $validatedExtractionCandidate =
        $validatedExtractionCandidateMatch.Groups["value"].Value
    $validatedExtractionCandidateReference =
        $validatedExtractionCandidateReferenceMatch.Groups["value"].Value
    if ($m107StageStatus -in @("in_progress", "blocked") -and
        $m1TaskStatuses["M1-07-T03"] -eq "pending") {
        Assert-Condition `
            ($validatedExtractionCandidate -eq "none" -and
                $validatedExtractionCandidateReference -eq "none") `
            "M1-07 must retain no validated extraction candidate before its decision task begins."
    }
    if ($validatedExtractionCandidate -eq "none") {
        Assert-Condition `
            ($validatedExtractionCandidateReference -eq "none") `
            "M1 extraction-candidate reference must remain none when no candidate is validated."
    }
    else {
        Assert-Condition `
            ($validatedExtractionCandidateReference -notin @(
                "none", "'none'", '"none"')) `
            "M1 validated extraction candidate must include an evidence reference."
        Assert-Condition `
            ($validatedExtractionCandidate -ne
                $selectedCapabilityMatch.Groups["value"].Value) `
            "M1 may validate only a narrower reusable mechanism, not the complete selected capability."
        Assert-Condition `
            $m1ReuseEvidence.Contains($validatedExtractionCandidate) `
            "M1 validated extraction candidate must appear in the reuse-evidence record."
        Assert-Condition `
            ($validatedExtractionCandidateReference -eq
                "samples/SwordMaster/docs/player-assets-m1-07-reuse-evidence.md#t03-decision") `
            "M1 validated extraction candidate must reference the T03 evidence decision."
    }

    $m1SelectionResolved = $m1Goal.Status -eq "complete" -or
        ($m1Goal.ExecutionAuthorized -and $m1Goal.ActiveStage -ne "M1-01")
    if ($m1SelectionResolved) {
        Assert-Condition `
            ($selectedCapabilityMatch.Groups["value"].Value -ne "none") `
            "M1 must name the selected capability after M1-01."
        Assert-Condition `
            ($capabilityApprovalStatus -eq "approved") `
            "M1 must record approved capability status after M1-01."
        Assert-Condition `
            ($capabilityApprovalReference -notin @(
                "none", "'none'", '"none"')) `
            "M1 must record a capability approval reference after M1-01."
        Assert-Condition `
            (-not [regex]::IsMatch(
                $m1DecisionRecord,
                '(?m)^\|[^|\r\n]+\|[ \t]*Not (?:selected|recorded)[ \t]*\|[ \t]*$')) `
            "M1 Capability Decision Record must be resolved after M1-01."
    }

    $extractionCandidateMatch = [regex]::Match(
        $m2Goal.Metadata,
        '(?m)^extraction_candidate:\s*(?<value>none|[a-z0-9][a-z0-9._-]*)\s*$')
    $extractionAuthorizationStatusMatch = [regex]::Match(
        $m2Goal.Metadata,
        '(?m)^extraction_authorization_status:\s*(?<value>pending|approved)\s*$')
    $extractionAuthorizationReferenceMatch = [regex]::Match(
        $m2Goal.Metadata,
        '(?m)^extraction_authorization_reference:\s*(?<value>[^\r\n]+?)\s*$')
    Assert-Condition `
        $extractionCandidateMatch.Success `
        "M2 goal metadata has an invalid extraction_candidate."
    Assert-Condition `
        $extractionAuthorizationStatusMatch.Success `
        "M2 goal metadata has an invalid extraction_authorization_status."
    Assert-Condition `
        $extractionAuthorizationReferenceMatch.Success `
        "M2 goal metadata is missing extraction_authorization_reference."
    $extractionAuthorizationStatus =
        $extractionAuthorizationStatusMatch.Groups["value"].Value
    $extractionAuthorizationReference =
        $extractionAuthorizationReferenceMatch.Groups["value"].Value
    if ($extractionAuthorizationStatus -eq "pending") {
        Assert-Condition `
            ($extractionAuthorizationReference -eq "none") `
            "M2 pending extraction authorization must use extraction_authorization_reference: none."
    }
    else {
        Assert-Condition `
            ($extractionAuthorizationReference -notin @(
                "none", "'none'", '"none"')) `
            "M2 approved extraction must include an authorization reference."
    }
    if ($m2Goal.Status -ne "drafting") {
        Assert-Condition `
            ($extractionCandidateMatch.Groups["value"].Value -ne "none") `
            "M2 must name the authorized extraction candidate before leaving drafting."
        Assert-Condition `
            ($extractionCandidateMatch.Groups["value"].Value -eq
                $validatedExtractionCandidate) `
            "M2 extraction candidate must match the candidate validated by completed M1 evidence."
        Assert-Condition `
            ($extractionAuthorizationStatus -eq "approved") `
            "M2 must record explicit extraction approval before leaving drafting."
        Assert-Condition `
            ($extractionAuthorizationReference -notin @(
                "none", "'none'", '"none"')) `
            "M2 must record an extraction authorization reference before leaving drafting."
    }

    if ($m2Goal.ExecutionAuthorized) {
        $activeM2Candidate = $extractionCandidateMatch.Groups["value"].Value
        $staleM2CurrentStatePhrases = @(
            "no repository milestone is active",
            "M2 remains drafting and inactive",
            "M2 remains inactive",
            "no extraction is authorized",
            "Authorized extraction candidates: none"
        )
        foreach ($currentStatusPath in @(
            "AGENTS.md",
            "README.md",
            "docs/README.md",
            "docs/roadmap.md",
            "samples/SwordMaster/README.md"
        )) {
            $currentStatusContent = Get-Content -LiteralPath $currentStatusPath -Raw
            Assert-Condition `
                $currentStatusContent.Contains($activeM2Candidate) `
                "$currentStatusPath must name the active M2 extraction candidate."
            Assert-Condition `
                $currentStatusContent.Contains($m2Goal.ActiveStage) `
                "$currentStatusPath must name the active M2 stage '$($m2Goal.ActiveStage)'."
            foreach ($stalePhrase in $staleM2CurrentStatePhrases) {
                Assert-Condition `
                    (-not $currentStatusContent.Contains(
                        $stalePhrase,
                        [StringComparison]::OrdinalIgnoreCase)) `
                    "$currentStatusPath contains stale M2 current-state text '$stalePhrase'."
            }
        }
    }

    if ($m2Goal.StageStatuses["M2-01"] -eq "complete") {
        $requiredM201AuthorizationReference =
            "user-approval-2026-08-10-game-config-snapshot-consumer-validation-extraction"
        Assert-Condition `
            ($extractionAuthorizationReference -eq
                $requiredM201AuthorizationReference) `
            "Completed M2-01 must retain the exact extraction authorization reference."
        foreach ($requiredM201EvidenceText in @(
            'dotnet test server/tests/NovaGame.EntityFrameworkCore.Tests/NovaGame.EntityFrameworkCore.Tests.csproj --no-restore --filter FullyQualifiedName~GameConfigDomainTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~GameConfigEquipmentDefinitionProviderTests',
            'pnpm --dir clients/cocos/packages/game-config-client test',
            'Node 24.14.0',
            'pnpm 10.33.4'
        )) {
            Assert-Condition `
                $m2EvidenceRecord.Contains($requiredM201EvidenceText) `
                "Completed M2-01 evidence is missing '$requiredM201EvidenceText'."
        }
        Assert-Condition `
            $m2Goal.Metadata.Contains("last_verification_status: passed") `
            "Completed M2-01 must record passed last-verification metadata."
    }

    if ($m2Goal.StageStatuses["M2-02"] -eq "complete") {
        foreach ($requiredM202EvidenceText in @(
            "## M2-02 Policy And Mechanism Map",
            "### Proven Boundary",
            "### Neutral Mechanism Invariants",
            "### SwordMaster Policy Retained",
            "strict UTF-8",
            "exact seven-fractional-digit",
            "Transport-specific strong ETag parsing remains",
            "No configuration flag, callback, or extension point moves these policies into NovaGame"
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM202EvidenceText) `
                "Completed M2-02 evidence is missing '$requiredM202EvidenceText'."
        }
    }

    if ($m2Goal.StageStatuses["M2-03"] -eq "complete") {
        foreach ($requiredM203EvidenceText in @(
            "## M2-03 Ownership And Experimental Contract",
            "### Layer Ownership",
            "### Smallest Public Contract",
            "### Composition And Migration",
            "### Operational Semantics",
            "public sealed class GameConfigSnapshotValidator",
            "public sealed class ValidatedGameConfigSnapshot",
            "[NotNullWhen(true)]",
            "one deterministic implementation",
            'direct SwordMaster Application reference to the public `NovaGame.Domain` project',
            "add a boundary unsupported by the evidence"
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM203EvidenceText) `
                "Completed M2-03 evidence is missing '$requiredM203EvidenceText'."
        }
        Assert-Condition `
            (-not $m2Goal.Content.Contains("IGameConfigSnapshotConsumerValidator")) `
            "M2-03 must not retain an unproven snapshot-validator interface."
        Assert-Condition `
            (-not $m2Goal.Content.Contains("GameConfigSnapshotValidationRequest")) `
            "M2-03 must not retain a one-use snapshot-validation request DTO."
    }

    if ($m2Goal.StageStatuses["M2-04"] -eq "complete") {
        foreach ($requiredM204Path in @(
            "server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotValidator.cs",
            "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigSnapshotValidatorTests.cs"
        )) {
            Assert-Condition `
                (Test-Path -LiteralPath $requiredM204Path -PathType Leaf) `
                "Completed M2-04 is missing required source '$requiredM204Path'."
        }
        foreach ($requiredM204EvidenceText in @(
            "## M2-04 Extracted Mechanism Record",
            "SnapshotMaximumByteLength",
            "Seventeen generalized validator tests",
            "authoritative neutral validation implementation",
            'FullyQualifiedName~GameConfigSnapshotValidatorTests',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Database'
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM204EvidenceText) `
                "Completed M2-04 evidence is missing '$requiredM204EvidenceText'."
        }

        $m204NeutralSource = [string]::Join("`n", @(
            Get-Content -LiteralPath `
                "server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotValidator.cs" -Raw
            Get-Content -LiteralPath `
                "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigSnapshotValidatorTests.cs" -Raw
        ))
        foreach ($forbiddenM204Term in @(
            "SwordMaster",
            "PlayerAssets",
            "EquipmentDefinition",
            "equipment-items",
            "swordmaster.player-assets.v1"
        )) {
            Assert-Condition `
                (-not $m204NeutralSource.Contains($forbiddenM204Term)) `
                "Completed M2-04 neutral code contains game-specific term '$forbiddenM204Term'."
        }
    }

    if ($m2Goal.StageStatuses["M2-05"] -eq "complete") {
        foreach ($requiredM205EvidenceText in @(
            "## M2-05 Consumer Migration Record",
            'directly references `NovaGame.Domain`',
            'explicitly depends on `NovaGameDomainModule`',
            "No bypass or second",
            'FullyQualifiedName~GameConfigEquipmentDefinitionProviderTests',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster'
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM205EvidenceText) `
                "Completed M2-05 evidence is missing '$requiredM205EvidenceText'."
        }
    }

    if ($m2Goal.StageStatuses["M2-06"] -eq "complete") {
        foreach ($requiredM206EvidenceText in @(
            "## M2-06 Verification Record",
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Server',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Client',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository',
            "generalized validator 17",
            "TypeScript GameConfig client 37",
            ".NET counts 23/43/35/12/5",
            "TypeScript counts 12/12/7",
            "no duplicate authoritative validator",
            "disposable-resource audit"
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM206EvidenceText) `
                "Completed M2-06 evidence is missing '$requiredM206EvidenceText'."
        }
    }

    if ($m2Goal.StageStatuses["M2-07"] -eq "complete") {
        foreach ($requiredM207EvidenceText in @(
            "## M2-07 Contract Maturity Decision",
            'keep `GameConfigSnapshotValidator` and `ValidatedGameConfigSnapshot` experimental',
            "no source or binary compatibility promise",
            "second independent .NET game backend or external consumer",
            "versioned NovaGame Domain packages",
            "shared producer-generated golden corpus",
            "25,166,848-byte limit",
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Server',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Client',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster'
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM207EvidenceText) `
                "Completed M2-07 evidence is missing '$requiredM207EvidenceText'."
        }

        $experimentalContractSource = Get-Content -LiteralPath `
            "server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotValidator.cs" -Raw
        Assert-Condition `
            $experimentalContractSource.Contains("remains experimental after M2") `
            "Completed M2-07 must mark the extracted C# contract experimental after M2."
        $m207Architecture = Get-Content -LiteralPath "docs/architecture.md" -Raw
        Assert-Condition `
            $m207Architecture.Contains("Extraction validates ownership and reuse") `
            "Completed M2-07 architecture must distinguish extraction from stable promotion."
    }

    if ($m2Goal.StageStatuses["M2-08"] -eq "complete") {
        $m208FinalCommand =
            "pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository"
        $m208FinalCommandPattern = '(?m)^last_verification_command:[ \t]*' +
            [regex]::Escape($m208FinalCommand) + '[ \t]*$'
        Assert-Condition `
            ([regex]::IsMatch($m2Goal.Metadata, $m208FinalCommandPattern)) `
            "Completed M2-08 must use Repository verification as its final command."

        $m208LastVerificationAtMatch = [regex]::Match(
            $m2Goal.Metadata,
            '(?m)^last_verification_at:\s*(?<value>\d{4}-\d{2}-\d{2})\s*$')
        $m208UpdatedAtMatch = [regex]::Match(
            $m2Goal.Metadata,
            '(?m)^updated_at:\s*(?<value>\d{4}-\d{2}-\d{2})\s*$')
        Assert-Condition `
            ($m208LastVerificationAtMatch.Success -and
                $m208UpdatedAtMatch.Success -and
                $m208LastVerificationAtMatch.Groups["value"].Value -eq
                    $m208UpdatedAtMatch.Groups["value"].Value) `
            "Completed M2 metadata must be updated on its final verification date."

        $m208LastResultMatch = [regex]::Match(
            $m2Goal.Metadata,
            '(?m)^last_verification_result:\s*(?<value>[^\r\n]+?)\s*$')
        Assert-Condition `
            $m208LastResultMatch.Success `
            "Completed M2-08 must record its final verification result."
        $m208LastResult = $m208LastResultMatch.Groups["value"].Value
        foreach ($requiredM208ResultText in @(
            "M2-08",
            "complete",
            "deactivated",
            "experimental",
            "no repository milestone remains active"
        )) {
            Assert-Condition `
                $m208LastResult.Contains(
                    $requiredM208ResultText,
                    [StringComparison]::OrdinalIgnoreCase) `
                "Completed M2-08 result is missing '$requiredM208ResultText'."
        }

        $m208FrozenExtractionDeltaText =
            'frozen, authorized 29-path extraction-only delta'
        $m208FrozenExtractionDeltaScopeText =
            'This historical count describes M2 at closure and is not a current worktree total.'
        foreach ($requiredM208AcceptanceText in @(
            '## M2-08 Acceptance Record',
            'pwsh -NoProfile -File scripts/bootstrap.ps1',
            'pwsh -NoProfile -File scripts/verify.ps1',
            'GameConfig Domain 18',
            'snapshot validator 17',
            'SwordMaster provider 20',
            'TypeScript GameConfig client 37',
            '.NET counts 23/43/35/12/5',
            'TypeScript counts 12/12/7',
            $m208FrozenExtractionDeltaText,
            $m208FrozenExtractionDeltaScopeText,
            'pre-commit maintenance does not rewrite it.',
            'consumed its one-time authorization'
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredM208AcceptanceText) `
                "Completed M2-08 acceptance evidence is missing '$requiredM208AcceptanceText'."
        }

        foreach ($requiredPostM2MaintenanceText in @(
            '## Post-M2 Pre-Commit Maintenance Record',
            'Admin version-detail drawer race',
            '`EquipmentSlot` path-segment safety',
            'TypeScript snapshot-depth preflight behavior',
            'Admin focused component tests | Passed: 4 tests.',
            'SwordMaster Domain | Passed: 29 tests.',
            'SwordMaster Application | Passed: 49 tests.',
            'SwordMaster API | Passed: 35 tests.',
            'Client | Passed: 53 tests.',
            'SwordMaster transport | Passed: 12 tests.',
            'Scope SwordMaster` | Passed.',
            'Scope Admin`',
            'Scope Repository`'
        )) {
            Assert-Condition `
                $m2Goal.Content.Contains($requiredPostM2MaintenanceText) `
                "Post-M2 maintenance evidence is missing '$requiredPostM2MaintenanceText'."
        }

        $m208VerificationLogMatch = [regex]::Match(
            $m2Goal.Content,
            '(?ms)^## Verification Log\r?\n(?<body>.*)\z')
        Assert-Condition `
            $m208VerificationLogMatch.Success `
            "Completed M2-08 must retain its Verification Log."
        $m208Rows = [string]::Join("`n", @(
            [regex]::Matches(
                $m208VerificationLogMatch.Groups["body"].Value,
                '(?m)^\|[ \t]*\d{4}-\d{2}-\d{2}[ \t]*\|[ \t]*M2-08[ \t]*\|[^\r\n]*$') |
                ForEach-Object { $_.Value }
        ))
        foreach ($requiredM208Command in @(
            "pwsh -NoProfile -File scripts/bootstrap.ps1",
            "pwsh -NoProfile -File scripts/verify.ps1",
            "pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster",
            "pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository",
            "git diff --check"
        )) {
            $passedM208CommandPattern =
                '(?m)^\|[^|]+\|[ \t]*M2-08[ \t]*\|[^|]*\x60' +
                [regex]::Escape($requiredM208Command) +
                '\x60[^|]*\|[ \t]*Passed(?:[ :.-][^|\r\n]*)?[ \t]*\|[ \t]*$'
            Assert-Condition `
                ([regex]::IsMatch($m208Rows, $passedM208CommandPattern)) `
                "Completed M2-08 has no Passed row for '$requiredM208Command'."
        }
        foreach ($requiredM208LogText in @(
            "exact counts 18/17/20/37",
            ".NET counts 23/43/35/12/5",
            "TypeScript counts 12/12/7",
            "game-config-snapshot-consumer-validation",
            "experimental",
            "no disposable resource or process leaked"
        )) {
            Assert-Condition `
                $m208Rows.Contains($requiredM208LogText) `
                "Completed M2-08 verification log is missing '$requiredM208LogText'."
        }

        $m208CurrentStateSentence =
            "M2 is complete and no repository milestone is active."
        $staleM208Phrases = @(
            "M2 is active",
            'active at `M2-08`',
            "only final repository acceptance is now authorized",
            "final repository acceptance is the only active stage",
            "M2-08 now performs final repository acceptance"
        )
        foreach ($currentStatusPath in @(
            "AGENTS.md",
            "README.md",
            "docs/README.md",
            "docs/roadmap.md",
            "samples/SwordMaster/README.md"
        )) {
            $currentStatusContent = Get-Content -LiteralPath $currentStatusPath -Raw
            foreach ($requiredCurrentStatusText in @(
                $m208CurrentStateSentence,
                "game-config-snapshot-consumer-validation",
                "experimental",
                "grants no continuing authorization"
            )) {
                Assert-Condition `
                    $currentStatusContent.Contains(
                        $requiredCurrentStatusText,
                        [StringComparison]::OrdinalIgnoreCase) `
                    "$currentStatusPath is missing completed M2 state '$requiredCurrentStatusText'."
            }
            foreach ($staleM208Phrase in $staleM208Phrases) {
                Assert-Condition `
                    (-not $currentStatusContent.Contains(
                        $staleM208Phrase,
                        [StringComparison]::OrdinalIgnoreCase)) `
                    "$currentStatusPath contains stale M2 state '$staleM208Phrase'."
            }
        }

        foreach ($unscheduledMilestone in @("M3", "M4")) {
            $unscheduledSectionPattern = '(?ms)^##\s+' +
                [regex]::Escape($unscheduledMilestone) +
                '\s+-[^\r\n]*\r?\n(?<body>.*?)(?=^##\s+|\z)'
            $unscheduledSectionMatch = [regex]::Match(
                $roadmap,
                $unscheduledSectionPattern)
            Assert-Condition `
                ($unscheduledSectionMatch.Success -and
                    [regex]::IsMatch(
                        $unscheduledSectionMatch.Groups["body"].Value,
                        '(?m)^\*\*Status:\*\*[ \t]*Unscheduled[ \t]*$')) `
                "$unscheduledMilestone must remain Unscheduled after M2 completes."
        }
    }

    $m2EvidenceResolved = $m2Goal.Status -in @("ready", "complete") -or
        ($m2Goal.ExecutionAuthorized -and $m2Goal.ActiveStage -ne "M2-01")
    if ($m2EvidenceResolved) {
        Assert-Condition `
            (-not [regex]::IsMatch(
                $m2EvidenceRecord,
                '(?m)^\|[^|\r\n]+\|[ \t]*Not (?:selected|recorded)[ \t]*\|[ \t]*$')) `
            "M2 Extraction Evidence Record must be resolved after M2-01."
    }

    $architecture = Get-Content -LiteralPath "docs/architecture.md" -Raw
    Assert-Condition $architecture.Contains("MySQL 8.4") "Architecture must declare MySQL 8.4."
    Assert-Condition $architecture.Contains("web-ele") "Architecture must declare Vben web-ele."
    Assert-Condition (-not $architecture.Contains("web-antd admin is")) "Architecture contains a conflicting web-antd decision."
    foreach ($requiredArchitectureHeading in @(
        "## Specific Game Backend Composition",
        "### NovaGame-Owned Baseline",
        "### Candidate Common Mechanisms",
        "### Specific Game-Owned By Default"
    )) {
        Assert-Condition `
            $architecture.Contains($requiredArchitectureHeading) `
            "Architecture is missing required ownership heading '$requiredArchitectureHeading'."
    }
    Assert-Condition `
        (-not [regex]::IsMatch(
            $architecture,
            '(?m)^##[ \t]+Business Domain Catalog[ \t]*$')) `
        "Architecture must not restore the obsolete Business Domain Catalog."
    $novaGameBaselineMatch = [regex]::Match(
        $architecture,
        '(?ms)^### NovaGame-Owned Baseline\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
    Assert-Condition `
        $novaGameBaselineMatch.Success `
        "Architecture must define the NovaGame-owned baseline section."
    $novaGameBaseline = $novaGameBaselineMatch.Groups["body"].Value
    foreach ($requiredBaselineCapability in @("AdminAccess", "GameConfig")) {
        Assert-Condition `
            $novaGameBaseline.Contains($requiredBaselineCapability) `
            "NovaGame-owned baseline must include '$requiredBaselineCapability'."
    }
    foreach ($gameOwnedSystem in @(
        "Battle",
        "Skills",
        "Drops",
        "Shop",
        "Enhancement",
        "Set Bonuses"
    )) {
        Assert-Condition `
            (-not [regex]::IsMatch(
                $novaGameBaseline,
                '(?i)(?<![A-Za-z0-9])' +
                    [regex]::Escape($gameOwnedSystem) +
                    '(?![A-Za-z0-9])')) `
            "Game-owned system '$gameOwnedSystem' must not appear in the NovaGame-owned baseline."
    }

    $m05Goal = $goalRecords["M0.5"].Content
    Assert-Condition `
        ([regex]::Matches(
            $m05Goal,
            "^## M05-\d{2} - ",
            [System.Text.RegularExpressions.RegexOptions]::Multiline).Count -eq 10) `
        "M0.5 goal must define stages M05-00 through M05-09."

    foreach ($futureGoal in @($m1Goal, $m2Goal)) {
        foreach ($requiredSection in @("## Activation Gate", "## Preconditions")) {
            Assert-Condition `
                $futureGoal.Content.Contains($requiredSection) `
                "Future goal is missing required section '$requiredSection'."
        }
        Assert-Condition `
            ([regex]::Matches(
                $futureGoal.Content,
                '^### Verification$',
                [System.Text.RegularExpressions.RegexOptions]::Multiline).Count -eq 8) `
            "Each M1/M2 stage must define a verification section."
    }

    Write-Output "Documentation verification passed."
}

function Test-Repository {
    Test-Documentation

    Assert-Condition (Test-Path -LiteralPath ".git" -PathType Container) "Git metadata directory '.git' is missing."
    Invoke-CheckedCommand "git" @("rev-parse", "--is-inside-work-tree")
    Assert-Condition (Test-Path -LiteralPath "NovaGame.sln" -PathType Leaf) "Root solution 'NovaGame.sln' is missing."

    foreach ($script in @(
        "scripts/bootstrap.ps1",
        "scripts/verify.ps1",
        "scripts/run-dev.ps1",
        "scripts/process-environment.ps1",
        "scripts/database-environment.ps1",
        "scripts/admin-access-environment.ps1",
        "scripts/update-swordmaster-player-assets-contract.ps1"
    )) {
        Assert-Condition (Test-Path -LiteralPath $script -PathType Leaf) "Missing repository script '$script'."
    }

    foreach ($requiredSourceFile in @(
        "admin/scripts/vsh/bin/vsh.mjs",
        "admin/scripts/turbo-run/bin/turbo-run.mjs"
    )) {
        $committableSourceFile = @(
            git ls-files --cached --others --exclude-standard -- $requiredSourceFile
        )
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect committable source file '$requiredSourceFile' through Git."
        }
        Assert-Condition `
            ($committableSourceFile -contains $requiredSourceFile) `
            "Required Vben workspace CLI source '$requiredSourceFile' is excluded by Git ignore rules."
    }

    $bootstrapScript = Get-Content -LiteralPath "scripts/bootstrap.ps1" -Raw
    $verifyScript = Get-Content -LiteralPath "scripts/verify.ps1" -Raw
    $processEnvironmentScript = Get-Content -LiteralPath `
        "scripts/process-environment.ps1" -Raw
    $databaseEnvironmentScript = Get-Content -LiteralPath `
        "scripts/database-environment.ps1" -Raw
    $adminAccessEnvironmentScript = Get-Content -LiteralPath `
        "scripts/admin-access-environment.ps1" -Raw
    Assert-Condition `
        $bootstrapScript.Contains('[string]$Scope = "All"') `
        "The root bootstrap command must default to the cumulative All scope."
    Assert-Condition `
        $verifyScript.Contains('[string]$Scope = "All"') `
        "The root verification command must default to the cumulative All scope."
    Assert-Condition `
        ([regex]::IsMatch(
            $bootstrapScript,
            '(?ms)else\s*\{\s*Join-Path \$localRoot "nuget"\s*\}\s*\$env:NUGET_PACKAGES = Join-Path \$nugetPackagesBase \$novaCoreCommit')) `
        "Bootstrap and verification must share the repository-local commit-keyed NuGet cache."
    Assert-Condition `
        $bootstrapScript.Contains('Clear-NovaCorePackageCache') `
        "Bootstrap must invalidate repository-local NovaCore package entries after publishing the locked feed."
    Assert-Condition `
        ([regex]::Matches(
                $verifyScript,
                '(?m)^\s*"--force-evaluate",$').Count -eq 2) `
        "NovaGame and SwordMaster restores must reevaluate same-version locked NovaCore packages."
    Assert-Condition `
        $processEnvironmentScript.Contains('[Environment]::GetEnvironmentVariables("Process")') `
        "Process environment snapshots must distinguish missing keys from empty values."
    Assert-Condition `
        $processEnvironmentScript.Contains(
            '[System.Management.Automation.Language.NullString]::Value') `
        "Process environment restoration must explicitly remove originally missing keys."
    foreach ($environmentScript in @(
        $databaseEnvironmentScript,
        $adminAccessEnvironmentScript
    )) {
        Assert-Condition `
            $environmentScript.Contains('Get-NovaGameProcessEnvironmentSnapshot') `
            "Environment helpers must use null-preserving process snapshots."
        Assert-Condition `
            $environmentScript.Contains('Restore-NovaGameProcessEnvironmentSnapshot') `
            "Environment helpers must use null-preserving process restoration."
    }
    Assert-Condition `
        ([regex]::Matches(
            $verifyScript,
            '(?m)^\s*\$previous(?:Seed|Runtime)?Environment\s*=\s*Get-NovaGameProcessEnvironmentSnapshot\s*`?$').Count -eq 4) `
        "Verification must use null-preserving snapshots in all four local lifecycles."
    Assert-Condition `
        ([regex]::Matches(
            $verifyScript,
            '(?m)^\s*Restore-NovaGameProcessEnvironmentSnapshot(?:\s|$)').Count -eq 4) `
        "Verification must use null-preserving restoration in all four local lifecycles."

    $readme = Get-Content -LiteralPath "README.md" -Raw
    foreach ($requiredCommand in @(
        "pwsh -NoProfile -File scripts/bootstrap.ps1",
        "pwsh -NoProfile -File scripts/verify.ps1",
        "pwsh -NoProfile -File scripts/run-dev.ps1 -Scope Migrations",
        "pwsh -NoProfile -File scripts/run-dev.ps1 -Scope All"
    )) {
        Assert-Condition `
            $readme.Contains($requiredCommand) `
            "README is missing supported command '$requiredCommand'."
    }

    Invoke-CheckedCommand "dotnet" @("sln", "NovaGame.sln", "list")
    Test-SwordMasterServerArchitecture
    Write-Output "Repository verification passed."
}

function Assert-StringSet {
    param(
        [string[]]$Expected = @(),
        [string[]]$Actual = @(),
        [Parameter(Mandatory)][string]$Message
    )

    $expectedText = (@($Expected) | Sort-Object) -join "`n"
    $actualText = (@($Actual) | Sort-Object) -join "`n"
    if ($expectedText -ne $actualText) {
        throw "$Message`nExpected:`n$expectedText`nActual:`n$actualText"
    }
}

function Get-PackageDependencyNames {
    param([Parameter(Mandatory)]$Package)

    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($sectionName in @(
        "dependencies",
        "devDependencies",
        "optionalDependencies",
        "peerDependencies"
    )) {
        $section = $Package.PSObject.Properties[$sectionName]
        if ($null -eq $section -or $null -eq $section.Value) {
            continue
        }

        foreach ($dependency in $section.Value.PSObject.Properties) {
            $null = $names.Add([string]$dependency.Name)
        }
    }

    return @($names | Sort-Object -Unique)
}

function Get-SolutionProjectPaths {
    param([Parameter(Mandatory)][string]$SolutionPath)

    $content = Get-Content -LiteralPath $SolutionPath -Raw
    return @(
        [regex]::Matches($content, 'Project\("\{[^"\r\n]+\}"\) = "[^"]+", "([^"]+\.csproj)"') |
            ForEach-Object { $_.Groups[1].Value.Replace("\", "/") }
    )
}

function Get-ProjectReferenceNames {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    return @(
        $project.SelectNodes("//ProjectReference") |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension([string]$_.Include) }
    )
}

function Get-ResolvedProjectReferencePaths {
    param([Parameter(Mandatory)][string]$ProjectPath)

    $projectFullPath = if ([System.IO.Path]::IsPathRooted($ProjectPath)) {
        [System.IO.Path]::GetFullPath($ProjectPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ProjectPath))
    }
    [xml]$project = Get-Content -LiteralPath $projectFullPath -Raw
    $projectDirectory = Split-Path -Parent $projectFullPath
    $resolvedPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($projectReference in @($project.SelectNodes("//ProjectReference"))) {
        $include = [string]$projectReference.Include
        $include = $include.Replace('$(NovaGameRepositoryRoot)', $repositoryRoot)
        Assert-Condition `
            (-not $include.Contains('$(', [StringComparison]::Ordinal)) `
            "Project '$ProjectPath' contains an unsupported property in ProjectReference '$($projectReference.Include)'."

        $referenceFullPath = if ([System.IO.Path]::IsPathRooted($include)) {
            [System.IO.Path]::GetFullPath($include)
        }
        else {
            [System.IO.Path]::GetFullPath((Join-Path $projectDirectory $include))
        }
        $repositoryRelativePath = [System.IO.Path]::GetRelativePath(
            $repositoryRoot,
            $referenceFullPath).Replace("\", "/")
        Assert-Condition `
            ($repositoryRelativePath -ne ".." -and
                -not $repositoryRelativePath.StartsWith("../", [StringComparison]::Ordinal)) `
            "Project '$ProjectPath' references a project outside the repository: '$($projectReference.Include)'."
        $resolvedPaths.Add($repositoryRelativePath)
    }

    return @($resolvedPaths)
}

function Get-PackageReferenceNames {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    return @(
        $project.SelectNodes("//PackageReference") |
            ForEach-Object { [string]$_.Include }
    )
}

function Test-NovaCorePackageSourceConfiguration {
    param(
        [Parameter(Mandatory)][string]$NuGetConfigPath,
        [Parameter(Mandatory)][string]$ExpectedRelativeSource,
        [Parameter(Mandatory)][string]$Description
    )

    $configFullPath = if ([System.IO.Path]::IsPathRooted($NuGetConfigPath)) {
        [System.IO.Path]::GetFullPath($NuGetConfigPath)
    }
    else {
        [System.IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $NuGetConfigPath))
    }
    [xml]$nugetConfig = Get-Content -LiteralPath $configFullPath -Raw
    Assert-Condition `
        (@($nugetConfig.configuration.packageSources.clear).Count -eq 1) `
        "$Description NuGet.Config must clear inherited package sources."
    $novaSources = @(
        @($nugetConfig.configuration.packageSources.add) |
            Where-Object { $_.key -eq "nova-local" }
    )
    Assert-Condition `
        ($novaSources.Count -eq 1) `
        "$Description NuGet.Config must define exactly one 'nova-local' package source."

    $novaSourcePath = [string]$novaSources[0].value
    Assert-Condition `
        (-not [System.IO.Path]::IsPathRooted($novaSourcePath)) `
        "$Description NovaCore package source must be repository-relative."
    Assert-Condition `
        ($novaSourcePath.Replace("\", "/") -eq
            $ExpectedRelativeSource.Replace("\", "/")) `
        "$Description NovaCore package source must be '$ExpectedRelativeSource'."

    $resolvedNovaSourcePath = [System.IO.Path]::GetFullPath(
        (Join-Path (Split-Path -Parent $configFullPath) $novaSourcePath))
    $expectedNovaSourcePath = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot ".novacore/packages"))
    Assert-Condition `
        ($resolvedNovaSourcePath -eq $expectedNovaSourcePath) `
        "$Description NovaCore package source must resolve to the repository-local '.novacore/packages' feed."

    $novaMappings = @(
        @($nugetConfig.configuration.packageSourceMapping.packageSource) |
            Where-Object { $_.key -eq "nova-local" }
    )
    Assert-Condition `
        ($novaMappings.Count -eq 1) `
        "$Description NuGet.Config must define exactly one 'nova-local' package-source mapping."
    $novaPatterns = @(
        @($novaMappings[0].package) |
            ForEach-Object { [string]$_.pattern }
    )
    Assert-StringSet `
        -Expected @("NovaCore.*") `
        -Actual $novaPatterns `
        -Message "$Description 'nova-local' package source must map exactly the 'NovaCore.*' package pattern."
}

function Get-VerifiedNovaCoreLock {
    $lockPath = Join-Path $repositoryRoot "server\novacore.lock.json"
    $provenancePath = Join-Path $repositoryRoot ".novacore\provenance.json"
    Assert-Condition `
        (Test-Path -LiteralPath $lockPath -PathType Leaf) `
        "NovaCore lock file is missing."
    Assert-Condition `
        (Test-Path -LiteralPath $provenancePath -PathType Leaf) `
        "NovaCore provenance is missing. Run scripts/bootstrap.ps1 -Scope Server."

    $novaCoreLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    $expectedPackageNames = @(
        $novaCoreLock.requiredPackages | ForEach-Object { [string]$_ })
    $provenancePackageNames = @(
        $provenance.packages | ForEach-Object { [string]$_.name })
    Assert-StringSet `
        $expectedPackageNames `
        $provenancePackageNames `
        "NovaCore provenance package set does not match the lock."
    Assert-Condition `
        ([string]$provenance.lock.version -eq [string]$novaCoreLock.version) `
        "NovaCore provenance version does not match the lock."
    Assert-Condition `
        ([string]$provenance.lock.commit -eq [string]$novaCoreLock.commit) `
        "NovaCore provenance commit does not match the lock."
    Assert-Condition `
        ([string]$provenance.source.resolvedCommit -eq
            [string]$novaCoreLock.commit) `
        "NovaCore source snapshot does not match the locked commit."
    Assert-Condition `
        ([string]$provenance.lock.sha256 -eq
            (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash.ToLowerInvariant()) `
        "NovaCore lock hash does not match provenance."

    $canonicalPackageSource = Join-Path `
        $repositoryRoot `
        ".novacore\feeds\$($novaCoreLock.commit)"
    $compatibilityPackageSource = Join-Path $repositoryRoot ".novacore\packages"
    Assert-Condition `
        (Test-Path -LiteralPath $canonicalPackageSource -PathType Container) `
        "Canonical NovaCore feed is missing."
    Assert-Condition `
        (Test-Path -LiteralPath $compatibilityPackageSource -PathType Container) `
        "Compatibility NovaCore feed is missing."
    Assert-Condition `
        ([System.IO.Path]::GetFullPath([string]$provenance.feed.root) -eq
            [System.IO.Path]::GetFullPath($canonicalPackageSource)) `
        "NovaCore provenance points at the wrong canonical feed."
    Assert-Condition `
        ([System.IO.Path]::GetFullPath(
                [string]$provenance.feed.compatibilityRoot) -eq
            [System.IO.Path]::GetFullPath($compatibilityPackageSource)) `
        "NovaCore provenance points at the wrong compatibility feed."

    $expectedPackageFiles = @(
        $expectedPackageNames |
            ForEach-Object { "$($_).$($novaCoreLock.version).nupkg" })
    Assert-StringSet `
        $expectedPackageFiles `
        @(Get-ChildItem `
            -LiteralPath $canonicalPackageSource `
            -Filter "*.nupkg" `
            -File | ForEach-Object Name) `
        "Canonical NovaCore feed contains an unexpected package set."
    Assert-StringSet `
        $expectedPackageFiles `
        @(Get-ChildItem `
            -LiteralPath $compatibilityPackageSource `
            -Filter "*.nupkg" `
            -File | ForEach-Object Name) `
        "Compatibility NovaCore feed contains an unexpected package set."

    foreach ($packageRecord in @($provenance.packages)) {
        Assert-Condition `
            ([string]$packageRecord.version -eq [string]$novaCoreLock.version) `
            "NovaCore package '$($packageRecord.name)' has the wrong provenance version."
        foreach ($feed in @(
            $canonicalPackageSource,
            $compatibilityPackageSource
        )) {
            $packagePath = Join-Path $feed ([string]$packageRecord.file)
            Assert-Condition `
                (Test-Path -LiteralPath $packagePath -PathType Leaf) `
                "NovaCore package '$packagePath' is missing."
            Assert-Condition `
                ((Get-FileHash `
                    -LiteralPath $packagePath `
                    -Algorithm SHA256).Hash.ToLowerInvariant() -eq
                    [string]$packageRecord.sha256) `
                "NovaCore package hash mismatch for '$packagePath'."
        }
    }

    return $novaCoreLock
}

function Test-NovaCoreProjectPackageReferences {
    param(
        [Parameter(Mandatory)][string[]]$ProjectPaths,
        [Parameter(Mandatory)]$NovaCoreLock
    )

    $allowedPackageNames = @(
        $NovaCoreLock.requiredPackages | ForEach-Object { [string]$_ })
    foreach ($projectPath in $ProjectPaths) {
        [xml]$project = Get-Content -LiteralPath $projectPath -Raw
        foreach ($packageReference in @(
            $project.SelectNodes("//PackageReference") |
                Where-Object {
                    ([string]$_.Include).StartsWith(
                        "NovaCore.",
                        [StringComparison]::Ordinal)
                }
        )) {
            $packageName = [string]$packageReference.Include
            Assert-Condition `
                ($allowedPackageNames -contains $packageName) `
                "Project '$projectPath' references NovaCore package '$packageName' outside the lock."
            Assert-Condition `
                ([string]$packageReference.Version -eq
                    [string]$NovaCoreLock.version) `
                "Project '$projectPath' must reference NovaCore $($NovaCoreLock.version), not '$($packageReference.Version)'."
        }
    }
}

function Test-ServerArchitecture {
    $serverRoot = Join-Path $repositoryRoot "server"
    $sourceRoot = Join-Path $serverRoot "src"
    $expectedProjects = @(
        "NovaGame.Domain.Shared/NovaGame.Domain.Shared.csproj",
        "NovaGame.Domain/NovaGame.Domain.csproj",
        "NovaGame.Application.Contracts/NovaGame.Application.Contracts.csproj",
        "NovaGame.Application/NovaGame.Application.csproj",
        "NovaGame.Host.EntityFrameworkCore/NovaGame.Host.EntityFrameworkCore.csproj",
        "NovaGame.Deployment/NovaGame.Deployment.csproj",
        "NovaGame.DbMigrator/NovaGame.DbMigrator.csproj",
        "NovaGame.Api/NovaGame.Api.csproj"
    )
    $expectedTestProjects = @(
        "NovaGame.EntityFrameworkCore.Tests/NovaGame.EntityFrameworkCore.Tests.csproj"
    )

    Assert-Condition (Test-Path -LiteralPath "server/NovaGame.sln" -PathType Leaf) "Server solution 'server/NovaGame.sln' is missing."
    Assert-Condition (Test-Path -LiteralPath "server/NuGet.Config" -PathType Leaf) "Server NuGet configuration is missing."
    Assert-Condition (Test-Path -LiteralPath "server/.config/dotnet-tools.json" -PathType Leaf) "Server .NET tool manifest is missing."
    Assert-Condition (-not (Test-Path -LiteralPath "server/src/Modules")) "Physical module scaffolding under 'server/src/Modules' is forbidden in M0.5."

    $actualProjects = @(
        Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter "*.csproj" |
            ForEach-Object { [System.IO.Path]::GetRelativePath($sourceRoot, $_.FullName).Replace("\", "/") }
    )
    Assert-StringSet $expectedProjects $actualProjects "The server must contain the six business-layer projects plus Deployment and DbMigrator."
    $expectedServerSolutionProjects = @(
        $expectedProjects | ForEach-Object { "src/$_" }
        $expectedTestProjects | ForEach-Object { "tests/$_" }
    )
    $expectedRootSolutionProjects = @(
        $expectedProjects | ForEach-Object { "server/src/$_" }
        $expectedTestProjects | ForEach-Object { "server/tests/$_" }
    )
    Assert-StringSet $expectedServerSolutionProjects (Get-SolutionProjectPaths "server/NovaGame.sln") "The server solution project set is incorrect."
    Assert-StringSet $expectedRootSolutionProjects (Get-SolutionProjectPaths "NovaGame.sln") "The root solution project set is incorrect."

    $expectedReferences = [ordered]@{
        "NovaGame.Domain.Shared" = @()
        "NovaGame.Domain" = @("NovaGame.Domain.Shared")
        "NovaGame.Application.Contracts" = @("NovaGame.Domain.Shared")
        "NovaGame.Application" = @("NovaGame.Application.Contracts", "NovaGame.Domain")
        "NovaGame.Host.EntityFrameworkCore" = @("NovaGame.Domain")
        "NovaGame.Deployment" = @()
        "NovaGame.DbMigrator" = @("NovaGame.Application", "NovaGame.Deployment", "NovaGame.Host.EntityFrameworkCore")
        "NovaGame.Api" = @("NovaGame.Application", "NovaGame.Deployment", "NovaGame.Host.EntityFrameworkCore")
    }

    foreach ($projectName in $expectedReferences.Keys) {
        $projectPath = Join-Path $sourceRoot "$projectName\$projectName.csproj"
        Assert-StringSet $expectedReferences[$projectName] (Get-ProjectReferenceNames $projectPath) "Invalid project references for '$projectName'."
    }

    foreach ($projectName in @(
        "NovaGame.Domain.Shared",
        "NovaGame.Domain",
        "NovaGame.Application.Contracts",
        "NovaGame.Application"
    )) {
        $projectPath = Join-Path $sourceRoot "$projectName\$projectName.csproj"
        $forbiddenPackages = @(Get-PackageReferenceNames $projectPath | Where-Object {
            $_ -match "EntityFrameworkCore|AspNetCore|Pomelo|MySql"
        })
        Assert-Condition ($forbiddenPackages.Count -eq 0) "Inner-layer project '$projectName' references infrastructure package(s): $($forbiddenPackages -join ', ')."
    }

    $forbiddenTopLevelFolders = @("Entities", "Repositories", "Services", "Dtos", "Common", "Helpers", "Utils")
    foreach ($projectDirectory in Get-ChildItem -LiteralPath $sourceRoot -Directory) {
        foreach ($file in Get-ChildItem -LiteralPath $projectDirectory.FullName -Recurse -File) {
            $relativePath = [System.IO.Path]::GetRelativePath($projectDirectory.FullName, $file.FullName)
            $topLevelFolder = ($relativePath -split "[\\/]")[0]
            Assert-Condition ($topLevelFolder -notin $forbiddenTopLevelFolders) "Entity-first or catch-all layout is forbidden: $($file.FullName)."
        }
    }

    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter "*.cs") {
        $content = Get-Content -LiteralPath $sourceFile.FullName -Raw
        Assert-Condition ($content -notmatch "(?m)^(namespace|using) NovaCore\.(Api|Application|Domain|Host)(\.|;)") "Application-owned code must use the NovaGame namespace: $($sourceFile.FullName)."
    }

    Test-NovaCorePackageSourceConfiguration `
        -NuGetConfigPath "server/NuGet.Config" `
        -ExpectedRelativeSource "../.novacore/packages" `
        -Description "Server"
    $novaCoreLock = Get-VerifiedNovaCoreLock
    Test-NovaCoreProjectPackageReferences `
        -ProjectPaths @(
            Get-ChildItem `
                -LiteralPath $sourceRoot `
                -Recurse `
                -File `
                -Filter "*.csproj" |
                ForEach-Object FullName
        ) `
        -NovaCoreLock $novaCoreLock

    Write-Output "Server architecture verification passed."
}

function Test-NovaCoreResolvedPackages {
    param([string[]]$SearchRoots = @("server/src"))

    $novaCoreLock = Get-Content -LiteralPath "server/novacore.lock.json" -Raw | ConvertFrom-Json
    $expectedPackageHashes = @{}
    foreach ($packageName in @(
        $novaCoreLock.requiredPackages | ForEach-Object { [string]$_ }
    )) {
        $packagePath = Join-Path `
            $repositoryRoot `
            ".novacore\packages\$packageName.$($novaCoreLock.version).nupkg"
        Assert-Condition `
            (Test-Path -LiteralPath $packagePath -PathType Leaf) `
            "Locked NovaCore package '$packagePath' is missing."
        $packageBytes = [System.IO.File]::ReadAllBytes($packagePath)
        $expectedPackageHashes[$packageName] = [Convert]::ToBase64String(
            [System.Security.Cryptography.SHA512]::HashData($packageBytes))
    }

    $assetsFiles = @(
        foreach ($searchRoot in $SearchRoots) {
            Get-ChildItem `
                -LiteralPath $searchRoot `
                -Recurse `
                -File `
                -Filter "project.assets.json" |
                Where-Object {
                    $_.FullName -match "[\\/]obj[\\/]project\.assets\.json$"
                }
        }
    )
    Assert-Condition `
        ($assetsFiles.Count -gt 0) `
        "No restored project.assets.json files were found under: $($SearchRoots -join ', ')."

    $resolvedNovaCorePackageCount = 0
    foreach ($assetsFile in $assetsFiles) {
        $assets = Get-Content -LiteralPath $assetsFile.FullName -Raw | ConvertFrom-Json
        foreach ($library in @($assets.libraries.PSObject.Properties | Where-Object {
                    $_.Name.StartsWith("NovaCore.", [StringComparison]::Ordinal)
                })) {
            $resolvedNovaCorePackageCount++
            $packageIdentity = $library.Name -split "/", 2
            $resolvedPackageName = $packageIdentity[0]
            $resolvedVersion = $packageIdentity[1]
            Assert-Condition `
                $expectedPackageHashes.ContainsKey($resolvedPackageName) `
                "Resolved NovaCore dependency '$($library.Name)' is not present in the lock."
            Assert-Condition (
                $resolvedVersion -eq [string]$novaCoreLock.version) `
                "Resolved NovaCore dependency '$($library.Name)' in '$($assetsFile.FullName)' does not match the lock."
            Assert-Condition `
                ([string]$library.Value.sha512 -eq
                    [string]$expectedPackageHashes[$resolvedPackageName]) `
                "Resolved NovaCore dependency '$($library.Name)' in '$($assetsFile.FullName)' does not match the package built from locked commit '$($novaCoreLock.commit)'. Remove the stale package cache or unset NUGET_PACKAGES and restore again."
        }
    }
    Assert-Condition `
        ($resolvedNovaCorePackageCount -gt 0) `
        "No restored NovaCore dependencies were found under: $($SearchRoots -join ', ')."
}

function Test-Server {
    Test-Repository
    Test-ServerArchitecture

    Invoke-CheckedCommand "dotnet" @(
        "restore",
        "server/NovaGame.sln",
        "--configfile", "server/NuGet.Config",
        "--force-evaluate",
        "--verbosity", "minimal",
        "-p:NuGetAudit=false"
    )
    Test-NovaCoreResolvedPackages
    Invoke-CheckedCommand "dotnet" @(
        "build",
        "server/NovaGame.sln",
        "--no-restore",
        "--disable-build-servers",
        "--verbosity", "minimal",
        "-warnaserror",
        "-p:NuGetAudit=false"
    )
    Invoke-CheckedCommand "dotnet" @(
        "build",
        "NovaGame.sln",
        "--no-restore",
        "--disable-build-servers",
        "--verbosity", "minimal",
        "-warnaserror",
        "-p:NuGetAudit=false"
    )
    Invoke-CheckedCommand "dotnet" @(
        "format",
        "server/NovaGame.sln",
        "--no-restore",
        "--verify-no-changes",
        "--verbosity", "minimal"
    )
    Invoke-CheckedCommand "dotnet" @("sln", "NovaGame.sln", "list")

    Write-Output "Server verification passed."
}

function Test-NoActiveSqliteRuntime {
    $runtimeExtensions = @(".cs", ".csproj", ".json", ".props", ".ps1", ".targets", ".yaml", ".yml")
    $forbiddenPatterns = @(
        "Microsoft\.EntityFrameworkCore\.Sqlite",
        "Microsoft\.Data\.Sqlite",
        "UseSqlite\s*\(",
        'Data Source\s*=.*\.db(?:;|$)'
    )
    $violations = [System.Collections.Generic.List[string]]::new()

    $runtimeFiles = @(
        Get-ChildItem -LiteralPath "server/src", "server/tests", "deploy" -Recurse -File
        Get-Item -LiteralPath `
            "scripts/bootstrap.ps1", `
            "scripts/run-dev.ps1", `
            "scripts/database-environment.ps1", `
            "scripts/admin-access-environment.ps1"
    )
    foreach ($file in $runtimeFiles | Where-Object {
            $_.Extension -in $runtimeExtensions -and $_.FullName -notmatch "[\\/](bin|obj)[\\/]"
        }) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($pattern in $forbiddenPatterns) {
            if ($content -match $pattern) {
                $violations.Add("$($file.FullName): $pattern")
            }
        }
    }

    Assert-Condition ($violations.Count -eq 0) "Active SQLite runtime references found:`n$($violations -join "`n")"
}

function Test-Database {
    param([switch]$RunDbMigratorSeed)

    Test-Server
    Test-NoActiveSqliteRuntime

    Assert-Condition (Test-Path -LiteralPath "deploy/docker-compose.dev.yml" -PathType Leaf) "MySQL Compose file is missing."
    Assert-Condition (Test-Path -LiteralPath "deploy/.env.example" -PathType Leaf) "Database environment example is missing."

    $composeContent = Get-Content -LiteralPath "deploy/docker-compose.dev.yml" -Raw
    Assert-Condition ($composeContent -match "(?m)^\s*image:\s*mysql:8\.4\s*$") "Compose must use the mysql:8.4 image."
    Assert-Condition $composeContent.Contains("utf8mb4") "Compose must configure utf8mb4."
    Assert-Condition $composeContent.Contains("--default-time-zone=+00:00") "Compose must configure UTC."
    Assert-Condition $composeContent.Contains("healthcheck:") "Compose must define a MySQL health check."

    $providerProject = Get-Content -LiteralPath `
        "server/src/NovaGame.Host.EntityFrameworkCore/NovaGame.Host.EntityFrameworkCore.csproj" -Raw
    Assert-Condition $providerProject.Contains('Include="Pomelo.EntityFrameworkCore.MySql"') "Pomelo provider reference is missing."
    $providerConfiguration = Get-Content -LiteralPath `
        "server/src/NovaGame.Host.EntityFrameworkCore/DependencyInjection/NovaGameEntityFrameworkCoreServiceCollectionExtensions.cs" -Raw
    Assert-Condition (-not $providerConfiguration.Contains("EnableRetryOnFailure")) "Provider retries must remain disabled while NovaCore owns the explicit transaction boundary."
    $apiSettings = Get-Content -LiteralPath "server/src/NovaGame.Api/appsettings.json" -Raw
    Assert-Condition $apiSettings.Contains('"ServerVersion": "8.4.0"') "API settings must pin MySQL 8.4.0."
    Assert-Condition $apiSettings.Contains('"Default": ""') "Committed API settings must not contain a database connection string."
    Assert-Condition (Test-Path -LiteralPath `
        "server/src/NovaGame.Host.EntityFrameworkCore/Migrations/20260723160000_InitialMySqlBaseline.cs" `
        -PathType Leaf) "Initial MySQL migration is missing."
    Assert-Condition (Test-Path -LiteralPath `
        "server/src/NovaGame.Host.EntityFrameworkCore/Migrations/20260725101129_AddAdministratorAccess.cs" `
        -PathType Leaf) "Administrator access migration is missing."
    Assert-Condition $providerConfiguration.Contains("AddNovaCoreDbContext<NovaGameDbContext>") "NovaCore DbContext registration is missing."
    Assert-Condition (-not (Test-Path -LiteralPath "server/src/NovaGame.Api/HealthChecks/MySqlDatabaseHealthCheck.cs")) "The duplicate custom MySQL health check must not exist."

    if (Test-Path -LiteralPath "deploy/.env" -PathType Leaf) {
        & git check-ignore --quiet -- "deploy/.env"
        Assert-Condition ($LASTEXITCODE -eq 0) "deploy/.env exists but is not ignored by Git."
    }

    Invoke-CheckedCommand "docker" @("compose", "version")
    Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")

    $projectName = "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $testRoot = Join-Path $repositoryRoot ".local\database-tests"
    $testEnvironmentFile = Join-Path $testRoot "$projectName.env"
    Write-NovaGameDatabaseEnvironmentFile `
        -Path $testEnvironmentFile `
        -Database "novagame_test" `
        -User "novagame_test" `
        -Port 0 `
        -AllowEphemeralPort
    $testEnvironment = Read-NovaGameDatabaseEnvironment `
        -Path $testEnvironmentFile `
        -AllowEphemeralPort
    $composeArguments = Get-NovaGameComposeArguments `
        -RepositoryRoot $repositoryRoot `
        -EnvironmentFile $testEnvironmentFile `
        -ProjectName $projectName

    $operationError = $null
    $cleanupError = $null
    $cleanupRequired = $false
    $previousTestConnectionString = $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING
    $previousMigrationConnectionString = $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING
    $previousDefaultConnectionString = $env:ConnectionStrings__Default
    $previousDatabaseEnvironment = Set-NovaGameDatabaseProcessEnvironment `
        -Values $testEnvironment

    try {
        Push-Location -LiteralPath (Join-Path $repositoryRoot "server")
        try {
            Invoke-CheckedCommand "dotnet" @(
                "tool", "restore",
                "--configfile", "NuGet.Config",
                "--ignore-failed-sources"
            )
        }
        finally {
            Pop-Location
        }

        Invoke-CheckedCommand "docker" ($composeArguments + @("config", "--quiet"))
        $cleanupRequired = $true
        Invoke-CheckedCommand "docker" ($composeArguments + @(
            "up", "--detach", "--wait", "--wait-timeout", "180", "mysql"
        ))

        $portOutput = Invoke-CheckedCommandCapture "docker" ($composeArguments + @("port", "mysql", "3306"))
        $publishedEndpoint = @($portOutput | Where-Object { $_ -match ":\d+\s*$" } | Select-Object -Last 1)
        Assert-Condition ($publishedEndpoint.Count -eq 1) "Unable to resolve the disposable MySQL host port."
        $publishedPort = [int]([regex]::Match([string]$publishedEndpoint[0], ":(\d+)\s*$").Groups[1].Value)
        Assert-Condition ($publishedPort -gt 0) "Docker did not allocate a valid MySQL host port."

        $connectionString = New-NovaGameMySqlConnectionString `
            -Environment $testEnvironment `
            -Port $publishedPort
        $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING = $connectionString
        $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING = $connectionString
        $env:ConnectionStrings__Default = $connectionString

        Push-Location -LiteralPath (Join-Path $repositoryRoot "server")
        try {
            Invoke-CheckedCommand "dotnet" @(
                "tool", "run", "dotnet-ef", "migrations", "list",
                "--project", "src/NovaGame.Host.EntityFrameworkCore",
                "--startup-project", "src/NovaGame.Host.EntityFrameworkCore",
                "--no-build"
            )
            Invoke-CheckedCommand "dotnet" @(
                "tool", "run", "dotnet-ef", "migrations", "has-pending-model-changes",
                "--project", "src/NovaGame.Host.EntityFrameworkCore",
                "--startup-project", "src/NovaGame.Host.EntityFrameworkCore",
                "--no-build"
            )
        }
        finally {
            Pop-Location
        }

        if ($RunDbMigratorSeed) {
            $seedEnvironmentNames = @(
                "DOTNET_ENVIRONMENT",
                "NovaGame__AdminAccess__Seed__UserName",
                "NovaGame__AdminAccess__Seed__Password",
                "NovaGame__AdminAccess__Seed__DisplayName"
            )
            $previousSeedEnvironment = Get-NovaGameProcessEnvironmentSnapshot `
                -Names $seedEnvironmentNames

            try {
                [Environment]::SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing", "Process")
                [Environment]::SetEnvironmentVariable("NovaGame__AdminAccess__Seed__UserName", "verification-admin", "Process")
                [Environment]::SetEnvironmentVariable("NovaGame__AdminAccess__Seed__Password", "Verification-Administrator-Password-123!", "Process")
                [Environment]::SetEnvironmentVariable("NovaGame__AdminAccess__Seed__DisplayName", "Verification Administrator", "Process")
                Invoke-CheckedCommand "dotnet" @(
                    "run",
                    "--project", "server/src/NovaGame.DbMigrator/NovaGame.DbMigrator.csproj",
                    "--no-build",
                    "--no-restore"
                )
            }
            finally {
                Restore-NovaGameProcessEnvironmentSnapshot `
                    -Snapshot $previousSeedEnvironment
            }
        }

        Invoke-CheckedCommand "dotnet" @(
            "test",
            "server/NovaGame.sln",
            "--no-build",
            "--no-restore",
            "--verbosity", "minimal",
            "-p:NuGetAudit=false"
        )
    }
    catch {
        $operationError = $_
    }
    finally {
        $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING = $previousTestConnectionString
        $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING = $previousMigrationConnectionString
        $env:ConnectionStrings__Default = $previousDefaultConnectionString

        if ($cleanupRequired) {
            if ($projectName -notmatch "^novagame-test-[a-f0-9]{12}$") {
                $cleanupError = "Refusing to remove Docker resources for invalid project '$projectName'."
            }
            else {
                & docker @($composeArguments + @("down", "--volumes", "--remove-orphans"))
                if ($LASTEXITCODE -ne 0) {
                    $cleanupError = "Failed to remove disposable Docker project '$projectName'."
                }
            }
        }

        Restore-NovaGameDatabaseProcessEnvironment -PreviousValues $previousDatabaseEnvironment

        if (Test-Path -LiteralPath $testEnvironmentFile -PathType Leaf) {
            $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
            $resolvedTestEnvironmentFile = [System.IO.Path]::GetFullPath($testEnvironmentFile)
            if (-not $resolvedTestEnvironmentFile.StartsWith($resolvedTestRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove temporary file outside '$resolvedTestRoot'."
            }
            Remove-Item -LiteralPath $resolvedTestEnvironmentFile -Force
        }
    }

    if ($null -ne $operationError) {
        throw $operationError
    }
    if ($null -ne $cleanupError) {
        throw $cleanupError
    }

    Write-Output "Database verification passed."
}

function Test-Authentication {
    $apiModule = Get-Content -LiteralPath "server/src/NovaGame.Api/NovaGameApiModule.cs" -Raw
    $authController = Get-Content -LiteralPath `
        "server/src/NovaGame.Api/Operations/AdminAccess/AdminAccessController.cs" -Raw
    $apiSettings = Get-Content -LiteralPath "server/src/NovaGame.Api/appsettings.json" -Raw
    $dbMigrator = Get-Content -LiteralPath "server/src/NovaGame.DbMigrator/Program.cs" -Raw
    $runDev = Get-Content -LiteralPath "scripts/run-dev.ps1" -Raw
    $adminAccessEnvironmentExample = Get-Content -LiteralPath "server/.env.example" -Raw

    Assert-Condition $apiModule.Contains("NovaCoreAuthenticationJwtBearerModule") "NovaCore JWT validation module is not composed."
    Assert-Condition $apiModule.Contains("PermissionAuthorizationHandler") "NovaCore's permission authorization handler must be registered by the API composition root."
    Assert-Condition $apiModule.Contains("UseAuthentication()") "Authentication middleware is missing."
    Assert-Condition $apiModule.Contains("UseAuthorization()") "Authorization middleware is missing."
    Assert-Condition $authController.Contains("UnitOfWork(IsDisabled = true)") "Auth controller must disable NovaCore's outer MVC unit of work."
    Assert-Condition $authController.Contains("HttpOnly = true") "Refresh-token cookie must be HttpOnly."
    Assert-Condition $authController.Contains("Secure = true") "Refresh-token cookie must be Secure."
    Assert-Condition $authController.Contains("SameSite = SameSiteMode.Strict") "Refresh-token cookie must use SameSite=Strict."
    Assert-Condition $apiSettings.Contains('"SigningKey": ""') "Committed API settings must not contain a JWT signing key."
    Assert-Condition $dbMigrator.Contains("IDataSeedManager") "DbMigrator must run data seed contributors."
    Assert-Condition (-not $apiModule.Contains("IDataSeedManager")) "API startup must not run database seeding."
    Assert-Condition ($adminAccessEnvironmentExample -match '(?m)^NovaGame__AdminAccess__Jwt__SigningKey=$') "Administrator access environment example must not contain a signing key."
    Assert-Condition $runDev.Contains("Read-NovaGameAdminAccessEnvironment") "Development startup must load ignored administrator access configuration."
    Assert-Condition $runDev.Contains("Set-NovaGameAdminAccessProcessEnvironment") "Development startup must inject administrator access configuration only into the child process environment."
    Assert-Condition $runDev.Contains("NovaGame.DbMigrator") "Development startup must migrate and seed before starting the API."

    $adminEnvironmentTestRoot = Join-Path $repositoryRoot ".local\admin-access-tests"
    $adminEnvironmentTestFile = Join-Path `
        $adminEnvironmentTestRoot `
        "admin-$([Guid]::NewGuid().ToString('N')).env"
    $previousAdminEnvironment = $null
    try {
        Write-NovaGameAdminAccessEnvironmentFile -Path $adminEnvironmentTestFile
        $adminEnvironment = Read-NovaGameAdminAccessEnvironment -Path $adminEnvironmentTestFile
        Assert-Condition `
            (-not [string]::IsNullOrWhiteSpace([string]$adminEnvironment["NovaGame__AdminAccess__Jwt__SigningKey"])) `
            "Administrator access bootstrap must generate a signing key."
        Assert-Condition `
            ([string]::IsNullOrEmpty([string]$adminEnvironment["NovaGame__AdminAccess__Seed__UserName"])) `
            "Administrator seed credentials must remain optional."

        $initialAdminEnvironmentHash = (Get-FileHash `
            -LiteralPath $adminEnvironmentTestFile `
            -Algorithm SHA256).Hash
        $initializeStatus = Initialize-NovaGameAdminAccessEnvironmentFile `
            -Path $adminEnvironmentTestFile
        Assert-Condition ($initializeStatus -eq "existing") "Administrator access bootstrap must preserve existing configuration."
        Assert-Condition `
            ((Get-FileHash -LiteralPath $adminEnvironmentTestFile -Algorithm SHA256).Hash -eq $initialAdminEnvironmentHash) `
            "Administrator access bootstrap must not overwrite an existing secret file."

        $partialSeedEnvironment = [ordered]@{}
        foreach ($key in $script:NovaGameAdminAccessEnvironmentKeys) {
            $partialSeedEnvironment[$key] = [string]$adminEnvironment[$key]
        }
        $partialSeedEnvironment["NovaGame__AdminAccess__Seed__UserName"] = "admin"
        $partialSeedRejected = $false
        try {
            Assert-NovaGameAdminAccessEnvironment -Values $partialSeedEnvironment
        }
        catch {
            $partialSeedRejected = $true
        }
        Assert-Condition $partialSeedRejected "Partial administrator seed credentials must be rejected."

        $previousDisplayName = [Environment]::GetEnvironmentVariable(
            "NovaGame__AdminAccess__Seed__DisplayName",
            "Process")
        try {
            [Environment]::SetEnvironmentVariable(
                "NovaGame__AdminAccess__Seed__DisplayName",
                "Environment Administrator",
                "Process")
            $adminEnvironment = Get-NovaGameAdminAccessEffectiveEnvironment `
                -FileValues $adminEnvironment
            Assert-Condition `
                ([string]$adminEnvironment["NovaGame__AdminAccess__Seed__DisplayName"] -eq "Environment Administrator") `
                "Process environment values must override the ignored administrator access file."
        }
        finally {
            [Environment]::SetEnvironmentVariable(
                "NovaGame__AdminAccess__Seed__DisplayName",
                $previousDisplayName,
                "Process")
        }

        $previousAdminEnvironment = Set-NovaGameAdminAccessProcessEnvironment `
            -Values $adminEnvironment
        Assert-Condition `
            ([Environment]::GetEnvironmentVariable(
                "NovaGame__AdminAccess__Jwt__SigningKey",
                "Process") -eq [string]$adminEnvironment["NovaGame__AdminAccess__Jwt__SigningKey"]) `
            "Administrator access configuration was not loaded into the process environment."
    }
    finally {
        if ($null -ne $previousAdminEnvironment) {
            Restore-NovaGameAdminAccessProcessEnvironment `
                -PreviousValues $previousAdminEnvironment
        }
        if (Test-Path -LiteralPath $adminEnvironmentTestFile -PathType Leaf) {
            Remove-Item -LiteralPath $adminEnvironmentTestFile -Force
        }
    }

    $defaultAdminEnvironmentFile = Resolve-NovaGameAdminAccessEnvFile `
        -RepositoryRoot $repositoryRoot
    if (Test-Path -LiteralPath $defaultAdminEnvironmentFile -PathType Leaf) {
        $null = Read-NovaGameAdminAccessEnvironment -Path $defaultAdminEnvironmentFile
        & git check-ignore --quiet -- "server/.env.local"
        Assert-Condition ($LASTEXITCODE -eq 0) "server/.env.local exists but is not ignored by Git."
    }

    Test-Database -RunDbMigratorSeed
    Write-Output "Authentication verification passed."
}

function Test-GameConfig {
    $domainSharedModule = Get-Content -LiteralPath `
        "server/src/NovaGame.Domain.Shared/NovaGameDomainSharedModule.cs" -Raw
    $applicationModule = Get-Content -LiteralPath `
        "server/src/NovaGame.Application/NovaGameApplicationModule.cs" -Raw
    $apiModule = Get-Content -LiteralPath `
        "server/src/NovaGame.Api/NovaGameApiModule.cs" -Raw
    $controller = Get-Content -LiteralPath `
        "server/src/NovaGame.Api/Content/GameConfig/GameConfigController.cs" -Raw
    $dbContext = Get-Content -LiteralPath `
        "server/src/NovaGame.Host.EntityFrameworkCore/NovaGameDbContext.cs" -Raw
    $providerRegistration = Get-Content -LiteralPath `
        "server/src/NovaGame.Host.EntityFrameworkCore/DependencyInjection/NovaGameEntityFrameworkCoreServiceCollectionExtensions.cs" -Raw
    $administratorSeed = Get-Content -LiteralPath `
        "server/src/NovaGame.Application/Operations/AdminAccess/AdministratorSeedContributor.cs" -Raw

    foreach ($path in @(
        "server/src/NovaGame.Domain/Content/GameConfig/GameConfigDraft.cs",
        "server/src/NovaGame.Domain/Content/GameConfig/PublishedGameConfigVersion.cs",
        "server/src/NovaGame.Domain/Content/GameConfig/GameConfigEnvironment.cs",
        "server/src/NovaGame.Domain/Content/GameConfig/GameConfigContentProcessor.cs",
        "server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotValidator.cs",
        "server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotBuilder.cs",
        "server/src/NovaGame.Application/Content/GameConfig/GameConfigAppService.cs",
        "server/src/NovaGame.Api/Content/GameConfig/GameConfigController.cs",
        "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigDomainTests.cs",
        "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigSnapshotValidatorTests.cs",
        "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigApplicationTests.cs",
        "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigApiTests.cs"
    )) {
        Assert-Condition (Test-Path -LiteralPath $path -PathType Leaf) "Required GameConfig file '$path' is missing."
    }

    Assert-Condition $domainSharedModule.Contains("GameConfigPermissionDefinitionProvider") "GameConfig permissions are not registered."
    Assert-Condition $applicationModule.Contains("IGameConfigAppService") "GameConfig application service is not registered."
    Assert-Condition $apiModule.Contains("AddGameConfigApi") "GameConfig API composition is missing."
    Assert-Condition $controller.Contains("[AllowAnonymous]") "The active snapshot endpoint must allow anonymous clients."
    Assert-Condition $controller.Contains("SnapshotHash") "Snapshot responses must use the persisted full snapshot hash."
    Assert-Condition (-not (Test-Path -LiteralPath `
        "server/src/NovaGame.Api/Content/GameConfig/GameConfigApiPermissions.cs")) `
        "The API must use shared GameConfig permission codes instead of local duplicates."

    foreach ($permissionName in @(
        "GameConfigPermissions.Access",
        "GameConfigPermissions.ManageDrafts",
        "GameConfigPermissions.Publish",
        "GameConfigPermissions.Activate"
    )) {
        Assert-Condition $administratorSeed.Contains($permissionName) "The Administrators seed is missing '$permissionName'."
    }

    foreach ($dbSetName in @(
        "GameConfigDrafts",
        "PublishedGameConfigVersions",
        "GameConfigEnvironments",
        "GameConfigAuditRecords"
    )) {
        Assert-Condition $dbContext.Contains($dbSetName) "NovaGameDbContext is missing '$dbSetName'."
    }

    foreach ($repositoryName in @(
        "IGameConfigDraftRepository",
        "IPublishedGameConfigVersionRepository",
        "IGameConfigEnvironmentRepository",
        "IGameConfigAuditRepository"
    )) {
        Assert-Condition $providerRegistration.Contains($repositoryName) "EF registration is missing '$repositoryName'."
    }

    $gameConfigMigrations = @(
        Get-ChildItem -LiteralPath `
            "server/src/NovaGame.Host.EntityFrameworkCore/Migrations" `
            -File `
            -Filter "*_AddGameConfig.cs" |
            Where-Object { $_.Name -notlike "*.Designer.cs" }
    )
    Assert-Condition ($gameConfigMigrations.Count -eq 1) "Exactly one AddGameConfig migration is required."

    Test-Authentication
    Write-Output "GameConfig verification passed."
}

function Invoke-SwordMasterRealApiAcceptance {
    param([Parameter(Mandatory)][string]$ApiUrl)

    $acceptanceEnvironmentName = "NOVAGAME_ACCEPTANCE_API_URL"
    $sensitiveEnvironmentNames = @(
        "ConnectionStrings__Default",
        "MYSQL_PWD",
        "NOVAGAME_E2E_API_URL",
        "NOVAGAME_E2E_BASE_URL",
        "NOVAGAME_E2E_MYSQL_CONTAINER",
        "NOVAGAME_E2E_MYSQL_DATABASE",
        "NOVAGAME_E2E_MYSQL_ROOT_PASSWORD",
        "NOVAGAME_E2E_PASSWORD",
        "NOVAGAME_E2E_UPSTREAM_API_URL",
        "NOVAGAME_E2E_USERNAME",
        "NOVAGAME_MYSQL_BIND_ADDRESS",
        "NOVAGAME_MYSQL_DATABASE",
        "NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING",
        "NOVAGAME_MYSQL_PASSWORD",
        "NOVAGAME_MYSQL_PORT",
        "NOVAGAME_MYSQL_ROOT_PASSWORD",
        "NOVAGAME_MYSQL_USER",
        "NOVAGAME_TEST_MYSQL_CONNECTION_STRING",
        "NovaGame__AdminAccess__Jwt__SigningKey",
        "NovaGame__AdminAccess__Seed__DisplayName",
        "NovaGame__AdminAccess__Seed__Password",
        "NovaGame__AdminAccess__Seed__UserName"
    )
    $previousEnvironment = Get-NovaGameProcessEnvironmentSnapshot `
        -Names @($sensitiveEnvironmentNames + $acceptanceEnvironmentName)

    try {
        foreach ($name in $sensitiveEnvironmentNames) {
            [Environment]::SetEnvironmentVariable(
                $name,
                [System.Management.Automation.Language.NullString]::Value,
                "Process")
        }
        [Environment]::SetEnvironmentVariable(
            $acceptanceEnvironmentName,
            $ApiUrl,
            "Process")

        $swordMasterToolchain = Initialize-SwordMasterToolchain
        Invoke-SwordMasterPnpm `
            -Toolchain $swordMasterToolchain `
            -ArgumentList @("run", "test:real-api")
    }
    finally {
        Restore-NovaGameProcessEnvironmentSnapshot -Snapshot $previousEnvironment
    }

    Write-Output "Real API to SwordMaster snapshot acceptance passed."
}

function Invoke-AdminEndToEnd {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [switch]$RunSwordMasterAcceptance
    )

    $browserChannel = Resolve-E2EBrowserChannel
    Invoke-CheckedCommand "docker" @("compose", "version")
    Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")

    $projectName = "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $testRoot = Join-Path $repositoryRoot ".local\database-tests"
    $testEnvironmentFile = Join-Path $testRoot "$projectName.env"
    $operationError = $null
    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    $cleanupRequired = $false
    $apiProcess = $null
    $logRoot = $null
    $previousAdminAccessEnvironment = $null
    $previousDatabaseEnvironment = $null
    $testEnvironment = $null
    $composeArguments = @()
    $previousConnectionValues = [ordered]@{
        NOVAGAME_TEST_MYSQL_CONNECTION_STRING = $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING
        NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING = $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING
        ConnectionStrings__Default = $env:ConnectionStrings__Default
    }
    $runtimeEnvironmentNames = @(
        "ASPNETCORE_URLS",
        "ASPNETCORE_ENVIRONMENT",
        "DOTNET_ENVIRONMENT",
        "NovaCore__Web__ForwardedHeaders__IsEnabled",
        "NovaCore__Web__ForwardedHeaders__KnownProxies__0",
        "VITE_GLOB_API_URL",
        "VITE_NOVAGAME_API_URL",
        "VITE_NITRO_MOCK",
        "VITE_HTTPS",
        "VITE_PORT",
        "NOVAGAME_ADMIN_PORT",
        "NOVAGAME_API_URL",
        "NOVAGAME_E2E_BASE_URL",
        "NOVAGAME_E2E_API_URL",
        "NOVAGAME_E2E_UPSTREAM_API_URL",
        "NOVAGAME_E2E_USERNAME",
        "NOVAGAME_E2E_PASSWORD",
        "NOVAGAME_E2E_BROWSER_CHANNEL",
        "NOVAGAME_E2E_MYSQL_CONTAINER",
        "NOVAGAME_E2E_MYSQL_ROOT_PASSWORD",
        "NOVAGAME_E2E_MYSQL_DATABASE"
    )
    $previousRuntimeEnvironment = Get-NovaGameProcessEnvironmentSnapshot `
        -Names $runtimeEnvironmentNames

    try {
        Write-NovaGameDatabaseEnvironmentFile `
            -Path $testEnvironmentFile `
            -Database "novagame_e2e" `
            -User "novagame_e2e" `
            -Port 0 `
            -AllowEphemeralPort
        $testEnvironment = Read-NovaGameDatabaseEnvironment `
            -Path $testEnvironmentFile `
            -AllowEphemeralPort
        $composeArguments = Get-NovaGameComposeArguments `
            -RepositoryRoot $repositoryRoot `
            -EnvironmentFile $testEnvironmentFile `
            -ProjectName $projectName
        $previousDatabaseEnvironment = Set-NovaGameDatabaseProcessEnvironment `
            -Values $testEnvironment

        Invoke-CheckedCommand "docker" ($composeArguments + @("config", "--quiet"))
        $cleanupRequired = $true
        Invoke-CheckedCommand "docker" ($composeArguments + @(
            "up", "--detach", "--wait", "--wait-timeout", "180", "mysql"
        ))

        $portOutput = Invoke-CheckedCommandCapture "docker" `
            ($composeArguments + @("port", "mysql", "3306"))
        $publishedEndpoint = @(
            $portOutput |
                Where-Object { $_ -match ":\d+\s*$" } |
                Select-Object -Last 1
        )
        Assert-Condition ($publishedEndpoint.Count -eq 1) "Unable to resolve the E2E MySQL host port."
        $publishedPort = [int]([regex]::Match(
                [string]$publishedEndpoint[0],
                ":(\d+)\s*$").Groups[1].Value)
        Assert-Condition ($publishedPort -gt 0) "Docker did not allocate a valid E2E MySQL host port."

        $containerOutput = Invoke-CheckedCommandCapture "docker" `
            ($composeArguments + @("ps", "--quiet", "mysql"))
        $mysqlContainers = @(
            $containerOutput |
                ForEach-Object { ([string]$_).Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        Assert-Condition ($mysqlContainers.Count -eq 1) "Unable to resolve the disposable E2E MySQL container."

        $connectionString = New-NovaGameMySqlConnectionString `
            -Environment $testEnvironment `
            -Port $publishedPort
        $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING = $connectionString
        $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING = $connectionString
        $env:ConnectionStrings__Default = $connectionString

        $seedUserName = "verification-admin"
        $seedPassword = "Verification-Administrator-Password-123!"
        $adminAccessEnvironment = [ordered]@{
            NovaGame__AdminAccess__Jwt__SigningKey = New-NovaGameAdminAccessSigningKey
            NovaGame__AdminAccess__Seed__UserName = $seedUserName
            NovaGame__AdminAccess__Seed__Password = $seedPassword
            NovaGame__AdminAccess__Seed__DisplayName = "Verification Administrator"
        }
        $previousAdminAccessEnvironment = Set-NovaGameAdminAccessProcessEnvironment `
            -Values $adminAccessEnvironment
        $env:DOTNET_ENVIRONMENT = "Testing"
        $env:ASPNETCORE_ENVIRONMENT = "Testing"

        Invoke-CheckedCommand "dotnet" @(
            "run",
            "--project", "server/src/NovaGame.DbMigrator/NovaGame.DbMigrator.csproj",
            "--no-build",
            "--no-restore"
        )

        $apiPort = Get-AvailableLoopbackPort
        do {
            $adminPort = Get-AvailableLoopbackPort
        } while ($adminPort -eq $apiPort)
        $apiUrl = "http://127.0.0.1:$apiPort"
        $adminUrl = "https://localhost:$adminPort"
        $env:ASPNETCORE_URLS = $apiUrl
        $env:NovaCore__Web__ForwardedHeaders__IsEnabled = "true"
        $env:NovaCore__Web__ForwardedHeaders__KnownProxies__0 = "127.0.0.1"

        $logRoot = Join-Path $repositoryRoot ".local\admin-e2e\$projectName"
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
        $apiStandardOutputPath = Join-Path $logRoot "api.stdout.log"
        $apiStandardErrorPath = Join-Path $logRoot "api.stderr.log"
        $dotnet = (Get-Command "dotnet" -ErrorAction Stop).Source
        $apiProcess = Start-Process `
            -FilePath $dotnet `
            -ArgumentList @(
                "run",
                "--project", "server/src/NovaGame.Api/NovaGame.Api.csproj",
                "--no-build",
                "--no-restore",
                "--no-launch-profile"
            ) `
            -WorkingDirectory $repositoryRoot `
            -WindowStyle Hidden `
            -RedirectStandardOutput $apiStandardOutputPath `
            -RedirectStandardError $apiStandardErrorPath `
            -PassThru

        Wait-HttpEndpoint `
            -Uri ([Uri]"$apiUrl/health/live") `
            -Process $apiProcess `
            -StandardOutputPath $apiStandardOutputPath `
            -StandardErrorPath $apiStandardErrorPath `
            -Headers @{
                "X-Forwarded-For" = "127.0.0.1"
                "X-Forwarded-Proto" = "https"
                "X-Forwarded-Host" = "localhost:$adminPort"
            }

        $e2eEnvironment = [ordered]@{
            VITE_GLOB_API_URL = "/api"
            VITE_NOVAGAME_API_URL = $apiUrl
            VITE_NITRO_MOCK = "false"
            VITE_HTTPS = "true"
            VITE_PORT = [string]$adminPort
            NOVAGAME_ADMIN_PORT = [string]$adminPort
            NOVAGAME_API_URL = $apiUrl
            NOVAGAME_E2E_BASE_URL = $adminUrl
            NOVAGAME_E2E_API_URL = $apiUrl
            NOVAGAME_E2E_UPSTREAM_API_URL = $apiUrl
            NOVAGAME_E2E_USERNAME = $seedUserName
            NOVAGAME_E2E_PASSWORD = $seedPassword
            NOVAGAME_E2E_BROWSER_CHANNEL = $browserChannel
            NOVAGAME_E2E_MYSQL_CONTAINER = [string]$mysqlContainers[0]
            NOVAGAME_E2E_MYSQL_ROOT_PASSWORD = [string]$testEnvironment["NOVAGAME_MYSQL_ROOT_PASSWORD"]
            NOVAGAME_E2E_MYSQL_DATABASE = [string]$testEnvironment["NOVAGAME_MYSQL_DATABASE"]
        }
        foreach ($entry in $e2eEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                [string]$entry.Key,
                [string]$entry.Value,
                "Process")
        }

        Invoke-AdminPnpm -Toolchain $Toolchain -ArgumentList @("run", "test:e2e")
        if ($RunSwordMasterAcceptance) {
            Invoke-SwordMasterRealApiAcceptance -ApiUrl $apiUrl
        }
    }
    catch {
        $operationError = $_
    }
    finally {
        try {
            Stop-ProcessTree -Process $apiProcess
        }
        catch {
            $null = $cleanupErrors.Add("Failed to stop the Admin E2E API process: $($_.Exception.Message)")
        }

        try {
            Restore-NovaGameProcessEnvironmentSnapshot `
                -Snapshot $previousRuntimeEnvironment
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to restore the Admin E2E process environment.")
        }
        if ($null -ne $previousAdminAccessEnvironment) {
            try {
                Restore-NovaGameAdminAccessProcessEnvironment `
                    -PreviousValues $previousAdminAccessEnvironment
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to restore the administrator-access environment.")
            }
        }
        try {
            $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING = `
                $previousConnectionValues.NOVAGAME_TEST_MYSQL_CONNECTION_STRING
            $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING = `
                $previousConnectionValues.NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING
            $env:ConnectionStrings__Default = $previousConnectionValues.ConnectionStrings__Default
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to restore database connection environment variables.")
        }

        try {
            if ($cleanupRequired) {
                if ($projectName -notmatch "^novagame-test-[a-f0-9]{12}$") {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove Docker resources for invalid project '$projectName'.")
                }
                else {
                    & docker @($composeArguments + @("down", "--volumes", "--remove-orphans"))
                    if ($LASTEXITCODE -ne 0) {
                        $null = $cleanupErrors.Add(
                            "Failed to remove disposable Docker project '$projectName'.")
                    }
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove disposable Docker project '$projectName'.")
        }

        if ($null -ne $previousDatabaseEnvironment) {
            try {
                Restore-NovaGameDatabaseProcessEnvironment `
                    -PreviousValues $previousDatabaseEnvironment
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to restore the MySQL process environment.")
            }
        }

        try {
            if (Test-Path -LiteralPath $testEnvironmentFile -PathType Leaf) {
                $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
                $resolvedTestEnvironmentFile = [System.IO.Path]::GetFullPath(
                    $testEnvironmentFile)
                if (-not $resolvedTestEnvironmentFile.StartsWith(
                        $resolvedTestRoot,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove temporary file outside '$resolvedTestRoot'.")
                }
                else {
                    Remove-Item -LiteralPath $resolvedTestEnvironmentFile -Force
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove temporary environment file '$testEnvironmentFile'.")
        }

        if ($null -eq $operationError -and
            $cleanupErrors.Count -eq 0 -and
            -not [string]::IsNullOrWhiteSpace([string]$logRoot) -and
            (Test-Path -LiteralPath $logRoot -PathType Container)) {
            try {
                $adminE2ERoot = Join-Path $repositoryRoot ".local\admin-e2e"
                $resolvedAdminE2ERoot = [System.IO.Path]::GetFullPath(
                    $adminE2ERoot).TrimEnd('\') + '\'
                $resolvedLogRoot = [System.IO.Path]::GetFullPath($logRoot)
                if (-not $resolvedLogRoot.StartsWith(
                        $resolvedAdminE2ERoot,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove Admin E2E logs outside '$resolvedAdminE2ERoot'.")
                }
                else {
                    Remove-Item -LiteralPath $resolvedLogRoot -Recurse -Force
                }
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to remove successful Admin E2E logs '$logRoot'.")
            }
        }
    }

    if ($null -ne $operationError) {
        if ($cleanupErrors.Count -gt 0) {
            throw "$operationError`nCleanup errors:`n$($cleanupErrors -join "`n")"
        }
        throw $operationError
    }
    if ($cleanupErrors.Count -gt 0) {
        throw "Admin E2E cleanup failed:`n$($cleanupErrors -join "`n")"
    }
}

function Test-Admin {
    param([switch]$RunSwordMasterAcceptance)

    Test-GameConfig

    foreach ($requiredPath in @(
        "admin/LICENSE",
        "admin/package.json",
        "admin/pnpm-lock.yaml",
        "admin/pnpm-workspace.yaml",
        "admin/apps/web-ele/package.json",
        "admin/apps/web-ele/.env.development",
        "admin/apps/web-ele/.env.production"
    )) {
        Assert-Condition (Test-Path -LiteralPath $requiredPath -PathType Leaf) "Missing admin file '$requiredPath'."
    }

    $adminPackage = Get-Content -LiteralPath "admin/package.json" -Raw | ConvertFrom-Json
    $webElePackage = Get-Content -LiteralPath "admin/apps/web-ele/package.json" -Raw | ConvertFrom-Json
    Assert-Condition `
        ([string]$adminPackage.packageManager -match '^pnpm@\d+\.\d+\.\d+$') `
        "Admin packageManager must pin an exact pnpm version."
    Assert-Condition `
        ([string]$adminPackage.engines.node -eq "^22.18.0 || ^24.0.0") `
        "Admin must preserve the fixed Vben Node.js engine constraint."
    Assert-Condition ([string]$webElePackage.name -eq "@vben/web-ele") "Admin must use the Vben web-ele application."
    Assert-Condition `
        ($null -ne $webElePackage.dependencies.PSObject.Properties["element-plus"]) `
        "Admin web-ele must retain Element Plus."
    Assert-Condition `
        ($null -eq $webElePackage.dependencies.PSObject.Properties["ant-design-vue"]) `
        "Admin web-ele must not depend on Ant Design Vue."

    $applicationDirectories = @(
        Get-ChildItem -LiteralPath "admin/apps" -Directory |
            ForEach-Object { $_.Name }
    )
    Assert-StringSet `
        -Expected @("web-ele") `
        -Actual $applicationDirectories `
        -Message "Admin must retain only the web-ele application variant."
    Assert-Condition `
        (-not (Test-Path -LiteralPath "admin/apps/backend-mock")) `
        "Admin must not include the Vben mock backend."

    $developmentEnvironment = Get-Content -LiteralPath "admin/apps/web-ele/.env.development" -Raw
    $productionEnvironment = Get-Content -LiteralPath "admin/apps/web-ele/.env.production" -Raw
    $viteConfiguration = Get-Content -LiteralPath "admin/apps/web-ele/vite.config.ts" -Raw
    Assert-Condition `
        ($developmentEnvironment -notmatch '(?im)^VITE_NITRO_MOCK\s*=\s*true\s*$') `
        "Admin development must not enable the Nitro mock backend."
    Assert-Condition `
        ($productionEnvironment -notmatch 'mock-napi\.vben\.pro') `
        "Admin production configuration must not target the Vben mock API."
    Assert-Condition $viteConfiguration.Contains("VITE_HTTPS") "Admin Vite must support the HTTPS runtime flag."
    Assert-Condition $viteConfiguration.Contains("NOVAGAME_ADMIN_PORT") "Admin Vite must honor the script-owned HTTPS port."
    Assert-Condition $viteConfiguration.Contains("NOVAGAME_API_URL") "Admin Vite must honor the script-owned API target."
    Assert-Condition ($viteConfiguration -match 'xfwd\s*:\s*true') "Admin Vite API proxy must forward the original HTTPS protocol."
    Assert-Condition ($viteConfiguration -notmatch 'rewrite\s*:') "Admin Vite must proxy /api without rewriting the path."

    $adminRequestSource = Get-Content `
        -LiteralPath "admin/apps/web-ele/src/api/request.ts" `
        -Raw
    Assert-Condition `
        ($adminRequestSource -match `
            '(?m)^const apiRequestTimeoutMs = 60_000;$') `
        "Admin API requests must allow the verified 60-second cold-start window."
    Assert-Condition `
        ([regex]::Matches(
                $adminRequestSource,
                '(?m)^\s*timeout: apiRequestTimeoutMs,$').Count -eq 2) `
        "Admin public and authenticated requests must explicitly override the Vben 10-second default timeout."

    $adminE2ESource = Get-Content `
        -LiteralPath "admin/apps/web-ele/e2e/game-config.spec.ts" `
        -Raw
    Assert-Condition `
        ($adminE2ESource.Contains(
                "waitForApiResponseOrFailure",
                [StringComparison]::Ordinal) -and
            $adminE2ESource.Contains(
                "page.on('requestfailed'",
                [StringComparison]::Ordinal)) `
        "Admin login E2E must fail promptly when the browser aborts the authentication request."
    Assert-Condition `
        ($adminE2ESource.Contains(
                "const loginPageReadyTimeoutMs = 120_000;",
                [StringComparison]::Ordinal) -and
            $adminE2ESource.Contains(
                "waitUntil: 'commit',",
                [StringComparison]::Ordinal) -and
            [regex]::Matches(
                $adminE2ESource,
                '(?m)^\s*await expect\((?:username|password)Field\)\.toBeVisible\(\{ timeout: loginPageReadyTimeoutMs \}\);$').Count -eq 2) `
        "Admin login E2E must bound cold SPA readiness after the main document commits."

    $adminFiles = @(git ls-files --cached --others --exclude-standard -- "admin")
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate admin files through Git."
    }
    $playwrightConfigs = @(
        $adminFiles | Where-Object { $_ -match '(?i)(^|/)playwright\.config\.(ts|mts|js|mjs)$' }
    )
    $browserSpecs = @(
        $adminFiles | Where-Object {
            $_ -match '(?i)(^|/)(e2e|tests/e2e)/.+\.spec\.ts$' -or
            $_ -match '(?i)\.e2e\.ts$'
        }
    )
    Assert-Condition ($playwrightConfigs.Count -ge 1) "Admin Playwright configuration is missing."
    Assert-Condition ($browserSpecs.Count -ge 1) "Admin browser coverage is missing."

    $toolchain = Initialize-AdminToolchain
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "format:check")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "lint")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "check:type")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "test:unit")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "build:ele")
        Invoke-AdminEndToEnd `
            -Toolchain $toolchain `
            -RunSwordMasterAcceptance:$RunSwordMasterAcceptance
    }
    finally {
        $env:CI = $previousCi
    }

    Write-Output "Admin verification passed."
}

function Test-AdminEndToEnd {
    $toolchain = Initialize-AdminToolchain
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-AdminEndToEnd -Toolchain $toolchain
    }
    finally {
        $env:CI = $previousCi
    }

    Write-Output "Admin E2E verification passed."
}

function Test-Client {
    param([switch]$SkipRepositoryPrerequisites)

    if (-not $SkipRepositoryPrerequisites) {
        Test-Repository
    }

    $clientRoot = "clients/cocos"
    $packageRoot = "$clientRoot/packages"
    $adapterRoot = "$clientRoot/adapters"
    $gameConfigClientRoot = "$packageRoot/game-config-client"
    $cocosCreatorAdapterRoot = "$adapterRoot/cocos-creator"
    foreach ($requiredPath in @(
        "$clientRoot/package.json",
        "$clientRoot/pnpm-lock.yaml",
        "$clientRoot/pnpm-workspace.yaml",
        "$clientRoot/.node-version",
        "$gameConfigClientRoot/package.json",
        "$cocosCreatorAdapterRoot/package.json"
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
            "Missing Cocos client file '$requiredPath'."
    }
    foreach ($requiredDirectory in @(
        $packageRoot,
        $adapterRoot,
        $gameConfigClientRoot,
        $cocosCreatorAdapterRoot
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredDirectory -PathType Container) `
            "Missing Cocos client directory '$requiredDirectory'."
    }

    $clientPackage = Get-Content -LiteralPath "$clientRoot/package.json" -Raw | ConvertFrom-Json
    $gameConfigClientPackage = Get-Content `
        -LiteralPath "$gameConfigClientRoot/package.json" `
        -Raw | ConvertFrom-Json
    $cocosCreatorAdapterPackage = Get-Content `
        -LiteralPath "$cocosCreatorAdapterRoot/package.json" `
        -Raw | ConvertFrom-Json

    Assert-Condition `
        ([string]$clientPackage.packageManager -eq "pnpm@10.33.4") `
        "The Cocos client workspace must pin packageManager to pnpm@10.33.4."
    Assert-Condition `
        ([string]$clientPackage.engines.node -eq "^22.18.0 || ^24.0.0") `
        "The Cocos client workspace must require Node.js ^22.18.0 or ^24.0.0."
    Assert-Condition `
        ((Get-Content -LiteralPath "$clientRoot/.node-version" -Raw).Trim() -eq "24.14.0") `
        "The Cocos client .node-version must remain pinned to 24.14.0."
    foreach ($requiredScript in @("format:check", "lint", "typecheck", "test", "build")) {
        Assert-Condition `
            ($null -ne $clientPackage.scripts.PSObject.Properties[$requiredScript]) `
            "The Cocos client package.json is missing required script '$requiredScript'."
    }

    $workspaceDefinition = Get-Content -LiteralPath "$clientRoot/pnpm-workspace.yaml" -Raw
    Assert-Condition `
        ($workspaceDefinition -match '(?m)^\s*-\s+packages/\*\s*$') `
        "The Cocos client workspace must include packages/*."
    Assert-Condition `
        ($workspaceDefinition -match '(?m)^\s*-\s+adapters/\*\s*$') `
        "The Cocos client workspace must include adapters/*."

    Assert-StringSet `
        -Expected @("game-config-client") `
        -Actual @(
            Get-ChildItem -LiteralPath $packageRoot -Directory |
                ForEach-Object { $_.Name }
        ) `
        -Message "M0.5 must contain only the engine-independent GameConfig client package."
    Assert-StringSet `
        -Expected @("cocos-creator") `
        -Actual @(
            Get-ChildItem -LiteralPath $adapterRoot -Directory |
                ForEach-Object { $_.Name }
        ) `
        -Message "M0.5 must contain only the Cocos Creator adapter package."
    foreach ($futureDirectory in @(
        "$clientRoot/framework",
        "$clientRoot/samples"
    )) {
        Assert-Condition `
            (-not (Test-Path -LiteralPath $futureDirectory)) `
            "Unvalidated future client directory '$futureDirectory' must not be scaffolded."
    }

    $gameConfigClientName = ([string]$gameConfigClientPackage.name).Trim()
    $cocosCreatorAdapterName = ([string]$cocosCreatorAdapterPackage.name).Trim()
    Assert-Condition `
        (-not [string]::IsNullOrWhiteSpace($gameConfigClientName)) `
        "The GameConfig client package name must not be empty."
    Assert-Condition `
        (-not [string]::IsNullOrWhiteSpace($cocosCreatorAdapterName)) `
        "The Cocos Creator adapter package name must not be empty."
    Assert-Condition `
        ($gameConfigClientName -ne $cocosCreatorAdapterName) `
        "Client package names must be unique."

    $gameConfigClientDependencies = @(
        Get-PackageDependencyNames -Package $gameConfigClientPackage
    )
    $cocosCreatorAdapterDependencies = @(
        Get-PackageDependencyNames -Package $cocosCreatorAdapterPackage
    )
    Assert-Condition `
        ($gameConfigClientDependencies -notcontains $cocosCreatorAdapterName) `
        "The engine-independent GameConfig client must not depend on the Cocos Creator adapter."
    Assert-Condition `
        ($gameConfigClientDependencies -notcontains "cc") `
        "The engine-independent GameConfig client must not depend on the Cocos Creator runtime."
    Assert-Condition `
        ($cocosCreatorAdapterDependencies -notcontains "cc") `
        "The Cocos Creator adapter must remain headlessly buildable without a direct cc package dependency."

    $adapterDependenciesProperty = $cocosCreatorAdapterPackage.PSObject.Properties["dependencies"]
    $adapterGameConfigDependency = if ($null -eq $adapterDependenciesProperty) {
        $null
    }
    else {
        $adapterDependenciesProperty.Value.PSObject.Properties[$gameConfigClientName]
    }
    Assert-Condition `
        ($null -ne $adapterGameConfigDependency) `
        "The Cocos Creator adapter must depend on '$gameConfigClientName'."
    Assert-Condition `
        ([string]$adapterGameConfigDependency.Value -eq "workspace:*") `
        "The Cocos Creator adapter must consume '$gameConfigClientName' through workspace:*."

    $clientFiles = @(
        git ls-files --cached --others --exclude-standard -- `
            $gameConfigClientRoot `
            $cocosCreatorAdapterRoot
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate Cocos client files through Git."
    }

    $referenceViolations = [System.Collections.Generic.List[string]]::new()
    $textExtensions = @(".cjs", ".js", ".json", ".mjs", ".ts", ".tsx", ".yaml", ".yml")
    $ccModulePattern = '(?im)(?:\bfrom\s+|\bimport\s*(?:\(\s*)?|\brequire\s*\(\s*)[''"]cc[''"]'
    foreach ($relativePath in $clientFiles) {
        if ([System.IO.Path]::GetExtension($relativePath) -notin $textExtensions) {
            continue
        }

        $content = Get-Content -LiteralPath $relativePath -Raw
        if ($content -match '(?i)SwordMaster') {
            $referenceViolations.Add("$relativePath references SwordMaster.")
        }
        if ($content -match $ccModulePattern) {
            $referenceViolations.Add("$relativePath imports the Cocos Creator cc module.")
        }

        $normalizedPath = $relativePath.Replace("\", "/")
        if ($normalizedPath.StartsWith("$gameConfigClientRoot/", [StringComparison]::OrdinalIgnoreCase) -and
            ($content.Contains($cocosCreatorAdapterName, [StringComparison]::Ordinal) -or
                $content -match '(?i)adapters[/\\]cocos-creator')) {
            $referenceViolations.Add("$relativePath references the Cocos Creator adapter from the engine-independent package.")
        }
    }
    Assert-Condition `
        ($referenceViolations.Count -eq 0) `
        "Cocos client boundary violations:`n$($referenceViolations -join "`n")"

    $toolchain = Initialize-ClientToolchain
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        foreach ($scriptName in @("format:check", "lint", "typecheck", "test", "build")) {
            Invoke-ClientPnpm `
                -Toolchain $toolchain `
                -ArgumentList @("run", $scriptName)
        }
    }
    finally {
        $env:CI = $previousCi
    }

    Write-Output "Client verification passed."
}

function Test-SwordMasterServerArchitecture {
    $serverRoot = "samples/SwordMaster/server"
    $solutionPath = "$serverRoot/SwordMaster.Server.sln"
    $nugetConfigPath = "$serverRoot/NuGet.Config"
    $expectedProjects = @(
        "src/SwordMaster.Domain/SwordMaster.Domain.csproj",
        "src/SwordMaster.Application.Contracts/SwordMaster.Application.Contracts.csproj",
        "src/SwordMaster.Application/SwordMaster.Application.csproj",
        "src/SwordMaster.Host.EntityFrameworkCore/SwordMaster.Host.EntityFrameworkCore.csproj",
        "src/SwordMaster.Api/SwordMaster.Api.csproj",
        "src/SwordMaster.DbMigrator/SwordMaster.DbMigrator.csproj",
        "tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj",
        "tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj",
        "tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj"
    )
    $requiredCompositionFiles = @(
        "src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigContract.cs",
        "src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigOptions.cs",
        "src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigSnapshotParser.cs",
        "src/SwordMaster.Application/PlayerAssets/GameConfigEquipmentDefinitionProvider.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/SwordMasterEntityFrameworkCoreModule.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/SwordMasterDbContext.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/SwordMasterDbContextFactory.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/DependencyInjection/SwordMasterEntityFrameworkCoreServiceCollectionExtensions.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/DependencyInjection/SwordMasterDatabaseConfigurationValidator.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsProfileConfiguration.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetCommandReceiptConfiguration.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsProfileRepository.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetCommandReceiptRepository.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsTransactionManager.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/Migrations/20260801121309_InitialPlayerAssets.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/Migrations/SwordMasterDbContextModelSnapshot.cs",
        "src/SwordMaster.Api/Program.cs",
        "src/SwordMaster.Api/appsettings.json",
        "src/SwordMaster.Api/appsettings.Production.json",
        "src/SwordMaster.Api/SwordMasterApiModule.cs",
        "src/SwordMaster.Api/PlayerAssets/SwordMasterPlayerJwtOptions.cs",
        "src/SwordMaster.Api/PlayerAssets/SwordMasterPlayerAuthentication.cs",
        "src/SwordMaster.Api/PlayerAssets/HttpCurrentPlayerContext.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerAssetsController.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerAssetsExceptionFilter.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerAssetsOperationLogger.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerAssetsOpenApiOperationFilter.cs",
        "src/SwordMaster.DbMigrator/Program.cs",
        "src/SwordMaster.DbMigrator/appsettings.json",
        "src/SwordMaster.DbMigrator/SwordMasterDbMigratorModule.cs",
        "src/SwordMaster.DbMigrator/SwordMasterDbMigratorConfigurationValidator.cs",
        "src/SwordMaster.DbMigrator/Auditing/SwordMasterDbMigratorRequestAuditContextAccessor.cs",
        "src/SwordMaster.DbMigrator/PlayerAssets/SwordMasterPlayerAssetsDevelopmentSeedRunner.cs",
        "tests/SwordMaster.Application.Tests/PlayerAssets/GameConfigEquipmentDefinitionProviderTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/SwordMasterApiFactory.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsApiTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsMySqlApiTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsObservabilityTests.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterRuntimeApiFactory.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterRuntimeCompositionTests.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterDbMigratorConfigurationTests.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterRuntimeDatabaseTests.cs"
    )

    foreach ($requiredPath in @(
        $solutionPath,
        $nugetConfigPath,
        "$serverRoot/Directory.Build.props"
    ) + @(
        ($expectedProjects + $requiredCompositionFiles) |
            ForEach-Object { "$serverRoot/$_" }
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
            "Missing current-stage SwordMaster server file '$requiredPath'."
    }

    $actualProjects = @(
        Get-ChildItem -LiteralPath $serverRoot -Recurse -File -Filter "*.csproj" |
            ForEach-Object {
                [System.IO.Path]::GetRelativePath($serverRoot, $_.FullName).Replace("\", "/")
            }
    )
    Assert-StringSet `
        -Expected $expectedProjects `
        -Actual $actualProjects `
        -Message "M1-04 SwordMaster server must contain exactly the approved six source and three test projects."
    Assert-StringSet `
        -Expected $expectedProjects `
        -Actual (Get-SolutionProjectPaths $solutionPath) `
        -Message "M1-04 SwordMaster solution project set is incorrect."

    [xml]$directoryBuildProps = Get-Content `
        -LiteralPath "$serverRoot/Directory.Build.props" `
        -Raw
    $configurationPropagationValues = @(
        $directoryBuildProps.SelectNodes(
            "//ShouldUnsetParentConfigurationAndPlatform") |
            ForEach-Object { $_.InnerText.Trim() }
    )
    Assert-Condition `
        ($configurationPropagationValues.Count -eq 1 -and
            $configurationPropagationValues[0] -eq "false") `
        "SwordMaster Directory.Build.props must preserve the parent Release configuration for external project references."
    $configurationPropagationTarget = $directoryBuildProps.SelectSingleNode(
        "/Project/Target[@Name='PreserveExternalProjectReferenceConfiguration']")
    Assert-Condition `
        ($null -ne $configurationPropagationTarget -and
            [string]$configurationPropagationTarget.AfterTargets -eq
                "AssignProjectConfiguration") `
        "SwordMaster Directory.Build.props must propagate configuration metadata after project-reference assignment."
    $setConfigurationNode = $configurationPropagationTarget.SelectSingleNode(
        ".//ProjectReferenceWithConfiguration/SetConfiguration")
    Assert-Condition `
        ($null -ne $setConfigurationNode -and
            $setConfigurationNode.InnerText.Contains(
                "ShouldUnsetParentConfigurationAndPlatform=false",
                [StringComparison]::Ordinal)) `
        "SwordMaster project references must propagate the external Release configuration transitively."

    Test-NovaCorePackageSourceConfiguration `
        -NuGetConfigPath $nugetConfigPath `
        -ExpectedRelativeSource "../../../.novacore/packages" `
        -Description "SwordMaster"
    $novaCoreLock = Get-VerifiedNovaCoreLock
    $swordMasterProjectPaths = @(
        $expectedProjects |
            ForEach-Object { "$serverRoot/$_" }
    )
    Test-NovaCoreProjectPackageReferences `
        -ProjectPaths $swordMasterProjectPaths `
        -NovaCoreLock $novaCoreLock

    $expectedPackageReferences = [ordered]@{
        "src/SwordMaster.Domain/SwordMaster.Domain.csproj" = @(
            "NovaCore.BuildingBlocks"
        )
        "src/SwordMaster.Application.Contracts/SwordMaster.Application.Contracts.csproj" = @(
            "NovaCore.BuildingBlocks",
            "NovaCore.Shared"
        )
        "src/SwordMaster.Application/SwordMaster.Application.csproj" = @(
            "Microsoft.Extensions.Options.ConfigurationExtensions",
            "NovaCore.BuildingBlocks"
        )
        "src/SwordMaster.Host.EntityFrameworkCore/SwordMaster.Host.EntityFrameworkCore.csproj" = @(
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.EntityFrameworkCore.Relational",
            "Pomelo.EntityFrameworkCore.MySql",
            "NovaCore.EntityFrameworkCore"
        )
        "src/SwordMaster.Api/SwordMaster.Api.csproj" = @(
            "NovaCore.BuildingBlocks"
        )
        "src/SwordMaster.DbMigrator/SwordMaster.DbMigrator.csproj" = @(
            "Microsoft.Extensions.Hosting",
            "NovaCore.BuildingBlocks"
        )
        "tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj" = @(
            "Microsoft.NET.Test.Sdk",
            "xunit",
            "xunit.runner.visualstudio"
        )
        "tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj" = @(
            "Microsoft.NET.Test.Sdk",
            "xunit",
            "xunit.runner.visualstudio"
        )
        "tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" = @(
            "Microsoft.AspNetCore.Mvc.Testing",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.NET.Test.Sdk",
            "xunit",
            "xunit.runner.visualstudio"
        )
    }
    foreach ($projectPath in $expectedPackageReferences.Keys) {
        $repositoryProjectPath = "$serverRoot/$projectPath"
        Assert-StringSet `
            -Expected $expectedPackageReferences[$projectPath] `
            -Actual (Get-PackageReferenceNames $repositoryProjectPath) `
            -Message "Invalid M1-04 package references for '$repositoryProjectPath'."
    }

    $expectedReferences = [ordered]@{
        "src/SwordMaster.Domain/SwordMaster.Domain.csproj" = @()
        "src/SwordMaster.Application.Contracts/SwordMaster.Application.Contracts.csproj" = @()
        "src/SwordMaster.Application/SwordMaster.Application.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Application.Contracts/SwordMaster.Application.Contracts.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Domain/SwordMaster.Domain.csproj",
            "server/src/NovaGame.Application.Contracts/NovaGame.Application.Contracts.csproj",
            "server/src/NovaGame.Domain/NovaGame.Domain.csproj"
        )
        "src/SwordMaster.Host.EntityFrameworkCore/SwordMaster.Host.EntityFrameworkCore.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Application/SwordMaster.Application.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Domain/SwordMaster.Domain.csproj"
        )
        "src/SwordMaster.Api/SwordMaster.Api.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Application/SwordMaster.Application.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/SwordMaster.Host.EntityFrameworkCore.csproj",
            "server/src/NovaGame.Api/NovaGame.Api.csproj",
            "server/src/NovaGame.Deployment/NovaGame.Deployment.csproj"
        )
        "src/SwordMaster.DbMigrator/SwordMaster.DbMigrator.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Application/SwordMaster.Application.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/SwordMaster.Host.EntityFrameworkCore.csproj",
            "server/src/NovaGame.Application/NovaGame.Application.csproj",
            "server/src/NovaGame.Host.EntityFrameworkCore/NovaGame.Host.EntityFrameworkCore.csproj",
            "server/src/NovaGame.Deployment/NovaGame.Deployment.csproj"
        )
        "tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Domain/SwordMaster.Domain.csproj"
        )
        "tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Application/SwordMaster.Application.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Application.Contracts/SwordMaster.Application.Contracts.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Domain/SwordMaster.Domain.csproj"
        )
        "tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" = @(
            "samples/SwordMaster/server/src/SwordMaster.Api/SwordMaster.Api.csproj",
            "samples/SwordMaster/server/src/SwordMaster.DbMigrator/SwordMaster.DbMigrator.csproj",
            "samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/SwordMaster.Host.EntityFrameworkCore.csproj"
        )
    }
    foreach ($projectPath in $expectedReferences.Keys) {
        $repositoryProjectPath = "$serverRoot/$projectPath"
        Assert-StringSet `
            -Expected $expectedReferences[$projectPath] `
            -Actual (Get-ResolvedProjectReferencePaths $repositoryProjectPath) `
            -Message "Invalid current-stage ProjectReference direction for '$repositoryProjectPath'."
    }

    $swordMasterApplicationModule = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Application/SwordMasterApplicationModule.cs" -Raw
    $snapshotParser = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigSnapshotParser.cs" -Raw
    $snapshotContract = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Application/PlayerAssets/PlayerAssetsGameConfigContract.cs" -Raw
    Assert-Condition `
        $swordMasterApplicationModule.Contains("NovaGameDomainModule") `
        "SwordMaster Application must compose the extracted NovaGame domain service explicitly."
    foreach ($requiredParserTerm in @(
        "GameConfigSnapshotValidator",
        "CanonicalContentJson"
    )) {
        Assert-Condition `
            $snapshotParser.Contains($requiredParserTerm) `
            "SwordMaster snapshot parser is missing extracted-validator usage '$requiredParserTerm'."
    }
    foreach ($obsoleteParserTerm in @(
        "SHA256",
        "SnapshotPropertyNames",
        "IntegrityPropertyNames",
        "TryGetNonEmptyGuid",
        "TryGetUtcDateTime",
        "IsLowercaseSha256",
        "ComputeHash"
    )) {
        Assert-Condition `
            (-not $snapshotParser.Contains($obsoleteParserTerm)) `
            "SwordMaster snapshot parser retains duplicate neutral validation '$obsoleteParserTerm'."
    }
    foreach ($obsoleteContractTerm in @(
        "SnapshotFormatVersion",
        "ContentHashAlgorithm",
        "CanonicalizationProfile"
    )) {
        Assert-Condition `
            (-not $snapshotContract.Contains($obsoleteContractTerm)) `
            "SwordMaster GameConfig contract retains extracted protocol constant '$obsoleteContractTerm'."
    }

    foreach ($innerProjectPath in @(
        "src/SwordMaster.Domain/SwordMaster.Domain.csproj",
        "src/SwordMaster.Application.Contracts/SwordMaster.Application.Contracts.csproj",
        "src/SwordMaster.Application/SwordMaster.Application.csproj"
    )) {
        $repositoryProjectPath = "$serverRoot/$innerProjectPath"
        $novaGamePackageReferences = @(
            Get-PackageReferenceNames $repositoryProjectPath |
                Where-Object { $_.StartsWith("NovaGame.", [StringComparison]::Ordinal) }
        )
        Assert-Condition `
            ($novaGamePackageReferences.Count -eq 0) `
            "Inner SwordMaster project '$repositoryProjectPath' must not consume NovaGame implementation packages: $($novaGamePackageReferences -join ', ')."
        $forbiddenInfrastructurePackages = @(
            Get-PackageReferenceNames $repositoryProjectPath |
                Where-Object {
                    $_ -match "EntityFrameworkCore|AspNetCore|Pomelo|MySql"
                }
        )
        Assert-Condition `
            ($forbiddenInfrastructurePackages.Count -eq 0) `
            "Inner SwordMaster project '$repositoryProjectPath' references infrastructure package(s): $($forbiddenInfrastructurePackages -join ', ')."
    }

    foreach ($novaGameProject in Get-ChildItem -LiteralPath "server" -Recurse -File -Filter "*.csproj") {
        $swordMasterReferences = @(
            Get-ResolvedProjectReferencePaths $novaGameProject.FullName |
                Where-Object {
                    $_.StartsWith("samples/SwordMaster/", [StringComparison]::OrdinalIgnoreCase)
                }
        )
        Assert-Condition `
            ($swordMasterReferences.Count -eq 0) `
            "NovaGame project '$($novaGameProject.FullName)' must not reference SwordMaster: $($swordMasterReferences -join ', ')."
    }
    foreach ($novaGameSolution in @("NovaGame.sln", "server/NovaGame.sln")) {
        $swordMasterProjects = @(
            Get-SolutionProjectPaths $novaGameSolution |
                Where-Object { $_ -match '(?i)(^|/)SwordMaster(?:\.|/)' }
        )
        Assert-Condition `
            ($swordMasterProjects.Count -eq 0) `
            "NovaGame solution '$novaGameSolution' must not include SwordMaster projects: $($swordMasterProjects -join ', ')."
    }

    $playerAssetsAppService = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Application/PlayerAssets/PlayerAssetsAppService.cs" `
        -Raw
    Assert-Condition `
        $playerAssetsAppService.Contains("IPlayerAssetsTransactionManager") `
        "PlayerAssets mutations must use the SwordMaster-owned transaction boundary."
    Assert-Condition `
        (-not $playerAssetsAppService.Contains("IUnitOfWorkManager")) `
        "PlayerAssets must not enlist every composed DbContext through NovaCore's generic unit of work."

    $entityFrameworkRegistration = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Host.EntityFrameworkCore/DependencyInjection/SwordMasterEntityFrameworkCoreServiceCollectionExtensions.cs" `
        -Raw
    Assert-Condition `
        $entityFrameworkRegistration.Contains(
            "AddScoped<IPlayerAssetsTransactionManager, PlayerAssetsTransactionManager>") `
        "SwordMaster EF must register the game-owned PlayerAssets transaction manager."

    $apiModule = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/SwordMasterApiModule.cs" `
        -Raw
    Assert-Condition `
        $apiModule.Contains("services.AddSwordMasterPlayerAssetsApi(configuration);") `
        "SwordMaster API composition must register the PlayerAssets API boundary."

    [xml]$apiProject = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/SwordMaster.Api.csproj" `
        -Raw
    Assert-Condition `
        ([string]$apiProject.Project.Sdk -eq "Microsoft.NET.Sdk.Web") `
        "SwordMaster API must be an executable ASP.NET Core Web project."
    $apiProgram = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/Program.cs" `
        -Raw
    Assert-Condition `
        ($apiProgram.Contains("NovaGameProductionConfigurationValidator.Validate(") -and
            $apiProgram.Contains("SwordMasterDatabaseConfigurationValidator.Validate(") -and
            $apiProgram.Contains("AddNovaCoreModules<SwordMasterApiModule>") -and
            $apiProgram.Contains("app.UseNovaCoreModules();") -and
            $apiProgram.Contains("namespace SwordMaster.Api;") -and
            $apiProgram.Contains("public partial class Program")) `
        "SwordMaster API Program must compose and expose the real SwordMaster module host."

    $apiSettings = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/appsettings.json" `
        -Raw | ConvertFrom-Json
    Assert-StringSet `
        -Expected @("NovaGame", "SwordMaster") `
        -Actual @($apiSettings.ConnectionStrings.PSObject.Properties.Name) `
        -Message "SwordMaster API must declare exactly the NovaGame and SwordMaster connection strings."
    Assert-Condition `
        ([string]$apiSettings.NovaGame.EntityFrameworkCore.ConnectionStringName -eq "NovaGame" -and
            [string]$apiSettings.SwordMaster.EntityFrameworkCore.ConnectionStringName -eq "SwordMaster") `
        "SwordMaster API DbContexts must use their explicitly named connection strings."

    [xml]$dbMigratorProject = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.DbMigrator/SwordMaster.DbMigrator.csproj" `
        -Raw
    $dbMigratorOutputTypes = @(
        $dbMigratorProject.SelectNodes("/Project/PropertyGroup/OutputType") |
            ForEach-Object { $_.InnerText.Trim() }
    )
    Assert-Condition `
        ($dbMigratorOutputTypes.Count -eq 1 -and $dbMigratorOutputTypes[0] -eq "Exe") `
        "SwordMaster DbMigrator must remain an executable project."
    $dbMigratorProgram = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.DbMigrator/Program.cs" `
        -Raw
    $novaGameMigrationIndex = $dbMigratorProgram.IndexOf(
        "GetRequiredService<NovaGameDbContext>()",
        [StringComparison]::Ordinal)
    $swordMasterMigrationIndex = $dbMigratorProgram.IndexOf(
        "GetRequiredService<SwordMasterDbContext>()",
        [StringComparison]::Ordinal)
    $administratorSeedCallIndex = $dbMigratorProgram.IndexOf(
        "await SeedNovaGameAdministratorAsync(host.Services);",
        [StringComparison]::Ordinal)
    $swordMasterSeedGateIndex = $dbMigratorProgram.IndexOf(
        "if (ShouldRunSwordMasterDevelopmentSeed(builder.Environment))",
        [StringComparison]::Ordinal)
    Assert-Condition `
        ($dbMigratorProgram.Contains("AddNovaCoreModules<SwordMasterDbMigratorModule>") -and
            $dbMigratorProgram.Contains("SwordMasterDbMigratorConfigurationValidator.Validate(") -and
            -not $dbMigratorProgram.Contains("NovaGameProductionConfigurationValidator.Validate(") -and
            $novaGameMigrationIndex -ge 0 -and
            $swordMasterMigrationIndex -gt $novaGameMigrationIndex -and
            $administratorSeedCallIndex -ge 0 -and
            $swordMasterSeedGateIndex -gt $administratorSeedCallIndex -and
            $dbMigratorProgram.Contains("environment.IsDevelopment()") -and
            $dbMigratorProgram.Contains('environment.IsEnvironment("Testing")') -and
            -not $dbMigratorProgram.Contains("IDataSeedManager")) `
        "SwordMaster DbMigrator must migrate NovaGame then SwordMaster and keep seed transactions database-specific."

    $dbMigratorConfigurationValidator = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.DbMigrator/SwordMasterDbMigratorConfigurationValidator.cs" `
        -Raw
    Assert-Condition `
        ($dbMigratorConfigurationValidator.Contains(
                "SwordMasterDatabaseConfigurationValidator.Validate(configuration);") -and
            $dbMigratorConfigurationValidator.Contains("AdminAccessSeedOptions.SectionName") -and
            $dbMigratorConfigurationValidator.Contains(
                "Administrator seed configuration requires both a user name and a password.") -and
            $dbMigratorConfigurationValidator.Contains(
                "Administrator seed passwords must contain")) `
        "SwordMaster DbMigrator must validate only its databases and configured administrator seed credentials."

    $dbMigratorSettings = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.DbMigrator/appsettings.json" `
        -Raw | ConvertFrom-Json
    Assert-StringSet `
        -Expected @("NovaGame", "SwordMaster") `
        -Actual @($dbMigratorSettings.ConnectionStrings.PSObject.Properties.Name) `
        -Message "SwordMaster DbMigrator must declare exactly the NovaGame and SwordMaster connection strings."
    Assert-Condition `
        ([string]$dbMigratorSettings.NovaGame.EntityFrameworkCore.ConnectionStringName -eq "NovaGame" -and
            [string]$dbMigratorSettings.SwordMaster.EntityFrameworkCore.ConnectionStringName -eq "SwordMaster" -and
            $null -ne $dbMigratorSettings.SwordMaster.PlayerAssets.DevelopmentSeed) `
        "SwordMaster DbMigrator must preserve separate database settings and the validation-profile seed contract."

    $developmentSeedRunner = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.DbMigrator/PlayerAssets/SwordMasterPlayerAssetsDevelopmentSeedRunner.cs" `
        -Raw
    Assert-Condition `
        ($developmentSeedRunner.Contains("IPlayerAssetsTransactionManager") -and
            $developmentSeedRunner.Contains("FindByPlayerIdForUpdateAsync") -and
            $developmentSeedRunner.Contains("existingProfile is null")) `
        "SwordMaster validation-profile seeding must be idempotent and use the SwordMaster-only transaction boundary."

    $databaseConfigurationValidator = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Host.EntityFrameworkCore/DependencyInjection/SwordMasterDatabaseConfigurationValidator.cs" `
        -Raw
    Assert-Condition `
        ($databaseConfigurationValidator.Contains("ConnectionStrings:NovaGame") -and
            $databaseConfigurationValidator.Contains("ConnectionStrings:SwordMaster") -and
            $databaseConfigurationValidator.Contains("must target distinct MySQL databases") -and
            $databaseConfigurationValidator.Contains("ServerVersion") -and
            $databaseConfigurationValidator.Contains("8.4.x")) `
        "SwordMaster runtime composition must validate separate MySQL 8.4 database targets."

    $playerAuthentication = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/SwordMasterPlayerAuthentication.cs" `
        -Raw
    Assert-Condition `
        ($playerAuthentication.Contains('public const string Scheme = "SwordMasterPlayer";') -and
            $playerAuthentication.Contains(".AddAuthentication()") -and
            $playerAuthentication.Contains(".AddJwtBearer(Scheme") -and
            $playerAuthentication.Contains("AuthenticationType = Scheme")) `
        "SwordMaster player authentication must remain a named scheme without replacing the administrator default."

    $playerAssetsController = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/PlayerAssetsController.cs" `
        -Raw
    foreach ($requiredControllerContract in @(
        "[Authorize(AuthenticationSchemes = SwordMasterPlayerAuthentication.Scheme)]",
        "[UnitOfWork(IsDisabled = true)]",
        '[HttpPut("characters/{characterId:guid}/equipment/{slot}")]',
        '[HttpDelete("characters/{characterId:guid}/equipment/{slot}")]',
        '[HttpGet("characters/{characterId:guid}/attributes")]',
        'public const string IdempotencyKeyHeaderName = "Idempotency-Key";',
        'public const string IfMatchHeaderName = "If-Match";'
    )) {
        Assert-Condition `
            $playerAssetsController.Contains($requiredControllerContract) `
            "SwordMaster PlayerAssets controller is missing required contract '$requiredControllerContract'."
    }

    $currentPlayerContext = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/HttpCurrentPlayerContext.cs" `
        -Raw
    Assert-Condition `
        ($currentPlayerContext.Contains("identities.Length != 1") -and
            $currentPlayerContext.Contains("identity.AuthenticationType") -and
            $currentPlayerContext.Contains("SecurityClaimNames.UserId") -and
            $currentPlayerContext.Contains("TokenUseClaim")) `
        "SwordMaster current-player resolution must use one named identity with player sub and token-use claims."

    $playerOpenApi = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/PlayerAssetsOpenApiOperationFilter.cs" `
        -Raw
    Assert-Condition `
        ($playerOpenApi.Contains("operation.Security =") -and
            $playerOpenApi.Contains("Id = SwordMasterPlayerAuthentication.Scheme") -and
            $playerOpenApi.Contains("PlayerAssetsController.IfMatchHeaderName") -and
            $playerOpenApi.Contains("PlayerAssetsController.IdempotencyKeyHeaderName")) `
        "SwordMaster OpenAPI must document player security and mutation headers."

    Write-Output "SwordMaster server architecture verification passed."
}

function Invoke-DotNetFilteredTestWithExecutionCount {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$Filter,
        [Parameter(Mandatory)][string]$ResultName
    )

    $resultsRoot = Join-Path `
        $localRoot `
        ("test-results\swordmaster-" + [Guid]::NewGuid().ToString("N"))
    $resolvedResultsRoot = [System.IO.Path]::GetFullPath($resultsRoot)
    $resolvedLocalRoot = [System.IO.Path]::GetFullPath($localRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        $resolvedResultsRoot.StartsWith(
            $resolvedLocalRoot,
            [StringComparison]::OrdinalIgnoreCase) `
        "Focused test results must remain under the repository-local '.local' directory."

    New-Item -ItemType Directory -Path $resolvedResultsRoot -Force | Out-Null
    $trxPath = Join-Path $resolvedResultsRoot $ResultName
    try {
        Invoke-CheckedCommand "dotnet" @(
            "test",
            $ProjectPath,
            "--no-build",
            "--configuration", "Release",
            "--filter", $Filter,
            "--logger", "trx;LogFileName=$ResultName",
            "--results-directory", $resolvedResultsRoot,
            "--verbosity", "minimal",
            "-p:NuGetAudit=false"
        )
        Assert-Condition `
            (Test-Path -LiteralPath $trxPath -PathType Leaf) `
            "Focused test run did not produce '$trxPath'."

        [xml]$testResult = Get-Content -LiteralPath $trxPath -Raw
        $counters = $testResult.SelectSingleNode(
            "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
        Assert-Condition `
            ($null -ne $counters) `
            "Focused test result '$trxPath' is missing execution counters."
        $executedCount = [int]$counters.GetAttribute("executed")
        $passedCount = [int]$counters.GetAttribute("passed")
        $failedCount = [int]$counters.GetAttribute("failed")
        Assert-Condition `
            ($executedCount -gt 0) `
            "Focused test filter '$Filter' executed zero tests for '$ProjectPath'."
        Assert-Condition `
            ($failedCount -eq 0 -and $passedCount -eq $executedCount) `
            "Focused test result for '$ProjectPath' was not completely successful: executed=$executedCount, passed=$passedCount, failed=$failedCount."
        Write-Output `
            "Focused test verification passed for '$ProjectPath': $executedCount test(s) executed."
    }
    finally {
        if (Test-Path -LiteralPath $resolvedResultsRoot -PathType Container) {
            Remove-Item -LiteralPath $resolvedResultsRoot -Recurse -Force
        }
    }
}

function Invoke-SwordMasterVitestWithExecutionCount {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)][string]$ScriptName,
        [Parameter(Mandatory)][int]$ExpectedTestCount,
        [Parameter(Mandatory)][string]$ResultName,
        [System.Collections.IDictionary]$IsolatedEnvironment
    )

    $resultsRoot = Join-Path `
        $localRoot `
        ("test-results\swordmaster-vitest-" + [Guid]::NewGuid().ToString("N"))
    $resolvedResultsRoot = [System.IO.Path]::GetFullPath($resultsRoot)
    $resolvedLocalRoot = [System.IO.Path]::GetFullPath($localRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        $resolvedResultsRoot.StartsWith(
            $resolvedLocalRoot,
            [StringComparison]::OrdinalIgnoreCase) `
        "Focused Vitest results must remain under the repository-local '.local' directory."

    New-Item -ItemType Directory -Path $resolvedResultsRoot -Force | Out-Null
    $resultPath = Join-Path $resolvedResultsRoot $ResultName
    try {
        $arguments = @(
            "run", $ScriptName,
            "--reporter=json", "--outputFile", $resultPath)
        if ($null -eq $IsolatedEnvironment) {
            Invoke-SwordMasterPnpm `
                -Toolchain $Toolchain `
                -ArgumentList $arguments
        }
        else {
            Invoke-SwordMasterPnpmIsolated `
                -Toolchain $Toolchain `
                -ArgumentList $arguments `
                -Environment $IsolatedEnvironment
        }
        Assert-Condition `
            (Test-Path -LiteralPath $resultPath -PathType Leaf) `
            "Focused Vitest run did not produce '$resultPath'."

        $testResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        foreach ($requiredCounter in @(
            "numTotalTests", "numPassedTests", "numFailedTests", "numPendingTests"
        )) {
            Assert-Condition `
                ($null -ne $testResult.PSObject.Properties[$requiredCounter]) `
                "Focused Vitest result '$resultPath' is missing '$requiredCounter'."
        }

        $totalCount = [int]$testResult.numTotalTests
        $passedCount = [int]$testResult.numPassedTests
        $failedCount = [int]$testResult.numFailedTests
        $pendingCount = [int]$testResult.numPendingTests
        Assert-Condition `
            ($totalCount -eq $ExpectedTestCount) `
            "Focused Vitest script '$ScriptName' must execute exactly $ExpectedTestCount tests; executed=$totalCount."
        Assert-Condition `
            ($passedCount -eq $ExpectedTestCount -and
                $failedCount -eq 0 -and
                $pendingCount -eq 0) `
            "Focused Vitest result for '$ScriptName' was not completely successful: total=$totalCount, passed=$passedCount, failed=$failedCount, pending=$pendingCount."
        Write-Output `
            "Focused Vitest verification passed for '$ScriptName': $totalCount test(s) executed."
    }
    finally {
        if (Test-Path -LiteralPath $resolvedResultsRoot -PathType Container) {
            Remove-Item -LiteralPath $resolvedResultsRoot -Recurse -Force
        }
    }
}

function Test-SwordMasterPlayerAssetsGeneratedTypes {
    param([Parameter(Mandatory)]$Toolchain)

    $trackedRoot = Join-Path `
        $repositoryRoot `
        "samples\SwordMaster\src\generated\player-assets"
    $generatedParent = Join-Path $localRoot "generated"
    $temporaryRoot = Join-Path `
        $generatedParent `
        "swordmaster-player-assets-$([Guid]::NewGuid().ToString('N'))"
    $resolvedGeneratedParent = [System.IO.Path]::GetFullPath(
        $generatedParent).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    Assert-Condition `
        $resolvedTemporaryRoot.StartsWith(
            $resolvedGeneratedParent,
            [StringComparison]::OrdinalIgnoreCase) `
        "Temporary PlayerAssets generation must remain under '$resolvedGeneratedParent'."

    $previousOutputPath = $env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT
    try {
        $env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT = $resolvedTemporaryRoot
        Invoke-SwordMasterPnpm `
            -Toolchain $Toolchain `
            -ArgumentList @("run", "contract:generate")

        $trackedFiles = @(
            Get-ChildItem -LiteralPath $trackedRoot -File -Recurse)
        $generatedFiles = @(
            Get-ChildItem -LiteralPath $resolvedTemporaryRoot -File -Recurse)
        $trackedRelativePaths = @(
            $trackedFiles | ForEach-Object {
                [System.IO.Path]::GetRelativePath($trackedRoot, $_.FullName)
            })
        $generatedRelativePaths = @(
            $generatedFiles | ForEach-Object {
                [System.IO.Path]::GetRelativePath(
                    $resolvedTemporaryRoot,
                    $_.FullName)
            })
        Assert-StringSet `
            -Expected $trackedRelativePaths `
            -Actual $generatedRelativePaths `
            -Message "Generated PlayerAssets TypeScript file set is stale."

        foreach ($relativePath in $trackedRelativePaths) {
            $trackedPath = Join-Path $trackedRoot $relativePath
            $generatedPath = Join-Path $resolvedTemporaryRoot $relativePath
            Assert-Condition `
                ((Get-FileHash -LiteralPath $trackedPath -Algorithm SHA256).Hash -eq
                    (Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256).Hash) `
                "Generated PlayerAssets TypeScript file '$relativePath' is stale."
        }
    }
    finally {
        $env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT = $previousOutputPath
        if (Test-Path -LiteralPath $resolvedTemporaryRoot -PathType Container) {
            Assert-Condition `
                $resolvedTemporaryRoot.StartsWith(
                    $resolvedGeneratedParent,
                    [StringComparison]::OrdinalIgnoreCase) `
                "Refusing to remove temporary output outside '$resolvedGeneratedParent'."
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
    }

    Write-Output "SwordMaster PlayerAssets generated types are current."
}

function ConvertTo-SwordMasterJwtBase64Url {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function ConvertFrom-SwordMasterJwtBase64Url {
    param([Parameter(Mandatory)][string]$Value)

    $base64 = $Value.Replace("-", "+").Replace("_", "/")
    $paddingLength = (4 - ($base64.Length % 4)) % 4
    if ($paddingLength -gt 0) {
        $base64 += [string]::new([char]"=", $paddingLength)
    }
    return [Convert]::FromBase64String($base64)
}

function New-SwordMasterPlayerAcceptanceToken {
    param(
        [Parameter(Mandatory)][string]$Issuer,
        [Parameter(Mandatory)][string]$Audience,
        [Parameter(Mandatory)][string]$SigningKey,
        [Parameter(Mandatory)][Guid]$PlayerId
    )

    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $headerJson = [ordered]@{
        alg = "HS256"
        typ = "JWT"
    } | ConvertTo-Json -Compress
    $payloadJson = [ordered]@{
        iss = $Issuer
        aud = $Audience
        sub = $PlayerId.ToString("D")
        token_use = "player"
        nbf = $now - 30
        iat = $now
        exp = $now + 300
        jti = [Guid]::NewGuid().ToString("D")
    } | ConvertTo-Json -Compress

    $header = ConvertTo-SwordMasterJwtBase64Url `
        -Bytes ([System.Text.Encoding]::UTF8.GetBytes($headerJson))
    $payload = ConvertTo-SwordMasterJwtBase64Url `
        -Bytes ([System.Text.Encoding]::UTF8.GetBytes($payloadJson))
    $unsignedToken = "$header.$payload"
    $hmac = [System.Security.Cryptography.HMACSHA256]::new(
        (ConvertFrom-SwordMasterJwtBase64Url -Value $SigningKey))
    try {
        $signature = ConvertTo-SwordMasterJwtBase64Url `
            -Bytes ($hmac.ComputeHash(
                [System.Text.Encoding]::UTF8.GetBytes($unsignedToken)))
    }
    finally {
        $hmac.Dispose()
    }

    return "$unsignedToken.$signature"
}

function Invoke-SwordMasterAcceptanceApiRequest {
    param(
        [Parameter(Mandatory)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][Uri]$Uri,
        [System.Collections.IDictionary]$Body,
        [string]$AccessToken
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $Uri)
    $response = $null
    try {
        if ($null -ne $Body) {
            $source = $Body | ConvertTo-Json -Depth 20 -Compress
            $request.Content = [System.Net.Http.StringContent]::new(
                $source,
                [System.Text.Encoding]::UTF8,
                "application/json")
        }
        if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
            $request.Headers.Authorization = `
                [System.Net.Http.Headers.AuthenticationHeaderValue]::new(
                    "Bearer",
                    $AccessToken)
        }

        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        $responseSource = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Assert-Condition `
            $response.IsSuccessStatusCode `
            "SwordMaster acceptance API request '$Method $($Uri.AbsolutePath)' failed with status $([int]$response.StatusCode)."
        try {
            $envelope = $responseSource | ConvertFrom-Json -Depth 100
        }
        catch {
            throw "SwordMaster acceptance API request '$Method $($Uri.AbsolutePath)' returned invalid JSON."
        }
        $succeeded = $envelope.PSObject.Properties["succeeded"]
        $error = $envelope.PSObject.Properties["error"]
        $data = $envelope.PSObject.Properties["data"]
        Assert-Condition `
            ($null -ne $succeeded -and [bool]$succeeded.Value) `
            "SwordMaster acceptance API request '$Method $($Uri.AbsolutePath)' returned an unsuccessful envelope."
        Assert-Condition `
            ($null -eq $error -or $null -eq $error.Value) `
            "SwordMaster acceptance API request '$Method $($Uri.AbsolutePath)' returned an unexpected error envelope."
        Assert-Condition `
            ($null -ne $data -and $null -ne $data.Value) `
            "SwordMaster acceptance API request '$Method $($Uri.AbsolutePath)' returned no data."
        Write-Output -NoEnumerate $data.Value
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
        $request.Dispose()
    }
}

function Publish-SwordMasterPlayerAssetsAcceptanceConfiguration {
    param(
        [Parameter(Mandatory)][string]$ApiUrl,
        [Parameter(Mandatory)][string]$AdministratorUserName,
        [Parameter(Mandatory)][string]$AdministratorPassword
    )

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    try {
        $baseUri = [Uri]::new($ApiUrl.TrimEnd("/") + "/")
        $login = Invoke-SwordMasterAcceptanceApiRequest `
            -Client $client `
            -Method "POST" `
            -Uri ([Uri]::new($baseUri, "api/auth/login")) `
            -Body ([ordered]@{
                userName = $AdministratorUserName
                password = $AdministratorPassword
            })
        $administratorToken = [string]$login.accessToken
        Assert-Condition `
            (-not [string]::IsNullOrWhiteSpace($administratorToken)) `
            "SwordMaster acceptance administrator login returned no access token."

        $configurationContent = [ordered]@{
            collections = [ordered]@{
                "equipment-items" = @(
                    [ordered]@{
                        id = "training-sword"
                        slot = "weapon"
                        modifiers = @(
                            [ordered]@{ statCode = "attack"; value = 7 },
                            [ordered]@{ statCode = "health"; value = 20 }
                        )
                    }
                )
            }
        } | ConvertTo-Json -Depth 10 -Compress
        $draft = Invoke-SwordMasterAcceptanceApiRequest `
            -Client $client `
            -Method "POST" `
            -Uri ([Uri]::new($baseUri, "api/game-config/drafts")) `
            -AccessToken $administratorToken `
            -Body ([ordered]@{
                configurationSet = "swordmaster"
                name = "player-assets-live-acceptance"
                schemaVersion = "swordmaster.player-assets.v1"
                content = $configurationContent
            })
        $draftId = [Guid]::Parse([string]$draft.id)
        $draftRevision = [long]$draft.revision

        $validation = Invoke-SwordMasterAcceptanceApiRequest `
            -Client $client `
            -Method "POST" `
            -Uri ([Uri]::new(
                $baseUri,
                "api/game-config/drafts/$($draftId.ToString('D'))/validate")) `
            -AccessToken $administratorToken `
            -Body ([ordered]@{ expectedRevision = $draftRevision })
        $validationIssues = $validation.PSObject.Properties["issues"]
        $validationIssueCount = if ($null -eq $validationIssues) {
            0
        }
        else {
            @($validationIssues.Value).Count
        }
        Assert-Condition `
            ([bool]$validation.isValid -and $validationIssueCount -eq 0) `
            "SwordMaster PlayerAssets acceptance configuration did not validate."

        $version = Invoke-SwordMasterAcceptanceApiRequest `
            -Client $client `
            -Method "POST" `
            -Uri ([Uri]::new(
                $baseUri,
                "api/game-config/drafts/$($draftId.ToString('D'))/publish")) `
            -AccessToken $administratorToken `
            -Body ([ordered]@{
                expectedRevision = $draftRevision
                releaseNotes = "PlayerAssets live acceptance configuration"
            })
        $versionId = [Guid]::Parse([string]$version.id)

        $activation = Invoke-SwordMasterAcceptanceApiRequest `
            -Client $client `
            -Method "POST" `
            -Uri ([Uri]::new(
                $baseUri,
                "api/game-config/environments/development/activate")) `
            -AccessToken $administratorToken `
            -Body ([ordered]@{
                configurationSet = "swordmaster"
                versionId = $versionId.ToString("D")
                expectedRevision = 0
                reason = "PlayerAssets live acceptance activation"
            })
        Assert-Condition `
            ([long]$activation.revision -eq 1 -and
                [Guid]::Parse([string]$activation.activeVersionId) -eq $versionId) `
            "SwordMaster PlayerAssets acceptance configuration activation is inconsistent."

        return $versionId.ToString("D")
    }
    finally {
        $client.Dispose()
    }
}

function Invoke-SwordMasterPlayerAssetsRealApiAcceptance {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)][string]$ApiUrl,
        [Parameter(Mandatory)][string]$PlayerToken,
        [Parameter(Mandatory)][string]$ConfigurationVersionId
    )

    $acceptanceEnvironment = [ordered]@{
        SWORDMASTER_ACCEPTANCE_API_URL = $ApiUrl
        SWORDMASTER_ACCEPTANCE_PLAYER_TOKEN = $PlayerToken
        SWORDMASTER_ACCEPTANCE_CHARACTER_ID = `
            "22222222-2222-2222-2222-222222222222"
        SWORDMASTER_ACCEPTANCE_OWNED_ITEM_ID = `
            "33333333-3333-3333-3333-333333333333"
        SWORDMASTER_ACCEPTANCE_EQUIPMENT_SLOT = "weapon"
        SWORDMASTER_ACCEPTANCE_CONFIGURED_ITEM_ID = "training-sword"
        SWORDMASTER_ACCEPTANCE_CONFIGURATION_VERSION_ID = `
            $ConfigurationVersionId
    }
    Assert-StringSet `
        -Expected @(
            "SWORDMASTER_ACCEPTANCE_API_URL",
            "SWORDMASTER_ACCEPTANCE_PLAYER_TOKEN",
            "SWORDMASTER_ACCEPTANCE_CHARACTER_ID",
            "SWORDMASTER_ACCEPTANCE_OWNED_ITEM_ID",
            "SWORDMASTER_ACCEPTANCE_EQUIPMENT_SLOT",
            "SWORDMASTER_ACCEPTANCE_CONFIGURED_ITEM_ID",
            "SWORDMASTER_ACCEPTANCE_CONFIGURATION_VERSION_ID"
        ) `
        -Actual @($acceptanceEnvironment.Keys) `
        -Message "SwordMaster PlayerAssets acceptance environment changed unexpectedly."

    Invoke-SwordMasterVitestWithExecutionCount `
        -Toolchain $Toolchain `
        -ScriptName "test:player-assets-real-api" `
        -ExpectedTestCount 1 `
        -ResultName "swordmaster-player-assets-real-api.json" `
        -IsolatedEnvironment $acceptanceEnvironment
    Write-Output "SwordMaster PlayerAssets real API acceptance passed."
}

function Assert-SwordMasterPlayerAssetsAcceptanceDatabaseState {
    param([Parameter(Mandatory)][object[]]$ComposeArguments)

    $playerId = "11111111-1111-1111-1111-111111111111"
    $verificationSql = @(
        "SELECT CONCAT('profile:', Revision) FROM sm_player_assets_profiles WHERE PlayerId = '$playerId';",
        "SELECT CONCAT('assignments:', COUNT(*)) FROM sm_equipment_assignments WHERE PlayerId = '$playerId';",
        "SELECT CONCAT('receipts:', COUNT(*), ':', COALESCE(GROUP_CONCAT(ResultingRevision ORDER BY ResultingRevision SEPARATOR ','), '')) FROM sm_player_asset_command_receipts WHERE PlayerId = '$playerId';"
    ) -join " "
    $databaseCommand = `
        'MYSQL_PWD="$MYSQL_PASSWORD" mysql --protocol=TCP --host=127.0.0.1 --user="$MYSQL_USER" --database=swordmaster_test --batch --skip-column-names --execute="' + `
        $verificationSql + '"'
    $output = @(
        Invoke-CheckedCommandCapture "docker" `
            ($ComposeArguments + @(
                "exec", "--no-TTY", "mysql", "sh", "-c", $databaseCommand)) |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Condition `
        ($output.Count -eq 3 -and
            $output[0] -eq "profile:3" -and
            $output[1] -eq "assignments:0" -and
            $output[2] -eq "receipts:2:2,3") `
        "SwordMaster PlayerAssets acceptance database state is inconsistent."
    Write-Output "SwordMaster PlayerAssets acceptance database state passed."
}

function Test-SwordMasterDatabase {
    param([Parameter(Mandatory)]$Toolchain)

    $sampleServerRoot = "samples/SwordMaster/server"
    $integrationProject = `
        "$sampleServerRoot/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj"

    Invoke-CheckedCommand "docker" @("compose", "version")
    Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")

    $projectName = "swordmaster-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $testRoot = Join-Path $repositoryRoot ".local\database-tests"
    $testEnvironmentFile = Join-Path $testRoot "$projectName.env"
    $novaGameDatabaseName = "novagame_test"
    $swordMasterDatabaseName = "swordmaster_test"
    $testEnvironment = $null
    $composeArguments = @()
    $operationError = $null
    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    $cleanupRequired = $false
    $apiProcess = $null
    $logRoot = $null
    $previousDatabaseEnvironment = $null
    $runtimeEnvironmentNames = @(
        "SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING",
        "SWORDMASTER_TEST_MYSQL_CONNECTION_STRING",
        "SWORDMASTER_TEST_DISPOSABLE_DATABASE_MARKER",
        "SWORDMASTER_MYSQL_MIGRATION_CONNECTION_STRING",
        "ConnectionStrings__NovaGame",
        "ConnectionStrings__SwordMaster",
        "DOTNET_ENVIRONMENT",
        "ASPNETCORE_ENVIRONMENT",
        "ASPNETCORE_URLS",
        "NovaGame__AdminAccess__Jwt__Issuer",
        "NovaGame__AdminAccess__Jwt__Audience",
        "NovaGame__AdminAccess__Jwt__SigningKey",
        "NovaGame__AdminAccess__Jwt__ClockSkew",
        "NovaGame__AdminAccess__Seed__UserName",
        "NovaGame__AdminAccess__Seed__Password",
        "NovaGame__AdminAccess__Seed__DisplayName",
        "SwordMaster__PlayerAuthentication__Jwt__Issuer",
        "SwordMaster__PlayerAuthentication__Jwt__Audience",
        "SwordMaster__PlayerAuthentication__Jwt__SigningKey",
        "SwordMaster__PlayerAuthentication__Jwt__ClockSkew",
        "SwordMaster__GameConfig__Environment",
        "SwordMaster__PlayerAssets__DevelopmentSeed__PlayerId",
        "SwordMaster__PlayerAssets__DevelopmentSeed__CharacterId",
        "SwordMaster__PlayerAssets__DevelopmentSeed__OwnedItemId",
        "SwordMaster__PlayerAssets__DevelopmentSeed__ConfiguredItemId",
        "SwordMaster__PlayerAssets__DevelopmentSeed__BaseAttack",
        "SwordMaster__PlayerAssets__DevelopmentSeed__BaseHealth"
    )
    $previousRuntimeEnvironment = Get-NovaGameProcessEnvironmentSnapshot `
        -Names $runtimeEnvironmentNames

    try {
        Write-NovaGameDatabaseEnvironmentFile `
            -Path $testEnvironmentFile `
            -Database $novaGameDatabaseName `
            -User "swordmaster_test" `
            -Port 0 `
            -AllowEphemeralPort
        $testEnvironment = Read-NovaGameDatabaseEnvironment `
            -Path $testEnvironmentFile `
            -AllowEphemeralPort
        $composeArguments = Get-NovaGameComposeArguments `
            -RepositoryRoot $repositoryRoot `
            -EnvironmentFile $testEnvironmentFile `
            -ProjectName $projectName
        $previousDatabaseEnvironment = Set-NovaGameDatabaseProcessEnvironment `
            -Values $testEnvironment

        Push-Location -LiteralPath (Join-Path $repositoryRoot $sampleServerRoot)
        try {
            Invoke-CheckedCommand "dotnet" @(
                "tool", "restore",
                "--configfile", "NuGet.Config",
                "--ignore-failed-sources"
            )
        }
        finally {
            Pop-Location
        }

        Invoke-CheckedCommand "docker" ($composeArguments + @("config", "--quiet"))
        $cleanupRequired = $true
        Invoke-CheckedCommand "docker" ($composeArguments + @(
            "up", "--detach", "--wait", "--wait-timeout", "180", "mysql"
        ))

        $portOutput = Invoke-CheckedCommandCapture "docker" `
            ($composeArguments + @("port", "mysql", "3306"))
        $publishedEndpoint = @(
            $portOutput |
                Where-Object { $_ -match ":\d+\s*$" } |
                Select-Object -Last 1)
        Assert-Condition `
            ($publishedEndpoint.Count -eq 1) `
            "Unable to resolve the disposable SwordMaster MySQL host port."
        $publishedPort = [int]([regex]::Match(
            [string]$publishedEndpoint[0],
            ":(\d+)\s*$").Groups[1].Value)
        Assert-Condition `
            ($publishedPort -gt 0) `
            "Docker did not allocate a valid SwordMaster MySQL host port."

        $provisionSecondDatabaseCommand = `
            'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql --protocol=TCP --host=127.0.0.1 --user=root --execute="CREATE DATABASE IF NOT EXISTS swordmaster_test CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci; GRANT ALL PRIVILEGES ON swordmaster_test.* TO ''$MYSQL_USER''@''%''; FLUSH PRIVILEGES;"'
        Invoke-CheckedCommand "docker" ($composeArguments + @(
            "exec", "--no-TTY", "mysql", "sh", "-c", $provisionSecondDatabaseCommand
        ))

        $novaGameEnvironment = [ordered]@{}
        $swordMasterEnvironment = [ordered]@{}
        foreach ($entry in $testEnvironment.GetEnumerator()) {
            $novaGameEnvironment[$entry.Key] = $entry.Value
            $swordMasterEnvironment[$entry.Key] = $entry.Value
        }
        $novaGameEnvironment["NOVAGAME_MYSQL_DATABASE"] = $novaGameDatabaseName
        $swordMasterEnvironment["NOVAGAME_MYSQL_DATABASE"] = $swordMasterDatabaseName

        $novaGameConnectionString = New-NovaGameMySqlConnectionString `
            -Environment $novaGameEnvironment `
            -Port $publishedPort
        $swordMasterConnectionString = New-NovaGameMySqlConnectionString `
            -Environment $swordMasterEnvironment `
            -Port $publishedPort
        $env:SWORDMASTER_TEST_NOVAGAME_MYSQL_CONNECTION_STRING = `
            $novaGameConnectionString
        $env:SWORDMASTER_TEST_MYSQL_CONNECTION_STRING = `
            $swordMasterConnectionString
        $env:SWORDMASTER_TEST_DISPOSABLE_DATABASE_MARKER = `
            "swordmaster-test-disposable-v1"
        $env:SWORDMASTER_MYSQL_MIGRATION_CONNECTION_STRING = `
            $swordMasterConnectionString
        $env:ConnectionStrings__NovaGame = $novaGameConnectionString
        $env:ConnectionStrings__SwordMaster = $swordMasterConnectionString
        $env:DOTNET_ENVIRONMENT = "Testing"
        $env:ASPNETCORE_ENVIRONMENT = "Testing"

        $administratorUserName = "runtime-admin"
        $administratorPassword = `
            "Runtime-$([Guid]::NewGuid().ToString('N'))-A1!"
        $env:NovaGame__AdminAccess__Jwt__Issuer = "NovaGame.Acceptance"
        $env:NovaGame__AdminAccess__Jwt__Audience = `
            "NovaGame.Admin.Acceptance"
        $env:NovaGame__AdminAccess__Jwt__SigningKey = `
            New-NovaGameAdminAccessSigningKey
        $env:NovaGame__AdminAccess__Jwt__ClockSkew = "00:00:00"
        $env:NovaGame__AdminAccess__Seed__UserName = `
            $administratorUserName
        $env:NovaGame__AdminAccess__Seed__Password = `
            $administratorPassword
        $env:NovaGame__AdminAccess__Seed__DisplayName = `
            "Runtime Administrator"

        $playerIssuer = "SwordMaster.Acceptance"
        $playerAudience = "SwordMaster.Player.Acceptance"
        $playerSigningKey = New-NovaGameAdminAccessSigningKey
        $env:SwordMaster__PlayerAuthentication__Jwt__Issuer = $playerIssuer
        $env:SwordMaster__PlayerAuthentication__Jwt__Audience = $playerAudience
        $env:SwordMaster__PlayerAuthentication__Jwt__SigningKey = `
            $playerSigningKey
        $env:SwordMaster__PlayerAuthentication__Jwt__ClockSkew = "00:00:00"
        $env:SwordMaster__GameConfig__Environment = "development"
        $env:SwordMaster__PlayerAssets__DevelopmentSeed__PlayerId = `
            "11111111-1111-1111-1111-111111111111"
        $env:SwordMaster__PlayerAssets__DevelopmentSeed__CharacterId = `
            "22222222-2222-2222-2222-222222222222"
        $env:SwordMaster__PlayerAssets__DevelopmentSeed__OwnedItemId = `
            "33333333-3333-3333-3333-333333333333"
        $env:SwordMaster__PlayerAssets__DevelopmentSeed__ConfiguredItemId = `
            "training-sword"
        $env:SwordMaster__PlayerAssets__DevelopmentSeed__BaseAttack = "10"
        $env:SwordMaster__PlayerAssets__DevelopmentSeed__BaseHealth = "100"

        $migratorAssembly = [System.IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot `
                "$sampleServerRoot/src/SwordMaster.DbMigrator/bin/Release/net8.0/SwordMaster.DbMigrator.dll"))
        Assert-Condition `
            (Test-Path -LiteralPath $migratorAssembly -PathType Leaf) `
            "Built SwordMaster DbMigrator assembly is missing."
        $firstMigratorOutput = @(
            Invoke-CheckedCommandCapture "dotnet" @($migratorAssembly))
        $firstMigratorLog = $firstMigratorOutput -join "`n"
        $novaGameMigrationIndex = $firstMigratorLog.IndexOf(
            "Applying NovaGame database migrations.",
            [StringComparison]::Ordinal)
        $swordMasterMigrationIndex = $firstMigratorLog.IndexOf(
            "Applying SwordMaster database migrations.",
            [StringComparison]::Ordinal)
        $administratorSeedIndex = $firstMigratorLog.IndexOf(
            "Running configured NovaGame administrator seed.",
            [StringComparison]::Ordinal)
        $playerAssetsSeedIndex = $firstMigratorLog.IndexOf(
            "Running SwordMaster validation-profile seed in Testing.",
            [StringComparison]::Ordinal)
        Assert-Condition `
            ($novaGameMigrationIndex -ge 0 -and
                $swordMasterMigrationIndex -gt $novaGameMigrationIndex -and
                $administratorSeedIndex -gt $swordMasterMigrationIndex -and
                $playerAssetsSeedIndex -gt $administratorSeedIndex) `
            "Combined migrator output did not preserve NovaGame, SwordMaster, administrator-seed, and validation-profile order."
        $null = Invoke-CheckedCommandCapture "dotnet" @($migratorAssembly)

        Push-Location -LiteralPath (Join-Path $repositoryRoot $sampleServerRoot)
        try {
            Invoke-CheckedCommand "dotnet" @(
                "tool", "run", "dotnet-ef", "migrations", "list",
                "--project", "src/SwordMaster.Host.EntityFrameworkCore",
                "--startup-project", "src/SwordMaster.Host.EntityFrameworkCore",
                "--configuration", "Release",
                "--no-build"
            )
            Invoke-CheckedCommand "dotnet" @(
                "tool", "run", "dotnet-ef", "migrations", "has-pending-model-changes",
                "--project", "src/SwordMaster.Host.EntityFrameworkCore",
                "--startup-project", "src/SwordMaster.Host.EntityFrameworkCore",
                "--configuration", "Release",
                "--no-build"
            )
        }
        finally {
            Pop-Location
        }

        $apiAssembly = [System.IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot `
                "$sampleServerRoot/src/SwordMaster.Api/bin/Release/net8.0/SwordMaster.Api.dll"))
        Assert-Condition `
            (Test-Path -LiteralPath $apiAssembly -PathType Leaf) `
            "Built SwordMaster API assembly is missing."
        $apiPort = Get-AvailableLoopbackPort
        $apiUrl = "http://127.0.0.1:$apiPort"
        $env:ASPNETCORE_URLS = $apiUrl
        $logRoot = Join-Path `
            $repositoryRoot `
            ".local\swordmaster-acceptance\$projectName"
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
        $apiStandardOutputPath = Join-Path $logRoot "api.stdout.log"
        $apiStandardErrorPath = Join-Path $logRoot "api.stderr.log"
        $dotnet = (Get-Command "dotnet" -ErrorAction Stop).Source
        $apiProcess = Start-Process `
            -FilePath $dotnet `
            -ArgumentList @($apiAssembly) `
            -WorkingDirectory (Split-Path -Parent $apiAssembly) `
            -WindowStyle Hidden `
            -RedirectStandardOutput $apiStandardOutputPath `
            -RedirectStandardError $apiStandardErrorPath `
            -PassThru

        Wait-HttpEndpoint `
            -Uri ([Uri]"$apiUrl/health/live") `
            -Process $apiProcess `
            -StandardOutputPath $apiStandardOutputPath `
            -StandardErrorPath $apiStandardErrorPath
        $configurationVersionId = `
            Publish-SwordMasterPlayerAssetsAcceptanceConfiguration `
                -ApiUrl $apiUrl `
                -AdministratorUserName $administratorUserName `
                -AdministratorPassword $administratorPassword
        $playerToken = New-SwordMasterPlayerAcceptanceToken `
            -Issuer $playerIssuer `
            -Audience $playerAudience `
            -SigningKey $playerSigningKey `
            -PlayerId ([Guid]"11111111-1111-1111-1111-111111111111")
        Invoke-SwordMasterPlayerAssetsRealApiAcceptance `
            -Toolchain $Toolchain `
            -ApiUrl $apiUrl `
            -PlayerToken $playerToken `
            -ConfigurationVersionId $configurationVersionId
        $playerToken = $null
        Assert-SwordMasterPlayerAssetsAcceptanceDatabaseState `
            -ComposeArguments $composeArguments

        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~SwordMasterRuntimeDatabaseTests" `
            -ResultName "swordmaster-runtime-databases.trx"
        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~PlayerAssetsMySqlApiTests" `
            -ResultName "swordmaster-mysql-player-api.trx"
        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~PlayerAssetsObservabilityTests" `
            -ResultName "swordmaster-player-assets-observability.trx"
        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~PlayerAssetsPersistenceTests" `
            -ResultName "swordmaster-mysql-player-assets.trx"
    }
    catch {
        $operationError = $_
    }
    finally {
        try {
            Stop-ProcessTree -Process $apiProcess
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to stop the SwordMaster acceptance API process.")
        }

        try {
            Restore-NovaGameProcessEnvironmentSnapshot `
                -Snapshot $previousRuntimeEnvironment
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to restore the SwordMaster process environment.")
        }

        try {
            if ($cleanupRequired) {
                if ($projectName -notmatch "^swordmaster-test-[a-f0-9]{12}$") {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove Docker resources for invalid project '$projectName'.")
                }
                else {
                    & docker @($composeArguments + @(
                        "down", "--volumes", "--remove-orphans"))
                    if ($LASTEXITCODE -ne 0) {
                        $null = $cleanupErrors.Add(
                            "Failed to remove disposable Docker project '$projectName'.")
                    }
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove disposable Docker project '$projectName'.")
        }

        if ($null -ne $previousDatabaseEnvironment) {
            try {
                Restore-NovaGameDatabaseProcessEnvironment `
                    -PreviousValues $previousDatabaseEnvironment
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to restore the MySQL process environment.")
            }
        }

        try {
            if (Test-Path -LiteralPath $testEnvironmentFile -PathType Leaf) {
                $resolvedTestRoot = `
                    [System.IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
                $resolvedTestEnvironmentFile = `
                    [System.IO.Path]::GetFullPath($testEnvironmentFile)
                if (-not $resolvedTestEnvironmentFile.StartsWith(
                        $resolvedTestRoot,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove temporary file outside '$resolvedTestRoot'.")
                }
                else {
                    Remove-Item -LiteralPath $resolvedTestEnvironmentFile -Force
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove temporary environment file '$testEnvironmentFile'.")
        }

        try {
            if (-not [string]::IsNullOrWhiteSpace([string]$logRoot) -and
                (Test-Path -LiteralPath $logRoot -PathType Container)) {
                $acceptanceLogRoot = Join-Path `
                    $repositoryRoot `
                    ".local\swordmaster-acceptance"
                $resolvedAcceptanceLogRoot = `
                    [System.IO.Path]::GetFullPath($acceptanceLogRoot).TrimEnd('\') + '\'
                $resolvedLogRoot = [System.IO.Path]::GetFullPath($logRoot)
                if (-not $resolvedLogRoot.StartsWith(
                        $resolvedAcceptanceLogRoot,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove acceptance logs outside '$resolvedAcceptanceLogRoot'.")
                }
                else {
                    Remove-Item -LiteralPath $resolvedLogRoot -Recurse -Force
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove SwordMaster acceptance logs.")
        }
    }

    if ($null -ne $operationError) {
        if ($cleanupErrors.Count -gt 0) {
            throw "$operationError`nCleanup errors:`n$($cleanupErrors -join "`n")"
        }
        throw $operationError
    }
    if ($cleanupErrors.Count -gt 0) {
        throw "SwordMaster database cleanup failed:`n$($cleanupErrors -join "`n")"
    }

    Write-Output "SwordMaster database verification passed."
}

function Test-SwordMaster {
    param(
        [switch]$SkipRepositoryPrerequisites,
        [switch]$SkipClientPrerequisites
    )

    if (-not $SkipRepositoryPrerequisites) {
        Test-Repository
    }
    else {
        Test-SwordMasterServerArchitecture
    }
    if (-not $SkipClientPrerequisites) {
        Test-Client -SkipRepositoryPrerequisites
    }

    $sampleRoot = "samples/SwordMaster"
    $snapshotPath = "$sampleRoot/config/game-config.snapshot.json"
    $snapshotManifestPath = `
        "$sampleRoot/config/game-config.snapshot.manifest.json"
    foreach ($requiredPath in @(
        "$sampleRoot/package.json",
        "$sampleRoot/pnpm-lock.yaml",
        "$sampleRoot/.node-version",
        "$sampleRoot/openapi-ts.config.mjs",
        "$sampleRoot/contracts/player-assets.openapi.json",
        "$sampleRoot/src/player-assets-cocos-component.ts",
        "$sampleRoot/src/player-assets-http-transport.ts",
        "$sampleRoot/src/player-assets-interaction-model.ts",
        "$sampleRoot/src/generated/player-assets/types.gen.ts",
        "$sampleRoot/tests/player-assets-cocos-component.test.ts",
        "$sampleRoot/tests/player-assets-http-transport.test.ts",
        "$sampleRoot/tests/player-assets-interaction-model.test.ts",
        "$sampleRoot/tests/player-assets-real-api.acceptance.ts",
        "scripts/update-swordmaster-player-assets-contract.ps1",
        $snapshotPath,
        $snapshotManifestPath
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
            "Missing SwordMaster smoke-consumer file '$requiredPath'."
    }
    Assert-Condition `
        (-not (Test-Path -LiteralPath "$sampleRoot/pnpm-workspace.yaml")) `
        "SwordMaster must remain a standalone consumer, not a nested pnpm workspace."

    $samplePackage = Get-Content `
        -LiteralPath "$sampleRoot/package.json" `
        -Raw | ConvertFrom-Json
    Assert-Condition `
        ([string]$samplePackage.packageManager -eq "pnpm@10.33.4") `
        "SwordMaster must pin packageManager to pnpm@10.33.4."
    Assert-Condition `
        ([string]$samplePackage.engines.node -eq "^22.18.0 || ^24.0.0") `
        "SwordMaster must require Node.js ^22.18.0 or ^24.0.0."
    Assert-Condition `
        ((Get-Content -LiteralPath "$sampleRoot/.node-version" -Raw).Trim() -eq `
            "24.14.0") `
        "The SwordMaster .node-version must remain pinned to 24.14.0."
    foreach ($requiredScript in @(
        "contract:generate", "format:check", "lint", "typecheck", "test",
        "test:player-assets-cocos", "test:player-assets-interaction",
        "test:player-assets-real-api",
        "test:player-assets-transport",
        "test:real-api", "build"
    )) {
        Assert-Condition `
            ($null -ne $samplePackage.scripts.PSObject.Properties[$requiredScript]) `
            "The SwordMaster package.json is missing required script '$requiredScript'."
    }
    Assert-Condition `
        ([string]$samplePackage.scripts."contract:generate" -eq `
            "openapi-ts --file openapi-ts.config.mjs") `
        "SwordMaster contract generation must use the pinned OpenAPI config file."
    Assert-Condition `
        ([string]$samplePackage.scripts."test:player-assets-cocos" -eq `
            "vitest run tests/player-assets-cocos-component.test.ts") `
        "SwordMaster focused PlayerAssets Cocos boundary tests must target the approved test file."
    Assert-Condition `
        ([string]$samplePackage.scripts."test:player-assets-interaction" -eq `
            "vitest run tests/player-assets-interaction-model.test.ts") `
        "SwordMaster focused PlayerAssets interaction tests must target the approved test file."
    Assert-Condition `
        ([string]$samplePackage.scripts."test:player-assets-real-api" -eq `
            "vitest run --config vitest.acceptance.config.ts tests/player-assets-real-api.acceptance.ts") `
        "SwordMaster PlayerAssets live acceptance must target exactly one approved test file."
    Assert-Condition `
        ([string]$samplePackage.scripts."test:player-assets-transport" -eq `
            "vitest run tests/player-assets-http-transport.test.ts") `
        "SwordMaster focused PlayerAssets transport tests must target the approved test file."
    Assert-Condition `
        ([string]$samplePackage.scripts."test:real-api" -eq `
            "vitest run --config vitest.acceptance.config.ts tests/real-api-game-config.acceptance.ts") `
        "SwordMaster GameConfig live acceptance must remain isolated from PlayerAssets."
    $openApiGenerator = `
        $samplePackage.devDependencies.PSObject.Properties[
            "@hey-api/openapi-ts"]
    Assert-Condition `
        ($null -ne $openApiGenerator -and
            [string]$openApiGenerator.Value -eq "0.99.0") `
        "SwordMaster must pin @hey-api/openapi-ts exactly to 0.99.0."
    $sampleLock = Get-Content `
        -LiteralPath "$sampleRoot/pnpm-lock.yaml" `
        -Raw
    Assert-Condition `
        $sampleLock.Contains("version: 0.99.0(typescript@6.0.3)") `
        "SwordMaster must resolve @hey-api/openapi-ts against TypeScript 6.0.3."

    $contractPath = "$sampleRoot/contracts/player-assets.openapi.json"
    $contractBytes = [System.IO.File]::ReadAllBytes($contractPath)
    $hasUtf8Bom = $contractBytes.Length -ge 3 -and
        $contractBytes[0] -eq 0xEF -and
        $contractBytes[1] -eq 0xBB -and
        $contractBytes[2] -eq 0xBF
    Assert-Condition `
        (-not $hasUtf8Bom) `
        "The PlayerAssets OpenAPI contract must use UTF-8 without BOM."
    $contractSource = [System.Text.UTF8Encoding]::new(
        $false,
        $true).GetString($contractBytes)
    Assert-Condition `
        ($contractSource.EndsWith("`n", [StringComparison]::Ordinal) -and
            -not $contractSource.EndsWith("`n`n", [StringComparison]::Ordinal) -and
            -not $contractSource.Contains("`r", [StringComparison]::Ordinal)) `
        "The PlayerAssets OpenAPI contract must use LF and one trailing newline."
    $contract = $contractSource | ConvertFrom-Json -Depth 100
    $attributesPath = `
        "/api/player-assets/characters/{characterId}/attributes"
    $equipmentPath = `
        "/api/player-assets/characters/{characterId}/equipment/{slot}"
    Assert-StringSet `
        -Expected @($attributesPath, $equipmentPath) `
        -Actual @(
            $contract.paths.PSObject.Properties |
                ForEach-Object { [string]$_.Name }) `
        -Message "The generated contract must contain only approved PlayerAssets paths."
    $attributesPathObject = `
        $contract.paths.PSObject.Properties[$attributesPath].Value
    $equipmentPathObject = `
        $contract.paths.PSObject.Properties[$equipmentPath].Value
    $attributesOperations = $attributesPathObject.PSObject.Properties
    $equipmentOperations = $equipmentPathObject.PSObject.Properties
    Assert-StringSet `
        -Expected @("get") `
        -Actual @(
            $attributesOperations | ForEach-Object { [string]$_.Name }) `
        -Message "The attributes path must contain only GET."
    Assert-StringSet `
        -Expected @("delete", "put") `
        -Actual @(
            $equipmentOperations | ForEach-Object { [string]$_.Name }) `
        -Message "The equipment path must contain only PUT and DELETE."
    Assert-StringSet `
        -Expected @("schemas", "securitySchemes") `
        -Actual @(
            $contract.components.PSObject.Properties |
                ForEach-Object { [string]$_.Name }) `
        -Message "The focused contract may retain only schemas and security schemes."
    Assert-StringSet `
        -Expected @("SwordMasterPlayer") `
        -Actual @(
            $contract.components.securitySchemes.PSObject.Properties |
                ForEach-Object { [string]$_.Name }) `
        -Message "The focused contract must retain only SwordMasterPlayer security."
    $trackedGeneratedFiles = @(
        Get-ChildItem `
            -LiteralPath "$sampleRoot/src/generated/player-assets" `
            -File `
            -Recurse)
    Assert-Condition `
        ($trackedGeneratedFiles.Count -eq 1 -and
            $trackedGeneratedFiles[0].Name -eq "types.gen.ts") `
        "PlayerAssets generation must track only types.gen.ts."
    $sampleEntryPoint = Get-Content `
        -LiteralPath "$sampleRoot/src/index.ts" `
        -Raw
    Assert-Condition `
        $sampleEntryPoint.Contains(
            'export type * from "./generated/player-assets/types.gen.js";',
            [StringComparison]::Ordinal) `
        "SwordMaster must expose generated PlayerAssets contracts as types only."
    Assert-Condition `
        ([regex]::IsMatch(
            $sampleEntryPoint,
            '(?ms)export\s*\{[^}]*\bSwordMasterPlayerAssetsCocosComponent\b[^}]*\}\s*from\s*"\./player-assets-cocos-component\.js";')) `
        "SwordMaster must publicly expose the PlayerAssets Cocos-compatible component."

    $gameConfigClientPackagePath = `
        "clients/cocos/packages/game-config-client/package.json"
    $cocosCreatorAdapterPackagePath = `
        "clients/cocos/adapters/cocos-creator/package.json"
    foreach ($publicPackagePath in @(
        $gameConfigClientPackagePath,
        $cocosCreatorAdapterPackagePath
    )) {
        Assert-Condition `
            (Test-Path -LiteralPath $publicPackagePath -PathType Leaf) `
            "Missing public NovaGame client package '$publicPackagePath'."
    }
    $gameConfigClientPackage = Get-Content `
        -LiteralPath $gameConfigClientPackagePath `
        -Raw | ConvertFrom-Json
    $cocosCreatorAdapterPackage = Get-Content `
        -LiteralPath $cocosCreatorAdapterPackagePath `
        -Raw | ConvertFrom-Json
    $gameConfigClientName = ([string]$gameConfigClientPackage.name).Trim()
    $cocosCreatorAdapterName = ([string]$cocosCreatorAdapterPackage.name).Trim()
    Assert-Condition `
        ($gameConfigClientName -eq "@novagame/game-config-client") `
        "The public GameConfig client package name changed unexpectedly."
    Assert-Condition `
        ($cocosCreatorAdapterName -eq "@novagame/cocos-creator-game-config") `
        "The public Cocos Creator adapter package name changed unexpectedly."

    $runtimeDependenciesProperty = `
        $samplePackage.PSObject.Properties["dependencies"]
    Assert-Condition `
        ($null -ne $runtimeDependenciesProperty) `
        "SwordMaster must declare its two public NovaGame runtime dependencies."
    $runtimeDependencies = @(
        $runtimeDependenciesProperty.Value.PSObject.Properties)
    Assert-StringSet `
        -Expected @($gameConfigClientName, $cocosCreatorAdapterName) `
        -Actual @($runtimeDependencies | ForEach-Object { [string]$_.Name }) `
        -Message "SwordMaster runtime dependencies must be exactly the two public NovaGame client packages."
    $expectedLinks = [ordered]@{
        $gameConfigClientName = `
            "link:../../clients/cocos/packages/game-config-client"
        $cocosCreatorAdapterName = `
            "link:../../clients/cocos/adapters/cocos-creator"
    }
    foreach ($dependency in $expectedLinks.GetEnumerator()) {
        $property = $runtimeDependenciesProperty.Value.PSObject.Properties[
            [string]$dependency.Key]
        Assert-Condition `
            ($null -ne $property -and
                [string]$property.Value -eq [string]$dependency.Value) `
            "SwordMaster must consume '$($dependency.Key)' through '$($dependency.Value)'."
    }

    $allDependencyNames = @(Get-PackageDependencyNames -Package $samplePackage)
    Assert-Condition `
        ($allDependencyNames -notcontains "cc") `
        "SwordMaster must remain headlessly verifiable without a direct cc package dependency."
    Assert-StringSet `
        -Expected @($gameConfigClientName, $cocosCreatorAdapterName) `
        -Actual @(
            $allDependencyNames | Where-Object { $_ -like "@novagame/*" }
        ) `
        -Message "SwordMaster may depend only on the two approved public NovaGame packages."

    $sampleFiles = @(
        git ls-files --cached --others --exclude-standard -- $sampleRoot)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate SwordMaster files through Git."
    }
    $normalizedSampleFiles = @($sampleFiles | ForEach-Object { $_.Replace("\", "/") })
    $nestedAgents = @(
        $normalizedSampleFiles |
            Where-Object { $_ -match '(?i)(^|/)AGENTS\.md$' })
    Assert-Condition `
        ($nestedAgents.Count -eq 0) `
        "SwordMaster must not contain a nested AGENTS.md:`n$($nestedAgents -join "`n")"
    $nestedWorkspaces = @(
        $normalizedSampleFiles |
            Where-Object { $_ -match '(?i)(^|/)pnpm-workspace\.ya?ml$' })
    Assert-Condition `
        ($nestedWorkspaces.Count -eq 0) `
        "SwordMaster must not contain a nested pnpm workspace:`n$($nestedWorkspaces -join "`n")"
    $productionCocosAssets = @(
        $normalizedSampleFiles |
            Where-Object { $_ -match '(?i)\.(?:scene|prefab)$' })
    Assert-Condition `
        ($productionCocosAssets.Count -eq 0) `
        "SwordMaster M1 must not contain production Cocos scenes or prefabs:`n$($productionCocosAssets -join "`n")"

    $boundaryViolations = [System.Collections.Generic.List[string]]::new()
    $textExtensions = @(".cjs", ".js", ".json", ".mjs", ".ts", ".tsx", ".yaml", ".yml")
    $sourceExtensions = @(".cjs", ".js", ".mjs", ".ts", ".tsx")
    $ccModulePattern = '(?im)(?:\bfrom\s+|\bimport\s*(?:\(\s*)?|\brequire\s*\(\s*)[''"]cc[''"]'
    $gameplayPathPattern = `
        '(?i)(^|/)(battle|combat|gameplay|inventory|quests?|rewards?|skills?)(/|$)'
    $sampleSourceFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $sampleFiles) {
        $normalizedPath = $relativePath.Replace("\", "/")
        if ($normalizedPath -match $gameplayPathPattern) {
            $boundaryViolations.Add("$relativePath introduces a gameplay-oriented path.")
        }
        if ($normalizedPath -match `
                '(?i)^samples/SwordMaster/(packages/game-config-client|adapters/cocos-creator)(/|$)') {
            $boundaryViolations.Add("$relativePath copies a NovaGame package layout into SwordMaster.")
        }

        $extension = [System.IO.Path]::GetExtension($relativePath)
        if ($extension -in $sourceExtensions) {
            $sampleSourceFiles.Add($relativePath)
        }
        if ($extension -notin $textExtensions) {
            continue
        }

        $content = Get-Content -LiteralPath $relativePath -Raw
        if ($content -match $ccModulePattern) {
            $boundaryViolations.Add("$relativePath imports the Cocos Creator cc module directly.")
        }
    }

    $frameworkSourceFiles = @(
        git ls-files --cached --others --exclude-standard -- `
            "clients/cocos/packages/game-config-client/src" `
            "clients/cocos/adapters/cocos-creator/src")
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate public NovaGame client sources through Git."
    }
    $frameworkSourceByHash = @{}
    foreach ($relativePath in $frameworkSourceFiles) {
        if ([System.IO.Path]::GetExtension($relativePath) -notin $sourceExtensions) {
            continue
        }

        $hash = (Get-FileHash -LiteralPath $relativePath -Algorithm SHA256).Hash
        if (-not $frameworkSourceByHash.ContainsKey($hash)) {
            $frameworkSourceByHash[$hash] = $relativePath
        }
    }
    foreach ($relativePath in $sampleSourceFiles) {
        $hash = (Get-FileHash -LiteralPath $relativePath -Algorithm SHA256).Hash
        if ($frameworkSourceByHash.ContainsKey($hash)) {
            $boundaryViolations.Add(
                "$relativePath duplicates public NovaGame source '$($frameworkSourceByHash[$hash])'.")
        }
    }
    Assert-Condition `
        ($boundaryViolations.Count -eq 0) `
        "SwordMaster boundary violations:`n$($boundaryViolations -join "`n")"

    $frameworkRuntimeFiles = @(
        git ls-files --cached --others --exclude-standard -- `
            "server" `
            "admin" `
            "clients/cocos" `
            "tools" `
            "templates" `
            "deploy")
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate NovaGame runtime files through Git."
    }
    $frameworkReferenceViolations = [System.Collections.Generic.List[string]]::new()
    $frameworkTextExtensions = @(
        ".cjs", ".cs", ".cshtml", ".csproj", ".css", ".html", ".js", ".json",
        ".mjs", ".props", ".ps1", ".razor", ".sln", ".sql", ".targets", ".toml",
        ".ts", ".tsx", ".vue", ".xml", ".yaml", ".yml")
    $samplePackageName = ([string]$samplePackage.name).Trim()
    foreach ($relativePath in $frameworkRuntimeFiles) {
        if ([System.IO.Path]::GetExtension($relativePath) -notin $frameworkTextExtensions) {
            continue
        }

        $content = Get-Content -LiteralPath $relativePath -Raw
        if ($content -match '(?i)SwordMaster' -or
            $content -match '(?i)samples[/\\]SwordMaster' -or
            (-not [string]::IsNullOrWhiteSpace($samplePackageName) -and
                $content.Contains($samplePackageName, [StringComparison]::Ordinal))) {
            $frameworkReferenceViolations.Add(
                "$relativePath references the SwordMaster consumer.")
        }
    }
    Assert-Condition `
        ($frameworkReferenceViolations.Count -eq 0) `
        "NovaGame-to-SwordMaster dependency violations:`n$($frameworkReferenceViolations -join "`n")"

    $snapshotManifestSource = Get-Content `
        -LiteralPath $snapshotManifestPath `
        -Raw
    $snapshotManifestMatch = [regex]::Match(
        $snapshotManifestSource,
        '\A[ \t\r\n]*\{[ \t\r\n]*"snapshotHash"[ \t\r\n]*:[ \t\r\n]*"(?<hash>[0-9a-f]{64})"[ \t\r\n]*\}[ \t\r\n]*\z')
    Assert-Condition `
        $snapshotManifestMatch.Success `
        "SwordMaster snapshot sidecar must contain exactly one lowercase SHA-256 snapshotHash."
    $snapshotHash = $snapshotManifestMatch.Groups["hash"].Value
    $actualSnapshotHash = (
        Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition `
        ($snapshotHash -ceq $actualSnapshotHash) `
        "SwordMaster snapshot sidecar hash does not match the exact snapshot bytes."

    $swordMasterServerSolution = "$sampleRoot/server/SwordMaster.Server.sln"
    $swordMasterNuGetConfig = "$sampleRoot/server/NuGet.Config"
    Invoke-CheckedCommand "dotnet" @(
        "restore",
        $swordMasterServerSolution,
        "--configfile", $swordMasterNuGetConfig,
        "--force-evaluate",
        "--verbosity", "minimal",
        "-p:NuGetAudit=false"
    )
    Test-NovaCoreResolvedPackages `
        -SearchRoots @("$sampleRoot/server")
    Invoke-CheckedCommand "dotnet" @(
        "format",
        $swordMasterServerSolution,
        "--no-restore",
        "--verify-no-changes",
        "--verbosity", "minimal"
    )
    Invoke-CheckedCommand "dotnet" @(
        "build",
        $swordMasterServerSolution,
        "--no-restore",
        "--configuration", "Release",
        "--disable-build-servers",
        "-maxcpucount:1",
        "--verbosity", "minimal",
        "-warnaserror",
        "-p:NuGetAudit=false"
    )
    foreach ($settingsContract in @(
        @(
            "$sampleRoot/server/src/SwordMaster.Api/appsettings.json",
            "$sampleRoot/server/src/SwordMaster.Api/bin/Release/net8.0/appsettings.json"
        ),
        @(
            "$sampleRoot/server/src/SwordMaster.Api/appsettings.Production.json",
            "$sampleRoot/server/src/SwordMaster.Api/bin/Release/net8.0/appsettings.Production.json"
        ),
        @(
            "$sampleRoot/server/src/SwordMaster.DbMigrator/appsettings.json",
            "$sampleRoot/server/src/SwordMaster.DbMigrator/bin/Release/net8.0/appsettings.json"
        )
    )) {
        $sourceSettingsPath = [string]$settingsContract[0]
        $outputSettingsPath = [string]$settingsContract[1]
        Assert-Condition `
            (Test-Path -LiteralPath $outputSettingsPath -PathType Leaf) `
            "SwordMaster runtime output is missing '$outputSettingsPath'."
        Assert-Condition `
            ((Get-FileHash -LiteralPath $sourceSettingsPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $outputSettingsPath -Algorithm SHA256).Hash) `
            "SwordMaster runtime output '$outputSettingsPath' was overwritten by a referenced project."
    }
    foreach ($testProject in @(
        "$sampleRoot/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj",
        "$sampleRoot/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj"
    )) {
        Invoke-CheckedCommand "dotnet" @(
            "test",
            $testProject,
            "--no-build",
            "--configuration", "Release",
            "--verbosity", "minimal",
            "-p:NuGetAudit=false"
        )
    }
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj" `
        -Filter "FullyQualifiedName~PlayerAssets" `
        -ResultName "swordmaster-domain-player-assets.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj" `
        -Filter "FullyQualifiedName~PlayerAssets" `
        -ResultName "swordmaster-application-player-assets.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" `
        -Filter "FullyQualifiedName~PlayerAssetsApiTests" `
        -ResultName "swordmaster-api-player-assets.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" `
        -Filter "FullyQualifiedName~PlayerAssetsOpenApiContractTests" `
        -ResultName "swordmaster-player-assets-openapi-contract.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" `
        -Filter "FullyQualifiedName~SwordMasterRuntimeCompositionTests" `
        -ResultName "swordmaster-runtime-composition.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" `
        -Filter "FullyQualifiedName~SwordMasterDbMigratorConfigurationTests" `
        -ResultName "swordmaster-dbmigrator-configuration.trx"
    $toolchain = Initialize-SwordMasterToolchain
    Test-SwordMasterPlayerAssetsGeneratedTypes -Toolchain $toolchain
    Invoke-SwordMasterVitestWithExecutionCount `
        -Toolchain $toolchain `
        -ScriptName "test:player-assets-transport" `
        -ExpectedTestCount 12 `
        -ResultName "swordmaster-player-assets-transport.json"
    Invoke-SwordMasterVitestWithExecutionCount `
        -Toolchain $toolchain `
        -ScriptName "test:player-assets-interaction" `
        -ExpectedTestCount 12 `
        -ResultName "swordmaster-player-assets-interaction.json"
    Invoke-SwordMasterVitestWithExecutionCount `
        -Toolchain $toolchain `
        -ScriptName "test:player-assets-cocos" `
        -ExpectedTestCount 7 `
        -ResultName "swordmaster-player-assets-cocos.json"
    Test-SwordMasterDatabase -Toolchain $toolchain

    $previousCi = $env:CI
    $env:CI = "true"
    try {
        foreach ($scriptName in @("format:check", "lint", "typecheck", "test", "build")) {
            Invoke-SwordMasterPnpm `
                -Toolchain $toolchain `
                -ArgumentList @("run", $scriptName)
        }
    }
    finally {
        $env:CI = $previousCi
    }

    Write-Output "SwordMaster verification passed."
}

switch ($Scope) {
    "Documentation" { Test-Documentation }
    "Repository" { Test-Repository }
    "Server" { Test-Server }
    "Database" { Test-Database }
    "Authentication" { Test-Authentication }
    "GameConfig" { Test-GameConfig }
    "AdminE2E" { Test-AdminEndToEnd }
    "Admin" { Test-Admin }
    "Client" { Test-Client }
    "SwordMaster" { Test-SwordMaster }
    "All" {
        Test-Admin -RunSwordMasterAcceptance
        Test-Client -SkipRepositoryPrerequisites
        Test-SwordMaster `
            -SkipRepositoryPrerequisites `
            -SkipClientPrerequisites
    }
    default { throw "Verification scope '$Scope' is not implemented yet. Complete the owning stage before enabling it." }
}

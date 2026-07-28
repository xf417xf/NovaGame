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
    $userProfile = if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $env:USERPROFILE
    }
    else {
        [Environment]::GetFolderPath("UserProfile")
    }
    $defaultGlobalPackages = Join-Path $userProfile ".nuget\packages"
    $env:NUGET_PACKAGES = if (-not [string]::IsNullOrWhiteSpace($env:NOVAGAME_NUGET_PACKAGES)) {
        $env:NOVAGAME_NUGET_PACKAGES
    }
    elseif (Test-Path -LiteralPath $defaultGlobalPackages -PathType Container) {
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
        "docs/goals/m0.5.md"
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

    $goal = Get-Content -LiteralPath "docs/goals/m0.5.md" -Raw
    $goalStatusMatch = [regex]::Match(
        $goal,
        '(?m)^status:\s*(?<status>[a-z_]+)\s*$')
    Assert-Condition $goalStatusMatch.Success "Goal YAML metadata is missing status."
    $goalStatus = $goalStatusMatch.Groups["status"].Value

    $roadmap = Get-Content -LiteralPath "docs/roadmap.md" -Raw
    $activeMilestoneCount = [regex]::Matches($roadmap, "\*\*Status:\*\* Active").Count
    $expectedActiveMilestoneCount = if ($goalStatus -eq "complete") { 0 } else { 1 }
    Assert-Condition `
        ($activeMilestoneCount -eq $expectedActiveMilestoneCount) `
        "Expected $expectedActiveMilestoneCount active roadmap milestone(s), found $activeMilestoneCount."
    $m05RoadmapStatusMatch = [regex]::Match(
        $roadmap,
        '(?s)## M0\.5 - Framework Bootstrap.*?\*\*Status:\*\*\s*(?<status>[A-Za-z]+)')
    Assert-Condition $m05RoadmapStatusMatch.Success "The roadmap is missing the M0.5 status."
    $expectedM05RoadmapStatus = if ($goalStatus -eq "complete") { "Complete" } else { "Active" }
    Assert-Condition `
        ($m05RoadmapStatusMatch.Groups["status"].Value -eq $expectedM05RoadmapStatus) `
        "M0.5 roadmap status must be '$expectedM05RoadmapStatus' while the goal is '$goalStatus'."

    $architecture = Get-Content -LiteralPath "docs/architecture.md" -Raw
    Assert-Condition $architecture.Contains("MySQL 8.4") "Architecture must declare MySQL 8.4."
    Assert-Condition $architecture.Contains("web-ele") "Architecture must declare Vben web-ele."
    Assert-Condition (-not $architecture.Contains("web-antd admin is")) "Architecture contains a conflicting web-antd decision."

    foreach ($requiredSection in @("## Blocker Log", "## Verification Log", "## M0.5 Exit Criteria")) {
        Assert-Condition $goal.Contains($requiredSection) "Goal is missing required section '$requiredSection'."
    }
    Assert-Condition ($goal.StartsWith("---")) "Goal is missing YAML metadata."
    Assert-Condition ([regex]::Matches($goal, "^## M05-\d{2} - ", [System.Text.RegularExpressions.RegexOptions]::Multiline).Count -eq 10) "Goal must define stages M05-00 through M05-09."

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
        "scripts/database-environment.ps1",
        "scripts/admin-access-environment.ps1"
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
    Assert-Condition `
        $bootstrapScript.Contains('[string]$Scope = "All"') `
        "The root bootstrap command must default to the cumulative All scope."
    Assert-Condition `
        $verifyScript.Contains('[string]$Scope = "All"') `
        "The root verification command must default to the cumulative All scope."

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

function Get-PackageReferenceNames {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    return @(
        $project.SelectNodes("//PackageReference") |
            ForEach-Object { [string]$_.Include }
    )
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

    [xml]$nugetConfig = Get-Content -LiteralPath "server/NuGet.Config" -Raw
    $novaSource = @($nugetConfig.configuration.packageSources.add) |
        Where-Object { $_.key -eq "nova-local" } |
        Select-Object -First 1
    Assert-Condition ($null -ne $novaSource) "Server NuGet.Config is missing the 'nova-local' package source."
    $novaSourcePath = [string]$novaSource.value
    Assert-Condition ($novaSourcePath.Replace("\", "/") -eq "../.novacore/packages") "The NovaCore package source must be repository-relative."
    Assert-Condition (-not [System.IO.Path]::IsPathRooted($novaSourcePath)) "The NovaCore package source must not be machine-specific."

    $lockPath = Join-Path $repositoryRoot "server\novacore.lock.json"
    $provenancePath = Join-Path $repositoryRoot ".novacore\provenance.json"
    Assert-Condition (Test-Path -LiteralPath $lockPath -PathType Leaf) "NovaCore lock file is missing."
    Assert-Condition (Test-Path -LiteralPath $provenancePath -PathType Leaf) "NovaCore provenance is missing. Run scripts/bootstrap.ps1 -Scope Server."

    $novaCoreLock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    $expectedPackageNames = @($novaCoreLock.requiredPackages | ForEach-Object { [string]$_ })
    $provenancePackageNames = @($provenance.packages | ForEach-Object { [string]$_.name })
    Assert-StringSet $expectedPackageNames $provenancePackageNames "NovaCore provenance package set does not match the lock."
    Assert-Condition ([string]$provenance.lock.version -eq [string]$novaCoreLock.version) "NovaCore provenance version does not match the lock."
    Assert-Condition ([string]$provenance.lock.commit -eq [string]$novaCoreLock.commit) "NovaCore provenance commit does not match the lock."
    Assert-Condition ([string]$provenance.source.resolvedCommit -eq [string]$novaCoreLock.commit) "NovaCore source snapshot does not match the locked commit."
    Assert-Condition (
        [string]$provenance.lock.sha256 -eq (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash.ToLowerInvariant()) `
        "NovaCore lock hash does not match provenance."

    $canonicalPackageSource = Join-Path $repositoryRoot ".novacore\feeds\$($novaCoreLock.commit)"
    $compatibilityPackageSource = Join-Path $repositoryRoot ".novacore\packages"
    Assert-Condition (Test-Path -LiteralPath $canonicalPackageSource -PathType Container) "Canonical NovaCore feed is missing."
    Assert-Condition (Test-Path -LiteralPath $compatibilityPackageSource -PathType Container) "Compatibility NovaCore feed is missing."
    Assert-Condition (
        [System.IO.Path]::GetFullPath([string]$provenance.feed.root) -eq [System.IO.Path]::GetFullPath($canonicalPackageSource)) `
        "NovaCore provenance points at the wrong canonical feed."
    Assert-Condition (
        [System.IO.Path]::GetFullPath([string]$provenance.feed.compatibilityRoot) -eq [System.IO.Path]::GetFullPath($compatibilityPackageSource)) `
        "NovaCore provenance points at the wrong compatibility feed."

    $expectedPackageFiles = @($expectedPackageNames | ForEach-Object { "$_.$($novaCoreLock.version).nupkg" })
    Assert-StringSet $expectedPackageFiles `
        @(Get-ChildItem -LiteralPath $canonicalPackageSource -Filter "*.nupkg" -File | ForEach-Object Name) `
        "Canonical NovaCore feed contains an unexpected package set."
    Assert-StringSet $expectedPackageFiles `
        @(Get-ChildItem -LiteralPath $compatibilityPackageSource -Filter "*.nupkg" -File | ForEach-Object Name) `
        "Compatibility NovaCore feed contains an unexpected package set."

    foreach ($packageRecord in @($provenance.packages)) {
        Assert-Condition ([string]$packageRecord.version -eq [string]$novaCoreLock.version) "NovaCore package '$($packageRecord.name)' has the wrong provenance version."
        foreach ($feed in @($canonicalPackageSource, $compatibilityPackageSource)) {
            $packagePath = Join-Path $feed ([string]$packageRecord.file)
            Assert-Condition (Test-Path -LiteralPath $packagePath -PathType Leaf) "NovaCore package '$packagePath' is missing."
            Assert-Condition (
                (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -eq [string]$packageRecord.sha256) `
                "NovaCore package hash mismatch for '$packagePath'."
        }
    }

    foreach ($projectFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter "*.csproj") {
        [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
        foreach ($packageReference in @($project.SelectNodes("//PackageReference") | Where-Object {
                    ([string]$_.Include).StartsWith("NovaCore.", [StringComparison]::Ordinal)
                })) {
            Assert-Condition (
                [string]$packageReference.Version -eq [string]$novaCoreLock.version) `
                "Project '$($projectFile.FullName)' must reference NovaCore $($novaCoreLock.version), not '$($packageReference.Version)'."
        }
    }

    Write-Output "Server architecture verification passed."
}

function Test-NovaCoreResolvedPackages {
    $novaCoreLock = Get-Content -LiteralPath "server/novacore.lock.json" -Raw | ConvertFrom-Json
    foreach ($assetsFile in Get-ChildItem -LiteralPath "server/src" -Recurse -File -Filter "project.assets.json" |
        Where-Object { $_.FullName -match "[\\/]obj[\\/]project\.assets\.json$" }) {
        $assets = Get-Content -LiteralPath $assetsFile.FullName -Raw | ConvertFrom-Json
        foreach ($library in @($assets.libraries.PSObject.Properties | Where-Object {
                    $_.Name.StartsWith("NovaCore.", [StringComparison]::Ordinal)
                })) {
            $resolvedVersion = ($library.Name -split "/", 2)[1]
            Assert-Condition (
                $resolvedVersion -eq [string]$novaCoreLock.version) `
                "Resolved NovaCore dependency '$($library.Name)' in '$($assetsFile.FullName)' does not match the lock."
        }
    }
}

function Test-Server {
    Test-Repository
    Test-ServerArchitecture

    Invoke-CheckedCommand "dotnet" @(
        "restore",
        "server/NovaGame.sln",
        "--configfile", "server/NuGet.Config",
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
            $previousSeedEnvironment = [ordered]@{}
            foreach ($name in $seedEnvironmentNames) {
                $previousSeedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
            }

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
                foreach ($name in $seedEnvironmentNames) {
                    [Environment]::SetEnvironmentVariable($name, $previousSeedEnvironment[$name], "Process")
                }
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
        "server/src/NovaGame.Domain/Content/GameConfig/GameConfigSnapshotBuilder.cs",
        "server/src/NovaGame.Application/Content/GameConfig/GameConfigAppService.cs",
        "server/src/NovaGame.Api/Content/GameConfig/GameConfigController.cs",
        "server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigDomainTests.cs",
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
    $previousEnvironment = [ordered]@{}
    foreach ($name in @($sensitiveEnvironmentNames + $acceptanceEnvironmentName)) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable(
            $name,
            "Process")
    }

    try {
        foreach ($name in $sensitiveEnvironmentNames) {
            [Environment]::SetEnvironmentVariable($name, $null, "Process")
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
        foreach ($entry in $previousEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                [string]$entry.Key,
                $entry.Value,
                "Process")
        }
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
    $previousRuntimeEnvironment = [ordered]@{}
    foreach ($name in $runtimeEnvironmentNames) {
        $previousRuntimeEnvironment[$name] = [Environment]::GetEnvironmentVariable(
            $name,
            "Process")
    }

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

        foreach ($name in $runtimeEnvironmentNames) {
            try {
                [Environment]::SetEnvironmentVariable(
                    $name,
                    $previousRuntimeEnvironment[$name],
                    "Process")
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to restore process environment variable '$name'.")
            }
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

function Test-SwordMaster {
    param(
        [switch]$SkipRepositoryPrerequisites,
        [switch]$SkipClientPrerequisites
    )

    if (-not $SkipRepositoryPrerequisites) {
        Test-Repository
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
        "format:check", "lint", "typecheck", "test", "test:real-api", "build"
    )) {
        Assert-Condition `
            ($null -ne $samplePackage.scripts.PSObject.Properties[$requiredScript]) `
            "The SwordMaster package.json is missing required script '$requiredScript'."
    }

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

    $toolchain = Initialize-SwordMasterToolchain
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

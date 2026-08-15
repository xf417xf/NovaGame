[CmdletBinding()]
param(
    [switch]$Check,

    [string]$NodePath,

    [string]$PnpmBootstrapPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$adminRoot = Join-Path $repositoryRoot "admin"
$adminApplicationRoot = Join-Path $adminRoot "apps\web-ele"
$verificationScript = Join-Path $PSScriptRoot "verify.ps1"
$adminPackagePath = Join-Path $adminApplicationRoot "package.json"
$adminWorkspacePath = Join-Path $adminRoot "pnpm-workspace.yaml"
$adminLockPath = Join-Path $adminRoot "pnpm-lock.yaml"
$generatorConfigPath = Join-Path $adminApplicationRoot "openapi-ts.config.mjs"
$trackedContractPath = Join-Path `
    $adminApplicationRoot `
    "contracts\admin-access.openapi.json"
$trackedTypesRoot = Join-Path `
    $adminApplicationRoot `
    "src\generated\admin-access"
$trackedTypesPath = Join-Path $trackedTypesRoot "types.gen.ts"
$temporaryRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    "novagame-admin-access-contract-$([Guid]::NewGuid().ToString('N'))"
$capturedContractPath = Join-Path $temporaryRoot "capture\admin-access.openapi.json"
$temporaryGeneratedTypesRoot = Join-Path `
    $temporaryRoot `
    "generated\admin-access"
$temporaryGeneratedTypesPath = Join-Path `
    $temporaryGeneratedTypesRoot `
    "types.gen.ts"

$expectedOperations = [ordered]@{
    "/api/admin-access/administrators" = @("get", "post")
    "/api/admin-access/administrators/{administratorId}" = @("get")
    "/api/admin-access/administrators/{administratorId}/display-name" = @("put")
    "/api/admin-access/administrators/{administratorId}/roles" = @("put")
    "/api/admin-access/administrators/{administratorId}/active-state" = @("put")
    "/api/admin-access/administrators/{administratorId}/password-reset" = @("post")
    "/api/admin-access/roles" = @("get", "post")
    "/api/admin-access/roles/{roleId}" = @("get")
    "/api/admin-access/roles/{roleId}/name" = @("put")
    "/api/admin-access/roles/{roleId}/active-state" = @("put")
    "/api/admin-access/roles/{roleId}/permissions" = @("put")
    "/api/admin-access/permissions" = @("get")
    "/api/admin-access/sessions" = @("get")
    "/api/admin-access/sessions/{familyId}/revoke" = @("post")
    "/api/admin-access/audit-records" = @("get")
}
$expectedSchemaNames = @(
    "AdminAccessAuditActionDto",
    "AdminAccessAuditRecordDto",
    "AdminAccessAuditRecordDtoAdminAccessPageDto",
    "AdminAccessAuditRecordDtoAdminAccessPageDtoApiResponse",
    "AdministratorDto",
    "AdministratorDtoAdminAccessPageDto",
    "AdministratorDtoAdminAccessPageDtoApiResponse",
    "AdministratorDtoApiResponse",
    "AdminRoleDto",
    "AdminRoleDtoApiResponse",
    "AdminRoleReferenceDto",
    "AdminRoleSummaryDto",
    "AdminRoleSummaryDtoAdminAccessPageDto",
    "AdminRoleSummaryDtoAdminAccessPageDtoApiResponse",
    "AdminSessionFamilyDto",
    "AdminSessionFamilyDtoAdminAccessPageDto",
    "AdminSessionFamilyDtoAdminAccessPageDtoApiResponse",
    "AdminSessionFamilyDtoApiResponse",
    "AdminSessionFamilyState",
    "AdminSessionRevocationReason",
    "ApiError",
    "ApiResponse",
    "ChangeAdministratorDisplayNameRequest",
    "CreateAdministratorRequest",
    "CreateAdminRoleRequest",
    "PermissionDefinitionDto",
    "PermissionGroupDto",
    "PermissionGroupDtoIReadOnlyListApiResponse",
    "RenameAdminRoleRequest",
    "ReplaceAdministratorRolesRequest",
    "ReplaceAdminRolePermissionsRequest",
    "ResetAdministratorPasswordRequest",
    "SetAdministratorActiveStateRequest",
    "SetAdminRoleActiveStateRequest"
)
$httpMethods = @("delete", "get", "head", "options", "patch", "post", "put", "trace")
$handwrittenContractPaths = @(
    (Join-Path $adminApplicationRoot "src\api\core\auth.ts"),
    (Join-Path $adminApplicationRoot "src\api\game-config.ts"),
    (Join-Path $adminApplicationRoot "src\api\nova-game-contracts.ts")
)

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

function Assert-Condition {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-StringSet {
    param(
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string[]]$Actual,
        [Parameter(Mandatory)][string]$Message
    )

    $expectedOrdered = @($Expected | Sort-Object -CaseSensitive)
    $actualOrdered = @($Actual | Sort-Object -CaseSensitive)
    if ($expectedOrdered.Count -ne $actualOrdered.Count) {
        throw "$Message Expected '$($expectedOrdered -join ', ')', actual '$($actualOrdered -join ', ')'."
    }

    for ($index = 0; $index -lt $expectedOrdered.Count; $index++) {
        if ($expectedOrdered[$index] -cne $actualOrdered[$index]) {
            throw "$Message Expected '$($expectedOrdered -join ', ')', actual '$($actualOrdered -join ', ')'."
        }
    }
}

function Get-NormalizedUtf8LfBytes {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasUtf8Bom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    if ($bytes.Length -eq 0 -or $hasUtf8Bom) {
        throw "Artifact '$Path' must be non-empty UTF-8 without a BOM."
    }

    $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd("`n") + "`n"
    return [System.Text.UTF8Encoding]::new($false).GetBytes($normalized)
}

function Normalize-Utf8LfFile {
    param([Parameter(Mandatory)][string]$Path)

    [System.IO.File]::WriteAllBytes($Path, (Get-NormalizedUtf8LfBytes -Path $Path))
}

function Test-ByteSequenceEqual {
    param(
        [Parameter(Mandatory)][byte[]]$Expected,
        [Parameter(Mandatory)][byte[]]$Actual
    )

    if ($Expected.Length -ne $Actual.Length) {
        return $false
    }

    for ($index = 0; $index -lt $Expected.Length; $index++) {
        if ($Expected[$index] -ne $Actual[$index]) {
            return $false
        }
    }

    return $true
}

function Assert-CanonicalUtf8LfFile {
    param([Parameter(Mandatory)][string]$Path)

    $actualBytes = [System.IO.File]::ReadAllBytes($Path)
    $canonicalBytes = Get-NormalizedUtf8LfBytes -Path $Path
    if (-not (Test-ByteSequenceEqual `
            -Expected $actualBytes `
            -Actual $canonicalBytes)) {
        throw "Artifact '$Path' must use canonical UTF-8/LF with one trailing newline."
    }
}

function Add-SchemaReferences {
    param(
        [Parameter(Mandatory)]$Node,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$Names
    )

    if ($Node -is [System.Collections.IDictionary]) {
        foreach ($entry in $Node.GetEnumerator()) {
            if ([string]$entry.Key -ceq '$ref') {
                $reference = [string]$entry.Value
                $prefix = '#/components/schemas/'
                if (-not $reference.StartsWith($prefix, [StringComparison]::Ordinal)) {
                    throw "The management OpenAPI capture contains unsupported reference '$reference'."
                }

                $name = $reference.Substring($prefix.Length).
                    Replace('~1', '/').Replace('~0', '~')
                $null = $Names.Add($name)
                continue
            }

            if ($null -ne $entry.Value) {
                Add-SchemaReferences -Node $entry.Value -Names $Names
            }
        }
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) {
            if ($null -ne $item) {
                Add-SchemaReferences -Node $item -Names $Names
            }
        }
    }
}

function Assert-ManagementContract {
    param([Parameter(Mandatory)][string]$Path)

    $text = [System.Text.UTF8Encoding]::new($false, $true).GetString(
        [System.IO.File]::ReadAllBytes($Path))
    Assert-Condition `
        ($text.EndsWith("`n", [StringComparison]::Ordinal) -and
            -not $text.EndsWith("`n`n", [StringComparison]::Ordinal) -and
            -not $text.Contains("`r", [StringComparison]::Ordinal)) `
        "Management OpenAPI capture must use UTF-8/LF with one trailing newline."
    $contract = $text | ConvertFrom-Json -AsHashtable -Depth 100
    Assert-Condition `
        ($contract -is [System.Collections.IDictionary]) `
        "Management OpenAPI capture must have an object root."
    Assert-Condition `
        ($contract.ContainsKey('paths') -and
            $contract.paths -is [System.Collections.IDictionary]) `
        "Management OpenAPI capture is missing its paths object."
    Assert-StringSet `
        -Expected @($expectedOperations.Keys) `
        -Actual @($contract.paths.Keys) `
        -Message "Management OpenAPI path allowlist differs."

    $operationCount = 0
    foreach ($path in $expectedOperations.Keys) {
        $operations = $contract.paths[$path]
        Assert-Condition `
            ($operations -is [System.Collections.IDictionary]) `
            "Management OpenAPI path '$path' is not an object."
        $actualMethods = @(
            $operations.Keys | Where-Object { $_ -in $httpMethods })
        Assert-StringSet `
            -Expected @($expectedOperations[$path]) `
            -Actual $actualMethods `
            -Message "Management OpenAPI operation allowlist differs for '$path'."
        foreach ($method in $actualMethods) {
            $operationCount++
            $operation = $operations[$method]
            Assert-Condition `
                ($operation -is [System.Collections.IDictionary]) `
                "Management OpenAPI operation '$method $path' is not an object."
            $security = @($operation.security)
            Assert-Condition `
                ($security.Count -eq 1 -and
                    $security[0] -is [System.Collections.IDictionary] -and
                    $security[0].Keys.Count -eq 1 -and
                    $security[0].ContainsKey('Bearer')) `
                "Management OpenAPI operation '$method $path' must require only Bearer security."
        }
    }
    Assert-Condition `
        ($operationCount -eq 17) `
        "Management OpenAPI capture must contain exactly 17 operations."

    Assert-Condition `
        ($contract.ContainsKey('components') -and
            $contract.components -is [System.Collections.IDictionary]) `
        "Management OpenAPI capture is missing components."
    Assert-StringSet `
        -Expected @('schemas', 'securitySchemes') `
        -Actual @($contract.components.Keys) `
        -Message "Management OpenAPI components must contain only schemas and security schemes."
    Assert-Condition `
        ($contract.components.schemas -is [System.Collections.IDictionary]) `
        "Management OpenAPI capture is missing component schemas."
    Assert-StringSet `
        -Expected $expectedSchemaNames `
        -Actual @($contract.components.schemas.Keys) `
        -Message "Management OpenAPI schema allowlist differs."
    foreach ($schemaName in $contract.components.schemas.Keys) {
        Assert-Condition `
            ($schemaName -notmatch '(?i)(password.*(response|hash)|token|hash|receipt|aggregate|entity)') `
            "Management OpenAPI schema '$schemaName' is forbidden."
    }
    Assert-Condition `
        ($contract.components.securitySchemes -is [System.Collections.IDictionary]) `
        "Management OpenAPI capture is missing security schemes."
    Assert-StringSet `
        -Expected @('Bearer') `
        -Actual @($contract.components.securitySchemes.Keys) `
        -Message "Management OpenAPI must expose only the Bearer security scheme."

    $reachableSchemas = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    Add-SchemaReferences -Node $contract.paths -Names $reachableSchemas
    $pendingSchemas = [System.Collections.Generic.Queue[string]]::new()
    foreach ($schemaName in $reachableSchemas) {
        $pendingSchemas.Enqueue($schemaName)
    }
    while ($pendingSchemas.Count -gt 0) {
        $schemaName = $pendingSchemas.Dequeue()
        Assert-Condition `
            ($contract.components.schemas.ContainsKey($schemaName)) `
            "Management OpenAPI references missing schema '$schemaName'."
        $before = $reachableSchemas.Count
        Add-SchemaReferences `
            -Node $contract.components.schemas[$schemaName] `
            -Names $reachableSchemas
        if ($reachableSchemas.Count -gt $before) {
            foreach ($candidate in $reachableSchemas) {
                if (-not $pendingSchemas.Contains($candidate) -and
                    $candidate -ne $schemaName) {
                    $pendingSchemas.Enqueue($candidate)
                }
            }
        }
    }
    Assert-StringSet `
        -Expected @($contract.components.schemas.Keys) `
        -Actual @($reachableSchemas) `
        -Message "Management OpenAPI schemas must be exactly transitively reachable."
}

function Assert-GeneratedArtifacts {
    param([Parameter(Mandatory)][string]$Root)

    Assert-Condition `
        (Test-Path -LiteralPath $Root -PathType Container) `
        "AdminAccess TypeScript generation did not create '$Root'."
    $files = @(
        Get-ChildItem -LiteralPath $Root -File -Recurse |
            Sort-Object FullName)
    $expectedPath = Join-Path $Root "types.gen.ts"
    Assert-Condition `
        ($files.Count -eq 1 -and $files[0].FullName -eq $expectedPath) `
        "AdminAccess TypeScript generation must produce only 'types.gen.ts'."
    Assert-CanonicalUtf8LfFile -Path $expectedPath
    $source = [System.Text.UTF8Encoding]::new($false, $true).GetString(
        [System.IO.File]::ReadAllBytes($expectedPath))
    Assert-Condition `
        (-not [string]::IsNullOrWhiteSpace($source) -and
            $source -notmatch '(?m)^export\s+(?!type\b)') `
        "AdminAccess generation must expose non-empty TypeScript types only."
}

function Assert-TrackedGeneratedArtifacts {
    param([switch]$Require)

    if (-not (Test-Path -LiteralPath $trackedTypesRoot -PathType Container)) {
        if ($Require) {
            throw "Tracked AdminAccess generated type directory is missing at '$trackedTypesRoot'."
        }
        return
    }

    Assert-GeneratedArtifacts -Root $trackedTypesRoot
}

function Assert-AdminAccessContractGeneratorConfiguration {
    foreach ($requiredPath in @(
            $adminPackagePath,
            $adminWorkspacePath,
            $adminLockPath,
            $generatorConfigPath)) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
            "AdminAccess contract-generation file is missing at '$requiredPath'."
    }

    $package = Get-Content -LiteralPath $adminPackagePath -Raw | ConvertFrom-Json
    $generatorDependency = $package.devDependencies.PSObject.Properties[
        "@hey-api/openapi-ts"]
    Assert-Condition `
        ($null -ne $generatorDependency -and
            [string]$generatorDependency.Value -eq "catalog:") `
        "web-ele must reference @hey-api/openapi-ts through the pinned catalog."
    Assert-Condition `
        ([string]$package.scripts."contract:generate" -eq
            "openapi-ts --file openapi-ts.config.mjs") `
        "web-ele contract generation must use the checked OpenAPI config."

    $workspace = Get-Content -LiteralPath $adminWorkspacePath -Raw
    Assert-Condition `
        ([System.Text.RegularExpressions.Regex]::IsMatch(
            $workspace,
            "(?m)^  '@hey-api/openapi-ts': 0\.99\.0$")) `
        "The admin workspace catalog must pin @hey-api/openapi-ts exactly to 0.99.0."

    $config = Get-Content -LiteralPath $generatorConfigPath -Raw
    $pluginMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $config,
        '(?m)^\s*plugins\s*:\s*\[\s*[''"]@hey-api/typescript[''"]\s*\]\s*,?\s*$')
    Assert-Condition `
        ($pluginMatches.Count -eq 1) `
        "web-ele OpenAPI generation must configure only the @hey-api/typescript plugin."
    foreach ($requiredConfigPattern in @(
            "NOVAGAME_ADMIN_ACCESS_OPENAPI_INPUT",
            "NOVAGAME_ADMIN_ACCESS_TYPES_OUTPUT",
            "entryFile\s*:\s*false",
            "clean\s*:\s*true",
            'postProcess\s*:\s*\[\s*[''"]oxfmt[''"]\s*\]')) {
        Assert-Condition `
            ([System.Text.RegularExpressions.Regex]::IsMatch(
                $config,
                $requiredConfigPattern)) `
            "web-ele OpenAPI config is missing required setting '$requiredConfigPattern'."
    }

    $normalizedLock = (Get-Content -LiteralPath $adminLockPath -Raw).
        Replace("`r`n", "`n")
    $webEleImporter = [System.Text.RegularExpressions.Regex]::Match(
        $normalizedLock,
        "(?ms)^  apps/web-ele:\n(?<body>.*?)(?=^  \S|\z)")
    Assert-Condition `
        $webEleImporter.Success `
        "The pnpm lockfile is missing the web-ele importer."
    Assert-Condition `
        ([System.Text.RegularExpressions.Regex]::IsMatch(
            $webEleImporter.Groups["body"].Value,
            "(?m)^      '@hey-api/openapi-ts':\n        specifier: 'catalog:'\n        version: 0\.99\.0\(magicast@0\.5\.3\)\(typescript@6\.0\.3\)$")) `
        "web-ele must resolve @hey-api/openapi-ts 0.99.0 against the pinned TypeScript 6.0.3 toolchain."
}

function Assert-HandwrittenContractsUnchanged {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$ExpectedHashes)

    foreach ($path in $ExpectedHashes.Keys) {
        if ((Get-FileSha256 -Path $path) -ne $ExpectedHashes[$path]) {
            throw "Existing handwritten admin contract '$path' changed during management contract generation."
        }
    }
}

function Get-ArtifactHashSnapshot {
    param([Parameter(Mandatory)][string[]]$Paths)

    $hashes = [ordered]@{}
    foreach ($path in $Paths) {
        Assert-Condition `
            (Test-Path -LiteralPath $path -PathType Leaf) `
            "Tracked AdminAccess artifact is missing at '$path'."
        $hashes[$path] = Get-FileSha256 -Path $path
    }

    return $hashes
}

function Assert-ArtifactHashSnapshotUnchanged {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$ExpectedHashes)

    foreach ($path in $ExpectedHashes.Keys) {
        if ((Get-FileSha256 -Path $path) -ne $ExpectedHashes[$path]) {
            throw "Check mode modified tracked AdminAccess artifact '$path'."
        }
    }
}

function Assert-ByteIdentical {
    param(
        [Parameter(Mandatory)][string]$ExpectedPath,
        [Parameter(Mandatory)][string]$ActualPath,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $ExpectedPath -PathType Leaf)) {
        throw "Tracked $Description is missing at '$ExpectedPath'."
    }
    if (-not (Test-ByteSequenceEqual `
            -Expected ([System.IO.File]::ReadAllBytes($ExpectedPath)) `
            -Actual ([System.IO.File]::ReadAllBytes($ActualPath)))) {
        throw "Tracked $Description differs from the regenerated artifact."
    }
}

function Publish-TrackedArtifacts {
    Assert-TrackedGeneratedArtifacts

    $artifacts = @(
        [pscustomobject]@{
            Staged = $capturedContractPath
            Destination = $trackedContractPath
            Existed = $false
            Backup = $null
            Staging = $null
        },
        [pscustomobject]@{
            Staged = $temporaryGeneratedTypesPath
            Destination = $trackedTypesPath
            Existed = $false
            Backup = $null
            Staging = $null
        }
    )
    $suffix = [Guid]::NewGuid().ToString('N')
    foreach ($artifact in $artifacts) {
        $destinationDirectory = Split-Path -Parent $artifact.Destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        $artifact.Existed = Test-Path -LiteralPath $artifact.Destination -PathType Leaf
        if ($artifact.Existed) {
            $artifact.Backup = "$($artifact.Destination).backup-$suffix"
            Copy-Item `
                -LiteralPath $artifact.Destination `
                -Destination $artifact.Backup `
                -ErrorAction Stop
        }
        $artifact.Staging = "$($artifact.Destination).staging-$suffix"
    }

    $publishError = $null
    try {
        foreach ($artifact in $artifacts) {
            Copy-Item `
                -LiteralPath $artifact.Staged `
                -Destination $artifact.Staging `
                -ErrorAction Stop
            if ($artifact.Existed) {
                [System.IO.File]::Replace(
                    $artifact.Staging,
                    $artifact.Destination,
                    $artifact.Backup)
            }
            else {
                [System.IO.File]::Move(
                    $artifact.Staging,
                    $artifact.Destination)
            }
        }

        foreach ($artifact in $artifacts) {
            Assert-ByteIdentical `
                -ExpectedPath $artifact.Destination `
                -ActualPath $artifact.Staged `
                -Description "published AdminAccess artifact"
        }
    }
    catch {
        $publishError = $_.Exception
    }
    finally {
        if ($null -ne $publishError) {
            foreach ($artifact in $artifacts) {
                try {
                    if ($artifact.Existed) {
                        Copy-Item `
                            -LiteralPath $artifact.Backup `
                            -Destination $artifact.Destination `
                            -Force
                    }
                    elseif (Test-Path -LiteralPath $artifact.Destination -PathType Leaf) {
                        Remove-Item -LiteralPath $artifact.Destination -Force
                    }
                }
                catch {
                    throw [AggregateException]::new(
                        "AdminAccess contract publishing failed and rollback was incomplete.",
                        @($publishError, $_.Exception))
                }
            }
        }

        foreach ($artifact in $artifacts) {
            if ($null -ne $artifact.Staging -and
                (Test-Path -LiteralPath $artifact.Staging -PathType Leaf)) {
                Remove-Item -LiteralPath $artifact.Staging -Force
            }
            if ($null -ne $artifact.Backup -and
                (Test-Path -LiteralPath $artifact.Backup -PathType Leaf)) {
                Remove-Item -LiteralPath $artifact.Backup -Force
            }
        }
    }

    if ($null -ne $publishError) {
        throw $publishError
    }

    Assert-TrackedGeneratedArtifacts -Require
}

Assert-PathWithin `
    -Path $temporaryRoot `
    -ParentPath ([System.IO.Path]::GetTempPath())
foreach ($handwrittenContractPath in $handwrittenContractPaths) {
    if (-not (Test-Path -LiteralPath $handwrittenContractPath -PathType Leaf)) {
        throw "Existing handwritten admin contract is missing at '$handwrittenContractPath'."
    }
}

$handwrittenContractHashes = [ordered]@{}
foreach ($handwrittenContractPath in $handwrittenContractPaths) {
    $handwrittenContractHashes[$handwrittenContractPath] =
        Get-FileSha256 -Path $handwrittenContractPath
}
Assert-AdminAccessContractGeneratorConfiguration
$trackedArtifactHashes = $null
if ($Check) {
    Assert-TrackedGeneratedArtifacts -Require
    Assert-ManagementContract -Path $trackedContractPath
    $trackedArtifactHashes = Get-ArtifactHashSnapshot -Paths @(
        $trackedContractPath,
        $trackedTypesPath
    )
}
$captureEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "NOVAGAME_ADMIN_ACCESS_OPENAPI_CAPTURE_PATH"
$inputEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "NOVAGAME_ADMIN_ACCESS_OPENAPI_INPUT"
$outputEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "NOVAGAME_ADMIN_ACCESS_TYPES_OUTPUT"
$pathEnvironment = Get-ProcessEnvironmentVariableSnapshot -Name "PATH"
$storeEnvironment = Get-ProcessEnvironmentVariableSnapshot `
    -Name "pnpm_config_store_dir"
$workflowError = $null

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    [Environment]::SetEnvironmentVariable(
        "NOVAGAME_ADMIN_ACCESS_OPENAPI_CAPTURE_PATH",
        $capturedContractPath,
        [EnvironmentVariableTarget]::Process)
    Invoke-CheckedCommand "pwsh" @(
        "-NoProfile",
        "-File", $verificationScript,
        "-Scope", "AdminManagement"
    )
    if (-not (Test-Path -LiteralPath $capturedContractPath -PathType Leaf)) {
        throw "The composed NovaGame API did not create '$capturedContractPath'."
    }

    $bootstrapArguments = @(
        "-NoProfile",
        "-File", (Join-Path $PSScriptRoot "bootstrap.ps1"),
        "-Scope", "Admin"
    )
    if (-not [string]::IsNullOrWhiteSpace($NodePath)) {
        $bootstrapArguments += @("-NodePath", $NodePath)
    }
    if (-not [string]::IsNullOrWhiteSpace($PnpmBootstrapPath)) {
        $bootstrapArguments += @("-PnpmBootstrapPath", $PnpmBootstrapPath)
    }
    Invoke-CheckedCommand "pwsh" $bootstrapArguments

    $toolchainManifestPath = Join-Path `
        $repositoryRoot `
        ".local\admin-toolchain.json"
    if (-not (Test-Path -LiteralPath $toolchainManifestPath -PathType Leaf)) {
        throw "Admin bootstrap did not create '$toolchainManifestPath'."
    }
    $toolchain = Get-Content `
        -LiteralPath $toolchainManifestPath `
        -Raw | ConvertFrom-Json
    foreach ($toolchainFile in @(
            [string]$toolchain.nodePath,
            [string]$toolchain.pnpmBootstrapPath)) {
        if (-not (Test-Path -LiteralPath $toolchainFile -PathType Leaf)) {
            throw "Admin toolchain file is missing at '$toolchainFile'."
        }
    }
    if ([string]$toolchain.pnpmVersion -ne "10.33.4") {
        throw "Admin toolchain must use pnpm 10.33.4."
    }

    [Environment]::SetEnvironmentVariable(
        "PATH",
        "$(Split-Path -Parent ([string]$toolchain.nodePath))$([System.IO.Path]::PathSeparator)$([string]$pathEnvironment.Value)",
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "pnpm_config_store_dir",
        [string]$toolchain.storePath,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "NOVAGAME_ADMIN_ACCESS_OPENAPI_INPUT",
        $capturedContractPath,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        "NOVAGAME_ADMIN_ACCESS_TYPES_OUTPUT",
        $temporaryGeneratedTypesRoot,
        [EnvironmentVariableTarget]::Process)
    Push-Location -LiteralPath $adminRoot
    try {
        Invoke-CheckedCommand ([string]$toolchain.nodePath) @(
            [string]$toolchain.pnpmBootstrapPath,
            "exec",
            "oxfmt",
            $capturedContractPath
        )
    }
    finally {
        Pop-Location
    }
    Normalize-Utf8LfFile -Path $capturedContractPath
    Assert-ManagementContract -Path $capturedContractPath
    Push-Location -LiteralPath $adminApplicationRoot
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

    Normalize-Utf8LfFile -Path $temporaryGeneratedTypesPath
    Assert-GeneratedArtifacts -Root $temporaryGeneratedTypesRoot
    if ($Check) {
        Assert-ByteIdentical `
            -ExpectedPath $trackedContractPath `
            -ActualPath $capturedContractPath `
            -Description "AdminAccess OpenAPI contract"
        Assert-ByteIdentical `
            -ExpectedPath $trackedTypesPath `
            -ActualPath $temporaryGeneratedTypesPath `
            -Description "AdminAccess generated types"
        Assert-TrackedGeneratedArtifacts -Require
    }
    else {
        Publish-TrackedArtifacts
    }
}
catch {
    $workflowError = $_.Exception
}

$finalizationErrors = [System.Collections.Generic.List[Exception]]::new()
try {
    Assert-HandwrittenContractsUnchanged -ExpectedHashes $handwrittenContractHashes
}
catch {
    $finalizationErrors.Add($_.Exception)
}
if ($Check) {
    try {
        Assert-ArtifactHashSnapshotUnchanged -ExpectedHashes $trackedArtifactHashes
    }
    catch {
        $finalizationErrors.Add($_.Exception)
    }
}

$environmentRestorations = [ordered]@{
    NOVAGAME_ADMIN_ACCESS_OPENAPI_CAPTURE_PATH = $captureEnvironment
    NOVAGAME_ADMIN_ACCESS_OPENAPI_INPUT = $inputEnvironment
    NOVAGAME_ADMIN_ACCESS_TYPES_OUTPUT = $outputEnvironment
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
        "AdminAccess contract update and final validation reported multiple failures.",
        $allErrors)
}

if ($Check) {
    Write-Output "AdminAccess OpenAPI contract and generated TypeScript types are current."
}
else {
    Write-Output "AdminAccess OpenAPI contract and generated TypeScript types updated."
}

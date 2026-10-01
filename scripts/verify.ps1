[CmdletBinding()]
param(
    [ValidateSet("Documentation", "Repository", "Server", "Database", "Authentication", "GameConfig", "AdminManagement", "AdminManagementAdmin", "AdminE2E", "Admin", "Client", "SwordMaster", "All")]
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

function Get-M35TaskStatus {
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^M35-\d{2}-T\d{2}$')]
        [string]$TaskId
    )

    $goalPath = Join-Path $repositoryRoot "docs\goals\m3.5.md"
    Assert-Condition `
        (Test-Path -LiteralPath $goalPath -PathType Leaf) `
        "M3.5 goal is missing at '$goalPath'."
    $goalContent = Get-Content -LiteralPath $goalPath -Raw
    $taskRegisterMatches = [regex]::Matches(
        $goalContent,
        '(?ms)^## Active Task Register\r?\n(?<body>.*?)(?=^##\s+|\z)')
    Assert-Condition `
        ($taskRegisterMatches.Count -eq 1) `
        "M3.5 must contain exactly one Active Task Register."
    $expectedStageId = $TaskId.Substring(0, 6)
    $taskPattern = '(?m)^\|[ \t]*' + [regex]::Escape($TaskId) +
        '[ \t]*\|[ \t]*' + [regex]::Escape($expectedStageId) +
        '[ \t]*\|[^\r\n]*\|[ \t]*' +
        '(?<status>pending|in_progress|blocked|complete)[ \t]*\|[ \t]*$'
    $taskMatches = [regex]::Matches(
        $taskRegisterMatches[0].Groups["body"].Value,
        $taskPattern)
    Assert-Condition `
        ($taskMatches.Count -eq 1) `
        "M3.5 task '$TaskId' must appear exactly once in the Active Task Register."

    return $taskMatches[0].Groups["status"].Value
}

function Test-M35ExternalNovaCoreCheckout {
    $expectedHead = "ef8db55173ff8f0286bb06abe753861c8a7d2d80"
    $configuredRoot = [Environment]::GetEnvironmentVariable(
        "NOVACORE_ROOT",
        [EnvironmentVariableTarget]::Process)
    $candidateRoot = if ([string]::IsNullOrWhiteSpace($configuredRoot)) {
        "D:\Programs\nova-core"
    }
    else {
        $configuredRoot.Trim()
    }

    Assert-Condition `
        (Test-Path -LiteralPath $candidateRoot -PathType Container) `
        "NovaCore source root was not found at '$candidateRoot'. Set NOVACORE_ROOT to override the default."
    $resolvedRoot = (Resolve-Path -LiteralPath $candidateRoot -ErrorAction Stop).Path

    $insideWorkTree = @(
        & git --no-optional-locks -C $resolvedRoot `
            rev-parse --is-inside-work-tree 2>$null)
    Assert-Condition `
        ($LASTEXITCODE -eq 0 -and
            $insideWorkTree.Count -eq 1 -and
            ([string]$insideWorkTree[0]).Trim() -eq "true") `
        "NovaCore source root '$resolvedRoot' is not a Git worktree."

    $headOutput = @(
        & git --no-optional-locks -C $resolvedRoot `
            rev-parse --verify "HEAD^{commit}" 2>$null)
    Assert-Condition `
        ($LASTEXITCODE -eq 0 -and $headOutput.Count -eq 1) `
        "Unable to resolve NovaCore HEAD at '$resolvedRoot'."
    $actualHead = ([string]$headOutput[0]).Trim().ToLowerInvariant()
    Assert-Condition `
        ($actualHead -eq $expectedHead) `
        "NovaCore source HEAD '$actualHead' differs from required audit commit '$expectedHead'."

    $statusOutput = @(
        & git --no-optional-locks -C $resolvedRoot `
            status --porcelain=v1 --untracked-files=all --ignore-submodules=none)
    Assert-Condition `
        ($LASTEXITCODE -eq 0) `
        "Unable to inspect NovaCore source status at '$resolvedRoot'."
    Assert-Condition `
        ($statusOutput.Count -eq 0) `
        "NovaCore source checkout '$resolvedRoot' is not clean:`n$($statusOutput -join "`n")"
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

function Invoke-AdminAcceptanceOwnershipScanner {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)][string[]]$SpecPaths,
        [string]$ExpectedFailureRule
    )

    $adminRoot = [System.IO.Path]::GetFullPath([string]$Toolchain.adminRoot)
    $relativeSpecPaths = @(
        $SpecPaths | ForEach-Object {
            $resolvedSpecPath = [System.IO.Path]::GetFullPath(
                (Join-Path $repositoryRoot $_))
            Assert-Condition `
                $resolvedSpecPath.StartsWith(
                    $adminRoot.TrimEnd('\') + '\',
                    [StringComparison]::OrdinalIgnoreCase) `
                "Admin acceptance ownership scan path '$_' is outside '$adminRoot'."
            [System.IO.Path]::GetRelativePath(
                $adminRoot,
                $resolvedSpecPath).Replace('\', '/')
        })
    $scannerSource = @'
import path from 'node:path';
import process from 'node:process';
import ts from 'typescript';

const [configArgument, ...fileArguments] = process.argv.slice(1);
const configPath = path.resolve(configArgument);
const configResult = ts.readConfigFile(configPath, ts.sys.readFile);
if (configResult.error) {
  process.stdout.write('typescript-config-invalid\n');
  process.exit(2);
}
const parsed = ts.parseJsonConfigFileContent(
  configResult.config,
  ts.sys,
  path.dirname(configPath),
  undefined,
  configPath,
);
const requestedFiles = fileArguments.map((value) => path.resolve(value));
const rootNames = [...new Set([...parsed.fileNames, ...requestedFiles])];
const program = ts.createProgram({ options: parsed.options, rootNames });
const checker = program.getTypeChecker();
const violations = [];
const adminRoot = path.dirname(configPath);
const evidenceHelperPath = path.resolve(
  adminRoot,
  'apps/web-ele/e2e/acceptance-evidence.ts',
);
const requestMethods = new Set([
  'delete',
  'fetch',
  'get',
  'head',
  'patch',
  'post',
  'put',
]);
const routeMethods = new Set(['route', 'unroute']);
const routeActionMethods = new Set([
  'abort',
  'continue',
  'fallback',
  'fulfill',
]);
const browserTypeConnectionMethods = new Set([
  'connect',
  'connectOverCDP',
  'launch',
  'launchPersistentContext',
]);
const screenshotMethods = new Set(['screenshot', 'toHaveScreenshot']);
const forbiddenTransportModules = new Set([
  'axios',
  'child_process',
  'http',
  'https',
  'net',
  'node:child_process',
  'node:http',
  'node:https',
  'node:net',
  'node:tls',
  'tls',
  'undici',
]);
const forbiddenDynamicModuleModules = new Set(['module', 'node:module']);
const reflectiveAcquisitionMethods = new Set([
  'get',
  'getOwnPropertyDescriptor',
]);
const processModuleAcquisitionMethods = new Set([
  'binding',
  'getBuiltinModule',
]);
const ownershipSensitiveTypes = new Set([
  'APIRequest',
  'APIRequestContext',
  'Browser',
  'BrowserContext',
  'BrowserType',
  'ElementHandle',
  'Locator',
  'Page',
  'Request',
  'Route',
]);

function containsType(type, names, location) {
  if (type.isUnionOrIntersection()) {
    return type.types.some((entry) => containsType(entry, names, location));
  }
  const rendered = checker.typeToString(
    type,
    location,
    ts.TypeFormatFlags.NoTruncation,
  );
  const symbolName = (type.aliasSymbol ?? type.getSymbol())?.getName();
  return [...names].some(
    (name) =>
      symbolName === name ||
      rendered === name ||
      rendered.endsWith(`.${name}`) ||
      rendered.startsWith(`${name}<`) ||
      rendered.includes(`.${name}<`),
  );
}

function typeAtLocationContains(node, names) {
  return containsType(checker.getTypeAtLocation(node), names, node);
}

function isStructuralTypeEscape(source, target, names) {
  return typeAtLocationContains(source, names) &&
    !typeAtLocationContains(target, names);
}

function originatesFromIdentifier(node, expectedName, visited = new Set()) {
  let current = node;
  while (
    ts.isAsExpression(current) ||
    ts.isTypeAssertionExpression(current) ||
    ts.isParenthesizedExpression(current) ||
    ts.isNonNullExpression(current)
  ) {
    current = current.expression;
  }
  if (!ts.isIdentifier(current)) {
    return false;
  }
  if (current.text === expectedName) {
    return true;
  }
  const symbol = checker.getSymbolAtLocation(current);
  if (!symbol || visited.has(symbol)) {
    return false;
  }
  visited.add(symbol);
  return Boolean(symbol.declarations?.some((declaration) =>
    ts.isVariableDeclaration(declaration) &&
    declaration.initializer &&
    originatesFromIdentifier(declaration.initializer, expectedName, visited)
  ));
}

function isReflectReceiver(node) {
  return originatesFromIdentifier(node, 'Reflect') ||
    typeAtLocationContains(node, new Set(['Reflect']));
}

function isProcessReceiver(node) {
  return originatesFromIdentifier(node, 'process') ||
    typeAtLocationContains(node, new Set(['Process']));
}

function memberName(node) {
  if (ts.isPropertyAccessExpression(node)) {
    return node.name.text;
  }
  if (
    ts.isElementAccessExpression(node) &&
    (ts.isStringLiteral(node.argumentExpression) ||
      ts.isNoSubstitutionTemplateLiteral(node.argumentExpression))
  ) {
    return node.argumentExpression.text;
  }
  return undefined;
}

function memberReceiver(node) {
  return ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)
    ? node.expression
    : undefined;
}

function signatureOwnerNames(call) {
  const names = new Set();
  const declaration = checker.getResolvedSignature(call)?.declaration;
  let current = declaration;
  while (current) {
    if (
      (ts.isInterfaceDeclaration(current) ||
        ts.isClassDeclaration(current) ||
        ts.isTypeAliasDeclaration(current)) &&
      current.name
    ) {
      names.add(current.name.text);
    }
    current = current.parent;
  }
  return names;
}

function addViolation(sourceFile, node, rule) {
  const position = sourceFile.getLineAndCharacterOfPosition(node.getStart());
  violations.push(
    `${rule}@${path.basename(sourceFile.fileName)}:${position.line + 1}:${position.character + 1}`,
  );
}

function isPlaywrightFixturePageIdentifier(node) {
  if (!ts.isIdentifier(node) || node.text !== 'page') {
    return false;
  }
  const symbol = checker.getSymbolAtLocation(node);
  return Boolean(symbol?.declarations?.some((declaration) => {
    if (
      !ts.isBindingElement(declaration) ||
      !ts.isIdentifier(declaration.name) ||
      declaration.name.text !== 'page' ||
      !ts.isObjectBindingPattern(declaration.parent)
    ) {
      return false;
    }
    const propertyName = declaration.propertyName;
    if (
      propertyName &&
      !(
        (ts.isIdentifier(propertyName) || ts.isStringLiteral(propertyName)) &&
        propertyName.text === 'page'
      )
    ) {
      return false;
    }
    const parameter = declaration.parent.parent;
    if (!ts.isParameter(parameter)) {
      return false;
    }
    const callback = parameter.parent;
    if (!ts.isArrowFunction(callback) && !ts.isFunctionExpression(callback)) {
      return false;
    }
    const call = callback.parent;
    return ts.isCallExpression(call) &&
      call.arguments.includes(callback) &&
      ts.isIdentifier(call.expression) &&
      call.expression.text === 'test' &&
      containsType(
        checker.getTypeAtLocation(declaration.name),
        new Set(['Page']),
        declaration.name,
      );
  }));
}

function isExactAuditedApiRequestArgument(node) {
  const call = node.parent;
  if (
    !ts.isPropertyAccessExpression(node) ||
    node.name.text !== 'request' ||
    !isPlaywrightFixturePageIdentifier(node.expression) ||
    !ts.isCallExpression(call) ||
    call.arguments[0] !== node
  ) {
    return false;
  }
  const callee = call.expression;
  const receiver = memberReceiver(callee);
  return memberName(callee) === 'apiRequest' && receiver &&
    containsType(
      checker.getTypeAtLocation(receiver),
      new Set(['AcceptanceEvidenceAudit']),
      receiver,
    );
}

function isDirectCaptureCall(node) {
  const call = node.parent;
  if (!ts.isCallExpression(call) || call.expression !== node) {
    return false;
  }
  const receiver = memberReceiver(node);
  return memberName(node) === 'captureScreenshot' && receiver &&
    containsType(
      checker.getTypeAtLocation(receiver),
      new Set(['AcceptanceEvidenceAudit']),
      receiver,
    );
}

function inspectModuleSpecifier(sourceFile, node, value) {
  if (forbiddenTransportModules.has(value)) {
    addViolation(sourceFile, node, 'direct-transport');
  }
  if (forbiddenDynamicModuleModules.has(value)) {
    addViolation(sourceFile, node, 'dynamic-module-loading');
  }
  if (!value.startsWith('.')) {
    return;
  }
  const resolved = ts.resolveModuleName(
    value,
    sourceFile.fileName,
    parsed.options,
    ts.sys,
  ).resolvedModule?.resolvedFileName;
  if (/\.(?:cjs|mjs|js)$/i.test(value) || (resolved && /\.(?:cjs|mjs|js)$/i.test(resolved))) {
    addViolation(sourceFile, node, 'local-javascript-source');
  }
}

function inspect(sourceFile, node) {
  if (node.kind === ts.SyntaxKind.AnyKeyword) {
    addViolation(sourceFile, node, 'unsafe-type-escape');
  }
  if (
    ts.isTypeReferenceNode(node) &&
    node.typeName.getText(sourceFile).endsWith('APIRequestContext')
  ) {
    addViolation(sourceFile, node, 'direct-api-source');
  }
  if (
    ts.isImportDeclaration(node) &&
    ts.isStringLiteral(node.moduleSpecifier)
  ) {
    inspectModuleSpecifier(sourceFile, node.moduleSpecifier, node.moduleSpecifier.text);
  }
  if (
    ts.isCallExpression(node) &&
    node.expression.kind === ts.SyntaxKind.ImportKeyword
  ) {
    addViolation(sourceFile, node, 'dynamic-module-loading');
    const moduleArgument = node.arguments[0];
    if (
      node.arguments.length === 1 &&
      moduleArgument &&
      (ts.isStringLiteral(moduleArgument) ||
        ts.isNoSubstitutionTemplateLiteral(moduleArgument))
    ) {
      inspectModuleSpecifier(sourceFile, moduleArgument, moduleArgument.text);
    }
  }
  if (
    ts.isCallExpression(node) &&
    ts.isIdentifier(node.expression) &&
    node.expression.text === 'require'
  ) {
    addViolation(sourceFile, node, 'dynamic-module-loading');
  }
  if (ts.isAsExpression(node) || ts.isTypeAssertionExpression(node)) {
    if (
      containsType(
        checker.getTypeAtLocation(node),
        new Set(['APIRequestContext']),
        node,
      )
    ) {
      addViolation(sourceFile, node, 'direct-api-source');
    }
    if (isStructuralTypeEscape(node.expression, node, ownershipSensitiveTypes)) {
      addViolation(sourceFile, node, 'unsafe-type-escape');
    }
    if (
      isStructuralTypeEscape(node.expression, node, new Set(['Reflect', 'Process']))
    ) {
      addViolation(sourceFile, node, 'dynamic-module-loading');
    }
  }
  if (
    ts.isBindingElement(node) &&
    containsType(
      checker.getTypeAtLocation(node.name),
      new Set(['APIRequestContext']),
      node.name,
    )
  ) {
    addViolation(sourceFile, node, 'direct-api-source');
  }
  if (
    ts.isVariableDeclaration(node) &&
    node.initializer &&
    containsType(
      checker.getTypeAtLocation(node.initializer),
      new Set(['APIRequestContext']),
      node.initializer,
    )
  ) {
    addViolation(sourceFile, node, 'direct-api-source');
  }
  if (
    ts.isVariableDeclaration(node) &&
    node.initializer &&
    isStructuralTypeEscape(
      node.initializer,
      node.name,
      ownershipSensitiveTypes,
    )
  ) {
    addViolation(sourceFile, node, 'unsafe-type-escape');
  }
  if (
    ts.isBinaryExpression(node) &&
    node.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
    isStructuralTypeEscape(
      node.right,
      node.left,
      ownershipSensitiveTypes,
    )
  ) {
    addViolation(sourceFile, node, 'unsafe-type-escape');
  }
  if (
    ts.isReturnStatement(node) &&
    node.expression &&
    containsType(
      checker.getTypeAtLocation(node.expression),
      new Set(['APIRequestContext']),
      node.expression,
    )
  ) {
    addViolation(sourceFile, node, 'direct-api-source');
  }
  if (
    ts.isVariableDeclaration(node) &&
    ts.isObjectBindingPattern(node.name) &&
    node.initializer
  ) {
    const initializerType = checker.getTypeAtLocation(node.initializer);
    for (const element of node.name.elements) {
      const originalName = element.propertyName &&
        (ts.isIdentifier(element.propertyName) ||
          ts.isStringLiteral(element.propertyName))
        ? element.propertyName.text
        : ts.isIdentifier(element.name)
          ? element.name.text
          : undefined;
      if (
        originalName === 'newContext' &&
        containsType(
          initializerType,
          new Set(['APIRequest', 'Browser']),
          node.initializer,
        )
      ) {
        addViolation(sourceFile, element, 'browser-context-creation');
      }
      if (
        originalName &&
        requestMethods.has(originalName) &&
        containsType(
          initializerType,
          new Set(['APIRequestContext']),
          node.initializer,
        )
      ) {
        addViolation(sourceFile, element, 'direct-api-request');
      }
      if (
        originalName === 'request' &&
        containsType(
          initializerType,
          new Set(['BrowserContext', 'Page']),
          node.initializer,
        )
      ) {
        addViolation(sourceFile, element, 'direct-api-source');
      }
      if (
        originalName &&
        reflectiveAcquisitionMethods.has(originalName) &&
        isReflectReceiver(node.initializer)
      ) {
        addViolation(sourceFile, element, 'unsafe-type-escape');
      }
      if (
        originalName &&
        processModuleAcquisitionMethods.has(originalName) &&
        isProcessReceiver(node.initializer)
      ) {
        addViolation(sourceFile, element, 'dynamic-module-loading');
      }
    }
  }
  if (ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) {
    const name = memberName(node);
    const receiver = memberReceiver(node);
    const receiverType = checker.getTypeAtLocation(receiver);
    const dynamicMember = name === undefined;
    if (
      name &&
      reflectiveAcquisitionMethods.has(name) &&
      isReflectReceiver(receiver)
    ) {
      addViolation(sourceFile, node, 'unsafe-type-escape');
    }
    if (
      name &&
      processModuleAcquisitionMethods.has(name) &&
      isProcessReceiver(receiver)
    ) {
      addViolation(sourceFile, node, 'dynamic-module-loading');
    }
    if (
      (name === 'newContext' || dynamicMember) &&
      containsType(receiverType, new Set(['APIRequest', 'Browser']), receiver)
    ) {
      addViolation(sourceFile, node, 'browser-context-creation');
    }
    if (
      (name === 'newPage' || dynamicMember) &&
      containsType(receiverType, new Set(['Browser']), receiver)
    ) {
      addViolation(sourceFile, node, 'browser-context-creation');
    }
    if (
      (dynamicMember || (name && browserTypeConnectionMethods.has(name))) &&
      containsType(receiverType, new Set(['BrowserType']), receiver)
    ) {
      addViolation(sourceFile, node, 'browser-process-creation');
    }
    if (
      name === 'request' &&
      containsType(receiverType, new Set(['BrowserContext', 'Page']), receiver) &&
      !isExactAuditedApiRequestArgument(node)
    ) {
      addViolation(sourceFile, node, 'direct-api-source');
    }
    if (
      (dynamicMember || (name && requestMethods.has(name))) &&
      containsType(receiverType, new Set(['APIRequestContext']), receiver)
    ) {
      addViolation(sourceFile, node, 'direct-api-request');
    }
    if (
      ((dynamicMember || (name && routeMethods.has(name))) &&
        containsType(receiverType, new Set(['BrowserContext', 'Page']), receiver)) ||
      ((dynamicMember || (name && routeActionMethods.has(name))) &&
        containsType(receiverType, new Set(['Route']), receiver))
    ) {
      addViolation(sourceFile, node, 'request-interception');
    }
    if (
      (dynamicMember || (name && screenshotMethods.has(name))) &&
      (name === 'toHaveScreenshot' ||
        containsType(
          receiverType,
          new Set(['ElementHandle', 'Locator', 'Page']),
          receiver,
        ))
    ) {
      addViolation(sourceFile, node, 'screenshot-ownership');
    }
    if (name === 'captureScreenshot' && !isDirectCaptureCall(node)) {
      addViolation(sourceFile, node, 'screenshot-ownership');
    }
    if (name === 'observeContext' || name === 'assertScreenshotSurface') {
      addViolation(sourceFile, node, 'evidence-internal-access');
    }
    if (
      containsType(
        checker.getTypeAtLocation(node),
        new Set(['APIRequestContext']),
        node,
      ) &&
      !isExactAuditedApiRequestArgument(node)
    ) {
      addViolation(sourceFile, node, 'direct-api-source');
    }
  }

  if (ts.isCallExpression(node)) {
    const callee = node.expression;
    const name = memberName(callee);
    const receiver = memberReceiver(callee);
    const receiverType = receiver
      ? checker.getTypeAtLocation(receiver)
      : undefined;
    const owners = signatureOwnerNames(node);
    for (const argument of node.arguments) {
      if (
        containsType(
          checker.getTypeAtLocation(argument),
          new Set(['APIRequestContext']),
          argument,
        ) &&
        !isExactAuditedApiRequestArgument(argument)
      ) {
        addViolation(sourceFile, argument, 'direct-api-source');
      }
    }
    if (
      (name && requestMethods.has(name) && receiverType &&
        containsType(receiverType, new Set(['APIRequestContext']), receiver)) ||
      (name && requestMethods.has(name) && owners.has('APIRequestContext'))
    ) {
      addViolation(sourceFile, node, 'direct-api-request');
    }
    if (
      (name === 'newContext' && receiverType &&
        containsType(receiverType, new Set(['APIRequest', 'Browser']), receiver)) ||
      (name === 'newContext' &&
        (owners.has('APIRequest') || owners.has('Browser')))
    ) {
      addViolation(sourceFile, node, 'browser-context-creation');
    }
    if (
      (name === 'newPage' && receiverType &&
        containsType(receiverType, new Set(['Browser']), receiver)) ||
      (name === 'newPage' && owners.has('Browser'))
    ) {
      addViolation(sourceFile, node, 'browser-context-creation');
    }
    if (
      (name && browserTypeConnectionMethods.has(name) && receiverType &&
        containsType(receiverType, new Set(['BrowserType']), receiver)) ||
      (name && browserTypeConnectionMethods.has(name) && owners.has('BrowserType'))
    ) {
      addViolation(sourceFile, node, 'browser-process-creation');
    }
    if (
      (name && routeMethods.has(name) && receiverType &&
        containsType(receiverType, new Set(['BrowserContext', 'Page']), receiver)) ||
      (name && routeMethods.has(name) &&
        (owners.has('BrowserContext') || owners.has('Page'))) ||
      (name && routeActionMethods.has(name) && receiverType &&
        containsType(receiverType, new Set(['Route']), receiver)) ||
      (name && routeActionMethods.has(name) && owners.has('Route'))
    ) {
      addViolation(sourceFile, node, 'request-interception');
    }
    if (
      name === 'fetch' ||
      (ts.isIdentifier(callee) && callee.text === 'fetch')
    ) {
      addViolation(sourceFile, node, 'direct-transport');
    }
    if (
      name === 'toHaveScreenshot' ||
      (name === 'screenshot' && receiverType &&
        containsType(
          receiverType,
          new Set(['ElementHandle', 'Locator', 'Page']),
          receiver,
        ))
    ) {
      addViolation(sourceFile, node, 'screenshot-ownership');
    }
    if (
      ts.isIdentifier(callee) &&
      new Set(['eval', 'Function']).has(callee.text)
    ) {
      addViolation(sourceFile, node, 'dynamic-code-execution');
    }
  }
  ts.forEachChild(node, (child) => inspect(sourceFile, child));
}

const invalidRequestedFiles = new Set();
for (const filePath of requestedFiles) {
  const sourceFile = program.getSourceFile(filePath);
  if (!sourceFile) {
    violations.push(`typescript-source-missing@${path.basename(filePath)}:1:1`);
    continue;
  }
  const diagnostics = [
    ...program.getSyntacticDiagnostics(sourceFile),
    ...program.getSemanticDiagnostics(sourceFile),
  ];
  if (diagnostics.length > 0) {
    invalidRequestedFiles.add(path.resolve(sourceFile.fileName));
    const diagnostic = diagnostics[0];
    const position = diagnostic.file && diagnostic.start !== undefined
      ? diagnostic.file.getLineAndCharacterOfPosition(diagnostic.start)
      : { line: 0, character: 0 };
    violations.push(
      `typescript-type-resolution-failed@${path.basename(filePath)}:${position.line + 1}:${position.character + 1}`,
    );
  }
}

for (const sourceFile of program.getSourceFiles()) {
  const sourcePath = path.resolve(sourceFile.fileName);
  const relativePath = path.relative(adminRoot, sourcePath);
  if (
    sourceFile.isDeclarationFile ||
    relativePath.startsWith(`..${path.sep}`) ||
    path.isAbsolute(relativePath) ||
    sourcePath === evidenceHelperPath ||
    invalidRequestedFiles.has(sourcePath) ||
    !/\.(?:cts|mts|tsx?)$/i.test(sourcePath)
  ) {
    continue;
  }
  inspect(sourceFile, sourceFile);
}

if (violations.length > 0) {
  process.stdout.write(`${[...new Set(violations)].sort().join('\n')}\n`);
  process.exit(1);
}
'@

    Push-Location -LiteralPath $adminRoot
    try {
        $scannerOutput = @(
            & ([string]$Toolchain.nodePath) `
                "--no-warnings" `
                "--input-type=module" `
                "--eval" `
                $scannerSource `
                "tsconfig.e2e.json" `
                @relativeSpecPaths 2>&1)
        $scannerExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedFailureRule)) {
        Assert-Condition `
            ($scannerExitCode -eq 0) `
            "Admin acceptance ownership scan failed: $($scannerOutput -join ', ')."
        return
    }
    Assert-Condition `
        ($scannerExitCode -ne 0 -and
            @($scannerOutput | Where-Object {
                    ([string]$_).StartsWith(
                        "$ExpectedFailureRule@",
                        [StringComparison]::Ordinal)
                }).Count -ge 1) `
        "Admin acceptance ownership scan accepted the '$ExpectedFailureRule' negative probe."
}

function Test-AdminAcceptanceOwnershipPolicy {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)][string[]]$SpecPaths
    )

    Invoke-AdminAcceptanceOwnershipScanner `
        -Toolchain $Toolchain `
        -SpecPaths $SpecPaths
    $probeRelativePath =
        "admin/apps/web-ele/e2e/.novagame-acceptance-ownership-probe.ts"
    $probePath = Join-Path $repositoryRoot $probeRelativePath
    $helperProbeRelativePath =
        "admin/apps/web-ele/e2e/.novagame-acceptance-ownership-probe-helper.ts"
    $helperProbePath = Join-Path $repositoryRoot $helperProbeRelativePath
    Assert-Condition `
        (-not (Test-Path -LiteralPath $probePath)) `
        "Admin acceptance ownership probe path already exists."
    Assert-Condition `
        (-not (Test-Path -LiteralPath $helperProbePath)) `
        "Admin acceptance ownership helper probe path already exists."
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    try {
        foreach ($probe in @(
                [pscustomobject]@{
                    Rule = "direct-api-request"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const aliasedPage = page;
await aliasedPage.request.post('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "direct-api-request"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const { request: aliasedRequest } = page;
await aliasedRequest.fetch('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "browser-context-creation"
                    Source = @'
import type { Browser } from '@playwright/test';
declare const browser: Browser;
const { newContext: createContext } = browser;
await createContext();
'@
                },
                [pscustomobject]@{
                    Rule = "request-interception"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const aliasedPage = page;
await aliasedPage.route('**/api/**', async (route) => route.abort());
'@
                },
                [pscustomobject]@{
                    Rule = "direct-api-request"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const post = page.request.post.bind(page.request);
await post('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "request-interception"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const intercept = page.route;
await intercept('**/api/**', async () => {});
'@
                },
                [pscustomobject]@{
                    Rule = "request-interception"
                    Source = @'
import type { Route } from '@playwright/test';
declare const route: Route;
const abort = route.abort.bind(route);
await abort();
'@
                },
                [pscustomobject]@{
                    Rule = "browser-context-creation"
                    Source = @'
import type { Browser } from '@playwright/test';
declare const browser: Browser;
await browser.newPage();
'@
                },
                [pscustomobject]@{
                    Rule = "browser-process-creation"
                    Source = @'
import { chromium } from '@playwright/test';
await chromium.launch();
'@
                },
                [pscustomobject]@{
                    Rule = "browser-process-creation"
                    Source = @'
import { chromium } from '@playwright/test';
await chromium.launchPersistentContext('output/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "browser-process-creation"
                    Source = @'
import { chromium } from '@playwright/test';
await chromium.connect('ws://127.0.0.1:1');
'@
                },
                [pscustomobject]@{
                    Rule = "browser-process-creation"
                    Source = @'
import { chromium } from '@playwright/test';
await chromium.connectOverCDP('http://127.0.0.1:1');
'@
                },
                [pscustomobject]@{
                    Rule = "direct-api-source"
                    Source = @'
import type { Page } from '@playwright/test';
import type { AcceptanceEvidenceAudit } from './acceptance-evidence';
declare const evidence: AcceptanceEvidenceAudit;
declare const page: Page;
const requestContext = page.request;
await evidence.apiRequest(requestContext, 'GET', 'http://127.0.0.1/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "direct-api-source"
                    Source = @'
import type { Page } from '@playwright/test';
import type { AcceptanceEvidenceAudit } from './acceptance-evidence';
declare const evidence: AcceptanceEvidenceAudit;
declare const page: Page;
await evidence.apiRequest(
  page.context().request,
  'GET',
  'http://127.0.0.1/api/probe',
);
'@
                },
                [pscustomobject]@{
                    Rule = "unsafe-type-escape"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
await (page as any).request.post('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "unsafe-type-escape"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const hiddenPage = page as unknown as {
  request: { get(url: string): Promise<unknown> };
};
await hiddenPage.request.get('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "unsafe-type-escape"
                    Source = @'
import type { Page } from '@playwright/test';
interface HiddenPage {
  request: { get(url: string): Promise<unknown> };
}
declare const page: Page;
const hiddenPage: HiddenPage = page;
await hiddenPage.request.get('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "unsafe-type-escape"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const propertyName: string = 'request';
const reflection = Reflect;
const hiddenRequest = reflection.get(page, propertyName);
await hiddenRequest.post('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "unsafe-type-escape"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const acquire = Reflect.get;
const hiddenRequest = acquire(page, 'request');
await hiddenRequest.get('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "unsafe-type-escape"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
const { get: acquire } = Reflect;
const hiddenRequest = acquire(page, 'request');
await hiddenRequest.get('/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "dynamic-module-loading"
                    Source = @'
const runtimeProcess = process;
const loadModule = runtimeProcess.getBuiltinModule;
const transport = loadModule('node:http');
transport.get('http://127.0.0.1/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "dynamic-module-loading"
                    Source = @'
const runtimeProcess = process as unknown as {
  binding(name: string): unknown;
};
const { binding: loadBinding } = runtimeProcess;
loadBinding('fs');
'@
                },
                [pscustomobject]@{
                    Rule = "direct-transport"
                    Source = @'
await fetch('http://127.0.0.1/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "direct-transport"
                    Source = @'
import { request } from 'node:http';
request('http://127.0.0.1/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "direct-transport"
                    Source = @'
import { spawnSync } from 'node:child_process';
spawnSync('curl', ['http://127.0.0.1/api/probe']);
'@
                },
                [pscustomobject]@{
                    Rule = "dynamic-module-loading"
                    Source = @'
const transport = await import(`node:http`);
transport.request('http://127.0.0.1/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "dynamic-module-loading"
                    Source = @'
import { createRequire } from 'node:module';
const loadModule = createRequire(import.meta.url);
const transport = loadModule('node:http');
transport.request('http://127.0.0.1/api/probe');
'@
                },
                [pscustomobject]@{
                    Rule = "local-javascript-source"
                    Source = @'
// @ts-ignore -- the scanner must fail closed before JavaScript can hide transport.
import './hidden-transport.js';
'@
                },
                [pscustomobject]@{
                    Rule = "screenshot-ownership"
                    Source = @'
import type { Page } from '@playwright/test';
declare const page: Page;
await page.screenshot({ path: 'output/probe.png' });
'@
                },
                [pscustomobject]@{
                    Rule = "screenshot-ownership"
                    Source = @'
import type { Locator } from '@playwright/test';
declare const locator: Locator;
await locator.screenshot({ path: 'output/probe.png' });
'@
                },
                [pscustomobject]@{
                    Rule = "screenshot-ownership"
                    Source = @'
import type { Page } from '@playwright/test';
import { expect } from '@playwright/test';
declare const page: Page;
await expect(page).toHaveScreenshot();
'@
                })) {
            [System.IO.File]::WriteAllText(
                $probePath,
                [string]$probe.Source,
                $utf8)
            Invoke-AdminAcceptanceOwnershipScanner `
                -Toolchain $Toolchain `
                -SpecPaths @($probeRelativePath) `
                -ExpectedFailureRule ([string]$probe.Rule)
        }
        [System.IO.File]::WriteAllText(
            $helperProbePath,
            @'
import type { Browser } from '@playwright/test';

export async function createHiddenContext(browser: Browser) {
  return browser.newContext();
}
'@,
            $utf8)
        [System.IO.File]::WriteAllText(
            $probePath,
            @'
import type { Browser } from '@playwright/test';
import { createHiddenContext } from './.novagame-acceptance-ownership-probe-helper';

declare const browser: Browser;
await createHiddenContext(browser);
'@,
            $utf8)
        Invoke-AdminAcceptanceOwnershipScanner `
            -Toolchain $Toolchain `
            -SpecPaths @($probeRelativePath) `
            -ExpectedFailureRule "browser-context-creation"
        [System.IO.File]::WriteAllText(
            $helperProbePath,
            @'
import type { Page } from '@playwright/test';

export function hiddenRequestContext(page: Page) {
  return page.request;
}
'@,
            $utf8)
        [System.IO.File]::WriteAllText(
            $probePath,
            @'
import type { Page } from '@playwright/test';
import type { AcceptanceEvidenceAudit } from './acceptance-evidence';
import { hiddenRequestContext } from './.novagame-acceptance-ownership-probe-helper';

declare const evidence: AcceptanceEvidenceAudit;
declare const page: Page;
await evidence.apiRequest(
  hiddenRequestContext(page),
  'GET',
  'http://127.0.0.1/api/probe',
);
'@,
            $utf8)
        Invoke-AdminAcceptanceOwnershipScanner `
            -Toolchain $Toolchain `
            -SpecPaths @($probeRelativePath) `
            -ExpectedFailureRule "direct-api-source"
    }
    finally {
        if (Test-Path -LiteralPath $probePath -PathType Leaf) {
            Remove-Item -LiteralPath $probePath -Force
        }
        if (Test-Path -LiteralPath $helperProbePath -PathType Leaf) {
            Remove-Item -LiteralPath $helperProbePath -Force
        }
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

function Invoke-AdminPnpmIsolated {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][System.Collections.IDictionary]$Environment,
        [int]$TimeoutSeconds = 300
    )

    Assert-Condition `
        ($TimeoutSeconds -gt 0 -and $TimeoutSeconds -le 600) `
        "Isolated SwordMaster admin acceptance timeout must be from 1 through 600 seconds."

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [string]$Toolchain.nodePath
    $startInfo.WorkingDirectory = [string]$Toolchain.adminRoot
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
            "Isolated SwordMaster admin acceptance variables must use the SWORDMASTER_ACCEPTANCE_ prefix."
        $startInfo.Environment[$name] = [string]$entry.Value
    }

    $null = $startInfo.ArgumentList.Add([string]$Toolchain.pnpmBootstrapPath)
    foreach ($argument in $ArgumentList) {
        $null = $startInfo.ArgumentList.Add([string]$argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        Assert-Condition $process.Start() "Unable to start the isolated SwordMaster admin acceptance process."
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            $process.Kill($true)
        }
        $process.WaitForExit()
        $standardOutputText = $standardOutput.GetAwaiter().GetResult()
        $standardErrorText = $standardError.GetAwaiter().GetResult()
        $diagnostics = Get-SanitizedIsolatedAcceptanceDiagnostics `
            -StandardOutput $standardOutputText `
            -StandardError $standardErrorText `
            -Environment $Environment
        if (-not $completed) {
            throw "The isolated SwordMaster admin acceptance process exceeded $TimeoutSeconds seconds. $diagnostics"
        }
        if ($process.ExitCode -ne 0) {
            throw "The isolated SwordMaster admin acceptance process failed with exit code $($process.ExitCode). $diagnostics"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-SanitizedIsolatedAcceptanceDiagnostics {
    param(
        [AllowEmptyString()][string]$StandardOutput,
        [AllowEmptyString()][string]$StandardError,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Environment
    )

    $diagnostics = "stdout:`n$StandardOutput`nstderr:`n$StandardError"
    $sensitiveValues = @(
        $Environment.GetEnumerator() |
            ForEach-Object { ([string]$_.Value).Trim() } |
            Where-Object { $_.Length -ge 4 } |
            Sort-Object -Unique -Descending { $_.Length }
    )
    foreach ($sensitiveValue in $sensitiveValues) {
        $diagnostics = $diagnostics.Replace($sensitiveValue, "[REDACTED]")
    }

    $diagnostics = $diagnostics.Trim()
    if ([string]::IsNullOrWhiteSpace($diagnostics)) {
        return "No child-process diagnostics were captured."
    }
    if ($diagnostics.Length -gt 4000) {
        return "$($diagnostics.Substring(0, 4000))`n[truncated]"
    }

    return $diagnostics
}

function Initialize-AdminManagementDynamicSecretCollectorType {
    if ("NovaGame.Verification.DynamicSecretPipeCollector" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NovaGame.Verification
{
    public sealed class DynamicSecretRecord
    {
        internal DynamicSecretRecord(byte kind, string value)
        {
            Kind = kind;
            Value = value;
        }

        public byte Kind { get; }
        public string Value { get; }
    }

    public sealed class DynamicSecretPipeCollector : IDisposable
    {
        private const int MaximumRegistrations = 512;
        private const int MaximumSecretBytes = 16 * 1024;
        private const int MaximumTotalSecretBytes = 1024 * 1024;
        private static readonly Regex AccessTokenPattern = new Regex(
            @"^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        private readonly byte[] authenticationKey;
        private readonly CancellationTokenSource acceptCancellation =
            new CancellationTokenSource();
        private readonly object gate = new object();
        private readonly string pipeName;
        private readonly ManualResetEventSlim ready =
            new ManualResetEventSlim(false);
        private readonly Dictionary<string, DynamicSecretRecord> records =
            new Dictionary<string, DynamicSecretRecord>(StringComparer.Ordinal);
        private string faultRule;
        private Task loopTask;
        private int registrationCount;
        private int totalSecretBytes;
        private bool disposed;
        private bool joined;
        private bool stopped;

        private DynamicSecretPipeCollector(
            string pipeName,
            byte[] authenticationKey)
        {
            this.pipeName = pipeName;
            this.authenticationKey = (byte[])authenticationKey.Clone();
        }

        public static DynamicSecretPipeCollector Start(
            string pipeName,
            byte[] authenticationKey)
        {
            if (string.IsNullOrWhiteSpace(pipeName) ||
                authenticationKey == null ||
                authenticationKey.Length != 32)
            {
                throw new InvalidOperationException(
                    "dynamic-secret-registry-configuration");
            }

            var collector = new DynamicSecretPipeCollector(
                pipeName,
                authenticationKey);
            collector.loopTask = Task.Run(collector.RunAsync);
            return collector;
        }

        public void WaitUntilReady(TimeSpan timeout)
        {
            if (!ready.Wait(timeout))
            {
                throw new InvalidOperationException(
                    "dynamic-secret-registry-start-timeout");
            }

            ThrowIfFaulted();
        }

        public DynamicSecretRecord[] StopAndSnapshot(TimeSpan timeout)
        {
            RequestStop();
            Join(timeout);
            ThrowIfFaulted();
            return SnapshotForCleanup();
        }

        public DynamicSecretRecord[] SnapshotForCleanup()
        {
            lock (gate)
            {
                if (!joined)
                {
                    throw new InvalidOperationException(
                        "dynamic-secret-registry-cleanup-snapshot-before-join");
                }

                return records.Values.ToArray();
            }
        }

        public void Dispose()
        {
            Dispose(TimeSpan.FromSeconds(10));
        }

        public void Dispose(TimeSpan timeout)
        {
            RequestStop();
            Join(timeout);

            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                CryptographicOperations.ZeroMemory(authenticationKey);
                acceptCancellation.Dispose();
                ready.Dispose();
            }
        }

        private void RequestStop()
        {
            var cancelAccept = false;
            lock (gate)
            {
                if (!stopped)
                {
                    stopped = true;
                    cancelAccept = true;
                }
            }

            if (cancelAccept)
            {
                acceptCancellation.Cancel();
            }
        }

        private void Join(TimeSpan timeout)
        {
            var task = loopTask;
            if (task == null || !task.Wait(timeout))
            {
                throw new InvalidOperationException(
                    "dynamic-secret-registry-stop-timeout");
            }

            lock (gate)
            {
                joined = true;
            }
        }

        private async Task RunAsync()
        {
            try
            {
                while (!acceptCancellation.IsCancellationRequested)
                {
                    using (var pipe = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                    {
                        ready.Set();
                        try
                        {
                            await pipe.WaitForConnectionAsync(
                                acceptCancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (acceptCancellation.IsCancellationRequested)
                        {
                            if (pipe.IsConnected)
                            {
                                await HandleConnectionAsync(pipe)
                                    .ConfigureAwait(false);
                            }
                            break;
                        }

                        await HandleConnectionAsync(pipe).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                SetFault("dynamic-secret-registry-protocol");
            }
            finally
            {
                ready.Set();
            }
        }

        private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
        {
            using (var timeout = new CancellationTokenSource())
            {
                // Stopping the accept loop must not turn a partial accepted frame
                // into a successful shutdown. Every accepted frame drains or faults.
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var header = new byte[10];
                await ReadExactAsync(pipe, header, timeout.Token)
                    .ConfigureAwait(false);
                if (header[0] != (byte)'N' ||
                    header[1] != (byte)'G' ||
                    header[2] != (byte)'D' ||
                    header[3] != (byte)'S' ||
                    header[4] != 1 ||
                    (header[5] != 1 && header[5] != 2))
                {
                    throw new InvalidDataException();
                }

                var secretLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                    new ReadOnlySpan<byte>(header, 6, 4)));
                if (secretLength <= 0 || secretLength > MaximumSecretBytes)
                {
                    throw new InvalidDataException();
                }

                var secretBytes = new byte[secretLength];
                var suppliedMac = new byte[32];
                await ReadExactAsync(pipe, secretBytes, timeout.Token)
                    .ConfigureAwait(false);
                await ReadExactAsync(pipe, suppliedMac, timeout.Token)
                    .ConfigureAwait(false);
                var authenticatedPayload = new byte[header.Length + secretBytes.Length];
                Buffer.BlockCopy(header, 0, authenticatedPayload, 0, header.Length);
                Buffer.BlockCopy(
                    secretBytes,
                    0,
                    authenticatedPayload,
                    header.Length,
                    secretBytes.Length);
                var expectedMac = HMACSHA256.HashData(
                    authenticationKey,
                    authenticatedPayload);
                var validMac = CryptographicOperations.FixedTimeEquals(
                    expectedMac,
                    suppliedMac);
                CryptographicOperations.ZeroMemory(expectedMac);
                CryptographicOperations.ZeroMemory(suppliedMac);
                CryptographicOperations.ZeroMemory(authenticatedPayload);
                if (!validMac)
                {
                    CryptographicOperations.ZeroMemory(secretBytes);
                    throw new InvalidDataException();
                }

                string secret;
                try
                {
                    secret = StrictUtf8.GetString(secretBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secretBytes);
                }
                ValidateSecret(header[5], secret);
                lock (gate)
                {
                    registrationCount++;
                    totalSecretBytes += secretLength;
                    if (registrationCount > MaximumRegistrations ||
                        totalSecretBytes > MaximumTotalSecretBytes)
                    {
                        throw new InvalidDataException();
                    }

                    var key = header[5].ToString() + "\0" + secret;
                    if (!records.ContainsKey(key))
                    {
                        records.Add(
                            key,
                            new DynamicSecretRecord(header[5], secret));
                    }
                }

                var acknowledgement = new byte[]
                {
                    (byte)'N', (byte)'G', (byte)'D', (byte)'A', 1, 0
                };
                await pipe.WriteAsync(
                    acknowledgement.AsMemory(),
                    timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                var trailingByte = new byte[1];
                var trailingCount = await pipe.ReadAsync(
                    trailingByte.AsMemory(),
                    timeout.Token).ConfigureAwait(false);
                if (trailingCount != 0)
                {
                    throw new InvalidDataException();
                }
            }
        }

        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += count;
            }
        }

        private static void ValidateSecret(byte kind, string secret)
        {
            if (kind == 1)
            {
                if (!AccessTokenPattern.IsMatch(secret))
                {
                    throw new InvalidDataException();
                }

                return;
            }

            if (!Regex.IsMatch(
                    secret,
                    @"^[A-Za-z0-9_-]{43}$",
                    RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException();
            }

            var padded = secret.Replace('-', '+').Replace('_', '/') + "=";
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                throw new InvalidDataException();
            }

            try
            {
                if (decoded.Length != 32)
                {
                    throw new InvalidDataException();
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }

        private void SetFault(string rule)
        {
            lock (gate)
            {
                if (faultRule == null)
                {
                    faultRule = rule;
                }
            }
        }

        private void ThrowIfFaulted()
        {
            lock (gate)
            {
                if (faultRule != null)
                {
                    throw new InvalidOperationException(faultRule);
                }
            }
        }
    }
}
'@ -Language CSharp
}

function New-AdminManagementDynamicSecretCollector {
    Initialize-AdminManagementDynamicSecretCollectorType
    $pipeName = "novagame-dynamic-secret-$([Guid]::NewGuid().ToString('N'))"
    $authenticationKey = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $encodedAuthenticationKey = [Convert]::ToBase64String($authenticationKey).
        TrimEnd('=').
        Replace('+', '-').
        Replace('/', '_')
    $collector = $null
    try {
        $collector = [NovaGame.Verification.DynamicSecretPipeCollector]::Start(
            $pipeName,
            $authenticationKey)
        $collector.WaitUntilReady([TimeSpan]::FromSeconds(5))
        return [pscustomobject]@{
            Collector = $collector
            EncodedAuthenticationKey = $encodedAuthenticationKey
            PipeName = $pipeName
        }
    }
    catch {
        if ($null -ne $collector) {
            $collector.Dispose()
        }
        throw "dynamic-secret-registry-start-failed"
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory(
            $authenticationKey)
    }
}

function Send-AdminManagementDynamicSecretProbe {
    param(
        [Parameter(Mandatory)]$Channel,
        [Parameter(Mandatory)][ValidateSet(1, 2)][int]$Kind,
        [Parameter(Mandatory)][string]$Value,
        [switch]$InvalidAuthenticationCode
    )

    $authenticationKey = [Convert]::FromBase64String(
        ([string]$Channel.EncodedAuthenticationKey).
            Replace('-', '+').
            Replace('_', '/') + '=')
    $secretBytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    $header = [byte[]]::new(10)
    [Text.Encoding]::ASCII.GetBytes("NGDS").CopyTo($header, 0)
    $header[4] = 1
    $header[5] = [byte]$Kind
    $header[6] = [byte](($secretBytes.Length -shr 24) -band 0xff)
    $header[7] = [byte](($secretBytes.Length -shr 16) -band 0xff)
    $header[8] = [byte](($secretBytes.Length -shr 8) -band 0xff)
    $header[9] = [byte]($secretBytes.Length -band 0xff)
    $authenticatedPayload = [byte[]]::new(
        $header.Length + $secretBytes.Length)
    $header.CopyTo($authenticatedPayload, 0)
    $secretBytes.CopyTo($authenticatedPayload, $header.Length)
    $authenticationCode =
        [Security.Cryptography.HMACSHA256]::HashData(
            $authenticationKey,
            $authenticatedPayload)
    if ($InvalidAuthenticationCode) {
        $authenticationCode[0] = $authenticationCode[0] -bxor 0xff
    }
    $frame = [byte[]]::new(
        $authenticatedPayload.Length + $authenticationCode.Length)
    $authenticatedPayload.CopyTo($frame, 0)
    $authenticationCode.CopyTo($frame, $authenticatedPayload.Length)
    $client = $null
    try {
        $client = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            [string]$Channel.PipeName,
            [System.IO.Pipes.PipeDirection]::InOut,
            [System.IO.Pipes.PipeOptions]::None)
        $client.Connect(5000)
        $client.Write($frame, 0, $frame.Length)
        $client.Flush()
        $acknowledgement = [byte[]]::new(6)
        $offset = 0
        while ($offset -lt $acknowledgement.Length) {
            $count = $client.Read(
                $acknowledgement,
                $offset,
                $acknowledgement.Length - $offset)
            if ($count -eq 0) {
                break
            }
            $offset += $count
        }
        return $offset -eq 6 -and
            [Text.Encoding]::ASCII.GetString(
                $acknowledgement,
                0,
                4) -ceq "NGDA" -and
            $acknowledgement[4] -eq 1 -and
            $acknowledgement[5] -eq 0
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $client) {
            $client.Dispose()
        }
        foreach ($buffer in @(
                $authenticationKey, $secretBytes, $authenticatedPayload,
                $authenticationCode, $frame)) {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory(
                $buffer)
        }
    }
}

function Test-AdminManagementDynamicSecretClientPolicy {
    param(
        [Parameter(Mandatory)]$Toolchain
    )

    $sourcePath = Join-Path `
        $repositoryRoot `
        "admin\apps\web-ele\e2e\acceptance-evidence.ts"
    $probeModulePath = Join-Path `
        $repositoryRoot `
        "admin\apps\web-ele\e2e\.novagame-dynamic-secret-client-probe.mjs"
    $probeStandardOutputPath = Join-Path `
        $repositoryRoot `
        "admin\apps\web-ele\e2e\.novagame-dynamic-secret-client-probe.stdout.log"
    $probeStandardErrorPath = Join-Path `
        $repositoryRoot `
        "admin\apps\web-ele\e2e\.novagame-dynamic-secret-client-probe.stderr.log"
    Assert-Condition `
        (Test-Path -LiteralPath $sourcePath -PathType Leaf) `
        "The production dynamic-secret client source is missing."
    foreach ($probePath in @(
            $probeModulePath,
            $probeStandardOutputPath,
            $probeStandardErrorPath
        )) {
        Assert-Condition `
            (-not (Test-Path -LiteralPath $probePath)) `
            "A dynamic-secret client probe path already exists."
    }

    $channel = $null
    $jobHandle = [IntPtr]::Zero
    $probeProcess = $null
    $channelStopped = $false
    $operationError = $null
    $cleanupErrors = [Collections.ArrayList]::new()
    try {
        $channel = New-AdminManagementDynamicSecretCollector
        $missingPipeName =
            "novagame-dynamic-secret-missing-$([Guid]::NewGuid().ToString('N'))"
        $probeSource = @'
import { Buffer } from 'node:buffer';
import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import process from 'node:process';
import ts from 'typescript';

let failed = false;
try {
  const sourcePath = process.env.NOVAGAME_PROBE_SOURCE;
  const modulePath = process.env.NOVAGAME_PROBE_MODULE;
  if (!sourcePath || !modulePath) {
    throw new Error('configuration');
  }
  const source =
    readFileSync(sourcePath, 'utf8') +
    '\nexport { registerDynamicSecret as runDynamicSecretTransportProbe };\n';
  const transpiled = ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2022,
    },
    fileName: sourcePath,
    reportDiagnostics: true,
  });
  if (
    transpiled.diagnostics?.some(
      (diagnostic) => diagnostic.category === ts.DiagnosticCategory.Error,
    )
  ) {
    throw new Error('transpile');
  }
  writeFileSync(modulePath, transpiled.outputText, 'utf8');
  const production = await import(
    `${pathToFileURL(modulePath).href}?probe=${Date.now()}`
  );
  const registrations = Array.from({ length: 32 }, (_, index) => {
    if (index % 2 === 0) {
      return production.runDynamicSecretTransportProbe(
        'access',
        `eyJ${index.toString(36)}.cGF5bG9hZA.c2lnbmF0dXJl`,
      );
    }
    return production.runDynamicSecretTransportProbe(
      'refresh',
      Buffer.alloc(32, index + 1).toString('base64url'),
    );
  });
  const registrationResults = await Promise.allSettled(registrations);
  if (
    registrationResults.some((result) => result.status !== 'fulfilled')
  ) {
    throw new Error('concurrency');
  }

  process.env.NOVAGAME_E2E_DYNAMIC_SECRET_PIPE =
    process.env.NOVAGAME_PROBE_MISSING_PIPE;
  const missingStartedAt = Date.now();
  let boundedFailure = false;
  try {
    await production.runDynamicSecretTransportProbe(
      'access',
      'eyJmissing.cGF5bG9hZA.c2lnbmF0dXJl',
    );
  } catch (error) {
    const elapsed = Date.now() - missingStartedAt;
    const ruleId =
      error && typeof error === 'object' && 'ruleId' in error
        ? error.ruleId
        : undefined;
    boundedFailure =
      ruleId === 'dynamic-secret-pipe-timeout' &&
      elapsed >= 4500 &&
      elapsed <= 8000;
  }
  if (!boundedFailure) {
    throw new Error('deadline');
  }
} catch {
  failed = true;
}
if (failed) {
  process.exitCode = 1;
}
'@

        $nodeArguments = @(
            "--no-warnings",
            "--input-type=module",
            "--eval",
            $probeSource)
        $commandPayload = [ordered]@{
            FilePath = [string]$Toolchain.nodePath
            Arguments = $nodeArguments
        } | ConvertTo-Json -Compress
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        $startInfo.WorkingDirectory = [string]$Toolchain.adminRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        foreach ($argument in @(
                "-NoProfile",
                "-Command",
                '$payload = $env:NOVAGAME_PROBE_COMMAND_PAYLOAD | ConvertFrom-Json -AsHashtable; ' +
                '$stdoutPath = $env:NOVAGAME_PROBE_STDOUT_PATH; ' +
                '$stderrPath = $env:NOVAGAME_PROBE_STDERR_PATH; ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_PROBE_COMMAND_PAYLOAD", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_PROBE_STDOUT_PATH", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_PROBE_STDERR_PATH", $null, "Process"); ' +
                '& ([string]$payload.FilePath) @([string[]]$payload.Arguments) ' +
                '1> $stdoutPath 2> $stderrPath; exit $LASTEXITCODE'
            )) {
            $null = $startInfo.ArgumentList.Add([string]$argument)
        }
        $startInfo.Environment["NOVAGAME_E2E_DYNAMIC_SECRET_PIPE"] =
            [string]$channel.PipeName
        $startInfo.Environment["NOVAGAME_E2E_DYNAMIC_SECRET_PIPE_KEY"] =
            [string]$channel.EncodedAuthenticationKey
        $startInfo.Environment["NOVAGAME_PROBE_SOURCE"] = $sourcePath
        $startInfo.Environment["NOVAGAME_PROBE_MODULE"] = $probeModulePath
        $startInfo.Environment["NOVAGAME_PROBE_MISSING_PIPE"] =
            $missingPipeName
        $startInfo.Environment["NOVAGAME_PROBE_COMMAND_PAYLOAD"] =
            $commandPayload
        $startInfo.Environment["NOVAGAME_PROBE_STDOUT_PATH"] =
            $probeStandardOutputPath
        $startInfo.Environment["NOVAGAME_PROBE_STDERR_PATH"] =
            $probeStandardErrorPath

        $jobHandle = New-NovaGameProcessJob
        $probeProcess = Start-NovaGameJobProcess `
            -JobHandle $jobHandle `
            -TargetStartInfo $startInfo
        Assert-Condition `
            $probeProcess.WaitForExit(15000) `
            "The production dynamic-secret client probe exceeded its deadline."
        $probeProcess.WaitForExit()
        $probeProcess.Refresh()
        Assert-Condition `
            ((Test-Path -LiteralPath $probeStandardOutputPath -PathType Leaf) -and
                (Test-Path -LiteralPath $probeStandardErrorPath -PathType Leaf)) `
            "The production dynamic-secret client probe output files are missing."
        $standardOutput = [IO.File]::ReadAllText($probeStandardOutputPath)
        $standardError = [IO.File]::ReadAllText($probeStandardErrorPath)
        Assert-Condition `
            ([string]::IsNullOrEmpty($standardOutput) -and
                [string]::IsNullOrEmpty($standardError)) `
            "The production dynamic-secret client probe emitted output."
        Assert-Condition `
            ($probeProcess.ExitCode -eq 0) `
            "The production dynamic-secret client probe failed."

        $records = @(
            $channel.Collector.StopAndSnapshot(
                [TimeSpan]::FromSeconds(10)))
        $channelStopped = $true
        Assert-Condition `
            ($records.Count -eq 32) `
            "The production dynamic-secret client concurrency probe was incomplete."
        $recordKinds = @(
            $records |
                ForEach-Object { [string][int]$_.Kind } |
                Sort-Object -Unique)
        Assert-StringSet `
            -Expected @("1", "2") `
            -Actual $recordKinds `
            -Message "The production dynamic-secret client probe did not register both token kinds."
    }
    catch {
        $operationError = $_.Exception.Message
    }
    finally {
        if ($jobHandle -ne [IntPtr]::Zero) {
            try {
                Close-NovaGameProcessJob -JobHandle $jobHandle
                $jobHandle = [IntPtr]::Zero
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to close the dynamic-secret client probe job.")
            }
        }
        if ($null -ne $probeProcess) {
            try {
                $probeProcess.Dispose()
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to dispose the dynamic-secret client probe process.")
            }
        }
        if ($null -ne $channel) {
            if (-not $channelStopped) {
                try {
                    $null = $channel.Collector.StopAndSnapshot(
                        [TimeSpan]::FromSeconds(10))
                    $channelStopped = $true
                }
                catch {
                    $null = $cleanupErrors.Add(
                        "Failed to stop the dynamic-secret client probe collector.")
                }
            }
            try {
                $channel.Collector.Dispose()
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to dispose the dynamic-secret client probe collector.")
            }
        }
        foreach ($probePath in @(
                $probeModulePath,
                $probeStandardOutputPath,
                $probeStandardErrorPath
            )) {
            try {
                Remove-Item `
                    -LiteralPath $probePath `
                    -Force `
                    -ErrorAction SilentlyContinue
                if (Test-Path -LiteralPath $probePath) {
                    throw "probe residue"
                }
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to remove a dynamic-secret client probe path.")
            }
        }
    }

    if ($null -ne $operationError) {
        if ($cleanupErrors.Count -gt 0) {
            throw "$operationError Cleanup errors: $($cleanupErrors -join ' ')"
        }
        throw $operationError
    }
    if ($cleanupErrors.Count -gt 0) {
        throw "Dynamic-secret client probe cleanup failed: $($cleanupErrors -join ' ')"
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

function Wait-HttpsEndpoint {
    param(
        [Parameter(Mandatory)][Uri]$Uri,
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$StandardOutputPath,
        [Parameter(Mandatory)][string]$StandardErrorPath,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "HTTPS process exited with code $($Process.ExitCode). Logs: '$StandardOutputPath', '$StandardErrorPath'."
        }

        try {
            $response = Invoke-WebRequest `
                -Uri $Uri `
                -Method Get `
                -SkipCertificateCheck `
                -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "HTTPS endpoint did not become reachable at '$Uri' within $TimeoutSeconds seconds. Logs: '$StandardOutputPath', '$StandardErrorPath'."
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

function Initialize-NovaGameProcessJobType {
    if ("NovaGame.Verification.NativeJob" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NovaGame.Verification
{
    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public static class NativeJob
    {
        public const int JobObjectBasicProcessIdList = 3;
        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JobObjectLimitKillOnJobClose = 0x00002000;
        public const uint CreateSuspended = 0x00000004;
        public const uint CreateNoWindow = 0x08000000;
        public const uint CreateUnicodeEnvironment = 0x00000400;
        public const uint WaitObject0 = 0x00000000;
        public const uint WaitTimeout = 0x00000102;
        public const uint WaitFailed = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo
        {
            public int Size;
            public string Reserved;
            public string Desktop;
            public string Title;
            public int X;
            public int Y;
            public int XSize;
            public int YSize;
            public int XCountChars;
            public int YCountChars;
            public int FillAttribute;
            public int Flags;
            public short ShowWindow;
            public short Reserved2Count;
            public IntPtr Reserved2;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public int ProcessId;
            public int ThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            ref ExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength,
            out uint returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string applicationName,
            string commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(
            IntPtr handle,
            uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
"@
}

function New-NovaGameProcessJob {
    param([switch]$InjectConfigurationFailure)

    Initialize-NovaGameProcessJobType
    $handle = [NovaGame.Verification.NativeJob]::CreateJobObject(
        [IntPtr]::Zero,
        $null)
    if ($handle -eq [IntPtr]::Zero) {
        throw [System.ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            "Unable to create the Admin management process job.")
    }

    try {
        $limits = [NovaGame.Verification.ExtendedLimitInformation]::new()
        $basicLimits = [NovaGame.Verification.BasicLimitInformation]::new()
        $basicLimits.LimitFlags =
            [NovaGame.Verification.NativeJob]::JobObjectLimitKillOnJobClose
        $limits.BasicLimitInformation = $basicLimits
        $limitSize = [Runtime.InteropServices.Marshal]::SizeOf($limits)
        if (-not [NovaGame.Verification.NativeJob]::SetInformationJobObject(
                $handle,
                [NovaGame.Verification.NativeJob]::JobObjectExtendedLimitInformation,
                [ref]$limits,
                [uint32]$limitSize)) {
            throw [System.ComponentModel.Win32Exception]::new(
                [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                "Unable to configure the Admin management process job.")
        }
        if ($InjectConfigurationFailure) {
            throw [InvalidOperationException]::new(
                "Injected Admin management process-job configuration failure.")
        }
    }
    catch {
        $configurationError = $_.Exception
        try {
            Close-NovaGameProcessJob -JobHandle $handle
        }
        catch {
            throw "$($configurationError.Message) Failed to release the unconfigured Admin management process job: $($_.Exception.Message)"
        }
        throw $configurationError
    }

    return $handle
}

function Close-NovaGameNativeHandleVerified {
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [Parameter(Mandatory)]
        [ValidateSet("process", "thread")]
        [string]$Kind,
        [switch]$InjectFailure
    )

    if ($Handle -eq [IntPtr]::Zero) {
        return
    }
    if (-not [NovaGame.Verification.NativeJob]::CloseHandle($Handle)) {
        throw [System.ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            "Unable to close the native Admin management $Kind handle.")
    }
    if ($InjectFailure) {
        throw [InvalidOperationException]::new(
            "Injected native Admin management $Kind-handle close failure.")
    }
}

function Start-NovaGameJobProcess {
    param(
        [Parameter(Mandatory)][IntPtr]$JobHandle,
        [Parameter(Mandatory)]
        [System.Diagnostics.ProcessStartInfo]$TargetStartInfo,
        [switch]$InjectThreadHandleCloseFailure,
        [switch]$InjectProcessHandleCloseFailure
    )

    Assert-Condition `
        (-not $TargetStartInfo.RedirectStandardOutput -and
            -not $TargetStartInfo.RedirectStandardError) `
        "Job-owned process output must be redirected by the target command."

    function ConvertTo-WindowsCommandLineArgument {
        param([AllowEmptyString()][string]$Value)

        if ($Value -notmatch '[\s"]') {
            return $Value
        }
        $builder = [System.Text.StringBuilder]::new()
        $null = $builder.Append('"')
        $backslashCount = 0
        foreach ($character in $Value.ToCharArray()) {
            if ($character -eq '\') {
                $backslashCount++
                continue
            }
            if ($character -eq '"') {
                $null = $builder.Append(('\' * (($backslashCount * 2) + 1)))
                $null = $builder.Append('"')
                $backslashCount = 0
                continue
            }
            if ($backslashCount -gt 0) {
                $null = $builder.Append(('\' * $backslashCount))
                $backslashCount = 0
            }
            $null = $builder.Append($character)
        }
        if ($backslashCount -gt 0) {
            $null = $builder.Append(('\' * ($backslashCount * 2)))
        }
        $null = $builder.Append('"')
        return $builder.ToString()
    }

    $applicationPath = [System.IO.Path]::GetFullPath(
        [string]$TargetStartInfo.FileName)
    $commandLine = @(
        (ConvertTo-WindowsCommandLineArgument -Value $applicationPath)
        $TargetStartInfo.ArgumentList |
            ForEach-Object {
                ConvertTo-WindowsCommandLineArgument -Value ([string]$_)
            }
    ) -join ' '
    $environmentBlockBuilder = [System.Text.StringBuilder]::new()
    foreach ($environmentEntry in @(
            $TargetStartInfo.Environment.GetEnumerator() |
                Sort-Object Key)) {
        $null = $environmentBlockBuilder.Append(
            "$($environmentEntry.Key)=$($environmentEntry.Value)")
        $null = $environmentBlockBuilder.Append([char]0)
    }
    $null = $environmentBlockBuilder.Append([char]0)
    $environmentPointer = [Runtime.InteropServices.Marshal]::StringToHGlobalUni(
        $environmentBlockBuilder.ToString())
    $startupInfo = [NovaGame.Verification.NativeJob+StartupInfo]::new()
    $startupInfo.Size = [Runtime.InteropServices.Marshal]::SizeOf($startupInfo)
    $processInformation = [NovaGame.Verification.NativeJob+ProcessInformation]::new()
    $process = $null
    $launchError = $null
    $terminationError = $null
    $launchCleanupErrors = [System.Collections.Generic.List[string]]::new()
    try {
        $creationFlags =
            [NovaGame.Verification.NativeJob]::CreateSuspended -bor
            [NovaGame.Verification.NativeJob]::CreateNoWindow -bor
            [NovaGame.Verification.NativeJob]::CreateUnicodeEnvironment
        if (-not [NovaGame.Verification.NativeJob]::CreateProcess(
                $applicationPath,
                $commandLine,
                [IntPtr]::Zero,
                [IntPtr]::Zero,
                $false,
                $creationFlags,
                $environmentPointer,
                [string]$TargetStartInfo.WorkingDirectory,
                [ref]$startupInfo,
                [ref]$processInformation)) {
            throw [System.ComponentModel.Win32Exception]::new(
                [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                "Unable to create a suspended Admin management process.")
        }
        if (-not [NovaGame.Verification.NativeJob]::AssignProcessToJobObject(
                $JobHandle,
                $processInformation.Process)) {
            throw [System.ComponentModel.Win32Exception]::new(
                [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                "Unable to assign suspended process '$($processInformation.ProcessId)' to the Admin management process job.")
        }
        $process = [System.Diagnostics.Process]::GetProcessById(
            $processInformation.ProcessId)
        # Cache a queryable handle before a fast child can exit.
        $null = $process.SafeHandle
        if ([NovaGame.Verification.NativeJob]::ResumeThread(
                $processInformation.Thread) -eq [uint32]::MaxValue) {
            throw [System.ComponentModel.Win32Exception]::new(
                [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                "Unable to resume Admin management process '$($processInformation.ProcessId)'.")
        }
    }
    catch {
        $launchError = $_
        try {
            if ($processInformation.Process -ne [IntPtr]::Zero) {
                $waitResult = [NovaGame.Verification.NativeJob]::WaitForSingleObject(
                    $processInformation.Process,
                    0)
                if ($waitResult -ne
                        [NovaGame.Verification.NativeJob]::WaitObject0) {
                    if ($waitResult -eq
                            [NovaGame.Verification.NativeJob]::WaitFailed) {
                        throw [System.ComponentModel.Win32Exception]::new(
                            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                            "Unable to inspect the failed suspended Admin management process.")
                    }
                    if (-not [NovaGame.Verification.NativeJob]::TerminateProcess(
                            $processInformation.Process,
                            1)) {
                        throw [System.ComponentModel.Win32Exception]::new(
                            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                            "Unable to terminate the failed suspended Admin management process.")
                    }
                    $waitResult =
                        [NovaGame.Verification.NativeJob]::WaitForSingleObject(
                            $processInformation.Process,
                            10000)
                    if ($waitResult -ne
                            [NovaGame.Verification.NativeJob]::WaitObject0) {
                        if ($waitResult -eq
                                [NovaGame.Verification.NativeJob]::WaitFailed) {
                            throw [System.ComponentModel.Win32Exception]::new(
                                [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                                "Unable to inspect termination of the failed suspended Admin management process.")
                        }
                        throw "The failed suspended Admin management process did not terminate within 10 seconds."
                    }
                }
            }
        }
        catch {
            $terminationError = $_
        }
        finally {
            if ($null -ne $process) {
                try {
                    $process.Dispose()
                }
                catch {
                    $null = $launchCleanupErrors.Add(
                        "Managed process handle disposal failed: $($_.Exception.Message)")
                }
                $process = $null
            }
        }
    }
    finally {
        [Runtime.InteropServices.Marshal]::FreeHGlobal($environmentPointer)
        if ($processInformation.Thread -ne [IntPtr]::Zero) {
            try {
                Close-NovaGameNativeHandleVerified `
                    -Handle $processInformation.Thread `
                    -Kind "thread" `
                    -InjectFailure:$InjectThreadHandleCloseFailure
            }
            catch {
                $null = $launchCleanupErrors.Add($_.Exception.Message)
            }
        }
        if ($processInformation.Process -ne [IntPtr]::Zero) {
            try {
                Close-NovaGameNativeHandleVerified `
                    -Handle $processInformation.Process `
                    -Kind "process" `
                    -InjectFailure:$InjectProcessHandleCloseFailure
            }
            catch {
                $null = $launchCleanupErrors.Add($_.Exception.Message)
            }
        }
    }

    if ($launchCleanupErrors.Count -gt 0) {
        if ($null -ne $process) {
            try {
                $process.Dispose()
            }
            catch {
                $null = $launchCleanupErrors.Add(
                    "Managed process handle disposal failed: $($_.Exception.Message)")
            }
            $process = $null
        }
        $launchContext = if ($null -ne $launchError) {
            "Launch error: $($launchError.Exception.Message)"
        }
        else {
            "Launch reached native-handle cleanup."
        }
        $terminationContext = if ($null -ne $terminationError) {
            " Native termination error: $($terminationError.Exception.Message)"
        }
        else {
            ""
        }
        throw "$launchContext$terminationContext Native handle cleanup errors: $($launchCleanupErrors -join ' ')"
    }
    if ($null -ne $terminationError) {
        throw "Admin management process launch failed: $($launchError.Exception.Message) Native termination could not be verified: $($terminationError.Exception.Message)"
    }
    if ($null -ne $launchError) {
        throw $launchError
    }

    return $process
}

function Get-NovaGameJobProcessIds {
    param([Parameter(Mandatory)][IntPtr]$JobHandle)

    $capacity = 4096
    $bufferLength = 8 + ([IntPtr]::Size * $capacity)
    $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal($bufferLength)
    try {
        $returnLength = [uint32]0
        if (-not [NovaGame.Verification.NativeJob]::QueryInformationJobObject(
                $JobHandle,
                [NovaGame.Verification.NativeJob]::JobObjectBasicProcessIdList,
                $buffer,
                [uint32]$bufferLength,
                [ref]$returnLength)) {
            throw [System.ComponentModel.Win32Exception]::new(
                [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
                "Unable to query the Admin management process job.")
        }
        $assignedProcessCount = [Runtime.InteropServices.Marshal]::ReadInt32(
            $buffer,
            0)
        $listedProcessCount = [Runtime.InteropServices.Marshal]::ReadInt32(
            $buffer,
            4)
        Assert-Condition `
            ($assignedProcessCount -eq $listedProcessCount -and
                $listedProcessCount -le $capacity) `
            "Admin management process job exceeded its bounded identity snapshot."
        return @(
            for ($index = 0; $index -lt $listedProcessCount; $index++) {
                $offset = 8 + ([IntPtr]::Size * $index)
                if ([IntPtr]::Size -eq 8) {
                    [int][Runtime.InteropServices.Marshal]::ReadInt64(
                        $buffer,
                        $offset)
                }
                else {
                    [Runtime.InteropServices.Marshal]::ReadInt32(
                        $buffer,
                        $offset)
                }
            })
    }
    finally {
        [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer)
    }
}

function Get-NovaGameJobProcessIdentities {
    param([Parameter(Mandatory)][IntPtr]$JobHandle)

    $processIds = @(Get-NovaGameJobProcessIds -JobHandle $JobHandle)
    $processRows = @(
        Get-CimInstance Win32_Process -ErrorAction Stop |
            Where-Object { [int]$_.ProcessId -in $processIds })
    Assert-Condition `
        ($processRows.Count -eq $processIds.Count) `
        "Admin management process job identity snapshot was incomplete."
    return @(
        foreach ($processId in $processIds) {
            $processRow = $processRows |
                Where-Object { [int]$_.ProcessId -eq $processId } |
                Select-Object -First 1
            [pscustomobject]@{
                Id = [int]$processRow.ProcessId
                CreationDate = ([DateTime]$processRow.CreationDate).ToUniversalTime()
            }
        })
}

function Close-NovaGameProcessJob {
    param(
        [Parameter(Mandatory)][IntPtr]$JobHandle,
        [switch]$InjectPrimaryCloseFailure
    )

    $primaryCloseError = $null
    if (-not $InjectPrimaryCloseFailure) {
        if ([NovaGame.Verification.NativeJob]::CloseHandle($JobHandle)) {
            return
        }
        $primaryCloseError = [System.ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            "Unable to close the Admin management process job.")
    }
    else {
        $primaryCloseError = [InvalidOperationException]::new(
            "Injected Admin management process-job close failure.")
    }

    if (-not [NovaGame.Verification.NativeJob]::TerminateJobObject(
            $JobHandle,
            1)) {
        $terminationError = [System.ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            "Unable to terminate the Admin management process job after close failure.")
        throw "$($primaryCloseError.Message) $($terminationError.Message)"
    }

    $terminationDeadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $remainingProcessIds = @(
            Get-NovaGameJobProcessIds -JobHandle $JobHandle)
        if ($remainingProcessIds.Count -gt 0 -and
            [DateTime]::UtcNow -lt $terminationDeadline) {
            Start-Sleep -Milliseconds 100
        }
    } while ($remainingProcessIds.Count -gt 0 -and
        [DateTime]::UtcNow -lt $terminationDeadline)
    Assert-Condition `
        ($remainingProcessIds.Count -eq 0) `
        "Admin management process-job fallback did not reach zero owned processes."

    if (-not [NovaGame.Verification.NativeJob]::CloseHandle($JobHandle)) {
        $fallbackCloseError = [System.ComponentModel.Win32Exception]::new(
            [Runtime.InteropServices.Marshal]::GetLastWin32Error(),
            "Unable to close the terminated Admin management process job.")
        throw "$($primaryCloseError.Message) $($fallbackCloseError.Message)"
    }
}

function Test-NovaGameProcessJobLifecycle {
    $jobHandle = [IntPtr]::Zero
    $probeProcess = $null
    $probeIdentities = @()
    $fallbackJobHandle = [IntPtr]::Zero
    $fallbackProbeProcess = $null
    try {
        $configurationFailureRejected = $false
        try {
            $null = New-NovaGameProcessJob -InjectConfigurationFailure
        }
        catch {
            $configurationFailureRejected = $_.Exception.Message.Equals(
                "Injected Admin management process-job configuration failure.",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $configurationFailureRejected `
            "Admin management process-job configuration failure did not complete verified handle closure."

        $jobHandle = New-NovaGameProcessJob
        $probeStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $probeStartInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        $probeStartInfo.WorkingDirectory = $repositoryRoot
        $probeStartInfo.UseShellExecute = $false
        $probeStartInfo.CreateNoWindow = $true
        $null = $probeStartInfo.ArgumentList.Add("-NoProfile")
        $null = $probeStartInfo.ArgumentList.Add("-Command")
        $null = $probeStartInfo.ArgumentList.Add(
            '$child = Start-Process -FilePath pwsh -ArgumentList @(' +
            "'-NoProfile', '-Command', 'Start-Sleep -Seconds 30') " +
            '-WindowStyle Hidden -PassThru; ' +
            'while ($null -eq (Get-Process -Id $child.Id ' +
            '-ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 10 }; ' +
            'exit 7')
        $probeProcess = Start-NovaGameJobProcess `
            -JobHandle $jobHandle `
            -TargetStartInfo $probeStartInfo
        Assert-Condition `
            $probeProcess.WaitForExit(10000) `
            "Admin management process-job launcher did not exit."
        $probeProcess.Refresh()
        Assert-Condition `
            ($probeProcess.ExitCode -eq 7) `
            "Admin management process-job launcher did not preserve its exit code."
        $probeIdentities = @(
            Get-NovaGameJobProcessIdentities -JobHandle $jobHandle)
        Assert-Condition `
            ($probeIdentities.Count -gt 0 -and
                $probeIdentities.Id -notcontains $probeProcess.Id) `
            "Admin management process-job descendant did not remain owned after its launcher exited."
        Close-NovaGameProcessJob -JobHandle $jobHandle
        $jobHandle = [IntPtr]::Zero
        $probeExitDeadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $remainingProbeIdentities = @(
                foreach ($probeIdentity in $probeIdentities) {
                    $probeRow = Get-CimInstance `
                        Win32_Process `
                        -Filter "ProcessId = $([int]$probeIdentity.Id)" `
                        -ErrorAction SilentlyContinue
                    if ($null -ne $probeRow -and
                        ([DateTime]$probeRow.CreationDate).ToUniversalTime() -eq
                            [DateTime]$probeIdentity.CreationDate) {
                        $probeIdentity
                    }
                })
            if ($remainingProbeIdentities.Count -gt 0 -and
                [DateTime]::UtcNow -lt $probeExitDeadline) {
                Start-Sleep -Milliseconds 100
            }
        } while ($remainingProbeIdentities.Count -gt 0 -and
            [DateTime]::UtcNow -lt $probeExitDeadline)
        Assert-Condition `
            ($remainingProbeIdentities.Count -eq 0) `
            "Admin management descendant process identity survived Job Object closure."

        $fallbackJobHandle = New-NovaGameProcessJob
        $fallbackStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $fallbackStartInfo.FileName =
            (Get-Command "pwsh" -ErrorAction Stop).Source
        $fallbackStartInfo.WorkingDirectory = $repositoryRoot
        $fallbackStartInfo.UseShellExecute = $false
        $fallbackStartInfo.CreateNoWindow = $true
        $null = $fallbackStartInfo.ArgumentList.Add("-NoProfile")
        $null = $fallbackStartInfo.ArgumentList.Add("-Command")
        $null = $fallbackStartInfo.ArgumentList.Add("Start-Sleep -Seconds 30")
        $fallbackProbeProcess = Start-NovaGameJobProcess `
            -JobHandle $fallbackJobHandle `
            -TargetStartInfo $fallbackStartInfo
        Assert-Condition `
            (@(Get-NovaGameJobProcessIds `
                -JobHandle $fallbackJobHandle).Count -eq 1) `
            "The process-job fallback probe did not own its live child."
        Close-NovaGameProcessJob `
            -JobHandle $fallbackJobHandle `
            -InjectPrimaryCloseFailure
        $fallbackJobHandle = [IntPtr]::Zero
        Assert-Condition `
            $fallbackProbeProcess.WaitForExit(10000) `
            "The process-job fallback did not terminate its live child."

        $assignmentFailureMarker =
            "novagame-job-assignment-$([Guid]::NewGuid().ToString('N'))"
        $assignmentFailureStartInfo =
            [System.Diagnostics.ProcessStartInfo]::new()
        $assignmentFailureStartInfo.FileName =
            (Get-Command "pwsh" -ErrorAction Stop).Source
        $assignmentFailureStartInfo.WorkingDirectory = $repositoryRoot
        $assignmentFailureStartInfo.UseShellExecute = $false
        $assignmentFailureStartInfo.CreateNoWindow = $true
        $null = $assignmentFailureStartInfo.ArgumentList.Add("-NoProfile")
        $null = $assignmentFailureStartInfo.ArgumentList.Add("-Command")
        $null = $assignmentFailureStartInfo.ArgumentList.Add(
            "Start-Sleep -Seconds 30 # $assignmentFailureMarker")
        $assignmentFailureRejected = $false
        try {
            $null = Start-NovaGameJobProcess `
                -JobHandle ([IntPtr](-1)) `
                -TargetStartInfo $assignmentFailureStartInfo `
                -InjectThreadHandleCloseFailure `
                -InjectProcessHandleCloseFailure
        }
        catch {
            $assignmentFailureMessage = $_.Exception.Message
            $assignmentFailureRejected =
                $assignmentFailureMessage.Contains(
                    "Unable to assign suspended process",
                    [StringComparison]::Ordinal) -and
                $assignmentFailureMessage.Contains(
                    "Injected native Admin management thread-handle close failure.",
                    [StringComparison]::Ordinal) -and
                $assignmentFailureMessage.Contains(
                    "Injected native Admin management process-handle close failure.",
                    [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $assignmentFailureRejected `
            "Admin management process-job launcher lost launch or native-handle cleanup context."
        $assignmentFailureDeadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $orphanedAssignmentProcesses = @(
                Get-CimInstance Win32_Process -ErrorAction Stop |
                    Where-Object {
                        ([string]$_.CommandLine).Contains(
                            $assignmentFailureMarker,
                            [StringComparison]::Ordinal)
                    })
            if ($orphanedAssignmentProcesses.Count -gt 0 -and
                [DateTime]::UtcNow -lt $assignmentFailureDeadline) {
                Start-Sleep -Milliseconds 100
            }
        } while ($orphanedAssignmentProcesses.Count -gt 0 -and
            [DateTime]::UtcNow -lt $assignmentFailureDeadline)
        Assert-Condition `
            ($orphanedAssignmentProcesses.Count -eq 0) `
            "A suspended Admin management process survived Job assignment failure."
    }
    finally {
        if ($jobHandle -ne [IntPtr]::Zero) {
            Close-NovaGameProcessJob -JobHandle $jobHandle
            $jobHandle = [IntPtr]::Zero
        }
        if ($fallbackJobHandle -ne [IntPtr]::Zero) {
            Close-NovaGameProcessJob -JobHandle $fallbackJobHandle
            $fallbackJobHandle = [IntPtr]::Zero
        }
        if ($null -ne $probeProcess) {
            try {
                if (-not $probeProcess.HasExited) {
                    $probeProcess.Kill($true)
                }
            }
            catch {
            }
            $probeProcess.Dispose()
        }
        if ($null -ne $fallbackProbeProcess) {
            try {
                if (-not $fallbackProbeProcess.HasExited) {
                    $fallbackProbeProcess.Kill($true)
                }
            }
            catch {
            }
            $fallbackProbeProcess.Dispose()
        }
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

function Test-AdminEndToEndHostCapacitySnapshot {
    param([object[]]$Snapshot)

    try {
        $records = @($Snapshot)
        if ($records.Count -ne 1 -or $null -eq $records[0]) {
            return $false
        }

        $record = $records[0]
        $integralCapacityValueTypes = @(
            [sbyte],
            [byte],
            [int16],
            [uint16],
            [int32],
            [uint32],
            [int64],
            [uint64]
        )
        $capacityValues = [ordered]@{}
        foreach ($propertyName in @(
                "AvailableMBytes",
                "CommittedBytes",
                "CommitLimit"
            )) {
            $property = $record.PSObject.Properties[$propertyName]
            if ($null -eq $property -or $null -eq $property.Value) {
                return $false
            }

            $propertyValue = $property.Value
            if (-not ($integralCapacityValueTypes -contains $propertyValue.GetType())) {
                return $false
            }

            $capacityValues[$propertyName] = [Convert]::ToInt64(
                $propertyValue,
                [Globalization.CultureInfo]::InvariantCulture)
        }

        $availableMBytes = $capacityValues["AvailableMBytes"]
        $committedBytes = $capacityValues["CommittedBytes"]
        $commitLimit = $capacityValues["CommitLimit"]
        if ($availableMBytes -lt 0 -or
            $committedBytes -lt 0 -or
            $commitLimit -lt 0 -or
            $commitLimit -lt $committedBytes) {
            return $false
        }

        return ($availableMBytes -ge 2048 -and
            ($commitLimit - $committedBytes) -ge 4GB)
    }
    catch {
        return $false
    }
}

function Assert-AdminEndToEndHostCapacity {
    param([object[]]$Snapshot)

    try {
        $effectiveSnapshot = if ($PSBoundParameters.ContainsKey("Snapshot")) {
            @($Snapshot)
        }
        else {
            @(Get-CimInstance `
                    -ClassName Win32_PerfFormattedData_PerfOS_Memory `
                    -ErrorAction Stop)
        }
        Assert-Condition `
            (Test-AdminEndToEndHostCapacitySnapshot -Snapshot $effectiveSnapshot) `
            "admin-e2e-host-capacity-unavailable"
    }
    catch {
        throw "admin-e2e-host-capacity-unavailable"
    }
}

function Test-AdminEndToEndHostCapacityAdmissionPolicy {
    $admittedSnapshot = [pscustomobject]@{
        AvailableMBytes = [uint64]2048
        CommittedBytes = [uint64]4294967296
        CommitLimit = [uint64]8589934592
    }
    Assert-AdminEndToEndHostCapacity -Snapshot @($admittedSnapshot)

    $rejectedCases = @(
        [pscustomobject]@{
            Name = "empty"
            Snapshot = [object[]]@()
        },
        [pscustomobject]@{
            Name = "multiple"
            Snapshot = [object[]]@($admittedSnapshot, $admittedSnapshot)
        },
        [pscustomobject]@{
            Name = "low-physical"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = 2047
                    CommittedBytes = 4GB
                    CommitLimit = 8GB
                })
        },
        [pscustomobject]@{
            Name = "fractional-physical"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = [decimal]2047.5
                    CommittedBytes = 4GB
                    CommitLimit = 8GB
                })
        },
        [pscustomobject]@{
            Name = "low-commit"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = 2048
                    CommittedBytes = 4GB + 1
                    CommitLimit = 8GB
                })
        },
        [pscustomobject]@{
            Name = "fractional-commit"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = 2048
                    CommittedBytes = [decimal]4294967296.5
                    CommitLimit = 8GB
                })
        },
        [pscustomobject]@{
            Name = "integral-double"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = [double]2048
                    CommittedBytes = 4GB
                    CommitLimit = 8GB
                })
        },
        [pscustomobject]@{
            Name = "overflow"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = [uint64]::MaxValue
                    CommittedBytes = 4GB
                    CommitLimit = 8GB
                })
        },
        [pscustomobject]@{
            Name = "invalid-commit-order"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = 2048
                    CommittedBytes = 8GB
                    CommitLimit = 4GB
                })
        },
        [pscustomobject]@{
            Name = "missing-property"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = 2048
                    CommittedBytes = 4GB
                })
        },
        [pscustomobject]@{
            Name = "malformed"
            Snapshot = [object[]]@([pscustomobject]@{
                    AvailableMBytes = "not-a-number"
                    CommittedBytes = 4GB
                    CommitLimit = 8GB
                })
        }
    )
    foreach ($rejectedCase in $rejectedCases) {
        $rejected = $false
        try {
            Assert-AdminEndToEndHostCapacity `
                -Snapshot $rejectedCase.Snapshot
        }
        catch {
            $rejected = $_.Exception.Message.Equals(
                "admin-e2e-host-capacity-unavailable",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $rejected `
            "Admin E2E host capacity policy '$($rejectedCase.Name)' did not fail closed."
    }
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
        "docs/goals/m3.md",
        "docs/goals/m3.5.md",
        "samples/SwordMaster/docs/equipment-attribute-effect.md",
        "samples/SwordMaster/docs/player-equipment-grant.md",
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
        },
        [pscustomobject]@{
            Milestone = "M3"
            Path = "docs/goals/m3.md"
            ExpectedId = "novagame-m3"
            StageIdPrefix = "M3"
            FirstStageNumber = 1
            FinalStageId = "M3-08"
            StagePattern = '^## M3-\d{2} - '
            ExpectedStageCount = 8
            ExitSection = "## M3 Exit Criteria"
            RequiredDependency = "novagame-m2-complete"
        },
        [pscustomobject]@{
            Milestone = "M3.5"
            Path = "docs/goals/m3.5.md"
            ExpectedId = "novagame-m3.5"
            StageIdPrefix = "M35"
            FirstStageNumber = 1
            FinalStageId = "M35-08"
            StagePattern = '^## M35-\d{2} - '
            ExpectedStageCount = 8
            ExitSection = "## M3.5 Exit Criteria"
            RequiredDependency = "novagame-m3-complete"
        }
    )
    $allowedGoalStatuses = @(
        "drafting", "ready", "in_progress", "blocked", "complete")
    $activeGoalStatuses = @("in_progress", "blocked")
    $allowedLastVerificationStatuses = @("pending", "passed", "failed", "blocked")
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
        $lastVerificationStatus =
            $lastVerificationStatusMatch.Groups["value"].Value
        Assert-Condition `
            ($goalStatus -in $allowedGoalStatuses) `
            "$($definition.Milestone) goal has unsupported status '$goalStatus'."
        Assert-Condition `
            ($lastVerificationStatus -in $allowedLastVerificationStatuses) `
            "$($definition.Milestone) goal has unsupported last_verification_status '$lastVerificationStatus'."
        if ($lastVerificationStatus -eq "passed") {
            Assert-Condition `
                ([regex]::IsMatch(
                    $lastVerificationResult,
                    '^Passed(?:[ :.-].*)?$')) `
                "$($definition.Milestone) passed verification metadata must record a Passed result."
        }
        if ($lastVerificationStatus -eq "failed") {
            Assert-Condition `
                ([regex]::IsMatch(
                    $lastVerificationResult,
                    '^Failed(?:[ :.-].*)?$')) `
                "$($definition.Milestone) failed verification metadata must record a Failed result."
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
    $m3Goal = $goalRecords["M3"]
    $m35Goal = $goalRecords["M3.5"]

    foreach ($requiredM35Metadata in @(
        'capability: administrator-management',
        'capability_owner: novagame',
        'owning_domain: Operations/AdminAccess',
        'authorization_reference: user-request-2026-08-13-start-admin-management',
        'baseline_commit: 42d1ffe83770571d8f48cdb58f35bfc591a4bd1c',
        'extraction_authorized: false',
        'novacore_changes_authorized: false'
    )) {
        Assert-Condition `
            $m35Goal.Metadata.Contains($requiredM35Metadata) `
            "M3.5 goal metadata is missing '$requiredM35Metadata'."
    }
    Assert-Condition `
        ($m3Goal.Status -eq "complete") `
        "M3.5 requires the completed M3 baseline."

    $m35TaskRegisterMatches = [regex]::Matches(
        $m35Goal.Content,
        '(?ms)^## Active Task Register\r?\n(?<body>.*?)(?=^##\s+|\z)')
    Assert-Condition `
        ($m35TaskRegisterMatches.Count -eq 1) `
        "M3.5 must contain exactly one Active Task Register."
    $m35TaskRegister =
        $m35TaskRegisterMatches[0].Groups["body"].Value
    $m35TaskRows = [regex]::Matches(
        $m35TaskRegister,
        '(?m)^\|[ \t]*(?<id>M35-\d{2}-T\d{2})[ \t]*\|[ \t]*(?<stage>M35-\d{2})[ \t]*\|[^\r\n]*\|[ \t]*(?<status>pending|in_progress|blocked|complete)[ \t]*\|[ \t]*$')
    Assert-Condition `
        ($m35TaskRows.Count -gt 0) `
        "M3.5 Active Task Register must contain executable tasks."
    $duplicateM35TaskIds = @(
        $m35TaskRows |
            Group-Object { $_.Groups["id"].Value } |
            Where-Object Count -gt 1)
    Assert-Condition `
        ($duplicateM35TaskIds.Count -eq 0) `
        "M3.5 Active Task Register contains duplicate task identifiers."

    $expectedM35TaskIds = @(
        "M35-01-T01", "M35-01-T02", "M35-01-T03", "M35-01-T04",
        "M35-02-T01", "M35-02-T02", "M35-02-T03", "M35-02-T04",
        "M35-02-T05", "M35-02-T06",
        "M35-03-T01", "M35-03-T02", "M35-03-T03", "M35-03-T04",
        "M35-04-T01", "M35-04-T02", "M35-04-T03", "M35-04-T04",
        "M35-04-T05", "M35-04-T06",
        "M35-05-T01", "M35-05-T02", "M35-05-T03", "M35-05-T04",
        "M35-05-T05", "M35-05-T06",
        "M35-06-T01", "M35-06-T02", "M35-06-T03", "M35-06-T04",
        "M35-07-T01", "M35-07-T02", "M35-07-T03", "M35-07-T04",
        "M35-08-T01", "M35-08-T02", "M35-08-T03"
    )
    $actualM35TaskIds = @(
        $m35TaskRows | ForEach-Object { $_.Groups["id"].Value })
    Assert-Condition `
        ($actualM35TaskIds.Count -eq 37) `
        "M3.5 Active Task Register must contain exactly 37 tasks."
    for ($m35TaskIndex = 0;
        $m35TaskIndex -lt $expectedM35TaskIds.Count;
        $m35TaskIndex++) {
        Assert-Condition `
            ($actualM35TaskIds[$m35TaskIndex] -eq
                $expectedM35TaskIds[$m35TaskIndex]) `
            "M3.5 task order differs at index $m35TaskIndex."
    }

    $m35TaskStatuses = @{}
    foreach ($m35TaskRow in $m35TaskRows) {
        $m35TaskStatuses[$m35TaskRow.Groups["id"].Value] =
            $m35TaskRow.Groups["status"].Value
    }
    Assert-Condition `
        ([regex]::IsMatch(
            $m35Goal.Content,
            '(?m)^\|[ \t]*M35-02-T03[ \t]*\|[ \t]*M35-02[ \t]*\|[ \t]*Persistence[ \t]*\|[^\r\n]*\|[ \t]*M35-02-T02[ \t]*\|')) `
        "M35-02-T03 must retain M35-02-T02 as its prerequisite."
    Assert-Condition `
        ([regex]::IsMatch(
            $m35Goal.Content,
            '(?m)^\|[ \t]*M35-04-T01[ \t]*\|[ \t]*M35-04[ \t]*\|[ \t]*Persistence[ \t]*\|[^\r\n]*all three frozen receipt kinds[^\r\n]*\|')) `
        "M35-04-T01 must implement all three frozen receipt kinds."

    $expectedM35ContractHeadings = @(
        "## M35-02 Frozen Domain And Security Contract",
        "## M35-02 Frozen Application And HTTP Contract",
        "## M35-02 Frozen MySQL Contract",
        "## M35-02 Frozen Vben And Browser Contract",
        "## M35-02 Frozen Verification Contract"
    )
    $actualM35ContractHeadings = @(
        [regex]::Matches(
            $m35Goal.Content,
            '(?m)^## M35-02 Frozen [^\r\n]+ Contract$') |
            ForEach-Object { $_.Value })
    Assert-Condition `
        ($actualM35ContractHeadings.Count -eq
            $expectedM35ContractHeadings.Count) `
        "M35-02 must contain exactly five frozen contract sections."
    for ($m35ContractIndex = 0;
        $m35ContractIndex -lt $expectedM35ContractHeadings.Count;
        $m35ContractIndex++) {
        Assert-Condition `
            ($actualM35ContractHeadings[$m35ContractIndex] -eq
                $expectedM35ContractHeadings[$m35ContractIndex]) `
            "M35-02 frozen contract headings must remain exact and ordered."
    }
    Assert-Condition `
        (-not [regex]::IsMatch(
            $m35Goal.Content,
            '(?im)\b(?:TODO|TBD|FIXME)\b|to be decided|<placeholder>')) `
        "M3.5 goal contains an unresolved design marker."
    Assert-Condition `
        (-not $m35Goal.Content.Contains(
            "generate or mechanically verify",
            [StringComparison]::OrdinalIgnoreCase)) `
        "M3.5 must retain the single frozen TypeScript contract-generation workflow."

    if ($m35TaskStatuses["M35-02-T05"] -in @(
            "in_progress", "blocked", "complete")) {
        $m35VerificationContractMatches = [regex]::Matches(
            $m35Goal.Content,
            '(?ms)^## M35-02 Frozen Verification Contract\r?\n(?<body>.*?)(?=^##\s+|\z)')
        Assert-Condition `
            ($m35VerificationContractMatches.Count -eq 1) `
            "M3.5 must contain one frozen verification contract."
        $m35VerificationContract =
            $m35VerificationContractMatches[0].Groups["body"].Value
        $expectedM35VerificationHeadings = @(
            "### Evidence Rules",
            "### Requirement-To-Evidence Contract",
            "### Focused Verification Commands",
            "### Real Acceptance And Cleanup",
            "### Ordered Cumulative Verification"
        )
        $actualM35VerificationHeadings = @(
            [regex]::Matches(
                $m35VerificationContract,
                '(?m)^### [^\r\n]+$') |
                ForEach-Object { $_.Value })
        Assert-Condition `
            ($actualM35VerificationHeadings.Count -eq
                $expectedM35VerificationHeadings.Count) `
            "M3.5 verification contract must contain exactly five subsections."
        for ($m35HeadingIndex = 0;
            $m35HeadingIndex -lt $expectedM35VerificationHeadings.Count;
            $m35HeadingIndex++) {
            Assert-Condition `
                ($actualM35VerificationHeadings[$m35HeadingIndex] -eq
                    $expectedM35VerificationHeadings[$m35HeadingIndex]) `
                "M3.5 verification contract subsections must remain exact and ordered."
        }

        $m35EvidenceRows = [regex]::Matches(
            $m35VerificationContract,
            '(?m)^\|[ \t]*(?<id>M35-EV-\d{2})[ \t]*\|[ \t]*(?<requirement>[^|\r\n]+?)[ \t]*\|[ \t]*(?<evidence>[^|\r\n]+?)[ \t]*\|[ \t]*(?<command>[^|\r\n]+?)[ \t]*\|[ \t]*(?<owner>[^|\r\n]+?)[ \t]*\|[ \t]*$')
        Assert-Condition `
            ($m35EvidenceRows.Count -eq 12) `
            "M3.5 requirement-to-evidence contract must contain exactly 12 rows."
        for ($m35EvidenceIndex = 0;
            $m35EvidenceIndex -lt 12;
            $m35EvidenceIndex++) {
            $expectedEvidenceId = "M35-EV-{0:D2}" -f ($m35EvidenceIndex + 1)
            $m35EvidenceRow = $m35EvidenceRows[$m35EvidenceIndex]
            Assert-Condition `
                ($m35EvidenceRow.Groups["id"].Value -eq $expectedEvidenceId) `
                "M3.5 evidence rows must remain M35-EV-01 through M35-EV-12 in order."
            foreach ($m35EvidenceField in @(
                "requirement", "evidence", "command", "owner")) {
                Assert-Condition `
                    (-not [string]::IsNullOrWhiteSpace(
                        $m35EvidenceRow.Groups[$m35EvidenceField].Value)) `
                    "M3.5 evidence '$expectedEvidenceId' has an empty '$m35EvidenceField' field."
            }
        }

        $expectedM35EvidenceRowTokens = @(
            [pscustomobject]@{ Tokens = @("M35-EV-01", "Administrator transitions", "Positive-count Domain tests", "AdminManagement", "M35-03-T01, M35-03-T03") },
            [pscustomobject]@{ Tokens = @("M35-EV-02", "Custom-role", "Domain and policy tests", "AdminManagement", "M35-03-T01, M35-03-T03") },
            [pscustomobject]@{ Tokens = @("M35-EV-03", "All nine permissions", "denial-order tests", "AdminManagement", "M35-03-T02, M35-03-T03, M35-04-T03") },
            [pscustomobject]@{ Tokens = @("M35-EV-04", "Strong ETags", "audit-failure tests", "AdminManagement", "M35-03-T02, M35-03-T03, M35-04-T04") },
            [pscustomobject]@{ Tokens = @("M35-EV-05", "Family projections", "session-projection tests", "AdminManagement", "M35-04-T01, M35-04-T04") },
            [pscustomobject]@{ Tokens = @("M35-EV-06", "additive migration", "pending-model tests", "AdminManagement", "M35-04-T01, M35-04-T04") },
            [pscustomobject]@{ Tokens = @("M35-EV-07", "DbMigrator synchronizes", "second-writer MySQL tests", "AdminManagement", "M35-04-T02, M35-04-T04") },
            [pscustomobject]@{ Tokens = @("M35-EV-08", "count/page snapshot", "consistent-snapshot tests", "AdminManagement", "M35-04-T01, M35-04-T04") },
            [pscustomobject]@{ Tokens = @("M35-EV-09", "All 17 routes", "artifact regeneration check", "update-admin-access-contract.ps1 -Check", "M35-04-T03, M35-04-T05") },
            [pscustomobject]@{ Tokens = @("M35-EV-10", "Vben transport", "focused Vitest", "AdminManagementAdmin", "M35-05-T01, M35-05-T05") },
            [pscustomobject]@{ Tokens = @("M35-EV-11", "All four workspaces", "component Vitest", "Scope Admin", "M35-05-T02, M35-05-T03, M35-05-T04, M35-05-T05") },
            [pscustomobject]@{ Tokens = @("M35-EV-12", "real Vben/API/DbMigrator/MySQL workflow", "Playwright JSON", "Scope AdminE2E", "M35-06, M35-07, M35-08") }
        )
        for ($m35EvidenceIndex = 0;
            $m35EvidenceIndex -lt $expectedM35EvidenceRowTokens.Count;
            $m35EvidenceIndex++) {
            $m35EvidenceRowText = $m35EvidenceRows[$m35EvidenceIndex].Value
            foreach ($requiredM35EvidenceRowToken in
                $expectedM35EvidenceRowTokens[$m35EvidenceIndex].Tokens) {
                Assert-Condition `
                    $m35EvidenceRowText.Contains($requiredM35EvidenceRowToken) `
                    "M3.5 evidence row '$($m35EvidenceRows[$m35EvidenceIndex].Groups['id'].Value)' is missing '$requiredM35EvidenceRowToken'."
            }
        }

        $m35FocusedCommandsMatch = [regex]::Match(
            $m35VerificationContract,
            '(?ms)^### Focused Verification Commands\r?\n(?<body>.*?)(?=^###\s+|\z)')
        Assert-Condition `
            $m35FocusedCommandsMatch.Success `
            "M3.5 verification contract is missing focused commands."
        $m35FilterTableMatches = [regex]::Matches(
            $m35FocusedCommandsMatch.Groups["body"].Value,
            '(?ms)^\| Activation task \| Exact filter \|\r?\n' +
                '\| --- \| --- \|\r?\n' +
                '(?<rows>(?:\|[^\r\n]+\|(?:\r?\n|\z))+)' )
        Assert-Condition `
            ($m35FilterTableMatches.Count -eq 1) `
            "M3.5 focused commands must contain exactly one canonical filter table."
        $m35FilterLines = @(
            $m35FilterTableMatches[0].Groups["rows"].Value -split '\r?\n' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $m35FilterRows = @(
            foreach ($m35FilterLine in $m35FilterLines) {
                $m35FilterRow = [regex]::Match(
                    $m35FilterLine,
                    '^\|[ \t]*(?<task>M35-\d{2}-T\d{2})[ \t]*\|[ \t]*(?:retained[ \t]+)?`(?<filter>FullyQualifiedName~[^`]+)`(?:[ \t]+regression)?[ \t]*\|[ \t]*$')
                Assert-Condition `
                    $m35FilterRow.Success `
                    "M3.5 focused filter table contains a malformed row '$m35FilterLine'."
                $m35FilterRow
            })
        $expectedM35FilterManifest = @(
            [pscustomobject]@{ Task = "M35-03-T01"; Filter = "FullyQualifiedName~AdminAccessManagementAdministratorDomainTests" },
            [pscustomobject]@{ Task = "M35-03-T01"; Filter = "FullyQualifiedName~AdminAccessManagementRoleDomainTests" },
            [pscustomobject]@{ Task = "M35-03-T02"; Filter = "FullyQualifiedName~AdminAccessManagementAuthorizationTests" },
            [pscustomobject]@{ Task = "M35-03-T02"; Filter = "FullyQualifiedName~AdminAccessManagementConcurrencyTests" },
            [pscustomobject]@{ Task = "M35-04-T01"; Filter = "FullyQualifiedName~AdminAccessManagementSessionTests" },
            [pscustomobject]@{ Task = "M35-04-T01"; Filter = "FullyQualifiedName~AdminAccessManagementMigrationTests" },
            [pscustomobject]@{ Task = "M35-04-T01"; Filter = "FullyQualifiedName~AdminAccessManagementQueryTests" },
            [pscustomobject]@{ Task = "M35-04-T02"; Filter = "FullyQualifiedName~AdminAccessManagementSeedTests" },
            [pscustomobject]@{ Task = "M35-04-T03"; Filter = "FullyQualifiedName~AdminAccessManagementApiTests" },
            [pscustomobject]@{ Task = "M35-04-T03"; Filter = "FullyQualifiedName~AdminAccessManagementOpenApiTests" },
            [pscustomobject]@{ Task = "M35-06-T02"; Filter = "FullyQualifiedName~AdminAccessManagementComposedApiAcceptanceTests" },
            [pscustomobject]@{ Task = "M35-03-T01"; Filter = "FullyQualifiedName~AdministratorAccessApiTests" }
        )
        Assert-Condition `
            ($m35FilterRows.Count -eq $expectedM35FilterManifest.Count) `
            "M3.5 focused filter manifest must contain exactly 12 rows."
        for ($m35FilterIndex = 0;
            $m35FilterIndex -lt $expectedM35FilterManifest.Count;
            $m35FilterIndex++) {
            Assert-Condition `
                ($m35FilterRows[$m35FilterIndex].Groups["task"].Value -eq
                    $expectedM35FilterManifest[$m35FilterIndex].Task -and
                $m35FilterRows[$m35FilterIndex].Groups["filter"].Value -eq
                    $expectedM35FilterManifest[$m35FilterIndex].Filter) `
                "M3.5 focused filter manifest differs at index $m35FilterIndex."
        }

        if ($m35TaskStatuses["M35-03-T01"] -in @(
                "in_progress", "blocked", "complete")) {
            $verificationScript = Get-Content `
                -LiteralPath "scripts/verify.ps1" `
                -Raw
            $adminManagementFunctionMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Test-AdminManagement \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($adminManagementFunctionMatches.Count -eq 1) `
                "AdminManagement must have exactly one stage-aware verification function."
            $adminManagementFunction =
                $adminManagementFunctionMatches[0].Groups["body"].Value
            $implementedFilterRows = @(
                [regex]::Matches(
                    $adminManagementFunction,
                    '(?ms)Task = "(?<task>M35-\d{2}-T\d{2})"\s+Filter = "(?<filter>FullyQualifiedName~[^"]+)"'))
            Assert-Condition `
                ($implementedFilterRows.Count -eq
                    $expectedM35FilterManifest.Count) `
                "AdminManagement implementation must contain the exact frozen filter manifest."
            for ($implementedFilterIndex = 0;
                $implementedFilterIndex -lt $expectedM35FilterManifest.Count;
                $implementedFilterIndex++) {
                Assert-Condition `
                    ($implementedFilterRows[$implementedFilterIndex].Groups["task"].Value -eq
                        $expectedM35FilterManifest[$implementedFilterIndex].Task -and
                    $implementedFilterRows[$implementedFilterIndex].Groups["filter"].Value -eq
                        $expectedM35FilterManifest[$implementedFilterIndex].Filter) `
                    "AdminManagement implementation manifest differs at index $implementedFilterIndex."
            }
            Assert-Condition `
                ($adminManagementFunction.Contains(
                        'Get-M35TaskStatus -TaskId $_.Task',
                        [StringComparison]::Ordinal) -and
                    $adminManagementFunction.Contains(
                        '@("in_progress", "blocked", "complete")',
                        [StringComparison]::Ordinal)) `
                "AdminManagement must select focused families through current task status."
            $focusedTestFunctionMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Invoke-DotNetFilteredTestWithExecutionCount \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($focusedTestFunctionMatches.Count -eq 1) `
                "Focused .NET evidence must have exactly one positive-count helper."
            $focusedTestFunction =
                $focusedTestFunctionMatches[0].Groups["body"].Value
            Assert-Condition `
                ($verificationScript.Contains(
                        '"AdminManagement" { Test-AdminManagement }',
                        [StringComparison]::Ordinal) -and
                    $focusedTestFunction.Contains(
                        'test-results\focused-',
                        [StringComparison]::Ordinal) -and
                    -not $focusedTestFunction.Contains(
                        'test-results\swordmaster-',
                        [StringComparison]::Ordinal)) `
                "AdminManagement must expose the frozen scope and use neutral focused-test evidence paths."
        }

        if ($m35TaskStatuses["M35-07-T03"] -in @(
                "in_progress", "blocked", "complete")) {
            $verificationScript = Get-Content `
                -LiteralPath "scripts/verify.ps1" `
                -Raw
            $capacityPredicateMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Test-AdminEndToEndHostCapacitySnapshot \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($capacityPredicateMatches.Count -eq 1) `
                "Admin E2E host-capacity policy must have exactly one snapshot predicate."
            $capacityPredicate =
                $capacityPredicateMatches[0].Groups["body"].Value
            $integralTypeCheckPosition = $capacityPredicate.IndexOf(
                '$integralCapacityValueTypes -contains $propertyValue.GetType()',
                [StringComparison]::Ordinal)
            $capacityConversionPosition = $capacityPredicate.IndexOf(
                '[Convert]::ToInt64(',
                [StringComparison]::Ordinal)
            Assert-Condition `
                ($integralTypeCheckPosition -ge 0 -and
                    $capacityConversionPosition -gt $integralTypeCheckPosition) `
                "Admin E2E host-capacity policy must reject non-integral values before conversion."
            foreach ($requiredIntegralCapacityValueType in @(
                    "[sbyte]",
                    "[byte]",
                    "[int16]",
                    "[uint16]",
                    "[int32]",
                    "[uint32]",
                    "[int64]",
                    "[uint64]"
                )) {
                Assert-Condition `
                    $capacityPredicate.Contains(
                        $requiredIntegralCapacityValueType,
                        [StringComparison]::Ordinal) `
                    "Admin E2E host-capacity policy is missing integral type '$requiredIntegralCapacityValueType'."
            }
            $capacityGateMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Assert-AdminEndToEndHostCapacity \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($capacityGateMatches.Count -eq 1) `
                "Admin E2E host-capacity policy must have exactly one admission gate."
            $capacityGate = $capacityGateMatches[0].Groups["body"].Value
            foreach ($requiredCapacityPredicateText in @(
                    "AvailableMBytes",
                    "CommittedBytes",
                    "CommitLimit",
                    "2048",
                    "4GB"
                )) {
                Assert-Condition `
                    $capacityPredicate.Contains(
                        $requiredCapacityPredicateText,
                        [StringComparison]::Ordinal) `
                    "Admin E2E host-capacity predicate is missing '$requiredCapacityPredicateText'."
            }
            foreach ($requiredCapacityGateText in @(
                    "Win32_PerfFormattedData_PerfOS_Memory",
                    "admin-e2e-host-capacity-unavailable"
                )) {
                Assert-Condition `
                    $capacityGate.Contains(
                        $requiredCapacityGateText,
                        [StringComparison]::Ordinal) `
                    "Admin E2E host-capacity gate is missing '$requiredCapacityGateText'."
            }
            Assert-Condition `
                ([regex]::Matches($capacityGate, 'Get-CimInstance').Count -eq 1) `
                "Admin E2E host-capacity gate must query exactly one aggregate snapshot."
            foreach ($forbiddenCapacityGateText in @(
                    "Win32_Process",
                    "Get-Process",
                    "Start-Process",
                    "Stop-Process",
                    ".Kill",
                    "docker"
                )) {
                Assert-Condition `
                    (-not $capacityGate.Contains(
                            $forbiddenCapacityGateText,
                            [StringComparison]::OrdinalIgnoreCase)) `
                    "Admin E2E host-capacity gate must not contain '$forbiddenCapacityGateText'."
            }

            $invokeAdminE2EMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Invoke-AdminEndToEnd \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($invokeAdminE2EMatches.Count -eq 1) `
                "Admin E2E must have exactly one composition function."
            $invokeAdminE2E = $invokeAdminE2EMatches[0].Groups["body"].Value
            $invokeGatePosition = $invokeAdminE2E.IndexOf(
                "Assert-AdminEndToEndHostCapacity",
                [StringComparison]::Ordinal)
            $invokeDockerPosition = $invokeAdminE2E.IndexOf(
                'Invoke-CheckedCommand "docker"',
                [StringComparison]::Ordinal)
            Assert-Condition `
                ($invokeGatePosition -ge 0 -and
                    $invokeDockerPosition -gt $invokeGatePosition) `
                "Admin E2E must apply host-capacity admission before Docker composition."

            $adminE2EEntryMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Test-AdminEndToEnd \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($adminE2EEntryMatches.Count -eq 1) `
                "Admin E2E must have exactly one direct entry point."
            $adminE2EEntry = $adminE2EEntryMatches[0].Groups["body"].Value
            $entryGatePosition = $adminE2EEntry.IndexOf(
                "Assert-AdminEndToEndHostCapacity",
                [StringComparison]::Ordinal)
            $toolchainPosition = $adminE2EEntry.IndexOf(
                "Initialize-AdminToolchain",
                [StringComparison]::Ordinal)
            Assert-Condition `
                ($entryGatePosition -ge 0 -and
                    $toolchainPosition -gt $entryGatePosition) `
                "Admin E2E must apply host-capacity admission before toolchain startup."

            $adminEntryMatches = [regex]::Matches(
                $verificationScript,
                '(?ms)^function Test-Admin \{\r?\n(?<body>.*?)(?=^function\s+|\z)')
            Assert-Condition `
                ($adminEntryMatches.Count -eq 1) `
                "Admin must have exactly one browser-capable entry point."
            $adminEntry = $adminEntryMatches[0].Groups["body"].Value
            $adminModeGuardPosition = $adminEntry.IndexOf(
                'if ($m35BrowserMode -eq "M35Management") {',
                [StringComparison]::Ordinal)
            $adminEntryGatePosition = $adminEntry.IndexOf(
                "Assert-AdminEndToEndHostCapacity",
                [StringComparison]::Ordinal)
            $adminToolchainPosition = $adminEntry.IndexOf(
                "Initialize-AdminToolchain",
                [StringComparison]::Ordinal)
            Assert-Condition `
                ($adminModeGuardPosition -ge 0 -and
                    $adminEntryGatePosition -gt $adminModeGuardPosition -and
                    $adminToolchainPosition -gt $adminEntryGatePosition) `
                "Admin M35 management must apply host-capacity admission before toolchain startup."
        }

        foreach ($requiredM35VerificationText in @(
            'total > 0',
            'executed > 0',
            'passed = executed',
            'failed = 0',
            'notExecuted = 0',
            'numTotalTests > 0',
            'numPassedTests = numTotalTests',
            'numFailedTests = 0',
            'numPendingTests = 0',
            'exactly five tests',
            'FullyQualifiedName~AdminAccessManagementAdministratorDomainTests',
            'FullyQualifiedName~AdminAccessManagementRoleDomainTests',
            'FullyQualifiedName~AdminAccessManagementAuthorizationTests',
            'FullyQualifiedName~AdminAccessManagementConcurrencyTests',
            'FullyQualifiedName~AdminAccessManagementSessionTests',
            'FullyQualifiedName~AdminAccessManagementQueryTests',
            'FullyQualifiedName~AdminAccessManagementApiTests',
            'FullyQualifiedName~AdminAccessManagementOpenApiTests',
            'FullyQualifiedName~AdminAccessManagementMigrationTests',
            'FullyQualifiedName~AdminAccessManagementSeedTests',
            'FullyQualifiedName~AdminAccessManagementComposedApiAcceptanceTests',
            'FullyQualifiedName~AdministratorAccessApiTests',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope AdminManagement',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope AdminManagementAdmin',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope AdminE2E',
            'pwsh -NoProfile -File scripts/update-admin-access-contract.ps1 -Check',
            'novagame-test-[a-f0-9]{12}',
            'docker compose down --volumes --remove-orphans',
            '.local/admin-management/<run-id>',
            'Windows Job Object',
            'kill-on-close',
            'PID plus creation time',
            'Failure screenshots',
            'remaining binary failed artifact',
            'in-memory exact-secret registry',
            'Windows named pipe',
            'data.accessToken',
            'duplicate keys',
            'raw source span',
            'response-audit JSON',
            'audited wrapper',
            'closes every registered browser context',
            'M35-07-T01',
            'strict UTF-8 body',
            'pixel diversity',
            'empty allowlist',
            'NOVACORE_ROOT',
            'D:\Programs\nova-core',
            'ef8db55173ff8f0286bb06abe753861c8a7d2d80',
            '222f5c59188d06098b677a7ff004c546871391ea',
            'finally',
            'Vben mock backend',
            'sequentially, never concurrently'
        )) {
            Assert-Condition `
                $m35VerificationContract.Contains($requiredM35VerificationText) `
                "M3.5 verification contract is missing '$requiredM35VerificationText'."
        }
        Assert-Condition `
            ([regex]::IsMatch(
                $m35VerificationContract,
                '(?s)Composed API and browser acceptance.*?direct MySQL\s+mutation to create management state')) `
            "M3.5 real acceptance must prohibit direct MySQL management-state creation."

        $m35VerificationLogMatches = [regex]::Matches(
            $m35Goal.Content,
            '(?ms)^## Verification Log\r?\n(?<body>.*?)(?=^##\s+|\z)')
        Assert-Condition `
            ($m35VerificationLogMatches.Count -eq 1) `
            "M3.5 goal must contain exactly one Verification Log."
        $m35VerificationLog =
            $m35VerificationLogMatches[0].Groups["body"].Value
        foreach ($completedM35TaskId in @(
                $m35TaskRows |
                    Where-Object {
                        $_.Groups["status"].Value -eq "complete"
                    } |
                    ForEach-Object { $_.Groups["id"].Value })) {
            $m35TaskLogPattern =
                '(?m)^\|[ \t]*\d{4}-\d{2}-\d{2}[ \t]*\|[ \t]*' +
                [regex]::Escape($completedM35TaskId) +
                '[ \t]*\|[ \t]*(?<command>[^|\r\n]*)\|[ \t]*' +
                '(?<result>[^|\r\n]*)\|[ \t]*$'
            $m35TaskLogMatches = [regex]::Matches(
                $m35VerificationLog,
                $m35TaskLogPattern)
            Assert-Condition `
                ($m35TaskLogMatches.Count -eq 1) `
                "Completed M3.5 task '$completedM35TaskId' needs exactly one canonical verification-log row."
            Assert-Condition `
                (-not [string]::IsNullOrWhiteSpace(
                    $m35TaskLogMatches[0].Groups["command"].Value)) `
                "Completed M3.5 task '$completedM35TaskId' has an empty verification command."
            Assert-Condition `
                ([regex]::IsMatch(
                    $m35TaskLogMatches[0].Groups["result"].Value.Trim(),
                    '^Passed(?:[:.].*)?$')) `
                "Completed M3.5 task '$completedM35TaskId' must record a canonical Passed result."
        }
    }

    if ($m35TaskStatuses["M35-06-T01"] -in @(
            "in_progress", "blocked", "complete")) {
        $m356EvidenceHeading = "## M35-06 Real Server Acceptance Evidence Record"
        $m356EvidenceMatches = [regex]::Matches(
            $m35Goal.Content,
            '(?ms)^## M35-06 Real Server Acceptance Evidence Record\r?\n(?<body>.*?)(?=^##\s+|\z)')
        Assert-Condition `
            ($m356EvidenceMatches.Count -eq 1) `
            "M35-06-T01 requires exactly one real-server acceptance evidence record."
        $m356Evidence = $m356EvidenceMatches[0].Groups["body"].Value
        $m35VerificationContractIndex = $m35Goal.Content.IndexOf(
            "## M35-02 Frozen Verification Contract",
            [StringComparison]::Ordinal)
        $m356EvidenceIndex = $m35Goal.Content.IndexOf(
            $m356EvidenceHeading,
            [StringComparison]::Ordinal)
        $m35StageStatusIndex = $m35Goal.Content.IndexOf(
            "## Stage Status",
            [StringComparison]::Ordinal)
        Assert-Condition `
            ($m35VerificationContractIndex -ge 0 -and
            $m356EvidenceIndex -gt $m35VerificationContractIndex -and
            $m35StageStatusIndex -gt $m356EvidenceIndex) `
            "M35-06 real-server evidence must follow the frozen M35-02 verification contract and precede Stage Status."

        $expectedM356Headings = @(
            "### Evidence Rules",
            "### Requirement-To-Evidence Matrix",
            "### Exact M35-06 Acceptance Commands",
            "### Frozen T02 Boundary"
        )
        $actualM356Headings = @(
            [regex]::Matches(
                $m356Evidence,
                '(?m)^### [^\r\n]+$') |
                ForEach-Object { $_.Value })
        Assert-Condition `
            ($actualM356Headings.Count -eq $expectedM356Headings.Count) `
            "M35-06 real-server evidence must contain exactly four subsections."
        for ($m356HeadingIndex = 0;
            $m356HeadingIndex -lt $expectedM356Headings.Count;
            $m356HeadingIndex++) {
            Assert-Condition `
                ($actualM356Headings[$m356HeadingIndex] -eq
                    $expectedM356Headings[$m356HeadingIndex]) `
                "M35-06 real-server evidence subsections must remain exact and ordered."
        }

        Assert-Condition `
            $m356Evidence.Contains(
                "| ID | Acceptance requirement | Required boundary | Planned evidence | Exact verification command | Owning task |") `
            "M35-06 real-server evidence must retain its exact matrix header."
        $m356EvidenceRows = @(
            [regex]::Matches(
                $m356Evidence,
                '(?m)^\|[ \t]*(?<id>M356-AE-\d{2})[ \t]*\|[ \t]*(?<requirement>[^|\r\n]+?)[ \t]*\|[ \t]*(?<boundary>[^|\r\n]+?)[ \t]*\|[ \t]*(?<evidence>[^|\r\n]+?)[ \t]*\|[ \t]*(?<command>[^|\r\n]+?)[ \t]*\|[ \t]*(?<owner>[^|\r\n]+?)[ \t]*\|[ \t]*$'))
        $expectedM356EvidenceIds = @(
            "M356-AE-01", "M356-AE-02", "M356-AE-03",
            "M356-AE-04", "M356-AE-05", "M356-AE-06")
        Assert-Condition `
            ($m356EvidenceRows.Count -eq $expectedM356EvidenceIds.Count) `
            "M35-06 real-server evidence must define exactly six matrix rows."
        for ($m356EvidenceIndex = 0;
            $m356EvidenceIndex -lt $expectedM356EvidenceIds.Count;
            $m356EvidenceIndex++) {
            $m356EvidenceRow = $m356EvidenceRows[$m356EvidenceIndex]
            Assert-Condition `
                ($m356EvidenceRow.Groups["id"].Value -eq
                    $expectedM356EvidenceIds[$m356EvidenceIndex]) `
                "M35-06 real-server evidence rows must remain M356-AE-01 through M356-AE-06 in order."
            foreach ($m356EvidenceField in @(
                    "requirement", "boundary", "evidence", "command", "owner")) {
                Assert-Condition `
                    (-not [string]::IsNullOrWhiteSpace(
                        $m356EvidenceRow.Groups[$m356EvidenceField].Value)) `
                    "M35-06 evidence '$($m356EvidenceRow.Groups['id'].Value)' has an empty '$m356EvidenceField' field."
            }
            Assert-Condition `
                ($m356EvidenceRow.Groups["command"].Value.Trim() -eq
                    '`pwsh -NoProfile -File scripts/verify.ps1 -Scope AdminManagement`') `
                "M35-06 evidence '$($m356EvidenceRow.Groups['id'].Value)' must use the qualifying AdminManagement command."
            Assert-Condition `
                ($m356EvidenceRow.Groups["owner"].Value.Trim() -eq "M35-06-T02") `
                "M35-06 evidence '$($m356EvidenceRow.Groups['id'].Value)' must remain owned by M35-06-T02."
        }

        $expectedM356EvidenceTokens = @(
            [pscustomobject]@{ Tokens = @("MySQL 8.4", "NovaGame.DbMigrator", "/api/auth/login", "NovaGame.Api") },
            [pscustomobject]@{ Tokens = @("custom role", "registered permissions", "administrator", "ETags") },
            [pscustomobject]@{ Tokens = @("/api/auth/codes", "/api/user/info", "protected management route", "immediately") },
            [pscustomobject]@{ Tokens = @("Password reset", "deactivation", "refresh families", "redacted", "15-minute") },
            [pscustomobject]@{ Tokens = @("created by HTTP", "bounded filters", "ordering", "paging", "durable MySQL reload", "audit evidence") },
            [pscustomobject]@{ Tokens = @("redacted", "17 routes", "envelopes", "ETags", "idempotency", "OpenAPI") }
        )
        for ($m356EvidenceIndex = 0;
            $m356EvidenceIndex -lt $expectedM356EvidenceTokens.Count;
            $m356EvidenceIndex++) {
            $m356EvidenceRowText = $m356EvidenceRows[$m356EvidenceIndex].Value
            foreach ($requiredM356EvidenceToken in
                $expectedM356EvidenceTokens[$m356EvidenceIndex].Tokens) {
                Assert-Condition `
                    $m356EvidenceRowText.Contains($requiredM356EvidenceToken) `
                    "M35-06 evidence row '$($m356EvidenceRows[$m356EvidenceIndex].Groups['id'].Value)' is missing '$requiredM356EvidenceToken'."
            }
        }

        $m356NormalizedEvidence = $m356Evidence.Replace("`r`n", "`n")
        $expectedM356T01CommandBlock = @'
pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation
pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository
pwsh -NoProfile -Command "[scriptblock]::Create((Get-Content -Raw scripts/verify.ps1)) | Out-Null"
git diff --check
'@
        $expectedM356T01CommandBlock = $expectedM356T01CommandBlock.Replace("`r`n", "`n")
        Assert-Condition `
            $m356NormalizedEvidence.Contains($expectedM356T01CommandBlock) `
            "M35-06-T01 must retain its exact ordered documentation-governance commands."
        foreach ($requiredM356EvidenceText in @(
                'Existing focused Domain, Application, persistence, API, and `WebApplicationFactory` tests remain',
                'real out-of-process `NovaGame.Api` Kestrel',
                "production registrations",
                "disposable MySQL 8.4",
                "Only the configured bootstrap administrator may be seeded.",
                "WebApplicationFactory",
                "mock backend",
                "response interception",
                "synthesized permissions",
                "repository replacement",
                "second DbContext",
                "direct MySQL management-state mutation",
                "Browser or Vben evidence is not T02 evidence.",
                "Raw MySQL is read-only for durable-state assertions after the relevant HTTP command.",
                "Test-only fault injection is reserved for T03",
                "denial order and",
                "self-lockout and built-in-role protection",
                "stale revisions and",
                "replay, fingerprint conflict, and concurrent commands",
                "secret/log redaction",
                "transactional audit-failure rollback",
                "AdminAccessManagementComposedApiAcceptanceTests",
                "M35-06-T02 adds this qualifying composed-server command")) {
            Assert-Condition `
                $m356Evidence.Contains($requiredM356EvidenceText) `
                "M35-06 real-server evidence is missing '$requiredM356EvidenceText'."
        }

        $m356T02AcceptancePath = `
            "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementComposedApiAcceptanceTests.cs"
        if ($m35TaskStatuses["M35-06-T02"] -eq "pending") {
            Assert-Condition `
                (-not (Test-Path -LiteralPath $m356T02AcceptancePath)) `
                "M35-06-T02 acceptance source appeared before T02 activation."
        }
    }

    $m35ImmutableConsumerPaths = @(
        "clients/cocos",
        "samples/SwordMaster",
        "server/novacore.lock.json"
    )
    $m35ImmutableTrackedArguments = @(
        "diff", "--name-only", "--no-ext-diff",
        "42d1ffe83770571d8f48cdb58f35bfc591a4bd1c", "--"
    ) + $m35ImmutableConsumerPaths
    $m35ImmutableTrackedChanges = @(
        & git @m35ImmutableTrackedArguments |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to audit M3.5 immutable consumer paths."
    }
    $m35ImmutableUntrackedArguments = @(
        "ls-files", "--others", "--exclude-standard", "--"
    ) + $m35ImmutableConsumerPaths
    $m35ImmutableUntrackedChanges = @(
        & git @m35ImmutableUntrackedArguments |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to audit M3.5 untracked immutable consumer paths."
    }
    $m35ImmutableConsumerChanges = @(
        $m35ImmutableTrackedChanges
        $m35ImmutableUntrackedChanges
    )
    Assert-Condition `
        ($m35ImmutableConsumerChanges.Count -eq 0) `
        "M3.5 must not change Cocos, SwordMaster, or the NovaCore lock:`n$($m35ImmutableConsumerChanges -join "`n")"

    $m35VerificationOnlyAdminHashes = [ordered]@{
        "admin/apps/web-ele/e2e/acceptance-evidence.ts" =
            "C9804592723D309CB64721AD68465369F3BACC3A57C214AE67C53E8FBC27D920"
        "admin/apps/web-ele/e2e/game-config.spec.ts" =
            "2A9671EE7AF10D1A6335FB5E6F89481377F992B3014312F52D5C27A1B987770D"
    }
    $m35VerificationOnlyAdminPaths = @(
        $m35VerificationOnlyAdminHashes.Keys)
    foreach ($m35VerificationOnlyAdminPath in
        $m35VerificationOnlyAdminPaths) {
        $m35VerificationOnlyPathMustRemainProtected =
            if ($m35VerificationOnlyAdminPath -eq
                "admin/apps/web-ele/e2e/acceptance-evidence.ts") {
                $m35Goal.Status -ne "complete"
            }
            else {
                $m35TaskStatuses["M35-07-T01"] -eq "pending"
            }
        if ($m35VerificationOnlyPathMustRemainProtected) {
            Assert-Condition `
                (Test-Path `
                    -LiteralPath $m35VerificationOnlyAdminPath `
                    -PathType Leaf) `
                "The protected Admin E2E evidence file '$m35VerificationOnlyAdminPath' is missing."
            $m35VerificationOnlySource = Get-Content `
                -LiteralPath $m35VerificationOnlyAdminPath `
                -Raw
            $m35NormalizedVerificationOnlySource =
                $m35VerificationOnlySource.Replace("`r`n", "`n")
            $m35VerificationOnlyHash = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData(
                    [Text.UTF8Encoding]::new($false).GetBytes(
                        $m35NormalizedVerificationOnlySource)))
            Assert-Condition `
                ($m35VerificationOnlyHash -eq
                    $m35VerificationOnlyAdminHashes[
                        $m35VerificationOnlyAdminPath]) `
                "The protected evidence file '$m35VerificationOnlyAdminPath' differs before its release gate."
        }
    }

    if ($m35TaskStatuses["M35-03-T01"] -eq "pending") {
        $m35PrematureImplementationPaths = @(
            "server/src",
            "server/tests",
            "admin/apps/web-ele",
            "admin/package.json",
            "admin/pnpm-lock.yaml",
            "admin/playwright.config.ts",
            "admin/tsconfig.e2e.json",
            "scripts/update-admin-access-contract.ps1"
        )
        $m35TrackedImplementationArguments = @(
            "diff", "--name-only", "--no-ext-diff",
            "42d1ffe83770571d8f48cdb58f35bfc591a4bd1c", "--"
        ) + $m35PrematureImplementationPaths
        $m35TrackedImplementationChanges = @(
            & git @m35TrackedImplementationArguments |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to audit M3.5 tracked implementation paths."
        }
        $m35UntrackedImplementationArguments = @(
            "ls-files", "--others", "--exclude-standard", "--"
        ) + $m35PrematureImplementationPaths
        $m35UntrackedImplementationChanges = @(
            & git @m35UntrackedImplementationArguments |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to audit M3.5 untracked implementation paths."
        }
        $m35ImplementationChanges = @(
            $m35TrackedImplementationChanges
            $m35UntrackedImplementationChanges
        )
        $m35VerificationOnlyAdminChanges = @(
            $m35ImplementationChanges |
                Where-Object {
                    $m35VerificationOnlyAdminPaths -contains
                        ([string]$_).Replace('\', '/')
                })
        if ($m35VerificationOnlyAdminChanges.Count -gt 0) {
            Assert-Condition `
                ($m35TaskStatuses["M35-02-T05"] -in @(
                    "in_progress", "blocked", "complete")) `
                "The Admin E2E evidence guard requires M35-02-T05 activation."
            Assert-StringSet `
                -Expected $m35VerificationOnlyAdminPaths `
                -Actual @(
                    $m35VerificationOnlyAdminChanges |
                        ForEach-Object { ([string]$_).Replace('\', '/') }) `
                -Message "The M35-02-T05 verification-only file set changed unexpectedly."
        }
        $m35PrematureImplementationChanges = @(
            $m35ImplementationChanges |
                Where-Object {
                    $m35VerificationOnlyAdminPaths -notcontains
                        ([string]$_).Replace('\', '/')
                })
        Assert-Condition `
            ($m35PrematureImplementationChanges.Count -eq 0) `
            "M3.5 implementation changed before M35-03-T01:`n$($m35PrematureImplementationChanges -join "`n")"
    }

    $m35StageProtectedPathSets = @(
        [pscustomobject]@{
            ActivationTask = "M35-03-T02"
            Paths = @(
                "server/src/NovaGame.Application.Contracts",
                "server/src/NovaGame.Application",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementAuthorizationTests.cs",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementConcurrencyTests.cs"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-04-T01"
            Paths = @(
                "server/src/NovaGame.Host.EntityFrameworkCore",
                "server/src/NovaGame.Application/Operations/AdminAccess/RefreshTokenService.cs",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementSessionTests.cs",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementMigrationTests.cs",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementQueryTests.cs"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-04-T02"
            Paths = @(
                "server/src/NovaGame.Domain.Shared/Operations/AdminAccess/AdminAccessPermissionDefinitionProvider.cs",
                "server/src/NovaGame.Application/Operations/AdminAccess/AdministratorSeedContributor.cs",
                "server/src/NovaGame.Application/Operations/AdminAccess/AdminAccessSeedOptions.cs",
                "server/src/NovaGame.DbMigrator",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementSeedTests.cs"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-04-T03"
            Paths = @(
                "server/src/NovaGame.Api",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementApiTests.cs",
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementOpenApiTests.cs"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-06-T02"
            Paths = @(
                "server/tests/NovaGame.EntityFrameworkCore.Tests/AdminAccessManagementComposedApiAcceptanceTests.cs"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-04-T05"
            Paths = @(
                "admin/package.json",
                "admin/pnpm-lock.yaml",
                "admin/pnpm-workspace.yaml",
                "admin/eslint.config.mjs",
                "admin/apps/web-ele/package.json",
                "admin/apps/web-ele/contracts/admin-access.openapi.json",
                "admin/apps/web-ele/openapi-ts.config.mjs",
                "admin/apps/web-ele/src/generated/admin-access",
                "scripts/update-admin-access-contract.ps1"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-05-T01"
            Paths = @(
                "admin/apps/web-ele/src/api/admin-access",
                "admin/apps/web-ele/src/router/routes/modules/admin-access.ts"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-05-T02"
            Paths = @(
                "admin/apps/web-ele/src/views/admin-access"
            )
        },
        [pscustomobject]@{
            ActivationTask = "M35-07-T01"
            Paths = @(
                "admin/playwright.config.ts",
                "admin/tsconfig.e2e.json",
                "admin/apps/web-ele/e2e/admin-access.spec.ts"
            )
        }
    )
    foreach ($m35ProtectedPathSet in $m35StageProtectedPathSets) {
        if ($m35TaskStatuses[$m35ProtectedPathSet.ActivationTask] -ne "pending") {
            continue
        }
        $m35ProtectedTrackedArguments = @(
            "diff", "--name-only", "--no-ext-diff",
            "42d1ffe83770571d8f48cdb58f35bfc591a4bd1c", "--"
        ) + $m35ProtectedPathSet.Paths
        $m35ProtectedTrackedChanges = @(
            & git @m35ProtectedTrackedArguments |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to audit paths owned by '$($m35ProtectedPathSet.ActivationTask)'."
        }
        $m35ProtectedUntrackedArguments = @(
            "ls-files", "--others", "--exclude-standard", "--"
        ) + $m35ProtectedPathSet.Paths
        $m35ProtectedUntrackedChanges = @(
            & git @m35ProtectedUntrackedArguments |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to audit untracked paths owned by '$($m35ProtectedPathSet.ActivationTask)'."
        }
        $m35ProtectedChanges = @(
            $m35ProtectedTrackedChanges
            $m35ProtectedUntrackedChanges
        )
        Assert-Condition `
            ($m35ProtectedChanges.Count -eq 0) `
            "M3.5 paths changed before '$($m35ProtectedPathSet.ActivationTask)' activation:`n$($m35ProtectedChanges -join "`n")"
    }

    if ($m35TaskStatuses["M35-04-T05"] -in @(
            "in_progress", "blocked", "complete")) {
        $m35T05RequiredPaths = @(
            "admin/eslint.config.mjs",
            "admin/pnpm-workspace.yaml",
            "admin/apps/web-ele/contracts/admin-access.openapi.json",
            "admin/apps/web-ele/openapi-ts.config.mjs",
            "admin/apps/web-ele/src/generated/admin-access/types.gen.ts",
            "scripts/update-admin-access-contract.ps1"
        )
        foreach ($m35T05RequiredPath in $m35T05RequiredPaths) {
            Assert-Condition `
                (Test-Path -LiteralPath $m35T05RequiredPath -PathType Leaf) `
                "M35-04-T05 requires '$m35T05RequiredPath'."
        }

        $m35WebElePackage = Get-Content `
            -LiteralPath "admin/apps/web-ele/package.json" `
            -Raw | ConvertFrom-Json
        $m35OpenApiGenerator = `
            $m35WebElePackage.devDependencies.PSObject.Properties[
                "@hey-api/openapi-ts"]
        Assert-Condition `
            ($null -ne $m35OpenApiGenerator -and
                [string]$m35OpenApiGenerator.Value -eq "catalog:") `
            "M35-04-T05 must reference @hey-api/openapi-ts through the pinned catalog."
        Assert-Condition `
            ([string]$m35WebElePackage.scripts."contract:generate" -eq
                "openapi-ts --file openapi-ts.config.mjs") `
            "M35-04-T05 must use the checked web-ele OpenAPI config."

        $m35AdminWorkspace = Get-Content `
            -LiteralPath "admin/pnpm-workspace.yaml" `
            -Raw
        Assert-Condition `
            ([regex]::IsMatch(
                $m35AdminWorkspace,
                "(?m)^  '@hey-api/openapi-ts': 0\.99\.0$")) `
            "M35-04-T05 workspace catalog must pin @hey-api/openapi-ts exactly to 0.99.0."

        $m35AdminEslintConfig = Get-Content `
            -LiteralPath "admin/eslint.config.mjs" `
            -Raw
        Assert-Condition `
            ($m35AdminEslintConfig.Contains(
                "'apps/web-ele/src/generated/admin-access/types.gen.ts'",
                [StringComparison]::Ordinal) -and
            $m35AdminEslintConfig.Contains(
                "'perfectionist/sort-object-types': 'off'",
                [StringComparison]::Ordinal) -and
            $m35AdminEslintConfig.Contains(
                "'perfectionist/sort-union-types': 'off'",
                [StringComparison]::Ordinal)) `
            "M35-04-T05 must exempt only generated AdminAccess type ordering from hand-authored lint rules."

        $m35GeneratorConfig = Get-Content `
            -LiteralPath "admin/apps/web-ele/openapi-ts.config.mjs" `
            -Raw
        $m35TypesPluginMatches = [regex]::Matches(
            $m35GeneratorConfig,
            '(?m)^\s*plugins\s*:\s*\[\s*[''"]@hey-api/typescript[''"]\s*\]\s*,?\s*$')
        Assert-Condition `
            ($m35TypesPluginMatches.Count -eq 1) `
            "M35-04-T05 must configure only the @hey-api/typescript plugin."
        foreach ($m35RequiredGeneratorSetting in @(
                "NOVAGAME_ADMIN_ACCESS_OPENAPI_INPUT",
                "NOVAGAME_ADMIN_ACCESS_TYPES_OUTPUT",
                "entryFile\s*:\s*false",
                "clean\s*:\s*true",
                'postProcess\s*:\s*\[\s*[''"]oxfmt[''"]\s*\]')) {
            Assert-Condition `
                ([regex]::IsMatch(
                    $m35GeneratorConfig,
                    $m35RequiredGeneratorSetting)) `
                "M35-04-T05 OpenAPI config is missing '$m35RequiredGeneratorSetting'."
        }

        $m35AdminLock = (Get-Content `
            -LiteralPath "admin/pnpm-lock.yaml" `
            -Raw).Replace("`r`n", "`n")
        $m35WebEleImporter = [regex]::Match(
            $m35AdminLock,
            "(?ms)^  apps/web-ele:\n(?<body>.*?)(?=^  \S|\z)")
        Assert-Condition `
            $m35WebEleImporter.Success `
            "M35-04-T05 lockfile is missing the web-ele importer."
        Assert-Condition `
            ([regex]::IsMatch(
                $m35WebEleImporter.Groups["body"].Value,
                "(?m)^      '@hey-api/openapi-ts':\n        specifier: 'catalog:'\n        version: 0\.99\.0\(magicast@0\.5\.3\)\(typescript@6\.0\.3\)$")) `
            "M35-04-T05 must resolve @hey-api/openapi-ts 0.99.0 against TypeScript 6.0.3."
        Assert-Condition `
            ([regex]::IsMatch(
                $m35AdminLock,
                "(?m)^  '@hey-api/openapi-ts@0\.99\.0':$")) `
            "M35-04-T05 lockfile is missing the pinned @hey-api/openapi-ts package resolution."
    }

    $m35UnownedWorkspaceTrackedChanges = @(
        & git diff --name-only --no-ext-diff `
            42d1ffe83770571d8f48cdb58f35bfc591a4bd1c -- `
            admin/pnpm-workspace.yaml |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to audit tracked admin workspace metadata."
    }
    $m35UnownedWorkspaceUntrackedChanges = @(
        & git ls-files --others --exclude-standard -- admin/pnpm-workspace.yaml |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to audit untracked admin workspace metadata."
    }
    $m35UnownedWorkspaceChanges = @(
        $m35UnownedWorkspaceTrackedChanges
        $m35UnownedWorkspaceUntrackedChanges
    )
    if ($m35TaskStatuses["M35-04-T05"] -in @(
            "in_progress", "blocked", "complete")) {
        Assert-Condition `
            ($m35UnownedWorkspaceUntrackedChanges.Count -eq 0) `
            "M35-04-T05 must not replace the tracked admin workspace file."
        $m35WorkspaceDiffStat = @(
            & git diff --numstat --no-ext-diff `
                42d1ffe83770571d8f48cdb58f35bfc591a4bd1c -- `
                admin/pnpm-workspace.yaml |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to audit the M35-04-T05 workspace catalog diff."
        }
        Assert-Condition `
            ($m35WorkspaceDiffStat.Count -eq 1 -and
                $m35WorkspaceDiffStat[0] -eq
                    "1`t0`tadmin/pnpm-workspace.yaml") `
            "M35-04-T05 may add only the pinned OpenAPI generator catalog entry."
    }
    else {
        Assert-Condition `
            ($m35UnownedWorkspaceChanges.Count -eq 0) `
            "M3.5 has no task authorized to change admin/pnpm-workspace.yaml."
    }

    if ($m35TaskStatuses["M35-07-T01"] -in @(
            "in_progress", "blocked", "complete")) {
        $m35GameConfigBrowserSource = Get-Content `
            -LiteralPath "admin/apps/web-ele/e2e/game-config.spec.ts" `
            -Raw
        foreach ($forbiddenM35GameConfigSqlFixture in @(
            "DELETE FROM admin_role_permissions",
            "INSERT INTO admin_role_permissions"
        )) {
            Assert-Condition `
                (-not $m35GameConfigBrowserSource.Contains(
                    $forbiddenM35GameConfigSqlFixture)) `
                "M35-07 GameConfig regression must create permission state through management HTTP."
        }
    }

    $activeM35TaskRows = @(
        $m35TaskRows |
            Where-Object {
                $_.Groups["status"].Value -in @("in_progress", "blocked")
            })
    if ($m35Goal.Status -eq "in_progress") {
        Assert-Condition `
            ($activeM35TaskRows.Count -eq 1 -and
                $activeM35TaskRows[0].Groups["status"].Value -eq
                    "in_progress") `
            "Active M3.5 must have exactly one in-progress task."
        Assert-Condition `
            ($activeM35TaskRows[0].Groups["stage"].Value -eq
                $m35Goal.ActiveStage) `
            "The active M3.5 task must belong to the active M3.5 stage."
    }
    elseif ($m35Goal.Status -eq "blocked") {
        Assert-Condition `
            ($activeM35TaskRows.Count -eq 1 -and
                $activeM35TaskRows[0].Groups["status"].Value -eq "blocked") `
            "Blocked M3.5 must have exactly one blocked task."
        Assert-Condition `
            ($activeM35TaskRows[0].Groups["stage"].Value -eq
                $m35Goal.ActiveStage) `
            "The blocked M3.5 task must belong to the active M3.5 stage."
    }
    elseif ($m35Goal.Status -eq "complete") {
        $incompleteM35TaskRows = @(
            $m35TaskRows |
                Where-Object { $_.Groups["status"].Value -ne "complete" })
        Assert-Condition `
            ($incompleteM35TaskRows.Count -eq 0) `
            "Completed M3.5 must mark every registered task complete."
    }

    foreach ($m35StageId in $m35Goal.StageStatuses.Keys) {
        $m35StageTaskRows = @(
            $m35TaskRows |
                Where-Object {
                    $_.Groups["stage"].Value -eq $m35StageId
                })
        Assert-Condition `
            ($m35StageTaskRows.Count -gt 0) `
            "M3.5 stage '$m35StageId' must contain at least one task."

        $m35StageStatus = $m35Goal.StageStatuses[$m35StageId]
        if ($m35StageStatus -eq "complete") {
            foreach ($m35StageTaskRow in $m35StageTaskRows) {
                Assert-Condition `
                    ($m35StageTaskRow.Groups["status"].Value -eq "complete") `
                    "Completed M3.5 stage '$m35StageId' requires every task to be complete."
            }
            continue
        }

        if ($m35StageStatus -eq "pending") {
            foreach ($m35StageTaskRow in $m35StageTaskRows) {
                Assert-Condition `
                    ($m35StageTaskRow.Groups["status"].Value -eq "pending") `
                    "Pending M3.5 stage '$m35StageId' requires every task to be pending."
            }
            continue
        }

        Assert-Condition `
            ($m35StageId -eq $m35Goal.ActiveStage) `
            "Only the active M3.5 stage may be in progress or blocked."
        $activeM35StageTaskIndexes = @(
            for ($taskIndex = 0;
                $taskIndex -lt $m35StageTaskRows.Count;
                $taskIndex++) {
                if ($m35StageTaskRows[$taskIndex].Groups["status"].Value -in @(
                        "in_progress", "blocked")) {
                    $taskIndex
                }
            })
        Assert-Condition `
            ($activeM35StageTaskIndexes.Count -eq 1) `
            "Active M3.5 stage '$m35StageId' must contain exactly one current task."
        $activeM35StageTaskIndex = $activeM35StageTaskIndexes[0]
        for ($taskIndex = 0;
            $taskIndex -lt $m35StageTaskRows.Count;
            $taskIndex++) {
            $expectedM35TaskStatus = if ($taskIndex -lt $activeM35StageTaskIndex) {
                "complete"
            }
            elseif ($taskIndex -eq $activeM35StageTaskIndex) {
                $m35StageStatus
            }
            else {
                "pending"
            }
            Assert-Condition `
                ($m35StageTaskRows[$taskIndex].Groups["status"].Value -eq
                    $expectedM35TaskStatus) `
                "M3.5 stage '$m35StageId' tasks must advance in registered order."
        }
    }

    if ($m35Goal.ExecutionAuthorized) {
        $currentM35TaskId = $activeM35TaskRows[0].Groups["id"].Value
        $m35ActivationGateMatch = [regex]::Match(
            $m35Goal.Content,
            '(?ms)^## Activation Gate\r?\n(?<body>.*?)(?=^##\s+|\z)')
        Assert-Condition `
            $m35ActivationGateMatch.Success `
            "Active M3.5 is missing its Activation Gate section."
        $m35ActivationGate = $m35ActivationGateMatch.Groups["body"].Value
        foreach ($requiredM35ActivationText in @(
            $m35Goal.ActiveStage,
            $currentM35TaskId
        )) {
            Assert-Condition `
                $m35ActivationGate.Contains($requiredM35ActivationText) `
                "M3.5 Activation Gate is missing active state '$requiredM35ActivationText'."
        }
        foreach ($currentM35StatusPath in @(
            "AGENTS.md",
            "README.md",
            "docs/README.md",
            "docs/roadmap.md"
        )) {
            $currentM35StatusContent = Get-Content `
                -LiteralPath $currentM35StatusPath `
                -Raw
            foreach ($requiredM35CurrentText in @(
                "administrator-management",
                $m35Goal.ActiveStage,
                $currentM35TaskId
            )) {
                Assert-Condition `
                    $currentM35StatusContent.Contains($requiredM35CurrentText) `
                    "$currentM35StatusPath is missing active M3.5 state '$requiredM35CurrentText'."
            }
            Assert-Condition `
                (-not $currentM35StatusContent.Contains(
                    "No repository milestone is active.",
                    [StringComparison]::OrdinalIgnoreCase)) `
                "$currentM35StatusPath contains a stale no-active-milestone statement."
        }

        $m4SectionMatch = [regex]::Match(
            $roadmap,
            '(?ms)^##\s+M4\s+-[^\r\n]*\r?\n(?<body>.*?)(?=^##\s+|\z)')
        Assert-Condition `
            ($m4SectionMatch.Success -and
                [regex]::IsMatch(
                    $m4SectionMatch.Groups["body"].Value,
                    '(?m)^\*\*Status:\*\*[ \t]*Unscheduled[ \t]*$')) `
            "M4 must remain Unscheduled while M3.5 is active."
    }

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

        $m208HistoricalClosureSentence =
            "no repository milestone remains active"
        Assert-Condition `
            $m2Goal.Content.Contains($m208HistoricalClosureSentence) `
            "Completed M2 must retain its historical no-active-milestone closure state."
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
            Assert-Condition `
                ([regex]::IsMatch(
                    $currentStatusContent,
                    '(?is)(?:\bM2\b.{0,120}\bcomplet(?:e|ed)\b|\bcomplet(?:e|ed)\b.{0,120}\bM2\b)')) `
                "$currentStatusPath must identify M2 as complete."
            foreach ($requiredCurrentStatusText in @(
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

        foreach ($unscheduledMilestone in @("M4")) {
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
                "$unscheduledMilestone must remain Unscheduled while M3 is active."
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

    foreach ($requiredM3Metadata in @(
        'selected_capability: player-equipment-grant',
        'capability_owner: swordmaster',
        'capability_approval_status: approved',
        'capability_approval_reference: user-approval-2026-08-10-player-equipment-grant',
        'baseline_commit: ba83e4af0d494da4176ee922c2caaf24f5fce905',
        'extraction_authorized: false'
    )) {
        Assert-Condition `
            $m3Goal.Metadata.Contains($requiredM3Metadata) `
            "M3 goal metadata is missing '$requiredM3Metadata'."
    }
    Assert-Condition `
        ($m2Goal.Status -eq "complete") `
        "M3 requires the completed M2 baseline."

    $m3DecisionRecordMatch = [regex]::Match(
        $m3Goal.Content,
        '(?ms)^## Capability Decision Record\r?\n(?<body>.*?)(?=^##\s+|\z)')
    Assert-Condition `
        $m3DecisionRecordMatch.Success `
        "M3 goal is missing the Capability Decision Record."
    $m3DecisionRecord = $m3DecisionRecordMatch.Groups["body"].Value
    $expectedM3DecisionFields = @(
        "Selection status",
        "Requirement source",
        "Operator outcome",
        "Owning domain",
        "Authorization",
        "Grant invariants",
        "Rollback invariants",
        "Idempotency and concurrency",
        "Audit and time",
        "HTTP surface",
        "Admin surface",
        "Stable failures",
        "Verification",
        "Explicit non-goals",
        "Approval evidence"
    )
    $actualM3DecisionFields = @(
        [regex]::Matches(
            $m3DecisionRecord,
            '(?m)^\|[ \t]*(?<field>[^|\r\n]+?)[ \t]*\|[^\r\n]*$') |
            ForEach-Object { $_.Groups["field"].Value.Trim() } |
            Where-Object { $_ -notin @("Field", "---") })
    Assert-Condition `
        (($actualM3DecisionFields -join ',') -eq
            ($expectedM3DecisionFields -join ',')) `
        "M3 Capability Decision Record must contain the exact ordered fields."
    foreach ($requiredM3DecisionText in @(
        'SwordMaster.PlayerAssets.EquipmentGrants.Manage',
        'GET /api/swordmaster/operations/player-assets/{playerId}/equipment-grants',
        'POST /api/swordmaster/operations/player-assets/{playerId}/equipment-grants',
        'POST /api/swordmaster/operations/player-assets/{playerId}/equipment-grants/{grantId}/rollback',
        '`samples/SwordMaster/admin/`',
        'It never auto-unequips',
        'No extraction'
    )) {
        Assert-Condition `
            $m3Goal.Content.Contains($requiredM3DecisionText) `
            "M3 approved boundary is missing '$requiredM3DecisionText'."
    }

    $m302DesignPath = "samples/SwordMaster/docs/player-equipment-grant.md"
    if ($m3Goal.StageStatuses["M3-02"] -in @(
            "in_progress", "blocked", "complete")) {
        Assert-Condition `
            (Test-Path -LiteralPath $m302DesignPath -PathType Leaf) `
            "M3-02 requires the frozen Player Equipment Grant design document."
        $m302Design = Get-Content -LiteralPath $m302DesignPath -Raw
        foreach ($requiredM302Section in @(
            "## Approval And Scope",
            "## Physical Boundary",
            "## Domain Ownership",
            "## Application Contract",
            "## Authorization",
            "## Configuration Boundary",
            "## Idempotency And Transaction Order",
            "## Rollback Semantics",
            "## HTTP Contract",
            "## Stable Failures",
            "## Persistence Contract",
            "## Audit And Observability",
            "## Administrator Application Boundary",
            "## Generated Administrator Contract",
            "## Planned Source Files",
            "## Focused Test Contract",
            "## Exact Verification Commands",
            "## Non-Goals",
            "## M3-02 Completion Contract"
        )) {
            Assert-Condition `
                $m302Design.Contains($requiredM302Section) `
                "M3-02 design is missing section '$requiredM302Section'."
        }
        foreach ($requiredM302Text in @(
            'The capability remains entirely in the existing',
            'SwordMaster.PlayerAssets.EquipmentGrants.Manage',
            'Active -> RolledBack',
            'PlayerId + CommandKind + IdempotencyKey',
            'The lock order is always profile then grant.',
            'M3 introduces no distributed transaction.',
            'GET /api/swordmaster/operations/player-assets/{playerId}/equipment-grants?page=1&pageSize=20',
            'POST /api/swordmaster/operations/player-assets/{playerId}/equipment-grants',
            'POST /api/swordmaster/operations/player-assets/{playerId}/equipment-grants/{grantId}/rollback',
            'sm_player_equipment_grants',
            'UNIQUE (PlayerId, GrantIdempotencyKey)',
            'UNIQUE (PlayerId, RollbackIdempotencyKey)',
            'samples/SwordMaster/admin/contracts/player-equipment-grants.openapi.json',
            'scripts/update-swordmaster-player-equipment-grant-contract.ps1',
            'samples/SwordMaster/contracts/player-assets.openapi.json',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run typecheck',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrant',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrant',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantMySqlApplicationTests',
            'It does not create a NovaGame feature',
            'It does not create another token'
        )) {
            Assert-Condition `
                $m302Design.Contains($requiredM302Text) `
                "M3-02 design is missing frozen evidence '$requiredM302Text'."
        }
        foreach ($unresolvedM302Text in @(
            "TBD",
            "TODO",
            "to be decided",
            "Not selected",
            "pending decision"
        )) {
            Assert-Condition `
                (-not $m302Design.Contains(
                    $unresolvedM302Text,
                    [StringComparison]::OrdinalIgnoreCase)) `
                "M3-02 design contains unresolved text '$unresolvedM302Text'."
        }
    }

    $m3TaskRows = [regex]::Matches(
        $m3Goal.Content,
        '(?m)^\|[ \t]*(?<id>M3-\d{2}-T\d{2})[ \t]*\|[ \t]*(?<stage>M3-\d{2})[ \t]*\|[^\r\n]*\|[ \t]*(?<status>pending|in_progress|blocked|complete)[ \t]*\|[ \t]*$')
    Assert-Condition `
        ($m3TaskRows.Count -gt 0) `
        "M3 Active Task Register must contain executable tasks."
    $duplicateM3TaskIds = @(
        $m3TaskRows |
            Group-Object { $_.Groups["id"].Value } |
            Where-Object Count -gt 1)
    Assert-Condition `
        ($duplicateM3TaskIds.Count -eq 0) `
        "M3 Active Task Register contains duplicate task identifiers."
    $m3TaskStatuses = [ordered]@{}
    foreach ($m3TaskRow in $m3TaskRows) {
        $m3TaskStatuses[$m3TaskRow.Groups["id"].Value] =
            $m3TaskRow.Groups["status"].Value
    }
    $m307ReuseDecisionStatusMatch = [regex]::Match(
        $m3Goal.Metadata,
        '(?m)^reuse_decision_status:[ \t]*(?<value>pending|recorded)[ \t]*$')
    $m307ValidatedCandidateMatch = [regex]::Match(
        $m3Goal.Metadata,
        '(?m)^validated_extraction_candidate:[ \t]*(?<value>none|[a-z0-9][a-z0-9._-]*)[ \t]*$')
    $m307ValidatedCandidateReferenceMatch = [regex]::Match(
        $m3Goal.Metadata,
        '(?m)^validated_extraction_candidate_reference:[ \t]*(?<value>[^\r\n]+?)[ \t]*$')
    Assert-Condition `
        $m307ReuseDecisionStatusMatch.Success `
        "M3 goal metadata has an invalid reuse_decision_status."
    Assert-Condition `
        $m307ValidatedCandidateMatch.Success `
        "M3 goal metadata has an invalid validated_extraction_candidate."
    Assert-Condition `
        $m307ValidatedCandidateReferenceMatch.Success `
        "M3 goal metadata is missing validated_extraction_candidate_reference."
    $m307ReuseDecisionStatus =
        $m307ReuseDecisionStatusMatch.Groups["value"].Value
    $m307ValidatedCandidate =
        $m307ValidatedCandidateMatch.Groups["value"].Value
    $m307ValidatedCandidateReference =
        $m307ValidatedCandidateReferenceMatch.Groups["value"].Value
    $m307T03TaskStatus = $m3TaskStatuses["M3-07-T03"]

    if ($m307T03TaskStatus -eq "pending") {
        Assert-Condition `
            ($m307ReuseDecisionStatus -eq "pending" -and
                $m307ValidatedCandidate -eq "none" -and
                $m307ValidatedCandidateReference -eq "none") `
            "Pending M3-07-T03 must retain pending/none/none decision metadata."
        Assert-Condition `
            (-not $m3Goal.Content.Contains(
                "### M3-07-T03 Boundary Decision")) `
            "Pending M3-07-T03 must not contain a boundary decision record."
    }
    elseif ($m307T03TaskStatus -in @("in_progress", "blocked")) {
        Assert-Condition `
            ($m307ReuseDecisionStatus -in @("pending", "recorded")) `
            "Active M3-07-T03 decision metadata must be pending or recorded."
    }
    else {
        Assert-Condition `
            ($m307ReuseDecisionStatus -eq "recorded") `
            "Completed M3-07-T03 must have recorded decision metadata."
    }

    if ($m307ReuseDecisionStatus -eq "pending") {
        Assert-Condition `
            ($m307ValidatedCandidate -eq "none" -and
                $m307ValidatedCandidateReference -eq "none") `
            "Pending M3 reuse decision must retain candidate/reference none."
    }
    else {
        Assert-Condition `
            ($m307ValidatedCandidate -eq
                "request-audit-context-consumer-contract") `
            "Recorded M3 reuse decision must name the evidence-backed request-audit-context-consumer-contract candidate."
        Assert-Condition `
            ($m307ValidatedCandidate -ne "player-equipment-grant") `
            "M3 may not validate the complete selected capability as an extraction candidate."
        Assert-Condition `
            ($m307ValidatedCandidateReference -eq
                "docs/goals/m3.md#m3-07-t03-boundary-decision") `
            "Recorded M3 reuse decision must reference its exact T03 heading."
        Assert-Condition `
            $m3Goal.Content.Contains(
                "### M3-07-T03 Boundary Decision") `
            "Recorded M3 reuse-decision reference anchor does not resolve."
    }
    $activeM3TaskRows = @(
        $m3TaskRows |
            Where-Object {
                $_.Groups["status"].Value -in @("in_progress", "blocked")
            })
    if ($m3Goal.Status -eq "in_progress") {
        Assert-Condition `
            ($activeM3TaskRows.Count -eq 1 -and
                $activeM3TaskRows[0].Groups["status"].Value -eq
                    "in_progress") `
            "Active M3 must have exactly one in-progress task."
        Assert-Condition `
            ($activeM3TaskRows[0].Groups["stage"].Value -eq
                $m3Goal.ActiveStage) `
            "The active M3 task must belong to the active M3 stage."
    }
    elseif ($m3Goal.Status -eq "blocked") {
        Assert-Condition `
            ($activeM3TaskRows.Count -eq 1 -and
                $activeM3TaskRows[0].Groups["status"].Value -eq "blocked") `
            "Blocked M3 must have exactly one blocked task."
        Assert-Condition `
            ($activeM3TaskRows[0].Groups["stage"].Value -eq
                $m3Goal.ActiveStage) `
            "The blocked M3 task must belong to the active M3 stage."
    }
    elseif ($m3Goal.Status -eq "complete") {
        $incompleteM3TaskRows = @(
            $m3TaskRows |
                Where-Object { $_.Groups["status"].Value -ne "complete" })
        Assert-Condition `
            ($incompleteM3TaskRows.Count -eq 0) `
            "Completed M3 must mark every registered task complete."
    }

    foreach ($m3StageId in $m3Goal.StageStatuses.Keys) {
        $m3StageTaskRows = @(
            $m3TaskRows |
                Where-Object {
                    $_.Groups["stage"].Value -eq $m3StageId
                })
        Assert-Condition `
            ($m3StageTaskRows.Count -gt 0) `
            "M3 stage '$m3StageId' must contain at least one task."

        $m3StageStatus = $m3Goal.StageStatuses[$m3StageId]
        if ($m3StageStatus -eq "complete") {
            foreach ($m3StageTaskRow in $m3StageTaskRows) {
                Assert-Condition `
                    ($m3StageTaskRow.Groups["status"].Value -eq "complete") `
                    "Completed M3 stage '$m3StageId' requires every task to be complete."
            }
            continue
        }

        if ($m3StageStatus -eq "pending") {
            foreach ($m3StageTaskRow in $m3StageTaskRows) {
                Assert-Condition `
                    ($m3StageTaskRow.Groups["status"].Value -eq "pending") `
                    "Pending M3 stage '$m3StageId' requires every task to be pending."
            }
            continue
        }

        Assert-Condition `
            ($m3StageId -eq $m3Goal.ActiveStage) `
            "Only the active M3 stage may be in progress or blocked."
        $activeM3StageTaskIndexes = @(
            for ($taskIndex = 0; $taskIndex -lt $m3StageTaskRows.Count; $taskIndex++) {
                if ($m3StageTaskRows[$taskIndex].Groups["status"].Value -in @(
                        "in_progress", "blocked")) {
                    $taskIndex
                }
            })
        Assert-Condition `
            ($activeM3StageTaskIndexes.Count -eq 1) `
            "Active M3 stage '$m3StageId' must contain exactly one current task."
        $activeM3StageTaskIndex = $activeM3StageTaskIndexes[0]
        for ($taskIndex = 0; $taskIndex -lt $m3StageTaskRows.Count; $taskIndex++) {
            $expectedTaskStatus = if ($taskIndex -lt $activeM3StageTaskIndex) {
                "complete"
            }
            elseif ($taskIndex -eq $activeM3StageTaskIndex) {
                $m3StageStatus
            }
            else {
                "pending"
            }
            Assert-Condition `
                ($m3StageTaskRows[$taskIndex].Groups["status"].Value -eq
                    $expectedTaskStatus) `
                "M3 stage '$m3StageId' tasks must advance in registered order."
        }
    }

    if ($m3TaskStatuses["M3-07-T01"] -in @(
            "in_progress", "blocked", "complete")) {
        $m307BaselineMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^### Completed M3-06 Baseline Record\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
        Assert-Condition `
            $m307BaselineMatch.Success `
            "M3-07-T01 requires the completed M3-06 baseline record."
        $m307Baseline = $m307BaselineMatch.Groups["body"].Value
        $expectedM307BaselineFields = @(
            "Base commit",
            "Capture time",
            "Metadata",
            "Tracked patch",
            "Tracked manifest",
            "Tracked byte overrides",
            "Untracked manifest",
            "Untracked list",
            "Untracked archive",
            "Worktree status",
            "Excluded untracked",
            "NovaCore"
        )
        $actualM307BaselineFields = @(
            [regex]::Matches(
                $m307Baseline,
                '(?m)^\|[ \t]*(?<field>[^|\r\n]+?)[ \t]*\|[^\r\n]*$') |
                ForEach-Object { $_.Groups["field"].Value.Trim() } |
                Where-Object { $_ -notin @("Field", "---") })
        Assert-Condition `
            (($actualM307BaselineFields -join ',') -eq
                ($expectedM307BaselineFields -join ',')) `
            "M3 completed-M3-06 baseline record must contain the exact ordered fields."
        foreach ($requiredM307BaselineText in @(
            'ba83e4af0d494da4176ee922c2caaf24f5fce905',
            '2026-08-12T14:04:39.9656127+00:00',
            '.local/m3-07-baseline/baseline.json',
            '5c6c67213c76d34efc2feb7ee26ec86eb9a157f7f65552124c850437cf937ebc',
            '.local/m3-07-baseline/tracked-full-index.patch',
            '9f39e8954aaa9d260064c36bc183e36735dbe60ddce0665d265b97b77346d0e1',
            '7b38eca28b06b5580f35573dd58d8c896bd92160ded12272c11665df33f1dbfa',
            '.local/m3-07-baseline/tracked-byte-overrides.tar.gz',
            'dfc25d5607c7a18afd0bbd1cc17968bee717b8f04bfdc4a45491fcfc2d3522ab',
            'ebf48d48a099bd4099a8106df687537a0170c756ccf04cb797986200ee581a96',
            'cde5086c6d543365e356924b4f2b812684ef53743be951f2013464784a82eb0f',
            '4c5b044a089337012bda43a0229d42d87ac8f6c668b63d2faed430ee2bd17714',
            '.local/m3-07-baseline/untracked-files.txt',
            '7b29b8578ff461194c8f5f3af31efc49850ed633056b3e38d353cdabbd484417',
            '.local/m3-07-baseline/untracked-files.tar.gz',
            'eb2298c9c793548c444af32c58fbbcfd19629992f45d1f10e3a22e23b6519817',
            '.local/m3-07-baseline/worktree-status.txt',
            '6b90f59643ed7690ecf3f0551738943ad49094ae8602f90e13261bbc57c3cc6c',
            '.local/m3-07-baseline/excluded-untracked.txt',
            'd7fa4096a3bd5e8f5144cc21810c99f98a82b3e7f676872e0b6317334f17ecf6',
            '89951797a15ac7f78e2e2979735a29377eace45f27154120d970dbf3b253669d',
            'ef8db55173ff8f0286bb06abe753861c8a7d2d80',
            '25 tracked modifications',
            '102 selected',
            '127 Git-visible paths',
            '26 files in `.local/m3-07-baseline/tracked-byte-overrides.tar.gz`',
            'git diff --binary --full-index --no-ext-diff HEAD -- .',
            'git apply --check --reverse --binary .local/m3-07-baseline/tracked-full-index.patch',
            'lefthook.yml',
            'explicitly excluded from M3 reproduction',
            'all 1337 tracked file hashes',
            'all 102 untracked file hashes',
            'Normal Documentation and Repository checks intentionally validate this durable',
            'without depending on ignored local artifacts'
        )) {
            Assert-Condition `
                $m307Baseline.Contains($requiredM307BaselineText) `
                "M3 completed-M3-06 baseline record is missing '$requiredM307BaselineText'."
        }

        if ($m3TaskStatuses["M3-07-T01"] -eq "complete") {
            Assert-Condition `
                ([regex]::IsMatch(
                    $m3Goal.Content,
                    '(?m)^\|[ \t]*2026-08-12[ \t]*\|[ \t]*M3-07-T01[ \t]*\|[ \t]*completed-M3-06 baseline integrity verification and fresh-clone reconstruction[ \t]*\|[ \t]*Passed:')) `
                "Completed M3-07-T01 must record passing baseline integrity verification."
        }
    }

    if ($m3TaskStatuses["M3-07-T02"] -in @(
            "in_progress", "blocked", "complete")) {
        $m307T02EvidencePath =
            "samples/SwordMaster/docs/player-equipment-grant-m3-07-reuse-evidence.md"
        Assert-Condition `
            (Test-Path -LiteralPath $m307T02EvidencePath -PathType Leaf) `
            "M3-07-T02 requires its SwordMaster reuse-pressure evidence record."
        $m307T02Evidence = Get-Content -LiteralPath $m307T02EvidencePath -Raw

        $expectedM307T02Headings = @(
            "Scope And Authority",
            "Evidence Method",
            "Frozen Baseline And Search Scope",
            "Concrete Capability Behavior And Test Evidence",
            "Observed Duplication And Variation",
            "Absent Or Insufficient Evidence",
            "Game Policy And Ownership Retained",
            "T03 Handoff",
            "Non-Decisions",
            "Verification"
        )
        $actualM307T02Headings = @(
            [regex]::Matches($m307T02Evidence, '(?m)^## (?<heading>[^\r\n]+)$') |
                ForEach-Object { $_.Groups["heading"].Value })
        Assert-Condition `
            (($actualM307T02Headings -join ',') -eq
                ($expectedM307T02Headings -join ',')) `
            "M3-07-T02 evidence must contain the exact ordered evidence-only sections."

        foreach ($requiredM307T02Text in @(
            "# SwordMaster Player Equipment Grant M3-07 Reuse Evidence",
            "**Task:** M3-07-T02 - Audit Concrete Reuse Pressure",
            '**Capability:** `player-equipment-grant`',
            "**Status:** T02 evidence audit complete; frozen before the M3-07-T03 boundary decision",
            "5c6c67213c76d34efc2feb7ee26ec86eb9a157f7f65552124c850437cf937ebc",
            "At T02 completion this record did not choose a candidate, evaluate sufficiency, select a NovaGame",
            "owner, or authorize extraction.",
            "M3-07-T03 was the only task authorized to record one evidence-backed",
            'candidate or `none`; its later decision is recorded in the',
            "[M3 Goal](../../../docs/goals/m3.md#m3-07-t03-boundary-decision)",
            "Each observation is factual input for the T03 boundary decision.",
            "It does not establish candidate",
            "identity, independent usefulness, ownership, extraction authorization, or a public contract.",
            "The current repository has one concrete game sample",
            "25 tracked modifications and 102 selected untracked files across 127",
            "zero M3 feature",
            "The complete equipment-grant capability remains SwordMaster-owned.",
            "T02 supplied facts and questions only. It provided no answer to them.",
            "No candidate identifier was selected or proposed by T02.",
            "No NovaGame owner, layer, package, module, contract, API, adapter, or extension point was selected",
            "by T02.",
            'M3-07-T03 was the only task allowed to record one evidence-backed candidate or `none`;',
            "evidence file remains the frozen T02 input rather than the later decision record.",
            "router/routes/core.ts",
            "same-path hash comparison found 29 overlapping source files: 12 identical and 17 different, with 21 additional SwordMaster-only files",
            "There is no second game backend",
            'Generic `admin/apps/web-ele` contains no SwordMaster grant route',
            "Extraction remains unauthorized."
        )) {
            Assert-Condition `
                $m307T02Evidence.Contains($requiredM307T02Text) `
                "M3-07-T02 reuse-pressure evidence is missing '$requiredM307T02Text'."
        }

        $expectedM307BehaviorIds = @(
            "M307-BE-01", "M307-BE-02", "M307-BE-03",
            "M307-BE-04", "M307-BE-05", "M307-BE-06")
        $actualM307BehaviorRows = @(
            [regex]::Matches(
                $m307T02Evidence,
                '(?m)^\|[ \t]*(?<id>M307-BE-\d{2})[ \t]*\|(?<behavior>[^|]*)\|(?<implementation>[^|]*)\|(?<evidence>[^|]*)\|[ \t]*$'))
        $actualM307BehaviorIds = @(
            $actualM307BehaviorRows |
                ForEach-Object { $_.Groups["id"].Value })
        Assert-Condition `
            (($actualM307BehaviorIds -join ',') -eq
                ($expectedM307BehaviorIds -join ',')) `
            "M3-07-T02 must contain exactly the six ordered capability-behavior rows."
        foreach ($m307BehaviorRow in $actualM307BehaviorRows) {
            $m307BehaviorId = $m307BehaviorRow.Groups["id"].Value
            Assert-Condition `
                ([regex]::IsMatch(
                    $m307BehaviorRow.Groups["implementation"].Value,
                    '\[[^\]]+\]\([^)]+/src/[^)]+\)')) `
                "M3-07-T02 behavior row '$m307BehaviorId' must link authoritative source implementation."
            Assert-Condition `
                ([regex]::IsMatch(
                    $m307BehaviorRow.Groups["evidence"].Value,
                    '\[[^\]]+\]\((?:[^)]*/tests/[^)]*|[^)]*\.test\.[^)]*|[^)]*\.spec\.[^)]*)\)')) `
                "M3-07-T02 behavior row '$m307BehaviorId' must link focused test evidence."
        }

        $expectedM307ReusePressureIds = @(
            "M307-RP-01", "M307-RP-02", "M307-RP-03", "M307-RP-04",
            "M307-RP-05", "M307-RP-06", "M307-RP-07", "M307-RP-08",
            "M307-RP-09", "M307-RP-10", "M307-RP-11")
        $actualM307ReusePressureRows = @(
            [regex]::Matches(
                $m307T02Evidence,
                '(?m)^\|[ \t]*(?<id>M307-RP-\d{2})[ \t]*\|(?<row>[^\r\n]*)$'))
        $actualM307ReusePressureIds = @(
            $actualM307ReusePressureRows |
                ForEach-Object { $_.Groups["id"].Value })
        Assert-Condition `
            (($actualM307ReusePressureIds -join ',') -eq
                ($expectedM307ReusePressureIds -join ',')) `
            "M3-07-T02 must contain exactly the eleven ordered duplication-and-variation rows."
        foreach ($m307ReusePressureRow in $actualM307ReusePressureRows) {
            $m307ReusePressureId =
                $m307ReusePressureRow.Groups["id"].Value
            $m307ReusePressureEvidence =
                $m307ReusePressureRow.Groups["row"].Value
            Assert-Condition `
                ([regex]::IsMatch(
                    $m307ReusePressureEvidence,
                    '\[[^\]]+\]\([^)]+/src/[^)]+\)')) `
                "M3-07-T02 reuse-pressure row '$m307ReusePressureId' must link current source implementation."
            Assert-Condition `
                ([regex]::IsMatch(
                    $m307ReusePressureEvidence,
                    '\[[^\]]+\]\((?:[^)]*/tests/[^)]*|[^)]*\.test\.[^)]*|[^)]*\.spec\.[^)]*|[^)]*scripts/verify\.ps1)\)')) `
                "M3-07-T02 reuse-pressure row '$m307ReusePressureId' must link current test or executable verifier evidence."
        }

        # M3 T02 recorded this comparison at the completed-M3 baseline. Read
        # Git objects from that snapshot so later authorized generic-admin work
        # does not retroactively change the evidence record.
        $m307T02EvidenceSnapshotCommit =
            "42d1ffe83770571d8f48cdb58f35bfc591a4bd1c"
        $m307T02EvidenceSnapshot = @(
            & git ls-tree -r $m307T02EvidenceSnapshotCommit -- `
                "admin/apps/web-ele/src" `
                "samples/SwordMaster/admin/src"
        )
        Assert-Condition `
            ($LASTEXITCODE -eq 0) `
            "Unable to read the completed-M3 admin evidence snapshot."

        $m307GenericAdminPrefix = "admin/apps/web-ele/src/"
        $m307SwordMasterAdminPrefix = "samples/SwordMaster/admin/src/"
        $m307GenericAdminObjects = @{}
        $m307SwordMasterAdminObjects = @{}
        foreach ($m307T02EvidenceSnapshotLine in $m307T02EvidenceSnapshot) {
            $m307T02EvidenceSnapshotMatch = [regex]::Match(
                [string]$m307T02EvidenceSnapshotLine,
                '^[0-9]{6} blob (?<object>[0-9a-f]{40})\t(?<path>.+)$')
            Assert-Condition `
                $m307T02EvidenceSnapshotMatch.Success `
                "Completed-M3 admin evidence snapshot contains an invalid tree entry."

            $m307T02EvidencePath =
                $m307T02EvidenceSnapshotMatch.Groups["path"].Value
            $m307T02EvidenceObject =
                $m307T02EvidenceSnapshotMatch.Groups["object"].Value
            if ($m307T02EvidencePath.StartsWith(
                    $m307GenericAdminPrefix,
                    [StringComparison]::Ordinal)) {
                $m307GenericAdminObjects[$m307T02EvidencePath] =
                    $m307T02EvidenceObject
            }
            elseif ($m307T02EvidencePath.StartsWith(
                    $m307SwordMasterAdminPrefix,
                    [StringComparison]::Ordinal)) {
                $m307SwordMasterAdminObjects[$m307T02EvidencePath] =
                    $m307T02EvidenceObject
            }
            else {
                throw "Completed-M3 admin evidence snapshot contains an out-of-scope path '$m307T02EvidencePath'."
            }
        }
        Assert-Condition `
            ($m307GenericAdminObjects.Count -gt 0 -and
                $m307SwordMasterAdminObjects.Count -gt 0) `
            "Completed-M3 admin evidence snapshot is missing a source root."

        $m307AdminOverlapCount = 0
        $m307AdminIdenticalCount = 0
        $m307AdminDifferentCount = 0
        $m307SwordMasterOnlyCount = 0
        foreach ($m307SwordMasterAdminObject in
            $m307SwordMasterAdminObjects.GetEnumerator()) {
            $m307AdminRelativePath = $m307SwordMasterAdminObject.Key.Substring(
                $m307SwordMasterAdminPrefix.Length)
            $m307GenericAdminPath =
                $m307GenericAdminPrefix + $m307AdminRelativePath
            if (-not $m307GenericAdminObjects.ContainsKey($m307GenericAdminPath)) {
                $m307SwordMasterOnlyCount++
                continue
            }

            $m307AdminOverlapCount++
            if ($m307SwordMasterAdminObject.Value -eq
                $m307GenericAdminObjects[$m307GenericAdminPath]) {
                $m307AdminIdenticalCount++
            }
            else {
                $m307AdminDifferentCount++
            }
        }
        Assert-Condition `
            ($m307AdminOverlapCount -eq 29 -and
                $m307AdminIdenticalCount -eq 12 -and
                $m307AdminDifferentCount -eq 17 -and
                $m307SwordMasterOnlyCount -eq 21) `
            "M3-07-T02 admin evidence is stale: expected overlap/identical/different/SwordMaster-only counts 29/12/17/21, found $m307AdminOverlapCount/$m307AdminIdenticalCount/$m307AdminDifferentCount/$m307SwordMasterOnlyCount."

        $m307UnpinnedIdenticalAdminPath =
            "router/routes/core.ts"
        $m307UnpinnedSwordMasterAdminPath =
            $m307SwordMasterAdminPrefix + $m307UnpinnedIdenticalAdminPath
        $m307UnpinnedGenericAdminPath =
            $m307GenericAdminPrefix + $m307UnpinnedIdenticalAdminPath
        Assert-Condition `
            ($m307SwordMasterAdminObjects.ContainsKey(
                    $m307UnpinnedSwordMasterAdminPath) -and
                $m307GenericAdminObjects.ContainsKey(
                    $m307UnpinnedGenericAdminPath) -and
                ($m307SwordMasterAdminObjects[
                    $m307UnpinnedSwordMasterAdminPath] -eq
                    $m307GenericAdminObjects[
                        $m307UnpinnedGenericAdminPath])) `
            "M3-07-T02 must retain evidence that '$m307UnpinnedIdenticalAdminPath' is the twelfth identical admin file outside the eleven-copy equality guard."

        foreach ($forbiddenM307T02Pattern in @(
            '(?im)^##[ \t]+T02 Candidate For Decision[ \t]*$',
            '(?im)^##[ \t]+T03 Decision[ \t]*$',
            '(?im)^#{1,6}[ \t]+(?:Selected|Proposed|Recommended|Validated)[ \t]+(?:Extraction[ \t]+)?Candidate\b',
            '(?im)^#{1,6}[ \t]+[^\r\n]*(?:Candidate Proposal|Mechanism Decision|Extraction Recommendation)[^\r\n]*$',
            '(?im)^[ \t]*The candidate is[ \t]+',
            '(?im)^[ \t]*(?:Selected|Proposed|Recommended|Validated)[ \t]+(?:Extraction[ \t]+)?Candidate[ \t]*(?::|\|)',
            '(?im)^[ \t]*(?:This record|T02|We)[ \t]+(?:selects?|proposes?|recommends?|validates?)[ \t]+(?:an?[ \t]+)?(?:extraction[ \t]+)?candidate\b',
            '(?im)^[ \t]*NovaGame[ \t]+(?:should|could|may|must|will)[ \t]+(?:own|provide|extract)[ \t]+',
            '(?im)^[ \t]*Evidence is sufficient[ \t]+',
            '(?im)^[ \t]*(?:The[ \t]+)?Evidence[ \t]+(?:is|was)[ \t]+sufficient\b',
            '(?im)^[ \t]*(?:The[ \t]+|Current[ \t]+)?Evidence[ \t]+(?:supports?|proves?|establishes?)[^\r\n]*\bextraction\b',
            '(?im)^[ \t]*(?:Extraction|Migration|Promotion)[ \t]+(?:is|was|should be|will be)[ \t]+(?:approved|authorized|recommended)\b',
            '(?im)^[ \t]*T03 should select none[ \t]*$',
            '(?im)^[ \t]*(?:Candidate|Decision result|Extraction decision|NovaGame owner|Target owner|Public contract)[ \t]*(?::|\|)',
            '(?im)^\|[ \t]*Stable identifier[ \t]*\|',
            '(?im)^\|[ \t]*Possible NovaGame owner[ \t]*\|',
            '(?im)^\|[ \t]*Proposed experimental contract[ \t]*\|',
            '(?im)^\|[ \t]*Migration and duplicate-removal plan[ \t]*\|'
        )) {
            Assert-Condition `
                (-not [regex]::IsMatch(
                    $m307T02Evidence,
                    $forbiddenM307T02Pattern)) `
                "M3-07-T02 evidence crossed into the T03 candidate decision."
        }

        if ($m3TaskStatuses["M3-07-T02"] -in @(
                "in_progress", "blocked")) {
            Assert-Condition `
                ($m3TaskStatuses["M3-07-T03"] -eq "pending") `
                "M3-07-T03 must remain pending while T02 evidence is being audited."
        }
        else {
            Assert-Condition `
                ($m3TaskStatuses["M3-07-T03"] -in @(
                    "pending", "in_progress", "complete")) `
                "Completed M3-07-T02 permits only the ordered pending, in-progress, or complete T03 states."
        }
        Assert-Condition `
            ([regex]::IsMatch(
                $m3Goal.Metadata,
                '(?m)^extraction_authorized:[ \t]*false[ \t]*$')) `
            "M3-07-T02 must keep extraction unauthorized."
        Assert-Condition `
            $m3Goal.Content.Contains(
                "[SwordMaster Player Equipment Grant M3-07 Reuse Evidence](../../samples/SwordMaster/docs/player-equipment-grant-m3-07-reuse-evidence.md)") `
            "M3 Goal must link the T02 reuse-pressure evidence record."

        if ($m3TaskStatuses["M3-07-T02"] -eq "complete") {
            $m307T02VerificationRows = [regex]::Matches(
                $m3Goal.Content,
                '(?m)^\|[ \t]*2026-08-12[ \t]*\|[ \t]*M3-07-T02[ \t]*\|[ \t]*(?<commands>[^\r\n|]*reuse-pressure audit[^\r\n|]*)[ \t]*\|[ \t]*Passed:[ \t]*(?<result>[^\r\n|]+)[ \t]*\|[ \t]*$')
            Assert-Condition `
                ($m307T02VerificationRows.Count -eq 1) `
                "Completed M3-07-T02 must record exactly one passing reuse-pressure audit row."
            $m307T02VerificationCommands =
                $m307T02VerificationRows[0].Groups["commands"].Value
            $m307T02VerificationResult =
                $m307T02VerificationRows[0].Groups["result"].Value
            foreach ($requiredM307T02VerificationCommand in @(
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository`',
                'framework-boundary and NovaCore audits',
                '`git diff --check`'
            )) {
                Assert-Condition `
                    $m307T02VerificationCommands.Contains(
                        $requiredM307T02VerificationCommand,
                        [StringComparison]::Ordinal) `
                    "Completed M3-07-T02 verification is missing '$requiredM307T02VerificationCommand'."
            }
            foreach ($requiredM307T02VerificationResult in @(
                'SwordMaster passed',
                'Documentation passed',
                'Repository passed',
                'framework tracked and untracked changes remained `0/0`',
                'NovaCore remained clean at `ef8db55173ff8f0286bb06abe753861c8a7d2d80`',
                '`git diff --check` passed'
            )) {
                Assert-Condition `
                    $m307T02VerificationResult.Contains(
                        $requiredM307T02VerificationResult,
                        [StringComparison]::Ordinal) `
                    "Completed M3-07-T02 result is missing '$requiredM307T02VerificationResult'."
            }
        }
    }

    if ($m307ReuseDecisionStatus -eq "recorded") {
        $m307T03DecisionMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^### M3-07-T03 Boundary Decision\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
        Assert-Condition `
            $m307T03DecisionMatch.Success `
            "M3-07-T03 requires its exact boundary decision record."
        $m307T03Decision = $m307T03DecisionMatch.Groups["body"].Value

        $expectedM307T03DecisionFields = @(
            "Decision status",
            "Candidate identifier",
            "Evidence evaluated",
            "Independently useful mechanism",
            "Proven reuse and variation",
            "SwordMaster policy retained",
            "Possible NovaGame owner",
            "Re-evaluation evidence required",
            "Extraction authorization",
            "Extraction implementation"
        )
        $m307T03DecisionTableMatch = [regex]::Match(
            $m307T03Decision,
            '(?ms)^\| Field \| Decision \|\r?\n\| --- \| --- \|\r?\n(?<rows>.*?)(?=\r?\n\r?\n)')
        Assert-Condition `
            $m307T03DecisionTableMatch.Success `
            "M3-07-T03 boundary decision table cannot be read."
        $actualM307T03DecisionFields = @(
            [regex]::Matches(
                $m307T03DecisionTableMatch.Groups["rows"].Value,
                '(?m)^\|[ \t]*(?<field>[^|\r\n]+?)[ \t]*\|[^\r\n]*$') |
                ForEach-Object { $_.Groups["field"].Value.Trim() } |
                Where-Object { $_ -ne "---" })
        Assert-Condition `
            (($actualM307T03DecisionFields -join ',') -eq
                ($expectedM307T03DecisionFields -join ',')) `
            "M3-07-T03 must contain the exact ordered boundary-decision fields."
        $m307T03DecisionValues = [ordered]@{}
        foreach ($m307T03DecisionRow in @(
            [regex]::Matches(
                $m307T03DecisionTableMatch.Groups["rows"].Value,
                '(?m)^\|[ \t]*(?<field>[^|\r\n]+?)[ \t]*\|[ \t]*(?<value>[^|\r\n]+?)[ \t]*\|[ \t]*$')
        )) {
            $m307T03DecisionField =
                $m307T03DecisionRow.Groups["field"].Value.Trim()
            Assert-Condition `
                (-not $m307T03DecisionValues.Contains($m307T03DecisionField)) `
                "M3-07-T03 boundary decision duplicates field '$m307T03DecisionField'."
            $m307T03DecisionValues[$m307T03DecisionField] =
                $m307T03DecisionRow.Groups["value"].Value.Trim()
        }
        foreach ($requiredM307T03DecisionValue in ([ordered]@{
            "Decision status" = "Recorded"
            "Candidate identifier" =
                '`request-audit-context-consumer-contract`'
            "Extraction authorization" = "Not granted"
            "Extraction implementation" = "Not started"
        }).GetEnumerator()) {
            Assert-Condition `
                ($m307T03DecisionValues[$requiredM307T03DecisionValue.Key] -ceq
                    $requiredM307T03DecisionValue.Value) `
                "M3-07-T03 field '$($requiredM307T03DecisionValue.Key)' must equal '$($requiredM307T03DecisionValue.Value)'."
        }
        $m307DecisionTableCandidate =
            $m307T03DecisionValues["Candidate identifier"].Trim('`')
        Assert-Condition `
            ($m307DecisionTableCandidate -ceq $m307ValidatedCandidate) `
            "M3-07-T03 decision-table candidate must equal goal metadata."

        foreach ($requiredM307T03Text in @(
            '`request-audit-context-consumer-contract`',
            'M307-BE-02',
            'M307-BE-05',
            'M307-RP-05',
            'M307-RP-11',
            'optional IP address, user agent, and correlation ID',
            'NovaGame AdminAccess and GameConfig already consume the same framework context',
            'its Application project consumes NovaGame contracts rather than NovaGame Application implementation',
            'The complete grant and rollback lifecycle',
            'no new business domain or physical module is implied',
            'Separate user authorization',
            '| Extraction authorization | Not granted |',
            '| Extraction implementation | Not started |',
            'It is narrower than request auditing as a complete subsystem.',
            'The candidate concerns consumability and layer placement of the existing',
            'It is not a new audit mechanism and must not create a',
            'parallel context or provider.',
            'The current NovaGame provider and framework consumers',
            'remain unchanged during M3.',
            'No rejected item is a second candidate.',
            'does not authorize extraction, source movement',
            'M307-RP-01',
            'M307-RP-02',
            'M307-RP-03',
            'M307-RP-04',
            'M307-RP-06',
            'M307-RP-07',
            'M307-RP-08',
            'M307-RP-09',
            'M307-RP-10'
        )) {
            Assert-Condition `
                $m307T03Decision.Contains($requiredM307T03Text) `
                "M3-07-T03 boundary decision is missing '$requiredM307T03Text'."
        }

        foreach ($requiredM307T03Link in @(
            '../../server/src/NovaGame.Application/Auditing/RequestAuditContext.cs',
            '../../server/src/NovaGame.Api/Auditing/HttpRequestAuditContextAccessor.cs',
            '../../server/src/NovaGame.DbMigrator/Auditing/DbMigratorRequestAuditContextAccessor.cs',
            '../../server/tests/NovaGame.EntityFrameworkCore.Tests/DbMigratorCompositionTests.cs',
            '../../server/src/NovaGame.Application/Operations/AdminAccess/AdminAccessAppService.cs',
            '../../server/src/NovaGame.Application/Content/GameConfig/GameConfigAppService.cs',
            '../../samples/SwordMaster/server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantApplicationPorts.cs',
            '../../samples/SwordMaster/server/src/SwordMaster.Api/PlayerAssets/HttpPlayerEquipmentGrantAuditContextAccessor.cs',
            '../../samples/SwordMaster/server/src/SwordMaster.Application/SwordMaster.Application.csproj',
            '../../server/tests/NovaGame.EntityFrameworkCore.Tests/AdministratorAccessApiTests.cs',
            '../../server/tests/NovaGame.EntityFrameworkCore.Tests/GameConfigApiTests.cs',
            '../../samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantApiTests.cs',
            '../../samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantPersistenceTests.cs'
        )) {
            Assert-Condition `
                $m307T03Decision.Contains($requiredM307T03Link) `
                "M3-07-T03 boundary decision is missing evidence link '$requiredM307T03Link'."
        }

        foreach ($forbiddenM307T03Pattern in @(
            '(?im)^#{1,6}[ \t]+(?:Public Contract|Implementation Plan|Migration Plan|Module Plan)[ \t]*$',
            '(?im)^\|[ \t]*(?:Proposed public contract|Package|Physical module|Migration plan)[ \t]*\|',
            '(?im)^[ \t]*(?:Extraction|Implementation)[ \t]+(?:is|was)[ \t]+(?:authorized|approved|started)\b',
            '(?im)^[ \t]*(?:We[ \t]+)?recommend(?:s|ed)?[ \t]+(?:the[ \t]+)?extraction\b',
            '(?im)^[ \t]*Extraction[ \t]+(?:is|was)[ \t]+recommended\b',
            '(?im)^[ \t]*(?:The[ \t]+)?Candidate[ \t]+(?:is|was)[ \t]+(?:approved|authorized)\b',
            '(?im)^[ \t]*Implementation[ \t]+(?:may|can|should|will|must)[ \t]+start\b',
            '(?im)^[ \t]*NovaGame[ \t]+(?:should|may|could|can|will|must)[ \t]+(?:move|implement|extract)[ \t]+'
        )) {
            Assert-Condition `
                (-not [regex]::IsMatch(
                    $m307T03Decision,
                    $forbiddenM307T03Pattern)) `
                "M3-07-T03 crossed from an evidence decision into extraction design or authorization."
        }

        Assert-Condition `
            ([regex]::IsMatch(
                $m3Goal.Metadata,
                '(?m)^extraction_authorized:[ \t]*false[ \t]*$')) `
            "M3-07-T03 must keep extraction unauthorized."
        if ($m3TaskStatuses["M3-07-T03"] -in @(
                "in_progress", "blocked")) {
            Assert-Condition `
                ($m3TaskStatuses["M3-07-T04"] -eq "pending") `
                "M3-07-T04 must remain pending while the T03 decision is being verified."
        }
        else {
            Assert-Condition `
                ($m3TaskStatuses["M3-07-T04"] -in @(
                    "pending", "in_progress", "complete")) `
                "Completed M3-07-T03 permits only the ordered pending, in-progress, or complete T04 states."
            $m3VerificationLogMatch = [regex]::Match(
                $m3Goal.Content,
                '(?ms)^## Verification Log\r?\n(?<body>.*)\z')
            Assert-Condition `
                $m3VerificationLogMatch.Success `
                "Completed M3-07-T03 requires the M3 Verification Log."
            $m307T03VerificationRows = [regex]::Matches(
                $m3VerificationLogMatch.Groups["body"].Value,
                '(?m)^\|[ \t]*2026-08-12[ \t]*\|[ \t]*M3-07-T03[ \t]*\|[ \t]*(?<commands>[^\r\n|]+)[ \t]*\|[ \t]*Passed:[ \t]*(?<result>[^\r\n|]+)[ \t]*\|[ \t]*$')
            Assert-Condition `
                ($m307T03VerificationRows.Count -eq 1) `
                "Completed M3-07-T03 must record exactly one passing verification row."
            $m307T03VerificationCommands =
                $m307T03VerificationRows[0].Groups["commands"].Value
            $m307T03VerificationResult =
                $m307T03VerificationRows[0].Groups["result"].Value
            Assert-Condition `
                $m307T03VerificationCommands.Contains(
                    'boundary decision verification',
                    [StringComparison]::Ordinal) `
                "Completed M3-07-T03 verification must identify the boundary decision verification."
            foreach ($requiredM307T03VerificationCommand in @(
                'PowerShell parse',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository`',
                'decision-link and framework-boundary audits',
                'NovaCore status audit',
                '`git diff --check`'
            )) {
                Assert-Condition `
                    $m307T03VerificationCommands.Contains(
                        $requiredM307T03VerificationCommand,
                        [StringComparison]::Ordinal) `
                    "Completed M3-07-T03 verification is missing '$requiredM307T03VerificationCommand'."
            }
            foreach ($requiredM307T03VerificationResult in @(
                'candidate `request-audit-context-consumer-contract` recorded',
                '`extraction_authorized: false`',
                'PowerShell parser passed',
                'Documentation passed',
                'Repository passed',
                'all 15 decision links resolved',
                'framework tracked and untracked changes remained `0/0`',
                'NovaCore remained clean at `ef8db55173ff8f0286bb06abe753861c8a7d2d80`',
                '`git diff --check` passed'
            )) {
                Assert-Condition `
                    $m307T03VerificationResult.Contains(
                        $requiredM307T03VerificationResult,
                        [StringComparison]::Ordinal) `
                    "Completed M3-07-T03 result is missing '$requiredM307T03VerificationResult'."
            }
        }
    }

    if ($m3TaskStatuses["M3-07-T04"] -in @(
            "in_progress", "blocked", "complete")) {
        $m307T04AuditMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^### M3-07-T04 Boundary Audit\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
        Assert-Condition `
            $m307T04AuditMatch.Success `
            "M3-07-T04 requires its exact boundary audit record."
        $m307T04Audit = $m307T04AuditMatch.Groups["body"].Value
        foreach ($requiredM307T04Text in @(
            'The frozen completed-M3-06 baseline contained 127 selected Git-status paths.',
            'all 127 with the same status and found no missing baseline file',
            'The current M3 Git-status scope',
            'contains 129 paths',
            '`lefthook.yml` is the 130th current status path',
            'Exactly nine M3 paths follow the frozen implementation baseline:',
            '| Current-state governance | `AGENTS.md`, `README.md`, `docs/README.md`, `docs/roadmap.md`, `samples/SwordMaster/README.md` |',
            '| Architecture result | `docs/architecture.md` |',
            '| Goal governance | `docs/goals/m3.md` |',
            '| Reuse evidence | `samples/SwordMaster/docs/player-equipment-grant-m3-07-reuse-evidence.md` |',
            '| Verification governance | `scripts/verify.ps1` |',
            'the reuse-evidence',
            'document is the only new M3 path',
            'No runtime, migration, generated contract, project, package, or',
            'game-admin source follows the frozen implementation baseline.',
            '`server/src`, `server/tests`, `clients/cocos`, and `admin/apps/web-ele` remain at tracked/untracked',
            '`0/0` relative to base commit `ba83e4af0d494da4176ee922c2caaf24f5fce905`',
            '`request-audit-context-consumer-contract` is a possible later candidate only',
            '`extraction_authorized: false`',
            'the complete grant and',
            'rollback capability remains SwordMaster-owned'
        )) {
            Assert-Condition `
                $m307T04Audit.Contains($requiredM307T04Text) `
                "M3-07-T04 boundary audit is missing '$requiredM307T04Text'."
        }

        $m307CandidateSourceHashes = [ordered]@{
            'server/src/NovaGame.Application/Auditing/RequestAuditContext.cs' =
                '8adae246a16c3c17073f423addba0f2fb384985d6eb251da67c04e0d17a6566e'
            'server/src/NovaGame.Api/Auditing/HttpRequestAuditContextAccessor.cs' =
                '6b38fd33c88d37f6d82eb3b7072f306b2b0b277f5c488a27a8afe7af2673e02c'
            'server/src/NovaGame.DbMigrator/Auditing/DbMigratorRequestAuditContextAccessor.cs' =
                '7f881cfb0aae436d3118e6e925636be0f8d69dc15f6f46944f8fa0c843c7f97b'
        }
        foreach ($m307CandidateSourceHash in $m307CandidateSourceHashes.GetEnumerator()) {
            Assert-Condition `
                (Test-Path -LiteralPath $m307CandidateSourceHash.Key -PathType Leaf) `
                "M3-07-T04 candidate evidence source '$($m307CandidateSourceHash.Key)' is missing."
            Assert-Condition `
                ((Get-FileHash `
                    -LiteralPath $m307CandidateSourceHash.Key `
                    -Algorithm SHA256).Hash.ToLowerInvariant() -ceq
                    $m307CandidateSourceHash.Value) `
                "M3-07-T04 candidate evidence source '$($m307CandidateSourceHash.Key)' changed after the frozen baseline."
        }

        if ($m35Goal.Status -notin @("in_progress", "blocked", "complete")) {
            $m307T04FrameworkTracked = @(
                & git diff --name-only `
                    "ba83e4af0d494da4176ee922c2caaf24f5fce905" `
                    -- `
                    "server/src" `
                    "server/tests" `
                    "clients/cocos" `
                    "admin/apps/web-ele")
            Assert-Condition `
                ($LASTEXITCODE -eq 0) `
                "Unable to audit tracked M3-07-T04 framework paths."
            $m307T04FrameworkUntracked = @(
                & git ls-files `
                    --others `
                    --exclude-standard `
                    -- `
                    "server/src" `
                    "server/tests" `
                    "clients/cocos" `
                    "admin/apps/web-ele")
            Assert-Condition `
                ($LASTEXITCODE -eq 0) `
                "Unable to audit untracked M3-07-T04 framework paths."
            Assert-Condition `
                ($m307T04FrameworkTracked.Count -eq 0 -and
                    $m307T04FrameworkUntracked.Count -eq 0) `
                "M3-07-T04 requires framework tracked/untracked changes to remain 0/0 before a later framework milestone is active."
        }

        $m307Architecture = Get-Content -LiteralPath "docs/architecture.md" -Raw
        foreach ($requiredM307ArchitectureText in @(
            '`request-audit-context-consumer-contract` as evidence vocabulary',
            'Extraction remains',
            'unauthorized and unimplemented',
            'adds no public API, business domain, physical module, or',
            'parallel provider',
            'The complete SwordMaster equipment-grant and rollback lifecycle',
            'remain owned by SwordMaster'
        )) {
            Assert-Condition `
                $m307Architecture.Contains($requiredM307ArchitectureText) `
                "M3-07-T04 architecture result is missing '$requiredM307ArchitectureText'."
        }

        $m307BaselineRoot = Join-Path $repositoryRoot ".local\m3-07-baseline"
        $m307BaselineStatusPath = Join-Path $m307BaselineRoot "worktree-status.txt"
        $m307TrackedManifestPath = Join-Path $m307BaselineRoot "tracked-files.sha256"
        $m307UntrackedManifestPath = Join-Path $m307BaselineRoot "untracked-files.sha256"
        $m307RequiredBaselineArtifacts = [ordered]@{
            $m307BaselineStatusPath =
                '6b90f59643ed7690ecf3f0551738943ad49094ae8602f90e13261bbc57c3cc6c'
            $m307TrackedManifestPath =
                '7b38eca28b06b5580f35573dd58d8c896bd92160ded12272c11665df33f1dbfa'
            $m307UntrackedManifestPath =
                '4c5b044a089337012bda43a0229d42d87ac8f6c668b63d2faed430ee2bd17714'
        }
        $m307MissingBaselineArtifacts = @(
            $m307RequiredBaselineArtifacts.Keys |
                Where-Object {
                    -not (Test-Path -LiteralPath $_ -PathType Leaf)
                })
        $m307PresentBaselineArtifactCount =
            $m307RequiredBaselineArtifacts.Count -
            $m307MissingBaselineArtifacts.Count
        if ($m3Goal.Status -ne "complete" -or
            $m307PresentBaselineArtifactCount -gt 0) {
            Assert-Condition `
                ($m307MissingBaselineArtifacts.Count -eq 0) `
                "Active M3-07/M3-08 verification requires all three frozen baseline audit artifacts; missing:`n$($m307MissingBaselineArtifacts -join "`n")"
            foreach ($m307RequiredBaselineArtifact in
                $m307RequiredBaselineArtifacts.GetEnumerator()) {
                Assert-Condition `
                    ((Get-FileHash `
                        -LiteralPath $m307RequiredBaselineArtifact.Key `
                        -Algorithm SHA256).Hash.ToLowerInvariant() -ceq
                        $m307RequiredBaselineArtifact.Value) `
                    "M3-07 frozen baseline artifact '$($m307RequiredBaselineArtifact.Key)' failed its recorded SHA-256 check."
            }
            $m307ChangedBaselinePaths = [System.Collections.Generic.List[string]]::new()
            $m307MissingBaselinePaths = [System.Collections.Generic.List[string]]::new()
            foreach ($m307ManifestPath in @(
                $m307TrackedManifestPath,
                $m307UntrackedManifestPath
            )) {
                foreach ($m307ManifestLine in Get-Content -LiteralPath $m307ManifestPath) {
                    Assert-Condition `
                        ($m307ManifestLine -match
                            '^(?<hash>[0-9a-f]{64}) \*(?<path>.+)$') `
                        "M3-07 baseline manifest contains an invalid row."
                    $m307ManifestFile = $Matches["path"].Replace('\', '/')
                    if (-not (Test-Path -LiteralPath $m307ManifestFile -PathType Leaf)) {
                        $m307MissingBaselinePaths.Add($m307ManifestFile)
                        continue
                    }
                    $m307CurrentHash = (Get-FileHash `
                        -LiteralPath $m307ManifestFile `
                        -Algorithm SHA256).Hash.ToLowerInvariant()
                    if ($m307CurrentHash -cne $Matches["hash"]) {
                        $m307ChangedBaselinePaths.Add($m307ManifestFile)
                    }
                }
            }
            Assert-Condition `
                ($m307MissingBaselinePaths.Count -eq 0) `
                "M3-07-T04 cannot remove a frozen baseline file."
            $expectedM307ChangedBaselinePaths = @(
                'AGENTS.md',
                'README.md',
                'docs/README.md',
                'docs/architecture.md',
                'docs/goals/m3.md',
                'docs/roadmap.md',
                'samples/SwordMaster/README.md',
                'scripts/verify.ps1'
            ) | Sort-Object
            $actualM307ChangedBaselinePaths = @(
                $m307ChangedBaselinePaths | Sort-Object -Unique)
            Assert-Condition `
                (($actualM307ChangedBaselinePaths -join "`n") -ceq
                    ($expectedM307ChangedBaselinePaths -join "`n")) `
                "M3-07-T04 changed-baseline path set is stale:`n$($actualM307ChangedBaselinePaths -join "`n")"

            $m307BaselineStatus = [ordered]@{}
            foreach ($m307BaselineStatusLine in Get-Content -LiteralPath $m307BaselineStatusPath) {
                $m307BaselineStatus[
                    $m307BaselineStatusLine.Substring(3).Replace('\', '/')
                ] = $m307BaselineStatusLine.Substring(0, 2)
            }
            $m307CurrentStatusLines = @(& git status --short --untracked-files=all)
            Assert-Condition `
                ($LASTEXITCODE -eq 0) `
                "Unable to audit the current M3-07-T04 worktree status."
            $m307CurrentStatus = [ordered]@{}
            foreach ($m307CurrentStatusLine in $m307CurrentStatusLines) {
                $m307CurrentStatus[
                    $m307CurrentStatusLine.Substring(3).Replace('\', '/')
                ] = $m307CurrentStatusLine.Substring(0, 2)
            }
            foreach ($m307BaselineStatusEntry in $m307BaselineStatus.GetEnumerator()) {
                Assert-Condition `
                    ($m307CurrentStatus.Contains($m307BaselineStatusEntry.Key) -and
                        $m307CurrentStatus[$m307BaselineStatusEntry.Key] -ceq
                            $m307BaselineStatusEntry.Value) `
                    "M3-07-T04 changed or removed frozen status path '$($m307BaselineStatusEntry.Key)'."
            }
            $actualM307NewStatusPaths = @(
                $m307CurrentStatus.Keys |
                    Where-Object { -not $m307BaselineStatus.Contains($_) } |
                    Sort-Object)
            $expectedM307NewStatusPaths = @(
                'docs/architecture.md',
                'lefthook.yml',
                'samples/SwordMaster/docs/player-equipment-grant-m3-07-reuse-evidence.md'
            ) | Sort-Object
            Assert-Condition `
                (($actualM307NewStatusPaths -join "`n") -ceq
                    ($expectedM307NewStatusPaths -join "`n")) `
                "M3-07-T04 post-baseline new-path set is stale:`n$($actualM307NewStatusPaths -join "`n")"
            Assert-Condition `
                ((Get-FileHash -LiteralPath "lefthook.yml" -Algorithm SHA256).Hash.ToLowerInvariant() -ceq
                    '89951797a15ac7f78e2e2979735a29377eace45f27154120d970dbf3b253669d') `
                "M3-07-T04 must preserve the excluded lefthook.yml unchanged."
        }

        Assert-Condition `
            ([regex]::IsMatch(
                $m3Goal.Metadata,
                '(?m)^extraction_authorized:[ \t]*false[ \t]*$')) `
            "M3-07-T04 must keep extraction unauthorized."
        if ($m3TaskStatuses["M3-07-T04"] -in @(
                "in_progress", "blocked")) {
            Assert-Condition `
                ($m3TaskStatuses["M3-08-T01"] -eq "pending") `
                "M3-08-T01 must remain pending while M3-07-T04 is being verified."
        }
        else {
            Assert-Condition `
                ($m3TaskStatuses["M3-08-T01"] -in @(
                    "pending", "in_progress", "complete")) `
                "Completed M3-07-T04 permits only the ordered M3-08-T01 states."
            $m307T04VerificationLogMatch = [regex]::Match(
                $m3Goal.Content,
                '(?ms)^## Verification Log\r?\n(?<body>.*)\z')
            Assert-Condition `
                $m307T04VerificationLogMatch.Success `
                "Completed M3-07-T04 requires the M3 Verification Log."
            $m307T04VerificationRows = [regex]::Matches(
                $m307T04VerificationLogMatch.Groups["body"].Value,
                '(?m)^\|[ \t]*2026-08-13[ \t]*\|[ \t]*M3-07-T04[ \t]*\|[ \t]*(?<commands>[^\r\n|]+)[ \t]*\|[ \t]*Passed:[ \t]*(?<result>[^\r\n|]+)[ \t]*\|[ \t]*$')
            Assert-Condition `
                ($m307T04VerificationRows.Count -eq 1) `
                "Completed M3-07-T04 must record exactly one passing verification row."
            $m307T04VerificationCommands =
                $m307T04VerificationRows[0].Groups["commands"].Value
            $m307T04VerificationResult =
                $m307T04VerificationRows[0].Groups["result"].Value
            foreach ($requiredM307T04VerificationCommand in @(
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository`',
                'PowerShell parse',
                'post-baseline path, framework-boundary, candidate-source, NovaCore, and cleanup audits',
                '`git diff --check`'
            )) {
                Assert-Condition `
                    $m307T04VerificationCommands.Contains(
                        $requiredM307T04VerificationCommand,
                        [StringComparison]::Ordinal) `
                    "Completed M3-07-T04 verification is missing '$requiredM307T04VerificationCommand'."
            }
            foreach ($requiredM307T04VerificationResult in @(
                'SwordMaster passed',
                'Documentation passed',
                'Repository passed',
                'post-baseline M3 paths were exactly `9`',
                'framework tracked and untracked changes remained `0/0`',
                'three candidate-source hashes remained frozen',
                '`extraction_authorized: false`',
                '`lefthook.yml` remained excluded and unchanged',
                'NovaCore remained clean at `ef8db55173ff8f0286bb06abe753861c8a7d2d80`',
                'disposable containers, networks, volumes, processes, and artifacts were zero',
                '`git diff --check` passed'
            )) {
                Assert-Condition `
                    $m307T04VerificationResult.Contains(
                        $requiredM307T04VerificationResult,
                        [StringComparison]::Ordinal) `
                    "Completed M3-07-T04 result is missing '$requiredM307T04VerificationResult'."
            }
        }
    }

    if ($m3TaskStatuses["M3-08-T01"] -in @(
            "in_progress", "blocked", "complete")) {
        $m308T01RecordMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^### M3-08-T01 Cumulative Acceptance Contract\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
        Assert-Condition `
            $m308T01RecordMatch.Success `
            "M3-08-T01 requires its cumulative acceptance contract."
        $m308T01Record = $m308T01RecordMatch.Groups["body"].Value
        $expectedM308T01Commands = @(
            'pwsh -NoProfile -File scripts/bootstrap.ps1',
            'pwsh -NoProfile -File scripts/verify.ps1',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope Admin',
            'pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster',
            'pwsh -NoProfile -Command "[scriptblock]::Create((Get-Content -Raw scripts/verify.ps1)) | Out-Null"',
            'git diff --check',
            'dotnet restore samples/SwordMaster/server/SwordMaster.Server.sln --configfile samples/SwordMaster/server/NuGet.Config --force-evaluate',
            'dotnet format samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --verify-no-changes --verbosity minimal',
            'dotnet build samples/SwordMaster/server/SwordMaster.Server.sln --no-restore --configuration Release -maxcpucount:1 -warnaserror',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Domain.Tests/SwordMaster.Domain.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrant',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Application.Tests/SwordMaster.Application.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrant',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantApiTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantOpenApiContractTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantPersistenceTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantMySqlApplicationTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~PlayerEquipmentGrantPermissionSeedTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterRuntimeCompositionTests',
            'dotnet test samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~SwordMasterDbMigratorConfigurationTests',
            'pwsh -NoProfile -File scripts/update-swordmaster-player-equipment-grant-contract.ps1',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run format:check',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run lint',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run typecheck',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run test:equipment-grant-transport',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run test:equipment-grant-model',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run test:equipment-grant-view',
            'pnpm --dir admin --filter @swordmaster/admin-web-ele run build'
        )
        $actualM308T01Commands = @(
            [regex]::Matches(
                $m308T01Record,
                '(?m)^- `(?<command>[^`\r\n]+)`[ \t]*$') |
                ForEach-Object { $_.Groups["command"].Value })
        Assert-Condition `
            (($actualM308T01Commands -join "`n") -ceq
                ($expectedM308T01Commands -join "`n")) `
            "M3-08-T01 must retain the exact ordered 28-command acceptance set."
        foreach ($requiredM308T01RecordText in @(
            'The exact deduplicated acceptance set contains 28 commands in this order:',
            '`test:e2e:equipment-grant` invocation is diagnostic-only',
            'The qualifying SwordMaster scope owns and verifies that browser path.',
            'Domain total/focused `49/20`',
            'Application',
            'total/focused `85/36`',
            'player/operator API `35/42`',
            'player/operator OpenAPI `1/3`',
            'composition/DbMigrator configuration `12/5`',
            '`8/5/2`',
            'Client `53`',
            'sample `38`',
            'independent-admin auth/transport/model/view `45/12/14/10`',
            'composed API `3/3`',
            'Playwright `2/2`',
            'one desktop and one mobile',
            '33f6859d882eca0b9d6c647506f2c801e6ca1ae114b07bd15b11feafbc600c6f',
            '852b2d412ff50262736f4207eaaf8f083f042092dca6a854e8709aa309db7a0a',
            '222f5c59188d06098b677a7ff004c546871391ea',
            'ef8db55173ff8f0286bb06abe753861c8a7d2d80',
            'All disposable Docker resources',
            'must be absent after the command set.'
        )) {
            Assert-Condition `
                $m308T01Record.Contains($requiredM308T01RecordText) `
                "M3-08-T01 acceptance contract is missing '$requiredM308T01RecordText'."
        }

        if ($m3TaskStatuses["M3-08-T01"] -in @(
                "in_progress", "blocked")) {
            Assert-Condition `
                $m308T01Record.Contains('**Status:** In progress.') `
                "Active M3-08-T01 must mark its cumulative acceptance contract in progress."
            Assert-Condition `
                (-not $m308T01Record.Contains('**Status:** Complete.')) `
                "Active M3-08-T01 cannot mark its cumulative acceptance contract complete."
            Assert-Condition `
                ($m3TaskStatuses["M3-08-T02"] -eq "pending" -and
                    $m3TaskStatuses["M3-08-T03"] -eq "pending") `
                "M3-08-T02 and T03 must remain pending while T01 acceptance runs."
        }
        else {
            Assert-Condition `
                $m308T01Record.Contains('**Status:** Complete.') `
                "Completed M3-08-T01 must mark its cumulative acceptance contract complete."
            Assert-Condition `
                (-not $m308T01Record.Contains('**Status:** In progress.')) `
                "Completed M3-08-T01 cannot retain an in-progress acceptance status."
            Assert-Condition `
                ($m3TaskStatuses["M3-08-T02"] -in @(
                    "pending", "in_progress", "complete")) `
                "Completed M3-08-T01 permits only ordered M3-08-T02 states."
            $m308T01VerificationLogMatch = [regex]::Match(
                $m3Goal.Content,
                '(?ms)^## Verification Log\r?\n(?<body>.*)\z')
            Assert-Condition `
                $m308T01VerificationLogMatch.Success `
                "Completed M3-08-T01 requires the M3 Verification Log."
            $m308T01VerificationRows = [regex]::Matches(
                $m308T01VerificationLogMatch.Groups["body"].Value,
                '(?m)^\|[ \t]*2026-08-13[ \t]*\|[ \t]*M3-08-T01[ \t]*\|[ \t]*(?<commands>[^\r\n|]+)[ \t]*\|[ \t]*Passed:[ \t]*(?<result>[^\r\n|]+)[ \t]*\|[ \t]*$')
            Assert-Condition `
                ($m308T01VerificationRows.Count -eq 1) `
                "Completed M3-08-T01 must record exactly one passing verification row."
            $m308T01VerificationCommands =
                $m308T01VerificationRows[0].Groups["commands"].Value
            $m308T01VerificationResult =
                $m308T01VerificationRows[0].Groups["result"].Value
            foreach ($requiredM308T01VerificationCommand in @(
                '28-command deduplicated M3 acceptance set',
                '`pwsh -NoProfile -File scripts/bootstrap.ps1`',
                '`pwsh -NoProfile -File scripts/verify.ps1`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Admin`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster`',
                '`pwsh -NoProfile -File scripts/update-swordmaster-player-equipment-grant-contract.ps1`',
                'PowerShell parse',
                '`git diff --check`'
            )) {
                Assert-Condition `
                    $m308T01VerificationCommands.Contains(
                        $requiredM308T01VerificationCommand,
                        [StringComparison]::Ordinal) `
                    "Completed M3-08-T01 verification is missing '$requiredM308T01VerificationCommand'."
            }
            foreach ($requiredM308T01VerificationResult in @(
                'all 28 unique qualifying commands passed',
                'bootstrap passed',
                'cumulative All passed',
                'generic Admin real E2E passed',
                'Client `53/53`',
                'Domain `49/49` and focused `20/20`',
                'Application `85/85` and focused `36/36`',
                'player/operator API `35/42`',
                'player/operator OpenAPI `1/3`',
                'runtime composition/DbMigrator `12/5`',
                'grant persistence/MySQL application/permission seed `8/5/2`',
                'independent-admin auth/transport/model/view `45/12/14/10`',
                'composed API `3/3`',
                'Playwright `2/2` desktop/mobile',
                'zero retries, skips, pending, flakes, unexpected, or not-executed results',
                'operator OpenAPI/type hashes remained `33f6859d882eca0b9d6c647506f2c801e6ca1ae114b07bd15b11feafbc600c6f`/`852b2d412ff50262736f4207eaaf8f083f042092dca6a854e8709aa309db7a0a`',
                'locked NovaCore snapshot remained `222f5c59188d06098b677a7ff004c546871391ea`',
                'local NovaCore source remained clean at `ef8db55173ff8f0286bb06abe753861c8a7d2d80`',
                'disposable resources, owned processes, results, artifacts, logs, and environment state were zero',
                '`git diff --check` passed'
            )) {
                Assert-Condition `
                    $m308T01VerificationResult.Contains(
                        $requiredM308T01VerificationResult,
                        [StringComparison]::Ordinal) `
                    "Completed M3-08-T01 result is missing '$requiredM308T01VerificationResult'."
            }
        }
    }

    if ($m3TaskStatuses["M3-08-T02"] -in @(
            "in_progress", "blocked", "complete")) {
        $m308T02RecordMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^### M3-08-T02 Final Governance Audit\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
        Assert-Condition `
            $m308T02RecordMatch.Success `
            "M3-08-T02 requires its final governance audit record."
        $m308T02Record = $m308T02RecordMatch.Groups["body"].Value
        $m308T02StatusRows = [regex]::Matches(
            $m308T02Record,
            '(?m)^\*\*Status:\*\* (?<value>Pending|In progress|Blocked|Complete)\.[ \t]*$')
        Assert-Condition `
            ($m308T02StatusRows.Count -eq 1) `
            "M3-08-T02 final governance audit must contain exactly one status row."

        $m308FrozenFiles = [ordered]@{
            'samples/SwordMaster/contracts/player-assets.openapi.json' =
                '189f10cb20dc1973f7dbc23ab9da6c4db008bb72e55c889b60626a36fcf3cc06'
            'samples/SwordMaster/src/generated/player-assets/types.gen.ts' =
                'd0ea918d33a970fcd341f928fe06bd278314197db10d04f8ed969f4fb6b1bf9f'
            'samples/SwordMaster/admin/contracts/player-equipment-grants.openapi.json' =
                '33f6859d882eca0b9d6c647506f2c801e6ca1ae114b07bd15b11feafbc600c6f'
            'samples/SwordMaster/admin/src/generated/player-equipment-grants/types.gen.ts' =
                '852b2d412ff50262736f4207eaaf8f083f042092dca6a854e8709aa309db7a0a'
            'samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/Migrations/20260810105902_AddPlayerEquipmentGrants.cs' =
                '117df3a162d32730fcb5771a1d1368a02ea8bfd7c1bee95bf3d5ef6d310f6167'
            'samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/Migrations/20260810105902_AddPlayerEquipmentGrants.Designer.cs' =
                '6c8c105171a22baecb20865cad975542b2e8b7935755ca9b4eafdda5f13c7fc3'
            'samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/Migrations/SwordMasterDbContextModelSnapshot.cs' =
                'a620e176668178245dd627da3cdbd10cfe6722d46eba1915b77f78c2614a1524'
        }
        foreach ($m308FrozenFile in $m308FrozenFiles.GetEnumerator()) {
            Assert-Condition `
                (Test-Path -LiteralPath $m308FrozenFile.Key -PathType Leaf) `
                "M3-08-T02 frozen file '$($m308FrozenFile.Key)' is missing."
            Assert-Condition `
                ((Get-FileHash `
                    -LiteralPath $m308FrozenFile.Key `
                    -Algorithm SHA256).Hash.ToLowerInvariant() -ceq
                    $m308FrozenFile.Value) `
                "M3-08-T02 frozen file '$($m308FrozenFile.Key)' changed after acceptance."
        }

        $m308NovaGameApiSettings = Get-Content `
            -LiteralPath "server/src/NovaGame.Api/appsettings.json" `
            -Raw | ConvertFrom-Json
        $m308NovaGameProductionSettings = Get-Content `
            -LiteralPath "server/src/NovaGame.Api/appsettings.Production.json" `
            -Raw | ConvertFrom-Json
        $m308SwordMasterApiSettings = Get-Content `
            -LiteralPath "samples/SwordMaster/server/src/SwordMaster.Api/appsettings.json" `
            -Raw | ConvertFrom-Json
        $m308SwordMasterProductionSettings = Get-Content `
            -LiteralPath "samples/SwordMaster/server/src/SwordMaster.Api/appsettings.Production.json" `
            -Raw | ConvertFrom-Json
        $m308SwordMasterMigratorSettings = Get-Content `
            -LiteralPath "samples/SwordMaster/server/src/SwordMaster.DbMigrator/appsettings.json" `
            -Raw | ConvertFrom-Json
        $m308CommittedSecretValues = @(
            [string]$m308NovaGameApiSettings.ConnectionStrings.Default,
            [string]$m308NovaGameApiSettings.NovaGame.AdminAccess.Jwt.SigningKey,
            [string]$m308NovaGameApiSettings.NovaGame.AdminAccess.Seed.UserName,
            [string]$m308NovaGameApiSettings.NovaGame.AdminAccess.Seed.Password,
            [string]$m308NovaGameProductionSettings.ConnectionStrings.Default,
            [string]$m308NovaGameProductionSettings.NovaGame.AdminAccess.Jwt.SigningKey,
            [string]$m308SwordMasterApiSettings.ConnectionStrings.NovaGame,
            [string]$m308SwordMasterApiSettings.ConnectionStrings.SwordMaster,
            [string]$m308SwordMasterApiSettings.NovaGame.AdminAccess.Jwt.SigningKey,
            [string]$m308SwordMasterApiSettings.NovaGame.AdminAccess.Seed.UserName,
            [string]$m308SwordMasterApiSettings.NovaGame.AdminAccess.Seed.Password,
            [string]$m308SwordMasterApiSettings.SwordMaster.PlayerAuthentication.Jwt.SigningKey,
            [string]$m308SwordMasterProductionSettings.ConnectionStrings.NovaGame,
            [string]$m308SwordMasterProductionSettings.ConnectionStrings.SwordMaster,
            [string]$m308SwordMasterProductionSettings.NovaGame.AdminAccess.Jwt.SigningKey,
            [string]$m308SwordMasterProductionSettings.SwordMaster.PlayerAuthentication.Jwt.SigningKey,
            [string]$m308SwordMasterMigratorSettings.ConnectionStrings.NovaGame,
            [string]$m308SwordMasterMigratorSettings.ConnectionStrings.SwordMaster,
            [string]$m308SwordMasterMigratorSettings.NovaGame.AdminAccess.Jwt.SigningKey,
            [string]$m308SwordMasterMigratorSettings.NovaGame.AdminAccess.Seed.UserName,
            [string]$m308SwordMasterMigratorSettings.NovaGame.AdminAccess.Seed.Password
        )
        Assert-Condition `
            (@($m308CommittedSecretValues | Where-Object { $_ -cne '' }).Count -eq 0) `
            "M3-08-T02 committed connection strings, signing keys, and seed credentials must remain empty."
        foreach ($m308LocalOverridePath in @(
            "server/.env.local",
            "deploy/.env",
            "admin/apps/web-ele/.env.local",
            "samples/SwordMaster/admin/.env.local"
        )) {
            if (Test-Path -LiteralPath $m308LocalOverridePath) {
                & git check-ignore --quiet -- $m308LocalOverridePath
                Assert-Condition `
                    ($LASTEXITCODE -eq 0) `
                    "Local secret override '$m308LocalOverridePath' must remain ignored."
            }
        }
        foreach ($m308SwordMasterAdminEnvironmentPath in @(
            "samples/SwordMaster/admin/.env.development",
            "samples/SwordMaster/admin/.env.production",
            "samples/SwordMaster/admin/.env.example"
        )) {
            $m308SwordMasterAdminEnvironmentKeys = @(
                Get-Content -LiteralPath $m308SwordMasterAdminEnvironmentPath |
                    Where-Object { $_ -match '^[A-Z][A-Z0-9_]*=' } |
                    ForEach-Object { ($_ -split '=', 2)[0] })
            $m308UnexpectedEnvironmentKeys = @(
                $m308SwordMasterAdminEnvironmentKeys |
                    Where-Object {
                        $_ -notin @(
                            'VITE_APP_TITLE',
                            'VITE_APP_NAMESPACE',
                            'VITE_APP_STORE_SECURE_KEY',
                            'VITE_PORT',
                            'VITE_BASE',
                            'VITE_GLOB_API_URL',
                            'VITE_SWORDMASTER_API_URL',
                            'VITE_HTTPS',
                            'VITE_NITRO_MOCK',
                            'VITE_DEVTOOLS',
                            'VITE_INJECT_APP_LOADING',
                            'VITE_COMPRESS',
                            'VITE_PWA',
                            'VITE_ROUTER_HISTORY',
                            'VITE_ARCHIVER'
                        )
                    })
            Assert-Condition `
                ($m308UnexpectedEnvironmentKeys.Count -eq 0) `
                "SwordMaster admin environment '$m308SwordMasterAdminEnvironmentPath' contains a non-public configuration key."
        }

        if ($m3TaskStatuses["M3-08-T02"] -in @(
                "in_progress", "blocked")) {
            $expectedM308T02Status = if (
                $m3TaskStatuses["M3-08-T02"] -eq "blocked") {
                'Blocked'
            }
            else {
                'In progress'
            }
            Assert-Condition `
                ($m308T02StatusRows[0].Groups["value"].Value -ceq
                    $expectedM308T02Status) `
                "Active M3-08-T02 must use status '$expectedM308T02Status'."
            Assert-Condition `
                ($m3TaskStatuses["M3-08-T03"] -eq "pending") `
                "M3-08-T03 must remain pending while T02 is active."
        }
        else {
            Assert-Condition `
                ($m308T02StatusRows[0].Groups["value"].Value -ceq
                    'Complete') `
                "Completed M3-08-T02 must mark its final governance audit complete."
            Assert-Condition `
                ($m3TaskStatuses["M3-08-T03"] -in @(
                    "pending", "in_progress", "complete")) `
                "Completed M3-08-T02 permits only ordered M3-08-T03 states."

            $m308T02VerificationLogMatch = [regex]::Match(
                $m3Goal.Content,
                '(?ms)^## Verification Log\r?\n(?<body>.*)\z')
            $m308T02VerificationRows = [regex]::Matches(
                $m308T02VerificationLogMatch.Groups["body"].Value,
                '(?m)^\|[ \t]*2026-08-13[ \t]*\|[ \t]*M3-08-T02[ \t]*\|[ \t]*(?<commands>[^|\r\n]+)[ \t]*\|[ \t]*Passed:[ \t]*(?<result>[^|\r\n]+)[ \t]*\|[ \t]*$')
            Assert-Condition `
                ($m308T02VerificationRows.Count -eq 1) `
                "Completed M3-08-T02 must record exactly one passing final-audit row."
            $m308T02VerificationCommands =
                $m308T02VerificationRows[0].Groups["commands"].Value
            $m308T02VerificationResult =
                $m308T02VerificationRows[0].Groups["result"].Value
            foreach ($requiredM308T02VerificationCommand in @(
                'dependency-direction audit',
                'contract-hash audit',
                'migration audit',
                'tracked-secret audit',
                'NovaCore audit',
                'disposable-resource audit',
                'PowerShell parse',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Documentation`',
                '`pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository`',
                '`git diff --check`'
            )) {
                Assert-Condition `
                    $m308T02VerificationCommands.Contains(
                        $requiredM308T02VerificationCommand,
                        [StringComparison]::Ordinal) `
                    "Completed M3-08-T02 verification is missing '$requiredM308T02VerificationCommand'."
            }
            foreach ($requiredM308T02VerificationResult in @(
                'framework tracked/untracked changes remained `0/0`',
                'player OpenAPI/type hashes remained `189f10cb20dc1973f7dbc23ab9da6c4db008bb72e55c889b60626a36fcf3cc06`/`d0ea918d33a970fcd341f928fe06bd278314197db10d04f8ed969f4fb6b1bf9f`',
                'operator OpenAPI/type hashes remained `33f6859d882eca0b9d6c647506f2c801e6ca1ae114b07bd15b11feafbc600c6f`/`852b2d412ff50262736f4207eaaf8f083f042092dca6a854e8709aa309db7a0a`',
                'migration `20260810105902_AddPlayerEquipmentGrants` and model snapshot remained current',
                'no committed secret or production credential was found',
                'committed connection strings, signing keys, and seed credentials remained empty',
                'local overrides remained ignored',
                'locked NovaCore snapshot remained `222f5c59188d06098b677a7ff004c546871391ea`',
                'local NovaCore source remained clean at `ef8db55173ff8f0286bb06abe753861c8a7d2d80`',
                'disposable containers, networks, volumes, processes, results, artifacts, acceptance logs, and temporary environment files were zero',
                'M4 remained `Unscheduled`',
                '`extraction_authorized: false`',
                '`git diff --check` passed'
            )) {
                Assert-Condition `
                    $m308T02VerificationResult.Contains(
                        $requiredM308T02VerificationResult,
                        [StringComparison]::Ordinal) `
                    "Completed M3-08-T02 result is missing '$requiredM308T02VerificationResult'."
            }
        }
    }

    if ($m3TaskStatuses["M3-08-T01"] -eq "complete") {
        $m308T03ContractMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^### M3-08-T03 Closure Contract\r?\n(?<body>.*?)(?=^###\s+|^##\s+|\z)')
        Assert-Condition `
            $m308T03ContractMatch.Success `
            "M3-08 requires its T03 closure contract."
        $m308T03Contract = $m308T03ContractMatch.Groups["body"].Value
        $m308T03StatusRows = [regex]::Matches(
            $m308T03Contract,
            '(?m)^\*\*Status:\*\* (?<value>Pending|In progress|Blocked|Complete)\.[ \t]*$')
        Assert-Condition `
            ($m308T03StatusRows.Count -eq 1) `
            "M3-08-T03 closure contract must contain exactly one status row."
        $expectedM308T03Status = switch ($m3TaskStatuses["M3-08-T03"]) {
            "pending" { 'Pending' }
            "in_progress" { 'In progress' }
            "blocked" { 'Blocked' }
            "complete" { 'Complete' }
        }
        Assert-Condition `
            ($m308T03StatusRows[0].Groups["value"].Value -ceq
                $expectedM308T03Status) `
            "M3-08-T03 closure contract must use status '$expectedM308T03Status'."
    }

    if ($m3Goal.Status -eq "complete") {
        $m308LastVerificationCommandMatch = [regex]::Match(
            $m3Goal.Metadata,
            '(?m)^last_verification_command:[ \t]*(?<value>[^\r\n]+?)[ \t]*$')
        $m308LastVerificationResultMatch = [regex]::Match(
            $m3Goal.Metadata,
            '(?m)^last_verification_result:[ \t]*(?<value>[^\r\n]+?)[ \t]*$')
        $m308LastVerificationAtMatch = [regex]::Match(
            $m3Goal.Metadata,
            '(?m)^last_verification_at:[ \t]*(?<value>[^\r\n]+?)[ \t]*$')
        $m308UpdatedAtMatch = [regex]::Match(
            $m3Goal.Metadata,
            '(?m)^updated_at:[ \t]*(?<value>[^\r\n]+?)[ \t]*$')
        Assert-Condition `
            ($m308LastVerificationCommandMatch.Groups["value"].Value -ceq
                'pwsh -NoProfile -File scripts/verify.ps1 -Scope Repository') `
            "Completed M3 must use the durable Repository command as its last verification."
        Assert-Condition `
            ($m308LastVerificationAtMatch.Groups["value"].Value -ceq
                $m308UpdatedAtMatch.Groups["value"].Value) `
            "Completed M3 metadata must be updated on its final verification date."
        foreach ($requiredM308LastResultText in @(
            'M3-08',
            'complete',
            'deactivated',
            'player-equipment-grant',
            'request-audit-context-consumer-contract',
            'no repository milestone is active',
            'grants no continuing authorization'
        )) {
            Assert-Condition `
                $m308LastVerificationResultMatch.Groups["value"].Value.Contains(
                    $requiredM308LastResultText,
                    [StringComparison]::OrdinalIgnoreCase) `
                "Completed M3 result is missing '$requiredM308LastResultText'."
        }

        $m308ClosureLog = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^## Verification Log\r?\n(?<body>.*)\z').Groups["body"].Value
        $m308ClosureRows = [regex]::Matches(
            $m308ClosureLog,
            '(?m)^\|[ \t]*2026-08-13[ \t]*\|[ \t]*M3-08[ \t]*\|[ \t]*`pwsh -NoProfile -File scripts/verify\.ps1 -Scope Repository`(?<commands>[^|\r\n]*)\|[ \t]*Passed:[ \t]*(?<result>[^|\r\n]+)[ \t]*\|[ \t]*$')
        Assert-Condition `
            ($m308ClosureRows.Count -eq 1) `
            "Completed M3 must record exactly one passing M3-08 closure row."
        $m308ClosureCommands = $m308ClosureRows[0].Groups["commands"].Value
        $m308ClosureResult = $m308ClosureRows[0].Groups["result"].Value
        foreach ($requiredM308ClosureCommand in @(
            '`git diff --check`',
            'final NovaCore audit',
            'final disposable-resource audit'
        )) {
            Assert-Condition `
                $m308ClosureCommands.Contains(
                    $requiredM308ClosureCommand,
                    [StringComparison]::Ordinal) `
                "Completed M3 closure command is missing '$requiredM308ClosureCommand'."
        }
        foreach ($requiredM308ClosureResult in @(
            'M3 complete and deactivated',
            'no repository milestone is active',
            'complete `player-equipment-grant` capability remains SwordMaster-owned',
            '`request-audit-context-consumer-contract` remains a possible-later candidate only',
            'M3 grants no continuing authorization for extraction, another capability, M4, or NovaCore changes',
            'M3-only local evidence was removed',
            'disposable resources and owned processes were zero',
            'Repository and whitespace checks passed'
        )) {
            Assert-Condition `
                $m308ClosureResult.Contains(
                    $requiredM308ClosureResult,
                    [StringComparison]::OrdinalIgnoreCase) `
                "Completed M3 closure result is missing '$requiredM308ClosureResult'."
        }

        foreach ($completedM3StatusPath in @(
            "AGENTS.md",
            "README.md",
            "docs/README.md",
            "docs/roadmap.md",
            "samples/SwordMaster/README.md"
        )) {
            $completedM3StatusContent = Get-Content `
                -LiteralPath $completedM3StatusPath `
                -Raw
            $normalizedCompletedM3StatusContent = [regex]::Replace(
                $completedM3StatusContent,
                '\s+',
                ' ')
            foreach ($requiredCompletedM3Text in @(
                'M3 is complete.',
                'player-equipment-grant',
                'request-audit-context-consumer-contract',
                'The complete player-equipment-grant capability remains SwordMaster-owned.',
                'M3 grants no continuing authorization for extraction, another capability, M4, or NovaCore changes.'
            )) {
                Assert-Condition `
                    $normalizedCompletedM3StatusContent.Contains(
                        $requiredCompletedM3Text,
                        [StringComparison]::OrdinalIgnoreCase) `
                    "$completedM3StatusPath is missing completed M3 state '$requiredCompletedM3Text'."
            }
            if ($activeGoalMilestones.Count -eq 0) {
                Assert-Condition `
                    $normalizedCompletedM3StatusContent.Contains(
                        'No repository milestone is active.',
                        [StringComparison]::OrdinalIgnoreCase) `
                    "$completedM3StatusPath must identify the current no-active-milestone state."
            }
            foreach ($staleCompletedM3Text in @(
                'M3 is active',
                'M3 remains active',
                'active at `M3-08`',
                'Only `M3-08-T02` is active',
                'Only M3-08-T02 is active',
                'Only `M3-08-T03` is active',
                'Only M3-08-T03 is active',
                'Do not start M3-08-T03',
                'M3 active goal'
            )) {
                Assert-Condition `
                    (-not $normalizedCompletedM3StatusContent.Contains(
                        $staleCompletedM3Text,
                        [StringComparison]::OrdinalIgnoreCase)) `
                    "$completedM3StatusPath contains stale completed-M3 state '$staleCompletedM3Text'."
            }
        }
    }

    if ($m3TaskStatuses["M3-06-T01"] -in @(
            "in_progress", "blocked", "complete")) {
        $m306EvidenceHeading = "## M3-06 Acceptance Evidence Record"
        Assert-Condition `
            $m3Goal.Content.Contains($m306EvidenceHeading) `
            "M3-06-T01 requires the frozen M3-06 acceptance evidence record."
        $m306EvidenceMatch = [regex]::Match(
            $m3Goal.Content,
            '(?ms)^## M3-06 Acceptance Evidence Record\r?\n(?<body>.*?)(?=^##\s+|\z)')
        Assert-Condition `
            $m306EvidenceMatch.Success `
            "M3-06 acceptance evidence record cannot be read."
        $m306Evidence = $m306EvidenceMatch.Groups["body"].Value
        foreach ($requiredM306Heading in @(
            "### Evidence Rules",
            "### Requirement-To-Evidence Matrix",
            "### Exact M3-06 Acceptance Commands",
            "### Frozen T02 Boundary",
            "### Frozen T03 Boundary"
        )) {
            Assert-Condition `
                $m306Evidence.Contains($requiredM306Heading) `
                "M3-06 acceptance evidence is missing '$requiredM306Heading'."
        }
        Assert-Condition `
            $m306Evidence.Contains(
                "| ID | Acceptance Requirement | Required Boundary | Planned Evidence | Exact Verification Command | Owning Task |") `
            "M3-06 acceptance evidence must retain its exact matrix header."
        foreach ($requiredM306EvidenceText in @(
            "M306-AE-01",
            "M306-AE-02",
            "M306-AE-03",
            "M306-AE-04",
            "M306-AE-05",
            "M306-AE-06",
            "real administrator login",
            "anonymous rejection",
            "player-token rejection",
            "missing-permission rejection",
            "canonical GUID",
            "grant history",
            "strong ETag",
            "exact replay",
            "changed fingerprint",
            "stale revision",
            "concurrent requests",
            "equip the granted item",
            "rollback blocked",
            "player unequip",
            "successful rollback",
            "rollback replay",
            "persistence reload",
            "actor",
            "reason",
            "UTC",
            "configuration version",
            "revision",
            "correlation ID",
            "immutable history",
            "Playwright desktop",
            "Playwright mobile",
            "conflict",
            "retry",
            "logout",
            "dual MySQL",
            "NovaGame",
            "SwordMaster",
            "deterministic cleanup",
            "No mock may substitute for a missing cross-boundary acceptance path.",
            "T02 uses the real composed SwordMaster.Api Program and its production registrations.",
            "T03 must use the same non-mock loopback API and disposable dual-MySQL fixture; a fake backend,",
            "T02 expected count: 3",
            "T03 expected count: 2",
            "The T02 command must fail unless exactly 3 tests execute with zero failed, skipped, or not-executed",
            "The T03 command must fail unless exactly 2 tests execute with zero failed, skipped, or pending",
            "pwsh -NoProfile -File scripts/verify.ps1 -Scope SwordMaster",
            "pnpm --dir admin --filter @swordmaster/admin-web-ele run test:e2e:equipment-grant"
        )) {
            Assert-Condition `
                $m306Evidence.Contains($requiredM306EvidenceText) `
                "M3-06 acceptance evidence is missing '$requiredM306EvidenceText'."
        }
        Assert-Condition `
            ([regex]::IsMatch(
                $m306Evidence,
                'T02 must not replace IPlayerEquipmentGrantAppService,\s+ICurrentSwordMasterAdministrator,\s+IPlayerEquipmentGrantAuditContextAccessor, authorization, GameConfig, audit, or persistence services\.')) `
            "M3-06 acceptance evidence must prohibit replacement of real T02 services."

        $m306ExpectedMatrixIds = @(
            "M306-AE-01", "M306-AE-02", "M306-AE-03",
            "M306-AE-04", "M306-AE-05", "M306-AE-06")
        $m306ActualMatrixIds = @(
            [regex]::Matches(
                $m306Evidence,
                '(?m)^\|[ \t]*(?<id>M306-AE-\d{2})[ \t]*\|') |
                ForEach-Object { $_.Groups["id"].Value })
        Assert-Condition `
            (($m306ActualMatrixIds -join ',') -eq
                ($m306ExpectedMatrixIds -join ',')) `
            "M3-06 acceptance evidence must define exactly the six ordered M306-AE matrix rows."

        $m306T02AcceptancePath = `
            "samples/SwordMaster/server/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantComposedApiAcceptanceTests.cs"
        $m306T02VerifierMarkerPattern = `
            '(?m)^function\s+Invoke-SwordMasterPlayerEquipmentGrantComposedApiAcceptance\b'
        if ($m3TaskStatuses["M3-06-T02"] -eq "pending") {
            Assert-Condition `
                (-not (Test-Path -LiteralPath $m306T02AcceptancePath)) `
                "M3-06-T02 acceptance source appeared before T02 activation."
            Assert-Condition `
                (-not [regex]::IsMatch(
                    (Get-Content -LiteralPath "scripts/verify.ps1" -Raw),
                    $m306T02VerifierMarkerPattern)) `
                "M3-06-T02 verifier integration appeared before T02 activation."
        }
        elseif ($m3TaskStatuses["M3-06-T02"] -in @("in_progress", "complete")) {
            Assert-Condition `
                (Test-Path -LiteralPath $m306T02AcceptancePath -PathType Leaf) `
                "Activated M3-06-T02 requires its composed API acceptance source."
            $m306VerifierSource = Get-Content -LiteralPath "scripts/verify.ps1" -Raw
            Assert-Condition `
                ([regex]::IsMatch($m306VerifierSource, $m306T02VerifierMarkerPattern)) `
                "Activated M3-06-T02 requires its composed API acceptance verifier."
            Assert-Condition `
                ([regex]::Matches(
                    $m306VerifierSource,
                    '(?m)^[ \t]*Invoke-SwordMasterPlayerEquipmentGrantComposedApiAcceptance[ \t]+`?[ \t]*\r?$').Count -eq 1) `
                "M3-06-T02 must invoke its composed API acceptance exactly once."
        }

        $m306T03AcceptancePaths = @(
            "samples/SwordMaster/admin/playwright.config.ts",
            "samples/SwordMaster/admin/e2e/player-equipment-grants.spec.ts")
        $m306T03VerifierMarkerPattern = `
            '(?m)^function\s+Invoke-SwordMasterPlayerEquipmentGrantPlaywrightAcceptance\b'
        if ($m3TaskStatuses["M3-06-T03"] -eq "pending") {
            foreach ($m306T03AcceptancePath in $m306T03AcceptancePaths) {
                Assert-Condition `
                    (-not (Test-Path -LiteralPath $m306T03AcceptancePath)) `
                    "M3-06-T03 acceptance source '$m306T03AcceptancePath' appeared before T03 activation."
            }
            Assert-Condition `
                (-not [regex]::IsMatch(
                    (Get-Content -LiteralPath "scripts/verify.ps1" -Raw),
                    $m306T03VerifierMarkerPattern)) `
                "M3-06-T03 verifier integration appeared before T03 activation."
        }
        elseif ($m3TaskStatuses["M3-06-T03"] -in @("in_progress", "complete")) {
            foreach ($m306T03AcceptancePath in $m306T03AcceptancePaths) {
                Assert-Condition `
                    (Test-Path -LiteralPath $m306T03AcceptancePath -PathType Leaf) `
                    "Activated M3-06-T03 requires '$m306T03AcceptancePath'."
            }
            $m306VerifierSource = Get-Content -LiteralPath "scripts/verify.ps1" -Raw
            Assert-Condition `
                ([regex]::IsMatch($m306VerifierSource, $m306T03VerifierMarkerPattern)) `
                "Activated M3-06-T03 requires its Playwright acceptance verifier."
            Assert-Condition `
                ([regex]::Matches(
                    $m306VerifierSource,
                    '(?m)^[ \t]*Invoke-SwordMasterPlayerEquipmentGrantPlaywrightAcceptance[ \t]+`?[ \t]*\r?$').Count -eq 1) `
                "M3-06-T03 must invoke its Playwright acceptance exactly once."

            $m306T03Package = Get-Content `
                -LiteralPath "samples/SwordMaster/admin/package.json" `
                -Raw | ConvertFrom-Json
            $m306T03Script = $m306T03Package.scripts.PSObject.Properties[
                "test:e2e:equipment-grant"]
            Assert-Condition `
                ($null -ne $m306T03Script -and
                    [string]$m306T03Script.Value -ceq
                        "playwright test --config playwright.config.ts") `
                "M3-06-T03 must expose its exact Playwright package script."
            Assert-Condition `
                ($m306T03Package.devDependencies.PSObject.Properties.Name -contains
                    "@playwright/test") `
                "M3-06-T03 must declare the Playwright test dependency."
            $m306T03Source = Get-Content `
                -LiteralPath "samples/SwordMaster/admin/e2e/player-equipment-grants.spec.ts" `
                -Raw
            Assert-Condition `
                ([regex]::Matches($m306T03Source, '(?m)^test\(').Count -eq 2) `
                "M3-06-T03 must define exactly two browser tests."
            foreach ($requiredM306T03Evidence in @(
                "route.fetch()",
                "route.abort(",
                "SWORDMASTER_ACCEPTANCE_",
                "RollbackBlockedItemEquipped",
                "ConcurrencyConflict",
                "permission",
                "logout",
                "390",
                "1440"
            )) {
                Assert-Condition `
                    $m306T03Source.Contains($requiredM306T03Evidence) `
                    "M3-06-T03 Playwright evidence is missing '$requiredM306T03Evidence'."
            }
        }
    }

    $requiredM302TaskIds = @(
        "M3-02-T01",
        "M3-02-T02",
        "M3-02-T03",
        "M3-02-T04",
        "M3-02-T05",
        "M3-02-T06"
    )
    foreach ($requiredM302TaskId in $requiredM302TaskIds) {
        Assert-Condition `
            ($m3TaskStatuses.Keys -contains $requiredM302TaskId) `
            "M3 task register is missing '$requiredM302TaskId'."
    }
    if ($m3Goal.StageStatuses["M3-02"] -eq "complete") {
        foreach ($requiredM302TaskId in $requiredM302TaskIds) {
            Assert-Condition `
                ($m3TaskStatuses[$requiredM302TaskId] -eq "complete") `
                "Completed M3-02 requires completed task '$requiredM302TaskId'."
        }
    }
    if ($m3Goal.StageStatuses["M3-03"] -eq "pending") {
        foreach ($prematureM303Path in @(
            "samples/SwordMaster/server/src/SwordMaster.Domain/PlayerAssets/PlayerEquipmentGrant.cs",
            "samples/SwordMaster/server/src/SwordMaster.Domain/PlayerAssets/IPlayerEquipmentGrantRepository.cs",
            "samples/SwordMaster/server/src/SwordMaster.Application.Contracts/PlayerAssets/IPlayerEquipmentGrantAppService.cs",
            "samples/SwordMaster/server/src/SwordMaster.Application/PlayerAssets/PlayerEquipmentGrantAppService.cs",
            "samples/SwordMaster/server/src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerEquipmentGrantConfiguration.cs",
            "samples/SwordMaster/server/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantsController.cs",
            "samples/SwordMaster/admin"
        )) {
            Assert-Condition `
                (-not (Test-Path -LiteralPath $prematureM303Path)) `
                "M3 implementation path '$prematureM303Path' appeared before M3-03 activation."
        }
    }

    if ($m3Goal.StageStatuses["M3-05"] -in @(
            "in_progress", "blocked", "complete")) {
        $swordMasterAdminRoot = "samples/SwordMaster/admin"
        foreach ($requiredSwordMasterAdminPath in @(
            "$swordMasterAdminRoot/.env.development",
            "$swordMasterAdminRoot/.env.example",
            "$swordMasterAdminRoot/.env.production",
            "$swordMasterAdminRoot/LICENSE",
            "$swordMasterAdminRoot/index.html",
            "$swordMasterAdminRoot/openapi-ts.config.mjs",
            "$swordMasterAdminRoot/oxfmt.config.mts",
            "$swordMasterAdminRoot/oxlint.config.mts",
            "$swordMasterAdminRoot/package.json",
            "$swordMasterAdminRoot/src/app.vue",
            "$swordMasterAdminRoot/src/bootstrap.ts",
            "$swordMasterAdminRoot/src/main.ts",
            "$swordMasterAdminRoot/src/preferences.ts",
            "$swordMasterAdminRoot/tsconfig.json",
            "$swordMasterAdminRoot/tsconfig.node.json",
            "$swordMasterAdminRoot/vite.config.mts"
        )) {
            Assert-Condition `
                (Test-Path -LiteralPath $requiredSwordMasterAdminPath -PathType Leaf) `
                "M3-05 SwordMaster admin scaffold is missing '$requiredSwordMasterAdminPath'."
        }

        $swordMasterAdminPackageContent = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/package.json" `
            -Raw
        $swordMasterAdminPackage = $swordMasterAdminPackageContent |
            ConvertFrom-Json
        Assert-Condition `
            ([string]$swordMasterAdminPackage.name -eq
                "@swordmaster/admin-web-ele" -and
                [bool]$swordMasterAdminPackage.private -and
                [string]$swordMasterAdminPackage.license -eq "MIT" -and
                [string]$swordMasterAdminPackage.packageManager -eq
                    "pnpm@10.33.4" -and
                [string]$swordMasterAdminPackage.engines.node -eq
                    "^22.18.0 || ^24.0.0") `
            "SwordMaster admin package identity or pinned toolchain is invalid."
        $expectedSwordMasterAdminScripts = [ordered]@{
            "build" = "pnpm vite build --mode production"
            "contract:generate" = "openapi-ts --file openapi-ts.config.mjs"
            "dev" = "pnpm vite --mode development"
            "format:check" = "oxfmt --config oxfmt.config.mts --check ."
            "lint" = "oxlint --deny-warnings e2e openapi-ts.config.mjs oxfmt.config.mts oxlint.config.mts playwright.config.ts src vite.config.mts vitest.config.mts"
            "preview" = "vite preview --host 127.0.0.1"
            "typecheck" = "vue-tsc --noEmit --skipLibCheck"
        }
        foreach ($expectedSwordMasterAdminScript in
            $expectedSwordMasterAdminScripts.GetEnumerator()) {
            $scriptProperty = $swordMasterAdminPackage.scripts.PSObject.Properties[
                $expectedSwordMasterAdminScript.Key]
            Assert-Condition `
                ($null -ne $scriptProperty -and
                    [string]$scriptProperty.Value -ceq
                        [string]$expectedSwordMasterAdminScript.Value) `
                "SwordMaster admin script '$($expectedSwordMasterAdminScript.Key)' must remain exactly '$($expectedSwordMasterAdminScript.Value)'."
        }
        Assert-Condition `
            ($swordMasterAdminPackage.dependencies.PSObject.Properties.Name -contains
                "@vben/types") `
            "SwordMaster admin must explicitly declare the Vben global types package."
        Assert-Condition `
            (-not $swordMasterAdminPackageContent.Contains(
                '"@vben/web-ele"')) `
            "SwordMaster admin must not depend on the generic web-ele application."

        $adminWorkspace = Get-Content -LiteralPath "admin/pnpm-workspace.yaml" -Raw
        Assert-Condition `
            ($adminWorkspace -match
                '(?m)^[ \t]*-[ \t]+\.\./samples/SwordMaster/admin[ \t]*$') `
            "Pinned admin workspace must list the independent SwordMaster app."
        $adminLock = Get-Content -LiteralPath "admin/pnpm-lock.yaml" -Raw
        Assert-Condition `
            $adminLock.Contains("../samples/SwordMaster/admin:") `
            "Admin lockfile is missing the SwordMaster workspace importer."

        $swordMasterAdminVite = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/vite.config.mts" `
            -Raw
        Assert-Condition `
            ($swordMasterAdminVite.Contains("injectGlobalScss: false") -and
                $swordMasterAdminVite.Contains("injectMetadata: false") -and
                $swordMasterAdminVite.Contains("SWORDMASTER_ADMIN_PORT") -and
                $swordMasterAdminVite.Contains("SWORDMASTER_API_URL")) `
            "SwordMaster external-workspace Vite composition is not explicit."
        $swordMasterAdminDevelopmentEnvironment = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/.env.development" `
            -Raw
        Assert-Condition `
            $swordMasterAdminDevelopmentEnvironment.Contains(
                "VITE_APP_NAMESPACE=swordmaster-admin") `
            "SwordMaster admin must use its isolated storage namespace."
        Assert-Condition `
            ((Get-FileHash -LiteralPath "$swordMasterAdminRoot/LICENSE" -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath "admin/LICENSE" -Algorithm SHA256).Hash) `
            "SwordMaster admin must preserve the pinned Vben MIT license byte-for-byte."
    }

    if ($m3TaskStatuses["M3-05-T02"] -in @(
            "in_progress", "blocked", "complete")) {
        $swordMasterAdminRoot = "samples/SwordMaster/admin"
        foreach ($requiredSwordMasterAuthPath in @(
            "$swordMasterAdminRoot/vitest.config.mts",
            "$swordMasterAdminRoot/src/storage-namespace.ts",
            "$swordMasterAdminRoot/src/api/api-result.ts",
            "$swordMasterAdminRoot/src/api/administrator-auth-response-interceptor.ts",
            "$swordMasterAdminRoot/src/api/nova-game-contracts.ts",
            "$swordMasterAdminRoot/src/api/request.ts",
            "$swordMasterAdminRoot/src/api/core/auth.ts",
            "$swordMasterAdminRoot/src/api/core/user.ts",
            "$swordMasterAdminRoot/src/store/auth.ts",
            "$swordMasterAdminRoot/src/router/access.ts",
            "$swordMasterAdminRoot/src/router/guard.ts",
            "$swordMasterAdminRoot/src/router/permission.ts",
            "$swordMasterAdminRoot/src/router/routes/modules/player-equipment-grants.ts",
            "$swordMasterAdminRoot/src/features/player-equipment-grants/permissions.ts",
            "$swordMasterAdminRoot/src/layouts/basic.vue",
            "$swordMasterAdminRoot/src/layouts/auth.vue",
            "$swordMasterAdminRoot/src/locales/index.ts",
            "$swordMasterAdminRoot/src/views/_core/authentication/login.vue",
            "$swordMasterAdminRoot/src/api/request.test.ts",
            "$swordMasterAdminRoot/src/store/auth.test.ts",
            "$swordMasterAdminRoot/src/router/auth-routing.test.ts",
            "$swordMasterAdminRoot/src/storage-namespace.test.ts",
            "$swordMasterAdminRoot/src/main.test.ts",
            "$swordMasterAdminRoot/src/bootstrap.test.ts"
        )) {
            Assert-Condition `
                (Test-Path -LiteralPath $requiredSwordMasterAuthPath -PathType Leaf) `
                "M3-05-T02 authentication composition is missing '$requiredSwordMasterAuthPath'."
        }

        $swordMasterAdminPackage = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/package.json" `
            -Raw | ConvertFrom-Json
        $swordMasterAdminNodeTsconfig = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/tsconfig.node.json" `
            -Raw | ConvertFrom-Json
        Assert-Condition `
            ($swordMasterAdminNodeTsconfig.include -ccontains
                "vitest.config.mts") `
            "SwordMaster admin must type-check vitest.config.mts through tsconfig.node.json."
        $expectedSwordMasterAuthScripts = [ordered]@{
            "test:auth" = "vitest run --config vitest.config.mts src/api/api-result.test.ts src/api/core/auth.test.ts src/api/core/user.test.ts src/api/request.test.ts src/store/auth.test.ts src/router/auth-routing.test.ts src/storage-namespace.test.ts src/main.test.ts src/bootstrap.test.ts"
            "test:unit" = "vitest run --config vitest.config.mts"
        }
        foreach ($expectedSwordMasterAuthScript in
            $expectedSwordMasterAuthScripts.GetEnumerator()) {
            $scriptProperty = $swordMasterAdminPackage.scripts.PSObject.Properties[
                $expectedSwordMasterAuthScript.Key]
            Assert-Condition `
                ($null -ne $scriptProperty -and
                    [string]$scriptProperty.Value -ceq
                        [string]$expectedSwordMasterAuthScript.Value) `
                "SwordMaster admin script '$($expectedSwordMasterAuthScript.Key)' must remain exactly '$($expectedSwordMasterAuthScript.Value)'."
        }
        foreach ($requiredSwordMasterAuthDependency in @(
            "@vben/access",
            "@vben/common-ui",
            "@vben/constants",
            "@vben/layouts",
            "@vben/locales",
            "@vben/request",
            "@vben/stores",
            "@vueuse/core",
            "dayjs",
            "pinia",
            "vue-router"
        )) {
            Assert-Condition `
                ($swordMasterAdminPackage.dependencies.PSObject.Properties.Name -contains
                    $requiredSwordMasterAuthDependency) `
                "SwordMaster admin is missing direct dependency '$requiredSwordMasterAuthDependency'."
        }
        foreach ($forbiddenSwordMasterAuthDependency in @(
            "@vben/plugins",
            "@vben/web-ele"
        )) {
            Assert-Condition `
                (-not ($swordMasterAdminPackage.dependencies.PSObject.Properties.Name -contains
                        $forbiddenSwordMasterAuthDependency)) `
                "M3-05-T02 must not add '$forbiddenSwordMasterAuthDependency'."
        }

        foreach ($boundedSwordMasterCompositionPath in @(
            "src/app.vue",
            "src/api/api-result.ts",
            "src/api/core/auth.ts",
            "src/api/core/index.ts",
            "src/store/index.ts",
            "src/router/access.ts",
            "src/layouts/basic.vue",
            "src/layouts/index.ts",
            "src/views/_core/fallback/coming-soon.vue",
            "src/views/_core/fallback/forbidden.vue",
            "src/views/_core/fallback/not-found.vue"
        )) {
            $genericCompositionPath =
                "admin/apps/web-ele/" + $boundedSwordMasterCompositionPath
            $swordMasterCompositionPath =
                "samples/SwordMaster/admin/" + $boundedSwordMasterCompositionPath
            Assert-Condition `
                ($m307SwordMasterAdminObjects.ContainsKey(
                        $swordMasterCompositionPath) -and
                    $m307GenericAdminObjects.ContainsKey(
                        $genericCompositionPath) -and
                    ($m307SwordMasterAdminObjects[
                        $swordMasterCompositionPath] -eq
                        $m307GenericAdminObjects[
                            $genericCompositionPath])) `
                "Completed-M3 bounded SwordMaster composition copy '$boundedSwordMasterCompositionPath' is missing or differs from its generic snapshot."
        }

        $swordMasterAuthContracts = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/src/api/nova-game-contracts.ts" `
            -Raw
        Assert-Condition `
            (-not $swordMasterAuthContracts.Contains(
                "refreshToken",
                [StringComparison]::OrdinalIgnoreCase)) `
            "SwordMaster JavaScript contracts must never expose the HttpOnly refresh token."
        $swordMasterProductionAuthFiles = @(
            Get-ChildItem `
                -LiteralPath "$swordMasterAdminRoot/src" `
                -Recurse `
                -File |
                Where-Object {
                    $_.Name -notmatch '\.test\.ts$' -and
                    $_.Extension -in @(".ts", ".vue")
                }
        )
        $swordMasterProductionAuthContent = ($swordMasterProductionAuthFiles |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
        Assert-Condition `
            ($swordMasterProductionAuthContent -notmatch '\bsetRefreshToken\s*\(') `
            "SwordMaster admin must leave the shared refresh-token store field unused."
        Assert-Condition `
            ($swordMasterProductionAuthContent -notmatch '\b(localStorage|sessionStorage)\b') `
            "SwordMaster authentication must not create JavaScript-readable credential storage outside Vben stores."
        Assert-Condition `
            ($swordMasterProductionAuthContent.Contains(
                    "SwordMaster.PlayerAssets.EquipmentGrants.Manage") -and
                $swordMasterProductionAuthContent.Contains(
                    "/player-equipment-grants") -and
                $swordMasterProductionAuthContent.Contains(
                    "withCredentials: true")) `
            "SwordMaster authentication, permission, home route, or cookie request policy is incomplete."
        Assert-Condition `
            (-not (Test-Path -LiteralPath "$swordMasterAdminRoot/src/adapter")) `
            "M3-05-T02 must not copy the generic admin component adapters."
    }

    if ($m3TaskStatuses["M3-05-T03"] -in @(
            "in_progress", "blocked", "complete")) {
        $swordMasterAdminRoot = "samples/SwordMaster/admin"
        foreach ($requiredSwordMasterEquipmentGrantPath in @(
            "$swordMasterAdminRoot/src/api/player-equipment-grants.ts",
            "$swordMasterAdminRoot/src/api/player-equipment-grants.test.ts",
            "$swordMasterAdminRoot/src/features/player-equipment-grants/model.ts",
            "$swordMasterAdminRoot/src/features/player-equipment-grants/model.test.ts",
            "$swordMasterAdminRoot/src/features/player-equipment-grants/state.ts"
        )) {
            Assert-Condition `
                (Test-Path -LiteralPath $requiredSwordMasterEquipmentGrantPath -PathType Leaf) `
                "M3-05-T03 equipment-grant transport or interaction state is missing '$requiredSwordMasterEquipmentGrantPath'."
        }

        $swordMasterAdminPackage = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/package.json" `
            -Raw | ConvertFrom-Json
        $expectedSwordMasterEquipmentGrantScripts = [ordered]@{
            "test:equipment-grant-transport" = "vitest run --config vitest.config.mts src/api/player-equipment-grants.test.ts"
            "test:equipment-grant-model" = "vitest run --config vitest.config.mts src/features/player-equipment-grants/model.test.ts"
        }
        foreach ($expectedSwordMasterEquipmentGrantScript in
            $expectedSwordMasterEquipmentGrantScripts.GetEnumerator()) {
            $scriptProperty = $swordMasterAdminPackage.scripts.PSObject.Properties[
                $expectedSwordMasterEquipmentGrantScript.Key]
            Assert-Condition `
                ($null -ne $scriptProperty -and
                    [string]$scriptProperty.Value -ceq
                        [string]$expectedSwordMasterEquipmentGrantScript.Value) `
                "SwordMaster admin script '$($expectedSwordMasterEquipmentGrantScript.Key)' must remain exactly '$($expectedSwordMasterEquipmentGrantScript.Value)'."
        }
    }

    if ($m3TaskStatuses["M3-05-T04"] -in @(
            "in_progress", "blocked", "complete")) {
        $swordMasterAdminRoot = "samples/SwordMaster/admin"
        foreach ($requiredSwordMasterEquipmentGrantViewPath in @(
            "$swordMasterAdminRoot/src/views/player-equipment-grants/index.vue",
            "$swordMasterAdminRoot/src/views/player-equipment-grants/index.test.ts"
        )) {
            Assert-Condition `
                (Test-Path -LiteralPath $requiredSwordMasterEquipmentGrantViewPath -PathType Leaf) `
                "M3-05-T04 equipment-grant operations view is missing '$requiredSwordMasterEquipmentGrantViewPath'."
        }

        $swordMasterEquipmentGrantRoute = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/src/router/routes/modules/player-equipment-grants.ts" `
            -Raw
        Assert-Condition `
            ($swordMasterEquipmentGrantRoute.Contains(
                    "component: () => import('#/views/player-equipment-grants/index.vue'),") -and
                -not $swordMasterEquipmentGrantRoute.Contains(
                    "#/views/_core/fallback/coming-soon.vue")) `
            "M3-05-T04 equipment-grant route must render the game-owned operations view."

        $swordMasterAdminPackage = Get-Content `
            -LiteralPath "$swordMasterAdminRoot/package.json" `
            -Raw | ConvertFrom-Json
        $expectedSwordMasterEquipmentGrantViewScripts = [ordered]@{
            "test:equipment-grant-view" = "vitest run --config vitest.config.mts src/views/player-equipment-grants/index.test.ts"
        }
        foreach ($expectedSwordMasterEquipmentGrantViewScript in
            $expectedSwordMasterEquipmentGrantViewScripts.GetEnumerator()) {
            $scriptProperty = $swordMasterAdminPackage.scripts.PSObject.Properties[
                $expectedSwordMasterEquipmentGrantViewScript.Key]
            Assert-Condition `
                ($null -ne $scriptProperty -and
                    [string]$scriptProperty.Value -ceq
                        [string]$expectedSwordMasterEquipmentGrantViewScript.Value) `
                "SwordMaster admin script '$($expectedSwordMasterEquipmentGrantViewScript.Key)' must remain exactly '$($expectedSwordMasterEquipmentGrantViewScript.Value)'."
        }
    }

    if ($m3TaskStatuses["M3-05-T02"] -in @(
            "in_progress", "blocked", "complete")) {
        $swordMasterAdminRoot = "samples/SwordMaster/admin"
        $swordMasterAdminFullRoot = [System.IO.Path]::GetFullPath(
            $swordMasterAdminRoot)
        $expectedM305T02ProductionPaths = @(
            "src/api/administrator-auth-response-interceptor.ts",
            "src/api/api-result.ts",
            "src/api/core/auth.ts",
            "src/api/core/index.ts",
            "src/api/core/user.ts",
            "src/api/index.ts",
            "src/api/nova-game-contracts.ts",
            "src/api/request.ts",
            "src/app.vue",
            "src/bootstrap.ts",
            "src/features/player-equipment-grants/permissions.ts",
            "src/generated/player-equipment-grants/types.gen.ts",
            "src/layouts/auth.vue",
            "src/layouts/basic.vue",
            "src/layouts/index.ts",
            "src/locales/index.ts",
            "src/locales/langs/en-US/page.json",
            "src/locales/langs/zh-CN/page.json",
            "src/main.ts",
            "src/preferences.ts",
            "src/router/access.ts",
            "src/router/guard.ts",
            "src/router/index.ts",
            "src/router/permission.ts",
            "src/router/routes/core.ts",
            "src/router/routes/index.ts",
            "src/router/routes/modules/player-equipment-grants.ts",
            "src/storage-namespace.ts",
            "src/store/auth.ts",
            "src/store/index.ts",
            "src/views/_core/authentication/login.vue",
            "src/views/_core/fallback/coming-soon.vue",
            "src/views/_core/fallback/forbidden.vue",
            "src/views/_core/fallback/not-found.vue"
        )
        $actualSwordMasterAdminProductionPaths = @(
            Get-ChildItem `
                -LiteralPath "$swordMasterAdminRoot/src" `
                -Recurse `
                -File |
                ForEach-Object {
                    [System.IO.Path]::GetRelativePath(
                        $swordMasterAdminFullRoot,
                        $_.FullName).Replace("\", "/")
                } |
                Where-Object {
                    $_ -notmatch '(?i)(^|/)__tests__(/|$)' -and
                    $_ -notmatch '(?i)\.(?:test|spec)\.[^/]+$'
                } |
                Sort-Object
        )
        $missingM305T02ProductionPaths = @(
            $expectedM305T02ProductionPaths |
                Where-Object {
                    $actualSwordMasterAdminProductionPaths -cnotcontains $_
                })
        Assert-Condition `
            ($missingM305T02ProductionPaths.Count -eq 0) `
            "SwordMaster admin is missing frozen M3-05-T02 production source:`n$($missingM305T02ProductionPaths -join "`n")"

        $allowM305T03Production = $m3TaskStatuses["M3-05-T03"] -in @(
            "in_progress", "blocked", "complete")
        $allowM305T04Production = $m3TaskStatuses["M3-05-T04"] -in @(
            "in_progress", "blocked", "complete")
        $unexpectedSwordMasterAdminProductionPaths =
            [System.Collections.Generic.List[string]]::new()
        foreach ($productionPath in $actualSwordMasterAdminProductionPaths) {
            if ($expectedM305T02ProductionPaths -ccontains $productionPath) {
                continue
            }

            $isM305T03ProductionPath =
                $productionPath -ceq
                    "src/api/player-equipment-grants.ts" -or
                $productionPath -cmatch
                    '^src/api/player-equipment-grants/.+\.ts$' -or
                $productionPath -ceq
                    "src/features/player-equipment-grants/model.ts" -or
                $productionPath -ceq
                    "src/features/player-equipment-grants/state.ts" -or
                $productionPath -ceq
                    "src/store/player-equipment-grants.ts"
            if ($allowM305T03Production -and $isM305T03ProductionPath) {
                continue
            }

            $isM305T04ProductionPath = $productionPath -cmatch
                '^src/views/player-equipment-grants/.+\.(?:ts|vue)$'
            if ($allowM305T04Production -and $isM305T04ProductionPath) {
                continue
            }

            $unexpectedSwordMasterAdminProductionPaths.Add($productionPath)
        }
        Assert-Condition `
            ($unexpectedSwordMasterAdminProductionPaths.Count -eq 0) `
            "SwordMaster admin production source exceeds the active M3-05 task boundary (T03=$($m3TaskStatuses['M3-05-T03']), T04=$($m3TaskStatuses['M3-05-T04'])):`n$($unexpectedSwordMasterAdminProductionPaths -join "`n")"
    }

    if ($m3Goal.ExecutionAuthorized -and
        $m35Goal.Status -notin @("in_progress", "blocked", "complete")) {
        $currentM3TaskId = $activeM3TaskRows[0].Groups["id"].Value
        foreach ($currentStatusPath in @(
            "AGENTS.md",
            "README.md",
            "docs/README.md",
            "docs/roadmap.md",
            "samples/SwordMaster/README.md"
        )) {
            $currentStatusContent = Get-Content -LiteralPath $currentStatusPath -Raw
            foreach ($requiredM3CurrentText in @(
                "player-equipment-grant",
                $m3Goal.ActiveStage,
                $currentM3TaskId
            )) {
                Assert-Condition `
                    $currentStatusContent.Contains($requiredM3CurrentText) `
                    "$currentStatusPath is missing active M3 state '$requiredM3CurrentText'."
            }
            Assert-Condition `
                (-not $currentStatusContent.Contains(
                    "no repository milestone is active",
                    [StringComparison]::OrdinalIgnoreCase)) `
                "$currentStatusPath contains a stale no-active-milestone statement."
        }

        $frameworkM3Paths = @(
            & git diff --name-only `
                "ba83e4af0d494da4176ee922c2caaf24f5fce905" `
                -- `
                "server/src" `
                "server/tests" `
                "clients/cocos" `
                "admin/apps/web-ele")
        Assert-Condition `
            ($LASTEXITCODE -eq 0) `
            "Unable to compare M3 framework paths with the approved baseline."
        $untrackedFrameworkM3Paths = @(
            & git ls-files `
                --others `
                --exclude-standard `
                -- `
                "server/src" `
                "server/tests" `
                "clients/cocos" `
                "admin/apps/web-ele")
        Assert-Condition `
            ($LASTEXITCODE -eq 0) `
            "Unable to inspect untracked M3 framework paths."
        Assert-Condition `
            ($frameworkM3Paths.Count -eq 0 -and
                $untrackedFrameworkM3Paths.Count -eq 0) `
            "M3 game capability source must not change NovaGame server, client, or generic-admin feature paths."
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
    Test-M35ExternalNovaCoreCheckout
    Test-NovaGameProcessJobLifecycle
    Test-AdminManagementArtifactPolicy
    Test-AdminManagementSuccessfulCleanupRetention
    Test-AdminEndToEndHostCapacityAdmissionPolicy
    Assert-Condition (Test-Path -LiteralPath "NovaGame.sln" -PathType Leaf) "Root solution 'NovaGame.sln' is missing."

    foreach ($script in @(
        "scripts/bootstrap.ps1",
        "scripts/verify.ps1",
        "scripts/run-dev.ps1",
        "scripts/process-environment.ps1",
        "scripts/database-environment.ps1",
        "scripts/admin-access-environment.ps1",
        "scripts/update-swordmaster-player-assets-contract.ps1",
        "scripts/update-swordmaster-player-equipment-grant-contract.ps1"
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
                '(?m)^[ \t]*"--force-evaluate",\r?$').Count -eq 2) `
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
            '(?m)^[ \t]*\$previous(?:Seed|Runtime)?Environment[ \t]*=[ \t]*Get-NovaGameProcessEnvironmentSnapshot[ \t]*`?\r?$').Count -eq 5) `
        "Verification must use null-preserving snapshots in all five local lifecycles."
    Assert-Condition `
        ([regex]::Matches(
            $verifyScript,
            '(?m)^\s*Restore-NovaGameProcessEnvironmentSnapshot(?:\s|$)').Count -eq 5) `
        "Verification must use null-preserving restoration in all five local lifecycles."

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
    param(
        [switch]$RunDbMigratorSeed,
        [scriptblock]$AdditionalVerification,
        [switch]$SkipSolutionTests
    )

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

        if ($null -ne $AdditionalVerification) {
            & $AdditionalVerification
        }
        if (-not $SkipSolutionTests) {
            Invoke-CheckedCommand "dotnet" @(
                "test",
                "server/NovaGame.sln",
                "--no-build",
                "--no-restore",
                "--verbosity", "minimal",
                "--filter", "FullyQualifiedName!~AdminAccessManagementComposedApiAcceptanceTests",
                "-p:NuGetAudit=false"
            )
        }
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

function Invoke-AdminManagementComposedApiAcceptance {
    param(
        [Parameter(Mandatory)][string]$TestProject,
        [Parameter(Mandatory)][string]$Filter,
        [Parameter(Mandatory)][string]$ResultName
    )

    Invoke-CheckedCommand "docker" @("compose", "version")
    Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")

    $projectName = "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $adminManagementRoot = Join-Path $repositoryRoot ".local\admin-management"
    $runRoot = Join-Path $adminManagementRoot $projectName
    $testEnvironmentFile = Join-Path $runRoot "database.env"
    $composedMarker = "novagame-admin-management-composed-v1"
    $operationError = $null
    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    $cleanupRequired = $false
    $processJobHandle = [IntPtr]::Zero
    $ownedProcessIdentities = @()
    $dbMigratorProcess = $null
    $apiProcess = $null
    $previousAdminAccessEnvironment = $null
    $previousDatabaseEnvironment = $null
    $testEnvironment = $null
    $composeArguments = @()
    $publishedPort = 0
    $apiPort = 0
    $connectionString = $null
    $bootstrapUserName = $null
    $bootstrapPassword = $null
    $dbMigratorStandardOutputPath = $null
    $dbMigratorStandardErrorPath = $null
    $apiStandardOutputPath = $null
    $apiStandardErrorPath = $null
    $safeApiFailureTypes = @()
    $adminAccessEnvironment = [ordered]@{}
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
        "NovaGame__AdminAccess__Jwt__Issuer",
        "NovaGame__AdminAccess__Jwt__Audience",
        "NovaGame__AdminAccess__Jwt__ClockSkew",
        "NovaGame__AdminAccess__LoginRateLimit__PermitLimit",
        "NovaGame__AdminAccess__LoginRateLimit__Window",
        "NovaGame__AdminAccess__LoginRateLimit__QueueLimit",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_URL",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_MYSQL_CONNECTION_STRING",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_PROJECT_NAME",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DISPOSABLE_MARKER",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_BOOTSTRAP_USER_NAME",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_BOOTSTRAP_PASSWORD",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_STDOUT_PATH",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_STDERR_PATH",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DBMIGRATOR_STDOUT_PATH",
        "NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DBMIGRATOR_STDERR_PATH"
    )
    $previousRuntimeEnvironment = Get-NovaGameProcessEnvironmentSnapshot `
        -Names $runtimeEnvironmentNames

    try {
        New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
        $processJobHandle = New-NovaGameProcessJob
        Write-NovaGameDatabaseEnvironmentFile `
            -Path $testEnvironmentFile `
            -Database "novagame_composed" `
            -User "novagame_composed" `
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
                Select-Object -Last 1)
        Assert-Condition `
            ($publishedEndpoint.Count -eq 1) `
            "Unable to resolve the composed API MySQL host port."
        $publishedPort = [int]([regex]::Match(
                [string]$publishedEndpoint[0],
                ":(\d+)\s*$").Groups[1].Value)
        Assert-Condition `
            ($publishedPort -gt 0) `
            "Docker did not allocate a valid composed API MySQL host port."

        $connectionString = New-NovaGameMySqlConnectionString `
            -Environment $testEnvironment `
            -Port $publishedPort
        $env:NOVAGAME_TEST_MYSQL_CONNECTION_STRING = $connectionString
        $env:NOVAGAME_MYSQL_MIGRATION_CONNECTION_STRING = $connectionString
        $env:ConnectionStrings__Default = $connectionString

        $bootstrapUserName = "composed-admin"
        $bootstrapPassword = "Composed-$projectName-Password-123!"
        $adminAccessEnvironment = [ordered]@{
            NovaGame__AdminAccess__Jwt__SigningKey = New-NovaGameAdminAccessSigningKey
            NovaGame__AdminAccess__Seed__UserName = $bootstrapUserName
            NovaGame__AdminAccess__Seed__Password = $bootstrapPassword
            NovaGame__AdminAccess__Seed__DisplayName = "Composed Administrator"
        }
        $previousAdminAccessEnvironment = Set-NovaGameAdminAccessProcessEnvironment `
            -Values $adminAccessEnvironment
        $runtimeSettings = [ordered]@{
            ASPNETCORE_ENVIRONMENT = "Development"
            DOTNET_ENVIRONMENT = "Development"
            NovaCore__Web__ForwardedHeaders__IsEnabled = "true"
            NovaCore__Web__ForwardedHeaders__KnownProxies__0 = "127.0.0.1"
            NovaGame__AdminAccess__Jwt__Issuer = "NovaGame.AdminManagement.Composed"
            NovaGame__AdminAccess__Jwt__Audience = "NovaGame.AdminManagement.Composed"
            NovaGame__AdminAccess__Jwt__ClockSkew = "00:00:00"
            NovaGame__AdminAccess__LoginRateLimit__PermitLimit = "100"
            NovaGame__AdminAccess__LoginRateLimit__Window = "00:01:00"
            NovaGame__AdminAccess__LoginRateLimit__QueueLimit = "0"
        }
        foreach ($runtimeSetting in $runtimeSettings.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                [string]$runtimeSetting.Key,
                [string]$runtimeSetting.Value,
                "Process")
        }

        $dbMigratorStandardOutputPath = Join-Path $runRoot "dbmigrator.stdout.log"
        $dbMigratorStandardErrorPath = Join-Path $runRoot "dbmigrator.stderr.log"
        $dbMigratorStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $dbMigratorStartInfo.WorkingDirectory = $repositoryRoot
        $dbMigratorStartInfo.UseShellExecute = $false
        $dbMigratorStartInfo.CreateNoWindow = $true
        $dbMigratorStartInfo.RedirectStandardOutput = $false
        $dbMigratorStartInfo.RedirectStandardError = $false
        $dbMigratorStartInfo.Environment["NOVAGAME_VERIFICATION_STDOUT_PATH"] =
            $dbMigratorStandardOutputPath
        $dbMigratorStartInfo.Environment["NOVAGAME_VERIFICATION_STDERR_PATH"] =
            $dbMigratorStandardErrorPath
        $dbMigratorStartInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        foreach ($dbMigratorArgument in @(
                "-NoProfile",
                "-Command",
                '$stdoutPath = $env:NOVAGAME_VERIFICATION_STDOUT_PATH; ' +
                '$stderrPath = $env:NOVAGAME_VERIFICATION_STDERR_PATH; ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDOUT_PATH", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDERR_PATH", $null, "Process"); ' +
                '& dotnet run --project server/src/NovaGame.DbMigrator/NovaGame.DbMigrator.csproj --no-build --no-restore ' +
                '1> $stdoutPath 2> $stderrPath; exit $LASTEXITCODE'
            )) {
            $null = $dbMigratorStartInfo.ArgumentList.Add([string]$dbMigratorArgument)
        }
        $dbMigratorProcess = Start-NovaGameJobProcess `
            -JobHandle $processJobHandle `
            -TargetStartInfo $dbMigratorStartInfo
        $dbMigratorCompleted = $dbMigratorProcess.WaitForExit(300000)
        if (-not $dbMigratorCompleted) {
            $dbMigratorProcess.Kill($true)
        }
        $dbMigratorProcess.WaitForExit()
        $dbMigratorProcess.Refresh()
        $dbMigratorExitCode = $dbMigratorProcess.ExitCode
        if (-not $dbMigratorCompleted) {
            throw "NovaGame DbMigrator exceeded 300 seconds."
        }
        if ($dbMigratorExitCode -ne 0) {
            throw "NovaGame DbMigrator failed with exit code $dbMigratorExitCode."
        }

        $apiPort = Get-AvailableLoopbackPort
        $apiUrl = "http://127.0.0.1:$apiPort"
        $env:ASPNETCORE_URLS = $apiUrl
        $apiStandardOutputPath = Join-Path $runRoot "api.stdout.log"
        $apiStandardErrorPath = Join-Path $runRoot "api.stderr.log"
        $apiStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $apiStartInfo.WorkingDirectory = $repositoryRoot
        $apiStartInfo.UseShellExecute = $false
        $apiStartInfo.CreateNoWindow = $true
        $apiStartInfo.RedirectStandardOutput = $false
        $apiStartInfo.RedirectStandardError = $false
        $apiStartInfo.Environment["NOVAGAME_VERIFICATION_STDOUT_PATH"] =
            $apiStandardOutputPath
        $apiStartInfo.Environment["NOVAGAME_VERIFICATION_STDERR_PATH"] =
            $apiStandardErrorPath
        $apiStartInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        foreach ($apiArgument in @(
                "-NoProfile",
                "-Command",
                '$stdoutPath = $env:NOVAGAME_VERIFICATION_STDOUT_PATH; ' +
                '$stderrPath = $env:NOVAGAME_VERIFICATION_STDERR_PATH; ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDOUT_PATH", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDERR_PATH", $null, "Process"); ' +
                '& dotnet run --project server/src/NovaGame.Api/NovaGame.Api.csproj --no-build --no-restore --no-launch-profile ' +
                '1> $stdoutPath 2> $stderrPath; exit $LASTEXITCODE'
            )) {
            $null = $apiStartInfo.ArgumentList.Add([string]$apiArgument)
        }
        $apiProcess = Start-NovaGameJobProcess `
            -JobHandle $processJobHandle `
            -TargetStartInfo $apiStartInfo

        Wait-HttpEndpoint `
            -Uri ([Uri]"$apiUrl/health/live") `
            -Process $apiProcess `
            -StandardOutputPath $apiStandardOutputPath `
            -StandardErrorPath $apiStandardErrorPath `
            -Headers @{
                "X-Forwarded-For" = "127.0.0.1"
                "X-Forwarded-Proto" = "https"
                "X-Forwarded-Host" = "localhost"
            }

        $composedTestEnvironment = [ordered]@{
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_URL = $apiUrl
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_MYSQL_CONNECTION_STRING = $connectionString
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_PROJECT_NAME = $projectName
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DISPOSABLE_MARKER = $composedMarker
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_BOOTSTRAP_USER_NAME = $bootstrapUserName
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_BOOTSTRAP_PASSWORD = $bootstrapPassword
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_STDOUT_PATH = $apiStandardOutputPath
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_API_STDERR_PATH = $apiStandardErrorPath
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DBMIGRATOR_STDOUT_PATH = $dbMigratorStandardOutputPath
            NOVAGAME_ADMIN_MANAGEMENT_COMPOSED_DBMIGRATOR_STDERR_PATH = $dbMigratorStandardErrorPath
        }
        foreach ($entry in $composedTestEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                [string]$entry.Key,
                [string]$entry.Value,
                "Process")
        }

        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $TestProject `
            -Filter $Filter `
            -ResultName $ResultName `
            -ResultsRoot (Join-Path $runRoot "test-results")
    }
    catch {
        $operationError = $_
        try {
            $safeApiFailureTypes = @(
                & {
                    foreach ($apiDiagnosticPath in @($apiStandardOutputPath, $apiStandardErrorPath)) {
                        if ([string]::IsNullOrWhiteSpace($apiDiagnosticPath) -or
                            -not (Test-Path -LiteralPath $apiDiagnosticPath -PathType Leaf)) {
                            continue
                        }

                        foreach ($line in Get-Content -LiteralPath $apiDiagnosticPath) {
                            foreach ($match in [regex]::Matches(
                                    [string]$line,
                                    '\b(?<type>[A-Za-z][A-Za-z0-9_.]*(?:Exception|Error))\b')) {
                                $match.Groups["type"].Value
                            }
                        }
                    }
                } |
                    Sort-Object -Unique |
                    Select-Object -First 4)
        }
        catch {
            $safeApiFailureTypes = @()
        }
    }
    finally {
        try {
            if ($processJobHandle -ne [IntPtr]::Zero) {
                $ownedProcessIdentities = @(
                    Get-NovaGameJobProcessIdentities -JobHandle $processJobHandle)
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to capture the composed API process job: $($_.Exception.Message)")
        }

        try {
            if ($processJobHandle -ne [IntPtr]::Zero) {
                Close-NovaGameProcessJob -JobHandle $processJobHandle
                $processJobHandle = [IntPtr]::Zero
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to close the composed API process job: $($_.Exception.Message)")
        }

        if ($processJobHandle -ne [IntPtr]::Zero) {
            try {
                Close-NovaGameProcessJob -JobHandle $processJobHandle
                $processJobHandle = [IntPtr]::Zero
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to force-close the composed API process job handle.")
            }
        }

        foreach ($process in @($dbMigratorProcess, $apiProcess)) {
            if ($null -eq $process) {
                continue
            }
            try {
                $process.Dispose()
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to dispose a composed API process handle.")
            }
        }

        try {
            Restore-NovaGameProcessEnvironmentSnapshot `
                -Snapshot $previousRuntimeEnvironment
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to restore the composed API runtime environment.")
        }
        if ($null -ne $previousAdminAccessEnvironment) {
            try {
                Restore-NovaGameAdminAccessProcessEnvironment `
                    -PreviousValues $previousAdminAccessEnvironment
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to restore the composed API administrator-access environment.")
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
                "Failed to restore composed API database connection environment variables.")
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
                    "Failed to restore the composed API MySQL process environment.")
            }
        }

        try {
            if (Test-Path -LiteralPath $testEnvironmentFile -PathType Leaf) {
                $resolvedRunRoot = Resolve-AdminManagementRunRoot -RunRoot $runRoot
                $resolvedEnvironmentFile = [System.IO.Path]::GetFullPath($testEnvironmentFile)
                if (-not $resolvedEnvironmentFile.StartsWith(
                        $resolvedRunRoot.TrimEnd('\') + '\',
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove temporary environment file outside '$resolvedRunRoot'.")
                }
                else {
                    Remove-Item -LiteralPath $resolvedEnvironmentFile -Force
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove temporary environment file '$testEnvironmentFile'.")
        }

        try {
            Assert-AdminManagementCleanup `
                -ProjectName $projectName `
                -Ports @($apiPort, $publishedPort) `
                -OwnedProcessIdentities $ownedProcessIdentities `
                -TemporarySecretPaths @($testEnvironmentFile)
        }
        catch {
            $null = $cleanupErrors.Add(
                "Composed API final cleanup assertion failed: $($_.Exception.Message)")
        }

        try {
            if (Test-Path -LiteralPath $runRoot -PathType Container) {
                Remove-AdminManagementRunRootVerified `
                    -RunRoot $runRoot `
                    -MaximumAttempts 6
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove composed API run root '$runRoot'.")
        }
    }

    if ($null -ne $operationError) {
        $safeFailureDetail = if ($safeApiFailureTypes.Count -gt 0) {
            "`nSafe API exception types: $($safeApiFailureTypes -join ', ')."
        }
        else {
            ""
        }
        if ($cleanupErrors.Count -gt 0) {
            throw "$operationError$safeFailureDetail`nCleanup errors:`n$($cleanupErrors -join "`n")"
        }
        throw "$operationError$safeFailureDetail"
    }
    if ($cleanupErrors.Count -gt 0) {
        throw "Composed API acceptance cleanup failed:`n$($cleanupErrors -join "`n")"
    }

    Write-Output "Admin management composed API acceptance passed."
}

function Test-AdminManagement {
    $testProject =
        "server/tests/NovaGame.EntityFrameworkCore.Tests/NovaGame.EntityFrameworkCore.Tests.csproj"
    $manifest = @(
        [pscustomobject]@{
            Task = "M35-03-T01"
            Filter = "FullyQualifiedName~AdminAccessManagementAdministratorDomainTests"
            ResultName = "admin-management-administrator-domain.trx"
        },
        [pscustomobject]@{
            Task = "M35-03-T01"
            Filter = "FullyQualifiedName~AdminAccessManagementRoleDomainTests"
            ResultName = "admin-management-role-domain.trx"
        },
        [pscustomobject]@{
            Task = "M35-03-T02"
            Filter = "FullyQualifiedName~AdminAccessManagementAuthorizationTests"
            ResultName = "admin-management-authorization.trx"
        },
        [pscustomobject]@{
            Task = "M35-03-T02"
            Filter = "FullyQualifiedName~AdminAccessManagementConcurrencyTests"
            ResultName = "admin-management-concurrency.trx"
        },
        [pscustomobject]@{
            Task = "M35-04-T01"
            Filter = "FullyQualifiedName~AdminAccessManagementSessionTests"
            ResultName = "admin-management-session.trx"
        },
        [pscustomobject]@{
            Task = "M35-04-T01"
            Filter = "FullyQualifiedName~AdminAccessManagementMigrationTests"
            ResultName = "admin-management-migration.trx"
        },
        [pscustomobject]@{
            Task = "M35-04-T01"
            Filter = "FullyQualifiedName~AdminAccessManagementQueryTests"
            ResultName = "admin-management-query.trx"
        },
        [pscustomobject]@{
            Task = "M35-04-T02"
            Filter = "FullyQualifiedName~AdminAccessManagementSeedTests"
            ResultName = "admin-management-seed.trx"
        },
        [pscustomobject]@{
            Task = "M35-04-T03"
            Filter = "FullyQualifiedName~AdminAccessManagementApiTests"
            ResultName = "admin-management-api.trx"
        },
        [pscustomobject]@{
            Task = "M35-04-T03"
            Filter = "FullyQualifiedName~AdminAccessManagementOpenApiTests"
            ResultName = "admin-management-openapi.trx"
        },
        [pscustomobject]@{
            Task = "M35-06-T02"
            Filter = "FullyQualifiedName~AdminAccessManagementComposedApiAcceptanceTests"
            ResultName = "admin-management-composed-api.trx"
        },
        [pscustomobject]@{
            Task = "M35-03-T01"
            Filter = "FullyQualifiedName~AdministratorAccessApiTests"
            ResultName = "admin-management-authentication-regression.trx"
        }
    )
    $enabledStatuses = @("in_progress", "blocked", "complete")
    $selectedManifest = @(
        $manifest | Where-Object {
            (Get-M35TaskStatus -TaskId $_.Task) -in $enabledStatuses
        })
    Assert-Condition `
        ($selectedManifest.Count -gt 0) `
        "AdminManagement has no active focused test family."

    Invoke-CheckedCommand "dotnet" @(
        "build",
        "server/NovaGame.sln",
        "--configuration", "Release",
        "--no-restore",
        "--disable-build-servers",
        "--verbosity", "minimal",
        "-warnaserror",
        "-p:NuGetAudit=false"
    )

    $composedApiManifest = @(
        $selectedManifest | Where-Object { $_.Task -eq "M35-06-T02" }
    )
    $databaseManifest = @(
        $selectedManifest | Where-Object { $_.Task -ne "M35-06-T02" }
    )
    $focusedVerification = {
        foreach ($entry in $databaseManifest) {
            Invoke-DotNetFilteredTestWithExecutionCount `
                -ProjectPath $testProject `
                -Filter $entry.Filter `
                -ResultName $entry.ResultName
        }
    }.GetNewClosure()

    if ($databaseManifest.Count -gt 0) {
        Test-Database `
            -RunDbMigratorSeed `
            -AdditionalVerification $focusedVerification `
            -SkipSolutionTests
    }
    foreach ($entry in $composedApiManifest) {
        Invoke-AdminManagementComposedApiAcceptance `
            -TestProject $testProject `
            -Filter $entry.Filter `
            -ResultName $entry.ResultName
    }
    Write-Output `
        "AdminManagement verification passed for $($selectedManifest.Count) focused test families."
}

function Invoke-AdminManagementVitestWithExecutionCount {
    param([Parameter(Mandatory)]$Toolchain)

    $adminRoot = [System.IO.Path]::GetFullPath([string]$Toolchain.adminRoot)
    $sourceRoot = Join-Path $adminRoot "apps\web-ele\src"
    Assert-Condition `
        (Test-Path -LiteralPath $sourceRoot -PathType Container) `
        "Admin management source root is missing at '$sourceRoot'."

    $matchingTestFiles = @(
        Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter "*.test.ts" |
            ForEach-Object {
                $relativePath = [System.IO.Path]::GetRelativePath(
                    $adminRoot,
                    $_.FullName).Replace("\", "/")
                [pscustomobject]@{
                    FullName = $_.FullName
                    RelativePath = $relativePath
                }
            } |
            Where-Object {
                $_.RelativePath -match '^apps/web-ele/src/.*/?admin-access[^/]*\.test\.ts$' -or
                $_.RelativePath -match '^apps/web-ele/src/views/admin-access/.+\.test\.ts$'
            } |
            Sort-Object RelativePath)
    Assert-Condition `
        ($matchingTestFiles.Count -gt 0) `
        "AdminManagementAdmin has no focused transport or route tests."
    foreach ($matchingTestFile in $matchingTestFiles) {
        Assert-Condition `
            ($matchingTestFile.RelativePath -match '^apps/web-ele/src/.*/?admin-access[^/]*\.test\.ts$' -or
                $matchingTestFile.RelativePath -match '^apps/web-ele/src/views/admin-access/.+\.test\.ts$') `
            "AdminManagementAdmin selected an out-of-scope test '$($matchingTestFile.RelativePath)'."
    }

    $resultsRoot = Join-Path `
        $localRoot `
        ("test-results\admin-management-admin-vitest-" +
            [Guid]::NewGuid().ToString("N"))
    $resolvedResultsRoot = [System.IO.Path]::GetFullPath($resultsRoot)
    $resolvedLocalRoot = [System.IO.Path]::GetFullPath($localRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        $resolvedResultsRoot.StartsWith(
            $resolvedLocalRoot,
            [StringComparison]::OrdinalIgnoreCase) `
        "AdminManagementAdmin Vitest results must remain under the repository-local '.local' directory."

    New-Item -ItemType Directory -Path $resolvedResultsRoot -Force | Out-Null
    $resultPath = Join-Path $resolvedResultsRoot "admin-management-admin.json"
    try {
        $arguments = @("exec", "vitest", "run") + @(
            $matchingTestFiles | ForEach-Object { $_.RelativePath }) + @(
            "--reporter=json", "--outputFile", $resultPath)
        Invoke-AdminPnpm -Toolchain $Toolchain -ArgumentList $arguments
        Assert-Condition `
            (Test-Path -LiteralPath $resultPath -PathType Leaf) `
            "AdminManagementAdmin did not produce '$resultPath'."

        $testResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        foreach ($requiredCounter in @(
            "numTotalTests", "numPassedTests", "numFailedTests", "numPendingTests"
        )) {
            Assert-Condition `
                ($null -ne $testResult.PSObject.Properties[$requiredCounter]) `
                "AdminManagementAdmin result '$resultPath' is missing '$requiredCounter'."
        }

        $totalCount = [int]$testResult.numTotalTests
        $passedCount = [int]$testResult.numPassedTests
        $failedCount = [int]$testResult.numFailedTests
        $pendingCount = [int]$testResult.numPendingTests
        Assert-Condition `
            ($totalCount -gt 0) `
            "AdminManagementAdmin focused Vitest selection executed zero tests."
        Assert-Condition `
            ($passedCount -eq $totalCount -and
                $failedCount -eq 0 -and
                $pendingCount -eq 0) `
            "AdminManagementAdmin focused Vitest result was not completely successful: total=$totalCount, passed=$passedCount, failed=$failedCount, pending=$pendingCount."
        Write-Output `
            "AdminManagementAdmin focused Vitest verification passed for $totalCount test(s)."
    }
    finally {
        if (Test-Path -LiteralPath $resolvedResultsRoot -PathType Container) {
            Remove-Item -LiteralPath $resolvedResultsRoot -Recurse -Force
        }
    }
}

function Test-AdminManagementAdmin {
    Assert-Condition `
        ((Get-M35TaskStatus -TaskId "M35-05-T01") -in @(
                "in_progress", "blocked", "complete")) `
        "AdminManagementAdmin is unavailable before M35-05-T01 activation."

    $toolchain = Initialize-AdminToolchain
    $previousCi = $env:CI
    $previousNodeOptionsExists = Test-Path -LiteralPath "Env:NODE_OPTIONS"
    $previousNodeOptions = $env:NODE_OPTIONS
    $env:CI = "true"
    try {
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "format:check")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "lint")
        Invoke-AdminManagementVitestWithExecutionCount -Toolchain $toolchain
        Invoke-AdminPnpm `
            -Toolchain $toolchain `
            -ArgumentList @("--filter", "@vben/web-ele", "run", "typecheck")
        $env:NODE_OPTIONS = "--max-old-space-size=8192"
        Invoke-AdminPnpm `
            -Toolchain $toolchain `
            -ArgumentList @(
                "--filter", "@vben/web-ele...", "--if-present", "run", "build")
    }
    finally {
        if ($previousNodeOptionsExists) {
            $env:NODE_OPTIONS = $previousNodeOptions
        }
        else {
            Remove-Item -LiteralPath "Env:NODE_OPTIONS" -ErrorAction SilentlyContinue
        }
        $env:CI = $previousCi
    }

    Write-Output "AdminManagementAdmin verification passed."
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
    $gameConfigPermissionDefinitionProvider = Get-Content -LiteralPath `
        "server/src/NovaGame.Domain.Shared/Content/GameConfig/GameConfigPermissionDefinitionProvider.cs" -Raw
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

    Assert-Condition `
        ($administratorSeed.Contains("GetRegisteredPermissionNames") -and
            $administratorSeed.Contains("_permissionDefinitionManager.Groups") -and
            $administratorSeed.Contains("SynchronizeBuiltInRolePermissions")) `
        "The Administrators seed must synchronize its built-in role from the registered runtime permission catalog."

    foreach ($permissionName in @(
        "GameConfigPermissions.Access",
        "GameConfigPermissions.ManageDrafts",
        "GameConfigPermissions.Publish",
        "GameConfigPermissions.Activate"
    )) {
        Assert-Condition `
            $gameConfigPermissionDefinitionProvider.Contains($permissionName) `
            "The GameConfig permission definition is missing '$permissionName'."
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

function Get-AdminManagementRetainableTextExtensions {
    return @(
        ".cjs", ".css", ".htm", ".html", ".js", ".json", ".log",
        ".map", ".md", ".mjs", ".trx", ".txt", ".xml", ".yaml", ".yml")
}

function Resolve-AdminManagementRunRoot {
    param([Parameter(Mandatory)][string]$RunRoot)

    $resolvedAllowedRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot ".local\admin-management"))
    $resolvedRunRoot = [System.IO.Path]::GetFullPath($RunRoot)
    Assert-Condition `
        ([System.IO.Path]::GetDirectoryName($resolvedRunRoot).Equals(
            $resolvedAllowedRoot,
            [StringComparison]::OrdinalIgnoreCase)) `
        "Admin management run roots must be direct children of '$resolvedAllowedRoot'."
    Assert-Condition `
        ([System.IO.Path]::GetFileName($resolvedRunRoot) -match
            '^novagame-test-[a-f0-9]{12}$') `
        "Admin management run root '$resolvedRunRoot' has an invalid run ID."

    return $resolvedRunRoot
}

function Assert-AdminManagementTreeHasNoReparsePoints {
    param([Parameter(Mandatory)][string]$RootPath)

    if (-not (Test-Path -LiteralPath $RootPath)) {
        return
    }
    $rootItem = Get-Item -LiteralPath $RootPath -Force -ErrorAction Stop
    if ($rootItem.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
        throw "Admin management artifact tree contains a reparse point: $($rootItem.FullName)."
    }
    if (-not $rootItem.PSIsContainer) {
        return
    }

    $pendingPaths = [System.Collections.Generic.Stack[string]]::new()
    $pendingPaths.Push($rootItem.FullName)
    while ($pendingPaths.Count -gt 0) {
        $currentPath = $pendingPaths.Pop()
        foreach ($childItem in @(Get-ChildItem `
                -LiteralPath $currentPath `
                -Force `
                -ErrorAction Stop)) {
            if ($childItem.Attributes.HasFlag(
                    [System.IO.FileAttributes]::ReparsePoint)) {
                throw "Admin management artifact tree contains a reparse point: $($childItem.FullName)."
            }
            if ($childItem.PSIsContainer) {
                $pendingPaths.Push($childItem.FullName)
            }
        }
    }
}

function Remove-AdminManagementRunRoot {
    param([Parameter(Mandatory)][string]$RunRoot)

    $resolvedRunRoot = Resolve-AdminManagementRunRoot -RunRoot $RunRoot
    if (-not (Test-Path -LiteralPath $resolvedRunRoot)) {
        return
    }
    Assert-Condition `
        (Test-Path -LiteralPath $resolvedRunRoot -PathType Container) `
        "Admin management run root '$resolvedRunRoot' is not a directory."
    Assert-AdminManagementTreeHasNoReparsePoints -RootPath $resolvedRunRoot
    Remove-Item `
        -LiteralPath $resolvedRunRoot `
        -Recurse `
        -Force `
        -ErrorAction Stop
}

function Remove-AdminManagementRunRootVerified {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [ValidateRange(1, 10)][int]$MaximumAttempts = 3
    )

    $resolvedRunRoot = Resolve-AdminManagementRunRoot -RunRoot $RunRoot
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            Remove-AdminManagementRunRoot -RunRoot $resolvedRunRoot
            if (-not (Test-Path -LiteralPath $resolvedRunRoot)) {
                return
            }
        }
        catch {
            # Retry only the already validated disposable run root.
        }
        if ($attempt -lt $MaximumAttempts) {
            Start-Sleep -Milliseconds 200
        }
    }

    throw "Admin management run-root removal could not be verified after $MaximumAttempts attempts."
}

function Test-AdminManagementRunRootRemovalRequired {
    param(
        [Parameter(Mandatory)][bool]$OperationFailed,
        [Parameter(Mandatory)]
        [ValidateRange(0, 2147483647)]
        [int]$CleanupErrorCount,
        [Parameter(Mandatory)][bool]$DynamicSecretRegistryComplete,
        [Parameter(Mandatory)][bool]$ArtifactSecretScanPassed
    )

    return $OperationFailed -or
        $CleanupErrorCount -gt 0 -or
        -not $DynamicSecretRegistryComplete -or
        -not $ArtifactSecretScanPassed
}

function Test-AdminManagementRunRootRemovalPolicy {
    $adminManagementRoot = Join-Path `
        $repositoryRoot `
        ".local\admin-management"
    $transientRunRoot = Join-Path `
        $adminManagementRoot `
        "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $permanentRunRoot = Join-Path `
        $adminManagementRoot `
        "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $boundaryRunRoot = Join-Path `
        $adminManagementRoot `
        "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $lockProcess = $null
    $permanentLock = $null
    try {
        Assert-Condition `
            (Test-AdminManagementRunRootRemovalRequired `
                -OperationFailed $false `
                -CleanupErrorCount 1 `
                -DynamicSecretRegistryComplete $true `
                -ArtifactSecretScanPassed $true) `
            "A cleanup failure must force whole-run-root removal."
        Assert-Condition `
            (-not (Test-AdminManagementRunRootRemovalRequired `
                    -OperationFailed $false `
                    -CleanupErrorCount 0 `
                    -DynamicSecretRegistryComplete $true `
                    -ArtifactSecretScanPassed $true)) `
            "Verified clean evidence must remain eligible for its normal retention policy."

        New-Item -ItemType Directory -Path $transientRunRoot -Force |
            Out-Null
        $transientLockPath = Join-Path $transientRunRoot "transient.lock"
        $transientReadyPath = Join-Path $transientRunRoot "transient.ready"
        [System.IO.File]::WriteAllText($transientLockPath, "locked")
        $lockScript = @'
$stream = [System.IO.File]::Open(
    $env:NOVAGAME_REMOVAL_PROBE_LOCK,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    [System.IO.File]::WriteAllText(
        $env:NOVAGAME_REMOVAL_PROBE_READY,
        "ready")
    Start-Sleep -Milliseconds 650
}
finally {
    $stream.Dispose()
}
'@
        $lockStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $lockStartInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        $lockStartInfo.WorkingDirectory = $repositoryRoot
        $lockStartInfo.UseShellExecute = $false
        $lockStartInfo.CreateNoWindow = $true
        $lockStartInfo.Environment["NOVAGAME_REMOVAL_PROBE_LOCK"] =
            $transientLockPath
        $lockStartInfo.Environment["NOVAGAME_REMOVAL_PROBE_READY"] =
            $transientReadyPath
        $encodedLockScript = [Convert]::ToBase64String(
            [Text.Encoding]::Unicode.GetBytes($lockScript))
        $null = $lockStartInfo.ArgumentList.Add("-NoProfile")
        $null = $lockStartInfo.ArgumentList.Add("-EncodedCommand")
        $null = $lockStartInfo.ArgumentList.Add($encodedLockScript)
        $lockProcess = [System.Diagnostics.Process]::Start($lockStartInfo)

        $readyDeadline = [DateTime]::UtcNow.AddSeconds(5)
        while (-not (Test-Path -LiteralPath $transientReadyPath -PathType Leaf) -and
            -not $lockProcess.HasExited -and
            [DateTime]::UtcNow -lt $readyDeadline) {
            Start-Sleep -Milliseconds 25
        }
        Assert-Condition `
            ((Test-Path -LiteralPath $transientReadyPath -PathType Leaf) -and
                -not $lockProcess.HasExited) `
            "The transient run-root lock probe did not become ready."
        Remove-AdminManagementRunRootVerified `
            -RunRoot $transientRunRoot `
            -MaximumAttempts 6
        Assert-Condition `
            (-not (Test-Path -LiteralPath $transientRunRoot)) `
            "The transient run-root lock survived bounded deletion retries."
        Assert-Condition `
            $lockProcess.WaitForExit(5000) `
            "The transient run-root lock process did not exit."
        Assert-Condition `
            ($lockProcess.ExitCode -eq 0) `
            "The transient run-root lock process failed."

        New-Item -ItemType Directory -Path $permanentRunRoot -Force |
            Out-Null
        $permanentLockPath = Join-Path $permanentRunRoot "permanent.lock"
        $permanentLock = [System.IO.File]::Open(
            $permanentLockPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $permanentRemovalRejected = $false
        try {
            Remove-AdminManagementRunRootVerified `
                -RunRoot $permanentRunRoot `
                -MaximumAttempts 2
        }
        catch {
            $permanentRemovalRejected = $_.Exception.Message.Contains(
                "could not be verified after 2 attempts",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            ($permanentRemovalRejected -and
                (Test-Path -LiteralPath $permanentRunRoot -PathType Container)) `
            "Permanent run-root deletion failure was not bounded and preserved."
        $permanentLock.Dispose()
        $permanentLock = $null
        Remove-AdminManagementRunRootVerified -RunRoot $permanentRunRoot

        New-Item -ItemType Directory -Path $boundaryRunRoot -Force |
            Out-Null
        $nestedRunRoot = Join-Path `
            $boundaryRunRoot `
            "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
        New-Item -ItemType Directory -Path $nestedRunRoot -Force |
            Out-Null
        $boundarySentinel = Join-Path $nestedRunRoot "sentinel.txt"
        [System.IO.File]::WriteAllText($boundarySentinel, "preserve")
        $boundaryRemovalRejected = $false
        try {
            Remove-AdminManagementRunRootVerified -RunRoot $nestedRunRoot
        }
        catch {
            $boundaryRemovalRejected = $_.Exception.Message.Contains(
                "must be direct children",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            ($boundaryRemovalRejected -and
                (Test-Path -LiteralPath $boundarySentinel -PathType Leaf)) `
            "Run-root deletion crossed or weakened its validated boundary."
        Remove-AdminManagementRunRootVerified -RunRoot $boundaryRunRoot
    }
    finally {
        if ($null -ne $permanentLock) {
            $permanentLock.Dispose()
        }
        if ($null -ne $lockProcess) {
            try {
                if (-not $lockProcess.HasExited) {
                    $lockProcess.Kill($true)
                    $null = $lockProcess.WaitForExit(5000)
                }
            }
            catch {
            }
            $lockProcess.Dispose()
        }
        foreach ($probeRoot in @(
                $transientRunRoot,
                $permanentRunRoot,
                $boundaryRunRoot)) {
            if (Test-Path -LiteralPath $probeRoot) {
                Remove-AdminManagementRunRootVerified `
                    -RunRoot $probeRoot `
                    -MaximumAttempts 6
            }
        }
    }
}

function Get-AdminManagementAllowedScreenshotPolicy {
    param(
        [Parameter(Mandatory)][string]$ArtifactRoot,
        [string[]]$AllowedScreenshotRelativePaths = @()
    )

    $resolvedArtifactRoot = Resolve-AdminManagementRunRoot `
        -RunRoot $ArtifactRoot
    $allowedPaths = [System.Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $rootPrefix = $resolvedArtifactRoot.TrimEnd('\') + '\'
    foreach ($relativePath in $AllowedScreenshotRelativePaths) {
        Assert-Condition `
            (-not [string]::IsNullOrWhiteSpace($relativePath)) `
            "Admin management screenshot allowlist contains an empty path."
        $normalizedRelativePath = $relativePath.Replace('\', '/')
        Assert-Condition `
            (-not [System.IO.Path]::IsPathRooted($relativePath) -and
                $normalizedRelativePath -notmatch '^[A-Za-z]:' -and
                $normalizedRelativePath -notmatch '(^|/)\.\.?(/|$)' -and
                $normalizedRelativePath -notmatch '//') `
            "Admin management screenshot allowlist path '$relativePath' is unsafe."
        Assert-Condition `
            ([System.IO.Path]::GetExtension($normalizedRelativePath).Equals(
                ".png",
                [StringComparison]::OrdinalIgnoreCase)) `
            "Admin management screenshot allowlist path '$relativePath' is not a PNG."
        $resolvedScreenshotPath = [System.IO.Path]::GetFullPath(
            (Join-Path `
                $resolvedArtifactRoot `
                $normalizedRelativePath.Replace('/', '\')))
        Assert-Condition `
            $resolvedScreenshotPath.StartsWith(
                $rootPrefix,
                [StringComparison]::OrdinalIgnoreCase) `
            "Admin management screenshot '$relativePath' escapes '$resolvedArtifactRoot'."
        $canonicalRelativePath = [System.IO.Path]::GetRelativePath(
            $resolvedArtifactRoot,
            $resolvedScreenshotPath).Replace('\', '/')
        Assert-Condition `
            $canonicalRelativePath.Equals(
                $normalizedRelativePath,
                [StringComparison]::Ordinal) `
            "Admin management screenshot allowlist path '$relativePath' is not canonical."
        Assert-Condition `
            (-not $allowedPaths.ContainsKey($canonicalRelativePath)) `
            "Admin management screenshot allowlist repeats '$canonicalRelativePath'."
        $allowedPaths.Add($canonicalRelativePath, $resolvedScreenshotPath)
    }

    return [pscustomobject]@{
        RootPath = $resolvedArtifactRoot
        Paths = $allowedPaths
    }
}

function Remove-AdminManagementNonRetainableArtifacts {
    param(
        [Parameter(Mandatory)][string]$ArtifactRoot,
        [string[]]$AllowedScreenshotRelativePaths = @()
    )

    $screenshotPolicy = Get-AdminManagementAllowedScreenshotPolicy `
        -ArtifactRoot $ArtifactRoot `
        -AllowedScreenshotRelativePaths $AllowedScreenshotRelativePaths
    $resolvedArtifactRoot = [string]$screenshotPolicy.RootPath
    if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
        return
    }
    Assert-AdminManagementTreeHasNoReparsePoints -RootPath $resolvedArtifactRoot
    $retainableExtensions = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in Get-AdminManagementRetainableTextExtensions) {
        $null = $retainableExtensions.Add($extension)
    }
    foreach ($artifactFile in Get-ChildItem `
        -LiteralPath $resolvedArtifactRoot `
        -Recurse `
            -File `
            -Force `
            -ErrorAction Stop) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $resolvedArtifactRoot,
            $artifactFile.FullName).Replace('\', '/')
        $isAllowedScreenshot =
            $artifactFile.Extension.Equals(
                ".png",
                [StringComparison]::OrdinalIgnoreCase) -and
            $screenshotPolicy.Paths.ContainsKey($relativePath)
        $isRawProcessTranscript = $relativePath -cmatch
            '^(?:api|dbmigrator|playwright)\.(?:stdout|stderr)\.log$'
        $isPlaywrightFailureContext = $relativePath -cmatch
            '^playwright-output(?:/[^/]+)*/error-context\.md$'
        if (($isPlaywrightFailureContext -or
                $isRawProcessTranscript -or
                -not $retainableExtensions.Contains($artifactFile.Extension)) -and
            -not $isAllowedScreenshot) {
            Remove-Item -LiteralPath $artifactFile.FullName -Force
        }
    }
}

function Read-AdminManagementPngStructure {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    Assert-Condition `
        ($bytes.Length -ge 45 -and $bytes.Length -le 20MB) `
        "Admin management screenshot has an invalid PNG byte length."
    $expectedSignature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    Assert-Condition `
        ([Linq.Enumerable]::SequenceEqual(
                [byte[]]$bytes[0..7],
                $expectedSignature)) `
        "Admin management screenshot does not have a canonical PNG signature."
    $allowedChunkTypes = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($chunkType in @(
            "IHDR", "PLTE", "IDAT", "IEND", "cHRM", "gAMA", "sBIT",
            "sRGB", "bKGD", "tRNS", "pHYs")) {
        $null = $allowedChunkTypes.Add($chunkType)
    }
    $offset = 8
    $chunkIndex = 0
    $idatCount = 0
    $iendCount = 0
    while ($offset -lt $bytes.Length) {
        Assert-Condition `
            (($bytes.Length - $offset) -ge 12) `
            "Admin management screenshot has a truncated PNG chunk."
        $chunkLength =
            ([uint64]$bytes[$offset] -shl 24) -bor
            ([uint64]$bytes[$offset + 1] -shl 16) -bor
            ([uint64]$bytes[$offset + 2] -shl 8) -bor
            [uint64]$bytes[$offset + 3]
        $chunkType = [Text.Encoding]::ASCII.GetString($bytes, $offset + 4, 4)
        $chunkEnd = [uint64]$offset + 12 + $chunkLength
        Assert-Condition `
            ($chunkType -cmatch '^[A-Za-z]{4}$' -and
                $chunkEnd -le [uint64]$bytes.Length) `
            "Admin management screenshot has an invalid PNG chunk boundary."
        Assert-Condition `
            $allowedChunkTypes.Contains($chunkType) `
            "Admin management screenshot contains an unsafe PNG chunk '$chunkType'."
        if ($chunkIndex -eq 0) {
            Assert-Condition `
                ($chunkType -ceq "IHDR" -and $chunkLength -eq 13) `
                "Admin management screenshot does not have a canonical PNG IHDR header."
        }
        elseif ($chunkType -ceq "IHDR") {
            throw "Admin management screenshot contains more than one PNG IHDR chunk."
        }
        if ($chunkType -ceq "IDAT") {
            $idatCount++
        }
        if ($chunkType -ceq "IEND") {
            $iendCount++
            Assert-Condition `
                ($chunkLength -eq 0 -and
                    $iendCount -eq 1 -and
                    $chunkEnd -eq [uint64]$bytes.Length) `
                "Admin management screenshot must end at one terminal PNG IEND chunk."
        }
        $offset = [int]$chunkEnd
        $chunkIndex++
    }
    Assert-Condition `
        ($offset -eq $bytes.Length -and $idatCount -gt 0 -and $iendCount -eq 1) `
        "Admin management screenshot must contain image data and one terminal PNG IEND chunk."
    $width =
        ([uint64]$bytes[16] -shl 24) -bor
        ([uint64]$bytes[17] -shl 16) -bor
        ([uint64]$bytes[18] -shl 8) -bor
        [uint64]$bytes[19]
    $height =
        ([uint64]$bytes[20] -shl 24) -bor
        ([uint64]$bytes[21] -shl 16) -bor
        ([uint64]$bytes[22] -shl 8) -bor
        [uint64]$bytes[23]
    return [pscustomobject]@{
        Width = $width
        Height = $height
    }
}

function Read-AdminManagementPngHeader {
    param([Parameter(Mandatory)][string]$Path)

    return Read-AdminManagementPngStructure -Path $Path
}

function Initialize-AdminManagementScreenshotPixelInspectorType {
    if ("NovaGame.Verification.ScreenshotPixelInspector" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace NovaGame.Verification
{
    public sealed class ScreenshotPixelStatistics
    {
        public long OpaquePixels { get; internal set; }
        public long NonDominantVisiblePixels { get; internal set; }
        public int VisibleColorCount { get; internal set; }
    }

    public static class ScreenshotPixelInspector
    {
        public static bool ContainsSequence(byte[] value, byte[] candidate)
        {
            if (value == null || candidate == null || candidate.Length == 0)
            {
                return false;
            }
            return value.AsSpan().IndexOf(candidate) >= 0;
        }

        public static byte[] InsertChunkBeforeIend(
            byte[] png,
            string chunkType,
            byte[] data)
        {
            if (png == null || png.Length < 12 || data == null ||
                string.IsNullOrWhiteSpace(chunkType) || chunkType.Length != 4)
            {
                throw new ArgumentException("invalid-png-probe-chunk");
            }
            var typeBytes = Encoding.ASCII.GetBytes(chunkType);
            var iendOffset = png.Length - 12;
            if (Encoding.ASCII.GetString(png, iendOffset + 4, 4) != "IEND")
            {
                throw new ArgumentException("missing-terminal-iend");
            }
            var chunk = new byte[checked(data.Length + 12)];
            BinaryPrimitives.WriteUInt32BigEndian(
                chunk.AsSpan(0, 4),
                checked((uint)data.Length));
            typeBytes.CopyTo(chunk, 4);
            data.CopyTo(chunk, 8);
            var crc = ComputePngCrc(chunk.AsSpan(4, data.Length + 4));
            BinaryPrimitives.WriteUInt32BigEndian(
                chunk.AsSpan(data.Length + 8, 4),
                crc);
            var output = new byte[checked(png.Length + chunk.Length)];
            Buffer.BlockCopy(png, 0, output, 0, iendOffset);
            Buffer.BlockCopy(chunk, 0, output, iendOffset, chunk.Length);
            Buffer.BlockCopy(
                png,
                iendOffset,
                output,
                iendOffset + chunk.Length,
                12);
            return output;
        }

        private static uint ComputePngCrc(ReadOnlySpan<byte> value)
        {
            var crc = uint.MaxValue;
            foreach (var current in value)
            {
                crc ^= current;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc >> 1) ^
                        ((crc & 1) == 0 ? 0u : 0xedb88320u);
                }
            }
            return ~crc;
        }

        public static ScreenshotPixelStatistics Inspect(byte[] bgraPixels)
        {
            if (bgraPixels == null || bgraPixels.Length == 0 ||
                bgraPixels.Length % 4 != 0)
            {
                throw new ArgumentException("invalid-bgra-pixel-buffer");
            }

            long opaquePixels = 0;
            uint majorityCandidate = 0;
            long majorityBalance = 0;
            var visibleColors = new HashSet<uint>();
            for (var offset = 0; offset < bgraPixels.Length; offset += 4)
            {
                if (bgraPixels[offset + 3] < 250)
                {
                    continue;
                }

                opaquePixels++;
                var color =
                    (uint)bgraPixels[offset] |
                    ((uint)bgraPixels[offset + 1] << 8) |
                    ((uint)bgraPixels[offset + 2] << 16) |
                    ((uint)bgraPixels[offset + 3] << 24);
                if (visibleColors.Count < 16)
                {
                    visibleColors.Add(color);
                }
                if (majorityBalance == 0)
                {
                    majorityCandidate = color;
                    majorityBalance = 1;
                }
                else if (majorityCandidate == color)
                {
                    majorityBalance++;
                }
                else
                {
                    majorityBalance--;
                }
            }

            long majorityCandidatePixels = 0;
            if (opaquePixels > 0)
            {
                for (var offset = 0; offset < bgraPixels.Length; offset += 4)
                {
                    if (bgraPixels[offset + 3] < 250)
                    {
                        continue;
                    }
                    var color =
                        (uint)bgraPixels[offset] |
                        ((uint)bgraPixels[offset + 1] << 8) |
                        ((uint)bgraPixels[offset + 2] << 16) |
                        ((uint)bgraPixels[offset + 3] << 24);
                    if (color == majorityCandidate)
                    {
                        majorityCandidatePixels++;
                    }
                }
            }

            return new ScreenshotPixelStatistics
            {
                OpaquePixels = opaquePixels,
                NonDominantVisiblePixels = opaquePixels - majorityCandidatePixels,
                VisibleColorCount = visibleColors.Count
            };
        }

        public static byte[] CreateStrideAlignedProbe(
            int width,
            int height,
            bool transparentBackground)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("invalid-probe-dimensions");
            }
            var pixelCount = checked(width * height);
            var pixels = new byte[checked(pixelCount * 4)];
            for (var pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                var offset = pixelIndex * 4;
                pixels[offset] = 240;
                pixels[offset + 1] = 240;
                pixels[offset + 2] = 240;
                pixels[offset + 3] = transparentBackground ? (byte)0 : (byte)255;
            }

            var oldSampleStep = Math.Max(1, pixelCount / 50000);
            var oldSampleCount =
                (pixelCount + oldSampleStep - 1) / oldSampleStep;
            var changedSampleCount = transparentBackground
                ? oldSampleCount
                : Math.Max(17, (int)Math.Ceiling(oldSampleCount * 0.02));
            var sampleOrdinal = 0;
            for (var pixelIndex = 0;
                pixelIndex < pixelCount && sampleOrdinal < changedSampleCount;
                pixelIndex += oldSampleStep)
            {
                var offset = pixelIndex * 4;
                pixels[offset] = (byte)((sampleOrdinal * 17 + 1) % 239);
                pixels[offset + 1] = (byte)((sampleOrdinal * 29 + 2) % 239);
                pixels[offset + 2] = (byte)((sampleOrdinal * 43 + 3) % 239);
                pixels[offset + 3] = 255;
                sampleOrdinal++;
            }
            return pixels;
        }
    }
}
'@
}

function Assert-AdminManagementSuccessfulScreenshots {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][object[]]$ScreenshotSpecifications
    )

    $expectedPaths = @(
        $ScreenshotSpecifications |
            ForEach-Object { [string]$_.RelativePath })
    $screenshotPolicy = Get-AdminManagementAllowedScreenshotPolicy `
        -ArtifactRoot $RunRoot `
        -AllowedScreenshotRelativePaths $expectedPaths
    Assert-Condition `
        (Test-Path -LiteralPath $screenshotPolicy.RootPath -PathType Container) `
        "Admin management successful screenshot root is missing."
    Assert-AdminManagementTreeHasNoReparsePoints `
        -RootPath $screenshotPolicy.RootPath
    $actualPaths = @(
        Get-ChildItem `
            -LiteralPath $screenshotPolicy.RootPath `
            -Recurse `
            -File `
            -Filter "*.png" `
            -Force `
            -ErrorAction Stop |
            ForEach-Object {
                [System.IO.Path]::GetRelativePath(
                    $screenshotPolicy.RootPath,
                    $_.FullName).Replace('\', '/')
            })
    Assert-StringSet `
        -Expected $expectedPaths `
        -Actual $actualPaths `
        -Message "Admin management successful screenshots differ from the exact allowlist."

    Add-Type -AssemblyName PresentationCore -ErrorAction Stop
    $inspectionResults = [System.Collections.Generic.List[object]]::new()
    foreach ($specification in $ScreenshotSpecifications) {
        $relativePath = [string]$specification.RelativePath
        Assert-Condition `
            ($null -ne $specification.PSObject.Properties["Width"] -and
                $null -ne $specification.PSObject.Properties["MinimumHeight"] -and
                $null -ne $specification.PSObject.Properties["MaximumHeight"]) `
            "Admin management screenshot '$relativePath' has an incomplete dimension contract."
        $expectedWidth = [int]$specification.Width
        $minimumHeight = [int]$specification.MinimumHeight
        $maximumHeight = [int]$specification.MaximumHeight
        Assert-Condition `
            ($expectedWidth -gt 0 -and
                $minimumHeight -gt 0 -and
                $maximumHeight -ge $minimumHeight) `
            "Admin management screenshot '$relativePath' has invalid dimension bounds."
        $screenshotPath = [string]$screenshotPolicy.Paths[$relativePath]
        Assert-Condition `
            (Test-Path -LiteralPath $screenshotPath -PathType Leaf) `
            "Admin management screenshot '$relativePath' is missing."
        $screenshotFile = Get-Item -LiteralPath $screenshotPath -Force
        Assert-Condition `
            ($screenshotFile.Length -gt 0 -and $screenshotFile.Length -le 20MB) `
            "Admin management screenshot '$relativePath' has an invalid file size."
        $pngHeader = Read-AdminManagementPngHeader -Path $screenshotPath
        Assert-Condition `
            ($pngHeader.Width -eq $expectedWidth -and
                $pngHeader.Height -ge $minimumHeight -and
                $pngHeader.Height -le $maximumHeight) `
            "Admin management screenshot '$relativePath' declares dimensions $($pngHeader.Width)x$($pngHeader.Height), expected width $expectedWidth and height $minimumHeight..$maximumHeight."
        $declaredPixelCount = [uint64]$pngHeader.Width *
            [uint64]$pngHeader.Height
        Assert-Condition `
            ($declaredPixelCount -gt 0 -and
                $declaredPixelCount -le 20000000) `
            "Admin management screenshot '$relativePath' exceeds the pre-decode pixel-inspection bound."

        $stream = $null
        try {
            $stream = [System.IO.File]::Open(
                $screenshotPath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::Read)
            $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
                $stream,
                [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
                [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
            Assert-Condition `
                ($decoder.Frames.Count -eq 1) `
                "Admin management screenshot '$relativePath' must contain exactly one PNG frame."
            $frame = $decoder.Frames[0]
        }
        catch {
            throw "Admin management screenshot '$relativePath' is not a valid PNG: $($_.Exception.Message)"
        }
        finally {
            if ($null -ne $stream) {
                $stream.Dispose()
            }
        }

        Assert-Condition `
            ($frame.PixelWidth -eq [int]$pngHeader.Width -and
                $frame.PixelHeight -eq [int]$pngHeader.Height) `
            "Admin management screenshot '$relativePath' decoded dimensions differ from its IHDR."
        $pixelCount = [long]$frame.PixelWidth * [long]$frame.PixelHeight
        Assert-Condition `
            ($pixelCount -gt 0 -and $pixelCount -le 20000000) `
            "Admin management screenshot '$relativePath' exceeds the pixel-inspection bound."
        $bitmap = if ($frame.Format -eq
            [System.Windows.Media.PixelFormats]::Bgra32) {
            $frame
        }
        else {
            [System.Windows.Media.Imaging.FormatConvertedBitmap]::new(
                $frame,
                [System.Windows.Media.PixelFormats]::Bgra32,
                $null,
                0)
        }
        $stride = [int]$frame.PixelWidth * 4
        $pixels = [byte[]]::new($stride * [int]$frame.PixelHeight)
        $bitmap.CopyPixels($pixels, $stride, 0)

        Initialize-AdminManagementScreenshotPixelInspectorType
        $pixelStatistics =
            [NovaGame.Verification.ScreenshotPixelInspector]::Inspect($pixels)
        $opaquePixelCount = [long]$pixelStatistics.OpaquePixels
        $nonDominantVisiblePixelCount =
            [long]$pixelStatistics.NonDominantVisiblePixels
        $visibleColorCount = [int]$pixelStatistics.VisibleColorCount
        $minimumOpaquePixels = [long][Math]::Ceiling($pixelCount * 0.95)
        $minimumNonDominantPixels = [Math]::Max(
            64,
            [long][Math]::Ceiling($opaquePixelCount * 0.01))
        Assert-Condition `
            ($opaquePixelCount -ge $minimumOpaquePixels) `
            "Admin management screenshot '$relativePath' is transparent or lacks an opaque surface."
        Assert-Condition `
            ($visibleColorCount -ge 16 -and
                $nonDominantVisiblePixelCount -ge
                    $minimumNonDominantPixels) `
            "Admin management screenshot '$relativePath' is blank or lacks pixel diversity."

        $null = $inspectionResults.Add([pscustomobject]@{
                RelativePath = $relativePath
                Sha256 = (Get-FileHash `
                    -LiteralPath $screenshotPath `
                    -Algorithm SHA256).Hash
                Width = [int]$frame.PixelWidth
                Height = [int]$frame.PixelHeight
                VisibleColors = $visibleColorCount
                OpaquePixels = $opaquePixelCount
                NonDominantPixels = $nonDominantVisiblePixelCount
            })
    }

    return $inspectionResults.ToArray()
}

function Assert-AdminManagementScreenshotIdentity {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][object[]]$InspectionRecords
    )

    $relativePaths = @(
        $InspectionRecords | ForEach-Object { [string]$_.RelativePath })
    $screenshotPolicy = Get-AdminManagementAllowedScreenshotPolicy `
        -ArtifactRoot $RunRoot `
        -AllowedScreenshotRelativePaths $relativePaths
    foreach ($record in $InspectionRecords) {
        $relativePath = [string]$record.RelativePath
        $screenshotPath = [string]$screenshotPolicy.Paths[$relativePath]
        Assert-Condition `
            (Test-Path -LiteralPath $screenshotPath -PathType Leaf) `
            "Validated Admin management screenshot '$relativePath' is missing."
        $currentHash = (Get-FileHash `
            -LiteralPath $screenshotPath `
            -Algorithm SHA256).Hash
        $header = Read-AdminManagementPngHeader -Path $screenshotPath
        Assert-Condition `
            ($currentHash -ceq [string]$record.Sha256 -and
                $header.Width -eq [int]$record.Width -and
                $header.Height -eq [int]$record.Height) `
            "Validated Admin management screenshot '$relativePath' changed before retained-artifact scanning."
    }
}

function Read-AdminManagementRetainedText {
    param([Parameter(Mandatory)][System.IO.FileInfo]$ArtifactFile)

    $retainableExtensions = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in Get-AdminManagementRetainableTextExtensions) {
        $null = $retainableExtensions.Add($extension)
    }
    Assert-Condition `
        $retainableExtensions.Contains($ArtifactFile.Extension) `
        "Admin management failed evidence must not retain non-text artifact '$($ArtifactFile.FullName)'."
    if ($ArtifactFile.Length -gt 20MB) {
        throw "Admin management artifact '$($ArtifactFile.FullName)' exceeds the bounded secret-scan size."
    }

    $bytes = [System.IO.File]::ReadAllBytes($ArtifactFile.FullName)
    try {
        $content = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        throw "Admin management artifact '$($ArtifactFile.FullName)' is not strict UTF-8."
    }
    foreach ($character in $content.ToCharArray()) {
        if ([char]::GetUnicodeCategory($character) -eq
                [System.Globalization.UnicodeCategory]::Control -and
            $character -notin @([char]9, [char]10, [char]13)) {
            throw "Admin management artifact '$($ArtifactFile.FullName)' contains an unexpected control character."
        }
    }

    return $content
}

function Assert-AdminManagementArtifactSecrets {
    param(
        [Parameter(Mandatory)][string]$ArtifactRoot,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$SensitiveValues,
        [string[]]$AllowedScreenshotRelativePaths = @(),
        [object[]]$ScreenshotInspectionRecords = @()
    )

    $screenshotPolicy = Get-AdminManagementAllowedScreenshotPolicy `
        -ArtifactRoot $ArtifactRoot `
        -AllowedScreenshotRelativePaths $AllowedScreenshotRelativePaths
    $resolvedArtifactRoot = [string]$screenshotPolicy.RootPath
    if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
        return
    }
    if ($AllowedScreenshotRelativePaths.Count -gt 0) {
        Assert-StringSet `
            -Expected $AllowedScreenshotRelativePaths `
            -Actual @(
                $ScreenshotInspectionRecords |
                    ForEach-Object { [string]$_.RelativePath }) `
            -Message "Admin management screenshot inspection records differ from the retained allowlist."
        Assert-AdminManagementScreenshotIdentity `
            -RunRoot $resolvedArtifactRoot `
            -InspectionRecords $ScreenshotInspectionRecords
    }
    else {
        Assert-Condition `
            ($ScreenshotInspectionRecords.Count -eq 0) `
            "Admin management failure evidence must not retain screenshot inspection records."
    }
    Assert-AdminManagementTreeHasNoReparsePoints -RootPath $resolvedArtifactRoot
    $sensitiveRepresentations =
        [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
    foreach ($sensitiveValue in $SensitiveValues) {
        if ([string]::IsNullOrWhiteSpace($sensitiveValue)) {
            continue
        }
        $null = $sensitiveRepresentations.Add($sensitiveValue)
        $secretHash = [Security.Cryptography.SHA256]::HashData(
            [Text.UTF8Encoding]::new($false).GetBytes($sensitiveValue))
        $secretHashHex = [Convert]::ToHexString($secretHash)
        $null = $sensitiveRepresentations.Add($secretHashHex)
        $null = $sensitiveRepresentations.Add(
            $secretHashHex.ToLowerInvariant())
        $null = $sensitiveRepresentations.Add(
            [Convert]::ToBase64String($secretHash))
        $null = $sensitiveRepresentations.Add(
            [Convert]::ToBase64String($secretHash).
                TrimEnd('=').
                Replace('+', '-').
                Replace('/', '_'))
        if ($sensitiveValue -cmatch '^[A-Za-z0-9_-]{43}$') {
            $decodedSecret = $null
            try {
                $decodedSecret = [Convert]::FromBase64String(
                    $sensitiveValue.Replace('-', '+').Replace('_', '/') + '=')
                if ($decodedSecret.Length -eq 32) {
                    $decodedSecretHash =
                        [Security.Cryptography.SHA256]::HashData(
                            $decodedSecret)
                    $decodedSecretHashHex =
                        [Convert]::ToHexString($decodedSecretHash)
                    $null = $sensitiveRepresentations.Add(
                        $decodedSecretHashHex)
                    $null = $sensitiveRepresentations.Add(
                        $decodedSecretHashHex.ToLowerInvariant())
                    $null = $sensitiveRepresentations.Add(
                        [Convert]::ToBase64String($decodedSecretHash))
                    $null = $sensitiveRepresentations.Add(
                        [Convert]::ToBase64String($decodedSecretHash).
                            TrimEnd('=').
                            Replace('+', '-').
                            Replace('/', '_'))
                }
            }
            catch [FormatException] {
                # The structured retained-secret patterns reject malformed values.
            }
            finally {
                if ($null -ne $decodedSecret) {
                    [Security.Cryptography.CryptographicOperations]::ZeroMemory(
                        $decodedSecret)
                }
            }
        }
    }
    $forbiddenPatterns = @(
        '(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?![A-Za-z0-9_-])',
        '(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])',
        '(?im)\bAuthorization\s*:\s*Bearer\s+\S+',
        '(?im)\bSet-Cookie\s*:[^\r\n]*(?:refresh|token)[^\r\n]*',
        '(?im)"(?:password|connection[\s_-]*string|access[\s_-]*token|refresh[\s_-]*token|signing[\s_-]*key|token[\s_-]*hash)"\s*:\s*"(?!\[REDACTED\])[^"\r\n]+"',
        '(?im)\b(?:password|connection[\s_-]*string|access[\s_-]*token|refresh[\s_-]*token|signing[\s_-]*key|token[\s_-]*hash)\b\s*=\s*(?!\[REDACTED\])[^;\s"''<>]+',
        '(?im)\b(?:server|host)\s*=\s*[^;\s]+;[^\r\n]{0,512}\bpassword\s*=',
        '(?im)\bmysql(?:\+[A-Za-z0-9._-]+)?://[^\s"''<>]+',
        '(?<![A-Za-z0-9+/])AQAAAA[A-Za-z0-9+/]{48,}={0,2}(?![A-Za-z0-9+/=])'
    )
    foreach ($artifactFile in Get-ChildItem `
        -LiteralPath $resolvedArtifactRoot `
        -Recurse `
            -File `
            -Force `
            -ErrorAction Stop) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $resolvedArtifactRoot,
            $artifactFile.FullName).Replace('\', '/')
        if ($artifactFile.Extension.Equals(
                ".png",
                [StringComparison]::OrdinalIgnoreCase) -and
            $screenshotPolicy.Paths.ContainsKey($relativePath)) {
            $pngBytes = [System.IO.File]::ReadAllBytes($artifactFile.FullName)
            Initialize-AdminManagementScreenshotPixelInspectorType
            foreach ($sensitiveRepresentation in $sensitiveRepresentations) {
                $representationBytes =
                    [Text.UTF8Encoding]::new($false).GetBytes(
                        $sensitiveRepresentation)
                try {
                    if ([NovaGame.Verification.ScreenshotPixelInspector]::
                            ContainsSequence($pngBytes, $representationBytes)) {
                        throw "Admin management artifact '$($artifactFile.FullName)' contains an exact sensitive value."
                    }
                }
                finally {
                    [Security.Cryptography.CryptographicOperations]::ZeroMemory(
                        $representationBytes)
                }
            }
            continue
        }
        $content = Read-AdminManagementRetainedText -ArtifactFile $artifactFile
        foreach ($sensitiveRepresentation in $sensitiveRepresentations) {
            if ($content.Contains(
                    $sensitiveRepresentation,
                    [StringComparison]::Ordinal)) {
                throw "Admin management artifact '$($artifactFile.FullName)' contains an exact sensitive value."
            }
        }
        foreach ($forbiddenPattern in $forbiddenPatterns) {
            if ([regex]::IsMatch($content, $forbiddenPattern)) {
                throw "Admin management artifact '$($artifactFile.FullName)' violates a retained-secret pattern."
            }
        }
    }
}

function Get-AdminManagementArtifactSnapshot {
    param([Parameter(Mandatory)][string]$ArtifactRoot)

    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
    $entries = [System.Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $resolvedArtifactRoot)) {
        return [pscustomobject]@{
            RootPath = $resolvedArtifactRoot
            RootExists = $false
            Entries = $entries
        }
    }
    Assert-Condition `
        (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container) `
        "Artifact snapshot root '$resolvedArtifactRoot' is not a directory."
    Assert-AdminManagementTreeHasNoReparsePoints -RootPath $resolvedArtifactRoot
    foreach ($artifactItem in Get-ChildItem `
        -LiteralPath $resolvedArtifactRoot `
        -Recurse `
        -Force `
        -ErrorAction Stop) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $resolvedArtifactRoot,
            $artifactItem.FullName).Replace('\', '/')
        $isDirectory = $artifactItem.PSIsContainer
        $entries.Add($relativePath, [pscustomobject]@{
                Kind = if ($isDirectory) { "Directory" } else { "File" }
                Length = if ($isDirectory) { 0L } else { $artifactItem.Length }
                Sha256 = if ($isDirectory) {
                    $null
                }
                else {
                    (Get-FileHash `
                        -LiteralPath $artifactItem.FullName `
                        -Algorithm SHA256).Hash
                }
            })
    }

    return [pscustomobject]@{
        RootPath = $resolvedArtifactRoot
        RootExists = $true
        Entries = $entries
    }
}

function Compare-AdminManagementArtifactSnapshots {
    param(
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After
    )

    Assert-Condition `
        ([string]$Before.RootPath).Equals(
            [string]$After.RootPath,
            [StringComparison]::OrdinalIgnoreCase) `
        "Admin management artifact snapshots target different roots."
    $changes = [System.Collections.Generic.List[string]]::new()
    if ([bool]$Before.RootExists -ne [bool]$After.RootExists) {
        $null = $changes.Add(
            $(if ($After.RootExists) { "Added root" } else { "Removed root" }))
    }
    $paths = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @($Before.Entries.Keys) + @($After.Entries.Keys)) {
        $null = $paths.Add([string]$path)
    }
    foreach ($path in @($paths | Sort-Object)) {
        $beforeContains = $Before.Entries.ContainsKey($path)
        $afterContains = $After.Entries.ContainsKey($path)
        if (-not $beforeContains) {
            $null = $changes.Add("Added '$path'")
            continue
        }
        if (-not $afterContains) {
            $null = $changes.Add("Removed '$path'")
            continue
        }
        $beforeEntry = $Before.Entries[$path]
        $afterEntry = $After.Entries[$path]
        if ($beforeEntry.Kind -ne $afterEntry.Kind -or
            $beforeEntry.Length -ne $afterEntry.Length -or
            $beforeEntry.Sha256 -ne $afterEntry.Sha256) {
            $null = $changes.Add("Modified '$path'")
        }
    }

    return $changes.ToArray()
}

function Get-AdminManagementScreenshotSpecifications {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("LegacyGameConfig", "M35Management")]
        [string]$Mode
    )

    if ($Mode -eq "M35Management") {
        $m35BrowserTaskStatus = Get-M35TaskStatus -TaskId "M35-07-T01"
        Assert-Condition `
            ($m35BrowserTaskStatus -in @("in_progress", "blocked", "complete")) `
            "AdminE2E is unavailable before M35-07-T01 is active."
        return @(
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/game-config-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/game-config-desktop-1440x900.png"
                Width = 1440
                MinimumHeight = 900
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-desktop-1440x900.png"
                Width = 1440
                MinimumHeight = 900
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-roles-desktop-1440x900.png"
                Width = 1440
                MinimumHeight = 900
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-roles-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-roles-create-drawer-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-sessions-desktop-1440x900.png"
                Width = 1440
                MinimumHeight = 900
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-sessions-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-sessions-revoke-dialog-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-audit-desktop-1440x900.png"
                Width = 1440
                MinimumHeight = 900
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-audit-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-conflict-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-empty-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-retry-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-loading-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-validation-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-administrators-forbidden-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-client-route-not-found-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            },
            [pscustomobject]@{
                RelativePath =
                    "output/playwright/visual/admin-access-unauthenticated-mobile-390x844.png"
                Width = 390
                MinimumHeight = 844
                MaximumHeight = 10000
            }
        )
    }

    return @(
        [pscustomobject]@{
            RelativePath =
                "output/playwright/visual/game-config-mobile-390x844.png"
            Width = 390
            MinimumHeight = 844
            MaximumHeight = 10000
        },
        [pscustomobject]@{
            RelativePath =
                "output/playwright/visual/game-config-desktop-1440x900.png"
            Width = 1440
            MinimumHeight = 900
            MaximumHeight = 10000
        }
    )
}

function Get-AdminManagementResponseAuditManifest {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("LegacyGameConfig", "M35Management")]
        [string]$Mode
    )

    $manifest = [System.Collections.Generic.List[object]]::new()
    $null = $manifest.Add([pscustomobject]@{
            ExactContextCount = 1
            ExactDirectRequestCount = 8
            ExpectedNetworkFailureCount = 0
            ExpectedNetworkFailureMethod = $null
            ExpectedNetworkFailurePathAndQuery = $null
            FileName = "game-config-response-audit.json"
            SourceFile = "game-config.spec.ts"
            TestTitle =
                "operator completes the GameConfig lifecycle against the real API"
            ExactScreenshotSurfaceScanCount = 2
            MinimumScreenshotSurfaceScanCount = 2
        })
    if ($Mode -eq "M35Management") {
        foreach ($entry in @(
                [pscustomobject]@{
                    ExactContextCount = 2
                    ExactDirectRequestCount = $null
                    ExpectedNetworkFailureCount = 0
                    ExpectedNetworkFailureMethod = $null
                    ExpectedNetworkFailurePathAndQuery = $null
                    FileName =
                        "admin-access-administrator-lifecycle-response-audit.json"
                    TestTitle = "administrator lifecycle and concurrency"
                    MinimumScreenshotSurfaceScanCount = 0
                },
                [pscustomobject]@{
                    ExactContextCount = 1
                    ExactDirectRequestCount = $null
                    ExpectedNetworkFailureCount = 0
                    ExpectedNetworkFailureMethod = $null
                    ExpectedNetworkFailurePathAndQuery = $null
                    FileName =
                        "admin-access-roles-permissions-response-audit.json"
                    TestTitle = "roles, permissions, and authorization"
                    MinimumScreenshotSurfaceScanCount = 0
                },
                [pscustomobject]@{
                    ExactContextCount = 1
                    ExactDirectRequestCount = $null
                    ExpectedNetworkFailureCount = 0
                    ExpectedNetworkFailureMethod = $null
                    ExpectedNetworkFailurePathAndQuery = $null
                    FileName =
                        "admin-access-sessions-audit-response-audit.json"
                    TestTitle = "sessions and audit"
                    MinimumScreenshotSurfaceScanCount = 0
                },
                [pscustomobject]@{
                    ExactContextCount = 1
                    ExactDirectRequestCount = $null
                    ExpectedNetworkFailureCount = 1
                    ExpectedNetworkFailureMethod = "GET"
                    ExpectedNetworkFailurePathAndQuery =
                        "/api/admin-access/administrators?Page=1&PageSize=20"
                    FileName =
                        "admin-access-responsive-failures-response-audit.json"
                    TestTitle = "responsive and failure states"
                    MinimumScreenshotSurfaceScanCount = 18
                })) {
            $entry | Add-Member `
                -NotePropertyName SourceFile `
                -NotePropertyValue "admin-access.spec.ts"
            $entry | Add-Member `
                -NotePropertyName ExactScreenshotSurfaceScanCount `
                -NotePropertyValue $(
                    if ($entry.TestTitle -ceq "responsive and failure states") {
                        18
                    }
                    else {
                        $null
                    })
            $null = $manifest.Add($entry)
        }
    }
    return $manifest.ToArray()
}

function Assert-AdminManagementJsonObjectProperties {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Description
    )

    Assert-Condition `
        ($Value -is [pscustomobject]) `
        "$Description must be a JSON object."
    $expectedProperties = @($Expected | Sort-Object -CaseSensitive)
    $actualProperties = @(
        $Value.PSObject.Properties.Name | Sort-Object -CaseSensitive)
    Assert-Condition `
        (($actualProperties -join "`n") -ceq
            ($expectedProperties -join "`n")) `
        "$Description properties differ from the exact case-sensitive schema."
}

function Test-AdminManagementJsonInteger {
    param($Value)

    return $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64]
}

function Assert-AdminManagementExpectedNetworkFailures {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records,
        [Parameter(Mandatory)]$ExpectedReport,
        [Parameter(Mandatory)][string]$Description
    )

    Assert-Condition `
        ($Records.Count -eq
            [int]$ExpectedReport.ExpectedNetworkFailureCount) `
        "$Description has an unexpected network-failure count."
    if ($Records.Count -eq 0) {
        Assert-Condition `
            ($null -eq $ExpectedReport.ExpectedNetworkFailureMethod -and
                $null -eq
                    $ExpectedReport.ExpectedNetworkFailurePathAndQuery) `
            "$Description has an unexpected empty network-failure contract."
        return
    }

    Assert-Condition `
        ($Records.Count -eq 1 -and
            -not [string]::IsNullOrWhiteSpace(
                [string]$ExpectedReport.ExpectedNetworkFailureMethod) -and
            -not [string]::IsNullOrWhiteSpace(
                [string]$ExpectedReport.ExpectedNetworkFailurePathAndQuery)) `
        "$Description has an incomplete exact network-failure contract."
    $record = $Records[0]
    Assert-AdminManagementJsonObjectProperties `
        -Value $record `
        -Expected @("count", "method", "pathAndQuery") `
        -Description "$Description expected network failure"
    Assert-Condition `
        ((Test-AdminManagementJsonInteger $record.count) -and
            [int64]$record.count -eq 1 -and
            $record.method -is [string] -and
            $record.method -ceq
                [string]$ExpectedReport.ExpectedNetworkFailureMethod -and
            $record.pathAndQuery -is [string] -and
            $record.pathAndQuery -ceq
                [string]$ExpectedReport.ExpectedNetworkFailurePathAndQuery -and
            $record.pathAndQuery -cmatch '^/api/[^#\s]+(?:\?[^#\s]+)?$') `
        "$Description differs from the exact expected network failure."
}

function Assert-AdminManagementResponseAuditRawJson {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Description
    )

    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse(
            $Content,
            [System.Text.Json.JsonDocumentOptions]@{
                AllowTrailingCommas = $false
                CommentHandling =
                    [System.Text.Json.JsonCommentHandling]::Disallow
                MaxDepth = 64
            })
        Assert-Condition `
            ($document.RootElement.ValueKind -eq
                [System.Text.Json.JsonValueKind]::Object) `
            "$Description root must be a JSON object."
        $visitJsonElement = {
            param(
                [System.Text.Json.JsonElement]$Element,
                [string]$Path
            )

            if ($Element.ValueKind -eq
                [System.Text.Json.JsonValueKind]::Object) {
                $propertyNames =
                    [System.Collections.Generic.HashSet[string]]::new(
                        [StringComparer]::Ordinal)
                foreach ($property in $Element.EnumerateObject()) {
                    Assert-Condition `
                        $propertyNames.Add($property.Name) `
                        "$Description contains duplicate JSON property '$($property.Name)' at '$Path'."
                    & $visitJsonElement `
                        -Element $property.Value `
                        -Path "$Path.$($property.Name)"
                }
            }
            elseif ($Element.ValueKind -eq
                [System.Text.Json.JsonValueKind]::Array) {
                $itemIndex = 0
                foreach ($item in $Element.EnumerateArray()) {
                    & $visitJsonElement `
                        -Element $item `
                        -Path "$Path[$itemIndex]"
                    $itemIndex++
                }
            }
        }
        & $visitJsonElement -Element $document.RootElement -Path '$'
        foreach ($arrayPropertyName in @(
                "expectedNetworkFailures", "responseBodies", "violations")) {
            $arrayProperty = $document.RootElement.GetProperty(
                $arrayPropertyName)
            Assert-Condition `
                ($arrayProperty.ValueKind -eq
                    [System.Text.Json.JsonValueKind]::Array) `
                "$Description property '$arrayPropertyName' must be a JSON array."
        }
    }
    catch [System.Text.Json.JsonException] {
        throw "$Description is not strict JSON."
    }
    finally {
        if ($null -ne $document) {
            $document.Dispose()
        }
    }
}

function Assert-AdminManagementResponseAuditReports {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)]
        [ValidateSet("LegacyGameConfig", "M35Management")]
        [string]$Mode
    )

    $resolvedRunRoot = Resolve-AdminManagementRunRoot -RunRoot $RunRoot
    $evidenceRoot = Join-Path `
        $resolvedRunRoot `
        "output\playwright\evidence"
    Assert-Condition `
        (Test-Path -LiteralPath $evidenceRoot -PathType Container) `
        "Admin Playwright response-audit directory is missing."
    Assert-AdminManagementTreeHasNoReparsePoints -RootPath $evidenceRoot
    $manifest = @(Get-AdminManagementResponseAuditManifest -Mode $Mode)
    $expectedFileNames = @($manifest | ForEach-Object { $_.FileName })
    $actualFileNames = @(
        Get-ChildItem `
            -LiteralPath $evidenceRoot `
            -File `
            -Filter "*-response-audit.json" `
            -Force `
            -ErrorAction Stop |
            ForEach-Object { $_.Name })
    Assert-StringSet `
        -Expected $expectedFileNames `
        -Actual $actualFileNames `
        -Message "Admin Playwright response-audit reports differ from the exact manifest."

    $validatedReports = [System.Collections.Generic.List[object]]::new()
    foreach ($expectedReport in $manifest) {
        $reportPath = Join-Path $evidenceRoot $expectedReport.FileName
        $reportFile = Get-Item -LiteralPath $reportPath -Force
        $reportContent = Read-AdminManagementRetainedText `
            -ArtifactFile $reportFile
        Assert-AdminManagementResponseAuditRawJson `
            -Content $reportContent `
            -Description "Admin Playwright response-audit '$($expectedReport.FileName)'"
        try {
            $report = $reportContent | ConvertFrom-Json
        }
        catch {
            throw "Admin Playwright response-audit '$($expectedReport.FileName)' is not valid JSON."
        }
        Assert-AdminManagementJsonObjectProperties `
            -Value $report `
            -Expected @(
                "contextLedger", "directRequestLedger",
                "expectedNetworkFailures", "requestLedger", "responseBodies",
                "responseBodyCount", "schemaVersion", "screenshotSurfaceScanCount",
                "sourceFile", "testStatus", "testTitle", "violations") `
            -Description "Admin Playwright response-audit '$($expectedReport.FileName)'"
        $responses = @($report.responseBodies)
        Assert-Condition `
            ((Test-AdminManagementJsonInteger $report.schemaVersion) -and
                [int64]$report.schemaVersion -eq 1 -and
                $report.sourceFile -is [string] -and
                $report.sourceFile -ceq $expectedReport.SourceFile -and
                $report.testTitle -is [string] -and
                $report.testTitle -ceq $expectedReport.TestTitle -and
                $report.testStatus -is [string] -and
                $report.testStatus -ceq "passed" -and
                (Test-AdminManagementJsonInteger $report.responseBodyCount) -and
                [int64]$report.responseBodyCount -gt 0 -and
                [int64]$report.responseBodyCount -eq $responses.Count -and
                @($report.violations).Count -eq 0) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' has invalid summary metadata."
        Assert-Condition `
            (Test-AdminManagementJsonInteger `
                $report.screenshotSurfaceScanCount) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' has a non-integer screenshot scan count."
        $screenshotScanCount = [int64]$report.screenshotSurfaceScanCount
        Assert-Condition `
            ($screenshotScanCount -ge
                [int]$expectedReport.MinimumScreenshotSurfaceScanCount) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' has too few screenshot-surface scans."
        if ($null -ne $expectedReport.ExactScreenshotSurfaceScanCount) {
            Assert-Condition `
                ($screenshotScanCount -eq
                    [int]$expectedReport.ExactScreenshotSurfaceScanCount) `
                "Admin Playwright response-audit '$($expectedReport.FileName)' changed its exact screenshot-surface scan count."
        }

        $expectedNetworkFailures = @($report.expectedNetworkFailures)
        Assert-AdminManagementExpectedNetworkFailures `
            -Records $expectedNetworkFailures `
            -ExpectedReport $expectedReport `
            -Description "Admin Playwright response-audit '$($expectedReport.FileName)'"

        $contextLedger = $report.contextLedger
        Assert-AdminManagementJsonObjectProperties `
            -Value $contextLedger `
            -Expected @("closedBeforeListenerDetach", "observed") `
            -Description "Admin Playwright context ledger"
        Assert-Condition `
            ((Test-AdminManagementJsonInteger $contextLedger.observed) -and
                (Test-AdminManagementJsonInteger `
                    $contextLedger.closedBeforeListenerDetach) -and
                [int64]$contextLedger.observed -eq
                    [int]$expectedReport.ExactContextCount -and
                [int64]$contextLedger.closedBeforeListenerDetach -eq
                    [int64]$contextLedger.observed) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' has an incomplete context ledger."

        $directLedger = $report.directRequestLedger
        Assert-AdminManagementJsonObjectProperties `
            -Value $directLedger `
            -Expected @(
                "directApiRequestsCompleted", "directApiRequestsFailed",
                "directApiRequestsObserved") `
            -Description "Admin Playwright direct-request ledger"
        foreach ($directLedgerProperty in @(
                "directApiRequestsCompleted", "directApiRequestsFailed",
                "directApiRequestsObserved")) {
            Assert-Condition `
                (Test-AdminManagementJsonInteger `
                    $directLedger.$directLedgerProperty) `
                "Admin Playwright response-audit '$($expectedReport.FileName)' has a non-integer direct-request ledger."
        }
        Assert-Condition `
            ([int64]$directLedger.directApiRequestsFailed -eq 0 -and
                [int64]$directLedger.directApiRequestsCompleted -eq
                    [int64]$directLedger.directApiRequestsObserved -and
                ($null -eq $expectedReport.ExactDirectRequestCount -or
                    [int64]$directLedger.directApiRequestsObserved -eq
                        [int]$expectedReport.ExactDirectRequestCount)) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' has an incomplete direct-request ledger."

        $ledger = $report.requestLedger
        Assert-AdminManagementJsonObjectProperties `
            -Value $ledger `
            -Expected @(
                "browserRequestFailuresExpected", "browserRequestsCompleted",
                "browserRequestsFailed", "browserRequestsObserved",
                "browserResponsesObserved") `
            -Description "Admin Playwright browser-request ledger"
        foreach ($ledgerProperty in @(
                "browserRequestFailuresExpected", "browserRequestsCompleted",
                "browserRequestsFailed", "browserRequestsObserved",
                "browserResponsesObserved")) {
            Assert-Condition `
                (Test-AdminManagementJsonInteger $ledger.$ledgerProperty) `
                "Admin Playwright response-audit '$($expectedReport.FileName)' has a non-integer browser-request ledger."
        }
        Assert-Condition `
            ([int64]$ledger.browserRequestsObserved -gt 0 -and
                [int64]$ledger.browserRequestFailuresExpected -eq
                    [int]$expectedReport.ExpectedNetworkFailureCount -and
                [int64]$ledger.browserRequestsFailed -eq
                    [int64]$ledger.browserRequestFailuresExpected -and
                [int64]$ledger.browserRequestsObserved -eq
                    [int64]$ledger.browserRequestsCompleted +
                        [int64]$ledger.browserRequestsFailed -and
                [int64]$ledger.browserResponsesObserved -eq
                    [int64]$ledger.browserRequestsCompleted) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' has an incomplete browser request ledger."
        Assert-Condition `
            ($responses.Count -eq
                [int64]$ledger.browserResponsesObserved +
                    [int64]$directLedger.directApiRequestsCompleted) `
            "Admin Playwright response-audit '$($expectedReport.FileName)' response count does not match its request ledgers."

        for ($responseIndex = 0;
            $responseIndex -lt $responses.Count;
            $responseIndex++) {
            $response = $responses[$responseIndex]
            Assert-AdminManagementJsonObjectProperties `
                -Value $response `
                -Expected @(
                    "bodyBytes", "method", "route", "scanned", "sequence",
                    "status") `
                -Description "Admin Playwright response-audit response $responseIndex"
            Assert-Condition `
                ((Test-AdminManagementJsonInteger $response.sequence) -and
                    [int64]$response.sequence -eq $responseIndex -and
                    $response.method -is [string] -and
                    $response.method -cmatch
                        '^(?:DELETE|GET|PATCH|POST|PUT)$' -and
                    $response.route -is [string] -and
                    $response.route -cmatch '^/api/[^?#]*$' -and
                    (Test-AdminManagementJsonInteger $response.status) -and
                    [int64]$response.status -ge 100 -and
                    [int64]$response.status -le 599 -and
                    (Test-AdminManagementJsonInteger $response.bodyBytes) -and
                    [int64]$response.bodyBytes -ge 0 -and
                    [int64]$response.bodyBytes -le 20MB -and
                    $response.scanned -is [bool] -and
                    $response.scanned -eq $true) `
                "Admin Playwright response-audit '$($expectedReport.FileName)' response $responseIndex is invalid or unscanned."
        }
        $null = $validatedReports.Add([pscustomobject]@{
                FileName = [string]$expectedReport.FileName
                ResponseCount = $responses.Count
                ScreenshotSurfaceScanCount = $screenshotScanCount
                Sha256 = (Get-FileHash `
                    -LiteralPath $reportPath `
                    -Algorithm SHA256).Hash
            })
    }

    return $validatedReports.ToArray()
}

function Write-AdminManagementProbePng {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [switch]$NearBlank,
        [switch]$Solid,
        [switch]$Transparent
    )

    Add-Type -AssemblyName PresentationCore -ErrorAction Stop
    $stride = $Width * 4
    $pixels = [byte[]]::new($stride * $Height)
    for ($y = 0; $y -lt $Height; $y++) {
        for ($x = 0; $x -lt $Width; $x++) {
            $offset = ($y * $stride) + ($x * 4)
            $isNearBlankOutlier = $NearBlank -and
                (($y * $Width) + $x) -lt 15
            if ($Solid -or ($NearBlank -and -not $isNearBlankOutlier)) {
                $pixels[$offset] = 240
                $pixels[$offset + 1] = 240
                $pixels[$offset + 2] = 240
            }
            else {
                $pixels[$offset] = [byte](($x * 7 + $y * 3) % 256)
                $pixels[$offset + 1] = [byte](($x * 5 + $y * 11) % 256)
                $pixels[$offset + 2] = [byte](($x * 13 + $y * 2) % 256)
            }
            $pixels[$offset + 3] = if ($Transparent) { 0 } else { 255 }
        }
    }
    $bitmap = [System.Windows.Media.Imaging.BitmapSource]::Create(
        $Width,
        $Height,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Bgra32,
        $null,
        $pixels,
        $stride)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add(
        [System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $encoder.Save($stream)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Write-AdminManagementStrideAlignedProbePng {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [switch]$TransparentBackground
    )

    Add-Type -AssemblyName PresentationCore -ErrorAction Stop
    Initialize-AdminManagementScreenshotPixelInspectorType
    $pixels = [NovaGame.Verification.ScreenshotPixelInspector]::
        CreateStrideAlignedProbe(
            $Width,
            $Height,
            $TransparentBackground.IsPresent)
    $stride = $Width * 4
    $bitmap = [System.Windows.Media.Imaging.BitmapSource]::Create(
        $Width,
        $Height,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Bgra32,
        $null,
        $pixels,
        $stride)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add(
        [System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $encoder.Save($stream)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Test-AdminManagementSuccessfulCleanupRetention {
    $parseTokens = $null
    $parseErrors = $null
    $verifyAst = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $repositoryRoot "scripts\verify.ps1"),
        [ref]$parseTokens,
        [ref]$parseErrors)
    Assert-Condition ($parseErrors.Count -eq 0) `
        "Successful cleanup retention proof requires a valid verification script."
    $browserFunctions = @($verifyAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq "Invoke-AdminEndToEnd"
    }, $false))
    Assert-Condition ($browserFunctions.Count -eq 1) `
        "Successful cleanup retention proof requires the exact browser caller."
    $successCleanupBranches = @($browserFunctions[0].Body.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.IfStatementAst] -and
            $node.Clauses[0].Item1.Extent.Text.Contains('$artifactSecretScanPassed') -and
            $node.Clauses[0].Item1.Extent.Text.Contains('$logRoot')
    }, $false))
    Assert-Condition ($successCleanupBranches.Count -eq 1) `
        "Successful cleanup retention proof requires the exact final success branch."
    $successCleanup = [scriptblock]::Create(
        $successCleanupBranches[0].Extent.Text)
    $adminManagementRoot = Join-Path $repositoryRoot ".local\admin-management"
    $runRoot = Join-Path `
        $adminManagementRoot `
        "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $logRoot = $runRoot
    $operationError = $null
    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    $artifactSecretScanPassed = $true
    $artifactSensitiveValues = @("synthetic-sensitive-value-not-in-fixture")
    $allowedScreenshotRelativePaths = @("output/playwright/visual/probe.png")
    $screenshotPath = Join-Path $runRoot $allowedScreenshotRelativePaths[0]
    $auditPath = Join-Path $runRoot "response-audit.json"
    $rawLogPath = Join-Path $runRoot "api.stdout.log"
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $screenshotPath) `
            -Force | Out-Null
        Write-AdminManagementProbePng -Path $screenshotPath -Width 64 -Height 64
        $screenshotInspectionRecords = @([pscustomobject]@{
            RelativePath = $allowedScreenshotRelativePaths[0]
            Sha256 = (Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash
            Width = 64
            Height = 64
        })
        [System.IO.File]::WriteAllText($auditPath, '{"fixture":"synthetic"}')
        [System.IO.File]::WriteAllText($rawLogPath, "Synthetic raw transport log.")
        & $successCleanup
        Assert-Condition ($cleanupErrors.Count -eq 0) `
            "Successful cleanup retention proof encountered a cleanup error."
        Assert-Condition (Test-Path -LiteralPath $screenshotPath -PathType Leaf) `
            "Final successful cleanup must retain its validated screenshot."
        Assert-Condition `
            ((Test-Path -LiteralPath $auditPath -PathType Leaf) -and
                [System.IO.File]::ReadAllText($auditPath) -ceq '{"fixture":"synthetic"}') `
            "Final successful cleanup must preserve its scanned response-audit evidence."
        Assert-Condition (-not (Test-Path -LiteralPath $rawLogPath)) `
            "Final successful cleanup must remove raw process transcripts."
        Assert-AdminManagementScreenshotIdentity `
            -RunRoot $runRoot -InspectionRecords $screenshotInspectionRecords
        Remove-AdminManagementNonRetainableArtifacts -ArtifactRoot $runRoot
        Assert-Condition (-not (Test-Path -LiteralPath $screenshotPath)) `
            "Failure reduction must still remove screenshot evidence."
    }
    finally {
        Remove-AdminManagementRunRootVerified -RunRoot $runRoot -MaximumAttempts 6
    }
}

function Test-AdminManagementArtifactPolicy {
    $probeRunRoot = Join-Path `
        $repositoryRoot `
        ".local\admin-management\novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    try {
        $dynamicCollectorProbe = New-AdminManagementDynamicSecretCollector
        try {
            Assert-Condition `
                (Send-AdminManagementDynamicSecretProbe `
                    -Channel $dynamicCollectorProbe `
                    -Kind 1 `
                    -Value "eyJhbGciOiJIUzI1NiJ9.cGF5bG9hZA.c2lnbmF0dXJl") `
                "The dynamic-secret collector rejected a valid access token."
            Assert-Condition `
                (Send-AdminManagementDynamicSecretProbe `
                    -Channel $dynamicCollectorProbe `
                    -Kind 2 `
                    -Value ("A" * 43)) `
                "The dynamic-secret collector rejected a valid refresh token."
            Assert-Condition `
                (Send-AdminManagementDynamicSecretProbe `
                    -Channel $dynamicCollectorProbe `
                    -Kind 2 `
                    -Value ("A" * 43)) `
                "The dynamic-secret collector rejected a duplicate registration."
            $dynamicCollectorRecords = @(
                $dynamicCollectorProbe.Collector.StopAndSnapshot(
                    [TimeSpan]::FromSeconds(5)))
            Assert-Condition `
                ($dynamicCollectorRecords.Count -eq 2) `
                "The dynamic-secret collector did not deduplicate probe records."
        }
        finally {
            $dynamicCollectorProbe.Collector.Dispose()
        }

        $invalidDynamicCollectorProbe =
            New-AdminManagementDynamicSecretCollector
        try {
            Assert-Condition `
                (Send-AdminManagementDynamicSecretProbe `
                    -Channel $invalidDynamicCollectorProbe `
                    -Kind 1 `
                    -Value "eyJhbGciOiJIUzI1NiJ9.cGF5bG9hZA.c2lnbmF0dXJl") `
                "The faulted dynamic-secret probe rejected its valid access token."
            Assert-Condition `
                (Send-AdminManagementDynamicSecretProbe `
                    -Channel $invalidDynamicCollectorProbe `
                    -Kind 2 `
                    -Value ("B" * 43)) `
                "The faulted dynamic-secret probe rejected its valid refresh token."
            Assert-Condition `
                (-not (Send-AdminManagementDynamicSecretProbe `
                    -Channel $invalidDynamicCollectorProbe `
                    -Kind 1 `
                    -Value "eyJhbGciOiJIUzI1NiJ9.cGF5bG9hZA.c2lnbmF0dXJl" `
                    -InvalidAuthenticationCode)) `
                "The dynamic-secret collector acknowledged an invalid HMAC."
            $invalidDynamicCollectorRejected = $false
            try {
                $null = $invalidDynamicCollectorProbe.Collector.StopAndSnapshot(
                    [TimeSpan]::FromSeconds(5))
            }
            catch {
                $invalidDynamicCollectorRejected = $_.Exception.Message.Contains(
                    "dynamic-secret-registry-protocol",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $invalidDynamicCollectorRejected `
                "The dynamic-secret collector did not fail closed after an invalid HMAC."
            $faultedDynamicCollectorRecords = @(
                $invalidDynamicCollectorProbe.Collector.SnapshotForCleanup())
            Assert-Condition `
                ($faultedDynamicCollectorRecords.Count -eq 2) `
                "The faulted dynamic-secret collector lost its cleanup-only snapshot."
        }
        finally {
            $invalidDynamicCollectorProbe.Collector.Dispose()
        }

        $partialDynamicCollectorProbe =
            New-AdminManagementDynamicSecretCollector
        $partialDynamicCollectorClient = $null
        try {
            $partialDynamicCollectorClient =
                [System.IO.Pipes.NamedPipeClientStream]::new(
                    ".",
                    [string]$partialDynamicCollectorProbe.PipeName,
                    [System.IO.Pipes.PipeDirection]::InOut,
                    [System.IO.Pipes.PipeOptions]::None)
            $partialDynamicCollectorClient.Connect(5000)
            $partialFrame = [Text.Encoding]::ASCII.GetBytes("NGDS")
            $partialDynamicCollectorClient.Write(
                $partialFrame,
                0,
                $partialFrame.Length)
            $partialDynamicCollectorClient.Flush()
            $partialDynamicCollectorStopTimedOut = $false
            try {
                $null = $partialDynamicCollectorProbe.Collector.StopAndSnapshot(
                    [TimeSpan]::FromMilliseconds(250))
            }
            catch {
                $partialDynamicCollectorStopTimedOut = $_.Exception.Message.Contains(
                    "dynamic-secret-registry-stop-timeout",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                ($partialDynamicCollectorStopTimedOut -and
                    $partialDynamicCollectorClient.IsConnected) `
                "Stopping the dynamic-secret collector canceled an accepted partial frame."

            $partialDynamicCollectorDisposeTimedOut = $false
            try {
                $partialDynamicCollectorProbe.Collector.Dispose(
                    [TimeSpan]::FromMilliseconds(250))
            }
            catch {
                $partialDynamicCollectorDisposeTimedOut = $_.Exception.Message.Contains(
                    "dynamic-secret-registry-stop-timeout",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                ($partialDynamicCollectorDisposeTimedOut -and
                    $partialDynamicCollectorClient.IsConnected) `
                "The dynamic-secret collector released worker-owned state before joining."

            $prematureDynamicCollectorSnapshotRejected = $false
            try {
                $null = $partialDynamicCollectorProbe.Collector.SnapshotForCleanup()
            }
            catch {
                $prematureDynamicCollectorSnapshotRejected =
                    $_.Exception.Message.Contains(
                        "dynamic-secret-registry-cleanup-snapshot-before-join",
                        [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $prematureDynamicCollectorSnapshotRejected `
                "The dynamic-secret collector exposed worker-owned state before joining."

            $partialDynamicCollectorClient.Dispose()
            $partialDynamicCollectorClient = $null
            $partialDynamicCollectorRejected = $false
            try {
                $null = $partialDynamicCollectorProbe.Collector.StopAndSnapshot(
                    [TimeSpan]::FromSeconds(5))
            }
            catch {
                $partialDynamicCollectorRejected = $_.Exception.Message.Contains(
                    "dynamic-secret-registry-protocol",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $partialDynamicCollectorRejected `
                "The dynamic-secret collector did not drain and fault the closed partial frame."
            Assert-Condition `
                (@($partialDynamicCollectorProbe.Collector.SnapshotForCleanup()).Count -eq 0) `
                "The faulted partial-frame collector exposed an unexpected record."
        }
        finally {
            if ($null -ne $partialDynamicCollectorClient) {
                $partialDynamicCollectorClient.Dispose()
            }
            $partialDynamicCollectorProbe.Collector.Dispose()
        }

        New-Item `
            -ItemType Directory `
            -Path $probeRunRoot `
            -Force | Out-Null
        $preservedHashes = @{}
        foreach ($extension in Get-AdminManagementRetainableTextExtensions) {
            $path = Join-Path `
                $probeRunRoot `
                "valid$($extension.ToLowerInvariant())"
            $nonAsciiProbe = "Unicode: $([char]0x00E9)"
            [System.IO.File]::WriteAllText(
                $path,
                "NovaGame retained evidence`r`n`t$nonAsciiProbe`n",
                $utf8)
            $preservedHashes[[System.IO.Path]::GetFileName($path)] =
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
        foreach ($fileName in @(
            "binary.har", "binary.mp4", "binary.trace", "binary.tar",
            "binary.gz", "binary.tgz", "binary.7z", "binary.pdf",
            "binary.dmp", "binary.png", "binary.zip", "database.env",
            "pnpm.cmd", "extensionless", "api.stdout.log",
            "api.stderr.log", "dbmigrator.stdout.log",
            "dbmigrator.stderr.log", "playwright.stdout.log",
            "playwright.stderr.log")) {
            [System.IO.File]::WriteAllBytes(
                (Join-Path $probeRunRoot $fileName),
                [byte[]](0, 1, 2, 255))
        }
        $playwrightFailureContextProbePath = Join-Path `
            $probeRunRoot `
            "playwright-output\policy-probe\error-context.md"
        New-Item `
            -ItemType Directory `
            -Path (Split-Path -Parent $playwrightFailureContextProbePath) `
            -Force | Out-Null
        [System.IO.File]::WriteAllText(
            $playwrightFailureContextProbePath,
            "Generated Playwright failure context.",
            $utf8)

        $probeScreenshotRelativePath =
            "output/playwright/visual/probe.png"
        $probeScreenshotPath = Join-Path `
            $probeRunRoot `
            $probeScreenshotRelativePath.Replace('/', '\')
        New-Item `
            -ItemType Directory `
            -Path (Split-Path -Parent $probeScreenshotPath) `
            -Force | Out-Null
        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64
        $probeScreenshotSpecification = [pscustomobject]@{
            RelativePath = $probeScreenshotRelativePath
            Width = 64
            MinimumHeight = 64
            MaximumHeight = 64
        }

        Remove-AdminManagementNonRetainableArtifacts `
            -ArtifactRoot $probeRunRoot `
            -AllowedScreenshotRelativePaths @($probeScreenshotRelativePath)
        Assert-Condition `
            (-not (Test-Path `
                    -LiteralPath $playwrightFailureContextProbePath `
                    -PathType Leaf)) `
            "Admin management cleanup retained a Playwright failure context."
        $remainingFiles = @(
            Get-ChildItem `
                -LiteralPath $probeRunRoot `
                -File `
                -Force |
                ForEach-Object { $_.Name } |
                Sort-Object)
        $expectedRemainingFiles = @($preservedHashes.Keys | Sort-Object)
        Assert-Condition `
            (($remainingFiles -join "`n") -ceq
                ($expectedRemainingFiles -join "`n")) `
            "Admin management retainable-text policy kept an unexpected artifact."
        foreach ($fileName in $expectedRemainingFiles) {
            Assert-Condition `
                ((Get-FileHash `
                    -LiteralPath (Join-Path $probeRunRoot $fileName) `
                    -Algorithm SHA256).Hash -eq $preservedHashes[$fileName]) `
                "Admin management retainable-text cleanup modified '$fileName'."
        }
        Assert-Condition `
            (Test-Path -LiteralPath $probeScreenshotPath -PathType Leaf) `
            "Admin management successful cleanup removed an allowlisted screenshot."
        $probeScreenshotInspectionRecords = @(
            Assert-AdminManagementSuccessfulScreenshots `
            -RunRoot $probeRunRoot `
            -ScreenshotSpecifications @($probeScreenshotSpecification))
        Assert-AdminManagementScreenshotIdentity `
            -RunRoot $probeRunRoot `
            -InspectionRecords $probeScreenshotInspectionRecords

        $validProbePngBytes = [System.IO.File]::ReadAllBytes(
            $probeScreenshotPath)
        $trailingBytes = [Text.Encoding]::ASCII.GetBytes("trailing-data")
        [System.IO.File]::WriteAllBytes(
            $probeScreenshotPath,
            [byte[]]@($validProbePngBytes + $trailingBytes))
        $trailingPngRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $trailingPngRejected = $_.Exception.Message.Contains(
                "terminal PNG IEND",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $trailingPngRejected `
            "Admin management evidence must reject bytes after terminal PNG IEND."

        Initialize-AdminManagementScreenshotPixelInspectorType
        $textChunkPng = [NovaGame.Verification.ScreenshotPixelInspector]::
            InsertChunkBeforeIend(
                $validProbePngBytes,
                "tEXt",
                [Text.Encoding]::ASCII.GetBytes("Comment`0carrier"))
        [System.IO.File]::WriteAllBytes($probeScreenshotPath, $textChunkPng)
        $textChunkRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $textChunkRejected = $_.Exception.Message.Contains(
                "unsafe PNG chunk 'tEXt'",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $textChunkRejected `
            "Admin management evidence must reject PNG text carrier chunks."

        $unknownChunkPng = [NovaGame.Verification.ScreenshotPixelInspector]::
            InsertChunkBeforeIend(
                $validProbePngBytes,
                "ruSt",
                [byte[]](1, 2, 3, 4))
        [System.IO.File]::WriteAllBytes($probeScreenshotPath, $unknownChunkPng)
        $unknownChunkRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $unknownChunkRejected = $_.Exception.Message.Contains(
                "unsafe PNG chunk 'ruSt'",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $unknownChunkRejected `
            "Admin management evidence must reject unknown PNG carrier chunks."

        $pngSecret = "ABCDEFGH"
        $pngSecretBytes = [Text.Encoding]::ASCII.GetBytes($pngSecret)
        $physicalChunkData = [byte[]]::new(9)
        $pngSecretBytes.CopyTo($physicalChunkData, 0)
        $secretCarrierPng = [NovaGame.Verification.ScreenshotPixelInspector]::
            InsertChunkBeforeIend(
                $validProbePngBytes,
                "pHYs",
                $physicalChunkData)
        [System.IO.File]::WriteAllBytes($probeScreenshotPath, $secretCarrierPng)
        $secretCarrierInspectionRecords = @(
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification))
        $pngSecretRejected = $false
        try {
            Assert-AdminManagementArtifactSecrets `
                -ArtifactRoot $probeRunRoot `
                -SensitiveValues @($pngSecret) `
                -AllowedScreenshotRelativePaths @(
                    $probeScreenshotRelativePath) `
                -ScreenshotInspectionRecords $secretCarrierInspectionRecords
        }
        catch {
            $pngSecretRejected = $_.Exception.Message.Contains(
                "exact sensitive value",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $pngSecretRejected `
            "Admin management evidence must byte-scan exact secrets inside allowlisted PNGs."
        [System.IO.File]::WriteAllBytes(
            $probeScreenshotPath,
            $validProbePngBytes)

        $extraScreenshotPath = Join-Path `
            (Split-Path -Parent $probeScreenshotPath) `
            "extra.png"
        Write-AdminManagementProbePng `
            -Path $extraScreenshotPath `
            -Width 64 `
            -Height 64
        $extraScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $extraScreenshotRejected = $_.Exception.Message.Contains(
                "exact allowlist",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $extraScreenshotRejected `
            "Admin management evidence must reject an extra successful PNG."
        Remove-Item -LiteralPath $extraScreenshotPath -Force

        [System.IO.File]::WriteAllBytes(
            $probeScreenshotPath,
            [byte[]](0, 1, 2, 255))
        $corruptScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $corruptScreenshotRejected = $_.Exception.Message.Contains(
                "invalid PNG byte length",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $corruptScreenshotRejected `
            "Admin management evidence must reject a corrupt successful PNG."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64 `
            -Solid
        $blankScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $blankScreenshotRejected = $_.Exception.Message.Contains(
                "pixel diversity",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $blankScreenshotRejected `
            "Admin management evidence must reject a blank successful PNG."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64 `
            -Transparent
        $transparentScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $transparentScreenshotRejected = $_.Exception.Message.Contains(
                "transparent or lacks an opaque surface",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $transparentScreenshotRejected `
            "Admin management evidence must reject a transparent PNG with hidden RGB diversity."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64 `
            -NearBlank
        $nearBlankScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $nearBlankScreenshotRejected = $_.Exception.Message.Contains(
                "pixel diversity",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $nearBlankScreenshotRejected `
            "Admin management evidence must reject a near-blank PNG with sparse outliers."

        Write-AdminManagementStrideAlignedProbePng `
            -Path $probeScreenshotPath `
            -Width 1440 `
            -Height 900 `
            -TransparentBackground
        $alignedTransparentSpecification = [pscustomobject]@{
            RelativePath = $probeScreenshotRelativePath
            Width = 1440
            MinimumHeight = 900
            MaximumHeight = 900
        }
        $alignedTransparentScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @(
                    $alignedTransparentSpecification) |
                Out-Null
        }
        catch {
            $alignedTransparentScreenshotRejected = $_.Exception.Message.Contains(
                "transparent or lacks an opaque surface",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $alignedTransparentScreenshotRejected `
            "Admin management evidence must inspect every pixel of a sample-aligned transparent PNG."

        Write-AdminManagementStrideAlignedProbePng `
            -Path $probeScreenshotPath `
            -Width 1440 `
            -Height 900
        $alignedDesktopNearBlankSpecification = [pscustomobject]@{
            RelativePath = $probeScreenshotRelativePath
            Width = 1440
            MinimumHeight = 900
            MaximumHeight = 900
        }
        $alignedDesktopNearBlankScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @(
                    $alignedDesktopNearBlankSpecification) |
                Out-Null
        }
        catch {
            $alignedDesktopNearBlankScreenshotRejected =
                $_.Exception.Message.Contains(
                    "pixel diversity",
                    [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $alignedDesktopNearBlankScreenshotRejected `
            "Admin management evidence must inspect every pixel of a desktop sample-aligned near-blank PNG."

        Write-AdminManagementStrideAlignedProbePng `
            -Path $probeScreenshotPath `
            -Width 1440 `
            -Height 3600
        $alignedNearBlankSpecification = [pscustomobject]@{
            RelativePath = $probeScreenshotRelativePath
            Width = 1440
            MinimumHeight = 3600
            MaximumHeight = 3600
        }
        $alignedNearBlankScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($alignedNearBlankSpecification) |
                Out-Null
        }
        catch {
            $alignedNearBlankScreenshotRejected = $_.Exception.Message.Contains(
                "pixel diversity",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $alignedNearBlankScreenshotRejected `
            "Admin management evidence must inspect every pixel of a tall sample-aligned near-blank PNG."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64
        $wrongSizeSpecification = [pscustomobject]@{
            RelativePath = $probeScreenshotRelativePath
            Width = 63
            MinimumHeight = 64
            MaximumHeight = 64
        }
        $wrongSizeScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($wrongSizeSpecification) |
                Out-Null
        }
        catch {
            $wrongSizeScreenshotRejected = $_.Exception.Message.Contains(
                "declares dimensions",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $wrongSizeScreenshotRejected `
            "Admin management evidence must reject a wrong-size PNG."

        Remove-Item -LiteralPath $probeScreenshotPath -Force
        $missingScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification) |
                Out-Null
        }
        catch {
            $missingScreenshotRejected = $_.Exception.Message.Contains(
                "exact allowlist",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $missingScreenshotRejected `
            "Admin management evidence must reject a missing PNG."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64
        $oversizedHeaderBytes = [System.IO.File]::ReadAllBytes(
            $probeScreenshotPath)
        foreach ($headerOffset in @(16, 20)) {
            $oversizedHeaderBytes[$headerOffset] = 0
            $oversizedHeaderBytes[$headerOffset + 1] = 0
            $oversizedHeaderBytes[$headerOffset + 2] = 19
            $oversizedHeaderBytes[$headerOffset + 3] = 136
        }
        [System.IO.File]::WriteAllBytes(
            $probeScreenshotPath,
            $oversizedHeaderBytes)
        $oversizedSpecification = [pscustomobject]@{
            RelativePath = $probeScreenshotRelativePath
            Width = 5000
            MinimumHeight = 5000
            MaximumHeight = 5000
        }
        $oversizedScreenshotRejected = $false
        try {
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($oversizedSpecification) |
                Out-Null
        }
        catch {
            $oversizedScreenshotRejected = $_.Exception.Message.Contains(
                "pre-decode pixel-inspection bound",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $oversizedScreenshotRejected `
            "Admin management evidence must reject oversized IHDR dimensions before decode."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64
        $identityProbeRecords = @(
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification))
        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64 `
            -Solid
        $mutatedScreenshotRejected = $false
        try {
            Assert-AdminManagementScreenshotIdentity `
                -RunRoot $probeRunRoot `
                -InspectionRecords $identityProbeRecords
        }
        catch {
            $mutatedScreenshotRejected = $_.Exception.Message.Contains(
                "changed before retained-artifact scanning",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $mutatedScreenshotRejected `
            "Admin management evidence must reject a post-validation PNG mutation."

        Write-AdminManagementProbePng `
            -Path $probeScreenshotPath `
            -Width 64 `
            -Height 64
        $probeScreenshotInspectionRecords = @(
            Assert-AdminManagementSuccessfulScreenshots `
                -RunRoot $probeRunRoot `
                -ScreenshotSpecifications @($probeScreenshotSpecification))
        Assert-AdminManagementArtifactSecrets `
            -ArtifactRoot $probeRunRoot `
            -SensitiveValues @("not-present") `
            -AllowedScreenshotRelativePaths @($probeScreenshotRelativePath) `
            -ScreenshotInspectionRecords $probeScreenshotInspectionRecords
        Remove-AdminManagementNonRetainableArtifacts `
            -ArtifactRoot $probeRunRoot
        Assert-Condition `
            (@(Get-ChildItem `
                    -LiteralPath $probeRunRoot `
                    -Recurse `
                    -File `
                    -Filter "*.png" `
                    -Force).Count -eq 0) `
            "Admin management failed cleanup must remove every PNG."
        Assert-AdminManagementArtifactSecrets `
            -ArtifactRoot $probeRunRoot `
            -SensitiveValues @("not-present")

        $invalidUtf8Path = Join-Path $probeRunRoot "invalid-utf8.log"
        [System.IO.File]::WriteAllBytes(
            $invalidUtf8Path,
            [byte[]](0xC3, 0x28))
        $invalidUtf8Rejected = $false
        try {
            Assert-AdminManagementArtifactSecrets `
                -ArtifactRoot $probeRunRoot `
                -SensitiveValues @("not-present")
        }
        catch {
            $invalidUtf8Rejected = $_.Exception.Message.Contains(
                "not strict UTF-8",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $invalidUtf8Rejected `
            "Admin management evidence must reject invalid UTF-8."
        Remove-Item -LiteralPath $invalidUtf8Path -Force

        foreach ($controlCase in @(
                [pscustomobject]@{ Name = "nul"; Bytes = [byte[]](65, 0, 66) },
                [pscustomobject]@{ Name = "c0"; Bytes = [byte[]](65, 1, 66) },
                [pscustomobject]@{ Name = "del"; Bytes = [byte[]](65, 127, 66) },
                [pscustomobject]@{ Name = "c1"; Bytes = [byte[]](65, 0xC2, 0x80, 66) })) {
            $controlPath = Join-Path `
                $probeRunRoot `
                "$($controlCase.Name).log"
            [System.IO.File]::WriteAllBytes(
                $controlPath,
                $controlCase.Bytes)
            $controlRejected = $false
            try {
                Assert-AdminManagementArtifactSecrets `
                    -ArtifactRoot $probeRunRoot `
                    -SensitiveValues @("not-present")
            }
            catch {
                $controlRejected = $_.Exception.Message.Contains(
                    "unexpected control character",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $controlRejected `
                "Admin management evidence must reject '$($controlCase.Name)' controls."
            Remove-Item -LiteralPath $controlPath -Force
        }

        $secretPath = Join-Path $probeRunRoot "secret.log"
        [System.IO.File]::WriteAllText(
            $secretPath,
            "exact-probe-secret",
            $utf8)
        $exactSecretRejected = $false
        try {
            Assert-AdminManagementArtifactSecrets `
                -ArtifactRoot $probeRunRoot `
                -SensitiveValues @("exact-probe-secret")
        }
        catch {
            $exactSecretRejected = $_.Exception.Message.Contains(
                "exact sensitive value",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $exactSecretRejected `
            "Admin management evidence must reject exact sensitive values."
        $exactSecretHash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                [Text.UTF8Encoding]::new($false).GetBytes(
                    "exact-probe-secret")))
        [System.IO.File]::WriteAllText(
            $secretPath,
            $exactSecretHash,
            $utf8)
        $derivedSecretRejected = $false
        try {
            Assert-AdminManagementArtifactSecrets `
                -ArtifactRoot $probeRunRoot `
                -SensitiveValues @("exact-probe-secret")
        }
        catch {
            $derivedSecretRejected = $_.Exception.Message.Contains(
                "exact sensitive value",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $derivedSecretRejected `
            "Admin management evidence must reject derived sensitive hashes."
        $opaqueTokenProbe = "A" * 43
        $decodedOpaqueToken = [Convert]::FromBase64String(
            $opaqueTokenProbe.Replace('-', '+').Replace('_', '/') + '=')
        try {
            $decodedOpaqueHash = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData(
                    $decodedOpaqueToken))
        }
        finally {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory(
                $decodedOpaqueToken)
        }
        [System.IO.File]::WriteAllText(
            $secretPath,
            $decodedOpaqueHash,
            $utf8)
        $decodedOpaqueHashRejected = $false
        try {
            Assert-AdminManagementArtifactSecrets `
                -ArtifactRoot $probeRunRoot `
                -SensitiveValues @($opaqueTokenProbe)
        }
        catch {
            $decodedOpaqueHashRejected = $_.Exception.Message.Contains(
                "exact sensitive value",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $decodedOpaqueHashRejected `
            "Admin management evidence must reject decoded-token hash representations."
        [System.IO.File]::WriteAllText(
            $secretPath,
            "password: string",
            $utf8)
        Assert-AdminManagementArtifactSecrets `
            -ArtifactRoot $probeRunRoot `
            -SensitiveValues @("not-present")

        foreach ($secretPatternProbe in @(
                "eyJhbGciOiJIUzI1NiJ9.cGF5bG9hZA.c2lnbmF0dXJl",
                "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abcde",
                "AQAAAAEAACcQAAAAEDhQMTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDEyMzQ1Njc4OTA=")) {
            [System.IO.File]::WriteAllText(
                $secretPath,
                $secretPatternProbe,
                $utf8)
            $patternSecretRejected = $false
            try {
                Assert-AdminManagementArtifactSecrets `
                    -ArtifactRoot $probeRunRoot `
                    -SensitiveValues @("not-present")
            }
            catch {
                $patternSecretRejected = $_.Exception.Message.Contains(
                    "retained-secret pattern",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $patternSecretRejected `
                "Admin management evidence must reject structured secret patterns."
        }
        Remove-Item -LiteralPath $secretPath -Force

        $playwrightFailureProbePath = Join-Path `
            $probeRunRoot `
            "playwright-failure-probe.json"
        $playwrightFailureProbe = [ordered]@{
            errors = @()
            stats = [ordered]@{
                expected = 0
                flaky = 0
                skipped = 0
                unexpected = 1
            }
            suites = @(
                [ordered]@{
                    file = "game-config.spec.ts"
                    specs = @(
                        [ordered]@{
                            file = "game-config.spec.ts"
                            title =
                                "operator completes the GameConfig lifecycle against the real API"
                            tests = @(
                                [ordered]@{
                                    expectedStatus = "passed"
                                    status = "unexpected"
                                    results = @(
                                        [ordered]@{
                                            errors = @(
                                                [ordered]@{
                                                    location = [ordered]@{
                                                        column = 17
                                                        file =
                                                            "D:\probe\game-config.spec.ts"
                                                        line = 321
                                                    }
                                                    message =
                                                        "exact-probe-secret Acceptance evidence rule 'opaque-base64url-32-byte' failed."
                                                })
                                            retry = 0
                                            status = "failed"
                                        })
                                })
                        })
                })
        }
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        $playwrightFailureDiagnostic =
            Get-AdminPlaywrightFailureDiagnostic `
                -ResultPath $playwrightFailureProbePath `
                -Mode "LegacyGameConfig"
        Assert-Condition `
            ($playwrightFailureDiagnostic -ceq
                "game-config-lifecycle-not-passed@game-config.spec.ts:321:17#opaque-base64url-32-byte") `
            "Admin Playwright failure diagnostics did not return the validated source location."
        Assert-Condition `
            (-not $playwrightFailureDiagnostic.Contains(
                "exact-probe-secret",
                [StringComparison]::Ordinal)) `
            "Admin Playwright failure diagnostics exposed an error message."
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0].errors[0].message =
            "exact-probe-secret Acceptance evidence failed rule 'dynamic-secret-pipe-timeout' for POST /api/auth/login (200)."
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "game-config-lifecycle-not-passed@game-config.spec.ts:321:17#dynamic-secret-pipe-timeout") `
            "Admin Playwright failure diagnostics did not return an allowlisted aggregate evidence rule."
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0].errors[0].message =
            "Acceptance evidence rule 'role-permission-api-wait-restored-access-codes-not-observed' failed."
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "game-config-lifecycle-not-passed@game-config.spec.ts:321:17#role-permission-api-wait-restored-access-codes-not-observed") `
            "Admin Playwright failure diagnostics did not return a source-safe API wait result."
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0].errors[0].message =
            "Acceptance evidence rule 'role-permission-api-wait-restored-access-codes-request-started-no-response' failed."
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "game-config-lifecycle-not-passed@game-config.spec.ts:321:17#role-permission-api-wait-restored-access-codes-request-started-no-response") `
            "Admin Playwright failure diagnostics did not return a source-safe started-request result."
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0].errors[0].message =
            "Acceptance evidence rule 'role-permission-api-wait-untrusted-not-observed' failed."
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "game-config-lifecycle-not-passed@game-config.spec.ts:321:17") `
            "Admin Playwright failure diagnostics accepted an unregistered API wait result."
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0].errors[0].message =
            "exact-probe-secret Acceptance evidence rule 'opaque-base64url-32-byte' failed."
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0].errors[0].location =
            $null
        $playwrightFailureProbe.suites[0].specs[0].tests[0].results[0]["error"] =
            [ordered]@{
                stack =
                    "exact-probe-secret at D:\probe\game-config.spec.ts:654:9"
            }
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "game-config-lifecycle-not-passed@game-config.spec.ts:654:9#opaque-base64url-32-byte") `
            "Admin Playwright failure diagnostics did not validate a stack source location."
        $playwrightFailureProbe.suites[0].specs[0].title =
            "exact-probe-secret"
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "result-manifest-game-config-lifecycle-not-passed-missing") `
            "Admin Playwright failure diagnostics trusted an unknown test title."
        $playwrightFailureProbe.suites[0].specs = @(
            $playwrightFailureProbe.suites[0].specs[0],
            $playwrightFailureProbe.suites[0].specs[0])
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "result-manifest-record-count-nonzero") `
            "Admin Playwright failure diagnostics did not classify a nonzero invalid result count."
        $playwrightFailureProbe.suites[0].specs = @()
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "result-manifest-zero-records-no-top-level-errors") `
            "Admin Playwright failure diagnostics did not classify an empty error-free result manifest."
        $topLevelErrorSentinel = "top-level-error-sentinel"
        $playwrightFailureProbe.errors = @([ordered]@{
                message = $topLevelErrorSentinel
            })
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        $topLevelErrorDiagnostic =
            Get-AdminPlaywrightFailureDiagnostic `
                -ResultPath $playwrightFailureProbePath `
                -Mode "LegacyGameConfig"
        Assert-Condition `
            ($topLevelErrorDiagnostic -ceq
                "result-manifest-zero-records-top-level-errors") `
            "Admin Playwright failure diagnostics did not classify top-level result errors."
        Assert-Condition `
            (-not $topLevelErrorDiagnostic.Contains(
                    $topLevelErrorSentinel,
                    [StringComparison]::Ordinal)) `
            "Admin Playwright failure diagnostics exposed a top-level error value."
        $playwrightFailureProbe.errors = @()
        $playwrightFailureProbe.stats.expected = "invalid"
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "result-manifest-zero-records-stats-invalid") `
            "Admin Playwright failure diagnostics did not reject invalid zero-record statistics."
        $playwrightFailureProbe.stats.expected = 0
        $null = $playwrightFailureProbe.Remove("errors")
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "result-manifest-zero-records-errors-invalid") `
            "Admin Playwright failure diagnostics did not reject a missing error collection."
        $playwrightFailureProbe["errors"] = @()
        $null = $playwrightFailureProbe.Remove("stats")
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "LegacyGameConfig") -ceq
                "result-manifest-zero-records-stats-invalid") `
            "Admin Playwright failure diagnostics did not reject missing zero-record statistics."
        $playwrightFailureProbe = [ordered]@{
            suites = @(
                [ordered]@{
                    file = "D:\probe\admin-access.spec.ts"
                    specs = @(
                        [ordered]@{
                            title = "administrator lifecycle and concurrency"
                            tests = @([ordered]@{})
                        },
                        [ordered]@{
                            title = "roles, permissions, and authorization"
                            tests = @([ordered]@{})
                        },
                        [ordered]@{
                            title = "sessions and audit"
                            tests = @([ordered]@{})
                        },
                        [ordered]@{
                            title = "sessions and audit"
                            tests = @([ordered]@{})
                        })
                },
                [ordered]@{
                    file = "D:\probe\game-config.spec.ts"
                    specs = @(
                        [ordered]@{
                            title =
                                "operator completes the GameConfig lifecycle against the real API"
                            tests = @([ordered]@{})
                        })
                })
        }
        [System.IO.File]::WriteAllText(
            $playwrightFailureProbePath,
            ($playwrightFailureProbe | ConvertTo-Json -Depth 12),
            $utf8)
        Assert-Condition `
            ((Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $playwrightFailureProbePath `
                    -Mode "M35Management") -ceq
                "result-manifest-sessions-audit-not-passed-duplicate") `
            "Admin Playwright failure diagnostics did not classify a duplicated expected record."
        Remove-Item -LiteralPath $playwrightFailureProbePath -Force

        $responseAuditRoot = Join-Path `
            $probeRunRoot `
            "output\playwright\evidence"
        New-Item `
            -ItemType Directory `
            -Path $responseAuditRoot `
            -Force | Out-Null
        $responseAuditProbe = [ordered]@{
            schemaVersion = 1
            sourceFile = "game-config.spec.ts"
            testTitle =
                "operator completes the GameConfig lifecycle against the real API"
            testStatus = "passed"
            responseBodies = @(
                0..8 | ForEach-Object {
                    [ordered]@{
                        sequence = $_
                        method = "GET"
                        route = "/api/probe"
                        status = 200
                        bodyBytes = 2
                        scanned = $true
                    }
                })
            responseBodyCount = 9
            contextLedger = [ordered]@{
                closedBeforeListenerDetach = 1
                observed = 1
            }
            directRequestLedger = [ordered]@{
                directApiRequestsCompleted = 8
                directApiRequestsFailed = 0
                directApiRequestsObserved = 8
            }
            expectedNetworkFailures = @()
            requestLedger = [ordered]@{
                browserRequestFailuresExpected = 0
                browserRequestsCompleted = 1
                browserRequestsFailed = 0
                browserRequestsObserved = 1
                browserResponsesObserved = 1
            }
            screenshotSurfaceScanCount = 2
            violations = @()
        }
        $responseAuditPath = Join-Path `
            $responseAuditRoot `
            "game-config-response-audit.json"
        $writeResponseAuditProbe = {
            [System.IO.File]::WriteAllText(
                $responseAuditPath,
                ($responseAuditProbe | ConvertTo-Json -Depth 8),
                $utf8)
        }
        & $writeResponseAuditProbe
        Assert-AdminManagementResponseAuditReports `
            -RunRoot $probeRunRoot `
            -Mode "LegacyGameConfig" |
            Out-Null

        $validResponseAuditJson =
            $responseAuditProbe | ConvertTo-Json -Depth 8
        $duplicatePropertyJson = $validResponseAuditJson.Replace(
            '  "schemaVersion": 1,',
            "  `"schemaVersion`": 1,`n  `"schemaVersion`": 1,")
        Assert-Condition `
            ($duplicatePropertyJson -cne $validResponseAuditJson) `
            "Admin response-audit duplicate-property probe was not constructed."
        [System.IO.File]::WriteAllText(
            $responseAuditPath,
            $duplicatePropertyJson,
            $utf8)
        $duplicatePropertyRejected = $false
        try {
            Assert-AdminManagementResponseAuditReports `
                -RunRoot $probeRunRoot `
                -Mode "LegacyGameConfig" |
                Out-Null
        }
        catch {
            $duplicatePropertyRejected = $_.Exception.Message.Contains(
                "duplicate JSON property",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $duplicatePropertyRejected `
            "Admin management evidence must reject duplicate raw JSON properties."

        $caseShiftedPropertyJson = $validResponseAuditJson.Replace(
            '  "schemaVersion": 1,',
            '  "SchemaVersion": 1,')
        Assert-Condition `
            ($caseShiftedPropertyJson -cne $validResponseAuditJson) `
            "Admin response-audit case-shift probe was not constructed."
        [System.IO.File]::WriteAllText(
            $responseAuditPath,
            $caseShiftedPropertyJson,
            $utf8)
        $caseShiftedPropertyRejected = $false
        try {
            Assert-AdminManagementResponseAuditReports `
                -RunRoot $probeRunRoot `
                -Mode "LegacyGameConfig" |
                Out-Null
        }
        catch {
            $caseShiftedPropertyRejected =
                $_.Exception.Message.Contains(
                    "case-sensitive schema",
                    [StringComparison]::Ordinal) -or
                $_.Exception.Message.Contains(
                    "not strict JSON",
                    [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $caseShiftedPropertyRejected `
            "Admin management evidence must reject case-shifted JSON properties."

        foreach ($arrayShapeProbe in @(
                [pscustomobject]@{
                    Name = "expectedNetworkFailures"
                    ObjectValue = [ordered]@{
                        count = 1
                        method = "GET"
                        pathAndQuery = "/api/probe"
                    }
                    RestoreValue = @()
                },
                [pscustomobject]@{
                    Name = "responseBodies"
                    ObjectValue = $responseAuditProbe.responseBodies[0]
                    RestoreValue = $responseAuditProbe.responseBodies
                },
                [pscustomobject]@{
                    Name = "violations"
                    ObjectValue = [ordered]@{
                        method = "GET"
                        route = "/api/probe"
                        ruleId = "probe"
                        status = 0
                    }
                    RestoreValue = @()
                })) {
            $responseAuditProbe[$arrayShapeProbe.Name] =
                $arrayShapeProbe.ObjectValue
            & $writeResponseAuditProbe
            $objectInsteadOfArrayRejected = $false
            try {
                Assert-AdminManagementResponseAuditReports `
                    -RunRoot $probeRunRoot `
                    -Mode "LegacyGameConfig" |
                    Out-Null
            }
            catch {
                $objectInsteadOfArrayRejected = $_.Exception.Message.Contains(
                    "must be a JSON array",
                    [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $objectInsteadOfArrayRejected `
                "Admin management evidence must reject an object in place of '$($arrayShapeProbe.Name)'."
            $responseAuditProbe[$arrayShapeProbe.Name] =
                $arrayShapeProbe.RestoreValue
        }
        & $writeResponseAuditProbe

        $responseAuditProbe["unexpectedField"] = "must-fail"
        & $writeResponseAuditProbe
        $extraReportFieldRejected = $false
        try {
            Assert-AdminManagementResponseAuditReports `
                -RunRoot $probeRunRoot `
                -Mode "LegacyGameConfig" |
                Out-Null
        }
        catch {
            $extraReportFieldRejected = $_.Exception.Message.Contains(
                "case-sensitive schema",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $extraReportFieldRejected `
            "Admin management evidence must reject extra response-report fields."
        $null = $responseAuditProbe.Remove("unexpectedField")

        $responseAuditProbe.responseBodies[0].scanned = "false"
        & $writeResponseAuditProbe
        $stringBooleanRejected = $false
        try {
            Assert-AdminManagementResponseAuditReports `
                -RunRoot $probeRunRoot `
                -Mode "LegacyGameConfig" |
                Out-Null
        }
        catch {
            $stringBooleanRejected = $_.Exception.Message.Contains(
                "invalid or unscanned",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $stringBooleanRejected `
            "Admin management evidence must reject a string-valued scan flag."
        $responseAuditProbe.responseBodies[0].scanned = $true

        $responseAuditProbe.responseBodies[0].route = "/api/probe?secret=no"
        & $writeResponseAuditProbe
        $queryRouteRejected = $false
        try {
            Assert-AdminManagementResponseAuditReports `
                -RunRoot $probeRunRoot `
                -Mode "LegacyGameConfig" |
                Out-Null
        }
        catch {
            $queryRouteRejected = $_.Exception.Message.Contains(
                "invalid or unscanned",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $queryRouteRejected `
            "Admin management evidence must reject response routes with a query."
        $responseAuditProbe.responseBodies[0].route = "/api/probe"

        $responseAuditProbe.expectedNetworkFailures = @(
            [ordered]@{
                count = 1
                method = "GET"
                pathAndQuery = "/api/probe"
            })
        $responseAuditProbe.requestLedger.browserRequestFailuresExpected = 1
        $responseAuditProbe.requestLedger.browserRequestsCompleted = 0
        $responseAuditProbe.requestLedger.browserRequestsFailed = 1
        $responseAuditProbe.requestLedger.browserResponsesObserved = 0
        $responseAuditProbe.responseBodies = @(
            $responseAuditProbe.responseBodies | Select-Object -Skip 1)
        $responseAuditProbe.responseBodyCount = 8
        & $writeResponseAuditProbe
        $unexpectedNetworkFailureRejected = $false
        try {
            Assert-AdminManagementResponseAuditReports `
                -RunRoot $probeRunRoot `
                -Mode "LegacyGameConfig" |
                Out-Null
        }
        catch {
            $unexpectedNetworkFailureRejected = $_.Exception.Message.Contains(
                "unexpected network-failure count",
                [StringComparison]::Ordinal)
        }
        Assert-Condition `
            $unexpectedNetworkFailureRejected `
            "Admin management evidence must reject a network failure in the GameConfig report."

        $responsiveNetworkFailureContract = [pscustomobject]@{
            ExpectedNetworkFailureCount = 1
            ExpectedNetworkFailureMethod = "GET"
            ExpectedNetworkFailurePathAndQuery =
                "/api/admin-access/administrators?Page=1&PageSize=20"
        }
        $correctNetworkFailureRecord = [pscustomobject]@{
            count = 1
            method = "GET"
            pathAndQuery =
                "/api/admin-access/administrators?Page=1&PageSize=20"
        }
        Assert-AdminManagementExpectedNetworkFailures `
            -Records @($correctNetworkFailureRecord) `
            -ExpectedReport $responsiveNetworkFailureContract `
            -Description "Admin network-failure policy probe"
        foreach ($invalidNetworkFailureCase in @(
                [pscustomobject]@{
                    Name = "wrong method"
                    Records = @([pscustomobject]@{
                            count = 1
                            method = "POST"
                            pathAndQuery =
                                "/api/admin-access/administrators?Page=1&PageSize=20"
                        })
                },
                [pscustomobject]@{
                    Name = "wrong path"
                    Records = @([pscustomobject]@{
                            count = 1
                            method = "GET"
                            pathAndQuery =
                                "/api/admin-access/roles?page=1&pageSize=20"
                        })
                },
                [pscustomobject]@{
                    Name = "wrong query"
                    Records = @([pscustomobject]@{
                            count = 1
                            method = "GET"
                            pathAndQuery =
                                "/api/admin-access/administrators?Page=2&PageSize=20"
                        })
                },
                [pscustomobject]@{
                    Name = "duplicate"
                    Records = @(
                        $correctNetworkFailureRecord,
                        $correctNetworkFailureRecord)
                })) {
            $invalidNetworkFailureRejected = $false
            try {
                Assert-AdminManagementExpectedNetworkFailures `
                    -Records @($invalidNetworkFailureCase.Records) `
                    -ExpectedReport $responsiveNetworkFailureContract `
                    -Description "Admin network-failure policy probe"
            }
            catch {
                $invalidNetworkFailureRejected =
                    $_.Exception.Message.Contains(
                        "network-failure",
                        [StringComparison]::Ordinal) -or
                    $_.Exception.Message.Contains(
                        "expected network failure",
                        [StringComparison]::Ordinal)
            }
            Assert-Condition `
                $invalidNetworkFailureRejected `
                "Admin network-failure policy accepted the $($invalidNetworkFailureCase.Name) probe."
        }

        $responseAuditProbe.responseBodies = @(
            0..8 | ForEach-Object {
                [ordered]@{
                    sequence = $_
                    method = "GET"
                    route = "/api/probe"
                    status = 200
                    bodyBytes = 2
                    scanned = $true
                }
            })
        $responseAuditProbe.responseBodyCount = 9
        $responseAuditProbe.expectedNetworkFailures = @()
        $responseAuditProbe.requestLedger.browserRequestFailuresExpected = 0
        $responseAuditProbe.requestLedger.browserRequestsCompleted = 1
        $responseAuditProbe.requestLedger.browserRequestsFailed = 0
        $responseAuditProbe.requestLedger.browserResponsesObserved = 1
        & $writeResponseAuditProbe
        Assert-AdminManagementResponseAuditReports `
            -RunRoot $probeRunRoot `
            -Mode "LegacyGameConfig" |
            Out-Null

        $legacyRoot = Join-Path $probeRunRoot "legacy-output"
        New-Item -ItemType Directory -Path $legacyRoot -Force | Out-Null
        $legacyPath = Join-Path $legacyRoot "sentinel.log"
        [System.IO.File]::WriteAllText($legacyPath, "alpha", $utf8)
        $baselineSnapshot = Get-AdminManagementArtifactSnapshot `
            -ArtifactRoot $legacyRoot
        $unchangedSnapshot = Get-AdminManagementArtifactSnapshot `
            -ArtifactRoot $legacyRoot
        Assert-Condition `
            (@(Compare-AdminManagementArtifactSnapshots `
                -Before $baselineSnapshot `
                -After $unchangedSnapshot).Count -eq 0) `
            "Unchanged Admin management artifact snapshots must compare equal."

        $originalWriteTime = (Get-Item -LiteralPath $legacyPath).LastWriteTimeUtc
        [System.IO.File]::WriteAllText($legacyPath, "bravo", $utf8)
        (Get-Item -LiteralPath $legacyPath).LastWriteTimeUtc =
            $originalWriteTime
        $modifiedChanges = @(
            Compare-AdminManagementArtifactSnapshots `
                -Before $baselineSnapshot `
                -After (Get-AdminManagementArtifactSnapshot `
                    -ArtifactRoot $legacyRoot))
        Assert-Condition `
            ($modifiedChanges.Count -eq 1 -and
                $modifiedChanges[0] -eq "Modified 'sentinel.log'") `
            "Admin management artifact snapshots must detect same-length rewrites."
        [System.IO.File]::WriteAllText($legacyPath, "alpha", $utf8)

        Remove-Item -LiteralPath $legacyPath -Force
        $removedChanges = @(
            Compare-AdminManagementArtifactSnapshots `
                -Before $baselineSnapshot `
                -After (Get-AdminManagementArtifactSnapshot `
                    -ArtifactRoot $legacyRoot))
        Assert-Condition `
            ($removedChanges.Count -eq 1 -and
                $removedChanges[0] -eq "Removed 'sentinel.log'") `
            "Admin management artifact snapshots must detect deletions."
        [System.IO.File]::WriteAllText($legacyPath, "alpha", $utf8)

        $renamedPath = Join-Path $legacyRoot "renamed.log"
        Move-Item -LiteralPath $legacyPath -Destination $renamedPath
        $renamedChanges = @(
            Compare-AdminManagementArtifactSnapshots `
                -Before $baselineSnapshot `
                -After (Get-AdminManagementArtifactSnapshot `
                    -ArtifactRoot $legacyRoot))
        Assert-Condition `
            ($renamedChanges.Count -eq 2 -and
                $renamedChanges -contains "Added 'renamed.log'" -and
                $renamedChanges -contains "Removed 'sentinel.log'") `
            "Admin management artifact snapshots must detect renames."
        Move-Item -LiteralPath $renamedPath -Destination $legacyPath

        $addedPath = Join-Path $legacyRoot "added.log"
        [System.IO.File]::WriteAllText($addedPath, "added", $utf8)
        $addedChanges = @(
            Compare-AdminManagementArtifactSnapshots `
                -Before $baselineSnapshot `
                -After (Get-AdminManagementArtifactSnapshot `
                    -ArtifactRoot $legacyRoot))
        Assert-Condition `
            ($addedChanges.Count -eq 1 -and
                $addedChanges[0] -eq "Added 'added.log'") `
            "Admin management artifact snapshots must detect additions."

        Test-AdminManagementRunRootRemovalPolicy
    }
    finally {
        if (Test-Path -LiteralPath $probeRunRoot) {
            Remove-AdminManagementRunRootVerified -RunRoot $probeRunRoot
        }
    }
}

function Assert-AdminManagementCleanup {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][int[]]$Ports,
        [object[]]$OwnedProcessIdentities = @(),
        [string[]]$TemporarySecretPaths = @()
    )

    if ($ProjectName -notmatch '^novagame-test-[a-f0-9]{12}$') {
        throw "Admin management cleanup requires a valid disposable project name."
    }
    foreach ($dockerQuery in @(
        @("ps", "--all", "--quiet", "--filter", "label=com.docker.compose.project=$ProjectName"),
        @("network", "ls", "--quiet", "--filter", "label=com.docker.compose.project=$ProjectName"),
        @("volume", "ls", "--quiet", "--filter", "label=com.docker.compose.project=$ProjectName")
    )) {
        $dockerResources = @(
            & docker @dockerQuery |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to audit Docker cleanup for '$ProjectName'."
        }
        Assert-Condition `
            ($dockerResources.Count -eq 0) `
            "Docker resources remain for '$ProjectName': $($dockerResources -join ', ')."
    }

    foreach ($port in @($Ports | Where-Object { $_ -gt 0 } | Sort-Object -Unique)) {
        $listeners = @(
            Get-NetTCPConnection `
                -State Listen `
                -LocalPort $port `
                -ErrorAction SilentlyContinue)
        Assert-Condition `
            ($listeners.Count -eq 0) `
            "Admin management port '$port' remains in use after cleanup."
    }

    $remainingOwnedProcessIds = @()
    $processExitDeadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $remainingOwnedProcessIds = @(
            foreach ($ownedProcessIdentity in $OwnedProcessIdentities) {
                $processRow = Get-CimInstance `
                    Win32_Process `
                    -Filter "ProcessId = $([int]$ownedProcessIdentity.Id)" `
                    -ErrorAction SilentlyContinue
                if ($null -ne $processRow -and
                    ([DateTime]$processRow.CreationDate).ToUniversalTime() -eq
                        [DateTime]$ownedProcessIdentity.CreationDate) {
                    [int]$ownedProcessIdentity.Id
                }
            })
        if ($remainingOwnedProcessIds.Count -gt 0 -and
            [DateTime]::UtcNow -lt $processExitDeadline) {
            Start-Sleep -Milliseconds 100
        }
    } while ($remainingOwnedProcessIds.Count -gt 0 -and
        [DateTime]::UtcNow -lt $processExitDeadline)
    Assert-Condition `
        ($remainingOwnedProcessIds.Count -eq 0) `
        "Admin management API/Vite/Playwright/browser processes remain after cleanup: $($remainingOwnedProcessIds -join ', ')."
    foreach ($temporarySecretPath in $TemporarySecretPaths) {
        Assert-Condition `
            (-not (Test-Path -LiteralPath $temporarySecretPath)) `
            "Admin management temporary secret file remains after cleanup: '$temporarySecretPath'."
    }
}

function Assert-AdminPlaywrightResult {
    param(
        [Parameter(Mandatory)][string]$ResultPath,
        [Parameter(Mandatory)]
        [ValidateSet("LegacyGameConfig", "M35Management")]
        [string]$Mode
    )

    Assert-Condition `
        (Test-Path -LiteralPath $ResultPath -PathType Leaf) `
        "Admin Playwright did not produce '$ResultPath'."
    $report = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
    foreach ($requiredProperty in @("errors", "stats", "suites")) {
        Assert-Condition `
            ($null -ne $report.PSObject.Properties[$requiredProperty]) `
            "Admin Playwright result '$ResultPath' is missing '$requiredProperty'."
    }
    Assert-Condition `
        (@($report.errors).Count -eq 0) `
        "Admin Playwright reported top-level errors."

    $testRecords = [System.Collections.Generic.List[object]]::new()
    function Add-AdminPlaywrightTests {
        param(
            [Parameter(Mandatory)]$Suite,
            [string]$InheritedFile
        )

        $suiteFile = $InheritedFile
        if ($null -ne $Suite.PSObject.Properties["file"] -and
            -not [string]::IsNullOrWhiteSpace([string]$Suite.file)) {
            $suiteFile = [string]$Suite.file
        }
        if ($null -ne $Suite.PSObject.Properties["specs"] -and
            $null -ne $Suite.specs) {
            foreach ($spec in @($Suite.specs)) {
                $specFile = $suiteFile
                if ($null -ne $spec.PSObject.Properties["file"] -and
                    -not [string]::IsNullOrWhiteSpace([string]$spec.file)) {
                    $specFile = [string]$spec.file
                }
                Assert-Condition `
                    (-not [string]::IsNullOrWhiteSpace($specFile)) `
                    "Admin Playwright specification '$($spec.title)' is missing its source file."
                Assert-Condition `
                    ($null -ne $spec.PSObject.Properties["tests"] -and
                        $null -ne $spec.tests) `
                    "Admin Playwright specification '$($spec.title)' is missing tests."
                foreach ($test in @($spec.tests)) {
                    $null = $testRecords.Add([pscustomobject]@{
                            File = [System.IO.Path]::GetFileName($specFile)
                            Title = [string]$spec.title
                            Test = $test
                        })
                }
            }
        }
        if ($null -ne $Suite.PSObject.Properties["suites"] -and
            $null -ne $Suite.suites) {
            foreach ($childSuite in @($Suite.suites)) {
                Add-AdminPlaywrightTests `
                    -Suite $childSuite `
                    -InheritedFile $suiteFile
            }
        }
    }
    foreach ($suite in @($report.suites)) {
        Add-AdminPlaywrightTests -Suite $suite
    }

    $expectedTitlesByFile = if ($Mode -eq "M35Management") {
        [ordered]@{
            "admin-access.spec.ts" = @(
                "administrator lifecycle and concurrency",
                "roles, permissions, and authorization",
                "sessions and audit",
                "responsive and failure states"
            )
            "game-config.spec.ts" = @(
                "operator completes the GameConfig lifecycle against the real API")
        }
    }
    else {
        [ordered]@{
            "game-config.spec.ts" = @(
                "operator completes the GameConfig lifecycle against the real API")
        }
    }
    $expectedTestCount = @(
        $expectedTitlesByFile.Values | ForEach-Object { @($_) }).Count
    Assert-Condition `
        ($testRecords.Count -eq $expectedTestCount) `
        "Admin Playwright '$Mode' must define exactly $expectedTestCount tests; found $($testRecords.Count)."
    Assert-StringSet `
        -Expected @($expectedTitlesByFile.Keys) `
        -Actual @($testRecords | ForEach-Object { $_.File } | Sort-Object -Unique) `
        -Message "Admin Playwright '$Mode' source files changed unexpectedly."
    foreach ($expectedFile in $expectedTitlesByFile.Keys) {
        Assert-StringSet `
            -Expected @($expectedTitlesByFile[$expectedFile]) `
            -Actual @(
                $testRecords |
                    Where-Object { $_.File -eq $expectedFile } |
                    ForEach-Object { $_.Title }) `
            -Message "Admin Playwright '$expectedFile' test titles changed unexpectedly."
    }
    foreach ($testRecord in $testRecords) {
        if ($null -ne $testRecord.Test.PSObject.Properties["expectedStatus"]) {
            Assert-Condition `
                ([string]$testRecord.Test.expectedStatus -eq "passed") `
                "Admin Playwright test '$($testRecord.Title)' must expect a passing outcome."
        }
        if ($null -ne $testRecord.Test.PSObject.Properties["status"]) {
            Assert-Condition `
                ([string]$testRecord.Test.status -eq "expected") `
                "Admin Playwright test '$($testRecord.Title)' must have expected aggregate status."
        }
        $results = @($testRecord.Test.results)
        Assert-Condition `
            ($results.Count -eq 1) `
            "Admin Playwright test '$($testRecord.Title)' must have exactly one result."
        Assert-Condition `
            ([string]$results[0].status -eq "passed" -and
                [int]$results[0].retry -eq 0) `
            "Admin Playwright test '$($testRecord.Title)' must pass without retry."
    }
    foreach ($requiredCounter in @("expected", "flaky", "skipped", "unexpected")) {
        Assert-Condition `
            ($null -ne $report.stats.PSObject.Properties[$requiredCounter]) `
            "Admin Playwright result '$ResultPath' is missing stats.$requiredCounter."
    }
    Assert-Condition `
        ([int]$report.stats.expected -eq $expectedTestCount -and
            [int]$report.stats.flaky -eq 0 -and
            [int]$report.stats.skipped -eq 0 -and
            [int]$report.stats.unexpected -eq 0) `
        "Admin Playwright '$Mode' must report exactly $expectedTestCount expected tests with zero flaky, skipped, or unexpected outcomes."
}

function Get-AdminPlaywrightFailureDiagnostic {
    param(
        [Parameter(Mandatory)][string]$ResultPath,
        [Parameter(Mandatory)]
        [ValidateSet("LegacyGameConfig", "M35Management")]
        [string]$Mode
    )

    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        return "result-unavailable"
    }
    try {
        $report = Get-Content -LiteralPath $ResultPath -Raw |
            ConvertFrom-Json
        $testRecords = [System.Collections.Generic.List[object]]::new()
        function Add-AdminPlaywrightDiagnosticTests {
            param(
                [Parameter(Mandatory)]$Suite,
                [string]$InheritedFile
            )

            $suiteFile = $InheritedFile
            if ($null -ne $Suite.PSObject.Properties["file"] -and
                -not [string]::IsNullOrWhiteSpace([string]$Suite.file)) {
                $suiteFile = [string]$Suite.file
            }
            if ($null -ne $Suite.PSObject.Properties["specs"] -and
                $null -ne $Suite.specs) {
                foreach ($spec in @($Suite.specs)) {
                    $specFile = $suiteFile
                    if ($null -ne $spec.PSObject.Properties["file"] -and
                        -not [string]::IsNullOrWhiteSpace([string]$spec.file)) {
                        $specFile = [string]$spec.file
                    }
                    foreach ($test in @($spec.tests)) {
                        $null = $testRecords.Add([pscustomobject]@{
                                File = [System.IO.Path]::GetFileName($specFile)
                                Title = [string]$spec.title
                                Test = $test
                            })
                    }
                }
            }
            if ($null -ne $Suite.PSObject.Properties["suites"] -and
                $null -ne $Suite.suites) {
                foreach ($childSuite in @($Suite.suites)) {
                    Add-AdminPlaywrightDiagnosticTests `
                        -Suite $childSuite `
                        -InheritedFile $suiteFile
                }
            }
        }
        foreach ($suite in @($report.suites)) {
            Add-AdminPlaywrightDiagnosticTests -Suite $suite
        }

        $expectedTests = if ($Mode -eq "M35Management") {
            @(
                [pscustomobject]@{
                    File = "admin-access.spec.ts"
                    Title = "administrator lifecycle and concurrency"
                    Rule = "administrator-lifecycle-not-passed"
                }
                [pscustomobject]@{
                    File = "admin-access.spec.ts"
                    Title = "roles, permissions, and authorization"
                    Rule = "roles-permissions-not-passed"
                }
                [pscustomobject]@{
                    File = "admin-access.spec.ts"
                    Title = "sessions and audit"
                    Rule = "sessions-audit-not-passed"
                }
                [pscustomobject]@{
                    File = "admin-access.spec.ts"
                    Title = "responsive and failure states"
                    Rule = "responsive-failure-states-not-passed"
                }
                [pscustomobject]@{
                    File = "game-config.spec.ts"
                    Title =
                        "operator completes the GameConfig lifecycle against the real API"
                    Rule = "game-config-lifecycle-not-passed"
                }
            )
        }
        else {
            @(
                [pscustomobject]@{
                    File = "game-config.spec.ts"
                    Title =
                        "operator completes the GameConfig lifecycle against the real API"
                    Rule = "game-config-lifecycle-not-passed"
                }
            )
        }
        if ($testRecords.Count -ne $expectedTests.Count) {
            if ($testRecords.Count -eq 0) {
                if ($null -eq $report.PSObject.Properties["errors"]) {
                    return "result-manifest-zero-records-errors-invalid"
                }
                if (@($report.errors).Count -gt 0) {
                    return "result-manifest-zero-records-top-level-errors"
                }
                if ($null -eq $report.PSObject.Properties["stats"] -or
                    $null -eq $report.stats) {
                    return "result-manifest-zero-records-stats-invalid"
                }
                foreach ($statName in @(
                        "expected",
                        "flaky",
                        "skipped",
                        "unexpected")) {
                    $statValue = $report.stats.PSObject.Properties[$statName]
                    if ($null -eq $statValue -or
                        -not (Test-AdminManagementJsonInteger $statValue.Value) -or
                        [int64]$statValue.Value -lt 0) {
                        return "result-manifest-zero-records-stats-invalid"
                    }
                }
                return "result-manifest-zero-records-no-top-level-errors"
            }
            return "result-manifest-record-count-nonzero"
        }
        foreach ($expectedTest in $expectedTests) {
            $matchingRecords = @(
                $testRecords |
                    Where-Object {
                        $_.File -ceq $expectedTest.File -and
                        $_.Title -ceq $expectedTest.Title
                    })
            if ($matchingRecords.Count -ne 1) {
                if ($matchingRecords.Count -eq 0) {
                    return "result-manifest-$($expectedTest.Rule)-missing"
                }
                return "result-manifest-$($expectedTest.Rule)-duplicate"
            }
        }

        foreach ($expectedTest in $expectedTests) {
            $testRecord = $testRecords |
                Where-Object {
                    $_.File -ceq $expectedTest.File -and
                    $_.Title -ceq $expectedTest.Title
                } |
                Select-Object -First 1
            $results = @($testRecord.Test.results)
            if ($results.Count -ne 1) {
                return "$($expectedTest.Rule)-result-invalid"
            }
            if ([string]$results[0].status -ceq "passed") {
                continue
            }
            $knownEvidenceRule = $null
            $safeEvidenceRuleIds = @(
                "aspnet-password-hash",
                "authorization-bearer",
                "dynamic-secret-pipe-acknowledgement",
                "dynamic-secret-pipe-closed",
                "dynamic-secret-pipe-configuration",
                "dynamic-secret-pipe-connection",
                "dynamic-secret-pipe-payload",
                "dynamic-secret-pipe-timeout",
                "dynamic-secret-pipe-write",
                "exact-sensitive-value",
                "jwt",
                "mysql-uri",
                "opaque-base64url-32-byte",
                "password-connection-string",
                "sensitive-json-field"
            )
            $rolePermissionApiWaitDiagnosticIds = @(
                "access-only-game-config-drafts",
                "access-only-game-config-environment",
                "access-only-game-config-versions",
                "browser-read-denial",
                "cold-start-access-codes",
                "cold-start-user-info",
                "leaf-only-access-codes",
                "leaf-only-login",
                "leaf-only-user-info",
                "refresh-session",
                "refreshed-game-config-drafts",
                "refreshed-game-config-environment",
                "refreshed-game-config-versions",
                "restored-access-codes",
                "restored-game-config-drafts",
                "restored-game-config-environment",
                "restored-game-config-versions",
                "restored-user-info"
            )
            foreach ($rolePermissionApiWaitDiagnosticId in
                $rolePermissionApiWaitDiagnosticIds) {
                foreach ($rolePermissionApiWaitOutcome in @(
                        "not-observed",
                        "request-started-no-response",
                        "request-failed",
                        "unexpected-response")) {
                    $safeEvidenceRuleIds +=
                        "role-permission-api-wait-$rolePermissionApiWaitDiagnosticId-$rolePermissionApiWaitOutcome"
                }
            }
            $failureTexts = @(
                if ($null -ne $results[0].PSObject.Properties["error"] -and
                    $null -ne $results[0].error) {
                    if ($null -ne $results[0].error.PSObject.Properties["message"]) {
                        [string]$results[0].error.message
                    }
                    if ($null -ne $results[0].error.PSObject.Properties["stack"]) {
                        [string]$results[0].error.stack
                    }
                }
                foreach ($errorRecord in @($results[0].errors)) {
                    if ($null -ne $errorRecord.PSObject.Properties["message"]) {
                        [string]$errorRecord.message
                    }
                }
            )
            foreach ($safeEvidenceRuleId in $safeEvidenceRuleIds) {
                $ruleMessages = @(
                    "Acceptance evidence rule '$safeEvidenceRuleId' failed.",
                    "Acceptance evidence failed rule '$safeEvidenceRuleId' ")
                if (@($failureTexts | Where-Object {
                            $failureText = $_
                            @($ruleMessages | Where-Object {
                                    $failureText.Contains(
                                        $_,
                                        [StringComparison]::Ordinal)
                                }).Count -gt 0
                        }).Count -gt 0) {
                    $knownEvidenceRule = $safeEvidenceRuleId
                    break
                }
            }
            $evidenceRuleSuffix = if ($null -ne $knownEvidenceRule) {
                "#$knownEvidenceRule"
            }
            else {
                ""
            }
            $locationCandidates = [System.Collections.Generic.List[object]]::new()
            if ($null -ne $results[0].PSObject.Properties["errorLocation"] -and
                $null -ne $results[0].errorLocation) {
                $null = $locationCandidates.Add($results[0].errorLocation)
            }
            if ($null -ne $results[0].PSObject.Properties["error"] -and
                $null -ne $results[0].error -and
                $null -ne $results[0].error.PSObject.Properties["location"] -and
                $null -ne $results[0].error.location) {
                $null = $locationCandidates.Add($results[0].error.location)
            }
            foreach ($errorRecord in @($results[0].errors)) {
                if ($null -ne $errorRecord.PSObject.Properties["location"] -and
                    $null -ne $errorRecord.location) {
                    $null = $locationCandidates.Add($errorRecord.location)
                }
            }
            foreach ($location in $locationCandidates) {
                if ([System.IO.Path]::GetFileName([string]$location.file) -cne
                        [string]$expectedTest.File -or
                    -not (Test-AdminManagementJsonInteger $location.line) -or
                    -not (Test-AdminManagementJsonInteger $location.column) -or
                    [int64]$location.line -lt 1 -or
                    [int64]$location.line -gt 1000000 -or
                    [int64]$location.column -lt 1 -or
                    [int64]$location.column -gt 1000000) {
                    continue
                }
                return ('{0}@{1}:{2}:{3}{4}' -f
                    $expectedTest.Rule,
                    $expectedTest.File,
                    [int64]$location.line,
                    [int64]$location.column,
                    $evidenceRuleSuffix)
            }
            $sourceLocationPattern =
                '(?<![A-Za-z0-9_.-])' +
                [regex]::Escape([string]$expectedTest.File) +
                ':(?<line>[0-9]{1,7}):(?<column>[0-9]{1,7})(?![0-9])'
            $stackCandidates = @(
                if ($null -ne $results[0].PSObject.Properties["error"] -and
                    $null -ne $results[0].error -and
                    $null -ne $results[0].error.PSObject.Properties["stack"] -and
                    -not [string]::IsNullOrWhiteSpace(
                        [string]$results[0].error.stack)) {
                    [string]$results[0].error.stack
                }
            )
            foreach ($stackCandidate in $stackCandidates) {
                foreach ($sourceLocation in [regex]::Matches(
                        $stackCandidate,
                        $sourceLocationPattern)) {
                    $line = [int64]$sourceLocation.Groups["line"].Value
                    $column = [int64]$sourceLocation.Groups["column"].Value
                    if ($line -lt 1 -or $line -gt 1000000 -or
                        $column -lt 1 -or $column -gt 1000000) {
                        continue
                    }
                    return ('{0}@{1}:{2}:{3}{4}' -f
                        $expectedTest.Rule,
                        $expectedTest.File,
                        $line,
                        $column,
                        $evidenceRuleSuffix)
                }
            }
            return "$($expectedTest.Rule)$evidenceRuleSuffix"
        }
        return "nonzero-with-passing-result"
    }
    catch {
        return "result-unavailable"
    }
}

function Invoke-AdminEndToEnd {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)]
        [ValidateSet("LegacyGameConfig", "M35Management")]
        [string]$Mode,
        [switch]$RunSwordMasterAcceptance
    )

    if ($Mode -eq "M35Management") {
        Assert-AdminEndToEndHostCapacity
    }

    $managementSpecPath = "admin/apps/web-ele/e2e/admin-access.spec.ts"
    $gameConfigSpecPath = "admin/apps/web-ele/e2e/game-config.spec.ts"
    $playwrightSpecArguments = if ($Mode -eq "M35Management") {
        Assert-Condition `
            (Test-Path -LiteralPath $managementSpecPath -PathType Leaf) `
            "AdminE2E is unavailable until '$managementSpecPath' defines the four frozen management tests."
        $gameConfigSource = Get-Content -LiteralPath $gameConfigSpecPath -Raw
        foreach ($forbiddenSqlFixture in @(
            "DELETE FROM admin_role_permissions",
            "INSERT INTO admin_role_permissions"
        )) {
            Assert-Condition `
                (-not $gameConfigSource.Contains($forbiddenSqlFixture)) `
                "M35 management E2E requires GameConfig permission state to be created through management HTTP."
        }
        @(
            "apps/web-ele/e2e/admin-access.spec.ts",
            "apps/web-ele/e2e/game-config.spec.ts"
        )
    }
    else {
        @("apps/web-ele/e2e/game-config.spec.ts")
    }

    Invoke-CheckedCommand "docker" @("compose", "version")
    Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")
    $browserChannel = Resolve-E2EBrowserChannel

    $projectName = "novagame-test-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $adminManagementRoot = Join-Path $repositoryRoot ".local\admin-management"
    $runRoot = Join-Path $adminManagementRoot $projectName
    $testEnvironmentFile = Join-Path $runRoot "database.env"
    $legacyAdminPlaywrightRoot = Join-Path `
        ([string]$Toolchain.adminRoot) `
        "output\playwright"
    $preexistingAdminPlaywrightSnapshot =
        Get-AdminManagementArtifactSnapshot `
            -ArtifactRoot $legacyAdminPlaywrightRoot
    $operationError = $null
    $cleanupErrors = [System.Collections.Generic.List[string]]::new()
    $cleanupRequired = $false
    $apiProcess = $null
    $dbMigratorProcess = $null
    $playwrightProcess = $null
    $dynamicSecretChannel = $null
    $dynamicSecretRecords = @()
    $dynamicSecretSnapshotComplete = $false
    $dynamicSecretRegistryComplete = $false
    $processJobHandle = [IntPtr]::Zero
    $ownedProcessIdentities = @()
    $processJobIdentityCaptured = $false
    $screenshotSpecifications = @()
    $screenshotInspectionRecords = @()
    $successfulScreenshotsValidated = $false
    $logRoot = $null
    $previousAdminAccessEnvironment = $null
    $previousDatabaseEnvironment = $null
    $testEnvironment = $null
    $composeArguments = @()
    $publishedPort = 0
    $apiPort = 0
    $adminPort = 0
    $connectionString = $null
    $seedUserName = $null
    $seedPassword = $null
    $createPassword = $null
    $createRetryPassword = $null
    $resetPassword = $null
    $resetRetryPassword = $null
    $adminAccessEnvironment = [ordered]@{}
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
        "NovaGame__AdminAccess__LoginRateLimit__PermitLimit",
        "NovaGame__AdminAccess__LoginRateLimit__Window",
        "NovaGame__AdminAccess__LoginRateLimit__QueueLimit",
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
        "NOVAGAME_E2E_CREATE_PASSWORD",
        "NOVAGAME_E2E_CREATE_RETRY_PASSWORD",
        "NOVAGAME_E2E_RESET_PASSWORD",
        "NOVAGAME_E2E_RESET_RETRY_PASSWORD",
        "NOVAGAME_E2E_BROWSER_CHANNEL",
        "NOVAGAME_E2E_MYSQL_CONTAINER",
        "NOVAGAME_E2E_MYSQL_ROOT_PASSWORD",
        "NOVAGAME_E2E_MYSQL_DATABASE",
        "PLAYWRIGHT_HTML_OUTPUT_DIR",
        "PLAYWRIGHT_JSON_OUTPUT_FILE"
    )
    $previousRuntimeEnvironment = Get-NovaGameProcessEnvironmentSnapshot `
        -Names $runtimeEnvironmentNames

    try {
        $logRoot = $runRoot
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
        $processJobHandle = New-NovaGameProcessJob
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
        $createPassword = "Create-$projectName-Password-123!"
        $createRetryPassword = "Create-Retry-$projectName-Password-456!"
        $resetPassword = "Reset-$projectName-Password-789!"
        $resetRetryPassword = "Reset-Retry-$projectName-Password-012!"
        $adminAccessEnvironment = [ordered]@{
            NovaGame__AdminAccess__Jwt__SigningKey = New-NovaGameAdminAccessSigningKey
            NovaGame__AdminAccess__Seed__UserName = $seedUserName
            NovaGame__AdminAccess__Seed__Password = $seedPassword
            NovaGame__AdminAccess__Seed__DisplayName = "Verification Administrator"
        }
        $previousAdminAccessEnvironment = Set-NovaGameAdminAccessProcessEnvironment `
            -Values $adminAccessEnvironment
        $runtimeSettings = [ordered]@{
            # The real browser suite performs multiple valid logins while preserving
            # production's five-attempt default for its dedicated rate-limit tests.
            NovaGame__AdminAccess__LoginRateLimit__PermitLimit = "100"
            NovaGame__AdminAccess__LoginRateLimit__Window = "00:01:00"
            NovaGame__AdminAccess__LoginRateLimit__QueueLimit = "0"
        }
        foreach ($runtimeSetting in $runtimeSettings.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                [string]$runtimeSetting.Key,
                [string]$runtimeSetting.Value,
                "Process")
        }
        $env:DOTNET_ENVIRONMENT = "Testing"
        $env:ASPNETCORE_ENVIRONMENT = "Testing"

        $dbMigratorStandardOutputPath = Join-Path `
            $logRoot `
            "dbmigrator.stdout.log"
        $dbMigratorStandardErrorPath = Join-Path `
            $logRoot `
            "dbmigrator.stderr.log"
        $dbMigratorStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $dbMigratorStartInfo.WorkingDirectory = $repositoryRoot
        $dbMigratorStartInfo.UseShellExecute = $false
        $dbMigratorStartInfo.CreateNoWindow = $true
        $dbMigratorStartInfo.RedirectStandardOutput = $false
        $dbMigratorStartInfo.RedirectStandardError = $false
        $dbMigratorStartInfo.Environment["NOVAGAME_VERIFICATION_STDOUT_PATH"] =
            $dbMigratorStandardOutputPath
        $dbMigratorStartInfo.Environment["NOVAGAME_VERIFICATION_STDERR_PATH"] =
            $dbMigratorStandardErrorPath
        $dbMigratorStartInfo.FileName =
            (Get-Command "pwsh" -ErrorAction Stop).Source
        foreach ($dbMigratorArgument in @(
                "-NoProfile",
                "-Command",
                '$stdoutPath = $env:NOVAGAME_VERIFICATION_STDOUT_PATH; ' +
                '$stderrPath = $env:NOVAGAME_VERIFICATION_STDERR_PATH; ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDOUT_PATH", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDERR_PATH", $null, "Process"); ' +
                '& dotnet run --project server/src/NovaGame.DbMigrator/NovaGame.DbMigrator.csproj --no-build --no-restore ' +
                '1> $stdoutPath 2> $stderrPath; exit $LASTEXITCODE'
            )) {
            $null = $dbMigratorStartInfo.ArgumentList.Add(
                [string]$dbMigratorArgument)
        }
        $dbMigratorProcess = Start-NovaGameJobProcess `
            -JobHandle $processJobHandle `
            -TargetStartInfo $dbMigratorStartInfo
        $dbMigratorCompleted = $dbMigratorProcess.WaitForExit(300000)
        if (-not $dbMigratorCompleted) {
            $dbMigratorProcess.Kill($true)
        }
        $dbMigratorProcess.WaitForExit()
        $dbMigratorProcess.Refresh()
        $dbMigratorExitCode = $dbMigratorProcess.ExitCode
        if (-not $dbMigratorCompleted) {
            throw "NovaGame DbMigrator exceeded 300 seconds."
        }
        if ($dbMigratorExitCode -ne 0) {
            throw "NovaGame DbMigrator failed with exit code $dbMigratorExitCode."
        }

        $apiPort = Get-AvailableLoopbackPort
        do {
            $adminPort = Get-AvailableLoopbackPort
        } while ($adminPort -eq $apiPort)
        $apiUrl = "http://127.0.0.1:$apiPort"
        $adminUrl = "https://localhost:$adminPort"
        $env:ASPNETCORE_URLS = $apiUrl
        $env:NovaCore__Web__ForwardedHeaders__IsEnabled = "true"
        $env:NovaCore__Web__ForwardedHeaders__KnownProxies__0 = "127.0.0.1"

        $apiStandardOutputPath = Join-Path $logRoot "api.stdout.log"
        $apiStandardErrorPath = Join-Path $logRoot "api.stderr.log"
        $apiStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $apiStartInfo.WorkingDirectory = $repositoryRoot
        $apiStartInfo.UseShellExecute = $false
        $apiStartInfo.CreateNoWindow = $true
        $apiStartInfo.RedirectStandardOutput = $false
        $apiStartInfo.RedirectStandardError = $false
        foreach ($apiArgument in @(
                "run",
                "--project", "server/src/NovaGame.Api/NovaGame.Api.csproj",
                "--no-build",
                "--no-restore",
                "--no-launch-profile"
            )) {
            $null = $apiStartInfo.ArgumentList.Add($apiArgument)
        }
        $apiStartInfo.Environment["NOVAGAME_VERIFICATION_STDOUT_PATH"] =
            $apiStandardOutputPath
        $apiStartInfo.Environment["NOVAGAME_VERIFICATION_STDERR_PATH"] =
            $apiStandardErrorPath
        $apiStartInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        $apiStartInfo.ArgumentList.Clear()
        foreach ($apiArgument in @(
                "-NoProfile",
                "-Command",
                '$stdoutPath = $env:NOVAGAME_VERIFICATION_STDOUT_PATH; ' +
                '$stderrPath = $env:NOVAGAME_VERIFICATION_STDERR_PATH; ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDOUT_PATH", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDERR_PATH", $null, "Process"); ' +
                '& dotnet run --project server/src/NovaGame.Api/NovaGame.Api.csproj --no-build --no-restore --no-launch-profile ' +
                '1> $stdoutPath 2> $stderrPath; exit $LASTEXITCODE'
            )) {
            $null = $apiStartInfo.ArgumentList.Add($apiArgument)
        }
        $apiProcess = Start-NovaGameJobProcess `
            -JobHandle $processJobHandle `
            -TargetStartInfo $apiStartInfo

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
            NOVAGAME_E2E_CREATE_PASSWORD = $createPassword
            NOVAGAME_E2E_CREATE_RETRY_PASSWORD = $createRetryPassword
            NOVAGAME_E2E_RESET_PASSWORD = $resetPassword
            NOVAGAME_E2E_RESET_RETRY_PASSWORD = $resetRetryPassword
            NOVAGAME_E2E_BROWSER_CHANNEL = $browserChannel
            NOVAGAME_E2E_MYSQL_CONTAINER = [string]$mysqlContainers[0]
            NOVAGAME_E2E_MYSQL_ROOT_PASSWORD = [string]$testEnvironment["NOVAGAME_MYSQL_ROOT_PASSWORD"]
            NOVAGAME_E2E_MYSQL_DATABASE = [string]$testEnvironment["NOVAGAME_MYSQL_DATABASE"]
            PLAYWRIGHT_HTML_OUTPUT_DIR = (Join-Path $logRoot "playwright-report")
            PLAYWRIGHT_JSON_OUTPUT_FILE = (Join-Path $logRoot "playwright-result.json")
        }
        foreach ($entry in $e2eEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                [string]$entry.Key,
                [string]$entry.Value,
                "Process")
        }

        $dynamicSecretChannel =
            New-AdminManagementDynamicSecretCollector
        $playwrightStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $playwrightStartInfo.FileName = [string]$Toolchain.nodePath
        $playwrightStartInfo.WorkingDirectory = $runRoot
        $playwrightStartInfo.UseShellExecute = $false
        $playwrightStartInfo.CreateNoWindow = $true
        $playwrightStartInfo.RedirectStandardOutput = $false
        $playwrightStartInfo.RedirectStandardError = $false
        $playwrightStartInfo.Environment["CI"] = "true"
        $playwrightStartInfo.Environment["NO_COLOR"] = "1"
        $playwrightStartInfo.Environment["FORCE_COLOR"] = "0"
        $playwrightStartInfo.Environment["pnpm_config_store_dir"] =
            [string]$Toolchain.storePath
        $pnpmShimPath = Join-Path $runRoot "pnpm.cmd"
        $pnpmShimContent = @(
            "@echo off",
            ('"{0}" "{1}" dlx "pnpm@{2}" %*' -f
                [string]$Toolchain.nodePath,
                [string]$Toolchain.pnpmBootstrapPath,
                [string]$Toolchain.pnpmVersion)
        ) -join "`r`n"
        Set-Content `
            -LiteralPath $pnpmShimPath `
            -Value $pnpmShimContent `
            -Encoding Ascii `
            -NoNewline
        $playwrightStartInfo.Environment["PATH"] =
            "$runRoot$([System.IO.Path]::PathSeparator)" +
            "$(Split-Path -Parent ([string]$Toolchain.nodePath))" +
            "$([System.IO.Path]::PathSeparator)$env:PATH"
        foreach ($entry in $e2eEnvironment.GetEnumerator()) {
            $playwrightStartInfo.Environment[[string]$entry.Key] = [string]$entry.Value
        }
        $playwrightStartInfo.Environment[
            "NOVAGAME_E2E_DYNAMIC_SECRET_PIPE"] =
            [string]$dynamicSecretChannel.PipeName
        $playwrightStartInfo.Environment[
            "NOVAGAME_E2E_DYNAMIC_SECRET_PIPE_KEY"] =
            [string]$dynamicSecretChannel.EncodedAuthenticationKey
        $playwrightStandardOutputPath = Join-Path `
            $logRoot `
            "playwright.stdout.log"
        $playwrightStandardErrorPath = Join-Path `
            $logRoot `
            "playwright.stderr.log"
        $playwrightStartInfo.Environment["NOVAGAME_VERIFICATION_STDOUT_PATH"] =
            $playwrightStandardOutputPath
        $playwrightStartInfo.Environment["NOVAGAME_VERIFICATION_STDERR_PATH"] =
            $playwrightStandardErrorPath
        $playwrightCliPath = Join-Path `
            ([string]$Toolchain.adminRoot) `
            "node_modules\@playwright\test\cli.js"
        Assert-Condition `
            (Test-Path -LiteralPath $playwrightCliPath -PathType Leaf) `
            "Admin Playwright CLI is missing at '$playwrightCliPath'."
        $playwrightTargetArguments = @(
            $playwrightCliPath,
            "test",
            "--config", (Join-Path `
                ([string]$Toolchain.adminRoot) `
                "playwright.config.ts")
        ) + $playwrightSpecArguments + @(
            "--reporter=json",
            "--output", (Join-Path $logRoot "playwright-output")
        )
        $playwrightCommandPayload = [ordered]@{
            FilePath = [string]$Toolchain.nodePath
            Arguments = $playwrightTargetArguments
        } | ConvertTo-Json -Compress
        $playwrightStartInfo.Environment["NOVAGAME_VERIFICATION_COMMAND_PAYLOAD"] =
            $playwrightCommandPayload
        $playwrightStartInfo.FileName = (Get-Command "pwsh" -ErrorAction Stop).Source
        $playwrightStartInfo.ArgumentList.Clear()
        foreach ($playwrightArgument in @(
                "-NoProfile",
                "-Command",
                '$payload = $env:NOVAGAME_VERIFICATION_COMMAND_PAYLOAD | ConvertFrom-Json -AsHashtable; ' +
                '$stdoutPath = $env:NOVAGAME_VERIFICATION_STDOUT_PATH; ' +
                '$stderrPath = $env:NOVAGAME_VERIFICATION_STDERR_PATH; ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_COMMAND_PAYLOAD", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDOUT_PATH", $null, "Process"); ' +
                '[Environment]::SetEnvironmentVariable("NOVAGAME_VERIFICATION_STDERR_PATH", $null, "Process"); ' +
                '& ([string]$payload.FilePath) @([string[]]$payload.Arguments) ' +
                '1> $stdoutPath 2> $stderrPath; exit $LASTEXITCODE'
            )) {
            $null = $playwrightStartInfo.ArgumentList.Add(
                [string]$playwrightArgument)
        }
        $playwrightProcess = Start-NovaGameJobProcess `
            -JobHandle $processJobHandle `
            -TargetStartInfo $playwrightStartInfo
        # The frozen serial suite has five tests that can each use its 300-second
        # Playwright budget. Keep the outer process budget above that aggregate so
        # the runner returns its registered failure diagnostic instead of being cut off.
        $playwrightCompleted = $playwrightProcess.WaitForExit(1800000)
        if (-not $playwrightCompleted) {
            $ownedProcessIdentities = @(
                Get-NovaGameJobProcessIdentities -JobHandle $processJobHandle)
            $processJobIdentityCaptured = $true
            $playwrightProcess.Kill($true)
        }
        $playwrightProcess.WaitForExit()
        $playwrightProcess.Refresh()
        $playwrightExitCode = $playwrightProcess.ExitCode
        if (-not $playwrightCompleted) {
            throw "Admin Playwright acceptance exceeded 1800 seconds."
        }
        if ($playwrightExitCode -ne 0) {
            $playwrightFailureDiagnostic =
                Get-AdminPlaywrightFailureDiagnostic `
                    -ResultPath $env:PLAYWRIGHT_JSON_OUTPUT_FILE `
                    -Mode $Mode
            throw "Admin Playwright acceptance failed with exit code $playwrightExitCode. Failure rule: $playwrightFailureDiagnostic."
        }
        try {
            Assert-AdminPlaywrightResult `
                -ResultPath $env:PLAYWRIGHT_JSON_OUTPUT_FILE `
                -Mode $Mode
        }
        catch {
            throw "Admin Playwright result validation failed."
        }
        Assert-AdminManagementResponseAuditReports `
            -RunRoot $runRoot `
            -Mode $Mode |
            Out-Null
        $screenshotSpecifications = @(
            Get-AdminManagementScreenshotSpecifications -Mode $Mode)
        if ($RunSwordMasterAcceptance) {
            Invoke-SwordMasterRealApiAcceptance -ApiUrl $apiUrl
        }
    }
    catch {
        $operationError = $_
    }
    finally {
        try {
            if (-not $processJobIdentityCaptured -and
                $processJobHandle -ne [IntPtr]::Zero) {
                $ownedProcessIdentities = @(
                    Get-NovaGameJobProcessIdentities `
                        -JobHandle $processJobHandle)
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to capture the Admin management process job: $($_.Exception.Message)")
        }

        try {
            if ($processJobHandle -ne [IntPtr]::Zero) {
                Close-NovaGameProcessJob -JobHandle $processJobHandle
                $processJobHandle = [IntPtr]::Zero
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to close the Admin management process job: $($_.Exception.Message)")
        }

        if ($processJobHandle -ne [IntPtr]::Zero) {
            try {
                Close-NovaGameProcessJob -JobHandle $processJobHandle
                $processJobHandle = [IntPtr]::Zero
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to force-close the Admin management process job handle.")
            }
        }

        try {
            if ($null -ne $playwrightProcess) {
                $playwrightProcess.Dispose()
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to dispose the Admin Playwright process handle.")
        }

        try {
            if ($null -ne $dbMigratorProcess) {
                $dbMigratorProcess.Dispose()
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to dispose the Admin DbMigrator process handle.")
        }

        try {
            if ($null -ne $apiProcess) {
                $apiProcess.Dispose()
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to dispose the Admin API process handle.")
        }

        if ($null -ne $dynamicSecretChannel) {
            try {
                $dynamicSecretRecords = @(
                    $dynamicSecretChannel.Collector.StopAndSnapshot(
                        [TimeSpan]::FromSeconds(10)))
                $dynamicSecretSnapshotComplete = $true
                $dynamicSecretKinds = @(
                    $dynamicSecretRecords |
                        ForEach-Object { [int]$_.Kind } |
                        Sort-Object -Unique)
                Assert-StringSet `
                    -Expected @("1", "2") `
                    -Actual @($dynamicSecretKinds | ForEach-Object { [string]$_ }) `
                    -Message "The dynamic-secret registry did not receive both access and refresh tokens."
                $dynamicSecretRegistryComplete = $true
            }
            catch {
                $null = $cleanupErrors.Add(
                    "dynamic-secret-registry-incomplete")
                try {
                    $dynamicSecretRecords = @(
                        $dynamicSecretChannel.Collector.SnapshotForCleanup())
                    $dynamicSecretSnapshotComplete = $true
                }
                catch {
                    $null = $cleanupErrors.Add(
                        "dynamic-secret-registry-cleanup-snapshot-failed")
                }
            }
            finally {
                try {
                    $dynamicSecretChannel.Collector.Dispose()
                }
                catch {
                    $null = $cleanupErrors.Add(
                        "dynamic-secret-registry-dispose-failed")
                }
            }
        }
        elseif ($null -eq $operationError) {
            $null = $cleanupErrors.Add(
                "dynamic-secret-registry-missing")
        }

        if ($null -eq $operationError -and $cleanupErrors.Count -eq 0) {
            try {
                $screenshotInspectionRecords = @(
                    Assert-AdminManagementSuccessfulScreenshots `
                        -RunRoot $runRoot `
                        -ScreenshotSpecifications $screenshotSpecifications)
                Assert-AdminManagementScreenshotIdentity `
                    -RunRoot $runRoot `
                    -InspectionRecords $screenshotInspectionRecords
                $successfulScreenshotsValidated = $true
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Admin management screenshot validation failed after process shutdown.")
            }
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
                $resolvedTestRoot = [System.IO.Path]::GetFullPath($runRoot).TrimEnd('\') + '\'
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

        try {
            Assert-AdminManagementCleanup `
                -ProjectName $projectName `
                -Ports @($apiPort, $adminPort, $publishedPort) `
                -OwnedProcessIdentities $ownedProcessIdentities `
                -TemporarySecretPaths @($testEnvironmentFile)
        }
        catch {
            $null = $cleanupErrors.Add(
                "Admin management final cleanup assertion failed: $($_.Exception.Message)")
        }

        try {
            $finalAdminPlaywrightSnapshot =
                Get-AdminManagementArtifactSnapshot `
                    -ArtifactRoot $legacyAdminPlaywrightRoot
            $changedLegacyAdminArtifacts = @(
                Compare-AdminManagementArtifactSnapshots `
                    -Before $preexistingAdminPlaywrightSnapshot `
                    -After $finalAdminPlaywrightSnapshot)
            Assert-Condition `
                ($changedLegacyAdminArtifacts.Count -eq 0) `
                "Admin management evidence escaped its run root:`n$($changedLegacyAdminArtifacts -join "`n")"
        }
        catch {
            $null = $cleanupErrors.Add(
                "Admin management legacy-output audit failed: $($_.Exception.Message)")
        }

        if ($null -eq $operationError -and
            $cleanupErrors.Count -eq 0 -and
            -not $successfulScreenshotsValidated) {
            $null = $cleanupErrors.Add(
                "Admin management successful screenshots were not validated before cleanup.")
        }
        $allowedScreenshotRelativePaths = if (
            $null -eq $operationError -and
            $cleanupErrors.Count -eq 0 -and
            $successfulScreenshotsValidated) {
            @($screenshotSpecifications |
                ForEach-Object { [string]$_.RelativePath })
        }
        else {
            @()
        }

        if ($dynamicSecretRegistryComplete -and
            $dynamicSecretSnapshotComplete) {
            try {
                Remove-AdminManagementNonRetainableArtifacts `
                    -ArtifactRoot $runRoot `
                    -AllowedScreenshotRelativePaths $allowedScreenshotRelativePaths
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to apply the Admin management artifact policy: $($_.Exception.Message)")
            }
        }
        else {
            $allowedScreenshotRelativePaths = @()
        }
        if (($null -ne $operationError -or $cleanupErrors.Count -gt 0) -and
            $dynamicSecretRegistryComplete -and
            $dynamicSecretSnapshotComplete) {
            $allowedScreenshotRelativePaths = @()
            try {
                Remove-AdminManagementNonRetainableArtifacts `
                    -ArtifactRoot $runRoot
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to remove binary Admin management failure evidence: $($_.Exception.Message)")
            }
        }

        $artifactSensitiveValues = @(
            @(
                $seedPassword
                $createPassword
                $createRetryPassword
                $resetPassword
                $resetRetryPassword
                [string]$adminAccessEnvironment[
                    "NovaGame__AdminAccess__Jwt__SigningKey"]
                if ($null -ne $testEnvironment) {
                    [string]$testEnvironment["NOVAGAME_MYSQL_PASSWORD"]
                    [string]$testEnvironment["NOVAGAME_MYSQL_ROOT_PASSWORD"]
                }
                [string]$connectionString
                if ($null -ne $dynamicSecretChannel) {
                    [string]$dynamicSecretChannel.EncodedAuthenticationKey
                }
                $dynamicSecretRecords |
                    ForEach-Object { [string]$_.Value }
            ) | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_)
            }
        )
        $artifactSecretScanPassed = $false
        if ($dynamicSecretRegistryComplete -and
            $dynamicSecretSnapshotComplete) {
            try {
                Assert-AdminManagementArtifactSecrets `
                    -ArtifactRoot $runRoot `
                    -SensitiveValues $artifactSensitiveValues `
                    -AllowedScreenshotRelativePaths $allowedScreenshotRelativePaths `
                    -ScreenshotInspectionRecords $screenshotInspectionRecords
                $artifactSecretScanPassed = $true
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Admin management retained-artifact secret scan failed: $($_.Exception.Message)")
                try {
                    Remove-AdminManagementRunRootVerified `
                        -RunRoot $runRoot `
                        -MaximumAttempts 6
                }
                catch {
                    $null = $cleanupErrors.Add(
                        "Failed to remove unsafe Admin management artifacts: $($_.Exception.Message)")
                }
            }
        }

        $runRootRemovalRequired =
            Test-AdminManagementRunRootRemovalRequired `
                -OperationFailed ($null -ne $operationError) `
                -CleanupErrorCount $cleanupErrors.Count `
                -DynamicSecretRegistryComplete $dynamicSecretRegistryComplete `
                -ArtifactSecretScanPassed $artifactSecretScanPassed
        if ($runRootRemovalRequired -and
            (Test-Path -LiteralPath $runRoot)) {
            try {
                Remove-AdminManagementRunRootVerified `
                    -RunRoot $runRoot `
                    -MaximumAttempts 6
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to remove incomplete dynamic-secret evidence: $($_.Exception.Message)")
            }
        }

        if ($null -eq $operationError -and
            $cleanupErrors.Count -eq 0 -and
            $artifactSecretScanPassed -and
            -not [string]::IsNullOrWhiteSpace([string]$logRoot) -and
            (Test-Path -LiteralPath $logRoot -PathType Container)) {
            try {
                $adminE2ERoot = $adminManagementRoot
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
                    Remove-AdminManagementNonRetainableArtifacts `
                        -ArtifactRoot $resolvedLogRoot `
                        -AllowedScreenshotRelativePaths $allowedScreenshotRelativePaths
                    Assert-AdminManagementScreenshotIdentity `
                        -RunRoot $runRoot `
                        -InspectionRecords $screenshotInspectionRecords
                }
            }
            catch {
                $null = $cleanupErrors.Add(
                    "Failed to apply the successful Admin E2E artifact policy.")
                try {
                    Remove-AdminManagementNonRetainableArtifacts `
                        -ArtifactRoot $runRoot
                    Assert-AdminManagementArtifactSecrets `
                        -ArtifactRoot $runRoot `
                        -SensitiveValues $artifactSensitiveValues
                }
                catch {
                    $null = $cleanupErrors.Add(
                        "Failed to reduce unremoved Admin E2E evidence to safe text: $($_.Exception.Message)")
                    try {
                        Remove-AdminManagementRunRootVerified `
                            -RunRoot $runRoot `
                            -MaximumAttempts 6
                    }
                    catch {
                        $null = $cleanupErrors.Add(
                            "Failed to remove unverified Admin E2E evidence '$runRoot'.")
                    }
                }
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
    $m35BrowserMode = if ((Get-M35TaskStatus -TaskId "M35-07-T01") -in @(
            "in_progress", "blocked", "complete")) {
        "M35Management"
    }
    else {
        "LegacyGameConfig"
    }
    if ($m35BrowserMode -eq "M35Management") {
        Assert-AdminEndToEndHostCapacity
    }

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
    $expectedBrowserSpecs = if ($m35BrowserMode -eq "M35Management") {
        @(
            "admin/apps/web-ele/e2e/admin-access.spec.ts",
            "admin/apps/web-ele/e2e/game-config.spec.ts"
        )
    }
    else {
        @("admin/apps/web-ele/e2e/game-config.spec.ts")
    }
    Assert-StringSet `
        -Expected $expectedBrowserSpecs `
        -Actual $browserSpecs `
        -Message "Admin browser specifications do not match the active M3.5 stage."
    foreach ($browserSpec in $browserSpecs) {
        $browserSpecSource = Get-Content -LiteralPath $browserSpec -Raw
        Assert-Condition `
            (-not [regex]::IsMatch(
                $browserSpecSource,
                '(?m)\b(?:page|context)\.request\.(?:delete|fetch|get|head|patch|post|put)\s*\(|\brequest\.newContext\s*\(')) `
            "Admin browser spec '$browserSpec' must use the audited direct-request wrapper."
        Assert-Condition `
            (-not [regex]::IsMatch(
                $browserSpecSource,
                '(?m)\b(?:page|context)\.route\s*\(|\broute\.abort\s*\(')) `
            "Admin browser spec '$browserSpec' must not own request interception or aborts."
    }
    $acceptanceEvidenceSource = Get-Content `
        -LiteralPath "admin/apps/web-ele/e2e/acceptance-evidence.ts" `
        -Raw
    Assert-Condition `
        ($acceptanceEvidenceSource.Contains(
                "withExpectedApiNetworkFailure",
                [StringComparison]::Ordinal) -and
            $acceptanceEvidenceSource.Contains(
                "newObservedContext",
                [StringComparison]::Ordinal) -and
            $acceptanceEvidenceSource.Contains(
                "await route.abort('failed');",
                [StringComparison]::Ordinal)) `
        "Admin expected network failures must use the evidence-owned exact abort scope."
    $contextClosePosition = $acceptanceEvidenceSource.IndexOf(
        "await this.closeContext(context);",
        [StringComparison]::Ordinal)
    $listenerDetachPosition = $acceptanceEvidenceSource.IndexOf(
        "context.off('request', handlers.request);",
        [StringComparison]::Ordinal)
    Assert-Condition `
        ($contextClosePosition -ge 0 -and
            $listenerDetachPosition -gt $contextClosePosition) `
        "Admin response evidence must close browser contexts before detaching request listeners."
    Assert-Condition `
        ($acceptanceEvidenceSource.Contains(
                "private observeContext(",
                [StringComparison]::Ordinal) -and
            -not $acceptanceEvidenceSource.Contains(
                "assertScreenshotSurface",
                [StringComparison]::Ordinal)) `
        "Admin acceptance context observation and screenshot scanning must remain evidence-internal."
    Assert-Condition `
        ($acceptanceEvidenceSource.Contains(
                "this.browserApiOrigin = requiredHttpOrigin('NOVAGAME_E2E_BASE_URL');",
                [StringComparison]::Ordinal) -and
            $acceptanceEvidenceSource.Contains(
                "this.directApiOrigin = requiredHttpOrigin('NOVAGAME_E2E_API_URL');",
                [StringComparison]::Ordinal) -and
            $acceptanceEvidenceSource.Contains(
                "this.browserApiOrigin === this.directApiOrigin",
                [StringComparison]::Ordinal) -and
            $acceptanceEvidenceSource.Contains(
                "parsedUrl.origin !== this.directApiOrigin",
                [StringComparison]::Ordinal) -and
            $acceptanceEvidenceSource.Contains(
                "context ? this.browserApiOrigin : this.directApiOrigin",
                [StringComparison]::Ordinal)) `
        "Admin acceptance browser and direct API origins must remain distinct and source-bound."
    $gameConfigAcceptanceSource = Get-Content `
        -LiteralPath "admin/apps/web-ele/e2e/game-config.spec.ts" `
        -Raw
    Assert-Condition `
        ($gameConfigAcceptanceSource.Contains(
                "const apiBaseURL = requireEnvironment('NOVAGAME_E2E_API_URL');",
                [StringComparison]::Ordinal) -and
            -not $gameConfigAcceptanceSource.Contains(
                "NOVAGAME_E2E_BASE_URL",
                [StringComparison]::Ordinal)) `
        "The legacy GameConfig direct-request source must remain NOVAGAME_E2E_API_URL."
    $captureScreenshotStart = $acceptanceEvidenceSource.IndexOf(
        "async captureScreenshot(page: Page, requiredPath: string): Promise<void> {",
        [StringComparison]::Ordinal)
    $captureScreenshotEnd = $acceptanceEvidenceSource.IndexOf(
        "prepareLegacyGameConfigPermissionFixture",
        [StringComparison]::Ordinal)
    Assert-Condition `
        ($captureScreenshotStart -ge 0 -and
            $captureScreenshotEnd -gt $captureScreenshotStart) `
        "Admin acceptance screenshots must use the evidence-owned atomic capture method."
    $captureScreenshotSource = $acceptanceEvidenceSource.Substring(
        $captureScreenshotStart,
        $captureScreenshotEnd - $captureScreenshotStart)
    $screenshotScanPosition = $captureScreenshotSource.IndexOf(
        "await this.scanScreenshotSurface(page);",
        [StringComparison]::Ordinal)
    $screenshotCapturePosition = $captureScreenshotSource.IndexOf(
        "await page.screenshot({",
        [StringComparison]::Ordinal)
    $screenshotPathLedgerPosition = $captureScreenshotSource.IndexOf(
        "this.screenshotPaths.add(requiredPath);",
        [StringComparison]::Ordinal)
    $screenshotScanLedgerPosition = $captureScreenshotSource.IndexOf(
        "this.screenshotSurfaceScans += 1;",
        [StringComparison]::Ordinal)
    Assert-Condition `
        ($screenshotScanPosition -ge 0 -and
            $screenshotCapturePosition -gt $screenshotScanPosition -and
            $screenshotPathLedgerPosition -gt $screenshotCapturePosition -and
            $screenshotScanLedgerPosition -gt $screenshotPathLedgerPosition -and
            $acceptanceEvidenceSource.Contains(
                "this.screenshotSurfaceScans !== this.screenshotPaths.size",
                [StringComparison]::Ordinal)) `
        "Admin acceptance screenshots must scan before capture and ledger only successful unique captures."

    $toolchain = Initialize-AdminToolchain
    Test-AdminManagementDynamicSecretClientPolicy -Toolchain $toolchain
    Test-AdminAcceptanceOwnershipPolicy `
        -Toolchain $toolchain `
        -SpecPaths $browserSpecs
    $previousCi = $env:CI
    $previousNodeOptionsExists = Test-Path -LiteralPath "Env:NODE_OPTIONS"
    $previousNodeOptions = $env:NODE_OPTIONS
    $env:CI = "true"
    try {
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "format:check")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "lint")
        # Turbo requires workspace packages to resolve below the Vben root. SwordMaster is an
        # intentionally external consumer, so run the equivalent generic app and E2E checks directly.
        Invoke-AdminPnpm `
            -Toolchain $toolchain `
            -ArgumentList @("--filter", "@vben/web-ele", "run", "typecheck")
        Invoke-AdminPnpm `
            -Toolchain $toolchain `
            -ArgumentList @("exec", "tsc", "--noEmit", "-p", "tsconfig.e2e.json")
        Invoke-AdminPnpm -Toolchain $toolchain -ArgumentList @("run", "test:unit")
        # Build the generic app and its workspace dependencies without Turbo. Turbo rejects the
        # intentionally external SwordMaster consumer during workspace discovery before filtering.
        # Keep this equivalent to the Vben root build command's Node heap setting.
        $env:NODE_OPTIONS = "--max-old-space-size=8192"
        Invoke-AdminPnpm `
            -Toolchain $toolchain `
            -ArgumentList @(
                "--filter", "@vben/web-ele...", "--if-present", "run", "build")
        Invoke-AdminEndToEnd `
            -Toolchain $toolchain `
            -Mode $m35BrowserMode `
            -RunSwordMasterAcceptance:$RunSwordMasterAcceptance
    }
    finally {
        if ($previousNodeOptionsExists) {
            $env:NODE_OPTIONS = $previousNodeOptions
        }
        else {
            Remove-Item -LiteralPath "Env:NODE_OPTIONS" -ErrorAction SilentlyContinue
        }
        $env:CI = $previousCi
    }

    Write-Output "Admin verification passed."
}

function Test-AdminEndToEnd {
    $m35BrowserTaskStatus = Get-M35TaskStatus -TaskId "M35-07-T01"
    Assert-Condition `
        ($m35BrowserTaskStatus -in @("in_progress", "blocked", "complete")) `
        "AdminE2E is unavailable before M35-07-T01 is active. Use -Scope Admin for the legacy GameConfig regression."
    Assert-AdminEndToEndHostCapacity
    $toolchain = Initialize-AdminToolchain
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        Invoke-AdminEndToEnd -Toolchain $toolchain -Mode "M35Management"
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
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerEquipmentGrantConfiguration.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsProfileRepository.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetCommandReceiptRepository.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerEquipmentGrantRepository.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/PlayerAssets/PlayerAssetsTransactionManager.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/Migrations/20260801121309_InitialPlayerAssets.cs",
        "src/SwordMaster.Host.EntityFrameworkCore/Migrations/20260810105902_AddPlayerEquipmentGrants.cs",
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
        "src/SwordMaster.Api/PlayerAssets/HttpCurrentSwordMasterAdministrator.cs",
        "src/SwordMaster.Api/PlayerAssets/HttpPlayerEquipmentGrantAuditContextAccessor.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantsController.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantExceptionFilter.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantOperationLogger.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantOpenApiOperationFilter.cs",
        "src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantAuthorizationMiddlewareResultHandler.cs",
        "src/SwordMaster.DbMigrator/Program.cs",
        "src/SwordMaster.DbMigrator/appsettings.json",
        "src/SwordMaster.DbMigrator/SwordMasterDbMigratorModule.cs",
        "src/SwordMaster.DbMigrator/SwordMasterDbMigratorConfigurationValidator.cs",
        "src/SwordMaster.DbMigrator/Auditing/SwordMasterDbMigratorRequestAuditContextAccessor.cs",
        "src/SwordMaster.DbMigrator/PlayerAssets/SwordMasterPlayerAssetsDevelopmentSeedRunner.cs",
        "src/SwordMaster.DbMigrator/PlayerAssets/SwordMasterPlayerEquipmentGrantPermissionSeedRunner.cs",
        "tests/SwordMaster.Application.Tests/PlayerAssets/GameConfigEquipmentDefinitionProviderTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/SwordMasterApiFactory.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsApiTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsMySqlApiTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerAssetsObservabilityTests.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterRuntimeApiFactory.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterRuntimeCompositionTests.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterDbMigratorConfigurationTests.cs",
        "tests/SwordMaster.Integration.Tests/RuntimeComposition/SwordMasterRuntimeDatabaseTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantComposedApiAcceptanceTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantPersistenceTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantPermissionSeedTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantMySqlApplicationTests.cs",
        "tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantApiTests.cs",
        "tests/SwordMaster.Integration.Tests/ClientContracts/PlayerEquipmentGrantOpenApiContract.cs",
        "tests/SwordMaster.Integration.Tests/ClientContracts/PlayerEquipmentGrantOpenApiContractTests.cs"
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
    Assert-Condition `
        $entityFrameworkRegistration.Contains(
            "AddScoped<IPlayerEquipmentGrantRepository, PlayerEquipmentGrantRepository>") `
        "SwordMaster EF must register the game-owned equipment-grant repository."

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
        "await SeedNovaGameAdministratorAndSwordMasterPermissionAsync(",
        [StringComparison]::Ordinal)
    $administratorContributorIndex = $dbMigratorProgram.IndexOf(
        ".OfType<AdministratorSeedContributor>()",
        [StringComparison]::Ordinal)
    $equipmentGrantPermissionSeedIndex = $dbMigratorProgram.IndexOf(
        ".GetRequiredService<SwordMasterPlayerEquipmentGrantPermissionSeedRunner>()",
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
            $administratorContributorIndex -ge 0 -and
            $equipmentGrantPermissionSeedIndex -gt $administratorContributorIndex -and
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

    $permissionSeedRunner = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.DbMigrator/PlayerAssets/SwordMasterPlayerEquipmentGrantPermissionSeedRunner.cs" `
        -Raw
    Assert-Condition `
        ($permissionSeedRunner.Contains("PlayerEquipmentGrantPermissions.Manage") -and
            $permissionSeedRunner.Contains("role.GrantPermission(") -and
            $permissionSeedRunner.Contains("AdminAccessAuditAction.PermissionGranted") -and
            $permissionSeedRunner.Contains("if (permission is null)")) `
        "SwordMaster equipment-grant permission seed must use the framework role aggregate and audit only new assignments."

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

    $equipmentGrantController = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantsController.cs" `
        -Raw
    foreach ($requiredEquipmentGrantControllerContract in @(
        '[Route("api/swordmaster/operations/player-assets/{playerId}/equipment-grants")]',
        '[NovaAuthorize(PlayerEquipmentGrantPermissions.Manage)]',
        '[UnitOfWork(IsDisabled = true)]',
        '[HttpGet]',
        '[HttpPost]',
        '[HttpPost("{grantId}/rollback")]',
        'public const string IdempotencyKeyHeaderName = "Idempotency-Key";',
        'public const string IfMatchHeaderName = "If-Match";'
    )) {
        Assert-Condition `
            $equipmentGrantController.Contains($requiredEquipmentGrantControllerContract) `
            "SwordMaster equipment-grant controller is missing required contract '$requiredEquipmentGrantControllerContract'."
    }
    Assert-Condition `
        (-not $equipmentGrantController.Contains("{playerId:guid}")) `
        "SwordMaster equipment-grant identifiers must reach the stable model-validation response instead of routing 404."

    $playerAuthenticationRegistration = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/SwordMasterPlayerAuthentication.cs" `
        -Raw
    foreach ($requiredEquipmentGrantRegistration in @(
        "ICurrentSwordMasterAdministrator",
        "IPlayerEquipmentGrantAuditContextAccessor",
        "PlayerEquipmentGrantExceptionFilter",
        "PlayerEquipmentGrantUnsupportedMediaTypeResultFilter",
        "PlayerEquipmentGrantAuthorizationMiddlewareResultHandler",
        "PlayerEquipmentGrantOpenApiOperationFilter",
        "InvalidModelStateResponseFactory"
    )) {
        Assert-Condition `
            $playerAuthenticationRegistration.Contains($requiredEquipmentGrantRegistration) `
            "SwordMaster equipment-grant API registration is missing '$requiredEquipmentGrantRegistration'."
    }

    $equipmentGrantAuthorization = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantAuthorizationMiddlewareResultHandler.cs" `
        -Raw
    Assert-Condition `
        ($equipmentGrantAuthorization.Contains("AuthorizationMiddlewareResultHandler") -and
            $equipmentGrantAuthorization.Contains("UnauthorizedAdministrator") -and
            $equipmentGrantAuthorization.Contains("PlayerEquipmentGrantErrorCodes.Forbidden") -and
            $equipmentGrantAuthorization.Contains('WWWAuthenticate = "Bearer"') -and
            $equipmentGrantAuthorization.Contains("_defaultHandler.HandleAsync(")) `
        "SwordMaster equipment-grant authorization responses must be endpoint-specific and delegate every unrelated endpoint."

    $equipmentGrantExceptionFilter = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantExceptionFilter.cs" `
        -Raw
    Assert-Condition `
        ($equipmentGrantExceptionFilter.Contains("OperationCanceledException") -and
            $equipmentGrantExceptionFilter.Contains("RequestAborted.IsCancellationRequested") -and
            $equipmentGrantExceptionFilter.Contains("NovaCoreErrorCodes.InternalServerError") -and
            $equipmentGrantExceptionFilter.Contains("PlayerEquipmentGrantUnsupportedMediaTypeResultFilter")) `
        "SwordMaster equipment-grant failures must preserve cancellation and sanitize pipeline and internal failures."

    $equipmentGrantOpenApi = Get-Content -LiteralPath `
        "$serverRoot/src/SwordMaster.Api/PlayerAssets/PlayerEquipmentGrantOpenApiOperationFilter.cs" `
        -Raw
    Assert-Condition `
        ($equipmentGrantOpenApi.Contains('Id = "Bearer"') -and
            $equipmentGrantOpenApi.Contains("PlayerEquipmentGrantsController.IfMatchHeaderName") -and
            $equipmentGrantOpenApi.Contains("PlayerEquipmentGrantsController.IdempotencyKeyHeaderName") -and
            $equipmentGrantOpenApi.Contains("RequireRequestProperties(") -and
            $equipmentGrantOpenApi.Contains("schema.Required = new HashSet<string>(") -and
            $equipmentGrantOpenApi.Contains("propertySchema.Nullable = false")) `
        "SwordMaster equipment-grant OpenAPI must use administrator Bearer security and document required non-null mutation contracts."

    $equipmentGrantContractUpdater = Get-Content -LiteralPath `
        "scripts/update-swordmaster-player-equipment-grant-contract.ps1" `
        -Raw
    foreach ($requiredUpdaterBoundary in @(
        "Assert-TemporaryOperatorArtifacts",
        "Publish-TrackedOperatorArtifacts",
        "Assert-LegacyPlayerArtifactsUnchanged",
        "temporaryGeneratedTypesRoot",
        "SimulateInvalidGenerationInput"
    )) {
        Assert-Condition `
            $equipmentGrantContractUpdater.Contains($requiredUpdaterBoundary) `
            "SwordMaster equipment-grant contract updater is missing '$requiredUpdaterBoundary'."
    }

    $equipmentGrantMySqlApplicationTests = Get-Content -LiteralPath `
        "$serverRoot/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantMySqlApplicationTests.cs" `
        -Raw
    foreach ($requiredMySqlApplicationEvidence in @(
        "GrantCommitReplayAndChangedFingerprintUseOnePersistedResult",
        "EquippedRollbackRejectsThenUnequippedRollbackCommitsInProfileFirstLockOrder",
        "ConcurrentSameKeyGrantsConvergeOnOnePersistedResult",
        "ConcurrentDifferentKeyGrantsProduceOneSuccessAndOneStaleConflict",
        "FailureAfterFlushBeforeCommitRollsBackProfileAndGrant",
        "Task.WhenAll(",
        "ProfileLockContentionInterceptor",
        "AssertContenderRemainsBlockedAsync",
        "DrainStartedGrantTasksAsync",
        "WaitForProfileLockAcquiredAsync",
        "LockingCommandRecorder",
        "FlushThenFailTransactionManager"
    )) {
        Assert-Condition `
            $equipmentGrantMySqlApplicationTests.Contains(
                $requiredMySqlApplicationEvidence) `
            "SwordMaster real-MySQL equipment-grant evidence is missing '$requiredMySqlApplicationEvidence'."
    }

    $composedApiAcceptanceTests = Get-Content -LiteralPath `
        "$serverRoot/tests/SwordMaster.Integration.Tests/PlayerAssets/PlayerEquipmentGrantComposedApiAcceptanceTests.cs" `
        -Raw
    Assert-Condition `
        ([regex]::Matches($composedApiAcceptanceTests, '(?m)^\s*\[Fact\]\s*$').Count -eq 3) `
        "SwordMaster composed API acceptance must define exactly three Fact tests."
    foreach ($requiredComposedAcceptanceEvidence in @(
        "SwordMasterRuntimeDatabaseCollection",
        "[Collection(SwordMasterRuntimeDatabaseCollection.Name)]",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_API_URL",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_PLAYER_TOKEN",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_CONFIGURATION_VERSION_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_PLAYER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_CHARACTER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_PLAYER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CHARACTER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_GRANT_CORRELATION_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CORRELATION_ID",
        "HttpClient",
        "MySqlConnection",
        "/api/auth/login",
        "CollectionPath",
        "HistoryPath",
        "PlayerEquipmentPath",
        "DELETE FROM admin_role_permissions",
        "INSERT INTO admin_role_permissions",
        "Task.WhenAll(",
        "GrantConcurrentlyAsync",
        "ReadRequiredGuid",
        "AssertUtcDateTimeEqualAtMySqlPrecision",
        "CreateExpectedGrantFingerprint",
        "CreateExpectedRollbackFingerprint",
        "SHA256.HashData"
    )) {
        Assert-Condition `
            $composedApiAcceptanceTests.Contains($requiredComposedAcceptanceEvidence) `
            "SwordMaster composed API acceptance is missing '$requiredComposedAcceptanceEvidence'."
    }
    foreach ($forbiddenComposedAcceptanceEvidence in @(
        "WebApplicationFactory",
        "SwordMasterApiFactory",
        "SwordMasterRuntimeApiFactory",
        "ConfigureServices",
        "ConfigureTestServices",
        "RemoveAll",
        "IPlayerEquipmentGrantAppService",
        "ICurrentSwordMasterAdministrator",
        "IPlayerEquipmentGrantAuditContextAccessor",
        "Moq",
        "NSubstitute",
        "FakeItEasy"
    )) {
        Assert-Condition `
            (-not $composedApiAcceptanceTests.Contains($forbiddenComposedAcceptanceEvidence)) `
            "SwordMaster composed API acceptance must not use '$forbiddenComposedAcceptanceEvidence'."
    }

    Write-Output "SwordMaster server architecture verification passed."
}

function Invoke-DotNetFilteredTestWithExecutionCount {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$Filter,
        [Parameter(Mandatory)][string]$ResultName,
        [ValidateRange(0, 100000)][int]$ExpectedTestCount = 0,
        [string]$ResultsRoot
    )

    $resultsRoot = if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
        Join-Path `
            $localRoot `
            ("test-results\focused-" + [Guid]::NewGuid().ToString("N"))
    }
    else {
        [System.IO.Path]::GetFullPath($ResultsRoot)
    }
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
    Assert-Condition `
        (-not $resolvedResultsRoot.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar).Equals(
                $resolvedLocalRoot.TrimEnd(
                    [System.IO.Path]::DirectorySeparatorChar,
                    [System.IO.Path]::AltDirectorySeparatorChar),
                [StringComparison]::OrdinalIgnoreCase)) `
        "Focused test results must not use the repository-local '.local' root itself."

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
        $totalCount = [int]$counters.GetAttribute("total")
        $executedCount = [int]$counters.GetAttribute("executed")
        $passedCount = [int]$counters.GetAttribute("passed")
        $failedCount = [int]$counters.GetAttribute("failed")
        $notExecutedCount = [int]$counters.GetAttribute("notExecuted")
        Assert-Condition `
            ($executedCount -gt 0) `
            "Focused test filter '$Filter' executed zero tests for '$ProjectPath'."
        if ($ExpectedTestCount -gt 0) {
            Assert-Condition `
                ($totalCount -eq $ExpectedTestCount -and
                    $executedCount -eq $ExpectedTestCount -and
                    $passedCount -eq $ExpectedTestCount -and
                    $failedCount -eq 0 -and
                    $notExecutedCount -eq 0) `
                "Focused test filter '$Filter' must execute exactly $ExpectedTestCount successful tests for '$ProjectPath': total=$totalCount, executed=$executedCount, passed=$passedCount, failed=$failedCount, notExecuted=$notExecutedCount."
        }
        Assert-Condition `
            ($failedCount -eq 0 -and
                $notExecutedCount -eq 0 -and
                $passedCount -eq $executedCount) `
            "Focused test result for '$ProjectPath' was not completely successful: executed=$executedCount, passed=$passedCount, failed=$failedCount, notExecuted=$notExecutedCount."
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

function Invoke-SwordMasterAdminVitestWithExecutionCount {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)][string]$ScriptName,
        [Parameter(Mandatory)][int]$ExpectedTestCount,
        [Parameter(Mandatory)][string]$ResultName
    )

    $resultsRoot = Join-Path `
        $localRoot `
        ("test-results\swordmaster-admin-vitest-" +
            [Guid]::NewGuid().ToString("N"))
    $resolvedResultsRoot = [System.IO.Path]::GetFullPath($resultsRoot)
    $resolvedLocalRoot = [System.IO.Path]::GetFullPath($localRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        $resolvedResultsRoot.StartsWith(
            $resolvedLocalRoot,
            [StringComparison]::OrdinalIgnoreCase) `
        "Focused SwordMaster admin Vitest results must remain under the repository-local '.local' directory."

    New-Item -ItemType Directory -Path $resolvedResultsRoot -Force | Out-Null
    $resultPath = Join-Path $resolvedResultsRoot $ResultName
    try {
        Invoke-AdminPnpm `
            -Toolchain $Toolchain `
            -ArgumentList @(
                "--filter", "@swordmaster/admin-web-ele",
                "run", $ScriptName,
                "--reporter=json", "--outputFile", $resultPath)
        Assert-Condition `
            (Test-Path -LiteralPath $resultPath -PathType Leaf) `
            "Focused SwordMaster admin Vitest run did not produce '$resultPath'."

        $testResult = Get-Content -LiteralPath $resultPath -Raw |
            ConvertFrom-Json
        foreach ($requiredCounter in @(
            "numTotalTests", "numPassedTests", "numFailedTests",
            "numPendingTests"
        )) {
            Assert-Condition `
                ($null -ne $testResult.PSObject.Properties[$requiredCounter]) `
                "Focused SwordMaster admin Vitest result '$resultPath' is missing '$requiredCounter'."
        }

        $totalCount = [int]$testResult.numTotalTests
        $passedCount = [int]$testResult.numPassedTests
        $failedCount = [int]$testResult.numFailedTests
        $pendingCount = [int]$testResult.numPendingTests
        Assert-Condition `
            ($totalCount -eq $ExpectedTestCount) `
            "Focused SwordMaster admin Vitest script '$ScriptName' must execute exactly $ExpectedTestCount tests; executed=$totalCount."
        Assert-Condition `
            ($passedCount -eq $ExpectedTestCount -and
                $failedCount -eq 0 -and
                $pendingCount -eq 0) `
            "Focused SwordMaster admin Vitest result for '$ScriptName' was not completely successful: total=$totalCount, passed=$passedCount, failed=$failedCount, pending=$pendingCount."
        Write-Output `
            "Focused SwordMaster admin Vitest verification passed for '$ScriptName': $totalCount test(s) executed."
    }
    finally {
        if (Test-Path -LiteralPath $resolvedResultsRoot -PathType Container) {
            Remove-Item -LiteralPath $resolvedResultsRoot -Recurse -Force
        }
    }
}

function Invoke-SwordMasterPlayerEquipmentGrantPlaywrightAcceptance {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)][string]$BaseUrl,
        [Parameter(Mandatory)][string]$ApiUrl,
        [Parameter(Mandatory)][string]$AdministratorUserName,
        [Parameter(Mandatory)][string]$AdministratorPassword,
        [Parameter(Mandatory)][string]$PlayerId,
        [Parameter(Mandatory)][string]$CharacterId,
        [Parameter(Mandatory)][string]$PlayerToken,
        [Parameter(Mandatory)][string]$ConfiguredItemId,
        [Parameter(Mandatory)][string]$NovaGameConnectionString,
        [Parameter(Mandatory)][string]$SwordMasterConnectionString,
        [Parameter(Mandatory)][string]$DisposableDatabaseMarker,
        [Parameter(Mandatory)][string]$BrowserChannel,
        [Parameter(Mandatory)][string]$ArtifactRoot
    )

    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
    $resolvedAcceptanceRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot ".local\\swordmaster-acceptance")).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        $resolvedArtifactRoot.StartsWith(
            $resolvedAcceptanceRoot,
            [StringComparison]::OrdinalIgnoreCase) `
        "SwordMaster Playwright artifacts must remain under '$resolvedAcceptanceRoot'."

    $resultPath = Join-Path $resolvedArtifactRoot "playwright-result.json"
    $outputRoot = Join-Path $resolvedArtifactRoot "playwright"
    $resolvedOutputRoot = [System.IO.Path]::GetFullPath($outputRoot)
    $resolvedArtifactRootPrefix = $resolvedArtifactRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        $resolvedOutputRoot.StartsWith(
            $resolvedArtifactRootPrefix,
            [StringComparison]::OrdinalIgnoreCase) `
        "SwordMaster Playwright output must remain under its disposable artifact root."
    $acceptanceEnvironment = [ordered]@{
        SWORDMASTER_ACCEPTANCE_BASE_URL = $BaseUrl
        SWORDMASTER_ACCEPTANCE_API_URL = $ApiUrl
        SWORDMASTER_ACCEPTANCE_BROWSER_CHANNEL = $BrowserChannel
        SWORDMASTER_ACCEPTANCE_USERNAME = $AdministratorUserName
        SWORDMASTER_ACCEPTANCE_PASSWORD = $AdministratorPassword
        SWORDMASTER_ACCEPTANCE_PLAYER_ID = $PlayerId
        SWORDMASTER_ACCEPTANCE_CHARACTER_ID = $CharacterId
        SWORDMASTER_ACCEPTANCE_PLAYER_TOKEN = $PlayerToken
        SWORDMASTER_ACCEPTANCE_CONFIGURED_ITEM_ID = $ConfiguredItemId
        SWORDMASTER_ACCEPTANCE_NOVAGAME_CONNECTION_STRING = $NovaGameConnectionString
        SWORDMASTER_ACCEPTANCE_SWORDMASTER_CONNECTION_STRING = $SwordMasterConnectionString
        SWORDMASTER_ACCEPTANCE_DISPOSABLE_DATABASE_MARKER = $DisposableDatabaseMarker
        SWORDMASTER_ACCEPTANCE_OUTPUT_DIR = $resolvedOutputRoot
        SWORDMASTER_ACCEPTANCE_RESULT_PATH = $resultPath
    }
    Assert-StringSet `
        -Expected @(
            "SWORDMASTER_ACCEPTANCE_API_URL",
            "SWORDMASTER_ACCEPTANCE_BASE_URL",
            "SWORDMASTER_ACCEPTANCE_BROWSER_CHANNEL",
            "SWORDMASTER_ACCEPTANCE_CHARACTER_ID",
            "SWORDMASTER_ACCEPTANCE_CONFIGURED_ITEM_ID",
            "SWORDMASTER_ACCEPTANCE_DISPOSABLE_DATABASE_MARKER",
            "SWORDMASTER_ACCEPTANCE_NOVAGAME_CONNECTION_STRING",
            "SWORDMASTER_ACCEPTANCE_OUTPUT_DIR",
            "SWORDMASTER_ACCEPTANCE_PASSWORD",
            "SWORDMASTER_ACCEPTANCE_PLAYER_ID",
            "SWORDMASTER_ACCEPTANCE_PLAYER_TOKEN",
            "SWORDMASTER_ACCEPTANCE_RESULT_PATH",
            "SWORDMASTER_ACCEPTANCE_SWORDMASTER_CONNECTION_STRING",
            "SWORDMASTER_ACCEPTANCE_USERNAME"
        ) `
        -Actual @($acceptanceEnvironment.Keys) `
        -Message "SwordMaster Playwright acceptance environment changed unexpectedly."

    Invoke-AdminPnpmIsolated `
        -Toolchain $Toolchain `
        -ArgumentList @(
            "--filter", "@swordmaster/admin-web-ele",
            "run", "test:e2e:equipment-grant") `
        -Environment $acceptanceEnvironment `
        -TimeoutSeconds 480
    Assert-Condition `
        (Test-Path -LiteralPath $resultPath -PathType Leaf) `
        "SwordMaster Playwright acceptance did not produce '$resultPath'."

    $testResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    foreach ($requiredCounter in @("stats", "suites")) {
        Assert-Condition `
            ($null -ne $testResult.PSObject.Properties[$requiredCounter]) `
            "SwordMaster Playwright result '$resultPath' is missing '$requiredCounter'."
    }

    $allTests = [System.Collections.Generic.List[object]]::new()
    function Add-PlaywrightTests {
        param([Parameter(Mandatory)]$Suite)

        $specsProperty = $Suite.PSObject.Properties["specs"]
        if ($null -ne $specsProperty -and $null -ne $specsProperty.Value) {
            foreach ($spec in @($specsProperty.Value)) {
                $testsProperty = $spec.PSObject.Properties["tests"]
                Assert-Condition `
                    ($null -ne $testsProperty -and $null -ne $testsProperty.Value) `
                    "SwordMaster Playwright specification '$($spec.title)' is missing tests."
                foreach ($test in @($testsProperty.Value)) {
                    $null = $allTests.Add([pscustomobject]@{
                            SpecTitle = [string]$spec.title
                            Test = $test
                        })
                }
            }
        }
        $suitesProperty = $Suite.PSObject.Properties["suites"]
        if ($null -ne $suitesProperty -and $null -ne $suitesProperty.Value) {
            foreach ($childSuite in @($suitesProperty.Value)) {
                Add-PlaywrightTests -Suite $childSuite
            }
        }
    }
    foreach ($suite in @($testResult.suites)) {
        Add-PlaywrightTests -Suite $suite
    }

    Assert-Condition `
        ($allTests.Count -eq 2) `
        "SwordMaster Playwright acceptance must define exactly two tests; found $($allTests.Count)."
    foreach ($testRecord in $allTests) {
        $test = $testRecord.Test
        $results = @($test.results)
        Assert-Condition `
            ($results.Count -eq 1) `
            "SwordMaster Playwright test '$($testRecord.SpecTitle)' must have exactly one result and zero retries."
        $result = $results[0]
        Assert-Condition `
            ([string]$result.status -eq "passed" -and [int]$result.retry -eq 0) `
            "SwordMaster Playwright test '$($testRecord.SpecTitle)' must pass without retries; status=$($result.status), retry=$($result.retry)."
    }

    foreach ($requiredCounter in @(
        "expected", "flaky", "skipped", "unexpected"
    )) {
        Assert-Condition `
            ($null -ne $testResult.stats.PSObject.Properties[$requiredCounter]) `
            "SwordMaster Playwright result '$resultPath' is missing stats.$requiredCounter."
    }
    $expectedCount = 2
    Assert-Condition `
        ([int]$testResult.stats.expected -eq $expectedCount -and
            [int]$testResult.stats.flaky -eq 0 -and
            [int]$testResult.stats.skipped -eq 0 -and
            [int]$testResult.stats.unexpected -eq 0) `
        "SwordMaster Playwright result must report exactly two expected tests with no flaky, skipped, or unexpected tests."
    Write-Output "SwordMaster PlayerEquipmentGrant Playwright acceptance passed: 2 tests executed."
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

function Test-SwordMasterPlayerEquipmentGrantGeneratedTypes {
    param([Parameter(Mandatory)]$Toolchain)

    $sampleRoot = Join-Path $repositoryRoot "samples\SwordMaster"
    $contractPath = Join-Path `
        $sampleRoot `
        "admin\contracts\player-equipment-grants.openapi.json"
    $trackedRoot = Join-Path `
        $sampleRoot `
        "admin\src\generated\player-equipment-grants"
    $generatedParent = Join-Path $localRoot "generated"
    $temporaryRoot = Join-Path `
        $generatedParent `
        "swordmaster-player-equipment-grants-$([Guid]::NewGuid().ToString('N'))"
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
        "Temporary equipment-grant generation must remain under '$resolvedGeneratedParent'."

    foreach ($requiredPath in @($contractPath, $trackedRoot)) {
        Assert-Condition `
            (Test-Path -LiteralPath $requiredPath) `
            "Missing generated equipment-grant contract path '$requiredPath'."
    }

    $inputEnvironmentExists = Test-Path `
        -LiteralPath "Env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT"
    $previousInputPath = $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT
    $outputEnvironmentExists = Test-Path `
        -LiteralPath "Env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT"
    $previousOutputPath = $env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT
    try {
        $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT = $contractPath
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
            -Expected @("types.gen.ts") `
            -Actual $trackedRelativePaths `
            -Message "Equipment-grant generation must track only types.gen.ts."
        Assert-StringSet `
            -Expected $trackedRelativePaths `
            -Actual $generatedRelativePaths `
            -Message "Generated equipment-grant TypeScript file set is stale."

        foreach ($relativePath in $trackedRelativePaths) {
            $trackedPath = Join-Path $trackedRoot $relativePath
            $generatedPath = Join-Path $resolvedTemporaryRoot $relativePath
            Assert-Condition `
                ((Get-FileHash -LiteralPath $trackedPath -Algorithm SHA256).Hash -eq
                    (Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256).Hash) `
                "Generated equipment-grant TypeScript file '$relativePath' is stale."
        }

        $generatedTypes = Get-Content `
            -LiteralPath (Join-Path $trackedRoot "types.gen.ts") `
            -Raw
        Assert-Condition `
            ($generatedTypes -notmatch '(?m)^export\s+(?!type\b)') `
            "Equipment-grant generation must expose TypeScript types only."
    }
    finally {
        if ($inputEnvironmentExists) {
            $env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT = $previousInputPath
        }
        else {
            Remove-Item `
                -LiteralPath "Env:SWORDMASTER_PLAYER_ASSETS_OPENAPI_INPUT" `
                -ErrorAction SilentlyContinue
        }
        if ($outputEnvironmentExists) {
            $env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT = $previousOutputPath
        }
        else {
            Remove-Item `
                -LiteralPath "Env:SWORDMASTER_PLAYER_ASSETS_TYPES_OUTPUT" `
                -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $resolvedTemporaryRoot -PathType Container) {
            Assert-Condition `
                $resolvedTemporaryRoot.StartsWith(
                    $resolvedGeneratedParent,
                    [StringComparison]::OrdinalIgnoreCase) `
                "Refusing to remove temporary output outside '$resolvedGeneratedParent'."
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
    }

    Write-Output "SwordMaster equipment-grant generated types are current."
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

function Invoke-SwordMasterPlayerEquipmentGrantComposedApiAcceptance {
    param(
        [Parameter(Mandatory)][string]$IntegrationProject,
        [Parameter(Mandatory)][string]$ApiUrl,
        [Parameter(Mandatory)][string]$PlayerToken,
        [Parameter(Mandatory)][string]$ConfigurationVersionId,
        [Parameter(Mandatory)][string]$EvidencePlayerId,
        [Parameter(Mandatory)][string]$EvidenceCharacterId,
        [Parameter(Mandatory)][string]$RollbackPlayerId,
        [Parameter(Mandatory)][string]$RollbackCharacterId,
        [Parameter(Mandatory)][string]$GrantCorrelationId,
        [Parameter(Mandatory)][string]$RollbackCorrelationId
    )

    $acceptanceEnvironment = [ordered]@{
        SWORDMASTER_ACCEPTANCE_COMPOSED_API_URL = $ApiUrl
        SWORDMASTER_ACCEPTANCE_COMPOSED_PLAYER_TOKEN = $PlayerToken
        SWORDMASTER_ACCEPTANCE_COMPOSED_CONFIGURATION_VERSION_ID = $ConfigurationVersionId
        SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_PLAYER_ID = $EvidencePlayerId
        SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_CHARACTER_ID = $EvidenceCharacterId
        SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_PLAYER_ID = $RollbackPlayerId
        SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CHARACTER_ID = $RollbackCharacterId
        SWORDMASTER_ACCEPTANCE_COMPOSED_GRANT_CORRELATION_ID = $GrantCorrelationId
        SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CORRELATION_ID = $RollbackCorrelationId
    }
    Assert-StringSet `
        -Expected @(
            "SWORDMASTER_ACCEPTANCE_COMPOSED_API_URL",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_PLAYER_TOKEN",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_CONFIGURATION_VERSION_ID",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_PLAYER_ID",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_CHARACTER_ID",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_PLAYER_ID",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CHARACTER_ID",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_GRANT_CORRELATION_ID",
            "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CORRELATION_ID"
        ) `
        -Actual @($acceptanceEnvironment.Keys) `
        -Message "SwordMaster composed API acceptance environment changed unexpectedly."

    foreach ($entry in $acceptanceEnvironment.GetEnumerator()) {
        Set-Item -Path "Env:$($entry.Key)" -Value ([string]$entry.Value)
    }

    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath $IntegrationProject `
        -Filter "FullyQualifiedName~PlayerEquipmentGrantComposedApiAcceptanceTests" `
        -ResultName "swordmaster-player-equipment-grant-composed-api.trx" `
        -ExpectedTestCount 3
    Write-Output "SwordMaster PlayerEquipmentGrant composed API acceptance passed."
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

function Assert-SwordMasterPlayerEquipmentGrantComposedApiAcceptanceDatabaseState {
    param(
        [Parameter(Mandatory)][object[]]$ComposeArguments,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][Guid]$ConfigurationVersionId,
        [Parameter(Mandatory)][Guid]$EvidencePlayerId,
        [Parameter(Mandatory)][Guid]$RollbackPlayerId,
        [Parameter(Mandatory)][string]$GrantCorrelationId,
        [Parameter(Mandatory)][string]$RollbackCorrelationId
    )

    Assert-Condition `
        ($env:SWORDMASTER_TEST_DISPOSABLE_DATABASE_MARKER -eq
            "swordmaster-test-disposable-v1") `
        "SwordMaster composed API state inspection requires the disposable database marker."
    Assert-Condition `
        ($ProjectName -match "^swordmaster-test-[a-f0-9]{12}$") `
        "SwordMaster composed API state inspection requires a marker-guarded Docker project."
    foreach ($correlationId in @($GrantCorrelationId, $RollbackCorrelationId)) {
        Assert-Condition `
            ($correlationId -match "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$") `
            "SwordMaster composed API state inspection requires a safe correlation identifier."
    }

    $configurationVersion = $ConfigurationVersionId.ToString("D")
    $evidencePlayer = $EvidencePlayerId.ToString("D")
    $rollbackPlayer = $RollbackPlayerId.ToString("D")
    $permissionSql = @(
        "SELECT CONCAT('administrator-seed:', CASE WHEN (SELECT COUNT(*) FROM admin_administrators AS administrator INNER JOIN admin_role_memberships AS membership ON membership.AdministratorId = administrator.Id INNER JOIN admin_roles AS role ON role.Id = membership.RoleId WHERE administrator.NormalizedUserName = 'RUNTIME-ADMIN' AND administrator.IsActive = 1 AND role.NormalizedName = 'ADMINISTRATORS') = 1 THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('permission:', CASE WHEN (SELECT COUNT(*) FROM admin_role_permissions AS permission INNER JOIN admin_roles AS role ON role.Id = permission.RoleId WHERE role.NormalizedName = 'ADMINISTRATORS' AND permission.Name = 'SwordMaster.PlayerAssets.EquipmentGrants.Manage') = 1 AND (SELECT COUNT(*) FROM admin_audit_records AS audit INNER JOIN admin_roles AS role ON role.Id = audit.RoleId WHERE audit.Action = 'PermissionGranted' AND audit.Succeeded = 1 AND role.NormalizedName = 'ADMINISTRATORS' AND audit.PermissionName = 'SwordMaster.PlayerAssets.EquipmentGrants.Manage') = 1 THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('active-configuration:', CASE WHEN (SELECT ActiveVersionId FROM game_config_environments WHERE EnvironmentName = 'development' AND ConfigurationSetKey = 'swordmaster') = '$configurationVersion' THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('published-configuration:', CASE WHEN (SELECT COUNT(*) FROM game_config_published_versions WHERE Id = '$configurationVersion' AND ConfigurationSetKey = 'swordmaster' AND SchemaVersion = 'swordmaster.player-assets.v1' AND PublishedAtUtc IS NOT NULL) = 1 THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('novagame-isolation:', CASE WHEN (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'novagame_test' AND LEFT(TABLE_NAME, 3) = 'sm_') = 0 THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('swordmaster-isolation:', CASE WHEN (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'swordmaster_test' AND (LEFT(TABLE_NAME, 6) = 'admin_' OR LEFT(TABLE_NAME, 12) = 'game_config_')) = 0 THEN 'ok' ELSE 'failed' END);"
    ) -join " "
    $swordMasterSql = @(
        "SELECT CONCAT('evidence:', CASE WHEN (SELECT Revision FROM sm_player_assets_profiles WHERE PlayerId = '$evidencePlayer') = 2 AND (SELECT COUNT(*) FROM sm_player_equipment_grants WHERE PlayerId = '$evidencePlayer' AND Status = 1 AND ConfiguredItemId = 'training-sword' AND ConfigurationVersionId = '$configurationVersion' AND GrantedByAdministratorId = (SELECT Id FROM novagame_test.admin_administrators WHERE NormalizedUserName = 'RUNTIME-ADMIN' AND IsActive = 1) AND GrantedAtUtc IS NOT NULL AND GrantReason = 'M306 grant evidence' AND REGEXP_LIKE(GrantIdempotencyKey, '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$', 'c') AND REGEXP_LIKE(GrantRequestFingerprint, '^[A-F0-9]{64}$', 'c') AND GrantResultingRevision = 2 AND GrantIpAddress IS NOT NULL AND CHAR_LENGTH(TRIM(GrantIpAddress)) > 0 AND GrantUserAgent = 'NovaGame-M306-T02/1.0' AND GrantCorrelationId = '$GrantCorrelationId' AND RolledBackByAdministratorId IS NULL AND RolledBackAtUtc IS NULL AND RollbackReason IS NULL AND RollbackIdempotencyKey IS NULL AND RollbackRequestFingerprint IS NULL AND RollbackResultingRevision IS NULL AND RollbackIpAddress IS NULL AND RollbackUserAgent IS NULL AND RollbackCorrelationId IS NULL) = 1 AND (SELECT COUNT(*) FROM sm_owned_equipment_items AS item INNER JOIN sm_player_equipment_grants AS equipmentGrant ON equipmentGrant.PlayerId = item.PlayerId AND equipmentGrant.OwnedItemId = item.OwnedItemId WHERE equipmentGrant.PlayerId = '$evidencePlayer' AND equipmentGrant.Status = 1) = 1 AND (SELECT COUNT(*) FROM sm_equipment_assignments WHERE PlayerId = '$evidencePlayer') = 0 AND (SELECT COUNT(*) FROM sm_player_asset_command_receipts WHERE PlayerId = '$evidencePlayer') = 0 THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('rollback:', CASE WHEN (SELECT Revision FROM sm_player_assets_profiles WHERE PlayerId = '$rollbackPlayer') = 5 AND (SELECT COUNT(*) FROM sm_player_equipment_grants WHERE PlayerId = '$rollbackPlayer' AND Status = 2 AND ConfiguredItemId = 'training-sword' AND ConfigurationVersionId = '$configurationVersion' AND GrantedByAdministratorId = (SELECT Id FROM novagame_test.admin_administrators WHERE NormalizedUserName = 'RUNTIME-ADMIN' AND IsActive = 1) AND GrantedAtUtc IS NOT NULL AND GrantReason = 'M306 rollback grant' AND REGEXP_LIKE(GrantIdempotencyKey, '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$', 'c') AND REGEXP_LIKE(GrantRequestFingerprint, '^[A-F0-9]{64}$', 'c') AND GrantResultingRevision = 2 AND GrantIpAddress IS NOT NULL AND CHAR_LENGTH(TRIM(GrantIpAddress)) > 0 AND GrantUserAgent = 'NovaGame-M306-T02/1.0' AND GrantCorrelationId = '$GrantCorrelationId' AND RolledBackByAdministratorId = (SELECT Id FROM novagame_test.admin_administrators WHERE NormalizedUserName = 'RUNTIME-ADMIN' AND IsActive = 1) AND RolledBackAtUtc IS NOT NULL AND RolledBackAtUtc >= GrantedAtUtc AND RollbackReason = 'M306 rollback evidence' AND REGEXP_LIKE(RollbackIdempotencyKey, '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$', 'c') AND REGEXP_LIKE(RollbackRequestFingerprint, '^[A-F0-9]{64}$', 'c') AND RollbackResultingRevision = 5 AND RollbackIpAddress IS NOT NULL AND CHAR_LENGTH(TRIM(RollbackIpAddress)) > 0 AND RollbackUserAgent = 'NovaGame-M306-T02/1.0' AND RollbackCorrelationId = '$RollbackCorrelationId') = 1 AND (SELECT COUNT(*) FROM sm_owned_equipment_items AS item INNER JOIN sm_player_equipment_grants AS equipmentGrant ON equipmentGrant.PlayerId = item.PlayerId AND equipmentGrant.OwnedItemId = item.OwnedItemId WHERE equipmentGrant.PlayerId = '$rollbackPlayer' AND equipmentGrant.Status = 2) = 0 AND (SELECT COUNT(*) FROM sm_equipment_assignments WHERE PlayerId = '$rollbackPlayer') = 0 AND (SELECT COUNT(*) FROM sm_player_asset_command_receipts WHERE PlayerId = '$rollbackPlayer') = 2 AND COALESCE((SELECT GROUP_CONCAT(ResultingRevision ORDER BY ResultingRevision SEPARATOR ',') FROM sm_player_asset_command_receipts WHERE PlayerId = '$rollbackPlayer'), '') = '3,4' THEN 'ok' ELSE 'failed' END);",
        "SELECT CONCAT('grants:', CASE WHEN (SELECT COUNT(*) FROM sm_player_equipment_grants) = 2 THEN 'ok' ELSE 'failed' END);"
    ) -join " "
    $permissionCommand = `
        'MYSQL_PWD="$MYSQL_PASSWORD" mysql --protocol=TCP --host=127.0.0.1 --user="$MYSQL_USER" --database=novagame_test --batch --skip-column-names --execute="' + `
        $permissionSql + '"'
    $swordMasterCommand = `
        'MYSQL_PWD="$MYSQL_PASSWORD" mysql --protocol=TCP --host=127.0.0.1 --user="$MYSQL_USER" --database=swordmaster_test --batch --skip-column-names --execute="' + `
        $swordMasterSql + '"'
    $permissionOutput = @(
        Invoke-CheckedCommandCapture "docker" `
            ($ComposeArguments + @(
                "exec", "--no-TTY", "mysql", "sh", "-c", $permissionCommand)) |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $swordMasterOutput = @(
        Invoke-CheckedCommandCapture "docker" `
            ($ComposeArguments + @(
                "exec", "--no-TTY", "mysql", "sh", "-c", $swordMasterCommand)) |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Condition `
        (($permissionOutput -join ',') -eq
            "administrator-seed:ok,permission:ok,active-configuration:ok,published-configuration:ok,novagame-isolation:ok,swordmaster-isolation:ok") `
        "SwordMaster composed API NovaGame database state is inconsistent."
    Assert-Condition `
        (($swordMasterOutput -join ',') -eq
            "evidence:ok,rollback:ok,grants:ok") `
        "SwordMaster composed API game database state is inconsistent."
    Write-Output "SwordMaster PlayerEquipmentGrant composed API database state passed."
}

function Assert-SwordMasterPlayerEquipmentGrantComposedApiAcceptanceLogs {
    param(
        [Parameter(Mandatory)][string]$StandardOutputPath,
        [Parameter(Mandatory)][string]$StandardErrorPath,
        [Parameter(Mandatory)][string]$GrantCorrelationId,
        [Parameter(Mandatory)][string]$RollbackCorrelationId,
        [Parameter(Mandatory)][string[]]$SensitiveRuntimeValues
    )

    $logContents = [System.Collections.Generic.List[string]]::new()
    foreach ($logPath in @($StandardOutputPath, $StandardErrorPath)) {
        $logFileName = Split-Path -Leaf $logPath
        Assert-Condition `
            (Test-Path -LiteralPath $logPath -PathType Leaf) `
            "SwordMaster acceptance process log '$logFileName' was not created."
        $logStream = [System.IO.File]::Open(
            $logPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        try {
            $logReader = [System.IO.StreamReader]::new($logStream)
            try {
                $content = $logReader.ReadToEnd()
            }
            finally {
                $logReader.Dispose()
            }
        }
        finally {
            $logStream.Dispose()
        }
        $null = $logContents.Add($content)
        foreach ($sensitiveValue in $SensitiveRuntimeValues) {
            if (-not [string]::IsNullOrWhiteSpace($sensitiveValue) -and
                $content.Contains($sensitiveValue, [StringComparison]::Ordinal)) {
                throw "SwordMaster acceptance log '$logFileName' violated the exact-sensitive-value rule."
            }
        }
        if ([regex]::IsMatch(
                $content,
                '(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+(?![A-Za-z0-9_-])')) {
            throw "SwordMaster acceptance log '$logFileName' violated the JWT-shape rule."
        }
        if ([regex]::IsMatch(
                $content,
                '(?im)"?\b(?:password|connection\s*string|access\s*token|refresh\s*token|signing\s*key)\b"?\s*(?:=|:)\s*"?\S+')) {
            throw "SwordMaster acceptance log '$logFileName' violated the secret-field rule."
        }
        if ([regex]::IsMatch(
                $content,
                '(?im)\bAuthorization\s*:\s*Bearer\s+\S+')) {
            throw "SwordMaster acceptance log '$logFileName' violated the authorization-header rule."
        }
    }

    $combinedLogContent = $logContents -join "`n"
    $grantSuccessPattern =
        '(?m)Player equipment grant operation completed\.\s+CorrelationId=' +
        [regex]::Escape($GrantCorrelationId) +
        '\s+[^\r\n]*\bCommandType=Grant\s+ResultCode=Success\s+Revision=2\.'
    $rollbackSuccessPattern =
        '(?m)Player equipment grant operation completed\.\s+CorrelationId=' +
        [regex]::Escape($RollbackCorrelationId) +
        '\s+[^\r\n]*\bCommandType=Rollback\s+ResultCode=Success\s+Revision=5\.'
    Assert-Condition `
        ([regex]::Matches($combinedLogContent, $grantSuccessPattern).Count -ge 1) `
        "SwordMaster acceptance process logs are missing the composed grant success record."
    Assert-Condition `
        ([regex]::Matches($combinedLogContent, $rollbackSuccessPattern).Count -ge 1) `
        "SwordMaster acceptance process logs are missing the composed rollback success record."

    Write-Output "SwordMaster PlayerEquipmentGrant composed API process logs passed."
}

function Test-SwordMasterDatabase {
    param(
        [Parameter(Mandatory)]$Toolchain,
        [Parameter(Mandatory)]$AdminToolchain
    )

    $sampleServerRoot = "samples/SwordMaster/server"
    $integrationProject = `
        "$sampleServerRoot/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj"

    Invoke-CheckedCommand "docker" @("compose", "version")
    Invoke-CheckedCommand "docker" @("info", "--format", "{{.ServerVersion}}")
    $browserChannel = Resolve-E2EBrowserChannel

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
    $viteProcess = $null
    $playwrightArtifactRoot = $null
    $viteStandardOutputPath = $null
    $viteStandardErrorPath = $null
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
        "NovaGame__AdminAccess__LoginRateLimit__PermitLimit",
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
        "SwordMaster__PlayerAssets__DevelopmentSeed__BaseHealth",
        "SWORDMASTER_ADMIN_PORT",
        "SWORDMASTER_API_URL",
        "VITE_HTTPS",
        "VITE_NITRO_MOCK",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_API_URL",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_PLAYER_TOKEN",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_CONFIGURATION_VERSION_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_PLAYER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_EVIDENCE_CHARACTER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_PLAYER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CHARACTER_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_GRANT_CORRELATION_ID",
        "SWORDMASTER_ACCEPTANCE_COMPOSED_ROLLBACK_CORRELATION_ID"
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
        # The real acceptance flow performs six deliberate loopback logins in one API process.
        $env:NovaGame__AdminAccess__LoginRateLimit__PermitLimit = "6"
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
        $equipmentGrantPermissionSeedIndex = $firstMigratorLog.IndexOf(
            "Running SwordMaster player-equipment-grant permission seed.",
            [StringComparison]::Ordinal)
        Assert-Condition `
            ($novaGameMigrationIndex -ge 0 -and
                $swordMasterMigrationIndex -gt $novaGameMigrationIndex -and
                $administratorSeedIndex -gt $swordMasterMigrationIndex -and
                $equipmentGrantPermissionSeedIndex -gt $administratorSeedIndex -and
                $playerAssetsSeedIndex -gt $equipmentGrantPermissionSeedIndex) `
            "Combined migrator output did not preserve NovaGame, SwordMaster, administrator-seed, permission-seed, and validation-profile order."
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
        $playwrightArtifactRoot = Join-Path $logRoot "playwright"
        New-Item -ItemType Directory -Path $playwrightArtifactRoot -Force | Out-Null
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
        $composedEvidencePlayerId = [Guid]"44444444-4444-4444-4444-444444444444"
        $composedEvidenceCharacterId = [Guid]"55555555-5555-5555-5555-555555555555"
        $composedRollbackPlayerId = [Guid]"66666666-6666-6666-6666-666666666666"
        $composedRollbackCharacterId = [Guid]"77777777-7777-7777-7777-777777777777"
        $composedGrantCorrelationId = "m306-grant-$([Guid]::NewGuid().ToString('N'))"
        $composedRollbackCorrelationId = "m306-rollback-$([Guid]::NewGuid().ToString('N'))"
        $composedPlayerToken = New-SwordMasterPlayerAcceptanceToken `
            -Issuer $playerIssuer `
            -Audience $playerAudience `
            -SigningKey $playerSigningKey `
            -PlayerId $composedRollbackPlayerId
        Invoke-SwordMasterPlayerEquipmentGrantComposedApiAcceptance `
            -IntegrationProject $integrationProject `
            -ApiUrl $apiUrl `
            -PlayerToken $composedPlayerToken `
            -ConfigurationVersionId $configurationVersionId `
            -EvidencePlayerId $composedEvidencePlayerId.ToString("D") `
            -EvidenceCharacterId $composedEvidenceCharacterId.ToString("D") `
            -RollbackPlayerId $composedRollbackPlayerId.ToString("D") `
            -RollbackCharacterId $composedRollbackCharacterId.ToString("D") `
            -GrantCorrelationId $composedGrantCorrelationId `
            -RollbackCorrelationId $composedRollbackCorrelationId
        Assert-SwordMasterPlayerEquipmentGrantComposedApiAcceptanceDatabaseState `
            -ComposeArguments $composeArguments `
            -ProjectName $projectName `
            -ConfigurationVersionId ([Guid]$configurationVersionId) `
            -EvidencePlayerId $composedEvidencePlayerId `
            -RollbackPlayerId $composedRollbackPlayerId `
            -GrantCorrelationId $composedGrantCorrelationId `
            -RollbackCorrelationId $composedRollbackCorrelationId
        $legacyPlayerToken = New-SwordMasterPlayerAcceptanceToken `
            -Issuer $playerIssuer `
            -Audience $playerAudience `
            -SigningKey $playerSigningKey `
            -PlayerId ([Guid]"11111111-1111-1111-1111-111111111111")
        Invoke-SwordMasterPlayerAssetsRealApiAcceptance `
            -Toolchain $Toolchain `
            -ApiUrl $apiUrl `
            -PlayerToken $legacyPlayerToken `
            -ConfigurationVersionId $configurationVersionId
        Assert-SwordMasterPlayerAssetsAcceptanceDatabaseState `
            -ComposeArguments $composeArguments
        Assert-SwordMasterPlayerEquipmentGrantComposedApiAcceptanceLogs `
            -StandardOutputPath $apiStandardOutputPath `
            -StandardErrorPath $apiStandardErrorPath `
            -GrantCorrelationId $composedGrantCorrelationId `
            -RollbackCorrelationId $composedRollbackCorrelationId `
            -SensitiveRuntimeValues @(
                $administratorPassword,
                $env:NovaGame__AdminAccess__Jwt__SigningKey,
                $playerSigningKey,
                $novaGameConnectionString,
                $swordMasterConnectionString,
                $legacyPlayerToken,
                $composedPlayerToken,
                "M306 grant evidence",
                "M306 rollback grant",
                "M306 rollback evidence",
                "NovaGame-M306-T02/1.0"
            )
        $legacyPlayerToken = $null
        $composedPlayerToken = $null

        $browserPlayerId = [Guid]"88888888-1111-1111-1111-111111111111"
        $browserCharacterId = [Guid]"99999999-1111-1111-1111-111111111111"
        $browserSeedOwnedItemId = [Guid]"aaaaaaaa-1111-1111-1111-111111111111"
        $browserResetSql = @(
            "DELETE FROM sm_player_equipment_grants WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "DELETE FROM sm_player_asset_command_receipts WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "DELETE FROM sm_equipment_assignments WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "DELETE FROM sm_owned_equipment_items WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "DELETE FROM sm_player_character_base_attributes WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "DELETE FROM sm_player_characters WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "DELETE FROM sm_player_assets_profiles WHERE PlayerId = '$($browserPlayerId.ToString("D"))';",
            "INSERT INTO sm_player_assets_profiles (PlayerId, Revision) VALUES ('$($browserPlayerId.ToString("D"))', 1);",
            "INSERT INTO sm_player_characters (PlayerId, CharacterId) VALUES ('$($browserPlayerId.ToString("D"))', '$($browserCharacterId.ToString("D"))');",
            "INSERT INTO sm_player_character_base_attributes (PlayerId, StatCode, StatValue) VALUES ('$($browserPlayerId.ToString("D"))', 'attack', 10), ('$($browserPlayerId.ToString("D"))', 'health', 100);",
            "INSERT INTO sm_owned_equipment_items (PlayerId, OwnedItemId, ConfiguredItemId) VALUES ('$($browserPlayerId.ToString("D"))', '$($browserSeedOwnedItemId.ToString("D"))', 'training-sword');"
        ) -join " "
        Assert-Condition `
            ($projectName -match '^swordmaster-test-[a-f0-9]{12}$') `
            "Refusing browser fixture reset outside a disposable SwordMaster project."
        $browserResetCommand = `
            'MYSQL_PWD="$MYSQL_PASSWORD" mysql --protocol=TCP --host=127.0.0.1 --user="$MYSQL_USER" --database=swordmaster_test --execute="' + `
            $browserResetSql + '"'
        Invoke-CheckedCommand "docker" ($composeArguments + @(
            "exec", "--no-TTY", "mysql", "sh", "-c", $browserResetCommand
        ))

        $adminPort = Get-AvailableLoopbackPort
        while ($adminPort -eq $apiPort) {
            $adminPort = Get-AvailableLoopbackPort
        }
        $adminUrl = "https://127.0.0.1:$adminPort"
        $env:SWORDMASTER_ADMIN_PORT = [string]$adminPort
        $env:SWORDMASTER_API_URL = $apiUrl
        $env:VITE_HTTPS = "true"
        $env:VITE_NITRO_MOCK = "false"
        $viteStandardOutputPath = Join-Path $logRoot "vite.stdout.log"
        $viteStandardErrorPath = Join-Path $logRoot "vite.stderr.log"
        $viteEnvironment = @{
            "PATH" = [Environment]::GetEnvironmentVariable("PATH", "Process")
        }
        foreach ($name in @(
            "APPDATA", "ComSpec", "HOMEDRIVE", "HOMEPATH", "LOCALAPPDATA",
            "NUMBER_OF_PROCESSORS", "OS", "PATHEXT", "PROCESSOR_ARCHITECTURE",
            "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "SystemDrive",
            "SystemRoot", "TEMP", "TMP", "USERPROFILE", "WINDIR"
        )) {
            $value = [Environment]::GetEnvironmentVariable($name, "Process")
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $viteEnvironment[$name] = $value
            }
        }
        $adminNodeDirectory = Split-Path -Parent ([string]$AdminToolchain.nodePath)
        $viteEnvironment["PATH"] = `
            "$adminNodeDirectory$([System.IO.Path]::PathSeparator)$($viteEnvironment["PATH"])"
        $viteEnvironment["CI"] = "true"
        $viteEnvironment["NO_COLOR"] = "1"
        $viteEnvironment["pnpm_config_store_dir"] = [string]$AdminToolchain.storePath
        $viteEnvironment["SWORDMASTER_ADMIN_PORT"] = [string]$adminPort
        $viteEnvironment["SWORDMASTER_API_URL"] = $apiUrl
        $viteEnvironment["VITE_HTTPS"] = "true"
        $viteEnvironment["VITE_NITRO_MOCK"] = "false"
        $viteProcess = Start-Process `
            -FilePath ([string]$AdminToolchain.nodePath) `
            -ArgumentList @(
                [string]$AdminToolchain.pnpmBootstrapPath,
                "--filter", "@swordmaster/admin-web-ele", "run", "dev"
            ) `
            -WorkingDirectory ([string]$AdminToolchain.adminRoot) `
            -WindowStyle Hidden `
            -RedirectStandardOutput $viteStandardOutputPath `
            -RedirectStandardError $viteStandardErrorPath `
            -Environment $viteEnvironment `
            -PassThru
        Wait-HttpsEndpoint `
            -Uri ([Uri]$adminUrl) `
            -Process $viteProcess `
            -StandardOutputPath $viteStandardOutputPath `
            -StandardErrorPath $viteStandardErrorPath

        $browserPlayerToken = New-SwordMasterPlayerAcceptanceToken `
            -Issuer $playerIssuer `
            -Audience $playerAudience `
            -SigningKey $playerSigningKey `
            -PlayerId $browserPlayerId
        Invoke-SwordMasterPlayerEquipmentGrantPlaywrightAcceptance `
            -Toolchain $AdminToolchain `
            -BaseUrl $adminUrl `
            -ApiUrl $apiUrl `
            -AdministratorUserName $administratorUserName `
            -AdministratorPassword $administratorPassword `
            -PlayerId $browserPlayerId.ToString("D") `
            -CharacterId $browserCharacterId.ToString("D") `
            -PlayerToken $browserPlayerToken `
            -ConfiguredItemId "training-sword" `
            -NovaGameConnectionString $novaGameConnectionString `
            -SwordMasterConnectionString $swordMasterConnectionString `
            -DisposableDatabaseMarker "swordmaster-test-disposable-v1" `
            -BrowserChannel $browserChannel `
            -ArtifactRoot $playwrightArtifactRoot
        $browserPlayerToken = $null

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
        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~PlayerEquipmentGrantPersistenceTests" `
            -ResultName "swordmaster-mysql-player-equipment-grants.trx"
        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~PlayerEquipmentGrantMySqlApplicationTests" `
            -ResultName "swordmaster-mysql-player-equipment-grant-application.trx"
        Invoke-DotNetFilteredTestWithExecutionCount `
            -ProjectPath $integrationProject `
            -Filter "FullyQualifiedName~PlayerEquipmentGrantPermissionSeedTests" `
            -ResultName "swordmaster-mysql-player-equipment-grant-permission-seed.trx"
    }
    catch {
        $operationError = $_
    }
    finally {
        try {
            Stop-ProcessTree -Process $viteProcess
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to stop the SwordMaster acceptance Vite process.")
        }

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
            if (-not [string]::IsNullOrWhiteSpace([string]$playwrightArtifactRoot) -and
                (Test-Path -LiteralPath $playwrightArtifactRoot -PathType Container)) {
                $resolvedLogRoot = [System.IO.Path]::GetFullPath($logRoot).TrimEnd('\') + '\'
                $resolvedPlaywrightArtifactRoot = `
                    [System.IO.Path]::GetFullPath($playwrightArtifactRoot)
                if (-not $resolvedPlaywrightArtifactRoot.StartsWith(
                        $resolvedLogRoot,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $null = $cleanupErrors.Add(
                        "Refusing to remove Playwright artifacts outside '$resolvedLogRoot'.")
                }
                else {
                    Remove-Item -LiteralPath $resolvedPlaywrightArtifactRoot -Recurse -Force
                }
            }
        }
        catch {
            $null = $cleanupErrors.Add(
                "Failed to remove SwordMaster Playwright acceptance artifacts.")
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
        "$sampleRoot/admin/contracts/player-equipment-grants.openapi.json",
        "$sampleRoot/admin/src/generated/player-equipment-grants/types.gen.ts",
        "$sampleRoot/src/player-assets-cocos-component.ts",
        "$sampleRoot/src/player-assets-http-transport.ts",
        "$sampleRoot/src/player-assets-interaction-model.ts",
        "$sampleRoot/src/generated/player-assets/types.gen.ts",
        "$sampleRoot/tests/player-assets-cocos-component.test.ts",
        "$sampleRoot/tests/player-assets-http-transport.test.ts",
        "$sampleRoot/tests/player-assets-interaction-model.test.ts",
        "$sampleRoot/tests/player-assets-real-api.acceptance.ts",
        "scripts/update-swordmaster-player-assets-contract.ps1",
        "scripts/update-swordmaster-player-equipment-grant-contract.ps1",
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
    $allowedSwordMasterWorkspaceMetadata = [ordered]@{
        "admin/pnpm-lock.yaml" =
            '(?m)^[ \t]*\.\./samples/SwordMaster/admin:[ \t]*\r?$'
        "admin/pnpm-workspace.yaml" =
            '(?m)^[ \t]*-[ \t]+\.\./samples/SwordMaster/admin[ \t]*\r?$'
    }
    $samplePackageName = ([string]$samplePackage.name).Trim()
    foreach ($relativePath in $frameworkRuntimeFiles) {
        if ([System.IO.Path]::GetExtension($relativePath) -notin $frameworkTextExtensions) {
            continue
        }

        $content = Get-Content -LiteralPath $relativePath -Raw
        if ($allowedSwordMasterWorkspaceMetadata.Contains($relativePath)) {
            Assert-Condition `
                ([regex]::IsMatch(
                        $content,
                        [string]$allowedSwordMasterWorkspaceMetadata[$relativePath]) -and
                    [regex]::Matches(
                        $content,
                        'SwordMaster',
                        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
                    ).Count -eq 1) `
                "Admin workspace metadata '$relativePath' must contain exactly its one approved SwordMaster importer reference."
            continue
        }
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
        -Filter "FullyQualifiedName~PlayerEquipmentGrantApiTests" `
        -ResultName "swordmaster-api-player-equipment-grants.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" `
        -Filter "FullyQualifiedName~PlayerAssetsOpenApiContractTests" `
        -ResultName "swordmaster-player-assets-openapi-contract.trx"
    Invoke-DotNetFilteredTestWithExecutionCount `
        -ProjectPath "$sampleRoot/server/tests/SwordMaster.Integration.Tests/SwordMaster.Integration.Tests.csproj" `
        -Filter "FullyQualifiedName~PlayerEquipmentGrantOpenApiContractTests" `
        -ResultName "swordmaster-player-equipment-grant-openapi-contract.trx"
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
    Test-SwordMasterPlayerEquipmentGrantGeneratedTypes -Toolchain $toolchain
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

    $adminToolchain = Initialize-AdminToolchain
    $previousCi = $env:CI
    $env:CI = "true"
    try {
        foreach ($scriptName in @("format:check", "lint", "typecheck")) {
            Invoke-AdminPnpm `
                -Toolchain $adminToolchain `
                -ArgumentList @(
                    "--filter", "@swordmaster/admin-web-ele",
                    "run", $scriptName)
        }
        Invoke-SwordMasterAdminVitestWithExecutionCount `
            -Toolchain $adminToolchain `
            -ScriptName "test:auth" `
            -ExpectedTestCount 45 `
            -ResultName "swordmaster-admin-auth.json"
        Invoke-SwordMasterAdminVitestWithExecutionCount `
            -Toolchain $adminToolchain `
            -ScriptName "test:equipment-grant-transport" `
            -ExpectedTestCount 12 `
            -ResultName "swordmaster-admin-equipment-grant-transport.json"
        Invoke-SwordMasterAdminVitestWithExecutionCount `
            -Toolchain $adminToolchain `
            -ScriptName "test:equipment-grant-model" `
            -ExpectedTestCount 14 `
            -ResultName "swordmaster-admin-equipment-grant-model.json"
        Invoke-SwordMasterAdminVitestWithExecutionCount `
            -Toolchain $adminToolchain `
            -ScriptName "test:equipment-grant-view" `
            -ExpectedTestCount 10 `
            -ResultName "swordmaster-admin-equipment-grant-view.json"
        Invoke-AdminPnpm `
            -Toolchain $adminToolchain `
            -ArgumentList @(
                "--filter", "@swordmaster/admin-web-ele",
                "run", "build")
    }
    finally {
        $env:CI = $previousCi
    }

    Test-SwordMasterDatabase `
        -Toolchain $toolchain `
        -AdminToolchain $adminToolchain

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
    "AdminManagement" { Test-AdminManagement }
    "AdminManagementAdmin" { Test-AdminManagementAdmin }
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

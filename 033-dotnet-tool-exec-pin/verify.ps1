[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sampleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path (Join-Path $sampleRoot 'src') (Join-Path 'DemoTool' 'DemoTool.csproj')
$configPath = Join-Path $sampleRoot 'NuGet.Config'
$artifactsPath = Join-Path $sampleRoot 'artifacts'
$feedPath = Join-Path $artifactsPath 'packages'
$packageId = 'Sukhpinder.DevTo.ToolExec.Demo'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Actual,

        [Parameter(Mandatory = $true)]
        [string] $Expected
    )

    if ($Actual.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "Expected output to contain '$Expected', but got: $Actual"
    }
}

Push-Location $sampleRoot
try {
    $resolvedRoot = [IO.Path]::GetFullPath($sampleRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $resolvedArtifacts = [IO.Path]::GetFullPath($artifactsPath)
    $requiredPrefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar

    if (-not $resolvedArtifacts.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean artifacts outside the sample root: $resolvedArtifacts"
    }

    if (Test-Path -LiteralPath $resolvedArtifacts) {
        Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
    }

    New-Item -ItemType Directory -Path $feedPath -Force | Out-Null

    Invoke-DotNet -Arguments @(
        'restore', $projectPath,
        '--configfile', $configPath,
        '--nologo'
    )

    foreach ($version in @('1.0.0', '2.0.0')) {
        Invoke-DotNet -Arguments @(
            'pack', $projectPath,
            '--configuration', 'Release',
            '--no-restore',
            '--nologo',
            "-p:Version=$version",
            '--output', $feedPath
        )
    }

    $pinnedOutput = & dotnet tool exec `
        --configfile $configPath `
        --source $feedPath `
        "$packageId@1.0.0" `
        -- --label ci 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned invocation failed with exit code $LASTEXITCODE."
    }
    $pinnedText = $pinnedOutput -join [Environment]::NewLine
    Assert-Contains -Actual $pinnedText -Expected 'demo-tool version=1.0.0 label=ci'
    Write-Host 'PASS exact @1.0.0 pin selected version 1.0.0'

    $latestOutput = & dotnet tool exec `
        --configfile $configPath `
        --source $feedPath `
        $packageId `
        -- --label unpinned 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unpinned invocation failed with exit code $LASTEXITCODE."
    }
    $latestText = $latestOutput -join [Environment]::NewLine
    Assert-Contains -Actual $latestText -Expected 'demo-tool version=2.0.0 label=unpinned'
    Write-Host 'PASS unpinned invocation selected the newest available version'

    $failureOutput = & dotnet tool exec `
        --configfile $configPath `
        --source $feedPath `
        "$packageId@1.0.0" `
        -- --label gate --exit-code 23 2>&1
    $toolExitCode = $LASTEXITCODE
    if ($toolExitCode -ne 23) {
        throw "Expected tool exit code 23, but got $toolExitCode. Output: $($failureOutput -join ' ')"
    }
    Assert-Contains -Actual ($failureOutput -join [Environment]::NewLine) `
        -Expected 'demo-tool version=1.0.0 label=gate'
    Write-Host 'PASS dotnet tool exec propagated tool exit code 23'

    Write-Host '3/3 deterministic checks passed.'
}
finally {
    Pop-Location
}

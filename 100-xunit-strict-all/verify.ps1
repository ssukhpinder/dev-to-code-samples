[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$sampleRoot = $PSScriptRoot
$project = Join-Path $sampleRoot 'tests\StrictAll.Tests\StrictAll.Tests.csproj'

function Invoke-DotnetAndCapture {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $lines = @(& dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $lines -join [Environment]::NewLine
    }
}

function Assert-Summary {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)][int] $ExitCode,
        [Parameter(Mandatory)][int] $Failed,
        [Parameter(Mandatory)][int] $Succeeded
    )

    if ($Result.ExitCode -ne $ExitCode) {
        throw "Expected exit code $ExitCode, got $($Result.ExitCode).`n$($Result.Output)"
    }

    if ($Result.Output -notmatch "(?im)^\s*total:\s*1\s*$" -or
        $Result.Output -notmatch "(?im)^\s*failed:\s*$Failed\s*$" -or
        $Result.Output -notmatch "(?im)^\s*succeeded:\s*$Succeeded\s*$") {
        throw "Unexpected test summary.`n$($Result.Output)"
    }
}

Push-Location $sampleRoot
try {
    $legacy = Invoke-DotnetAndCapture -Arguments @(
        'run', '--project', $project,
        '--configuration', 'LegacyControl', '--no-restore'
    )
    Assert-Summary -Result $legacy -ExitCode 0 -Failed 0 -Succeeded 1
    Write-Output 'PASS: the two-argument overload reproduced the empty-result false pass'

    $strict = Invoke-DotnetAndCapture -Arguments @(
        'run', '--project', $project,
        '--configuration', 'StrictFailure', '--no-restore'
    )
    Assert-Summary -Result $strict -ExitCode 2 -Failed 1 -Succeeded 0
    if ($strict.Output -notmatch 'Assert\.All\(\) Failure: The collection was empty\.') {
        throw "Expected the strict empty-collection diagnostic.`n$($strict.Output)"
    }
    Write-Output 'PASS: throwIfEmpty rejected the same empty result'

    $valid = Invoke-DotnetAndCapture -Arguments @(
        'run', '--project', $project,
        '--configuration', 'Release', '--no-restore'
    )
    Assert-Summary -Result $valid -ExitCode 0 -Failed 0 -Succeeded 1
    Write-Output 'PASS: throwIfEmpty checked a nonempty valid result'
}
finally {
    Pop-Location
}

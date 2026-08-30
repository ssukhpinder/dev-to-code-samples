[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sampleRoot = $PSScriptRoot
$project = Join-Path $sampleRoot 'JsonMetadataConflicts.csproj'

function Invoke-TargetAndCapture {
    param([Parameter(Mandatory)][string] $Framework)

    $lines = @(
        & dotnet run --project $project --framework $Framework `
            --configuration Release --no-build --no-restore 2>&1 |
            ForEach-Object { $_.ToString().TrimEnd() }
    )
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "$Framework exited $exitCode.`n$($lines -join [Environment]::NewLine)"
    }

    return $lines
}

function Assert-ExactOutput {
    param(
        [Parameter(Mandatory)][string] $Framework,
        [Parameter(Mandatory)][string[]] $Actual,
        [Parameter(Mandatory)][string[]] $Expected
    )

    $actualText = $Actual -join "`n"
    $expectedText = $Expected -join "`n"
    if ($actualText -cne $expectedText) {
        throw "Unexpected $Framework output.`nExpected:`n$expectedText`nActual:`n$actualText"
    }
}

$net9Expected = @(
    'runtime-major=9'
    'broken-serialize=none'
    'broken-type-property-count=2'
    'broken-roundtrip=JsonException'
    'fixed-kind=created'
    'fixed-type=Created'
    'fixed-roundtrip=FixedCreatedEvent'
    'PASS: 7/7'
)

$net10Expected = @(
    'runtime-major=10'
    'broken-serialize=InvalidOperationException'
    'broken-type-property-count=0'
    'broken-roundtrip=not-run'
    'fixed-kind=created'
    'fixed-type=Created'
    'fixed-roundtrip=FixedCreatedEvent'
    'PASS: 7/7'
)

Push-Location $sampleRoot
try {
    $net9 = Invoke-TargetAndCapture -Framework 'net9.0'
    Assert-ExactOutput -Framework 'net9.0' -Actual $net9 -Expected $net9Expected
    Write-Output 'PASS: .NET 9 reproduced ambiguous metadata output'

    $net10 = Invoke-TargetAndCapture -Framework 'net10.0'
    Assert-ExactOutput -Framework 'net10.0' -Actual $net10 -Expected $net10Expected
    Write-Output 'PASS: .NET 10 rejected the conflicting contract before JSON emission'

    Write-Output 'PASS: the $kind repair round-tripped on both runtimes'
}
finally {
    Pop-Location
}

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sampleRoot = $PSScriptRoot
$project = Join-Path $sampleRoot 'GenericMathShiftMasking.csproj'

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
    'byte-left-8=0'
    'byte-left-9=0'
    'byte-unsigned-right-8=0'
    'ushort-left-16=0'
    'ushort-left-17=0'
    'int-left-32-control=1'
    'explicit-mask-byte-9=2'
    'explicit-reject-byte-8=ArgumentOutOfRangeException'
    'PASS: 8/8'
)

$net10Expected = @(
    'runtime-major=10'
    'byte-left-8=1'
    'byte-left-9=2'
    'byte-unsigned-right-8=128'
    'ushort-left-16=1'
    'ushort-left-17=2'
    'int-left-32-control=1'
    'explicit-mask-byte-9=2'
    'explicit-reject-byte-8=ArgumentOutOfRangeException'
    'PASS: 8/8'
)

Push-Location $sampleRoot
try {
    $net9 = Invoke-TargetAndCapture -Framework 'net9.0'
    Assert-ExactOutput -Framework 'net9.0' -Actual $net9 -Expected $net9Expected
    Write-Output 'PASS: .NET 9 reproduced the previous small-integer overshift behavior'

    $net10 = Invoke-TargetAndCapture -Framework 'net10.0'
    Assert-ExactOutput -Framework 'net10.0' -Actual $net10 -Expected $net10Expected
    Write-Output 'PASS: .NET 10 masked small-integer shift counts consistently'

    Write-Output 'PASS: explicit reject and modulo policies stayed runtime-independent'
}
finally {
    Pop-Location
}

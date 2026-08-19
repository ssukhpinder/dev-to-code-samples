[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sampleRoot = $PSScriptRoot
$solution = Join-Path $sampleRoot 'MixedTesting.slnx'

function Invoke-DotnetAndCapture {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $lines = @(& dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $lines -join [Environment]::NewLine
    }
}

Push-Location $sampleRoot
try {
    $unscoped = Invoke-DotnetAndCapture -Arguments @(
        'test', '--solution', $solution,
        '--configuration', 'Release', '--no-build', '--no-restore',
        '--filter-trait', 'Category=Integration'
    )

    if ($unscoped.ExitCode -ne 5) {
        throw "Expected unscoped filter to return exit code 5, got $($unscoped.ExitCode).`n$($unscoped.Output)"
    }
    Write-Output 'PASS: unscoped xUnit filter returned exit code 5'

    $routed = Invoke-DotnetAndCapture -Arguments @(
        'test', '--solution', $solution,
        '--configuration', 'Release', '--no-build', '--no-restore',
        '-p:MSTestSpecificArgs=--filter TestCategory=Integration',
        '-p:XUnitSpecificArgs=--filter-trait Category=Integration'
    )

    if ($routed.ExitCode -ne 0) {
        throw "Expected routed filters to return exit code 0, got $($routed.ExitCode).`n$($routed.Output)"
    }
    Write-Output 'PASS: routed filters returned exit code 0'

    if ($routed.Output -notmatch '(?im)^\s*total:\s*2\s*$') {
        throw "Expected exactly two selected tests.`n$($routed.Output)"
    }
    Write-Output 'PASS: exactly two integration tests ran'
}
finally {
    Pop-Location
}

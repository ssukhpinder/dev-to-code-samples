[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:TESTINGPLATFORM_NOBANNER = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

$sampleRoot = $PSScriptRoot
$project = Join-Path $sampleRoot 'tests\Inventory.Tests\Inventory.Tests.csproj'
$manifestPath = Join-Path $sampleRoot 'expected-tests.json'

function Invoke-DotnetAndCapture {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $lines = @(& dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $lines -join [Environment]::NewLine
    }
}

function Get-TestIdentity {
    param([Parameter(Mandatory)] $Test)

    $traits = @(
        $Test.traits |
            ForEach-Object { "$($_.key)=$($_.value)" } |
            Sort-Object
    )

    "$($Test.type.namespace).$($Test.type.typeName).$($Test.type.methodName)|$($traits -join ';')"
}

Push-Location $sampleRoot
try {
    $result = Invoke-DotnetAndCapture -Arguments @(
        'run',
        '--project', $project,
        '--configuration', 'Release',
        '--no-build',
        '--no-restore',
        '--',
        '--list-tests', 'json',
        '--no-banner'
    )

    if ($result.ExitCode -ne 0) {
        throw "Discovery command failed with exit code $($result.ExitCode).$([Environment]::NewLine)$($result.Output)"
    }

    try {
        $actual = $result.Output | ConvertFrom-Json
    }
    catch {
        throw "Discovery output was not valid JSON.$([Environment]::NewLine)$($result.Output)"
    }

    $expected = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($actual.schemaVersion -ne 1 -or $expected.schemaVersion -ne 1) {
        throw "Expected discovery schema version 1, got $($actual.schemaVersion)."
    }

    $actualKeys = @($actual.tests | ForEach-Object { Get-TestIdentity $_ } | Sort-Object)
    $expectedKeys = @($expected.tests | ForEach-Object { Get-TestIdentity $_ } | Sort-Object)
    $difference = @(Compare-Object -ReferenceObject $expectedKeys -DifferenceObject $actualKeys)

    if ($actualKeys.Count -ne $expectedKeys.Count -or $difference.Count -ne 0) {
        $details = $difference | Out-String
        throw "Discovered test inventory differs from expected-tests.json.$([Environment]::NewLine)$details"
    }

    Write-Output 'PASS: parsed Microsoft.Testing.Platform discovery schema version 1'
    Write-Output "PASS: exact inventory matched $($actualKeys.Count) test methods and traits"
}
finally {
    Pop-Location
}

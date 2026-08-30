[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:TESTINGPLATFORM_NOBANNER = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

$sampleRoot = $PSScriptRoot
$project = Join-Path $sampleRoot 'tests\OrderContract.Tests\OrderContract.Tests.csproj'
$source = Join-Path $sampleRoot 'tests\OrderContract.Tests\DeepEquivalenceTests.cs'
$expectedTestCount = 6

$declaredTestCount = @(Select-String -LiteralPath $source -Pattern '^\s*\[TestMethod\]\s*$').Count
if ($declaredTestCount -ne $expectedTestCount) {
    throw "Expected $expectedTestCount test methods, found $declaredTestCount."
}

Push-Location $sampleRoot
try {
    & dotnet test --project $project --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Deep-equivalence tests failed with exit code $LASTEXITCODE."
    }

    Write-Output "PASS: $expectedTestCount MSTest deep-equivalence contract tests"
}
finally {
    Pop-Location
}

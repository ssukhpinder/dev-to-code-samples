[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$sampleRoot = $PSScriptRoot
$project = Join-Path $sampleRoot "MtpCrashTrx.csproj"
$resultsRoot = Join-Path $sampleRoot "TestResults"
$normalResults = Join-Path $resultsRoot "normal"
$crashResults = Join-Path $resultsRoot "crash"
$crashLog = Join-Path $crashResults "crash.console.log"
$crashVariable = "DEMO_CRASH"

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-TrxResults {
    param([Parameter(Mandatory)][string]$Path)

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    return @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
}

function Assert-PassedResult {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode[]]$Results,

        [Parameter(Mandatory)]
        [string]$TestName
    )

    $match = @($Results | Where-Object {
        $_.GetAttribute("testName").EndsWith($TestName, [StringComparison]::Ordinal) -and
        $_.GetAttribute("outcome") -eq "Passed"
    })
    Assert-True ($match.Count -eq 1) "Expected one passed TRX result for $TestName."
}

if (Test-Path -LiteralPath $resultsRoot) {
    $resolvedResults = (Resolve-Path -LiteralPath $resultsRoot).Path
    $expectedResults = [System.IO.Path]::GetFullPath($resultsRoot)
    Assert-True ([StringComparer]::OrdinalIgnoreCase.Equals($resolvedResults, $expectedResults)) `
        "Refusing to clear an unexpected results directory: $resolvedResults"
    Remove-Item -LiteralPath $resolvedResults -Recurse -Force
}

New-Item -ItemType Directory -Path $normalResults -Force | Out-Null
New-Item -ItemType Directory -Path $crashResults -Force | Out-Null

Write-Output "Running the normal passing session..."
& dotnet run --project $project --configuration $Configuration --no-build -- `
    --report-trx `
    --report-trx-filename normal.trx `
    --results-directory $normalResults
Assert-True ($LASTEXITCODE -eq 0) "The normal test session failed with exit code $LASTEXITCODE."

$normalTrx = Join-Path $normalResults "normal.trx"
Assert-True (Test-Path -LiteralPath $normalTrx -PathType Leaf) "The normal TRX was not created."
$normal = @(Get-TrxResults $normalTrx)
Assert-True ($normal.Count -eq 3) "Expected three normal TRX results, found $($normal.Count)."
Assert-PassedResult $normal "A_CompletedBeforeCrash"
Assert-PassedResult $normal "B_CrashHostOnlyWhenRequested"
Assert-PassedResult $normal "C_WouldRunAfterCrash"

Write-Output "Running the guarded, intentionally crashing session..."
$previousCrashValue = [Environment]::GetEnvironmentVariable($crashVariable, "Process")
$previousErrorActionPreference = $ErrorActionPreference
try {
    [Environment]::SetEnvironmentVariable($crashVariable, "1", "Process")
    $ErrorActionPreference = "Continue"
    & dotnet run --project $project --configuration $Configuration --no-build -- `
        --report-trx `
        --report-trx-filename crash.trx `
        --results-directory $crashResults `
        --crashdump `
        --crashdump-type Mini `
        --crash-sequence on *> $crashLog
    $crashExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
    [Environment]::SetEnvironmentVariable($crashVariable, $previousCrashValue, "Process")
}

Assert-True ($crashExitCode -ne 0) "The guarded crash session unexpectedly exited successfully."

$crashTrx = Join-Path $crashResults "crash.trx"
Assert-True (Test-Path -LiteralPath $crashTrx -PathType Leaf) "The streamed crash TRX was not preserved."
$crash = @(Get-TrxResults $crashTrx)
Assert-PassedResult $crash "A_CompletedBeforeCrash"
Assert-True (-not ($crash | Where-Object {
    $_.GetAttribute("testName").EndsWith("C_WouldRunAfterCrash", [StringComparison]::Ordinal)
})) "The post-crash test unexpectedly appeared in the crash TRX."

$sequenceFiles = @(Get-ChildItem -LiteralPath $crashResults -Recurse -File | Where-Object {
    $_.Name -match "sequence"
})
Assert-True ($sequenceFiles.Count -ge 1) "No crash-sequence file was created."
$sequenceText = ($sequenceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
Assert-True ($sequenceText.IndexOf("A_CompletedBeforeCrash", [StringComparison]::Ordinal) -ge 0) `
    "The crash sequence did not record the completed test."
Assert-True ($sequenceText.IndexOf("B_CrashHostOnlyWhenRequested", [StringComparison]::Ordinal) -ge 0) `
    "The crash sequence did not identify the in-flight test."

Write-Output "PASS normal TRX: 3/3 tests passed"
Write-Output "PASS crash exit: $crashExitCode"
Write-Output "PASS streamed crash TRX retained A_CompletedBeforeCrash"
Write-Output "PASS crash sequence identified B_CrashHostOnlyWhenRequested"

$dumpFiles = @(Get-ChildItem -LiteralPath $crashResults -Recurse -File | Where-Object {
    $_.Extension -in ".dmp", ".core" -or $_.Name -match "dump"
})
if ($dumpFiles.Count -gt 0 -and ($dumpFiles | Measure-Object -Property Length -Sum).Sum -gt 0) {
    Write-Output "PASS crash dump: $($dumpFiles[0].Name) ($($dumpFiles[0].Length) bytes)"
}
else {
    Write-Output "INFO crash dump unavailable on this host; inspect crash.console.log"
}

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sampleRoot = $PSScriptRoot
$unsafeProject = Join-Path $sampleRoot 'tests\UnsafeRaceDemo\UnsafeRaceDemo.csproj'
$safeProject = Join-Path $sampleRoot 'tests\SafeParallelTests\SafeParallelTests.csproj'

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
    $unsafe = Invoke-DotnetAndCapture -Arguments @(
        'test', '--project', $unsafeProject,
        '--configuration', 'Release', '--no-build', '--no-restore'
    )

    if ($unsafe.ExitCode -ne 2) {
        throw "Expected the unsafe race to return MTP exit code 2, got $($unsafe.ExitCode).`n$($unsafe.Output)"
    }
    if ($unsafe.Output -notmatch '(?im)^\s*failed:\s*2\s*$' -or
        $unsafe.Output -notmatch '(?im)^\s*total:\s*2\s*$') {
        throw "Expected both unsafe theory rows to fail deterministically.`n$($unsafe.Output)"
    }
    Write-Output 'PASS: ParallelMode.All reproduced the two-row lost update'

    $parallelSafe = Invoke-DotnetAndCapture -Arguments @(
        'test', '--project', $safeProject,
        '--configuration', 'Release', '--no-build', '--no-restore',
        '--filter-method', 'SafeParallelTests.ParallelSafetyTests.Same_class_cases_overlap_safely'
    )

    if ($parallelSafe.ExitCode -ne 0) {
        throw "Expected the atomic parallel cases to pass, got $($parallelSafe.ExitCode).`n$($parallelSafe.Output)"
    }
    if ($parallelSafe.Output -notmatch '(?im)^\s*succeeded:\s*2\s*$' -or
        $parallelSafe.Output -notmatch '(?im)^\s*total:\s*2\s*$') {
        throw "Expected both atomic parallel cases to pass.`n$($parallelSafe.Output)"
    }
    Write-Output 'PASS: two same-class cases overlapped with atomic state'

    $control = Invoke-DotnetAndCapture -Arguments @(
        'test', '--project', $safeProject,
        '--configuration', 'ParallelOptOutControl', '--no-restore',
        '--filter-method', 'SafeParallelTests.ParallelSafetyTests.Shared_state_cases_opt_out_of_parallelism'
    )

    if ($control.ExitCode -ne 2) {
        throw "Expected the no-opt-out control to return MTP exit code 2, got $($control.ExitCode).`n$($control.Output)"
    }
    if ($control.Output -notmatch '(?im)^\s*failed:\s*1\s*$' -or
        $control.Output -notmatch '(?im)^\s*total:\s*2\s*$') {
        throw "Expected exactly one shared-state case to lose the exclusive lease without the opt-out.`n$($control.Output)"
    }
    Write-Output 'PASS: removing DisableParallelization reproduced the lease conflict'

    $optedOut = Invoke-DotnetAndCapture -Arguments @(
        'test', '--project', $safeProject,
        '--configuration', 'Release', '--no-build', '--no-restore',
        '--filter-method', 'SafeParallelTests.ParallelSafetyTests.Shared_state_cases_opt_out_of_parallelism'
    )

    if ($optedOut.ExitCode -ne 0) {
        throw "Expected the opted-out shared-state cases to pass, got $($optedOut.ExitCode).`n$($optedOut.Output)"
    }
    if ($optedOut.Output -notmatch '(?im)^\s*succeeded:\s*2\s*$' -or
        $optedOut.Output -notmatch '(?im)^\s*total:\s*2\s*$') {
        throw "Expected both opted-out shared-state cases to pass.`n$($optedOut.Output)"
    }
    Write-Output 'PASS: DisableParallelization serialized the shared-state cases'
}
finally {
    Pop-Location
}

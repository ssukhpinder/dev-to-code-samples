[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sampleRoot = $PSScriptRoot
$safeProject = Join-Path $sampleRoot 'SafeDemo\SafeDemo.csproj'
$unsafeProject = Join-Path $sampleRoot 'UnsafeProbe\UnsafeProbe.csproj'

function Invoke-DotNetChecked {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Invoke-DotNetChecked @('restore', $safeProject)
Invoke-DotNetChecked @('restore', $unsafeProject)
Invoke-DotNetChecked @('format', 'whitespace', $safeProject, '--verify-no-changes', '--no-restore')
Invoke-DotNetChecked @('format', 'whitespace', $unsafeProject, '--verify-no-changes', '--no-restore')
Invoke-DotNetChecked @('build', $safeProject, '--configuration', 'Release', '--no-restore')
Invoke-DotNetChecked @('run', '--project', $safeProject, '--configuration', 'Release', '--no-build')

$unsafeOutput = (& dotnet build $unsafeProject --configuration Release --no-restore 2>&1 | Out-String)
$unsafeExitCode = $LASTEXITCODE

if ($unsafeExitCode -eq 0) {
    throw 'UnsafeProbe unexpectedly built successfully.'
}

if ($unsafeOutput -notmatch 'EF1003') {
    throw "UnsafeProbe failed without the expected EF1003 diagnostic.`n$unsafeOutput"
}

Write-Output 'PASS UnsafeProbe failed with EF1003 as required.'
Write-Output 'Verifier: safe runtime checks 6/6; analyzer gate 1/1 passed.'

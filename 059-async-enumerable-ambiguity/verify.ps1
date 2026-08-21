[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$sampleRoot = $PSScriptRoot
$platformProject = Join-Path $sampleRoot 'PlatformFix\PlatformFix.csproj'
$transitiveProject = Join-Path $sampleRoot 'TransitiveFix\TransitiveFix.csproj'
$collisionProject = Join-Path $sampleRoot 'CollisionProbe\CollisionProbe.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-NoVulnerablePackages {
    param([Parameter(Mandatory)][string]$Project)

    $auditOutput = @(& dotnet list $Project package --vulnerable --include-transitive 2>&1)
    $auditExitCode = $LASTEXITCODE
    $auditText = $auditOutput -join [Environment]::NewLine

    if ($auditExitCode -ne 0) {
        throw "Package vulnerability audit failed for $Project.`n$auditText"
    }

    if ($auditText -match 'has the following vulnerable packages') {
        throw "Package vulnerability audit found a vulnerable dependency in $Project.`n$auditText"
    }

    Write-Host "PASS: no vulnerable packages in $(Split-Path $Project -Leaf)"
}

foreach ($project in @($platformProject, $transitiveProject, $collisionProject)) {
    Invoke-DotNet -Arguments @('restore', $project)
}

Invoke-DotNet -Arguments @('format', $platformProject, '--verify-no-changes', '--no-restore')
Invoke-DotNet -Arguments @('format', $transitiveProject, '--verify-no-changes', '--no-restore')
Invoke-DotNet -Arguments @('format', $collisionProject, '--verify-no-changes', '--no-restore')
Invoke-DotNet -Arguments @('build', $platformProject, '-c', $Configuration, '--no-restore')
Invoke-DotNet -Arguments @('build', $transitiveProject, '-c', $Configuration, '--no-restore')
Invoke-DotNet -Arguments @('run', '--project', $platformProject, '-c', $Configuration, '--no-build')
Invoke-DotNet -Arguments @('run', '--project', $transitiveProject, '-c', $Configuration, '--no-build')
Assert-NoVulnerablePackages -Project $transitiveProject
Assert-NoVulnerablePackages -Project $collisionProject

$collisionOutput = @(& dotnet build $collisionProject -c $Configuration --no-restore 2>&1)
$collisionExitCode = $LASTEXITCODE
$collisionText = $collisionOutput -join [Environment]::NewLine

if ($collisionExitCode -eq 0) {
    throw 'CollisionProbe unexpectedly compiled. The package collision was not reproduced.'
}

if ($collisionText -notmatch 'CS0121') {
    throw "CollisionProbe failed without the expected CS0121 ambiguity.`n$collisionText"
}

Write-Host 'PASS: CollisionProbe fails with CS0121 as expected'
Write-Host 'PASS verification 6/6'

$ErrorActionPreference = 'Stop'

$sampleDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $sampleDirectory 'CliSchemaContract.csproj'

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Push-Location $sampleDirectory
try {
    Invoke-Checked { dotnet restore $projectPath } 'Restore'
    Invoke-Checked { dotnet format whitespace $projectPath --verify-no-changes --no-restore } 'Format verification'
    Invoke-Checked { dotnet build $projectPath --configuration Release --no-restore } 'Release build'

    $runs = @()
    for ($runNumber = 1; $runNumber -le 5; $runNumber++) {
        $runOutput = & dotnet run --project $projectPath --configuration Release --no-build | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "Verifier run $runNumber failed with exit code $LASTEXITCODE."
        }

        $runs += $runOutput.TrimEnd()
    }

    $baseline = $runs[0]
    $differentRuns = @($runs | Where-Object { $_ -cne $baseline })
    if ($differentRuns.Count -ne 0) {
        throw 'Repeated verifier output was not byte-identical.'
    }

    $baseline
    Write-Host 'PASS repeated verifier output was identical across 5 runs'

    Invoke-Checked { dotnet package list --project $projectPath --include-transitive } 'Dependency listing'
    Invoke-Checked { dotnet package list --project $projectPath --vulnerable --include-transitive } 'Vulnerability audit'

    Write-Host 'Validation complete.'
}
finally {
    Pop-Location
}

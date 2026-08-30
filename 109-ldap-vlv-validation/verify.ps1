$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'LdapVlvValidation.csproj'

function Invoke-Sample {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Framework
    )

    $lines = @(& dotnet run `
        --project $project `
        --framework $Framework `
        --configuration Release `
        --no-build `
        --no-restore 2>&1)

    if ($LASTEXITCODE -ne 0) {
        throw "$Framework failed with exit code $LASTEXITCODE.`n$($lines -join [Environment]::NewLine)"
    }

    return $lines -join "`n"
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Value,

        [Parameter(Mandatory = $true)]
        [string] $Expected
    )

    if (-not $Value.Contains($Expected)) {
        throw "Expected output to contain '$Expected'."
    }
}

$net9First = Invoke-Sample -Framework 'net9.0'
$net10First = Invoke-Sample -Framework 'net10.0'
$net9Second = Invoke-Sample -Framework 'net9.0'
$net10Second = Invoke-Sample -Framework 'net10.0'

Assert-Contains -Value $net9First -Expected 'Package: System.DirectoryServices.Protocols 9.0.19'
Assert-Contains -Value $net9First -Expected 'PASS unpaired high surrogate becomes EF-BF-BD'
Assert-Contains -Value $net9First -Expected 'PASS unpaired low surrogate becomes EF-BF-BD'
Assert-Contains -Value $net9First -Expected 'Summary: 6/6 passed'

Assert-Contains -Value $net10First -Expected 'Package: System.DirectoryServices.Protocols 10.0.11'
Assert-Contains -Value $net10First -Expected 'PASS unpaired high surrogate throws EncoderFallbackException'
Assert-Contains -Value $net10First -Expected 'PASS unpaired low surrogate throws EncoderFallbackException'
Assert-Contains -Value $net10First -Expected 'Summary: 6/6 passed'

foreach ($output in @($net9First, $net10First)) {
    Assert-Contains -Value $output -Expected 'PASS ASCII target encodes as UTF-8'
    Assert-Contains -Value $output -Expected 'PASS supplementary scalar target encodes as UTF-8'
    Assert-Contains -Value $output -Expected 'PASS repeated GetValue calls return identical BER'
}

if ($net9First -cne $net9Second -or $net10First -cne $net10Second) {
    throw 'Repeated runs did not produce byte-for-byte identical output.'
}

Write-Output 'PASS: net9.0 verified replacement bytes for both malformed targets'
Write-Output 'PASS: net10.0 verified early EncoderFallbackException for both malformed targets'
Write-Output 'PASS: valid target encodings match across both package versions'
Write-Output 'PASS: both targets report 6/6 internal checks'
Write-Output 'PASS: repeated output is byte-for-byte deterministic'
Write-Output 'Summary: 5/5 passed'

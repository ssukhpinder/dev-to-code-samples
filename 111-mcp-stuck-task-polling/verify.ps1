$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'McpStuckTaskPolling.csproj'
$runArguments = @(
    'run'
    '--project', $project
    '--configuration', 'Release'
    '--no-build'
    '--no-restore'
)

$firstRun = @(& dotnet @runArguments)
if ($LASTEXITCODE -ne 0) {
    throw "First verifier run failed with exit code $LASTEXITCODE."
}

$secondRun = @(& dotnet @runArguments)
if ($LASTEXITCODE -ne 0) {
    throw "Second verifier run failed with exit code $LASTEXITCODE."
}

$expected = @(
    'PASS: stuck poll guard raised McpException'
    'PASS: repeated input key was presented once'
    'PASS: threshold 3 stopped polling after 4 tasks/get calls'
    'PASS: input response was sent once'
    'PASS: best-effort tasks/cancel was sent once'
)

if (($firstRun -join "`n") -cne ($expected -join "`n")) {
    throw 'Verifier output did not match the expected contract.'
}

if (($firstRun -join "`n") -cne ($secondRun -join "`n")) {
    throw 'Repeated verifier output was not deterministic.'
}

$firstRun
'PASS: repeated output was byte-for-byte deterministic'
'Summary: 6/6 passed'

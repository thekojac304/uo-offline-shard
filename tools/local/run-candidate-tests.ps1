param(
    [Parameter(Mandatory)][string]$EnginePath,
    [Parameter(Mandatory)][string]$DataDir,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [Parameter(Mandatory)][string]$ResultsPath
)
$ErrorActionPreference = 'Stop'
$engineRoot = (Resolve-Path -LiteralPath $EnginePath).Path
$mapRoot = (Resolve-Path -LiteralPath $DataDir).Path
$candidateRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if (-not (Test-Path -LiteralPath (Join-Path $engineRoot '.uo-offline-candidate-engine'))) {
    throw 'Engine must be a deliberately marked isolated candidate checkout.'
}
if ($engineRoot.StartsWith('\\') -or $engineRoot -match '(^|[\\/])Servers([\\/]|$)') {
    throw 'Remote/server installations cannot be test targets.'
}
if ((git -C $engineRoot rev-parse HEAD).Trim() -ne 'e7f85d404d52e0def1fb342b3dc185894a57017d') {
    throw 'Unexpected engine revision.'
}
# Candidate engine checkouts must remain world-free; world trials use separate
# runtime copies. Reject every config savePath, not just the conventional name.
if (Test-Path -LiteralPath (Join-Path $engineRoot 'Distribution/Configuration/modernuo.json')) {
    throw 'Use a build-only engine checkout without runtime configuration.'
}
if (Test-Path -LiteralPath (Join-Path $engineRoot 'Distribution/Saves')) {
    throw 'World-bearing engine checkout is not an automated test target.'
}
$botTarget = Join-Path $engineRoot 'Projects/UOContent/CustomBots'
foreach ($retired in @('BankFixtures.FloorBounds.cs','GenerateBotsCommand.BankSitterFloor.cs','BotSessionManager.SeatReservation.cs')) {
    if (Test-Path -LiteralPath (Join-Path $botTarget $retired)) { throw "Retired source remains: $retired" }
}
# No destructive sync: reject foreign source instead of deleting it.
$expected = @(Get-ChildItem -LiteralPath (Join-Path $candidateRepo 'playerbots/source/CustomBots') -File -Recurse)
$actual = @(Get-ChildItem -LiteralPath $botTarget -File -Recurse)
if ($expected.Count -ne $actual.Count) { throw 'Stage the exact candidate source before running tests.' }
foreach ($file in $expected) {
    $rel = $file.FullName.Substring((Join-Path $candidateRepo 'playerbots/source/CustomBots').Length + 1)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $botTarget $rel)).Hash) {
        throw "Candidate source hash mismatch: $rel"
    }
}
$env:MODERNUO_TEST_DATA_DIR = $mapRoot
$env:MODERNUO_CLIENT_PATH = $mapRoot # Server.Tests uses a different variable
Push-Location -LiteralPath $engineRoot
try {
    dotnet build ModernUO.slnx -c $Configuration --nologo
    if ($LASTEXITCODE) { throw 'Build failed.' }
    # Simulated bot-clock tests and upstream path-cache tests own process-global
    # clocks/caches. Execute both complete sets in independent test hosts.
    dotnet test Projects/Server.Tests -c $Configuration --no-build --logger trx --results-directory (Join-Path $ResultsPath 'server')
    if ($LASTEXITCODE) { throw 'Server suite failed.' }
    dotnet test Projects/UOContent.Tests -c $Configuration --no-build --filter 'FullyQualifiedName!~Server.Tests.CustomBots' --logger trx --results-directory (Join-Path $ResultsPath 'upstream-content')
    if ($LASTEXITCODE) { throw 'Upstream content suite failed.' }
    dotnet test Projects/UOContent.Tests -c $Configuration --no-build --filter 'FullyQualifiedName~Server.Tests.CustomBots' --logger trx --results-directory (Join-Path $ResultsPath 'custom-bots')
    if ($LASTEXITCODE) { throw 'Custom bot suite failed.' }
} finally { Pop-Location }

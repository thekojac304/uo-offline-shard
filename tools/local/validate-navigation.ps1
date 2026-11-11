param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
function Read-Records([string]$Ref, [string]$Path, [string]$Key) {
    $json = git -C $repoRoot show "${Ref}:playerbots/data/$Path"
    if ($LASTEXITCODE) { throw "Cannot read $Ref $Path" }
    return @(($json -join "`n" | ConvertFrom-Json -AsHashtable)[$Key])
}
function Get-Issues([string]$Ref) {
    $nodes = Read-Records $Ref 'Waypoints/waypoints.json' 'Waypoints'
    $destinations = Read-Records $Ref 'Destinations/destinations.json' 'Destinations'
    $zones = Read-Records $Ref 'Zones/zones.json' 'Zones'
    $names = @{}
    foreach ($node in $nodes) {
        if ($names.ContainsKey($node.Name)) { "duplicate-waypoint|$($node.Name)" }
        $names[$node.Name] = $node
    }
    foreach ($node in $nodes) {
        foreach ($target in $node.Connects) {
            if (-not $names.ContainsKey($target)) { "missing-waypoint-edge|$($node.Name)|$target" }
        }
    }
    foreach ($dest in $destinations) {
        if ($dest.NearestWaypoint -and -not $names.ContainsKey($dest.NearestWaypoint)) {
            "missing-destination-waypoint|$($dest.Name)|$($dest.NearestWaypoint)"
        }
        foreach ($arrival in $dest.Arrivals) {
            foreach ($target in $arrival.Waypoints) {
                if (-not $names.ContainsKey($target)) { "missing-arrival-waypoint|$($dest.Name)|$target" }
            }
        }
        foreach ($point in @($dest)) {
            if ($point.X -lt 0 -or $point.X -ge 7168 -or $point.Y -lt 0 -or $point.Y -ge 4096) {
                "out-of-map-destination|$($dest.Name)"
            }
        }
        if ($dest.ContainsKey('TargetX') -and ($dest.TargetX -lt 0 -or $dest.TargetX -ge 7168 -or $dest.TargetY -lt 0 -or $dest.TargetY -ge 4096)) {
            "out-of-map-portal-target|$($dest.Name)"
        }
    }
    foreach ($zone in $zones) {
        if ($zone.Points.Count -lt 3) { "invalid-zone-polygon|$($zone.Name)" }
        foreach ($point in $zone.Points) {
            if ($point.Count -ne 2 -or $point[0] -lt 0 -or $point[0] -ge 7168 -or $point[1] -lt 0 -or $point[1] -ge 4096) {
                "out-of-map-zone-point|$($zone.Name)|$($point -join ',')"
            }
        }
    }
}
$parsed = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'playerbots/data') -Recurse -File -Filter '*.json') {
    $null = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{path=$file.FullName.Substring($repoRoot.Length+1);sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
$baseline = @(Get-Issues '9799a1b' | Sort-Object -Unique)
$candidate = @(Get-Issues 'HEAD' | Sort-Object -Unique)
$new = @($candidate | Where-Object { $_ -notin $baseline })
$resolved = @($baseline | Where-Object { $_ -notin $candidate })
$result = [ordered]@{
    official='0b5c471ac1fc69bb93d962c812ec230544631ce9';baseline='9799a1b';parsed_json=$parsed;
    destinations=(Read-Records 'HEAD' 'Destinations/destinations.json' 'Destinations').Count;
    waypoints=(Read-Records 'HEAD' 'Waypoints/waypoints.json' 'Waypoints').Count;
    zones=(Read-Records 'HEAD' 'Zones/zones.json' 'Zones').Count;
    existing_issues=$baseline;candidate_issues=$candidate;introduced_issues=$new;resolved_issues=$resolved;
    scope='JSON structure/name references/map bounds. Does not prove physical route reachability.'
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Parsed $($parsed.Count) JSON files; existing issues $($baseline.Count), candidate issues $($candidate.Count), introduced $($new.Count), resolved $($resolved.Count)."
if ($new.Count) { Write-Output $new; exit 2 }

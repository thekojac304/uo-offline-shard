# Candidate regression tests

The candidate uses official UO Offline population management. It retains the
spawner deletion/rearm tests and diagnostics, and adds capped-three admission,
return timing, map/zone, serialization and functional bank-spawn coverage.

The floor-repair tests and all-ready reservation expectations are intentionally
retired. Historical coverage remains at stable commit `9799a1b`; bank roofs are
accepted unless they cause functional instability or ownership/refill failure.

Build a clean engine at `e7f85d404`, apply the official patches (0001–0011 at f90e419) in order,
and copy the exact candidate source/data/tests into it. Mark the build-only root
with `.uo-offline-candidate-engine`. Use `tools/local/run-candidate-tests.ps1`
with explicit engine, map-data and result paths. It refuses runtime/world-bearing
or unmarked targets and does not overwrite source. Require real map data; the
new map tests fail instead of silently skipping when it is missing.

The upstream and custom suites run in separate processes because the virtual
bot-clock simulations reset global clock/timer state. Both sets must pass;
running only a filtered subset is insufficient. Unit/virtual-time results do
not replace natural-cadence world/save/restart and soak validation.

`tools/local/validation/CandidateValidation.csproj` builds a separate observer
assembly for marked loopback-only disposable runtimes. It records aggregate
population/resource counters and hashes of persistent identity/ownership.
Never include this assembly, private world copies, test data, or request files
in a deployment artifact. No compatibility AnyReady shim is used.

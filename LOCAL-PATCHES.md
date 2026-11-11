# Changes on top of official UO Offline

Baseline: official main `f90e41964194b6440117dd26703b72a0df1a4d4b` (2026-11-08).
Engine: ModernUO `e7f85d404d52e0def1fb342b3dc185894a57017d` with UO Offline's own patches 0001–0011.

Runtime changes:

- `PlayerBot.SpawnerRelease.cs` and the `partial` keyword on `PlayerBot`.
  A deleted bot gives its slot back and a formerly full spawner rearms.
  Upstream still lacks this; without it 7 of 12 deletion tests fail.
- `BotSpawnerStatsCommand.cs`: read-only `[BotSpawnerStats` diagnostics for
  capacity, stale references, refill timers, potentially stalled owners, the
  population gate with `ReadyToHold()` held seats, fixtures versus ordinary bots.

If an update replaces `PlayerBot.cs` without `partial`, the build fails on the
extra file instead of silently dropping the fix. If upstream ever adds its own
`PlayerBot.OnDelete`, the build fails as a duplicate member. Either way, stop
and review.

Tests and tooling: `playerbots/tests/CustomBots/` (spawner release and rearm,
population, persistence, navigation data, stats command) and `tools/local/`
(running them against an isolated ModernUO checkout; see `playerbots/tests/README.md`).

Everything else is official upstream behaviour. Earlier fixes for rooftop bank
fixtures, BankSitter floors and seat reservation were retired in favour of
upstream's own behaviour.

Don't run the stock installer or updater over an existing installation to apply
this; use the release package's update steps.

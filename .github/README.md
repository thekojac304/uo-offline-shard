# UO Offline — shard fork with the PlayerBot spawner fix

A fork of [**UO Offline**](https://github.com/Klein187/uo-offline) by Klein187. UO Offline is a single-player
Ultima Online shard built on [ModernUO](https://github.com/modernuo/ModernUO) and
[ClassicUO](https://github.com/ClassicUO/ClassicUO). Its PlayerBots fill Britannia with bots that fight, travel,
shop, bank, own houses and chat. This fork runs the same game and adds one server-side fix plus a diagnostic
command, with the regression tests that check them.

This fork is not affiliated with or endorsed by Klein187, the ModernUO project or the ClassicUO project. For what
UO Offline is and how to play it, read upstream's [README](../README.md); this repository's root README is
upstream's, unchanged.

## Download

**[Server 2026.11.08](https://github.com/thekojac304/uo-offline-shard/releases/tag/server-2026.11.08)**:
`uo-offline-shard-server-2026.11.08-f90e419-spawnerfix-win-x64.zip`, a prebuilt **server update package** for Windows
x64. It contains:
- the compiled ModernUO `Distribution` folder: engine `e7f85d4` with upstream patches 0001–0011, and UO Offline
  `f90e419` CustomBots with this fork's spawner fix;
- the exact bot and engine sources it was built from.

It contains no world saves, accounts, configuration, Ultima Online client files or ClassicUO.

It updates an **existing** UO Offline installation. For a new installation, use upstream's installer
(`install.bat` / `install.sh`). The installer builds the server, installs ClassicUO and asks for your own Ultima
Online client files. The release notes say how to apply the package.

## Why the fork exists

The shard runs as a long-lived dedicated server rather than a short single-player session. Over hours of uptime the
bot population drained, because a deleted PlayerBot never told its spawner. A full spawner with a stopped timer then
never refilled. The fix keeps the population stable on a server that stays up for days, without changing the game's
design, data files or save format.

## Differences from upstream

| Change | Files | Status |
|---|---|---|
| A deleted PlayerBot removes itself from its spawner (`PlayerBot.OnDelete`), so full spawners rearm and refill | `playerbots/source/CustomBots/PlayerBot.SpawnerRelease.cs`, `partial` on `PlayerBot` | Upstream `f90e419` still has no delete notification; a control build without it fails 7 of 12 deletion/rearm tests |
| `[BotSpawnerStats [detail]`: a read-only GameMaster report on population by kind, spawner slots, refill timers, stale references and the population gate | `playerbots/source/CustomBots/BotSpawnerStatsCommand.cs` | Diagnostic only |
| Regression tests and an isolated test runner | `playerbots/tests/CustomBots/`, `tools/local/` | Not part of the server |

The fix is added as **a new file that declares a `partial` class**; the upstream file only gains the `partial`
keyword. If an upstream update overwrites it, the build fails with a missing-`partial` or duplicate-member error,
so the fix can never disappear silently. No ModernUO engine file is changed. See [LOCAL-PATCHES.md](../LOCAL-PATCHES.md).

The `main` branch is official UO Offline history up to `f90e419` (2026-11-08) plus this fork's commit. Tag
`server-2026.11.08` marks the source of the release package.

## Setup and requirements

- **Windows 10/11 x64.** Linux and the Steam Deck are supported by upstream's installer; this fork's package was
  built and tested on Windows only.
- **.NET 10 runtime** (SDK 10.0.403 was used to build).
- **Your own Ultima Online client data files.** These are EA's property and never included.
- **A ClassicUO client**, installed by upstream's installer.

## Building and testing

Build the server as upstream describes, with this repository in place of upstream's. To run the fork's tests
against an isolated ModernUO checkout, use `tools/local/run-candidate-tests.ps1` (see
[playerbots/tests/README.md](../playerbots/tests/README.md)).

Results for this release, on clean Debug and Release builds (0 warnings, 0 errors):

| Suite | Result |
|---|---|
| ModernUO `Server.Tests` | 837 passed |
| UOContent tests | 720 passed |
| Fork's bot regression tests | 61 passed |

A copy of an existing world loaded, booted, saved and reloaded with no exceptions or missing types. Not tested: a
real-client login and long runs.

## Known issues

- An inherited ModernUO race can log `Attempted to start timer ... while world is Saving` from `BaseHouse.Serialize`
  during saves. It's left unfixed.
- The first boot fills the drawn house spots once (8 bot houses in the test world). Leave out
  `Data/CustomSpawns/house_spots.json` if you don't want that.

## Credits and licenses

- **UO Offline** and PlayerBots by Klein187: MIT ([LICENSE](../LICENSE)). This fork's changes are offered under the
  same MIT License.
- **ModernUO**: GPL-3.0. The server binaries in the release are built from ModernUO `e7f85d4` with UO Offline's
  patches; the package includes the patched sources needed to rebuild it and the GPL text.
- **ClassicUO**: BSD-2-Clause; not redistributed here.
- Ultima Online is a trademark of Electronic Arts. No EA content is included.

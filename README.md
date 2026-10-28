# Straphangers

Station ridership statistics for **Timberborn**: saved boarding totals and rolling daily averages, displayed in each tubeway or zipline station's building panel.

**Development preview.** The repository starts from the locally tested v0.1.10 prototype. There is no public binary release yet. Tubeways have been exercised on small and large colonies; ziplines still need a live validation run. See [release readiness](docs/release-readiness.md) before distributing a build.

## What it measures

Each pedestrian crossing into a station counts as one boarding. Repeated journeys and bots count. Passing through a tube segment, zipline pylon or beam does not create another boarding. An unusual construction visit can count as a station entry without a completed ride.

The panel currently shows total boardings and an average over **up to seven complete game days**. Zero-traffic days count; the first partial tracking day and the current day are excluded. New stations start their own tracking periods. The first average becomes available after a complete tracked day.

Both boarding and alighting totals and daily buckets are already saved internally. Showing both directions in a compact table, and adding a native time-of-day activity display, are planned design changes; they are not implemented in this source snapshot.

## Saved data

Counts are embedded in normal saves and autosaves, not stored in a separate colony database. Loading an earlier save restores that timeline. The first load of a save without mod data starts at zero. Each active station keeps lifetime totals and up to eight daily buckets. Demolished stations are omitted from subsequent save snapshots.

The public name is Straphangers. The legacy mod ID `Kellan.BoardingCounter.Prototype`, `BoardingCounter.dll` assembly name, and save key `Kellan.BoardingCounter.Ridership` are intentionally retained so existing prototype saves remain compatible. Install only one copy of the mod.

## Build locally

Install the .NET 8 SDK and Python 3. Supply your own Timberborn installation; game assemblies and assets are not in this repository.

Set `TIMBERBORN_MANAGED` to the game's `Managed` directory, then run:

```sh
./test.sh
./build.sh
```

The mod builds against your installed game. `tools/package.py` derives a minimal diagnostic doorway reference from your local `StreamingAssets/Modding/Blueprints.zip`. The resulting package is placed under `artifacts/`; generated references are ignored by Git.

On macOS, `Managed` is inside `Timberborn.app/Contents/Resources/Data/Managed`. You may also pass `-p:GameManaged=...` to `dotnet build Source/BoardingCounter.csproj` directly. PowerShell users can set `$env:TIMBERBORN_MANAGED` and run the `dotnet` and Python commands from the scripts individually.

Unpack a local package into your Timberborn mods folder and enable it in the game's mod manager. To upgrade a prototype installation, replace its existing folder contents rather than install a second copy. Restart the game after changing the DLL.

## Tests

`./test.sh` runs deterministic counting, geometry, completed-movement, persistence and replay checks without needing the game. Public fixtures are constructed, not private map recordings. Set `TIMBERBORN_MANAGED` to additionally exercise the native save adapter against your local game assemblies. CI runs the game-free tests.

Automated tests do not establish that every game version or custom station layout is supported. Live validation so far is on macOS with Timberborn **1.1.2.8**. The observer uses read-only access to a private native movement buffer and stops counting explicitly when the buffer contract fails.

## Diagnostics in this development snapshot

Detailed diagnostics are **currently enabled automatically**. A release-default-off option is a release blocker, not an existing feature.

- `Logs/`: rolling event, route and station CSVs plus a summary and observer status. The rolling files consume roughly 22 MiB at their thresholds, with possible overshoot between flushes.
- `Recordings/`: at most three recognized owned sessions. Each trace closes at 16 MiB compressed or 128 MiB raw input, allowing a final complete record. Total recording storage is roughly bounded to 54 MiB plus metadata.
- Native route recording selects up to 64 concurrent passengers; larger populations are marked partial. Live station counting observes all registered passengers. Tube membership records contain all observed passenger IDs.

Recordings include in-game entity IDs and routes. Review diagnostic bundles before sharing them publicly. No private recordings, game saves, game DLLs, decompiled game source, or downloaded SDK/tool bundles are committed here.

## Flavor

- Iron Teeth: “A colony that rides together thrives together.”
- Folktails: “The pastries are still warm if you take the zipline.”

## License

MIT. Timberborn and its assets belong to their respective owners. This is an independent mod.

# Validation status

Checked on 2026-10-28 against the source-publication snapshot:

- Game-free suite: 222 C# checks plus 21 Python replay checks passed.
- With locally installed game assemblies: 229 C# checks plus the same 21 Python checks passed, including native save-object round trips.
- The portable mod build completed without warnings/errors against Timberborn 1.1.2.8 on macOS.
- Local packaging included only the mod DLL, manifest, documentation, auditor and locally generated minimal doorway reference. No game DLLs were packaged.

The public fixtures are constructed. They cover directional station crossings, side/over/under passes, plane contact, duplicate samples, station geometry changes, stacked doors, elevated zipline pass-through, bends, reversals, future smoothing points, instant edges, long movement spans, persistence timelines, rolling averages, tube membership evidence, same-station excursions and corrupted/incomplete replay handling.

Separate private live-game captures exercised tubeways on a two-station map and an eleven-station colony. These actual routes and entity IDs are not published. They do not replace the outstanding live zipline validation or a full release soak with diagnostics disabled.

v0.1.11 adds the two-direction table and loads the actual native attraction busyness bars. Tests cover a 500-day bounded-history run, quiet and partial days, float hour boundaries, separate directional averages, detached hourly save arrays, schema-one migration, native save-object migration and malformed hourly data. A twelve-day replay fixture confirms that saved-history trimming does not truncate replay accounting.

The native-style table, hourly strip and flavor sayings compile against the installed game UI APIs. In-game visual checks across UI scales and the first-hour/first-complete-day behavior remain release work. See [release readiness](release-readiness.md).

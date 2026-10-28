# Release readiness

The current code is a working development preview, ready for source review and continued testing. It is not yet a broadly validated player release.

## Before a first public binary

1. **Validate ziplines in the game.** Test station A → pylon/beam → station B in both directions, different orientations/heights, and travel through an intermediate station without using its pedestrian entrance. Pylons and beams must never show station ridership or create boardings. Verify a save/reload cycle.
2. **Make diagnostics opt-in and off by default.** Gate the detailed CSVs, replay serialization, reference loading and tube visitor subscriptions together. Stop tube observation when a bounded capture closes. Preserve normal counting, saving and the panel while diagnostics are off. A diagnostic disk/write failure should not disable normal counts; currently legacy CSV failures can stop the observer.
3. **Validate the new station metrics display.** v0.1.11 implements the compact boardings/alightings table and native hourly busyness strip with bounded history. Verify the actual appearance, hover text, first-day fallback and later complete-day averaging in game.
4. **Check the native panel in a fresh session.** Confirm row alignment, long numbers, wrapping, tooltips, both faction sayings and UI scale. Automated presentation tests cover strings and arithmetic, not rendered pixels.
5. **Run a release soak test.** Check totals, seven complete days of averages, save/load, station construction/demolition, game-speed changes, bots and representative larger colonies with diagnostics off.
6. **Prepare distribution.** Add a thumbnail and screenshots, plain installation/upgrade instructions, tested version/platform metadata, and a reproducible release ZIP. Publish a preview explicitly before calling it stable.

## Findings from source review

- Counts and averages have pure tests; the native save adapter is separately testable with installed game assemblies. The native buffer and tube fields are accessed read-only. No Harmony patches are used.
- Saved per-station storage is bounded: totals plus up to eight daily buckets. Counts are tied to game-save snapshots and station entity IDs.
- Replay storage has size and session retention bounds. Exported ZIPs are independent copies and are not pruned by the mod.
- Diagnostic event/route writing and tube subscriptions are still coupled to the live observer. The CSV disk-write failure path can stop counting; this should be separated for release.
- v0.1.11 removes demolished station entries during refresh and bounds both daily and hourly history to seven previous days plus today.
- Private movement-buffer and station-marker assumptions are game-version-specific. Only the tested game build should be advertised; future versions need a replay check.
- The current implementation scans stations for each completed movement span and refreshes entities periodically. Benchmark with larger networks before claiming low overhead.

## Repository preparation completed

The public tree uses environment-supplied game references, game-free synthetic tests, an MIT license and CI configuration. Personal workstation paths, real entity IDs/routes, save files, validation exports, proprietary binaries and full extracted blueprints are excluded. Native doorway references are generated locally from a user's installed game during packaging.

The installed prototype and its private validation history remain outside this repository. Source publication does not by itself install a new mod build or publish a binary release.

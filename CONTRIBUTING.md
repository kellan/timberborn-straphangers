# Contributing

This is a development preview. Please describe the game version, transport type, reproducible steps, and expected versus observed counts when reporting a problem. Avoid posting game saves or route recordings publicly without reviewing their contents.

Run `./test.sh` before submitting a change. It uses .NET 8 and Python 3, with no proprietary assemblies or actual colony recordings. Set `TIMBERBORN_MANAGED` to your installed game's Managed directory to enable native save-adapter tests and `./build.sh`.

Keep the legacy mod ID, assembly name and save key compatible with existing users unless a migration is supplied. Saved counts and replay counts have different scopes: persistence covers station history; replay covers a bounded capture and selected actors.

Add meaningful regression coverage for counting or saved-data changes. UI-only edits should be built and checked in-game at the supported UI scales. Test ziplines through pylons and intermediate stations rather than count every tower as a boarding.

Do not commit game DLLs, decompiled source, full extracted blueprints, SDKs, saves, actual route logs or generated recordings. Build outputs and test artifacts belong in `artifacts/`.

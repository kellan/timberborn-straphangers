#!/bin/sh
set -eu
cd "$(dirname "$0")"
DOTNET="${DOTNET_ROOT:+$DOTNET_ROOT/}dotnet"
"$DOTNET" run --project Tests/Tests.csproj -c Release -p:UseSharedCompilation=false
python3 Tests/test_replay_routes.py
python3 Tests/test_offline_recording.py

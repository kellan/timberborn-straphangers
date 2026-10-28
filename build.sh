#!/bin/sh
set -eu
cd "$(dirname "$0")"
: "${TIMBERBORN_MANAGED:?Set TIMBERBORN_MANAGED to the installed game Managed directory}"
DOTNET="${DOTNET_ROOT:+$DOTNET_ROOT/}dotnet"
"$DOTNET" build Source/BoardingCounter.csproj -c Release --configfile NuGet.Config -p:UseSharedCompilation=false
python3 tools/package.py --managed "$TIMBERBORN_MANAGED"

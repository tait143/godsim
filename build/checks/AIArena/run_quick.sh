#!/usr/bin/env bash
# AI Arena smoke check: builds tools/GodSim.AIArena against build/GodSim.Core.csproj and runs the quick arena
# (2 seeds x 40 years x 128 map). Writes quick_report.md / quick_results.json next to this script.
# Extra args are passed through, e.g.  ./run_quick.sh --min-score 6
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
dotnet build "$root/tools/GodSim.AIArena/GodSim.AIArena.csproj" -c Release -nologo -v q
dotnet "$root/tools/GodSim.AIArena/bin/Release/net8.0/GodSim.AIArena.dll" --quick \
  --out "$here/quick_report.md" --json "$here/quick_results.json" "$@"

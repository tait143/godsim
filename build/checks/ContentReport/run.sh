#!/usr/bin/env bash
# Content-only build of tools/GodSim.ContentReport (Contracts + Races + Progression/Content + Buildings + Divine/Powers),
# usable while systems modules are mid-edit. Regenerates docs/CONTENT_REFERENCE.md by default; extra arguments
# are passed to the tool (e.g. --check, --json out.json, --verify).
#   build/checks/ContentReport/run.sh [tool options]
# Drop a content folder that does not compile: EXCLUDE="Buildings Divine" build/checks/ContentReport/run.sh
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
props=()
for f in ${EXCLUDE:-}; do props+=("-p:Exclude${f}=true"); done
dotnet build "$here/ContentReport.Check.csproj" -nologo -v q --disable-build-servers -c Release "${props[@]}"
exec dotnet "$here/bin/Release/net8.0/GodSim.ContentReport.Check.dll" --root "$here/../../.." "$@"

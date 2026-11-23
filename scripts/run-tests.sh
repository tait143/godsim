#!/usr/bin/env bash
# run-tests.sh: local equivalent of CI: compat check -> build -> test -> headless smoke (if present).
#
# Usage: scripts/run-tests.sh [-c Debug|Release] [--skip-compat] [--no-smoke] [--years N] [--seed N]
#   HEADLESS_ARGS overrides the smoke-run arguments entirely (default: --years N --seed N --strict --out <json>).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG=Release
COMPAT=1
SMOKE=1
YEARS=60
SEED=12345

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration) CONFIG="$2"; shift 2 ;;
        --skip-compat) COMPAT=0; shift ;;
        --no-smoke) SMOKE=0; shift ;;
        --years) YEARS="$2"; shift 2 ;;
        --seed) SEED="$2"; shift 2 ;;
        -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
        *) echo "error: unknown argument: $1" >&2; exit 2 ;;
    esac
done
[[ "$CONFIG" =~ ^(Debug|Release)$ ]] || { echo "error: configuration must be Debug or Release" >&2; exit 2; }
[[ "$YEARS" =~ ^[0-9]+$ && "$SEED" =~ ^[0-9]+$ ]] || { echo "error: --years/--seed must be integers" >&2; exit 2; }

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
DOTNET_FLAGS=(-nologo --disable-build-servers)

step() { printf '\n==> %s\n' "$*"; }

if [[ $COMPAT -eq 1 ]]; then
    step "Unity compatibility check"
    bash scripts/check-unity-compat.sh
fi

if [[ -f GodSim.sln ]]; then TARGET=GodSim.sln; else TARGET=build/GodSim.Core.csproj; fi
step "Build $TARGET ($CONFIG)"
dotnet build "$TARGET" -c "$CONFIG" "${DOTNET_FLAGS[@]}"

TEST_PROJ=tests/GodSim.Tests/GodSim.Tests.csproj
if [[ -f "$TEST_PROJ" ]]; then
    step "Test $TEST_PROJ"
    dotnet test "$TEST_PROJ" -c "$CONFIG" --no-build "${DOTNET_FLAGS[@]}" \
        --logger "trx;LogFileName=GodSim.Tests.trx" --results-directory TestResults
else
    echo "note: $TEST_PROJ not found; skipping tests"
fi

HEADLESS=tools/GodSim.Headless/GodSim.Headless.csproj
if [[ $SMOKE -eq 1 && -f "$HEADLESS" ]]; then
    mkdir -p snapshots
    OUT="snapshots/smoke-${YEARS}y-seed${SEED}.json"
    # HEADLESS_ARGS is split on whitespace on purpose (it is a list of CLI arguments).
    # shellcheck disable=SC2206
    ARGS=(${HEADLESS_ARGS:---years $YEARS --seed $SEED --strict --out $OUT})
    step "Headless smoke: ${ARGS[*]}"
    # The tool project is not in GodSim.sln until the lead adds it, so let `dotnet run` build it if needed.
    dotnet run --project "$HEADLESS" -c "$CONFIG" -- "${ARGS[@]}"
    if [[ -z "${HEADLESS_ARGS:-}" && ! -s "$OUT" ]]; then
        echo "error: headless run produced no snapshot at $OUT" >&2; exit 1
    fi
elif [[ $SMOKE -eq 1 ]]; then
    echo "note: $HEADLESS not found; skipping headless smoke"
fi

step "All checks passed"

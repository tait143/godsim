#!/usr/bin/env bash
# Smoke test for tools/GodSim.SaveTool: builds the check project, generates sample saves and asserts every
# subcommand's exit code (and that no key material / escape sequence is printed).
# Usage: ./smoke.sh [stubs|persistence]   (default stubs; exit 0 = all assertions passed)
#   stubs        Contracts + stubbed Persistence + stubbed Composition
#   persistence  Contracts + REAL Persistence + stubbed Composition (replay steps skipped: stub replay format)
set -u
MODE="${1:-stubs}"
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
OUT="$HERE/bin/smoke-$MODE"
dotnet build -nologo -v q --disable-build-servers -p:SaveToolCheckMode="$MODE" -o "$OUT" >"$OUT.log" 2>&1 || { echo "build failed (see $OUT.log)"; exit 1; }
BIN="dotnet $OUT/SaveTool.Check.dll"
W="$(mktemp -d)"; trap 'rm -rf "$W"' EXIT
KEY=00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff
BAD=ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
printf '%s\n' "$KEY" > "$W/key.hex"
fails=0
expect() { local want=$1; shift; "$@" >"$W/out.txt" 2>&1; local got=$?
  if [ "$got" != "$want" ]; then echo "FAIL (want $want got $got): ${*:3}"; sed 's/^/    /' "$W/out.txt" | head -20; fails=$((fails+1)); else echo "ok   ($got) ${*:3}"; fi
  if grep -q "$KEY" "$W/out.txt"; then echo "FAIL: key material printed"; fails=$((fails+1)); fi; }

$BIN make-sample "$W/plain.gsav" - 10 >/dev/null
$BIN make-sample "$W/signed.gsav" "$KEY" 10 >/dev/null
$BIN make-sample "$W/later.gsav" - 30 >/dev/null
$BIN make-sample "$W/evil.gsav" - 10 $'Evil\e[31mRed\nFAKE' >/dev/null
$BIN make-replay "$W/replay.grpl" >/dev/null 2>&1
head -c 60 "$W/plain.gsav" > "$W/trunc.gsav"
cp "$W/signed.gsav" "$W/tampered.gsav"; printf '\x01' | dd of="$W/tampered.gsav" bs=1 seek=$(( $(stat -c %s "$W/tampered.gsav") - 3 )) conv=notrunc 2>/dev/null

expect 0 $BIN help
expect 2 $BIN
expect 2 $BIN bogus
expect 2 $BIN info
expect 2 $BIN info "$W/plain.gsav" --nope
expect 2 $BIN info "$W/plain.gsav" extra
expect 2 $BIN verify "$W/plain.gsav" --key-file "$W/key.hex" --key-hex "$KEY"
expect 2 $BIN verify "$W/plain.gsav" --key-hex zz
expect 2 $BIN verify "$W/*.gsav"
expect 3 $BIN info "$W/missing.gsav"
expect 0 $BIN info "$W/signed.gsav"
expect 4 $BIN info "$W/trunc.gsav"
expect 0 $BIN verify "$W/plain.gsav"
expect 5 $BIN verify "$W/plain.gsav" --require-signature
expect 5 $BIN verify "$W/signed.gsav"
expect 0 $BIN verify "$W/signed.gsav" --key-file "$W/key.hex" --require-signature
expect 5 $BIN verify "$W/signed.gsav" --key-hex "$BAD"
expect 5 $BIN verify "$W/tampered.gsav" --key-file "$W/key.hex"
expect 4 $BIN verify "$W/trunc.gsav"
expect 0 $BIN stats "$W/plain.gsav"
expect 0 $BIN stats "$W/evil.gsav"
if grep -q $'\e' "$W/out.txt"; then echo "FAIL: escape sequence printed"; fails=$((fails+1)); fi
expect 0 $BIN diff "$W/plain.gsav" "$W/signed.gsav" --key-file "$W/key.hex"
expect 6 $BIN diff "$W/plain.gsav" "$W/later.gsav"
if [ "$MODE" = stubs ]; then
expect 0 $BIN replay-verify "$W/plain.gsav" "$W/replay.grpl" --ticks 20
H=$(grep '^Hash:' "$W/out.txt" | awk '{print $2}')
expect 0 $BIN replay-verify "$W/plain.gsav" "$W/replay.grpl" --ticks 20 --expect-hash "$H"
expect 6 $BIN replay-verify "$W/plain.gsav" "$W/replay.grpl" --ticks 21 --expect-hash "$H"
fi
expect 2 $BIN replay-verify "$W/plain.gsav" "$W/replay.grpl"
expect 4 $BIN replay-verify "$W/plain.gsav" "$W/trunc.gsav" --ticks 1
expect 0 $BIN sign "$W/plain.gsav" "$W/resigned.gsav" --key-file "$W/key.hex"
expect 0 $BIN verify "$W/resigned.gsav" --key-file "$W/key.hex" --require-signature
expect 3 $BIN sign "$W/plain.gsav" "$W/resigned.gsav" --key-file "$W/key.hex"
expect 2 $BIN sign "$W/plain.gsav" "$W/plain.gsav" --key-file "$W/key.hex"
expect 2 $BIN sign "$W/plain.gsav" "$W/x.gsav"
expect 5 $BIN strip-signature "$W/signed.gsav" "$W/stripped.gsav"
expect 0 $BIN strip-signature "$W/signed.gsav" "$W/stripped.gsav" --key-file "$W/key.hex"
expect 0 $BIN verify "$W/stripped.gsav"
expect 0 $BIN fuzz "$W/plain.gsav" --iterations 300 --seed 7
expect 0 $BIN fuzz "$W/signed.gsav" --iterations 200 --seed 0x10 --key-file "$W/key.hex"
expect 0 $BIN fuzz "$W/plain.gsav" --iterations 300 --seed 9 --deep
expect 2 $BIN fuzz "$W/plain.gsav" --iterations 0 --seed 1
expect 2 $BIN fuzz "$W/plain.gsav" --iterations 10
echo "----"; if [ $fails -eq 0 ]; then echo "SMOKE PASS"; else echo "SMOKE FAIL: $fails"; fi
exit $fails

#!/usr/bin/env bash
# Self-test for check-unity-compat.sh using build/checks/Infra/compat-fixtures:
#   good/  valid C# lookalikes (comments, strings, identifiers) that must produce no findings
#   bad/   one file per rule, named <RULE>.cs, which must be flagged by exactly that rule
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHECK="$ROOT/scripts/check-unity-compat.sh"
FIX="$ROOT/build/checks/Infra/compat-fixtures"
fail=0

if ! out="$(GITHUB_ACTIONS=false bash "$CHECK" --core "$FIX/good" --no-asmdef --warnings-as-errors 2>&1)"; then
    echo "FAIL: good fixtures were flagged:"; echo "$out"; fail=1
fi

set +e
out="$(GITHUB_ACTIONS=false bash "$CHECK" --core "$FIX/bad" --no-asmdef -q 2>&1)"
rc=$?
set -e
if [[ $rc -ne 1 ]]; then echo "FAIL: expected exit 1 for bad fixtures, got $rc"; fail=1; fi

for f in "$FIX"/bad/*.cs; do
    rule="$(basename "$f" .cs)"
    if ! grep -Eq "/bad/${rule}\.cs:[0-9]+: (error|warning) \[${rule}\]" <<< "$out"; then
        echo "FAIL: rule $rule did not flag bad/$rule.cs"; fail=1
    fi
done
# No cross-rule hits: every finding in bad/<RULE>.cs must be for <RULE>.
while IFS= read -r line; do
    file_rule="$(sed -E 's#.*/bad/([A-Z_]+)\.cs:.*#\1#' <<< "$line")"
    hit_rule="$(sed -E 's#.*\[([A-Z_]+)\].*#\1#' <<< "$line")"
    if [[ "$file_rule" != "$hit_rule" ]]; then echo "FAIL: unexpected $hit_rule finding: $line"; fail=1; fi
done < <(grep -E '/bad/[A-Z_]+\.cs:[0-9]+: ' <<< "$out" || true)

if [[ $fail -eq 0 ]]; then
    echo "test-check-unity-compat: OK ($(ls "$FIX"/bad/*.cs | wc -l | tr -d ' ') rules, good fixtures clean)"
fi
exit $fail

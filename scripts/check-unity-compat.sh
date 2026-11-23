#!/usr/bin/env bash
# check-unity-compat.sh: enforce the Unity-compatibility and determinism rules of
# docs/ENGINEERING_CONTRACT.md (section 3) on Assets/GodSim/Core/**/*.cs, and validate the
# Unity assembly definitions.
#
# Usage: scripts/check-unity-compat.sh [--core DIR] [--no-asmdef] [--warnings-as-errors] [-q]
#   --core DIR             directory to scan (default: Assets/GodSim/Core)
#   --no-asmdef            skip the .asmdef checks (used by the self-test)
#   --warnings-as-errors   make WARN rules fail the run too
#   -q                     print violations only (no summary)
#
# Exit codes: 0 = clean, 1 = violations found, 2 = usage or environment error.
#
# The scanner lexes each file first: comments are removed and string/char literal contents are
# blanked, so "never use System.Random" in a comment or an @"..." string is not a violation.
# A reviewed exception can be suppressed on its own line with a trailing comment:
#     var t = DateTime.UtcNow; // godsim-compat: allow WALL_CLOCK <reason>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CORE_DIR="$ROOT/Assets/GodSim/Core"
CHECK_ASMDEF=1
WARN_AS_ERROR=0
QUIET=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --core) [[ $# -ge 2 ]] || { echo "error: --core needs a directory" >&2; exit 2; }
                CORE_DIR="$(cd "$2" && pwd)"; shift 2 ;;
        --no-asmdef) CHECK_ASMDEF=0; shift ;;
        --warnings-as-errors) WARN_AS_ERROR=1; shift ;;
        -q|--quiet) QUIET=1; shift ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "error: unknown argument: $1" >&2; exit 2 ;;
    esac
done

[[ -d "$CORE_DIR" ]] || { echo "error: core directory not found: $CORE_DIR" >&2; exit 2; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
CODE="$TMP/code.txt"   # lexed code, one line per source line
LOC="$TMP/loc.txt"     # matching "path<TAB>line"
RAW="$TMP/raw.txt"     # matching original source line
HITS="$TMP/hits.txt"

ERRORS=0
WARNINGS=0
GHA="${GITHUB_ACTIONS:-false}"

rel() { local p="$1"; printf '%s' "${p#"$ROOT"/}"; }

report() { # severity rule path line message source
    local sev="$1" rule="$2" path="$3" line="$4" msg="$5" src="$6"
    printf '%s:%s: %s [%s] %s\n' "$path" "$line" "$sev" "$rule" "$msg"
    [[ -n "$src" ]] && printf '    > %s\n' "$src"
    if [[ "$GHA" == "true" ]]; then
        local level=error; [[ "$sev" == "warning" ]] && level=warning
        printf '::%s file=%s,line=%s,title=%s::%s\n' "$level" "$path" "$line" "$rule" "$msg"
    fi
    if [[ "$sev" == "error" ]]; then ERRORS=$((ERRORS + 1)); else WARNINGS=$((WARNINGS + 1)); fi
}

# ---------------------------------------------------------------- collect + lex sources
FILES=()
while IFS= read -r -d '' f; do FILES+=("$f"); done < <(
    find "$CORE_DIR" -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | LC_ALL=C sort -z)

if [[ ${#FILES[@]} -eq 0 ]]; then
    echo "warning: no .cs files found under $(rel "$CORE_DIR")" >&2
    : > "$CODE"; : > "$LOC"; : > "$RAW"
else
    # POSIX awk (works with mawk, gawk, BSD awk). States: block comment, verbatim string, raw string.
    LC_ALL=C awk -v root="$ROOT/" -v code="$CODE" -v loc="$LOC" -v raw="$RAW" '
    FNR == 1 { inblock = 0; inverb = 0; inraw = 0; path = FILENAME; if (index(path, root) == 1) path = substr(path, length(root) + 1) }
    {
        line = $0; n = length(line)
        if (n > 0 && substr(line, n, 1) == "\r") { n--; line = substr(line, 1, n) }
        out = ""; i = 1
        while (i <= n) {
            c = substr(line, i, 1); c2 = substr(line, i, 2); c3 = substr(line, i, 3)
            if (inblock) { if (c2 == "*/") { inblock = 0; i += 2; out = out " " } else i++; continue }
            if (inverb)  { if (c2 == "\"\"") { i += 2; continue }
                           if (c == "\"") { inverb = 0; out = out "\"" }
                           i++; continue }
            if (inraw)   { if (c3 == "\"\"\"") { inraw = 0; out = out "\"\"\""; i += 3; while (substr(line, i, 1) == "\"") i++ }
                           else i++
                           continue }
            if (c2 == "//") break
            if (c2 == "/*") { inblock = 1; i += 2; continue }
            if (c3 == "\"\"\"") { out = out " __RAW_STRING_LITERAL__ "; inraw = 1; i += 3; while (substr(line, i, 1) == "\"") i++; continue }
            if (c3 == "$@\"" || c3 == "@$\"") { out = out "@\""; inverb = 1; i += 3; continue }
            if (c2 == "@\"") { out = out "@\""; inverb = 1; i += 2; continue }
            if (c == "\"" || c == "\047") {
                q = c; j = i + 1
                while (j <= n) { ch = substr(line, j, 1); if (ch == "\\") j += 2; else if (ch == q) break; else j++ }
                out = out q q; i = j + 1; continue
            }
            out = out c; i++
        }
        print out > code
        print path "\t" FNR > loc
        print line > raw
    }' "${FILES[@]}"
fi

# ---------------------------------------------------------------- rules
# Portable ERE (GNU and BSD grep): no \b or \s.
L='(^|[^A-Za-z0-9_])'   # left identifier boundary
R='([^A-Za-z0-9_]|$)'   # right identifier boundary
S='[[:space:]]'
MOD='(public|internal|private|protected|sealed|abstract|partial|readonly|static|unsafe|new|ref|file)'

# id | severity | message | regex
RULES=(
"UNITY_ENGINE|error|UnityEngine/UnityEditor in Core (GodSim.Core.asmdef has noEngineReferences)|${L}Unity(Engine|Editor)${R}"
"SYSTEM_RANDOM|error|System.Random / UnityEngine.Random: use ctx.Rng (PCG32 Rng)|${L}(System${S}*\.${S}*Random${R}|new${S}+Random${S}*\(|Random${S}*\.${S}*(Shared|Range|value|InitState)${R})"
"WALL_CLOCK|error|DateTime(Offset).Now/UtcNow/Today is non-deterministic: use SimTime|${L}DateTime(Offset)?${S}*\.${S}*(Now|UtcNow|Today)${R}"
"TICK_COUNT|error|Environment.TickCount is non-deterministic|${L}Environment${S}*\.${S}*TickCount(64)?${R}"
"GUID_NEWGUID|error|Guid.NewGuid is non-deterministic: derive ids from ctx.Rng or counters|${L}Guid${S}*\.${S}*NewGuid${R}"
"THREADING|error|threads/tasks/async are not allowed in Core|${L}(System${S}*\.${S}*Threading|Thread${S}*\.${S}*(Sleep|Start|CurrentThread|Yield|SpinWait)|new${S}+Thread${S}*\(|ThreadPool|Parallel${S}*\.${S}*(For|ForEach|Invoke)|Task${S}*\.${S}*(Run|Factory|Delay|WhenAll|WhenAny|Yield|FromResult|CompletedTask)|(Value)?Task${S}*<|async${S}+[A-Za-z_(]|await${S})"
"SYSTEM_TEXT_JSON|error|System.Text.Json is not available in Unity|${L}System${S}*\.${S}*Text${S}*\.${S}*Json${R}"
"FILE_SCOPED_NAMESPACE|error|file-scoped namespace (C# 10): use a block namespace|^${S}*namespace${S}+[^{]+;${S}*$"
"RECORD|error|record types (C# 9 records need IsExternalInit, C# 10 record struct) are not allowed|^${S}*(\[[^]]*\]${S}*)*(${MOD}${S}+)*record${S}+((struct|class)${S}+)?[A-Za-z_][A-Za-z0-9_]*${S}*([({<:;]|$)"
"INIT_ACCESSOR|error|init accessors are not supported by Unity: use a constructor or set|(^|[{;}])${S}*((private|protected|internal)${S}+)*init${S}*(;|\{|=>)"
"GLOBAL_USING|error|global using (C# 10) is not allowed|^${S}*global${S}+using${R}"
"RAW_STRING_LITERAL|error|raw string literal (C# 11) is not allowed|__RAW_STRING_LITERAL__"
"PARAM_NULL_CHECK|error|!! parameter null checking is not valid C#: use Guard/if checks|[A-Za-z0-9_]!!${S}*[,)=]"
"REQUIRED_MEMBER|error|required members (C# 11) are not allowed|(public|internal|protected|private)${S}+([a-z]+${S}+)*required${S}+[A-Za-z_]|${L}required${S}+(public|internal|protected|private)${R}"
"STRING_HASH|warning|string.GetHashCode is randomized per process (.NET) and differs in Mono: use Hashing.Fnv1a64 for seeds|(\"\"|ToString${S}*\(${S}*\)|[Nn]ame|[Kk]ey|[Tt]ext|[Ss]tr(ing)?|[Tt]ag|[Ll]abel|[Ss]eed[A-Za-z0-9_]*|StringComparer${S}*\.${S}*[A-Za-z]+)${S}*\.${S}*GetHashCode${S}*\(|[Ss]eed.*GetHashCode${S}*\(|GetHashCode${S}*\(.*[Ss]eed|[Rr]ng.*GetHashCode${S}*\("
)

for rule in "${RULES[@]}"; do
    id="${rule%%|*}"; rest="${rule#*|}"
    sev="${rest%%|*}"; rest="${rest#*|}"
    msg="${rest%%|*}"; re="${rest#*|}"
    grep -nE -- "$re" "$CODE" | cut -d: -f1 > "$HITS" || true
    [[ -s "$HITS" ]] || continue
    # Join hit line numbers with location + original source; honour suppressions.
    while IFS=$'\t' read -r path line src; do
        report "$sev" "$id" "$path" "$line" "$msg" "$src"
    done < <(awk -v id="$id" '
        FILENAME == ARGV[1] { want[$1] = 1; next }
        FILENAME == ARGV[2] { if (FNR in want) loc[FNR] = $0; next }
        (FNR in want) {
            if (index($0, "godsim-compat: allow " id) > 0) next
            s = $0; gsub(/\t/, " ", s); sub(/^[ ]+/, "", s); sub(/\r$/, "", s)
            if (length(s) > 160) s = substr(s, 1, 157) "..."
            print loc[FNR] "\t" s
        }' "$HITS" "$LOC" "$RAW")
done

# Raw U+2028 / U+2029 / U+0085 are line terminators for the C# compiler, even inside string or char
# literals ("Newline in constant"). Checked on the raw bytes, since the lexer blanks literal contents.
if [[ ${#FILES[@]} -gt 0 ]]; then
    while IFS= read -r hit; do
        path="${hit%%:*}"; rest="${hit#*:}"; line="${rest%%:*}"
        report error SOURCE_LINE_SEPARATOR "$(rel "$path")" "$line" \
            "raw U+2028/U+2029/U+0085 character: C# treats it as a line break; write \\u2028, \\u2029 or \\u0085" ""
    done < <(LC_ALL=C grep -nH -e $'\xe2\x80\xa8' -e $'\xe2\x80\xa9' -e $'\xc2\x85' -- "${FILES[@]}" 2> /dev/null || true)
fi

# ---------------------------------------------------------------- asmdef checks
if [[ "$CHECK_ASMDEF" -eq 1 ]]; then
    PY=""
    if command -v python3 > /dev/null 2>&1; then PY=python3; elif command -v python > /dev/null 2>&1; then PY=python; fi
    if [[ -z "$PY" ]]; then
        echo "error: python3 is required for the .asmdef checks (or pass --no-asmdef)" >&2
        exit 2
    fi
    ASMDEFS=()
    while IFS= read -r -d '' f; do ASMDEFS+=("$f"); done < <(
        find "$ROOT/Assets" -type f \( -name '*.asmdef' -o -name '*.asmref' \) -print0 2> /dev/null | LC_ALL=C sort -z)
    for f in ${ASMDEFS[@]+"${ASMDEFS[@]}"}; do
        # Same parser as `python3 -m json.tool`, but tolerant of a UTF-8 BOM (Unity accepts one).
        if ! err="$("$PY" -I -c 'import json,sys; json.load(open(sys.argv[1], encoding="utf-8-sig"))' "$f" 2>&1)"; then
            report error ASMDEF_JSON "$(rel "$f")" 1 "invalid JSON: ${err##*$'\n'}" ""
        fi
    done
    CORE_ASMDEF="$ROOT/Assets/GodSim/Core/GodSim.Core.asmdef"
    if [[ ! -f "$CORE_ASMDEF" ]]; then
        report error ASMDEF_CORE "$(rel "$CORE_ASMDEF")" 1 "missing: Core must be its own assembly with noEngineReferences: true" ""
    else
        if ! res="$("$PY" -I -c '
import json, sys
try:
    d = json.load(open(sys.argv[1], encoding="utf-8-sig"))
except Exception:
    sys.exit(0)  # already reported as ASMDEF_JSON
problems = []
if d.get("noEngineReferences") is not True:
    problems.append("noEngineReferences must be true")
if d.get("name") != "GodSim.Core":
    problems.append("name must be \"GodSim.Core\" (got %r)" % d.get("name"))
if problems:
    print("; ".join(problems)); sys.exit(1)
' "$CORE_ASMDEF" 2>&1)"; then
            report error ASMDEF_CORE "$(rel "$CORE_ASMDEF")" 1 "$res" ""
        fi
    fi
fi

# ---------------------------------------------------------------- summary
FAIL=0
[[ "$ERRORS" -gt 0 ]] && FAIL=1
[[ "$WARN_AS_ERROR" -eq 1 && "$WARNINGS" -gt 0 ]] && FAIL=1
if [[ "$QUIET" -eq 0 ]]; then
    printf 'check-unity-compat: scanned %d file(s) under %s: %d error(s), %d warning(s)\n' \
        "${#FILES[@]}" "$(rel "$CORE_DIR")" "$ERRORS" "$WARNINGS"
fi
exit "$FAIL"

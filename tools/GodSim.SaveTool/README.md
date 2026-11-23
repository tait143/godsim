# GodSim.SaveTool

Offline command-line tool for GodSim save files (`.gsav`): inspect, verify, compare, re-sign and fuzz.
It is a .NET 8 console app built on `build/GodSim.Core.csproj`, so it uses the same serializer, validator and
state hasher as the game. It is not Unity code.

```bash
cd tools/GodSim.SaveTool
dotnet run -c Release -- help
dotnet run -c Release -- verify ~/saves/slot1.gsav --key-file ~/.godsim/install.key --require-signature
```

## Commands

| Command | What it does |
|---|---|
| `info <file>` | Reads only the header (no payload parse): format/schema versions, flags, signed/compressed, game version, creation time, seed, tick and date (`SimTime.Format`), population, payload size. Header values are not verified. |
| `verify <file> [KEY] [--require-signature]` | Checks integrity (SHA-256 checksum, or HMAC-SHA256 with a key), runs the full parse and `StateValidator.Validate`, and prints the `StateHasher` hash. Exit code 0, 4 or 5. |
| `stats <file> [KEY] [--top N]` | Lists civilizations (name, race, age, population, settlements, buildings, techs, research), a settlement table (top N by population), buildings by category and the most common types, wars, chronicle highlights (importance 2 or higher) and the state hash. |
| `diff <a> <b> [KEY]` | Compares two saves for desync investigations: seed, tick, RNG state, entity counts, id allocators, per-civ deltas (matched by id), per-section state hashes (`StateHasher.ComputeSections`) and the final hash. Exit code 0 when the hashes are identical, 6 when they differ. |
| `replay-verify <save> <replay> --ticks N [KEY] [--expect-hash HEX]` | Loads the save with the replay header's config. Re-casts every replay command whose tick falls in `[saveTick, saveTick+N)` through `GodSimulation.TryCastPower` just before that tick runs, then runs N ticks. Compares the recorded checkpoints inside the window and prints the final hash. Exit code 6 on a checkpoint divergence or a mismatch with `--expect-hash`. |
| `sign <in> <out> --key-file <path> [--in-key-file <p>\|--in-key-hex <h>] [--force]` | Verifies and parses `<in>` (with `--in-key-*`, or with the output key when the input is signed), re-serializes it signed, reloads the result to prove the state hash is unchanged, then writes it atomically. |
| `strip-signature <in> <out> [KEY] [--force]` | Writes the same state unsigned (SHA-256 checksum). A signed input must be verified with KEY first, so stripping cannot launder a tampered file. |
| `fuzz <file> --iterations N --seed S [KEY] [--max-mutations K] [--deep] [--save-failures <dir>]` | Applies deterministic byte mutations (PCG32 `Rng`): bit flips, random bytes, truncation, insertion, deletion, duplication, interesting u32 values, reversal and fills. It asserts that `ReadHeader` and `Deserialize` only ever throw `SaveCorruptException`, and prints a summary with the top rejection reasons. `--deep` re-encodes the save uncompressed and unsigned and re-seals the SHA-256 checksum after each mutation, so mutations reach the payload parser. `--save-failures` writes each failing input (at most 50) for reproduction. |

`KEY` is `--key-file <path>` or `--key-hex <hex>`, never both. A key file may hold raw bytes (for example the
game's 32-byte `install.key`), hex text (optionally prefixed `hex:`) or `base64:<text>`. Keys must be 16 to 1024
bytes long.

Global options: `--verbose` (stack traces, simulation warnings and replay rejections on stderr) and `--help` / `-h`.
Options take `--name value` or `--name=value`. A lone `--` ends option parsing.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | OK. verify: valid. diff: identical. replay-verify: no divergence, and the hash matches `--expect-hash` if one was given. fuzz: no unexpected exception. |
| 1 | Internal error (unexpected exception; re-run with `--verbose`). |
| 2 | Usage error: unknown command or option, repeated option, missing value, wrong argument count, malformed number or key, wildcard in a path. |
| 3 | I/O error: file missing, unreadable, a directory or too large, output already exists (use `--force`), or output directory missing. |
| 4 | Corrupt save or replay: truncated, checksum mismatch, unsupported version, structurally invalid, or `StateValidator` issues. |
| 5 | Signature failure: signed file without a key, wrong key, signed file modified after signing, or an unsigned file under `--require-signature`. |
| 6 | Mismatch. diff: the saves differ. replay-verify: a checkpoint diverged or the hash differs from `--expect-hash`. |
| 7 | Fuzz failure: the loader threw something other than `SaveCorruptException`. |

## Security

- **Key material is never printed.** It is not echoed in errors (they name only the option or the path), and
  key arrays are wiped after use. `--key-hex` is visible in process listings, so prefer `--key-file`.
- **Bounded reads.** The file size is checked before reading, and the read aborts if the file changes while
  it is read. Limits: saves 64 MiB, replays 32 MiB, key files 4 KiB. Decompression is bounded by the
  serializer (`SaveOptions.MaxUncompressedBytes`).
- **Explicit paths only.** No globbing. A path containing `*` or `?` is rejected, and only the given files are
  read. Outputs are written to `<out>.savetool.tmp` and then moved into place. An existing output is never
  overwritten without `--force`, and input and output must be different files.
- **Sanitized output.** Every string taken from a save (civ, settlement and war names, chronicle text, the
  game version) is stripped of control and format characters before printing, so a crafted save cannot
  inject terminal escape sequences or fake output lines.
- **Clean errors.** Failures print a single `error: ...` line on stderr. A stack trace appears only with
  `--verbose`.
- **Signature classification.** For signed files the tool recomputes the HMAC itself (header || payload,
  with the tag at `SaveHeader.HeaderLength`) before parsing. That is how it tells a wrong key or tampering
  (exit 5) apart from payload corruption (exit 4).

## CI usage

```bash
savetool verify build/artifacts/smoke.gsav --require-signature --key-file ci.key   # 0 / 4 / 5
savetool fuzz tests/data/sample.gsav --iterations 2000 --seed 1 --deep              # 0 / 7
savetool replay-verify start.gsav run.grpl --ticks 960 --expect-hash 0x1234ABCD5678EF00
```

## Isolated build check

`build/checks/SaveTool/` compiles these sources without the full Core. It also has a smoke test that asserts
every exit code:

```bash
cd build/checks/SaveTool
./smoke.sh              # Contracts + stub Persistence + stub Composition
./smoke.sh persistence  # Contracts + REAL Persistence + stub Composition
dotnet build -p:SaveToolCheckMode=real   # against build/GodSim.Core.csproj
```

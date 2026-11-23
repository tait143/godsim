# GodSim Security Policy

This document covers how to report vulnerabilities, which versions receive fixes, the secure-coding
rules every contributor (human or agent) follows in this repository, and the checklist reviewers
apply to pull requests. The threat analysis behind these rules is in [THREAT_MODEL.md](THREAT_MODEL.md).

## 1. Reporting a vulnerability

- **Do not open a public issue** for a suspected vulnerability. Report it privately to the security
  team via the repository's private vulnerability reporting ("Security" tab, "Report a
  vulnerability") or the studio's security mailbox listed in the internal handbook.
- Include: affected version or commit, platform (Unity player, editor, or headless), reproduction
  steps or a proof-of-concept file (save, config, mod or replay), the impact you observed, and
  whether the issue is already public.
- Malicious sample files (saves, mods, replays) must be sent as password-protected archives, with the
  password sent separately.
- We follow coordinated disclosure. Please give us the response windows below before publishing.
  We credit reporters in release notes unless they ask us not to.
- Good-faith research on your own copies of the game is welcome. Do not attack other players,
  shared infrastructure or CI.

### Response targets (SLAs)

| Severity | Examples | Acknowledge | Triage and fix plan | Fix released |
|---|---|---|---|---|
| Critical | Code execution from a save, mod or replay. Path traversal that writes outside the save directory. CI secret exposure. | 24 h | 3 days | 7 days |
| High | Crash or hang triggered by a crafted file (DoS). Integrity (HMAC) bypass. Multiplayer cheating primitive. | 2 business days | 7 days | 30 days |
| Medium | Resource exhaustion that needs unusual input. Info leak of local paths or usernames. | 5 business days | 14 days | Next minor release |
| Low | Hardening gaps, defense-in-depth issues | 10 business days | Best effort | Next release train |

## 2. Supported versions

| Version | Status | Security fixes |
|---|---|---|
| `main` (development) | Active | Yes |
| Latest released minor (N) | Supported | Yes |
| Previous minor (N-1) | Maintenance | Critical and High only, for 6 months after N ships |
| Older releases | End of life | No. Please upgrade. |

Save files are versioned (`WorldState.SchemaVersion`). Loading old saves stays supported for N-1,
and every loader must reject unknown future versions instead of guessing.

## 3. Secure coding rules for this repository

These add to [ENGINEERING_CONTRACT.md](ENGINEERING_CONTRACT.md). Every one of them can block a PR.

### 3.1 Untrusted inputs (validate at the boundary, then trust the type)

| Input | Entry point | Required handling |
|---|---|---|
| **God commands** (UI, replay, future network) | `CommandQueue.TryEnqueue` | Install `Security.GodCommandValidator` as the queue validator. Never construct effects from a command that was not validated. The divine module re-validates at execution (TOCTOU). |
| **Config** (UI, CLI, files) | before world creation | `InputSanitizer.ClampConfig(config)` (it calls `SimConfig.Sanitize` and caps map area x races). Never allocate from raw config values. |
| **Player text** (settlement, civ, god names) | UI | `InputSanitizer.SanitizeName`. Store only the sanitized result. |
| **Seeds** (free text) | UI, CLI | `InputSanitizer.SanitizeSeedText`. |
| **Save slot names** and file paths | Persistence | `InputSanitizer.IsValidSlotName` / `SanitizeSlotName`, plus `Persistence.SaveFileStore` path containment. Never concatenate user text into paths. |
| **Saves and replays** | `Persistence.SaveSerializer.Deserialize` | Check the version, verify the HMAC, and bound every length and count before allocating. Treat the payload as hostile even when the HMAC is valid (the key ships with the client). After load, run `IntegrityChecker.Check`. `IntegritySystem` also does this at `Initialize`. |
| **Content and mods** | catalog registration | `ContentValidator.Analyze(db)` must report 0 errors before `db.Freeze()`. Content is data only. It never contains code. |
| **Network** (future) | transport layer | Authenticate the peer. Map the peer to a fixed `GodCommand.Source` key on the server side (`"network:<peerId>"`). Never trust a client-supplied `Source` or `Sequence`. |

General rules:

- **Bound everything.** Strings, arrays, counts, radii, loop iterations and recursion depth all need
  explicit limits. Check the length *before* scanning or copying. Prefer an allow-list to a deny-list.
- **Never echo untrusted text** into logs, chronicle entries or rejection reasons. Echo ids, enum
  names and numbers instead. Validators in this repo are written this way.
- **Validate enums.** Use `Enum.IsDefined` or `(int)value < Counts.X` before using a value as an
  array index. An undefined byte from a save or mod is an out-of-range index waiting to happen.
- **No NaN or Infinity in state.** Use `MathUtil.Sanitize/Clamp` on every computed float that is
  persisted. Comparisons must fail closed: write `!(mana >= cost)`, not `mana < cost`.
- **Never throw out of `Tick` on bad data.** Repair or skip the bad entry, and log ids, not content.
- **Mutations go through `WorldOps`.** The only exception is `IntegrityChecker.Repair`, which fixes
  links that are already broken, using the narrowest change possible.

### 3.2 Determinism is an integrity property

Saves, replays and any future lockstep multiplayer depend on bit-exact simulation. A determinism
bug is a desync, and a desync can be exploited.

- All randomness comes from `ctx.Rng`. Do not use `System.Random`, `Guid.NewGuid`, `DateTime.Now`,
  `Environment.TickCount` or `string.GetHashCode()`. Use `Hashing.Fnv1a64` for hashing.
- Never let `Dictionary`/`HashSet` iteration order affect a decision. Iterate `EntityStore.Items`
  by index. Sort keys (ordinal) before acting on them.
- Rate limiting uses simulation ticks only, never the wall clock (`RateLimiter`).
- Text that enters state must be sanitized *before* it reaches the simulation. Unicode tables differ
  between runtimes, so the stored sanitized string becomes the source of truth.
- No threads, `async`, or timers in Core.

### 3.3 No reflection or dynamic code

- Do not use `System.Reflection` (`Activator.CreateInstance`, `Type.GetType(string)`, `Assembly.Load*`),
  `System.Reflection.Emit`, `dynamic`, or expression compilation. Do not use
  `BinaryFormatter`/`NetDataContractSerializer`/`SoapFormatter` or any polymorphic deserializer
  that resolves type names from data.
- Mods are data only (definitions validated by `ContentValidator`). There is no mod scripting
  runtime. Adding one requires a separate security design review.
- Persistence uses explicit, versioned, hand-written binary readers.

### 3.4 Dependencies and supply chain

- Pin every dependency to an exact version: NuGet `Version="x.y.z"` (no floating `*`), Unity
  packages in `Packages/manifest.json` with exact versions and a committed `packages-lock.json`.
- New dependencies need a reviewer from the security owners. Prefer the BCL. Core may only use
  `System`, `System.Collections.Generic`, `System.Text`, `System.IO`,
  `System.Security.Cryptography` and `System.IO.Compression` (plus `System.Globalization` for
  Unicode categories in the sanitizer).
- Restore from the approved feeds only (nuget.org and the Unity registry). Dependabot or Renovate
  alerts are triaged within the SLA above.
- Never commit binaries (`.dll`, `.exe`) except vetted Unity plugins listed in the plugin manifest.

### 3.5 CI/CD and secrets

- Never commit secrets: API keys, signing keys, Steam or console credentials, HMAC production keys.
  Secret scanning (push protection) is enabled. If a secret leaks, **rotate it first** and then
  clean up the history.
- CI secrets live in the CI secret store, scoped to protected branches and environments. Jobs
  triggered from forks (`pull_request` from forks) never receive secrets. Never use
  `pull_request_target` with a checkout of untrusted code.
- Workflows pin third-party actions by **full commit SHA** and declare least-privilege
  `permissions:` (default `contents: read`).
- Release signing and store uploads run only from protected tags, in a dedicated environment that
  requires manual approval.
- Build logs must not print environment variables or config dumps that may contain secrets.

## 4. Security review checklist for PRs

Copy into the PR description for any change touching Core, Persistence, Security, the Unity input
layer, build files or CI.

**Inputs and validation**
- [ ] Every new external input is listed in section 3.1 and goes through the required sanitizer or validator.
- [ ] Lengths and counts are checked before allocation, copying or scanning. No unbounded loops or recursion over data.
- [ ] Enum values from data are range-checked before indexing arrays.
- [ ] No untrusted text is echoed in logs, reasons or chronicle entries.
- [ ] Integer arithmetic on untrusted values cannot overflow (use `long` or pre-clamp). Float comparisons fail closed on NaN.

**State and determinism**
- [ ] No NaN or Infinity can enter `WorldState`. New persistent floats get a default in `IntegrityChecker` if relevant.
- [ ] Entity and link mutations go through `WorldOps`. Iteration skips `!IsAlive` / `IsDestroyed`.
- [ ] No wall clock, `System.Random`, hash-order dependence, threads or `string.GetHashCode()` in Core.
- [ ] `Tick` cannot throw on malformed state.

**Commands and rate limits**
- [ ] New god powers have a `GodPowerDefinition` with sane radius, cost and cooldown. Wrath powers cost more than 0. `ContentValidator` passes.
- [ ] Execution re-validates the dequeued command and handles rejection gracefully.

**Persistence and files**
- [ ] File paths are built only from validated slot names inside the save root. No `..`, absolute paths or device names.
- [ ] Deserialization bounds every count and string and rejects unknown versions.

**Code and dependencies**
- [ ] No reflection, dynamic loading, `BinaryFormatter` or type-name-driven deserialization.
- [ ] New or changed dependencies are pinned exactly and approved. The lock file is updated.
- [ ] No secrets, tokens or production keys in code, tests, fixtures or logs.

**CI**
- [ ] Workflow changes keep least-privilege `permissions:` and SHA-pinned actions, and never expose secrets to fork PRs.

**Tests**
- [ ] Negative tests cover the new input paths (malformed, oversized, NaN, boundary values).

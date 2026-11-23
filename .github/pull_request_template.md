## Summary

<!-- What does this change do and why? Link the issue (e.g. "Closes #123"). -->

**Module(s):** <!-- e.g. Population, Economy/Services, Persistence -->

## Testing

<!-- How was this verified? Paste commands and results. -->

- [ ] `bash scripts/check-unity-compat.sh` passes
- [ ] `cd build/checks/<Module> && dotnet test -nologo -v q --disable-build-servers` passes (0 errors)
- [ ] `bash scripts/run-tests.sh` passes (full solution + headless smoke), or the reason it does not is explained below
- [ ] New or changed behaviour has NUnit tests in `Assets/GodSim/Tests/EditMode/<Module>/` (NUnit 3 basic API only)

## Determinism impact

- [ ] None: no change to simulation output for a given seed
- [ ] Changes simulation output for a given seed (expected: snapshots/replays recorded before this PR will differ). Explain:

Checklist:

- [ ] All randomness goes through `ctx.Rng`; no `System.Random`, `Guid.NewGuid`, `DateTime.Now`, `Environment.TickCount` or `string.GetHashCode()`
- [ ] No decisions depend on `Dictionary`/`HashSet` iteration order; entity stores are iterated by index
- [ ] No threads, tasks or async in Core
- [ ] No NaN/Infinity can enter state (values clamped with `MathUtil.Clamp`/`Sanitize`)

## Save-format impact

- [ ] None
- [ ] Save format changed: version bumped, old saves migrate or are rejected with a clear error, and round-trip tests are updated

## Security checklist

- [ ] Player/god commands are validated by Security (`ICommandValidator`) and re-validated when executed
- [ ] Untrusted input (save files, mods/content, names) is size-limited and sanitized; no unbounded allocations
- [ ] File paths are confined to the save directory (no path traversal); no new `System.IO` access outside `SaveFileStore`
- [ ] No secrets, tokens or personal data in code, tests, logs or fixtures
- [ ] Workflow changes keep least-privilege `permissions:` and pass untrusted values through `env:`, not `${{ }}` in `run:`
- [ ] No `Core/Contracts/` edits (or the lead approved the contract change)

## Notes for reviewers

<!-- Risky areas, follow-ups, screenshots of the WebViewer if relevant. -->

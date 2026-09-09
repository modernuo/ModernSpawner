# ModernUO prerequisites

Engine changes ModernSpawner depends on. Each goes upstream as a ModernUO pull request from a branch off
`main` (worktrees under `C:\Repositories\ModernUO`). While a PR is open the `ModernUO/` submodule is pinned
to the PR head commit; once it merges the submodule moves back to `main` and the row moves to "Merged".
Performance is the gate for every change: nothing may add allocations or dispatch on per-tick or
per-movement paths without a measurement, because shards run 12k+ spawners.

## Open

| PR | Change | Why ModernSpawner needs it | Submodule pin |
|---|---|---|---|
(none)

## Merged

| PR | Change | Why ModernSpawner needs it |
|---|---|---|
| [#2619](https://github.com/modernuo/ModernUO/pull/2619) | `BaseSpawner.Dto.cs`: `private protected` DTO helpers → `protected` | `ModernSpawner.ToDto()` lives in another assembly and needs the `Dto*` helpers and `BoundsFromHomeRange` |

## Planned (see `architecture.md` §4–§5, §11; decisions D1, D2, D3, D11, D12)

- Abstract entry ownership on `BaseSpawner` with concrete lists per branch and a transient legacy-entry
  carrier for the save migration; explicit `ClearEntries`/`ReplaceEntries`/`CopyEntriesTo`. General
  streamlining of `BaseSpawner`/`Spawner` toward an agnostic base is in scope if it costs nothing at runtime.
- `Spawner` extensibility so `ModernSpawner` can derive from it (D11): virtual/hookable spiral scan and
  positioning, entry factory, DTO subtype support.
- Per-entry `Enabled` flag on `SpawnerEntry` (D12): default true, `[SaveFlag]`-style omission in binary and
  JSON when true, skipped by weighted selection, live spawns untouched, exposed in `SpawnerGump`.
- Virtual hooks: `OnStarted`/`OnStopped`, `OnBeforeSpawn(entry)`, `OnConfigureSpawned(entry, spawned)`,
  `OnSpawned(entry, spawned)`, `GetSpawnPosition(entry, spawned, map)`, `OnSpawnedDeath(entry, spawned, killer)`
  called from `BaseCreature.OnDeath` before the spawner link is cleared.
- `SkillEvents.SkillUsedEvent` raised from `SkillCheck` (D3).
- `TestServerInitializer` usable from an external test assembly (or a public variant that takes an assembly list).
- `[ImportSpawners`: GUID-based replacement, preserve `running`, no unconditional `Respawn()`.
- Deferred: sector-range movement subscription for proximity triggers wider than 24 tiles.

## Local branch note

`feat/spawner-stj-migration` in the ModernUO repo (worktree `.wt-spawner-stj`) is no longer the integration
branch. Its pre-reset tip is tagged `backup/spawner-stj-migration-2026-09-08`; the branch can be deleted.

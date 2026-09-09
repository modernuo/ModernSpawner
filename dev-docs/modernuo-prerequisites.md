# ModernUO prerequisites

Engine changes ModernSpawner depends on. Each goes upstream as a ModernUO pull request from a branch off
`main` (worktrees under `C:\Repositories\ModernUO`). While a PR is open the `ModernUO/` submodule is pinned
to the PR head commit; once it merges the submodule moves back to `main` and the row moves to "Merged".
Performance is the gate for every change: nothing may add allocations or dispatch on per-tick or
per-movement paths without a measurement, because shards run 12k+ spawners.

## Open

| PR | Change | Why ModernSpawner needs it | Submodule pin |
|---|---|---|---|
| [#2621](https://github.com/modernuo/ModernUO/pull/2621) | Subclass-owned entries (`BaseSpawner` v13 / `Spawner` v2 contract: `Entries`, `EntrySpan`, `CreateEntry`, `AddEntryCore`, `RemoveEntryCore`, `ClearEntriesCore`, `AdoptEntries`, `CloneEntry`, `TransferSpawned`, `RemoveAllEntries`, `CopyEntriesTo`, `RebuildSpawned`), lifecycle hooks (`OnStarted/OnStopped/OnBeforeSpawn/OnConfigureSpawned/GetSpawnPosition(entry,…)/OnSpawned/OnSpawnedDeath` + `NotifySpawnedDeath` from `BaseCreature.OnDeath`), `SpawnerEntry` v2 `Disabled` flag with gump toggle, DTO records own `entries`, save migration with v12 fixtures | D1/D11/D12: `ModernSpawner : Spawner` owns `List<ModernSpawnerEntry>` where `ModernSpawnerEntry : SpawnerEntry`; every stock path works on it; kill trigger needs the death hook | still `main` — ModernSpawner has not been ported to the new contract yet (its `ToDto`/`Entries` usage does not compile against the PR); pin moves when the port starts |

## Merged

| PR | Change | Why ModernSpawner needs it |
|---|---|---|
| [#2619](https://github.com/modernuo/ModernUO/pull/2619) | `BaseSpawner.Dto.cs`: `private protected` DTO helpers → `protected` | `ModernSpawner.ToDto()` lives in another assembly and needs the `Dto*` helpers and `BoundsFromHomeRange` |

## Planned (see `architecture.md` §4–§5, §11; decisions D1, D2, D3, D11, D12)

- (In PR #2621) entry ownership, hooks, `Disabled` flag, DTO per-record entries, save migration.
- `SkillEvents.SkillUsedEvent` raised from `SkillCheck` (D3).
- `TestServerInitializer` usable from an external test assembly (or a public variant that takes an assembly list).
- `[ImportSpawners`: GUID-based replacement, preserve `running`, no unconditional `Respawn()`.
- Deferred: sector-range movement subscription for proximity triggers wider than 24 tiles.

## Generator follow-up (SerializationGenerator repo)

- **Cross-assembly derived sub-objects lose `MarkDirty()` in generated setters.** When a
  `[SerializationGenerator]` class derives from a generated class in *another assembly*, the base's
  private `[DirtyTrackingEntity]` field is not visible in metadata, so the generator emits setters with
  no dirty call (SG3019). Workaround adopted in ModernUO PR A: `SpawnerEntry` exposes
  `protected BaseSpawner Parent`, and a subclass declares `[DirtyTrackingEntity] private BaseSpawner Owner => Parent;`.
  `ModernSpawnerEntry` must do the same. Proper fix: the generator should use an accessible `MarkDirty()`
  on the base type when one exists. Not blocking.

## Local branch note

`feat/spawner-stj-migration` in the ModernUO repo (worktree `.wt-spawner-stj`) is no longer the integration
branch. Its pre-reset tip is tagged `backup/spawner-stj-migration-2026-09-08`; the branch can be deleted.

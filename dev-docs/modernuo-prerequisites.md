# ModernUO prerequisites

Engine changes ModernSpawner depends on. Each goes upstream as a ModernUO pull request from a branch off
`main` (worktrees under `C:\Repositories\ModernUO`). While a PR is open the `ModernUO/` submodule is pinned
to the PR head commit; once it merges the submodule moves back to `main` and the row moves to "Merged".
Performance is the gate for every change: nothing may add allocations or dispatch on per-tick or
per-movement paths without a measurement, because shards run 12k+ spawners.

## Open

(none)

## Merged

| PR | Change | Why ModernSpawner needs it |
|---|---|---|
| [#2619](https://github.com/modernuo/ModernUO/pull/2619) | `BaseSpawner.Dto.cs`: `private protected` DTO helpers → `protected` | `ModernSpawner.ToDto()` lives in another assembly and needs the `Dto*` helpers and `BoundsFromHomeRange` |
| [#2621](https://github.com/modernuo/ModernUO/pull/2621) | Subclass-owned entries (`BaseSpawner` v13 / `Spawner` v2 owner contract), lifecycle hooks + `NotifySpawnedDeath`, `SpawnerEntry` v2 `Disabled`, DTO records own `entries`, save migration | D1/D11/D12: `ModernSpawner : Spawner` owns `List<ModernSpawnerEntry>` with `ModernSpawnerEntry : SpawnerEntry`; kill trigger via the death hook. The ModernSpawner side is ported in [ModernSpawner #1](https://github.com/modernuo/ModernSpawner/pull/1); submodule at `a52ce6ef7` |
| [#2636](https://github.com/modernuo/ModernUO/pull/2636) | `SkillEvents.SkillUsed` (`Action<Mobile, Skill, bool>`, `Server.Misc`) raised once per attempt from the four `Mobile_SkillCheck*` handlers; `InternalsVisibleTo("ModernSpawner.Tests")` on `Server.csproj` | D3 skill triggers subscribe cross-assembly; the test fixture seeds `Core._now`; submodule at `309fcfeb2` |
| [#2640](https://github.com/modernuo/ModernUO/pull/2640) | `BaseSpawner.OnTick` made `virtual`; `SpawnerDto`/`ApplyDto` carry the `group` flag | D2 needs a tick gate that does not also gate manual `Spawn()` — the non-virtual `OnTick` called the same virtual `Spawn()` every manual path uses; `group` was binary-persisted but absent from the DTO. Submodule at `c02909e2c` |

## Planned (see `architecture.md` §4–§5, §11; decisions D1, D2, D3, D11, D12)

- `[ImportSpawners`: GUID-based replacement, preserve `running`, no unconditional `Respawn()`.
- Deferred: sector-range movement subscription for proximity triggers wider than 24 tiles.

## Generator follow-ups (SerializationGenerator repo)

- [#58](https://github.com/modernuo/SerializationGenerator/issues/58) cross-assembly derived sub-objects lose `MarkDirty()`; [#57](https://github.com/modernuo/SerializationGenerator/issues/57) `[CanBeNull]` on a collection leaks onto its elements (blocks a lazy on-disk entry list until fixed with a wire-format bump for affected ModernUO types).

## Local branch note

`feat/spawner-stj-migration` in the ModernUO repo (worktree `.wt-spawner-stj`) is no longer the integration
branch. Its pre-reset tip is tagged `backup/spawner-stj-migration-2026-09-08`; the branch can be deleted.

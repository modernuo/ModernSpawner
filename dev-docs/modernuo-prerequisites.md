# ModernUO prerequisites

Engine changes ModernSpawner depends on that are not yet in ModernUO `main`. They live on the
`feat/spawner-stj-migration` branch of ModernUO, which the `ModernUO/` submodule tracks. Each entry
should be removed once the change lands in main and the submodule pointer moves.

| Commit | Change | Why ModernSpawner needs it |
|---|---|---|
| `79e3a8e34` | `BaseSpawner.Dto.cs`: `private protected` DTO helpers → `protected` | `ModernSpawner.ToDto()` lives in another assembly and needs `DtoName`, `DtoWalkingRange`, `DtoSpawnPositionMode`, `DtoMaxSpawnAttempts`, `DtoHomeRange`, `BoundsFromHomeRange`. |

## Known gaps (no change yet)

- **Extended area movement.** Proximity triggers wider than the 24-tile `OnMovement` radius need a
  sector-range movement subscription. `ModernSpawner.SetExtendedTriggerBounds` is a stub.
- **Entries ownership.** `BaseSpawner` owns `List<SpawnerEntry> Entries` and a non-virtual `AddEntry`.
  ModernSpawner keeps a parallel `List<ModernSpawnerEntry>` and hides `AddEntry`. The archived plan
  proposed abstracting entries in ModernUO; that never landed.

# ModernSpawner

Advanced spawner engine for [ModernUO](https://github.com/modernuo/ModernUO), built to replace XmlSpawner.

**Status: pre-release, under active rebuild.** The code compiles and 419 unit tests pass, but the runtime
spawn path has known structural defects and the staff gumps are broken. Do not deploy on a live shard yet.
See `dev-docs/product-spec.md` for the target and `docs/feature-audit.md` (local, uncommitted) for the
current state.

## What it adds over the stock ModernUO `Spawner`

- Entries with per-entry scripts, delays, subgroups, positioning rules and loot templates
- Sequential and Group spawn cycles with hold/advance/reset
- Triggers: proximity, speech, kill, skill, game-time and wall-clock windows, external
- One compiled script language for spawner and entry hooks and entry conditions
- Named positioning rules on top of ModernUO's own positioning
- Loot templates loaded from data files
- Import of XmlSpawner `.xml` saves with a migration report
- Lossless round trip through ModernUO's `[ExportSpawners` / `[ImportSpawners`

## Requirements

- .NET 10 SDK (see `global.json`)
- ModernUO, consumed as the `ModernUO/` git submodule tracking ModernUO `main`. Engine changes
  ModernSpawner needs are upstreamed as ModernUO pull requests; while one is open the submodule may be
  pinned to its head commit. They are listed in `dev-docs/modernuo-prerequisites.md`.

## Building

```sh
git clone --recurse-submodules https://github.com/modernuo/ModernSpawner.git
cd ModernSpawner
dotnet build ModernSpawner.slnx
dotnet test Projects/ModernSpawner.Tests
```

`dotnet build -c Analyze` runs the ModernUO analyzer rule set on this project only.

## Layout

| Path | Contents |
|---|---|
| `Projects/ModernSpawner/` | The engine (`Server.Engines.ModernSpawner`) |
| `Projects/ModernSpawner.Tests/` | xunit tests |
| `Projects/ModernSpawner.Benchmarks/` | BenchmarkDotNet, standalone |
| `ModernUO/` | Submodule |
| `dev-docs/` | Living docs: product spec, architecture, XmlSpawner migration requirements, ModernUO prerequisites |
| `docs/` | Gitignored working notes, reviews, and historical material |

## Using it on a shard

Not yet. The intended v1 path is source-level: add this repository as a submodule beside (or inside) your
ModernUO checkout and add `Projects/ModernSpawner/ModernSpawner.csproj` to your solution. A prebuilt DLL
dropped into `Distribution/Assemblies` is a later option once ModernUO main contains every prerequisite.

## Contributing

Read `CLAUDE.md` first: it points at ModernUO's coding rules, dev-docs and opt-in Claude skills, which all
apply here verbatim. Engine changes go upstream as ModernUO pull requests, never into this repo.

## Origin and license

Imported from the earlier `XmlSpawner-for-Modernuo` repository (branch `kb/modern_spawner`, commit `2b5193b`),
which has since been retired from GitHub. The legacy XmlSpawner sources it carried are not included.
GPL-3.0, see `LICENSE`.

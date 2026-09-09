# ModernSpawner

Advanced spawner engine for [ModernUO](https://github.com/modernuo/ModernUO): scripted spawns, triggers,
positioning rules, loot templates, and an importer for XmlSpawner files.

This README is a placeholder. The full README will be written once the product and architecture specs in
`dev-docs/` are complete.

## Building

```sh
git clone --recurse-submodules <this repo>
dotnet build ModernSpawner.slnx
dotnet test Projects/ModernSpawner.Tests
```

ModernUO is consumed as the `ModernUO/` submodule, pinned to the `feat/spawner-stj-migration` support branch.

## Origin

Imported from `modernuo/XmlSpawner-for-Modernuo`, branch `kb/modern_spawner`, commit `2b5193b`.
The legacy XmlSpawner sources that repository also carried are not included here.

## License

GPL-3.0. See `LICENSE`.

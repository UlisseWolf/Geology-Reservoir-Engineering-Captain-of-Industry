# Geology & Reservoir Engineering

A geology and subsurface-resources mod for [Captain of Industry](https://coigame.com), built as
a shared foundation layer for other mods rather than a standalone content pack. It adds
geothermal energy and a general-purpose underground injection system, and is designed for other
mods to depend on and extend — in the same spirit as `worldgen-plus-plus` or `recipes-plus-plus`.

- **Geology** — new deposit types registered as virtual terrain resources, generated as part of
  normal map/scenario data and selectable in the in-game map editor like any vanilla resource.
- **Reservoir engineering** — the machines and runtime systems that extract from, and inject
  back into, those deposits.

Nearly everything is built on the game's public modding API (`ProtoRegistrator`, `IMod`
lifecycle hooks, dependency injection). A few specific features have no supported API and use
Harmony — see [How this mod uses Harmony](#how-this-mod-uses-harmony).

## Features

### Geothermal energy

Three deposit tiers — High, Medium, and Low enthalpy — each with a dedicated extraction well and
its own color (red / orange / gold) and name in the resource list, distinct from the vanilla
steam products they're backed by:

| Tier            | Backing product      | Color  |
|------------------|-----------------------|--------|
| High enthalpy    | High-pressure steam   | Red    |
| Medium enthalpy  | Low-pressure steam    | Orange |
| Low enthalpy     | Depleted steam        | Gold   |

All three wells are listed under a new "Geothermal" subcategory of the vanilla Power production
toolbar menu.

### Underground water injection

A dedicated pump recharges geothermal reservoirs and the vanilla Groundwater deposit, consuming
water and auto-stopping once the target deposit is full. Geothermal and Groundwater recharge at
different, independently-tuned rates (see
[Design notes](#design-notes-and-known-limitations)). The pump can't recharge crude oil or
Natural Gas — those have their own dedicated pumps, below.

The vanilla Groundwater Pump is also moved alongside it, into a new "Groundwater" subcategory of
the "Water" menu (Harmony).

### Oil recovery

A second pump offers five recovery techniques directly on the vanilla crude oil deposit:

- **Enhanced oil recovery** — CO₂ gas injection
- **Hydraulic fracturing** — seawater-based fracturing fluid
- **Thermal EOR** — steam injection (two recipes, High and Super-pressurized steam)
- **Acid stimulation** — matrix acidizing, improving near-wellbore permeability

All five recharge the deposit toward its existing capacity, at a slower, separately-tuned rate
than geothermal/groundwater. Listed under a new "Oil wells" subcategory alongside the vanilla
Oil Pump (also reassigned there - Harmony).

### Natural gas

A new fluid product distinct from vanilla Fuel Gas: Fuel Gas is refined (`CrudeOil → ... →
FuelGas`), Natural Gas is the raw, unrefined gas as extracted from the ground.

- **Deposits** co-located with every crude oil deposit, on all six built-in maps, on
  custom/downloaded maps, in the map editor, and retrofitted into existing saves (Harmony) —
  representing associated gas, the most common real-world condition for an oil field.
- **Extraction** via a dedicated well, built in the same deposit radius as a vanilla Oil Pump, so
  both can run simultaneously on a co-located site.
- **Treatment**: a Chemical Plant recipe converts Natural Gas into vanilla Fuel Gas, making it
  usable anywhere Fuel Gas already is. It can also be burned directly, untreated, in a Flare or
  gas-fired Boiler.
- **Underground storage**: a third pump offers two independent recipes on the deposit —
  injecting treated Fuel Gas for storage, or Low-pressure steam for thermal enhanced recovery.
  Low-pressure steam is produced by the medium enthalpy geothermal well; the low enthalpy well
  produces Depleted steam, which this pump does not accept.
- **World Map**: a Natural Gas Rig, using the same public API the vanilla Oil Rig already uses
  (see [WorldGen++ compatibility](#worldgen-compatibility)).

### Cargo ship fuel

Fuel Gas and Natural Gas are both available as alternative cargo ship fuels (Harmony),
alongside Diesel/Heavy Oil/Hydrogen, compatible with each other but not the liquid/hydrogen
options. Fuel Gas matches Diesel's consumption and pollutes less (65%); Natural Gas consumes 30%
more and pollutes more (140%), reflecting untreated gas burning less cleanly.

### Power generation

Two electricity generators reusing the vanilla Diesel Generator II's model and stats — one
burning Fuel Gas, one burning Natural Gas (30% more fuel, 30% more pollution). Listed under a
new "Electric generators" subcategory alongside the vanilla Diesel Generator, also moved there
(Harmony).

### Research

| Node                          | Unlocks                          | Depends on |
|--------------------------------|-----------------------------------|------------|
| Geothermal extraction          | The three geothermal wells       | Underground water injection, Power generation II, Water recovery, Power generation III (vanilla) |
| Enhanced oil recovery          | The oil injection pump + one recipe | CO2 recycling (vanilla) |
| Hydraulic fracturing           | The oil injection pump + one recipe | Thermal desalination (vanilla) |
| Thermal enhanced oil recovery  | The oil injection pump + two recipes | Super heated steam (vanilla) |
| Acid stimulation               | The oil injection pump + one recipe | Sulfur processing (vanilla) |
| Natural gas extraction         | The natural gas well, treatment/Flare/Boiler/thermal gas recovery recipes, both electricity generators | Hydrogen production (vanilla) |
| Underground gas storage        | The natural gas injection pump + one recipe | Natural gas extraction |

### Languages

English, Italian, French, Spanish, German, and Portuguese. See
[Localization](#localization) below.

## How this mod uses Harmony

Four independent uses:

1. **Toolbar category reassignment** (reflection only, no method patch) — the vanilla Groundwater
   Pump, Oil Pump, and Diesel Generator (both tiers) are moved into this mod's own subcategories.
   There's no supported API to change a prototype's toolbar category after construction.
2. **Cargo ship fuel options** (reflection only, no method patch) — `CargoShipProto.AvailableFuels`
   is `readonly`; this mod appends two entries to the existing array on every ship tier.
3. **Natural Gas deposit placement** (two method patches) — every built-in map hard-codes its
   deposits in a private method with no supported extension point, so this mod patches it on all
   six maps. A second patch covers the map editor and custom/downloaded maps, which place
   deposits through a different, shared code path.
4. **Retrofit for existing saves** — a save generated before this mod (or before a given
   mechanism existed) doesn't re-run deposit generation on load, so this mod reaches into the
   live, already-deserialized resource manager once per session and adds any missing Natural Gas
   deposits directly. This is the most invasive mechanism in the mod: **back up your save before
   loading it with this version installed.**

Because these reach into internal implementation details rather than documented APIs, they're
more likely than the rest of the mod to break on a game update.

## Requirements

- Captain of Industry, version 0.8.6 or later.
- .NET Framework 4.8 SDK (for building from source).

[Harmony](https://github.com/pardeike/Harmony) (`0Harmony.dll`) is bundled in `Libs/`; no
separate installation step is needed.

## Installation

Download the release archive and extract it into your Captain of Industry `Mods` folder:

```
Mods/
  GeologyReservoirEngineering/
    manifest.json
    GeologyReservoirEngineering.dll
    0Harmony.dll
    Translations/
    AssetBundles/
```

The folder name must match the mod's manifest `id` (`GeologyReservoirEngineering`).

## Building from source

1. Set the `COI_ROOT` environment variable to your Captain of Industry installation directory,
   or create an `Options.user` file in the project root overriding `GameDir`.
2. Build with `dotnet build -c Release`, or open the project in your IDE of choice.

The build automatically deploys the compiled DLL, `Libs/0Harmony.dll`, `manifest.json`,
`Translations/`, and `AssetBundles/` to
`%APPDATA%\Captain of Industry\Mods\GeologyReservoirEngineering\`.

## Project structure

```
Source/
  GeologyReservoirEngineeringMod.cs   Mod entry point (IMod implementation)
  ModIds.cs                           Prototype IDs owned by this mod
  ModTranslation.cs                   JSON translation loader
  Data/
    ProductsData.cs                   Geothermal/Natural Gas deposit registration
    ToolbarCategoriesData.cs          Toolbar subcategories
    VanillaCategoryFixupData.cs       Reassigns vanilla machines' categories (Harmony, reflection)
    ShipFuelData.cs                   Adds Fuel Gas/Natural Gas as cargo ship fuel (Harmony, reflection)
    NaturalGasMapPatch.cs             Co-locates Natural Gas with crude oil deposits (Harmony, method patch)
    WorldMapData.cs                   Natural Gas Rig on the World Map
    MachinesData.cs                   Extraction wells and the three injection pumps
    PowerGeneratorsData.cs            Fuel Gas / Natural Gas electricity generators
    ResearchData.cs                   Research tree nodes
  Runtime/
    GeologyRegenManager.cs            Deposit recharge logic
    InjectionPumpProto.cs             Custom machine prototype
    InjectionPump.cs                  Custom machine entity
Translations/
  en.json, it.json, fr.json, es.json, de.json, pt.json
Assets/Geothermal/
  NaturalGas.png                      Source icon (see "Custom assets")
AssetBundles/
  geothermal_54ee, geothermal_54ee.manifest, mafi_bundles.manifest
Libs/
  0Harmony.dll
manifest.json
LICENSE
```

## Custom assets

Natural Gas uses a custom icon — a recolored, hue-shifted variant of the vanilla Fuel Gas icon.
A custom icon only resolves at runtime through a built Unity AssetBundle, not a loose file on
disk.

- `Assets/Geothermal/NaturalGas.png` — source PNG, kept for reference and future rebuilds.
- `AssetBundles/geothermal_54ee` + `.manifest` — the built bundle (Unity 6000.0.66f1), containing
  that PNG at the exact path `Assets/Geothermal/NaturalGas.png`, which must match
  `ProductsData.cs`'s `customIconPath` exactly.
- `AssetBundles/mafi_bundles.manifest` — lists which bundles this mod ships.

The `.csproj` copies `AssetBundles/` to the deployed mod directory automatically. To add more
custom icons or models, follow the "Assets creation" section of the official modding guide
(Unity Editor required) and add the resulting bundle's name to `mafi_bundles.manifest`.

## Localization

Translation strings live in `Translations/<lang>.json` as flat key-value pairs
(`<category>.<id>.<field>`). At startup, the mod resolves a translation file in this order: exact
game culture (e.g. `it-IT`), two-letter language code (e.g. `it`), then `en`. A missing or
malformed translation file falls back to the English string embedded in the source code.

To add a new language, copy `Translations/en.json`, translate the values, and save it as
`<language-code>.json` in the same folder.

## Design notes and known limitations

- **Extraction stays three separate machines.** The vanilla `WellPump` entity is sealed and its
  mined product is fixed at registration time, with no supported way to merge the three
  geothermal wells into one universal machine without Harmony. They share a single research
  unlock instead.
- **Recharge uses three independently-tuned tiers** (Geothermal, Groundwater, crude
  oil/Natural Gas), not one shared rate — geothermal reinjection is fast and intentional,
  Groundwater recharges much slower (closer to real aquifer timescales), and oil/Natural Gas
  recharge slowest of all, reflecting a modest recovery improvement rather than an indefinite
  refill.
- **Recipe duration does not affect recharge pace.** A machine is "working" on every tick a
  recipe is in progress, not just when it completes, so a 240-second recipe recharges a deposit
  just as reliably as a 10-second one provided its input never runs out. Only
  `GeologyRegenManager`'s own rate constants control pacing.
- **Recharge accumulates every tick**, crediting a small fraction of each tier's rate on any
  tick a qualifying pump is working, rather than sampling activity at infrequent periodic
  checks — so one continuously-working pump reliably reaches a tier's maximum rate, instead of
  needing several redundant pumps to make it likely at least one is active at a sampled instant.
- **Recharge is capped once per deposit per tick, not once per pump** — multiple pumps on the
  same deposit improve the odds one is working on a given tick, without compounding each other's
  contribution.
- **`GeologyRegenManager` tracks its own pump list incrementally** (via entity add/remove
  events), rather than re-querying the engine's entity collection every tick, which caused a
  crash under the frequent per-tick accumulation above. Its event subscriptions all use the
  `NonSaveable` variants: a plain `Add` on an engine event stores the callback, and therefore the
  subscribing object, in the save game, which fails for a service that isn't serializable.
- **The reserve status panel aggregates across every resource a pump recognizes** (relevant only
  to the water pump, which can see multiple deposit types at once) rather than showing just the
  first one found.
- **The injection pump entity stores no custom persisted fields**; it reads a static manager
  reference instead, since the engine's generic serializer can't handle an interface-typed field
  on a saved entity, and a saved `Machine` subclass needs hand-written serialization boilerplate
  that only the base game's own build pipeline normally generates.
- **Each injection pump is a fully dedicated machine** restricted to one deposit category at the
  entity level, rather than one shared pump with runtime restriction logic — this is what lets
  the oil and Natural Gas pumps coexist cleanly on a co-located site.

## Compatibility

This mod has no dependencies. It's intended to be declared as a `mod_dependencies` entry by
mods that build on its deposits, machines, or research nodes — prototype IDs are in
`Source/ModIds.cs`.

`manifest.json`'s `primary_dlls` lists `0Harmony.dll` before the mod's own DLL, ensuring this
mod's bundled Harmony instance initializes deterministically regardless of mod load order.

### WorldGen++ compatibility

World Map mines are a core game feature, not something WorldGen++ introduces — the vanilla Oil
Rig already uses the same public `WorldMapMineProto` API WorldGen++ itself builds on. This mod's
Natural Gas Rig uses that same API directly, so WorldGen++ is a genuinely optional companion:
the Rig works identically with or without it installed.

## License

Licensed under the Captain of Industry Open License (COI-Open) v1.0. See [LICENSE](LICENSE) for
the full text.

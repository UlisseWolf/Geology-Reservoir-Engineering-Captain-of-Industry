# Geology & Reservoir Engineering

A geology and subsurface-resources mod for [Captain of Industry](https://coigame.com), built as
a shared foundation layer for other mods rather than a standalone content pack. It introduces
geothermal energy and a general-purpose underground water injection system, and is designed for
other mods to depend on and extend, in the same spirit as `worldgen-plus-plus` or
`recipes-plus-plus`.

The name reflects its two areas of responsibility:

- **Geology** — registering new deposit types as virtual terrain resources, so they are
  generated as part of normal map/scenario data and are selectable in the in-game map editor
  like any vanilla resource.
- **Reservoir engineering** — the machines and runtime systems that extract from, and inject
  back into, those deposits.

Nearly every feature is implemented through the game's public modding API
(`ProtoRegistrator`, `IMod` lifecycle hooks, and public engine interfaces resolved through
dependency injection), with a single, narrow exception: reassigning the vanilla Groundwater
Pump's toolbar category, which has no supported API and uses Harmony's reflection helpers. See
[How this mod uses Harmony](#how-this-mod-uses-harmony) for details.

## Features

### Geothermal energy

Three deposit tiers — High, Medium, and Low enthalpy — registered as `VirtualResourceProductProto`
entries, each backed by an existing vanilla steam product for extraction purposes, but with its
own distinct name, description, and color rather than the backing product's own — a deposit and
the product it eventually yields are conceptually different things, and sharing the exact same
name/color as three vanilla steam grades made the tiers hard to tell apart in resource lists:

| Tier            | Backing product      | Color  |
|-----------------|-----------------------|--------|
| High enthalpy   | High-pressure steam   | Red    |
| Medium enthalpy | Low-pressure steam    | Orange |
| Low enthalpy    | Depleted steam        | Gold   |

Each tier has a dedicated extraction well, built on the vanilla Groundwater Pump prefab and
layout. All three wells are listed under a new "Geothermal" subcategory of the vanilla Power
production toolbar menu, alongside the vanilla "General" and "Nuclear" subcategories.

### Underground water injection

A dedicated water injection pump consumes water and recharges whichever recognized deposit is
present at its location — a geothermal reservoir of any tier, or the vanilla Groundwater
deposit — up to that deposit's configured capacity. Geothermal reservoirs and Groundwater
recharge at different rates: geothermal reinjection is fast, maintaining reservoir pressure for
continued heat extraction, while Groundwater recharges noticeably slower, reflecting how real
aquifer recharge happens over much longer timescales — see
[Design notes and known limitations](#design-notes-and-known-limitations) for the exact figures.
The pump is restricted, at the entity level, to only ever recognize those four deposit types; it
cannot recharge a crude oil or Natural Gas deposit even if built there by mistake — those are
handled by their own dedicated pumps (see below). The pump automatically disables itself once
the deposit it is recharging reaches full capacity. It is listed under the "Geothermal"
subcategory described above, and under a "Groundwater" subcategory of the vanilla "Water"
toolbar menu.

The vanilla Groundwater Pump is also moved into the "Groundwater" subcategory, so neither pump
appears directly under the top-level "Water" menu. This is one of the features in this mod that
requires Harmony — see [How this mod uses Harmony](#how-this-mod-uses-harmony).

The reserve status panel and low-reserve notification are provided by a custom entity class
that implements `IVirtualResourceMiningEntity`, the same interface the vanilla Groundwater Pump
uses. This same entity class is shared by all three injection pumps described in this document.

### Oil recovery

A second, dedicated oil injection pump — visually based on the same vanilla Groundwater Pump
prefab as the water injection pump — offers five recipes targeting the vanilla crude oil
deposit directly rather than a new deposit tier: enhanced oil recovery (gas-injection EOR,
consuming CO₂ — the same product the vanilla CO₂ disposal pump uses), hydraulic fracturing
(seawater-based fracturing fluid), thermal EOR — steam injection reducing the oil's viscosity,
real-world cyclic steam stimulation/SAGD — as two separate recipes, one consuming High steam and
one consuming Super-pressurized steam, since the pump's layout has a single fluid input port and
cannot bind two distinct input products to one recipe, and acid stimulation (matrix acidizing,
dissolving rock near the wellbore to improve permeability — a real technique distinct from the
other three, which raise reservoir pressure or reduce oil viscosity rather than improve
permeability directly). Both steam grades and Acid are products already available elsewhere in
the vanilla game. The pump is restricted, at the entity level, to only ever recognize crude oil
— it cannot be used to recharge geothermal, groundwater, or Natural Gas deposits, even if built
there by mistake. All five recipes recharge the deposit's quantity toward its existing
vanilla-defined capacity, using the same mechanism as geothermal reinjection and aquifer
storage — but at a substantially lower rate than geothermal/groundwater, and with a longer,
separate recharge check interval specific to oil, so the deposit stays meaningfully depletable
rather than becoming effectively inexhaustible with a pump permanently running. Recipe durations
(180s / 200s / 210s / 240s vs. 10s) are longer too, but that reflects logistics cost (how much
input the pump consumes over time), not recharge pacing — recharge rate is governed entirely by
`GeologyRegenManager`'s own constants, independent of recipe duration; see
[Design notes and known limitations](#design-notes-and-known-limitations). Enhanced oil
recovery, hydraulic fracturing, and acid stimulation are each unlocked by their own research
node; the two thermal EOR recipes are unlocked together by a single research node, since they're
two halves of one technique.

The oil injection pump and the vanilla Oil Pump are both listed under a new "Oil wells"
subcategory of the vanilla "Crude oil refining" toolbar menu — the vanilla Oil Pump is
reassigned there from its default "Basic" subcategory; see
[How this mod uses Harmony](#how-this-mod-uses-harmony).

Recharging only happens while a pump is both switched on **and** actively completing a
production cycle (`Machine.WorkedThisTick`), not merely powered on — a pump idling with
"waiting for products" (no CO₂/Seawater/Steam/Water/Fuel Gas available) does not recharge
anything.

The engine rejects multiple recipes bound to the same machine if they share the same set of
input/output products, regardless of quantity — every recipe on the oil injection pump
therefore consumes a genuinely distinct product.

### Research

| Node                          | Unlocks                          | Depends on |
|--------------------------------|-----------------------------------|------------|
| Geothermal extraction          | The three geothermal wells       | Underground water injection, Power generation II, Water recovery, Power generation III |
| Underground water injection    | The water injection pump + its recipe | Groundwater pump (vanilla) |
| Enhanced oil recovery          | The oil injection pump + one recipe | CO2 recycling (vanilla) |
| Hydraulic fracturing           | The oil injection pump + one recipe | Thermal desalination (vanilla) |
| Thermal enhanced oil recovery  | The oil injection pump + two recipes | Super heated steam (vanilla) |
| Acid stimulation               | The oil injection pump + one recipe | Sulfur processing (vanilla) |
| Natural gas extraction         | The natural gas well + 4 recipes (treatment, Flare, Boiler, thermal gas recovery) + two electricity generators | Hydrogen production (vanilla) |
| Underground gas storage        | The natural gas injection pump + one recipe | Natural gas extraction |

### Natural gas

A new storable fluid product, distinct from vanilla Fuel Gas: Fuel Gas is a refined,
downstream product in the vanilla distillation chain (`CrudeOil → ... → LightOil → FuelGas`),
while Natural Gas represents the raw, unrefined gas as extracted from the ground.

- **Deposit placement, three independent ways**: registered as a standard
  `VirtualResourceProductProto`, so it is automatically placeable in the map editor like any
  other deposit — co-located with every existing crude oil deposit on all six built-in
  starting maps, on custom/downloaded maps (e.g. from COI Hub), and on any deposit placed in
  the map editor, representing associated gas, the most common real-world condition for a
  producing oil field — and retrofitted into already-existing saves whose map predates this
  mechanism, added to the live game world the next time it's loaded. See
  [How this mod uses Harmony](#how-this-mod-uses-harmony).
- **Extraction**: a dedicated well (`WellPumpProtoBuilder`, the same extraction pattern as the
  vanilla Groundwater Pump and Oil Pump), visually based on the vanilla Groundwater Pump's own
  prefab. Because it targets its own deposit type rather than crude oil, it can be built within
  the same deposit radius as a vanilla Oil Pump, extracting oil and gas simultaneously from a
  co-located deposit.
- **Use**: a Chemical Plant recipe converts Natural Gas into vanilla Fuel Gas (plus a small
  amount of Sour Water), rather than duplicating every existing Fuel Gas recipe (steam
  generation, hydrogen reforming, kiln fuel, and so on) — once treated, Natural Gas becomes
  usable anywhere Fuel Gas already is, without modifying any of those recipes individually. Raw
  Natural Gas can also be used directly, without treatment, in two vanilla machines: burned off
  in a Flare (mirroring real-world flaring of associated gas not worth capturing) or burned in a
  gas-fired Boiler to generate steam (mirroring on-site use of untreated gas before export).
- **Underground storage & thermal recovery**: a third, dedicated injection pump (visually based
  on the same vanilla Groundwater Pump prefab as the other two injection pumps) offers two
  separate recipes on the Natural Gas deposit: underground storage injects vanilla Fuel Gas —
  already treated, not raw Natural Gas — back into the deposit, mirroring real underground gas
  storage: pipeline-quality gas banked during low demand and withdrawn later via the natural gas
  well; thermal enhanced gas recovery injects Low steam instead, a genuinely different
  technique and input product, so the two recipes coexist without interfering with or
  duplicating each other's effect — both simply recharge the same deposit through the same
  mechanism `GeologyRegenManager` already uses for every other deposit type. This pump is
  restricted, at the entity level, to only ever recognize a Natural Gas deposit — it cannot
  recharge geothermal, groundwater, or crude oil deposits even if built there by mistake. It is
  a separate machine from the water and oil injection pumps described above, so it can be built
  alongside the oil injection pump on a co-located crude oil + Natural Gas site and both operate
  independently and simultaneously — see
  [How this mod uses Harmony](#how-this-mod-uses-harmony) and the design note on the injection
  pump family below.
- **World Map**: a Natural Gas Rig, registered against the same core `WorldMapMineProto` API
  the vanilla Oil Rig already uses on the World Map/exploration map — see
  [WorldGen++ compatibility](#worldgen-compatibility) below.

### Cargo ship fuel

Vanilla Fuel Gas and this mod's Natural Gas are both available as alternative cargo ship fuels,
alongside the vanilla options (Diesel, Heavy Oil, Hydrogen), on every registered cargo ship
tier. Fuel Gas consumes fuel at the same rate as Diesel and pollutes less (65% vs. Diesel's
100%), reflecting that it's already refined and clean-burning. Natural Gas consumes 30% more and
pollutes more (140%), since raw, untreated gas burns less efficiently — consistent with this
mod's own gas-fired Boiler recipe, where Natural Gas combustion produces Exhaust rather than the
cleaner byproduct Fuel Gas combustion produces elsewhere. Fuel Gas and Natural Gas are each
other's only compatible fuel (interchangeable without a switching cost, the same way Diesel and
Heavy Oil already are with each other) — not compatible with the liquid/hydrogen options,
representing a distinct gas-burning engine mode. Each fuel option is locked behind an existing
research node rather than a new, dedicated one: Fuel Gas behind the vanilla "Gas combustion"
node (the same node that already unlocks the gas-fired Boiler and Fuel Gas steam generation),
and Natural Gas behind this mod's own "Natural gas extraction" node — mirroring how the vanilla
Hydrogen fuel entry itself locks behind a technology rather than the Hydrogen product. Neither
fuel option introduces a new engine cost or ship model — both reuse the ship's existing engine
cost and default graphics, since no dedicated 3D assets exist for a gas-fueled variant. See
[How this mod uses Harmony](#how-this-mod-uses-harmony) for how this is implemented.

### Power generation

Two electricity generators, one burning vanilla Fuel Gas and one burning this mod's Natural
Gas, both reusing the vanilla Diesel Generator II's own prototype type
(`ElectricityGeneratorFromProductProto`), layout, construction cost, 5000 kW output, and 3D
model unmodified — visually indistinguishable in-game from the vanilla Diesel Generator II they're
built from. This prototype type takes exactly one fixed input product per instance (unlike a
`RecipeProtoBuilder`-bound machine, which can offer several recipes on one building), so Fuel
Gas and Natural Gas need two separate machines rather than one offering both as alternatives.
Fuel Gas matches the vanilla Diesel Generator II's own input rate one-to-one; Natural Gas
consumes 30% more and produces 30% more Exhaust for the same output — the same pollutant the
vanilla Diesel Generator II itself produces — following this mod's established pattern for the
difference between treated Fuel Gas and raw Natural Gas (see also the gas-fired Boiler recipe
and the cargo ship fuel entries above). Both generators are unlocked together by the "Natural
gas extraction" research node, alongside everything else that node already unlocks, rather than
a separate dedicated node — both generators need raw Natural Gas to be meaningful (one burns it
directly, the other burns Fuel Gas refined from it), so tying them to gas extraction itself
keeps the dependency in one place.

Both generators are listed under a new "Electric generators" subcategory of the vanilla "Power
production" toolbar menu, alongside the vanilla "General" and "Nuclear" subcategories and this
mod's own "Geothermal" subcategory. The vanilla Diesel Generator (both tiers) is also reassigned
there from its default "General" subcategory, so every combustion-fueled electricity generator
lives together in one place — see
[How this mod uses Harmony](#how-this-mod-uses-harmony).

A recolored variant of the Diesel Generator II's texture exists as a source asset in this
project (`Assets/Geothermal/NaturalGasEngine-512-albedo.png`), but using it in-game would
require building and shipping a Unity AssetBundle containing a new prefab/material that
references it — the same asset pipeline already used for the Natural Gas product icon (see
[Custom assets](#custom-assets)) — which hasn't been done for this texture yet, so both
generators currently look identical to the vanilla machine they're built from.

### Localization

English, Italian, French, Spanish, German, and Portuguese, using a small JSON-based translation
loader with no external dependencies. See [Localization](#localization) below.

## How this mod uses Harmony

Harmony is used in four independent ways.

**Reflection only, no method patch.** A machine's toolbar categories are set once, in
`LayoutEntityProto.Gfx.Categories`, a get-only property assigned when the base game constructs
its prototypes — before this mod runs. There is no supported API to reassign it afterward.
`Source/Data/VanillaCategoryFixupData.cs` works around this by locating the property's backing
field by type, using Harmony's `AccessTools` reflection helpers, and overwriting it directly on
already-constructed vanilla prototypes — the Groundwater Pump (into this mod's "Groundwater"
subcategory), the Oil Pump (from its default "Basic" subcategory of "Crude oil refining" into
this mod's "Oil wells" subcategory), and the Diesel Generator, both tiers (from its default
"General" subcategory of "Power production" into this mod's "Electric generators" subcategory).
The Diesel Generator is looked up as `LayoutEntityProto`, not `MachineProto`: it's registered as
`ElectricityGeneratorFromProductProto`, a sibling type that only shares `LayoutEntityProto` as a
common ancestor rather than being a `MachineProto` itself, so looking it up as the narrower
`MachineProto` type would throw a cast exception - the same distinction the research data (below)
has to make for the same two generator machines.

**Reflection only, no method patch, again.** `CargoShipProto.AvailableFuels` is a public but
`readonly` field, set once when the base game constructs its cargo ship prototypes, before this
mod runs. There is no supported API to add a fuel option afterward, so
`Source/Data/ShipFuelData.cs` locates the field by name and overwrites it with the existing fuel
list plus two new entries (Fuel Gas, Natural Gas) appended, on every registered
`CargoShipProto` — the same technique as the toolbar category fixup above, applied to a
different vanilla field. Each new entry is locked behind an existing research node (the vanilla
"Gas combustion" node for Fuel Gas, this mod's own "Natural gas extraction" node for Natural
Gas) via `FuelData.LockingProto`, which accepts any `Proto` — the vanilla Hydrogen fuel entry
itself already locks behind a technology rather than a product, so no new, dedicated research
node is needed for either.

**Two genuine method patches**, because oil deposits reach newly-generated game content through
two different code paths.

Every built-in map hard-codes its deposits' positions in a private `createVirtualResources()`
method that directly constructs `SimpleVirtualResource` instances and returns a fixed array.
There is no supported API to add to that list, so `Source/Data/NaturalGasMapPatch.cs` patches
this method on all six built-in static island maps (Curland, Alpha, Beach, Golden Peak, Insula
Mortis, You Shall Not Pass) — these are just named starting-location choices presented when
starting a new game, with no single "default" among them, so all six need patching for the
deposit to appear regardless of which one the player picks.

Map-editor-placed deposits and custom/downloaded maps (e.g. from COI Hub) don't go through
`IStaticIslandMap.CreateIslandMap()` at all — a custom map is loaded as a save file via
`LoadGameArgsFromMapFile`. Inspecting an actual exported map file's serialized contents shows
its crude oil deposits are represented as
`Mafi.Base.Terrain.FeatureGenerators.VirtualResourceFeatureGenerator` instances — the same class
the in-game map editor itself uses for hand-placed resource features. Each instance's public
`GenerateResources()` method turns its own configuration (product, position, capacity, radius)
into the actual placed resource, and this method is shared and universal — it runs for every
deposit placed this way, on any map, regardless of source. Patching it once covers the map
editor and every custom/downloaded map at the same time, without needing to know anything about
a specific map in advance.

Both patches only append new entries to what the vanilla code already builds; neither alters any
existing entry or any other behavior of the patched methods. Because deposit positions are
computed once — when a map is first generated, or when a feature is placed — and saved into the
game state from that point on, both patches only affect newly generated content: a new game on
a map created after this version of the mod is installed, or a deposit placed in the map editor
after installing it. Neither has any effect on a save whose map/deposits already existed.

**A live-state retrofit, for saves generated before either patch above existed.** Loading an
existing save doesn't re-run any generation method — deposit positions are deserialized as fixed
data, verbatim, with no processing. So for a save whose map predates this mechanism, there is no
method call left to intercept at all. Instead, `NaturalGasMapPatch.RetrofitExistingSave`
(called from `GeologyReservoirEngineeringMod.Initialize`, which runs once per game session
whether a new game was created or an existing save was loaded) reaches into the live,
already-deserialized `VirtualResourceManager` and mutates its two private fields directly —
`m_virtualResources` (the flat deposit list) and `m_virtualResourcesMap` (a cache grouped by
product) — adding a co-located gas deposit for every crude oil deposit that doesn't already have
one at the exact same position. This is the same `AccessTools` reflection technique as the
toolbar category fixup, but applied to **live game state that already exists in a save**,
rather than to prototype data at registration time. It's the most invasive mechanism in this
mod: **back up your save before loading it with this version installed**, and treat it as the
first thing to investigate if something looks wrong with deposits after an update.

On a brand new game, mod initialization can run before the vanilla `VirtualResourceManager` has
populated `m_virtualResources` for the freshly generated map yet - reading it in that state
would be an uninitialized `ImmutableArray` wrapping a null backing array, which throws if
enumerated. `RetrofitExistingSave` checks for this (`current.IsNotValid`) and returns
immediately when it's the case, since there is nothing to retrofit on a brand new game anyway -
the two generation-time patches above already handle it directly.

Because all four of these reach into internal implementation details rather than documented
APIs, they are more likely than the rest of the mod to break on a game update that changes how
toolbar categories, cargo ship fuel data, map generation, or the resource manager's internal
structure are represented.

## Requirements

- Captain of Industry, version 0.8.6 or later.
- .NET Framework 4.8 SDK (for building from source).

[Harmony](https://github.com/pardeike/Harmony) (`0Harmony.dll`) is bundled in `Libs/` and
required only for the toolbar category fixup described above; no separate installation step is
needed.

## Installation

Download the release archive and extract it into your Captain of Industry `Mods` folder, so
that the result is:

```
Mods/
  GeologyReservoirEngineering/
    manifest.json
    GeologyReservoirEngineering.dll
    0Harmony.dll
    Translations/
    AssetBundles/
```

`0Harmony.dll` is included in the release archive; no separate download is needed.

The folder name must match the mod's manifest `id` (`GeologyReservoirEngineering`) for the game
to recognize it.

## Building from source

1. Set the `COI_ROOT` environment variable to your Captain of Industry installation directory,
   or create an `Options.user` file in the project root overriding `GameDir`.
2. Build with `dotnet build -c Release`, or open the project in your IDE of choice.

`0Harmony.dll` is referenced directly from `Libs/` — no additional configuration is required.
The build automatically deploys the compiled DLL, `Libs/0Harmony.dll`, `manifest.json`, and
`Translations/` to `%APPDATA%\Captain of Industry\Mods\GeologyReservoirEngineering\`.

## Project structure

```
Source/
  GeologyReservoirEngineeringMod.cs   Mod entry point (IMod implementation)
  ModIds.cs                           Prototype IDs owned by this mod
  ModTranslation.cs                   JSON translation loader
  Data/
    ProductsData.cs                   Geothermal deposit registration
    ToolbarCategoriesData.cs          Geothermal and Groundwater toolbar subcategories
    VanillaCategoryFixupData.cs       Reassigns vanilla machines' categories (Harmony, reflection only)
    ShipFuelData.cs                    Adds Fuel Gas/Natural Gas as cargo ship fuel (Harmony, reflection only)
    NaturalGasMapPatch.cs             Co-locates Natural Gas with crude oil deposits (Harmony, method patch)
    WorldMapData.cs                   Natural Gas Rig on the World Map (no Harmony, no WorldGen++ dependency)
    MachinesData.cs                   Extraction wells and the three injection pumps
    PowerGeneratorsData.cs            Fuel Gas / Natural Gas electricity generators (no Harmony)
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
  geothermal_54ee, geothermal_54ee.manifest, mafi_bundles.manifest   Built bundle (see "Custom assets")
Libs/
  0Harmony.dll                        Bundled dependency, see "How this mod uses Harmony"
manifest.json
LICENSE
```

## Custom assets

Natural Gas uses a custom icon — a recolored variant of the vanilla Fuel Gas icon, same
silhouette, hue-shifted to blue to read as distinct at a glance. Unlike a vanilla icon path
(`Assets/Base/...`), a custom one only resolves at runtime through a built Unity AssetBundle,
not from a loose file on disk — the engine looks up the path against the bundle's own manifest.

- `Assets/Geothermal/NaturalGas.png` — the source PNG, kept for reference and future rebuilds.
- `AssetBundles/geothermal_54ee` + `geothermal_54ee.manifest` — the built AssetBundle, produced
  from a companion Unity project (Unity 6000.0.66f1) and containing that PNG at the exact path
  `Assets/Geothermal/NaturalGas.png`. `ProductsData.cs`'s `customIconPath` must match this path
  exactly, since the engine resolves it against the manifest, not the filesystem.
- `AssetBundles/mafi_bundles.manifest` — lists which bundles this mod ships (just
  `geothermal_54ee` for now), the same top-level manifest role Fusion Horizon's own
  `AssetBundles/mafi_bundles.manifest` plays for its (much larger) set of bundles.

The `.csproj` copies the whole `AssetBundles/` folder to the deployed mod directory
automatically, the same way `Translations/` and `Libs/` are handled.

To add more custom icons or models later, follow the "Assets creation" section of the official
modding guide (Unity Editor required) and add the resulting bundle's name to
`mafi_bundles.manifest`.

### Pipe transport colors

`ProductProto.Gfx`'s three color fields (`color`, `transportColor`, `transportAccentColor`)
control how Natural Gas renders in pipes. Cross-referencing Fusion Horizon's own
`ProductData.cs` (which registers many custom fluids) is informative here: its
`transportAccentColor` values vary widely and inconsistently across fluids — orange, purple,
gold, dark green — with no fixed relationship to the base `color`. If this field controlled a
pipe's flow-direction chevrons, which need to stay readable, that inconsistency wouldn't make
sense across a working, shipped mod. The values in this mod instead treat `transportColor` as
the pipe's main visible body/flow tint and `transportAccentColor` as a lighter highlight in the
same hue, the more common pattern across Fusion Horizon's own fluids. The white chevrons visible
on in-game pipes are most likely a fixed part of the pipe shader, not something a product's
`Gfx` values control - but this isn't confirmed against Unity shader/rendering source, which
this project doesn't have access to.

## Localization

Translation strings live in `Translations/<lang>.json` as flat key-value pairs, using a
`<category>.<id>.<field>` key convention (for example,
`build-machine.UndergroundInjectionPump.name`). At startup, the mod resolves a translation file
in the following order: exact game culture (e.g. `it-IT`), two-letter language code (e.g.
`it`), then `en`. If no file is found for the current language, each lookup falls back to the
English string embedded in the source code, so a missing or malformed translation file never
breaks the mod.

To add a new language, copy `Translations/en.json`, translate the values, and save it as
`<language-code>.json` in the same folder.

## Design notes and known limitations

**Extraction remains three separate machines.** Unlike the injection pump, the three geothermal
wells cannot be merged into a single universal machine without Harmony. The vanilla `WellPump`
entity class is sealed, its production logic depends on an `internal` field on `Machine` that is
not accessible outside the game's own assembly, and a well prototype's mined product is fixed at
registration time with no supported way to resolve it dynamically at runtime. The three wells
share a single research unlock as the closest practical equivalent.

**No custom persisted state.** The custom injection pump entity intentionally stores no new
fields; its reserve/capacity data is recomputed on demand from the engine's virtual resource
manager on every read. This avoids any dependency on custom save-file serialization and keeps
the entity's persistence behavior identical to the base `Machine` class it extends.

**Vanilla toolbar category reassignment is reflection-based, not a supported API.** See
[How this mod uses Harmony](#how-this-mod-uses-harmony). This is the only part of the mod with
this characteristic; every other feature uses documented, public APIs.

**Recharge uses three distinct tiers, not two.** Geothermal, Groundwater, and crude
oil/Natural Gas each recharge at a different pace, reflecting three different real-world
categories: geothermal reinjection is immediate and intentional (60 units every 30 simulation
steps — 2.0 units/step), Groundwater recharges noticeably slower (20 units every 90 steps —
about 0.22 units/step, roughly 9× slower than geothermal), and crude oil/Natural Gas recharge
slowest of all (6 units every 120 steps — 0.05 units/step, so Groundwater is still around 4-5×
faster than oil/gas). Geothermal and Groundwater share a pump but not a rate; oil and Natural
Gas share both a pump-family pattern and a rate, since both represent the same "geological, not
indefinitely replenishable" category in this mod's model, while Groundwater is a naturally
replenishing resource that just recharges more slowly than an actively-managed geothermal
reservoir.

**Recipe duration does not pace recharge rate; only `GeologyRegenManager`'s own constants do.**
A machine is in `State.Working` (and therefore `Machine.WorkedThisTick` is true) on every
simulation tick a recipe is actively in progress, not only on the tick it completes, per the
engine's own `Machine.updateWorkOnRecipes()`. A pump running a 240-second recipe is "working"
just as continuously, tick for tick, as one running a 10-second recipe, provided its input
supply never runs out. Recipe duration governs input consumption rate (logistics cost), not how
often the recharge manager finds a pump actively working - the manager's `REGEN_PER_CHECK` /
`SLOW_REGEN_PER_CHECK` / `STEPS_BETWEEN_CHECKS` / `SLOW_CHECK_MULTIPLIER` constants are what
actually control pacing.

**`AllowedResourceIds` must be checked explicitly by `GeologyRegenManager`, not assumed.**
Restricting a pump to a specific deposit type at the entity level
(`InjectionPumpProto.AllowedResourceIds`) only affects what `InjectionPump` itself reports and
enables/disables on - it has no automatic effect on `GeologyRegenManager`'s separate recharge
loop, which reads deposits directly from the terrain rather than through the pump entity.
Without an explicit check there too, a pump could recharge a deposit type it isn't restricted
to whenever a different, allowed deposit happens to share the same tile - for example, a natural
gas injection pump built on a co-located crude oil + Natural Gas site (see
`NaturalGasMapPatch`) recharging the crude oil deposit there too, despite being restricted to
Natural Gas everywhere else in the mod. The loop checks each machine's own `AllowedResourceIds`
before considering any resource at its tile for recharge, matching what the entity already
enforces for display.

**Recharge is capped once per deposit per check, not once per pump.** A deposit's radius means
multiple pumps built at different positions can all resolve to the same underlying deposit.
Without a cap, each working pump targeting that deposit would independently trigger a recharge
in the same check, so building enough pumps around a single deposit could refill it far faster
than the configured slow pace intends, regardless of how conservative
`SLOW_REGEN_PER_CHECK`/`SLOW_CHECK_MULTIPLIER` are individually. `GeologyRegenManager` tracks
which deposits (by position) have already been recharged in the current check and skips any
further pump targeting the same one - building more pumps around a deposit adds redundancy, not
additional recharge speed.

**The reserve status panel aggregates across every resource a pump recognizes, rather than
showing only the first one found.** In practice this only matters for the water injection pump,
whose `AllowedResourceIds` spans all three geothermal tiers and Groundwater at once - the oil
and natural gas injection pumps each only ever recognize a single deposit type, so there's
nothing to aggregate for them. `InjectionPump.CapacityOfMine`/`QuantityLeftToMine` sum across
every recognized resource present, `ProductToMine` reports whichever one has the lowest fill
percentage (the one most in need of attention), and `IsEnabledNow` stays enabled as long as any
recognized resource still has room. This is purely a display/auto-stop improvement -
`GeologyRegenManager` already iterated every resource at a pump's tile independently of what
this panel reported.

**A saved entity cannot store an interface-typed instance field, and a sealed `Machine`
subclass also needs its own hand-written serialization boilerplate - two separate requirements
behind the same `Failed to create generic serializer for 'InjectionPump'` error.**
`InjectionPump` needs a live `IVirtualResourceManager` reference to query current deposit state
on demand, not just once at construction, but no entity in the base game stores a
manager-style interface as an instance field - the vanilla `WellPump`, for example, only uses
its own `IVirtualResourceManager` constructor parameter to compute a concrete result once, and
never keeps the interface reference itself. `InjectionPump` follows the same rule: it has no
instance field of this kind at all, and instead reads
`GeologyReservoirEngineeringMod.VirtualResourceManager`, a `static` field set once per game
session in that mod's `Initialize` - a `static` field belongs to the type, not to any individual
entity instance, so it is never part of an entity's own serialized state.

That alone isn't sufficient, though: the game's save system does not serialize every entity
type through one fully automatic reflection path. Concrete `Machine` subclasses each provide
their own `public static void Serialize(TSelf value, BlobWriter writer)` /
`public new static TSelf Deserialize(BlobReader reader)` pair, plus `SerializeData`/
`DeserializeData` overrides - confirmed directly in the vanilla `WellPump` class, which has this
exact boilerplate despite having very few fields of its own. This is most likely produced by a
source generator as part of the base game's own build (triggered by a `[GenerateSerializer]`
attribute, which requires a `partial class` declaration to inject the generated half) - not
something available to an externally-compiled mod assembly. `InjectionPump` reproduces the same
boilerplate by hand, calling only the base `Machine` implementation in both data methods, since
it has nothing of its own left to serialize.

**The injection pump family is three fully dedicated machines, each restricted to its own
deposit type, rather than one shared pump with runtime restriction logic.** Water, oil, and
Natural Gas each have their own injection pump, entity-level restricted to their own fixed set
of deposit types via `InjectionPumpProto.AllowedResourceIds` (see `MachinesData.cs`) — the water
pump cannot recharge crude oil or Natural Gas, the oil pump cannot recharge anything but crude
oil, and so on. All three share the same `InjectionPump`/`InjectionPumpProto` entity/prototype
pair; they differ only in which resource IDs they're constructed with, which recipe(s) are
bound to them, and their visual prefab.

This matters specifically because oil and Natural Gas are deliberately co-located (see
`NaturalGasMapPatch`) to represent associated gas: on a co-located site, a pump would need to
correctly offer both EOR/Fracturing-style recipes and gas storage at the same time, not
exclusively. Restricting each pump to a single deposit category at the entity level means there
is nothing to dynamically restrict at runtime — a pump that can only ever recognize one deposit
type simply has no ambiguity to resolve.

## Compatibility

This mod does not depend on any other mod. It is intended to be declared as a `mod_dependencies`
entry by other mods that build on top of its deposits, machines, or research nodes; the
prototype IDs it exposes are defined in `Source/ModIds.cs`.

Because `VanillaCategoryFixupData` reassigns the vanilla Groundwater Pump's toolbar category
directly, another mod that reassigns the same category on the same prototype may produce a
result that depends on mod load order.

### Mod load order and Harmony

`manifest.json`'s `primary_dlls` lists `0Harmony.dll` before `GeologyReservoirEngineering.dll`.
This mod bundles its own copy of `0Harmony.dll` (see `Libs/`, copied to the mod's root folder on
build/deploy) since Harmony isn't part of the base game install. Mods appear to load in
alphabetical order by default, so if another installed mod also bundles its own `0Harmony.dll`
and happens to sort after this one, that mod's own copy could end up initializing second,
depending on exactly how the game resolves duplicate assembly loads across mods - listing
`0Harmony.dll` first in `primary_dlls` makes this mod's own Harmony instance load deterministically
before its main DLL runs, regardless of overall mod load order.

### WorldGen++ compatibility

`Source/Data/WorldMapData.cs` registers a Natural Gas Rig on the World Map/exploration map.
World Map mines are a core game feature — the vanilla game already ships an Oil Rig
(`Mafi.Base.Prototypes.World.WorldMapEntitiesData`) using the same public `WorldMapMineProto`
API before any third-party mod is involved; WorldGen++ adds more mine types (gold, iron, copper,
titanium, sand, lithium) on top of this same system, not a system of its own. This mod's Natural
Gas Rig uses the identical API, so WorldGen++ is a genuinely optional companion, not a
dependency: with WorldGen++ installed, the Natural Gas Rig sits alongside its own mines in the
fuller World Map/campaign experience WorldGen++ provides; without it, the Natural Gas Rig is
still a normal, functional part of whatever World Map access the base game itself provides.
Neither this mod's `manifest.json` nor its code references WorldGen++ or checks for its
presence — there is nothing to detect, since both cases use the same vanilla registration path.

## License

Licensed under the Captain of Industry Open License (COI-Open) v1.0. See [LICENSE](LICENSE) for
the full text.

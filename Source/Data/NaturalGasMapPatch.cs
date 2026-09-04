using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Base.Terrain.FeatureGenerators;
using Mafi.Base.Terrain.Maps;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Map;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Co-locates a Natural Gas deposit with every existing crude oil deposit, representing
/// associated gas - gas dissolved in or capping the same reservoir as the oil, the most common
/// real-world condition for a producing oil field. Three independent mechanisms are needed,
/// because oil deposits reach the game through three different code paths:
///
/// <list type="number">
/// <item>
/// <b>Built-in maps.</b> Each of the six built-in static island maps hard-codes its deposits'
/// positions in a private <c>createVirtualResources()</c> method that directly constructs
/// <c>SimpleVirtualResource</c> instances and returns a fixed
/// <c>ImmutableArray&lt;IVirtualTerrainResource&gt;</c>. There is no supported API to add to
/// that list, so <see cref="MapPostfix"/> patches this method on all six map classes
/// (<c>CurlandMap</c>, <c>AlphaStaticIslandMap</c>, <c>BeachStaticIslandMap</c>,
/// <c>GoldenPeakStaticIslandMap</c>, <c>InsulaMortis</c>, <c>YouShallNotPassStaticIslandMap</c>)
/// - these are just named starting-location choices presented when starting a new game, with no
/// single "default" among them, so all six need patching for the deposit to appear regardless
/// of which one the player picks.
/// </item>
/// <item>
/// <b>Map-editor-placed and custom/downloaded maps (e.g. from COI Hub).</b> These do not go
/// through <c>IStaticIslandMap.CreateIslandMap()</c> at all - a custom map is loaded as a save
/// file via <c>LoadGameArgsFromMapFile</c>, and both the map editor and a loaded custom map
/// represent each hand-placed deposit as a
/// <c>Mafi.Base.Terrain.FeatureGenerators.VirtualResourceFeatureGenerator</c> instance (visible
/// in an exported map file's serialized contents, where crude oil deposits are stored as this
/// exact type alongside an <c>IVirtualTerrainResourceGenerator</c> reference). Each instance's
/// public <c>GenerateResources()</c> method is what turns its own configuration (product,
/// position, capacity, radius) into the actual <c>SimpleVirtualResource</c> placed on the
/// terrain - <see cref="GeneratorPostfix"/> patches this instance method, so it runs for every
/// deposit placed this way regardless of which map (or map editor session) it came from.
/// </item>
/// <item>
/// <b>Already-existing saves.</b> Neither patch above helps a save whose map/deposits were
/// generated before this mod (or this version of it) was installed: deposit positions are
/// computed once, at generation time, and from then on simply deserialized as fixed data on
/// every load - no generation method runs again, so there is nothing left for a method patch to
/// intercept. <see cref="RetrofitExistingSave"/> instead mutates the live, already-deserialized
/// <c>VirtualResourceManager</c> directly: it holds every currently known deposit in two
/// private fields (<c>m_virtualResources</c>, a flat list, and <c>m_virtualResourcesMap</c>, a
/// cache grouped by product), populated once from generation for a new game and otherwise
/// restored verbatim from a save with no further processing. Reading and overwriting both
/// fields via Harmony's <c>AccessTools</c> reflection (the same technique
/// <see cref="VanillaCategoryFixupData"/> uses on prototype data) lets this mod add missing gas
/// deposits to an already-loaded game world, whether newly started or restored from a save -
/// called from <c>GeologyReservoirEngineeringMod.Initialize</c>, which runs once per game
/// session regardless of whether a new game was created or an existing save was loaded. Unlike
/// the two patches above, which touch prototype/generation-time data no player has interacted
/// with yet, this mutates live, already-simulated game state, which makes it worth testing
/// carefully around save/reload cycles before relying on it in an important save.
/// </item>
/// </list>
///
/// All three only add new deposits; none of them alter or remove any existing entry. None
/// depends on the others - a given world could plausibly pick up gas deposits through more than
/// one of them over time, and each independently avoids adding a duplicate where one it already
/// added (or another of the three already added) is present at the same position.
///
/// This is independent of - and in addition to - registering Natural Gas as a standard
/// <see cref="Mafi.Core.Products.VirtualResourceProductProto"/> (see <c>ProductsData.cs</c>),
/// which alone already makes it freely placeable in the map editor, unrelated to any oil
/// deposit. Nothing here affects that.
/// </summary>
internal static class NaturalGasMapPatch {

    /// <summary>
    /// Set once, during <see cref="ProductsData"/> registration, before any of the three
    /// mechanisms above can possibly run.
    /// </summary>
    internal static VirtualResourceProductProto? NaturalGasProto;

    /// <summary>Natural gas quantity as a fraction of the co-located oil deposit's own capacity.</summary>
    private const float GAS_CAPACITY_FRACTION = 0.4f;

    private static readonly Type[] PATCHED_MAP_TYPES = {
        typeof(CurlandMap),
        typeof(AlphaStaticIslandMap),
        typeof(BeachStaticIslandMap),
        typeof(GoldenPeakStaticIslandMap),
        typeof(InsulaMortis),
        typeof(YouShallNotPassStaticIslandMap),
    };

    public static void Apply(Harmony harmony) {
        MethodInfo mapPostfix = AccessTools.Method(typeof(NaturalGasMapPatch), nameof(MapPostfix));
        foreach (Type mapType in PATCHED_MAP_TYPES) {
            MethodInfo original = AccessTools.Method(mapType, "createVirtualResources");
            harmony.Patch(original, postfix: new HarmonyMethod(mapPostfix));
        }

        MethodInfo generatorOriginal = AccessTools.Method(typeof(VirtualResourceFeatureGenerator), nameof(VirtualResourceFeatureGenerator.GenerateResources));
        MethodInfo generatorPostfix = AccessTools.Method(typeof(NaturalGasMapPatch), nameof(GeneratorPostfix));
        harmony.Patch(generatorOriginal, postfix: new HarmonyMethod(generatorPostfix));
    }

    /// <summary>Postfix for each built-in map's private <c>createVirtualResources()</c>.</summary>
    private static void MapPostfix(ref ImmutableArray<IVirtualTerrainResource> __result) {
        if (NaturalGasProto == null) {
            return;
        }

        var additions = new List<IVirtualTerrainResource>();
        foreach (IVirtualTerrainResource resource in __result) {
            if (resource.Product.Id != IdsCore.Products.VirtualCrudeOil) {
                continue;
            }

            // Guards against this postfix ever running more than once for the same result -
            // for example, if the patched method is invoked more than once for the same map
            // during a single session, an unguarded postfix would append a second gas deposit
            // at the same position each time, silently doubling every gas deposit on the map.
            bool alreadyHasGas = __result.AsEnumerable().Any(r => r.Product.Id == NaturalGasProto.Id && r.Position == resource.Position)
                || additions.Any(r => r.Position == resource.Position);
            if (alreadyHasGas) {
                continue;
            }

            var gasQuantity = new Quantity((int)(resource.ConfiguredCapacity.Value * GAS_CAPACITY_FRACTION));
            additions.Add(new SimpleVirtualResource(NaturalGasProto, gasQuantity, resource.Position, resource.MaxRadius));
        }

        if (additions.Count > 0) {
            __result = __result.AddRange(additions);
        }
    }

    /// <summary>
    /// Postfix for <see cref="VirtualResourceFeatureGenerator.GenerateResources"/> - covers
    /// map-editor-placed deposits and custom/downloaded maps.
    /// </summary>
    private static void GeneratorPostfix(VirtualResourceFeatureGenerator __instance, ref ImmutableArray<IVirtualTerrainResource> __result) {
        if (NaturalGasProto == null) {
            return;
        }

        VirtualResourceFeatureGenerator.Configuration config = __instance.ConfigMutable;
        if (config.VirtualResource == null || config.VirtualResource.Id != IdsCore.Products.VirtualCrudeOil) {
            return;
        }

        Tile3i gasPosition = config.Position.Tile2iRounded.ExtendZ(0);

        // Same guard as MapPostfix above: without this check, this postfix running more than
        // once for the same feature generator instance would append a duplicate gas deposit at
        // the same position each time.
        bool alreadyHasGas = __result.AsEnumerable().Any(r => r.Product.Id == NaturalGasProto.Id && r.Position == gasPosition);
        if (alreadyHasGas) {
            return;
        }

        var gasQuantity = new Quantity((int)(config.ConfiguredCapacity.Value * GAS_CAPACITY_FRACTION));
        var gasResource = new SimpleVirtualResource(NaturalGasProto, gasQuantity, gasPosition, config.MaxRadius);
        __result = __result.Add(gasResource);
    }

    /// <summary>
    /// Adds a co-located Natural Gas deposit for every crude oil deposit already present in the
    /// live game world that doesn't already have one at the exact same position - covering
    /// saves whose map was generated before this mechanism existed. Called once per game
    /// session from <c>GeologyReservoirEngineeringMod.Initialize</c>. On a brand new game, mod
    /// initialization can run before the vanilla <c>VirtualResourceManager</c> has populated its
    /// resource list for the freshly generated map yet, in which case there is nothing to
    /// retrofit and this method returns immediately - the two generation-time patches above
    /// already handle a brand new game's deposits directly.
    /// </summary>
    public static void RetrofitExistingSave(IVirtualResourceManager virtualResourceManager) {
        if (NaturalGasProto == null) {
            return;
        }
        if (virtualResourceManager is not VirtualResourceManager manager) {
            Log.Warning("[Geology & Reservoir Engineering] IVirtualResourceManager is not the expected VirtualResourceManager type; skipping natural gas save retrofit.");
            return;
        }

        FieldInfo listField = AccessTools.Field(typeof(VirtualResourceManager), "m_virtualResources");
        FieldInfo mapField = AccessTools.Field(typeof(VirtualResourceManager), "m_virtualResourcesMap");

        var current = (ImmutableArray<IVirtualTerrainResource>)listField.GetValue(manager);
        if (current.IsNotValid) {
            // On a brand new game, mod Initialize() can run before the vanilla
            // VirtualResourceManager has populated this field for the freshly generated map -
            // its default value is an uninitialized ImmutableArray wrapping a null backing
            // array, which throws if enumerated. There is nothing to retrofit yet in that case:
            // the two generation-time patches in this file already handle a brand new game's
            // deposits directly, so simply skipping here is correct, not a fallback.
            return;
        }

        var additions = new List<IVirtualTerrainResource>();
        foreach (IVirtualTerrainResource resource in current) {
            if (resource.Product.Id != IdsCore.Products.VirtualCrudeOil) {
                continue;
            }

            bool alreadyHasGas = current.AsEnumerable().Any(r => r.Product.Id == NaturalGasProto.Id && r.Position == resource.Position)
                || additions.Any(r => r.Position == resource.Position);
            if (alreadyHasGas) {
                continue;
            }

            var gasQuantity = new Quantity((int)(resource.ConfiguredCapacity.Value * GAS_CAPACITY_FRACTION));
            additions.Add(new SimpleVirtualResource(NaturalGasProto, gasQuantity, resource.Position, resource.MaxRadius));
        }

        if (additions.Count == 0) {
            return;
        }

        ImmutableArray<IVirtualTerrainResource> newList = current.AddRange(additions);
        listField.SetValue(manager, newList);

        Dict<VirtualResourceProductProto, ImmutableArray<IVirtualTerrainResource>> newMap = newList
            .AsEnumerable()
            .GroupBy(r => r.Product)
            .ToDict(g => g.Key, g => g.ToImmutableArray());
        mapField.SetValue(manager, newMap);

        Log.Info($"[Geology & Reservoir Engineering] Added {additions.Count} natural gas deposit(s) to an already-loaded world.");
    }
}

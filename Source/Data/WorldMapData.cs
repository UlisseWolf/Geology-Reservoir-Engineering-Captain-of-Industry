using Mafi;
using Mafi.Base;
using Mafi.Core;
using Mafi.Core.Economy;
using Mafi.Core.Entities;
using Mafi.Core.Maintenance;
using Mafi.Core.Mods;
using Mafi.Core.Population;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.World.Entities;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Registers a Natural Gas Rig as a World Map mine, extracting Natural Gas from a World Map
/// site the same way the vanilla Oil Rig extracts crude oil.
///
/// World Map mines are a core game feature, not something WorldGen++ introduces:
/// <c>Mafi.Base.Prototypes.World.WorldMapEntitiesData</c> already registers a vanilla Oil Rig
/// (<c>Ids.World.OilRigCost1</c> and its upgrade tiers) using the public
/// <see cref="WorldMapMineProto"/> API, before any third-party mod is involved. WorldGen++ adds
/// more mine types (gold, iron, copper, titanium, sand, lithium) on top of this same vanilla
/// system, using the identical API - it doesn't own or gate access to World Map mines itself.
/// Registering a Natural Gas Rig here, the same way, therefore works identically whether
/// WorldGen++ is installed or not: with it, players get the fuller World Map/campaign
/// experience WorldGen++ provides, with this mine included alongside vanilla and WorldGen++'s
/// own; without it, this mine is still a normal, functional part of whatever World Map access
/// the base game itself provides. Neither case requires detecting WorldGen++'s presence or
/// referencing its assembly - this file has no dependency on it at all.
///
/// Registration is wrapped in a try/catch, mirroring WorldGen++'s own defensive pattern in its
/// mine registration: if a referenced vanilla prototype is ever missing (e.g. a future game
/// version changes IDs), this mod logs a warning and continues without the World Map mine,
/// rather than failing to load entirely over an optional, non-essential feature.
/// </summary>
internal class WorldMapData : IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        try {
            registerNaturalGasRig(registrator);
        } catch (System.Exception ex) {
            Log.Warning($"[Geology & Reservoir Engineering] Could not register the Natural Gas World Map mine; skipping. {ex}");
        }
    }

    private static void registerNaturalGasRig(ProtoRegistrator registrator) {
        ProtosDb db = registrator.PrototypesDb;

        ProductProto naturalGas = db.GetOrThrow<ProductProto>(ModIds.Products.NaturalGas);
        ProductProto constructionParts2 = db.GetOrThrow<ProductProto>(Ids.Products.ConstructionParts2);
        ProductProto constructionParts3 = db.GetOrThrow<ProductProto>(Ids.Products.ConstructionParts3);
        VirtualProductProto maintenanceT1 = db.GetOrThrow<VirtualProductProto>(Ids.Products.MaintenanceT1);

        // The same Upoints stats category the vanilla Oil Rig and every WorldGen++ mine use -
        // registered by the base game itself, not WorldGen++, so this lookup succeeds
        // regardless of whether WorldGen++ is installed.
        UpointsStatsCategoryProto statsCategory = db.GetOrThrow<UpointsStatsCategoryProto>(new Proto.ID("UpointsStatsCat_WorldMapMines"));

        UpointsCategoryProto upointsCategory = db.Add(new UpointsCategoryProto(
            ModIds.World.NaturalGasRig,
            "Assets/Base/Icons/WorldMap/OilRig.svg",
            statsCategory));

        Proto.Str strings = Proto.CreateStr(
            ModIds.World.NaturalGasRig,
            ModTranslation.Get("world.NaturalGasRig.name", "Natural gas rig"),
            ModTranslation.Get("world.NaturalGasRig.description", "This station provides natural gas when assigned with workers."));

        // No dedicated "gas rig" World Map icon exists in the base game - both icon paths below
        // reuse the vanilla Oil Rig's own graphics as the closest available placeholder.
        var graphics = new WorldMapEntityProto.Gfx("Assets/Unity/UserInterface/WorldMap/OilRigBig.svg", "Assets/Base/Icons/WorldMap/OilRig.svg");

        EntityCosts costFor(int level) {
            AssetValue baseConstructionCost = level > 4
                ? new AssetValue(constructionParts3.WithQuantity(100 + (level - 3) * 100))
                : new AssetValue(constructionParts2.WithQuantity(200));
            return new EntityCosts(baseConstructionCost, 9, level * 18, new MaintenanceCosts(maintenanceT1, level * 18.Quantity()));
        }

        db.Add(new WorldMapMineProto(
            ModIds.World.NaturalGasRig,
            strings,
            new ProductQuantity(naturalGas, 10.Quantity()),
            20.Seconds(),
            0.4.Upoints(),
            upointsCategory,
            costFor(1),
            costFor,
            16,
            1000000.Quantity(),
            graphics));
    }
}

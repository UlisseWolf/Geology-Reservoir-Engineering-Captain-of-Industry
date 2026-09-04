using Mafi;
using Mafi.Base;
using Mafi.Base.Prototypes.Machines.PowerGenerators;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Registers two electricity generators - one burning vanilla Fuel Gas, one burning this mod's
/// Natural Gas - both reusing the vanilla Diesel Generator II's own prototype type, layout,
/// construction cost, and 3D model.
///
/// <c>ElectricityGeneratorFromProductProto</c> (the type both the vanilla Diesel Generator and
/// these two use) is a fully public, ready-made vanilla prototype, not something this mod
/// defines - it takes exactly one fixed input product per instance, unlike a
/// <c>RecipeProtoBuilder</c>-bound machine, which can offer several recipes on the same
/// building. Because of that one-fixed-input constraint, Fuel Gas and Natural Gas each need
/// their own separate machine rather than one machine offering both as alternatives - the same
/// underlying limitation that led the cargo ship fuel feature to use
/// <c>CargoShipProto.AvailableFuels</c> instead, since that array-based design *does* support
/// several alternatives on one entity.
///
/// Both generators reuse the vanilla Diesel Generator II's exact layout, construction cost
/// (<c>Costs.Machines.DieselGeneratorT2</c>), 5000 kW output, and prefab
/// (<c>Assets/Base/Machines/PowerPlant/CombustionEngineT2.prefab</c>) unmodified - visually
/// indistinguishable from the vanilla Diesel Generator II in-game. A recolored variant of this
/// prefab's texture exists as a source asset in this project
/// (<c>Assets/Geothermal/NaturalGasEngine-512-albedo.png</c>), but using it in-game would
/// require building and shipping a Unity AssetBundle containing a new prefab/material that
/// references it - the same asset pipeline already used for the Natural Gas product icon (see
/// "Custom assets" in the README) - which hasn't been done for this texture, so both generators
/// currently look identical to the vanilla machine they're built from.
///
/// Both are listed under this mod's own "Electric generators" toolbar subcategory (see
/// <see cref="ModIds.ToolbarCategories.ElectricGenerators"/>), alongside the vanilla Diesel
/// Generator itself, reassigned there from its default "General" subcategory by
/// <see cref="VanillaCategoryFixupData"/>.
///
/// Input quantities and pollution output follow this mod's established pattern for the
/// difference between treated Fuel Gas and raw Natural Gas (see also the gas-fired Boiler
/// recipe and the cargo ship fuel entries): Fuel Gas matches the vanilla Diesel Generator II's
/// own input rate one-to-one, while Natural Gas consumes 30% more and produces 30% more
/// Exhaust for the same electricity output, reflecting that raw, untreated gas burns less
/// efficiently and dirtier than an already-refined fuel - the same pollutant the vanilla Diesel
/// Generator II itself produces.
/// </summary>
internal class PowerGeneratorsData : IModData {

    /// <summary>Natural Gas consumes/pollutes this much more than Fuel Gas, for the same electricity output.</summary>
    private static readonly Percent NATURAL_GAS_MULTIPLIER = 130.Percent();

    public void RegisterData(ProtoRegistrator registrator) {
        ProtosDb db = registrator.PrototypesDb;

        ProductProto electricity = db.GetOrThrow<ProductProto>(IdsCore.Products.Electricity);
        ProductProto fuelGas = db.GetOrThrow<ProductProto>(Ids.Products.FuelGas);
        ProductProto naturalGas = db.GetOrThrow<ProductProto>(ModIds.Products.NaturalGas);
        ProductProto exhaust = db.GetOrThrow<ProductProto>(Ids.Products.Exhaust);

        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            "[3][3][3][3][4][4][4][4]",
            "[3][3][3][3][4][4][4][4]",
            "[3][3][3][3][4][4][4][4]",
            "F@^               v@S   ");

        EntityCosts costs = Costs.Machines.DieselGeneratorT2.MapToEntityCosts(registrator);

        ImmutableArray<ToolbarEntryData> categories = registrator.GetCategoriesProtos(ModIds.ToolbarCategories.ElectricGenerators);

        var gfx = new ElectricityGeneratorFromProductProto.Gfx(
            "Assets/Base/Machines/PowerPlant/CombustionEngineT2.prefab",
            ImmutableArray<ParticlesParams>.Empty,
            "Assets/Base/Machines/PowerPlant/CombustionEngine/CombustionEngine_Sound.prefab",
            categories,
            useSemiInstancedRendering: true);

        db.Add(new ElectricityGeneratorFromProductProto(
            ModIds.Machines.GasGenerator,
            Proto.CreateStr(
                ModIds.Machines.GasGenerator,
                ModTranslation.Get("build-machine.GasGenerator.name", "Gas generator"),
                ModTranslation.Get("build-machine.GasGenerator.description", "Burns Fuel Gas to create electricity.")),
            layout,
            costs,
            5000.Kw(),
            10,
            fuelGas.WithQuantity(6),
            exhaust.WithQuantity(8),
            electricity,
            4,
            20.Seconds(),
            DestroyReason.UsedAsFuel,
            ImmutableArray.Create<AnimationParams>(AnimationParams.Loop(150.Percent())),
            gfx));

        db.Add(new ElectricityGeneratorFromProductProto(
            ModIds.Machines.NaturalGasGenerator,
            Proto.CreateStr(
                ModIds.Machines.NaturalGasGenerator,
                ModTranslation.Get("build-machine.NaturalGasGenerator.name", "Natural gas generator"),
                ModTranslation.Get("build-machine.NaturalGasGenerator.description", "Burns raw Natural Gas to create electricity, without treating it into Fuel Gas first.")),
            layout,
            costs,
            5000.Kw(),
            10,
            naturalGas.WithQuantity(new Quantity(6).ScaledBy(NATURAL_GAS_MULTIPLIER).Value),
            exhaust.WithQuantity(new Quantity(8).ScaledBy(NATURAL_GAS_MULTIPLIER).Value),
            electricity,
            4,
            20.Seconds(),
            DestroyReason.UsedAsFuel,
            ImmutableArray.Create<AnimationParams>(AnimationParams.Loop(150.Percent())),
            gfx));
    }
}

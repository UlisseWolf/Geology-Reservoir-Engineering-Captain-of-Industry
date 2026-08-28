using Mafi;
using Mafi.Base;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.WellPumps;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using GeologyReservoirEngineering.Runtime;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Registers the geology/reservoir machines: three geothermal extraction wells, one per
/// enthalpy tier, and three dedicated injection pumps - Water (geothermal/groundwater), Oil
/// (crude oil), and Natural Gas - each entity-level restricted to its own fixed set of deposit
/// types.
///
/// The three wells are built with <see cref="WellPumpProtoBuilder"/>, in the same way the
/// vanilla oil pump is registered. All three injection pumps are built directly as
/// <c>InjectionPumpProto</c> (see <c>Source/Runtime</c>) rather than through
/// <c>MachineProtoBuilder</c>, so that it instantiates a custom entity class implementing
/// <c>IVirtualResourceMiningEntity</c> — the interface that drives the vanilla reserve-status
/// panel — and can automatically disable itself once its target deposit reaches capacity.
///
/// The injection pumps intentionally do not use <c>WellInjectionPumpProtoBuilder</c>: that
/// builder produces a warning when placed on a tile that already has a virtual resource, which
/// is precisely where a reinjection well is meant to be built. A plain machine avoids that
/// validation while the actual recharge behavior is handled separately, at runtime, by
/// <c>GeologyRegenManager</c> (see <c>Source/Runtime</c>).
///
/// All three injection pumps share the same <c>InjectionPumpProto</c>/<c>InjectionPump</c>
/// entity pair; they differ only in which deposit types they're constructed to recognize
/// (<c>InjectionPumpProto.AllowedResourceIds</c>), which recipe(s) are bound to them, and their
/// visual prefab. Because each pump is restricted to a single deposit category, none of them
/// need runtime recipe restriction - a pump that can only ever recognize one deposit type has
/// nothing to dynamically enable or disable; see <c>GeologyRegenManager</c> for the recharge
/// logic that does remain.
///
/// All seven machines this mod registers - the three geothermal wells, the natural gas well,
/// and the three injection pumps - reuse the vanilla Groundwater Pump prefab and layout, with
/// the fluid port direction flipped for the injection pumps (output for extraction, input for
/// injection). A separate recipe converts raw Natural Gas into vanilla Fuel Gas on the existing
/// Chemical Plant (see <c>registerNaturalGasTreatmentRecipe</c>).
/// </summary>
internal class MachinesData : IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        registerWell(
            registrator,
            name: ModTranslation.Get("build-machine.GeothermalWellHighEnthalpy.name", "Geothermal well (high enthalpy)"),
            resourceDescription: ModTranslation.Get("build-machine.GeothermalWellHighEnthalpy.reserve-description", "Shows the overall status of the high enthalpy geothermal reservoir. Left un-recharged, it will slowly deplete; an injection pump built nearby will recharge it over time."),
            machineId: ModIds.Machines.WellHighEnthalpy,
            minedResource: ModIds.VirtualResources.GeothermalHighEnthalpy,
            electricityKw: 220,
            steelCost: 220,
            workers: 3,
            recipeId: ModIds.Recipes.PumpingHighEnthalpy,
            outputProduct: Ids.Products.SteamHi,
            outputQuantity: 6);

        registerWell(
            registrator,
            name: ModTranslation.Get("build-machine.GeothermalWellMediumEnthalpy.name", "Geothermal well (medium enthalpy)"),
            resourceDescription: ModTranslation.Get("build-machine.GeothermalWellMediumEnthalpy.reserve-description", "Shows the overall status of the medium enthalpy geothermal reservoir. Left un-recharged, it will slowly deplete; an injection pump built nearby will recharge it over time."),
            machineId: ModIds.Machines.WellMediumEnthalpy,
            minedResource: ModIds.VirtualResources.GeothermalMediumEnthalpy,
            electricityKw: 160,
            steelCost: 180,
            workers: 2,
            recipeId: ModIds.Recipes.PumpingMediumEnthalpy,
            outputProduct: Ids.Products.SteamLo,
            outputQuantity: 6);

        registerWell(
            registrator,
            name: ModTranslation.Get("build-machine.GeothermalWellLowEnthalpy.name", "Geothermal well (low enthalpy)"),
            resourceDescription: ModTranslation.Get("build-machine.GeothermalWellLowEnthalpy.reserve-description", "Shows the overall status of the low enthalpy geothermal reservoir. Left un-recharged, it will slowly deplete; an injection pump built nearby will recharge it over time."),
            machineId: ModIds.Machines.WellLowEnthalpy,
            minedResource: ModIds.VirtualResources.GeothermalLowEnthalpy,
            electricityKw: 110,
            steelCost: 140,
            workers: 2,
            recipeId: ModIds.Recipes.PumpingLowEnthalpy,
            outputProduct: Ids.Products.SteamDepleted,
            outputQuantity: 6);

        // Each pump recharges whichever recognized deposit is present at its build location;
        // see GeologyRegenManager and InjectionPump for the runtime behavior.
        registerWaterInjectionPump(registrator);
        registerOilInjectionPump(registrator);

        registerNaturalGasWell(registrator);
        registerNaturalGasInjectionPump(registrator);
        registerNaturalGasTreatmentRecipe(registrator);
        registerNaturalGasDirectUseRecipes(registrator);
    }

    /// <summary>
    /// Registers the water injection pump: injects Water, restricted to only ever recognizing
    /// the three geothermal tiers and the vanilla Groundwater deposit
    /// (<see cref="InjectionPumpProto.AllowedResourceIds"/>). Crude oil and Natural Gas are
    /// handled by their own dedicated pumps - see <see cref="registerOilInjectionPump"/> and
    /// <see cref="registerNaturalGasInjectionPump"/>.
    /// </summary>
    private static void registerWaterInjectionPump(ProtoRegistrator registrator) {
        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            "[2][7][7][2]   ",
            "[2][7][7][2]   ",
            "[2][7][7][2]   ",
            "[2][4][4][2]X@<",
            "   [2][2][2]   ",
            "   [2][2][2]   ");

        // EntityCostsTpl.Build.Product(...).Workers(...) returns an EntityCostsTpl.Builder; an
        // implicit conversion to EntityCostsTpl applies on assignment.
        EntityCostsTpl costsTpl = EntityCostsTpl.Build.Product(200, Ids.Products.Steel).Workers(2);
        EntityCosts costs = costsTpl.MapToEntityCosts(registrator);

        ImmutableArray<ToolbarEntryData> categories = registrator.GetCategoriesProtos(ModIds.ToolbarCategories.Groundwater, ModIds.ToolbarCategories.Geothermal);

        // Feeds the reserve-status panel/overlay for each deposit type this pump can recharge.
        VirtualResourceProductProto geoHigh = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(ModIds.VirtualResources.GeothermalHighEnthalpy);
        VirtualResourceProductProto geoMedium = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(ModIds.VirtualResources.GeothermalMediumEnthalpy);
        VirtualResourceProductProto geoLow = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(ModIds.VirtualResources.GeothermalLowEnthalpy);
        VirtualResourceProductProto groundwater = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(Mafi.Core.IdsCore.Products.Groundwater);

        var visualizedLayers = new LayoutEntityProto.VisualizedLayers(
            terrainDesignators: false,
            treeDesignators: false,
            ImmutableArray<TerrainMaterialProto>.Empty,
            ImmutableArray.Create(geoHigh, geoMedium, geoLow, groundwater));

        // MachineProto declares its own Gfx type (MachineProto.Gfx : LayoutEntityProto.Gfx),
        // which carries machine-specific data such as machineSoundPrefabPath.
        var gfx = new MachineProto.Gfx(
            "Assets/Base/Machines/Pump/LandWaterPump.prefab",
            categories,
            machineSoundPrefabPath: Option.Create("Assets/Base/Machines/Pump/LandWaterPump/LandWaterPump_Sound.prefab"),
            useSemiInstancedRendering: true,
            visualizedLayers: visualizedLayers);

        var proto = new InjectionPumpProto(
            ModIds.Machines.WaterInjectionPump,
            Proto.CreateStr(
                ModIds.Machines.WaterInjectionPump,
                ModTranslation.Get("build-machine.WaterInjectionPump.name", "Water injection pump"),
                ModTranslation.Get("build-machine.WaterInjectionPump.description", "Injects water underground, recharging whatever deposit is beneath it over time - a geothermal reservoir or a groundwater aquifer. Build within range of the deposit it should support.")),
            layout,
            costs,
            220.Kw(),
            Computing.Zero,
            buffersMultiplier: null,
            useAllRecipesAtStartOrAfterUnlock: false,
            animationParams: ImmutableArray<AnimationParams>.Empty,
            graphics: gfx,
            allowedResourceIds: ImmutableArray.Create<Proto.ID>(
                ModIds.VirtualResources.GeothermalHighEnthalpy,
                ModIds.VirtualResources.GeothermalMediumEnthalpy,
                ModIds.VirtualResources.GeothermalLowEnthalpy,
                Mafi.Core.IdsCore.Products.Groundwater));

        registrator.PrototypesDb.Add(proto);

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.Injection)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(8, Ids.Products.Water, "X")
            .BuildAndAdd()
            .BindTo(proto, 10.Seconds());
    }

    /// <summary>
    /// Registers the oil injection pump: injects CO2 (enhanced oil recovery), Seawater
    /// (hydraulic fracturing), High steam, Super-pressurized steam (thermal EOR, as two
    /// separate recipes - see below), or Acid (acid stimulation), restricted to only ever
    /// recognizing the vanilla crude oil deposit (<see cref="InjectionPumpProto.AllowedResourceIds"/>).
    /// Separate from <see cref="registerWaterInjectionPump"/> so it cannot be used, by accident
    /// or otherwise, to inject anything into geothermal or groundwater deposits.
    ///
    /// The engine rejects two recipes bound to the same machine if they share the same set of
    /// input/output products, regardless of quantity, raising a <c>ProtoBuilderException</c> at
    /// registration time. This pump's layout also defines only one fluid input port ("X"), so a
    /// single recipe cannot bind two distinct input products to it - which is why thermal EOR is
    /// two separate single-input recipes rather than one recipe consuming both steam grades
    /// together. Every recipe here therefore consumes a genuinely different product from every
    /// other one on this pump. Enhanced oil recovery is modeled as gas-injection EOR
    /// (CarbonDioxide, reusing the vanilla product the base game's own CO2 disposal pump uses),
    /// hydraulic fracturing as seawater-based fracturing fluid (Seawater, also an existing
    /// vanilla product), thermal EOR as steam injection (High and Super-pressurized steam, each
    /// its own recipe - real cyclic steam stimulation/SAGD, and both products this mod's own
    /// geothermal wells already produce), and acid stimulation as matrix acidizing (vanilla
    /// Acid, dissolving rock near the wellbore to improve permeability - a real technique
    /// distinct from the other three, which raise reservoir pressure or reduce oil viscosity
    /// rather than improve permeability directly).
    ///
    /// All recipes here run much longer per cycle than the water injection recipe (10s),
    /// reflecting that real EOR, fracturing, thermal stimulation, and acidizing are slow,
    /// resource-intensive processes, not quick pump cycles - but this duration is purely a
    /// logistics cost (how much input the pump consumes per unit of real time), not a pacing
    /// control. <c>GeologyRegenManager</c> only checks whether the pump is actively working
    /// (<c>Machine.WorkedThisTick</c>), which is true for every tick a recipe is in progress
    /// regardless of its length; the actual recharge rate is entirely governed by the manager's
    /// own constants (<c>SLOW_REGEN_PER_CHECK</c> / <c>SLOW_CHECK_MULTIPLIER</c>), independent
    /// of recipe duration.
    /// </summary>
    private static void registerOilInjectionPump(ProtoRegistrator registrator) {
        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            "[2][7][7][2]   ",
            "[2][7][7][2]   ",
            "[2][7][7][2]   ",
            "[2][4][4][2]X@<",
            "   [2][2][2]   ",
            "   [2][2][2]   ");

        EntityCostsTpl costsTpl = EntityCostsTpl.Build.Product(200, Ids.Products.Steel).Workers(2);
        EntityCosts costs = costsTpl.MapToEntityCosts(registrator);

        ImmutableArray<ToolbarEntryData> categories = registrator.GetCategoriesProtos(ModIds.ToolbarCategories.OilWells);

        VirtualResourceProductProto crudeOil = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(Mafi.Core.IdsCore.Products.VirtualCrudeOil);

        var visualizedLayers = new LayoutEntityProto.VisualizedLayers(
            terrainDesignators: false,
            treeDesignators: false,
            ImmutableArray<TerrainMaterialProto>.Empty,
            ImmutableArray.Create(crudeOil));

        var gfx = new MachineProto.Gfx(
            "Assets/Base/Machines/Pump/LandWaterPump.prefab",
            categories,
            machineSoundPrefabPath: Option.Create("Assets/Base/Machines/Pump/LandWaterPump/LandWaterPump_Sound.prefab"),
            useSemiInstancedRendering: true,
            visualizedLayers: visualizedLayers);

        var proto = new InjectionPumpProto(
            ModIds.Machines.OilInjectionPump,
            Proto.CreateStr(
                ModIds.Machines.OilInjectionPump,
                ModTranslation.Get("build-machine.OilInjectionPump.name", "Oil injection pump"),
                ModTranslation.Get("build-machine.OilInjectionPump.description", "Injects CO2, seawater, or steam into a crude oil deposit for enhanced oil recovery, hydraulic fracturing, or thermal EOR. Only works on a crude oil deposit - build within range of the deposit it should support.")),
            layout,
            costs,
            220.Kw(),
            Computing.Zero,
            buffersMultiplier: null,
            useAllRecipesAtStartOrAfterUnlock: false,
            animationParams: ImmutableArray<AnimationParams>.Empty,
            graphics: gfx,
            allowedResourceIds: ImmutableArray.Create<Proto.ID>(Mafi.Core.IdsCore.Products.VirtualCrudeOil));

        registrator.PrototypesDb.Add(proto);

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.EnhancedOilRecovery)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(8, Ids.Products.CarbonDioxide, "X")
            .BuildAndAdd()
            .BindTo(proto, 180.Seconds());

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.HydraulicFracturing)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(16, Ids.Products.Seawater, "X")
            .BuildAndAdd()
            .BindTo(proto, 240.Seconds());

        // Thermal EOR: injects steam, reducing the oil's viscosity so it flows more easily -
        // real-world cyclic steam stimulation / SAGD. Two separate recipes, one per steam
        // grade, since this pump's layout defines only one fluid input port ("X") - a single
        // recipe cannot bind two distinct input products to one port. Each recipe's input is
        // still a genuinely different product from the other recipes on this pump (Water, CO2,
        // Seawater), so no risk of the same-input/output-signature collision the engine
        // otherwise rejects.
        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.ThermalEnhancedOilRecoveryHighSteam)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(10, Ids.Products.SteamHi, "X")
            .BuildAndAdd()
            .BindTo(proto, 210.Seconds());

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.ThermalEnhancedOilRecoverySuperSteam)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(6, Ids.Products.SteamSp, "X")
            .BuildAndAdd()
            .BindTo(proto, 210.Seconds());

        // Acid stimulation (matrix acidizing): a real, distinct well-stimulation technique from
        // the three above - injecting acid to dissolve rock near the wellbore and improve
        // permeability, rather than raising reservoir pressure (gas/water injection) or reducing
        // oil viscosity (thermal). Vanilla Acid is a genuinely different product from CO2,
        // Seawater, and the two steam grades already used on this pump, avoiding the
        // same-input/output-signature collision the engine otherwise rejects.
        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.AcidStimulation)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(10, Ids.Products.Acid, "X")
            .BuildAndAdd()
            .BindTo(proto, 200.Seconds());
    }

    /// <summary>
    /// Registers the natural gas injection pump: injects vanilla Fuel Gas (underground storage)
    /// or Low steam (thermal enhanced gas recovery), restricted to only ever recognizing the
    /// Natural Gas deposit (<see cref="InjectionPumpProto.AllowedResourceIds"/> contains only
    /// that one ID) - separate from <see cref="registerWaterInjectionPump"/> and
    /// <see cref="registerOilInjectionPump"/>, so it cannot be used, by accident or otherwise,
    /// to inject anything into geothermal, groundwater, or crude oil deposits. Storage injects
    /// vanilla Fuel Gas - already treated to pipeline quality, not raw Natural Gas - back into
    /// the ground, mirroring how real underground gas storage facilities bank already-processed
    /// gas during low demand for withdrawal later via the natural gas well. Thermal recovery
    /// injects steam instead, a genuinely different technique and a genuinely different input
    /// product, so the two recipes coexist without interfering with each other - see the
    /// remarks on <see cref="ModIds.Recipes.NaturalGasThermalRecovery"/>. Reuses the same
    /// <see cref="InjectionPump"/>/<see cref="InjectionPumpProto"/> entity pair as the other two
    /// injection pumps; only the allowed-resource list, the bound recipe(s), and the prefab
    /// differ.
    /// </summary>
    private static void registerNaturalGasInjectionPump(ProtoRegistrator registrator) {
        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            "[2][7][7][2]   ",
            "[2][7][7][2]   ",
            "[2][7][7][2]   ",
            "[2][4][4][2]X@<",
            "   [2][2][2]   ",
            "   [2][2][2]   ");

        EntityCostsTpl costsTpl = EntityCostsTpl.Build.Product(200, Ids.Products.Steel).Workers(2);
        EntityCosts costs = costsTpl.MapToEntityCosts(registrator);

        ImmutableArray<ToolbarEntryData> categories = registrator.GetCategoriesProtos(ModIds.ToolbarCategories.OilWells);

        VirtualResourceProductProto naturalGasDeposit = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(ModIds.VirtualResources.NaturalGas);

        var visualizedLayers = new LayoutEntityProto.VisualizedLayers(
            terrainDesignators: false,
            treeDesignators: false,
            ImmutableArray<TerrainMaterialProto>.Empty,
            ImmutableArray.Create(naturalGasDeposit));

        var gfx = new MachineProto.Gfx(
            "Assets/Base/Machines/Pump/LandWaterPump.prefab",
            categories,
            machineSoundPrefabPath: Option.Create("Assets/Base/Machines/Pump/LandWaterPump/LandWaterPump_Sound.prefab"),
            useSemiInstancedRendering: true,
            visualizedLayers: visualizedLayers);

        var proto = new InjectionPumpProto(
            ModIds.Machines.NaturalGasInjectionPump,
            Proto.CreateStr(
                ModIds.Machines.NaturalGasInjectionPump,
                ModTranslation.Get("build-machine.NaturalGasInjectionPump.name", "Natural gas injection pump"),
                ModTranslation.Get("build-machine.NaturalGasInjectionPump.description", "Injects treated Fuel Gas into a Natural Gas deposit for underground storage. Only works on a Natural Gas deposit - build within range of the deposit it should support.")),
            layout,
            costs,
            220.Kw(),
            Computing.Zero,
            buffersMultiplier: null,
            useAllRecipesAtStartOrAfterUnlock: false,
            animationParams: ImmutableArray<AnimationParams>.Empty,
            graphics: gfx,
            allowedResourceIds: ImmutableArray.Create<Proto.ID>(ModIds.VirtualResources.NaturalGas));

        registrator.PrototypesDb.Add(proto);

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.NaturalGasStorage)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(10, Ids.Products.FuelGas, "X")
            .BuildAndAdd()
            .BindTo(proto, 60.Seconds());

        // Thermal enhanced gas recovery: a second, genuinely distinct recipe on this same pump -
        // injects Low steam rather than Fuel Gas, reflecting real thermal stimulation that
        // improves gas flow, as opposed to storing already-treated gas for later withdrawal.
        // Low steam (a different product from Fuel Gas, and from the High/Super-pressurized
        // grades this mod's oil injection pump already uses) keeps this recipe's input
        // signature distinct, avoiding the same-signature collision the engine otherwise
        // rejects. Both recipes ultimately recharge the same Natural Gas deposit through the
        // same GeologyRegenManager pathway, which already caps recharge once per deposit per
        // check regardless of which recipe (or which pump) triggered it - so running both
        // simply offers two alternative ways to work the same deposit, neither one doubling up
        // on or interfering with the other.
        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.NaturalGasThermalRecovery)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(12, Ids.Products.SteamLo, "X")
            .BuildAndAdd()
            .BindTo(proto, 200.Seconds());
    }

    /// <summary>
    /// Registers a single geothermal extraction well bound to one enthalpy tier, along with its
    /// steam-output recipe.
    /// </summary>
    private static void registerWell(
        ProtoRegistrator registrator,
        string name,
        string resourceDescription,
        MachineProto.ID machineId,
        Mafi.Core.Products.VirtualResourceProductProto.ID minedResource,
        int electricityKw,
        int steelCost,
        int workers,
        Mafi.Core.Factory.Recipes.RecipeProto.ID recipeId,
        Mafi.Core.Products.ProductProto.ID outputProduct,
        int outputQuantity) {

        WellPumpProto well = registrator.WellPumpProtoBuilder
            .Start(name, resourceDescription, machineId)
            .Description(ModTranslation.Get("build-machine.GeothermalWell.description", "Pumps steam from an underground geothermal reservoir."))
            .SetLayout(
                "[2][7][7][2]   ",
                "[2][7][7][2]   ",
                "[2][7][7][2]   ",
                "[2][4][4][2]>@X",
                "   [2][2][2]   ",
                "   [2][2][2]   ")
            .SetPrefabPath("Assets/Base/Machines/Pump/LandWaterPump.prefab")
            .SetCost(EntityCostsTpl.Build.Product(steelCost, Ids.Products.Steel).Workers(workers))
            .SetElectricityConsumption(electricityKw.Kw())
            .SetMinedProduct(minedResource)
            .SetCategories(ModIds.ToolbarCategories.Geothermal)
            .SetMachineSound("Assets/Base/Machines/Pump/LandWaterPump/LandWaterPump_Sound.prefab")
            .EnableSemiInstancedRendering()
            .BuildAndAdd();

        registrator.RecipeProtoBuilder
            .Start(recipeId)
            .AddOutput(outputQuantity, outputProduct, "X")
            .BuildAndAdd()
            .BindTo(well, 10.Seconds());
    }

    /// <summary>
    /// Registers the natural gas extraction well. Built with the same
    /// <see cref="WellPumpProtoBuilder"/> extraction pattern as the geothermal wells - the same
    /// mechanism the vanilla Groundwater Pump and Oil Pump use - and visually reuses the vanilla
    /// Groundwater Pump's own prefab. Because it targets its own deposit type
    /// (<see cref="ModIds.VirtualResources.NaturalGas"/>) rather than crude oil, it can be built
    /// within the same deposit radius as a vanilla Oil Pump and run independently and
    /// simultaneously, extracting both resources from a co-located deposit (see
    /// <see cref="NaturalGasMapPatch"/>).
    /// </summary>
    private static void registerNaturalGasWell(ProtoRegistrator registrator) {
        WellPumpProto well = registrator.WellPumpProtoBuilder
            .Start(
                ModTranslation.Get("build-machine.NaturalGasWell.name", "Natural gas well"),
                ModTranslation.Get("build-machine.NaturalGasWell.reserve-description", "Shows the overall status of the natural gas deposit."),
                ModIds.Machines.NaturalGasWell)
            .Description(ModTranslation.Get("build-machine.NaturalGasWell.description", "Extracts raw natural gas from an underground deposit, whether standalone or associated with a crude oil deposit. Can be built alongside an Oil Pump on the same deposit to extract both simultaneously."))
            .SetLayout(
                "[2][7][7][2]   ",
                "[2][7][7][2]   ",
                "[2][7][7][2]   ",
                "[2][4][4][2]>@X",
                "   [2][2][2]   ",
                "   [2][2][2]   ")
            .SetPrefabPath("Assets/Base/Machines/Pump/LandWaterPump.prefab")
            .SetCost(EntityCostsTpl.Build.Product(200, Ids.Products.Steel).Workers(2))
            .SetElectricityConsumption(180.Kw())
            .SetMinedProduct(ModIds.VirtualResources.NaturalGas)
            .SetCategories(ModIds.ToolbarCategories.OilWells)
            .SetMachineSound("Assets/Base/Machines/Pump/LandWaterPump/LandWaterPump_Sound.prefab")
            .EnableSemiInstancedRendering()
            .BuildAndAdd();

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.NaturalGasExtraction)
            .AddOutput(6, ModIds.Products.NaturalGas, "X")
            .BuildAndAdd()
            .BindTo(well, 10.Seconds());
    }

    /// <summary>
    /// Binds a natural gas treatment recipe to the vanilla Chemical Plant (both tiers), not to
    /// any machine this mod registers. Converts raw Natural Gas into vanilla Fuel Gas, so every
    /// existing recipe that already consumes Fuel Gas becomes usable without modifying any of
    /// them individually - see <c>ModIds.Recipes.NaturalGasTreatment</c>.
    /// </summary>
    private static void registerNaturalGasTreatmentRecipe(ProtoRegistrator registrator) {
        MachineProto chemicalPlant = registrator.PrototypesDb.GetOrThrow<MachineProto>(Ids.Machines.ChemicalPlant);
        MachineProto chemicalPlant2 = registrator.PrototypesDb.GetOrThrow<MachineProto>(Ids.Machines.ChemicalPlant2);

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.NaturalGasTreatment)
            .AddInput(12, ModIds.Products.NaturalGas, "A")
            .AddOutput(10, Ids.Products.FuelGas, "X")
            .AddOutput(1, Ids.Products.SourWater, "Z")
            .BuildAndAdd()
            .BindTo(chemicalPlant, 20.Seconds())
            .BindTo(chemicalPlant2, 10.Seconds());
    }

    /// <summary>
    /// Binds two recipes that let raw Natural Gas be used directly, without first being treated
    /// into vanilla Fuel Gas on the Chemical Plant - both on vanilla machines, not any machine
    /// this mod registers:
    /// <list type="bullet">
    /// <item>The vanilla Flare (<see cref="Ids.Machines.Flare"/>) gains a disposal recipe for
    /// Natural Gas, mirroring its existing Fuel Gas recipe (16 in, 4 air pollution) - real-world
    /// gas flaring burns off excess associated gas that isn't worth capturing or treating.</item>
    /// <item>The vanilla gas-fired Boiler (<see cref="Ids.Machines.BoilerGas"/>) gains a steam
    /// generation recipe for Natural Gas (8 Water + 12 Natural Gas in, 8 High steam + 10 Exhaust
    /// out) - real natural gas is commonly burned directly for heat/steam without prior
    /// refining. Unlike the machine's existing Fuel Gas recipe, which produces CO2, this one
    /// produces Exhaust instead, reflecting that burning untreated gas is less clean than
    /// burning already-refined Fuel Gas.</item>
    /// </list>
    /// Neither recipe collides with any existing recipe on its machine, since Natural Gas is a
    /// product neither machine's vanilla recipes consume.
    /// </summary>
    private static void registerNaturalGasDirectUseRecipes(ProtoRegistrator registrator) {
        MachineProto flare = registrator.PrototypesDb.GetOrThrow<MachineProto>(Ids.Machines.Flare);

        // AddAirPollution itself (used by the vanilla Flare recipes this mirrors) lives on an
        // internal extension class in Mafi.Base and isn't accessible from an external mod
        // assembly. Its implementation is a plain AddOutput of the PollutedAir product, which is
        // reproduced directly here instead.
        ProductProto pollutedAir = registrator.PrototypesDb.GetOrThrow<ProductProto>(Mafi.Core.IdsCore.Products.PollutedAir);

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.NaturalGasFlaring)
            .SetProductsDestroyReason(Mafi.Core.Products.DestroyReason.DumpedOnTerrain)
            .AddInput(16, ModIds.Products.NaturalGas)
            .AddOutput(pollutedAir, 4.Quantity())
            .BuildAndAdd()
            .BindTo(flare, 20.Seconds(), 1, 1.Percent());

        MachineProto boilerGas = registrator.PrototypesDb.GetOrThrow<MachineProto>(Ids.Machines.BoilerGas);

        registrator.RecipeProtoBuilder
            .Start(ModIds.Recipes.NaturalGasSteamGeneration)
            .AddInput(8, Ids.Products.Water, "B")
            .AddInput(12, ModIds.Products.NaturalGas, "A")
            .AddOutput(8, Ids.Products.SteamHi, "X")
            .AddOutput(10, Ids.Products.Exhaust, "Y")
            .BuildAndAdd()
            .BindTo(boilerGas, 10.Seconds());
    }
}

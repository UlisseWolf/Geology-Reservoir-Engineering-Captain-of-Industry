using Mafi.Core.Entities;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Products;
using Mafi.Core.Research;

namespace GeologyReservoirEngineering;

/// <summary>
/// Prototype IDs owned by this mod. Centralizing them here lets other mods reference the
/// deposit types, machines, and research nodes this mod registers, without duplicating string
/// literals.
/// </summary>
public static class ModIds {

    /// <summary>
    /// Geothermal deposit tiers, registered as <see cref="VirtualResourceProductProto"/>. Being
    /// standard virtual terrain resources, they are automatically available in the in-game
    /// map/scenario editor's resource-feature list, in the same way as vanilla deposits.
    /// </summary>
    public static class VirtualResources {
        public static readonly VirtualResourceProductProto.ID GeothermalHighEnthalpy = new("GeothermalHighEnthalpy");
        public static readonly VirtualResourceProductProto.ID GeothermalMediumEnthalpy = new("GeothermalMediumEnthalpy");
        public static readonly VirtualResourceProductProto.ID GeothermalLowEnthalpy = new("GeothermalLowEnthalpy");

        /// <summary>
        /// A standalone natural gas deposit. Registered as data, so - like the tiers above - it
        /// is automatically placeable in the map editor, independent of any oil deposit. It is
        /// additionally co-located with existing crude oil deposits on the game's built-in maps
        /// via <see cref="Data.NaturalGasMapPatch"/>, representing associated gas; the two
        /// placement paths are independent of each other.
        /// </summary>
        public static readonly VirtualResourceProductProto.ID NaturalGas = new("NaturalGasDeposit");
    }

    public static class Products {
        /// <summary>
        /// Raw natural gas, as extracted from the ground - distinct from the vanilla Fuel Gas
        /// product, which represents refinery/processed gas several steps downstream of crude
        /// oil distillation. A dedicated Chemical Plant recipe converts this into Fuel Gas.
        /// </summary>
        public static readonly ProductProto.ID NaturalGas = new("NaturalGasProduct");
    }

    public static class Machines {
        /// <summary>Extraction wells, one per geothermal enthalpy tier.</summary>
        public static readonly MachineProto.ID WellHighEnthalpy = new("GeothermalWellHighEnthalpy");
        public static readonly MachineProto.ID WellMediumEnthalpy = new("GeothermalWellMediumEnthalpy");
        public static readonly MachineProto.ID WellLowEnthalpy = new("GeothermalWellLowEnthalpy");

        /// <summary>
        /// Injects Water, recharging whichever of the three geothermal tiers or the vanilla
        /// Groundwater deposit is present at its build location. Restricted to only those four
        /// deposit types via <c>InjectionPumpProto.AllowedResourceIds</c> - crude oil and
        /// Natural Gas are handled by their own dedicated pumps
        /// (<see cref="OilInjectionPump"/>, <see cref="NaturalGasInjectionPump"/>).
        /// </summary>
        public static readonly MachineProto.ID WaterInjectionPump = new("UndergroundInjectionPump");

        /// <summary>
        /// Injects CO2 (enhanced oil recovery) or Seawater (hydraulic fracturing), restricted to
        /// only ever recognizing the vanilla crude oil deposit via
        /// <c>InjectionPumpProto.AllowedResourceIds</c>. Separate from
        /// <see cref="WaterInjectionPump"/> so it cannot be used, by accident or otherwise, to
        /// inject anything into geothermal or groundwater deposits.
        /// </summary>
        public static readonly MachineProto.ID OilInjectionPump = new("OilInjectionPump");

        /// <summary>
        /// Natural gas extraction well. Built with the same <c>WellPumpProtoBuilder</c>
        /// extraction pattern as the geothermal wells and the vanilla Groundwater Pump, and
        /// visually reuses the Groundwater Pump's own prefab. Since it targets its own deposit
        /// type rather than crude oil, it can be built within the same deposit radius as the
        /// vanilla Oil Pump - both operate independently and simultaneously on a co-located
        /// oil + gas deposit.
        /// </summary>
        public static readonly MachineProto.ID NaturalGasWell = new("NaturalGasWell");

        /// <summary>
        /// Injects vanilla Fuel Gas into a Natural Gas deposit, simulating underground gas
        /// storage. Restricted to only ever recognizing Natural Gas via
        /// <c>InjectionPumpProto.AllowedResourceIds</c>, the same pattern as
        /// <see cref="OilInjectionPump"/>.
        /// </summary>
        public static readonly MachineProto.ID NaturalGasInjectionPump = new("NaturalGasInjectionPump");
    }

    public static class Recipes {
        public static readonly RecipeProto.ID PumpingHighEnthalpy = new("GeothermalPumpingHighEnthalpy");
        public static readonly RecipeProto.ID PumpingMediumEnthalpy = new("GeothermalPumpingMediumEnthalpy");
        public static readonly RecipeProto.ID PumpingLowEnthalpy = new("GeothermalPumpingLowEnthalpy");
        public static readonly RecipeProto.ID Injection = new("UndergroundInjectionRecipe");

        /// <summary>
        /// Bound to the dedicated oil injection pump (<see cref="Machines.OilInjectionPump"/>),
        /// targeting the vanilla crude oil deposit. Each is unlocked by its own research node.
        /// </summary>
        public static readonly RecipeProto.ID EnhancedOilRecovery = new("EnhancedOilRecoveryRecipe");
        public static readonly RecipeProto.ID HydraulicFracturing = new("HydraulicFracturingRecipe");

        /// <summary>
        /// Thermal EOR: injects steam into a crude oil deposit, reducing viscosity so oil flows
        /// more easily - real-world steam injection (cyclic steam stimulation / SAGD). Two
        /// separate recipes, one per steam grade, since the oil injection pump's layout defines
        /// only one fluid input port - a single recipe cannot bind two distinct input products
        /// to it. Both are unlocked together by the same research node. Bound to the same
        /// dedicated oil injection pump as the two recipes above.
        /// </summary>
        public static readonly RecipeProto.ID ThermalEnhancedOilRecoveryHighSteam = new("ThermalEnhancedOilRecoveryHighSteamRecipe");
        public static readonly RecipeProto.ID ThermalEnhancedOilRecoverySuperSteam = new("ThermalEnhancedOilRecoverySuperSteamRecipe");

        /// <summary>
        /// Acid stimulation (matrix acidizing): injects vanilla Acid into a crude oil deposit,
        /// dissolving rock near the wellbore to improve permeability - a real, distinct
        /// well-stimulation technique from gas injection, fracturing, or thermal EOR. Bound to
        /// the same dedicated oil injection pump as the recipes above.
        /// </summary>
        public static readonly RecipeProto.ID AcidStimulation = new("AcidStimulationRecipe");

        public static readonly RecipeProto.ID NaturalGasExtraction = new("NaturalGasExtractionRecipe");

        /// <summary>
        /// Bound to the vanilla Chemical Plant (<c>Ids.Machines.ChemicalPlant</c>), not to any
        /// machine this mod registers - converts raw natural gas into vanilla Fuel Gas, so every
        /// existing recipe that already consumes Fuel Gas (steam generation, hydrogen reforming,
        /// kiln fuel, and so on) becomes usable without modifying any of them individually.
        /// </summary>
        public static readonly RecipeProto.ID NaturalGasTreatment = new("NaturalGasTreatmentRecipe");

        /// <summary>
        /// Bound to the dedicated natural gas injection pump (<see cref="Machines.NaturalGasInjectionPump"/>),
        /// targeting the Natural Gas deposit - injects vanilla Fuel Gas (rather than raw Natural
        /// Gas) back into the ground, simulating real-world underground gas storage: gas already
        /// treated to pipeline quality is stored in a suitable deposit during low demand and
        /// withdrawn later via the natural gas well.
        /// </summary>
        public static readonly RecipeProto.ID NaturalGasStorage = new("NaturalGasStorageRecipe");

        /// <summary>
        /// Thermal enhanced gas recovery: injects Low steam into a Natural Gas deposit, a real,
        /// distinct technique (thermal stimulation improving gas flow) from
        /// <see cref="NaturalGasStorage"/> above, which injects already-treated Fuel Gas for
        /// storage rather than raw steam for recovery - two separate procedures on the same
        /// pump, each with its own genuinely different input product, so neither interferes
        /// with or duplicates the other's effect on the shared deposit (see
        /// <c>GeologyRegenManager</c>'s per-deposit-per-check recharge cap, which already
        /// applies uniformly regardless of which recipe on which pump triggered it).
        /// </summary>
        public static readonly RecipeProto.ID NaturalGasThermalRecovery = new("NaturalGasThermalRecoveryRecipe");

        /// <summary>
        /// Bound to the vanilla Flare (<c>Ids.Machines.Flare</c>) - burns excess raw Natural Gas
        /// on-site, mirroring real-world flaring of associated gas not worth capturing.
        /// </summary>
        public static readonly RecipeProto.ID NaturalGasFlaring = new("NaturalGasFlaringRecipe");

        /// <summary>
        /// Bound to the vanilla gas-fired Boiler (<c>Ids.Machines.BoilerGas</c>) - burns raw
        /// Natural Gas directly to generate steam, alongside the vanilla Fuel Gas recipe already
        /// on that machine.
        /// </summary>
        public static readonly RecipeProto.ID NaturalGasSteamGeneration = new("NaturalGasSteamGenerationRecipe");
    }

    public static class ResearchNodes {
        public static readonly ResearchNodeProto.ID GeothermalExtraction = new("GeothermalExtractionResearch");
        public static readonly ResearchNodeProto.ID SubsurfaceInjection = new("SubsurfaceInjectionResearch");
        public static readonly ResearchNodeProto.ID EnhancedOilRecovery = new("EnhancedOilRecoveryResearch");
        public static readonly ResearchNodeProto.ID HydraulicFracturing = new("HydraulicFracturingResearch");
        public static readonly ResearchNodeProto.ID ThermalEnhancedOilRecovery = new("ThermalEnhancedOilRecoveryResearch");
        public static readonly ResearchNodeProto.ID AcidStimulation = new("AcidStimulationResearch");
        public static readonly ResearchNodeProto.ID NaturalGasExtraction = new("NaturalGasExtractionResearch");
        public static readonly ResearchNodeProto.ID NaturalGasStorage = new("NaturalGasStorageResearch");
    }

    public static class ToolbarCategories {
        /// <summary>
        /// A subcategory of the vanilla "Power production" toolbar menu, sibling to the vanilla
        /// "General" and "Nuclear" subcategories. Holds the three geothermal wells. The water
        /// injection pump is also listed here in addition to the vanilla "Water" category,
        /// since it recharges both geothermal and groundwater deposits.
        /// </summary>
        public static readonly ToolbarCategoryProto.ID Geothermal = new("geothermalCategory");

        /// <summary>
        /// A subcategory of the vanilla "Water" toolbar menu. Holds the water injection pump.
        /// The vanilla Groundwater Pump is also reassigned here (see
        /// <see cref="Data.VanillaCategoryFixupData"/>) so neither pump is listed directly
        /// under "Water".
        /// </summary>
        public static readonly ToolbarCategoryProto.ID Groundwater = new("groundwaterCategory");

        /// <summary>
        /// A subcategory of the vanilla "Crude oil refining" toolbar menu. Holds the oil
        /// injection pump (enhanced oil recovery / hydraulic fracturing), the natural gas well,
        /// and the natural gas injection pump. The vanilla Oil Pump is also reassigned here from
        /// its default "Basic" subcategory (see <see cref="Data.VanillaCategoryFixupData"/>), so
        /// extraction, recovery/fracturing, and gas extraction/storage all live together in one
        /// place.
        /// </summary>
        public static readonly ToolbarCategoryProto.ID OilWells = new("oilWellsCategory");
    }

    /// <summary>
    /// World Map entity IDs. The vanilla game already has an Oil Rig World Map mine
    /// (<c>Ids.World.OilRigCost1</c> and its upgrade tiers, registered by
    /// <c>Mafi.Base.Prototypes.World.WorldMapEntitiesData</c>) - a core game feature, not
    /// something WorldGen++ introduces. WorldGen++ simply adds more mine types on top of this
    /// same vanilla system (see its own <c>WorldGenMineData</c>). This mod's Natural Gas Rig
    /// uses the identical public API, so it works identically whether WorldGen++ is installed
    /// or not - see <see cref="Data.WorldMapData"/>.
    /// </summary>
    public static class World {
        public static readonly EntityProto.ID NaturalGasRig = new("NaturalGasRig");
    }
}

using Mafi;
using Mafi.Base;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Registers eight research nodes, in dependency order (Underground water injection is
/// registered first, since Geothermal extraction depends on it):
///
/// <list type="number">
/// <item>
/// <b>Underground water injection</b> unlocks the water injection pump and its one recipe
/// (<c>AddRecipeToUnlock(ModIds.Recipes.Injection)</c> with <c>unlockAllRecipes: false</c>). It
/// depends on (<c>AddParents</c>) the vanilla "Groundwater pump" node, but is positioned
/// (<c>SetGridPosition</c>) beneath "Settlement water" — the unlock dependency and the node's
/// position in the research tree are set independently.
/// </item>
/// <item>
/// <b>Geothermal extraction</b> unlocks the three geothermal wells. It depends on Underground
/// water injection and three vanilla nodes: Power generation II, Water recovery, and Power
/// generation III. Positioned at (82, 33), near "Power generation III" (76, 31).
/// </item>
/// <item>
/// <b>Enhanced oil recovery</b>, <b>Hydraulic fracturing</b>, <b>Thermal enhanced oil
/// recovery</b>, and <b>Acid stimulation</b> each unlock the dedicated oil injection pump (a
/// separate machine from the water injection pump - see
/// <c>MachinesData.registerOilInjectionPump</c>). Enhanced oil recovery, Hydraulic fracturing,
/// and Acid stimulation each unlock their own one recipe; Thermal enhanced oil recovery unlocks
/// two recipes together (High steam and Super-pressurized steam), since the oil injection
/// pump's single fluid input port means the technique is split into two recipes rather than one
/// consuming both steam grades at once. All four nodes call <c>AddMachineToUnlock</c>, since
/// none of them alone is otherwise responsible for unlocking that machine; unlocking an
/// already-unlocked machine from a second, third, or fourth node is idempotent, mirroring how
/// vanilla research nodes sometimes offer alternate unlock paths to the same content. Enhanced
/// oil recovery depends on the vanilla "CO2 recycling" node (cost 96) — consistent with its
/// recipe consuming CO2 — at a cost of 110, positioned at (132, 7), near "CO2 recycling"
/// (124, 7). Hydraulic fracturing depends on "Thermal desalination" (cost 60, distinct from
/// "Basic desalination" and "Vacuum desalination") at a cost of 75, positioned at (100, 11).
/// Thermal enhanced oil recovery depends on the vanilla "Super heated steam" node (cost 240) —
/// consistent with one of its two recipes consuming Super-pressurized steam — at a cost of 260,
/// positioned at (200, 33), on the same row as "Super heated steam" (156, 33), far enough right
/// to also clear a cluster of nodes belonging to a different, unrelated third-party mod (Fusion
/// Horizon) that happens to occupy (124-195, 31-50) in this save's tree - the margin-check
/// method described below only checks vanilla positions, so a collision with another mod's
/// nodes isn't caught by it; there is no general way to know every other mod's node positions in
/// advance, so a wide margin here is a mitigation, not a guarantee. Acid stimulation depends on
/// the vanilla "Sulfur processing" node (cost 54) — consistent with its recipe consuming Acid,
/// itself unlocked by that same node — at a cost of 65, positioned at (70, 35).
/// </item>
/// <item>
/// <b>Natural gas extraction</b> unlocks the natural gas well and, via
/// <c>AddRecipeToUnlock</c>, five recipes: the Chemical Plant treatment recipe that converts
/// raw Natural Gas into vanilla Fuel Gas, a Flare disposal recipe, a gas-fired Boiler steam
/// generation recipe, and thermal enhanced gas recovery (Low steam injection on the natural gas
/// injection pump) - all bundled together rather than split into separate nodes, since the
/// well alone produces raw gas with nowhere to go and every consuming recipe needs the well to
/// have something to consume. The thermal recovery recipe's own machine (the natural gas
/// injection pump) isn't unlocked here - Underground gas storage below does that - but since
/// that node depends on this one, the recipe is never usable before its machine exists.
/// Depends on the vanilla "Hydrogen production" node (cost 72), keeping the dependency
/// alongside other gas-handling technologies rather than the oil-extraction branch, at a cost
/// of 85, positioned at (102, 3), near "Hydrogen production" (88, 3).
/// </item>
/// <item>
/// <b>Underground gas storage</b> unlocks the dedicated natural gas injection pump (a separate
/// machine from the water and oil injection pumps - see
/// <c>MachinesData.registerNaturalGasInjectionPump</c>) and, explicitly, only its storage
/// recipe (<c>unlockAllRecipes: false</c>) - thermal enhanced gas recovery, the pump's other
/// recipe, is unlocked separately by Natural gas extraction above, so the two procedures stay
/// gated by different nodes even though they share a machine and don't interfere with each
/// other at runtime. Depends only on Natural gas extraction, since storage is a use for
/// already-treated gas, downstream of being able to extract/treat it at all, at a cost of 100,
/// positioned at (108, 1).
/// </item>
/// </list>
///
/// Every grid position above was verified to have a minimum 6-unit clearance in both X and Y
/// from every vanilla node position registered in <c>ResearchNodesPositionSetup.cs</c> - an
/// exact-coordinate match isn't sufficient, since two node boxes positioned only a few units
/// apart render visually merged in-game even without sharing a coordinate.
/// </summary>
internal class ResearchData : IResearchNodesData, IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        ResearchNodeProto groundwaterPump = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.UndergroundWater);

        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.SubsurfaceInjection.name", "Underground water injection"), ModIds.ResearchNodes.SubsurfaceInjection, costMonths: 28)
            .AddParents(groundwaterPump)
            .SetGridPosition(new Vector2i(28, 29))
            .AddMachineToUnlock(ModIds.Machines.WaterInjectionPump, unlockAllRecipes: false)
            .AddRecipeToUnlock(ModIds.Recipes.Injection)
            .BuildAndAdd();

        ResearchNodeProto powerGeneration2 = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.PowerGeneration2);
        ResearchNodeProto waterRecovery = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.WaterRecovery);
        ResearchNodeProto powerGeneration3 = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.PowerGeneration3);

        ResearchNodeProto subsurfaceInjection = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(ModIds.ResearchNodes.SubsurfaceInjection);

        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.GeothermalExtraction.name", "Geothermal extraction"), ModIds.ResearchNodes.GeothermalExtraction, costMonths: 90)
            .Description(ModTranslation.Get("research.GeothermalExtraction.description", "Unlocks the three geothermal extraction wells, tapping high, medium, and low enthalpy reservoirs to produce steam."))
            .AddParents(subsurfaceInjection, powerGeneration2, waterRecovery, powerGeneration3)
            .SetGridPosition(new Vector2i(82, 33))
            .AddMachineToUnlock(ModIds.Machines.WellHighEnthalpy, unlockAllRecipes: true)
            .AddMachineToUnlock(ModIds.Machines.WellMediumEnthalpy, unlockAllRecipes: true)
            .AddMachineToUnlock(ModIds.Machines.WellLowEnthalpy, unlockAllRecipes: true)
            .BuildAndAdd();

        ResearchNodeProto carbonDioxideRecycling = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.CarbonDioxideRecycling);
        ResearchNodeProto thermalDesalination = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.ThermalDesalination);

        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.EnhancedOilRecovery.name", "Enhanced oil recovery"), ModIds.ResearchNodes.EnhancedOilRecovery, costMonths: 110)
            .Description(ModTranslation.Get("research.EnhancedOilRecovery.description", "Unlocks CO2 gas injection on the oil injection pump, recovering additional oil from an existing crude oil deposit."))
            .AddParents(carbonDioxideRecycling)
            .SetGridPosition(new Vector2i(132, 7))
            .AddMachineToUnlock(ModIds.Machines.OilInjectionPump, unlockAllRecipes: false)
            .AddRecipeToUnlock(ModIds.Recipes.EnhancedOilRecovery)
            .BuildAndAdd();

        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.HydraulicFracturing.name", "Hydraulic fracturing"), ModIds.ResearchNodes.HydraulicFracturing, costMonths: 75)
            .Description(ModTranslation.Get("research.HydraulicFracturing.description", "Unlocks seawater-based hydraulic fracturing on the oil injection pump, recovering additional oil from an existing crude oil deposit."))
            .AddParents(thermalDesalination)
            .SetGridPosition(new Vector2i(100, 11))
            .AddMachineToUnlock(ModIds.Machines.OilInjectionPump, unlockAllRecipes: false)
            .AddRecipeToUnlock(ModIds.Recipes.HydraulicFracturing)
            .BuildAndAdd();

        ResearchNodeProto superPressSteam = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.SuperPressSteam);

        // Depends on vanilla "Super heated steam" (cost 240) specifically, since one of the two
        // recipes it unlocks consumes that exact product - the same rationale already used for
        // tying Enhanced oil recovery to "CO2 recycling" and Hydraulic fracturing to "Thermal
        // desalination". Unlocks both thermal EOR recipes (High steam and Super-pressurized
        // steam) together, since they are two halves of one technique split only because the
        // oil injection pump's layout has a single fluid input port.
        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.ThermalEnhancedOilRecovery.name", "Thermal enhanced oil recovery"), ModIds.ResearchNodes.ThermalEnhancedOilRecovery, costMonths: 260)
            .AddParents(superPressSteam)
            .SetGridPosition(new Vector2i(200, 33))
            .AddMachineToUnlock(ModIds.Machines.OilInjectionPump, unlockAllRecipes: false)
            .AddRecipeToUnlock(ModIds.Recipes.ThermalEnhancedOilRecoveryHighSteam)
            .AddRecipeToUnlock(ModIds.Recipes.ThermalEnhancedOilRecoverySuperSteam)
            .BuildAndAdd();

        ResearchNodeProto sulfurProcessing = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.SulfurProcessing);

        // Depends on vanilla "Sulfur processing" (cost 54), which is what unlocks Acid
        // production in the first place - consistent with tying each oil injection pump recipe
        // to whichever vanilla node unlocks its specific input product.
        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.AcidStimulation.name", "Acid stimulation"), ModIds.ResearchNodes.AcidStimulation, costMonths: 65)
            .Description(ModTranslation.Get("research.AcidStimulation.description", "Unlocks acid injection on the oil injection pump, recovering additional oil from an existing crude oil deposit."))
            .AddParents(sulfurProcessing)
            .SetGridPosition(new Vector2i(70, 35))
            .AddMachineToUnlock(ModIds.Machines.OilInjectionPump, unlockAllRecipes: false)
            .AddRecipeToUnlock(ModIds.Recipes.AcidStimulation)
            .BuildAndAdd();

        ResearchNodeProto hydrogenProduction = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.HydrogenReforming);

        // Unlocks the extraction well, the Chemical Plant treatment recipe, the two direct-use
        // recipes on the vanilla Flare and gas-fired Boiler, and the thermal enhanced gas
        // recovery recipe on the natural gas injection pump - all bundled together, since the
        // well alone produces raw gas with nowhere to go and every consuming recipe needs the
        // well to have something to consume. The thermal recovery recipe's own machine (the
        // natural gas injection pump) isn't unlocked here - "Underground gas storage" below
        // does that - but since that node depends on this one, the recipe is never usable before
        // its machine exists. Depends on "Hydrogen production" rather than "Basic diesel": both
        // raw natural gas and hydrogen reforming are gas-handling technologies, and this keeps
        // the dependency in that part of the tree instead of the oil-extraction branch.
        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.NaturalGasExtraction.name", "Natural gas extraction"), ModIds.ResearchNodes.NaturalGasExtraction, costMonths: 85)
            .AddParents(hydrogenProduction)
            .SetGridPosition(new Vector2i(102, 3))
            .AddMachineToUnlock(ModIds.Machines.NaturalGasWell, unlockAllRecipes: true)
            .AddRecipeToUnlock(ModIds.Recipes.NaturalGasTreatment)
            .AddRecipeToUnlock(ModIds.Recipes.NaturalGasFlaring)
            .AddRecipeToUnlock(ModIds.Recipes.NaturalGasSteamGeneration)
            .AddRecipeToUnlock(ModIds.Recipes.NaturalGasThermalRecovery)
            .BuildAndAdd();

        ResearchNodeProto naturalGasExtraction = registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(ModIds.ResearchNodes.NaturalGasExtraction);

        // Unlocks the dedicated natural gas injection pump (a separate machine from the water
        // and oil injection pumps - see MachinesData.registerNaturalGasInjectionPump) and,
        // explicitly, only its storage recipe - unlockAllRecipes is false here so that thermal
        // enhanced gas recovery, a second recipe on this same pump, stays gated by Natural gas
        // extraction above rather than being unlocked as a side effect of this one. Depends only
        // on Natural gas extraction, since storage is a use for already-treated gas, downstream
        // of being able to extract/treat it at all.
        registrator.ResearchNodeProtoBuilder
            .Start(ModTranslation.Get("research.NaturalGasStorage.name", "Underground gas storage"), ModIds.ResearchNodes.NaturalGasStorage, costMonths: 100)
            .Description(ModTranslation.Get("research.NaturalGasStorage.description", "Unlocks the natural gas injection pump, storing already-treated Fuel Gas underground in a Natural Gas deposit for later withdrawal."))
            .AddParents(naturalGasExtraction)
            .SetGridPosition(new Vector2i(108, 1))
            .AddMachineToUnlock(ModIds.Machines.NaturalGasInjectionPump, unlockAllRecipes: false)
            .AddRecipeToUnlock(ModIds.Recipes.NaturalGasStorage)
            .BuildAndAdd();
    }
}

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;

namespace GeologyReservoirEngineering.Runtime;

/// <summary>
/// Recharges deposits that this mod's injection pumps are connected to.
///
/// Three pumps are involved - water (geothermal/groundwater), oil (crude oil), and natural gas
/// - each entity-level restricted to its own fixed set of deposit types via
/// <c>InjectionPumpProto.AllowedResourceIds</c> (see <c>MachinesData.cs</c>). Because each pump
/// can only ever recognize the deposit type(s) it was registered for, no runtime recipe
/// restriction is needed here: whichever recipe(s) research has unlocked on a given pump are
/// simply usable, since building that pump anywhere its recipe wouldn't apply just means it
/// recharges nothing.
///
/// This manager must check <c>AllowedResourceIds</c> itself, explicitly, for each machine before
/// recharging anything at its tile - it is not enforced automatically just because
/// <c>InjectionPump</c> (the entity class) already restricts its own reserve-panel display to
/// the same set. Without this check, a pump could recharge a deposit type it isn't restricted
/// to whenever a different, allowed deposit happens to share the same tile - for example, a
/// natural gas injection pump built on a co-located crude oil + Natural Gas site (see
/// <c>NaturalGasMapPatch</c>) recharging the crude oil deposit there too, despite being
/// restricted to Natural Gas everywhere else. The explicit <c>allowedIds.Contains(...)</c>
/// check below, applied per machine before its resources are considered for recharge, is what
/// prevents this.
///
/// While a pump is enabled AND actively completing production cycles (see
/// <see cref="Machine.WorkedThisTick"/> below), this manager tops up the deposit at its location
/// using <see cref="IVirtualTerrainResource.AddAsMuchAs"/>, which clamps to the deposit's
/// configured capacity. This covers the three geothermal tiers this mod introduces, the vanilla
/// Groundwater deposit, the vanilla crude oil deposit, and the Natural Gas deposit, so a single
/// recharge loop serves geothermal reinjection, aquifer water storage, enhanced oil recovery,
/// hydraulic fracturing, and underground gas storage alike, across all three pumps.
///
/// Checking <c>machine.IsEnabled</c> alone is not sufficient: a machine stays enabled while
/// blocked waiting for input ("waiting for products" in the UI). <c>Machine.WorkedThisTick</c>
/// (backed by the public <c>CurrentState</c> property) reflects whether the machine actually
/// completed a production cycle, and is checked here alongside <c>IsEnabled</c> so recharging
/// only happens when the pump is genuinely running, not merely powered on.
///
/// Three separate recharge rates are used, reflecting three different real-world pacing
/// categories:
/// <list type="bullet">
/// <item>Geothermal (the three enthalpy tiers this mod introduces) recharges fastest
/// (<see cref="GEOTHERMAL_REGEN_PER_CHECK"/> every <see cref="STEPS_BETWEEN_CHECKS"/> steps) -
/// reinjection is an immediate, intentional part of geothermal operation, maintaining reservoir
/// pressure for continued heat extraction.</item>
/// <item>Groundwater recharges at a distinctly slower rate
/// (<see cref="GROUNDWATER_REGEN_PER_CHECK"/>, checked only once every
/// <see cref="GROUNDWATER_CHECK_MULTIPLIER"/> general checks) - real aquifer recharge, natural
/// or managed, happens over much longer timescales than geothermal reinjection.</item>
/// <item>Crude oil and Natural Gas recharge slowest of all
/// (<see cref="SLOW_REGEN_PER_CHECK"/>, checked only once every
/// <see cref="SLOW_CHECK_MULTIPLIER"/> general checks) - enhanced oil recovery, hydraulic
/// fracturing, thermal EOR, acid stimulation, and underground gas storage each improve how much
/// of a field's resource is ultimately recoverable/available by a modest, bounded amount, not an
/// indefinite refill. Oil and gas share this same rate/cadence rather than each having their
/// own, since both represent the same category of "geological, not indefinitely replenishable"
/// resource in this mod's model.
/// </item>
/// </list>
///
/// Recipe duration is not part of any of this pacing, despite its name suggesting otherwise: a
/// machine is in <c>State.Working</c> - and therefore <c>WorkedThisTick</c> is true - on every
/// simulation tick a recipe is actively in progress, not only on the tick it completes. A pump
/// running a 240-second recipe is "working" just as continuously as one running a 10-second
/// recipe, provided its input supply never runs out. Recipe duration governs how much input a
/// pump consumes per unit of real time (its logistics cost), not how often this manager finds
/// it actively working. The only actual throttle on recharge rate is the constants below.
///
/// Recharge is capped once per deposit per check, not once per pump: a deposit's radius means
/// multiple pumps can be built at different positions and all resolve to the same underlying
/// deposit. Without a cap, each working pump targeting that deposit would trigger its own
/// <c>AddAsMuchAs</c> call in the same check, so recharge would scale linearly, and uncapped,
/// with the number of pumps built around a single deposit - reachable even though each tier's
/// own rate/cadence deliberately keeps a single pump's pace measured. <see cref="OnSimUpdate"/>
/// tracks which deposits (by position) have already been recharged in the current check and
/// skips any further pump targeting the same one, so building more pumps around a deposit adds
/// redundancy rather than compounding recharge speed.
///
/// The manager also periodically calls <see cref="Entity.UpdateIsEnabled"/> on each pump, since
/// the engine only re-evaluates a machine's enabled state at discrete trigger points
/// (construction, pause toggling, maintenance events), not on every simulation tick. Forcing a
/// periodic check ensures each pump's auto-stop-when-full behavior (implemented in
/// <c>InjectionPump.IsEnabledNow</c>) is applied continuously during normal play.
///
/// This class is wired through <see cref="GeologyReservoirEngineeringMod.Initialize"/> using
/// standard dependency injection and public engine interfaces. No Harmony patching is involved.
/// </summary>
public sealed class GeologyRegenManager : IDisposable {

    /// <summary>Quantity restored to a geothermal deposit on each check.</summary>
    private const int GEOTHERMAL_REGEN_PER_CHECK = 60;

    /// <summary>
    /// Quantity restored to the Groundwater deposit on each medium-tier check - substantially
    /// lower than <see cref="GEOTHERMAL_REGEN_PER_CHECK"/>, since real aquifer recharge is much
    /// slower than geothermal reinjection.
    /// </summary>
    private const int GROUNDWATER_REGEN_PER_CHECK = 20;

    /// <summary>
    /// Quantity restored to a crude oil or Natural Gas deposit on each slow-tier check -
    /// substantially lower than either tier above, since EOR/fracturing/gas storage represent a
    /// modest recovery/storage improvement in reality, not an indefinite refill.
    /// </summary>
    private const int SLOW_REGEN_PER_CHECK = 6;

    /// <summary>Number of simulation steps between general checks.</summary>
    private const int STEPS_BETWEEN_CHECKS = 30;

    /// <summary>
    /// Number of general checks between medium-tier (Groundwater) recharges - the deposit is
    /// recharged only once every this many <see cref="STEPS_BETWEEN_CHECKS"/> cycles
    /// (effectively every <c>STEPS_BETWEEN_CHECKS * GROUNDWATER_CHECK_MULTIPLIER</c> simulation
    /// steps), on top of the already-reduced <see cref="GROUNDWATER_REGEN_PER_CHECK"/> amount,
    /// kept separate from the geothermal and oil/gas cadences so tuning groundwater recharge
    /// speed doesn't affect them.
    /// </summary>
    private const int GROUNDWATER_CHECK_MULTIPLIER = 3;

    /// <summary>
    /// Number of general checks between slow-tier (oil/gas) recharges - see
    /// <see cref="GROUNDWATER_CHECK_MULTIPLIER"/> for how this style of multiplier works.
    /// </summary>
    private const int SLOW_CHECK_MULTIPLIER = 4;

    private readonly IEntitiesManager m_entitiesManager;
    private readonly IVirtualResourceManager m_virtualResourceManager;
    private readonly ISimLoopEvents m_simLoopEvents;

    private int m_stepsSinceLastCheck;
    private int m_checksSinceLastGroundwaterRecharge;
    private int m_checksSinceLastSlowRecharge;

    public GeologyRegenManager(
        IEntitiesManager entitiesManager,
        IVirtualResourceManager virtualResourceManager,
        ISimLoopEvents simLoopEvents) {

        m_entitiesManager = entitiesManager;
        m_virtualResourceManager = virtualResourceManager;
        m_simLoopEvents = simLoopEvents;

        ((IEventNonSaveable)m_simLoopEvents.Update).AddNonSaveable<GeologyRegenManager>(this, OnSimUpdate);
    }

    public void Dispose() {
        ((IEventNonSaveable)m_simLoopEvents.Update).RemoveNonSaveable<GeologyRegenManager>(this, OnSimUpdate);
    }

    private void OnSimUpdate() {
        m_stepsSinceLastCheck++;
        if (m_stepsSinceLastCheck < STEPS_BETWEEN_CHECKS) {
            return;
        }
        m_stepsSinceLastCheck = 0;

        m_checksSinceLastGroundwaterRecharge++;
        bool rechargeGroundwaterThisCheck = m_checksSinceLastGroundwaterRecharge >= GROUNDWATER_CHECK_MULTIPLIER;
        if (rechargeGroundwaterThisCheck) {
            m_checksSinceLastGroundwaterRecharge = 0;
        }

        m_checksSinceLastSlowRecharge++;
        bool rechargeSlowResourcesThisCheck = m_checksSinceLastSlowRecharge >= SLOW_CHECK_MULTIPLIER;
        if (rechargeSlowResourcesThisCheck) {
            m_checksSinceLastSlowRecharge = 0;
        }

        // Tracks deposits already recharged this check, by position, so a deposit reachable by
        // several pumps is only recharged once per check regardless of how many of them are
        // working - see the class-level remarks on per-deposit vs. per-pump capping.
        var rechargedDepositPositions = new HashSet<Tile3i>();

        foreach (Machine machine in m_entitiesManager.GetAllEntitiesOfType<Machine>()) {
            var machineId = (MachineProto.ID)machine.Prototype.Id;
            if (machineId != ModIds.Machines.WaterInjectionPump
                && machineId != ModIds.Machines.OilInjectionPump
                && machineId != ModIds.Machines.NaturalGasInjectionPump) {
                continue;
            }

            machine.UpdateIsEnabled();

            if (!machine.IsEnabled || !machine.WorkedThisTick) {
                continue;
            }

            ImmutableArray<Proto.ID> allowedIds = ((InjectionPumpProto)machine.Prototype).AllowedResourceIds;

            Tile2i tile = machine.Position2f.Tile2i;
            foreach (IVirtualTerrainResource resource in m_virtualResourceManager.RetrieveAllResourcesAt(tile)) {
                if (!allowedIds.Contains(allowedId => allowedId == resource.Product.Id)) {
                    continue;
                }

                if (isSlowTierResource(resource) && !rechargeSlowResourcesThisCheck) {
                    continue;
                }

                if (isGroundwater(resource) && !rechargeGroundwaterThisCheck) {
                    continue;
                }

                int? regenAmount = regenAmountFor(resource);
                if (!regenAmount.HasValue) {
                    continue;
                }

                if (!rechargedDepositPositions.Add(resource.Position)) {
                    continue;
                }

                resource.AddAsMuchAs(new Quantity(regenAmount.Value));
            }
        }
    }

    /// <summary>Whether the given resource uses the slower oil/gas recharge cadence.</summary>
    private static bool isSlowTierResource(IVirtualTerrainResource resource) {
        var id = resource.Product.Id;
        return id == Mafi.Core.IdsCore.Products.VirtualCrudeOil || id == ModIds.VirtualResources.NaturalGas;
    }

    /// <summary>Whether the given resource is the vanilla Groundwater deposit.</summary>
    private static bool isGroundwater(IVirtualTerrainResource resource) {
        return resource.Product.Id == Mafi.Core.IdsCore.Products.Groundwater;
    }

    /// <summary>
    /// The amount to recharge the given resource by on each check, or null if this mod does not
    /// recognize it. Crude oil, Natural Gas, and Groundwater each use a distinctly different
    /// rate from geothermal - see the class-level remarks.
    /// </summary>
    private static int? regenAmountFor(IVirtualTerrainResource resource) {
        var id = resource.Product.Id;
        if (id == Mafi.Core.IdsCore.Products.VirtualCrudeOil || id == ModIds.VirtualResources.NaturalGas) {
            return SLOW_REGEN_PER_CHECK;
        }
        if (id == Mafi.Core.IdsCore.Products.Groundwater) {
            return GROUNDWATER_REGEN_PER_CHECK;
        }
        if (id == ModIds.VirtualResources.GeothermalHighEnthalpy
            || id == ModIds.VirtualResources.GeothermalMediumEnthalpy
            || id == ModIds.VirtualResources.GeothermalLowEnthalpy) {
            return GEOTHERMAL_REGEN_PER_CHECK;
        }
        return null;
    }
}

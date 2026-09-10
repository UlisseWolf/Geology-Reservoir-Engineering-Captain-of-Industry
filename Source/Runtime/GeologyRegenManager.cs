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
/// Recharge is accumulated every simulation tick, not sampled at infrequent periodic checks.
/// <see cref="Machine.WorkedThisTick"/> reflects only the current tick, not "worked at some
/// point recently" - a pump that is genuinely productive most of the time, but happens to be
/// momentarily blocked waiting for input at the exact tick a periodic check fires, would lose
/// that entire cycle's recharge under a sampling design, even though it was actively working
/// almost the whole interval. A single pump's normal, intermittent logistics (brief gaps while
/// waiting for CO2/Seawater/Steam/Acid deliveries, for example) make this common. Checking every
/// tick instead - crediting a small fraction of a tier's per-tick rate to a deposit whenever any
/// qualifying pump on it is working that tick, and flushing whole units to
/// <see cref="IVirtualTerrainResource.AddAsMuchAs"/> once the accumulated fraction reaches at
/// least 1 - means a pump that works, say, a third of the time is credited a third of the
/// maximum rate, rather than being at the mercy of whether it happened to be active at one
/// arbitrarily-timed sample. Many pumps on the same deposit no longer need to "get lucky" to
/// reach the maximum rate either - one pump working continuously already achieves it.
///
/// <see cref="Machine.IsEnabled"/> alone is not sufficient to decide whether a pump is
/// contributing: a machine stays enabled while blocked waiting for input ("waiting for
/// products" in the UI). <c>WorkedThisTick</c> (backed by the public <c>CurrentState</c>
/// property) reflects whether the machine actually completed a production step this tick, and
/// is checked here alongside <c>IsEnabled</c> so accumulation only happens when the pump is
/// genuinely running, not merely powered on.
///
/// Three separate recharge rates are used, reflecting three different real-world pacing
/// categories - expressed here as a per-tick fraction of the same rates this manager used before
/// switching to per-tick accumulation, so the maximum achievable long-run rate for each tier is
/// unchanged, only how reliably a given pump count reaches it:
/// <list type="bullet">
/// <item>Geothermal (the three enthalpy tiers this mod introduces) recharges fastest
/// (<see cref="GEOTHERMAL_REGEN_PER_CHECK"/> every <see cref="STEPS_BETWEEN_CHECKS"/> ticks,
/// worth of accumulation) - reinjection is an immediate, intentional part of geothermal
/// operation, maintaining reservoir pressure for continued heat extraction.</item>
/// <item>Groundwater recharges at a distinctly slower rate
/// (<see cref="GROUNDWATER_REGEN_PER_CHECK"/> every <c>STEPS_BETWEEN_CHECKS *
/// GROUNDWATER_CHECK_MULTIPLIER</c> ticks, worth of accumulation) - real aquifer recharge,
/// natural or managed, happens over much longer timescales than geothermal reinjection.</item>
/// <item>Crude oil and Natural Gas recharge slowest of all
/// (<see cref="SLOW_REGEN_PER_CHECK"/> every <c>STEPS_BETWEEN_CHECKS * SLOW_CHECK_MULTIPLIER</c>
/// ticks, worth of accumulation) - enhanced oil recovery, hydraulic fracturing, thermal EOR,
/// acid stimulation, and underground gas storage each improve how much of a field's resource is
/// ultimately recoverable/available by a modest, bounded amount, not an indefinite refill. Oil
/// and gas share this same rate rather than each having their own, since both represent the
/// same category of "geological, not indefinitely replenishable" resource in this mod's model.
/// </item>
/// </list>
///
/// Recipe duration is still not part of any of this pacing, despite its name suggesting
/// otherwise: a machine is in <c>State.Working</c> - and therefore <c>WorkedThisTick</c> is true
/// - on every simulation tick a recipe is actively in progress, not only on the tick it
/// completes. A pump running a 240-second recipe is "working" just as continuously as one
/// running a 10-second recipe, provided its input supply never runs out. Recipe duration governs
/// how much input a pump consumes per unit of real time (its logistics cost), not how often this
/// manager finds it actively working.
///
/// Accumulation is still capped once per deposit per tick, not once per pump: a deposit's radius
/// means multiple pumps can be built at different positions and all resolve to the same
/// underlying deposit. Without this cap, each working pump targeting that deposit would credit
/// its own fraction in the same tick, so accumulation would scale linearly, and uncapped, with
/// the number of pumps built around a single deposit. <see cref="OnSimUpdate"/> tracks which
/// deposits (by position) have already been credited this tick and skips any further pump
/// targeting the same one, so building more pumps around a deposit improves the odds that at
/// least one of them is working on any given tick, without letting several simultaneously-
/// working pumps compound each other's contribution on the same tick.
///
/// The manager also periodically calls <see cref="Entity.UpdateIsEnabled"/> on each pump, since
/// the engine only re-evaluates a machine's enabled state at discrete trigger points
/// (construction, pause toggling, maintenance events), not on every simulation tick. This check
/// stays on the coarser <see cref="STEPS_BETWEEN_CHECKS"/> cadence, since the auto-stop-when-full
/// behavior it supports (implemented in <c>InjectionPump.IsEnabledNow</c>) doesn't need
/// per-tick precision the way accumulation does.
///
/// This class is wired through <see cref="GeologyReservoirEngineeringMod.Initialize"/> using
/// standard dependency injection and public engine interfaces. No Harmony patching is involved.
/// </summary>
public sealed class GeologyRegenManager : IDisposable {

    /// <summary>Quantity restored to a geothermal deposit over <see cref="STEPS_BETWEEN_CHECKS"/> ticks of continuous work.</summary>
    private const int GEOTHERMAL_REGEN_PER_CHECK = 60;

    /// <summary>
    /// Quantity restored to the Groundwater deposit over <c>STEPS_BETWEEN_CHECKS *
    /// GROUNDWATER_CHECK_MULTIPLIER</c> ticks of continuous work - substantially lower than
    /// <see cref="GEOTHERMAL_REGEN_PER_CHECK"/>'s equivalent rate, since real aquifer recharge
    /// is much slower than geothermal reinjection.
    /// </summary>
    private const int GROUNDWATER_REGEN_PER_CHECK = 20;

    /// <summary>
    /// Quantity restored to a crude oil or Natural Gas deposit over <c>STEPS_BETWEEN_CHECKS *
    /// SLOW_CHECK_MULTIPLIER</c> ticks of continuous work - substantially lower than either tier
    /// above, since EOR/fracturing/gas storage represent a modest recovery/storage improvement
    /// in reality, not an indefinite refill.
    /// </summary>
    private const int SLOW_REGEN_PER_CHECK = 6;

    /// <summary>Reference tick window the geothermal rate above is expressed over.</summary>
    private const int STEPS_BETWEEN_CHECKS = 30;

    /// <summary>How many <see cref="STEPS_BETWEEN_CHECKS"/> windows the Groundwater rate is expressed over.</summary>
    private const int GROUNDWATER_CHECK_MULTIPLIER = 3;

    /// <summary>How many <see cref="STEPS_BETWEEN_CHECKS"/> windows the oil/gas rate is expressed over.</summary>
    private const int SLOW_CHECK_MULTIPLIER = 4;

    private static readonly Fix32 GEOTHERMAL_REGEN_PER_TICK = Fix32.FromFraction(GEOTHERMAL_REGEN_PER_CHECK, STEPS_BETWEEN_CHECKS);
    private static readonly Fix32 GROUNDWATER_REGEN_PER_TICK = Fix32.FromFraction(GROUNDWATER_REGEN_PER_CHECK, STEPS_BETWEEN_CHECKS * GROUNDWATER_CHECK_MULTIPLIER);
    private static readonly Fix32 SLOW_REGEN_PER_TICK = Fix32.FromFraction(SLOW_REGEN_PER_CHECK, STEPS_BETWEEN_CHECKS * SLOW_CHECK_MULTIPLIER);

    private readonly IEntitiesManager m_entitiesManager;
    private readonly IVirtualResourceManager m_virtualResourceManager;
    private readonly ISimLoopEvents m_simLoopEvents;

    private int m_ticksSinceLastEnabledCheck;

    /// <summary>
    /// Fractional recharge accumulated so far for each deposit position, not yet large enough
    /// to flush a whole unit to <see cref="IVirtualTerrainResource.AddAsMuchAs"/>. Not saved -
    /// losing at most a few ticks' worth of partial progress on load is immaterial, and avoiding
    /// serialization keeps this manager a plain, unsaved service (see
    /// <see cref="GeologyReservoirEngineeringMod.Initialize"/>).
    /// </summary>
    private readonly Dictionary<Tile3i, Fix32> m_pendingRecharge = new();

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
        m_ticksSinceLastEnabledCheck++;
        bool refreshEnabledStateThisTick = m_ticksSinceLastEnabledCheck >= STEPS_BETWEEN_CHECKS;
        if (refreshEnabledStateThisTick) {
            m_ticksSinceLastEnabledCheck = 0;
        }

        // Tracks deposits already credited this tick, by position, so a deposit reachable by
        // several pumps is only credited once per tick regardless of how many of them are
        // working - see the class-level remarks on per-deposit vs. per-pump capping.
        var creditedPositionsThisTick = new HashSet<Tile3i>();

        foreach (Machine machine in m_entitiesManager.GetAllEntitiesOfType<Machine>()) {
            var machineId = (MachineProto.ID)machine.Prototype.Id;
            if (machineId != ModIds.Machines.WaterInjectionPump
                && machineId != ModIds.Machines.OilInjectionPump
                && machineId != ModIds.Machines.NaturalGasInjectionPump) {
                continue;
            }

            if (refreshEnabledStateThisTick) {
                machine.UpdateIsEnabled();
            }

            if (!machine.IsEnabled || !machine.WorkedThisTick) {
                continue;
            }

            ImmutableArray<Proto.ID> allowedIds = ((InjectionPumpProto)machine.Prototype).AllowedResourceIds;

            Tile2i tile = machine.Position2f.Tile2i;
            foreach (IVirtualTerrainResource resource in m_virtualResourceManager.RetrieveAllResourcesAt(tile)) {
                if (!allowedIds.Contains(allowedId => allowedId == resource.Product.Id)) {
                    continue;
                }

                Fix32? regenPerTick = regenPerTickFor(resource);
                if (!regenPerTick.HasValue) {
                    continue;
                }

                if (!creditedPositionsThisTick.Add(resource.Position)) {
                    continue;
                }

                accumulate(resource, regenPerTick.Value);
            }
        }
    }

    /// <summary>
    /// Adds this tick's fractional credit for the given deposit, flushing whole units to the
    /// deposit itself once the running total reaches at least 1.
    /// </summary>
    private void accumulate(IVirtualTerrainResource resource, Fix32 regenPerTick) {
        Fix32 pending = m_pendingRecharge.TryGetValue(resource.Position, out Fix32 existing) ? existing : Fix32.Zero;
        pending += regenPerTick;

        int wholeUnits = pending.ToIntFloored();
        if (wholeUnits > 0) {
            resource.AddAsMuchAs(new Quantity(wholeUnits));
            pending -= wholeUnits;
        }

        m_pendingRecharge[resource.Position] = pending;
    }

    /// <summary>
    /// The per-tick fraction to accumulate for the given resource, or null if this mod does not
    /// recognize it. Crude oil, Natural Gas, and Groundwater each use a distinctly different
    /// rate from geothermal - see the class-level remarks.
    /// </summary>
    private static Fix32? regenPerTickFor(IVirtualTerrainResource resource) {
        var id = resource.Product.Id;
        if (id == Mafi.Core.IdsCore.Products.VirtualCrudeOil || id == ModIds.VirtualResources.NaturalGas) {
            return SLOW_REGEN_PER_TICK;
        }
        if (id == Mafi.Core.IdsCore.Products.Groundwater) {
            return GROUNDWATER_REGEN_PER_TICK;
        }
        if (id == ModIds.VirtualResources.GeothermalHighEnthalpy
            || id == ModIds.VirtualResources.GeothermalMediumEnthalpy
            || id == ModIds.VirtualResources.GeothermalLowEnthalpy) {
            return GEOTHERMAL_REGEN_PER_TICK;
        }
        return null;
    }
}

using System.Collections.Generic;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.WellPumps;
using Mafi.Core.Maintenance;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;
using Mafi.Core.Vehicles;

namespace GeologyReservoirEngineering.Runtime;

/// <summary>
/// Runtime entity for the injection pump.
///
/// Implements <see cref="IVirtualResourceMiningEntity"/>, the interface the vanilla
/// <c>WellPump</c> uses, so the game's entity inspector displays the reserve-status panel for
/// whichever deposit — geothermal, Groundwater, crude oil, or Natural Gas — is present at the
/// pump's location, even though this machine injects rather than extracts.
///
/// <see cref="FindAllRechargeableResources"/> collects every recognized resource at the tile,
/// not just the first one found. This matters for a pump whose <c>AllowedResourceIds</c> spans
/// more than one deposit type at once (in practice, only the water injection pump, whose
/// allowed set covers all three geothermal tiers and Groundwater together - the oil and natural
/// gas injection pumps each only ever recognize a single deposit type, so this situation cannot
/// arise for them). <see cref="CapacityOfMine"/> and <see cref="QuantityLeftToMine"/> report the
/// sum across every resource found, so the panel reflects the pump's full recharge
/// responsibility rather than an arbitrary single pick; <see cref="ProductToMine"/> reports
/// whichever resource has the lowest fill percentage, surfacing the one most in need of
/// attention for its icon/name. <see cref="IsEnabledNow"/> similarly stays enabled as long as
/// any recognized resource still has room, rather than only checking one. None of this affects
/// which deposits actually get recharged - <c>GeologyRegenManager</c> already iterates every
/// resource at a pump's tile independently of what this class reports for display.
///
/// The entity stores no persisted fields of its own. <see cref="ProductToMine"/>,
/// <see cref="CapacityOfMine"/>, and <see cref="QuantityLeftToMine"/> are recomputed on demand
/// from <see cref="IVirtualResourceManager"/>, which is supplied fresh by dependency injection
/// on every construction, whether the entity is newly built or restored from a save. As a
/// result, this class relies entirely on the serialization already provided by its base
/// <c>Machine</c> class.
///
/// <see cref="IsEnabledNow"/> is overridden to disable the pump once its target deposit(s) reach
/// full capacity, stopping input consumption once storage is full.
/// </summary>
public sealed class InjectionPump : Machine, IVirtualResourceMiningEntity {

    private readonly IVirtualResourceManager m_virtualResourceManager;

    public InjectionPump(
        EntityId id,
        InjectionPumpProto proto,
        TileTransform transform,
        EntityContext context,
        VirtualBuffersMap buffersMap,
        IVirtualResourceManager virtualResourceManager,
        UnlockedProtosDb unlockedProtosDb,
        IVehicleBuffersRegistry vehicleBuffersRegistry,
        IEntityMaintenanceProvidersFactory maintenanceProvidersFactory,
        IAnimationStateFactory animationStateFactory)
        : base(id, proto, transform, context, buffersMap, unlockedProtosDb, vehicleBuffersRegistry, maintenanceProvidersFactory, animationStateFactory) {

        m_virtualResourceManager = virtualResourceManager;
    }

    /// <summary>
    /// Finds every deposit this instance is allowed to recharge/report on at its current
    /// position - restricted to <see cref="InjectionPumpProto.AllowedResourceIds"/> on this
    /// specific machine's prototype, so the water, oil, and natural gas injection pumps (see
    /// <c>MachinesData.cs</c>) each only ever see the deposit types they were registered for,
    /// even though all three use this same entity class.
    /// </summary>
    private List<IVirtualTerrainResource> FindAllRechargeableResources() {
        ImmutableArray<Proto.ID> allowedIds = ((InjectionPumpProto)Prototype).AllowedResourceIds;
        var found = new List<IVirtualTerrainResource>();
        foreach (IVirtualTerrainResource resource in m_virtualResourceManager.RetrieveAllResourcesAt(Position2f.Tile2i)) {
            if (allowedIds.Contains(allowedId => allowedId == resource.Product.Id)) {
                found.Add(resource);
            }
        }
        return found;
    }

    /// <summary>Fraction of capacity currently filled, used to pick the neediest resource for display.</summary>
    private static double fillRatio(IVirtualTerrainResource resource) {
        return resource.Capacity.Value == 0 ? 1.0 : (double)resource.Quantity.Value / resource.Capacity.Value;
    }

    public string Description => ModTranslation.Get("build-machine.InjectionPump.reserve-description", "Status of the underground reservoir this pump is recharging.");

    /// <summary>
    /// The interface declares a non-nullable return type. This can legitimately be null when no
    /// recognized deposit is present; callers should check <see cref="CapacityOfMine"/> or
    /// <see cref="QuantityLeftToMine"/> rather than relying on this value alone. When more than
    /// one recognized resource is present, reports whichever has the lowest fill percentage.
    /// </summary>
    public ProductProto ProductToMine {
        get {
            List<IVirtualTerrainResource> resources = FindAllRechargeableResources();
            if (resources.Count == 0) {
                return null!;
            }

            IVirtualTerrainResource neediest = resources[0];
            for (int i = 1; i < resources.Count; i++) {
                if (fillRatio(resources[i]) < fillRatio(neediest)) {
                    neediest = resources[i];
                }
            }
            return neediest.Product.Product;
        }
    }

    public Quantity CapacityOfMine {
        get {
            int total = 0;
            foreach (IVirtualTerrainResource resource in FindAllRechargeableResources()) {
                total += resource.Capacity.Value;
            }
            return new Quantity(total);
        }
    }

    public Quantity QuantityLeftToMine {
        get {
            int total = 0;
            foreach (IVirtualTerrainResource resource in FindAllRechargeableResources()) {
                total += resource.Quantity.Value;
            }
            return new Quantity(total);
        }
    }

    /// <summary>
    /// This reserve is being filled, not depleted, so a low-reserve notification does not apply
    /// the way it does for an extraction well.
    /// </summary>
    public bool NotifyOnLowReserve => false;

    protected override bool IsEnabledNow {
        get {
            if (!base.IsEnabledNow) {
                return false;
            }

            List<IVirtualTerrainResource> resources = FindAllRechargeableResources();
            if (resources.Count == 0) {
                return true;
            }

            foreach (IVirtualTerrainResource resource in resources) {
                if (resource.Quantity < resource.Capacity) {
                    return true;
                }
            }
            return false;
        }
    }
}

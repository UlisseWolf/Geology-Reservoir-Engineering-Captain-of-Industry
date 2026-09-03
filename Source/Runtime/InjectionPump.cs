using System;
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
using Mafi.Serialization;

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
/// This entity stores no instance field of its own at all - in particular, no reference to
/// <c>IVirtualResourceManager</c>. <see cref="FindAllRechargeableResources"/> reads
/// <see cref="GeologyReservoirEngineeringMod.VirtualResourceManager"/> instead, a `static`
/// field set once per game session in that mod's `Initialize` - a `static` field belongs to the
/// type, not to any individual entity instance, so it is never part of an entity's own
/// serialized state.
///
/// Having no extra fields is not, by itself, enough to make a sealed <c>Machine</c> subclass
/// saveable. The game's save system does not serialize every entity type through one fully
/// automatic path: concrete <c>Machine</c> subclasses each provide their own `public static
/// void Serialize(TSelf value, BlobWriter writer)` / `public new static TSelf Deserialize(BlobReader
/// reader)` pair, plus `SerializeData`/`DeserializeData` overrides - confirmed directly in the
/// vanilla `WellPump` class, which has this exact same boilerplate despite itself having very
/// few fields of its own. This is most likely produced by a source generator as part of the
/// base game's own build (triggered by a `[GenerateSerializer]` attribute mentioned in that
/// attribute's own documentation, requiring a `partial class` declaration to inject the
/// generated half) - not something available to an externally-compiled mod assembly, so this
/// class reproduces the same boilerplate by hand instead, calling only the base `Machine`
/// implementation in both data methods, since it has nothing of its own to add.
/// </summary>
public sealed class InjectionPump : Machine, IVirtualResourceMiningEntity {

    private static readonly Action<object, BlobWriter> s_serializeDataDelayedAction;
    private static readonly Action<object, BlobReader> s_deserializeDataDelayedAction;

    static InjectionPump() {
        s_serializeDataDelayedAction = (obj, writer) => ((InjectionPump)obj).SerializeData(writer);
        s_deserializeDataDelayedAction = (obj, reader) => ((InjectionPump)obj).DeserializeData(reader);
    }

    public static void Serialize(InjectionPump value, BlobWriter writer) {
        if (writer.TryStartClassSerialization(value)) {
            writer.EnqueueDataSerialization(value, s_serializeDataDelayedAction);
        }
    }

    public new static InjectionPump Deserialize(BlobReader reader) {
        if (reader.TryStartClassDeserialization(out InjectionPump obj)) {
            reader.EnqueueDataDeserialization(obj, s_deserializeDataDelayedAction);
        }
        return obj;
    }

    protected override void SerializeData(BlobWriter writer) {
        base.SerializeData(writer);
    }

    protected override void DeserializeData(BlobReader reader) {
        base.DeserializeData(reader);
    }

    public InjectionPump(
        EntityId id,
        InjectionPumpProto proto,
        TileTransform transform,
        EntityContext context,
        VirtualBuffersMap buffersMap,
        UnlockedProtosDb unlockedProtosDb,
        IVehicleBuffersRegistry vehicleBuffersRegistry,
        IEntityMaintenanceProvidersFactory maintenanceProvidersFactory,
        IAnimationStateFactory animationStateFactory)
        : base(id, proto, transform, context, buffersMap, unlockedProtosDb, vehicleBuffersRegistry, maintenanceProvidersFactory, animationStateFactory) {
    }

    /// <summary>
    /// Finds every deposit this instance is allowed to recharge/report on at its current
    /// position - restricted to <see cref="InjectionPumpProto.AllowedResourceIds"/> on this
    /// specific machine's prototype, so the water, oil, and natural gas injection pumps (see
    /// <c>MachinesData.cs</c>) each only ever see the deposit types they were registered for,
    /// even though all three use this same entity class. Returns an empty list if
    /// <see cref="GeologyReservoirEngineeringMod.VirtualResourceManager"/> hasn't been set yet
    /// (before the first <c>Initialize</c> call of a session), rather than throwing.
    /// </summary>
    private List<IVirtualTerrainResource> FindAllRechargeableResources() {
        var found = new List<IVirtualTerrainResource>();

        IVirtualResourceManager? virtualResourceManager = GeologyReservoirEngineeringMod.VirtualResourceManager;
        if (virtualResourceManager == null) {
            return found;
        }

        ImmutableArray<Proto.ID> allowedIds = ((InjectionPumpProto)Prototype).AllowedResourceIds;
        foreach (IVirtualTerrainResource resource in virtualResourceManager.RetrieveAllResourcesAt(Position2f.Tile2i)) {
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

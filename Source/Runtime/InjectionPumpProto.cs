using System;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Prototypes;

namespace GeologyReservoirEngineering.Runtime;

/// <summary>
/// A <see cref="MachineProto"/> subtype whose sole purpose is to point <see cref="EntityType"/>
/// at the custom <see cref="InjectionPump"/> class instead of the base <c>Machine</c> class.
/// This mirrors the pattern the base game itself uses for
/// <c>WellPumpProto</c> → <c>WellPump</c>.
///
/// <see cref="MachineProto.EntityType"/> is a virtual property that <see cref="MachineProto"/>
/// itself overrides and does not seal, so a further subclass can override it again.
/// <c>DefaultStaticEntityFactory</c> resolves <c>proto.EntityType</c> through dependency
/// injection at construction time; no Harmony patching is involved.
///
/// Instances are built directly (<c>new InjectionPumpProto(...)</c> followed by
/// <c>registrator.PrototypesDb.Add(...)</c>) rather than through <c>MachineProtoBuilder</c>,
/// which only ever produces plain <c>MachineProto</c>. This is the same direct-construction
/// pattern the base game uses for other one-off prototype subtypes.
///
/// <see cref="AllowedResourceIds"/> lets more than one machine share the
/// <see cref="InjectionPump"/> entity class while each recognizing a different, fixed set of
/// deposit types - the water injection pump (geothermal/groundwater), the oil injection pump
/// (crude oil), and the natural gas injection pump (Natural Gas only) all use this same
/// proto/entity pair, differing only in which resource IDs they're constructed with.
/// </summary>
public sealed class InjectionPumpProto : MachineProto {

    public override Type EntityType => typeof(InjectionPump);

    /// <summary>
    /// The set of <see cref="VirtualResourceProductProto"/> IDs this specific machine instance
    /// is allowed to recharge/report on, read by <see cref="InjectionPump.FindRechargeableResource"/>.
    /// A resource present at the pump's location but not in this list is treated as if nothing
    /// recognized were there at all.
    /// </summary>
    public ImmutableArray<Proto.ID> AllowedResourceIds { get; }

    public InjectionPumpProto(
        ID id,
        Str strings,
        EntityLayout layout,
        EntityCosts costs,
        Electricity consumedPowerPerTick,
        Computing computingConsumed,
        int? buffersMultiplier,
        bool useAllRecipesAtStartOrAfterUnlock,
        ImmutableArray<AnimationParams> animationParams,
        Gfx graphics,
        ImmutableArray<Proto.ID> allowedResourceIds)
        : base(id, strings, layout, costs, consumedPowerPerTick, computingConsumed, buffersMultiplier, useAllRecipesAtStartOrAfterUnlock, animationParams, graphics) {

        AllowedResourceIds = allowedResourceIds;
    }
}

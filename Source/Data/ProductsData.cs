using Mafi;
using Mafi.Base;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Registers the three geothermal deposit tiers as <see cref="VirtualResourceProductProto"/>,
/// following the same pattern the base game uses for VirtualCrudeOil and Groundwater
/// (<c>Mafi.Base.Prototypes.Machines.WellPumpsData</c>). Each tier is backed by an existing
/// vanilla steam product for extraction purposes, but has its own distinct name/description
/// rather than reusing that product's own vanilla strings - a deposit and the product it
/// eventually yields are conceptually different things, and sharing the exact same displayed
/// name made the three tiers hard to tell apart in resource lists (they all also inherited that
/// steam product's own near-white/grey UI color for the same reason, addressed below):
///
/// | Tier            | Backing product        |
/// |-----------------|------------------------|
/// | High enthalpy   | High-pressure steam    |
/// | Medium enthalpy | Low-pressure steam     |
/// | Low enthalpy    | Depleted steam         |
///
/// This mirrors real-world geothermal classification: high-enthalpy reservoirs are hot enough
/// to flash directly into high-pressure steam, medium-enthalpy reservoirs yield lower-pressure
/// steam, and low-enthalpy reservoirs only produce a depleted steam quality.
///
/// <c>VirtualResourceProductProto.Gfx.ResourcesVizColor</c> (the field this class sets via each
/// deposit's <c>Gfx</c> constructor argument) is a distinct color specific to the deposit
/// itself, not inherited from the backing steam product either. The three tiers use a red →
/// orange → gold gradient (hottest to coolest), clearly distinct from each other and from any
/// vanilla resource's own color, rather than three similar pale tones that were hard to
/// distinguish in the resource list/map overlay.
///
/// Because these are registered as standard <see cref="VirtualResourceProductProto"/> entries,
/// they are automatically available in the in-game map editor's resource-feature list, in the
/// same way as vanilla deposits.
/// </summary>
internal class ProductsData : IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        ProtosDb db = registrator.PrototypesDb;

        ProductProto steamHi = db.GetOrThrow<ProductProto>(Mafi.Base.Ids.Products.SteamHi);
        ProductProto steamLo = db.GetOrThrow<ProductProto>(Mafi.Base.Ids.Products.SteamLo);
        ProductProto steamDepleted = db.GetOrThrow<ProductProto>(Mafi.Base.Ids.Products.SteamDepleted);

        db.Add(new VirtualResourceProductProto(
            ModIds.VirtualResources.GeothermalHighEnthalpy,
            Proto.CreateStr(
                ModIds.VirtualResources.GeothermalHighEnthalpy,
                ModTranslation.Get("virtual-resource.GeothermalHighEnthalpy.name", "Geothermal reservoir (high enthalpy)"),
                ModTranslation.Get("virtual-resource.GeothermalHighEnthalpy.description", "A hot geothermal reservoir, hot enough to flash directly into high-pressure steam when tapped.")),
            steamHi,
            isResourceFinal: false,
            new VirtualResourceProductProto.Gfx(13379614, 6.0.TilesThick())));

        db.Add(new VirtualResourceProductProto(
            ModIds.VirtualResources.GeothermalMediumEnthalpy,
            Proto.CreateStr(
                ModIds.VirtualResources.GeothermalMediumEnthalpy,
                ModTranslation.Get("virtual-resource.GeothermalMediumEnthalpy.name", "Geothermal reservoir (medium enthalpy)"),
                ModTranslation.Get("virtual-resource.GeothermalMediumEnthalpy.description", "A moderately hot geothermal reservoir, yielding lower-pressure steam when tapped.")),
            steamLo,
            isResourceFinal: false,
            new VirtualResourceProductProto.Gfx(15104532, 8.0.TilesThick())));

        db.Add(new VirtualResourceProductProto(
            ModIds.VirtualResources.GeothermalLowEnthalpy,
            Proto.CreateStr(
                ModIds.VirtualResources.GeothermalLowEnthalpy,
                ModTranslation.Get("virtual-resource.GeothermalLowEnthalpy.name", "Geothermal reservoir (low enthalpy)"),
                ModTranslation.Get("virtual-resource.GeothermalLowEnthalpy.description", "A cooler geothermal reservoir, only yielding a depleted steam quality when tapped.")),
            steamDepleted,
            isResourceFinal: false,
            new VirtualResourceProductProto.Gfx(15121448, 10.0.TilesThick())));

        registerNaturalGas(registrator);
    }

    /// <summary>
    /// Registers raw Natural Gas as a new, storable fluid product (distinct from vanilla Fuel
    /// Gas - see <c>ModIds.Products.NaturalGas</c>), and a matching deposit type. The deposit is
    /// placed two ways, independent of each other: automatically available in the map editor
    /// (a consequence of being registered as a standard <see cref="VirtualResourceProductProto"/>,
    /// same as every other deposit in this mod), and co-located with existing crude oil deposits
    /// on the game's built-in maps via <see cref="NaturalGasMapPatch"/>, which reads the
    /// resource ID registered here.
    ///
    /// Uses a custom icon, a recolored variant of the vanilla Fuel Gas icon - same silhouette
    /// (canister + flame) for visual consistency with the base game, hue-shifted to blue to
    /// read as clearly distinct at a glance. The UI accent colors on the <c>Gfx</c> below are
    /// sampled directly from this icon so the product's color, tooltip, and storage bar match
    /// it exactly.
    ///
    /// <c>customIconPath</c> below (<c>Assets/Geothermal/NaturalGas.png</c>) must match, byte
    /// for byte, the asset path recorded inside the built AssetBundle's own manifest
    /// (<c>AssetBundles/geothermal_54ee.manifest</c>) - the engine resolves this path against
    /// the bundle's manifest at load time, not against any file on disk directly. The source
    /// PNG under <c>Assets/Geothermal/</c> in this repository is kept only for reference/future
    /// rebuilds; it is the already-built bundle in <c>AssetBundles/</c> that the game actually
    /// loads. See "Custom assets" in the README for the full asset pipeline.
    /// </summary>
    private static void registerNaturalGas(ProtoRegistrator registrator) {
        ProtosDb db = registrator.PrototypesDb;

        ProductProto naturalGas = db.Add(new FluidProductProto(
            ModIds.Products.NaturalGas,
            Proto.CreateStr(
                ModIds.Products.NaturalGas,
                ModTranslation.Get("product.NaturalGas.name", "Natural Gas"),
                ModTranslation.Get("product.NaturalGas.description", "Raw natural gas as extracted from the ground. Treat it in a Chemical Plant to obtain Fuel Gas.")),
            isStorable: true,
            canBeDiscarded: true,
            isWaste: false,
            graphics: new FluidProductProto.Gfx(
                prefabPath: Option<string>.None,
                customIconPath: "Assets/Geothermal/NaturalGas.png",
                color: new ColorRgba(64, 187, 229, 255),
                transportColor: new ColorRgba(50, 200, 235, 255),
                transportAccentColor: new ColorRgba(170, 230, 250, 255))));

        db.Add(new VirtualResourceProductProto(
            ModIds.VirtualResources.NaturalGas,
            naturalGas.Strings,
            naturalGas,
            isResourceFinal: true,
            new VirtualResourceProductProto.Gfx(4242405, 7.0.TilesThick())));
    }
}

using GeologyReservoirEngineering.Data;
using GeologyReservoirEngineering.Runtime;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Entities;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;

namespace GeologyReservoirEngineering;

/// <summary>
/// Mod entry point. Registers prototypes (products, machines, research) through
/// <see cref="ProtoRegistrator"/>, and wires up the runtime deposit-recharge service through
/// the standard <see cref="IMod"/> lifecycle. Harmony is used in four places: reassigning a
/// vanilla machine's toolbar category (reflection only, no method patch, see
/// <see cref="Data.VanillaCategoryFixupData"/>); adding Fuel Gas and Natural Gas as cargo ship
/// fuel options (reflection only, no method patch, see <see cref="Data.ShipFuelData"/>);
/// co-locating Natural Gas deposits with existing crude oil at map/feature generation time (two
/// genuine method patches, see <see cref="Data.NaturalGasMapPatch"/>); and retrofitting Natural
/// Gas into already-loaded saves whose map predates this mechanism (live-state reflection, also
/// in <see cref="Data.NaturalGasMapPatch"/>).
/// </summary>
public sealed class GeologyReservoirEngineeringMod : IMod {

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig => default;

    public ModManifest Manifest { get; private set; }

    public ModJsonConfig JsonConfig { get; }

    private GeologyRegenManager? m_regenManager;
    private readonly Harmony m_harmony = new("com.geology-reservoir-engineering.mod");

    public GeologyReservoirEngineeringMod(ModManifest manifest) {
        Manifest = manifest;
        JsonConfig = new(this);
        Log.Info($"{manifest.DisplayName} v{manifest.Version}");
    }

    public void RegisterPrototypes(ProtoRegistrator registrator) {
        Log.Info("[Geology & Reservoir Engineering] Registering prototypes");

        ModTranslation.Initialize(Manifest.RootDirectoryPath);

        registrator.RegisterData<ProductsData>();
        registrator.RegisterData<WorldMapData>();
        registrator.RegisterData<ToolbarCategoriesData>();
        registrator.RegisterData<VanillaCategoryFixupData>();
        registrator.RegisterData<MachinesData>();
        registrator.RegisterData<ResearchData>();

        // Registered after ResearchData: it looks up this mod's own "Natural gas extraction"
        // research node by ID to use as a ship-fuel unlock condition, which must already exist
        // in the database by the time this runs.
        registrator.RegisterData<ShipFuelData>();

        // NaturalGasMapPatch reads this static reference when the patched map-generation
        // method actually runs, later, when a map is loaded - registered here, after
        // ProductsData has added the proto to the database.
        NaturalGasMapPatch.NaturalGasProto = registrator.PrototypesDb.GetOrThrow<VirtualResourceProductProto>(ModIds.VirtualResources.NaturalGas);
        NaturalGasMapPatch.Apply(m_harmony);
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded) { }

    public void EarlyInit(DependencyResolver resolver) { }

    /// <summary>
    /// Called once per game session. Resolves engine services through dependency injection,
    /// starts <see cref="GeologyRegenManager"/> (which subscribes to the simulation loop), and
    /// retrofits Natural Gas deposits into an already-loaded world (new or restored from a
    /// save) via <see cref="NaturalGasMapPatch.RetrofitExistingSave"/>.
    /// </summary>
    public void Initialize(DependencyResolver resolver, bool gameWasLoaded) {
        IEntitiesManager entitiesManager = resolver.Resolve<IEntitiesManager>();
        IVirtualResourceManager virtualResourceManager = resolver.Resolve<IVirtualResourceManager>();
        ISimLoopEvents simLoopEvents = resolver.Resolve<ISimLoopEvents>();

        m_regenManager = new GeologyRegenManager(entitiesManager, virtualResourceManager, simLoopEvents);

        NaturalGasMapPatch.RetrofitExistingSave(virtualResourceManager);
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues) { }

    public void Dispose() {
        m_regenManager?.Dispose();
        m_regenManager = null;
    }
}

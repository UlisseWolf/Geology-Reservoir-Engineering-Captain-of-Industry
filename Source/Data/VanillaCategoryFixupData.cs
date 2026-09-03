using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi.Base;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Reassigns vanilla machines' toolbar categories so they sit alongside this mod's own
/// machines instead of only under their default vanilla category:
/// <list type="bullet">
/// <item>The Groundwater Pump moves from the top-level "Water" category into this mod's
/// "Groundwater" subcategory (see <see cref="ToolbarCategoriesData"/>).</item>
/// <item>The Oil Pump moves from its default "Basic" subcategory of "Crude oil refining" into
/// this mod's "Oil wells" subcategory, alongside the oil injection pump.</item>
/// <item>The Diesel Generator (both tiers) moves from its default "General" subcategory of
/// "Power production" into this mod's "Electric generators" subcategory, alongside this mod's
/// own Fuel Gas and Natural Gas generators.</item>
/// </list>
///
/// A machine's toolbar categories are stored in <c>LayoutEntityProto.Gfx.Categories</c>, a
/// get-only property assigned once when the prototype is constructed by the base game. There is
/// no supported API to change it afterward, so this class locates the property's backing field
/// by type using Harmony's <see cref="AccessTools"/> and overwrites it directly. This is
/// reflection only - it does not patch any method, unlike the genuine method patches in
/// <see cref="NaturalGasMapPatch"/>.
///
/// <see cref="reassignCategory"/> looks up the target prototype as <c>LayoutEntityProto</c>,
/// not <c>MachineProto</c>: the Groundwater Pump and Oil Pump are both true <c>MachineProto</c>
/// instances, but the Diesel Generator is <c>ElectricityGeneratorFromProductProto</c>, a
/// sibling type that shares <c>LayoutEntityProto</c> as a common ancestor rather than being a
/// <c>MachineProto</c> itself - looking it up as the narrower <c>MachineProto</c> type would
/// throw a cast exception. This mirrors the same distinction <c>ResearchData.cs</c> makes
/// between <c>AddMachineToUnlock</c> (requires a genuine <c>MachineProto</c>) and
/// <c>AddLayoutEntityToUnlock</c> (only requires the shared <c>LayoutEntityProto</c> ancestor)
/// when unlocking these same two generators.
///
/// Must run after <see cref="ToolbarCategoriesData"/>, since it depends on the "Groundwater",
/// "Oil wells", and "Electric generators" subcategories already being registered.
/// </summary>
internal class VanillaCategoryFixupData : IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        reassignCategory(registrator, Ids.Machines.LandWaterPump, ModIds.ToolbarCategories.Groundwater);
        reassignCategory(registrator, Ids.Machines.OilPump, ModIds.ToolbarCategories.OilWells);
        reassignCategory(registrator, Ids.Machines.DieselGenerator, ModIds.ToolbarCategories.ElectricGenerators);
        reassignCategory(registrator, Ids.Machines.DieselGeneratorT2, ModIds.ToolbarCategories.ElectricGenerators);
    }

    private static void reassignCategory(ProtoRegistrator registrator, MachineProto.ID machineId, params ToolbarCategoryProto.ID[] newCategoryIds) {
        LayoutEntityProto entity = registrator.PrototypesDb.GetOrThrow<LayoutEntityProto>(machineId);

        ImmutableArray<ToolbarEntryData> newCategories = registrator.GetCategoriesProtos(newCategoryIds);

        FieldInfo categoriesField = AccessTools.GetDeclaredFields(typeof(LayoutEntityProto.Gfx))
            .First(field => !field.IsStatic && field.FieldType == typeof(ImmutableArray<ToolbarEntryData>));

        categoriesField.SetValue(entity.Graphics, newCategories);
    }
}

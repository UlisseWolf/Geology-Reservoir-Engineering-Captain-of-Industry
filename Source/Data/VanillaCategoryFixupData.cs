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
/// </list>
///
/// A machine's toolbar categories are stored in <c>LayoutEntityProto.Gfx.Categories</c>, a
/// get-only property assigned once when the prototype is constructed by the base game. There is
/// no supported API to change it afterward, so this class locates the property's backing field
/// by type using Harmony's <see cref="AccessTools"/> and overwrites it directly. This is
/// reflection only - it does not patch any method, unlike the genuine method patches in
/// <see cref="NaturalGasMapPatch"/>.
///
/// Must run after <see cref="ToolbarCategoriesData"/>, since it depends on the "Groundwater" and
/// "Oil wells" subcategories already being registered.
/// </summary>
internal class VanillaCategoryFixupData : IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        reassignCategory(registrator, Ids.Machines.LandWaterPump, ModIds.ToolbarCategories.Groundwater);
        reassignCategory(registrator, Ids.Machines.OilPump, ModIds.ToolbarCategories.OilWells);
    }

    private static void reassignCategory(ProtoRegistrator registrator, MachineProto.ID machineId, params ToolbarCategoryProto.ID[] newCategoryIds) {
        MachineProto machine = registrator.PrototypesDb.GetOrThrow<MachineProto>(machineId);

        ImmutableArray<ToolbarEntryData> newCategories = registrator.GetCategoriesProtos(newCategoryIds);

        FieldInfo categoriesField = AccessTools.GetDeclaredFields(typeof(LayoutEntityProto.Gfx))
            .First(field => !field.IsStatic && field.FieldType == typeof(ImmutableArray<ToolbarEntryData>));

        categoriesField.SetValue(machine.Graphics, newCategories);
    }
}

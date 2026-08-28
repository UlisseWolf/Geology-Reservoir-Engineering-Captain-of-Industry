using Mafi.Base;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Registers three toolbar subcategories:
/// <list type="bullet">
/// <item>"Geothermal", under the vanilla "Power production" menu, alongside the vanilla
/// "General" and "Nuclear" subcategories.</item>
/// <item>"Groundwater", under the vanilla "Water" menu.</item>
/// <item>"Oil wells", under the vanilla "Crude oil refining" menu, alongside the vanilla
/// "Basic" subcategory the vanilla Oil Pump defaults to.</item>
/// </list>
/// Must run before <see cref="MachinesData"/>, which assigns machines to these categories, and
/// before <see cref="VanillaCategoryFixupData"/>, which reassigns vanilla machines into them.
/// </summary>
internal class ToolbarCategoriesData : IModData {

    public void RegisterData(ProtoRegistrator registrator) {
        ToolbarCategoryProto powerCategory = registrator.PrototypesDb.GetOrThrow<ToolbarCategoryProto>(Ids.ToolbarCategories.Power);
        ToolbarCategoryProto waterCategory = registrator.PrototypesDb.GetOrThrow<ToolbarCategoryProto>(Ids.ToolbarCategories.Water);
        ToolbarCategoryProto oilCategory = registrator.PrototypesDb.GetOrThrow<ToolbarCategoryProto>(Ids.ToolbarCategories.Oil);

        registrator.PrototypesDb.Add(new ToolbarCategoryProto(
            ModIds.ToolbarCategories.Geothermal,
            Proto.CreateStr(ModIds.ToolbarCategories.Geothermal, ModTranslation.Get("toolbar-category.Geothermal.name", "Geothermal"), null),
            order: 2f,
            iconPath: "Assets/Unity/UserInterface/Toolbar/Power.svg",
            isTransportBuildAllowed: false,
            parentCategory: powerCategory));

        registrator.PrototypesDb.Add(new ToolbarCategoryProto(
            ModIds.ToolbarCategories.Groundwater,
            Proto.CreateStr(ModIds.ToolbarCategories.Groundwater, ModTranslation.Get("toolbar-category.Groundwater.name", "Groundwater"), null),
            order: 1f,
            iconPath: "Assets/Unity/UserInterface/Toolbar/WaterMachines.svg",
            isTransportBuildAllowed: false,
            parentCategory: waterCategory));

        registrator.PrototypesDb.Add(new ToolbarCategoryProto(
            ModIds.ToolbarCategories.OilWells,
            Proto.CreateStr(ModIds.ToolbarCategories.OilWells, ModTranslation.Get("toolbar-category.OilWells.name", "Oil wells"), null),
            order: 1f,
            iconPath: "Assets/Unity/UserInterface/Toolbar/Oil.svg",
            isTransportBuildAllowed: true,
            parentCategory: oilCategory));
    }
}

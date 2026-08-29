using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Base;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Buildings.Cargo.Ships;
using Mafi.Core.Economy;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;

namespace GeologyReservoirEngineering.Data;

/// <summary>
/// Adds vanilla Fuel Gas and this mod's Natural Gas as alternative cargo ship fuels, alongside
/// the vanilla options (Diesel, Heavy Oil, Hydrogen).
///
/// <c>CargoShipProto.AvailableFuels</c> is a public, but <c>readonly</c>, field set once by the
/// base game when it constructs its cargo ship prototypes
/// (<c>Mafi.Base.Prototypes.Cargo.CargoShipsData</c>), before this mod runs. There is no
/// supported API to add a fuel option afterward, so this class locates the field by name using
/// Harmony's <see cref="AccessTools"/> and overwrites it with the existing array plus two new
/// entries appended - the same reflection-only technique (no method patch)
/// <see cref="VanillaCategoryFixupData"/> uses for toolbar categories.
///
/// Applies to every registered <c>CargoShipProto</c> (<c>ProtosDb.All&lt;CargoShipProto&gt;()</c>),
/// not just the four vanilla tiers by ID, so it also covers any additional cargo ship tier
/// another mod might register.
///
/// Consumption rates for the two new fuels are derived from each ship's own existing Diesel
/// entry rather than a fixed constant, since the vanilla per-tier fuel consumption values aren't
/// exposed publicly: Fuel Gas matches Diesel's rate one-to-one (a fairly standard combustion
/// fuel), while Natural Gas consumes 30% more (raw, untreated gas burning less efficiently than
/// a refined fuel). Pollution is set relative to Diesel's own 100% in the same direction as
/// consumption: Fuel Gas, already refined and clean-burning, pollutes less (65%); Natural Gas,
/// burned raw and untreated, pollutes more (140%) - consistent with this mod's own gas-fired
/// Boiler recipe, where Natural Gas combustion produces Exhaust rather than the cleaner CO2
/// byproduct Fuel Gas combustion produces elsewhere in the base game. A ship without a Diesel
/// entry at all (unexpected, but not impossible for a third-party mod's own ship) is skipped
/// rather than guessed at.
///
/// Fuel Gas and Natural Gas are each other's only compatible fuel (interchangeable without
/// switching cost, as Diesel/Heavy Oil already are with each other) - not compatible with
/// Diesel/Heavy Oil/Hydrogen, representing a distinct gas-burning engine mode rather than a
/// drop-in replacement for liquid fuels. <c>FuelData.LockingProto</c> accepts any
/// <c>Proto</c>, not just a product - the vanilla Hydrogen entry itself locks behind a
/// <c>TechnologyProto</c> rather than the Hydrogen product. Following that pattern, each new
/// entry here is locked behind an existing research node rather than its own product: Fuel Gas
/// behind the vanilla "Gas combustion" node (<c>Ids.Research.GasCombustion</c> - the node that
/// already unlocks the gas-fired Boiler and Fuel Gas steam generation, representing the point at
/// which the game considers Fuel Gas combustion a mastered technology), and Natural Gas behind
/// this mod's own "Natural gas extraction" node (<c>ModIds.ResearchNodes.NaturalGasExtraction</c>).
/// No new, dedicated research node is introduced for either. Neither fuel option introduces a
/// new engine cost or ship model - both reuse the existing Diesel entry's construction cost and
/// the ship's default graphics, since no dedicated 3D assets exist for a gas-fueled variant.
/// </summary>
internal class ShipFuelData : IModData {

    /// <summary>Natural Gas consumes this much more than Fuel Gas per journey, for the same ship.</summary>
    private static readonly Percent NATURAL_GAS_CONSUMPTION_MULTIPLIER = 130.Percent();

    public void RegisterData(ProtoRegistrator registrator) {
        ProtosDb db = registrator.PrototypesDb;

        ProductProto fuelGas = db.GetOrThrow<ProductProto>(Ids.Products.FuelGas);
        ProductProto naturalGas = db.GetOrThrow<ProductProto>(ModIds.Products.NaturalGas);
        ResearchNodeProto gasCombustion = db.GetOrThrow<ResearchNodeProto>(Ids.Research.GasCombustion);
        ResearchNodeProto naturalGasExtraction = db.GetOrThrow<ResearchNodeProto>(ModIds.ResearchNodes.NaturalGasExtraction);

        FieldInfo availableFuelsField = AccessTools.Field(typeof(CargoShipProto), "AvailableFuels");

        foreach (CargoShipProto ship in db.All<CargoShipProto>()) {
            CargoShipProto.FuelData? dieselEntry = null;
            foreach (CargoShipProto.FuelData entry in ship.AvailableFuels) {
                if (entry.FuelProto.Id == Ids.Products.Diesel) {
                    dieselEntry = entry;
                    break;
                }
            }
            if (dieselEntry == null) {
                continue;
            }

            var fuelGasEntry = new CargoShipProto.FuelData(
                fuelGas,
                dieselEntry.FuelPerJourneyBase,
                dieselEntry.FuelPerJourneyPerModule,
                gasCombustion,
                ImmutableArray.Create(naturalGas),
                dieselEntry.Cost,
                65.Percent(),
                Option<CargoShipProto.Gfx>.None);

            var naturalGasEntry = new CargoShipProto.FuelData(
                naturalGas,
                dieselEntry.FuelPerJourneyBase.ScaledBy(NATURAL_GAS_CONSUMPTION_MULTIPLIER),
                dieselEntry.FuelPerJourneyPerModule.ScaledBy(NATURAL_GAS_CONSUMPTION_MULTIPLIER),
                naturalGasExtraction,
                ImmutableArray.Create(fuelGas),
                dieselEntry.Cost,
                140.Percent(),
                Option<CargoShipProto.Gfx>.None);

            ImmutableArray<CargoShipProto.FuelData> updatedFuels = ship.AvailableFuels.AddRange(new List<CargoShipProto.FuelData> { fuelGasEntry, naturalGasEntry });
            availableFuelsField.SetValue(ship, updatedFuels);
        }
    }
}

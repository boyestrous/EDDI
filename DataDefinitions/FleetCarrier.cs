﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Utilities;

namespace EddiDataDefinitions
{
    public class FleetCarrier : INotifyPropertyChanged
    {
        private long? _carrierId;
        private FrontierApiStation _market = new FrontierApiStation();
        private string _name;
        private string _callsign;
        private string _currentStarSystem;
        private string _nextStarSystem;
        private int _fuel;
        private int _fuelInCargo;
        private string _state;
        private string _dockingAccess;
        private bool _notoriousAccess;
        private int _usedCapacity;
        private int _freeCapacity;
        private ulong _bankBalance;
        private ulong _bankReservedBalance;
        private ulong _bankPurchaseAllocationsBalance;
        private JArray _cargo = new JArray();
        private JArray _carrierLockerAssets = new JArray();
        private JArray _carrierLockerGoods = new JArray();
        private JArray _carrierLockerData = new JArray();
        private JArray _commoditySalesOrders = new JArray();
        private JArray _commodityPurchaseOrders = new JArray();
        private JArray _microresourceSalesOrders = new JArray();
        private JArray _microresourcePurchaseOrders = new JArray();

        public long? carrierID
        {
            get => _carrierId;
            set
            {
                if (value == _carrierId) return;
                _carrierId = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The name of the carrier (requires Frontier API access or a 'Carrier Stats' event)")]
        public string name
        {
            get => _name;
            set
            {
                if (value == _name) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The callsign (alphanumeric designation) of the carrier (requires Frontier API access or a 'Carrier Stats' event)")]
        public string callsign
        {
            get => _callsign;
            set
            {
                if (value == _callsign) return;
                _callsign = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The current location (star system) of the carrier")]
        public string currentStarSystem
        {
            get => _currentStarSystem;
            set
            {
                if (value == _currentStarSystem) return;
                _currentStarSystem = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The next scheduled location (star system) of the carrier, if any")]
        public string nextStarSystem
        {
            get => _nextStarSystem;
            set
            {
                if (value == _nextStarSystem) return;
                _nextStarSystem = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The last reported tritium fuel level of the carrier")]
        public int fuel // Tritium Fuel Reserves
        {
            get => _fuel;
            set
            {
                if (value == _fuel) return;
                _fuel = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The last reported amount of stored tritium held in the carrier's cargo (requires Frontier API access)")]
        public int fuelInCargo // Tritium Fuel carried as cargo
        {
            get => _fuelInCargo;
            set
            {
                if (value == _fuelInCargo) return;
                _fuelInCargo = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The carrier's current operating state (requires Frontier API access) (one of 'normalOperation', 'debtState' (if services are offline due to lack of funds), or 'pendingDecommission')")]
        public string state // one of "normalOperation", "debtState" (if services are offline due to lack of funds), or "pendingDecommission" 
        {
            get => _state;
            set
            {
                if (value == _state) return;
                _state = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The carrier's last reported docking access (one of one of 'all', 'squadronfriends', 'friends', or 'none')")]
        public string dockingAccess // one of "all", "squadronfriends", "friends", or "none".
                                    // Value is reported by the `Carrier stats` event
        {
            get => _dockingAccess;
            set
            {
                if (value == _dockingAccess) return;
                _dockingAccess = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("True if the last reported state permits docking access by notorious commanders")]
        public bool notoriousAccess // Value is reported by the `Carrier stats` event
        {
            get => _notoriousAccess;
            set
            {
                if (value == _notoriousAccess) return;
                _notoriousAccess = value;
                OnPropertyChanged();
            }
        }

        // Capacity

        [PublicAPI("The last reported total used capacity of the carrier")]
        public int usedCapacity // Value is reported by the `Carrier stats` event
        {
            get => _usedCapacity;
            set
            {
                if (value == _usedCapacity) return;
                _usedCapacity = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The last reported free capacity of the carrier")]
        public int freeCapacity // Value is reported by the `Carrier stats` event
        {
            get => _freeCapacity;
            set
            {
                if (value == _freeCapacity) return;
                _freeCapacity = value;
                OnPropertyChanged();
            }
        }

        // Finances

        [PublicAPI("The last reported bank balance of the carrier")]
        public ulong bankBalance // Value is reported by the `Carrier stats` event
        {
            get => _bankBalance;
            set
            {
                if (value == _bankBalance) return;
                _bankBalance = value;
                OnPropertyChanged();
            }
        }

        [PublicAPI("The last reported available bank balance of the carrier")]
        public ulong bankAvailableBalance => bankBalance - bankReservedBalance - bankPurchaseAllocationsBalance;

        [PublicAPI("The last reported reserved bank balance of the carrier")]
        public ulong bankReservedBalance // Value is reported by the `Carrier stats` event
        {
            get => _bankReservedBalance;
            set
            {
                if (value == _bankReservedBalance) return;
                _bankReservedBalance = value;
                OnPropertyChanged();
            }
        }

        public ulong bankPurchaseAllocationsBalance
        {
            get => _bankPurchaseAllocationsBalance;
            set
            {
                if (value == _bankPurchaseAllocationsBalance) return;
                _bankPurchaseAllocationsBalance = value;
                OnPropertyChanged();
            }
        }

        // Inventories

        public JArray Cargo // Current cargo inventory
        {
            get => _cargo;
            set
            {
                if (Equals(value, _cargo)) return;
                _cargo = value;
                OnPropertyChanged();
            }
        }

        public JArray CarrierLockerAssets // Current MicroResource Inventory of Assets
        {
            get => _carrierLockerAssets;
            set
            {
                if (Equals(value, _carrierLockerAssets)) return;
                _carrierLockerAssets = value;
                OnPropertyChanged();
            }
        }

        public JArray CarrierLockerGoods // Current MicroResource Inventory of Goods
        {
            get => _carrierLockerGoods;
            set
            {
                if (Equals(value, _carrierLockerGoods)) return;
                _carrierLockerGoods = value;
                OnPropertyChanged();
            }
        }

        public JArray CarrierLockerData // Current MicroResource Inventory of Data
        {
            get => _carrierLockerData;
            set
            {
                if (Equals(value, _carrierLockerData)) return;
                _carrierLockerData = value;
                OnPropertyChanged();
            }
        }

        // Market Buy/Sell Orders

        public JArray commoditySalesOrders
        {
            get => _commoditySalesOrders;
            set
            {
                if (Equals(value, _commoditySalesOrders)) return;
                _commoditySalesOrders = value;
                OnPropertyChanged();
            }
        }

        public JArray commodityPurchaseOrders
        {
            get => _commodityPurchaseOrders;
            set
            {
                if (Equals(value, _commodityPurchaseOrders)) return;
                _commodityPurchaseOrders = value;
                OnPropertyChanged();
            }
        }

        public JArray microresourceSalesOrders
        {
            get => _microresourceSalesOrders;
            set
            {
                if (Equals(value, _microresourceSalesOrders)) return;
                _microresourceSalesOrders = value;
                OnPropertyChanged();
            }
        }

        public JArray microresourcePurchaseOrders
        {
            get => _microresourcePurchaseOrders;
            set
            {
                if (Equals(value, _microresourcePurchaseOrders)) return;
                _microresourcePurchaseOrders = value;
                OnPropertyChanged();
            }
        }

        // Station properties

        public FrontierApiStation Market
        {
            get => _market;
            set
            {
                if (_market == value) return;
                _market = value;
                OnPropertyChanged();
            }
        }

        // Administrative Metadata

        public JObject json { get; set; } // The raw data from the endpoint as a JObject

        public DateTime timestamp { get; set; } // When the raw data was obtained

        // Constructors

        [JsonConstructor]
        public FleetCarrier(long? carrierID)
        {
            this.carrierID = carrierID;
        }

        public FleetCarrier (JObject newJson, DateTime newTimeStamp)
        {
            json = newJson;
            timestamp = newTimeStamp;
        }

        // Methods

        public FleetCarrier UpdateFrom(JObject newJson, DateTime newTimeStamp)
        {
            var newFleetCarrier = new FleetCarrier(newJson, newTimeStamp);

            try
            {
                // Name must be converted from a hexadecimal to a string
                string ConvertHexString(string hexString)
                {
                    string ascii = string.Empty;
                    for (int i = 0; i < hexString.Length; i += 2)
                    {
                        var hs = hexString.Substring(i, 2);
                        var decval = Convert.ToUInt32(hs, 16);
                        var character = Convert.ToChar(decval);
                        ascii += character;
                    }

                    return ascii;
                }

                newFleetCarrier.carrierID = newJson["market"]?["id"]?.ToObject<long?>();
                newFleetCarrier.callsign = newJson["name"]?["callsign"]?.ToString();
                newFleetCarrier.name = ConvertHexString(newJson["name"]["vanityName"]?.ToString());
                newFleetCarrier.currentStarSystem = newJson["currentStarSystem"]?.ToString();
                newFleetCarrier.fuel = int.Parse(newJson["fuel"]?.ToString() ?? string.Empty);
                newFleetCarrier.state = newJson["state"]?.ToString();
                newFleetCarrier.dockingAccess = newJson["dockingAccess"]?.ToString();
                notoriousAccess = newJson["notoriousAccess"]?.ToObject<bool>() ?? false;

                // Capacity
                var shipPacks = newJson["capacity"]?["shipPacks"]?.ToObject<int>() ?? 0;
                var modulePacks = newJson["capacity"]?["modulePacks"]?.ToObject<int>() ?? 0;
                var cargoForSale = newJson["capacity"]?["cargoForSale"]?.ToObject<int>() ?? 0;
                var cargoNotForSale = newJson["capacity"]?["cargoNotForSale"]?.ToObject<int>() ?? 0;
                var reservedSpace = newJson["capacity"]?["cargoSpaceReserved"]?.ToObject<int>() ?? 0;
                var crew = newJson["capacity"]?["crew"]?.ToObject<int>() ?? 0;
                newFleetCarrier.usedCapacity =
                    shipPacks +
                    modulePacks +
                    cargoForSale +
                    cargoNotForSale +
                    reservedSpace +
                    crew;
                newFleetCarrier.freeCapacity = newJson["capacity"]?["freeSpace"]?.ToObject<int>() ?? 0;

                // Itinerary
                newFleetCarrier.nextStarSystem = newJson["itinerary"]?["currentJump"]?.ToString();

                // Finances
                newFleetCarrier.bankBalance = newJson["finance"]?["bankBalance"]?.ToObject<ulong>() ?? 0;
                newFleetCarrier.bankReservedBalance =
                    newJson["finance"]?["bankReservedBalance"]?.ToObject<ulong>() ?? 0;
                newFleetCarrier.bankPurchaseAllocationsBalance =
                    newJson["marketFinances"]?["balanceAllocForPurchaseOrders"]?.ToObject<ulong>() ?? 0
                    + newJson["blackmarketFinances"]?["balanceAllocForPurchaseOrders"]?.ToObject<ulong>() ?? 0
                    + newJson["finance"]?["bartender"]?["balanceAllocForPurchaseOrders"]?.ToObject<ulong>() ?? 0;

                // Inventories
                newFleetCarrier.Cargo = JArray.FromObject(newJson["cargo"] ?? new JArray());
                newFleetCarrier.CarrierLockerAssets =
                    JArray.FromObject(newJson["carrierLocker"]?["assets"] ?? new JArray());
                newFleetCarrier.CarrierLockerGoods =
                    JArray.FromObject(newJson["carrierLocker"]?["goods"] ?? new JArray());
                newFleetCarrier.CarrierLockerData =
                    JArray.FromObject(newJson["carrierLocker"]?["data"] ?? new JArray());

                // Market Buy/Sell Orders
                newFleetCarrier.commodityPurchaseOrders =
                    JArray.FromObject(newJson["orders"]?["commodities"]?["purchases"] ?? new JArray());
                newFleetCarrier.commoditySalesOrders =
                    JArray.FromObject(newJson["orders"]?["commodities"]?["sales"] ?? new JArray());
                newFleetCarrier.microresourcePurchaseOrders = JArray.FromObject(
                    newJson["orders"]?["onfootmicroresources"]?["purchases"]?.Values() ?? new JEnumerable<JToken>());
                newFleetCarrier.microresourceSalesOrders = JArray.FromObject(
                    newJson["orders"]?["onfootmicroresources"]?["sales"]?.Values() ?? new JEnumerable<JToken>());

                // Station properties
                newFleetCarrier.Market = FrontierApiStation.FromJson(newJson["market"]?.ToObject<JObject>(), null);
                newFleetCarrier.Market.commoditiesupdatedat = newTimeStamp;
                newFleetCarrier.Market.outfittingupdatedat = newTimeStamp;
                newFleetCarrier.Market.shipyardupdatedat = newTimeStamp;

                // Misc - Tritium stored in cargo
                foreach (var cargo in Cargo)
                {
                    if (cargo["commodity"]?.ToString() is "Tritium")
                    {
                        newFleetCarrier.fuelInCargo += cargo["qty"]?.ToObject<int>() ?? 0;
                    }
                }

            }
            catch (Exception e)
            {
                e.Data.Add("fleetCarrierJson", newJson);
                Logging.Error("Fleet carrier parsing error", e);
                return null;
            }

            return UpdateFrom(newFleetCarrier);
        }

        public FleetCarrier UpdateFrom(FleetCarrier newFleetCarrier)
        {
            if (newFleetCarrier is null) { return this; } // Null data
            if (json.DeepEquals(newFleetCarrier.json)) { return this; } // No changes
            if (!CarrierMatches(newFleetCarrier)) { return this; } // Not the same carrier

            var fleetCarrier = new FleetCarrier(newFleetCarrier.Market.marketId)
            {
                carrierID = newFleetCarrier.Market.marketId,
                callsign = newFleetCarrier.callsign,

                // Information exclusively available from the Frontier API
                Cargo = newFleetCarrier.Cargo,
                CarrierLockerAssets = newFleetCarrier.CarrierLockerAssets,
                CarrierLockerGoods = newFleetCarrier.CarrierLockerGoods,
                CarrierLockerData = newFleetCarrier.CarrierLockerData,
                commodityPurchaseOrders = newFleetCarrier.commodityPurchaseOrders,
                commoditySalesOrders = newFleetCarrier.commoditySalesOrders,
                microresourcePurchaseOrders = newFleetCarrier.microresourcePurchaseOrders,
                microresourceSalesOrders = newFleetCarrier.microresourceSalesOrders,
                Market = newFleetCarrier.Market
            };

            // Information which might be newer, check timestamp prior to updating
            if (newFleetCarrier.timestamp > timestamp)
            {
                fleetCarrier.name = newFleetCarrier.name;
                fleetCarrier.currentStarSystem = newFleetCarrier.currentStarSystem;
                fleetCarrier.nextStarSystem = newFleetCarrier.nextStarSystem;
                fleetCarrier.dockingAccess = newFleetCarrier.dockingAccess;
                fleetCarrier.notoriousAccess = newFleetCarrier.notoriousAccess;
                fleetCarrier.fuel = newFleetCarrier.fuel;
                fleetCarrier.fuelInCargo = newFleetCarrier.fuelInCargo;
                fleetCarrier.state = newFleetCarrier.state;
                fleetCarrier.bankBalance = newFleetCarrier.bankBalance;
                fleetCarrier.bankReservedBalance = newFleetCarrier.bankReservedBalance;
                fleetCarrier.bankPurchaseAllocationsBalance = newFleetCarrier.bankPurchaseAllocationsBalance;
                fleetCarrier.usedCapacity = newFleetCarrier.usedCapacity;
                fleetCarrier.freeCapacity = newFleetCarrier.freeCapacity;
            };

            return fleetCarrier;
        }

        public bool CarrierMatches(FleetCarrier newFleetCarrier)
        {
            // Verify that the profile information matches the current fleet carrier callsign
            if (callsign != null && newFleetCarrier.callsign != callsign)
            {
                Logging.Warn("Frontier API incorrectly configured: Returning information for Fleet Carrier " +
                             newFleetCarrier.callsign + " rather than for " + callsign + ". Disregarding incorrect information.");
                return false;
            }
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}

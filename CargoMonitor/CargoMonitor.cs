﻿using Eddi;
using EddiConfigService;
using EddiCore;
using EddiDataDefinitions;
using EddiEvents;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Utilities;

namespace EddiCargoMonitor
{
    /// Monitor cargo for the current ship
    public class CargoMonitor : EDDIMonitor
    {
        // Observable collection for us to handle changes
        public ObservableCollection<Cargo> inventory { get; private set; }
        public int cargoCarried;
        private bool checkHaulage = false;
        private DateTime updateDat;

        private static readonly object inventoryLock = new object();
        public event EventHandler InventoryUpdatedEvent;

        private static Dictionary<string, string> CHAINED = new Dictionary<string, string>()
        {
            {"clearingthepath", "delivery"},
            {"helpfinishtheorder", "delivery"},
            {"rescuefromthetwins", "salvage"},
            {"rescuethewares", "salvage"}
        };

        public string MonitorName()
        {
            return "Cargo monitor";
        }

        public string LocalizedMonitorName()
        {
            return Properties.CargoMonitor.cargo_monitor_name;
        }

        public string MonitorDescription()
        {
            return Properties.CargoMonitor.cargo_monitor_desc;
        }

        public bool IsRequired()
        {
            return true;
        }

        /// <summary>
        /// Create a new CargoMonitor, reading the configuration from the default location on the file system.
        /// This is required for the DLL to load
        /// </summary>
        [UsedImplicitly]
        public CargoMonitor() : this(null)
        {}

        /// <summary>
        /// Create a new CargoMonitor, optionally passing in a non-default configuration
        /// </summary>
        /// <param name="configuration">The configuration to use. If null, it will be read from the file system</param>
        public CargoMonitor(CargoMonitorConfiguration configuration)
        {
            inventory = new ObservableCollection<Cargo>();
            BindingOperations.CollectionRegistering += Inventory_CollectionRegistering;
            initializeCargoMonitor(configuration);
        }

        private void initializeCargoMonitor(CargoMonitorConfiguration configuration = null)
        {
            readInventory(configuration);
            Logging.Info($"Initialized {MonitorName()}");
        }

        private void Inventory_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
        {
            if (Application.Current != null)
            {
                // Synchronize this collection between threads
                BindingOperations.EnableCollectionSynchronization(inventory, inventoryLock);
            }
            else
            {
                // If started from VoiceAttack, the dispatcher is on a different thread. Invoke synchronization there.
                Dispatcher.CurrentDispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(inventory, inventoryLock); });
            }
        }
        public bool NeedsStart()
        {
            // We don't actively do anything, just listen to events
            return false;
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Reload()
        {
            readInventory();
            Logging.Info($"Reloaded {MonitorName()}");
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        public void EnableConfigBinding(MainWindow configWindow)
        {
            configWindow.Dispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(inventory, inventoryLock); });
        }

        public void DisableConfigBinding(MainWindow configWindow)
        {
            configWindow.Dispatcher.Invoke(() => { BindingOperations.DisableCollectionSynchronization(inventory); });
        }

        public void HandleProfile(JObject profile)
        {
        }

        public void PostHandle(Event @event)
        {
        }

        public void PreHandle(Event @event)
        {
            Logging.Debug("Received event " + JsonConvert.SerializeObject(@event));

            // Handle the events that we care about
            if (@event is CargoEvent)
            {
                handleCargoEvent((CargoEvent)@event);
            }
            else if (@event is CommodityCollectedEvent)
            {
                handleCommodityCollectedEvent((CommodityCollectedEvent)@event);
            }
            else if (@event is CommodityEjectedEvent)
            {
                handleCommodityEjectedEvent((CommodityEjectedEvent)@event);
            }
            else if (@event is CommodityPurchasedEvent)
            {
                handleCommodityPurchasedEvent((CommodityPurchasedEvent)@event);
            }
            else if (@event is CommodityRefinedEvent)
            {
                handleCommodityRefinedEvent((CommodityRefinedEvent)@event);
            }
            else if (@event is CommoditySoldEvent)
            {
                handleCommoditySoldEvent((CommoditySoldEvent)@event);
            }
            else if (@event is CargoDepotEvent)
            {
                // If cargo is collected or delivered in a wing mission
                handleCargoDepotEvent((CargoDepotEvent)@event);
            }
            else if (@event is LimpetPurchasedEvent)
            {
                handleLimpetPurchasedEvent((LimpetPurchasedEvent)@event);
            }
            else if (@event is MissionsEvent)
            {
                // Remove cargo haulage stragglers for completed missions
                handleMissionsEvent((MissionsEvent)@event);
            }
            else if (@event is MissionAbandonedEvent)
            {
                // If we abandon a mission with cargo it becomes stolen
                handleMissionAbandonedEvent((MissionAbandonedEvent)@event);
            }
            else if (@event is MissionAcceptedEvent)
            {
                // Check to see if this is a cargo mission and update our inventory accordingly
                handleMissionAcceptedEvent((MissionAcceptedEvent)@event);
            }
            else if (@event is MissionCompletedEvent)
            {
                // Check to see if this is a cargo mission and update our inventory accordingly
                handleMissionCompletedEvent((MissionCompletedEvent)@event);
            }
            else if (@event is MissionExpiredEvent)
            {
                // Check to see if this is a cargo mission and update our inventory accordingly
                handleMissionExpiredEvent((MissionExpiredEvent)@event);
            }
            else if (@event is MissionFailedEvent)
            {
                // If we fail a mission with cargo it becomes stolen
                handleMissionFailedEvent((MissionFailedEvent)@event);
            }
            else if (@event is DiedEvent)
            {
                handleDiedEvent((DiedEvent)@event);
            }
            else if (@event is EngineerContributedEvent)
            {
                handleEngineerContributedEvent((EngineerContributedEvent)@event);
            }
            else if (@event is SynthesisedEvent)
            {
                handleSynthesisedEvent((SynthesisedEvent)@event);
            }
        }

        private void handleCargoEvent(CargoEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleCargoEvent(@event);
                writeInventory();
            }
        }

        private void _handleCargoEvent(CargoEvent @event)
        {
            if (@event.vessel == Constants.VEHICLE_SHIP)
            {
                cargoCarried = @event.cargocarried;
                if (@event.inventory != null)
                {
                    List<EddiDataDefinitions.CargoInfoItem> infoList = @event.inventory.ToList();

                    // Remove strays from the manifest
                    foreach (Cargo inventoryCargo in inventory.ToList())
                    {
                        string name = inventoryCargo.edname;
                        EddiDataDefinitions.CargoInfoItem infoItem = @event.inventory.FirstOrDefault(i => i.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (infoItem == null)
                        {
                            if (inventoryCargo.haulageData?.Any() ?? false)
                            {
                                // Keep cargo entry in manifest with zeroed amounts, if missions are pending
                                inventoryCargo.haulage = 0;
                                inventoryCargo.owned = 0;
                                inventoryCargo.stolen = 0;
                                inventoryCargo.CalculateNeed();
                            }
                            else
                            {
                                // Strip out the stray from the manifest
                                _RemoveCargoWithEDName(inventoryCargo.edname);
                            }
                        }
                    }

                    // Update existing cargo in the manifest
                    while (infoList.Any())
                    {
                        string name = infoList.ToList().First().name;
                        List<EddiDataDefinitions.CargoInfoItem> cargoInfo = infoList.Where(i => i.name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                        Cargo cargo = inventory.FirstOrDefault(c => c.edname.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (cargo != null)
                        {
                            int total = cargoInfo.Sum(i => i.count);
                            int stolen = cargoInfo.Where(i => i.missionid == null).Sum(i => i.stolen);
                            int missionCount = cargoInfo.Count(i => i.missionid != null);
                            if (total != cargo.total || stolen != cargo.stolen || missionCount != cargo.haulageData.Count())
                            {
                                UpdateCargoFromInfo(cargo, cargoInfo);
                            }
                        }
                        else
                        {
                            // Add cargo entries for those missing
                            cargo = new Cargo(name);
                            UpdateCargoFromInfo(cargo, cargoInfo);
                        }
                        AddOrUpdateCargo(cargo);
                        infoList.RemoveAll(i => i.name == name);
                    }
                }
            }
        }

        private void handleCommodityCollectedEvent(CommodityCollectedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityCollectedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityCollectedEvent(CommodityCollectedEvent @event)
        {
            bool update = false;
            Cargo cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                Haulage haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == @event.missionid);
                if (EDDI.Instance?.Vehicle != Constants.VEHICLE_SHIP)
                {
                    if (haulage != null)
                    {
                        cargo.AddDetailedQty(CargoType.haulage, 1, 0);
                    }
                    else if (@event.stolen)
                    {
                        cargo.AddDetailedQty(CargoType.stolen, 1, 0);
                    }
                    else
                    {
                        cargo.AddDetailedQty(CargoType.owned, 1, 0);
                    }
                    cargo.CalculateNeed();
                    update = true;
                }
                if (haulage != null && ((haulage.typeEDName?.Contains("mining") ?? false)
                    || (haulage.typeEDName?.Contains("piracy") ?? false)
                    || (haulage.typeEDName?.Contains("rescue") ?? false)
                    || (haulage.typeEDName?.Contains("salvage") ?? false)))
                {
                    haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                    haulage.sourcebody = EDDI.Instance?.CurrentStellarBody?.bodyname;
                    update = true;
                }
            }
            return update;
        }

        private void handleCommodityEjectedEvent(CommodityEjectedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityEjectedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityEjectedEvent(CommodityEjectedEvent @event)
        {
            bool update = false;
            Cargo cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                if (EDDI.Instance?.Vehicle != Constants.VEHICLE_SHIP)
                {
                    if (@event.missionid != null)
                    {
                        cargo.RemoveDetailedQty(CargoType.haulage, @event.amount, (long)@event.missionid);
                    }
                    else
                    {
                        cargo.RemoveDetailedQty(CargoType.owned, @event.amount);
                    }
                    cargo.CalculateNeed();
                    update = true;
                }

                Haulage haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == @event.missionid);
                if (haulage != null)
                {
                    switch (haulage.typeEDName)
                    {
                        case "delivery":
                        case "deliverywing":
                        case "smuggle":
                            {
                                haulage.status = "Failed";
                                var mission = ConfigService.Instance.missionMonitorConfiguration
                                    ?.missions
                                    ?.FirstOrDefault(m => m.missionid == @event.missionid);
                                if (mission != null)
                                {
                                    mission.statusDef = MissionStatus.FromEDName("Failed");
                                }
                                update = true;
                            }
                            break;
                    }
                }
            }
            return update;
        }

        private void handleCommodityPurchasedEvent(CommodityPurchasedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityPurchasedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityPurchasedEvent(CommodityPurchasedEvent @event)
        {
            Cargo cargo = GetCargoWithEDName(@event.commodityDefinition?.edname) ?? new Cargo(@event.commodityDefinition?.edname);
            Haulage haulage = cargo.haulageData.FirstOrDefault(h => h.typeEDName
                .ToLowerInvariant()
                .Contains("collect"));
            if (haulage != null)
            {
                haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                haulage.sourcebody = EDDI.Instance?.CurrentStation?.name;
                cargo.AddDetailedQty(CargoType.haulage, @event.amount, @event.price, haulage);
            }
            else
            {
                cargo.AddDetailedQty(CargoType.owned, @event.amount, @event.price);
            }
            AddOrUpdateCargo(cargo);
            return true;
        }

        private void handleCommodityRefinedEvent(CommodityRefinedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleCommodityRefinedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleCommodityRefinedEvent(CommodityRefinedEvent @event)
        {
            bool update = false;
            Cargo cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                Haulage haulage = cargo.haulageData.FirstOrDefault(h => h.typeEDName
                    .ToLowerInvariant()
                    .Contains("mining"));
                if (haulage != null)
                {
                    haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                    haulage.sourcebody = EDDI.Instance?.CurrentStation?.name;
                    update = true;
                }
            }
            return update;
        }

        private void handleCommoditySoldEvent(CommoditySoldEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                Cargo cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
                if (cargo != null)
                {
                    // Flag event to check whether haulage was sold in following 'Cargo' event
                    checkHaulage = true;
                }
            }
        }

        private void handleCargoDepotEvent(CargoDepotEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                _handleCargoDepotEvent(@event);
                writeInventory();
            }
        }

        private void _handleCargoDepotEvent(CargoDepotEvent @event)
        {
            var mission = ConfigService.Instance.missionMonitorConfiguration
                ?.missions
                ?.FirstOrDefault(m => m.missionid == @event.missionid);
            var cargo = new Cargo();
            var haulage = new Haulage();
            int amountRemaining = @event.totaltodeliver - @event.delivered;

            switch (@event.updatetype)
            {
                case "Collect":
                    {
                        cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                        if (cargo != null)
                        {
                            // Cargo instantiated by either 'Mission accepted' event or previous 'WingUpdate' update
                            haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                            if (haulage != null)
                            {
                                haulage.remaining = amountRemaining;
                                haulage.originsystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                            }

                            // Update commodity definition if instantiated other than 'Mission accepted'
                            cargo.commodityDef = @event.commodityDefinition;
                        }
                        else
                        {
                            // First exposure to new cargo.
                            cargo = new Cargo(@event.commodityDefinition.edname); // Total will be updated by following 'Cargo' event
                            string originSystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                            string name = mission?.name ?? "MISSION_DeliveryWing";
                            haulage = new Haulage(@event.missionid ?? 0, name, originSystem, amountRemaining, null, true);
                            cargo.haulageData.Add(haulage);
                        }
                        if (haulage != null)
                        {
                            haulage.collected = @event.collected;
                            haulage.delivered = @event.delivered;
                            haulage.startmarketid = @event.startmarketid;
                            haulage.endmarketid = @event.endmarketid;                            
                        }
                        AddOrUpdateCargo(cargo);
                    }
                    break;
                case "Deliver":
                    {
                        cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                        if (cargo != null)
                        {
                            // Cargo instantiated by either 'Mission accepted' event, previous 'WingUpdate' or 'Collect' updates
                            haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                            if (haulage != null)
                            {
                                haulage.remaining = amountRemaining;
                                haulage.need = amountRemaining;

                                //Update commodity definition
                                haulage.amount = @event.totaltodeliver;
                                cargo.commodityDef = @event.commodityDefinition;
                                haulage.originsystem = (@event.startmarketid == 0) ? EDDI.Instance?.CurrentStarSystem?.systemname : null;
                            }
                            else
                            {
                                string originSystem = (@event.startmarketid == 0) ? EDDI.Instance?.CurrentStarSystem?.systemname : null;
                                string name = mission?.name ?? (@event.startmarketid == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing");
                                haulage = new Haulage(@event.missionid ?? 0, name, originSystem, amountRemaining, null);
                                cargo.haulageData.Add(haulage);
                            }
                        }
                        else
                        {
                            // Check if cargo instantiated by previous 'Market buy' event
                            // Total will be updated by following 'Cargo' event
                            cargo = GetCargoWithEDName(@event.commodityDefinition.edname) ?? new Cargo(@event.commodityDefinition.edname); 
                            string originSystem = (@event.startmarketid == 0) ? EDDI.Instance?.CurrentStarSystem?.systemname : null;
                            string name = mission?.name ?? (@event.startmarketid == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing");
                            haulage = new Haulage(@event.missionid ?? 0, name, originSystem, amountRemaining, null, true);
                            cargo.haulageData.Add(haulage);
                            AddOrUpdateCargo(cargo);
                        }

                        // Update 'Need' when cargo is delivered, as the 'Cargo' event handler does not update 'Collect' mission types
                        cargo.CalculateNeed();

                        haulage.collected = @event.collected;
                        haulage.delivered = @event.delivered;
                        haulage.endmarketid = (haulage.endmarketid == 0) ? @event.endmarketid : haulage.endmarketid;

                        // Check for mission completion
                        if (amountRemaining == 0)
                        {
                            if (haulage.shared)
                            {
                                cargo.haulageData.Remove(haulage);
                                RemoveCargo(cargo);
                            }
                            else
                            {
                                haulage.status = "Complete";
                            }
                        }
                    }
                    break;
                case "WingUpdate":
                    {
                        cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                        if (cargo != null)
                        {
                            // Cargo instantiated by either 'Mission accepted' event, previous 'WingUpdate' or 'Collect' updates
                            haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                            if (haulage != null)
                            {
                                haulage.remaining = amountRemaining;
                                haulage.need = amountRemaining;                                
                            }
                        }
                        else
                        {
                            // First exposure to new cargo, use 'Unknown' as placeholder
                            cargo = new Cargo("Unknown");
                            string name = mission?.name ?? (@event.startmarketid == 0 ? "MISSION_CollectWing" : "MISSION_DeliveryWing");
                            haulage = new Haulage(@event.missionid ?? 0, name, null, amountRemaining, null, true);
                            cargo.haulageData.Add(haulage);
                        }
                        AddOrUpdateCargo(cargo);

                        // Generate a derived event when a wing-mate collects or delivers cargo for a wing mission
                        if (haulage != null)
                        {
                            int amount = Math.Max(@event.collected - haulage.collected, @event.delivered - haulage.delivered);
                            if (amount > 0)
                            {
                                string updatetype = @event.collected > haulage.collected ? "Collect" : "Deliver";
                                EDDI.Instance.enqueueEvent(new CargoWingUpdateEvent(DateTime.UtcNow, haulage.missionid, updatetype, cargo.commodityDef, amount, @event.collected, @event.delivered, @event.totaltodeliver));
                                haulage.collected = @event.collected;
                                haulage.delivered = @event.delivered;
                                haulage.startmarketid = @event.startmarketid;
                                haulage.endmarketid = @event.endmarketid;

                                // Update 'Need' when a wing-mate delivers cargo for a wing mission
                                if (updatetype == "Deliver") { cargo.CalculateNeed(); }
                            }

                            // Check for mission completion
                            if (amountRemaining == 0)
                            {
                                if (haulage.shared)
                                {
                                    cargo.haulageData.Remove(haulage);
                                    RemoveCargo(cargo);
                                }
                                else
                                {
                                    haulage.status = "Complete";
                                }
                            }
                        }
                    }
                    break;
            }
        }
        private void handleLimpetPurchasedEvent(LimpetPurchasedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleLimpetPurchasedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleLimpetPurchasedEvent(LimpetPurchasedEvent @event)
        {
            Cargo cargo = GetCargoWithEDName("Drones") ?? new Cargo("Drones");
            cargo.AddDetailedQty(CargoType.owned, @event.amount, @event.price);
            AddOrUpdateCargo(cargo);
            return true;
        }

        private void handleMissionsEvent(MissionsEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionsEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionsEvent(MissionsEvent @event)
        {
            bool update = false;
            foreach (Cargo cargo in inventory.ToList())
            {
                // Strip out haulage strays
                foreach (Haulage haulage in cargo.haulageData.ToList())
                {
                    Mission mission = @event.missions.FirstOrDefault(m => m.missionid == haulage.missionid);
                    if (mission == null)
                    {
                        cargo.haulageData.Remove(haulage);
                        update = true;
                    }
                }
            }
            return update;
        }

        private void handleMissionAbandonedEvent(MissionAbandonedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionAbandonedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionAbandonedEvent(MissionAbandonedEvent @event)
        {
            bool update = false;
            Haulage haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage != null)
            {
                Cargo cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                int onboard = haulage.remaining - haulage.need;
                cargo.RemoveDetailedQty(CargoType.haulage, onboard, @event.missionid);
                cargo.AddDetailedQty(CargoType.stolen, onboard, cargo.price);
                RemoveCargo(cargo);
                update = true;
            }
            return update;
        }

        private void handleMissionAcceptedEvent(MissionAcceptedEvent @event)
        {
            if (@event.timestamp > updateDat && @event.Mission.CommodityDefinition != null)
            {
                updateDat = @event.timestamp;
                if (_handleMissionAcceptedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionAcceptedEvent(MissionAcceptedEvent @event)
        {
            bool update = false;
            Haulage haulage = new Haulage();

            // Protect against duplicates & empty strings
            haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage == null && !string.IsNullOrEmpty(@event.name))
            {
                Cargo cargo = new Cargo();

                string type = @event.name.Split('_').ElementAt(1)?.ToLowerInvariant();
                if (type != null && CHAINED.TryGetValue(type, out string value))
                {
                    type = value;
                }
                else if (type == "ds" || type == "rs" || type == "welcome")
                {
                    type = @event.name.Split('_').ElementAt(2)?.ToLowerInvariant();
                }

                switch (type)
                {
                    case "altruism":
                    case "collect":
                    case "collectwing":
                    case "delivery":
                    case "deliverywing":
                    case "mining":
                    case "piracy":
                    case "rescue":
                    case "salvage":
                    case "smuggle":
                        {
                            bool naval = @event.name.ToLowerInvariant().Contains("rank");
                            string originSystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                            haulage = new Haulage(@event.missionid ?? 0, @event.name, originSystem, @event.amount ?? 0, @event.expiry)
                            {
                                startmarketid = (type.Contains("delivery") && !naval) ? EDDI.Instance?.CurrentStation?.marketId ?? 0 : 0,
                                endmarketid = (type.Contains("collect")) ? EDDI.Instance?.CurrentStation?.marketId ?? 0 : 0,
                            };

                            if (type.Contains("delivery") || type == "smuggle")
                            {
                                haulage.sourcesystem = EDDI.Instance?.CurrentStarSystem?.systemname;
                                haulage.sourcebody = EDDI.Instance?.CurrentStation?.name;
                            }
                            else if (type == "rescue" || type == "salvage")
                            {
                                haulage.sourcesystem = @event.destinationsystem;
                            }

                            cargo = GetCargoWithEDName(@event.Mission.CommodityDefinition?.edname) ?? new Cargo(@event.Mission.CommodityDefinition?.edname);
                            cargo.haulageData.Add(haulage);
                            cargo.CalculateNeed();
                            AddOrUpdateCargo(cargo);
                            update = true;
                        }
                        break;
                }
            }
            return update;
        }

        private void handleMissionCompletedEvent(MissionCompletedEvent @event)
        {
            if (@event.commodityDefinition != null || @event.commodityrewards != null)
            {
                if (@event.timestamp > updateDat)
                {
                    updateDat = @event.timestamp;
                    if (_handleMissionCompletedEvent(@event))
                    {
                        writeInventory();
                    }
                }
            }
        }

        private bool _handleMissionCompletedEvent(MissionCompletedEvent @event)
        {
            bool update = false;
            Cargo cargo = GetCargoWithEDName(@event.commodityDefinition?.edname);
            if (cargo != null)
            {
                Haulage haulage = cargo.haulageData.FirstOrDefault(ha => ha.missionid == @event.missionid);
                if (haulage != null)
                {
                    cargo.haulageData.Remove(haulage);
                }
                RemoveCargo(cargo);
                update = true;
            }
            return update;
        }

        private void handleMissionExpiredEvent(MissionExpiredEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionExpiredEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionExpiredEvent(MissionExpiredEvent @event)
        {
            bool update = false;
            Haulage haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage != null)
            {
                haulage.status = "Failed";
                update = true;
            }
            return update;
        }

        private void handleMissionFailedEvent(MissionFailedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleMissionFailedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleMissionFailedEvent(MissionFailedEvent @event)
        {
            bool update = false;
            Haulage haulage = GetHaulageWithMissionId(@event.missionid ?? 0);
            if (haulage != null)
            {
                Cargo cargo = GetCargoWithMissionId(@event.missionid ?? 0);
                int onboard = haulage.remaining - haulage.need;
                cargo.RemoveDetailedQty(CargoType.haulage, onboard, haulage);
                cargo.AddDetailedQty(CargoType.stolen, onboard, cargo.price);
                RemoveCargo(cargo);
                return true;
            }
            return update;
        }

        private void handleDiedEvent(DiedEvent @event)
        {
            inventory.Clear();
            writeInventory();
        }

        private void handleEngineerContributedEvent(EngineerContributedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleEngineerContributedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleEngineerContributedEvent(EngineerContributedEvent @event)
        {
            bool update = false;
            if (@event.commodityAmount != null)
            {
                Cargo cargo = GetCargoWithEDName(@event.commodityAmount.edname);
                if (cargo != null)
                {
                    cargo.RemoveDetailedQty(CargoType.owned, Math.Min(cargo.owned, @event.commodityAmount.amount));
                    RemoveCargo(cargo);
                    update = true;
                }
            }
            return update;
        }

        private void handleSynthesisedEvent(SynthesisedEvent @event)
        {
            if (@event.timestamp > updateDat)
            {
                updateDat = @event.timestamp;
                if (_handleSynthesisedEvent(@event))
                {
                    writeInventory();
                }
            }
        }

        private bool _handleSynthesisedEvent(SynthesisedEvent @event)
        {
            if (@event.synthesis.Contains("Limpet"))
            {
                Cargo cargo = GetCargoWithEDName("Drones") ?? new Cargo("Drones");
                cargo.AddDetailedQty(CargoType.owned, 4, 0);
                AddOrUpdateCargo(cargo);
                return true;
            }
            return false;
        }

        public IDictionary<string, object> GetVariables()
        {
            IDictionary<string, object> variables = new Dictionary<string, object>
            {
                ["inventory"] = new List<Cargo>(inventory),
                ["cargoCarried"] = cargoCarried
            };
            return variables;
        }

        public void writeInventory()
        {
            lock (inventoryLock)
            {
                // Write cargo configuration with current inventory
                var configuration = new CargoMonitorConfiguration()
                {
                    updatedat = updateDat,
                    cargo = inventory,
                    cargocarried = cargoCarried
                };
                ConfigService.Instance.cargoMonitorConfiguration = configuration;
            }
            // Make sure the UI is up to date
            RaiseOnUIThread(InventoryUpdatedEvent, inventory);
        }

        private void readInventory(CargoMonitorConfiguration configuration = null)
        {
            lock (inventoryLock)
            {
                // Obtain current cargo inventory from configuration
                configuration = configuration ?? ConfigService.Instance.cargoMonitorConfiguration;
                cargoCarried = configuration.cargocarried;
                updateDat = configuration.updatedat;

                // Build a new inventory
                List<Cargo> newInventory = new List<Cargo>();

                // Start with the materials we have in the log
                foreach (Cargo cargo in configuration.cargo)
                {
                    if (cargo.commodityDef == null)
                    {
                        cargo.commodityDef = CommodityDefinition.FromEDName(cargo.edname);
                    }
                    cargo.CalculateNeed();
                    newInventory.Add(cargo);
                }

                // Now order the list by name
                newInventory = newInventory.OrderBy(c => c.invariantName).ToList();

                // Update the inventory 
                inventory.Clear();
                foreach (Cargo cargo in newInventory)
                {
                    inventory.Add(cargo);
                }
            }
        }

        private void AddOrUpdateCargo(Cargo cargo)
        {
            if (cargo == null) { return; }
            lock (inventoryLock)
            {
                bool found = false;
                for (int i = 0; i < inventory.Count; i++)
                {
                    if (string.Equals(inventory[i].edname, cargo.edname, StringComparison.InvariantCultureIgnoreCase))
                    {
                        found = true;
                        inventory[i] = cargo;
                        break;
                    }
                }
                if (!found)
                {
                    inventory.Add(cargo);
                }
            }
        }

        private void RemoveCargo(Cargo cargo)
        {
            // Check if missions are pending
            if (cargo.haulageData == null || !cargo.haulageData.Any())
            {
                if (cargo.total < 1)
                {
                    // All of the commodity was either expended, ejected, or sold
                    _RemoveCargoWithEDName(cargo.edname);
                }
            }
            else
            {
                cargo.CalculateNeed();
            }
        }

        private void _RemoveCargoWithEDName(string edname)
        {
            lock (inventoryLock)
            {
                if (edname != null)
                {
                    edname = edname.ToLowerInvariant();
                    for (int i = 0; i < inventory.Count; i++)
                    {
                        if (inventory[i].edname.ToLowerInvariant() == edname)
                        {
                            inventory.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        public Cargo GetCargoWithEDName(string edname)
        {
            if (edname == null)
            {
                return null;
            }
            edname = edname.ToLowerInvariant();
            return inventory.FirstOrDefault(c => c.edname.ToLowerInvariant() == edname);
        }

        public Cargo GetCargoWithMissionId(long missionid)
        {
            foreach (Cargo cargo in inventory.ToList())
            {
                if (cargo.haulageData.FirstOrDefault(h => h.missionid == missionid) != null)
                {
                    return cargo;
                }
            }
            return null;
        }

        public Haulage GetHaulageWithMissionId(long missionid)
        {
            foreach (Cargo cargo in inventory.ToList())
            {
                Haulage haulage = cargo.haulageData.FirstOrDefault(h => h.missionid == missionid);
                if (haulage != null)
                {
                    return haulage;
                }
            }
            return null;
        }

        private void UpdateCargoFromInfo(Cargo cargo, List<CargoInfoItem> infoList)
        {
            cargo.haulage = infoList.Where(i => i.missionid != null).Sum(i => i.count);
            cargo.stolen = infoList.Where(i => i.missionid == null).Sum(i => i.stolen);
            cargo.owned = infoList.Sum(i => i.count) - cargo.haulage - cargo.stolen;

            foreach (CargoInfoItem info in infoList.Where(i => i.missionid != null).ToList())
            {
                var mission = ConfigService.Instance.missionMonitorConfiguration
                    ?.missions
                    ?.FirstOrDefault(m => m.missionid == info.missionid);
                var cargoHaulage = cargo.haulageData.FirstOrDefault(h => h.missionid == info.missionid);
                if (cargoHaulage != null)
                {
                    // Check for sold haulage
                    if (checkHaulage && cargoHaulage.need > info.count)
                    {
                        // We lost haulage
                        switch (cargoHaulage.typeEDName)
                        {
                            case "delivery":
                            case "deliverywing":
                            case "smuggle":
                                {
                                    cargoHaulage.status = "Failed";
                                    if (mission != null)
                                    {
                                        mission.statusDef = MissionStatus.FromEDName("Failed");
                                    }
                                }
                                break;
                        }
                    }
                }
                else
                {
                    string name = mission?.name ?? "Mission_None";
                    int amount = mission?.amount ?? info.count;
                    DateTime? expiry = mission?.expiry;

                    cargoHaulage = new Haulage(info.missionid ?? 0, name, mission?.originsystem, amount, expiry);
                    cargo.haulageData.Add(cargoHaulage);
                }
            }
            cargo.CalculateNeed();
            checkHaulage = false;
        }

        static void RaiseOnUIThread(EventHandler handler, object sender)
        {
            if (handler != null)
            {
                SynchronizationContext uiSyncContext = SynchronizationContext.Current ?? new SynchronizationContext();
                if (uiSyncContext == null)
                {
                    handler(sender, EventArgs.Empty);
                }
                else
                {
                    uiSyncContext.Send(delegate { handler(sender, EventArgs.Empty); }, null);
                }
            }
        }
    }
}

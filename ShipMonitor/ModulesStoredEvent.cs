﻿using EddiDataDefinitions;
using EddiEvents;
using System;
using System.Collections.Generic;
using Utilities;

namespace EddiShipMonitor
{
    public class ModulesStoredEvent : Event
    {
        public const string NAME = "Modules stored";
        public const string DESCRIPTION = "Triggered when you store multiple modules";
        public const string SAMPLE = "{ \"timestamp\":\"2017-09-18T16:42:53Z\", \"event\":\"MassModuleStore\", \"MarketID\": 128666762, \"Ship\":\"federation_corvette\", \"ShipID\":10, \"Items\":[ { \"Slot\":\"Slot02_Size7\", \"Name\":\"$int_shieldcellbank_size7_class5_name;\", \"Hot\":false }, { \"Slot\":\"Slot03_Size7\", \"Name\":\"$int_shieldcellbank_size7_class5_name;\", \"Hot\":false }, { \"Slot\":\"Slot05_Size6\", \"Name\":\"$int_cargorack_size6_class1_name;\", \"Hot\":true }, { \"Slot\":\"Slot07_Size5\", \"Name\":\"$int_fuelscoop_size5_class5_name;\", \"Hot\":false }] }";
        public static Dictionary<string, string> VARIABLES = new Dictionary<string, string>();

        static ModulesStoredEvent()
        {
            VARIABLES.Add("ship", "The ship from which the modules were stored");
            VARIABLES.Add("shipid", "The ID of the ship from which the module were stored");
            VARIABLES.Add("slots", "The outfitting slots");
            VARIABLES.Add("modules", "The stored modules (as objects)");
        }

        [PublicAPI]
        public string ship => shipDefinition?.model;

        [PublicAPI]
        public int? shipid { get; private set; }

        [PublicAPI]
        public List<string> slots { get; private set; }

        [PublicAPI]
        public List<Module> modules { get; private set; }

        // Not intended to be user facing

        public long marketId { get; private set; }

        public Ship shipDefinition { get; private set; }

        public ModulesStoredEvent(DateTime timestamp, string ship, int? shipid, List<string> slots, List<Module> modules, long marketId) : base(timestamp, NAME)
        {
            this.shipDefinition = ShipDefinitions.FromEDModel(ship);
            this.shipid = shipid;
            this.slots = slots;
            this.modules = modules;
            this.marketId = marketId;
        }
    }
}

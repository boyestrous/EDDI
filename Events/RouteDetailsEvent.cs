﻿using System;
using System.Collections.Generic;
using System.Linq;
using EddiDataDefinitions;
using Utilities;

namespace EddiEvents
{
    [PublicAPI]
    public class RouteDetailsEvent : Event
    {
        public const string NAME = "Route details";
        public const string DESCRIPTION = "Triggered when a route has been generated or updated";
        public const string SAMPLE = null;

        [PublicAPI("Type of route query")]
        public string routetype { get; private set; }

        [PublicAPI("The destination system")]
        public string system { get; private set; }

        [PublicAPI("The destination station, if applicable")]
        public string station { get; private set; }

        [PublicAPI("List of system names, if applicable")]
        public List<string> route => Route.Select(r => r.systemName).ToList();

        [PublicAPI("Count of missions, systems, or expiry seconds, depending on route type")]
        public long count { get; private set; }

        [PublicAPI("The distance to the destination system")]
        public decimal distance { get; private set; }

        [PublicAPI("The remaining distance of the missions route, if applicable")]
        public decimal routedistance { get; private set; }

        [PublicAPI("The mission ID(s) associated with the destination system, if applicable")]
        public List<long> missionids { get; private set; }

        // Not intended to be user facing
        public List<NavWaypoint> Route { get; private set; }

        public RouteDetailsEvent(DateTime timestamp, string routetype, string system, string station, List<NavWaypoint> route, long count, decimal distance, decimal routedistance, List<long> missionids) : base(timestamp, NAME)
        {
            this.routetype = routetype;
            this.system = system;
            this.station = station;
            this.Route = route;
            this.count = count;
            this.distance = distance;
            this.routedistance = routedistance;
            this.missionids = missionids;
        }
    }
}

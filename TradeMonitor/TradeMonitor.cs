using EddiConfigService;
using EddiCore;
using EddiDataDefinitions;
using EddiEvents;
using EddiNavigationMonitor;
using EddiStarMapService;
using MathNet.Numerics.RootFinding;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Windows.Services.Maps;
using Windows.System;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using JetBrains.Annotations;
using Utilities;

namespace EddiTradeMonitor
{
    [UsedImplicitly]
    public class TradeMonitor : EDDIMonitor
    {

        #region monitorInfo
        public string MonitorName() { return ("Trade Monitor"); }
        public string LocalizedMonitorName() { return Properties.TradeResources.name; }
        public string MonitorDescription() { return Properties.TradeResources.desc; }
        public bool IsRequired() { return false; }

        #endregion

        #region variables

        IEdsmService edsmService = new StarMapService();

        public NavWaypointCollection validSystems = new NavWaypointCollection();
        public static event EventHandler NavRouteUpdatedEvent;
        public static readonly object tradeConfigLock = new object();

        StarSystem currentSystem {get; set;}
        Superpower cmdrAllegiance {get; set;}
        Superpower cmdrSuperPower {get; set;}
        Power cmdrSquadronPower {get; set;}

        //int maxStationDistance { get; set; }

        #region devComponents
        NavWaypointCollection navRouteList;


        #endregion

        public IDictionary<string, object> GetVariables()
        {
            //var tradeConfig = ConfigService.Instance.tradeMonitorConfiguration;
            IDictionary<string, object> variables = new Dictionary<string, object>
            {
                ["validSystems"] = validSystems,
                // TODO add TradeConfig to ConfigService Instances
                // TradeConfig info
                //["orbitalpriority"] = tradeConfig.prioritizeOrbitalStations,
                //["maxStationDistance"] = tradeConfig.maxSearchDistanceFromStarLs
                //["minSystemPopulation"] = tradeConfig.maxSearchDistanceFromStarLs
                //["prioritizeAllegiance"] = tradeConfig.maxSearchDistanceFromStarLs
                //["stationState"] = tradeConfig.maxSearchDistanceFromStarLs
                //["maxListingAge"] = tradeConfig.maxSearchDistanceFromStarLs
                //["prioritizeRare"] = tradeConfig.maxSearchDistanceFromStarLs
            };
            return variables;
        }

        #endregion

        #region classControls


        public UserControl ConfigurationTabItem() { return new ConfigurationWindow(); }

        public TradeMonitor()
        {
            BindingOperations.CollectionRegistering += TradeMonitor_CollectionRegistering;
            LoadMonitor();
            Logging.Info($"Initialized {MonitorName()}");
        }

        private void LoadMonitor()
        {
            //TODO get current Cargo
            
            //cmdrAllegiance = EDDI.Instance?.Cmdr.squadronallegiance;
            //cmdrSuperPower = EDDI.Instance?.Cmdr.Power.Allegiance;
            //cmdrSquadronPower = EDDI.Instance?.Cmdr.squadronpower;
        }

        private void TradeMonitor_CollectionRegistering(object sender, CollectionRegisteringEventArgs e)
        {
            if (Application.Current != null)
            {
                // Synchronize this collection between threads
                BindingOperations.EnableCollectionSynchronization(validSystems.Waypoints, tradeConfigLock);
            }
            else
            {
                // If started from VoiceAttack, the dispatcher is on a different thread. Invoke synchronization there.
                Dispatcher.CurrentDispatcher.Invoke(() => { BindingOperations.EnableCollectionSynchronization(validSystems.Waypoints, tradeConfigLock); });
            }
        }

        public bool NeedsStart() { return false; }

        public void Start(){}
        public void Stop(){}
        public void Reload()
        {
            LoadMonitor();
            Logging.Info($"Reloaded {MonitorName()}");
        }

        #endregion

        #region handlers

        public void HandleProfile(JObject profile){}

        public void PreHandle(Event @event) { }

        public void PostHandle(Event @event) {
            if (@event.fromLoad)
            {
                return;
            }

            if (@event is DockedEvent)
            {
                @event = (DockedEvent)@event;
                //Console.WriteLine("Trade Monitor has identified a Docked Event!");
            }
            else if (@event is NavRouteEvent)
            {
                NavRouteEvent navEvent = (NavRouteEvent)@event;

                // Make sure ship is set correctly in interface
                    // to obtain the commodity information EDDI needs access to the companion API. This requires that the user has configured their "Companion App" tab on the EDDI interface.
                    //EDDI will only provide this information if you are flying in a ship that is configured for trading.Ensure that your ship is configured for either "Trading" or "Multi-purpose" in the "Shipyard" tab on the EDDI interface.

                // get systems in sphere radius
                    // radius = distance to next navpoint
                    // center of sphere - next navpoint
                    // or nearest point between where 2r < max jump
                List<StarSystem> systemsAlongRoute = getSystemsAlongRoute(navEvent);
                //List<Station> stationsAlongRoute = getStations(systemsAlongRoute);

                // get stations in those systems
                // cache systems, so we don't have to hit the API so many times
                // apply user settings for station search
                // Through Navigation Monitor -> Config

                // get listings at those stations
                // from stations list, filter out those without a market

                // use Navigation service to re-route after landing and selling
                // at minimum, copy next destination to clipboard
            }
            else if (@event is JumpedEvent)
            {
                Console.WriteLine("Trade Monitor has identified a JUMPED Event!");

            }
        }

        #endregion

        
        #region preHandledEvents

        private void handleNavRouteEvent()
        {}

        #endregion


        #region postHandledEvents





        #endregion


        private List<StarSystem> getSystemsAlongRoute(NavRouteEvent navEvent)
        {
            Console.WriteLine("TradeMonitor getSystemsAlongRoute --------------");
            if (navEvent is null) { return new List<StarSystem>(); }
            
            navRouteList = new NavWaypointCollection();
            currentSystem = EDDI.Instance?.CurrentStarSystem;


            if (currentSystem == null) { throw new Exception("current system is not defined"); }
            Console.WriteLine($"CurrentStarSystem retrieved: {currentSystem.systemname}");

            List<NavWaypoint> navWaypoints = new List<NavWaypoint>();
            foreach (NavRouteInfoItem w in navEvent.route)
            {
                navWaypoints.Add(new NavWaypoint(w));
            }

            navRouteList.AddRange(navWaypoints);
            Console.WriteLine($"Waypoints added - len: {navRouteList.Waypoints.Count}");


            var nextWaypoint = navRouteList.Waypoints.FirstOrDefault(w => w.systemName != currentSystem.systemname);
            Console.WriteLine($"Next waypoint: {nextWaypoint.systemName} - distance: {nextWaypoint.distance}");


            // get Sphere systems
            List<Dictionary<string, object>> sphereSystems = edsmService.GetStarMapSystemsSphere(nextWaypoint.systemName, 0, Decimal.ToInt32(nextWaypoint.distance)); //TODO ship max jump
            sphereSystems = sphereSystems
                                .Where(kvp => (kvp["system"] as StarSystem)?.requirespermit == false)
                                .Where(kvp => Decimal.Parse(kvp["distance"].ToString()) < nextWaypoint.distance) //TODO ship max jump
                                // TODO if Allegiance
                                //.Select<StarSystem>(system => new StarSystem(system["system"])
                                //.Where(kvp => (kvp["system"] as StarSystem)?.Faction.Allegiance == 'Empire') 
                                .ToList();


            foreach (Dictionary<string,object> system in sphereSystems)
            {
                validSystems.Waypoints.Add(new NavWaypoint(system["system"] as StarSystem));
                //validSystems.Append(system["system"] as StarSystem);
            }


            // filter systems
            // dict information
            // int population
            // string allegiance
            // string factionState

            // get Stations in systems
            // edsmService.GetStarMapStations
            // filter stations 
            // bool haveMarket
            // string type is not "Fleet carrier"
            // string allegiance
            // int distanceToArrival
            // string type skip_station_types = ['Planetary Outpost','Planetary Port','Odyssey Settlement']
            // get markets from filtered stations
            // create Listing class
            // merge market ID info into the commodities objects
            // bring in rare commodities resource from eddb
            // commodities that match available at current market
            // int demand > something
            // sellPrice > buy price


            //sphereSystems = sphereSystems.Where(kvp => (kvp["system"] as StarSystem)?.stations.Count > 0).ToList();
            Console.WriteLine($"spheresystems count: {sphereSystems.Count}");
            return new List<StarSystem>();
        }


        

        

        




    }
}

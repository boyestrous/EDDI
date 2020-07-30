﻿using Eddi;
using EddiCompanionAppService;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiEvents;
using EddiSpeechService;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Utilities;

namespace EddiCore
{
    /// <summary>
    /// Eddi is the controller for all EDDI operations.  Its job is to retain the state of the objects such as the commander, the current system, etc.
    /// and keep them up-to-date with changes that occur.
    /// It also acts as the switchboard for passing events through all parts of the application including both responders and monitors.
    /// </summary>
    public class EDDI
    {
        // True if the Speech Responder tab is waiting on a modal dialog window. Accessed by VoiceAttack plugin.
        public bool SpeechResponderModalWait { get; set; } = false;

        private static bool started;
        internal static bool running = true;

        private static bool allowMarketUpdate = false;
        private static bool allowOutfittingUpdate = false;
        private static bool allowShipyardUpdate = false;

        public bool inCQC { get; private set; } = false;
        public bool inCrew { get; private set; } = false;

        public bool inHorizons { get; private set; } = true;
        public bool gameIsBeta { get; private set; } = false;

        static EDDI()
        {
            // Set up our app directory
            Directory.CreateDirectory(Constants.DATA_DIR);
        }

        public static void Init(bool safeMode)
        {
            if (instance == null)
            {
                lock (instanceLock)
                {
                    if (instance == null)
                    {
                        Logging.Debug("No EDDI instance: creating one");
                        instance = new EDDI(safeMode);
                    }
                }
            }
        }

        // EDDI Instance
        public static EDDI Instance
        {
            get
            {
                Init(false);
                return instance;
            }
        }
        private static EDDI instance;
        private static readonly object instanceLock = new object();

        public List<EDDIMonitor> monitors = new List<EDDIMonitor>();
        private ConcurrentBag<EDDIMonitor> activeMonitors = new ConcurrentBag<EDDIMonitor>();
        private static readonly object monitorLock = new object();

        public List<EDDIResponder> responders = new List<EDDIResponder>();
        private ConcurrentBag<EDDIResponder> activeResponders = new ConcurrentBag<EDDIResponder>();
        private static readonly object responderLock = new object();

        public string vaVersion { get; set; }

        // Information obtained from the configuration
        public StarSystem HomeStarSystem { get; private set; } // May be null when the commander hasn't set a home star system
        public Station HomeStation { get; private set; }
        public StarSystem SquadronStarSystem { get; private set; } // May be null when the commander hasn't set a squadron star system

        // Destination variables
        public StarSystem DestinationStarSystem { get; private set; }
        public Station DestinationStation { get; private set; }
        public decimal DestinationDistanceLy { get; set; }

        // Information obtained from the player journal
        public Commander Cmdr { get; private set; } // Also includes information from the configuration and companion app service
        public string Environment { get; set; }
        public StarSystem CurrentStarSystem { get; private set; }
        public StarSystem LastStarSystem { get; private set; }
        public StarSystem NextStarSystem { get; private set; }
        public Station CurrentStation { get; private set; }
        public Body CurrentStellarBody { get; private set; }
        public DateTime JournalTimeStamp { get; set; } = DateTime.MinValue;

        // Information from the last jump we initiated (for reference)
        public FSDEngagedEvent LastFSDEngagedEvent { get; private set; }

        // Current vehicle of player
        public string Vehicle { get; set; } = Constants.VEHICLE_SHIP;
        public Ship CurrentShip { get; set; }

        public ObservableConcurrentDictionary<string, object> State = new ObservableConcurrentDictionary<string, object>();

        // The event queue
        public ConcurrentQueue<Event> eventQueue { get; private set; } = new ConcurrentQueue<Event>();

        private EDDI(bool safeMode)
        {
            running = !safeMode;
            try
            {
                Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " starting");

                // Ensure that our primary data structures have something in them.  This allows them to be updated from any source
                Cmdr = new Commander();

                // Set up the Elite configuration
                EliteConfiguration eliteConfiguration = EliteConfiguration.FromFile();
                gameIsBeta = eliteConfiguration.Beta;
                Logging.Info(gameIsBeta ? "On beta" : "On live");
                inHorizons = eliteConfiguration.Horizons;

                // Set up our CompanionAppService instance
                // CAUTION: CompanionAppService.Instance must be invoked with the EDDI .ctor to correctly
                // configure the CompanionAppService to receive DDE messages from its custom URL Protocol.
                CompanionAppService.Instance.gameIsBeta = gameIsBeta;

                // Retrieve commander preferences
                EDDIConfiguration configuration = EDDIConfiguration.FromFile();

                List<Task> essentialAsyncTasks = new List<Task>();
                if (running)
                {
                    // Tasks we can start asynchronously but need to complete before other dependent code is called
                    essentialAsyncTasks.AddRange(new List<Task>()
                    {
                        Task.Run(() => responders = findResponders()), // Set up responders
                        Task.Run(() => monitors = findMonitors()), // Set up monitors 
                    });
                    // If our home system and squadron system are the same, run those tasks in the same thread to prevent fetching from the star system database multiple times.
                    // Otherwise, run them in seperate threads.
                    void ActionUpdateHomeSystemStation()
                    {
                        updateHomeSystemStation(configuration);
                    }
                    void ActionUpdateSquadronSystem()
                    {
                        updateSquadronSystem(configuration);
                        Cmdr.squadronname = configuration.SquadronName;
                        Cmdr.squadronid = configuration.SquadronID;
                        Cmdr.squadronrank = configuration.SquadronRank;
                        Cmdr.squadronallegiance = configuration.SquadronAllegiance;
                        Cmdr.squadronpower = configuration.SquadronPower;
                        Cmdr.squadronfaction = configuration.SquadronFaction;
                    }
                    if (configuration.HomeSystem == configuration.SquadronSystem)
                    {
                        // Run both actions on the same thread
                        essentialAsyncTasks.Add(Task.Run((Action)ActionUpdateHomeSystemStation + ActionUpdateSquadronSystem));
                    }
                    else
                    {
                        // Run both actions on distinct threads
                        essentialAsyncTasks.AddRange(new List<Task>()
                        {
                            // Method groups are considered by AppVeyor to be an ambiguous call.
                            // ReSharper disable ConvertClosureToMethodGroup
                            Task.Run(() => ActionUpdateHomeSystemStation()),
                            Task.Run(() => ActionUpdateSquadronSystem())
                            // ReSharper restore ConvertClosureToMethodGroup
                        });
                    }
                }
                else
                {
                    Logging.Info("Mandatory upgrade required! EDDI initializing in safe mode until upgrade is completed.");
                }

                // Tasks we can start asynchronously and don't need to wait for
                Cmdr.name = configuration.CommanderName;
                Cmdr.phoneticName = configuration.PhoneticName;
                Cmdr.gender = configuration.Gender;
                Task.Run(() => updateDestinationSystemStation(configuration));
                Task.Run(() =>
                {
                    // Set up the Frontier API service
                    if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.Authorized)
                    {
                        // Carry out initial population of profile
                        try
                        {
                            refreshProfile();
                        }
                        catch (Exception ex)
                        {
                            Logging.Debug("Failed to obtain Frontier API profile: " + ex);
                        }
                    }
                    if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.Authorized)
                    {
                        Logging.Info("EDDI access to the Frontier API is enabled.");
                    }
                    else
                    {
                        Logging.Info("EDDI access to the Frontier API is not enabled.");
                    }
                });

                // Make sure that our essential tasks have completed before we start
                Task.WaitAll(essentialAsyncTasks.ToArray());

                // We always start in normal space
                Environment = Constants.ENVIRONMENT_NORMAL_SPACE;

                Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " initialised");
            }
            catch (Exception ex)
            {
                Logging.Error("Failed to initialise", ex);
            }
        }

        public bool EddiIsBeta() => Constants.EDDI_VERSION.phase < Utilities.Version.TestPhase.rc;

        public bool ShouldUseTestEndpoints()
        {
#if DEBUG
            return true;
#else
            // use test endpoints if the game is in beta or EDDI is not release candidate or final
            return EDDI.Instance.gameIsBeta || EddiIsBeta();
#endif
        }

        public void Start()
        {
            if (!started)
            {
                EDDIConfiguration configuration = EDDIConfiguration.FromFile();

                foreach (EDDIMonitor monitor in monitors)
                {
                    if (!configuration.Plugins.TryGetValue(monitor.MonitorName(), out bool enabled))
                    {
                        // No information; default to enabled
                        enabled = true;
                    }

                    if (!enabled)
                    {
                        Logging.Debug(monitor.MonitorName() + " is disabled; not starting");
                    }
                    else
                    {
                        activeMonitors.Add(monitor);
                        if (monitor.NeedsStart())
                        {
                            Thread monitorThread = new Thread(() => keepAlive(monitor.MonitorName(), monitor.Start))
                            {
                                IsBackground = true
                            };
                            Logging.Info("Starting keepalive for " + monitor.MonitorName());
                            monitorThread.Start();
                        }
                    }
                }

                foreach (EDDIResponder responder in responders)
                {
                    if (!configuration.Plugins.TryGetValue(responder.ResponderName(), out bool enabled))
                    {
                        // No information; default to enabled
                        enabled = true;
                    }

                    if (!enabled)
                    {
                        Logging.Debug(responder.ResponderName() + " is disabled; not starting");
                    }
                    else
                    {
                        try
                        {
                            bool responderStarted = responder.Start();
                            if (responderStarted)
                            {
                                activeResponders.Add(responder);
                                Logging.Info("Started " + responder.ResponderName());
                            }
                            else
                            {
                                Logging.Warn("Failed to start " + responder.ResponderName());
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Error("Failed to start " + responder.ResponderName(), ex);
                        }

                    }
                }

                started = true;
            }
        }

        public void Stop()
        {
            running = false; // Otherwise keepalive restarts them
            if (started)
            {
                foreach (EDDIResponder responder in responders)
                {
                    DisableResponder(responder.ResponderName());
                }
                foreach (EDDIMonitor monitor in monitors)
                {
                    DisableMonitor(monitor.MonitorName());
                }
            }

            SpeechService.Instance.ShutUp();
            started = false;
            Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " stopped");
        }

        /// <summary>
        /// Reload all monitors and responders
        /// </summary>
        public void Reload()
        {
            foreach (EDDIResponder responder in responders)
            {
                responder.Reload();
            }
            foreach (EDDIMonitor monitor in monitors)
            {
                monitor.Reload();
            }

            Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " reloaded");
        }

        /// <summary>
        /// Obtain a named monitor
        /// </summary>
        public EDDIMonitor ObtainMonitor(string invariantName)
        {
            foreach (EDDIMonitor monitor in monitors)
            {
                if (monitor.MonitorName().Equals(invariantName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return monitor;
                }
            }
            return null;
        }

        /// <summary> Obtain a named responder </summary>
        public EDDIResponder ObtainResponder(string invariantName)
        {
            foreach (EDDIResponder responder in responders)
            {
                if (responder.ResponderName().Equals(invariantName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return responder;
                }
            }
            return null;
        }

        /// <summary> Disable a named responder for this session.  This does not update the on-disk status of the responder </summary>
        public void DisableResponder(string invariantName)
        {
            EDDIResponder responder = ObtainResponder(invariantName);
            if (responder != null)
            {
                lock (responderLock)
                {
                    responder.Stop();
                    ConcurrentBag<EDDIResponder> newResponders = new ConcurrentBag<EDDIResponder>();
                    while (activeResponders.TryTake(out EDDIResponder item))
                    {
                        if (item != responder) { newResponders.Add(item); }
                    }
                    activeResponders = newResponders;
                }
            }
        }

        /// <summary> Enable a named responder for this session.  This does not update the on-disk status of the responder </summary>
        public void EnableResponder(string invariantName)
        {
            EDDIResponder responder = ObtainResponder(invariantName);
            if (responder != null)
            {
                if (!activeResponders.Contains(responder))
                {
                    responder.Start();
                    activeResponders.Add(responder);
                }
            }
        }

        /// <summary> Disable a named monitor for this session.  This does not update the on-disk status of the responder </summary>
        public void DisableMonitor(string invariantName)
        {
            EDDIMonitor monitor = ObtainMonitor(invariantName);
            if (monitor != null)
            {
                lock (monitorLock)
                {
                    monitor.Stop();
                    ConcurrentBag<EDDIMonitor> newMonitors = new ConcurrentBag<EDDIMonitor>();
                    while (activeMonitors.TryTake(out EDDIMonitor item))
                    {
                        if (item != monitor) { newMonitors.Add(item); }
                    }
                    activeMonitors = newMonitors;
                }
            }
        }

        /// <summary> Enable a named monitor for this session.  This does not update the on-disk status of the responder </summary>
        public void EnableMonitor(string invariantName)
        {
            EDDIMonitor monitor = ObtainMonitor(invariantName);
            if (monitor != null)
            {
                if (!activeMonitors.Contains(monitor))
                {
                    if (monitor.NeedsStart())
                    {
                        activeMonitors.Add(monitor);
                        Thread monitorThread = new Thread(() => keepAlive(monitor.MonitorName(), monitor.Start))
                        {
                            IsBackground = true
                        };
                        Logging.Info("Starting keepalive for " + monitor.MonitorName());
                        monitorThread.Start();
                    }
                }
            }
        }

        /// <summary> Reload a specific monitor or responder </summary>
        public void Reload(string name)
        {
            foreach (EDDIResponder responder in responders)
            {
                if (responder.ResponderName() == name)
                {
                    responder.Reload();
                    return;
                }
            }
            foreach (EDDIMonitor monitor in monitors)
            {
                if (monitor.MonitorName() == name)
                {
                    monitor.Reload();
                }
            }

            Logging.Info($"{Constants.EDDI_NAME} {Constants.EDDI_VERSION} module {name} reloaded");
        }

        /// <summary> Keep a monitor thread alive, restarting it as required </summary>
        private void keepAlive(string name, Action start)
        {
            try
            {
                int failureCount = 0;
                while (running && failureCount < 5 && activeMonitors.FirstOrDefault(m => m.MonitorName() == name) != null)
                {
                    try
                    {
                        Thread monitorThread = new Thread(() => start())
                        {
                            Name = name,
                            IsBackground = true
                        };
                        Logging.Info("Starting " + name + " (" + failureCount + ")");
                        monitorThread.Start();
                        monitorThread.Join();
                    }
                    catch (ThreadAbortException tax)
                    {
                        Thread.ResetAbort();
                        if (running)
                        {
                            Logging.Error("Restarting " + name + " after thread abort", tax);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (running)
                        {
                            Logging.Error("Restarting " + name + " after exception", ex);
                        }
                    }
                    failureCount++;
                }
                if (running)
                {
                    DisableMonitor(name);
                    Logging.Warn(name + " stopping after too many failures");
                }
            }
            catch (ThreadAbortException)
            {
                Logging.Debug("Thread aborted");
            }
            catch (Exception ex)
            {
                Logging.Warn("keepAlive for " + name + " failed", ex);
            }
        }

        public void enqueueEvent(Event @event)
        {
            eventQueue.Enqueue(@event);

            try
            {
                Thread eventHandler = new Thread(() => dequeueEvent())
                {
                    Name = "EventHandler",
                    IsBackground = true
                };
                eventHandler.Start();
                eventHandler.Join();
            }
            catch (ThreadAbortException tax)
            {
                Thread.ResetAbort();
                Logging.Debug("Thread aborted", tax);
            }
            catch (Exception ex)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    { "event", JsonConvert.SerializeObject(@event) },
                    { "exception", ex.Message },
                    { "stacktrace", ex.StackTrace }
                };
                Logging.Error("Failed to enqueue event", data);
            }
        }

        private void dequeueEvent()
        {
            if (eventQueue.TryDequeue(out Event @event))
            {
                eventHandler(@event);
            }
        }

        private void eventHandler(Event @event)
        {
            if (@event != null)
            {
                try
                {
                    Logging.Debug("Handling event " + JsonConvert.SerializeObject(@event));
                    // We have some additional processing to do for a number of events
                    bool passEvent = true;
                    if (@event is FileHeaderEvent)
                    {
                        passEvent = eventFileHeader((FileHeaderEvent)@event);
                    }
                    else if (@event is LocationEvent)
                    {
                        passEvent = eventLocation((LocationEvent)@event);
                    }
                    else if (@event is DockedEvent)
                    {
                        passEvent = eventDocked((DockedEvent)@event);
                    }
                    else if (@event is UndockedEvent)
                    {
                        passEvent = eventUndocked((UndockedEvent)@event);
                    }
                    else if (@event is DockingRequestedEvent dockingRequestedEvent)
                    {
                        passEvent = eventDockingRequested(dockingRequestedEvent);
                    }
                    else if (@event is TouchdownEvent)
                    {
                        passEvent = eventTouchdown((TouchdownEvent)@event);
                    }
                    else if (@event is LiftoffEvent)
                    {
                        passEvent = eventLiftoff((LiftoffEvent)@event);
                    }
                    else if (@event is FSDEngagedEvent)
                    {
                        passEvent = eventFSDEngaged((FSDEngagedEvent)@event);
                    }
                    else if (@event is FSDTargetEvent)
                    {
                        passEvent = eventFSDTarget((FSDTargetEvent)@event);
                    }
                    else if (@event is JumpedEvent)
                    {
                        passEvent = eventJumped((JumpedEvent)@event);
                    }
                    else if (@event is EnteredSupercruiseEvent)
                    {
                        passEvent = eventEnteredSupercruise((EnteredSupercruiseEvent)@event);
                    }
                    else if (@event is EnteredNormalSpaceEvent)
                    {
                        passEvent = eventEnteredNormalSpace((EnteredNormalSpaceEvent)@event);
                    }
                    else if (@event is CommanderLoadingEvent)
                    {
                        passEvent = eventCommanderLoading((CommanderLoadingEvent)@event);
                    }
                    else if (@event is CommanderContinuedEvent)
                    {
                        passEvent = eventCommanderContinued((CommanderContinuedEvent)@event);
                    }
                    else if (@event is CommanderRatingsEvent)
                    {
                        passEvent = eventCommanderRatings((CommanderRatingsEvent)@event);
                    }
                    else if (@event is CombatPromotionEvent)
                    {
                        passEvent = eventCombatPromotion((CombatPromotionEvent)@event);
                    }
                    else if (@event is TradePromotionEvent)
                    {
                        passEvent = eventTradePromotion((TradePromotionEvent)@event);
                    }
                    else if (@event is ExplorationPromotionEvent)
                    {
                        passEvent = eventExplorationPromotion((ExplorationPromotionEvent)@event);
                    }
                    else if (@event is FederationPromotionEvent)
                    {
                        passEvent = eventFederationPromotion((FederationPromotionEvent)@event);
                    }
                    else if (@event is EmpirePromotionEvent)
                    {
                        passEvent = eventEmpirePromotion((EmpirePromotionEvent)@event);
                    }
                    else if (@event is CrewJoinedEvent)
                    {
                        passEvent = eventCrewJoined((CrewJoinedEvent)@event);
                    }
                    else if (@event is CrewLeftEvent)
                    {
                        passEvent = eventCrewLeft((CrewLeftEvent)@event);
                    }
                    else if (@event is EnteredCQCEvent)
                    {
                        passEvent = eventEnteredCQC((EnteredCQCEvent)@event);
                    }
                    else if (@event is SRVLaunchedEvent)
                    {
                        passEvent = eventSRVLaunched((SRVLaunchedEvent)@event);
                    }
                    else if (@event is SRVDockedEvent)
                    {
                        passEvent = eventSRVDocked((SRVDockedEvent)@event);
                    }
                    else if (@event is FighterLaunchedEvent)
                    {
                        passEvent = eventFighterLaunched((FighterLaunchedEvent)@event);
                    }
                    else if (@event is FighterDockedEvent)
                    {
                        passEvent = eventFighterDocked((FighterDockedEvent)@event);
                    }
                    else if (@event is StarScannedEvent)
                    {
                        passEvent = eventStarScanned((StarScannedEvent)@event);
                    }
                    else if (@event is BodyScannedEvent)
                    {
                        passEvent = eventBodyScanned((BodyScannedEvent)@event);
                    }
                    else if (@event is BodyMappedEvent)
                    {
                        passEvent = eventBodyMapped((BodyMappedEvent)@event);
                    }
                    else if (@event is RingMappedEvent)
                    {
                        passEvent = eventRingMapped((RingMappedEvent)@event);
                    }
                    else if (@event is VehicleDestroyedEvent)
                    {
                        passEvent = eventVehicleDestroyed((VehicleDestroyedEvent)@event);
                    }
                    else if (@event is NearSurfaceEvent)
                    {
                        passEvent = eventNearSurface((NearSurfaceEvent)@event);
                    }
                    else if (@event is SquadronStatusEvent)
                    {
                        passEvent = eventSquadronStatus((SquadronStatusEvent)@event);
                    }
                    else if (@event is SquadronRankEvent)
                    {
                        passEvent = eventSquadronRank((SquadronRankEvent)@event);
                    }
                    else if (@event is FriendsEvent)
                    {
                        passEvent = eventFriends((FriendsEvent)@event);
                    }
                    else if (@event is MarketEvent)
                    {
                        passEvent = eventMarket((MarketEvent)@event);
                    }
                    else if (@event is OutfittingEvent)
                    {
                        passEvent = eventOutfitting((OutfittingEvent)@event);
                    }
                    else if (@event is ShipyardEvent)
                    {
                        passEvent = eventShipyard((ShipyardEvent)@event);
                    }
                    else if (@event is SettlementApproachedEvent)
                    {
                        passEvent = eventSettlementApproached((SettlementApproachedEvent)@event);
                    }
                    else if (@event is DiscoveryScanEvent)
                    {
                        passEvent = eventDiscoveryScan((DiscoveryScanEvent)@event);
                    }
                    else if (@event is SystemScanComplete)
                    {
                        passEvent = eventSystemScanComplete((SystemScanComplete)@event);
                    }
                    else if (@event is PowerplayEvent)
                    {
                        passEvent = eventPowerplay((PowerplayEvent)@event);
                    }
                    else if (@event is PowerDefectedEvent)
                    {
                        passEvent = eventPowerDefected((PowerDefectedEvent)@event);
                    }
                    else if (@event is PowerJoinedEvent)
                    {
                        passEvent = eventPowerJoined((PowerJoinedEvent)@event);
                    }
                    else if (@event is PowerLeftEvent)
                    {
                        passEvent = eventPowerLeft((PowerLeftEvent)@event);
                    }
                    else if (@event is PowerPreparationVoteCast)
                    {
                        passEvent = eventPowerPreparationVoteCast((PowerPreparationVoteCast)@event);
                    }
                    else if (@event is PowerSalaryClaimedEvent)
                    {
                        passEvent = eventPowerSalaryClaimed((PowerSalaryClaimedEvent)@event);
                    }
                    else if (@event is PowerVoucherReceivedEvent)
                    {
                        passEvent = eventPowerVoucherReceived((PowerVoucherReceivedEvent)@event);
                    }
                    else if (@event is CarrierJumpEngagedEvent)
                    {
                        passEvent = eventCarrierJumpEngaged((CarrierJumpEngagedEvent)@event);
                    }
                    else if (@event is CarrierJumpedEvent)
                    {
                        passEvent = eventCarrierJumped((CarrierJumpedEvent)@event);
                    }

                    // Additional processing is over, send to the event responders if required
                    if (passEvent)
                    {
                        OnEvent(@event);
                    }
                }
                catch (Exception ex)
                {
                    Dictionary<string, object> data = new Dictionary<string, object>
                    {
                        { "event", JsonConvert.SerializeObject(@event) },
                        { "exception", ex.Message },
                        { "stacktrace", ex.StackTrace }
                    };

                    Logging.Error("EDDI core failed to handle event " + @event.type, data);

                    // Even if an error occurs, we still need to pass the raw data 
                    // to the EDDN responder to maintain it's integrity.
                    Instance.ObtainResponder("EDDN responder").Handle(@event);
                }
            }
        }

        private bool eventCarrierJumpEngaged(CarrierJumpEngagedEvent @event)
        {
            // Only update our information if we are still docked at the carrier
            if (Environment == Constants.ENVIRONMENT_DOCKED && @event.carrierId == CurrentStation?.marketId)
            {
                // We are in witch space and in the ship.
                Environment = Constants.ENVIRONMENT_WITCH_SPACE;
                Vehicle = Constants.VEHICLE_SHIP;

                // Make sure we have at least basic information about the destination star system
                if (NextStarSystem is null)
                {
                    NextStarSystem = new StarSystem()
                    {
                        systemname = @event.systemname,
                        systemAddress = @event.systemAddress
                    };
                }

                // Remove the carrier from its prior location in the origin system so that we can re-save it with a new location
                CurrentStarSystem?.stations.RemoveAll(s => s.marketId == @event.carrierId);

                // Set the destination system as the current star system
                updateCurrentSystem(@event.systemname);

                // Update our station information
                CurrentStation = CurrentStarSystem.stations.FirstOrDefault(s => s.marketId == @event.carrierId) ?? new Station();
                CurrentStation.marketId = @event.carrierId;
                CurrentStation.systemname = @event.systemname;
                CurrentStation.systemAddress = @event.systemAddress;

                // Add the carrier to the destination system
                CurrentStarSystem.stations.Add(CurrentStation);

                // (When jumping near a body) Set the destination body as the current stellar body
                if (@event.bodyname != null)
                {
                    updateCurrentStellarBody(@event.bodyname, @event.systemname, @event.systemAddress);
                    CurrentStellarBody.bodyId = @event.bodyId;
                }
            }
            else if (!string.IsNullOrEmpty(@event.originSystemName))
            {
                // Remove the carrier from its prior location in the origin system so that we can re-save it with a new location
                StarSystem starSystem = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystem(@event.originSystemName);
                Station carrier = starSystem?.stations.FirstOrDefault(s => s.marketId == @event.carrierId);
                starSystem?.stations.RemoveAll(s => s.marketId == @event.carrierId);
                // Save the carrier to the updated star system
                if (carrier != null)
                {
                    carrier.systemname = @event.systemname;
                    carrier.systemAddress = @event.systemAddress;
                    if (@event.systemname == CurrentStarSystem?.systemname)
                    {
                        CurrentStarSystem?.stations.Add(carrier);
                        StarSystemSqLiteRepository.Instance.SaveStarSystem(starSystem);
                    }
                    else
                    {
                        StarSystem updatedStarSystem = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystem(@event.systemname);
                        if (updatedStarSystem != null)
                        {
                            updatedStarSystem.stations.Add(carrier);
                            StarSystemSqLiteRepository.Instance.SaveStarSystem(updatedStarSystem);
                        }
                    }
                }
            }
            return true;
        }

        private bool eventCarrierJumped(CarrierJumpedEvent @event)
        {
            if (@event.docked)
            {
                Logging.Info("Carrier jumped to: " + @event.systemname);

                // We are docked and in the ship
                Environment = Constants.ENVIRONMENT_DOCKED;
                Vehicle = Constants.VEHICLE_SHIP;

                // Remove the carrier from its prior location (of any) so that we can re-save it with a new location
                // If we haven't already updated our current star system, the carrier should be in `CurrentStarSystem`. If we have, it should be in `LastStarSystem`.
                if (CurrentStation?.marketId == @event.carrierId || CurrentStation?.name == @event.carriername)
                {
                    CurrentStarSystem?.stations.RemoveAll(s => s.marketId == @event.carrierId);
                }
                else
                {
                    CurrentStation = CurrentStarSystem.stations.FirstOrDefault(s => s.marketId == @event.carrierId || s.name == @event.carriername);
                    if (CurrentStation is null)
                    {
                        CurrentStation = LastStarSystem.stations.FirstOrDefault(s => s.marketId == @event.carrierId || s.name == @event.carriername);
                        if (CurrentStation != null)
                        {
                            LastStarSystem.stations.RemoveAll(s => s.marketId == @event.carrierId);
                            StarSystemSqLiteRepository.Instance.SaveStarSystem(LastStarSystem);
                        }
                    }
                }
                if (CurrentStation == null)
                {
                    // This carrier is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder.
                    CurrentStation = new Station();
                }

                // Update current station properties
                CurrentStation.name = @event.carriername;
                CurrentStation.marketId = @event.carrierId;
                CurrentStation.systemname = @event.systemname;
                CurrentStation.systemAddress = @event.systemAddress;
                CurrentStation.Faction = @event.carrierFaction;
                CurrentStation.LargestPad = LandingPadSize.Large; // Carriers always include large pads
                CurrentStation.Model = @event.carrierType;
                CurrentStation.economyShares = @event.carrierEconomies;
                CurrentStation.stationServices = @event.carrierServices;

                // Update our current star system and carrier location
                updateCurrentSystem(@event.systemname);

                // Update our system properties
                CurrentStarSystem.systemAddress = @event.systemAddress;
                CurrentStarSystem.x = @event.x;
                CurrentStarSystem.y = @event.y;
                CurrentStarSystem.z = @event.z;

                // Add our carrier to the new current star system
                CurrentStarSystem.stations.Add(CurrentStation);

                // Update the mutable system data from the journal
                if (@event.population != null)
                {
                    CurrentStarSystem.population = @event.population;
                    CurrentStarSystem.Economies = new List<Economy> { @event.systemEconomy, @event.systemEconomy2 };
                    CurrentStarSystem.securityLevel = @event.securityLevel;
                    CurrentStarSystem.Faction = @event.controllingsystemfaction;
                }

                // Update system faction data if available
                if (@event.factions != null)
                {
                    CurrentStarSystem.factions = @event.factions;

                    // Update station controlling faction data
                    foreach (Station station in CurrentStarSystem.stations)
                    {
                        Faction stationFaction = @event.factions.FirstOrDefault(f => f.name == station.Faction.name);
                        if (stationFaction != null)
                        {
                            station.Faction = stationFaction;
                        }
                    }

                    // Check if current system is inhabited by or HQ for squadron faction
                    Faction squadronFaction = @event.factions.FirstOrDefault(f =>
                    {
                        var squadronhomesystem = f.presences.
                            FirstOrDefault(p => p.systemName == CurrentStarSystem.systemname)?.squadronhomesystem;
                        return squadronhomesystem != null && ((bool)squadronhomesystem || f.squadronfaction);
                    });
                    if (squadronFaction != null)
                    {
                        updateSquadronData(squadronFaction, CurrentStarSystem.systemname);
                    }
                }

                // (When near a body) Update the body
                if (@event.bodyname != null && CurrentStellarBody?.bodyname != @event.bodyname)
                {
                    updateCurrentStellarBody(@event.bodyname, @event.systemname, @event.systemAddress);
                    CurrentStellarBody.bodyId = @event.bodyId;
                    CurrentStellarBody.bodyType = @event.bodyType;
                }

                // (When pledged) Powerplay information
                CurrentStarSystem.Power = @event.Power ?? CurrentStarSystem.Power;
                CurrentStarSystem.powerState = @event.powerState ?? CurrentStarSystem.powerState;

                // Update to most recent information
                CurrentStarSystem.visitLog.Add(@event.timestamp);
                CurrentStarSystem.updatedat = Dates.fromDateTimeToSeconds(@event.timestamp);
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);

                // Kick off the profile refresh if the companion API is available
                if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.Authorized)
                {
                    // Refresh station data
                    if (@event.fromLoad) { return true; } // Don't fire this event when loading pre-existing logs
                    profileUpdateNeeded = true;
                    profileStationRequired = CurrentStation.name;
                    Thread updateThread = new Thread(() =>
                    {
                        Thread.Sleep(5000);
                        conditionallyRefreshProfile();
                    })
                    {
                        IsBackground = true
                    };
                    updateThread.Start();
                }
            }
            else
            {
                // We shouldn't be here - `the CarrierJump` event is only supposed to be written when docked with a fleet carrier as it jumps.
                Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
                Logging.Error("Whoops! CarrierJump event recorded when not docked.", @event);
                throw new NotImplementedException();
            }
            return true;
        }

        private bool eventPowerVoucherReceived(PowerVoucherReceivedEvent @event)
        {
            Cmdr.Power = @event.Power;
            return true;
        }

        private bool eventPowerSalaryClaimed(PowerSalaryClaimedEvent @event)
        {
            Cmdr.Power = @event.Power;
            return true;
        }

        private bool eventPowerPreparationVoteCast(PowerPreparationVoteCast @event)
        {
            Cmdr.Power = @event.Power;
            return true;
        }

        private bool eventPowerLeft(PowerLeftEvent @event)
        {
            Cmdr.Power = Power.None;
            Cmdr.powermerits = null;
            Cmdr.powerrating = 0;

            // Store power merits
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            configuration.powerMerits = Cmdr.powermerits;
            configuration.ToFile();

            return true;
        }

        private bool eventPowerJoined(PowerJoinedEvent @event)
        {
            Cmdr.Power = @event.Power;
            Cmdr.powermerits = 0;
            Cmdr.powerrating = 1;

            // Store power merits
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            configuration.powerMerits = Cmdr.powermerits;
            configuration.ToFile();

            return true;
        }

        private bool eventPowerDefected(PowerDefectedEvent @event)
        {
            Cmdr.Power = @event.toPower;
            // Merits are halved upon defection
            Cmdr.powermerits = (int)Math.Round((double)Cmdr.powermerits / 2, 0);
            if (Cmdr.powermerits > 10000)
            {
                Cmdr.powerrating = 4;
            }
            if (Cmdr.powermerits > 1500)
            {
                Cmdr.powerrating = 3;
            }
            if (Cmdr.powermerits > 750)
            {
                Cmdr.powerrating = 2;
            }
            if (Cmdr.powermerits > 100)
            {
                Cmdr.powerrating = 1;
            }
            else
            {
                Cmdr.powerrating = 0;
            }

            // Store power merits
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            configuration.powerMerits = Cmdr.powermerits;
            configuration.ToFile();

            return true;
        }

        private bool eventPowerplay(PowerplayEvent @event)
        {
            if (Cmdr.powermerits is null)
            {
                // Per the journal, this is written at startup. In actuality, it can also be written whenever switching FSD states
                // and needs to be filtered to prevent redundant outputs.
                Cmdr.Power = @event.Power;
                Cmdr.powerrating = @event.rank;
                Cmdr.powermerits = @event.merits;

                // Store power merits
                EDDIConfiguration configuration = EDDIConfiguration.FromFile();
                configuration.powerMerits = @event.merits;
                configuration.ToFile();

                return true;
            }
            else
            {
                return false;
            }
        }

        private bool eventSystemScanComplete(SystemScanComplete @event)
        {
            // There is a bug in the player journal output (as of player journal v.25) that can cause the `SystemScanComplete` event to fire multiple times 
            // in rapid succession when performing a system scan of a star system with only stars and no other bodies.
            if (CurrentStarSystem != null)
            {
                if ((bool)CurrentStarSystem?.systemScanCompleted)
                {
                    // We will suppress repetitions of the event within the same star system.
                    return false;
                }
                CurrentStarSystem.systemScanCompleted = true;
            }
            return true;
        }

        private bool eventDiscoveryScan(DiscoveryScanEvent @event)
        {
            if (CurrentStarSystem != null)
            {
                CurrentStarSystem.totalbodies = @event.totalbodies;
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
            }
            return true;
        }

        private bool eventSettlementApproached(SettlementApproachedEvent @event)
        {
            Station station = CurrentStarSystem?.stations?.Find(s => s.name == @event.name);
            if (station == null && @event.systemAddress == CurrentStarSystem?.systemAddress)
            {
                // This settlement is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder
                station = new Station
                {
                    name = @event.name,
                    marketId = @event.marketId,
                    systemname = CurrentStarSystem.systemname
                };
                CurrentStarSystem?.stations?.Add(station);
            }
            return true;
        }

        private bool eventFriends(FriendsEvent @event)
        {
            bool passEvent = false;
            Friend cmdr = new Friend
            {
                name = @event.name,
                status = @event.status
            };

            /// Does this friend exist in our friends list?
            int index = Cmdr.friends.FindIndex(friend => friend.name == @event.name);
            if (index >= 0)
            {
                if (Cmdr.friends[index].status != @event.status)
                {
                    /// This is a known friend with a revised status: replace in situ (this is more efficient than removing and re-adding).
                    Cmdr.friends[index] = cmdr;
                    passEvent = true;
                }
            }
            else
            {
                /// This is a new friend, add them to the list
                Cmdr.friends.Add(cmdr);
            }
            return passEvent;
        }

        private async void OnEvent(Event @event)
        {
            // We send the event to all monitors to ensure that their info is up-to-date
            // All changes to state must be handled here, so this must be synchronous
            passToMonitorPreHandlers(@event);

            // Now we pass the data to the responders to process asynchronously, waiting for all to complete
            // Responders must not change global states.
            await passToRespondersAsync(@event);

            // We also pass the event to all active monitors in case they have asynchronous follow-on work, waiting for all to complete
            await passToMonitorPostHandlersAsync(@event);
        }

        private void passToMonitorPreHandlers(Event @event)
        {
            foreach (EDDIMonitor monitor in activeMonitors)
            {
                try
                {
                    monitor.PreHandle(@event);
                }
                catch (Exception ex)
                {
                    Dictionary<string, object> data = new Dictionary<string, object>
                    {
                        { "event", JsonConvert.SerializeObject(@event) },
                        { "exception", ex.Message },
                        { "stacktrace", ex.StackTrace }
                    };
                    Logging.Error(monitor.MonitorName() + " failed to handle event " + @event.type, data);
                }
            }
        }

        private async Task passToRespondersAsync(Event @event)
        {
            List<Task> responderTasks = new List<Task>();
            foreach (EDDIResponder responder in activeResponders)
            {
                try
                {
                    var responderTask = Task.Run(() =>
                    {
                        try
                        {
                            responder.Handle(@event);
                        }
                        catch (Exception ex)
                        {
                            Dictionary<string, object> data = new Dictionary<string, object>
                            {
                                { "event", JsonConvert.SerializeObject(@event) },
                                { "exception", ex.Message },
                                { "stacktrace", ex.StackTrace }
                            };

                            Logging.Error(responder.ResponderName() + " failed to handle event " + @event.type, data);
                        }
                    });
                    responderTasks.Add(responderTask);
                }
                catch (Exception ex)
                {
                    Dictionary<string, object> data = new Dictionary<string, object>
                    {
                        { "event", JsonConvert.SerializeObject(@event) },
                        { "exception", ex.Message },
                        { "stacktrace", ex.StackTrace }
                    };
                    Logging.Error(responder.ResponderName() + " failed to handle event " + @event.type, data);
                }
            }
            await Task.WhenAll(responderTasks.ToArray());
        }

        private async Task passToMonitorPostHandlersAsync(Event @event)
        {
            List<Task> monitorTasks = new List<Task>();
            foreach (EDDIMonitor monitor in activeMonitors)
            {
                try
                {
                    var monitorTask = Task.Run(() =>
                    {
                        try
                        {
                            monitor.PostHandle(@event);
                        }
                        catch (Exception ex)
                        {
                            Logging.Error(monitor.MonitorName() + " failed to post-handle event " + JsonConvert.SerializeObject(@event), ex);
                        }
                    });
                    monitorTasks.Add(monitorTask);
                }
                catch (ThreadAbortException tax)
                {
                    Thread.ResetAbort();
                    Logging.Debug("Thread aborted", tax);
                }
                catch (Exception ex)
                {
                    Dictionary<string, object> data = new Dictionary<string, object>
                    {
                        { "event", JsonConvert.SerializeObject(@event) },
                        { "exception", ex.Message },
                        { "stacktrace", ex.StackTrace }
                    };
                    Logging.Error(monitor.MonitorName() + " failed to post-handle event " + @event.type, data);
                }
                await Task.WhenAll(monitorTasks.ToArray());
            }
        }

        private bool eventLocation(LocationEvent theEvent)
        {
            Logging.Info("Location StarSystem: " + theEvent.systemname);

            updateCurrentSystem(theEvent.systemname);
            // Our data source may not include the system address
            CurrentStarSystem.systemAddress = theEvent.systemAddress;
            // Always update the current system with the current co-ordinates, just in case things have changed
            CurrentStarSystem.x = theEvent.x;
            CurrentStarSystem.y = theEvent.y;
            CurrentStarSystem.z = theEvent.z;

            // Update the mutable system data from the journal
            if (theEvent.population != null)
            {
                CurrentStarSystem.population = theEvent.population;
                CurrentStarSystem.Economies = new List<Economy> { theEvent.Economy, theEvent.Economy2 };
                CurrentStarSystem.securityLevel = theEvent.securityLevel;
                CurrentStarSystem.Faction = theEvent.controllingsystemfaction;
            }

            // Update system faction data if available
            if (theEvent.factions != null)
            {
                CurrentStarSystem.factions = theEvent.factions;

                // Update station controlling faction data
                foreach (Station station in CurrentStarSystem.stations)
                {
                    Faction stationFaction = theEvent.factions.FirstOrDefault(f => f.name == station.Faction.name);
                    if (stationFaction != null)
                    {
                        station.Faction = stationFaction;
                    }
                }

                // Check if current system is inhabited by or HQ for squadron faction
                Faction squadronFaction = theEvent.factions.FirstOrDefault(f => (bool)f.presences.
                    FirstOrDefault(p => p.systemName == CurrentStarSystem.systemname)?.squadronhomesystem || f.squadronfaction);
                if (squadronFaction != null)
                {
                    updateSquadronData(squadronFaction, CurrentStarSystem.systemname);
                }
            }

            // (When pledged) Powerplay information
            CurrentStarSystem.Power = theEvent.Power is null ? CurrentStarSystem.Power : theEvent.Power;
            CurrentStarSystem.powerState = theEvent.powerState is null ? CurrentStarSystem.powerState : theEvent.powerState;

            if (theEvent.docked || theEvent.bodytype.ToLowerInvariant() == "station")
            {
                // In this case body = station and our body information is invalid
                CurrentStellarBody = null;

                // Update the station
                string stationName = theEvent.docked ? theEvent.station : theEvent.bodyname;

                Logging.Debug("Now at station " + stationName);
                Station station = CurrentStarSystem.stations.Find(s => s.name == stationName);
                if (station == null)
                {
                    // This station is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder
                    station = new Station
                    {
                        name = stationName,
                        systemname = theEvent.systemname
                    };
                    CurrentStarSystem.stations.Add(station);
                }
                CurrentStation = station;

                if (theEvent.docked)
                {
                    // We are docked and in the ship
                    Environment = Constants.ENVIRONMENT_DOCKED;
                    Vehicle = Constants.VEHICLE_SHIP;

                    // Update station properties known from this event
                    station.marketId = theEvent.marketId;
                    station.systemAddress = theEvent.systemAddress;
                    station.Faction = theEvent.controllingstationfaction;
                    station.Model = theEvent.stationModel;
                    station.distancefromstar = theEvent.distancefromstar;

                    // Kick off the profile refresh if the companion API is available
                    if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.Authorized)
                    {
                        // Refresh station data
                        if (theEvent.fromLoad) { return true; } // Don't fire this event when loading pre-existing logs
                        profileUpdateNeeded = true;
                        profileStationRequired = CurrentStation.name;
                        Thread updateThread = new Thread(() => conditionallyRefreshProfile())
                        {
                            IsBackground = true
                        };
                        updateThread.Start();
                    }
                }
                else
                {
                    Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
                }
            }
            else if (theEvent.bodyname != null)
            {
                // If we are not at a station then our station information is invalid 
                CurrentStation = null;

                // Update the body 
                Logging.Debug("Now at body " + theEvent.bodyname);
                updateCurrentStellarBody(theEvent.bodyname, theEvent.systemname, theEvent.systemAddress);

                if (theEvent.latitude != null && theEvent.longitude != null)
                {
                    Environment = Constants.ENVIRONMENT_LANDED;
                }
                else
                {
                    Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
                }
            }
            else
            {
                // We are near neither a stellar body nor a station. 
                CurrentStellarBody = null;
                CurrentStation = null;
            }

            return true;
        }

        private bool eventDockingRequested(DockingRequestedEvent theEvent)
        {
            Station station = CurrentStarSystem.stations.Find(s => s.name == theEvent.station);
            if (station == null)
            {
                // This station is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder
                station = new Station
                {
                    name = theEvent.station,
                    marketId = theEvent.marketId,
                    systemname = CurrentStarSystem.systemname,
                    systemAddress = CurrentStarSystem.systemAddress
                };
                CurrentStarSystem.stations.Add(station);
            }
            station.Model = theEvent.stationDefinition;
            return true;
        }

        private bool eventDocked(DockedEvent theEvent)
        {
            bool passEvent = true;
            updateCurrentSystem(theEvent.system);

            // Upon docking, allow manual station updates once
            allowMarketUpdate = true;
            allowOutfittingUpdate = true;
            allowShipyardUpdate = true;

            Station station = CurrentStarSystem.stations.Find(s => s.name == theEvent.station);
            if (Environment == Constants.ENVIRONMENT_DOCKED && CurrentStation?.marketId == station?.marketId)
            {
                // We are already at this station
                Logging.Debug("Already at station " + theEvent.station);
                passEvent = false;
            }
            else
            {
                // Update the station
                Logging.Debug("Now at station " + theEvent.station);
                if (station == null)
                {
                    // This station is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder
                    station = new Station
                    {
                        name = theEvent.station,
                        systemname = theEvent.system
                    };
                    CurrentStarSystem.stations.Add(station);
                }
            }

            // We are docked and in the ship
            if (station != null)
            {
                Environment = Constants.ENVIRONMENT_DOCKED;
                Vehicle = Constants.VEHICLE_SHIP;

                // Not all stations in our database will have a system address or market id, so we set them here
                station.systemAddress = theEvent.systemAddress;
                station.marketId = theEvent.marketId;

                // Information from the event might be more current than our data source so use it in preference
                station.Faction = theEvent.controllingfaction;
                station.stationServices = theEvent.stationServices;
                station.economyShares = theEvent.economyShares;

                // Update other station information available from the event
                station.Model = theEvent.stationModel;
                station.stationServices = theEvent.stationServices;
                station.distancefromstar = theEvent.distancefromstar;

                CurrentStation = station;
                CurrentStellarBody = null;

                // Kick off the profile refresh if the companion API is available
                if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.Authorized)
                {
                    // Refresh station data
                    if (theEvent.fromLoad || !passEvent) { return true; } // Don't fire this event when loading pre-existing logs or if we were already at this station
                    profileUpdateNeeded = true;
                    profileStationRequired = CurrentStation.name;
                    Thread updateThread = new Thread(() => conditionallyRefreshProfile())
                    {
                        IsBackground = true
                    };
                    updateThread.Start();
                }
            }
            return passEvent;
        }

        private bool eventUndocked(UndockedEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
            Vehicle = Constants.VEHICLE_SHIP;

            // Call refreshProfile() to ensure that our ship is up-to-date
            refreshProfile();

            return true;
        }

        private bool eventTouchdown(TouchdownEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_LANDED;
            if (theEvent.playercontrolled)
            {
                Vehicle = Constants.VEHICLE_SHIP;
            }
            else
            {
                Vehicle = Constants.VEHICLE_SRV;
            }
            return true;
        }

        private bool eventLiftoff(LiftoffEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
            if (theEvent.playercontrolled)
            {
                Vehicle = Constants.VEHICLE_SHIP;
            }
            else
            {
                Vehicle = Constants.VEHICLE_SRV;
            }
            return true;
        }

        private bool eventMarket(MarketEvent theEvent)
        {
            // Don't proceed if we've already viewed the market while docked or when loading pre-existing logs
            if (!allowMarketUpdate || theEvent.fromLoad) { return false; }

            MarketInfoReader info = MarketInfoReader.FromFile();

            var quotes = new List<EddnCommodityMarketQuote>();
            foreach (MarketInfo item in info.Items)
            {
                try
                {
                    quotes.Add(new EddnCommodityMarketQuote(item));
                }
                catch (Exception e)
                {
                    Dictionary<string, object> data = new Dictionary<string, object>()
                    {
                        { "item", JsonConvert.SerializeObject(item) },
                        { "Exception", e }
                    };
                    Logging.Error($"Failed to handle market.json commodity item {item.name}", data);
                }
            }

            // Post an update event for new market data
            enqueueEvent(new MarketInformationUpdatedEvent(info.timestamp, theEvent.system, theEvent.station, theEvent.marketId, quotes, null, null, null, inHorizons));

            if (info.MarketID == theEvent.marketId
                && info.StarSystem == theEvent.system
                && info.StationName == theEvent.station)
            {
                if (info.Items.Count == quotes.Count) // We've successfully parsed all commodity quote items
                {
                    // Update the current station commodities
                    if (CurrentStation != null && CurrentStation?.marketId == theEvent.marketId)
                    {
                        allowMarketUpdate = false;
                        CurrentStation.commodities = quotes.Select(q => q.ToCommodityMarketQuote()).ToList();
                        CurrentStation.commoditiesupdatedat = Dates.fromDateTimeToSeconds(theEvent.timestamp);

                        // Update the current station information in our backend DB
                        Logging.Debug("Star system information updated from remote server; updating local copy");
                        StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                    }
                }

                return true;
            }
            return false;
        }

        private bool eventOutfitting(OutfittingEvent theEvent)
        {
            // Don't proceed if we've already viewed outfitting while docked or when loading pre-existing logs
            if (!allowOutfittingUpdate || theEvent.fromLoad) { return false; }

            OutfittingInfoReader info = OutfittingInfoReader.FromFile();
            if (info.Items != null && info.MarketID == theEvent.marketId
                && info.StarSystem == theEvent.system
                && info.StationName == theEvent.station
                && info.Horizons == Instance.inHorizons)
            {
                // Post an update event for new outfitting data
                enqueueEvent(new MarketInformationUpdatedEvent(info.timestamp, theEvent.system, theEvent.station, theEvent.marketId, null, null, info.Items.Select(i => i.name).ToList(), null, inHorizons));

                var modules = info.Items
                    .Select(i => EddiDataDefinitions.Module.FromOutfittingInfo(i))
                    .Where(i => i != null)
                    .ToList();
                if (info.Items.Count == modules.Count) // We've successfully parsed all module items
                {
                    // Update the current station outfitting
                    if (CurrentStation?.marketId != null && CurrentStation?.marketId == theEvent.marketId)
                    {
                        allowOutfittingUpdate = false;
                        CurrentStation.outfitting = modules;
                        CurrentStation.outfittingupdatedat = Dates.fromDateTimeToSeconds(info.timestamp);

                        // Update the current station information in our backend DB
                        Logging.Debug("Star system information updated from remote server; updating local copy");
                        StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                    }
                }
                return true;
            }
            return false;
        }

        private bool eventShipyard(ShipyardEvent theEvent)
        {
            // Don't proceed if we've already viewed outfitting while docked or when loading pre-existing logs
            if (!allowShipyardUpdate || theEvent.fromLoad) { return false; }

            ShipyardInfoReader info = ShipyardInfoReader.FromFile();
            if (info.PriceList != null && info.MarketID == theEvent.marketId
                && info.StarSystem == theEvent.system
                && info.StationName == theEvent.station
                && info.Horizons == Instance.inHorizons)
            {
                // Post an update event for new shipyard data
                enqueueEvent(new MarketInformationUpdatedEvent(info.timestamp, theEvent.system, theEvent.station, theEvent.marketId, null, null, null, info.PriceList.Select(s => s.shiptype).ToList(), inHorizons, info.AllowCobraMkIV));

                var ships = info.PriceList
                    .Select(s => Ship.FromShipyardInfo(s))
                    .Where(s => s != null)
                    .ToList();
                if (info.PriceList.Count == ships.Count) // We've successfully parsed all ship items
                {
                    // Update the current station shipyard
                    if (CurrentStation?.marketId != null && CurrentStation?.marketId == theEvent.marketId)
                    {
                        allowShipyardUpdate = false;
                        CurrentStation.shipyard = ships;
                        CurrentStation.shipyardupdatedat = Dates.fromDateTimeToSeconds(info.timestamp);

                        // Update the current station information in our backend DB
                        Logging.Debug("Star system information updated from remote server; updating local copy");
                        StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                    }
                }
                return true;
            }
            return false;
        }

        private void updateCurrentSystem(string name)
        {
            if (name == null || CurrentStarSystem?.systemname == name) { return; }

            // We have changed system so update the old one as to when we left
            StarSystemSqLiteRepository.Instance.LeaveStarSystem(CurrentStarSystem);

            LastStarSystem = CurrentStarSystem;
            if (NextStarSystem?.systemname == name)
            {
                CurrentStarSystem = NextStarSystem;
                NextStarSystem = null;
            }
            else
            {
                CurrentStarSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(name);
            }

            setSystemDistanceFromHome(CurrentStarSystem);
            setSystemDistanceFromDestination(CurrentStarSystem);
            setCommanderTitle();
        }

        private void updateCurrentStellarBody(string bodyName, string systemName, long? systemAddress = null)
        {
            // Make sure our system information is up to date
            if (CurrentStarSystem == null || CurrentStarSystem.systemname != systemName)
            {
                updateCurrentSystem(systemName);
            }
            // Update the body 
            if (CurrentStarSystem != null)
            {
                Body body = CurrentStarSystem.bodies?.Find(s => s.bodyname == bodyName);
                if (body == null)
                {
                    // We may be near a ring. For rings, we want to select the parent body
                    List<Body> ringedBodies = CurrentStarSystem.bodies?
                        .Where(b => b?.rings?.Count > 0).ToList();
                    foreach (Body ringedBody in ringedBodies)
                    {
                        Ring ring = ringedBody.rings.FirstOrDefault(r => r.name == bodyName);
                        if (ring != null)
                        {
                            body = ringedBody;
                            break;
                        }
                    }
                }
                if (body == null)
                {
                    // This body is unknown to us, might not be in EDDB or we might not have connectivity.  Use a placeholder 
                    body = new Body
                    {
                        bodyname = bodyName,
                        systemname = systemName,
                        systemAddress = systemAddress,
                    };
                }
                CurrentStellarBody = body;
            }
        }

        private bool eventFSDEngaged(FSDEngagedEvent @event)
        {
            // Keep track of our environment
            if (@event.target == "Supercruise")
            {
                Environment = Constants.ENVIRONMENT_SUPERCRUISE;
            }
            else
            {
                Environment = Constants.ENVIRONMENT_WITCH_SPACE;
            }

            // We are in the ship
            Vehicle = Constants.VEHICLE_SHIP;

            // Remove information about the current station and stellar body 
            CurrentStation = null;
            CurrentStellarBody = null;

            // Set the destination system as the current star system
            updateCurrentSystem(@event.system);

            // Save a copy of this event for later reference
            LastFSDEngagedEvent = @event;

            return true;
        }

        private bool eventFSDTarget(FSDTargetEvent @event)
        {
            // Set and prepare data about the next star system
            NextStarSystem = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystem(@event.system);
            return true;
        }

        private bool eventFileHeader(FileHeaderEvent @event)
        {
            // Test whether we're in beta by checking the filename, version described by the header, 
            // and certain version / build combinations
            gameIsBeta =
                (
                    @event.filename.Contains("Beta") ||
                    @event.version.Contains("Beta") ||
                    (
                        @event.version.Contains("2.2") &&
                        (
                            @event.build.Contains("r121645/r0") ||
                            @event.build.Contains("r129516/r0")
                        )
                    )
                );
            Logging.Info(gameIsBeta ? "On beta" : "On live");
            EliteConfiguration config = EliteConfiguration.FromFile();
            config.Beta = gameIsBeta;
            config.ToFile();

            return true;
        }

        private bool eventJumped(JumpedEvent theEvent)
        {
            bool passEvent;
            Logging.Info("Jumped to " + theEvent.system);
            if (CurrentStarSystem == null || CurrentStarSystem.systemname != theEvent.system)
            {
                // The 'StartJump' event must have been missed
                updateCurrentSystem(theEvent.system);
            }

            passEvent = true;
            CurrentStarSystem.systemAddress = theEvent.systemAddress;
            CurrentStarSystem.x = theEvent.x;
            CurrentStarSystem.y = theEvent.y;
            CurrentStarSystem.z = theEvent.z;
            CurrentStarSystem.Faction = theEvent.controllingfaction;
            CurrentStellarBody = CurrentStarSystem.bodies.Find(b => b.bodyname == theEvent.star)
                ?? CurrentStarSystem.bodies.Find(b => b.distance == 0);
            CurrentStarSystem.conflicts = theEvent.conflicts;

            // Update system faction data if available
            if (theEvent.factions != null)
            {
                CurrentStarSystem.factions = theEvent.factions;

                // Update station controlling faction data
                foreach (Station station in CurrentStarSystem.stations)
                {
                    Faction stationFaction = theEvent.factions.Find(f => f.name == station.Faction.name);
                    if (stationFaction != null)
                    {
                        station.Faction = stationFaction;
                    }
                }

                // Check if current system is inhabited by or HQ for squadron faction
                Faction squadronFaction = theEvent.factions.Find(f => (bool)f.presences.
                    Find(p => p.systemName == CurrentStarSystem.systemname)?.squadronhomesystem || f.squadronfaction);
                if (squadronFaction != null)
                {
                    updateSquadronData(squadronFaction, CurrentStarSystem.systemname);
                }
            }

            CurrentStarSystem.Economies = new List<Economy> { theEvent.Economy, theEvent.Economy2 };
            CurrentStarSystem.securityLevel = theEvent.securityLevel;
            if (theEvent.population != null)
            {
                CurrentStarSystem.population = theEvent.population;
            }

            // If we don't have any information about bodies in the system yet, create a basic star from current and saved event data
            if (CurrentStellarBody == null && !string.IsNullOrEmpty(theEvent.star))
            {
                CurrentStellarBody = new Body()
                {
                    bodyname = theEvent.star,
                    bodyType = BodyType.FromEDName("Star"),
                    stellarclass = LastFSDEngagedEvent?.stellarclass,
                };
                CurrentStarSystem.AddOrUpdateBody(CurrentStellarBody);
            }

            // (When pledged) Powerplay information
            CurrentStarSystem.Power = theEvent.Power is null ? CurrentStarSystem.Power : theEvent.Power;
            CurrentStarSystem.powerState = theEvent.powerState is null ? CurrentStarSystem.powerState : theEvent.powerState;

            // Update to most recent information
            CurrentStarSystem.visitLog.Add(theEvent.timestamp);
            CurrentStarSystem.updatedat = Dates.fromDateTimeToSeconds(theEvent.timestamp);
            StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);

            // After jump has completed we are always in supercruise
            Environment = Constants.ENVIRONMENT_SUPERCRUISE;

            // No longer in 'station instance'
            CurrentStation = null;

            return passEvent;
        }

        private bool eventEnteredSupercruise(EnteredSupercruiseEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_SUPERCRUISE;
            updateCurrentSystem(theEvent.system);

            // No longer in 'station instance'
            CurrentStation = null;

            // We are in the ship
            Vehicle = Constants.VEHICLE_SHIP;

            return true;
        }

        private bool eventEnteredNormalSpace(EnteredNormalSpaceEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_NORMAL_SPACE;

            if (theEvent.bodyType == BodyType.FromEDName("Station"))
            {
                // In this case body == station
                Station station = CurrentStarSystem.stations.Find(s => s.name == theEvent.bodyname);
                if (station == null)
                {
                    // This station is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder
                    station = new Station
                    {
                        name = theEvent.bodyname,
                        systemname = theEvent.systemname
                    };
                }
                CurrentStation = station;
            }
            else if (theEvent.bodyname != null)
            {
                updateCurrentStellarBody(theEvent.bodyname, theEvent.systemname, theEvent.systemAddress);
            }
            updateCurrentSystem(theEvent.systemname);
            return true;
        }

        private bool eventCrewJoined(CrewJoinedEvent theEvent)
        {
            inCrew = true;
            Logging.Info("Entering multicrew session");
            return true;
        }

        private bool eventCrewLeft(CrewLeftEvent theEvent)
        {
            inCrew = false;
            Logging.Info("Leaving multicrew session");
            return true;
        }

        private bool eventCommanderLoading(CommanderLoadingEvent theEvent)
        {
            // Set our commander name and ID
            if (Cmdr.name != theEvent.name)
            {
                Cmdr.name = theEvent.name;
                ObtainResponder("EDSM Responder").Reload();
            }
            Cmdr.EDID = theEvent.frontierID;
            return true;
        }

        private bool eventCommanderContinued(CommanderContinuedEvent theEvent)
        {
            // Set Vehicle state for commander from ship model
            if (theEvent.shipEDModel == "TestBuggy" || theEvent.shipEDModel == "SRV")
            {
                Vehicle = Constants.VEHICLE_SRV;
            }
            else
            {
                Vehicle = Constants.VEHICLE_SHIP;
            }

            // Set Environment state for the ship if 'startlanded' is present in the event
            if (theEvent.startlanded ?? false)
            {
                Environment = Constants.ENVIRONMENT_LANDED;
            }
            else if (theEvent.startlanded != null)
            {
                Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
            }

            // If we see this it means that we aren't in CQC
            inCQC = false;

            // Set our commander name and ID
            if (Cmdr.name != theEvent.commander)
            {
                Cmdr.name = theEvent.commander;
                ObtainResponder("EDSM Responder").Reload();
            }
            Cmdr.EDID = theEvent.frontierID;

            // Set game version
            inHorizons = theEvent.horizons;
            EliteConfiguration config = EliteConfiguration.FromFile();
            config.Horizons = inHorizons;
            config.ToFile();

            return true;
        }

        private bool eventCommanderRatings(CommanderRatingsEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null)
            {
                Cmdr.combatrating = theEvent.combat;
                Cmdr.traderating = theEvent.trade;
                Cmdr.explorationrating = theEvent.exploration;
                Cmdr.cqcrating = theEvent.cqc;
                Cmdr.empirerating = theEvent.empire;
                Cmdr.federationrating = theEvent.federation;
            }
            return true;
        }

        private bool eventCombatPromotion(CombatPromotionEvent theEvent)
        {
            // There is a bug with the journal where it reports superpower increases in rank as combat increases
            // Hence we check to see if this is a real event by comparing our known combat rating to the promoted rating
            if ((Cmdr == null || Cmdr.combatrating == null) || theEvent.rating != Cmdr.combatrating.localizedName)
            {
                // Real event. Capture commander ratings and add them to the commander object
                if (Cmdr != null)
                {
                    Cmdr.combatrating = CombatRating.FromName(theEvent.rating);
                }
                return true;
            }
            else
            {
                // False event
                return false;
            }
        }

        private bool eventTradePromotion(TradePromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null)
            {
                Cmdr.traderating = TradeRating.FromName(theEvent.rating);
            }
            return true;
        }

        private bool eventExplorationPromotion(ExplorationPromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null)
            {
                Cmdr.explorationrating = ExplorationRating.FromName(theEvent.rating);
            }
            return true;
        }

        private bool eventFederationPromotion(FederationPromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null)
            {
                Cmdr.federationrating = FederationRating.FromName(theEvent.rank);
            }
            return true;
        }

        private bool eventEmpirePromotion(EmpirePromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null)
            {
                Cmdr.empirerating = EmpireRating.FromName(theEvent.rank);
            }
            return true;
        }

        private bool eventSquadronStartup(SquadronStartupEvent theEvent)
        {
            SquadronRank rank = SquadronRank.FromRank(theEvent.rank + 1);

            // Update the configuration file
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            configuration.SquadronName = theEvent.name;
            configuration.SquadronRank = rank;
            configuration.ToFile();

            // Update the squadron UI data
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (Application.Current?.MainWindow != null)
                {
                    ((MainWindow)Application.Current.MainWindow).eddiSquadronNameText.Text = theEvent.name;
                    ((MainWindow)Application.Current.MainWindow).squadronRankDropDown.SelectedItem = rank.localizedName;
                }
            });

            // Update the commander object, if it exists
            if (Cmdr != null)
            {
                Cmdr.squadronname = theEvent.name;
                Cmdr.squadronrank = rank;
            }
            return true;
        }

        private bool eventSquadronStatus(SquadronStatusEvent theEvent)
        {
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();

            switch (theEvent.status)
            {
                case "created":
                    {
                        SquadronRank rank = SquadronRank.FromRank(1);

                        // Update the configuration file
                        configuration.SquadronName = theEvent.name;
                        configuration.SquadronRank = rank;

                        // Update the squadron UI data
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (Application.Current?.MainWindow != null)
                            {
                                ((MainWindow)Application.Current.MainWindow).eddiSquadronNameText.Text = theEvent.name;
                                ((MainWindow)Application.Current.MainWindow).squadronRankDropDown.SelectedItem = rank.localizedName;
                                configuration = ((MainWindow)Application.Current.MainWindow).resetSquadronRank(configuration);
                            }
                        });

                        // Update the commander object, if it exists
                        if (Cmdr != null)
                        {
                            Cmdr.squadronname = theEvent.name;
                            Cmdr.squadronrank = rank;
                        }
                        break;
                    }
                case "joined":
                    {
                        // Update the configuration file
                        configuration.SquadronName = theEvent.name;

                        // Update the squadron UI data
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (Application.Current?.MainWindow != null)
                            {
                                ((MainWindow)Application.Current.MainWindow).eddiSquadronNameText.Text = theEvent.name;
                            }
                        });

                        // Update the commander object, if it exists
                        if (Cmdr != null)
                        {
                            Cmdr.squadronname = theEvent.name;
                        }
                        break;
                    }
                case "disbanded":
                case "kicked":
                case "left":
                    {
                        // Update the configuration file
                        configuration.SquadronName = null;
                        configuration.SquadronID = null;

                        // Update the squadron UI data
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (Application.Current?.MainWindow != null)
                            {
                                ((MainWindow)Application.Current.MainWindow).eddiSquadronNameText.Text = string.Empty;
                                ((MainWindow)Application.Current.MainWindow).eddiSquadronIDText.Text = string.Empty;
                                configuration = ((MainWindow)Application.Current.MainWindow).resetSquadronRank(configuration);
                            }
                        });

                        // Update the commander object, if it exists
                        if (Cmdr != null)
                        {
                            Cmdr.squadronname = null;
                        }
                        break;
                    }
            }
            configuration.ToFile();
            return true;
        }

        private bool eventSquadronRank(SquadronRankEvent theEvent)
        {
            SquadronRank rank = SquadronRank.FromRank(theEvent.newrank + 1);

            // Update the configuration file
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            configuration.SquadronName = theEvent.name;
            configuration.SquadronRank = rank;
            configuration.ToFile();

            // Update the squadron UI data
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (Application.Current?.MainWindow != null)
                {
                    ((MainWindow)Application.Current.MainWindow).eddiSquadronNameText.Text = theEvent.name;
                    ((MainWindow)Application.Current.MainWindow).squadronRankDropDown.SelectedItem = rank.localizedName;
                }
            });

            // Update the commander object, if it exists
            if (Cmdr != null)
            {
                Cmdr.squadronname = theEvent.name;
                Cmdr.squadronrank = rank;
            }
            return true;
        }

        private bool eventEnteredCQC(EnteredCQCEvent theEvent)
        {
            // In CQC we don't want to report anything, so set our CQC flag
            inCQC = true;
            return true;
        }

        private bool eventSRVLaunched(SRVLaunchedEvent theEvent)
        {
            // SRV is always player-controlled, so we are in the SRV
            Vehicle = Constants.VEHICLE_SRV;
            return true;
        }

        private bool eventSRVDocked(SRVDockedEvent theEvent)
        {
            // We are back in the ship
            Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool eventFighterLaunched(FighterLaunchedEvent theEvent)
        {
            if (theEvent.playercontrolled)
            {
                // We are in the fighter
                Vehicle = Constants.VEHICLE_FIGHTER;
            }
            else
            {
                // We are (still) in the ship
                Vehicle = Constants.VEHICLE_SHIP;
            }
            return true;
        }

        private bool eventFighterDocked(FighterDockedEvent theEvent)
        {
            // We are back in the ship
            Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool eventVehicleDestroyed(VehicleDestroyedEvent theEvent)
        {
            // We are back in the ship
            Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool eventNearSurface(NearSurfaceEvent theEvent)
        {
            // We won't update CurrentStation with this event, as doing so triggers false / premature updates from the Frontier API
            CurrentStation = null;

            if (theEvent.approaching_surface)
            {
                // Update the body 
                Body body = CurrentStarSystem?.bodies?.Find(s => s.bodyname == theEvent.bodyname);
                if (body == null)
                {
                    // This body is unknown to us, might not be in our data source or we might not have connectivity.  Use a placeholder 
                    body = new Body
                    {
                        bodyname = theEvent.bodyname,
                        systemname = theEvent.systemname
                    };
                }
                // System address may not be included in our data source, so we add it here. 
                body.systemAddress = theEvent.systemAddress;
                CurrentStellarBody = body;
            }
            else
            {
                // Clear the body we are leaving 
                CurrentStellarBody = null;
            }
            updateCurrentSystem(theEvent.systemname);
            return true;
        }

        private bool eventStarScanned(StarScannedEvent theEvent)
        {
            // We just scanned a star.  We can only proceed if we know our current star system
            updateCurrentSystem(theEvent.star?.systemname);
            if (CurrentStarSystem == null) { return false; }

            Body star = CurrentStarSystem?.bodies?.Find(s => s.bodyname == theEvent.bodyname);
            if (star?.scanned is null)
            {
                CurrentStarSystem.AddOrUpdateBody(theEvent.star);
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                return true;
            }
            return false;
        }

        private bool eventBodyScanned(BodyScannedEvent theEvent)
        {
            // We just scanned a body.  We can only proceed if we know our current star system
            updateCurrentSystem(theEvent.body?.systemname);
            if (CurrentStarSystem == null) { return false; }

            // Add this body if it hasn't been previously added to our database, but don't
            // replace prior data which isn't re-obtainable from this event. 
            // (e.g. alreadydiscovered, scanned, alreadymapped, mapped, mappedEfficiently, etc.)
            Body body = CurrentStarSystem?.bodies?.Find(s => s.bodyname == theEvent.bodyname);
            if (body?.scanned is null)
            {
                CurrentStarSystem.AddOrUpdateBody(theEvent.body);

                Logging.Debug("Saving data for scanned body " + theEvent.bodyname);
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                return true;
            }
            return false;
        }

        private bool eventBodyMapped(BodyMappedEvent theEvent)
        {
            if (CurrentStarSystem != null && theEvent.systemAddress == CurrentStarSystem?.systemAddress)
            {
                // We've already updated the body (via the journal monitor) if the CurrentStarSystem isn't null
                // Here, we just need to save the data and update our current stellar body
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                updateCurrentStellarBody(theEvent.bodyName, CurrentStarSystem?.systemname, CurrentStarSystem?.systemAddress);
            }
            return true;
        }

        private bool eventRingMapped(RingMappedEvent theEvent)
        {
            if (CurrentStarSystem != null && theEvent.systemAddress == CurrentStarSystem?.systemAddress)
            {
                updateCurrentStellarBody(theEvent.ringname, CurrentStarSystem?.systemname, CurrentStarSystem?.systemAddress);
            }
            return true;
        }

        /// <summary>Obtain information from the companion API and use it to refresh our own data</summary>
        public bool refreshProfile(bool refreshStation = false)
        {
            bool success = true;
            if (CompanionAppService.Instance?.CurrentState == CompanionAppService.State.Authorized)
            {
                try
                {
                    Profile profile = CompanionAppService.Instance.Profile();
                    if (profile != null)
                    {
                        // Update our commander object
                        Cmdr = Commander.FromFrontierApiCmdr(Cmdr, profile.Cmdr, profile.timestamp, JournalTimeStamp, out bool cmdrMatches);

                        // Stop if the commander returned from the profile does not match our expected commander name
                        if (!cmdrMatches) { return false; }

                        bool updatedCurrentStarSystem = false;

                        // Only set the current star system if it is not present, otherwise we leave it to events
                        if (CurrentStarSystem == null)
                        {
                            updateCurrentSystem(profile.CurrentStarSystem.systemName);
                            setSystemDistanceFromHome(CurrentStarSystem);
                            setSystemDistanceFromDestination(CurrentStarSystem);
                            setCommanderTitle();

                            if (profile.docked && profile.CurrentStarSystem?.systemName == CurrentStarSystem.systemname && CurrentStarSystem.stations != null)
                            {
                                CurrentStation = CurrentStarSystem.stations.FirstOrDefault(s => s.name == profile.LastStation.name);
                                if (CurrentStation != null)
                                {
                                    // Only set the current station if it is not present, otherwise we leave it to events
                                    Logging.Debug("Set current station to " + CurrentStation.name);
                                    CurrentStation.updatedat = Dates.fromDateTimeToSeconds(DateTime.UtcNow);
                                    updatedCurrentStarSystem = true;
                                }
                            }
                        }

                        if (refreshStation && CurrentStation != null && Environment == Constants.ENVIRONMENT_DOCKED)
                        {
                            // Refresh station data
                            profileUpdateNeeded = true;
                            profileStationRequired = CurrentStation.name;
                            Thread updateThread = new Thread(() => conditionallyRefreshProfile())
                            {
                                IsBackground = true
                            };
                            updateThread.Start();
                        }

                        if (updatedCurrentStarSystem)
                        {
                            Logging.Debug("Star system information updated from remote server; updating local copy");
                            StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                        }

                        foreach (EDDIMonitor monitor in activeMonitors)
                        {
                            try
                            {
                                Thread monitorThread = new Thread(() =>
                                {
                                    try
                                    {
                                        monitor.HandleProfile(profile.json);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logging.Warn("Monitor failed", ex);
                                    }
                                })
                                {
                                    Name = monitor.MonitorName(),
                                    IsBackground = true
                                };
                                monitorThread.Start();
                            }
                            catch (ThreadAbortException tax)
                            {
                                Thread.ResetAbort();
                                Logging.Debug("Thread aborted", tax);
                                success = false;
                            }
                            catch (Exception ex)
                            {
                                Dictionary<string, object> data = new Dictionary<string, object>
                                {
                                    { "message", ex.Message },
                                    { "stacktrace", ex.StackTrace },
                                    { "profile", JsonConvert.SerializeObject(profile) }
                                };
                                Logging.Error("Monitor " + monitor.MonitorName() + " failed to handle profile.", data);
                                success = false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Error("Exception obtaining profile", ex);
                    success = false;
                }
            }
            return success;
        }

        private void setSystemDistanceFromHome(StarSystem system)
        {
            if (HomeStarSystem is null) { return; }
            system.distancefromhome = getSystemDistance(system, HomeStarSystem);
            Logging.Debug("Distance from home is " + system.distancefromhome);
        }

        public void setSystemDistanceFromDestination(StarSystem system)
        {
            if (DestinationStarSystem is null) { return; }
            DestinationDistanceLy = getSystemDistance(system, DestinationStarSystem);
            Logging.Debug("Distance from destination system is " + DestinationDistanceLy);
        }

        public decimal getSystemDistance(StarSystem curr, StarSystem dest)
        {
            return curr?.DistanceFromStarSystem(dest) ?? 0;
        }

        /// <summary>Work out the title for the commander in the current system</summary>
        private const int minEmpireRankForTitle = 3;
        private const int minFederationRankForTitle = 1;
        private void setCommanderTitle()
        {
            if (Cmdr != null)
            {
                Cmdr.title = Eddi.Properties.EddiResources.Commander;
                if (CurrentStarSystem != null)
                {
                    if (CurrentStarSystem.Faction?.Allegiance?.invariantName == "Federation" && Cmdr.federationrating != null && Cmdr.federationrating.rank > minFederationRankForTitle)
                    {
                        Cmdr.title = Cmdr.federationrating.localizedName;
                    }
                    else if (CurrentStarSystem.Faction?.Allegiance?.invariantName == "Empire" && Cmdr.empirerating != null && Cmdr.empirerating.rank > minEmpireRankForTitle)
                    {
                        Cmdr.title = Cmdr.empirerating.maleRank.localizedName;
                    }
                }
            }
        }

        /// <summary>
        /// Find all monitors
        /// </summary>
        public List<EDDIMonitor> findMonitors()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(path))
            {
                Logging.Warn("Unable to start EDDI Monitors, application directory path not found.");
                return null;
            }

            DirectoryInfo dir = new DirectoryInfo(path);
            List<EDDIMonitor> monitors = new List<EDDIMonitor>();
            Type pluginType = typeof(EDDIMonitor);
            foreach (FileInfo file in dir.GetFiles("*Monitor.dll", SearchOption.AllDirectories))
            {
                Logging.Debug("Checking potential plugin at " + file.FullName);
                try
                {
                    Assembly assembly = Assembly.LoadFrom(file.FullName);
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsInterface || type.IsAbstract)
                        {
                            continue;
                        }
                        else
                        {
                            if (type.GetInterface(pluginType.FullName) != null)
                            {
                                try
                                {
                                    Logging.Debug("Instantiating monitor plugin at " + file.FullName);
                                    EDDIMonitor monitor = type.InvokeMember(null,
                                                               BindingFlags.CreateInstance,
                                                               null, null, null) as EDDIMonitor;
                                    monitors.Add(monitor);
                                }
                                catch (TargetInvocationException)
                                {
                                    Logging.Warn($"Error loading {file.Name}. Failed to load {type.Name} from {type.Assembly}.");
                                }
                            }
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    // Ignore this; probably due to CPU architecture mismatch
                }
                catch (ReflectionTypeLoadException ex)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (Exception exSub in ex.LoaderExceptions)
                    {
                        sb.AppendLine(exSub.Message);
                        if (exSub is FileNotFoundException exFileNotFound)
                        {
                            if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                            {
                                sb.AppendLine("Fusion Log:");
                                sb.AppendLine(exFileNotFound.FusionLog);
                            }
                        }
                        sb.AppendLine();
                    }
                    Logging.Warn("Failed to instantiate plugin at " + file.FullName + ":\n" + sb.ToString());
                }
                catch (FileLoadException flex)
                {
                    string msg = string.Format(Eddi.Properties.EddiResources.problem_load_monitor_file, dir.FullName);
                    Logging.Error(msg, flex);
                    SpeechService.Instance.Say(null, msg, 0);
                }
                catch (Exception ex)
                {
                    string msg = string.Format(Eddi.Properties.EddiResources.problem_load_monitor, $"{file.Name}.\n{ex.Message} {ex.InnerException?.Message ?? ""}");
                    Logging.Error(msg, ex);
                    SpeechService.Instance.Say(null, msg, 0);
                }
            }
            return monitors;
        }

        /// <summary>
        /// Find all responders
        /// </summary>
        public List<EDDIResponder> findResponders()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(path))
            {
                Logging.Warn("Unable to start EDDI Responders, application directory path not found.");
                return null;
            }
            DirectoryInfo dir = new DirectoryInfo(path);
            List<EDDIResponder> responders = new List<EDDIResponder>();
            Type pluginType = typeof(EDDIResponder);
            foreach (FileInfo file in dir.GetFiles("*Responder.dll", SearchOption.AllDirectories))
            {
                Logging.Debug("Checking potential plugin at " + file.FullName);
                try
                {
                    Assembly assembly = Assembly.LoadFrom(file.FullName);
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsInterface || type.IsAbstract)
                        {
                            continue;
                        }
                        else
                        {
                            if (type.GetInterface(pluginType.FullName) != null)
                            {
                                Logging.Debug("Instantiating responder plugin at " + file.FullName);
                                EDDIResponder responder = type.InvokeMember(null,
                                                           BindingFlags.CreateInstance,
                                                           null, null, null) as EDDIResponder;
                                responders.Add(responder);
                            }
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    // Ignore this; probably due to CPU architecure mismatch
                }
                catch (ReflectionTypeLoadException ex)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (Exception exSub in ex.LoaderExceptions)
                    {
                        sb.AppendLine(exSub.Message);
                        if (exSub is FileNotFoundException exFileNotFound)
                        {
                            if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                            {
                                sb.AppendLine("Fusion Log:");
                                sb.AppendLine(exFileNotFound.FusionLog);
                            }
                        }
                        sb.AppendLine();
                    }
                    Logging.Warn("Failed to instantiate plugin at " + file.FullName + ":\n" + sb.ToString());
                }
            }
            return responders;
        }

        private bool profileUpdateNeeded = false;
        private string profileStationRequired = null;

        /// <summary>
        /// Update the profile when requested, ensuring that we meet the condition in the updated profile
        /// </summary>
        private void conditionallyRefreshProfile()
        {
            int maxTries = 6;

            while (running && maxTries > 0 && CompanionAppService.Instance.CurrentState == CompanionAppService.State.Authorized)
            {
                try
                {
                    Logging.Debug("Starting conditional profile fetch");

                    // See if we need to fetch the profile
                    if (profileUpdateNeeded)
                    {
                        // See if we still need this particular update
                        if (profileStationRequired != null && (CurrentStation?.name != profileStationRequired))
                        {
                            Logging.Debug("No longer at requested station; giving up on update");
                            profileUpdateNeeded = false;
                            profileStationRequired = null;
                            break;
                        }

                        // Make sure we know where we are
                        if (CurrentStarSystem.systemname.Length < 0)
                        {
                            break;
                        }

                        // We do need to fetch an updated profile; do so
                        Profile profile = CompanionAppService.Instance?.Profile();
                        if (profile != null)
                        {
                            // Sanity check
                            if (profile.Cmdr.name != Cmdr.name)
                            {
                                Logging.Warn("Frontier API incorrectly configured: Returning information for Commander " +
                                    $"'{profile.Cmdr.name}' rather than for '{Cmdr.name}'. Disregarding incorrect information.");
                                return;
                            }
                            else if (profile.CurrentStarSystem.systemName != CurrentStarSystem.systemname)
                            {
                                Logging.Warn("Frontier API incorrectly configured: Returning information for Star System " +
                                    $"'{profile.CurrentStarSystem.systemName}' rather than for '{CurrentStarSystem.systemname}'. Disregarding incorrect information.");
                                return;
                            }

                            // If we're docked, the lastStation information is located within the lastSystem identified by the profile
                            if (profile.docked && Environment == Constants.ENVIRONMENT_DOCKED)
                            {
                                Logging.Debug("Fetching station profile");
                                Profile stationProfile = CompanionAppService.Instance.Station(CurrentStarSystem.systemAddress, CurrentStarSystem.systemname);

                                // Post an update event
                                Event @event = new MarketInformationUpdatedEvent(profile.timestamp, stationProfile.CurrentStarSystem.systemName, stationProfile.LastStation.name, stationProfile.LastStation.marketId, stationProfile.LastStation.eddnCommodityMarketQuotes, stationProfile.LastStation.prohibitedCommodities?.Select(p => p.Value).ToList(), stationProfile.LastStation.outfitting?.Select(m => m.edName).ToList(), stationProfile.LastStation.ships?.Select(s => s.edName).ToList(), profile.contexts.inHorizons, profile.contexts.allowCobraMkIV);
                                enqueueEvent(@event);

                                // See if we need to update our current station
                                Logging.Debug("profileStationRequired is " + profileStationRequired + ", profile station is " + stationProfile.LastStation.name);

                                if (profileStationRequired != null && profileStationRequired == stationProfile.LastStation.name)
                                {
                                    // We have the required station information
                                    Logging.Debug("Current station matches profile information; updating info");
                                    Station station = CurrentStarSystem.stations.Find(s => s.name == stationProfile.LastStation.name);
                                    station = stationProfile.LastStation.UpdateStation(stationProfile.timestamp, station);

                                    // Update the current station information in our backend DB
                                    Logging.Debug("Star system information updated from Frontier API server; updating local copy");
                                    CurrentStation = station;
                                    StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);

                                    profileUpdateNeeded = false;
                                    allowMarketUpdate = false;
                                    allowOutfittingUpdate = false;
                                    allowShipyardUpdate = false;

                                    break;
                                }
                            }
                        }

                        // No luck; sleep and try again
                        Thread.Sleep(15000);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Error("Exception obtaining profile", ex);
                }
                finally
                {
                    maxTries--;
                }
            }

            if (maxTries == 0)
            {
                Logging.Info("Maximum attempts reached; giving up on request");
            }

            // Clear the update info
            profileUpdateNeeded = false;
            profileStationRequired = null;
        }

        internal static class NativeMethods
        {
            // Required to restart app after upgrade
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            internal static extern uint RegisterApplicationRestart(string pwzCommandLine, RestartFlags dwFlags);
        }

        // Flags for upgrade
        [Flags]
        internal enum RestartFlags
        {
            NONE = 0,
            RESTART_CYCLICAL = 1,
            RESTART_NOTIFY_SOLUTION = 2,
            RESTART_NOTIFY_FAULT = 4,
            RESTART_NO_CRASH = 8,
            RESTART_NO_HANG = 16,
            RESTART_NO_PATCH = 32,
            RESTART_NO_REBOOT = 64
        }

        public void updateDestinationSystemStation(EDDIConfiguration configuration)
        {
            updateDestinationSystem(configuration.DestinationSystem);
            updateDestinationStation(configuration.DestinationStation);
        }

        public void updateDestinationSystem(string destinationSystem)
        {
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            if (destinationSystem != null)
            {
                StarSystem system = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystem(destinationSystem);

                //Ignore null & empty systems
                if (system != null)
                {
                    if (system.systemname != DestinationStarSystem?.systemname)
                    {
                        Logging.Debug("Destination star system is " + system.systemname);
                        DestinationStarSystem = system;
                    }
                }
                else { destinationSystem = null; }
            }
            else
            {
                DestinationStarSystem = null;
            }
            configuration.DestinationSystem = destinationSystem;
            configuration.ToFile();
        }

        public void updateDestinationStation(string destinationStation)
        {
            EDDIConfiguration configuration = EDDIConfiguration.FromFile();
            if (destinationStation != null && DestinationStarSystem?.stations != null)
            {
                string destinationStationName = destinationStation.Trim();
                Station station = DestinationStarSystem.stations.FirstOrDefault(s => s.name == destinationStationName);
                if (station != null)
                {
                    if (station.name != DestinationStation?.name)
                    {
                        Logging.Debug("Destination station is " + station.name);
                        DestinationStation = station;
                    }
                }
            }
            else
            {
                DestinationStation = null;
            }
            configuration.DestinationStation = destinationStation;
            configuration.ToFile();
        }

        public void updateHomeSystemStation(EDDIConfiguration configuration)
        {
            updateHomeSystem(configuration);
            updateHomeStation(configuration);
            configuration.ToFile();
        }

        public EDDIConfiguration updateHomeSystem(EDDIConfiguration configuration)
        {
            Logging.Verbose = configuration.Debug;
            if (configuration.HomeSystem != null)
            {
                StarSystem system = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystem(configuration.HomeSystem);

                //Ignore null & empty systems
                if (system != null && system.bodies?.Count > 0)
                {
                    if (system.systemname != HomeStarSystem?.systemname)
                    {
                        HomeStarSystem = system;
                        Logging.Debug("Home star system is " + HomeStarSystem.systemname);
                        configuration.HomeSystem = system.systemname;
                    }
                }
            }
            else
            {
                HomeStarSystem = null;
            }
            return configuration;
        }

        public EDDIConfiguration updateHomeStation(EDDIConfiguration configuration)
        {
            Logging.Verbose = configuration.Debug;
            if (HomeStarSystem?.stations != null && configuration.HomeStation != null)
            {
                string homeStationName = configuration.HomeStation.Trim();
                foreach (Station station in HomeStarSystem.stations)
                {
                    if (station.name == homeStationName)
                    {
                        HomeStation = station;
                        Logging.Debug("Home station is " + HomeStation.name);
                        break;
                    }
                }
            }
            return configuration;
        }

        public EDDIConfiguration updateSquadronSystem(EDDIConfiguration configuration)
        {
            Logging.Verbose = configuration.Debug;
            if (configuration.SquadronSystem != null)
            {
                StarSystem system = StarSystemSqLiteRepository.Instance.GetOrFetchStarSystem(configuration.SquadronSystem.Trim());

                //Ignore null & empty systems
                if (system != null && system?.bodies.Count > 0)
                {
                    if (system.systemname != SquadronStarSystem?.systemname)
                    {
                        SquadronStarSystem = system;
                        if (SquadronStarSystem?.factions != null)
                        {
                            Logging.Debug("Squadron star system is " + SquadronStarSystem.systemname);
                            configuration.SquadronSystem = system.systemname;
                        }
                    }
                }
            }
            else
            {
                SquadronStarSystem = null;
            }
            return configuration;
        }

        public void updateSquadronData(Faction faction, string systemName)
        {
            if (faction != null)
            {
                EDDIConfiguration configuration = EDDIConfiguration.FromFile();

                //Update the squadron faction, if changed
                if (configuration.SquadronFaction == null || configuration.SquadronFaction != faction.name)
                {
                    configuration.SquadronFaction = faction.name;

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (Application.Current?.MainWindow != null)
                        {
                            ((MainWindow)Application.Current.MainWindow).squadronFactionDropDown.SelectedItem = faction.name;
                        }
                    });

                    Cmdr.squadronfaction = faction.name;
                }

                // Update system, allegiance, & power when in squadron home system
                if ((bool)faction.presences.FirstOrDefault(p => p.systemName == systemName)?.squadronhomesystem)
                {
                    // Update the squadron system data, if changed
                    string system = CurrentStarSystem.systemname;
                    if (configuration.SquadronSystem == null || configuration.SquadronSystem != system)
                    {
                        configuration.SquadronSystem = system;

                        var configurationCopy = configuration;
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (Application.Current?.MainWindow != null)
                            {
                                ((MainWindow)Application.Current.MainWindow).squadronSystemDropDown.Text = system;
                                ((MainWindow)Application.Current.MainWindow).ConfigureSquadronFactionOptions(configurationCopy);
                            }
                        });

                        configuration = updateSquadronSystem(configuration);
                    }

                    //Update the squadron allegiance, if changed
                    Superpower allegiance = CurrentStarSystem?.Faction?.Allegiance ?? Superpower.None;

                    //Prioritize UI entry if squadron system allegiance not specified
                    if (allegiance != Superpower.None)
                    {
                        if (configuration.SquadronAllegiance == Superpower.None || configuration.SquadronAllegiance != allegiance)
                        {
                            configuration.SquadronAllegiance = allegiance;
                            Cmdr.squadronallegiance = allegiance;
                        }
                    }

                    // Update the squadron power, if changed
                    Power power = Power.FromName(CurrentStarSystem?.power) ?? Power.None;

                    //Prioritize UI entry if squadron system power not specified
                    if (power != Power.None)
                    {
                        if (configuration.SquadronPower == Power.None && configuration.SquadronPower != power)
                        {
                            configuration.SquadronPower = power;

                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                if (Application.Current?.MainWindow != null)
                                {
                                    ((MainWindow)Application.Current.MainWindow).squadronPowerDropDown.SelectedItem = power.localizedName;
                                    ((MainWindow)Application.Current.MainWindow).ConfigureSquadronPowerOptions(configuration);
                                }
                            });
                        }
                    }
                }
                configuration.ToFile();
            }
        }
    }
}

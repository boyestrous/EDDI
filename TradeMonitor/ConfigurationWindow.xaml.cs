using EddiConfigService;
using EddiCore;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiEvents;
using EddiJournalMonitor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EddiTradeMonitor
{
    /// <summary>
    /// Interaction logic for ConfigurationWindow.xaml
    /// </summary>
    public partial class ConfigurationWindow : UserControl
    {
        public ConfigurationWindow()
        {
            InitializeComponent();
        }

        #region userActionFunctions
        public void tradeMaxStationDistanceChanged(object sender, TextChangedEventArgs e)
        {
            TradeMonitorConfiguration tradeMonitorConfiguration = ConfigService.Instance.tradeMonitorConfiguration;
            if (Int32.TryParse(tradeMaxStationDistance.Text, out int outVal))
            {
                tradeMonitorConfiguration.maxSearchDistanceFromStarLs = outVal;
            }
            else
            {
                tradeMaxStationDistance.Text = "Must be Number";
            }

        }
        public void tradeMaxStationDistance_LostFocus(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrWhiteSpace(tradeMaxStationDistance.Text))
            {
                //if (ConfigService.Instance.tradeMonitorConfiguration.maxSearchDistanceFromStarLs != 0)
                // Unless explicitly set, default to whatever the NavigationMonitor uses for Max Station Distance
                //NavigationMonitorConfiguration navMonitorConfig = ConfigService.Instance.navigationMonitorConfiguration;
                //int navMaxDistance = navMonitorConfig.maxSearchDistanceFromStarLs ?? 10000;              

                TradeMonitorConfiguration tradeMonitorConfiguration = ConfigService.Instance.tradeMonitorConfiguration;
                tradeMonitorConfiguration.maxSearchDistanceFromStarLs = 9999;                
            }
        }
        #endregion

        #region simulatorButtons

        /// <summary>
        /// Simulate a Docked Event
        /// </summary>
        public async void tradeSimulateDockedClicked(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Simulating Docked event");
            string line = @"{ ""timestamp"":""2017-04-14T19:34:32Z"", ""event"":""Docked"", ""StationName"":""Freeholm"", ""StationType"":""AsteroidBase"", ""StarSystem"":""Artemis"", ""StationFaction"":{ ""Name"":""Artemis Empire Assembly"", ""FactionState"":""Boom"" }, ""StationGovernment"":""$government_Patronage;"", ""StationGovernment_Localised"":""Patronage"", ""StationAllegiance"":""Empire"", ""StationEconomy"":""$economy_Industrial;"", ""StationEconomy_Localised"":""Industrial"", ""StationEconomies"": [ { ""Name"": ""$economy_Industrial;"", ""Proportion"": 0.7 }, { ""Name"": ""$economy_Extraction;"", ""Proportion"": 0.3 } ], ""DistFromStarLS"":2527.211914, ""StationServices"":[""Refuel""], ""MarketID"": 128169720, ""SystemAddress"": 3107509474002 }";
            List<Event> events = JournalMonitor.ParseJournalEntry(line);

            DockedEvent theEvent = (DockedEvent)events[0];
            EDDI.Instance.enqueueEvent(theEvent);

        }

        public async void tradeSimulateRouteClicked(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Simulating NavRoute event");
            NavRouteEvent theNavEvent = new NavRouteEvent(DateTime.UtcNow, new List<NavRouteInfoItem>()
            {
                new NavRouteInfoItem("LTT 2667", 633742561994, new List<decimal>() { 66.40625M, -123.37500M, 51.90625M }, "M"),
                new NavRouteInfoItem("Cemiess", 1522322606443, new List<decimal>() { 66.06250M, -105.34375M, 27.09375M }, "G"),
                new NavRouteInfoItem("LHS 4031", 1733254091490, new List<decimal>() {67.56250M, -134.12500M, 66.84375M }, "K")
            });
            EDDI.Instance.enqueueEvent(theNavEvent);

        }

        public async void tradeSimulateJumpClicked(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Simulating Jump event");
            string line = @"{
            ""timestamp"": ""2018-08-08T06: 56: 20Z"",
                ""event"": ""FSDJump"",
                ""StarSystem"": ""Diaguandri"",
                ""SystemAddress"": 670417429889,
                ""StarPos"": [-41.06250, -62.15625, -103.25000],
                ""SystemAllegiance"": ""Independent"",
                ""SystemEconomy"": ""$economy_HighTech;"",
                ""SystemEconomy_Localised"": ""HighTech"",
                ""SystemSecondEconomy"": ""$economy_Refinery;"",
                ""SystemSecondEconomy_Localised"": ""Refinery"",
                ""SystemGovernment"": ""$government_Democracy;"",
                ""SystemGovernment_Localised"": ""Democracy"",
                ""SystemSecurity"": ""$SYSTEM_SECURITY_medium;"",
                ""SystemSecurity_Localised"": ""MediumSecurity"",
                ""Population"": 10303479,
                ""JumpDist"": 19.340,
                ""FuelUsed"": 2.218082,
                ""FuelLevel"": 23.899260,
                ""Factions"": [{
                    ""Name"": ""DiaguandriInterstellar"",
                    ""FactionState"": ""Boom"",
                    ""Government"": ""Corporate"",
                    ""Influence"": 0.100398,
                    ""Allegiance"": ""Independent""
                },
                {
                    ""Name"": ""People'sMET20Liberals"",
                    ""FactionState"": ""Boom"",
                    ""Government"": ""Democracy"",
                    ""Influence"": 0.123260,
                    ""Allegiance"": ""Federation""
                },
                {
                    ""Name"": ""PilotsFederationLocalBranch"",
                    ""FactionState"": ""None"",
                    ""Government"": ""Democracy"",
                    ""Influence"": 0.000000,
                    ""Allegiance"": ""PilotsFederation""
                },
                {
                    ""Name"": ""NaturalDiaguandriRegulatoryState"",
                    ""FactionState"": ""None"",
                    ""Government"": ""Dictatorship"",
                    ""Influence"": 0.020875,
                    ""Allegiance"": ""Independent"",
                    ""RecoveringStates"": [{""State"": ""CivilWar"", ""Trend"": 0}]
                },
                {
                    ""Name"": ""CartelofDiaguandri"",
                    ""FactionState"": ""None"",
                    ""Government"": ""Anarchy"",
                    ""Influence"": 0.009940,
                    ""Allegiance"": ""Independent"",
                    ""PendingStates"": [{""State"": ""Bust"", ""Trend"": 0}, {""State"": ""CivilUnrest"", ""Trend"": 1}],
                    ""RecoveringStates"": [{""State"": ""CivilWar"", ""Trend"": 0}]
                },
                {
                    ""Name"": ""RevolutionaryPartyofDiaguandri"",
                    ""FactionState"": ""None"",
                    ""Government"": ""Democracy"",
                    ""Influence"": 0.124254,
                    ""Allegiance"": ""Federation"",
                    ""PendingStates"": [{""State"": ""Boom"", ""Trend"": 1}, {""State"": ""Bust"", ""Trend"": 1}]
                },
                {
                    ""Name"": ""TheBrotherhoodoftheDarkCircle"",
                    ""FactionState"": ""None"",
                    ""Government"": ""Corporate"",
                    ""Influence"": 0.093439,
                    ""Allegiance"": ""Independent"",
                    ""RecoveringStates"": [{""State"": ""CivilUnrest"", ""Trend"": 1}]
                },
                {
                    ""Name"": ""EXO"",
                    ""FactionState"": ""Expansion"",
                    ""Government"": ""Democracy"",
                    ""Influence"": 0.527833,
                    ""Allegiance"": ""Independent"",
                    ""PendingStates"": [{""State"": ""Boom"", ""Trend"": 1}]
                }],
                ""SystemFaction"": {
                    ""Name"": ""EXO"",
                    ""FactionState"": ""Expansion""
                }
            }";
            List<Event> events = JournalMonitor.ParseJournalEntry(line);

            JumpedEvent theJumpEvent = (JumpedEvent)events[0];
            EDDI.Instance.enqueueEvent(theJumpEvent);

        }
        #endregion

        private void CurrentRouteControl_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void CurrentRouteControl_Loaded_1(object sender, RoutedEventArgs e)
        {

        }
    }
}

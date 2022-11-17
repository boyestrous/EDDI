using EddiCore;
using EddiDataDefinitions;
using System.Windows;
using System.Windows.Controls;

namespace EddiTradeMonitor
{
    /// <summary>
    /// List of Stations within reasonable reach from current route
    /// </summary>
    public partial class SystemsList : UserControl
    {
        private TradeMonitor TradeMonitor()
        {
            return (TradeMonitor)EDDI.Instance.ObtainMonitor("Trade Monitor");
        }

        public SystemsList()
        {
            InitializeComponent();
            systemsListData.ItemsSource = TradeMonitor().validSystems.Waypoints;
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex()).ToString();
        }

        private void copySystemNameToClipboard(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                if (button.DataContext is NavWaypoint navWaypoint)
                {
                    Clipboard.SetText(navWaypoint.systemName);
                }
            }
        }
    }
}

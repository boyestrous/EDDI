using EddiCore;
using EddiDataDefinitions;
using System.Windows;
using System.Windows.Controls;
using EddiNavigationMonitor;

namespace EddiTradeMonitor
{
    /// <summary>
    /// Interaction logic for PlottedRouteControl.xaml
    /// </summary>
    public partial class CurrentRouteControl : UserControl
    {
        private NavigationMonitor navigationMonitor()
        {
            return (NavigationMonitor)EDDI.Instance.ObtainMonitor("Navigation monitor");
        }

        public CurrentRouteControl()
        {
            InitializeComponent();
            navRouteData.ItemsSource = navigationMonitor().NavRoute.Waypoints;
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

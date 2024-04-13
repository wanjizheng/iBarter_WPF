using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;

namespace iBarter
{
    internal class MapViewModel : INotifyPropertyChanged
    {
        private Map _map;

        public MapViewModel()
        {
            _ = SetupMap();
        }

        public Map Map
        {
            get => _map;
            set
            {
                _map = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task SetupMap()
        {
            // // Create a new map with a 'topographic vector' basemap.
            // Map = new Map(BasemapStyle.ArcGISTopographic);

            // Create a portal. If a URI is not specified, www.arcgis.com is used by default.
            var portal = await ArcGISPortal.CreateAsync();

            // Get the portal item for a web map using its unique item id.
            var mapItem = await PortalItem.CreateAsync(portal, "123e20006eb64d79be40744d3fed4914");

            // Create the map from the item.
            var map = new Map(mapItem);

            // To display the map, set the MapViewModel.Map property, which is bound to the map view.
            Map = map;
        }
    }
}
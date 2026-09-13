using iBarter.Localization;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Shared;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for StorageManagement.xaml
    /// </summary>
    public partial class StorageManagement : ChromelessWindow {
        private const double ZoomStep = 0.1;
        private const double MinZoom = 0.5;
        private const double MaxZoom = 2.0;

        private double zoomFactor = 1.0;
        private bool loadingMaxLevelCaps;

        private bool IsDesignMode => DesignerProperties.GetIsInDesignMode(this);

        // Phase 2 (i18n): SfDataGrid GridTextColumn.MappingName -> resource key.
        // HeaderText is a CLR property (not a DP) so {DynamicResource} cannot
        // refresh it on language flip; we override via this map in code-behind
        // on every LanguageService.LanguageChanged.
        private static readonly IReadOnlyDictionary<string, string> _headerKeyMap =
            new Dictionary<string, string> {
                ["ItemNameDisplay"]                   = "str.Grid.Storage.Col.Inventory",
                ["ItemIcon"]                          = "str.Grid.Storage.Col.Icon",
                ["ItemTierDisplay"]                   = "str.Grid.Storage.Col.Tier",
                ["StorageVeliaQuantity_Velia"]       = "str.Grid.Storage.Col.Velia",
                ["StorageVeliaQuantity_Iliya"]       = "str.Grid.Storage.Col.Iliya",
                ["StorageVeliaQuantity_Epheria"]     = "str.Grid.Storage.Col.Epheria",
                ["StorageVeliaQuantity_Ancado"]      = "str.Grid.Storage.Col.Ancado",
            };

        public StorageManagement() {
            InitializeComponent();
            if (IsDesignMode) return;
            DataContext = App.myStorageVM;
            DataGrid_Storage.ItemsSource = App.myStorageVM.StorageCollection;
            LoadSavedMaxLevelCaps();
            RefreshData();
            ApplyTypography();
            Loaded += (_, _) => ApplyTypography();

            ApplyLocalization();
            LanguageService.Instance.LanguageChanged += (_, _) => ApplyLocalization();
        }

        private void ApplyLocalization() {
            ApplyTypography();
            ApplyLocalizedHeaders();
            RefreshLocalizedDisplay();
        }

        private void LoadSavedMaxLevelCaps() {
            loadingMaxLevelCaps = true;
            try {
                ComboBox_MaxLV5.SelectedIndex = Math.Clamp(Properties.Settings.Default.SelectedComboBoxValueLV5, 0, 10);
                ComboBox_MaxLV6.SelectedIndex = Math.Clamp(Properties.Settings.Default.SelectedComboBoxValueLV6, 0, 10);
                ComboBox_MaxLV7.SelectedIndex = Math.Clamp(Properties.Settings.Default.SelectedComboBoxValueLV7, 0, 10);
            }
            finally {
                loadingMaxLevelCaps = false;
            }
        }

        private void ComboBox_MaxLV5_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            SaveMaxLevelCap(ComboBox_MaxLV5.SelectedIndex, value => Properties.Settings.Default.SelectedComboBoxValueLV5 = value);
        }

        private void ComboBox_MaxLV6_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            SaveMaxLevelCap(ComboBox_MaxLV6.SelectedIndex, value => Properties.Settings.Default.SelectedComboBoxValueLV6 = value);
        }

        private void ComboBox_MaxLV7_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            SaveMaxLevelCap(ComboBox_MaxLV7.SelectedIndex, value => Properties.Settings.Default.SelectedComboBoxValueLV7 = value);
        }

        private void SaveMaxLevelCap(int value, Action<int> save) {
            if (loadingMaxLevelCaps || value < 0) return;
            save(value);
            Properties.Settings.Default.Save();
        }

        private void ApplyTypography() {
            SfDataGridTypography.Apply(DataGrid_Storage);
        }

        private void ApplyLocalizedHeaders() {
            GridHeaderLocalization.ApplyHeaders(DataGrid_Storage, _headerKeyMap);
        }

        private void RefreshLocalizedDisplay() {
            if (DataGrid_Storage == null) {
                return;
            }
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(RefreshLocalizedDisplay);
                return;
            }

            // Re-bind storage items to the current catalog so ItemNameDisplay /
            // ItemNameZhTw reflect the active language. The bulk-replace happens
            // inside StorageViewModel.RefreshCatalogBindings which suppresses
            // auto-save, so a single language flip won't cascade 80+ saves.
            App.myStorageVM.RefreshCatalogBindings();
            DataGrid_Storage.View?.Refresh();
            DataGrid_Storage.InvalidateVisual();
        }

        public void RefreshData() {
            // Storage data is loaded once at app startup (App.OnStartup -> myStorageVM.LoadData()).
            // Re-invoking here is safe and idempotent: LoadData's dedup checks skip
            // already-present items. We still call it so the window reflects the latest
            // JSON state (e.g. if the user manually edited myStorage_Data.json while
            // the app was running). The bulk-add is internally suppressed, so a refresh
            // no longer cascades into 80+ mid-load SaveData() writes.
            App.myStorageVM.LoadData();
        }

        // The Hydrate / SeedHardcodedFallback helpers used to live here but moved to
        // StorageViewModel so the load path doesn't depend on a UI window being open.
        // Barter.InvQuantity used to call `new StorageManagement()` to force this load
        // on first read, which had the side effect of cascading 80+ saves on the first
        // click of the scanner's "Add to Planner" button. See StorageViewModel.LoadData
        // and Model/Barter.InvQuantity for the fix.

        // _ = HydrateStorageCollection; // (silence unused-helper reference: kept as a
        // comment marker in case a future UI-only hydration pass wants to reuse the
        // per-item catalog-resolution logic from StorageViewModel.HydrateStorageItem.)


        private void DataGrid_Storage_CurrentCellEndEdit(object sender, Syncfusion.UI.Xaml.Grid.CurrentCellEndEditEventArgs e) {
            App.myStorageVM.SaveData();
        }

        private void DataGrid_Storage_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) {
                return;
            }

            zoomFactor += e.Delta > 0 ? ZoomStep : -ZoomStep;
            zoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
            DataGrid_Storage.LayoutTransform = new ScaleTransform(zoomFactor, zoomFactor);
            e.Handled = true;
        }

        private void PinWindow_Click(object sender, System.Windows.RoutedEventArgs e) {
            if (this.Topmost) {
                this.Topmost = false;
                PinWindow.IsChecked = false;
            }
            else {
                this.Topmost = true;
                PinWindow.IsChecked = true;
            }
        }
    }
}

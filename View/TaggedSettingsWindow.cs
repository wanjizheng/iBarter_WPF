using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using iBarter.Routing;

namespace iBarter.View;

public sealed class TaggedSettingsWindow : Window {
    public TaggedTransportSettings Result { get; private set; }
    public event Action<TaggedTransportSettings>? SettingsChanged;
    private bool applying;
    private readonly List<Action> apply = [];
    private readonly List<DataGrid> grids = [];
    private static string L(string en, string zh) => TaggedRouteControl.L(en, zh);
    public TaggedSettingsWindow(TaggedTransportSettings original) {
        Result = JsonSerializer.Deserialize<TaggedTransportSettings>(JsonSerializer.Serialize(original))!;
        TaggedPortCatalog.Merge(Result);
        Result.HomeWarehouseId ??= "Iliya";
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/iBarter;component/Resources/Styles/TaggedControls.xaml", UriKind.Relative) });
        SetResourceReference(FontFamilyProperty, "AppFontFamily"); FontSize = 14;
        SetResourceReference(System.Windows.Media.TextOptions.TextFormattingModeProperty, "AppTextFormattingMode");
        SetResourceReference(System.Windows.Media.TextOptions.TextRenderingModeProperty, "AppTextRenderingMode");
        SetResourceReference(System.Windows.Media.TextOptions.TextHintingModeProperty, "AppTextHintingMode");
        Title = L("TAG transport settings", "TAG 輔助換貨設定"); Width = 840; Height = 740;
        MinWidth = 580; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppSurfaceBrush");
        SetResourceReference(ForegroundProperty, "AppTextBrush");
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        root.SetResourceReference(Panel.BackgroundProperty, "AppSurfaceBrush");
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = L("TAG-assisted barter", "TAG 輔助換貨"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        Note(heading, L("Plan your ship, characters and elephants together. Your settings are saved automatically.", "一起規劃船舶、角色與小象的運貨安排。設定會自動儲存。"));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var feedback = new TextBlock { Text = L("Changes save automatically.", "修改會自動儲存。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
        DockPanel.SetDock(feedback, Dock.Bottom); root.Children.Add(feedback);
        var save = new Button { Content = L("Done", "完成"), Padding = new Thickness(24, 8, 24, 8), Margin = new Thickness(4, 12, 0, 0), IsDefault = true };
        save.SetResourceReference(BackgroundProperty, "AppAccentBrush");
        save.SetResourceReference(ForegroundProperty, "AppAccentTextBrush");
        buttons.Children.Add(save);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var general = new StackPanel { Margin = new Thickness(10) };
        tabs.Items.Add(new TabItem { Header = L("Departure & planning", "出發與規劃"), Content = new ScrollViewer { Content = general, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        Section(general, L("Departure", "出發安排"));
        var start = new ComboBox { ItemsSource = App.listIslands?.Where(x => x.HasNavigationCoordinates).OrderBy(x => x.IslandsNameDisplay).ToArray(),
            DisplayMemberPath = "IslandsNameDisplay", SelectedValuePath = "IslandsName", SelectedValue = Result.StartIsland, MinWidth = 180 };
        Row(general, L("Departure", "出發地"), start);
        apply.Add(() => Result.StartIsland = start.SelectedValue as string ?? throw new ArgumentException(L("Choose a departure.", "請選擇出發地。")));
        var fixedDeparture = new CheckBox { Content = L("Start from this location (include travel to loading port)", "從此處出發（計入前往裝貨倉庫的航程）"),
            IsChecked = Result.StartFromSelectedLocation, Margin = new Thickness(0, 6, 0, 6) };
        general.Children.Add(fixedDeparture);
        start.IsEnabled = Result.StartFromSelectedLocation;
        fixedDeparture.Checked += (_, _) => start.IsEnabled = true;
        fixedDeparture.Unchecked += (_, _) => start.IsEnabled = false;
        apply.Add(() => Result.StartFromSelectedLocation = fixedDeparture.IsChecked == true);
        Note(general, L("Empty departures normally start at the first loading warehouse, as in standard routing. Existing cargo and in-progress runs retain their actual location.",
            "空載新規劃預設從第一個裝貨倉庫開始，與普通模式一致。已有攜貨或執行進度時保留實際位置。"));
        var active = new ComboBox { ItemsSource = new[] {
            new KeyValuePair<string, string>("main", L("Main character", "主號")),
            new KeyValuePair<string, string>("alt", L("TAG character", "TAG 小號")) },
            DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = Result.ActiveCharacter };
        Row(general, L("Current character", "目前角色"), active);
        apply.Add(() => Result.ActiveCharacter = active.SelectedValue as string ?? "main");
        var home = new ComboBox { ItemsSource = new[] { "Iliya", "Velia", "Epheria", "Ancado" },
            SelectedItem = Result.HomeWarehouseId, ItemTemplate = new DataTemplate() };
        var homeText = new FrameworkElementFactory(typeof(TextBlock));
        homeText.SetBinding(TextBlock.TextProperty, new Binding { Converter = new IslandDisplayConverter() });
        home.ItemTemplate.VisualTree = homeText;
        Row(general, L("Home warehouse", "主倉庫"), home);
        apply.Add(() => Result.HomeWarehouseId = home.SelectedItem as string ?? "Iliya");
        Note(general, L("Carry finished goods back to this warehouse. At other ports, use character space before storing goods locally.",
            "成品帶回主倉庫；途中優先借用人物背包腾出船位，不把其他港口當作成品的最終存放地。"));
        Note(general, L("Minimize total sailing distance. TAG cargo can carry supplies for several islands in one trip. Ship capacity uses your existing ship settings.", "以總航行距離最短為目標，利用 TAG 載貨一次串聯多個島嶼。船舶負重沿用現有船舶設定。"));
        Section(general, L("Planning limits", "規劃限制"));
        Number(general, L("Ship inventory slots", "船舱可用格數"), Result.ShipSlots, x => Result.ShipSlots = Integer(x, 1, 200));
        Note(general, L("Search mode and duration follow the main Auto Plan controls, including Custom duration.", "搜尋模式與時長沿用主介面自動規劃的選擇，包括自訂時長。"));
        var overload = new CheckBox { Content = L("Allow overloaded legs (sprint unavailable)", "允許超重航段（無法衝刺）"), IsChecked = Result.AllowOverloadedSailing, Margin = new Thickness(0, 8, 0, 8) };
        general.Children.Add(overload); apply.Add(() => Result.AllowOverloadedSailing = overload.IsChecked == true);
        Note(general, L("Loading onto the ship remains limited to 100%. These two operation thresholds are assumptions, independently configurable up to 170%; use your EU in-game results.",
            "主動裝船仍以 100% 為限。以下兩項為獨立操作假設，最高可設 170%；請以歐服實際操作結果校準。"));
        Number(general, L("Barter output allowance (%)", "交換後船載重容許值（%）"), Result.BarterOutputRatio * 100, x => Result.BarterOutputRatio = Ratio(x));
        Number(general, L("Character receiving limit (%)", "人物接貨負重上限（%）"), Result.CharacterReceiveRatio * 100, x => Result.CharacterReceiveRatio = Ratio(x));
        Note(general, L("LV5–LV7 goods use one slot each and must fit within this limit after receiving. Only stackable goods can cross it in a single stack transfer.",
            "LV5–LV7 每件佔一格，接貨後總重不可超過此上限。只有可堆疊物品才可透過一次整疊轉移超過門檻。"));

        var carriers = Grid(Result.Carriers, false);
        TextColumn(carriers, "Name", L("Character / elephant", "角色／小象"));
        TextColumn(carriers, "LimitLT", L("Capacity (LT)", "負重上限（LT）")); TextColumn(carriers, "OccupiedLT", L("Already used LT", "日常占用 LT"));
        TextColumn(carriers, "Slots", L("Free slots", "可用格數"));
        tabs.Items.Add(new TabItem { Header = L("Characters", "角色"), Content = WithNote(carriers,
            L("Main sails the ship. Each character can whistle-summon only an empty elephant. Trade goods must be moved from the elephant to its owner before sailing to the next island. Stacking via storage still requires a warehouse.",
                "由主號開船。每個角色只能用笛子召喚空的小象；前往下一個島嶼前，必須先把小象上的交易物品轉到所屬角色身上。透過倉庫疊貨仍需要倉庫。")) });
        var ports = Grid(Result.Ports, false);
        ports.Columns.Add(new DataGridCheckBoxColumn { Header = L("Use", "使用"), Binding = new Binding("Enabled") });
        ports.Columns[0].Width = new DataGridLength(60);
        void RefreshIslandColumns(object? sender, EventArgs e) {
            foreach (var column in ports.Columns.OfType<DataGridTextColumn>())
                if (column.Binding is Binding { Path.Path: "IslandId" or "WarehouseId" or "Npc" } binding)
                    column.Binding = new Binding(binding.Path.Path) { Converter = binding.Path.Path == "Npc" ? new NpcDisplayConverter() : new IslandDisplayConverter(), Mode = BindingMode.OneWay };
        }
        TextColumn(ports, "IslandId", L("Island", "島嶼"), true); TextColumn(ports, "Npc", L("Wharf manager", "碼頭管理員"), true);
        TextColumn(ports, "WarehouseId", L("Storage", "倉庫"), true);
        RefreshIslandColumns(null, EventArgs.Empty);
        Localization.LanguageService.Instance.LanguageChanged += RefreshIslandColumns;
        Closed += (_, _) => Localization.LanguageService.Instance.LanguageChanged -= RefreshIslandColumns;
        ports.RowStyle = new Style(typeof(DataGridRow));
        ports.RowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("Evidence")));
        tabs.Items.Add(new TabItem { Header = L("Ports", "碼頭"), Content = WithNote(ports,
            L("Enable only ports where your personal ship can transfer cargo. Candidate ports are off until verified. Island coordinates estimate the approach; local docking time is configurable. Sausan never links to Ancado storage.",
                "只啟用個人船確實能裝卸的碼頭。未核實候選預設關閉。薩扇營地碼頭不連接安卡杜內港倉庫。")) });

        var cargo = new ObservableCollection<CargoRow>(Result.InitialCargo.Select(x => new CargoRow { Container = x.Container, ItemId = x.ItemId, Quantity = x.Quantity }));
        var inventory = Grid(cargo, true);
        inventory.Columns.Add(new DataGridComboBoxColumn { Header = L("Container", "容器"), ItemsSource = new[] { "ship", "main", "alt", "main-elephant", "alt-elephant" }, SelectedItemBinding = new Binding("Container") });
        var items = App.myStorageVM?.StorageCollection.Select(x => new ItemChoice(x.ItemID, x.ItemNameDisplay)).ToArray() ?? [];
        inventory.Columns.Add(new DataGridComboBoxColumn { Header = L("Item", "貨物"), ItemsSource = items, SelectedValuePath = "Id", DisplayMemberPath = "Name", SelectedValueBinding = new Binding("ItemId") });
        TextColumn(inventory, "Quantity", L("Quantity", "數量"));
        tabs.Items.Add(new TabItem { Header = L("Already carried", "初始攜貨"), Content = WithNote(inventory,
            L("Optional: actual cargo already on your ship / characters / elephants, excluded from recorded warehouse stock. Leave empty when departing with empty inventories. The planner chooses the initial warehouse loading split automatically.",
                "選填：船、角色、小象上已存在且未算入倉庫的貨物。空背包出發可留空；規劃器會自行決定從倉庫如何分貨。")) });
        string lastSaved = JsonSerializer.Serialize(original);
        void ApplyEdits(bool close) {
            if (applying) return;
            applying = true;
            try {
                foreach (var grid in grids) {
                    if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException(L("Fix the highlighted cell.", "請修正欄位內容。"));
                }
                foreach (var action in apply) action();
                if (Result.OverloadedMetersPerSecond > Result.NormalMetersPerSecond) throw new ArgumentException(L("Overloaded speed must not exceed normal speed.", "超重航速不能大於正常航速。"));
                if (Result.Carriers.Any(x => !double.IsFinite(x.LimitLT) || x.LimitLT <= 0 || !double.IsFinite(x.OccupiedLT)
                    || x.OccupiedLT < 0 || x.Slots is <= 0 or > 200)) throw new ArgumentException(L("Check carrier capacity and slots.", "請檢查角色負重與格數。"));
                Result.InitialCargo = cargo.Where(x => !string.IsNullOrWhiteSpace(x.ItemId)).Select(x => {
                    if (x.Quantity <= 0) throw new ArgumentException(L("Cargo quantity must be positive.", "貨物數量必須大於零。"));
                    return new TaggedCargoEntry(x.Container, x.ItemId, x.Quantity);
                }).ToArray();
                string serialized = JsonSerializer.Serialize(Result);
                if (serialized != lastSaved) {
                    SettingsChanged?.Invoke(JsonSerializer.Deserialize<TaggedTransportSettings>(serialized)!);
                    lastSaved = serialized;
                }
                feedback.Text = L("Saved automatically.", "已自動儲存。");
                if (close) DialogResult = true;
            }
            catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or System.IO.IOException or UnauthorizedAccessException) {
                feedback.Text = L("Not saved: ", "未儲存：") + e.Message;
                if (close) MessageBox.Show(this, e.Message, Title);
            }
            finally { applying = false; }
        }
        save.Click += (_, _) => ApplyEdits(true);
        bool saveQueued = false;
        void QueueAutoSave() {
            if (saveQueued || applying || !IsLoaded) return;
            saveQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => {
                saveQueued = false;
                if (!IsLoaded) return;
                // Moving focus from a cell into its editor also raises LostKeyboardFocus.
                // Let WPF finish that transition; never commit an editor the user just entered.
                if (grids.Any(grid => grid.IsKeyboardFocusWithin && grid.Items.Cast<object>().Any(item =>
                    grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow { IsEditing: true }))) return;
                ApplyEdits(false);
            }));
        }
        LostKeyboardFocus += (_, _) => QueueAutoSave();
        foreach (var grid in grids) grid.RowEditEnding += (_, _) => QueueAutoSave();
        Closing += (_, _) => ApplyEdits(false);
    }
    private static double Positive(double x) => double.IsFinite(x) && x > 0 ? x : throw new ArgumentException(L("Enter a positive number.", "請輸入正數。"));
    private static double Ratio(double x) => double.IsFinite(x) && x >= 100 && x <= 170 ? x / 100 : throw new ArgumentException("100–170%");
    private static int Integer(double x, int min, int max) => x == Math.Truncate(x) && x >= min && x <= max ? (int)x : throw new ArgumentException($"{min}–{max}");
    private void Number(Panel parent, string label, double value, Action<double> setter) {
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), MinWidth = 140 };
        Row(parent, label, box); apply.Add(() => setter(double.Parse(box.Text, CultureInfo.CurrentCulture)));
    }
    private static void Row(Panel parent, string label, FrameworkElement field) {
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "AppTextBrush"); grid.Children.Add(caption);
        System.Windows.Controls.Grid.SetColumn(field, 1); grid.Children.Add(field); parent.Children.Add(grid);
    }
    private DataGrid Grid(System.Collections.IEnumerable source, bool add) {
        var grid = new DataGrid { ItemsSource = source, AutoGenerateColumns = false, CanUserAddRows = add, CanUserDeleteRows = add,
            Margin = new Thickness(6), MinColumnWidth = 105, HeadersVisibility = DataGridHeadersVisibility.Column, ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star) };
        grids.Add(grid); return grid;
    }
    private static void TextColumn(DataGrid grid, string path, string title, bool readOnly = false) => grid.Columns.Add(new DataGridTextColumn {
        Header = title, IsReadOnly = readOnly, Binding = new Binding(path) { ValidatesOnExceptions = true, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus } });
    private static void Note(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 10) });
    private static void Section(Panel panel, string title) {
        var heading = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccentBrush"); panel.Children.Add(heading);
    }
    private static FrameworkElement WithNote(FrameworkElement content, string note) {
        var panel = new DockPanel(); var text = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
        DockPanel.SetDock(text, Dock.Top); panel.Children.Add(text); panel.Children.Add(content); return panel;
    }
    private sealed record ItemChoice(string Id, string Name);
    public sealed class IslandDisplayConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            string id = value as string ?? "";
            var extra = TaggedPortCatalog.Additional.FirstOrDefault(p => p.Island == id);
            return id.Length == 0 ? "—" : extra is not null ? L(id, extra.ChineseIsland) : App.listIslands?.FirstOrDefault(i => i.IslandsName == id)?.IslandsNameDisplay ?? id;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    public sealed class NpcDisplayConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L(value as string ?? "—", TaggedPortCatalog.ChineseNpc(value as string ?? ""));
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    public sealed class CargoRow { public string Container { get; set; } = "ship"; public string ItemId { get; set; } = ""; public int Quantity { get; set; } = 1; }
}

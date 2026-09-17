using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iBarter;
using iBarter.Routing;
using iBarter.View;

internal static class Program {
    [STAThread]
    private static int Main(string[] args) {
        try {
            if (args.Length == 2 && args[0] == "--audit-algorithm") return TaggedAlgorithmAudit.Run(args[1]);
            if (args.Length == 2 && args[0] == "--trace-search") return TaggedAlgorithmAudit.TraceSearch(args[1]);
            if (args.Length == 2 && args[0] == "--audit-completion") return AuditCompletion(args[1]);
            if (args.Length == 3 && args[0] == "--audit-completion") return AuditCompletion(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--audit-home") return AuditHome(args[1]);
            if (args.Length == 2 && args[0] == "--audit-port-timing") return AuditPortTiming(args[1]);
            if (args.Length == 2 && args[0] == "--audit-lv7") return AuditLevelSeven(args[1]);
            if (args.Length == 2 && args[0] == "--audit-character-weight") return AuditCharacterWeight(args[1]);
            if (args.Length == 3 && args[0] == "--search") return VerifyLongSearch(args[1], double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(application.Dispatcher));
            iBarter.Localization.LanguageService.Instance.InitializeAtStartup();
            foreach (var key in new[] { "AppSurfaceBrush", "AppRaisedSurfaceBrush", "AppCardBrush" }) application.Resources[key] = new SolidColorBrush(Color.FromRgb(28, 31, 36));
            application.Resources["AppTextBrush"] = Brushes.WhiteSmoke;
            application.Resources["AppMutedTextBrush"] = Brushes.LightGray;
            application.Resources["AppAccentBrush"] = Brushes.LightSkyBlue;
            application.Resources["AppAccentTextBrush"] = Brushes.Black;
            application.Resources["AppFontFamily"] = new FontFamily("Segoe UI");
            application.Resources["AppCardBorderBrush"] = Brushes.Gray;
            App.listIslands = [new Islands(EnumLists.Island.Velia, 0) { NavigationX = 0, NavigationY = 0 }];
            App.listItems = []; App.myStorageVM = new();
            if (args.Length == 2 && args[0] == "--invalid-save") return VerifyInvalidSaveStartup(args[1]);
            if (args.Length == 1 && args[0] == "--toolbar") { App.myPVM = new(); VerifySharedToolbar(); return 0; }
            VerifySettingsEditing();
            VerifyLogTheme();
            VerifyChineseSettings();
            var request = new TaggedTransportRequest {
                ShipLimitLT = 2000,
                Items = new() { ["a"] = new("a", "Later-segment barter materials", 4, 1000), ["b"] = new("b", "Exchange output", 5, 1000) },
                Points = new() { ["Velia"] = new(0, 0), ["Kuit"] = new(1000000, 0), ["Island"] = new(1000100, 0) },
                Warehouses = new() { ["Velia"] = new() { ["a"] = 4 } },
                Trades = [new("row", "Island", "a", 1, "b", 1, 4)],
                Settings = new() { Enabled = true, MaxStates = 8000, SearchSeconds = 5, Ports = [
                    new() { IslandId = "Velia", Enabled = true, WarehouseId = "Velia" }, new() { IslandId = "Kuit", Enabled = true } ] }
            };
            request.Settings.Carriers[0].LimitLT = request.Settings.Carriers[1].LimitLT = 1000;
            var plan = new TaggedTransportPlanner().Plan(request).Plan ?? throw new Exception("No smoke route.");
            string folder = Path.Combine(AppContext.BaseDirectory, "Resources");
            Directory.CreateDirectory(Path.Combine(folder, "Images", "Items"));
            foreach (var pair in new[] { ("a", "800001"), ("b", "800002") })
                File.Copy(Path.Combine("Resources", "Images", "Items", pair.Item2 + ".bmp"), Path.Combine(folder, "Images", "Items", pair.Item1 + ".bmp"), true);
            TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-settings.json"), request.Settings);
            TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-session.json"), new TaggedTransportSession(1, request, plan, 0));
            var control = new TaggedRouteControl();
            Render(control, 350, 900, "tagged-route-350.png");
            int firstGroupEnd = TaggedOperationGroups.MoveCursor(request, plan, 0, true);
            var next = (Button)control.FindName("NextButton");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (firstGroupEnd <= 0 || control.Session?.CompletedSteps != firstGroupEnd) throw new Exception("Next loading group failed.");
            var restored = new TaggedRouteControl();
            if (restored.Session?.CompletedSteps != firstGroupEnd) throw new Exception("Progress restore failed.");
            ((Button)restored.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (restored.Session?.CompletedSteps != 0) throw new Exception("Undo failed.");
            VerifyRouteSelection(request, folder);
            VerifySaleRendering(request, folder);
            VerifySearchControls(request, plan, folder);
            var settings = new TaggedSettingsWindow(request.Settings);
            settings.SettingsChanged += value => TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-settings.json"), value);
            Render((FrameworkElement)settings.Content, 720, 590, "tagged-settings.png");
            VisualChildren<TextBox>(settings).First().Text = "31";
            settings.Close();
            var settingsReloaded = new TaggedRouteControl();
            if (settingsReloaded.Settings.ShipSlots != 31) throw new Exception("Settings did not auto-save and reload.");
            var invalid = new TaggedSettingsWindow(settingsReloaded.Settings);
            invalid.SettingsChanged += value => TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-settings.json"), value);
            Render((FrameworkElement)invalid.Content, 720, 590, "tagged-settings.png");
            var settingsTabs = VisualChildren<TabControl>((FrameworkElement)invalid.Content).Single();
            settingsTabs.SelectedIndex = 1;
            Render((FrameworkElement)invalid.Content, 790, 670, "tagged-characters.png");
            settingsTabs.SelectedIndex = 0;
            Render((FrameworkElement)invalid.Content, 790, 670, "tagged-settings.png");
            VisualChildren<TextBox>(invalid).First().Text = "invalid";
            invalid.Close();
            if (new TaggedRouteControl().Settings.ShipSlots != 31) throw new Exception("Invalid input overwrote valid settings.");
            ((CheckBox)restored.FindName("EnabledBox")).IsChecked = false;
            ((CheckBox)restored.FindName("EnabledBox")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (restored.IsEnabledMode || restored.RenderSnapshot() is not null) throw new Exception("Disable did not restore legacy routing.");
            application.Resources["AppSurfaceBrush"] = Brushes.White;
            application.Resources["AppCardBrush"] = Brushes.White;
            application.Resources["AppRaisedSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(247, 249, 252));
            application.Resources["AppTextBrush"] = new SolidColorBrush(Color.FromRgb(24, 33, 43));
            application.Resources["AppMutedTextBrush"] = new SolidColorBrush(Color.FromRgb(82, 97, 112));
            application.Resources["AppCardBorderBrush"] = new SolidColorBrush(Color.FromRgb(195, 205, 216));
            application.Resources["AppAccentBrush"] = new SolidColorBrush(Color.FromRgb(23, 111, 127));
            application.Resources["AppAccentTextBrush"] = Brushes.White;
            var light = new TaggedSettingsWindow(request.Settings);
            Render((FrameworkElement)light.Content, 790, 670, "tagged-settings-light.png");
            light.Close();
            if (args.Length == 1) RenderSavedSession(args[0]);
            VerifySharedToolbar();
            Console.WriteLine("PASS: focused cell editing, numeric editing, settings auto-save/load, invalid input protection, two-route grouping, route filtering, map focus, narrow route rendering, step completion, reload, undo, disable.");
            Console.WriteLine(AppContext.BaseDirectory);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void Render(FrameworkElement element, int width, int height, string file, bool drainDispatcher = true) {
        element.Margin = new Thickness(0);
        element.Width = width; element.Height = height;
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        if (drainDispatcher) element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, file)); encoder.Save(output);
    }
    private static int VerifyInvalidSaveStartup(string path) {
        var before = File.ReadAllBytes(path);
        var saved = System.Text.Json.JsonSerializer.Deserialize<TaggedTransportSession>(before)!;
        if (new TaggedTransportSimulator(saved.Request).Verify(saved.Plan, out _, out _)) throw new Exception("Fixture must contain an invalid old route.");
        string folder = Path.Combine(AppContext.BaseDirectory, "Resources");
        Directory.CreateDirectory(folder);
        var settings = saved.Request.Settings with { Enabled = true };
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-settings.json"), settings);
        File.Copy(path, Path.Combine(folder, "tagged-transport-session.json"), true);
        for (int attempt = 0; attempt < 2; attempt++) {
            var control = new TaggedRouteControl();
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Render(control, 420, 450, "tagged-invalid-save-startup.png");
            if (control.Session is not null || control.DisplayPlan is not null || !control.IsEnabledMode)
                throw new Exception("Invalid route was accepted or TAG settings were lost.");
            var status = ((TextBlock)control.FindName("StatusText")).Text;
            if (!status.Contains("failed validation") && !status.Contains("未通過校驗")) throw new Exception("Replan guidance was not retained after Loaded.");
        }
        if (!File.ReadAllBytes(path).SequenceEqual(before)) throw new Exception("Original saved resources were modified.");
        Console.WriteLine("PASS: actual invalid saved route opens twice without throwing; settings preserved; route hidden with replan guidance; original file unchanged.");
        return 0;
    }
    private static void RenderSavedSession(string path) {
        var session = TaggedTransportStorage.Load<TaggedTransportSession>(path) ?? throw new Exception("Missing session.");
        // This legacy fixture explicitly tests unrestricted warehouse restores.
        // New user settings default to bringing goods home to Iliya.
        if (session.Request.Settings.HomeWarehouseId is null) {
            var legacy = session.Request with { Settings = session.Request.Settings with { HomeWarehouseId = "" } };
            session = session with { Request = legacy, Plan = session.Plan with { Fingerprint = legacy.Fingerprint() } };
        }
        string folder = Path.Combine(AppContext.BaseDirectory, "Resources");
        // Isolated test output only; never save over the user's supplied runtime directory.
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-session.json"), session);
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-settings.json"), (session.WorkspaceInputs ?? session.SettlementBaseline ?? session.Request).Settings);
        foreach (string id in session.Request.Items.Keys) {
            string icon = Path.Combine(Path.GetDirectoryName(path)!, "Images", "Items", id + ".bmp");
            if (!File.Exists(icon)) icon = Path.Combine("Resources", "Images", "Items", id + ".bmp");
            if (File.Exists(icon)) File.Copy(icon, Path.Combine(folder, "Images", "Items", id + ".bmp"), true);
        }
        var control = new TaggedRouteControl();
        Render(control, 420, 1100, "tagged-actual-grouped.png");
        VerifyCardNumbering(control);
        var steps = (ListBox)control.FindName("StepsList");
        var cargoLines = steps.Items.Cast<object>().Take(1).SelectMany(card =>
            ((System.Collections.IEnumerable)card.GetType().GetProperty("Lines")!.GetValue(card)!).Cast<object>()).ToArray();
        if (session.Plan.Steps.Any(s => s.Action is { From: "warehouse:Iliya", To: "ship", ItemId: "800062", Quantity: 3 })) {
            var golden = cargoLines.Where(line => ((string?)line.GetType().GetProperty("Icon")!.GetValue(line))?.EndsWith("800062.bmp") == true).ToArray();
            if (golden.Length != 2 || !golden.Any(line => ((string)line.GetType().GetProperty("Text")!.GetValue(line)!).EndsWith("× 3")
                && ((string)line.GetType().GetProperty("CharacterText")!.GetValue(line)!).Length > 0))
                throw new Exception("Actual loading card repeated the three-item warehouse shuttle.");
            var highlighted = VisualChildren<TextBlock>(control).SelectMany(t => t.Inlines.OfType<System.Windows.Documents.Run>())
                .Where(r => r.FontWeight == FontWeights.Bold && r.Text.Length > 0).ToArray();
            if (highlighted.Length == 0 || !highlighted.Any(r => Equals(r.Foreground, Application.Current.Resources["AppAccentBrush"])))
                throw new Exception("Character labels are not highlighted with the theme accent.");
            Console.WriteLine("PASS: actual golden-candlestick loading shows Main x3 and Ship x2, with themed character emphasis and no duplicated shuttle quantity.");
        }
        var kuit = steps.Items.Cast<object>().FirstOrDefault(card => {
            int index = (int)card.GetType().GetProperty("Index")!.GetValue(card)!;
            int end = (int)card.GetType().GetProperty("End")!.GetValue(card)!;
            return session.Plan.Steps.Skip(index).Take(end - index).Any(step => step.Action is { Kind: TaggedActionKind.Transfer, Location: "Kuit", From: "ship", To: "main", ItemId: "800218" });
        });
        if (kuit is not null) {
            var lines = ((System.Collections.IEnumerable)kuit.GetType().GetProperty("Lines")!.GetValue(kuit)!).Cast<object>()
                .Where(line => ((string?)line.GetType().GetProperty("Icon")!.GetValue(line))?.EndsWith("800218.bmp") == true).ToArray();
            if (lines.Length != 1 || !((string)lines[0].GetType().GetProperty("Text")!.GetValue(lines[0])!).EndsWith("× 2"))
                throw new Exception("The two Kuit one-item transfers were not combined into a quantity-two line.");
            steps.ScrollIntoView(kuit);
            Render(control, 420, 850, "tagged-kuit-transfer.png");
        }
        ((TabControl)control.FindName("PlanTabs")).SelectedIndex = 1;
        Render(control, 420, 850, "tagged-cargo-locations.png");
        if (((TextBlock)control.FindName("InventoryText")).Text.Contains("warehouse:", StringComparison.Ordinal))
            throw new Exception("Cargo view still dumps internal warehouse identifiers.");
        ((TabControl)control.FindName("PlanTabs")).SelectedIndex = 0;
        VerifySharedMap(control, path);
        control = new TaggedRouteControl(); // map test restores its isolated saved snapshot
        var groups = TaggedOperationGroups.Build(session.Request, session.Plan);
        Console.WriteLine($"Supplied save: {session.Plan.Steps.Length} internal steps, {groups.Length} display cards, first card {groups[0].End - groups[0].Start} operations.");
        var next = (Button)control.FindName("NextButton");
        if (session.CompletedSteps == 0 && groups[0].End - groups[0].Start > 1) {
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (control.Session!.CompletedSteps != groups[0].End) throw new Exception("Grouped loading did not complete as one operation.");
            ((Button)control.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (control.Session.CompletedSteps != 0) throw new Exception("Grouped loading did not undo as one operation.");
        }
            VerifyRestoreCompatibility(session);
            VerifyRuntimeRestore(path);
    }
    private static void VerifyRuntimeRestore(string sessionPath) {
        string runtime = Path.GetDirectoryName(sessionPath)!;
        if (!File.Exists(Path.Combine(runtime, "myPlan_Data.json"))) return;
        string isolated = Path.Combine(AppContext.BaseDirectory, "Resources");
        var session = TaggedTransportStorage.Load<TaggedTransportSession>(sessionPath)!;
        var settings = TaggedTransportStorage.Load<TaggedTransportSettings>(Path.Combine(runtime, "tagged-transport-settings.json"))!;
        TaggedTransportStorage.Save(Path.Combine(isolated, "tagged-transport-session.json"), session);
        TaggedTransportStorage.Save(Path.Combine(isolated, "tagged-transport-settings.json"), settings);
        App.myPVM = new();
        App.myPVM.BarterCollection = new(Newtonsoft.Json.JsonConvert.DeserializeObject<List<Barter>>(
            File.ReadAllText(Path.Combine(runtime, "myPlan_Data.json")))!);
        App.myStorageVM = new();
        using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false))
            foreach (var item in Newtonsoft.Json.JsonConvert.DeserializeObject<List<Items>>(
                File.ReadAllText(Path.Combine(runtime, "myStorage_Data.json")))!) App.myStorageVM.StorageCollection.Add(item);
        using var ship = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime, "myShipProperty_Data.json")));
        App.myCargoProperty = new(ship.RootElement.GetProperty("ExtraLT").GetDouble(), ship.RootElement.GetProperty("TotalLT").GetDouble());
        for (int restart = 0; restart < 2; restart++) {
            var control = new TaggedRouteControl();
            if (!control.ValidateRestoredWorkspace() || control.DisplayPlan is null)
                throw new Exception("Actual runtime files did not restore: " + ((TextBlock)control.FindName("StatusText")).Text);
            if (control.Settings.StartFromSelectedLocation != settings.StartFromSelectedLocation)
                throw new Exception("Restore overwrote the user's departure preference.");
        }
        var continued = new TaggedRouteControl();
        var restored = continued.Session!;
        int exchange = Array.FindIndex(restored.Plan.Steps, s => s.Action.Kind == TaggedActionKind.Barter);
        var route = TaggedTransportRoutes.Build(restored.Request, restored.Plan).Single(r => r.Start <= exchange && exchange < r.End);
        WaitOnUi(continued.CompleteMapStep(route.Number, exchange - route.Start));
        if (continued.Session == restored || !new TaggedRouteControl().ValidateRestoredWorkspace())
            throw new Exception("Completing an exchange after equivalent-departure restore failed the next startup validation.");
        Console.WriteLine("PASS: actual runtime Planner, stock, ship, TAG settings and session restore twice without rewriting user preferences.");
    }
    private static void VerifyRestoreCompatibility(TaggedTransportSession session) {
        var request = session.WorkspaceInputs ?? session.SettlementBaseline ?? session.Request;
        App.listIslands = request.Trades.Select(t => t.IslandId).Distinct()
            .Select(id => new Islands(Enum.Parse<EnumLists.Island>(id), 0)).ToList();
        App.listItems = request.Items.Values.Select(i => new Items(i.DisplayName, i.ItemId, i.Level.ToString())).ToList();
        App.myPVM = new();
        App.myPVM.BarterCollection = new(request.Trades.Select(t => {
            var input = request.Items[t.InputId]; var output = request.Items[t.OutputId];
            var row = new Barter(App.listIslands.Single(i => i.IslandsName == t.IslandId),
                new Items(input.DisplayName, input.ItemId, input.Level.ToString(), t.InputPerExchange),
                new Items(output.DisplayName, output.ItemId, output.Level.ToString(), t.OutputPerExchange), t.Exchanges);
            typeof(Barter).GetField("plannerRowId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(row, t.RowId);
            return row;
        }));
        App.myCargoProperty = new(request.ShipOccupiedLT, request.ShipLimitLT);
        App.myStorageVM = new();
        using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false)) {
            foreach (var item in request.Items.Values) {
                int Stock(string wh) => request.Warehouses.GetValueOrDefault(wh)?.GetValueOrDefault(item.ItemId) ?? 0;
                App.myStorageVM.StorageCollection.Add(new Items(item.DisplayName, item.ItemId, item.Level.ToString(),
                    _intStorageVelia: Stock("Velia"), _intStorageIliya: Stock("Iliya"), _intStorageEpheria: Stock("Epheria"), _intStorageAncado: Stock("Ancado")));
            }
        }
        if (!new TaggedRouteControl().ValidateRestoredWorkspace()) throw new Exception("Matching TAG save was rejected at startup.");
        void MustClear(TaggedRouteControl control, string reason) {
            if (control.ValidateRestoredWorkspace() || control.Session is not null || control.DisplayPlan is not null
                || ((ListBox)control.FindName("StepsList")).Items.Count != 0 || (control.RenderSnapshot()?.Paths.Count ?? 0) != 0)
                throw new Exception("Stale TAG display survived " + reason);
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "tagged-transport-session.json")))
                throw new Exception("Invalidation deleted the recoverable saved session.");
        }
        if (request.Settings.InitialCargo.Length == 0 && session.Plan.Steps[0].Action is { Kind: TaggedActionKind.Transfer } loading
            && loading.Location == request.Settings.StartIsland && TaggedTransportSimulator.IsWarehouse(loading.From)) {
            var equivalent = new TaggedRouteControl();
            equivalent.Settings.StartFromSelectedLocation = !request.Settings.StartFromSelectedLocation;
            equivalent.Settings.StartIsland = loading.Location;
            if (!equivalent.ValidateRestoredWorkspace()) throw new Exception("Equivalent empty departure checkbox invalidated the route.");
            var different = new TaggedRouteControl();
            different.Settings.StartFromSelectedLocation = true;
            different.Settings.StartIsland = request.Points.Keys.First(id => id != loading.Location);
            MustClear(different, "different actual departure");
            var carried = new TaggedRouteControl();
            carried.Settings.InitialCargo = [new("ship", request.Items.Keys.First(), 1)];
            MustClear(carried, "different initial cargo");
        }
        var ship = new TaggedRouteControl(); App.myCargoProperty.TotalLT++;
        MustClear(ship, "ship change"); App.myCargoProperty.TotalLT--;
        var settings = new TaggedRouteControl(); settings.Settings.Carriers[1].LimitLT++;
        MustClear(settings, "TAG capacity change");
        var seedSource = (TaggedTransportSession?)typeof(TaggedRouteControl).GetField("invalidatedSeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings);
        var seedMethod = typeof(TaggedRouteControl).GetMethod("ReusableSeed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        if (seedMethod.Invoke(null, [seedSource, request]) is null || settings.DisplayPlan is not null)
            throw new Exception("Clearing stale display lost its independently verifiable search seed.");
        var changedTrades = request with { Trades = request.Trades.Skip(1).ToArray() };
        if (seedMethod.Invoke(null, [seedSource, changedTrades]) is not null) throw new Exception("Stale exchange identities reused a search seed.");
        var inventory = new TaggedRouteControl();
        using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false)) {
            var item = App.myStorageVM.StorageCollection.First(x => request.Items[x.ItemID].Level > 0);
            item.StorageVeliaQuantity_Velia++;
            MustClear(inventory, "inventory change"); item.StorageVeliaQuantity_Velia--;
        }
        var planner = new TaggedRouteControl(); App.myPVM.BarterCollection.RemoveAt(0);
        MustClear(planner, "Planner change");
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "Resources", "tagged-transport-settings.json");
        TaggedTransportStorage.Save(settingsPath, request.Settings with { Enabled = false });
        var disabled = new TaggedRouteControl();
        MustClear(disabled, "startup with TAG disabled and stale Planner");
        var toggle = (CheckBox)disabled.FindName("EnabledBox");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (disabled.DisplayPlan is not null || !new TaggedRouteControl().IsEnabledMode) throw new Exception("Enabling revived stale routes or failed to persist.");
        var reloaded = new TaggedRouteControl();
        ((CheckBox)reloaded.FindName("EnabledBox")).IsChecked = false; // simulated layout restoration, not a user click
        reloaded.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        if (((CheckBox)reloaded.FindName("EnabledBox")).IsChecked != true || !new TaggedRouteControl().IsEnabledMode)
            throw new Exception("Layout restoration overwrote the saved TAG switch.");
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (new TaggedRouteControl().IsEnabledMode) throw new Exception("Disabling TAG failed to persist.");
        TaggedTransportStorage.Save(settingsPath, request.Settings);
        Console.WriteLine("PASS: matching TAG startup restores; changed ship, TAG capacity, warehouse or Planner clears cards and map without deleting the saved file.");
    }
    private static void VerifyNextBarterFocus(TaggedRouteControl control) {
        var list = (ListBox)control.FindName("StepsList");
        var first = list.Items.Cast<object>().First(c => c.GetType().GetProperty("RowId")!.GetValue(c) is not null);
        if (!ReferenceEquals(first, list.SelectedItem)) throw new Exception("Completion did not select the next visible exchange card.");
        int index = (int)first.GetType().GetProperty("Index")!.GetValue(first)!;
        int number = (int)first.GetType().GetProperty("RouteNumber")!.GetValue(first)!;
        var action = control.Session!.Plan.Steps[index].Action;
        if (action.Kind != TaggedActionKind.Barter || !control.IsFocusedMarker(number, action.Location))
            throw new Exception("The selected next exchange has no matching map highlight.");
    }
    private static void VerifyCardNumbering(TaggedRouteControl control) {
        var counters = new Dictionary<int, int>();
        foreach (var card in ((ListBox)control.FindName("StepsList")).Items) {
            int route = (int)card.GetType().GetProperty("RouteNumber")!.GetValue(card)!;
            string title = (string)card.GetType().GetProperty("Title")!.GetValue(card)!;
            int number = int.Parse(System.Text.RegularExpressions.Regex.Match(title, @"\d+").Value);
            int expected = counters.GetValueOrDefault(route) + 1; counters[route] = expected;
            if (number != expected) throw new Exception("Step numbers did not restart in each route.");
        }
    }
    private static void VerifyLogTheme() {
        var resources = Application.Current.Resources;
        var previous = new Dictionary<string, object>();
        string[] keys = ["AppSurfaceBrush", "AppTextBrush", "AppMutedTextBrush", "AppLogSuccessBrush", "AppLogWarningBrush", "AppLogInfoBrush"];
        foreach (string key in keys) if (resources.Contains(key)) previous[key] = resources[key];
        var box = new RichTextBox { IsReadOnly = true };
        box.SetResourceReference(Control.BackgroundProperty, "AppSurfaceBrush");
        box.SetResourceReference(Control.ForegroundProperty, "AppTextBrush");
        box.Document.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AppTextBrush");
        var host = new Window { Content = box, Width = 720, Height = 180, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        LogAppearance.Append(box, "[12:00] ", "Route ready", Brushes.DarkOliveGreen);
        LogAppearance.Append(box, "[12:01] ", "Inventory changed", Brushes.OrangeRed);
        foreach (bool dark in new[] { true, false, true }) {
            resources[keys[0]] = dark ? Brushes.Black : Brushes.White;
            resources[keys[1]] = dark ? Brushes.White : Brushes.Black;
            resources[keys[2]] = dark ? Brushes.LightGray : Brushes.DimGray;
            resources[keys[3]] = dark ? Brushes.LightGreen : Brushes.DarkGreen;
            resources[keys[4]] = dark ? Brushes.LightSalmon : Brushes.DarkRed;
            resources[keys[5]] = dark ? Brushes.LightBlue : Brushes.DarkBlue;
            Render(box, 700, 150, dark ? "log-dark.png" : "log-light.png");
            var paragraphs = box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().TakeLast(2).ToArray();
            if (box.Background != resources[keys[0]]
                || paragraphs[0].Inlines.LastInline.Foreground != resources[keys[3]]
                || paragraphs[1].Inlines.LastInline.Foreground != resources[keys[4]])
                throw new Exception($"Existing log lines did not follow the theme palette: {dark}, background={box.Background}, success={paragraphs[0].Inlines.LastInline.Foreground}, warning={paragraphs[1].Inlines.LastInline.Foreground}.");
        }
        host.Close();
        foreach (string key in keys) { if (previous.TryGetValue(key, out var value)) resources[key] = value; else resources.Remove(key); }
        Console.WriteLine("PASS: existing log text and surfaces update through dark/light/dark palette changes.");
    }
    private static int VerifyLongSearch(string path, double seconds) {
        var session = TaggedTransportStorage.Load<TaggedTransportSession>(path) ?? throw new Exception("Missing supplied save.");
        var request = session.Request with { Settings = session.Request.Settings with {
            SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Custom) with { TotalTarget = TimeSpan.FromSeconds(seconds) }
        } };
        var watch = System.Diagnostics.Stopwatch.StartNew(); double last = -30;
        var result = new TaggedTransportPlanner().Plan(request, progress: p => {
            if (p.ElapsedSeconds - last < 30) return;
            last = p.ElapsedSeconds;
            Console.WriteLine($"Search {p.ElapsedSeconds:F1}/{p.BudgetSeconds:F1}s; candidates={p.ExpandedStates}; distance={p.BestDistance}; memory={Environment.WorkingSet / 1048576}MB");
        }, preferredTaggedPlan: session.Plan);
        if (result.Plan is null || result.PlannedRequest is null) throw new Exception(result.Message);
        if (watch.Elapsed.TotalSeconds < seconds - 0.1) throw new Exception("Custom search ended before its deadline.");
        if (!new TaggedTransportSimulator(result.PlannedRequest).Verify(result.Plan, out _, out string error)) throw new Exception(error);
        if (result.Plan.Steps.First().Action.Kind == TaggedActionKind.Sail) throw new Exception("Unrequested departure travel remains.");
        if (result.Plan.Distance > result.Plan.ShipOnlyDistance) throw new Exception("TAG replaced a shorter verified ship plan.");
        TaggedTransportStorage.Save(Path.Combine(AppContext.BaseDirectory, "tagged-long-search-result.json"), new TaggedTransportSession(1, result.PlannedRequest, result.Plan, 0));
        Console.WriteLine($"PASS actual-save search {watch.Elapsed.TotalSeconds:F2}s; start={result.PlannedRequest.Settings.StartIsland}; distance={result.Plan.Distance}; routes={TaggedTransportRoutes.Build(result.PlannedRequest, result.Plan).Length}; alt={result.Plan.Steps.Any(s => s.Action.From.StartsWith("alt") || s.Action.To.StartsWith("alt"))}; {result.Message}");
        return 0;
    }
    private static void VerifySharedMap(TaggedRouteControl control, string sessionPath) {
        var originalSession = control.Session!;
        var oldIslands = App.listIslands;
        var oldItems = App.listItems;
        App.listItems = control.Session!.Request.Items.Values.Select(i => new Items(i.DisplayName, i.ItemId, i.Level.ToString())).ToList();
        string catalogPath = Path.Combine(Path.GetDirectoryName(sessionPath)!, "Islands.csv");
        if (!File.Exists(catalogPath)) catalogPath = Path.Combine("Resources", "Islands.csv");
        App.listIslands = File.ReadLines(catalogPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => {
            var fields = line.Split(',');
            double N(int index) => double.Parse(fields[index], System.Globalization.CultureInfo.InvariantCulture);
            var island = new Islands(Enum.Parse<EnumLists.Island>(fields[0]), 0) {
                IslandsThickness = new Thickness(N(2), N(3), N(4), N(5)),
                NavigationX = N(6), NavigationY = N(7), NavigationSource = fields[8]
            };
            if (control.Session!.Request.Points.TryGetValue(fields[0], out var point)) {
                island.NavigationX = point.X; island.NavigationY = point.Y;
            }
            return island;
        }).ToList();
        App.myPVM = new(); App.myCVM = new(); App.myCargoProperty = new(originalSession.Request.ShipOccupiedLT, originalSession.Request.ShipLimitLT);
        App.myStorageVM = new();
        var recordedStock = originalSession.WorkspaceInputs ?? originalSession.SettlementBaseline ?? originalSession.Request;
        using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false)) foreach (var item in originalSession.Request.Items.Values) {
            int Stock(string wh) => recordedStock.Warehouses.GetValueOrDefault(wh)?.GetValueOrDefault(item.ItemId) ?? 0;
            App.myStorageVM.StorageCollection.Add(new Items(item.DisplayName, item.ItemId, item.Level.ToString(),
                _intStorageVelia: Stock("Velia"), _intStorageIliya: Stock("Iliya"), _intStorageEpheria: Stock("Epheria"), _intStorageAncado: Stock("Ancado")));
        }
        App.myPVM.BarterCollection = new(originalSession.Request.Trades.Select(t => {
            var a = originalSession.Request.Items[t.InputId]; var b = originalSession.Request.Items[t.OutputId];
            var row = new Barter(App.listIslands.Single(i => i.IslandsName == t.IslandId),
                new Items(a.DisplayName, a.ItemId, a.Level.ToString(), t.InputPerExchange),
                new Items(b.DisplayName, b.ItemId, b.Level.ToString(), t.OutputPerExchange), t.Exchanges);
            typeof(Barter).GetField("plannerRowId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(row, t.RowId);
            return row;
        }));
        using var coordinator = new iBarter.ViewModel.AutomaticRouteCoordinator(App.myStorageVM, App.myCargoProperty, App.myCVM, () => control);
        App.myRouteCoordinator = coordinator;
        var presentedBarter = coordinator.DisplayPlan!.Routes.SelectMany(route => route.Steps)
            .OfType<BarterStep>().First();
        var presentedSource = App.myPVM.BarterCollection.Single(row =>
            row.PlannerRowId == RouteTaskIdentity.PlannerRowId(presentedBarter.RowId));
        var presentedItems = originalSession.Request.Items
            .Where(pair => pair.Key == presentedBarter.Consumed.ItemId || pair.Key == presentedBarter.Produced.ItemId)
            .ToDictionary();
        var presentedCard = new iBarter.ViewModel.BarterRouteStepViewModel(
            presentedBarter, presentedSource.IsLandNameDisplay, presentedItems,
            presentedBarter.Load.PeakTotalLT, presentedSource);
        if (presentedCard.ExchangeCount is not > 0
            || !presentedCard.Title.Contains(presentedCard.ExchangeCount.Value.ToString(), StringComparison.Ordinal))
            throw new Exception("The ship cargo exchange title does not show this step's exchange count.");
        var map = new MapControl();
        try {
            Render(map, 1000, 700, "tagged-map-initial.png");
            control.DisplayChanged += Rebuild;
            var selector = (ComboBox)control.FindName("RouteSelector");
            for (int round = 0; round < 2; round++) {
                for (int index = 0; index < selector.Items.Count; index++) {
                    selector.SelectedIndex = index;
                    map.IslandsButtonInitialisation();
                    map.IslandsButtonRearrange();
                    Render(map, 1000, 700, "tagged-map-route-" + index + ".png");
                    var expected = RouteStepLabelPlanner.PlanLabels(coordinator.DisplayPlan, coordinator.DisplayShowAllRoutes,
                        coordinator.DisplaySelectedRouteNumber, new Dictionary<string, string>());
                    var actual = VisualChildren<FrameworkElement>(map).Select(e => e.Tag).OfType<RouteStepMapLabel>().ToArray();
                    if (!expected.Select(l => l.Identity).Order().SequenceEqual(actual.Select(l => l.Identity).Order()))
                        throw new Exception($"Map text mismatch switching route {index}: {actual.Length}/{expected.Count}. Missing: {string.Join(", ", expected.Where(l => actual.All(a => a.Identity != l.Identity)).Select(l => l.Identity + ":" + l.IslandId))}; actual: {string.Join(", ", actual.Select(l => l.Identity + ":" + l.IslandId))}");
                    var pins = VisualChildren<System.Windows.Shapes.Rectangle>(map)
                        .Select(e => e.Tag?.GetType().GetProperty("Pin")?.GetValue(e.Tag)).OfType<RouteStepBarterPin>().ToArray();
                    if (!pins.Select(p => p.Identity).Order().SequenceEqual(RouteStepBarterPinPlanner.Plan(expected).Select(p => p.Identity).Order()))
                        throw new Exception("Barter squares do not match the current visible TAG exchanges.");
                    if (!coordinator.HasAutomaticDisplay || coordinator.GetRenderSnapshot([]).Paths.Count != control.RenderSnapshot()!.Paths.Count)
                        throw new Exception("Map paths and labels did not use the same TAG selection.");
                }
            }
            selector.SelectedIndex = 0;
            var before = control.Session!;
            var labels = RouteStepLabelPlanner.PlanLabels(coordinator.DisplayPlan, true, null, new Dictionary<string, string>());
            var barter = labels.FirstOrDefault(l => l.RouteNumber == 1 && l.IslandId == "Pujara")
                ?? labels.First();
            selector.SelectedIndex = barter.RouteNumber;
            var route = TaggedTransportRoutes.Build(before.Request, before.Plan).Single(r => r.Number == barter.RouteNumber);
            var expectedRemaining = TaggedCompletionReplanner.Remaining(before, route.Start + barter.StepIndex);
            WaitOnUi(control.CompleteMapStep(barter.RouteNumber, barter.StepIndex));
            if (control.Session == before || control.Session.CompletedSteps != 0) throw new Exception("Map completion did not replan only the selected exchange: "
                + ((TextBlock)control.FindName("StatusText")).Text);
            int expectedSelection = Math.Min(barter.RouteNumber, TaggedTransportRoutes.Build(control.Session.Request, control.Session.Plan).Length);
            if (control.SelectedRouteNumber != expectedSelection || coordinator.DisplayShowAllRoutes
                || coordinator.GetRenderSnapshot([]).Paths.Any(p => p.RouteNumber != expectedSelection))
                throw new Exception("Map completion reset the selected route to all routes.");
            VerifyNextBarterFocus(control);
            if (control.FocusedSegment is not { } focus || !coordinator.IsFocusedMarker(focus.Route, focus.To)
                || !coordinator.IsFocusedSegment(focus.Route, focus.From, focus.To))
                throw new Exception("The next TAG exchange was not highlighted through the shared map coordinator.");
            if (!expectedRemaining.Trades.OrderBy(t => t.RowId).SequenceEqual(control.Session.Request.Trades.OrderBy(t => t.RowId)))
                throw new Exception("An unselected earlier/later exchange was removed.");
            foreach (var row in App.myPVM.BarterCollection) {
                var remainingRow = expectedRemaining.Trades.FirstOrDefault(t => t.RowId == row.PlannerRowId);
                if (row.ExchangeDone != (remainingRow is null) || remainingRow is not null && row.ExchangeQuantity != remainingRow.Exchanges)
                    throw new Exception("Planner completion did not affect only the selected task.");
            }
            control.Session.Current();
            Render(map, 1000, 700, "tagged-map-completed.png");
            if (new TaggedRouteControl().Session!.Request.Fingerprint() != control.Session.Request.Fingerprint())
                throw new Exception("Independent completion did not survive reload.");
            if (!new TaggedRouteControl().ValidateRestoredWorkspace())
                throw new Exception("Recorded cargo after map completion invalidated a matching workspace on restart.");
            var carried = control.Session.Request.Settings.InitialCargo;
            string location = control.Session.Request.Settings.StartIsland;
            var retainedSession = control.Session;
            coordinator.ClearRouteDisplayForPlanning();
            map.IslandsButtonInitialisation();
            if (control.Session != retainedSession || coordinator.DisplayPlan is not null || coordinator.GetRenderSnapshot([]).Paths.Count != 0
                || ((ListBox)control.FindName("StepsList")).Items.Count != 0 || selector.Items.Count != 0
                || VisualChildren<FrameworkElement>(map).Any(e => e.Tag is RouteStepMapLabel))
                throw new Exception("Auto Plan did not clear the TAG display while retaining actual cargo.");
            WaitOnUi(control.GenerateAsync(control.Session.WorkspaceInputs!, profile: RouteOptimizationProfile.For(RouteOptimizationMode.Quick)));
            if (control.IsDisplayCleared || control.SelectedRouteNumber is null || coordinator.DisplayPlan is null)
                throw new Exception("Auto Plan did not republish a selected route after clearing.");
            if (control.Session.Request.Settings.StartIsland != location || !carried.SequenceEqual(control.Session.Request.Settings.InitialCargo))
                throw new Exception("The main Auto Plan entry lost carried cargo or restarted at a warehouse.");
            if (!new TaggedRouteControl().ValidateRestoredWorkspace())
                throw new Exception("Continued Auto Plan did not preserve its workspace restore snapshot.");
            foreach (string button in new[] { "NextButton", "UndoButton", "ReplanButton", "SettleButton" })
                if (((Button)control.FindName(button)).Visibility != Visibility.Collapsed) throw new Exception("Obsolete mandatory action button is still visible.");
            if (((TextBlock)control.FindName("ExecutionHelp")).Visibility != Visibility.Collapsed)
                throw new Exception("Execution help text is still visible.");
            control.DisplayChanged -= Rebuild;
            Console.WriteLine("PASS: WPF map labels/pins survive route switching; middle-exchange completion preserves all other quantities, replays and reloads; extra action buttons are hidden.");
            void Rebuild(object? sender, EventArgs args) => map.IslandsButtonInitialisation();
        }
        finally { map.myTimer.Stop(); App.myRouteCoordinator = null!; App.listIslands = oldIslands; App.listItems = oldItems;
            TaggedTransportStorage.Save(Path.Combine(AppContext.BaseDirectory, "Resources", "tagged-transport-session.json"), originalSession); }
    }
    private static void WaitOnUi(Task task) {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) => { if (task.IsCompleted || watch.Elapsed.TotalSeconds > 30) frame.Continue = false; };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        if (!task.IsCompleted) throw new Exception("UI operation timed out.");
        task.GetAwaiter().GetResult();
    }
    private static void VerifyChineseSettings() {
        var service = iBarter.Localization.LanguageService.Instance;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var current = service.GetType().GetField("_current", flags)!;
        var oldLanguage = service.Current; var oldIslands = App.listIslands;
        var typography = typeof(App).GetMethod("ApplyLocalizedTypography", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        void Language(iBarter.Localization.AppLanguage language) {
            current.SetValue(service, language); // isolated smoke: no user.settings write
            typography.Invoke(null, null);
            ((EventHandler?)service.GetType().GetField("LanguageChanged", flags)!.GetValue(service))?.Invoke(service, EventArgs.Empty);
        }
        App.listIslands = File.ReadLines("Resources/Islands.zh-TW.csv").Skip(1).Select(line => line.Split(','))
            .Where(f => f.Length >= 2 && Enum.TryParse<EnumLists.Island>(f[0], out _))
            .Select(f => new Islands(Enum.Parse<EnumLists.Island>(f[0]), 0) { IslandsNameZhTw = f[1], NavigationX = 0, NavigationY = 0 }).ToList();
        TaggedSettingsWindow? window = null;
        try {
            Language(iBarter.Localization.AppLanguage.TraditionalChinese);
            window = new TaggedSettingsWindow(new()) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            var tabs = VisualChildren<TabControl>(window).Single(); tabs.SelectedIndex = 2;
            Render((FrameworkElement)window.Content, 790, 670, "tagged-ports-chinese.png");
            var grid = VisualChildren<DataGrid>(window).Single();
            var first = grid.Items[0];
            string Cell() => ((TextBlock)grid.Columns[1].GetCellContent(first)).Text;
            if (Cell() != "贝尔利亚村庄") throw new Exception("Ports did not use the Chinese island name.");
            if (((TextBlock)grid.Columns[2].GetCellContent(first)).Text != "克鲁瓦") throw new Exception("Wharf NPC did not use its Chinese name.");
            if (((TextBlock)grid.Columns[1].GetCellContent(first)).FontWeight != FontWeights.Normal)
                throw new Exception("Selected tab leaked its bold header weight into Chinese table cells.");
            foreach (var text in VisualChildren<TextBlock>(window))
                if (text.FontFamily.Source != ((FontFamily)Application.Current.Resources["AppFontFamily"]).Source
                    || TextOptions.GetTextFormattingMode(text) != TextFormattingMode.Ideal
                    || TextOptions.GetTextRenderingMode(text) != TextRenderingMode.Grayscale)
                    throw new Exception("TAG Chinese text differs from the shared typography.");
            Language(iBarter.Localization.AppLanguage.English);
            Render((FrameworkElement)window.Content, 790, 670, "tagged-ports-english.png");
            if (Cell() != "Velia") throw new Exception("Open ports grid did not switch back to English.");
            if (((TextBlock)grid.Columns[2].GetCellContent(first)).Text != "Croix") throw new Exception("Wharf NPC did not switch back to English.");
            Language(iBarter.Localization.AppLanguage.TraditionalChinese);
            tabs.SelectedIndex = 0;
            Render((FrameworkElement)window.Content, 790, 670, "tagged-departure-chinese.png");
            var departure = VisualChildren<ComboBox>(window).First();
            if (departure.SelectedValue as string != "Velia" || ((Islands)departure.SelectedItem).IslandsNameDisplay != "贝尔利亚村庄")
                throw new Exception("Localized departure changed its canonical saved ID.");
            if (window.Result.Ports[0].IslandId != "Velia") throw new Exception("Translation mutated port IDs.");
            Console.WriteLine("PASS: Chinese font/rendering matches main typography; live Chinese/English port names and departure preserve canonical IDs.");
        }
        finally { window?.Close(); Language(oldLanguage); App.listIslands = oldIslands; }
    }
    private static void VerifyRouteSelection(TaggedTransportRequest request, string folder) {
        var sim = new TaggedTransportSimulator(request); var state = sim.Initial();
        for (int trip = 0; trip < 2; trip++) {
            foreach (var action in new TaggedAction[] {
                new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 2),
                new(TaggedActionKind.Sail, "Island", "Velia"),
                new(TaggedActionKind.Barter, "Island", Quantity: 2, TradeIndex: 0),
                new(TaggedActionKind.Sail, "Velia", "Island"),
                new(TaggedActionKind.Transfer, "Velia", "ship", "warehouse:Velia", "b", 2)
            }) if (!sim.TryApply(state, action, out state, out string error)) throw new Exception(error);
        }
        var plan = new TaggedTransportPlan(request.Fingerprint(), state.Steps.ToArray(), state.Seconds,
            state.SailingSeconds, state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-session.json"), new TaggedTransportSession(1, request, plan, 0));
        var control = new TaggedRouteControl();
        VerifyLegacyModeSurvivesToggle(control, request);
        Render(control, 350, 900, "tagged-routes-all.png");
        VerifyCardNumbering(control);
        var selector = (ComboBox)control.FindName("RouteSelector");
        var steps = (ListBox)control.FindName("StepsList");
        if (selector.Items.Count != 3 || steps.Items.Count != 6 || control.RenderSnapshot()?.Paths.Count != 2)
            throw new Exception("Two-route overview failed.");
        selector.SelectedIndex = 2;
        if (steps.Items.Count != 3 || control.RenderSnapshot()?.Paths.Single().RouteNumber != 2 || control.Session!.CompletedSteps != 0)
            throw new Exception("Individual route selection changed execution or map incorrectly.");
        var reopened = new TaggedRouteControl();
        if (reopened.SelectedRouteNumber != 2 || reopened.RenderSnapshot()?.Paths.Single().RouteNumber != 2
            || reopened.Session!.CompletedSteps != 0) throw new Exception("Selected TAG route was not restored after reopening.");
        if (!control.FocusRouteSegment(1, "Velia", "Island") || steps.SelectedItem is null || !control.IsFocusedSegment(1, "Velia", "Island"))
            throw new Exception("Map-to-step focus failed.");
        if (new TaggedRouteControl().SelectedRouteNumber != 1) throw new Exception("Map-focused route selection was not saved.");
        selector.SelectedIndex = 2;
        Render(control, 350, 900, "tagged-route-single.png");
        selector.SelectedIndex = 0;
        if (steps.Items.Count != 6) throw new Exception("All routes were not restored.");
        reopened = new TaggedRouteControl();
        if (!reopened.ShowAllRoutes || reopened.RenderSnapshot()?.Paths.Count != 2) throw new Exception("All-routes selection was not restored after reopening.");
        var next = (Button)control.FindName("NextButton");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (control.Session?.CompletedSteps != 3 || control.Session.Current().Location != "Island")
            throw new Exception("Hidden sailing was not completed with the visible barter operation.");
        ((Button)control.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (control.Session?.CompletedSteps != 1 || control.Session.Current().Location != "Velia")
            throw new Exception("Undo did not restore the previous visible operation.");
        for (int i = 0; i < 5; i++) next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (control.Session?.CompletedSteps != 10 || !((Button)control.FindName("SettleButton")).IsEnabled)
            throw new Exception("Hidden legs left unfinishable execution steps.");
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-session.json"), new TaggedTransportSession(1, request, plan, 0));
        control = new TaggedRouteControl();
        WaitOnUi(control.CompleteMapStep(2, 2)); // exact second-trip half of the same Planner row
        if (!control.ShowAllRoutes) throw new Exception("Completion changed an explicit All routes selection.");
        if (control.Session!.CompletedSteps != 0 || control.Session.Request.Trades.Single().Exchanges != 2)
            throw new Exception("Completing the later split segment removed the earlier one.");
        VerifyNextBarterFocus(control);
        var remainingLabel = RouteStepLabelPlanner.PlanLabels(control.DisplayPlan, true, null, new Dictionary<string, string>()).Single();
        WaitOnUi(control.CompleteMapStep(remainingLabel.RouteNumber, remainingLabel.StepIndex));
        if (!control.Session.MapPlanCompleted || control.DisplayPlan is not null || control.RenderSnapshot()!.Paths.Count != 0
            || new TaggedRouteControl().Session?.MapPlanCompleted != true)
            throw new Exception("The last exchange required an extra finish button or did not persist completion.");
        if (((ListBox)control.FindName("StepsList")).SelectedItem is not null || control.FocusedSegment is not null)
            throw new Exception("The completed plan retained a stale selected exchange.");
        Console.WriteLine("PASS: completing the second split segment retains the first; final completion clears and restores without a settlement button.");
    }
    private static void VerifyLegacyModeSurvivesToggle(TaggedRouteControl control, TaggedTransportRequest request) {
        using var coordinator = new iBarter.ViewModel.AutomaticRouteCoordinator(App.myStorageVM, new(0, 2000), new(), () => control);
        App.myRouteCoordinator = coordinator;
        try {
            var ordinaryRequest = new AutomaticRoutePlanningRequest(
                [new("original", "Island", request.Points["Island"], "a", 1, "b", 1)], request.Items,
                [new("Velia", "Velia", request.Points["Velia"], request.Warehouses["Velia"])], 0, 2000, new(250000, 150), "toggle-smoke");
            var ordinary = new AutomaticRoutePlanner().Plan(ordinaryRequest, RouteOptimizationProfile.For(RouteOptimizationMode.Quick));
            if (!coordinator.PublishGeneratedPlan(ordinaryRequest, ordinary, RouteOptimizationMode.Quick)) throw new Exception("Could not seed ordinary route.");
            var originalPlan = coordinator.CurrentPlan;
            var enabled = (CheckBox)control.FindName("EnabledBox");
            enabled.IsChecked = false;
            enabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (coordinator.Mode != iBarter.ViewModel.CargoMode.AutomaticRoute || coordinator.DisplayPlan != originalPlan)
                throw new Exception("Disabling TAG did not restore the ordinary route.");
            coordinator.SelectRoute(1);
            coordinator.ClearRouteDisplayForPlanning();
            coordinator.RefreshLocalization();
            if (coordinator.CurrentPlan != originalPlan || coordinator.DisplayPlan is not null
                || coordinator.VisibleAutomaticSteps.Count != 0 || coordinator.RouteOptions.Count != 0
                || coordinator.GetRenderSnapshot([]).Paths.Count != 0 || !coordinator.HasAutomaticDisplay)
                throw new Exception("Auto Plan did not clear the ordinary display while retaining its search incumbent.");
            if (!coordinator.PublishGeneratedPlan(ordinaryRequest, ordinary, RouteOptimizationMode.Quick)
                || coordinator.DisplayPlan is null || coordinator.SelectedRouteNumber != 1 || coordinator.ShowAllRoutes)
                throw new Exception("Ordinary publication did not restore the selected route.");
            coordinator.SelectAll();
            coordinator.ClearRouteDisplayForPlanning();
            if (!coordinator.PublishGeneratedPlan(ordinaryRequest, ordinary, RouteOptimizationMode.Quick) || !coordinator.ShowAllRoutes)
                throw new Exception("Ordinary publication changed an explicit All routes selection.");
            originalPlan = coordinator.CurrentPlan;
            enabled.IsChecked = true;
            enabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (coordinator.Mode != iBarter.ViewModel.CargoMode.AutomaticRoute || coordinator.CurrentPlan != originalPlan || coordinator.DisplayPlan == originalPlan)
                throw new Exception("Enabling TAG changed the original route state.");
        }
        finally { App.myRouteCoordinator = null!; }
    }
    private static void VerifySearchControls(TaggedTransportRequest request, TaggedTransportPlan plan, string folder) {
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-session.json"), new TaggedTransportSession(1, request, plan, 0));
        var control = new TaggedRouteControl();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var feedback = new TaggedSearchFeedback();
        control.ClearRouteDisplayForPlanning();
        var task = control.GenerateAsync(request, profile: RouteOptimizationProfile.For(RouteOptimizationMode.Custom, TimeSpan.FromMinutes(10)), searchProgress: feedback.Update);
        if (control.DisplayPlan is not null || control.RenderSnapshot()!.Paths.Count != 0
            || ((ListBox)control.FindName("StepsList")).Items.Count != 0)
            throw new Exception("Old TAG routes remained visible at search start.");
        bool triedProgress = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => {
            if (!triedProgress) {
                triedProgress = true;
                ((ComboBox)control.FindName("RouteSelector")).SelectedIndex = 1;
                ((Button)control.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (control.Session!.CompletedSteps != 0 || ((Button)control.FindName("NextButton")).IsEnabled)
                    throw new Exception("Route selection re-enabled execution while searching.");
            }
            if (((Button)control.FindName("CancelButton")).Visibility != Visibility.Collapsed) throw new Exception("TAG cargo duplicates the shared toolbar progress controls.");
            if (watch.Elapsed.TotalSeconds >= 1) control.CancelSearch();
            if (task.IsCompleted || watch.Elapsed.TotalSeconds > 15) frame.Continue = false;
        };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!task.IsCompleted) throw new Exception("Stop did not finish TAG search.");
        var result = task.GetAwaiter().GetResult();
        if (result.Plan is null || control.Session!.Request.Settings.SearchProfile?.TotalTarget != TimeSpan.FromMinutes(10)
            || control.DisplayPlan is null || control.IsDisplayCleared || !((Button)control.FindName("NextButton")).IsEnabled)
            throw new Exception("TAG search UI did not pass the custom profile or retain the verified result on stop.");
        var retained = control.Session;
        control.ClearRouteDisplayForPlanning();
        WaitOnUi(control.GenerateAsync(request, stillCurrent: () => false, profile: RouteOptimizationProfile.For(RouteOptimizationMode.Quick)));
        if (control.Session != retained || control.DisplayPlan is not null || control.RenderSnapshot()!.Paths.Count != 0)
            throw new Exception("Rejected search restored the cleared route or erased recorded cargo.");
        Console.WriteLine("PASS: TAG UI passes the ten-minute profile, locks execution during search, and Stop retains a verified plan.");
    }
    private static void VerifySharedToolbar() {
        App.myPVM ??= new();
        var appXaml = System.Xml.Linq.XDocument.Load("App.xaml");
        var dictionary = new System.Xml.Linq.XElement(appXaml.Root!.Descendants()
            .First(e => e.Name.LocalName == "ResourceDictionary"));
        foreach (var ns in appXaml.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            dictionary.SetAttributeValue(ns.Name, ns.Value);
        foreach (var source in dictionary.Descendants().SelectMany(e => e.Attributes("Source"))
            .Where(a => a.Value.StartsWith("/Resources/")))
            source.Value = "pack://application:,,,/iBarter;component" + source.Value;
        Application.Current.Resources.MergedDictionaries.Add(
            (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()));
        Console.WriteLine("Toolbar: application resources loaded.");
        var control = new PlannerControl();
        Console.WriteLine("Toolbar: Planner constructed.");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var begin = typeof(PlannerControl).GetMethod("BeginExtremeSearchFeedback", flags)!;
        var refresh = typeof(PlannerControl).GetMethod("RefreshExtremeSearchFeedback", flags)!;
        var end = typeof(PlannerControl).GetMethod("EndExtremeSearchFeedback", flags)!;
        try {
            begin.Invoke(control, [RouteOptimizationProfile.For(RouteOptimizationMode.Custom, TimeSpan.FromMinutes(10)), true]);
            Console.WriteLine("Toolbar: TAG progress started.");
            var feedback = (TaggedSearchFeedback)typeof(PlannerControl).GetField("activeTaggedSearch", flags)!.GetValue(control)!;
            feedback.Update(new(75, 600, 1000, 161000, 3)); refresh.Invoke(control, null);
            var status = (FrameworkElement)control.FindName("Border_ExtremeSearchStatus");
            // Render the real status area without laying out unrelated scanner/data-grid controls.
            ((Panel)status.Parent).Children.Remove(status);
            var host = new Window { Content = status, Width = 1020, Height = 140, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false };
            try { host.Show(); Render(status, 1000, 100, "tagged-shared-toolbar.png", drainDispatcher: false); }
            finally { host.Close(); }
            if (!((TextBlock)control.FindName("Text_ExtremeSearchBest")).Text.Contains("161.00 km")
                || !((TextBlock)control.FindName("Text_ExtremeSearchHeader")).Text.Contains("TAG"))
                throw new Exception("Shared toolbar did not display TAG progress in kilometres.");
            feedback.Finish(new(null, "", Search: new(90, 1100, ExtremeSearchTerminationReason.UserCancelled)), TimeSpan.FromSeconds(90));
            end.Invoke(control, null);
            if (((Button)control.FindName("Button_DismissExtremeSearchStatus")).Visibility != Visibility.Visible)
                throw new Exception("TAG terminal toolbar cannot be dismissed.");
            begin.Invoke(control, [RouteOptimizationProfile.For(RouteOptimizationMode.Extreme), false]);
            if (typeof(PlannerControl).GetField("activeTaggedSearch", flags)!.GetValue(control) is not null)
                throw new Exception("Ordinary search inherited TAG feedback.");
            Console.WriteLine("PASS: the real Planner toolbar renders TAG progress, terminal state and km, then returns to ordinary search.");
        }
        finally { end.Invoke(control, null); }
    }
    private static int AuditCompletion(string path, string? island = null) {
        var session = TaggedTransportStorage.Load<TaggedTransportSession>(path)!;
        var routes = TaggedTransportRoutes.Build(session.Request, session.Plan);
        foreach (var route in routes) {
            foreach (int i in Enumerable.Range(route.Start, route.End - route.Start).Where(i => session.Plan.Steps[i].Action.Kind == TaggedActionKind.Barter)) {
                if (island is not null && session.Plan.Steps[i].Action.Location != island) continue;
                try {
                    var remaining = TaggedCompletionReplanner.AtCompletedExchange(session, i);
                    Console.WriteLine($"{route.Number}: {session.Plan.Steps[i].Action.Location} reconciled, {remaining.Trades.Length} exchanges remain");
                    var seed = TaggedCompletionReplanner.Seed(session, i, remaining);
                    Console.WriteLine($"Verified continuation seed: {seed is not null}");
                    var result = new TaggedTransportPlanner().Plan(remaining with { Settings = remaining.Settings with { SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick) } }, preferredTaggedPlan: seed);
                    if (result.Plan is null) throw new Exception(result.Message);
                    if (!new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out var error)) throw new Exception(error);
                    Console.WriteLine($"PASS: continuation {result.Plan.Distance / 1000:N2} km");
                } catch (Exception e) { Console.WriteLine("FAIL: " + session.Plan.Steps[i].Action.Location + " " + e.Message); return 1; }
            }
        }
        return 0;
    }
    private static int AuditPortTiming(string path) {
        var saved = TaggedTransportStorage.Load<TaggedTransportSession>(path)!;
        var sim = new TaggedTransportSimulator(saved.Request);
        if (!sim.Verify(saved.Plan, out var original, out var error)) throw new Exception(error);
        var type = typeof(TaggedTransportPlanner).Assembly.GetType("iBarter.Routing.TaggedPortDepartureOptimizer")!;
        var improved = (TaggedTransportState)type.GetMethod("Improve")!.Invoke(null, [sim, original, CancellationToken.None])!;
        var leg = improved.Steps.First(s => s.Action is { Kind: TaggedActionKind.Sail, From: "Midnight", Location: "Grandiha" });
        if (leg.ShipLT > saved.Request.ShipLimitLT || improved.Distance != original.Distance || improved.OverloadedDistance >= original.OverloadedDistance)
            throw new Exception("The Midnight departure remained overloaded or increased distance.");
        var plan = new TaggedTransportPlan(saved.Request.Fingerprint(), improved.Steps.ToArray(), improved.Seconds, improved.SailingSeconds,
            improved.Seconds - improved.SailingSeconds, improved.Distance, "BestKnownWithinLimit");
        if (!sim.Verify(plan, out _, out error)) throw new Exception(error);
        foreach (var cargo in original.Cargo)
            if (!cargo.Value.OrderBy(i => i.Key).SequenceEqual(improved.Cargo[cargo.Key].OrderBy(i => i.Key))) throw new Exception("Handling timing changed final cargo.");
        Console.WriteLine($"PASS: Midnight -> Grandiha departs at {leg.ShipLT:N0}/{saved.Request.ShipLimitLT:N0} LT; distance unchanged {plan.Distance / 1000:F2} km; overloaded distance {original.OverloadedDistance / 1000:F2} -> {improved.OverloadedDistance / 1000:F2} km.");
        var request = saved.Request with { Settings = saved.Request.Settings with { SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick) } };
        var result = new TaggedTransportPlanner().Plan(request, preferredTaggedPlan: saved.Plan);
        if (result.Plan is null || result.Plan.Distance > original.Distance
            || !new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out error)) throw new Exception("Planning integration failed: " + error);
        if (result.Plan.Steps.Any(s => s.Action is { Kind: TaggedActionKind.Sail, From: "Midnight", Location: "Grandiha" } && s.ShipLT > request.ShipLimitLT))
            throw new Exception("Search reintroduced the avoidable overloaded leg.");
        TaggedTransportStorage.Save(Path.Combine(AppContext.BaseDirectory, "tagged-port-timing-result.json"), new TaggedTransportSession(1, result.PlannedRequest!, result.Plan, 0));
        Console.WriteLine("PASS: Auto Plan retained the improvement and replayed all remaining exchanges.");
        return 0;
    }
    private static void VerifySaleRendering(TaggedTransportRequest original, string folder) {
        var request = original with {
            Items = new(original.Items) { ["b"] = original.Items["b"] with { Level = 7 } },
            Trades = [original.Trades[0] with { Exchanges = 2 }],
            Settings = original.Settings with { Ports = [.. original.Settings.Ports, new() { IslandId = "Island", Enabled = true }] }
        };
        var sim = new TaggedTransportSimulator(request); var state = sim.Initial();
        foreach (var action in new TaggedAction[] {
            new(TaggedActionKind.Transfer, "Velia", "warehouse:Velia", "ship", "a", 2),
            new(TaggedActionKind.Sail, "Island", "Velia"), new(TaggedActionKind.Barter, "Island", Quantity: 2, TradeIndex: 0),
            new(TaggedActionKind.Sell, "Island", "ship", "shop", "b", 2), new(TaggedActionKind.Sail, "Velia", "Island")
        }) if (!sim.TryApply(state, action, out state, out var error)) throw new Exception(error);
        var plan = new TaggedTransportPlan(request.Fingerprint(), state.Steps.ToArray(), state.Seconds, state.SailingSeconds,
            state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-settings.json"), request.Settings);
        TaggedTransportStorage.Save(Path.Combine(folder, "tagged-transport-session.json"), new TaggedTransportSession(1, request, plan, 0, SelectedRouteNumber: 1));
        var control = new TaggedRouteControl();
        Render(control, 350, 650, "tagged-lv7-sale.png");
        var card = ((ListBox)control.FindName("StepsList")).Items.Cast<object>().Single(c => (int)c.GetType().GetProperty("Index")!.GetValue(c)! == 3);
        var title = (string)card.GetType().GetProperty("Title")!.GetValue(card)!;
        if (!title.Contains("Sell") && !title.Contains("賣出")) throw new Exception("LV7 sale has no localized sale title.");
        var lines = ((System.Collections.IEnumerable)card.GetType().GetProperty("Lines")!.GetValue(card)!).Cast<object>().ToArray();
        if (lines.Length != 1 || !((string)lines[0].GetType().GetProperty("Text")!.GetValue(lines[0])!).Contains("× 2")
            || lines[0].GetType().GetProperty("Icon")!.GetValue(lines[0]) is null)
            throw new Exception("LV7 sale item name, count or icon is missing.");
        if (new TaggedRouteControl().SelectedRouteNumber != 1) throw new Exception("Sale route selection failed to restore.");
        Console.WriteLine("PASS: LV7 sale card shows item icon/count and restores selection without a transfer-to-character instruction.");
    }
    private static int AuditCharacterWeight(string path) {
        // Read old requests without accepting their now-illegal saved operations.
        var saved = System.Text.Json.JsonSerializer.Deserialize<TaggedTransportSession>(File.ReadAllText(path))!;
        var original = new TaggedTransportSimulator(saved.Request);
        if (original.Verify(saved.Plan, out _, out _)) throw new Exception("Expected the reported legacy overload to be rejected.");
        var request = saved.Request with { Settings = saved.Request.Settings with { SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick) } };
        var result = new TaggedTransportPlanner().Plan(request, preferredTaggedPlan: saved.Plan);
        if (result.Plan is null) throw new Exception("No corrected plan: " + result.Message);
        var sim = new TaggedTransportSimulator(result.PlannedRequest!);
        if (!sim.Verify(result.Plan, out var final, out var error)) throw new Exception(error);
        var state = sim.Initial(); int receives = 0;
        foreach (var step in result.Plan.Steps) {
            if (!sim.TryApply(state, step.Action, out state, out error)) throw new Exception(error);
            if (step.Action is { Kind: TaggedActionKind.Transfer, To: "main" or "alt" } action && !sim.Stackable(action.ItemId)) {
                var carrier = request.Settings.Carriers.Single(c => c.Id == action.To);
                if (action.Quantity != 1 || sim.Weight(state, action.To) > carrier.LimitLT * request.Settings.CharacterReceiveRatio)
                    throw new Exception("Corrected route still overfills a character.");
                receives++;
            }
        }
        var output = Path.Combine(AppContext.BaseDirectory, "tagged-weight-result.json");
        TaggedTransportStorage.Save(output, new TaggedTransportSession(1, result.PlannedRequest!, result.Plan, 0));
        if (TaggedTransportStorage.Load<TaggedTransportSession>(output) is null) throw new Exception("Corrected route did not restore.");
        Console.WriteLine($"PASS: old illegal route rejected; new route replayed/restored, {receives} individual character receives checked, all {request.Trades.Length} trades completed, {final.Distance / 1000:F2} km.");
        return 0;
    }
    private static int AuditLevelSeven(string path) {
        var saved = TaggedTransportStorage.Load<TaggedTransportSession>(path)!;
        if (!new TaggedTransportSimulator(saved.Request).Verify(saved.Plan, out var original, out var error)) throw new Exception(error);
        var request = saved.Request with { Settings = saved.Request.Settings with { SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick) } };
        var result = new TaggedTransportPlanner().Plan(request, preferredTaggedPlan: saved.Plan);
        if (result.Plan is null || !new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out var state, out error))
            throw new Exception("LV7 planning failed: " + error);
        if (state.SoldItems.Count == 0) throw new Exception("No LV7 sale was planned for the saved data.");
        foreach (var item in request.Items.Keys) {
            int before = original.Cargo.Sum(c => c.Value.GetValueOrDefault(item)) + original.SoldItems.GetValueOrDefault(item);
            int after = state.Cargo.Sum(c => c.Value.GetValueOrDefault(item)) + state.SoldItems.GetValueOrDefault(item);
            if (before != after) throw new Exception("LV7 planning changed inventory conservation for " + item);
        }
        if (state.Steps.Any(s => s.Action.Kind == TaggedActionKind.Transfer && request.Items[s.Action.ItemId].Level == 7))
            throw new Exception("Saved-data plan still transfers LV7 instead of selling it: " + string.Join("; ",
                state.Steps.Where(s => s.Action.Kind == TaggedActionKind.Transfer && request.Items[s.Action.ItemId].Level == 7)
                    .Select(s => $"{s.Action.Location}: {s.Action.From}->{s.Action.To} {s.Action.ItemId} x{s.Action.Quantity}; ship={s.ShipLT} main={s.MainLT} alt={s.AltLT}")));
        TaggedTransportStorage.Save(Path.Combine(AppContext.BaseDirectory, "tagged-lv7-result.json"),
            new TaggedTransportSession(1, result.PlannedRequest!, result.Plan, 0));
        Console.WriteLine($"PASS: saved-data plan sells {state.SoldItems.Values.Sum()} LV7 goods in {state.Steps.Count(s => s.Action.Kind == TaggedActionKind.Sell)} sale steps; no LV7 storage/character transfers; all exchanges and inventory conserved; {state.Distance / 1000:F2} km.");
        return 0;
    }
    private static int AuditHome(string path) {
        var saved = TaggedTransportStorage.Load<TaggedTransportSession>(path)!;
        var settings = saved.Request.Settings with { HomeWarehouseId = "Iliya", SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick) };
        settings.Ports.Single(p => p.IslandId == "Haemo").Enabled = true;
        var request = saved.Request with { Settings = settings };
        var sim = new TaggedTransportSimulator(request);
        var type = typeof(TaggedTransportPlanner).Assembly.GetType("iBarter.Routing.TaggedVoyageCompiler")!;
        var compiler = Activator.CreateInstance(type, sim, (Func<bool>)(() => false))!;
        var state = (TaggedTransportState?)type.GetMethod("Recompile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(compiler, [sim.Initial(), saved.Plan.Steps]);
        if (state is null) throw new Exception("Current saved order could not be recompiled with home cargo: "
            + type.GetField("LastFailure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(compiler));
        var seed = new TaggedTransportPlan(request.Fingerprint(), state.Steps.ToArray(), state.Seconds, state.SailingSeconds,
            state.Seconds - state.SailingSeconds, state.Distance, "BestKnownWithinLimit");
        if (!sim.Verify(seed, out _, out var error)) throw new Exception(error);
        if (!seed.Steps.Any(s => s.Action is { Kind: TaggedActionKind.Transfer, Location: "Epheria", ItemId: "800059", To: "main" or "alt" }))
            throw new Exception("Blue quartz was not parked on characters at Epheria.");
        if (seed.Steps.Any(s => s.Action is { Kind: TaggedActionKind.Transfer, ItemId: "800059", To: "warehouse:Epheria" }))
            throw new Exception("Blue quartz was left at Epheria.");
        Console.WriteLine($"PASS: home seed {seed.Distance / 1000:N2} km; quartz carried to Iliya; Haemo transfers: {seed.Steps.Count(s => s.Action is { Kind: TaggedActionKind.Transfer, Location: "Haemo" })}");
        var result = new TaggedTransportPlanner().Plan(request, preferredTaggedPlan: saved.Plan);
        if (result.Plan is null || !new TaggedTransportSimulator(result.PlannedRequest!).Verify(result.Plan, out _, out _)) throw new Exception("Home search failed.");
        if (result.Plan.Distance > seed.Distance + 0.001) throw new Exception("Home search lost the recompiled saved incumbent.");
        TaggedTransportStorage.Save(Path.Combine(AppContext.BaseDirectory, "home-result.json"), new TaggedTransportSession(1, result.PlannedRequest!, result.Plan, 0));
        Console.WriteLine($"PASS: home search {result.Plan.Distance / 1000:N2} km, {result.Plan.Steps.Length} steps; final port {result.Plan.Steps[^1].Action.Location}");
        var fresh = new TaggedTransportPlanner().Plan(request);
        if (fresh.Plan is null || !new TaggedTransportSimulator(fresh.PlannedRequest!).Verify(fresh.Plan, out _, out _)) throw new Exception("Fresh home search failed.");
        Console.WriteLine($"PASS: fresh home search without a saved seed {fresh.Plan.Distance / 1000:N2} km");
        return 0;
    }
    private static void VerifySettingsEditing() {
        var window = new TaggedSettingsWindow(new()) {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            ShowInTaskbar = false
        };
        TaggedTransportSettings? saved = null;
        window.SettingsChanged += value => saved = value;
        try {
            window.Show();
            var tabs = VisualChildren<TabControl>(window).Single();
            tabs.SelectedIndex = 1;
            window.UpdateLayout();
            var grid = VisualChildren<DataGrid>(window).Single();
            grid.SelectedItem = grid.Items[0];
            grid.CurrentCell = new DataGridCellInfo(grid.Items[0], grid.Columns[1]);
            grid.ScrollIntoView(grid.Items[0], grid.Columns[1]);
            window.UpdateLayout();
            var cell = VisualChildren<DataGridCell>(grid).First(x => x.Column == grid.Columns[1]);
            cell.Focus();
            if (!grid.BeginEdit() || !cell.IsEditing) throw new Exception("Carrier capacity editor cannot stay open.");
            var editor = VisualChildren<TextBox>(cell).Single();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!cell.IsEditing || !editor.IsKeyboardFocusWithin) throw new Exception("Auto-save interrupted cell focus.");
            editor.SelectAll(); editor.SelectedText = "2345.5";
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            tabs.SelectedIndex = 0;
            window.UpdateLayout();
            var number = VisualChildren<TextBox>(window).First();
            number.Focus(); number.SelectAll(); number.SelectedText = "32";
            VisualChildren<TextBox>(window).Skip(1).First().Focus();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (saved?.Carriers[0].LimitLT != 2345.5 || saved.ShipSlots != 32)
                throw new Exception("Focus changes did not auto-save edited values.");
        }
        finally { window.Close(); }
        if (window.Result.Carriers[0].LimitLT != 2345.5 || window.Result.ShipSlots != 32)
            throw new Exception("Edited settings were not applied.");
    }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject {
        // A not-shown Window has a logical Content root without a window visual yet.
        if (parent is Window window && window.Content is DependencyObject root) {
            foreach (var child in VisualChildren<T>(root)) yield return child;
            yield break;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }
}

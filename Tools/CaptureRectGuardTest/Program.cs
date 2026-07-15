using System.Reflection;
using System.Windows;
using iBarter;
using iBarter.View;
using PureDM;
using PureDM.DmSoft;

var islandResolver = new CFunctions();
if (islandResolver.IslandEnum("Velia") != EnumLists.Island.Velia) {
    Console.Error.WriteLine("Expected the canonical Velia catalog row to resolve to Velia.");
    return 1;
}

string startupSource = File.ReadAllText(Path.Combine(FindIBarterRepoRoot(), "MainWindow.xaml.cs"));
int cargoLoadIndex = startupSource.IndexOf("myShipCargo.RefreshData();", StringComparison.Ordinal);
int gameWorkerIndex = startupSource.IndexOf("PureDmWorker.Start();", StringComparison.Ordinal);
if (cargoLoadIndex < 0 || gameWorkerIndex < 0 || cargoLoadIndex > gameWorkerIndex) {
    Console.Error.WriteLine("Expected saved ship cargo properties to load before game-process attachment.");
    return 1;
}

string appStartupSource = File.ReadAllText(Path.Combine(FindIBarterRepoRoot(), "App.xaml.cs"));
int onStartupIndex = appStartupSource.IndexOf(
    "protected override void OnStartup", StringComparison.Ordinal);
int mainWindowConstructionIndex = appStartupSource.IndexOf(
    "myfmMain = new MainWindow();", StringComparison.Ordinal);
if (onStartupIndex < 0 || mainWindowConstructionIndex < onStartupIndex) {
    Console.Error.WriteLine(
        "Expected MainWindow construction after App.xaml resources are loaded in OnStartup.");
    return 1;
}

string fluentControlsSource = File.ReadAllText(Path.Combine(
    FindIBarterRepoRoot(), "Resources", "Styles", "FluentControls.xaml"));
if (!fluentControlsSource.Contains(
        "<Setter Property=\"Foreground\" Value=\"#35515E\" />", StringComparison.Ordinal)
    || !fluentControlsSource.Contains("Value=\"Windows11Dark\"", StringComparison.Ordinal)
    || !fluentControlsSource.Contains("Value=\"#E7EEF2\"", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Expected Fluent toolbar icons to use visible light and dark theme foreground colors.");
    return 1;
}

string plannerControlSource = File.ReadAllText(Path.Combine(
    FindIBarterRepoRoot(), "View", "PlannerControl.xaml.cs"));
if (!plannerControlSource.Contains("TryRestoreAutomaticRouteAfterLoad();", StringComparison.Ordinal)) {
    Console.Error.WriteLine("Expected every Planner load to restore a matching persisted automatic route.");
    return 1;
}

string shipCargoControlSource = File.ReadAllText(Path.Combine(
    FindIBarterRepoRoot(), "View", "ShipCargoControl.xaml.cs"));
if (!shipCargoControlSource.Contains(
        "ComboBoxAdv_RouteSelector.IsEnabled = coordinator?.RouteOptions.Count > 0", StringComparison.Ordinal)
    || !shipCargoControlSource.Contains("App.myCVM.ManualSteps", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Expected retained automatic routes to remain selectable and manual cargo to use projected cards.");
    return 1;
}
if (!shipCargoControlSource.Contains("DesignerProperties.GetIsInDesignMode(this)", StringComparison.Ordinal)
    || !shipCargoControlSource.Contains("if (IsDesignMode || App.myCVM is null) return;", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Expected ShipCargoControl to avoid runtime-only App state while Visual Studio renders the designer.");
    return 1;
}
foreach (string viewName in new[] {
    "StorageManagement.xaml.cs",
    "PlannerControl.xaml.cs",
    "BarterScanner.xaml.cs",
    "MapControl.xaml.cs",
}) {
    string source = File.ReadAllText(Path.Combine(FindIBarterRepoRoot(), "View", viewName));
    if (!source.Contains("DesignerProperties.GetIsInDesignMode(this)", StringComparison.Ordinal)) {
        Console.Error.WriteLine(
            $"Expected {viewName} to skip App runtime state while Visual Studio renders the designer.");
        return 1;
    }
}

string routeStepViewModelSource = File.ReadAllText(Path.Combine(
    FindIBarterRepoRoot(), "ViewModel", "AutomaticRouteStepViewModels.cs"));
if (!routeStepViewModelSource.Contains("SourceBarter", StringComparison.Ordinal)) {
    Console.Error.WriteLine("Expected manual route cards to retain their source Barter for drag and copy actions.");
    return 1;
}

string mapControlSource = File.ReadAllText(Path.Combine(
    FindIBarterRepoRoot(), "View", "MapControl.xaml.cs"));
string mapControlXaml = File.ReadAllText(Path.Combine(
    FindIBarterRepoRoot(), "View", "MapControl.xaml"));
if (!mapControlXaml.Contains("x:Name=\"MapViewport\"", StringComparison.Ordinal)
    || !mapControlXaml.Contains("x:Name=\"MapScaleTransform\"", StringComparison.Ordinal)
    || !mapControlXaml.Contains("x:Name=\"MapTranslateTransform\"", StringComparison.Ordinal)
    || !mapControlSource.Contains("MapViewport_MouseWheel", StringComparison.Ordinal)
    || !mapControlSource.Contains("MapViewport_MouseRightButtonDown", StringComparison.Ordinal)
    || !mapControlSource.Contains("ResetMapViewport", StringComparison.Ordinal)) {
    Console.Error.WriteLine("Expected a transform-based map viewport with zoom, pan, and reset controls.");
    return 1;
}
if (!mapControlSource.Contains("Math.Clamp", StringComparison.Ordinal)
    || !mapControlSource.Contains(", 11, 16)", StringComparison.Ordinal)
    || !mapControlSource.Contains("DrawRouteStepMarker", StringComparison.Ordinal)) {
    Console.Error.WriteLine("Expected bounded hybrid label scaling and numbered route-step markers.");
    return 1;
}
if (!File.Exists(Path.Combine(FindIBarterRepoRoot(), "View", "MapViewportState.cs"))) {
    Console.Error.WriteLine("Expected a dedicated map viewport state object for zoom and pan.");
    return 1;
}
var viewportState = new MapViewportState();
viewportState.ZoomAt(new Point(100, 50), 2);
if (Math.Abs(viewportState.Scale - 2) > 0.0001
    || Math.Abs(viewportState.OffsetX + 100) > 0.0001
    || Math.Abs(viewportState.OffsetY + 50) > 0.0001) {
    Console.Error.WriteLine("Expected map zoom to preserve the mouse anchor.");
    return 1;
}
viewportState.ZoomAt(new Point(0, 0), 100);
if (Math.Abs(viewportState.Scale - MapViewportState.MaxScale) > 0.0001) {
    Console.Error.WriteLine("Expected map zoom to clamp at the configured maximum.");
    return 1;
}
viewportState.PanBy(new Vector(12, -8));
viewportState.Reset();
if (viewportState.Scale != 1 || viewportState.OffsetX != 0 || viewportState.OffsetY != 0) {
    Console.Error.WriteLine("Expected map viewport reset to restore the default view.");
    return 1;
}
if (!mapControlSource.Contains("renderSnapshot.HighlightedIslandIds", StringComparison.Ordinal)
    || !mapControlSource.Contains("FontWeights.ExtraBold", StringComparison.Ordinal)) {
    Console.Error.WriteLine("Expected current route warehouse and barter islands to use strong map highlighting.");
    return 1;
}
if (!mapControlSource.Contains("MeasureLabelForPlacement(myLabel)", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Expected highlighted map labels to be remeasured before placement so bold text is not clipped.");
    return 1;
}
if (mapControlSource.Contains("label.Arrange(new Rect(measured))", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Map label measurement must not manually arrange child controls because the layout timer will cause position flicker.");
    return 1;
}
if (!mapControlSource.Contains("label.Width = measured.Width", StringComparison.Ordinal)
    || !mapControlSource.Contains("label.Height = measured.Height", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Expected the measured natural size to be pinned without bypassing the parent layout pass.");
    return 1;
}
if (mapControlSource.Contains("myLabel.Width = Double.NaN", StringComparison.Ordinal)
    || !mapControlSource.Contains("visual.LastMeasuredHighlight != routeHighlight", StringComparison.Ordinal)
    || !mapControlSource.Contains("visual.LastMeasuredContent != labelContent", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Map labels must only be remeasured when their highlight state or content changes, not on every timer tick.");
    return 1;
}
if (!mapControlSource.Contains("LabelPlacementWidth(_label1)", StringComparison.Ordinal)
    || !mapControlSource.Contains("LabelPlacementHeight(_label1)", StringComparison.Ordinal)
    || !mapControlSource.Contains("LabelPlacementWidth(_label)", StringComparison.Ordinal)
    || !mapControlSource.Contains("LabelPlacementHeight(_label)", StringComparison.Ordinal)) {
    Console.Error.WriteLine(
        "Collision and edge placement must use the newly measured explicit size instead of stale ActualWidth/ActualHeight values.");
    return 1;
}

var cv = new CV(
    () => IntPtr.Zero,
    () => null!,
    () => 2560,
    () => 1369,
    () => "",
    () => "",
    () => "",
    () => "",
    () => 0);

var method = typeof(CV).GetMethod(
    "TryNormalizeCaptureRectangle",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TryNormalizeCaptureRectangle");

object[] invokeArgs = { 0, 0, 2560, 1369, "" };
bool ok = (bool)method.Invoke(cv, invokeArgs)!;

if (!ok || (int)invokeArgs[2] != 2559 || (int)invokeArgs[3] != 1368) {
    Console.Error.WriteLine(
        $"Expected 0,0,2559,1368 but got {invokeArgs[0]},{invokeArgs[1]},{invokeArgs[2]},{invokeArgs[3]} ok={ok} reason={invokeArgs[4]}");
    return 1;
}

Console.WriteLine("Capture rectangle guard passed.");

var wordPageSegMethod = typeof(CV).GetMethod(
    "SelectPageSegMode",
    BindingFlags.Static | BindingFlags.NonPublic);
if (wordPageSegMethod == null) {
    Console.Error.WriteLine("Expected PureDM OCR page segmentation selector.");
    return 1;
}
var wordPageSeg = (Emgu.CV.OCR.PageSegMode)wordPageSegMethod.Invoke(
    null,
    new object?[] { CV.OCRType.Words, null })!;
if (wordPageSeg != Emgu.CV.OCR.PageSegMode.SingleLine) {
    Console.Error.WriteLine($"Expected word OCR to use SingleLine segmentation, got {wordPageSeg}.");
    return 1;
}
var numberPageSeg = (Emgu.CV.OCR.PageSegMode)wordPageSegMethod.Invoke(
    null,
    new object?[] { CV.OCRType.Number, null })!;
if (numberPageSeg != Emgu.CV.OCR.PageSegMode.Auto) {
    Console.Error.WriteLine($"Expected general numeric OCR to retain Auto segmentation, got {numberPageSeg}.");
    return 1;
}
var item2PageSeg = CV.SelectPageSegMode(
    CV.OCRType.Words,
    Emgu.CV.OCR.PageSegMode.Auto);
if (item2PageSeg != Emgu.CV.OCR.PageSegMode.Auto) {
    Console.Error.WriteLine($"Expected an explicit item2 OCR segmentation override to select Auto, got {item2PageSeg}.");
    return 1;
}
if (CFunctions.JoinItem2OcrLines("[7阶段]卡尔佩恩匠/\r\n珍珠项链\r\n")
    != "[7阶段]卡尔佩恩匠/珍珠项链") {
    Console.Error.WriteLine("Expected item2 OCR line breaks to be joined before catalog matching.");
    return 1;
}

// Remaining count is a bounded 0..10 domain. A semantic "N次" read wins;
// otherwise a short digit crop protects 0..9 and the long crop can only add
// the one valid two-digit value, 10.
if (CFunctions.ResolveRemainingCount(-1, 0, 2) != 0
    || CFunctions.ResolveRemainingCount(-1, 1, 10) != 10
    || CFunctions.ResolveRemainingCount(-1, 5, -1) != 5
    || CFunctions.ResolveRemainingCount(10, 1, -1) != 10
    || CFunctions.ResolveRemainingCount(-1, -1, 52) != -1) {
    Console.Error.WriteLine("Expected remaining-count resolution to enforce the 0..10 domain.");
    return 1;
}

// The four Phase-A quantity ROIs are correlated samples from one pipeline,
// not four independent votes. Independent R/G reads must win a three-way tie
// by reliability rather than being outvoted by repeated Phase-A crops.
if (CFunctions.ResolveQuantityPipelineVotes(9, 3, -1, 2, true) != 2
    || CFunctions.ResolveQuantityPipelineVotes(4, 2, -1, 2, true) != 2
    || CFunctions.ResolveQuantityPipelineVotes(1, 1, 20, -1, true) != 20
    || CFunctions.ResolveQuantityPipelineVotes(42, 3, 142, -1, true) != 142
    || CFunctions.ResolveQuantityPipelineVotes(1, 3, 1, 2, false) != 1) {
    Console.Error.WriteLine("Expected quantity voting to count independent OCR pipelines once each.");
    return 1;
}

var numericLanguageMethod = typeof(CFunctions).GetMethod(
    "NumericOcrLanguage",
    BindingFlags.Static | BindingFlags.NonPublic);
if (numericLanguageMethod == null
    || (string?)numericLanguageMethod.Invoke(null, null) != "eng") {
    Console.Error.WriteLine("Expected numeric OCR to use the English digit model independently of UI language.");
    return 1;
}
var selectOcrLanguageMethod = typeof(CFunctions).GetMethod(
    "SelectOcrLanguage",
    BindingFlags.Static | BindingFlags.NonPublic);
if (selectOcrLanguageMethod == null
    || (string?)selectOcrLanguageMethod.Invoke(
        null,
        new object[] { CV.OCRType.Number }) != "eng") {
    Console.Error.WriteLine("Expected OCRType.Number to select the English digit model.");
    return 1;
}

var debugCaptureField = typeof(CFunctions).GetField(
    "SaveOcrDebugCapture",
    BindingFlags.Static | BindingFlags.Public);
if (debugCaptureField == null || debugCaptureField.GetValue(null) is not false) {
    Console.Error.WriteLine("Expected OCR debug BMP capture to default OFF after diagnosis.");
    return 1;
}

var boundedLocalOcrMethod = typeof(CFunctions).GetMethod(
    "RunLocalOcrBounded",
    BindingFlags.Static | BindingFlags.NonPublic);
var localOcrPoisonedProperty = typeof(CFunctions).GetProperty(
    "IsLocalOcrPoisoned",
    BindingFlags.Static | BindingFlags.NonPublic);
if (boundedLocalOcrMethod == null || localOcrPoisonedProperty == null) {
    Console.Error.WriteLine("Expected a bounded local OCR circuit breaker.");
    return 1;
}
var localOcrSw = System.Diagnostics.Stopwatch.StartNew();
int slowLocalResult = (int)boundedLocalOcrMethod.Invoke(
    null,
    new object[] {
        "contract-slow",
        (Func<int>)(() => { Thread.Sleep(200); return 7; }),
        50,
    })!;
localOcrSw.Stop();
bool secondLocalOperationRan = false;
int rejectedLocalResult = (int)boundedLocalOcrMethod.Invoke(
    null,
    new object[] {
        "contract-rejected",
        (Func<int>)(() => { secondLocalOperationRan = true; return 9; }),
        50,
    })!;
if (slowLocalResult != -1
    || localOcrSw.ElapsedMilliseconds >= 500
    || localOcrPoisonedProperty.GetValue(null) is not true
    || rejectedLocalResult != -1
    || secondLocalOperationRan) {
    Console.Error.WriteLine(
        $"Expected bounded local OCR timeout and fail-fast rejection; first={slowLocalResult} elapsed={localOcrSw.ElapsedMilliseconds}ms poisoned={localOcrPoisonedProperty.GetValue(null)} second={rejectedLocalResult} secondRan={secondLocalOperationRan}.");
    return 1;
}
var digitDiffMethod = typeof(CV).GetMethod(
    "PrepareDigitDiffForOcr",
    BindingFlags.Static | BindingFlags.NonPublic);
if (digitDiffMethod == null) {
    Console.Error.WriteLine("Expected a testable binary digit-diff preprocessing pipeline.");
    return 1;
}
using (var diffInput = new Emgu.CV.Image<Emgu.CV.Structure.Gray, byte>(4, 4)) {
    for (int y = 0; y < 4; y++) {
        for (int x = 0; x < 4; x++) {
            diffInput.Data[y, x, 0] = (byte)((x + y * 4) * 16);
        }
    }
    using var prepared = (Emgu.CV.Mat)digitDiffMethod.Invoke(
        null,
        new object[] { diffInput.Mat })!;
    if (prepared.Width != 20 || prepared.Height != 20) {
        Console.Error.WriteLine($"Expected 5x digit-diff output, got {prepared.Width}x{prepared.Height}.");
        return 1;
    }
    using var preparedImage = prepared.ToImage<Emgu.CV.Structure.Gray, byte>();
    foreach (byte value in preparedImage.Data) {
        if (value != 0 && value != 255) {
            Console.Error.WriteLine($"Expected binary digit-diff pixels, got {value}.");
            return 1;
        }
    }
}
using (var thinStroke = new Emgu.CV.Image<Emgu.CV.Structure.Gray, byte>(8, 8)) {
    for (int y = 1; y < 7; y++) {
        thinStroke.Data[y, 4, 0] = 255;
    }
    using var preparedStroke = (Emgu.CV.Mat)digitDiffMethod.Invoke(
        null,
        new object[] { thinStroke.Mat })!;
    using var preparedStrokeImage = preparedStroke.ToImage<Emgu.CV.Structure.Gray, byte>();
    int whitePixels = 0;
    foreach (byte value in preparedStrokeImage.Data) {
        if (value == 255) whitePixels++;
    }
    if (whitePixels == 0) {
        Console.Error.WriteLine("Expected a one-pixel digit stroke to survive preprocessing before OCR.");
        return 1;
    }
}

var captureLiveMethod = typeof(CV).GetMethod(
    "CaptureLiveByDMToMat",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("CaptureLiveByDMToMat");

Emgu.CV.Mat BuildGuardFrame() {
    using var image = new Emgu.CV.Image<Emgu.CV.Structure.Bgr, byte>(
        11,
        11,
        new Emgu.CV.Structure.Bgr(1, 2, 3));
    return image.Mat.Clone();
}

bool InvokeLiveCaptureExpectingSizeFailure(CV candidate) {
    try {
        var unexpected = (Emgu.CV.Mat?)captureLiveMethod.Invoke(
            candidate,
            new object[] { 0, 0, 10, 10 });
        unexpected?.Dispose();
        return false;
    }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) {
        return true;
    }
}

int throwingSizeCaptureCalls = 0;
using (var throwingSizeCv = new CV(
    () => IntPtr.Zero,
    () => null!,
    () => throw new InvalidOperationException("synthetic size lookup failure"),
    () => 90,
    () => "",
    () => "",
    () => "",
    () => "",
    () => 0,
    (_, _, _, _) => {
        throwingSizeCaptureCalls++;
        return BuildGuardFrame();
    })) {
    if (!InvokeLiveCaptureExpectingSizeFailure(throwingSizeCv)
        || throwingSizeCaptureCalls != 0) {
        Console.Error.WriteLine(
            $"Expected throwing client-size lookup to fail before live capture; calls={throwingSizeCaptureCalls}");
        return 1;
    }
}

int invalidSizeCaptureCalls = 0;
using (var invalidSizeCv = new CV(
    () => IntPtr.Zero,
    () => null!,
    () => 0,
    () => 0,
    () => "",
    () => "",
    () => "",
    () => "",
    () => 0,
    (_, _, _, _) => {
        invalidSizeCaptureCalls++;
        return BuildGuardFrame();
    })) {
    if (!InvokeLiveCaptureExpectingSizeFailure(invalidSizeCv)
        || invalidSizeCaptureCalls != 0) {
        Console.Error.WriteLine(
            $"Expected nonpositive client size to fail before live capture; calls={invalidSizeCaptureCalls}");
        return 1;
    }
}

Console.WriteLine("Capture size lookup fail-closed contract passed.");
var tempPathMethod = typeof(CV).GetMethod(
    "BuildCaptureTempPaths",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("BuildCaptureTempPaths");

object[] tempPathArgs = { "C:\\App\\", "", "" };
tempPathMethod.Invoke(null, tempPathArgs);
string relativeTempPath = (string)tempPathArgs[1];
string fullTempPath = (string)tempPathArgs[2];
if (!relativeTempPath.StartsWith(Path.Combine("Images", "Testing") + Path.DirectorySeparatorChar)
    || !relativeTempPath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
    || fullTempPath != Path.Combine("C:\\App\\", "Resources", relativeTempPath)) {
    Console.Error.WriteLine(
        $"Unexpected capture temp paths relative={relativeTempPath} full={fullTempPath}");
    return 1;
}

Console.WriteLine("Capture temp path contract passed.");
var cvWaitMethod = typeof(CV).GetMethod(
    "TryWaitForCaptureFileReady",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("CV.TryWaitForCaptureFileReady");

string cvWaitFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bmp");
Task? cvWriter = null;
try {
    cvWriter = Task.Run(async () => {
        await Task.Delay(60);
        await File.WriteAllBytesAsync(cvWaitFile, new byte[] { 1, 2, 3, 4 });
    });

    object[] cvWaitArgs = { cvWaitFile, 10, 20, 0L };
    bool cvWaitOk = (bool)cvWaitMethod.Invoke(null, cvWaitArgs)!;
    if (!cvWaitOk || (long)cvWaitArgs[3] != 4L) {
        Console.Error.WriteLine($"Expected delayed CV capture file to become ready; ok={cvWaitOk} length={cvWaitArgs[3]}");
        return 1;
    }
}
finally {
    cvWriter?.Wait();
    if (File.Exists(cvWaitFile)) {
        File.Delete(cvWaitFile);
    }
}

Console.WriteLine("Capture file wait passed.");
string cvGrowingFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bmp");
Task? cvGrowingWriter = null;
try {
    File.WriteAllBytes(cvGrowingFile, new byte[] { 1 });
    cvGrowingWriter = Task.Run(async () => {
        await Task.Delay(60);
        await File.WriteAllBytesAsync(cvGrowingFile, new byte[] { 1, 2, 3, 4, 5 });
    });

    object[] cvGrowingArgs = { cvGrowingFile, 10, 20, 0L };
    bool cvGrowingOk = (bool)cvWaitMethod.Invoke(null, cvGrowingArgs)!;
    if (!cvGrowingOk || (long)cvGrowingArgs[3] != 5L) {
        Console.Error.WriteLine($"Expected CV capture wait to wait for stable final length; ok={cvGrowingOk} length={cvGrowingArgs[3]}");
        return 1;
    }
}
finally {
    cvGrowingWriter?.Wait();
    if (File.Exists(cvGrowingFile)) {
        File.Delete(cvGrowingFile);
    }
}

Console.WriteLine("Capture file stable-length wait passed.");
var item2RetryRectMethod = typeof(CFunctions).GetMethod(
    "TryBuildRetryItem2OcrRectangle",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TryBuildRetryItem2OcrRectangle");

object[] item2Args = { 1116, 585, 25, 1182, 2560, 0, 0, 0, 0 };
bool item2Ok = (bool)item2RetryRectMethod.Invoke(null, item2Args)!;
if (!item2Ok
    || (int)item2Args[5] != 1492
    || (int)item2Args[6] != 560
    || (int)item2Args[7] != 1658
    || (int)item2Args[8] != 586) {
    Console.Error.WriteLine(
        $"Expected retry item2 rect 1492,560,1658,586 but got {item2Args[5]},{item2Args[6]},{item2Args[7]},{item2Args[8]} ok={item2Ok}");
    return 1;
}

Console.WriteLine("Scanner fallback geometry passed.");
var createScannerItemMethod = typeof(CFunctions).GetMethod(
    "CreateScannerItemFromCatalog",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("CreateScannerItemFromCatalog");

App.listItems = new List<Items> {
    new Items("Fancy Camel Hide", "800203", "6", 1) {
        ItemNameZhTw = "華麗的駱駝皮"
    }
};

object[] scannerItemArgs = { "800203", 7 };
var scannerItem = (Items)createScannerItemMethod.Invoke(null, scannerItemArgs)!;
if (scannerItem.ItemName != "Fancy Camel Hide"
    || scannerItem.ItemNameZhTw != "華麗的駱駝皮"
    || scannerItem.ItemID != "800203"
    || scannerItem.ItemLV != "6"
    || scannerItem.ItemNumber != 7) {
    Console.Error.WriteLine(
        $"Expected scanner item to preserve catalog zh-TW name; name={scannerItem.ItemName}, zh={scannerItem.ItemNameZhTw}, id={scannerItem.ItemID}, lv={scannerItem.ItemLV}, qty={scannerItem.ItemNumber}");
    return 1;
}

Console.WriteLine("Scanner localized item clone passed.");

var shouldSkipIconMethod = typeof(CFunctions).GetMethod(
    "ShouldSkipIconConfirmation",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("ShouldSkipIconConfirmation");

bool skipHighTier = (bool)shouldSkipIconMethod.Invoke(null, new object[] { scannerItem })!;
bool skipLowTier = (bool)shouldSkipIconMethod.Invoke(null, new object[] { new Items("Beer", "9213", "0", 300) })!;
if (!skipHighTier || skipLowTier) {
    Console.Error.WriteLine($"Expected icon confirmation skip only for high-tier items; high={skipHighTier} low={skipLowTier}");
    return 1;
}

Console.WriteLine("High-tier icon confirmation skip passed.");

string FindIBarterRepoRoot() {
    DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null) {
        if (File.Exists(Path.Combine(current.FullName, "iBarter.csproj"))) {
            return current.FullName;
        }
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate iBarter.csproj from test output directory.");
}

string iBarterRepoRoot = FindIBarterRepoRoot();
string iBarterProjectText = File.ReadAllText(Path.Combine(iBarterRepoRoot, "iBarter.csproj"));
if (iBarterProjectText.Contains("SetLargeAddressAware", StringComparison.OrdinalIgnoreCase)
    || iBarterProjectText.Contains("<LargeAddressAware", StringComparison.OrdinalIgnoreCase)) {
    Console.Error.WriteLine("iBarter.csproj must not enable or patch LargeAddressAware.");
    return 1;
}

string? builtAppHost = Directory
    .EnumerateFiles(Path.Combine(iBarterRepoRoot, "bin", "Debug"), "iBarter.exe", SearchOption.AllDirectories)
    .OrderByDescending(File.GetLastWriteTimeUtc)
    .FirstOrDefault();
if (builtAppHost == null) {
    Console.Error.WriteLine("Could not locate the Debug iBarter.exe apphost for PE verification.");
    return 1;
}

byte[] appHostBytes = File.ReadAllBytes(builtAppHost);
int peOffset = BitConverter.ToInt32(appHostBytes, 0x3c);
ushort coffCharacteristics = BitConverter.ToUInt16(appHostBytes, peOffset + 4 + 18);
int optionalHeaderOffset = peOffset + 24;
uint numberOfRvaAndSizes = BitConverter.ToUInt32(appHostBytes, optionalHeaderOffset + 92);
if ((coffCharacteristics & 0x0020) != 0 || numberOfRvaAndSizes != 16) {
    Console.Error.WriteLine(
        $"Expected non-LAA uncorrupted PE apphost; COFF=0x{coffCharacteristics:X4}, NumberOfRvaAndSizes={numberOfRvaAndSizes}, file={builtAppHost}");
    return 1;
}

Console.WriteLine("Non-LAA PE apphost contract passed.");

// A scan capture session must preserve the scanner's absolute client-coordinate
// contract while serving every recognition operation from one immutable frame.
int liveCaptureCalls = 0;
int syntheticFrameNumber = 1;
Emgu.CV.Mat BuildSyntheticFrame(int frameNumber) {
    var image = new Emgu.CV.Image<Emgu.CV.Structure.Bgr, byte>(
        120,
        90,
        new Emgu.CV.Structure.Bgr(30 + frameNumber, 20 + frameNumber, 10 + frameNumber));

    // An asymmetric exact template at absolute client coordinate (37,42).
    Emgu.CV.CvInvoke.Rectangle(
        image,
        new System.Drawing.Rectangle(37, 42, 8, 8),
        new Emgu.CV.Structure.MCvScalar(240, 20, 180),
        -1);
    Emgu.CV.CvInvoke.Line(
        image,
        new System.Drawing.Point(37, 42),
        new System.Drawing.Point(44, 49),
        new Emgu.CV.Structure.MCvScalar(5, 250, 40),
        2);
    // Marker at the safe raw frame's last pixel. The session must replicate
    // it into the missing right column and bottom row without shifting it.
    Emgu.CV.CvInvoke.Rectangle(
        image,
        new System.Drawing.Rectangle(118, 88, 1, 1),
        new Emgu.CV.Structure.MCvScalar(77, 88, 99 + frameNumber),
        -1);
    Emgu.CV.Mat result = image.Mat.Clone();
    image.Dispose();
    return result;
}

using (Emgu.CV.Mat normalizationSource = BuildSyntheticFrame(1)) {
    using Emgu.CV.Mat exactNormalized = CV.NormalizeCapturedFrame(normalizationSource, 120, 90);
    if (exactNormalized.Width != 120 || exactNormalized.Height != 90) {
        Console.Error.WriteLine("Expected exact captured frame to remain 120x90.");
        return 1;
    }

    using var rightShort = new Emgu.CV.Mat(
        normalizationSource,
        new System.Drawing.Rectangle(0, 0, 119, 90));
    using Emgu.CV.Mat rightNormalized = CV.NormalizeCapturedFrame(rightShort, 120, 90);
    if (rightNormalized.Width != 120 || rightNormalized.Height != 90) {
        Console.Error.WriteLine("Expected right-only shortfall to normalize to 120x90.");
        return 1;
    }

    using var bottomShort = new Emgu.CV.Mat(
        normalizationSource,
        new System.Drawing.Rectangle(0, 0, 120, 89));
    using Emgu.CV.Mat bottomNormalized = CV.NormalizeCapturedFrame(bottomShort, 120, 90);
    if (bottomNormalized.Width != 120 || bottomNormalized.Height != 90) {
        Console.Error.WriteLine("Expected bottom-only shortfall to normalize to 120x90.");
        return 1;
    }

    bool twoPixelShortfallRejected = false;
    using (var tooShort = new Emgu.CV.Mat(
        normalizationSource,
        new System.Drawing.Rectangle(0, 0, 118, 88))) {
        try {
            using Emgu.CV.Mat _ = CV.NormalizeCapturedFrame(tooShort, 120, 90);
        }
        catch (InvalidOperationException) {
            twoPixelShortfallRejected = true;
        }
    }

    bool oversizeRejected = false;
    try {
        using Emgu.CV.Mat _ = CV.NormalizeCapturedFrame(normalizationSource, 119, 90);
    }
    catch (InvalidOperationException) {
        oversizeRejected = true;
    }

    bool emptyRejected = false;
    using (var emptyFrame = new Emgu.CV.Mat()) {
        try {
            using Emgu.CV.Mat _ = CV.NormalizeCapturedFrame(emptyFrame, 120, 90);
        }
        catch (InvalidOperationException) {
            emptyRejected = true;
        }
    }

    if (!twoPixelShortfallRejected || !oversizeRejected || !emptyRejected) {
        Console.Error.WriteLine(
            $"Expected invalid frame sizes to be rejected; short={twoPixelShortfallRejected} oversize={oversizeRejected} empty={emptyRejected}");
        return 1;
    }
}

Console.WriteLine("Captured-frame normalization edge contracts passed.");

Emgu.CV.Mat SyntheticCapture(int x1, int y1, int x2, int y2) {
    liveCaptureCalls++;
    if (x1 != 0 || y1 != 0 || x2 != 119 || y2 != 89) {
        throw new InvalidOperationException(
            $"Expected safe native capture 0,0,119,89 but got {x1},{y1},{x2},{y2}");
    }
    using Emgu.CV.Mat fullFrame = BuildSyntheticFrame(syntheticFrameNumber);
    return new Emgu.CV.Mat(
        fullFrame,
        new System.Drawing.Rectangle(0, 0, 119, 89)).Clone();
}

var sessionCv = new CV(
    () => IntPtr.Zero,
    () => null!,
    () => 120,
    () => 90,
    () => "",
    () => "",
    () => "",
    () => "",
    () => 0,
    SyntheticCapture);

if (!sessionCv.TryBeginCaptureSession(out CaptureSession? firstSession, out string firstSessionError)
    || firstSession == null) {
    Console.Error.WriteLine("Expected first synthetic capture session: " + firstSessionError);
    return 1;
}

long firstFrameId = firstSession.Id;
if (liveCaptureCalls != 1 || sessionCv.LiveCaptureCount != 1
    || sessionCv.ActiveCaptureSessionId != firstFrameId) {
    Console.Error.WriteLine(
        $"Expected one live capture and active frame {firstFrameId}; provider={liveCaptureCalls} cv={sessionCv.LiveCaptureCount} active={sessionCv.ActiveCaptureSessionId}");
    return 1;
}

byte[] cropBytes = firstSession.CaptureBmpBytes(10, 20, 19, 29);
using (var cropStream = new MemoryStream(cropBytes))
using (var cropBitmap = new System.Drawing.Bitmap(cropStream)) {
    if (cropBitmap.Width != 10 || cropBitmap.Height != 10) {
        Console.Error.WriteLine($"Expected inclusive 10x10 crop, got {cropBitmap.Width}x{cropBitmap.Height}");
        return 1;
    }
    System.Drawing.Color pixel = cropBitmap.GetPixel(0, 0);
    if (pixel.R != 11 || pixel.G != 21 || pixel.B != 31) {
        Console.Error.WriteLine($"Unexpected first-frame crop pixel {pixel.R},{pixel.G},{pixel.B}");
        return 1;
    }
}

byte[] edgeCropBytes = firstSession.CaptureBmpBytes(110, 80, 119, 89);
using (var edgeStream = new MemoryStream(edgeCropBytes))
using (var edgeBitmap = new System.Drawing.Bitmap(edgeStream)) {
    if (edgeBitmap.Width != 10 || edgeBitmap.Height != 10) {
        Console.Error.WriteLine($"Expected right/bottom inclusive 10x10 crop, got {edgeBitmap.Width}x{edgeBitmap.Height}");
        return 1;
    }
    System.Drawing.Color rawLastPixel = edgeBitmap.GetPixel(8, 8);
    System.Drawing.Color paddedRight = edgeBitmap.GetPixel(9, 8);
    System.Drawing.Color paddedBottom = edgeBitmap.GetPixel(8, 9);
    System.Drawing.Color paddedCorner = edgeBitmap.GetPixel(9, 9);
    if (rawLastPixel.R != 100 || rawLastPixel.G != 88 || rawLastPixel.B != 77
        || paddedRight != rawLastPixel
        || paddedBottom != rawLastPixel
        || paddedCorner != rawLastPixel) {
        Console.Error.WriteLine(
            $"Expected replicated edge pixel 100,88,77; raw={rawLastPixel} right={paddedRight} bottom={paddedBottom} corner={paddedCorner}");
        return 1;
    }
}

Console.WriteLine("Safe native bounds and in-memory frame completion contract passed.");

string templateDirectory = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "Resources",
    "Images",
    "Testing");
Directory.CreateDirectory(templateDirectory);
string templatePath = Path.Combine(templateDirectory, "session-coordinate-template.bmp");
using (Emgu.CV.Mat templateFrame = BuildSyntheticFrame(1))
using (var templateCrop = new Emgu.CV.Mat(
    templateFrame,
    new System.Drawing.Rectangle(37, 42, 8, 8))) {
    Emgu.CV.CvInvoke.Imwrite(templatePath, templateCrop);
}

PointPlus absoluteMatch = sessionCv.FindPicture(
    20, 30, 80, 70,
    "\\Images\\Testing\\session-coordinate-template.bmp",
    0.95,
    CV.Mode.OpenCV,
    false,
    CV.PictureColorMode.Color,
    false);
if (absoluteMatch.X != 37 || absoluteMatch.Y != 42) {
    Console.Error.WriteLine(
        $"Expected absolute template coordinate 37,42 from non-zero crop, got {absoluteMatch.X},{absoluteMatch.Y}");
    return 1;
}

// Capture normalization must also update the origin used to translate a
// crop-local match back into absolute client coordinates.
PointPlus clampedOriginMatch = sessionCv.FindPicture(
    -5, -7, 80, 70,
    "\\Images\\Testing\\session-coordinate-template.bmp",
    0.95,
    CV.Mode.OpenCV,
    false,
    CV.PictureColorMode.Color,
    false);
if (clampedOriginMatch.X != 37 || clampedOriginMatch.Y != 42) {
    Console.Error.WriteLine(
        $"Expected clamped-origin absolute coordinate 37,42, got {clampedOriginMatch.X},{clampedOriginMatch.Y}");
    return 1;
}

string oneRowTemplatePath = Path.Combine(templateDirectory, "session-coordinate-one-row-template.bmp");
using (Emgu.CV.Mat templateFrame = BuildSyntheticFrame(1))
using (var oneRowTemplate = new Emgu.CV.Mat(
    templateFrame,
    new System.Drawing.Rectangle(37, 42, 8, 1))) {
    Emgu.CV.CvInvoke.Imwrite(oneRowTemplatePath, oneRowTemplate);
}

PointPlus oneRowMatch = sessionCv.FindPicture(
    37, 42, 44, 42,
    "\\Images\\Testing\\session-coordinate-one-row-template.bmp",
    0.95,
    CV.Mode.OpenCV,
    false,
    CV.PictureColorMode.Color,
    false);
if (oneRowMatch.X != 37 || oneRowMatch.Y != 42) {
    Console.Error.WriteLine(
        $"Expected valid one-row inclusive match at 37,42, got {oneRowMatch.X},{oneRowMatch.Y}");
    return 1;
}

if (sessionCv.TryBeginCaptureSession(out CaptureSession? nestedSession, out _)
    || nestedSession != null) {
    Console.Error.WriteLine("Expected nested capture session to be rejected.");
    return 1;
}

firstSession.Dispose();
if (sessionCv.ActiveCaptureSessionId != null) {
    Console.Error.WriteLine("Expected first capture session disposal to clear the active frame.");
    return 1;
}

syntheticFrameNumber = 2;
if (!sessionCv.TryBeginCaptureSession(out CaptureSession? secondSession, out string secondSessionError)
    || secondSession == null) {
    Console.Error.WriteLine("Expected second synthetic capture session: " + secondSessionError);
    return 1;
}
using (secondSession) {
    if (secondSession.Id == firstFrameId || liveCaptureCalls != 2 || sessionCv.LiveCaptureCount != 2) {
        Console.Error.WriteLine(
            $"Expected a new frame and exactly one new capture; first={firstFrameId} second={secondSession.Id} provider={liveCaptureCalls} cv={sessionCv.LiveCaptureCount}");
        return 1;
    }

    byte[] secondCropBytes = secondSession.CaptureBmpBytes(10, 20, 19, 29);
    using var secondStream = new MemoryStream(secondCropBytes);
    using var secondBitmap = new System.Drawing.Bitmap(secondStream);
    System.Drawing.Color secondPixel = secondBitmap.GetPixel(0, 0);
    if (secondPixel.R != 12 || secondPixel.G != 22 || secondPixel.B != 32) {
        Console.Error.WriteLine(
            $"Expected second Scan to read a fresh frame, got {secondPixel.R},{secondPixel.G},{secondPixel.B}");
        return 1;
    }
}

syntheticFrameNumber = 3;
if (!sessionCv.TryBeginCaptureSession(out CaptureSession? ocrSession, out string ocrSessionError)
    || ocrSession == null) {
    Console.Error.WriteLine("Expected OCR synthetic capture session: " + ocrSessionError);
    return 1;
}
using (ocrSession) {
    if (sessionCv.CachedOcrEngineCount != 0) {
        Console.Error.WriteLine($"Expected no cached OCR engines before OCR, got {sessionCv.CachedOcrEngineCount}");
        return 1;
    }

    _ = sessionCv.OCRString(
        0, 0, 39, 29,
        CV.OCRType.Words,
        CV.OCRMode.Color,
        false,
        "",
        "eng");
    if (sessionCv.CachedOcrEngineCount != 1) {
        Console.Error.WriteLine($"Expected one cached English OCR engine, got {sessionCv.CachedOcrEngineCount}");
        return 1;
    }

    _ = sessionCv.OCRString(
        40, 30, 79, 59,
        CV.OCRType.Words,
        CV.OCRMode.Binary,
        false,
        "",
        "eng");
    if (sessionCv.CachedOcrEngineCount != 1) {
        Console.Error.WriteLine($"Expected repeated English OCR to reuse one engine, got {sessionCv.CachedOcrEngineCount}");
        return 1;
    }

    // Numeric OCR mutates Tesseract settings (numeric mode, PSM and DPI).
    // It therefore needs a separate cached engine from word OCR.
    _ = sessionCv.OCRString(
        0, 0, 39, 29,
        CV.OCRType.Number,
        CV.OCRMode.Color,
        false,
        "",
        "eng");
    if (sessionCv.CachedOcrEngineCount != 2) {
        Console.Error.WriteLine(
            $"Expected separate cached word/number OCR engines, got {sessionCv.CachedOcrEngineCount}");
        return 1;
    }

    _ = sessionCv.OCRString(
        40, 30, 79, 59,
        CV.OCRType.Words,
        CV.OCRMode.Color,
        false,
        "",
        "eng");
    if (sessionCv.CachedOcrEngineCount != 2) {
        Console.Error.WriteLine(
            $"Expected word OCR to reuse its uncontaminated engine, got {sessionCv.CachedOcrEngineCount}");
        return 1;
    }
}
if (sessionCv.CachedOcrEngineCount != 0) {
    Console.Error.WriteLine(
        $"Expected OCR engines to be released when the Scan capture session ended, got {sessionCv.CachedOcrEngineCount}.");
    return 1;
}

sessionCv.Dispose();
try { File.Delete(templatePath); } catch { }
try { File.Delete(oneRowTemplatePath); } catch { }
Console.WriteLine("Capture session coordinate and per-click freshness contract passed.");
Console.WriteLine("OCR engine reuse contract passed.");

PureDmWorker.Start();
int pureDmWorkerThreadId = PureDmWorker.Call(() => Environment.CurrentManagedThreadId);
int lifecycleCaptureCalls = 0;
int lifecycleCaptureThreadId = -1;
var lifecycleCv = new CV(
    () => IntPtr.Zero,
    () => null!,
    () => 120,
    () => 90,
    () => "",
    () => "",
    () => "",
    () => "",
    () => 0,
    (x1, y1, x2, y2) => {
        lifecycleCaptureThreadId = Environment.CurrentManagedThreadId;
        lifecycleCaptureCalls++;
        return BuildSyntheticFrame(lifecycleCaptureCalls);
    });

bool lifecycleBodyRan = false;
int lifecycleCallerThreadId = Environment.CurrentManagedThreadId;
int lifecycleBodyThreadId = -1;
bool firstLifecycleOk = CFunctions.RunInCaptureSession(
    lifecycleCv,
    session => {
        lifecycleBodyThreadId = Environment.CurrentManagedThreadId;
        lifecycleBodyRan = session.Id > 0;
    },
    out long lifecycleFrame1,
    out int lifecycleLiveCaptures1,
    out string lifecycleReason1);
if (!firstLifecycleOk || !lifecycleBodyRan || lifecycleFrame1 <= 0
    || lifecycleLiveCaptures1 != 1 || lifecycleCv.ActiveCaptureSessionId != null) {
    Console.Error.WriteLine(
        $"Expected successful disposed scan session; ok={firstLifecycleOk} body={lifecycleBodyRan} frame={lifecycleFrame1} captures={lifecycleLiveCaptures1} active={lifecycleCv.ActiveCaptureSessionId} reason={lifecycleReason1}");
    return 1;
}
if (lifecycleBodyThreadId != lifecycleCallerThreadId
    || lifecycleBodyThreadId == pureDmWorkerThreadId) {
    Console.Error.WriteLine(
        $"Expected scan body on caller thread {lifecycleCallerThreadId}, body={lifecycleBodyThreadId}, worker={pureDmWorkerThreadId}.");
    return 1;
}
if (lifecycleCaptureThreadId != pureDmWorkerThreadId) {
    Console.Error.WriteLine(
        $"Expected capture-session acquisition on PureDM worker thread {pureDmWorkerThreadId}, got {lifecycleCaptureThreadId}.");
    return 1;
}

try {
    CFunctions.RunInCaptureSession(
        lifecycleCv,
        _ => throw new InvalidOperationException("synthetic scan failure"),
        out _,
        out _,
        out _);
    Console.Error.WriteLine("Expected synthetic scan body failure.");
    return 1;
}
catch (InvalidOperationException ex) when (ex.Message == "synthetic scan failure") {
    if (lifecycleCv.ActiveCaptureSessionId != null) {
        Console.Error.WriteLine("Expected exceptional scan body to dispose its capture session.");
        return 1;
    }
}

bool thirdLifecycleOk = CFunctions.RunInCaptureSession(
    lifecycleCv,
    _ => { },
    out long lifecycleFrame3,
    out int lifecycleLiveCaptures3,
    out string lifecycleReason3);
if (!thirdLifecycleOk || lifecycleFrame3 == lifecycleFrame1
    || lifecycleLiveCaptures3 != 1 || lifecycleCaptureCalls != 3) {
    Console.Error.WriteLine(
        $"Expected every accepted scan to acquire a fresh frame; ok={thirdLifecycleOk} first={lifecycleFrame1} third={lifecycleFrame3} delta={lifecycleLiveCaptures3} provider={lifecycleCaptureCalls} reason={lifecycleReason3}");
    return 1;
}
lifecycleCv.Dispose();

var failedLifecycleCv = new CV(
    () => IntPtr.Zero,
    () => null!,
    () => 120,
    () => 90,
    () => "",
    () => "",
    () => "",
    () => "",
    () => 0,
    (_, _, _, _) => throw new InvalidOperationException("synthetic capture unavailable"));
bool failedBodyRan = false;
bool failedLifecycleOk = CFunctions.RunInCaptureSession(
    failedLifecycleCv,
    _ => failedBodyRan = true,
    out _,
    out _,
    out string failedLifecycleReason);
if (failedLifecycleOk || failedBodyRan || failedLifecycleCv.ActiveCaptureSessionId != null
    || !failedLifecycleReason.Contains("synthetic capture unavailable")) {
    Console.Error.WriteLine(
        $"Expected failed acquisition to skip scan body; ok={failedLifecycleOk} body={failedBodyRan} active={failedLifecycleCv.ActiveCaptureSessionId} reason={failedLifecycleReason}");
    return 1;
}
failedLifecycleCv.Dispose();

int nestedWorkerThreadId = -1;
Task nestedWorkerCall = Task.Run(() =>
    PureDmWorker.Call(() =>
        nestedWorkerThreadId = PureDmWorker.Call(() => Environment.CurrentManagedThreadId)));
if (!nestedWorkerCall.Wait(TimeSpan.FromSeconds(2))) {
    Console.Error.WriteLine("Expected a PureDM worker call nested from the worker thread to execute inline; call deadlocked.");
    return 1;
}
if (nestedWorkerThreadId != pureDmWorkerThreadId) {
    Console.Error.WriteLine(
        $"Expected nested PureDM call on worker thread {pureDmWorkerThreadId}, got {nestedWorkerThreadId}.");
    return 1;
}

var workerTimeoutProperty = typeof(PureDmWorker).GetProperty(
    "CallTimeoutMilliseconds",
    BindingFlags.Static | BindingFlags.NonPublic);
var workerPoisonedProperty = typeof(PureDmWorker).GetProperty(
    "IsPoisoned",
    BindingFlags.Static | BindingFlags.Public);
if (workerTimeoutProperty == null || workerPoisonedProperty == null) {
    Console.Error.WriteLine("Expected a PureDM worker timeout circuit breaker.");
    return 1;
}
workerTimeoutProperty.SetValue(null, 100);
bool workerTimeoutObserved = false;
try {
    PureDmWorker.Call(() => Thread.Sleep(300));
}
catch (TimeoutException) {
    workerTimeoutObserved = true;
}
bool rejectedWorkerActionRan = false;
var rejectedWorkerSw = System.Diagnostics.Stopwatch.StartNew();
bool rejectedWorkerCallObserved = false;
try {
    PureDmWorker.Call(() => rejectedWorkerActionRan = true);
}
catch (TimeoutException) {
    rejectedWorkerCallObserved = true;
}
rejectedWorkerSw.Stop();
if (!workerTimeoutObserved
    || workerPoisonedProperty.GetValue(null) is not true
    || !rejectedWorkerCallObserved
    || rejectedWorkerActionRan
    || rejectedWorkerSw.ElapsedMilliseconds >= 100) {
    Console.Error.WriteLine(
        $"Expected timed-out PureDM worker to reject later calls immediately; timeout={workerTimeoutObserved} poisoned={workerPoisonedProperty.GetValue(null)} rejected={rejectedWorkerCallObserved} secondRan={rejectedWorkerActionRan} elapsed={rejectedWorkerSw.ElapsedMilliseconds}ms.");
    return 1;
}
Thread.Sleep(250);
workerTimeoutProperty.SetValue(null, 8000);

PureDmWorker.Stop();
Console.WriteLine("Scanner capture-session lifecycle contract passed.");
Console.WriteLine("PureDM worker re-entrant call contract passed.");
return 0;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.OCR;
using Emgu.CV.Structure;
using FuzzySharp;
using ImageMagick;
using PureDM;
using System.Collections;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;
using SystemColors = System.Drawing.SystemColors;


namespace iBarter {
    public class CFunctions {
        private static FontType gameFontType = FontType.StrongSword;

        public CFunctions(FontType _font = FontType.StrongSword) {
            gameFontType = _font;
        }

        public enum Mode {
            DmSoft,
            OpenCV,
            Both
        }

        public enum FontType {
            DejaVuSans,
            StrongSword
        }

        public FontType GameFont {
            get { return gameFontType; }
            set { gameFontType = value; }
        }

        public void Log(string _message, Brush _color) {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(() => Log(_message, _color)));
            }
            else {
                if (App.myfmMain != null) {
                    var myDT = DateTime.Now;


                    var strTime = "[ " + myDT.ToString("hh:mm:ss") + " ]  ";


                    App.myfmMain.richTextBox_Log.AppendText(strTime);
                    var tr = new TextRange(App.myfmMain.richTextBox_Log.Document.ContentEnd,
                        App.myfmMain.richTextBox_Log.Document.ContentEnd);
                    tr.Text = _message + "\r\n";
                    var bc = new BrushConverter();
                    tr.ApplyPropertyValue(TextElement.ForegroundProperty, _color);
                    App.myfmMain.richTextBox_Log.ScrollToEnd();
                }
            }
        }


        // public List<PointPlus> getAnchor(int x1 = 0, int x2 =0, int y1 = 0, int y2=0)
        // {
        //     int intX = -1, intY = -1;
        //     //App.dmSoft.FindPic(x1,y1,x2,y2)
        //
        //     return new PointPlus(intX, intY);
        // }

        public void downloadMap() {
            try {
                for (var x = 0; x < 127; x++)
                for (var y = 0; y < 127; y++)
                    DownloadMapImage("https://www.somethinglovely.net/bdo/tiles2/15/" + x + "_" + y + ".jpg",
                        "D:\\Downloads\\Maps\\" + x + "_" + y + ".jpg", ImageFormat.Jpeg);
            }
            catch (ExternalException) {
                // Something is wrong with Format -- Maybe required Format is not 
                // applicable here
            }
            catch (ArgumentNullException) {
                // Something wrong with Stream
            }
        }

        private void DownloadMapImage(string imageUrl, string filename, ImageFormat format) {
            var client = new WebClient();
            var stream = client.OpenRead(imageUrl);
            var bitmap = new Bitmap(stream);

            if (bitmap != null) bitmap.Save(filename, format);

            stream.Flush();
            stream.Close();
            client.Dispose();
        }

        public void mapMerge() {
            var folderName = @"D:\Downloads\Maps";
            var imageFiles = Directory.GetFiles(folderName);
            var img0 = Image.FromFile(folderName + "\\0_0.jpg");
            var width = img0.Width * 127;
            var height = img0.Height * 127;
            var img3 = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);
            var g = Graphics.FromImage(img3);
            g.Clear(SystemColors.AppWorkspace);

            var imageHeights = new ArrayList();

            for (var x = 0; x < 127; x++)
            for (var y = 0; y < 127; y++) {
                var img = Image.FromFile(folderName + "\\" + x + "_" + y + ".jpg");
                g.DrawImage(img, x * img0.Width, y * img0.Height);
                img.Dispose();
            }

            //img3.Save("E:\\map.jpg",System.Drawing.Imaging.ImageFormat.Jpeg);

            using (var memory = new MemoryStream()) {
                using (var fs = new FileStream("E:\\map.jpg", FileMode.Create, FileAccess.ReadWrite)) {
                    img3.Save(memory, ImageFormat.Jpeg);
                    var bytes = memory.ToArray();
                    fs.Write(bytes, 0, bytes.Length);
                }
            }
        }


        #region RefreshItems

        public void DownloadMissingIcon() {
            foreach (Items item in App.listItems) {
                if (item == null) continue;
                // Guard against the same FormatException that previously
                // killed the loop at the first non-numeric ID row (the
                // famous "Gold Bar 1,000G" case): if LoadItemsCSV ever
                // returns a row whose ID isn't parseable, skip it instead
                // of throwing out of the foreach. Same pattern as
                // Items.ItemIcon.
                if (!int.TryParse(item.ItemID, out int idNum) || idNum <= 0) continue;
                string iconPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + item.ItemID + ".bmp";
                if (!File.Exists(iconPath)) {
                    try {
                        App.myCFun.RefreshItems(item.ItemID);
                    }
                    catch (Exception ex) {
                        // One bad item should never block the remaining 273
                        // - the previous behaviour was an unhandled throw
                        // out of DownloadMissingIcon, which silently left
                        // every subsequent item un-downloaded.
                        Log("Skip icon " + item.ItemID + " (" + item.ItemName + "): " + ex.Message, Brushes.OrangeRed);
                    }
                }
            }
        }

        // Strict two-way sync between Items.csv and Resources/Images/Items:
        //   1. For each ID in the CSV that has no bmp on disk -> queue a
        //      bdocodex download via the existing RefreshItems pipeline.
        //   2. For each .bmp on disk whose stem is NOT a CSV ID -> delete
        //      it (those icons are orphaned; the catalog no longer
        //      references them).
        // Deletions are synchronous so the user sees the cleanup
        // immediately; downloads are fire-and-forget (Task.Run in
        // RefreshItems) and complete in the background, with the in-app
        // Log showing per-item progress.
        public void SyncImages() {
            string imgDir = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items";
            if (!System.IO.Directory.Exists(imgDir)) {
                Log("SyncImages: image folder missing: " + imgDir, Brushes.Red);
                return;
            }

            // Build the set of valid IDs from the freshly-loaded CSV (NOT
            // the stale App.listItems - the user might have edited the
            // CSV between sessions and we want the on-disk state to match
            // the file on disk, not the in-memory snapshot from startup).
            var csvIds = new HashSet<string>(StringComparer.Ordinal);
            int skipped = 0;
            var csvItems = LoadItemsCSV();
            foreach (var it in csvItems) {
                if (it != null && !string.IsNullOrEmpty(it.ItemID)
                    && System.Text.RegularExpressions.Regex.IsMatch(it.ItemID, "^[0-9]+$")) {
                    csvIds.Add(it.ItemID);
                }
                else {
                    skipped++;
                }
            }
            Log($"SyncImages: CSV has {csvIds.Count} valid IDs ({skipped} skipped)", Brushes.Gray);

            // Phase 1: queue downloads for missing bmps.
            int toDownload = 0;
            foreach (var id in csvIds) {
                string iconPath = Path.Combine(imgDir, id + ".bmp");
                if (!File.Exists(iconPath)) {
                    try {
                        RefreshItems(id);
                        toDownload++;
                    }
                    catch (Exception ex) {
                        Log("SyncImages: download fail " + id + ": " + ex.Message, Brushes.OrangeRed);
                    }
                }
            }
            Log($"SyncImages: queued {toDownload} download(s)", Brushes.Blue);

            // Phase 2: delete orphaned bmps (stem not in CSV). Run sync
            // here so the user gets an immediate "deleted N" line in the
            // log instead of having to wait for the async downloads to
            // finish first.
            int deleted = 0;
            var deletedNames = new List<string>();
            foreach (var path in System.IO.Directory.EnumerateFiles(imgDir, "*.bmp")) {
                string stem = Path.GetFileNameWithoutExtension(path);
                if (!csvIds.Contains(stem)) {
                    try {
                        File.Delete(path);
                        deleted++;
                        if (deletedNames.Count < 20) deletedNames.Add(stem);
                    }
                    catch (Exception ex) {
                        Log("SyncImages: delete fail " + path + ": " + ex.Message, Brushes.OrangeRed);
                    }
                }
            }
            if (deleted > 0) {
                string preview = deletedNames.Count > 0
                    ? " (" + string.Join(", ", deletedNames) + (deleted > deletedNames.Count ? ", ..." : "") + ")"
                    : "";
                Log($"SyncImages: deleted {deleted} orphan(s){preview}", Brushes.Blue);
            }
            else {
                Log("SyncImages: no orphans to delete", Brushes.Gray);
            }
        }

        public void RefreshItems(string _itemID = "") {
            // Run the HTTP + disk-write loop on a worker thread so the UI
            // thread stays responsive during bdocodex downloads. Log() already
            // marshals to the UI thread via Dispatcher.Invoke, so the iteration
            // can safely run off-thread.
            System.Threading.Tasks.Task.Run(() => RefreshItemsCore(_itemID));
        }

        private void RefreshItemsCore(string _itemID) {
            List<Items> listItems = LoadItemsCSV();
            if (!string.IsNullOrEmpty(_itemID)) {
                Items myItem = listItems.Where(i => i.ItemID == _itemID).FirstOrDefault();
                // If the CSV has been edited since startup (or the ID was
                // never in the CSV to begin with), myItem is null and the
                // old code added it to a 1-element list - which then NRE'd
                // on the first `item.ItemID` access below. Bail out with a
                // visible log instead.
                if (myItem == null) {
                    Log("RefreshItemsCore: ID not in Items.csv: " + _itemID, Brushes.OrangeRed);
                    return;
                }
                listItems.Clear();
                listItems.Add(myItem);
            }

            int i = 1;
            foreach (var item in listItems) {
                // Short-circuit on cached icons. UpdateItemImagesAsync already
                // has this guard, but only at the disk-write site, so the
                // bdocodex HTTP fetch + 'Download icon for: ...' log line in
                // this method still ran on every startup for every item that
                // the caller (Item1Icon / Item2Icon / DownloadMissingIcon)
                // hadn't already filtered out. Put the guard here too so the
                // log message actually matches reality: either we are
                // downloading (bmp missing) or we are not (bmp present).
                string cachedBmp = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + item.ItemID + ".bmp";
                if (System.IO.File.Exists(cachedBmp)) {
                    i++;
                    continue;
                }

                var imageUrl = ResolveBdocodexItemImageUrl(item.ItemID);
                // Only log + dispatch the download if the URL was actually
                // resolved - bdocodex sometimes has no image for newly added
                // LV6/LV7 catalog entries, in which case the log line was
                // misleading. Log AFTER the URL check so the message tells
                // the truth: 'no image' or 'caching' rather than always 'downloading'.
                if (string.IsNullOrEmpty(imageUrl)) {
                    if (_itemID != "") {
                        Log("No bdocodex image for: " + item.ItemName + " (" + _itemID + ")", Brushes.OrangeRed);
                    }
                    i++;
                    continue;
                }
                UpdateItemImagesAsync(item.ItemID, imageUrl);
                if (_itemID == "") {
                    Log(i + "/" + listItems.Count, Brushes.Blue);
                }
                else {
                    // Use the local foreach 'item' - its ToString() was the
                    // class name 'iBarter.Items' because Items never
                    // overrode ToString(), masking the actual name.
                    Log("Download icon for: " + item.ItemName + " (" + _itemID + ")", Brushes.Gold);
                }

                i++;
                //Thread.Sleep(500);
            }

            if (_itemID == "") {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.Done"), Brushes.Red);
            }
        }

        // LV5+ items can only ever carry quantity=1 (per the current BDO
        // barter rules). The main scan loop short-circuits TryReadQuantity
        // when this returns true, so the OCR + Magick pipeline (and the
        // CSV-fallback) are skipped entirely - saves ~150-300ms per icon.
        private static bool IsHighTier(string itemLV) {
            int lv;
            return int.TryParse(itemLV, out lv) && lv >= 5;
        }

        private string getBetween(string strSource, string strStart, string strEnd) {
            if (strSource.Contains(strStart) && strSource.Contains(strEnd)) {
                int Start, End;
                Start = strSource.IndexOf(strStart, 0) + strStart.Length;
                End = strSource.IndexOf(strEnd, Start);
                return strSource.Substring(Start, End - Start);
            }

            return "";
        }

        // Hard-coded allow-list of acceptable icon CDN hosts. Prevents SSRF via
        // attacker-controlled <meta property="og:image"> redirects.
        private static readonly HashSet<string> BdocodexImageHosts = new(StringComparer.OrdinalIgnoreCase) {
            "bdocodex.com", "bdocodex-cdn.com", "www.bdocodex.com",
        };

        private static readonly Regex IdRegex = new("^[0-9]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private const long MAX_IMAGE_BYTES = 8L * 1024 * 1024;

        // Fetch bdocodex page, parse og:image, validate against allow-list.
        private string ResolveBdocodexItemImageUrl(string _itemID) {
            if (string.IsNullOrEmpty(_itemID) || !IdRegex.IsMatch(_itemID)) return string.Empty;
            try {
                string html = SharedHttpClient.GetStringAsync("https://bdocodex.com/us/item/" + _itemID).Result;
                string raw = getBetween(html, "<meta property=\"og:image\" content=\"", "\">");
                if (string.IsNullOrEmpty(raw)) return string.Empty;
                if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri parsed)) return string.Empty;
                if (parsed.Scheme != Uri.UriSchemeHttps) return string.Empty;
                return BdocodexImageHosts.Contains(parsed.Host) ? parsed.AbsoluteUri : string.Empty;
            }
            catch {
                return string.Empty;
            }
        }

        private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        }) { Timeout = TimeSpan.FromSeconds(8) };

        private void UpdateItemImagesAsync(string _id, string _url) {
            // Defence in depth: id must be numeric; URL already allow-listed by Resolve;
            // refuse to write to disk if either check fails.
            if (!IdRegex.IsMatch(_id ?? "")) return;
            if (string.IsNullOrEmpty(_url)) return;
            if (!Uri.TryCreate(_url, UriKind.Absolute, out Uri parsed) || !BdocodexImageHosts.Contains(parsed.Host))
                return;
            // Skip the whole pipeline if the BMP is already on disk - the icon
            // doesn't change between sessions. This is the actual fix for the
            // 'Download icon for: iBarter.Items' log spam at startup: the
            // caller used to call RefreshItems blindly for every catalog item,
            // and the missing File.Exists check here meant every item got a
            // bdocodex webp re-downloaded and a 44x44 bmp overwritten.
            string finalBmp = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + _id + ".bmp";
            if (System.IO.File.Exists(finalBmp)) {
                return;
            }
            // Off-UI-thread so a hung host does not freeze the scan button.
            Task.Run(() => UpdateItemImagesCore(_id, _url));
        }

        private void UpdateItemImagesCore(string _id, string _url) {
            string webpPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Testing\\" + _id + ".webp";
            try {
                using (var client = new WebClient()) {
                    using (var src = client.OpenRead(_url))
                    using (var dest = new FileStream(webpPath, FileMode.Create, FileAccess.Write)) {
                        // Stream-bounded copy with size cap so a misbehaving server
                        // cannot exhaust disk.
                        var buffer = new byte[81920];
                        long total = 0;
                        int read;
                        while ((read = src.Read(buffer, 0, buffer.Length)) > 0) {
                            total += read;
                            if (total > MAX_IMAGE_BYTES) {
                                dest.Close();
                                try { File.Delete(webpPath); } catch { }
                                return;
                            }
                            dest.Write(buffer, 0, read);
                        }
                    }
                }
            }
            catch {
                return;
            }

            // Magic-byte sniff before handing the file to MagickImage. RIFF/WEBP
            // files start with 'RIFF' ... 'WEBP'. Blocks non-WebP payloads from
            // masquerading as icons.
            try {
                byte[] head = new byte[12];
                int got = 0;
                using (var fs = File.OpenRead(webpPath)) {
                    while (got < 12) {
                        int r = fs.Read(head, got, 12 - got);
                        if (r <= 0) break;
                        got += r;
                    }
                }
                if (got < 12 ||
                    head[0] != (byte)'R' || head[1] != (byte)'I' || head[2] != (byte)'F' || head[3] != (byte)'F' ||
                    head[8] != (byte)'W' || head[9] != (byte)'E' || head[10] != (byte)'B' || head[11] != (byte)'P') {
                    try { File.Delete(webpPath); } catch { }
                    return;
                }
            }
            catch {
                return;
            }

            // Resize webp -> 44x44 grayscale, fill background with a dark pad,
            // then save the 44x44 .bmp that FindPicture looks for. This stage
            // was missing from the function (committed in this incomplete
            // state); restoring it is what actually writes the icon template
            // to Resources/Images/Items/<id>.bmp.
            try {
                string finalBmp = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + _id + ".bmp";
                using (var bitmap = new MagickImage(webpPath)) {
                    // BackgroundColor MUST be assigned BEFORE Alpha(Remove):
                    //   - Alpha(Remove) flattens transparency onto the current
                    //     background colour.
                    //   - If BackgroundColor is still Magick's default (white)
                    //     when Alpha(Remove) runs, transparent pixels get
                    //     flattened to white - producing the wrong colour
                    //     inversion (white-bg icons were unreadable by Tesseract,
                    //     which is trained on dark-bg / light-text).
                    // Assign the dark pad first, then flatten alpha, then scale.
                    bitmap.BackgroundColor = new MagickColor(24, 23, 25);
                    bitmap.Scale(new Percentage(400));   // upsample
                    bitmap.Alpha(AlphaOption.Remove);
                    bitmap.Resize(new MagickGeometry(44, 44));
                    bitmap.Format = MagickFormat.Bmp;
                    bitmap.Write(finalBmp);
                }
                try { System.IO.File.Delete(webpPath); } catch { }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR UpdateItemImagesCore bmp write fail " + _id + ": " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Quote-aware single-line CSV split. Returns the fields of one line
        // with surrounding double-quotes stripped and embedded `""` (escaped
        // quote) collapsed to a single `"`. Replaces the old `line.Split(',')`
        // which silently broke on names like `"Gold Bar 1,000G"` by stuffing
        // `000G"` into the ID column - that bogus ID then crashed any caller
        // doing `int.Parse(item.ItemID)` (DownloadMissingIcon, Item1Icon,
        // Item2Icon), killing the loop partway through the catalog.
        private static List<string> SplitCsvLine(string line) {
            var fields = new List<string>();
            if (line == null) return fields;
            var sb = new StringBuilder(line.Length);
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++) {
                char c = line[i];
                if (inQuotes) {
                    if (c == '"') {
                        // Doubled quote inside a quoted field = literal '"'.
                        if (i + 1 < line.Length && line[i + 1] == '"') {
                            sb.Append('"');
                            i++;
                        }
                        else {
                            inQuotes = false;
                        }
                    }
                    else {
                        sb.Append(c);
                    }
                }
                else {
                    if (c == ',') {
                        fields.Add(sb.ToString());
                        sb.Clear();
                    }
                    else if (c == '"' && sb.Length == 0) {
                        // Opening quote only if it starts the field - this
                        // keeps a stray '"' mid-field from being treated as
                        // a quote delimiter.
                        inQuotes = true;
                    }
                    else {
                        sb.Append(c);
                    }
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }

        public List<Items> LoadItemsCSV() {
            var listItems = new List<Items>();
            string csvPath = AppDomain.CurrentDomain.BaseDirectory + "\\Resources\\Items.csv";
            if (!System.IO.File.Exists(csvPath)) {
                Log("Items.csv not found: " + csvPath, Brushes.Red);
                return listItems;
            }
            using (var reader = new StreamReader(csvPath)) {
                int lineNo = 0;
                while (!reader.EndOfStream) {
                    lineNo++;
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var results = SplitCsvLine(line);
                    // Schema: Name, ID, LV, Number. Anything else is malformed.
                    if (results.Count < 4) {
                        Log($"Items.csv line {lineNo} skipped (expected 4 columns, got {results.Count}): {line}", Brushes.OrangeRed);
                        continue;
                    }
                    var strName = results[0].Replace("'", "").Replace("(", "").Replace(")", "").Trim();
                    var strID = results[1].Trim();

                    // Validate ID is numeric up front - downstream callers
                    // (DownloadMissingIcon / ItemIcon getters) parse it with
                    // int.Parse; a non-numeric ID would throw and abort the
                    // whole refresh loop.
                    if (!System.Text.RegularExpressions.Regex.IsMatch(strID, "^[0-9]+$")) {
                        Log($"Items.csv line {lineNo} skipped (non-numeric ID '{strID}'): {strName}", Brushes.OrangeRed);
                        continue;
                    }

                    int intNumber = -1;
                    if (!int.TryParse(results[3].Trim(), out intNumber)) {
                        Log($"Items.csv line {lineNo} bad number '{results[3]}': {strName}", Brushes.OrangeRed);
                        intNumber = -1;
                    }

                    var myItems = new Items(strName, strID, results[2].Trim(), intNumber);
                    listItems.Add(myItems);
                }
            }

            return listItems;
        }

        public List<Islands> LoadIslandsCSV() {
            var listIslands = new List<Islands>();
            using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory +
                                                 "\\Resources\\Islands.csv")) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var results = line.Split(',');
                    var strName = results[0].Replace("'", "").Replace("(", "").Replace(")", "");
                    int intParley = Convert.ToInt32(results[1]);

                    Thickness myThickness = new Thickness(0, 0, 0, 0);
                    try {
                        myThickness.Left = double.Parse(results[2]);
                        myThickness.Top = double.Parse(results[3]);
                        myThickness.Right = double.Parse(results[4]);
                        myThickness.Bottom = double.Parse(results[5]);
                    }
                    catch (Exception e) {
                    }

                    var myIslands = new Islands(IslandEnum(strName), intParley);
                    myIslands.IslandsThickness = myThickness;

                    listIslands.Add(myIslands);
                }
            }

            return listIslands;
        }

        /// <summary>
        ///     Phase 5 (i18n): re-reads <c>Resources/Items.zh-TW.csv</c> (the
        ///     sidecar produced by <c>Tools/ImportBdocodexNames</c>) and
        ///     mutates the matching in-memory <see cref="Items"/> instances
        ///     so the <c>ItemNameDisplay</c> getter returns the right string
        ///     when the user switches language.  Idempotent: safe to call
        ///     repeatedly.  Best-effort: a missing sidecar file is logged
        ///     once and leaves the catalog's zh-TW names empty (display
        ///     falls back to English).
        /// </summary>
        public int LoadItemsZhTw() {
            string csvPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Items.zh-TW.csv";
            if (!System.IO.File.Exists(csvPath)) {
                Log("Items.zh-TW.csv not found: " + csvPath + " - skipping; run Tools > Import Bdocodex Names to populate.", Brushes.Gray);
                return 0;
            }
            if (App.listItems is null || App.listItems.Count == 0) {
                return 0;
            }

            // Build a lookup by ItemID then mutate in place.  We keep the
            // English ItemName field untouched so the JSON persisted to
            // myStorage_Data.json / myShipCargoItems_Data.json stays stable
            // across language switches.
            var byId = new Dictionary<string, string>(StringComparer.Ordinal);
            try {
                using (var reader = new StreamReader(csvPath, Encoding.UTF8)) {
                    int lineNo = 0;
                    while (!reader.EndOfStream) {
                        lineNo++;
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        // skip BOM-only line
                        if (lineNo == 1 && line.StartsWith("﻿")) {
                            line = line.Substring(1);
                        }
                        if (lineNo == 1 && line.StartsWith("ItemID")) {
                            continue;  // header
                        }
                        var results = SplitCsvLine(line);
                        if (results.Count < 2) continue;
                        var id = results[0].Trim();
                        var name = results[1].Trim();
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
                        byId[id] = name;
                    }
                }
            }
            catch (Exception ex) {
                Log("Items.zh-TW.csv parse error: " + ex.Message, Brushes.Red);
                return 0;
            }

            int updated = 0;
            foreach (var item in App.listItems) {
                if (item is null || string.IsNullOrEmpty(item.ItemID)) continue;
                if (byId.TryGetValue(item.ItemID, out var zh)) {
                    if (item.ItemNameZhTw != zh) {
                        item.ItemNameZhTw = zh;
                        updated++;
                    }
                }
            }
            Log($"Items.zh-TW: applied {updated} names from sidecar.", Brushes.Gray);
            return updated;
        }

        /// <summary>
        ///     Phase 5 (i18n): re-reads <c>Resources/Islands.zh-TW.csv</c>
        ///     (hand-curated; bdocodex has no per-island page) and mutates
        ///     the matching in-memory <see cref="Islands"/> instances.
        ///     Schema: <c>EnumName,NameZhTW,Confidence,Note</c>.
        /// </summary>
        public int LoadIslandsZhTw() {
            string csvPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Islands.zh-TW.csv";
            if (!System.IO.File.Exists(csvPath)) {
                Log("Islands.zh-TW.csv not found: " + csvPath + " - skipping; English island names will show even in zh-TW mode.", Brushes.Gray);
                return 0;
            }
            if (App.listIslands is null || App.listIslands.Count == 0) {
                return 0;
            }

            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            try {
                using (var reader = new StreamReader(csvPath, Encoding.UTF8)) {
                    int lineNo = 0;
                    while (!reader.EndOfStream) {
                        lineNo++;
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (lineNo == 1 && line.StartsWith("EnumName")) {
                            continue;  // header
                        }
                        var results = SplitCsvLine(line);
                        if (results.Count < 2) continue;
                        var key = results[0].Trim();
                        var name = results[1].Trim();
                        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name)) continue;
                        byName[key] = name;
                    }
                }
            }
            catch (Exception ex) {
                Log("Islands.zh-TW.csv parse error: " + ex.Message, Brushes.Red);
                return 0;
            }

            int updated = 0;
            foreach (var island in App.listIslands) {
                if (island is null) continue;
                if (byName.TryGetValue(island.IslandsName, out var zh)) {
                    if (island.IslandsNameZhTw != zh) {
                        island.IslandsNameZhTw = zh;
                        updated++;
                    }
                }
            }
            Log($"Islands.zh-TW: applied {updated} names from sidecar.", Brushes.Gray);
            return updated;
        }

        #endregion


        #region Identify Route

        private void CleanDataGrid() {
            if (App.mySVM.BarterDetails != null) {
                App.mySVM.BarterDetails.Clear();
            }
        }

        public async Task IdentifyRoutes() {
            App.listBarterScanner.Clear();
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(CleanDataGrid));
            }
            else {
                CleanDataGrid();
            }


            List<PointPlus> listAnchors = App.myPureDM.CV.FindPictures(0, 0, App.myPureDM.WindowWidth,
                App.myPureDM.WindowHeight, "\\Images\\anchor.bmp", 0.8, false);


            listAnchors.Sort((p1, p2) => { return p1.Y.CompareTo(p2.Y); });

            // Surface a clear warning instead of silently looping zero times
            // and logging "Done!" with no scan work. The two most common
            // causes are (a) the barter screen is not visible in the bound
            // game window, or (b) the user minimized / covered the game
            // window after pressing Planner's Done, which doesn't touch
            // the game but the user may not realise the barter UI must
            // be on screen for anchor.bmp to be found.
            if (listAnchors.Count == 0) {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.NoAnchor", App.myPureDM.WindowWidth, App.myPureDM.WindowHeight), Brushes.OrangeRed);
            }


            // for (int i = 0; i < listAnchors.Count; i++) {
            //     // Thread myThread = new Thread(() => {
            //     //     Barter myBarter = IdentifyBarter(listAnchors[i]);
            //     //     if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null && App.listBarterScanner.FirstOrDefault(b => b.IsLand.GetIslandEnum().ToString().Equals(myBarter.IsLand.GetIslandEnum().ToString())) == null) {
            //     //         App.listBarterScanner.Add(myBarter);
            //     //     }
            //     // });
            //     // myThread.IsBackground = true;
            //     // myThread.Start();
            //     // Thread.Sleep(100);
            //     // listThread.Add(myThread);
            //
            //
            //
            //
            //     Barter myBarter = await IdentifyBarterAsync(listAnchors[i]);
            //     if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null &&
            //         App.listBarterScanner.FirstOrDefault(b =>
            //             b.IsLand.Island.ToString().Equals(myBarter.IsLand.Island.ToString())) == null) {
            //         App.listBarterScanner.Add(myBarter);
            //     }
            // }


            List<Task<Barter>> tasks = new List<Task<Barter>>();

            for (int i = 0; i < Math.Min(6, listAnchors.Count); i++) {
                var myBarter = IdentifyBarterAsync(listAnchors[i]); // 一个一个来

                if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null &&
                    App.listBarterScanner.FirstOrDefault(b => b.IsLand.Island.ToString().Equals(myBarter.IsLand.Island.ToString())) == null) {
                    App.listBarterScanner.Add(myBarter);
                }
            }


            // bool tofRunning = true;
            //
            // while (tofRunning) {
            //     tofRunning = false;
            //     foreach (Thread thread in listThread) {
            //         if (thread.IsAlive) {
            //             tofRunning = true;
            //             break;
            //         }
            //     }
            //
            //     Thread.Sleep(100);
            // }

            // ParallelLoopResult result = Parallel.ForEach(listAnchors, (currentAnchor, loopState) => {
            //     Barter myBarter = IdentifyBarter(currentAnchor);
            //     if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null && !App.listBarterScanner.Any(b => b.IsLand.GetIslandEnum().ToString().Equals(myBarter.IsLand.GetIslandEnum().ToString()))) {
            //         // 在多线程环境下修改共享资源，应该使用线程安全的方式
            //         lock (App.listBarterScanner) {
            //             if (!App.listBarterScanner.Any(b => b.IsLand.GetIslandEnum().ToString().Equals(myBarter.IsLand.GetIslandEnum().ToString()))) {
            //                 App.listBarterScanner.Add(myBarter);
            //             }
            //         }
            //     }
            // });

            // RefreshDataGrid NRE'd silently in the "instant Done" case after
            // the user closed the scanner window mid-scan (App.myBarterScanner
            // is reset to null by the Closed handler). Guard the call so the
            // loop results still land in App.listBarterScanner for the next
            // time the user reopens the window, and so a null window never
            // turns into a silent jump to "Done!".
            if (App.myBarterScanner != null) {
                App.myBarterScanner.RefreshDataGrid();
            }
        }

        // private async Task<Barter> IdentifyBarterAsync(PointPlus _pp) {
        //     PointPlus pointPlusAnchor = _pp;
        //     Barter myBarter = null;
        //     if (pointPlusAnchor.X == -1 || pointPlusAnchor.Y == -1) {
        //         return myBarter;
        //     }
        //
        //     PointPlus pointPlusEdge = App.myPureDM.CV.FindPicture(Math.Max(0, pointPlusAnchor.X - 300),
        //         pointPlusAnchor.Y - 5, pointPlusAnchor.X - 5, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5,
        //         "\\Images\\edge.bmp", 0.8, CV.Mode.OpenCV, false);
        //
        //     if (pointPlusEdge.X == -1 || pointPlusEdge.Y == -1) {
        //         return myBarter;
        //     }
        //
        //     //App.dmSoft.Capture(pointPlusEdge.X + pointPlusEdge.Size.Width, pointPlusAnchor.Y - 2, pointPlusAnchor.X - 2, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5, "island.bmp");
        //     //Thread.Sleep(100);
        //     string strIsland = App.myPureDM.CV.OCRString(pointPlusEdge.X + pointPlusEdge.Size.Width,
        //         pointPlusAnchor.Y - 2, pointPlusAnchor.X - 2, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5);
        //     myBarter = new Barter();
        //
        //
        //     //Identify Trade Iteams
        //     List<PointPlus> listPointPlus = new List<PointPlus>();
        //     int intX1 = pointPlusAnchor.X + pointPlusAnchor.Size.Width + 1;
        //     int intY1 = pointPlusAnchor.Y - 2;
        //     int intX2 = pointPlusAnchor.X + 700;
        //     int intY2 = pointPlusAnchor.Y + 60;
        //     App.myPureDM.DM.Capture(intX1, intY1, intX2, intY2, "barterItems.bmp");
        //
        //
        //     string strParleyPath = "\\Images\\Parley.bmp";
        //
        //     if (GameFont == FontType.DejaVuSans) {
        //         strParleyPath = "\\Images\\Parley2.bmp";
        //     }
        //
        //     PointPlus pointPlusParley = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
        //         strParleyPath, 0.8, CV.Mode.OpenCV, false);
        //
        //     if (pointPlusParley.IsEmpty) {
        //         pointPlusParley = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
        //             "\\Images\\Parley2.bmp", 0.8, CV.Mode.OpenCV, false);
        //         GameFont = FontType.DejaVuSans;
        //     }
        //
        //
        //     string strRequiredPath = "\\Images\\Required.bmp";
        //
        //     if (GameFont == FontType.DejaVuSans) {
        //         strRequiredPath = "\\Images\\Required2.bmp";
        //     }
        //
        //     PointPlus pointPlusRequired = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
        //         strRequiredPath, 0.8, CV.Mode.OpenCV, false);
        //
        //
        //     string strParley = App.myPureDM.CV.OCRString(pointPlusParley.X + pointPlusParley.Size.Width,
        //         pointPlusParley.Y, pointPlusRequired.X + 1, pointPlusParley.Y + pointPlusParley.Size.Height,
        //         CV.OCRType.Number);
        //
        //     int intParley = App.listIslands.Where(land => land.Island == IslandEnum(strIsland))
        //         .Select(land => land.Parley).FirstOrDefault();
        //
        //     try {
        //         intParley = int.Parse(strParley);
        //         if (intParley < 5000) {
        //             intParley = intParley * 10 + 6;
        //         }
        //     }
        //     catch (Exception e) {
        //     }
        //
        //     string strRemainingPath = "\\Images\\Remaining.bmp";
        //
        //     if (GameFont == FontType.DejaVuSans) {
        //         strRemainingPath = "\\Images\\Remaining2.bmp";
        //     }
        //
        //     PointPlus pointPlusRemaining = App.myPureDM.CV.FindPicture(0,
        //         pointPlusAnchor.Y + pointPlusAnchor.Size.Height, App.myPureDM.WindowWidth, pointPlusAnchor.Y + 60,
        //         strRemainingPath, 0.6, CV.Mode.OpenCV, false);
        //
        //
        //     string strRemaining = App.myPureDM.CV.OCRString(pointPlusRemaining.X + pointPlusRemaining.Size.Width,
        //         pointPlusRemaining.Y, pointPlusRemaining.X + pointPlusRemaining.Size.Width + 30,
        //         pointPlusRemaining.Y + pointPlusRemaining.Size.Height + 2, CV.OCRType.Number);
        //
        //     int intRemaining = 0;
        //     try {
        //         intRemaining = int.Parse(strRemaining);
        //     }
        //     catch (Exception e) {
        //         Log("Error, cannot identify the remaining number => " + strIsland, Brushes.IndianRed);
        //     }
        //
        //     if (IslandEnum(strIsland) == EnumLists.Island.UnKnown) {
        //         Log("Unknown islands! Double check your result! => " + strIsland, Brushes.Red);
        //     }
        //
        //     //Islands myIslands = new Islands(IslandEnum(strIsland), intParley, intRemaining);
        //     Islands myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName == IslandEnum(strIsland).ToString());
        //     if (myIslands == null) {
        //         Log("Error, cannot identify the islands information => " + strIsland, Brushes.Red);
        //         return null;
        //     }
        //
        //     myIslands.Parley = intParley;
        //     myIslands.Remaining = intRemaining;
        //
        //     myBarter.IsLand = myIslands;
        //
        //     Log("Identified island: " + myIslands.Island, Brushes.OrangeRed);
        //
        //     //List<Thread> listThread = new List<Thread>();
        //     //////////////////////////////////////////////
        //
        //     // ParallelLoopResult result = Parallel.ForEach(App.listItems, item => {
        //     //     PointPlus myPP = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
        //     //         "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
        //     //     if (myPP.X != -1 && myPP.Y != -1) {
        //     //         listPointPlus.Add(myPP);
        //     //     }
        //     // });
        //
        //
        //     // await Task.Run(() => {
        //     //     var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2 }; // 使用一半的核心
        //     //     Parallel.ForEach(App.listItems, options, item => {
        //     //         PointPlus myPP = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
        //     //             "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
        //     //         if (myPP.X != -1 && myPP.Y != -1) {
        //     //             lock (listPointPlus) {
        //     //                 listPointPlus.Add(myPP);
        //     //             }
        //     //         }
        //     //     });
        //     // });
        //
        //
        //     //////////////////////////////////////////////
        //     // foreach (Items item in App.listItems) {
        //     //     PointPlus myPP = FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, Mode.OpenCV);
        //     //     if (myPP.X != -1 && myPP.Y != -1)
        //     //     {
        //     //         listPointPlus.Add(myPP);
        //     //     }
        //     // }
        //     foreach (Items item in App.listItems) {
        //         PointPlus myPP = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
        //             "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
        //         if (myPP.X != -1 && myPP.Y != -1) {
        //             listPointPlus.Add(myPP);
        //         }
        //     }
        //
        //     if (listPointPlus.Count < 2) {
        //         Log("Can't identify items: " + myIslands.Island, Brushes.Red);
        //         return myBarter;
        //     }
        //
        //     listPointPlus.Sort((p1, p2) => { return p1.Sim.CompareTo(p2.Sim); });
        //     List<PointPlus> listPP = new List<PointPlus>();
        //     for (int i = listPointPlus.Count - 1; i >= 0; i--) {
        //         PointPlus myPP = listPointPlus[i];
        //
        //         if (listPP.Count == 2)
        //             break;
        //
        //         PointPlus pp1 = listPP.FirstOrDefault(pp => Math.Abs(pp.X - myPP.X) < 300);
        //
        //         if (listPP.Count == 0 || pp1.IsEmpty) {
        //             listPP.Add(myPP);
        //         }
        //
        //         // if (listPP.Count==0)
        //         //     listPP.Add(myPP);
        //         // else {
        //         //     
        //         //     foreach (PointPlus pp in listPP) {
        //         //         if (myPP.X > pp.X + pp.Size.Width || myPP.X < pp.X - pp.Size.Width) {
        //         //             listPP.Add(myPP);
        //         //         }
        //         //     }
        //         // }
        //     }
        //
        //     listPointPlus = listPP;
        //
        //     //listPointPlus.RemoveRange(0, listPointPlus.Count - 2);
        //     listPointPlus.Sort((p1, p2) => { return p1.X.CompareTo(p2.X); });
        //
        //     // \Images\Items\6020.bmp
        //
        //     string strID1 = listPointPlus[0].ImageID.Substring(14, listPointPlus[0].ImageID.Length - 18);
        //
        //     if (strID1 == "800011") {
        //         strID1 = "800012";
        //     }
        //     else if (strID1 == "800012") {
        //         strID1 = "800011";
        //     }
        //
        //     string strNumber1 = App.myPureDM.CV.OCRString(listPointPlus[0].X,
        //         (int)(listPointPlus[0].Y + (listPointPlus[0].Size.Height * 0.6)),
        //         listPointPlus[0].X + listPointPlus[0].Size.Width, listPointPlus[0].Y + listPointPlus[0].Size.Height,
        //         CV.OCRType.Number, CV.OCRMode.Diff, false, strID1);
        //
        //     int intNumber1 = App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemNumber).FirstOrDefault();
        //     try {
        //         intNumber1 = int.Parse(strNumber1);
        //     }
        //     catch (Exception e) {
        //     }
        //
        //     string strID2 = "10";
        //     string strNumber2 = "-1";
        //     if (listPointPlus.Count == 2) {
        //         strID2 = listPointPlus[1].ImageID.Substring(14, listPointPlus[1].ImageID.Length - 18);
        //         if (strID2 == "800011") {
        //             strID2 = "800012";
        //         }
        //         else if (strID2 == "800012") {
        //             strID2 = "800011";
        //         }
        //
        //         strNumber2 = App.myPureDM.CV.OCRString(listPointPlus[1].X,
        //             (int)(listPointPlus[1].Y + (listPointPlus[1].Size.Height * 0.6)),
        //             listPointPlus[1].X + listPointPlus[1].Size.Width, listPointPlus[1].Y + listPointPlus[1].Size.Height,
        //             CV.OCRType.Number, CV.OCRMode.Diff, false, strID2);
        //     }
        //     else {
        //         Log("Cannot identify the second item. Use Crow Coin instead.", Brushes.Red);
        //     }
        //
        //
        //     int intNumber2 = App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemNumber).FirstOrDefault();
        //     try {
        //         intNumber2 = int.Parse(strNumber2);
        //     }
        //     catch (Exception e) {
        //     }
        //
        //     Items item1 =
        //         new Items(App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemName).FirstOrDefault(), strID1,
        //             App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemLV).FirstOrDefault(), intNumber1);
        //     Items item2 =
        //         new Items(App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemName).FirstOrDefault(), strID2,
        //             App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemLV).FirstOrDefault(), intNumber2);
        //
        //     Log(
        //         "<" + myIslands.Island + " - " + myIslands.Remaining + " ~ " + myIslands.Parley + "> Item1: " +
        //         item1.ItemName + "=>" + intNumber1 + " | Item2: " + item2.ItemName + "=>" + intNumber2, Brushes.Blue);
        //     myBarter.Item1 = item1;
        //     myBarter.Item2 = item2;
        //
        //     return myBarter;
        // }

        // Reads the quantity overlay ("50") at the icon's bottom-right corner.
        // All candidates are INSIDE the icon's bounding rectangle (no out-of-icon
        // extension). The number is rendered as large white digits layered on
        // top of the icon's BR area; we just need to pick the right crop.
        //  Phase A — 4 ROI candidates, all bottom-of-icon, varying horizontal
        //            start (handles 1-, 2-, 3-, 4-digit numbers like "5", "50",
        //            "100", "1000").
        //  Phase B — captured BMP is upscaled via MagickImage for the operator;
        //            PureDM.OCRString reads screen coords so the upscaled
        //            bitmap cannot be fed back to OCR.
        //  Phase C — multi-ROI digit vote + sane-range gate.
        //  Phase D — diagnostic dump of the second candidate as ocr_<id>.bmp.
        //  Phase F — direct Emgu.CV.OCR.Tesseract pass on the dumped BMP after
        //            3x Magick upscale + binarize. Tesseract with a digit-only
        //            whitelist and psm=10 (single character) tends to be much
        //            more accurate than PureDM's screen-coords colour-diff OCR
        //            on the 6-8 px tall BDO digits. Used as a tiebreaker
        //            prefer-source when screen-coords voting is uncertain.
        //  No cross-scan cache — the on-screen count changes per scan (e.g.
        //  remaining inventory / remaining trades) so a cached value would be
        //  stale by the next run.
        private static Tesseract _tess;
        private static readonly object _tessLock = new object();

        private static Tesseract GetTesseract() {
            if (_tess != null) return _tess;
            lock (_tessLock) {
                if (_tess != null) return _tess;
                try {
                    // Tesseract 4 ctor: TessBaseAPI.Init(dataPath, language, oem).
                    // dataPath must be a *directory*; Tesseract appends
                    // "<language>.traineddata" to find the model file. The
                    // iBarter.csproj <Content> block already copies
                    // eng.traineddata to the build output, so we just point
                    // at the directory.
                    string tessDataDir = AppDomain.CurrentDomain.BaseDirectory + @"tessdata\";
                    if (!System.IO.Directory.Exists(tessDataDir) ||
                        !System.IO.File.Exists(tessDataDir + "eng.traineddata")) {
                        TryWriteDebugLog("[OCR] tessdata MISSING: " + tessDataDir);
                        return null;
                    }
                    _tess = new Tesseract(tessDataDir, "eng", OcrEngineMode.Default);
                    _tess.SetVariable("tessedit_char_whitelist", "0123456789");
                    // psm SetVariable throws 'Unable to set psm to X' on the
                    // Emgu.CV.OCR.Tesseract 4 + LSTM build shipped here. Default
                    // psm 3 (fully automatic) handles short digit runs well
                    // once the digit whitelist above is in effect.
                    return _tess;
                }
                catch (Exception ex) {
                    TryWriteDebugLog("[OCR] tess init fail: " + ex.GetType().Name + " " + ex.Message);
                    return null;
                }
            }
        }

        // Lightweight file logger for static helpers (Log is an instance method
        // that needs a UI thread, which we don't have here). Writes one line at
        // a time to a known location the operator can inspect.
        // Disabled - the operator no longer needs the log file (scan results
        // are surfaced via the in-app Log() method). Remove the body to make
        // every TryWriteDebugLog call a no-op while keeping the call sites
        // for documentation/grep value.
        private static void TryWriteDebugLog(string message) {
            // no-op: ocr_debug.log disabled
        }

        // Module-static cache of icon templates keyed by "<id>|<W>x<H>" so we
        // only read each bmp once per app session and resize per call-site size.
        private static readonly System.Collections.Generic.Dictionary<string, Image<Bgr, byte>>
            _tplCache = new System.Collections.Generic.Dictionary<string, Image<Bgr, byte>>(System.StringComparer.Ordinal);

        // Load the icon template from Resources\Images\Items\<id>.bmp. Templates
        // are the clean icon (no number overlay) downloaded from bdocodex.
        // Resize to liveSize if needed (the live capture may differ by a
        // pixel because of UI scaling / AA). Returns null if the bmp is
        // missing or unreadable.
        private Image<Bgr, byte> LoadIconTemplate(string itemID, System.Drawing.Size liveSize) {
            if (string.IsNullOrEmpty(itemID)) return null;
            string key = itemID + "|" + liveSize.Width + "x" + liveSize.Height;
            if (_tplCache.TryGetValue(key, out var cached)) return cached;
            string path = AppDomain.CurrentDomain.BaseDirectory +
                          "Resources\\Images\\Items\\" + itemID + ".bmp";
            if (!System.IO.File.Exists(path)) {
                TryWriteDebugLog("OCR.G tpl path MISSING: " + path);
                return null;
            }
            try {
                var tpl = new Image<Bgr, byte>(path);
                Image<Bgr, byte> sized = tpl;
                if (tpl.Size != liveSize) {
                    var resized = new Image<Bgr, byte>(liveSize);
                    // CvInvoke.Resize with dsize=Size(0,0) + fx=0,fy=0 throws
                    // 'inv_scale_x > 0' - we must pass the actual target size.
                    CvInvoke.Resize(tpl, resized, liveSize, 0.0, 0.0, Inter.Linear);
                    sized = resized;
                }
                _tplCache[key] = sized;
                return sized;
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.G tpl load fail " + itemID + " " + liveSize + ": " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        // Phase G: subtract icon template from the live capture, leaving ONLY
        // the digit overlay as bright pixels. The bdocodex template should
        // match the live icon body byte-for-byte; only the digit pixels differ
        // meaningfully. Then binarize via Otsu and run Tesseract.
        //
        // Target failure mode: the M-variant Phase F read (raw capture) misses
        // thin-stroke single-digit glyphs ("1") because the icon body's anti-
        // aliasing noise sits at the same intensity as the digit. With the
        // template subtracted, AA noise cancels and the digit stands alone.
        //
        // Pre-processing chain:
        //   1. Convert both to gray
        //   2. GaussianBlur 3x3 sigma 0.5 (suppress AA noise)
        //   3. AbsDiff (template vs live)
        //   4. Threshold Otsu (binary bilevel)
        //   5. Magick 3x upscale + Negate (Tesseract prefers dark text on light bg)
        //   6. Emgu.Tesseract via GetTesseract()
        //
        // Returns -1 on any failure (template missing, Tesseract missing, no
        // digits matched).
        // Phase G: BR-threshold on the FULL icon capture. Skip the
        // template-subtraction path entirely - the bdocodex template and the
        // BDO game render differ enough (different AA, brightness, color) that
        // the diff was dominated by icon-border differences, not the digit.
        // Phase G: BR-threshold on the FULL icon capture. Skip the
        // template-subtraction path entirely - the bdocodex template and the
        // BDO game render differ enough (different AA, brightness, color) that
        // the diff was dominated by icon-border differences, not the digit.
        // BDO's digit overlay is rendered as near-pure white (RGB ~245-255)
        // on top of a darker icon body (RGB ~30-70). A hard 78% threshold
        // isolates the digit strokes alone. Magick Scale 3x + Negate produces
        // a clean dark-on-light bitmap for Tesseract.
        private int TryTemplateDiffOcr(int x1, int y1, int x2, int y2) {
            var tess = GetTesseract();
            if (tess == null) {
                TryWriteDebugLog("OCR.G tess=null");
                return -1;
            }

            // Phase G: full-icon BR-threshold + bottom-right crop. The
            // screen rect is captured in-memory via CaptureScreenBytes
            // (PureDM.GetScreenDataBmp); no disk file is touched at any
            // point. Magick reads from a MemoryStream, transforms in
            // memory, and writes the preprocessed bytes back to another
            // MemoryStream that Bitmap/Mat/Tesseract consume.
            //
            // 1. MagickColorSpace.Gray
            // 2. Threshold(78%) - only the brightest 22% of pixels survive
            //    (BDO's digit overlay renders as near-pure white ~245; icon
            //    body AA highlights are usually <200)
            // 3. Scale(500%) - 5x upscaling gives a sub-pixel '1' enough
            //    px to be Tesseract-readable (a 1-2 px stroke becomes 5-10 px)
            // 4. Negate - dark text on light background (Tesseract-friendly)
            // 5. Morphology Close - 3x3 Square kernel closes 1-px gaps left
            //    by the upscale so '5'/'9'/'0' stay closed contours.
            // 6. Crop bottom-right 75% x 75% - the digit at original
            //    (25-44, 25-44) on a 44x44 icon maps to (125-220, 125-220)
            //    after 5x scale; this crop isolates it from residual
            //    icon body curve pixels in the upper-left.
            //
            // Returns -1 on any failure (engine unavailable, capture failed,
            // no digits matched).
            try {
                byte[] bmpBytes = CaptureScreenBytes(x1, y1, x2, y2);
                if (bmpBytes == null) {
                    TryWriteDebugLog("OCR.G capture failed: " + x1 + "," + y1 + "," + x2 + "," + y2);
                    return -1;
                }
                using (var msIn = new System.IO.MemoryStream(bmpBytes))
                using (var mi = new MagickImage(msIn)) {
                    mi.ColorSpace = ColorSpace.Gray;
                    mi.Threshold(new Percentage(78));
                    mi.Scale(new Percentage(500));
                    mi.Negate();
                    // Close 1-px gaps in digit strokes after the 5x upscale
                    // (Tesseract sees '5' / '9' / '0' as broken contours and
                    // mis-reads them - '5139' can collapse to '130' etc.).
                    // Square 3x3 kernel fixes axis-aligned breaks in '5' / '0'.
                    var morph = new MorphologySettings {
                        Method = MorphologyMethod.Close,
                        Kernel = Kernel.Square,
                        Iterations = 1,
                    };
                    mi.Morphology(morph);
                    // Crop to bottom-right quadrant. BDO anchors the
                    // digit overlay at the bottom-right corner of the
                    // icon; the upper-left of the preprocessed 220x220
                    // still contains residual icon body curve pixels that
                    // confuse Tesseract on close-call cases. 25% crop from
                    // each side keeps the bottom-right 75% x 75% - the
                    // digit at original (25-44, 25-44) on a 44x44 icon
                    // maps to (125-220, 125-220) after 5x scale.
                    int cropX = (int)(mi.Width / 4);
                    int cropY = (int)(mi.Height / 4);
                    int cropW = (int)(mi.Width - cropX);
                    int cropH = (int)(mi.Height - cropY);
                    mi.Crop(new MagickGeometry(cropX, cropY, (uint)cropW, (uint)cropH));
                    using (var ms = new System.IO.MemoryStream()) {
                        mi.Write(ms, MagickFormat.Bmp);
                        ms.Position = 0;
                        using (var bitmap = new System.Drawing.Bitmap(ms)) {
                            using (var src = bitmap.ToMat())
                            using (var gray = new Emgu.CV.Mat()) {
                                if (src == null || src.IsEmpty) {
                                    TryWriteDebugLog("OCR.G mat empty");
                                    return -1;
                                }
                                // BGR -> Gray (matching the previous
                                // CvInvoke.Imread(ImreadModes.Grayscale)
                                // behaviour). The Magick pipeline already
                                // runs ColorSpace.Gray above, but Bitmap
                                // may decode as 32-bit BGRA and ToMat may
                                // surface it as colour.
                                Emgu.CV.CvEnum.ColorConversion conv = src.NumberOfChannels == 4
                                    ? Emgu.CV.CvEnum.ColorConversion.Bgra2Gray
                                    : Emgu.CV.CvEnum.ColorConversion.Bgr2Gray;
                                Emgu.CV.CvInvoke.CvtColor(src, gray, conv);
                                tess.SetImage(gray);
                            }
                            tess.Recognize();
                            string raw = (tess.GetUTF8Text() ?? "").Trim();
                            Match m = Regex.Match(raw, @"\d{1,4}");
                            if (m.Success && int.TryParse(m.Value, out int n) && n > 0 && n < 10000) {
                                return n;
                            }
                            TryWriteDebugLog("OCR.G tesseract no digits: raw='" + raw + "'");
                        }
                    }
                }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.G Magick fail: " + ex.GetType().Name + " " + ex.Message);
                return -1;
            }
            return -1;
        }

        // (Phase F removed - M-variant preprocessing (3x + Negate + 50% threshold)
// benchmarked at 4/10 on the live barter scan vs Phase R's 6/10 with no
// preprocessing at all. The threshold + upscale amplified noise as much
// as it amplified signal for low-contrast icons, and F never uniquely
// rescued a case where A + R + G already agreed. -1 outcomes stayed -1.)

        // Capture a screen rect into a managed byte[] via PureDM's
        // GetScreenDataBmp (returns IntPtr + size, both copied via
        // Marshal.Copy). Replaces the old DM.Capture + file read pair -
        // the entire OCR pipeline is now disk-free: Phase A's screen
        // OCR goes through PureDM.CV directly, Phase R + G capture
        // screen pixels on demand and feed Tesseract from memory.
        // Returns null on capture failure (PureDM ret != 1, or 0 size).
        private static byte[] CaptureScreenBytes(int x1, int y1, int x2, int y2) {
            if (App.myPureDM == null || App.myPureDM.DM == null) return null;
            System.IntPtr data = System.IntPtr.Zero;
            int size = 0;
            try {
                int ret = App.myPureDM.DM.GetScreenDataBmp(x1, y1, x2, y2, out data, out size);
                if (ret != 1 || data == System.IntPtr.Zero || size <= 0) return null;
                byte[] bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
                return bytes;
            }
            catch {
                return null;
            }
        }

        // Phase R: feed the raw captured bitmap to Tesseract with NO
        // preprocessing. Experimental - checks whether the Magick scale /
        // negate / threshold pipeline in Phase F + G is actually helping,
        // or whether the digit overlay is already crisp enough on the
        // medium-ROI capture to read directly. Compared in the merge vote
        // alongside Phase A (screen-coords), F (M-variant preprocessing)
        // and G (78% threshold + 5x upscale + morphology close).
        private int TryRawOcr(int x1, int y1, int x2, int y2) {
            var tess = GetTesseract();
            if (tess == null) {
                TryWriteDebugLog("OCR.R tess=null");
                return -1;
            }
            try {
                byte[] bmpBytes = CaptureScreenBytes(x1, y1, x2, y2);
                if (bmpBytes == null) {
                    TryWriteDebugLog("OCR.R capture failed: " + x1 + "," + y1 + "," + x2 + "," + y2);
                    return -1;
                }
                using (var ms = new System.IO.MemoryStream(bmpBytes))
                using (var bitmap = new System.Drawing.Bitmap(ms)) {
                    using (var src = bitmap.ToMat())
                    using (var gray = new Emgu.CV.Mat()) {
                        if (src == null || src.IsEmpty) {
                            TryWriteDebugLog("OCR.R mat empty");
                            return -1;
                        }
                        // Convert to Gray before Tesseract. The previous
                        // CvInvoke.Imread(..., ImreadModes.Grayscale) path
                        // did this implicitly; the new Bitmap -> Mat path
                        // returns colour, which Tesseract reads differently
                        // (and worse - 800031 used to give 3, now gives
                        // 173; 9057 used to give -1, now gives 1100).
                        // PureDM.GetScreenDataBmp returns 32-bit BGRA BMPs,
                        // so ToMat can yield 4-channel BGRA. Branch on
                        // channel count to use the right CvtColor code.
                        Emgu.CV.CvEnum.ColorConversion conv = src.NumberOfChannels == 4
                            ? Emgu.CV.CvEnum.ColorConversion.Bgra2Gray
                            : Emgu.CV.CvEnum.ColorConversion.Bgr2Gray;
                        Emgu.CV.CvInvoke.CvtColor(src, gray, conv);
                        tess.SetImage(gray);
                    }
                    tess.Recognize();
                    string raw = (tess.GetUTF8Text() ?? "").Trim();
                    Match m = Regex.Match(raw, @"\d{1,4}");
                    if (m.Success && int.TryParse(m.Value, out int n) && n > 0 && n < 10000) {
                        return n;
                    }
                    TryWriteDebugLog("OCR.R tesseract no digits: raw='" + raw + "'");
                }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.R tesseract fail: " + ex.GetType().Name + " " + ex.Message);
            }
            return -1;
        }

        // (Phase H PaddleOCR removed - Sdcb only ships win64 native
        // runtime and iBarter is locked to win-x86 by PureDM's 32-bit
        // dm.dll, so the merge vote is back to Tesseract F + G + screen-
        // coords A, just like before the Phase H experiments.)

        private int TryReadQuantity(PointPlus icon, string strID) {
            int oX = (int)icon.X;
            int oY = (int)icon.Y;
            int oW = (int)icon.Size.Width;
            int oH = (int)icon.Size.Height;

            // (leftFrac, topFrac, rightFrac, bottomFrac) - all relative inside icon.
            // 4-digit "1000" digit tail touches icon-right edge, so rf must
            //      stay at 1.00 (full icon width). bf pulls in slightly so we
            //      don't crop the digit top.
            // lf widened to -0.30 on the widest candidate (~13 px outside
            //      icon-left edge) so the leftmost "1" of "1000" isn't
            //      clipped.
            // tf lowered to 0.40-0.50 - go up to roughly the icon's vertical
            //      mid to give the OCR engine more pixel rows.
            // 9999 cap + bounded ROI keep parley "10,432" style neighbour
            //      digit bleed out of the result.
            // (Briefly tried reducing to 2 ROIs for speed, but the right-half
            // and bottom-strip candidates uniquely rescue 3 cases - Pirates=3,
            // Cotton=10, Raft Toy=1. Keep all 4.)
            var candidates = new (double lf, double tf, double rf, double bf)[] {
                (-0.30, 0.40, 1.00, 0.98),   // widest - extends far left, full right
                (-0.10, 0.50, 1.00, 0.98),   // medium width
                ( 0.50, 0.55, 1.00, 0.96),   // right half, tight Y
                ( 0.00, 0.78, 1.00, 0.96),   // bottom strip safety net
            };

            var votes = new System.Collections.Generic.Dictionary<int, int>();
            string bestRaw = null;

            foreach (var c in candidates) {
                int x1 = (int)(oX + oW * c.lf);
                int y1 = (int)(oY + oH * c.tf);
                int x2 = (int)(oX + oW * c.rf);
                int y2 = (int)(oY + oH * c.bf);
                try {
                    string raw = App.myPureDM.CV.OCRString(x1, y1, x2, y2,
                        CV.OCRType.Number, CV.OCRMode.Diff, false, strID) ?? "";
                    // Cap at 4 digits and < 10000 - values >= 10000 mean we almost
                    // certainly picked up neighbouring row text (Parley "10,432",
                    // IslandRemaining, etc). Drop those as garbage.
                    Match m = Regex.Match(raw, @"\d{1,4}");
                    if (m.Success && int.TryParse(m.Value, out int n) && n > 0 && n < 10000) {
                        votes.TryGetValue(n, out int prev);
                        votes[n] = prev + 1;
                        if (bestRaw == null || raw.Length > bestRaw.Length) bestRaw = raw;
                    }
                }
                catch { /* single ROI miss should not kill the call */ }
            }

            // Vote: most-agreed wins; ties favour SMALLER value - icon overlay
            // counts are typically small (1..9999) and the false positives from
            // neighbouring text lean high, so smaller is safer on tie.
            int picked = -1, topCount = 0;
            foreach (var kvp in votes) {
                if (kvp.Value > topCount || (kvp.Value == topCount && kvp.Key < picked)) {
                    picked = kvp.Key;
                    topCount = kvp.Value;
                }
            }

            // Phase D: dump the second candidate ROI bitmap for the operator +
            // for the Phase F Emgu.Tesseract pass below.
            try {
                // (Phase A used to DM.Capture the medium ROI to disk here
                // for Phase R to read. Now both R and G capture from the
                // screen on demand via GetScreenDataBmp, so this capture
                // is gone entirely. The coordinates are still useful for
                // computing the Phase R ROI below.)
                var c = candidates[1];
                int x1 = (int)(oX + oW * c.lf);
                int y1 = (int)(oY + oH * c.tf);
                int x2 = (int)(oX + oW * c.rf);
                int y2 = (int)(oY + oH * c.bf);
                // Suppress unused-variable warning when DEBUG_WRITE_BMP is off
                _ = (x1, y1, x2, y2);
            }
            catch { }

            // Phase R + G: full in-memory pipeline. R captures the medium
            // ROI (candidates[1]) and runs Tesseract raw; G is conditional
            // and runs the heavier Magick pipeline on the full icon. Both
            // capture screen pixels via PureDM.GetScreenDataBmp into a
            // managed byte[] - no disk I/O anywhere in the OCR path.
            int rawPick = TryRawOcr(
                (int)(oX + oW * candidates[1].lf),
                (int)(oY + oH * candidates[1].tf),
                (int)(oX + oW * candidates[1].rf),
                (int)(oY + oH * candidates[1].bf));
            int diffPick = -1;
            if (rawPick <= 0) {
                // Capture full icon + run Magick pipeline only as fallback
                diffPick = TryTemplateDiffOcr((int)oX, (int)oY,
                    (int)oX + (int)oW, (int)oY + (int)oH);
            }

            // 3-way merge vote (A screen-coords + R raw + G Magick fallback):
            var merged = new System.Collections.Generic.Dictionary<int, int>();
            if (picked > 0) merged[picked] = merged.GetValueOrDefault(picked, 0) + System.Math.Max(1, topCount);
            if (rawPick > 0) merged[rawPick] = merged.GetValueOrDefault(rawPick, 0) + 1;
            if (diffPick > 0) merged[diffPick] = merged.GetValueOrDefault(diffPick, 0) + 1;

            // Tie-break: when 2+ candidates tie on vote count, prefer the one
            // with MORE digits (the assumption is OCR noise produces truncated
            // or merged-digit garbage like '4000' for a '1000' target, while
            // the correct full read is more often 3+ digits; 1-digit "1" wins
            // ties against 1-digit garbage). On second tie, prefer smaller.
            int finalPick = -1, finalCount = 0;
            foreach (var kvp in merged) {
                int lenA = kvp.Key.ToString().Length;
                int lenB = finalPick < 0 ? -1 : finalPick.ToString().Length;
                if (kvp.Value > finalCount ||
                    (kvp.Value == finalCount && lenA > lenB) ||
                    (kvp.Value == finalCount && lenA == lenB && kvp.Key < finalPick)) {
                    finalPick = kvp.Key;
                    finalCount = kvp.Value;
                }
            }

            if (finalPick > 0) {
                string tally = string.Join(",", merged.Select(kv => kv.Key + "x" + kv.Value));
                Log($"OCR qty {strID}: picked {finalPick} (votes={tally}; A={picked} R={rawPick} G={diffPick})", Brushes.Gray);
            }
            else {
                Log($"OCR qty {strID}: no consensus; raw={(bestRaw ?? "")} R={rawPick} G={diffPick}", Brushes.OrangeRed);
            }

            return finalPick;
        }

        private Barter IdentifyBarterAsync(PointPlus _pp) {
            // 检查锚点是否有效
            if (_pp.X == -1 || _pp.Y == -1)
                return (Barter)null;
            PointPlus pointPlusAnchor = _pp;
            Barter myBarter = new Barter();

            // 1. 查找边缘图片以确定岛屿信息
            PointPlus pointPlusEdge = App.myPureDM.CV.FindPicture(
                Math.Max(0, pointPlusAnchor.X - 300),
                pointPlusAnchor.Y - 5,
                pointPlusAnchor.X - 5,
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5,
                "\\Images\\edge.bmp",
                0.8,
                CV.Mode.OpenCV,
                false);
            if (pointPlusEdge.X == -1 || pointPlusEdge.Y == -1)
                return (Barter)null;

            // 通过 OCR 识别岛屿名称
            string strIsland = App.myPureDM.CV.OCRString(
                pointPlusEdge.X + pointPlusEdge.Size.Width,
                pointPlusAnchor.Y - 2,
                pointPlusAnchor.X - 2,
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5);

            // 2. 捕获交易物品区域截图
            int intX1 = pointPlusAnchor.X + pointPlusAnchor.Size.Width + 1;
            int intY1 = pointPlusAnchor.Y - 2;
            int intX2 = pointPlusAnchor.X + 700;
            int intY2 = pointPlusAnchor.Y + 60;
            // App.myPureDM.DM.Capture(intX1, intY1, intX2, intY2, "barterItems.bmp");

            // 3. 识别 Parley 数值
            string strParleyPath = (GameFont == FontType.DejaVuSans) ? "\\Images\\Parley2.bmp" : "\\Images\\Parley.bmp";
            PointPlus pointPlusParley = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, strParleyPath, 0.8, CV.Mode.OpenCV, false);
            if (pointPlusParley.IsEmpty) {
                pointPlusParley = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Parley2.bmp", 0.8, CV.Mode.OpenCV, false);
                GameFont = FontType.DejaVuSans;
            }

            string strRequiredPath = (GameFont == FontType.DejaVuSans) ? "\\Images\\Required2.bmp" : "\\Images\\Required.bmp";
            PointPlus pointPlusRequired = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, strRequiredPath, 0.8, CV.Mode.OpenCV, false);
            string strParley = App.myPureDM.CV.OCRString(
                pointPlusParley.X + pointPlusParley.Size.Width,
                pointPlusParley.Y,
                pointPlusRequired.X + 1,
                pointPlusParley.Y + pointPlusParley.Size.Height,
                CV.OCRType.Number);

            // 获取岛屿的默认 Parley 值，并尝试解析 OCR 得到的数值
            int intParley = App.listIslands.Where(land => land.Island == IslandEnum(strIsland))
                .Select(land => land.Parley).FirstOrDefault();
            try {
                intParley = int.Parse(strParley);
                if (intParley < 5000)
                    intParley = intParley * 10 + 6;
                else if (intParley == 215712)
                    intParley = 21572;
            }
            catch {
            }

            // 4. 识别剩余交易次数
            string strRemainingPath = (GameFont == FontType.DejaVuSans) ? "\\Images\\Remaining2.bmp" : "\\Images\\Remaining.bmp";
            PointPlus pointPlusRemaining = App.myPureDM.CV.FindPicture(
                0,
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height,
                App.myPureDM.WindowWidth,
                pointPlusAnchor.Y + 60,
                strRemainingPath, 0.6, CV.Mode.OpenCV, false);
            string strRemaining = App.myPureDM.CV.OCRString(
                pointPlusRemaining.X + pointPlusRemaining.Size.Width,
                pointPlusRemaining.Y,
                pointPlusRemaining.X + pointPlusRemaining.Size.Width + 30,
                pointPlusRemaining.Y + pointPlusRemaining.Size.Height + 2,
                CV.OCRType.Number);
            int intRemaining = 0;
            try {
                intRemaining = int.Parse(strRemaining);
            }
            catch {
                Log("Error, cannot identify the remaining number => " + strIsland, Brushes.IndianRed);
            }

            if (IslandEnum(strIsland) == EnumLists.Island.UnKnown)
                Log("Unknown islands! Double check your result! => " + strIsland, Brushes.Red);

            // 5. 获取岛屿信息
            Islands myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName == IslandEnum(strIsland).ToString());
            if (myIslands == null) {
                Log("Error, cannot identify the islands information => " + strIsland, Brushes.Red);
                return (Barter)null;
            }

            myIslands.Parley = intParley;
            myIslands.Remaining = intRemaining;
            myBarter.IsLand = myIslands;
            Log("Identified island: " + myIslands.Island, Brushes.OrangeRed);


            // 6. 识别交易物品
            IdentifyTradeItem:
            List<PointPlus> listPointPlus = new List<PointPlus>();
            // OCR the two trade-item labels directly from the screen via
            // PureDM.CV.OCRString (it captures the rect internally and
            // returns the recognised text). No disk file is involved.
            string strItem1 = App.myPureDM.CV.OCRString(
                pointPlusParley.X,
                pointPlusParley.Y - pointPlusParley.Size.Height,
                pointPlusRequired.X + 120,
                pointPlusParley.Y + 1, CV.OCRType.Words, CV.OCRMode.Color);


            string strItem2 = App.myPureDM.CV.OCRString(
                pointPlusParley.X + 376,
                pointPlusParley.Y - pointPlusParley.Size.Height,
                pointPlusRequired.X + 376 + 100,
                pointPlusParley.Y + pointPlusParley.Size.Height, CV.OCRType.Words, CV.OCRMode.Color);

            // (Removed two dead DM.Capture writes that produced
            // myItem1.bmp / myItem2.bmp on disk - the OCR text above
            // already gave us what we needed; no downstream code ever
            // read those BMPs.)

            Items myItems1 = FindMostSimilarItem(strItem1, ExtractLevelPrefix(strItem1).lv);
            Items myItems2 = FindMostSimilarItem(strItem2, ExtractLevelPrefix(strItem2).lv);


            PointPlus myPP1 = new PointPlus();
            if (myItems1 != null)
                myPP1 = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + myItems1.ItemID + ".bmp", 0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true, 0.7);
            if (myPP1.X != -1 && myPP1.Y != -1 && myPP1.X * myPP1.Y != 0)
                listPointPlus.Add(myPP1);
            else {
                List<PointPlus> listPointPlus_Temp = new List<PointPlus>();
                foreach (Items item in App.listItems) {
                    PointPlus myPP = App.myPureDM.CV.FindPicture(
                        intX1, intY1, intX2, intY2,
                        "\\Images\\Items\\" + item.ItemID + ".bmp",
                        0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true, 0.7);
                    if (myPP.X != -1 && myPP.Y != -1 && myPP.X * myPP.Y != 0)
                        listPointPlus_Temp.Add(myPP);
                }

                listPointPlus_Temp = PickTwoBest(listPointPlus_Temp);
                if (listPointPlus_Temp.Count >= 1)
                    listPointPlus.Add(listPointPlus_Temp[0]);
                else
                    Log($"PickTwoBest returned no candidates for slot1 (itemID={myItems1?.ItemID}, lv={myItems1?.ItemLV})", Brushes.IndianRed);
            }


            PointPlus myPP2 = new PointPlus();

            if (myItems2 != null)
                myPP2 = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + myItems2.ItemID + ".bmp", 0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true, 0.7);
            if (myPP2.X != -1 && myPP2.Y != -1 && myPP2.X * myPP2.Y != 0)
                listPointPlus.Add(myPP2);
            else {
                List<PointPlus> listPointPlus_Temp = new List<PointPlus>();
                foreach (Items item in App.listItems) {
                    PointPlus myPP = App.myPureDM.CV.FindPicture(
                        intX1, intY1, intX2, intY2,
                        "\\Images\\Items\\" + item.ItemID + ".bmp",
                        0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true, 0.7);
                    if (myPP.X != -1 && myPP.Y != -1 && myPP.X * myPP.Y != 0)
                        listPointPlus_Temp.Add(myPP);
                }

                listPointPlus_Temp = PickTwoBest(listPointPlus_Temp);
                if (listPointPlus_Temp.Count >= 2)
                    listPointPlus.Add(listPointPlus_Temp[1]);
                else if (listPointPlus_Temp.Count == 1)
                    listPointPlus.Add(listPointPlus_Temp[0]);
                else
                    Log($"PickTwoBest returned no candidates for slot2 (itemID={myItems2?.ItemID}, lv={myItems2?.ItemLV})", Brushes.IndianRed);
            }


            //
            // List<PointPlus> listPointPlus = new List<PointPlus>();
            // foreach (Items item in App.listItems) {
            //     PointPlus myPP = App.myPureDM.CV.FindPicture(
            //         intX1, intY1, intX2, intY2,
            //         "\\Images\\Items\\" + item.ItemID + ".bmp",
            //         0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true,0.7);
            //     if (myPP.X != -1 && myPP.Y != -1 && myPP.X * myPP.Y != 0)
            //         listPointPlus.Add(myPP);
            // }
            //
            // if (listPointPlus.Count < 2) {
            //     Log("Can't identify items: " + myIslands.Island, Brushes.Red);
            //     return myBarter;
            // }


            // 7. 选取匹配度最高且彼此距离较远的两个物品
            // listPointPlus = PickTwoBest(listPointPlus);
            // listPointPlus.Sort((p1, p2) => p1.X.CompareTo(p2.X));

            // 8. 识别第一个物品
            string strID1 = listPointPlus[0].ImageID.Substring(14, listPointPlus[0].ImageID.Length - 18);
            // if (strID1 == "800011")
            //     strID1 = "800012";
            // else if (strID1 == "800012")
            //     strID1 = "800011";
            // Pre-OCR skip for LV5+ items: per BDO barter rules these can
            // only ever carry quantity 1, so skip the entire Phase A/G/R
            // OCR pipeline (~150-300ms per icon) and the CSV fallback
            // lookup. Saves real time on every LV5+ barter item.
            int intNumber1 = 1;
            var lv1Item = App.listItems.FirstOrDefault(i => i.ItemID == strID1);
            if (lv1Item == null || !IsHighTier(lv1Item.ItemLV)) {
                // Multi-ROI voting for the bottom-right "50" overlay (Phase A+C+D).
                intNumber1 = TryReadQuantity(listPointPlus[0], strID1);
                if (intNumber1 <= 0) {
                    // OCR failed to agree - fall back to the CSV-default quantity and
                    // log so this case is visible.
                    intNumber1 = App.listItems.Where(i => i.ItemID == strID1)
                        .Select(i => i.ItemNumber).FirstOrDefault();
                    if (intNumber1 > 0)
                        Log($"OCR qty {strID1} fallback CSV={intNumber1}", Brushes.OrangeRed);
                }
            } else {
                Log($"LV5+ skip OCR for {strID1} ({lv1Item.ItemName}) -> 1", Brushes.Gold);
            }

            // 9. 识别第二个物品
            string strID2 = "10";
            int intNumber2 = -1;
            if (listPointPlus.Count == 2) {
                strID2 = listPointPlus[1].ImageID.Substring(14, listPointPlus[1].ImageID.Length - 18);
                if (strID2 == "800011")
                    strID2 = "800012";
                else if (strID2 == "800012")
                    strID2 = "800011";
                // Same pre-OCR LV5+ skip as for item 1.
                var lv2Item = App.listItems.FirstOrDefault(i => i.ItemID == strID2);
                if (lv2Item != null && IsHighTier(lv2Item.ItemLV)) {
                    intNumber2 = 1;
                    Log($"LV5+ skip OCR for {strID2} ({lv2Item.ItemName}) -> 1", Brushes.Gold);
                } else {
                    intNumber2 = TryReadQuantity(listPointPlus[1], strID2);
                }
            }
            else {
                Log("Cannot identify the second item. Use Crow Coin instead.", Brushes.Red);
            }

            if (intNumber2 <= 0) {
                intNumber2 = App.listItems.Where(i => i.ItemID == strID2)
                    .Select(i => i.ItemNumber).FirstOrDefault();
                if (intNumber2 > 0)
                    Log($"OCR qty {strID2} fallback CSV={intNumber2}", Brushes.OrangeRed);
            }

            // Note (commit-history): the legacy `if (ItemLV != "0") intNumberX = 1;` override
            // silently destroyed per-barter exchange rate and is removed; OCR or CSV
            // quantity now wins. Caller-side filtering (e.g. Sum-of-bundle flags) should
            // be expressed as a separate property, not by overwriting ItemNumber.


            // 10. 构造交易物品对象
            Items item1 = new Items(
                App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemName).FirstOrDefault(),
                strID1,
                App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemLV).FirstOrDefault(),
                intNumber1);
            Items item2 = new Items(
                App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemName).FirstOrDefault(),
                strID2,
                App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemLV).FirstOrDefault(),
                intNumber2);

            Log($"<{myIslands.Island} - {myIslands.Remaining} ~ {myIslands.Parley}> Item1: {item1.ItemName} => {intNumber1} | Item2: {item2.ItemName} => {intNumber2}", Brushes.Blue);

            myBarter.Item1 = item1;
            myBarter.Item2 = item2;

            return myBarter;
        }

        // 规范化：小写、合并空白、去重音
        private static string NormalizeBasic(string s) {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            s = Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");
            var formD = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var ch in formD)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // 切词（英文够用）：按非字母数字分割，保留整词
        private static HashSet<string> Tokenize(string s) {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in Regex.Split(s, @"[^0-9A-Za-z]+"))
                if (!string.IsNullOrEmpty(w))
                    set.Add(w);
            return set;
        }

        // 防“前缀吸走”的智能打分：WRatio/TokenSet，并对“无整词重合”重罚
        private int FuzzyScoreSmart(string a, string b) {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;

            a = NormalizeBasic(a);
            b = NormalizeBasic(b);

            if (a == b) return 200; // 精确一致，绝对优先

            var ta = Tokenize(a);
            var tb = Tokenize(b);
            int overlap = ta.Intersect(tb).Count();

            // 基础分（0..100）
            int w = Fuzz.WeightedRatio(a, b);
            int ts = Fuzz.TokenSetRatio(a, b);
            int score = Math.Max(w, ts);

            // 规则 1：没有任何整词重合 -> 重罚（解决 silk vs silkworm cocoon）
            if (ta.Count > 0 && tb.Count > 0 && overlap == 0)
                score -= 40;

            // 规则 2：候选比查询明显长且没有覆盖所有查询词 -> 长度惩罚（避免长串“含糊带走”）
            double lenRatio = (double)b.Length / Math.Max(1, a.Length);
            if (lenRatio > 1.6 && overlap < ta.Count)
                score -= (int)Math.Min(20, (lenRatio - 1.6) * 20);

            // 规则 3：候选包含所有查询词（整词） -> 小幅加分（stained seagull figurine）
            if (overlap == ta.Count && ta.Count > 0)
                score += 10;

            // 限定范围
            if (score < 0) score = 0;
            if (score > 100) score = 100;
            return score;
        }

        private string RemoveLevelPrefix(string input) {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var m = Regex.Match(input, @"^\[Level [1-7]\]\s*");
            return m.Success ? input.Substring(m.Length) : input;
        }

        private static readonly Regex LV_PREFIX_RE = new(@"\[Level ([0-9]+)\]", RegexOptions.Compiled);

        // Returns (name without level prefix, expected level string or null if no [Level N] prefix found)
        private (string name, string? lv) ExtractLevelPrefix(string input) {
            if (string.IsNullOrWhiteSpace(input)) return (input, null);
            var m = LV_PREFIX_RE.Match(input);
            if (!m.Success) return (input, null);
            string lv = m.Groups[1].Value;
            string stripped = LV_PREFIX_RE.Replace(input, "").Trim();
            return (stripped, lv);
        }

        // 只改这一处：用 FuzzyScoreSmart 排序（从高到低）
        // expectedLv: optional LV string ("1".."7") to constrain candidates so
        //   [Level 6] OCR text doesn't collide with same-named [Level 5] item.
        public Items FindMostSimilarItem(string strItem1, string expectedLv = null) {
            if (string.IsNullOrWhiteSpace(strItem1) || App.listItems == null || App.listItems.Count == 0)
                return null;

            string processed = NormalizeBasic(RemoveLevelPrefix(strItem1));

            // 精确匹配优先（规范化后）
            var exact = App.listItems.FirstOrDefault(it =>
                NormalizeBasic(it.ItemName) == processed);
            if (exact != null) return exact;

            // 用 LV-aware 过滤避免同名不同 tier 撞库；expectedLv 为空则不过滤
            IEnumerable<Items> candidates = App.listItems;
            if (!string.IsNullOrWhiteSpace(expectedLv))
                candidates = candidates.Where(it => it.ItemLV == expectedLv);

            return candidates
                .OrderByDescending(it => FuzzyScoreSmart(processed, it.ItemName))
                .ThenBy(it => it.ItemName?.Length ?? int.MaxValue)
                .FirstOrDefault();
        }

        const int minDx = 300;

        List<PointPlus> PickTwoBest(List<PointPlus> list) {
            var res = new List<PointPlus>();
            if (list == null || list.Count == 0) return res;

            // 过滤无效候选（可按需调整）
            var cand = list
                .Where(p => p.X >= 0 && p.Y >= 0 && p.Sim > 0 && p.Size.Width > 0 && p.Size.Height > 0)
                .ToList();
            if (cand.Count == 0) return res;
            if (cand.Count == 1) {
                res.Add(cand[0]);
                return res;
            }

            // 1) 按 X 升序
            var byX = cand.OrderBy(p => p.X).ToList();

            // 2) 寻找最大横向间距(≥ MinDx)作为分界
            int splitIdx = -1, maxGap = 0;
            for (int i = 0; i < byX.Count - 1; i++) {
                int gap = byX[i + 1].X - byX[i].X;
                if (gap >= minDx && gap > maxGap) {
                    maxGap = gap;
                    splitIdx = i;
                }
            }

            PointPlus a, b;

            if (splitIdx >= 0) {
                // 分成左右两组，各取本组 Sim 最大
                var leftGroup = byX.Take(splitIdx + 1);
                var rightGroup = byX.Skip(splitIdx + 1);

                a = leftGroup.OrderByDescending(p => p.Sim).First();
                b = rightGroup.OrderByDescending(p => p.Sim).First();
            }
            else {
                // 分不出来：全局 Top1 + Top2（概率第一、第二）
                var bySim = cand.OrderByDescending(p => p.Sim).ToList();
                a = bySim[0];
                b = (bySim.Count > 1) ? bySim[1] : default;
                if (Equals(b, default(PointPlus))) {
                    res.Add(a);
                    return res;
                }
            }

            // 3) 最后仅按 X 排序（左在前右在后），不改变选择结果
            if (a.X <= b.X) {
                res.Add(a);
                res.Add(b);
            }
            else {
                res.Add(b);
                res.Add(a);
            }

            return res;
        }


        public EnumLists.Island IslandEnum(string _island) {
            switch (_island) {
                case string s when s.Contains("Ajir") || s.Contains("Aji"):
                    return EnumLists.Island.Ajir;
                case string s when s.Contains("Albresser"):
                    return EnumLists.Island.Albresser;
                case string s when s.Contains("Almai"):
                    return EnumLists.Island.Almai;
                case string s when s.Contains("Al-Naha") || s.Contains("AI-Naha") || s.Contains("Al_Naha"):
                    return EnumLists.Island.Al_Naha;
                case string s when s.Contains("Ancient") || s.Contains("Shipwrecked") && s.Contains("Anci"):
                    return EnumLists.Island.Ancient;
                case string s when s.Contains("Angie"):
                    return EnumLists.Island.Angie;
                case string s when s.Contains("Arakil") || s.Contains("Araki"):
                    return EnumLists.Island.Arakil;
                case string s when s.Contains("Arita"):
                    return EnumLists.Island.Arita;
                case string s when s.Contains("Baeza"):
                    return EnumLists.Island.Baeza;
                case string s when s.Contains("Balvege"):
                    return EnumLists.Island.Balvege;
                case string s when s.Contains("Barater"):
                    return EnumLists.Island.Barater;
                case string s when s.Contains("Baremi"):
                    return EnumLists.Island.Baremi;
                case string s when s.Contains("Beiruwa"):
                    return EnumLists.Island.Beiruwa;
                case string s when s.Contains("Haran") || s.Contains("ShipwreckedHaran'sCarg") || s.Contains("Shipwrecked Hara"):
                    return EnumLists.Island.Haran;
                case string s when s.Contains("Carrack") || s.Contains("Old Moon Guild"):
                    return EnumLists.Island.Carrack;
                case string s when s.Contains("Cholace"):
                    return EnumLists.Island.Cholace;
                case string s when s.Contains("Cox Pirate") || s.Contains("Cox_Pirate") || s.Contains("Cox"):
                    return EnumLists.Island.Cox_Pirate;
                case string s when s.Contains("Crows Nest") || s.Contains("Crow's Nest") || s.Contains("Nest"):
                    return EnumLists.Island.Crows_Nest;
                case string s when s.Contains("Crow"):
                    return EnumLists.Island.Crow;
                case string s when s.Contains("Daton"):
                    return EnumLists.Island.Daton;
                case string s when s.Contains("Delinghart"):
                    return EnumLists.Island.Delinghart;
                case string s when s.Contains("Derko"):
                    return EnumLists.Island.Derko;
                case string s when s.Contains("Duch"):
                    return EnumLists.Island.Duch;
                case string s when s.Contains("Dunde"):
                    return EnumLists.Island.Dunde;
                case string s when s.Contains("Eberdeen"):
                    return EnumLists.Island.Eberdeen;
                case string s when s.Contains("Ephde Rune") || s.Contains("Ephde_Rune") || s.Contains("Ephde"):
                    return EnumLists.Island.Ephde_Rune;
                case string s when s.Contains("Esfah"):
                    return EnumLists.Island.Esfah;
                case string s when s.Contains("Eveto") || s.Contains("Evelo"):
                    return EnumLists.Island.Eveto;
                case string s when s.Contains("Ginburrey"):
                    return EnumLists.Island.Ginburrey;
                case string s when s.Contains("Hakoven"):
                    return EnumLists.Island.Hakoven;
                case string s when s.Contains("Halmad"):
                    return EnumLists.Island.Halmad;
                case string s when s.Contains("Iliya") || s.Contains("liya") || s.Contains("Miya") || s.Contains("lia"):
                    return EnumLists.Island.Iliya;
                case string s when s.Contains("Unfinished") || s.Contains("UnfinishedAdriftVessel") || s.Contains("Unfinished Adrift"):
                    return EnumLists.Island.Unfinished;
                case string s when s.Contains("Invernen"):
                    return EnumLists.Island.Invernen;
                case string s when s.Contains("Kanvera"):
                    return EnumLists.Island.Kanvera;
                case string s when s.Contains("Kashuma"):
                    return EnumLists.Island.Kashuma;
                case string s when s.Contains("Kuit"):
                    return EnumLists.Island.Kuit;
                case string s when s.Contains("Lantinia"):
                    return EnumLists.Island.Lantinia;
                case string s when s.Contains("Lema"):
                    return EnumLists.Island.Lema;
                case string s when s.Contains("Lerao"):
                    return EnumLists.Island.Lerao;
                case string s when s.Contains("Lisz"):
                    return EnumLists.Island.Lisz;
                case string s when s.Contains("Louruve"):
                    return EnumLists.Island.Louruve;
                case string s when s.Contains("Luivano"):
                    return EnumLists.Island.Luivano;
                case string s when s.Contains("Mariveno"):
                    return EnumLists.Island.Mariveno;
                case string s when s.Contains("Marka"):
                    return EnumLists.Island.Marka;
                case string s when s.Contains("Marlene"):
                    return EnumLists.Island.Marlene;
                case string s when s.Contains("Modric"):
                    return EnumLists.Island.Modric;
                case string s when s.Contains("Narvo"):
                    return EnumLists.Island.Narvo;
                case string s when s.Contains("Netnume"):
                    return EnumLists.Island.Netnume;
                case string s when s.Contains("Oben") || s.Contains("Qben"):
                    return EnumLists.Island.Oben;
                case string s when s.Contains("Orffs") || s.Contains("Drffs"):
                    return EnumLists.Island.Orffs;
                case string s when s.Contains("Orisha"):
                    return EnumLists.Island.Orisha;
                case string s when s.Contains("Ostra") || s.Contains("Dstra") || s.Contains("Qstra") || s.Contains("Osta"):
                    return EnumLists.Island.Ostra;
                case string s when s.Contains("Padix"):
                    return EnumLists.Island.Padix;
                case string s when s.Contains("Pakio"):
                    return EnumLists.Island.Pakio;
                case string s when s.Contains("Paratama"):
                    return EnumLists.Island.Paratama;
                case string s when s.Contains("Pilava"):
                    return EnumLists.Island.Pilava;
                case string s when s.Contains("Portanen"):
                    return EnumLists.Island.Portanen;
                case string s when s.Contains("Pujara"):
                    return EnumLists.Island.Pujara;
                case string s when s.Contains("Racid") || s.Contains("Raid"):
                    return EnumLists.Island.Racid;
                case string s when s.Contains("Rameda"):
                    return EnumLists.Island.Rameda;
                case string s when s.Contains("Randis"):
                    return EnumLists.Island.Randis;
                case string s when s.Contains("Rickun") || s.Contains("Ricku"):
                    return EnumLists.Island.Rickun;
                case string s when s.Contains("Riyed") || s.Contains("Ried"):
                    return EnumLists.Island.Riyed;
                case string s when s.Contains("Rosevan"):
                    return EnumLists.Island.Rosevan;
                case string s when s.Contains("Serca"):
                    return EnumLists.Island.Serca;
                case string s when s.Contains("Shasha"):
                    return EnumLists.Island.Shasha;
                case string s when s.Contains("Shirna") || s.Contains("Shira") || s.Contains("Shima"):
                    return EnumLists.Island.Shirna;
                case string s when s.Contains("Sokota"):
                    return EnumLists.Island.Sokota;
                case string s when s.Contains("Staren"):
                    return EnumLists.Island.Staren;
                case string s when s.Contains("Taramura") || s.Contains("Taram"):
                    return EnumLists.Island.Taramura;
                case string s when s.Contains("Tashu"):
                    return EnumLists.Island.Tashu;
                case string s when s.Contains("Teste"):
                    return EnumLists.Island.Teste;
                case string s when s.Contains("Teyamal"):
                    return EnumLists.Island.Teyamal;
                case string s when s.Contains("Theonil"):
                    return EnumLists.Island.Theonil;
                case string s when s.Contains("Tigris"):
                    return EnumLists.Island.Tigris;
                case string s when s.Contains("Tinberra"):
                    return EnumLists.Island.Tinberra;
                case string s when s.Contains("Tulu"):
                    return EnumLists.Island.Tulu;
                case string s when s.Contains("Wandering"):
                    return EnumLists.Island.Wandering;
                case string s when s.Contains("Weita"):
                    return EnumLists.Island.Weita;
                case string s when s.Contains("Marine"):
                    return EnumLists.Island.Marine;
                case string s when s.Contains("Boa"):
                    return EnumLists.Island.Boa;
                case string s when s.Contains("Olvia") || s.Contains("Olvia Coast"):
                    return EnumLists.Island.Olvia;
                case string s when s.Contains("Arehaza"):
                    return EnumLists.Island.Arehaza;
                case string s when s.Contains("Grandiha") || s.Contains("Grándiha"):
                    return EnumLists.Island.Grandiha;
                case string s when s.Contains("Starry Midnight") || s.Contains("Midnight"):
                    return EnumLists.Island.Midnight;
                case string s when s.Contains("Haemo") || s.Contains("Haemo Island"):
                    return EnumLists.Island.Haemo;
                case string s when s.Contains("Dallae") || s.Contains("Dallae Pier"):
                    return EnumLists.Island.Dallae;
                case string s when s.Contains("Epheria Sentry") || s.Contains("Epheria"):
                    return EnumLists.Island.Epheria;
                case string s when s.Contains("Sausan Garrison") || s.Contains("Sausan"):
                    return EnumLists.Island.Sausan;
                case string s when s.Contains("Sanctuary") || s.Contains("Sanctuary Coastal"):
                    return EnumLists.Island.Sanctuary;
                default:
                    return EnumLists.Island.UnKnown;
            }
        }


        public Size GetPicSize(string _img) {
            Size mySize = new Size(0, 0);
            Bitmap myBitMap = (Bitmap)Image.FromFile(_img);
            if (myBitMap.Width > 0 && myBitMap.Height > 0)
                mySize = new Size(myBitMap.Width, myBitMap.Height);

            return mySize;
        }

        #endregion
    }
}
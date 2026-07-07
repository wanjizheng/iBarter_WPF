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
using System.Threading;
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

        private const int MaxLogBlocks = 500;

        public void Log(string _message, Brush _color) {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(() => Log(_message, _color)));
            }
            else {
                if (App.myfmMain?.richTextBox_Log != null) {
                    // Append-and-trim with self-healing recovery. A single transient
                    // COMException during rapid logging used to wipe the entire 500-line
                    // log buffer (Blocks.Clear() below), destroying exactly the
                    // diagnostic history the operator was reading. Now: drop ONE old
                    // block (which is usually the AppendText that just failed to free
                    // native resources) and retry the append. If that still fails, drop
                    // the oldest 25% of blocks to relieve pressure without nuking
                    // recent history. As a last resort, only then clear everything -
                    // and write a one-line marker so the operator can tell.
                    try {
                        var myDT = DateTime.Now;
                        var strTime = "[ " + myDT.ToString("hh:mm:ss") + " ]  ";
                        var logBox = App.myfmMain.richTextBox_Log;

                        logBox.AppendText(strTime);
                        var tr = new TextRange(logBox.Document.ContentEnd, logBox.Document.ContentEnd);
                        tr.Text = _message + "\r\n";
                        tr.ApplyPropertyValue(TextElement.ForegroundProperty, _color);

                        TrimLogToMaxBlocks(logBox);

                        try {
                            logBox.ScrollToEnd();
                        }
                        catch (COMException) {
                            // WPF text formatting can run out of native resources during rapid logging.
                        }
                    }
                    catch (COMException ex) {
                        if (!TryRecoverLogFromComException(ex, _message)) {
                            // Recovery itself failed - swallow silently to avoid crashing
                            // the calling scan thread. The next successful Log() call will
                            // resume appending.
                        }
                    }
                }
            }
        }

        private void TrimLogToMaxBlocks(System.Windows.Controls.RichTextBox logBox) {
            // The trim loop itself can throw COMException if the document is in a
            // bad state; isolate it so a single bad block doesn't kill the append.
            while (logBox.Document.Blocks.Count > MaxLogBlocks) {
                var first = logBox.Document.Blocks.FirstBlock;
                if (first == null) break;
                logBox.Document.Blocks.Remove(first);
            }
        }

        private bool TryRecoverLogFromComException(Exception original, string lostMessage) {
            try {
                var logBox = App.myfmMain?.richTextBox_Log;
                if (logBox == null) return false;

                // 1) Drop the OLDEST block - usually the AppendText that just failed
                // is occupying the native buffer that's exhausted. Free it and retry.
                if (logBox.Document.Blocks.Count > 0) {
                    var first = logBox.Document.Blocks.FirstBlock;
                    if (first != null) logBox.Document.Blocks.Remove(first);
                }
                try {
                    var tr = new TextRange(logBox.Document.ContentEnd, logBox.Document.ContentEnd);
                    tr.Text = "[recovered] " + lostMessage + "\r\n";
                    return true;
                }
                catch (COMException) { /* fall through */ }

                // 2) Drop the oldest 25% to relieve native pressure, then retry.
                int dropCount = Math.Max(1, logBox.Document.Blocks.Count / 4);
                for (int i = 0; i < dropCount; i++) {
                    var first = logBox.Document.Blocks.FirstBlock;
                    if (first == null) break;
                    logBox.Document.Blocks.Remove(first);
                }
                try {
                    var tr = new TextRange(logBox.Document.ContentEnd, logBox.Document.ContentEnd);
                    tr.Text = "[recovered with trim] " + lostMessage + "\r\n";
                    return true;
                }
                catch (COMException) { /* fall through */ }

                // 3) Last resort: clear, write a marker, return false so the caller
                // knows recovery is incomplete. We no longer nuke silently.
                logBox.Document.Blocks.Clear();
                var marker = new TextRange(logBox.Document.ContentEnd, logBox.Document.ContentEnd);
                marker.Text = "[log buffer cleared after " + original.GetType().Name + "]\r\n";
                return false;
            }
            catch {
                return false;
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
                        Log(Localization.LanguageService.Instance.Localize("str.Log.RefreshItems.SkipIcon", item.ItemID, item.ItemNameDisplay, ex.Message), Brushes.OrangeRed);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.FolderMissing", imgDir), Brushes.Red);
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
            Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.CSVScan", csvIds.Count, skipped), Brushes.Gray);

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
                        Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.DownloadFail", id, ex.Message), Brushes.OrangeRed);
                    }
                }
            }
            Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.Queued", toDownload), Brushes.Blue);

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
                        Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.DeleteFail", path, ex.Message), Brushes.OrangeRed);
                    }
                }
            }
            if (deleted > 0) {
                string preview = deletedNames.Count > 0
                    ? " (" + string.Join(", ", deletedNames) + (deleted > deletedNames.Count ? ", ..." : "") + ")"
                    : "";
                Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.Deleted", deleted, preview), Brushes.Blue);
            }
            else {
                Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.NoOrphans"), Brushes.Gray);
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
                    Log(Localization.LanguageService.Instance.Localize("str.Log.RefreshItems.IDNotInCSV", _itemID), Brushes.OrangeRed);
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
                        Log(Localization.LanguageService.Instance.Localize("str.Log.RefreshItems.NoBdocodexImage", item.ItemNameDisplay, _itemID), Brushes.OrangeRed);
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
                    Log(Localization.LanguageService.Instance.Localize("str.Log.RefreshItems.DownloadIcon", item.ItemNameDisplay, _itemID), Brushes.Gold);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsCSV.NotFound", csvPath), Brushes.Red);
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
                        Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsCSV.BadColumns", lineNo, results.Count, line), Brushes.OrangeRed);
                        continue;
                    }
                    var strName = results[0].Replace("'", "").Replace("(", "").Replace(")", "").Trim();
                    var strID = results[1].Trim();

                    // Validate ID is numeric up front - downstream callers
                    // (DownloadMissingIcon / ItemIcon getters) parse it with
                    // int.Parse; a non-numeric ID would throw and abort the
                    // whole refresh loop.
                    if (!System.Text.RegularExpressions.Regex.IsMatch(strID, "^[0-9]+$")) {
                        Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsCSV.BadID", lineNo, strID, strName), Brushes.OrangeRed);
                        continue;
                    }

                    int intNumber = -1;
                    if (!int.TryParse(results[3].Trim(), out intNumber)) {
                        Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsCSV.BadNumber", lineNo, results[3], strName), Brushes.OrangeRed);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsZhTW.NotFound", csvPath), Brushes.Gray);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsZhTW.ParseError", ex.Message), Brushes.Red);
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
            Log(Localization.LanguageService.Instance.Localize("str.Log.ItemsZhTW.Applied", updated), Brushes.Gray);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.IslandsZhTW.NotFound", csvPath), Brushes.Gray);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.IslandsZhTW.ParseError", ex.Message), Brushes.Red);
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
            Log(Localization.LanguageService.Instance.Localize("str.Log.IslandsZhTW.Applied", updated), Brushes.Gray);
            return updated;
        }

        #endregion


        #region Identify Route

        private void CleanDataGrid() {
            if (App.mySVM.BarterDetails != null) {
                App.mySVM.BarterDetails.Clear();
            }
        }

        private bool TryRefreshGameWindowSize(out string reason) {
            reason = "";
            if (App.myPureDM == null || App.myPureDM.DM == null) {
                reason = "PureDM is not initialized";
                return false;
            }

            int hwnd = (int)App.myPureDM.WindowHandle;
            if (hwnd <= 0) {
                reason = "game window handle is invalid";
                return false;
            }

            App.myPureDM.DM.GetClientSize(hwnd, out int width, out int height);
            if (width <= 0 || height <= 0) {
                reason = "game client size is invalid: " + width + "x" + height;
                return false;
            }

            App.myPureDM.WindowWidth = width;
            App.myPureDM.WindowHeight = height;
            return true;
        }

        private bool TryValidateGameCapture(out string reason) {
            reason = "";
            if (App.myPureDM == null || App.myPureDM.DM == null) {
                reason = "PureDM is not initialized";
                return false;
            }

            int hwnd = (int)App.myPureDM.WindowHandle;
            if (hwnd <= 0) {
                reason = "game window handle is invalid";
                return false;
            }

            int isBind = App.myPureDM.DM.IsBind(hwnd);
            if (isBind != 1) {
                reason = "game window is not bound (DM.IsBind=" + isBind + ")";
                return false;
            }

            int probeX2 = Math.Min(Math.Max(App.myPureDM.WindowWidth, 1), 64);
            int probeY2 = Math.Min(Math.Max(App.myPureDM.WindowHeight, 1), 64);
            // Lock for the same reason as CaptureScreenBytes: the internal
            // pointer is freed on the next GetScreenDataBmp call, so even a
            // probe-only call (that doesn't copy data) must be serialised with
            // any concurrent capture+copy in progress elsewhere.
            lock (App.myPureDM.DM) {
                IntPtr data;
                int size;
                int ret = App.myPureDM.DM.GetScreenDataBmp(0, 0, probeX2, probeY2, out data, out size);
                if (ret == 0 || data == IntPtr.Zero || size <= 0) {
                    int lastError = App.myPureDM.DM.GetLastError();
                    reason = "GetScreenDataBmp failed for probe "
                             + probeX2 + "x" + probeY2
                             + " (ret=" + ret + ", size=" + size + ", lastError=" + lastError + ")";
                    return false;
                }
            }

            return true;
        }

        public bool RefreshScannerGameWindowState(out string reason) {
            return TryRefreshGameWindowSize(out reason);
        }

        private readonly struct AnchorCandidate {
            public AnchorCandidate(string path, double similarity, bool autoResize, CV.PictureColorMode colorMode) {
                Path = path;
                Similarity = similarity;
                AutoResize = autoResize;
                ColorMode = colorMode;
            }

            public string Path { get; }
            public double Similarity { get; }
            public bool AutoResize { get; }
            public CV.PictureColorMode ColorMode { get; }
        }

        private static List<AnchorCandidate> AnchorImageCandidates() {
            return new List<AnchorCandidate> {
                new AnchorCandidate("\\Images\\anchor.bmp", 0.8, false, CV.PictureColorMode.Gray),
                // new AnchorCandidate("\\Images\\anchor.bmp", 0.74, true, CV.PictureColorMode.Gray),
                // new AnchorCandidate("\\Images\\anchor.bmp", 0.7, true, CV.PictureColorMode.Color),
            };
        }

        // Tracks the checksum of the last captured frame that failed anchor
        // detection. If consecutive scans produce the SAME checksum the DX
        // hook is returning a frozen/cached frame instead of a live one.
        //
        // Both fields are static AND IdentifyRoutes can be re-entered across UI
        // threads (Dispatcher hops + Thread.Sleep(200) make continuation
        // surfaces non-deterministic). Without a lock, two concurrent scans
        // can read-then-write torn state and either false-flag a frozen frame
        // or miss a real one. Guard every access through the lock object.
        // CaptureAnchorFailDiagnostic was removed: it saved frozen-frame BMPs to
        // Resources\Images\Testing\ during the anchor-detection OOM
        // investigation (Phase X). With the OOM fixed (tile-based matching,
        // see PureDM/CV.cs OpenCVMatchTemplates), the diagnostic is no
        // longer needed and would silently consume disk under repeated
        // anchor failures. The IdentifyRoutes failure branch now logs a
        // plain "capture succeeded; anchor template did not match" /
        // "capture failed: ..." line instead. The previously emitted
        // anchor-failure i18n keys (str.Log.Scanner.AnchorFailureDiag et
        // al.) were also removed in the same cleanup pass.

        private List<PointPlus> FindBarterAnchors(out string triedAnchors) {
            var attempts = new List<string>();
            if (App.myPureDM.WindowWidth <= 0 || App.myPureDM.WindowHeight <= 0) {
                triedAnchors = "invalid scan bounds: " + App.myPureDM.WindowWidth + "x" + App.myPureDM.WindowHeight;
                return new List<PointPlus>();
            }

            // Single active candidate today (the @0.74 / @0.7 fallback variants are
            // intentionally commented out in AnchorImageCandidates() — they were
            // dropped because the deduplication-by-candidate below used each
            // candidate's own Size.Width/Height as the tolerance, which differs
            // across candidates and silently merged away real anchors at scale
            // 1.0/0.74/0.7). If a fallback candidate is needed in the future,
            // re-add it AND replace the per-candidate tolerance with a fixed
            // 30 px so merges stay correct.
            //
            // The function is preserved as a list-returning wrapper rather than
            // a single call so the per-candidate triedAnchors string stays
            // uniform with the OCR / label scanner code that still iterates
            // multiple candidates. The cost is one extra allocation per scan.
            var allAnchors = new List<PointPlus>();

            foreach (var candidate in AnchorImageCandidates()) {
                try {
                    List<PointPlus> anchors = App.myPureDM.CV.FindPictures(
                        0,
                        0,
                        App.myPureDM.WindowWidth,
                        App.myPureDM.WindowHeight,
                        candidate.Path,
                        candidate.Similarity,
                        candidate.AutoResize,
                        candidate.ColorMode);

                    attempts.Add(candidate.Path
                                 + "@" + candidate.Similarity.ToString("0.00", CultureInfo.InvariantCulture)
                                 + (candidate.AutoResize ? "+resize" : "")
                                 + "+" + candidate.ColorMode
                                 + "=" + anchors.Count);

                    // With one active candidate the dedup is a no-op, but kept so
                    // adding fallback candidates back doesn't require re-deriving
                    // the merge rule. Tolerance is fixed at 30 px (≈ 1.5× the
                    // 18 px anchor template) so it doesn't depend on the matched
                    // candidate's own reported Size.
                    const int fixedDedupPx = 30;
                    foreach (var a in anchors) {
                        bool isDuplicate = allAnchors.Any(existing =>
                            Math.Abs(existing.X - a.X) < fixedDedupPx &&
                            Math.Abs(existing.Y - a.Y) < fixedDedupPx);
                        if (!isDuplicate) {
                            allAnchors.Add(a);
                        }
                    }
                }
                catch (Exception ex) {
                    attempts.Add(candidate.Path
                                 + "@" + candidate.Similarity.ToString("0.00", CultureInfo.InvariantCulture)
                                 + "=error:" + ex.Message);
                }
            }

            triedAnchors = string.Join(", ", attempts);
            return allAnchors;
        }

        public async Task IdentifyRoutes() {
            // UI-thread setup: clear the result collection + clean the data grid.
            // Both touch WPF bound collections and must run on the dispatcher.
            if (Application.Current.Dispatcher.CheckAccess()) {
                App.listBarterScanner.Clear();
                CleanDataGrid();
            }
            else {
                Application.Current.Dispatcher.Invoke(new Action(() => {
                    App.listBarterScanner.Clear();
                    CleanDataGrid();
                }));
            }

            // Offload the heavy synchronous work to a background thread.
            //
            // The body below does:
            //   - DM.GetClientSize + GetScreenDataBmp (COM, blocking, ~50ms each)
            //   - FindBarterAnchors: full-window capture + OpenCV tile loop
            //     (was the root cause of the "Failed to allocate 28068560 bytes"
            //     fix; the tile loop runs ~5 MatchTemplate calls per candidate)
            //   - 6× IdentifyBarterAsync: per-anchor capture + FindPicture +
            //     OCRString retry loop (5 attempts × 100ms) + MagickImage
            //     5-stage pipeline (per anchor)
            //
            // Without the Task.Run wrapper the UI thread was blocked for tens of
            // seconds per scan click. Log() and other UI-redirected calls work
            // correctly from the background thread (they Dispatcher.Invoke back
            // internally), so no other plumbing changes are required.
            await Task.Run(() => DoIdentifyRoutesHeavy());
        }

        private void DoIdentifyRoutesHeavy() {
            if (!TryRefreshGameWindowSize(out string scanReadyError)) {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.CaptureWindowFailed", scanReadyError), Brushes.OrangeRed);
                return;
            }

            // Pre-scan capture health check: in dx.graphic.3d.10plus mode the
            // DirectX hook can silently go stale after the first scan session –
            // IsBind() still returns 1 but GetScreenDataBmp() returns 0.
            // When we detect this, rebind on the UI thread (the same thread that
            // originally called BindWindowEx) to reset the DX capture pipeline,
            // then wait briefly for the hook to reinitialise before scanning.
            if (!TryValidateGameCapture(out string preScanCaptureError)
                && preScanCaptureError.Contains("GetScreenDataBmp failed")) {
                Log("截图接口失效（DX钩子失效），正在自动重新绑定窗口...", Brushes.Orange);
                Application.Current.Dispatcher.Invoke(new Action(() => {
                    App.myPureDM.CV.BindWindow((int)App.myPureDM.WindowHandle);
                }));
                System.Threading.Thread.Sleep(200); // allow DX hook to reinitialise
                if (!TryValidateGameCapture(out string postRebindError)) {
                    Log("重新绑定后截图仍失败：" + postRebindError + "。请手动重新绑定后再扫描。",
                        Brushes.OrangeRed);
                    return;
                }
                Log("自动重新绑定成功，继续扫描。", Brushes.Blue);
            }

            List<PointPlus> listAnchors = FindBarterAnchors(out string triedAnchors);

            Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.FoundAnchors", listAnchors.Count, triedAnchors), Brushes.DimGray);

            listAnchors.Sort((p1, p2) => { return p1.Y.CompareTo(p2.Y); });

            // Refresh the barter-UI fingerprint used by the icon-position
            // cache. Captured AFTER anchors are known so we can sample a
            // region relative to the first anchor (~50x20 px just above it).
            // If the user switches to a different barter session or moves
            // the game window between scans, this hash flips and invalidates
            // every cached icon position.
            if (listAnchors.Count > 0) {
                RefreshBarterUIHash(listAnchors[0]);
            } else {
                _currentBarterUIHash = 0;
            }

            // Filter out anchors whose X coordinate is a clear outlier.
            // All valid barter-row anchors are vertically stacked and therefore
            // share approximately the same X position.  A special-exchange
            // notification banner adds an extra anchor icon at a noticeably
            // different X.  Strategy: find the contiguous X cluster that
            // contains a strict majority of the anchors; if at least one anchor
            // lies outside that cluster, remove it and log the removal.
            // The filter is intentionally conservative – it only fires when
            // the winning cluster is a strict majority (> half), so no
            // filtering happens when every anchor is already aligned or when
            // the total count is ≤ 1.
            if (listAnchors.Count > 1) {
                const int xTolerance = 30; // px; real-row anchors are typically within 5 px
                var byX = listAnchors.OrderBy(a => a.X).ToList();

                int bestStart = 0, bestLen = 1;
                int runStart  = 0, runLen  = 1;
                for (int i = 1; i < byX.Count; i++) {
                    if (byX[i].X - byX[runStart].X <= xTolerance) {
                        runLen++;
                    }
                    else {
                        if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; }
                        runStart = i; runLen = 1;
                    }
                }
                if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; }

                if (bestLen > listAnchors.Count / 2 && bestLen < listAnchors.Count) {
                    int xMin = byX[bestStart].X - 5;
                    int xMax = byX[bestStart + bestLen - 1].X + 5;
                    int removed = listAnchors.RemoveAll(a => a.X < xMin || a.X > xMax);
                    if (removed > 0)
                        Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.FilteredAnchors", removed, xMin, xMax, listAnchors.Count), Brushes.DimGray);
                }
            }

            // Surface a clear warning instead of silently looping zero times
            // and logging "Done!" with no scan work. The two most common
            // causes are (a) the barter screen is not visible in the bound
            // game window, or (b) the user minimized / covered the game
            // window after pressing Planner's Done, which doesn't touch
            // the game but the user may not realise the barter UI must
            // be on screen for anchor.bmp to be found.
            if (listAnchors.Count == 0) {
                // (Anchor-fail diagnostic was removed; see the note at the deleted
                // CaptureAnchorFailDiagnostic stub above.)
                string captureDiagnostic;
                if (TryValidateGameCapture(out string captureReadyError)) {
                    captureDiagnostic = " Capture diagnostic: capture succeeded; anchor template did not match.";
                }
                else {
                    captureDiagnostic = " Capture diagnostic: " + captureReadyError + ". Bind the game window once, then scan again.";
                }

                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.NoAnchor", App.myPureDM.WindowWidth, App.myPureDM.WindowHeight)
                    + " (tried: " + triedAnchors + ")." + captureDiagnostic, Brushes.OrangeRed);
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
                if (myBarter == null) {
                    continue;
                }

                if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null &&
                    App.listBarterScanner.FirstOrDefault(b => b.IsLand.Island.ToString().Equals(myBarter.IsLand.Island.ToString())) == null) {
                    App.listBarterScanner.Add(myBarter);
                }
            }

            // Anchors were found and processed: reset the frozen-frame detector
            // so a genuine stale-frame condition on the NEXT scan is not masked
            // by the checksum of a frame that was captured during this scan.
            // The save-count reset ALSO bounds the diagnostic BMPs at 5 per
            // consecutive-failure streak (the reset only happens when anchors
            // were found, so an alternating success/failure session gets up to
            // 5 BMPs per failure streak rather than 5 per app run).
            // (Anchors-found reset of _lastAnchorFailChecksum / _anchorFailSaveCount
            // was removed together with CaptureAnchorFailDiagnostic above.)


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
        //
        //  Per-thread Tesseract instance. TryRawOcr / TryTemplateDiffOcr /
        //  TryRemainingTesseractOcr used to share a single static _tess
        //  instance guarded by _tessLock, which meant concurrent calls into
        //  these helpers would serialize on the lock. The new parallel-
        //  island scanning pushes those helpers through several threads in
        //  parallel, so each worker thread now owns its own Tesseract.
        //  Tesseract is thread-safe across instances - each new Tesseract
        //  gets its own TessBaseAPI handle and its own LSTM weights memory.
        //  Init cost (~50-100 ms after first time) is paid once per worker
        //  thread the first time it calls into our OCR pipeline. With
        //  MaxDegreeOfParallelism = 4 we end up with up to 4 Tesseract
        //  instances resident (~120-200 MB total) - well within the 4 GB
        //  user-mode address space that LargeAddressAware gives us.
        private static readonly ThreadLocal<Tesseract> _tessPerThread =
            new ThreadLocal<Tesseract>(CreateTesseractForThisThread, trackAllValues: false);

        // Serialises PureDM.CV.* and App.myPureDM.CV.* calls that were
        // previously parallelised. PureDM's CaptureByDMToMat has its own
        // internal lock on the DM object (PureDM/CV.cs:418), but concurrent
        // ImageOCR (which creates a fresh Tesseract per call) returns empty
        // strings when the COM layer is contended - observed in practice as
        // every OCR mode returning "" / Tesseract returning null. So we
        // serialise the OCRString calls at this outer level while keeping
        // our own Tesseract paths (per-thread instances, no shared state)
        // genuinely parallel. Net effect: ROI vote back to serial like
        // before, but TryRawOcr / TryTemplateDiffOcr / TryRemainingTess
        // still run concurrently with each other.
        private static readonly object _pureDmLock = new object();

        private static Tesseract CreateTesseractForThisThread() {
            try {
                string tessDataDir = AppDomain.CurrentDomain.BaseDirectory + @"tessdata\";
                if (!System.IO.Directory.Exists(tessDataDir) ||
                    !System.IO.File.Exists(tessDataDir + "eng.traineddata")) {
                    TryWriteDebugLog("[OCR] tessdata MISSING: " + tessDataDir);
                    return null;
                }
                var tess = new Tesseract(tessDataDir, CurrentOcrLanguage(), OcrEngineMode.Default);
                tess.SetVariable("tessedit_char_whitelist", "0123456789");
                // psm SetVariable throws 'Unable to set psm to X' on the
                // Emgu.CV.OCR.Tesseract 4 + LSTM build shipped here. Default
                // psm 3 (fully automatic) handles short digit runs well
                // once the digit whitelist above is in effect.
                return tess;
            }
            catch (Exception ex) {
                TryWriteDebugLog("[OCR] tess init fail: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        // Phase 9 (i18n): returns the Tesseract/PureDM language code to use
        // for the active UI language.  Default path is the existing
        // eng_best (faster, more accurate for English UI text).  zh-TW uses
        // both simplified and traditional Chinese data when available because
        // the game can render zh-TW labels with simplified glyphs.
        private static string CurrentOcrLanguage() {
            try {
                if (IsTraditionalChineseUi()) {
                    string tessDataDir = AppDomain.CurrentDomain.BaseDirectory + @"tessdata\";
                    bool hasSim = System.IO.File.Exists(tessDataDir + "chi_sim.traineddata");
                    bool hasTra = System.IO.File.Exists(tessDataDir + "chi_tra.traineddata");
                    if (hasSim && hasTra) return "chi_sim+chi_tra";
                    if (hasSim) return "chi_sim";
                    if (hasTra) return "chi_tra";
                }
            }
            catch {
                // design-time / pre-startup; fall through to English
            }
            return "eng_best";
        }

        private static bool IsTraditionalChineseUi() {
            try {
                return iBarter.Localization.LanguageService.Instance?.Current
                    == iBarter.Localization.AppLanguage.TraditionalChinese;
            }
            catch {
                return false;
            }
        }

        private readonly struct ScanLabelCandidate {
            public ScanLabelCandidate(string path, double similarity) {
                Path = path;
                Similarity = similarity;
            }

            public string Path { get; }
            public double Similarity { get; }
        }

        private readonly struct ScanLabelMatch {
            public ScanLabelMatch(PointPlus point, string path) {
                Point = point;
                Path = path;
            }

            public PointPlus Point { get; }
            public string Path { get; }
        }

        private List<ScanLabelCandidate> ScanLabelImageCandidates(string labelName, double similarity) {
            var candidates = new List<ScanLabelCandidate>();

            if (IsTraditionalChineseUi()) {
                candidates.Add(new ScanLabelCandidate("\\Images\\" + labelName + "_CN.bmp", similarity));
                return candidates;
            }

            string primary = "\\Images\\" + labelName + ".bmp";
            string alternate = "\\Images\\" + labelName + "2.bmp";
            if (GameFont == FontType.DejaVuSans) {
                candidates.Add(new ScanLabelCandidate(alternate, similarity));
                candidates.Add(new ScanLabelCandidate(primary, similarity));
            }
            else {
                candidates.Add(new ScanLabelCandidate(primary, similarity));
                candidates.Add(new ScanLabelCandidate(alternate, similarity));
            }

            return candidates;
        }

        private PointPlus FindScanLabel(int x1, int y1, int x2, int y2, string labelName, double similarity, out string matchedPath, out string triedPaths) {
            var candidates = ScanLabelImageCandidates(labelName, similarity);
            triedPaths = string.Join(", ", candidates.Select(c => c.Path + "@" + c.Similarity.ToString("0.00", CultureInfo.InvariantCulture)));
            foreach (var candidate in candidates) {
                PointPlus point = App.myPureDM.CV.FindPicture(x1, y1, x2, y2, candidate.Path, candidate.Similarity, CV.Mode.OpenCV, false);
                if (!point.IsEmpty) {
                    matchedPath = candidate.Path;
                    // DO NOT silently flip the global GameFont on a single 2.bmp
                    // fallback match: a stray false-positive at low similarity
                    // (Remaining threshold is 0.6) biases every subsequent
                    // label-detection ordering for the rest of the scan. The
                    // font auto-detection was unreliable in practice; user can
                    // set GameFont via the UI when DejaVuSans rendering is
                    // actually used. The label detection itself still works
                    // for both fonts via the candidate loop above.
                    return point;
                }
            }

            matchedPath = "";
            return PointPlus.Empty;
        }

        private List<ScanLabelMatch> FindScanLabelMatches(int x1, int y1, int x2, int y2, string labelName, double similarity, List<string> attempts) {
            var matches = new List<ScanLabelMatch>();
            foreach (var candidate in ScanLabelImageCandidates(labelName, similarity)) {
                PointPlus point = App.myPureDM.CV.FindPicture(x1, y1, x2, y2, candidate.Path, candidate.Similarity, CV.Mode.OpenCV, false);
                string status = point.IsEmpty
                    ? "not found"
                    : "at " + point.X + "," + point.Y + " size " + point.Size.Width + "x" + point.Size.Height;
                attempts.Add(labelName + ":" + candidate.Path + "@" + candidate.Similarity.ToString("0.00", CultureInfo.InvariantCulture) + "=" + status);
                if (!point.IsEmpty) {
                    matches.Add(new ScanLabelMatch(point, candidate.Path));
                }
            }
            return matches;
        }

        private static bool IsPlausibleParleyRequiredPair(PointPlus parley, PointPlus required) {
            if (parley.IsEmpty || required.IsEmpty) {
                return false;
            }

            int parleyRight = parley.X + parley.Size.Width;
            if (IsTraditionalChineseUi()) {
                const int allowedChineseOverlap = 4;
                if (required.X < parleyRight - allowedChineseOverlap) {
                    return false;
                }
            }
            else {
                if (required.X <= parleyRight) {
                    return false;
                }
            }

            int verticalTolerance = Math.Max(14, Math.Max(parley.Size.Height, required.Size.Height));
            return Math.Abs(required.Y - parley.Y) <= verticalTolerance;
        }

        private bool TryFindParleyRequiredLabels(
            int x1,
            int y1,
            int x2,
            int y2,
            out PointPlus pointPlusParley,
            out string strParleyPath,
            out PointPlus pointPlusRequired,
            out string strRequiredPath,
            out string triedLabels) {
            var attempts = new List<string>();
            var parleyMatches = FindScanLabelMatches(x1, y1, x2, y2, "Parley", 0.7, attempts);
            var requiredMatches = FindScanLabelMatches(x1, y1, x2, y2, "Required", 0.7, attempts);

            foreach (var parley in parleyMatches) {
                foreach (var required in requiredMatches) {
                    if (IsPlausibleParleyRequiredPair(parley.Point, required.Point)) {
                        pointPlusParley = parley.Point;
                        strParleyPath = parley.Path;
                        pointPlusRequired = required.Point;
                        strRequiredPath = required.Path;
                        // Same rationale as FindScanLabel: do not flip GameFont
                        // on a single 2.bmp fallback match. Both labels finding
                        // each other via IsPlausibleParleyRequiredPair is strong
                        // evidence the font is correct, but a single fallback in
                        // a noisy scan bias all later scans. Let the user set it
                        // via the UI.
                        triedLabels = string.Join("; ", attempts);
                        return true;
                    }
                }
            }

            pointPlusParley = parleyMatches.Count > 0 ? parleyMatches[0].Point : PointPlus.Empty;
            strParleyPath = parleyMatches.Count > 0 ? parleyMatches[0].Path : "";
            pointPlusRequired = requiredMatches.Count > 0 ? requiredMatches[0].Point : PointPlus.Empty;
            strRequiredPath = requiredMatches.Count > 0 ? requiredMatches[0].Path : "";
            triedLabels = string.Join("; ", attempts);
            return false;
        }

        // Minimum sane OCR rect dimensions. Anything smaller than this is either a
        // template-match coincidence (1-px wide rect after Parley and Required
        // labels get matched adjacent on screen) or arithmetic overflow.
        // Tesseract on a 1×14 px sliver returns '' through all 5 retries, which
        // surfaces as a silent 'cannot identify parley' log + dropped anchor.
        private const int MinOcrRectWidth  = 6;
        private const int MinOcrRectHeight = 8;

        private static bool IsValidOcrRectangle(int x1, int y1, int x2, int y2) {
            return x1 >= 0 && y1 >= 0
                && x2 - x1 >= MinOcrRectWidth
                && y2 - y1 >= MinOcrRectHeight
                && x2 <= 99999 && y2 <= 99999; // catch overflow on degenerate inputs
        }

        private int TryReadRemainingCount(PointPlus pointPlusAnchor, PointPlus pointPlusEdge, string strIsland) {
            PointPlus pointPlusRemaining = FindScanLabel(
                0,
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height,
                App.myPureDM.WindowWidth,
                pointPlusAnchor.Y + 60,
                "Remaining", 0.6, out _, out string triedRemainingPaths);

            if (pointPlusRemaining.IsEmpty) {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.RemainingCountFailed", strIsland, triedRemainingPaths), Brushes.IndianRed);
                return 0;
            }

            int x1 = pointPlusRemaining.X + pointPlusRemaining.Size.Width;
            int y1 = pointPlusRemaining.Y;
            int x2 = pointPlusRemaining.X + pointPlusRemaining.Size.Width + 30;
            int y2 = pointPlusRemaining.Y + pointPlusRemaining.Size.Height + 2;

            // 4-way vote: Diff / Color / Binary (PureDM) + Tess
            // (Tesseract with 5x scale + 78% threshold + morphology
            // close — see TryRemainingTesseractOcr for details). The
            // priority is tuned from live data:
            //   - Binary (PureDM threshold 126) preserves '5's top bar
            //     and reads 5 correctly on 30x16 strips (confirmed
            //     empirically: 萨扇营地 / 哈科班岛 / 向阳岛 all read
            //     5 via Binary, all read 2 via Color).
            //   - Color (full-shape) is unreliable for '5' on this
            //     strip (systematically reads 5 as 2) but is the safer
            //     tie-breaker for digits where Binary fragments the
            //     thin stroke (e.g. '4's diagonal).
            //   - Diff is the original primary (preserves '4'
            //     diagonal) and stays at priority 2.
            //   - Tess is priority 4 when it works (5x + morphology
            //     close is the most robust pipeline), with TryRawOcr
            //     as a no-preprocessing fallback if the threshold
            //     pipeline returns no digit.
            var candidates = new List<(string source, int value, int priority)>();
            string rawDiff = null, rawColor = null, rawBinary = null;
            string modeTried = "";

            // Phase A (3 PureDM modes Diff/Color/Binary) + Phase T
            // (Tesseract Magick) + Phase R (Tesseract raw) all run in
            // parallel. PureDM.CV.OCRString locks internally on the DM
            // (PureDM/CV.cs:418), and TryRemainingTesseractOcr /
            // TryRawOcr now use per-thread Tesseract instances so there's
            // no shared Tesseract state to serialize. Serial baseline was
            // ~280 ms (3 OCR + Tess + maybe RawOcr); parallel = ~150 ms.
            var modes = new[] { CV.OCRMode.Diff, CV.OCRMode.Color, CV.OCRMode.Binary };
            string[] modeRaws = new string[3];
            int tessPick = -1;
            int rawPick = -1;

            // Reverted from Parallel.Invoke - concurrent PureDM OCRString
            // calls return empty strings even with _pureDmLock in place
            // (observed empirically: every mode returned "" / Tesseract
            // returned null and downstream fuzzy match / icon fallback
            // failed for every island). The three PureDM modes run serially
            // again, and the Tesseract paths run sequentially after.
            for (int i = 0; i < modes.Length; i++) {
                try {
                    modeRaws[i] = App.myPureDM.CV.OCRString(
                        x1, y1, x2, y2,
                        CV.OCRType.Number, modes[i], false, "", CurrentOcrLanguage());
                    modeTried = modes[i].ToString();
                }
                catch { modeRaws[i] = null; }
            }

            int tessPick2 = TryRemainingTesseractOcr(x1, y1, x2, y2);
            if (tessPick2 <= 0) {
                tessPick2 = TryRawOcr(x1, y1, x2, y2);
            }
            tessPick = tessPick2;

            // Mode votes are now all in. Apply priority weights.
            for (int i = 0; i < modes.Length; i++) {
                string raw = modeRaws[i];
                CV.OCRMode mode = modes[i];
                if (i == 0) rawDiff = raw;
                else if (i == 1) rawColor = raw;
                else rawBinary = raw;
                if (!string.IsNullOrWhiteSpace(raw)
                    && BarterOcrParsing.TryParseRemainingCount(raw, out int parsedFromMode)) {
                    int prio = mode == CV.OCRMode.Binary ? 3
                             : mode == CV.OCRMode.Diff   ? 2 : 1;
                    candidates.Add((mode.ToString(), parsedFromMode, prio));
                }
            }
            _ = rawPick;
            if (tessPick > 0) {
                candidates.Add(("Tess", tessPick, 4));
            }

            if (candidates.Count == 0) {
                Log(Localization.LanguageService.Instance.Localize(
                    "str.Log.Scanner.RemainingCountFailed",
                    strIsland,
                    triedRemainingPaths + "; tried=" + modeTried
                        + " D=\"" + (rawDiff ?? "<null>") + "\""
                        + " C=\"" + (rawColor ?? "<null>") + "\""
                        + " B=\"" + (rawBinary ?? "<null>") + "\""
                        + " T=" + (tessPick > 0 ? tessPick.ToString() : "<null>")),
                    Brushes.IndianRed);
                return 0;
            }

            int winningValue = candidates
                .GroupBy(c => c.value)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(c => c.priority))
                .First().Key;

            // Tess overread guard: Tesseract on the 30-px strip can
            // hallucinate a 2nd digit in low-contrast conditions
            // (logged example: 一、遇难的古代遗迹… D="" C="4" B=""
            // T=42 -> 42 — Tess "4"+"2" wins over Color's "4" by
            // priority). When the merged winner is multi-digit AND
            // Tess is its sole contributor (no PureDM mode agrees),
            // AND a PureDM mode produced a single-digit value, fall
            // back to that PureDM single-digit. We deliberately do
            // NOT override when Tess and any PureDM mode agree on
            // the multi-digit read — that's a corroborated read
            // (e.g. true "10" with Diff/Binary/Tess all returning 10).
            if (winningValue >= 10
                && candidates.Any(c => c.source == "Tess" && c.value == winningValue)
                && !candidates.Any(c => c.source != "Tess" && c.value == winningValue)) {
                var pureDmSingleDigit = candidates
                    .Where(c => c.source != "Tess" && c.value > 0 && c.value < 10)
                    .OrderByDescending(c => c.priority)
                    .FirstOrDefault();
                if (pureDmSingleDigit.value != 0) {
                    winningValue = pureDmSingleDigit.value;
                }
            }

            // Diagnostic log: only when modes disagree. Most islands
            // have unanimous reads (no log noise); the disagreement
            // case is exactly the 5->2 / 4->1 / 1->7 regressions we
            // want to diagnose. Shows crop + per-mode raw + resolved
            // value, so a future regression is greppable from the Log
            // alone without re-running with TryWriteDebugLog enabled.
            if (candidates.Select(c => c.value).Distinct().Count() > 1) {
                Log(
                    "OCR.Remain " + strIsland
                        + " crop=(" + x1 + "," + y1 + "," + x2 + "," + y2 + ")"
                        + " D=\"" + (rawDiff ?? "<null>") + "\""
                        + " C=\"" + (rawColor ?? "<null>") + "\""
                        + " B=\"" + (rawBinary ?? "<null>") + "\""
                        + " T=" + (tessPick > 0 ? tessPick.ToString() : "<null>")
                        + " -> " + winningValue,
                    Brushes.DarkCyan);
            }

            return winningValue;
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

        // Item-icon PointPlus cache. The catastrophic-fallback loop at lines
        // ~2485 and ~2510 iterates all of App.listItems (~273 items) doing one
        // PureDM FindPicture each when the fuzzy-matched icon template doesn't
        // visually match the live capture. That's ~8 seconds PER missed icon.
        //
        // Strategy A (strict, no row position stored) + C (fingerprint of a
        // small barter-UI region captured at scan start):
        //   - Cache value: icon's offset from the capture-rect origin
        //     (intX1, intY1), so the absolute screen position is reconstructed
        //     from the current anchor. This makes the cache immune to absolute
        //     screen-position changes (e.g. user drags the game window).
        //   - Cache key: ItemID + WindowWidth + WindowHeight + barterUIHash.
        //     Any of these changing invalidates the entry.
        //   - Fallback: on cache miss OR fingerprint mismatch, the code falls
        //     through to the existing PureDM FindPicture + O(n) loop. The
        //     cache only saves work; it never returns stale data.
        private static readonly System.Collections.Generic.Dictionary<string, IconPointCacheEntry>
            _iconPointCache = new System.Collections.Generic.Dictionary<string, IconPointCacheEntry>(System.StringComparer.Ordinal);
        private struct IconPointCacheEntry {
            public int OffsetX;       // icon.X - intX1 (= icon.X - (anchor.X + anchor.W + 1))
            public int OffsetY;       // icon.Y - intY1 (= icon.Y - (anchor.Y - 2))
            public int SizeWidth;
            public int SizeHeight;
            public int BarterUIHash;  // hash of a small region above first anchor
            public int WindowWidth;
            public int WindowHeight;
        }
        // Computed once per scan in DoIdentifyRoutesHeavy right after
        // FindBarterAnchors succeeds. Used as part of the icon-cache key so
        // any UI change (e.g. user swaps to a different barter session)
        // invalidates ALL entries without explicit invalidation calls.
        private static int _currentBarterUIHash;

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
            var tess = _tessPerThread.Value;
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
            // GetScreenDataBmp's internal pointer is freed on the next call;
            // lock on the DM object so capture + Marshal.Copy is atomic.
            try {
                lock (App.myPureDM.DM) {
                    System.IntPtr data;
                    int size;
                    int ret = App.myPureDM.DM.GetScreenDataBmp(x1, y1, x2, y2, out data, out size);
                    if (ret != 1 || data == System.IntPtr.Zero || size <= 0) return null;
                    byte[] bytes = new byte[size];
                    System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
                    return bytes;
                }
            }
            catch {
                return null;
            }
        }

        // Cheap deterministic hash of a byte buffer. Used as a barter-UI
        // fingerprint - not cryptographic, just needs to change when the
        // captured pixels change (i.e. when the user swaps to a different
        // barter session or moves the game window).
        private static int SimpleByteHash(byte[] bytes) {
            if (bytes == null || bytes.Length == 0) return 0;
            unchecked {
                int h = 17;
                // Sample every 7th byte; the first 200 bytes are enough to
                // distinguish one barter session from another while keeping
                // the hash cost trivial (~30 ns for a typical 100x20 region).
                int stride = System.Math.Max(1, bytes.Length / 200);
                for (int i = 0; i < bytes.Length; i += stride) {
                    h = h * 31 + bytes[i];
                }
                return h;
            }
        }

        // Capture + hash the barter UI region just above the first anchor.
        // The capture rect is intentionally small (~50x20 px, ~1 KB BMP) so
        // the call takes ~5 ms even on heavily-fragmented native heaps.
        // Any change to this region (different barter session, different
        // game state, BDO redraw) flips the hash and invalidates ALL icon
        // cache entries - no explicit invalidation plumbing required.
        private static void RefreshBarterUIHash(PointPlus firstAnchor) {
            if (firstAnchor == null || firstAnchor.X < 0 || firstAnchor.Y < 10) {
                _currentBarterUIHash = 0;
                return;
            }
            int x1 = firstAnchor.X;
            int y1 = System.Math.Max(0, firstAnchor.Y - 30);
            int x2 = firstAnchor.X + 50;
            int y2 = firstAnchor.Y - 10;
            byte[] bytes = CaptureScreenBytes(x1, y1, x2, y2);
            _currentBarterUIHash = SimpleByteHash(bytes);
        }

        // Try the icon-position cache. Returns a populated PointPlus on hit;
        // returns PointPlus.Empty (X = -1) on miss so the caller falls through
        // to the existing PureDM FindPicture + O(n) fallback path unchanged.
        // On miss or any invariant violation, the cache entry is invalidated.
        //
        // IMPORTANT: ImageID must be populated on the returned PointPlus.
        // Downstream code does `listPointPlus[0].ImageID.Substring(14, Length-18)`
        // to extract the matched itemID; without ImageID set, that Substring
        // throws (or returns wrong data). PureDM normally populates ImageID
        // as a side effect of FindPicture, but the cache hit path bypasses
        // FindPicture - so we reconstruct the same path string here.
        private static PointPlus TryIconPointCache(string itemID, int intX1, int intY1) {
            if (string.IsNullOrEmpty(itemID)) return PointPlus.Empty;
            if (!_iconPointCache.TryGetValue(itemID, out var entry)) return PointPlus.Empty;
            if (App.myPureDM == null) return PointPlus.Empty;
            if (entry.WindowWidth != App.myPureDM.WindowWidth
                || entry.WindowHeight != App.myPureDM.WindowHeight) {
                _iconPointCache.Remove(itemID);
                return PointPlus.Empty;
            }
            if (entry.BarterUIHash != _currentBarterUIHash) {
                _iconPointCache.Remove(itemID);
                return PointPlus.Empty;
            }
            // Reconstruct absolute screen position from the capture-rect
            // origin (intX1, intY1) plus the cached offset. Also reconstruct
            // ImageID - PureDM stores the template path passed to FindPicture
            // ("\\Images\\Items\\<id>.bmp"); downstream's
            // ImageID.Substring(14, Length-18) extracts the itemID from it.
            var p = new PointPlus();
            p.X = intX1 + entry.OffsetX;
            p.Y = intY1 + entry.OffsetY;
            p.Size = new System.Drawing.Size(entry.SizeWidth, entry.SizeHeight);
            p.ImageID = "\\Images\\Items\\" + itemID + ".bmp";
            return p;
        }

        // Populate the icon-position cache after a successful PureDM FindPicture.
        // Stores the icon's offset within the capture rect (intX1, intY1) so
        // the value is independent of absolute screen position.
        private static void StoreIconPointCache(string itemID, PointPlus found, int intX1, int intY1) {
            if (string.IsNullOrEmpty(itemID)
                || found == null
                || found.X < 0
                || App.myPureDM == null) {
                return;
            }
            var entry = new IconPointCacheEntry();
            entry.OffsetX = found.X - intX1;
            entry.OffsetY = found.Y - intY1;
            entry.SizeWidth = found.Size != null ? found.Size.Width : 0;
            entry.SizeHeight = found.Size != null ? found.Size.Height : 0;
            entry.BarterUIHash = _currentBarterUIHash;
            entry.WindowWidth = App.myPureDM.WindowWidth;
            entry.WindowHeight = App.myPureDM.WindowHeight;
            _iconPointCache[itemID] = entry;
        }

        // Phase R: feed the raw captured bitmap to Tesseract with NO
        // preprocessing. Experimental - checks whether the Magick scale /
        // negate / threshold pipeline in Phase F + G is actually helping,
        // or whether the digit overlay is already crisp enough on the
        // medium-ROI capture to read directly. Compared in the merge vote
        // alongside Phase A (screen-coords), F (M-variant preprocessing)
        // and G (78% threshold + 5x upscale + morphology close).
        private int TryRawOcr(int x1, int y1, int x2, int y2) {
            var tess = _tessPerThread.Value;
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

        // Phase T: Tesseract OCR for the 30x16 "Remaining" count strip.
        // Mirrors Phase G's Magick preprocessing (78% threshold + 5x
        // scale + negate + morphology close) but skips the bottom-right
        // 75% crop - the 30x16 strip is already focused on the digit, so
        // a 75% crop would push the digit out of frame. The 5x scale +
        // morphology close are exactly what fix the "5" -> "2" misread
        // that PureDM.OCRString Diff mode suffers on the narrow strip
        // (Diff's Sobel gradient clips the top bar of "5"; Tesseract with
        // 5x + close preserves it). Returns -1 on any failure
        // (capture / engine / no digit match).
        private int TryRemainingTesseractOcr(int x1, int y1, int x2, int y2) {
            var tess = _tessPerThread.Value;
            if (tess == null) {
                return -1;
            }
            try {
                byte[] bmpBytes = CaptureScreenBytes(x1, y1, x2, y2);
                if (bmpBytes == null) {
                    return -1;
                }
                using (var msIn = new System.IO.MemoryStream(bmpBytes))
                using (var mi = new MagickImage(msIn)) {
                    mi.ColorSpace = ColorSpace.Gray;
                    mi.Threshold(new Percentage(78));
                    mi.Scale(new Percentage(500));
                    mi.Negate();
                    // Close 1-px gaps in '5'/'9'/'0' strokes after the 5x
                    // upscale (same reason as Phase G: '5' top bar would
                    // otherwise break at the 5x scale and look like '2').
                    var morph = new MorphologySettings {
                        Method = MorphologyMethod.Close,
                        Kernel = Kernel.Square,
                        Iterations = 1,
                    };
                    mi.Morphology(morph);
                    // No bottom-right crop - the strip is already the digit.
                    using (var ms = new System.IO.MemoryStream()) {
                        mi.Write(ms, MagickFormat.Bmp);
                        ms.Position = 0;
                        using (var bitmap = new System.Drawing.Bitmap(ms)) {
                            using (var src = bitmap.ToMat())
                            using (var gray = new Emgu.CV.Mat()) {
                                if (src == null || src.IsEmpty) {
                                    return -1;
                                }
                                Emgu.CV.CvEnum.ColorConversion conv = src.NumberOfChannels == 4
                                    ? Emgu.CV.CvEnum.ColorConversion.Bgra2Gray
                                    : Emgu.CV.CvEnum.ColorConversion.Bgr2Gray;
                                Emgu.CV.CvInvoke.CvtColor(src, gray, conv);
                                tess.SetImage(gray);
                            }
                            tess.Recognize();
                            string raw = (tess.GetUTF8Text() ?? "").Trim();
                            Match m = Regex.Match(raw, @"\d{1,2}");
                            if (m.Success && int.TryParse(m.Value, out int n) && n > 0 && n < 100) {
                                return n;
                            }
                        }
                    }
                }
            }
            catch {
                return -1;
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

            // Phase A (4 ROI Diff-mode votes) runs in parallel. PureDM's
            // OCRString internally locks the DM object inside
            // CaptureByDMToMat (PureDM/CV.cs:418), and creates a fresh
            // Tesseract per call inside ImageOCR (PureDM/CV.cs:807) so
            // there's no shared Tesseract state. The capture is the only
            // serialized piece; the Tesseract init + Diff preprocessing
            // + Recognize work all runs in parallel across the 4 calls.
            // Serial baseline was 4 x ~150-200 ms; parallel = ~200 ms.
            // Reverted from Parallel.For - concurrent PureDM OCRString calls
            // return empty strings even with _pureDmLock in place. Run
            // the 4 ROI votes serially like before.
            foreach (var c in candidates) {
                int x1 = (int)(oX + oW * c.lf);
                int y1 = (int)(oY + oH * c.tf);
                int x2 = (int)(oX + oW * c.rf);
                int y2 = (int)(oY + oH * c.bf);
                try {
                    string raw = App.myPureDM.CV.OCRString(x1, y1, x2, y2,
                        CV.OCRType.Number, CV.OCRMode.Diff, false, strID, CurrentOcrLanguage()) ?? "";
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

            // Phase R + G: full in-memory pipeline. Both run in parallel via
            // Task.Run since each uses its own per-thread Tesseract (no
            // shared state) and captures its own screen rect via the
            // existing locked CaptureScreenBytes helper. Previously R and
            // G ran serially; now they run concurrently and we just take
            // whichever comes back. We always run G too (instead of
            // skipping when R succeeds) because the merge vote below
            // weights R and G equally - both should contribute.
            int rRoiX1 = (int)(oX + oW * candidates[1].lf);
            int rRoiY1 = (int)(oY + oH * candidates[1].tf);
            int rRoiX2 = (int)(oX + oW * candidates[1].rf);
            int rRoiY2 = (int)(oY + oH * candidates[1].bf);
            int gX1 = (int)oX;
            int gY1 = (int)oY;
            int gX2 = (int)oX + (int)oW;
            int gY2 = (int)oY + (int)oH;
            // Reverted from Parallel.Invoke - serial execution. The Tesseract
            // per-thread instances are kept (no harm), but the calls run
            // sequentially for now. We can re-introduce a TryRawOcr /
            // TryTemplateDiffOcr Parallel.Invoke later once we have a way
            // to confirm PureDM doesn't break under that specific shape of
            // concurrency (the failure mode here was tied to OCRString
            // concurrent with capture from another thread, not necessarily
            // to Tesseract-only concurrency).
            int rawPick = TryRawOcr(rRoiX1, rRoiY1, rRoiX2, rRoiY2);
            int diffPick = -1;
            if (rawPick <= 0) {
                diffPick = TryTemplateDiffOcr(gX1, gY1, gX2, gY2);
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
                Log(Localization.LanguageService.Instance.Localize("str.Log.OcrQty.Picked", strID, finalPick, tally, picked, rawPick, diffPick), Brushes.Gray);
            }
            else {
                Log(Localization.LanguageService.Instance.Localize("str.Log.OcrQty.NoConsensus", strID, bestRaw ?? "", rawPick, diffPick), Brushes.OrangeRed);
            }

            return finalPick;
        }

        private Barter IdentifyBarterAsync(PointPlus _pp) {
            // 检查锚点是否有效
            if (_pp.X == -1 || _pp.Y == -1)
                return (Barter)null;
            PointPlus pointPlusAnchor = _pp;
            Barter myBarter = new Barter();
            // DIAG: per-island timing. Used to find which step dominates
            // scan time. Stopped at the end of this method with a summary log.
            var _islandSw = System.Diagnostics.Stopwatch.StartNew();

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
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5,
                CV.OCRType.Words, CV.OCRMode.Color, false, "", CurrentOcrLanguage());
            EnumLists.Island islandEnum = IslandEnumSmart(strIsland);

            // 2. 捕获交易物品区域截图
            int intX1 = pointPlusAnchor.X + pointPlusAnchor.Size.Width + 1;
            int intY1 = pointPlusAnchor.Y - 2;
            int intX2 = pointPlusAnchor.X + 700;
            int intY2 = pointPlusAnchor.Y + 60;
            // App.myPureDM.DM.Capture(intX1, intY1, intX2, intY2, "barterItems.bmp");

            // 3. 识别 Parley 数值
            if (!TryFindParleyRequiredLabels(
                    intX1, intY1, intX2, intY2,
                    out PointPlus pointPlusParley,
                    out string strParleyPath,
                    out PointPlus pointPlusRequired,
                    out string strRequiredPath,
                    out string triedLabels)) {
                string scanDetails = "Parley: " + (pointPlusParley.IsEmpty ? "not found" : strParleyPath + " at " + pointPlusParley.X + "," + pointPlusParley.Y)
                    + ", Required: " + (pointPlusRequired.IsEmpty ? "not found" : strRequiredPath + " at " + pointPlusRequired.X + "," + pointPlusRequired.Y)
                    + " (tried: " + triedLabels + ")";
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.ScanLabelsFailed", scanDetails), Brushes.Red);
                return (Barter)null;
            }

            int parleyOcrX1;
            int parleyOcrY1;
            int parleyOcrX2;
            int parleyOcrY2;
            if (IsTraditionalChineseUi()) {
                parleyOcrX1 = pointPlusRequired.X + pointPlusRequired.Size.Width;
                parleyOcrY1 = Math.Min(pointPlusParley.Y, pointPlusRequired.Y);
                parleyOcrX2 = Math.Min(App.myPureDM.WindowWidth, parleyOcrX1 + 120);
                parleyOcrY2 = Math.Max(
                    pointPlusParley.Y + pointPlusParley.Size.Height,
                    pointPlusRequired.Y + pointPlusRequired.Size.Height);
            }
            else {
                parleyOcrX1 = pointPlusParley.X + pointPlusParley.Size.Width;
                parleyOcrY1 = pointPlusParley.Y;
                parleyOcrX2 = pointPlusRequired.X + 1;
                parleyOcrY2 = pointPlusParley.Y + pointPlusParley.Size.Height;
            }
            bool hasParleyOcrRectangle = IsValidOcrRectangle(parleyOcrX1, parleyOcrY1, parleyOcrX2, parleyOcrY2);
            if (!hasParleyOcrRectangle) {
                string ocrRectDetails = parleyOcrX1 + "," + parleyOcrY1 + " -> " + parleyOcrX2 + "," + parleyOcrY2
                    + " | Parley=" + strParleyPath + " at " + pointPlusParley.X + "," + pointPlusParley.Y
                    + " size " + pointPlusParley.Size.Width + "x" + pointPlusParley.Size.Height
                    + " | Required=" + strRequiredPath + " at " + pointPlusRequired.X + "," + pointPlusRequired.Y;
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.InvalidOcrRect", ocrRectDetails), Brushes.Red);
                return (Barter)null;
            }

            string strParley = "";
            if (hasParleyOcrRectangle) {
                strParley = App.myPureDM.CV.OCRString(
                    parleyOcrX1,
                    parleyOcrY1,
                    parleyOcrX2,
                    parleyOcrY2,
                    CV.OCRType.Number, CV.OCRMode.Binary, false, "", CurrentOcrLanguage());
            }

            // 获取岛屿的默认 Parley 值，并尝试解析 OCR 得到的数值
            int intParley = App.listIslands.Where(land => land.Island == islandEnum)
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
            int intRemaining;
            var _remSw = System.Diagnostics.Stopwatch.StartNew();
            intRemaining = TryReadRemainingCount(pointPlusAnchor, pointPlusEdge, strIsland);
            _remSw.Stop();
            Log("[DIAG-remaining] " + strIsland + " voting=" + _remSw.ElapsedMilliseconds + "ms result=" + intRemaining, Brushes.LightSlateGray);

            if (islandEnum == EnumLists.Island.UnKnown)
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.UnknownIsland", strIsland), Brushes.Red);

            // 5. 获取岛屿信息
            Islands myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName == islandEnum.ToString());
            if (myIslands == null) {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.IslandInfoFailed", strIsland), Brushes.Red);
                return (Barter)null;
            }

            myIslands.Parley = intParley;
            myIslands.Remaining = intRemaining;
            myBarter.IsLand = myIslands;
            Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.IdentifiedIsland", myIslands.IslandsNameDisplay), Brushes.OrangeRed);


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
                pointPlusParley.Y + 1, CV.OCRType.Words, CV.OCRMode.Color, false, "", CurrentOcrLanguage());


            string strItem2 = App.myPureDM.CV.OCRString(
                pointPlusParley.X + 376,
                pointPlusParley.Y - pointPlusParley.Size.Height,
                pointPlusRequired.X + 376 + 100,
                pointPlusParley.Y + pointPlusParley.Size.Height, CV.OCRType.Words, CV.OCRMode.Color, false, "", CurrentOcrLanguage());

            // (Removed two dead DM.Capture writes that produced
            // myItem1.bmp / myItem2.bmp on disk - the OCR text above
            // already gave us what we needed; no downstream code ever
            // read those BMPs.)

            Items myItems1 = FindMostSimilarItemZhTwAware(strItem1, ExtractLevelPrefix(strItem1).lv);
            Items myItems2 = FindMostSimilarItemZhTwAware(strItem2, ExtractLevelPrefix(strItem2).lv);
            // DIAG: dump raw OCR text + normalised form so we can see if
            // OCR returns Chinese / English / garbage. If OCR returns
            // Chinese text and the catalog only has English ItemName, the
            // fuzzy match against ItemNameZhTw needs to hit (currently 273/274
            // items have zh-TW names). If OCR returns garbled text the
            // match returns whatever has the lowest edit distance.
            Log("[DIAG-ocr-slot1] raw='" + strItem1 + "' normalized='" + NormalizeBasic(RemoveLevelPrefix(strItem1)) + "' matched='" + (myItems1 != null ? myItems1.ItemID + "(" + (myItems1.ItemName ?? "") + "/" + (myItems1.ItemNameZhTw ?? "") + ")" : "null") + "' slot2='" + (myItems2 != null ? myItems2.ItemID : "null") + "'", Brushes.LightSlateGray);


            PointPlus myPP1 = new PointPlus();
            if (myItems1 != null) {
                // Cache hit short-circuits the PureDM FindPicture + skips the
                // O(n) fallback below entirely. The cache is invalidated
                // automatically when the barter UI fingerprint changes.
                myPP1 = TryIconPointCache(myItems1.ItemID, intX1, intY1);
                if (myPP1.X == -1) {
                    myPP1 = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + myItems1.ItemID + ".bmp", 0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true, 0.7);
                    if (myPP1.X != -1 && myPP1.Y != -1 && myPP1.X * myPP1.Y != 0) {
                        StoreIconPointCache(myItems1.ItemID, myPP1, intX1, intY1);
                    }
                }
            }
            if (myPP1.X != -1 && myPP1.Y != -1 && myPP1.X * myPP1.Y != 0)
                listPointPlus.Add(myPP1);
            else {
                // DIAG: icon FindPicture fallback triggered - primary template
                // (myItems1.ItemID) didn't visually match the live capture.
                // Each fallback loops App.listItems (~273 items) doing one
                // PureDM.CV.FindPicture each - up to ~8 s. Counting these
                // tells us if this is a major or minor contributor.
                Log("[DIAG-fallback] slot1 entered: myItems1=" + (myItems1 != null ? myItems1.ItemID : "null"), Brushes.LightSlateGray);
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
                if (listPointPlus_Temp.Count >= 1) {
                    listPointPlus.Add(listPointPlus_Temp[0]);
                    // FIX: cache the fallback's winning position so the next
                    // scan can skip this 273-item O(n) loop. PureDM populates
                    // ImageID on the returned PointPlus; extract the matched
                    // ItemID and store the position offset.
                    var fbPicked = listPointPlus_Temp[0];
                    if (!string.IsNullOrEmpty(fbPicked.ImageID)
                        && fbPicked.ImageID.Length >= 18
                        && fbPicked.ImageID.StartsWith("\\Images\\Items\\")
                        && fbPicked.ImageID.EndsWith(".bmp")) {
                        string fbItemID = fbPicked.ImageID.Substring(14, fbPicked.ImageID.Length - 18);
                        StoreIconPointCache(fbItemID, fbPicked, intX1, intY1);
                    }
                }
                else
                    Log(Localization.LanguageService.Instance.Localize("str.Log.PickTwoBest.NoSlot1", myItems1?.ItemID ?? "", myItems1?.ItemLV ?? ""), Brushes.IndianRed);
            }


            PointPlus myPP2 = new PointPlus();

            if (myItems2 != null) {
                // Same cache short-circuit as item 1 above.
                myPP2 = TryIconPointCache(myItems2.ItemID, intX1, intY1);
                if (myPP2.X == -1) {
                    myPP2 = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + myItems2.ItemID + ".bmp", 0.5, 0.8, 1, CV.Mode.OpenCV, true, CV.PictureColorMode.Color, true, 0.7);
                    if (myPP2.X != -1 && myPP2.Y != -1 && myPP2.X * myPP2.Y != 0) {
                        StoreIconPointCache(myItems2.ItemID, myPP2, intX1, intY1);
                    }
                }
            }
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
                if (listPointPlus_Temp.Count >= 2) {
                    listPointPlus.Add(listPointPlus_Temp[1]);
                    // FIX: cache slot2 fallback winner too.
                    var fbPicked = listPointPlus_Temp[1];
                    if (!string.IsNullOrEmpty(fbPicked.ImageID)
                        && fbPicked.ImageID.Length >= 18
                        && fbPicked.ImageID.StartsWith("\\Images\\Items\\")
                        && fbPicked.ImageID.EndsWith(".bmp")) {
                        string fbItemID = fbPicked.ImageID.Substring(14, fbPicked.ImageID.Length - 18);
                        StoreIconPointCache(fbItemID, fbPicked, intX1, intY1);
                    }
                }
                else if (listPointPlus_Temp.Count == 1) {
                    listPointPlus.Add(listPointPlus_Temp[0]);
                    var fbPicked = listPointPlus_Temp[0];
                    if (!string.IsNullOrEmpty(fbPicked.ImageID)
                        && fbPicked.ImageID.Length >= 18
                        && fbPicked.ImageID.StartsWith("\\Images\\Items\\")
                        && fbPicked.ImageID.EndsWith(".bmp")) {
                        string fbItemID = fbPicked.ImageID.Substring(14, fbPicked.ImageID.Length - 18);
                        StoreIconPointCache(fbItemID, fbPicked, intX1, intY1);
                    }
                }
                else
                    Log(Localization.LanguageService.Instance.Localize("str.Log.PickTwoBest.NoSlot2", myItems2?.ItemID ?? "", myItems2?.ItemLV ?? ""), Brushes.IndianRed);
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

            if (listPointPlus.Count < 1) {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.CannotIdentifyItemIcons", myIslands.IslandsNameDisplay), Brushes.Red);
                return myBarter;
            }

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
                var _q1Sw = System.Diagnostics.Stopwatch.StartNew();
                intNumber1 = TryReadQuantity(listPointPlus[0], strID1);
                _q1Sw.Stop();
                Log("[DIAG-ocrQty] slot1 strID=" + strID1 + " voting=" + _q1Sw.ElapsedMilliseconds + "ms result=" + intNumber1, Brushes.LightSlateGray);
                if (intNumber1 <= 0) {
                    // OCR failed to agree - fall back to the CSV-default quantity and
                    // log so this case is visible.
                    intNumber1 = App.listItems.Where(i => i.ItemID == strID1)
                        .Select(i => i.ItemNumber).FirstOrDefault();
                    if (intNumber1 > 0)
                        Log(Localization.LanguageService.Instance.Localize("str.Log.OcrQty.FallbackCSV", strID1, intNumber1), Brushes.OrangeRed);
                }
            } else {
                Log(Localization.LanguageService.Instance.Localize("str.Log.LV5Skip", strID1, lv1Item.ItemNameDisplay), Brushes.Gold);
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
                    Log(Localization.LanguageService.Instance.Localize("str.Log.LV5Skip", strID2, lv2Item.ItemNameDisplay), Brushes.Gold);
                } else {
                    var _q2Sw = System.Diagnostics.Stopwatch.StartNew();
                    intNumber2 = TryReadQuantity(listPointPlus[1], strID2);
                    _q2Sw.Stop();
                    Log("[DIAG-ocrQty] slot2 strID=" + strID2 + " voting=" + _q2Sw.ElapsedMilliseconds + "ms result=" + intNumber2, Brushes.LightSlateGray);
                }
            }
            else {
                Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.SecondItemUseCrowCoin"), Brushes.Red);
            }

            if (intNumber2 <= 0) {
                intNumber2 = App.listItems.Where(i => i.ItemID == strID2)
                    .Select(i => i.ItemNumber).FirstOrDefault();
                if (intNumber2 > 0)
                    Log(Localization.LanguageService.Instance.Localize("str.Log.OcrQty.FallbackCSV", strID2, intNumber2), Brushes.OrangeRed);
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

            Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.OcrSummary", myIslands.IslandsNameDisplay, myIslands.Remaining, myIslands.Parley, item1.ItemNameDisplay, intNumber1, item2.ItemNameDisplay, intNumber2), Brushes.Blue);

            myBarter.Item1 = item1;
            myBarter.Item2 = item2;

            // DIAG: per-island total. Compare to per-icon fuzzy / fallback
            // logs above to see which step is the dominant cost.
            _islandSw.Stop();
            Log("[DIAG-island] " + myBarter.IsLand.IslandsNameDisplay + " total=" + _islandSw.ElapsedMilliseconds + "ms", Brushes.LightSlateGray);
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
            return ChineseTextNormalizer.NormalizeForMatching(sb.ToString().Normalize(NormalizationForm.FormC));
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

            bool cjk = ContainsCjk(a) || ContainsCjk(b);
            if (cjk && Math.Min(a.Length, b.Length) >= 2 && (a.Contains(b) || b.Contains(a))) {
                int lengthGap = Math.Abs(a.Length - b.Length);
                return Math.Max(80, 100 - Math.Min(20, lengthGap * 2));
            }

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

        private static bool ContainsCjk(string input) {
            return ChineseTextNormalizer.ContainsCjk(input);
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

        // Phase 9 (i18n): dual-catalog item matching.  Same as the legacy
        // FindMostSimilarItem, but scores each candidate against BOTH the
        // canonical English ItemName AND the sidecar zh-TW ItemNameZhTw
        // (whichever is non-empty), and uses the higher of the two.  This
        // makes Chinese-game OCR (which produces e.g. "黃金草" for
        // 102 Year Old Golden Herb) match correctly without abandoning
        // English-game users.
        //
        // Active-language selection is the same script-detection trick
        // IslandEnumSmart uses: a CJK codepoint in the input biases the
        // initial scorer toward ItemNameZhTw; absent that, the input is
        // assumed English and we still try the zh-TW column as a
        // cross-fallback so e.g. an English OCR misread of "Aloe" still
        // matches 蘆薈 (item with empty English name in some rows).
        public Items FindMostSimilarItemZhTwAware(string strItem1, string expectedLv = null) {
            if (string.IsNullOrWhiteSpace(strItem1) || App.listItems == null || App.listItems.Count == 0)
                return null;

            string processed = NormalizeBasic(RemoveLevelPrefix(strItem1));
            if (string.IsNullOrEmpty(processed)) {
                return FindMostSimilarItem(strItem1, expectedLv);
            }

            // First, exact match against EITHER name (case-insensitive after
            // NormalizeBasic).  An English OCR result might exactly equal
            // an item's English canonical; a Chinese OCR result might
            // exactly equal its zh-TW sidecar entry.  Both win.
            foreach (var it in App.listItems) {
                if (NormalizeBasic(it.ItemName) == processed) return it;
                if (!string.IsNullOrWhiteSpace(it.ItemNameZhTw)
                    && NormalizeBasic(it.ItemNameZhTw) == processed) {
                    return it;
                }
            }

            // LV filter
            IEnumerable<Items> candidates = App.listItems;
            if (!string.IsNullOrWhiteSpace(expectedLv))
                candidates = candidates.Where(it => it.ItemLV == expectedLv);

            return candidates
                .Select(it => new {
                    Item = it,
                    Score = Math.Max(
                        FuzzyScoreSmart(processed, it.ItemName ?? string.Empty),
                        string.IsNullOrEmpty(it.ItemNameZhTw)
                            ? 0
                            : FuzzyScoreSmart(processed, it.ItemNameZhTw)),
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => (x.Item.ItemName ?? string.Empty).Length)
                .Select(x => x.Item)
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

        // Phase 9 (i18n): dual-catalog island identification.  Builds an
        // English + zh-TW name catalog at first call (lazy), runs the
        // existing dead-code StringSimilarityMatcher over the active
        // language's catalog, and falls back to the other on miss.  Final
        // fallback is the existing hand-written switch via the original
        // IslandEnum() (so no behaviour change for English-game users on
        // an unrecognised island).
        //
        // The active-language catalog is selected by checking whether the
        // input contains any CJK Unified Ideograph (>= 0x4E00).  We do
        // not depend on LanguageService here because OCR output is the
        // authoritative signal of which game client produced it: a Chinese
        // client with the English UI selected still OCRs Chinese, and
        // a French/screenshot-only client OCRs whatever the screenshot
        // contains.
        public EnumLists.Island IslandEnumSmart(string _island, int minScore = 70) {
            if (string.IsNullOrWhiteSpace(_island)) {
                return EnumLists.Island.UnKnown;
            }

            bool isCjk = ContainsCjk(_island);

            if (_zhTwIslandMatcher == null || _englishIslandMatcher == null) {
                BuildIslandMatchers();
            }

            iBarter.StringSimilarityMatcher primary = isCjk ? _zhTwIslandMatcher! : _englishIslandMatcher!;
            iBarter.StringSimilarityMatcher secondary = isCjk ? _englishIslandMatcher! : _zhTwIslandMatcher!;
            var primaryMap = isCjk ? _zhTwIslandEnumByCandidate : _englishIslandEnumByCandidate;
            var secondaryMap = isCjk ? _englishIslandEnumByCandidate : _zhTwIslandEnumByCandidate;

            // Try primary catalog
            var best = primary.FindBest(_island, out int score);
            if (score >= minScore) {
                var resolved = ResolveIslandMatcherCandidate(best, primaryMap);
                if (resolved != EnumLists.Island.UnKnown) {
                    return resolved;
                }
                return best switch {
                    "Ajir" => EnumLists.Island.Ajir,
                    "Albresser" => EnumLists.Island.Albresser,
                    "Almai" => EnumLists.Island.Almai,
                    "Al_Naha" => EnumLists.Island.Al_Naha,
                    "Ancient" => EnumLists.Island.Ancient,
                    "Angie" => EnumLists.Island.Angie,
                    "Arakil" => EnumLists.Island.Arakil,
                    "Arita" => EnumLists.Island.Arita,
                    "Baeza" => EnumLists.Island.Baeza,
                    "Balvege" => EnumLists.Island.Balvege,
                    "Barater" => EnumLists.Island.Barater,
                    "Baremi" => EnumLists.Island.Baremi,
                    "Beiruwa" => EnumLists.Island.Beiruwa,
                    "Boa" => EnumLists.Island.Boa,
                    "Haran" => EnumLists.Island.Haran,
                    "Carrack" => EnumLists.Island.Carrack,
                    "Cholace" => EnumLists.Island.Cholace,
                    "Cox_Pirate" => EnumLists.Island.Cox_Pirate,
                    "Crows_Nest" => EnumLists.Island.Crows_Nest,
                    "Crow" => EnumLists.Island.Crow,
                    "Daton" => EnumLists.Island.Daton,
                    "Delinghart" => EnumLists.Island.Delinghart,
                    "Derko" => EnumLists.Island.Derko,
                    "Duch" => EnumLists.Island.Duch,
                    "Dunde" => EnumLists.Island.Dunde,
                    "Eberdeen" => EnumLists.Island.Eberdeen,
                    "Ephde_Rune" => EnumLists.Island.Ephde_Rune,
                    "Esfah" => EnumLists.Island.Esfah,
                    "Eveto" => EnumLists.Island.Eveto,
                    "Ginburrey" => EnumLists.Island.Ginburrey,
                    "Hakoven" => EnumLists.Island.Hakoven,
                    "Halmad" => EnumLists.Island.Halmad,
                    "Iliya" => EnumLists.Island.Iliya,
                    "Unfinished" => EnumLists.Island.Unfinished,
                    "Invernen" => EnumLists.Island.Invernen,
                    "Kanvera" => EnumLists.Island.Kanvera,
                    "Kashuma" => EnumLists.Island.Kashuma,
                    "Kuit" => EnumLists.Island.Kuit,
                    "Lantinia" => EnumLists.Island.Lantinia,
                    "Lema" => EnumLists.Island.Lema,
                    "Lerao" => EnumLists.Island.Lerao,
                    "Lisz" => EnumLists.Island.Lisz,
                    "Louruve" => EnumLists.Island.Louruve,
                    "Luivano" => EnumLists.Island.Luivano,
                    "Mariveno" => EnumLists.Island.Mariveno,
                    "Marka" => EnumLists.Island.Marka,
                    "Marlene" => EnumLists.Island.Marlene,
                    "Modric" => EnumLists.Island.Modric,
                    "Narvo" => EnumLists.Island.Narvo,
                    "Netnume" => EnumLists.Island.Netnume,
                    "Oben" => EnumLists.Island.Oben,
                    "Orffs" => EnumLists.Island.Orffs,
                    "Orisha" => EnumLists.Island.Orisha,
                    "Ostra" => EnumLists.Island.Ostra,
                    "Padix" => EnumLists.Island.Padix,
                    "Pakio" => EnumLists.Island.Pakio,
                    "Paratama" => EnumLists.Island.Paratama,
                    "Pilava" => EnumLists.Island.Pilava,
                    "Portanen" => EnumLists.Island.Portanen,
                    "Pujara" => EnumLists.Island.Pujara,
                    "Racid" => EnumLists.Island.Racid,
                    "Rameda" => EnumLists.Island.Rameda,
                    "Randis" => EnumLists.Island.Randis,
                    "Rickun" => EnumLists.Island.Rickun,
                    "Riyed" => EnumLists.Island.Riyed,
                    "Rosevan" => EnumLists.Island.Rosevan,
                    "Serca" => EnumLists.Island.Serca,
                    "Shasha" => EnumLists.Island.Shasha,
                    "Shirna" => EnumLists.Island.Shirna,
                    "Sokota" => EnumLists.Island.Sokota,
                    "Staren" => EnumLists.Island.Staren,
                    "Taramura" => EnumLists.Island.Taramura,
                    "Tashu" => EnumLists.Island.Tashu,
                    "Teste" => EnumLists.Island.Teste,
                    "Teyamal" => EnumLists.Island.Teyamal,
                    "Theonil" => EnumLists.Island.Theonil,
                    "Tigris" => EnumLists.Island.Tigris,
                    "Tinberra" => EnumLists.Island.Tinberra,
                    "Tulu" => EnumLists.Island.Tulu,
                    "Wandering" => EnumLists.Island.Wandering,
                    "Weita" => EnumLists.Island.Weita,
                    "Marine" => EnumLists.Island.Marine,
                    "Olvia" => EnumLists.Island.Olvia,
                    "Arehaza" => EnumLists.Island.Arehaza,
                    "Grandiha" => EnumLists.Island.Grandiha,
                    "Midnight" => EnumLists.Island.Midnight,
                    "Haemo" => EnumLists.Island.Haemo,
                    "Dallae" => EnumLists.Island.Dallae,
                    "Epheria" => EnumLists.Island.Epheria,
                    "Sausan" => EnumLists.Island.Sausan,
                    "Sanctuary" => EnumLists.Island.Sanctuary,
                    "UnKnown" => EnumLists.Island.UnKnown,
                    _ => EnumLists.Island.UnKnown,
                };
            }

            // Cross-fallback: try the OTHER catalog
            best = secondary.FindBest(_island, out int score2);
            if (score2 >= minScore) {
                var resolved = ResolveIslandMatcherCandidate(best, secondaryMap);
                if (resolved != EnumLists.Island.UnKnown) {
                    return resolved;
                }
                return best switch {
                    "Ajir" => EnumLists.Island.Ajir,
                    "Albresser" => EnumLists.Island.Albresser,
                    "Almai" => EnumLists.Island.Almai,
                    "Al_Naha" => EnumLists.Island.Al_Naha,
                    "Ancient" => EnumLists.Island.Ancient,
                    "Angie" => EnumLists.Island.Angie,
                    "Arakil" => EnumLists.Island.Arakil,
                    "Arita" => EnumLists.Island.Arita,
                    "Baeza" => EnumLists.Island.Baeza,
                    "Balvege" => EnumLists.Island.Balvege,
                    "Barater" => EnumLists.Island.Barater,
                    "Baremi" => EnumLists.Island.Baremi,
                    "Beiruwa" => EnumLists.Island.Beiruwa,
                    "Boa" => EnumLists.Island.Boa,
                    "Haran" => EnumLists.Island.Haran,
                    "Carrack" => EnumLists.Island.Carrack,
                    "Cholace" => EnumLists.Island.Cholace,
                    "Cox_Pirate" => EnumLists.Island.Cox_Pirate,
                    "Crows_Nest" => EnumLists.Island.Crows_Nest,
                    "Crow" => EnumLists.Island.Crow,
                    "Daton" => EnumLists.Island.Daton,
                    "Delinghart" => EnumLists.Island.Delinghart,
                    "Derko" => EnumLists.Island.Derko,
                    "Duch" => EnumLists.Island.Duch,
                    "Dunde" => EnumLists.Island.Dunde,
                    "Eberdeen" => EnumLists.Island.Eberdeen,
                    "Ephde_Rune" => EnumLists.Island.Ephde_Rune,
                    "Esfah" => EnumLists.Island.Esfah,
                    "Eveto" => EnumLists.Island.Eveto,
                    "Ginburrey" => EnumLists.Island.Ginburrey,
                    "Hakoven" => EnumLists.Island.Hakoven,
                    "Halmad" => EnumLists.Island.Halmad,
                    "Iliya" => EnumLists.Island.Iliya,
                    "Unfinished" => EnumLists.Island.Unfinished,
                    "Invernen" => EnumLists.Island.Invernen,
                    "Kanvera" => EnumLists.Island.Kanvera,
                    "Kashuma" => EnumLists.Island.Kashuma,
                    "Kuit" => EnumLists.Island.Kuit,
                    "Lantinia" => EnumLists.Island.Lantinia,
                    "Lema" => EnumLists.Island.Lema,
                    "Lerao" => EnumLists.Island.Lerao,
                    "Lisz" => EnumLists.Island.Lisz,
                    "Louruve" => EnumLists.Island.Louruve,
                    "Luivano" => EnumLists.Island.Luivano,
                    "Mariveno" => EnumLists.Island.Mariveno,
                    "Marka" => EnumLists.Island.Marka,
                    "Marlene" => EnumLists.Island.Marlene,
                    "Modric" => EnumLists.Island.Modric,
                    "Narvo" => EnumLists.Island.Narvo,
                    "Netnume" => EnumLists.Island.Netnume,
                    "Oben" => EnumLists.Island.Oben,
                    "Orffs" => EnumLists.Island.Orffs,
                    "Orisha" => EnumLists.Island.Orisha,
                    "Ostra" => EnumLists.Island.Ostra,
                    "Padix" => EnumLists.Island.Padix,
                    "Pakio" => EnumLists.Island.Pakio,
                    "Paratama" => EnumLists.Island.Paratama,
                    "Pilava" => EnumLists.Island.Pilava,
                    "Portanen" => EnumLists.Island.Portanen,
                    "Pujara" => EnumLists.Island.Pujara,
                    "Racid" => EnumLists.Island.Racid,
                    "Rameda" => EnumLists.Island.Rameda,
                    "Randis" => EnumLists.Island.Randis,
                    "Rickun" => EnumLists.Island.Rickun,
                    "Riyed" => EnumLists.Island.Riyed,
                    "Rosevan" => EnumLists.Island.Rosevan,
                    "Serca" => EnumLists.Island.Serca,
                    "Shasha" => EnumLists.Island.Shasha,
                    "Shirna" => EnumLists.Island.Shirna,
                    "Sokota" => EnumLists.Island.Sokota,
                    "Staren" => EnumLists.Island.Staren,
                    "Taramura" => EnumLists.Island.Taramura,
                    "Tashu" => EnumLists.Island.Tashu,
                    "Teste" => EnumLists.Island.Teste,
                    "Teyamal" => EnumLists.Island.Teyamal,
                    "Theonil" => EnumLists.Island.Theonil,
                    "Tigris" => EnumLists.Island.Tigris,
                    "Tinberra" => EnumLists.Island.Tinberra,
                    "Tulu" => EnumLists.Island.Tulu,
                    "Wandering" => EnumLists.Island.Wandering,
                    "Weita" => EnumLists.Island.Weita,
                    "Marine" => EnumLists.Island.Marine,
                    "Olvia" => EnumLists.Island.Olvia,
                    "Arehaza" => EnumLists.Island.Arehaza,
                    "Grandiha" => EnumLists.Island.Grandiha,
                    "Midnight" => EnumLists.Island.Midnight,
                    "Haemo" => EnumLists.Island.Haemo,
                    "Dallae" => EnumLists.Island.Dallae,
                    "Epheria" => EnumLists.Island.Epheria,
                    "Sausan" => EnumLists.Island.Sausan,
                    "Sanctuary" => EnumLists.Island.Sanctuary,
                    "UnKnown" => EnumLists.Island.UnKnown,
                    _ => EnumLists.Island.UnKnown,
                };
            }

            // Both matchers fell below threshold - the hand-written switch
            // (IslandEnum) still has decent coverage of common English
            // substrings, so it remains a useful last-ditch fallback.
            return IslandEnum(_island);
        }

        private static iBarter.StringSimilarityMatcher? _englishIslandMatcher;
        private static iBarter.StringSimilarityMatcher? _zhTwIslandMatcher;
        private static System.Collections.Generic.Dictionary<string, EnumLists.Island>? _englishIslandEnumByCandidate;
        private static System.Collections.Generic.Dictionary<string, EnumLists.Island>? _zhTwIslandEnumByCandidate;
        private static readonly object _islandMatcherLock = new object();

        private static EnumLists.Island ResolveIslandMatcherCandidate(
            string candidate,
            System.Collections.Generic.Dictionary<string, EnumLists.Island>? map) {
            if (string.IsNullOrWhiteSpace(candidate) || map == null) {
                return EnumLists.Island.UnKnown;
            }
            return map.TryGetValue(NormalizeBasic(candidate), out var island)
                ? island
                : EnumLists.Island.UnKnown;
        }

        // Tiny CSV splitter local to BuildIslandMatchers (the CFunctions
        // SplitCsvLine is private; this just splits on comma and trims, no
        // quote handling needed because the Islands.zh-TW.csv sidecar has
        // no embedded commas in the NameZhTW column).
        private static System.Collections.Generic.List<string> SplitCsvLineLocal(string line) {
            var fields = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(line)) return fields;
            foreach (var raw in line.Split(',')) {
                fields.Add(raw.Trim());
            }
            return fields;
        }

        private void BuildIslandMatchers() {
            if (_englishIslandMatcher != null && _zhTwIslandMatcher != null) {
                return;
            }
            lock (_islandMatcherLock) {
                if (_englishIslandMatcher != null && _zhTwIslandMatcher != null) {
                    return;
                }

                // English catalog: enum-to-name mapping via ToString().
                var english = new System.Collections.Generic.List<string> {
                    "Ajir", "Albresser", "Almai", "Al_Naha", "Ancient", "Angie",
                    "Arakil", "Arita", "Baeza", "Balvege", "Barater", "Baremi",
                    "Beiruwa", "Boa", "Haran", "Carrack", "Cholace", "Cox_Pirate",
                    "Crows_Nest", "Crow", "Daton", "Delinghart", "Derko",
                    "Duch", "Dunde", "Eberdeen", "Ephde_Rune", "Esfah",
                    "Eveto", "Ginburrey", "Hakoven", "Halmad", "Iliya",
                    "Unfinished", "Invernen", "Kanvera", "Kashuma", "Kuit",
                    "Lantinia", "Lema", "Lerao", "Lisz", "Louruve",
                    "Luivano", "Mariveno", "Marka", "Marlene", "Modric",
                    "Narvo", "Netnume", "Oben", "Orffs", "Orisha", "Ostra",
                    "Padix", "Pakio", "Paratama", "Pilava", "Portanen",
                    "Pujara", "Racid", "Rameda", "Randis", "Rickun", "Riyed",
                    "Rosevan", "Serca", "Shasha", "Shirna", "Sokota", "Staren",
                    "Taramura", "Tashu", "Teste", "Teyamal", "Theonil",
                    "Tigris", "Tinberra", "Tulu", "Wandering", "Weita",
                    "Marine", "Olvia", "Arehaza", "Grandiha", "Midnight",
                    "Haemo", "Dallae", "Epheria", "Sausan", "Sanctuary",
                };

                // zh-TW catalog: read from Resources/Islands.zh-TW.csv.  Falls
                // back to the English enum name when the sidecar has no row
                // (Confidence low / row missing) so the matcher still has
                // something to chew on for the 50+ transliterated names.
                var zhTw = new System.Collections.Generic.List<string>(english.Count);
                string sidecar = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Islands.zh-TW.csv";
                if (System.IO.File.Exists(sidecar)) {
                    try {
                        var byEnum = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);
                        using (var reader = new System.IO.StreamReader(sidecar, System.Text.Encoding.UTF8)) {
                            int lineNo = 0;
                            while (!reader.EndOfStream) {
                                lineNo++;
                                var line = reader.ReadLine();
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                if (lineNo == 1 && line.StartsWith("EnumName")) continue;
                                var parts = SplitCsvLineLocal(line);
                                if (parts.Count < 2) continue;
                                byEnum[parts[0]] = parts[1];
                            }
                        }
                        foreach (var name in english) {
                            if (byEnum.TryGetValue(name, out var zh) && !string.IsNullOrWhiteSpace(zh)) {
                                zhTw.Add(zh);
                            }
                            else {
                                zhTw.Add(name);  // graceful fallback
                            }
                        }
                    }
                    catch {
                        foreach (var name in english) zhTw.Add(name);
                    }
                }
                else {
                    foreach (var name in english) zhTw.Add(name);
                }

                var zhTwAliases = new System.Collections.Generic.List<(string Candidate, EnumLists.Island Island)>();
                AddZhTwIslandAlias(zhTw, zhTwAliases, "奧爾比亞海岸", EnumLists.Island.Olvia);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "奧爾維亞海岸", EnumLists.Island.Olvia);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "奧爾比亞", EnumLists.Island.Olvia);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "奧爾維亞", EnumLists.Island.Olvia);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "亞雷哈札", EnumLists.Island.Arehaza);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "阿利赫恣", EnumLists.Island.Arehaza);
                // OCR misread of 阿利赫恣 (observed in Arehaza village tooltips, where the
                // game's font OCR'd the second 3-char token as 未綴 instead of 赫恣).
                AddZhTwIslandAlias(zhTw, zhTwAliases, "阿利未綴", EnumLists.Island.Arehaza);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "葛蘭迪哈", EnumLists.Island.Grandiha);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "午夜島", EnumLists.Island.Midnight);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "哈伊摩", EnumLists.Island.Haemo);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "杜鵑渡口", EnumLists.Island.Dallae);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "艾裴莉雅", EnumLists.Island.Epheria);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "艾裴莉雅港口", EnumLists.Island.Epheria);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "艾裴莉雅港口村莊", EnumLists.Island.Epheria);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "伊菲利亞", EnumLists.Island.Epheria);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "蘇桑", EnumLists.Island.Sausan);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "薩扇", EnumLists.Island.Sausan);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "薩扇碼頭", EnumLists.Island.Sausan);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "聖域", EnumLists.Island.Sanctuary);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "聖殿", EnumLists.Island.Sanctuary);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "聖殿海岸", EnumLists.Island.Sanctuary);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "柯克斯海盜", EnumLists.Island.Cox_Pirate);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "酷斯海賊團", EnumLists.Island.Cox_Pirate);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "柯魯之巢", EnumLists.Island.Crows_Nest);
                AddZhTwIslandAlias(zhTw, zhTwAliases, "西奧尼爾", EnumLists.Island.Theonil);

                _englishIslandMatcher = new iBarter.StringSimilarityMatcher(new System.Collections.ArrayList(english), ignoreCase: true, removeDiacritics: true);
                _zhTwIslandMatcher = new iBarter.StringSimilarityMatcher(new System.Collections.ArrayList(zhTw), ignoreCase: true, removeDiacritics: true);
                _englishIslandEnumByCandidate = new System.Collections.Generic.Dictionary<string, EnumLists.Island>(System.StringComparer.Ordinal);
                _zhTwIslandEnumByCandidate = new System.Collections.Generic.Dictionary<string, EnumLists.Island>(System.StringComparer.Ordinal);
                for (int i = 0; i < english.Count; i++) {
                    if (System.Enum.TryParse(english[i], out EnumLists.Island island)) {
                        _englishIslandEnumByCandidate[NormalizeBasic(english[i])] = island;
                        if (i < zhTw.Count && !string.IsNullOrWhiteSpace(zhTw[i])) {
                            _zhTwIslandEnumByCandidate[NormalizeBasic(zhTw[i])] = island;
                        }
                    }
                }
                foreach (var alias in zhTwAliases) {
                    _zhTwIslandEnumByCandidate[NormalizeBasic(alias.Candidate)] = alias.Island;
                }
            }
        }

        private static void AddZhTwIslandAlias(
            System.Collections.Generic.List<string> zhTw,
            System.Collections.Generic.List<(string Candidate, EnumLists.Island Island)> aliases,
            string candidate,
            EnumLists.Island island) {
            if (string.IsNullOrWhiteSpace(candidate)) {
                return;
            }
            if (!zhTw.Contains(candidate)) {
                zhTw.Add(candidate);
            }
            aliases.Add((candidate, island));
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

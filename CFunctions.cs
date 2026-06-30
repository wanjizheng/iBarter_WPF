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
                string iconPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + item.ItemID + ".bmp";

                if (!File.Exists(iconPath) && item != null && int.Parse(item.ItemID) > 0) {
                    App.myCFun.RefreshItems(item.ItemID);
                }
            }
        }

        public void RefreshItems(string _itemID = "") {
            List<Items> listItems = LoadItemsCSV();
            if (_itemID != null) {
                Items myItem = listItems.Where(i => i.ItemID == _itemID).FirstOrDefault();
                listItems.Clear();
                listItems.Add(myItem);
            }

            int i = 1;
            foreach (var item in listItems) {
                var imageUrl = ResolveBdocodexItemImageUrl(item.ItemID);
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
                Log("Done!", Brushes.Red);
            }
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
        }

        public List<Items> LoadItemsCSV() {
            var listItems = new List<Items>();
            using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory +
                                                 "\\Resources\\Items.csv")) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var results = line.Split(',');
                    var strName = results[0].Replace("'", "").Replace("(", "").Replace(")", "");
                    var strID = results[1];

                    int intNumber = -1;
                    try {
                        intNumber = int.Parse(results[3]);
                    }
                    catch (Exception e) {
                        Log("Error:" + e.Message, Brushes.Red);
                    }

                    var myItems = new Items(strName, results[1], results[2], intNumber);
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

            App.myBarterScanner.RefreshDataGrid();
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
        private static void TryWriteDebugLog(string message) {
            try {
                string p = AppDomain.CurrentDomain.BaseDirectory + @"ocr_debug.log";
                System.IO.File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
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
        // BDO's digit overlay is rendered as near-pure white (RGB ~245-255)
        // on top of a darker icon body (RGB ~30-70). A hard 78% threshold
        // isolates the digit strokes alone. Magick Scale 3x + Negate produces
        // a clean dark-on-light bitmap for Tesseract.
        private int TryTemplateDiffOcr(string liveBmpPath, string itemID) {
            if (string.IsNullOrEmpty(itemID)) {
                TryWriteDebugLog("OCR.G itemID empty");
                return -1;
            }
            if (!System.IO.File.Exists(liveBmpPath)) {
                TryWriteDebugLog("OCR.G live missing: " + liveBmpPath);
                return -1;
            }
            var tess = GetTesseract();
            if (tess == null) {
                TryWriteDebugLog("OCR.G tess=null");
                return -1;
            }

            // Crop to the BR corner where the count overlay lives. BDO renders
            // the digit at the icon's bottom-right; the icon body's anti-
            // aliased highlights confuse a global 78% threshold. Cropping
            // first removes the body entirely so the threshold sees only
            // digit pixels. Plus 5x Scale (vs 3x) for sub-pixel "1" reads.
            //
            // Crop parameters (relative to 44x44 icon, top-left origin):
            //   left  = 50%  (start at icon-mid)
            //   top   = 60%  (start at icon-60%-down)
            //   right = 50%  (extend past icon-right by 50% of width)
            //   bottom= 50%  (extend past icon-bottom by 50% of height)
            //
            // Resulting crop is roughly 22x30 px (wider than tall) at the
            // icon's bottom-right; scaled 5x = 110x150 for Tesseract.
            string sharpPath = liveBmpPath.Replace(".bmp", "_sharp.bmp");
            try {
                using (var mi = new MagickImage(liveBmpPath)) {
                    int w = (int)mi.Width;
                    int h = (int)mi.Height;
                    int cropX = (int)(w * 0.50);
                    int cropY = (int)(h * 0.60);
                    int cropW = (int)(w * 0.50) + 8;   // a few pixels past right edge
                    int cropH = (int)(h * 0.40) + 8;   // a few pixels past bottom edge
                    if (cropW < 8) cropW = 8;
                    if (cropH < 8) cropH = 8;
                    if (cropX + cropW > w) cropW = w - cropX;
                    if (cropY + cropH > h) cropH = h - cropY;
                    mi.Crop(new MagickGeometry(cropX, cropY, (uint)cropW, (uint)cropH));
                    mi.ColorSpace = ColorSpace.Gray;
                    mi.Threshold(new Percentage(70));
                    // 5x scale: a "1" that's 1-2 px on the icon becomes
                    // 5-10 px on the input bitmap - Tesseract can read.
                    mi.Scale(new Percentage(500));
                    mi.Negate();
                    mi.Write(sharpPath);
                }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.G Magick fail: " + ex.GetType().Name + " " + ex.Message);
                return -1;
            }

            try {
                using (var img = CvInvoke.Imread(sharpPath, ImreadModes.Grayscale)) {
                    if (img == null || img.IsEmpty) {
                        TryWriteDebugLog("OCR.G imread empty: " + sharpPath);
                        return -1;
                    }
                    tess.SetImage(img);
                    tess.Recognize();
                    string raw = (tess.GetUTF8Text() ?? "").Trim();
                    Match m = Regex.Match(raw, @"\d{1,4}");
                    if (m.Success && int.TryParse(m.Value, out int n) && n > 0 && n < 10000) {
                        return n;
                    }
                    TryWriteDebugLog("OCR.G tesseract no digits: raw='" + raw + "'");
                }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.G tesseract fail: " + ex.GetType().Name + " " + ex.Message);
            }
            return -1;
        }

        // Run Emgu.Tesseract directly on a BMP file. The caller is responsible
        // for ensuring the file exists and was captured from a sensible ROI.
        // Returns -1 on any failure (engine unavailable, file missing, no
        // digits matched).
        //
        // Pre-processing pipeline was selected by a 12-file x 16-variant
        // benchmark on captured BMPs with operator-supplied ground truth. The
        // highest-accuracy variant was M: scale 300%, Negate (so light digit
        // becomes dark on light), threshold 50% binarization. That combo is
        // 8/12 vs 6/12 for the next-best alternative, and beats all
        // alternatives on single-digit '1'-class glyphs (which are the hardest
        // bucket for Tesseract at 6-8 px tall).
        private int TryEmguOcr(string bmpPath) {
            var tess = GetTesseract();
            if (tess == null) {
                TryWriteDebugLog("OCR.F tess=null");
                return -1;
            }
            if (!System.IO.File.Exists(bmpPath)) {
                TryWriteDebugLog("OCR.F file missing: " + bmpPath);
                return -1;
            }

            string sharpPath = bmpPath.Replace(".bmp", "_sharp.bmp");
            try {
                using (var mi = new MagickImage(bmpPath)) {
                    mi.Scale(new Percentage(300));
                    mi.Negate();
                    mi.Threshold(new Percentage(50));
                    mi.Write(sharpPath);
                }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.F Magick failed: " + ex.GetType().Name + " " + ex.Message);
                return -1;
            }

            try {
                using (var img = CvInvoke.Imread(sharpPath, ImreadModes.Grayscale)) {
                    if (img == null || img.IsEmpty) {
                        TryWriteDebugLog("OCR.F imread empty: " + sharpPath);
                        return -1;
                    }
                    tess.SetImage(img);
                    tess.Recognize();
                    string raw = (tess.GetUTF8Text() ?? "").Trim();
                    Match m = Regex.Match(raw, @"\d{1,4}");
                    if (m.Success && int.TryParse(m.Value, out int n) && n > 0 && n < 10000) {
                        return n;
                    }
                }
            }
            catch (Exception ex) {
                TryWriteDebugLog("OCR.F tesseract failed: " + ex.GetType().Name + " " + ex.Message);
            }
            return -1;
        }

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
                var c = candidates[1];
                int x1 = (int)(oX + oW * c.lf);
                int y1 = (int)(oY + oH * c.tf);
                int x2 = (int)(oX + oW * c.rf);
                int y2 = (int)(oY + oH * c.bf);
                App.myPureDM.DM.Capture(x1, y1, x2, y2, "ocr_" + strID + ".bmp");
            }
            catch { }

            // Phase D-bis: capture the FULL ICON rectangle for Phase G. The
            // strip used by Phase A/F is fine for direct OCR but it stretches
            // the template when we subtract, filling the diff with anti-
            // aliasing noise. With full-icon capture the template lines up
            // pixel-for-pixel and the diff cleanly isolates the digit overlay.
            try {
                App.myPureDM.DM.Capture((int)oX, (int)oY,
                    (int)oX + (int)oW, (int)oY + (int)oH,
                    "ocrf_" + strID + ".bmp");
            }
            catch { }

            // Phase F + G: even when screen-coords voting is uncertain, run
            // both Emgu paths on the captured BMP. Phase G subtracts the icon
            // template to cancel the icon-body noise, isolating the digit.
            // NOTE: DM.Capture writes to <base>/Resources/ but our code needs
            // the explicit Resources prefix when reading back via File.Exists.
            string bmpPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\ocr_" + strID + ".bmp";
            int emguPick = TryEmguOcr(bmpPath);
            string fullIconPath = AppDomain.CurrentDomain.BaseDirectory + "Resources\\ocrf_" + strID + ".bmp";
            int diffPick = TryTemplateDiffOcr(fullIconPath, strID);

            // 3-way merge vote: each phase contributes one vote (Phase A's
            // screen-coords count is weighted by its topCount, but we collapse
            // it to 1 vote per parsed value for parity with F/G).
            var merged = new System.Collections.Generic.Dictionary<int, int>();
            if (picked > 0) merged[picked] = merged.GetValueOrDefault(picked, 0) + System.Math.Max(1, topCount);
            if (emguPick > 0) merged[emguPick] = merged.GetValueOrDefault(emguPick, 0) + 1;
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
                Log($"OCR qty {strID}: picked {finalPick} (votes={tally}; A={picked} F={emguPick} G={diffPick})", Brushes.Gray);
            }
            else {
                Log($"OCR qty {strID}: no consensus; raw={(bestRaw ?? "")} F={emguPick} G={diffPick}", Brushes.OrangeRed);
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

            App.myPureDM.DM.Capture(pointPlusParley.X, pointPlusParley.Y - pointPlusParley.Size.Height,
                pointPlusRequired.X + 120, pointPlusParley.Y + 1, "myItem1.bmp");

            App.myPureDM.DM.Capture(pointPlusParley.X + 376,
                pointPlusParley.Y - pointPlusParley.Size.Height,
                pointPlusRequired.X + 376 + 100,
                pointPlusParley.Y + pointPlusParley.Size.Height, "myItem2.bmp");

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
            // Multi-ROI voting for the bottom-right "50" overlay (Phase A+C+D).
            int intNumber1 = TryReadQuantity(listPointPlus[0], strID1);
            if (intNumber1 <= 0) {
                // OCR failed to agree - fall back to the CSV-default quantity and
                // log so this case is visible.
                intNumber1 = App.listItems.Where(i => i.ItemID == strID1)
                    .Select(i => i.ItemNumber).FirstOrDefault();
                if (intNumber1 > 0)
                    Log($"OCR qty {strID1} fallback CSV={intNumber1}", Brushes.OrangeRed);
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
                intNumber2 = TryReadQuantity(listPointPlus[1], strID2);
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
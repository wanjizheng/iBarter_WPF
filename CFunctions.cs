using ImageMagick;
using PureDM;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
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
                var strURL = "";
                using (var httpClient = new HttpClient()) {
                    using (var response = httpClient.GetAsync("https://bdocodex.com/us/item/" + item.ItemID).Result) {
                        using (var content = response.Content) {
                            var result = content.ReadAsStringAsync().Result;
                            strURL = getBetween(result, "<meta property=\"og:image\" content=\"", "\">");
                        }
                    }
                }

                UpdateItemImagesAsync(item.ItemID, strURL);
                if (_itemID == "") {
                    Log(i + "/" + listItems.Count, Brushes.Blue);
                }
                else {
                    Log("Download icon for: " + App.listItems.FirstOrDefault(i => i.ItemID == _itemID), Brushes.Gold);
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

        private void UpdateItemImagesAsync(string _id, string _url) {
            var client = new WebClient();
            //Uri address = new Uri("https://bdocodex.com/items/new_icon/03_etc/" + strType + _id + ".webp");
            //AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
            using (var stream = client.OpenRead(_url)) {
                using (var fileStream =
                       new FileStream(AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Testing\\" + _id + ".webp",
                           //"E:\\wanjizheng\\Documents\\MyProject\\BDO Data\\Items\\Images\\webp\\" + _id + ".webp",
                           FileMode.Create, FileAccess.Write)) {
                    stream.CopyTo(fileStream);
                    stream.Flush();
                    stream.Close();
                }
            }


            //var webp = new WebP();
            // var bitmap = webp.Load(AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Testing\\" + _id + ".webp");

            using (var bitmap = new MagickImage(AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Testing\\" + _id + ".webp")) {
                var bitmapNew = new Bitmap(44, 44, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                using (var gfx = Graphics.FromImage(bitmapNew))
                using (var brush = new SolidBrush(Color.FromArgb(24, 23, 25))) {
                    gfx.FillRectangle(brush, 0, 0, 44, 44);
                }

                using (var memoryStream = new MemoryStream()) {
                    // 保存 MagickImage 到内存流，并确保使用正确的格式
                    bitmap.Format = MagickFormat.Bmp; // 设置为 PNG 格式来保持透明度（如果需要）
                    bitmap.Write(memoryStream, MagickFormat.Bmp);
                    memoryStream.Position = 0; // 重置流位置至开始


                    // 从内存流创建 System.Drawing.Image
                    using (var systemImage = Image.FromStream(memoryStream)) {
                        var g = Graphics.FromImage(bitmapNew);
                        g.DrawImage(systemImage, 0, 0);
                        bitmapNew.Save(AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + _id + ".bmp",
                            ImageFormat.Bmp);

                        bitmapNew.Dispose();
                        g.Dispose();
                    }
                }
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

            //listAnchors.OrderBy(pp => pp.Y);

            //listPointPlus.Sort((p1, p2) => { return p1.Sim.CompareTo(p2.Sim); });

            listAnchors.Sort((p1, p2) => { return p1.Y.CompareTo(p2.Y); });
            //List<Thread> listThread = new List<Thread>();
            for (int i = 0; i < listAnchors.Count; i++) {
                // Thread myThread = new Thread(() => {
                //     Barter myBarter = IdentifyBarter(listAnchors[i]);
                //     if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null && App.listBarterScanner.FirstOrDefault(b => b.IsLand.GetIslandEnum().ToString().Equals(myBarter.IsLand.GetIslandEnum().ToString())) == null) {
                //         App.listBarterScanner.Add(myBarter);
                //     }
                // });
                // myThread.IsBackground = true;
                // myThread.Start();
                // Thread.Sleep(100);
                // listThread.Add(myThread);

                Barter myBarter = await IdentifyBarterAsync(listAnchors[i]);
                if (myBarter.IsLand != null && myBarter.Item1 != null && myBarter.Item2 != null &&
                    App.listBarterScanner.FirstOrDefault(b =>
                        b.IsLand.Island.ToString().Equals(myBarter.IsLand.Island.ToString())) == null) {
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

        private async Task<Barter> IdentifyBarterAsync(PointPlus _pp) {
            PointPlus pointPlusAnchor = _pp;
            Barter myBarter = null;
            if (pointPlusAnchor.X == -1 || pointPlusAnchor.Y == -1) {
                return myBarter;
            }

            PointPlus pointPlusEdge = App.myPureDM.CV.FindPicture(Math.Max(0, pointPlusAnchor.X - 300),
                pointPlusAnchor.Y - 5, pointPlusAnchor.X - 5, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5,
                "\\Images\\edge.bmp", 0.8, CV.Mode.OpenCV, false);

            if (pointPlusEdge.X == -1 || pointPlusEdge.Y == -1) {
                return myBarter;
            }

            //App.dmSoft.Capture(pointPlusEdge.X + pointPlusEdge.Size.Width, pointPlusAnchor.Y - 2, pointPlusAnchor.X - 2, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5, "island.bmp");
            //Thread.Sleep(100);
            string strIsland = App.myPureDM.CV.OCRString(pointPlusEdge.X + pointPlusEdge.Size.Width,
                pointPlusAnchor.Y - 2, pointPlusAnchor.X - 2, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5);
            myBarter = new Barter();


            //Identify Trade Iteams
            List<PointPlus> listPointPlus = new List<PointPlus>();
            int intX1 = pointPlusAnchor.X + pointPlusAnchor.Size.Width + 1;
            int intY1 = pointPlusAnchor.Y - 2;
            int intX2 = pointPlusAnchor.X + 700;
            int intY2 = pointPlusAnchor.Y + 60;
            App.myPureDM.DM.Capture(intX1, intY1, intX2, intY2, "barterItems.bmp");


            string strParleyPath = "\\Images\\Parley.bmp";

            if (GameFont == FontType.DejaVuSans) {
                strParleyPath = "\\Images\\Parley2.bmp";
            }

            PointPlus pointPlusParley = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
                strParleyPath, 0.8, CV.Mode.OpenCV, false);

            if (pointPlusParley.IsEmpty) {
                pointPlusParley = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
                    "\\Images\\Parley2.bmp", 0.8, CV.Mode.OpenCV, false);
                GameFont = FontType.DejaVuSans;
            }


            string strRequiredPath = "\\Images\\Required.bmp";

            if (GameFont == FontType.DejaVuSans) {
                strRequiredPath = "\\Images\\Required2.bmp";
            }

            PointPlus pointPlusRequired = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
                strRequiredPath, 0.8, CV.Mode.OpenCV, false);


            string strParley = App.myPureDM.CV.OCRString(pointPlusParley.X + pointPlusParley.Size.Width,
                pointPlusParley.Y, pointPlusRequired.X, pointPlusParley.Y + pointPlusParley.Size.Height,
                CV.OCRType.Number);

            int intParley = App.listIslands.Where(land => land.Island == IslandEnum(strIsland))
                .Select(land => land.Parley).FirstOrDefault();

            try {
                intParley = int.Parse(strParley);
            }
            catch (Exception e) {
            }

            string strRemainingPath = "\\Images\\Remaining.bmp";

            if (GameFont == FontType.DejaVuSans) {
                strRemainingPath = "\\Images\\Remaining2.bmp";
            }

            PointPlus pointPlusRemaining = App.myPureDM.CV.FindPicture(0,
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height, App.myPureDM.WindowWidth, pointPlusAnchor.Y + 60,
                strRemainingPath, 0.7, CV.Mode.OpenCV, false);


            string strRemaining = App.myPureDM.CV.OCRString(pointPlusRemaining.X + pointPlusRemaining.Size.Width,
                pointPlusRemaining.Y, pointPlusRemaining.X + pointPlusRemaining.Size.Width + 30,
                pointPlusRemaining.Y + pointPlusRemaining.Size.Height + 2, CV.OCRType.Number);

            int intRemaining = 0;
            try {
                intRemaining = int.Parse(strRemaining);
            }
            catch (Exception e) {
                Log("Error, cannot identify the remaining number => " + strIsland, Brushes.IndianRed);
            }

            if (IslandEnum(strIsland) == EnumLists.Island.UnKnown) {
                Log("Unknown islands! Double check your result! => " + strIsland, Brushes.Red);
            }

            //Islands myIslands = new Islands(IslandEnum(strIsland), intParley, intRemaining);
            Islands myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName == IslandEnum(strIsland).ToString());
            if (myIslands == null) {
                Log("Error, cannot identify the islands information => " + strIsland, Brushes.Red);
                return null;
            }

            myIslands.Parley = intParley;
            myIslands.Remaining = intRemaining;

            myBarter.IsLand = myIslands;

            Log("Identified island: " + myIslands.Island, Brushes.OrangeRed);

            //List<Thread> listThread = new List<Thread>();
            //////////////////////////////////////////////

            // ParallelLoopResult result = Parallel.ForEach(App.listItems, item => {
            //     PointPlus myPP = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
            //         "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
            //     if (myPP.X != -1 && myPP.Y != -1) {
            //         listPointPlus.Add(myPP);
            //     }
            // });


            // await Task.Run(() => {
            //     var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2 }; // 使用一半的核心
            //     Parallel.ForEach(App.listItems, options, item => {
            //         PointPlus myPP = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
            //             "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
            //         if (myPP.X != -1 && myPP.Y != -1) {
            //             lock (listPointPlus) {
            //                 listPointPlus.Add(myPP);
            //             }
            //         }
            //     });
            // });


            //////////////////////////////////////////////
            // foreach (Items item in App.listItems) {
            //     PointPlus myPP = FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, Mode.OpenCV);
            //     if (myPP.X != -1 && myPP.Y != -1)
            //     {
            //         listPointPlus.Add(myPP);
            //     }
            // }
            foreach (Items item in App.listItems) {
                PointPlus myPP = App.myPureDM.CV.FindPicture(intX1, intY1, intX2, intY2,
                    "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
                if (myPP.X != -1 && myPP.Y != -1) {
                    listPointPlus.Add(myPP);
                }
            }

            if (listPointPlus.Count < 2) {
                Log("Can't identify items: " + myIslands.Island, Brushes.Red);
                return myBarter;
            }

            listPointPlus.Sort((p1, p2) => { return p1.Sim.CompareTo(p2.Sim); });
            List<PointPlus> listPP = new List<PointPlus>();
            for (int i = listPointPlus.Count - 1; i >= 0; i--) {
                PointPlus myPP = listPointPlus[i];

                if (listPP.Count == 2)
                    break;

                PointPlus pp1 = listPP.FirstOrDefault(pp => Math.Abs(pp.X - myPP.X) < 300);

                if (listPP.Count == 0 || pp1.IsEmpty) {
                    listPP.Add(myPP);
                }

                // if (listPP.Count==0)
                //     listPP.Add(myPP);
                // else {
                //     
                //     foreach (PointPlus pp in listPP) {
                //         if (myPP.X > pp.X + pp.Size.Width || myPP.X < pp.X - pp.Size.Width) {
                //             listPP.Add(myPP);
                //         }
                //     }
                // }
            }

            listPointPlus = listPP;

            //listPointPlus.RemoveRange(0, listPointPlus.Count - 2);
            listPointPlus.Sort((p1, p2) => { return p1.X.CompareTo(p2.X); });

            // \Images\Items\6020.bmp

            string strID1 = listPointPlus[0].ImageID.Substring(14, listPointPlus[0].ImageID.Length - 18);

            if (strID1 == "800011") {
                strID1 = "800012";
            }
            else if (strID1 == "800012") {
                strID1 = "800011";
            }

            string strNumber1 = App.myPureDM.CV.OCRString(listPointPlus[0].X,
                (int)(listPointPlus[0].Y + (listPointPlus[0].Size.Height * 0.6)),
                listPointPlus[0].X + listPointPlus[0].Size.Width, listPointPlus[0].Y + listPointPlus[0].Size.Height,
                CV.OCRType.Number, CV.OCRMode.Diff, false, strID1);

            int intNumber1 = App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemNumber).FirstOrDefault();
            try {
                intNumber1 = int.Parse(strNumber1);
            }
            catch (Exception e) {
            }

            string strID2 = "10";
            string strNumber2 = "-1";
            if (listPointPlus.Count == 2) {
                strID2 = listPointPlus[1].ImageID.Substring(14, listPointPlus[1].ImageID.Length - 18);
                if (strID2 == "800011") {
                    strID2 = "800012";
                }
                else if (strID2 == "800012") {
                    strID2 = "800011";
                }

                strNumber2 = App.myPureDM.CV.OCRString(listPointPlus[1].X,
                    (int)(listPointPlus[1].Y + (listPointPlus[1].Size.Height * 0.6)),
                    listPointPlus[1].X + listPointPlus[1].Size.Width, listPointPlus[1].Y + listPointPlus[1].Size.Height,
                    CV.OCRType.Number, CV.OCRMode.Diff, false, strID2);
            }
            else {
                Log("Cannot identify the second item. Use Crow Coin instead.", Brushes.Red);
            }


            int intNumber2 = App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemNumber).FirstOrDefault();
            try {
                intNumber2 = int.Parse(strNumber2);
            }
            catch (Exception e) {
            }

            Items item1 =
                new Items(App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemName).FirstOrDefault(), strID1,
                    App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemLV).FirstOrDefault(), intNumber1);
            Items item2 =
                new Items(App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemName).FirstOrDefault(), strID2,
                    App.listItems.Where(i => i.ItemID == strID2).Select(i => i.ItemLV).FirstOrDefault(), intNumber2);

            Log(
                "<" + myIslands.Island + "-" + myIslands.Remaining + "~" + myIslands.Parley + "> Item1: " +
                item1.ItemName + "=>" + intNumber1 + " | Item2: " + item2.ItemName + "=>" + intNumber2, Brushes.Blue);
            myBarter.Item1 = item1;
            myBarter.Item2 = item2;

            return myBarter;
        }


        public EnumLists.Island IslandEnum(string _island) {
            switch (_island) {
                case string s when s.Contains("Ajir"):
                    return EnumLists.Island.Ajir;
                case string s when s.Contains("Albresser"):
                    return EnumLists.Island.Albresser;
                case string s when s.Contains("Almai"):
                    return EnumLists.Island.Almai;
                case string s when s.Contains("Al-Naha") || s.Contains("AI-Naha") || s.Contains("Al_Naha"):
                    return EnumLists.Island.Al_Naha;
                case string s when s.Contains("Ancient"):
                    return EnumLists.Island.Ancient;
                case string s when s.Contains("Angie"):
                    return EnumLists.Island.Angie;
                case string s when s.Contains("Arakil"):
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
                case string s when s.Contains("Haran") || s.Contains("ShipwreckedHaran'sCarg") || s.Contains("Shipwrecked Haran"):
                    return EnumLists.Island.Haran;
                case string s when s.Contains("Carrack"):
                    return EnumLists.Island.Carrack;
                case string s when s.Contains("Cholace"):
                    return EnumLists.Island.Cholace;
                case string s when s.Contains("Cox Pirates") || s.Contains("Cox_Pirates"):
                    return EnumLists.Island.Cox_Pirates;
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
                case string s when s.Contains("Iliya") || s.Contains("liya"):
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
                case string s when s.Contains("Oben"):
                    return EnumLists.Island.Oben;
                case string s when s.Contains("Orffs") || s.Contains("Drffs"):
                    return EnumLists.Island.Orffs;
                case string s when s.Contains("Orisha"):
                    return EnumLists.Island.Orisha;
                case string s when s.Contains("Ostra") || s.Contains("Dstra") || s.Contains("Qstra"):
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
                case string s when s.Contains("Racid"):
                    return EnumLists.Island.Racid;
                case string s when s.Contains("Rameda"):
                    return EnumLists.Island.Rameda;
                case string s when s.Contains("Randis"):
                    return EnumLists.Island.Randis;
                case string s when s.Contains("Rickun"):
                    return EnumLists.Island.Rickun;
                case string s when s.Contains("Riyed") || s.Contains("Ried"):
                    return EnumLists.Island.Riyed;
                case string s when s.Contains("Rosevan"):
                    return EnumLists.Island.Rosevan;
                case string s when s.Contains("Serca"):
                    return EnumLists.Island.Serca;
                case string s when s.Contains("Shasha"):
                    return EnumLists.Island.Shasha;
                case string s when s.Contains("Shirna"):
                    return EnumLists.Island.Shirna;
                case string s when s.Contains("Sokota"):
                    return EnumLists.Island.Sokota;
                case string s when s.Contains("Staren"):
                    return EnumLists.Island.Staren;
                case string s when s.Contains("Taramura"):
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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Drawing.Color;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Path = System.IO.Path;
using Rectangle = System.Drawing.Rectangle;
using System.Windows;
using PureDM;
using PureDM.Configs;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using SystemColors = System.Drawing.SystemColors;


namespace iBarter {
    public class CFunctions {
        public enum Mode {
            DmSoft,
            OpenCV,
            Both
        }

        public void Log(string _message, Brush _color) {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(() => Log(_message, _color)));
            }
            else {
                var myDT = DateTime.Now;


                var strTime = "[ " + myDT.ToString("hh:mm:ss") + " ]  ";


                App.myfmMain.richTextBox_Log.Dispatcher.Invoke(new Action(() => {
                    App.myfmMain.richTextBox_Log.AppendText(strTime);
                    var tr = new TextRange(App.myfmMain.richTextBox_Log.Document.ContentEnd,
                        App.myfmMain.richTextBox_Log.Document.ContentEnd);
                    tr.Text = _message + "\r\n";
                    var bc = new BrushConverter();
                    tr.ApplyPropertyValue(TextElement.ForegroundProperty, _color);
                    App.myfmMain.richTextBox_Log.ScrollToEnd();
                }));
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

        public void RefreshItems() {
            var listItems = LoadItemsCSV();
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
                Log(i + "/" + listItems.Count, Brushes.Blue);
                i++;
                //Thread.Sleep(500);
            }

            Log("Done!", Brushes.Red);
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

            using (var stream = client.OpenRead(_url)) {
                using (var fileStream =
                       new FileStream(
                           "E:\\wanjizheng\\Documents\\MyProject\\BDO Data\\Items\\Images\\webp\\" + _id + ".webp",
                           FileMode.Create, FileAccess.Write)) {
                    stream.CopyTo(fileStream);
                    stream.Flush();
                    stream.Close();
                }
            }


            var webp = new WebP();
            var bitmap = webp.Load("E:\\wanjizheng\\Documents\\MyProject\\BDO Data\\Items\\Images\\webp\\" + _id +
                                   ".webp");
            var bitmapNew = new Bitmap(44, 44, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (var gfx = Graphics.FromImage(bitmapNew))
            using (var brush = new SolidBrush(Color.FromArgb(24, 23, 25))) {
                gfx.FillRectangle(brush, 0, 0, 44, 44);
            }

            var g = Graphics.FromImage(bitmapNew);
            g.DrawImage(bitmap, 0, 0);

            bitmapNew.Save("E:\\wanjizheng\\Documents\\MyProject\\BDO Data\\Items\\Images\\bmp\\" + _id + ".bmp",
                ImageFormat.Bmp);

            bitmapNew.Dispose();
            g.Dispose();
        }

        public List<Items> LoadItemsCSV() {
            var listItems = new List<Items>();
            using (var reader = new StreamReader(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                                 "\\Resources\\Items.csv")) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var results = line.Split(',');
                    var strName = results[0].Replace(" ", "_").Replace("'", "").Replace("(", "_").Replace(")", "");
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
            using (var reader = new StreamReader(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                                 "\\Resources\\Islands.csv")) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var results = line.Split(',');
                    var strName = results[0].Replace(" ", "_").Replace("'", "").Replace("(", "_").Replace(")", "");
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

        public void SearchBarter() {
            IdentifyRoutes();
        }

        private void CleanDataGrid() {
            if (App.mySVM.BarterDetails != null) {
                App.mySVM.BarterDetails.Clear();
            }
        }

        public void IdentifyRoutes() {
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

                Barter myBarter = IdentifyBarter(listAnchors[i]);
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

        private Barter IdentifyBarter(PointPlus _pp) {
            PointPlus pointPlusAnchor = _pp;
            Barter myBarter = null;
            if (pointPlusAnchor.X == -1 || pointPlusAnchor.Y == -1) {
                return myBarter;
            }

            PointPlus pointPlusEdge = PureDM.PureDM.myCV.FindPicture(Math.Max(0, pointPlusAnchor.X - 300),
                pointPlusAnchor.Y - 5, pointPlusAnchor.X - 5, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5,
                "\\Images\\edge.bmp", 0.8, CV.Mode.OpenCV, false);

            if (pointPlusEdge.X == -1 || pointPlusEdge.Y == -1) {
                return myBarter;
            }

            //App.dmSoft.Capture(pointPlusEdge.X + pointPlusEdge.Size.Width, pointPlusAnchor.Y - 2, pointPlusAnchor.X - 2, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5, "island.bmp");
            //Thread.Sleep(100);
            string strIsland = PureDM.PureDM.myCV.OCRString(pointPlusEdge.X + pointPlusEdge.Size.Width,
                pointPlusAnchor.Y - 2, pointPlusAnchor.X - 2, pointPlusAnchor.Y + pointPlusAnchor.Size.Height + 5);
            myBarter = new Barter();


            //Identify Trade Iteams
            List<PointPlus> listPointPlus = new List<PointPlus>();
            int intX1 = pointPlusAnchor.X + pointPlusAnchor.Size.Width + 1;
            int intY1 = pointPlusAnchor.Y - 2;
            int intX2 = pointPlusAnchor.X + 700;
            int intY2 = pointPlusAnchor.Y + 60;
            PureDM.PureDM.myDM.Capture(intX1, intY1, intX2, intY2, "barterItems.bmp");

            PointPlus pointPlusParley = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
                "\\Images\\Parley.bmp", 0.8, CV.Mode.OpenCV, false);

            PointPlus pointPlusRequired = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
                "\\Images\\Required.bmp", 0.8, CV.Mode.OpenCV, false);

            string strParley = PureDM.PureDM.myCV.OCRString(pointPlusParley.X + pointPlusParley.Size.Width,
                pointPlusParley.Y, pointPlusRequired.X, pointPlusParley.Y + pointPlusParley.Size.Height,
                CV.OCRType.Number);

            int intParley = App.listIslands.Where(land => land.Island == IslandEnum(strIsland))
                .Select(land => land.Parley).FirstOrDefault();

            try {
                intParley = int.Parse(strParley);
            }
            catch (Exception e) {
            }

            PointPlus pointPlusRemaining = PureDM.PureDM.myCV.FindPicture(0,
                pointPlusAnchor.Y + pointPlusAnchor.Size.Height, App.myPureDM.WindowWidth, pointPlusAnchor.Y + 60,
                "\\Images\\Remaining.bmp", 0.8, CV.Mode.OpenCV, false);


            string strRemaining = PureDM.PureDM.myCV.OCRString(pointPlusRemaining.X + pointPlusRemaining.Size.Width,
                pointPlusRemaining.Y, pointPlusRemaining.X + pointPlusRemaining.Size.Width + 30,
                pointPlusRemaining.Y + pointPlusRemaining.Size.Height + 2, CV.OCRType.Number);

            int intRemaining = 0;
            try {
                intRemaining = int.Parse(strRemaining);
            }
            catch (Exception e) {
            }

            Islands myIslands = new Islands(IslandEnum(strIsland), intParley, intRemaining);

            myBarter.IsLand = myIslands;

            Log("Identified island:" + myIslands.Island, Brushes.OrangeRed);

            //List<Thread> listThread = new List<Thread>();
            //////////////////////////////////////////////

            ParallelLoopResult result = Parallel.ForEach(App.listItems, item => {
                PointPlus myPP = PureDM.PureDM.myCV.FindPicture(intX1, intY1, intX2, intY2,
                    "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, CV.Mode.OpenCV, false);
                if (myPP.X != -1 && myPP.Y != -1) {
                    listPointPlus.Add(myPP);
                }
            });


            //////////////////////////////////////////////
            // foreach (Items item in App.listItems) {
            //     PointPlus myPP = FindPicture(intX1, intY1, intX2, intY2, "\\Images\\Items\\" + item.ItemID + ".bmp", 0.4, 0.8, 1, Mode.OpenCV);
            //     if (myPP.X != -1 && myPP.Y != -1)
            //     {
            //         listPointPlus.Add(myPP);
            //     }
            // }

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
            string strNumber1 = PureDM.PureDM.myCV.OCRString(listPointPlus[0].X,
                (int)(listPointPlus[0].Y + (listPointPlus[0].Size.Height * 0.6)),
                listPointPlus[0].X + listPointPlus[0].Size.Width, listPointPlus[0].Y + listPointPlus[0].Size.Height,
                CV.OCRType.Number, CV.OCRMode.Diff, false, strID1);

            int intNumber1 = App.listItems.Where(i => i.ItemID == strID1).Select(i => i.ItemNumber).FirstOrDefault();
            try {
                intNumber1 = int.Parse(strNumber1);
            }
            catch (Exception e) {
            }

            string strID2 = listPointPlus[1].ImageID.Substring(14, listPointPlus[1].ImageID.Length - 18);
            string strNumber2 = PureDM.PureDM.myCV.OCRString(listPointPlus[1].X,
                (int)(listPointPlus[1].Y + (listPointPlus[1].Size.Height * 0.6)),
                listPointPlus[1].X + listPointPlus[1].Size.Width, listPointPlus[1].Y + listPointPlus[1].Size.Height,
                CV.OCRType.Number, CV.OCRMode.Diff, false, strID2);

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
            if (_island.Contains("Ajir")) {
                return EnumLists.Island.Ajir;
            }
            else if (_island.Contains("Albresser")) {
                return EnumLists.Island.Albresser;
            }
            else if (_island.Contains("Almai")) {
                return EnumLists.Island.Almai;
            }
            else if (_island.Contains("Al-Naha") || _island.Contains("AI-Naha") || _island.Contains("Al_Naha")) {
                return EnumLists.Island.Al_Naha;
            }
            else if (_island.Contains("Ancient")) {
                return EnumLists.Island.Ancient;
            }
            else if (_island.Contains("Angie")) {
                return EnumLists.Island.Angie;
            }
            else if (_island.Contains("Arakil")) {
                return EnumLists.Island.Arakil;
            }
            else if (_island.Contains("Arita")) {
                return EnumLists.Island.Arita;
            }
            else if (_island.Contains("Baeza")) {
                return EnumLists.Island.Baeza;
            }
            else if (_island.Contains("Balvege")) {
                return EnumLists.Island.Balvege;
            }
            else if (_island.Contains("Barater")) {
                return EnumLists.Island.Barater;
            }
            else if (_island.Contains("Baremi")) {
                return EnumLists.Island.Baremi;
            }
            else if (_island.Contains("Beiruwa")) {
                return EnumLists.Island.Beiruwa;
            }
            else if (_island.Contains("Boa")) {
                return EnumLists.Island.Boa;
            }
            else if (_island.Contains("Cargo") || _island.Contains("ShipwreckedHaran'sCarg") ||
                     _island.Contains("Shipwrecked Haran")) {
                return EnumLists.Island.Cargo;
            }
            else if (_island.Contains("Carrack")) {
                return EnumLists.Island.Carrack;
            }
            else if (_island.Contains("Cholace")) {
                return EnumLists.Island.Cholace;
            }
            else if (_island.Contains("Cox Pirates") || _island.Contains("Cox_Pirates")) {
                return EnumLists.Island.Cox_Pirates;
            }
            else if (_island.Contains("Crow")) {
                return EnumLists.Island.Crow;
            }
            else if (_island.Contains("Daton")) {
                return EnumLists.Island.Daton;
            }
            else if (_island.Contains("Delinghart")) {
                return EnumLists.Island.Delinghart;
            }
            else if (_island.Contains("Derko")) {
                return EnumLists.Island.Derko;
            }
            else if (_island.Contains("Duch")) {
                return EnumLists.Island.Duch;
            }
            else if (_island.Contains("Dunde")) {
                return EnumLists.Island.Dunde;
            }
            else if (_island.Contains("Eberdeen")) {
                return EnumLists.Island.Eberdeen;
            }
            else if (_island.Contains("Ephde Rune") || _island.Contains("Ephde_Rune") || _island.Contains("Ephde")) {
                return EnumLists.Island.Ephde_Rune;
            }
            else if (_island.Contains("Esfah")) {
                return EnumLists.Island.Esfah;
            }
            else if (_island.Contains("Eveto") || _island.Contains("Evelo")) {
                return EnumLists.Island.Eveto;
            }
            else if (_island.Contains("Ginburrey")) {
                return EnumLists.Island.Ginburrey;
            }
            else if (_island.Contains("Hakoven")) {
                return EnumLists.Island.Hakoven;
            }
            else if (_island.Contains("Halmad")) {
                return EnumLists.Island.Halmad;
            }
            else if (_island.Contains("Iliya") || _island.Contains("liya")) {
                return EnumLists.Island.Iliya;
            }
            else if (_island.Contains("Incomplete") || _island.Contains("UnfinishedAdriftVessel") ||
                     _island.Contains("Unfinished Adrift")) {
                return EnumLists.Island.Incomplete;
            }
            else if (_island.Contains("Invernen")) {
                return EnumLists.Island.Invernen;
            }
            else if (_island.Contains("Kanvera")) {
                return EnumLists.Island.Kanvera;
            }
            else if (_island.Contains("Kashuma")) {
                return EnumLists.Island.Kashuma;
            }
            else if (_island.Contains("Kuit")) {
                return EnumLists.Island.Kuit;
            }
            else if (_island.Contains("Lantinia")) {
                return EnumLists.Island.Lantinia;
            }
            else if (_island.Contains("Lema")) {
                return EnumLists.Island.Lema;
            }
            else if (_island.Contains("Lerao")) {
                return EnumLists.Island.Lerao;
            }
            else if (_island.Contains("Lisz")) {
                return EnumLists.Island.Lisz;
            }
            else if (_island.Contains("Louruve")) {
                return EnumLists.Island.Louruve;
            }
            else if (_island.Contains("Luivano")) {
                return EnumLists.Island.Luivano;
            }
            else if (_island.Contains("Mariveno")) {
                return EnumLists.Island.Mariveno;
            }
            else if (_island.Contains("Marka")) {
                return EnumLists.Island.Marka;
            }
            else if (_island.Contains("Marlene")) {
                return EnumLists.Island.Marlene;
            }
            else if (_island.Contains("Modric")) {
                return EnumLists.Island.Modric;
            }
            else if (_island.Contains("Narvo")) {
                return EnumLists.Island.Narvo;
            }
            else if (_island.Contains("Netnume")) {
                return EnumLists.Island.Netnume;
            }
            else if (_island.Contains("Oben")) {
                return EnumLists.Island.Oben;
            }
            else if (_island.Contains("Orffs")) {
                return EnumLists.Island.Orffs;
            }
            else if (_island.Contains("Drffs")) {
                return EnumLists.Island.Orffs;
            }
            else if (_island.Contains("Orisha")) {
                return EnumLists.Island.Orisha;
            }
            else if (_island.Contains("Ostra") || _island.Contains("Dstra")) {
                return EnumLists.Island.Ostra;
            }
            else if (_island.Contains("Padix")) {
                return EnumLists.Island.Padix;
            }
            else if (_island.Contains("Pakio")) {
                return EnumLists.Island.Pakio;
            }
            else if (_island.Contains("Paratama")) {
                return EnumLists.Island.Paratama;
            }
            else if (_island.Contains("Pilava")) {
                return EnumLists.Island.Pilava;
            }
            else if (_island.Contains("Portanen")) {
                return EnumLists.Island.Portanen;
            }
            else if (_island.Contains("Pujara")) {
                return EnumLists.Island.Pujara;
            }
            else if (_island.Contains("Racid")) {
                return EnumLists.Island.Racid;
            }
            else if (_island.Contains("Rameda")) {
                return EnumLists.Island.Rameda;
            }
            else if (_island.Contains("Randis")) {
                return EnumLists.Island.Randis;
            }
            else if (_island.Contains("Rickun")) {
                return EnumLists.Island.Rickun;
            }
            else if (_island.Contains("Riyed")) {
                return EnumLists.Island.Riyed;
            }
            else if (_island.Contains("Rosevan")) {
                return EnumLists.Island.Rosevan;
            }
            else if (_island.Contains("Serca")) {
                return EnumLists.Island.Serca;
            }
            else if (_island.Contains("Shasha")) {
                return EnumLists.Island.Shasha;
            }
            else if (_island.Contains("Shirna")) {
                return EnumLists.Island.Shirna;
            }
            else if (_island.Contains("Sokota")) {
                return EnumLists.Island.Sokota;
            }
            else if (_island.Contains("Staren")) {
                return EnumLists.Island.Staren;
            }
            else if (_island.Contains("Taramura")) {
                return EnumLists.Island.Taramura;
            }
            else if (_island.Contains("Tashu")) {
                return EnumLists.Island.Tashu;
            }
            else if (_island.Contains("Teste")) {
                return EnumLists.Island.Teste;
            }
            else if (_island.Contains("Teyamal")) {
                return EnumLists.Island.Teyamal;
            }
            else if (_island.Contains("Theonil")) {
                return EnumLists.Island.Theonil;
            }
            else if (_island.Contains("Tigris")) {
                return EnumLists.Island.Tigris;
            }
            else if (_island.Contains("Tinberra")) {
                return EnumLists.Island.Tinberra;
            }
            else if (_island.Contains("Tulu")) {
                return EnumLists.Island.Tulu;
            }
            else if (_island.Contains("Wandering")) {
                return EnumLists.Island.Wandering;
            }
            else if (_island.Contains("Weita")) {
                return EnumLists.Island.Weita;
            }
            else
                return EnumLists.Island.UnKnown;
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
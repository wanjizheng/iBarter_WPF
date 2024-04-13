using System;
using System.IO;
using Syncfusion.Windows.Shared;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Linq;
using System.Windows.Media;

namespace iBarter {
    public class Barter : NotificationObject {
        private Islands _isLand = null!;
        private Items item1 = null!, item2 = null!;
        private String icon1 = null!, icon2 = null!;
        private String item1Name = "", item2Name = "";
        private int exchangeQuantity = 0;
        private bool exchangeDone = false;
        private int barterGroup = -1;

        public Barter() {
        }

        public Barter(Islands _isLand, Items _item1, Items _item2, int _exchangeQuantity = 0, bool _exchangeDone = false, int _barterGroup = -1) {
            this._isLand = _isLand;
            item1 = _item1;
            item2 = _item2;
            item1Name = item1.getName();
            item2Name = item2.getName();
            icon1 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item1.getID() + ".bmp";
            icon2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item2.getID() + ".bmp";
            this.exchangeQuantity = _exchangeQuantity;
            this.barterGroup = _barterGroup;
        }

        public Islands IsLand {
            get { return _isLand; }
            set {
                _isLand = value;
                RaisePropertyChanged("IsLand");
            }
        }

        public int BarterGroup {
            get { return barterGroup; }
            set { barterGroup = value; }
        }

        public bool ExchangeDone {
            get { return exchangeDone; }
            set { exchangeDone = value; }
        }

        public int ExchangeQuantity {
            get { return exchangeQuantity; }
            set { exchangeQuantity = value; }
        }

        public int Parley {
            get { return IsLand.Parley; }
            set {
                IsLand.Parley = value;
                RaisePropertyChanged("Parley");
            }
        }

        public string IsLandName {
            get { return IsLand.IslandsName; }
            set {
                Islands myIslands = new Islands(App.myCFun.IslandEnum(value), App.listIslands.Where(land => land.Island == App.myCFun.IslandEnum(value)).Select(land => land.Parley).FirstOrDefault());
                IsLand = myIslands;
            }
        }

        public int IslandRemaining {
            get { return IsLand.Remaining; }
            set {
                IsLand.Remaining = value;
                RaisePropertyChanged("remainingChange");
            }
        }

        public Items Item1 {
            get { return item1; }
            set {
                item1 = value;
                RaisePropertyChanged("Item1");
            }
        }

        public Items Item2 {
            get { return item2; }
            set {
                item2 = value;
                RaisePropertyChanged("Item2");
            }
        }

        public int Item1Number {
            get { return Item1.getNumber(); }
            set {
                Item1.setNumber(value);
                RaisePropertyChanged("ItemChange");
            }
        }

        public string Item1Name {
            get {
                if (item1Name.Equals(""))
                    item1Name = Item1.getName();
                return item1Name;
            }
            set {
                item1Name = value;
                UpdateItem();
                RaisePropertyChanged("ItemChange");
            }
        }

        public string Item1LV {
            get {
                string strLV = "";
                switch (Item1.ItemLV) {
                    case "0":
                        strLV = "[Basic Item]";
                        break;
                    case "1":
                        strLV = "[Level 1]";
                        break;
                    case "2":
                        strLV = "[Level 2]";
                        break;
                    case "3":
                        strLV = "[Level 3]";
                        break;
                    case "4":
                        strLV = "[Level 4]";
                        break;
                    case "5":
                        strLV = "[Level 5]";
                        break;
                    default:
                        strLV = "[Misc]";
                        break;
                }

                return strLV;
            }
        }

        public string Item1Icon {
            get {
                if (icon1 == null || !icon1.Contains(Item1.getID())) {
                    icon1 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item1.getID() + ".bmp";
                }

                return icon1;
            }
            set {
                icon1 = value;
                RaisePropertyChanged("IconChange");
            }
        }

        public int Item2Number {
            get { return Item2.getNumber(); }
            set {
                Item2.setNumber(value);
                RaisePropertyChanged("ItemChange");
            }
        }

        public string Item2Name {
            get {
                if (item2Name.Equals(""))
                    item2Name = Item2.getName();
                return item2Name;
            }
            set {
                item2Name = value;
                UpdateItem();
                RaisePropertyChanged("ItemChange");
            }
        }

        public string Item2Icon {
            get {
                if (icon2 == null || !icon2.Contains(Item2.getID())) {
                    icon2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item2.getID() + ".bmp";
                }

                return icon2;
            }
            set {
                icon2 = value;
                RaisePropertyChanged("ItemChange");
            }
        }

        private void UpdateItem() {
            if (!item1Name.Equals(Item1.getName())) {
                Items item1 = new Items(App.listItems.FirstOrDefault(i => i.getName().Equals(item1Name)).getName(), App.listItems.FirstOrDefault(i => i.getName().Equals(item1Name)).getID(), App.listItems.FirstOrDefault(i => i.getName().Equals(item1Name)).getLV());
                Item1 = item1;
                //icon1 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item1.getID() + ".bmp";
            }

            if (!item2Name.Equals(Item2.getName())) {
                Items item2 = new Items(App.listItems.FirstOrDefault(i => i.getName().Equals(item2Name)).getName(), App.listItems.FirstOrDefault(i => i.getName().Equals(item2Name)).getID(), App.listItems.FirstOrDefault(i => i.getName().Equals(item2Name)).getLV());
                Item2 = item2;
                //icon2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item2.getID() + ".bmp";
            }

            RaisePropertyChanged("ItemChange");
            App.myBarterScanner.RefreshDataGrid();
        }


        // public void SetIsland(Islands _island) {
        //     _isLand = _island;
        // }
        //
        // public Islands GetIsland() {
        //     return _isLand;
        // }

        // public Items GetItem1() {
        //     return item1;
        // }
        //
        // public Items GetItem2() {
        //     return item2;
        // }
        //
        // public void SetItem1(Items _item) {
        //     item1 = _item;
        // }
        //
        // public void SetItem2(Items _item) {
        //     item2 = _item;
        // }
    }
}
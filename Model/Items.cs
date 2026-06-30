using Syncfusion.Windows.Shared;
using System.IO;

namespace iBarter {
    public class Items : NotificationObject {
        private string strID;
        private string strLV;
        private string strName;
        private int intNumber;
        private int intStorage_Velia, intStorage_Iliya, intStorage_Epheria, intStorage_Ancado;
        private String icon = null!;

        public Items(string _name, string _id, string _lv, int _number = -1, int _intStorageVelia = 0, int _intStorageIliya = 0, int _intStorageEpheria = 0, int _intStorageAncado = 0) {
            strName = _name;
            strID = _id;
            strLV = _lv;
            intNumber = _number;
            intStorage_Velia = _intStorageVelia;
            intStorage_Iliya = _intStorageIliya;
            intStorage_Epheria = _intStorageEpheria;
            intStorage_Ancado = _intStorageAncado;
        }

        public string ItemName {
            get { return strName; }
            set {
                strName = value;
                RaisePropertyChanged("ItemName");
            }
        }

        public string ItemID {
            get { return strID; }
            set {
                strID = value;
                RaisePropertyChanged("ItemID");
            }
        }

        public string ItemIcon {
            get {
                if (icon == null || !icon.Contains(ItemID)) {
                    icon = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + ItemID + ".bmp";
                }

                if (!File.Exists(icon) && int.TryParse(ItemID, out int idNum) && idNum > 0) {
                    App.myCFun.RefreshItems(ItemID);
                    icon = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + ItemID + ".bmp";
                }

                return icon;
            }
            set {
                icon = value;
                RaisePropertyChanged("ItemIcon");
            }
        }

        public string ItemLV {
            get { return strLV; }
            set {
                strLV = value;
                RaisePropertyChanged("ItemLV");
            }
        }

        public string ItemTier {
            get {
                string strLV = "";
                switch (ItemLV) {
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
                    case "6":
                        strLV = "[Level 6]";
                        break;
                    case "7":
                        strLV = "[Level 7]";
                        break;
                    default:
                        strLV = "[Misc]";
                        break;
                }

                return strLV;
            }
        }

        public int ItemNumber {
            get { return intNumber; }
            set {
                intNumber = value;
                RaisePropertyChanged("ItemNumber");
            }
        }

        public int StorageVeliaQuantity_Velia {
            get { return intStorage_Velia; }
            set { intStorage_Velia = value; }
        }

        public int StorageVeliaQuantity_Iliya {
            get { return intStorage_Iliya; }
            set { intStorage_Iliya = value; }
        }

        public int StorageVeliaQuantity_Epheria {
            get { return intStorage_Epheria; }
            set { intStorage_Epheria = value; }
        }

        public int StorageVeliaQuantity_Ancado {
            get { return intStorage_Ancado; }
            set { intStorage_Ancado = value; }
        }
    }
}
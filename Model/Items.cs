using Syncfusion.Windows.Shared;
using System.IO;
using System.Reflection;

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
                RaisePropertyChanged("NameChange");
            }
        }

        public string ItemID {
            get { return strID; }
            set {
                strID = value;
                RaisePropertyChanged("IDChange");
            }
        }

        public string ItemIcon {
            get {
                if (icon == null || !icon.Contains(ItemID)) {
                    icon = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + ItemID + ".bmp";
                }

                return icon;
            }
            set {
                icon = value;
                RaisePropertyChanged("IconChange");
            }
        }

        public string ItemLV {
            get { return strLV; }
            set {
                strLV = value;
                RaisePropertyChanged("LVChange");
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
                RaisePropertyChanged("NumberChange");
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
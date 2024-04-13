using Syncfusion.Windows.Shared;

namespace iBarter {
    public class Items : NotificationObject {
        private string strID;
        private string strLV;
        private string strName;
        private int intNumber;

        public Items(string _name, string _id, string _lv, int _number = -1) {
            strName = _name;
            strID = _id;
            strLV = _lv;
            intNumber = _number;
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

        public string ItemLV
        {
            get { return strLV; }
            set
            {
                strLV = value;
                RaisePropertyChanged("LVChange");
            }
        }

        public int ItemNumber
        {
            get { return intNumber; }
            set
            {
                intNumber = value;
                RaisePropertyChanged("NumberChange");
            }
        }


        public void setName(string _name) {
            strName = _name;
        }

        public string getName() {
            return strName;
        }

        public string getID() {
            return strID;
        }

        public string getLV() {
            return strLV;
        }

        public void setLV(string _lv) {
            strLV = _lv;
        }

        public void setID(string _id) {
            strID = _id;
        }

        public void setNumber(int _number) {
            intNumber = _number;
        }

        public int getNumber() {
            return intNumber;
        }
    }
}
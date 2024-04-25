using System.Windows;

namespace iBarter {
    public class Islands {
        private EnumLists.Island enumIsland;
        private int intParley = 0;
        private int intRemaining;
        private Thickness myThickness;


        public Islands(EnumLists.Island _name, int _parley, int _remaining = 0) {
            intParley = _parley;
            enumIsland = _name;
            intRemaining = _remaining;
            myThickness = new Thickness(0, 0, 0, 0);
        }

        public Thickness IslandsThickness {
            get {
                if (myThickness.Left + myThickness.Right + myThickness.Top + myThickness.Bottom == 0) {
                    myThickness = App.listIslands.FirstOrDefault(i => i.IslandsName == IslandsName).IslandsThickness;
                }

                return myThickness;
            }
            set { myThickness = value; }
        }

        public EnumLists.Island Island {
            get { return enumIsland; }
            set { enumIsland = value; }
        }

        public string IslandsName {
            get { return Island.ToString(); }
        }

        public int Parley {
            get { return intParley; }
            set {
                intParley = value;
            }
        }

        public int Remaining {
            get { return intRemaining; }
            set { intRemaining = value; }
        }


        // public EnumLists.Island GetIslandEnum() {
        //     return enumIsland;
        // }

        // public void SetIslandEnum(EnumLists.Island _island) {
        //     enumIsland = _island;
        // }


        // public int GetParley() {
        //     return intParley;
        // }
        //
        // public void SetParley(int _parley) {
        //     intParley = _parley;
        // }

        // public void SetRemaining(int _remaining) {
        //     intRemaining = _remaining;
        // }
        //
        // public int GetRemaining() {
        //     return intRemaining;
        // }
    }
}
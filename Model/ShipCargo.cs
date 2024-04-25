namespace iBarter.Model {
    public class ShipCargo {
        private Barter myBarter;

        public ShipCargo(Barter _list) {
            myBarter = _list;
        }

        public Barter Barter {
            get { return myBarter; }
            set { myBarter = value; }
        }
    }
}
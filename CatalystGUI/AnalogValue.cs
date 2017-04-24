using System.ComponentModel;

namespace CatalystGUI
{
    internal class AnalogValue : INotifyPropertyChanged
    {
        public string DisplayName { get; set; } // e.g. "Ndl P"

        public int Pin { get; set; } // the analog pin it gets data from

        float _value; // pressure in whatever units
        public float Value
        {
            get
            {
                return _value;
            }
            set
            {
                _value = value;
                // need to notify it here and it will update the appropriate element in the ItemsControl
                NotifyPropertyChanged("Value"); 
            }
        }

        public AnalogValue(string DisplayName, int Pin)
        {
            this.DisplayName = DisplayName;
            this.Pin = Pin;

            _value = -1;
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
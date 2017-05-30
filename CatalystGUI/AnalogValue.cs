using System.ComponentModel;

namespace CatalystGUI
{
    internal class AnalogValue : INotifyPropertyChanged
    {
        public string DisplayName { get; set; } // e.g. "Ndl P"

        public int Pin { get; set; } // pin or "channel" on ADS1115

        float[] valuesArray; // pressure in whatever units added in array to take moving average
        int i;
        const int ARRAY_SIZE = 5; // # of readings do moving average
        public float Value
        {
            get
            {
                float sum = 0;
                for (int k = 0; k < ARRAY_SIZE; k++)
                {
                    sum += valuesArray[k];
                }
                return (int)(100 * sum / ARRAY_SIZE) / 100f; // make it show only 2 decimal places
            }
            set
            {
                valuesArray[i] = value;
                i++;
                if (i >= ARRAY_SIZE) i = 0; // reset index to not go out-of-bounds

                // need to notify it here and it will update the appropriate element in the ItemsControl
                NotifyPropertyChanged("Value"); 
            }
        }

        public AnalogValue(string DisplayName, int Pin)
        {
            this.DisplayName = DisplayName;
            this.Pin = Pin;

            valuesArray = new float[ARRAY_SIZE];
            i = 0;

            // initialize all to -1
            for (uint n = 0; n < ARRAY_SIZE; n++)
            {
                valuesArray[n] = -1;
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
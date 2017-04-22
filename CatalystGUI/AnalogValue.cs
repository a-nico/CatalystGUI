namespace CatalystGUI
{
    internal class AnalogValue
    {
        public string DisplayName { get; set; } // e.g. "Ndl P"
        public int Pin { get; set; } // the analog pin it gets data from
        public float Value { get; set; } // pressure in whatever units

        public AnalogValue(string DisplayName, int Pin)
        {
            this.DisplayName = DisplayName;
            this.Pin = Pin;
        }
    }
}
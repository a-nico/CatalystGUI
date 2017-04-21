using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CatalystGUI
{
    internal class ArduinoStuff : INotifyPropertyChanged
    {
        #region Misc fields
        public const int BAUD_RATE = 115200;
        SerialPort usb;
        Dispatcher UIDispatcher;
        DispatcherTimer serialTimer;
        Task serialTask; // handles incoming data from usb on separate thread

        int[] analogValues;
        Dictionary<string, int> nameToPinMap; // key: name of device plugged in, value: Arduino pin
        Dictionary<int, string> pinToNameMap; // key: Arduino pin, value: name of device plugged in
        #endregion

        #region  Properties that UI elements bind to
        public bool SIunits { get; set; } // true = SI (kPa, microns), false = standard (psi, thou)
        // these are read-only because it's just raw data that comes in (displayed in TextBlock)
        public float MainPressure
        {
            get { return ConvertRawToPressure(analogValues[nameToPinMap["MainPressure"]]); }
        }
        public float NeedlePressure
        {
            get { return ConvertRawToPressure(analogValues[nameToPinMap["NeedlePressure"]]); }
        }
        public float LiquidPressure
        {
            get { return ConvertRawToPressure(analogValues[nameToPinMap["LiquidPressure"]]); }
        }
        public float FanSpeed
        {
            get { return ConvertRawToPressure(analogValues[nameToPinMap["FanSpeed"]]); }
        }
        public float NeedlePosition
        {
            get { return ConvertRawToPressure(analogValues[nameToPinMap["NeedlePosition"]]); }
        }
        #endregion

        public ArduinoStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;

            // create task to obtain tokens from serial, add to queue, then process queue
            serialTask = new Task(new Action(() =>
            {
                while (true)
                {
                    GetSerialTokens();
                    ProcessTokenQueue();
                }
            }));

            serialTimer = new DispatcherTimer();
            serialTimer.Interval = TimeSpan.FromMilliseconds(500);
            serialTimer.Tick += ProcessSerial_Tick;
            serialTimer.Start();

            // arrays
            analogValues = new int[16]; // stores 10-bit numbers as they come in from Arduino (MEGA has 16 pins)
            serialTokenQueue = new Queue<string>(); // initialize
            
            // maps
            nameToPinMap = new Dictionary<string, int>();
            nameToPinMap.Add("MainPressure", 0);
            nameToPinMap.Add("NeedlePressure", 1);
            nameToPinMap.Add("LiquidPressure", 2);
            nameToPinMap.Add("FanSpeed", 3);
            nameToPinMap.Add("NeedlePosition", 4);
            pinToNameMap = MakeReverseMap(nameToPinMap);
        }
        
        // move motor
        public void MoveStepper(int motor, int steps)
        {
            usb.Write(String.Format("%M{0},{1},", motor, steps));
        }

        // Timer stuff
        private void ProcessSerial_Tick(object sender, EventArgs e)
        {
            // TEST: write some commands so the serial pipe won't be empty
            for (uint n = 0; n < 4; n++)
            {
                usb.Write(String.Format("%A{0};", n));
                //System.Threading.Thread.Sleep(10);
            }
            //GetSerialTokens();
            //ProcessTokenQueue();
        }


        #region Processing data coming from serial.
        string serialTokenBuffer; // adds chars until a complete token is made (till it sees ";")
        Queue<string> serialTokenQueue; // once a token is made it gets added to queue

        // enqueues tokens coming in from serial
        void GetSerialTokens()
        {
            while (null != this.incomingSerialBuffer && "" != incomingSerialBuffer)
            {
                if (incomingSerialBuffer[0] == ';')
                {
                    serialTokenQueue.Enqueue(serialTokenBuffer); // buffer is a complete command, add to queue
                    serialTokenBuffer = String.Empty; // clear the buffer to make space for next one
                    incomingSerialBuffer = incomingSerialBuffer.Remove(0, 1); // throw away the ";"
                }
                else
                {   // move one char from incomingSerialBuffer to serialTokenBuffer
                    serialTokenBuffer += incomingSerialBuffer[0];
                    incomingSerialBuffer = incomingSerialBuffer.Remove(0, 1);
                }

            }
        }

        // processes tokens from queue - basically updates fields/UI with data that came from Arduino
        private void ProcessTokenQueue()
        {
            while (serialTokenQueue.Count != 0)
            {
                string token = serialTokenQueue.Dequeue();
                //identifier possibilities are: A (analog pin reading), D (digital pin reading)
                char identifier = token[0];

                switch (identifier)
                {
                    case 'A':
                        // should split into [A], [pin], [value]
                        string[] elements = token.Split(',');
                        int pin;
                        int value;
                        int.TryParse(elements[1], out pin);
                        int.TryParse(elements[2], out value);
                        // update the analogValues array with this new data
                        analogValues[pin] = value;

                        // if UI is monitoring that pin, notify property changed
                        if (pinToNameMap.TryGetValue(pin, out string name))
                        {
                            NotifyPropertyChanged(name);
                        }
                        break;

                    case 'D':
                        // ... to do
                        break;

                    default:
                        // was some garbage identifier, token is popped so don't worry
                        break;
                }

            }
        }


        #endregion

        #region USB receiving bytes (event handler)
        private string incomingSerialBuffer; // holds incoming bytes as string
        // event handler for USB data received. All it does is read all available bytes and puts them in string buffer
        private void USB_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            this.incomingSerialBuffer += usb.ReadExisting();
        }
        #endregion

        // makes the "usb" object
        // PortSelector is dropdown thing from UI that selects COM
        public void Connect(ComboBox PortSelector)
        {
            try
            {
                if (usb == null)
                {
                    if (SerialPort.GetPortNames().Length == 1)
                    {   // if there's only 1 COM available, pick that one automatically
                        usb = new SerialPort(SerialPort.GetPortNames()[0], BAUD_RATE);
                        PortSelector.SelectedIndex = 0; // automatically show what's selected (first one)
                    }
                    else if (PortSelector.SelectedValue == null)
                    {
                        System.Windows.MessageBox.Show("No COM port selected.");
                        return;
                    }
                    else
                    { // use the COM selected from combo box
                        usb = new SerialPort(PortSelector.SelectedValue.ToString(), BAUD_RATE);
                    }
                    // subscribe handler to DataReceived event (gets raised kind of randomly after it receives byte in serial pipe)
                    usb.DataReceived += USB_DataReceived;

                    if (!usb.IsOpen) usb.Open();

                    // starts task that processes incoming serial data
                    serialTask.Start();
                }
            } 
            catch (Exception e)
            {
                System.Windows.MessageBox.Show("Exception thrown in ArduinoStuff class' public void Connect(ComboBox PortSelector) method. Exeption message: " + e.Message);
            }

        }
        
        // takes raw data from analog-digital converter and gives pressure (PSI or kPa depending on boolean property)
        float ConvertRawToPressure(int raw)
        {
            // the honeywell sensors have range .1(1023) to .9(1023) which map to 0-30 psi
            float psi = 30 * (raw - 1023 / 10) / (1023 * 8 / 10);

            if (SIunits)
            { // 1 psi = 6.89476 kPa
                return psi * 6.89476f;
            }
            return psi;
        }
        
        // reverses the map so I can have both
        private Dictionary<int, string> MakeReverseMap(Dictionary<string, int> map)
        {
            var reverseMap = new Dictionary<int, string>();
            foreach (var name in map.Keys)
            {
                reverseMap[map[name]] = name;
            }
            return reverseMap;
        }

        #region PropertyChanged stuff
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string name)
        {
            // since most things in this class are done on background thread, always use UI Dispatcher
            UIDispatcher?.BeginInvoke(new Action(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            }));

        }
        #endregion
    }
}
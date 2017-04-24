using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CatalystGUI
{
    // what I have:
    // Pressure - analog (read), motor (set)
    // Needle position - data from image not arduino (maybe count steps? but then need to keep track offline)
    // Solenoid - digital (on/off)
    // Fan - digital (on/off), serial (sets PWM duty cycle)
    // Future:
    // heater - serial (in), serial (PWM duty cycle)
    // LED ring - serial (PWM duty cycle)

    internal class ArduinoStuff : INotifyPropertyChanged
    {
        #region Pin Constants
        public const int SOLENOID_PIN = 4;
        public const int FAN_PIN = 5;
        public const int LED_PIN = 6;
        #endregion

        #region Misc fields
        public const int BAUD_RATE = 115200;
        public const int FAST_TIMER_TIMESPAN = 200; // 5 Hz
        public const int SLOW_TIMER_TIMESPAN = 500; // 2 Hz
        SerialPort usb;
        Dispatcher UIDispatcher;
        DispatcherTimer slowTimer; // for requesting  solenoid, fan, LED ring, moving motors
        DispatcherTimer fastTimer; // for requesting pressure readings
        Task serialTask; // handles incoming data from usb on separate thread
        int[] analogValues;
        int[] digitalValues;
        Dictionary<int, String> digitalMap; // maps digital pins to whatever they're connected to
        Dictionary<String, int> motorMap; // maps motor name (i.e. NeedleMotor, MainPressureMotor) to its motor # in arduino code
        #endregion

        #region  Properties for Control/UI
        public bool SIunits { get; set; } // true = SI (kPa, microns), false = standard (psi, thou)
        public bool LEDring // true = on
        {
            get
            {
                return digitalValues[LED_PIN] != 0;
            }
            set
            {
                this.serialOutgoingQueue.Enqueue(String.Format("%W,{0},{1};", LED_PIN, value ? 1 : 0));
                NotifyPropertyChanged(digitalMap[LED_PIN]);
            }
        }
        public bool Solenoid // true = current through solenoid (valve open)
        {
            get
            {
                return digitalValues[SOLENOID_PIN] != 0;
            }
            set
            {
                this.serialOutgoingQueue.Enqueue(String.Format("%W,{0},{1};", SOLENOID_PIN, value ? 1 : 0));
                NotifyPropertyChanged(digitalMap[SOLENOID_PIN]);
            }
        }
        int _fan;
        public int Fan
        {
            get
            {
                if (digitalValues[FAN_PIN] == 0) return 0;
                return _fan;
            }
            set
            {
                if (value == 0)
                {   // just switch the MOSFET off
                    this.serialOutgoingQueue.Enqueue(String.Format("%W,{0},{1};", FAN_PIN, 0));
                    goto Notify;
                }

                // getting here means value != 0, but fan shows as OFF
                if (digitalValues[FAN_PIN] == 0)
                {   // switch on the fan MOSFET
                    this.serialOutgoingQueue.Enqueue(String.Format("%W,{0},{1};", FAN_PIN, 1));
                }

                if (value >= 20 && value < 100)
                {
                    this.serialOutgoingQueue.Enqueue(String.Format("%F,{0};", value));
                }
                else if (value >= 100)
                {   // max out at 100 independent of value
                    this.serialOutgoingQueue.Enqueue(String.Format("%F,{0};", 100));
                }
                else
                {   // put it on low (20)
                    this.serialOutgoingQueue.Enqueue(String.Format("%F,{0};", 20));
                }


                Notify: NotifyPropertyChanged(digitalMap[FAN_PIN]);
            }
        }

        // ItemsControl collection for pressures:
        private List<AnalogValue> _pressures;
        public List<AnalogValue> Pressures
        {
            get
            {
                return _pressures;
            }
            set
            {
                _pressures = value;
            }
        }
        #endregion

        // to do: make UI elements for items in digital map - Solenoid, FanOnOff , LEDring
        // also, code up the outgoig queue in timers

        public ArduinoStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;

            // dictionaries
            digitalMap = new Dictionary<int, string>(); // <Digital_pin, name>
            digitalMap.Add(SOLENOID_PIN, "Solenoid");
            digitalMap.Add(FAN_PIN, "Fan");
            digitalMap.Add(LED_PIN, "LEDring");
            motorMap = new Dictionary<string, int>(); // <name, motor # in .ino code>
            motorMap.Add("NeedlePositionMotor", 0);
            motorMap.Add("LiquidPressureMotor", 1);
            motorMap.Add("NeedlePressureMotor", 2);
            motorMap.Add("MainPressureMotor", 3);

            // arrays
            _pressures = new List<AnalogValue>(); // list of objects that have DisplayName, Pin, Value. For UI binding
            analogValues = new int[16]; // stores 10-bit numbers as they come in from Arduino (MEGA has 16 pins)
            analogValues = new int[16]; // stores 0 or 1 as they come in from Arduino (only monitoring 0-13 (PWM pins))
            serialIncomingQueue = new Queue<string>(); // initialize
            serialOutgoingQueue = new Queue<string>(); // initialize


            // make AnalogValue objects that map to pressure
            // question: shouldn't it be _pressures.Add() and then Pressure = _pressures?
            Pressures.Add(new AnalogValue("Main p", 0));
            Pressures.Add(new AnalogValue("Liq p", 1));
            Pressures.Add(new AnalogValue("Ndl p", 2));
            NotifyPropertyChanged("Pressures");

            #region Tasks and Timers
            // create task to obtain tokens from serial, add to queue, then process queue
            serialTask = new Task(new Action(() =>
            {
                while (true)
                {
                    GetSerialTokens();
                    ProcessIncomingQueue();
                }
            }));

            this.slowTimer = new DispatcherTimer();
            this.slowTimer.Interval = TimeSpan.FromMilliseconds(SLOW_TIMER_TIMESPAN);
            this.slowTimer.Tick += SlowLoop_Tick;

            this.fastTimer = new DispatcherTimer();
            this.fastTimer.Interval = TimeSpan.FromMilliseconds(FAST_TIMER_TIMESPAN);
            this.fastTimer.Tick += FastLoop_Tick;
            // all these start on "Connect()"
            #endregion

        }

        #region Controls
        // move stepper motor by name in motorMap
        public void MoveStepper(string motorName, int steps)
        {
            if (motorMap.TryGetValue(motorName, out int motor))
            {   // if motor name exists, add to outgoing queue
                serialOutgoingQueue.Enqueue(String.Format("%M{0},{1},", motor, steps)); 

            }
        }
        #endregion

        #region Timer Stuff - Serial Outgoing
        Queue<string> serialOutgoingQueue;

        // updates pressures, processes outgoing queue: moves steppers, sets fan, solenoid, LED ring
        private void FastLoop_Tick(object sender, EventArgs e)
        { 
            while (serialOutgoingQueue.Count > 0)
            {
                this.usb.Write(this.serialOutgoingQueue.Dequeue());
            }

            foreach (var p in _pressures)
            {   // writing to USB makes arduino return that analog reading which gets handled by incoming task
                this.usb.Write(String.Format("%A{0};", p.Pin));
            }
        }
        
        // updates state of: fan, solenoid, LED ring.
        private void SlowLoop_Tick(object sender, EventArgs e)
        {   
            this.usb.Write(String.Format("%R{0};", LED_PIN));
            this.usb.Write(String.Format("%R{0};", SOLENOID_PIN));
            this.usb.Write(String.Format("%R{0};", FAN_PIN));
        }
        #endregion

        #region Serial Incoming
        string tokenBuffer; // adds chars until a complete token is made (till it sees ";")
        Queue<string> serialIncomingQueue; // once a token is made it gets added to queue

        // enqueues tokens coming in from serial
        void GetSerialTokens()
        {
            while (null != this.incomingSerialBuffer && "" != incomingSerialBuffer)
            {
                if (incomingSerialBuffer[0] == ';')
                {
                    this.serialIncomingQueue.Enqueue(this.tokenBuffer); // buffer is a complete command, add to queue
                    this.tokenBuffer = String.Empty; // clear the buffer for next token
                    this.incomingSerialBuffer = this.incomingSerialBuffer.Remove(0, 1); // throw away the ";"
                }
                else
                {   // move one char from incomingSerialBuffer to tokenBuffer
                    this.tokenBuffer += this.incomingSerialBuffer[0];
                    this.incomingSerialBuffer = this.incomingSerialBuffer.Remove(0, 1);
                }

            }
        }

        // processes tokens from queue - basically updates fields/UI with data that came from Arduino
        private void ProcessIncomingQueue()
        {
            while (this.serialIncomingQueue.Count != 0)
            {
                string[] elements = this.serialIncomingQueue.Dequeue().Split(',');

                // elements[0] possibilities are: A (analog pin reading), D (digital pin reading)
                switch ((elements[0])[0])
                {
                    case 'A':
                        {
                            // should split into [A], [pin], [value]
                            int pin; int value; // placeholders

                            if (int.TryParse(elements[1], out pin)
                                && int.TryParse(elements[2], out value))
                            {
                                // update the analogValues array with this new data
                                this.analogValues[pin] = value;

                                // if pressure sensor is attached to this pin, update collection
                                foreach (var p in this.Pressures)
                                { // inefficient but I don't know how to map "name" to Property.
                                    if (p.Pin == pin)
                                    {
                                        p.Value = ConvertRawToPressure(value);
                                        NotifyPropertyChanged(p.DisplayName);
                                    }
                                }
                            }
                            break;
                        }

                    case 'D':
                        {
                            // should split into [D], [pin], [value (0/1)]
                            int pin; int value; // placeholders

                            if (int.TryParse(elements[1], out pin)
                                && int.TryParse(elements[2], out value))
                            {
                                // update digitalValues array that holds latest data
                                this.digitalValues[pin] = value;
                                if (this.digitalMap.TryGetValue(pin, out string name))
                                {
                                    NotifyPropertyChanged(name);
                                }
                            }

                            break;
                        }

                    default:
                        // was some garbage identifier, token is popped so don't worry
                        break;
                }
            }
        }

        // USB receiving bytes (event handler)
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
                if (this.usb == null)
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
                    usb.Open();

                    serialTask.Start(); // starts task that processes incoming serial data
                    slowTimer.Start(); // start the timers that send requests to Arduino
                    fastTimer.Start();

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

            if (this.SIunits)
            { // 1 psi = 6.89476 kPa
                return psi * 6.89476f;
            }
            return psi;
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
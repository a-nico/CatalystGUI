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
    // LED ring - serial (PWM duty cycle)
    // Future:
    // heater - serial (in), serial (PWM duty cycle)

    internal class ArduinoStuff : INotifyPropertyChanged
    {
        #region Pin Constants
        public const int SOLENOID_PIN = 4;
        public const int FAN_PIN = 5;
        public const int LED_PIN = 6;
        #endregion

        #region Misc fields
        const int MAX_TEMP = 270; // limit on how much you can set temp
        public const int BAUD_RATE = 115200;
        public const int FAST_TIMER_TIMESPAN = 125; // 8 Hz
        public const int SLOW_TIMER_TIMESPAN = 511; // 2 Hz
        public const int FAN_TRESHOLD = 50; // below this it's all the same for some reason

        SerialPort usb;
        Dispatcher UIDispatcher;
        DispatcherTimer slowTimer; // for requesting  solenoid, fan, LED ring, moving motors
        DispatcherTimer fastTimer; // for requesting pressure readings
        Task serialTask; // handles incoming data from usb on separate thread
        int[] analogValues;
        int[] digitalValues;
        public Dictionary<String, int> motorNameMap; // maps motor name (i.e. NeedleMotor, MainPressureMotor) to its motor # in arduino code
        #endregion

        #region  Properties for Control/UI
        public bool SIunits { get; set; } // true = SI (kPa, microns), false = standard (psi, thou)

        int _temperature1;
        public int Temperature1
        {
            get
            {
                return _temperature1;
            }
            set
            {
                _temperature1 = value;
                NotifyPropertyChanged("Temperature1");
            }
        }

        int _temperature1Set;
        public int Temperature1Set
        {
            get
            {
                return _temperature1Set;
            }
            set
            {
                if (value > MAX_TEMP)
                {
                    _temperature1Set = MAX_TEMP;
                }
                else if (value < 0)
                {
                    _temperature1Set = 0;
                }
                else
                {
                    _temperature1Set = value;
                }
                NotifyPropertyChanged("Temperature1Set");
            }
        }

        int _potentiometer;
        public int Potentiometer // actually returns needle position in microns
        {
            get
            {
                return PotentiometerToMicrons(_potentiometer);
            }
            set
            {
                _potentiometer = value;
                NotifyPropertyChanged("Potentiometer");
            }
        }

        public bool controlNeedleBool;  // makes it stop controlling by "Stop" button on UI
        int _needlePositionSet;
        public int NeedlePositionSet
        {
            get
            {
                return _needlePositionSet;
            }
            set
            {
                _needlePositionSet = value;
                controlNeedleBool = true;
            }
        } // set only by UI

        public bool LEDring // true = on
        {
            get
            {
                return digitalValues[LED_PIN] != 0;
            }
            set
            {
                DigitalWrite(LED_PIN, value ? 1 : 0);
                //this.serialOutgoingQueue.Enqueue(String.Format("%W{0},{1};", LED_PIN, value ? 1 : 0));
            }
        }

        //public bool Solenoid // true = current through solenoid (valve open)
        //{
        //    get
        //    {
        //        return digitalValues[SOLENOID_PIN] != 0;
        //    }
        //    set
        //    {   // true does digitalWrite(SOLENOID_PIN, HIGH);

        //        this.serialOutgoingQueue.Enqueue(String.Format("%W{0},{1};", SOLENOID_PIN, value ? 1 : 0));
        //    }
        //}

        int _fan;

        public int Fan
        {
            get
            {
                return _fan;
            }
            set
            {
                if (value == 0)
                {   // just switch the MOSFET off
                    _fan = 0;
                    DigitalWrite(FAN_PIN, 0);
                    //this.serialOutgoingQueue.Enqueue(String.Format("%W{0},{1};", FAN_PIN, _fan));
                    goto Notify;
                }

                if (digitalValues[FAN_PIN] == 0)
                {   // getting here means value != 0, but fan shows as OFF
                    // switch on the fan MOSFET
                    DigitalWrite(FAN_PIN, 1);
                    //this.serialOutgoingQueue.Enqueue(String.Format("%W{0},{1};", FAN_PIN, 1));
                }


                if (value >= FAN_TRESHOLD && value < 100)
                {
                    _fan = value;
                }
                else if (value >= 100)
                {   // max out at 100 independent of value
                    _fan = 100;
                }
                else
                {   // anything 1 to FAN_TRESHOLD sets fan to FAN_TRESHOLD. To set to zero, have to enter "0"
                    _fan = FAN_TRESHOLD;
                }
                this.serialOutgoingQueue.Enqueue(String.Format("%F{0};", _fan));

                Notify: NotifyPropertyChanged("Fan");
            }
        }

        // ItemsControl collection for pressures:
        public List<AnalogValue> Pressures { get; set; }
        
        #endregion

        // Constructor
        public ArduinoStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;

            // names for pressure display
            string mainPressure = "Main p";
            string liquidPressure = "Liq p";
            string needlePressure = "Ndl p";

            // dictionary - names of motors
            motorNameMap = new Dictionary<string, int>(); // <name, motor # in .ino code>
            motorNameMap.Add("NeedlePositionMotor", 0);
            motorNameMap.Add(liquidPressure, 1);
            motorNameMap.Add(needlePressure, 2);
            motorNameMap.Add(mainPressure, 3);

            // make AnalogValue objects that map to pressure
            Pressures = new List<AnalogValue>();
            Pressures.Add(new AnalogValue(mainPressure, 0, 8));
            Pressures.Add(new AnalogValue(liquidPressure, 1, 9));
            Pressures.Add(new AnalogValue(needlePressure, 2, 10));
            NotifyPropertyChanged("Pressures"); // is this necessary?

            // arrays
            //_pressures = new List<AnalogValue>(); // list of objects that have DisplayName, Pin, Value. For UI binding
            analogValues = new int[16]; // stores 10-bit numbers as they come in from Arduino (MEGA has 16 pins)
            digitalValues = new int[16]; // stores 0 or 1 as they come in from Arduino (only monitoring 0-13 (PWM pins))
            serialIncomingQueue = new Queue<string>(); // initialize
            serialOutgoingQueue = new Queue<string>(); // initialize

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
            if (motorNameMap.TryGetValue(motorName, out int motor))
            {   // if motor name exists, add to outgoing queue
                serialOutgoingQueue.Enqueue(String.Format("%M{0},{1};", motor, steps)); 
            }
        }

        // Digital Pins
        public void DigitalWrite(int pin, int value)
        {
            if (value != 0) value = 1; // to avoid bugs
            this.serialOutgoingQueue.Enqueue(String.Format("%W{0},{1};", pin, value));

        }

        // reads from array, so it's not necessarily up-to-date. Have to request updates in timer method
        public bool DigitalRead(int pin)
        {
            return this.digitalValues[pin] == 1; // true means HIGH pin
        }

        void ControlTemperature(int tempSet, int tempActual) //if controlling more than 1, this needs to b redone
        {
            if (tempSet != 0)
            {
                int delta = tempSet - tempActual;

                if (delta > 0)
                {   // too cold
                    int signal = delta > 5 ? 255 : 255 * delta / 6; // slow it down when it gets within 5 deg C
                    this.usb.Write(String.Format("%H0,{0};", signal));
                }
                else
                {   // too hot -> off
                    this.usb.Write(String.Format("%H0,{0};", 0));
                } 
            }
        }

        void ControlNeedlePosition()
        {
            if (controlNeedleBool)
            {
                int error = NeedlePositionSet - Potentiometer;
                const int steps = 10;  // arbitrary, make it enough to last through a Fast_Loop cycle
                const int tolerance = 20; // +/- microns to call it good enough

                if (error > tolerance) // needs to increase
                {
                    MoveStepper("NeedlePositionMotor", steps);
                }
                else if (error < -tolerance) // needs to decrease
                {
                    MoveStepper("NeedlePositionMotor", -steps);
                }
                else
                {
                    MoveStepper("NeedlePositionMotor", 0);
                    controlNeedleBool = false;
                }
            }
        }

        // take in the motor name from button -> object.DisplayName
        void ControlPressureRegulator(AnalogValue obj)
        {
            if (obj.controlPressureBool)
            {
                float error = obj.SetPoint - obj.Value;
                const int steps = 10;
                const float tolerance = 0.05f; // psi tolerance

                if (error > tolerance) // needs to increase
                {
                    MoveStepper(obj.DisplayName, -steps); // -steps increases pressure
                }
                else if (error < -tolerance) // needs to decrease
                {
                    MoveStepper(obj.DisplayName, steps);
                }
                else
                {
                    MoveStepper(obj.DisplayName, 0);
                    obj.controlPressureBool = false;
                } 
            }

        }

        #endregion

        #region Timer Stuff - Serial Outgoing
        Queue<string> serialOutgoingQueue;

        // updates pressures, processes outgoing queue: moves steppers, sets fan, solenoid, LED ring
        private void FastLoop_Tick(object sender, EventArgs e)
        {
            // take care of outgoing queue
            while (serialOutgoingQueue.Count > 0)
            {
                string token = this.serialOutgoingQueue.Dequeue();
                this.usb.Write(token);
                Console.WriteLine(token);
            }

            // calls for ADS1115 readings 
            for (uint ch = 0; ch < 4; ch++)
            {
                this.usb.Write(String.Format("%C{0};", ch));
            }

            //if (this.usb.BytesToWrite > 128) return; // to prevent buffer overflow (64 bytes RX buffer on MEGA)

            // temperature control
            this.usb.Write(String.Format("%T{0};", 1)); // calls for T1 reading
            ControlTemperature(Temperature1Set, Temperature1);

            // needle control
            ControlNeedlePosition();

            // pressure regulator control
            foreach (AnalogValue p in Pressures)
            {
                ControlPressureRegulator(p);
            }

            

        }

        // updates state of: fan, solenoids, LED ring.
        private void SlowLoop_Tick(object sender, EventArgs e)
        {   
            this.usb.Write(String.Format("%R{0};", LED_PIN));
            this.usb.Write(String.Format("%R{0};", FAN_PIN));

            // check solenoids
            foreach (var p in Pressures)
            {
                this.usb.Write(String.Format("%R{0};", p.SolenoidPin));
            }

        }
        #endregion

        #region Serial Incoming
        string tokenBuffer; // adds chars until a complete token is made (till it sees ";")
        Queue<string> serialIncomingQueue; // once a token is made it gets added to queue

        // enqueues tokens coming in from serial
        void GetSerialTokens()
        {
            while (null != this.incomingSerialBuffer && "" != this.incomingSerialBuffer)
            {
                if (incomingSerialBuffer[0] == ';')
                {   // buffer is a complete command, add to queue
                    this.serialIncomingQueue.Enqueue(this.tokenBuffer);
                    //Console.WriteLine(this.tokenBuffer); // for debugging
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

                try // gotta try because sometimes I get index out of range exception (no idea why)
                {
                    // elements[0] possibilities are: A (analog pin reading), D (digital pin reading)
                    switch ((elements[0])[0]) // last [0] is to switch to char like charAt(0)
                    {
                        case 'A': // analog reads FROM ARDUINO'S BUILT IN ADC
                            {
                                // should split into [A], [pin], [value]
                                int pin; int value; // placeholders

                                if (int.TryParse(elements[1], out pin)
                                    && int.TryParse(elements[2], out value))
                                {
                                    // update the analogValues array with this new data
                                    this.analogValues[pin] = value;

                                    //// if pressure sensor is attached to this pin, update Pressures collection elements
                                    //foreach (var p in this.Pressures)
                                    //{ // inefficient but I don't know how to map "name" to Property.
                                    //    if (p.Pin == pin)
                                    //    {
                                    //        p.Value = ConvertRawToPressure(value);
                                    //    }
                                    //}
                                }
                                break;
                            }

                        case 'D': // digital reads
                            {
                                // should split into [D], [pin], [value (0/1)]
                                int pin; int value; // placeholders

                                if (int.TryParse(elements[1], out pin)
                                    && int.TryParse(elements[2], out value))
                                {
                                    // update digitalValues array that holds latest data
                                    this.digitalValues[pin] = value;

                                }

                                break;
                            }

                        case 'C': // from ADS1115
                            {
                                // should split into [C], [channel/pin], [value]
                                // arduino guarantees that 0 <= channel/pin <= 3
                                int pin; int value; // placeholders

                                if (int.TryParse(elements[1], out pin)
                                    && int.TryParse(elements[2], out value))
                                {
                                    // channel/pin 3 is the potentiometer
                                    if (pin == 3)
                                    {
                                        Potentiometer = value;
                                    }
                                    else // it's a pressure, figure out which one and update the object's Value property
                                    {
                                        foreach (var p in Pressures)
                                        {
                                            if (p.Pin == pin)
                                            {
                                                p.Value = ConvertRawToPressure(value);
                                            }
                                        }
                                    }
                                }

                                break;
                            }

                        case 'T':
                            {
                                // T,1,25; means TC#1 is at 25 Celsius
                                int thermocoupleNumber; int tempCelsius; // placeholders

                                if (int.TryParse(elements[1], out thermocoupleNumber)
                                    && int.TryParse(elements[2], out tempCelsius))
                                {
                                    Temperature1 = tempCelsius;
                                }

                                break;
                            }

                        default:
                            // was some garbage identifier, token is dequeued so don't worry
                            break;
                    }
                }
                catch (IndexOutOfRangeException ex)
                {
                    System.Windows.MessageBox.Show("IndexOutOfRangeException in switch case: " + ex.Message + "\n \"elements[]\" length: " + elements.Length);
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
                        PortSelector.ItemsSource = SerialPort.GetPortNames(); // in case USB was plugged in after
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
        
        // takes raw data from ADS1115 and gives pressure (PSI or kPa)
        float ConvertRawToPressure(int raw)
        {
            float bitstAt5V = 26550; // count at 5.0 volts
            // the honeywell sensors have range .1(bitstAt5V) to .9(bitstAt5V) which map to 0-30 psi
            float psi = 30.0f * (raw - bitstAt5V / 10) / (bitstAt5V * 8 / 10);

            if (this.SIunits)
            { // 1 psi = 6.89476 kPa
                return psi * 6.89476f;
            }
            return psi;
        }

        // turn potentiometer reading into needle distance (microns)
        int PotentiometerToMicrons(int analogCounts)
        {
            return (int)(0.2430 * analogCounts - 4508.9);
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
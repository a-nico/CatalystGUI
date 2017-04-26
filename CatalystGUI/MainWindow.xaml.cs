using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CatalystGUI
{
    public partial class MainWindow : Window
    {
        const int stepsPerClick = 100; // number of stepper steps per +/- click
        CameraStuff cameraStuff;
        ArduinoStuff arduinoStuff;

        public MainWindow()
        {
            InitializeComponent();

            PortSelector.ItemsSource = SerialPort.GetPortNames(); // populate combo box

            // gray out camera buttons until camera is started
            LiveButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            CaptureButton.IsEnabled = false;

        }


        #region Buttons: Exposure, Frame Rate, Count
        // when clicked, the value gets multiplied/divided by this amount:
        private const double plusMinusMultiplier = 1.5;

        private void ExposurePlus(object sender, RoutedEventArgs e)
        {
            if (!StartCamButton.IsEnabled) cameraStuff.ExposureTime = (uint)(cameraStuff.ExposureTime * plusMinusMultiplier);
        }

        private void ExposureMinus(object sender, RoutedEventArgs e)
        {
            if (!StartCamButton.IsEnabled) cameraStuff.ExposureTime = (uint)(cameraStuff.ExposureTime / plusMinusMultiplier);
        }

        private void FrameRateMinus(object sender, RoutedEventArgs e)
        {
            if (!StartCamButton.IsEnabled) cameraStuff.FrameRate /= plusMinusMultiplier;
        }

        private void FrameRatePlus(object sender, RoutedEventArgs e)
        {
            if (!StartCamButton.IsEnabled) cameraStuff.FrameRate *= plusMinusMultiplier;
        }

        private void FrameCountMinus(object sender, RoutedEventArgs e)
        {
            if (!StartCamButton.IsEnabled) cameraStuff.FrameCount -= 5;
        }

        private void FrameCountPlus(object sender, RoutedEventArgs e)
        {
            if (!StartCamButton.IsEnabled) cameraStuff.FrameCount += 5;

        }
        #endregion

        #region Buttons: Capture, Live, Stop, Start Cam
        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            //CaptureButton.IsEnabled = false; // don't know how to re-enable through binding
            cameraStuff.Capture();
            // debug openCV
            //cameraStuff.GetImage();
        }

        private void Live_Click(object sender, RoutedEventArgs e)
        {
            if (!cameraStuff.liveMode)
            {
                cameraStuff.Live();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            cameraStuff.liveMode = false;
        }

        // starts camera that way it won't happen right when UI starts
        private void StartCam_Click(object sender, RoutedEventArgs e)
        {
            if (cameraStuff == null)
            {
                cameraStuff = new CameraStuff(Dispatcher);
            }

            // once it initializes, it turns off Start Cam button
            if (cameraStuff.InitializeCamera())
            {
                CameraGrid.DataContext = cameraStuff; // every child in "Grid" gets properties from this object
                StartCamButton.IsEnabled = false;
                LiveButton.IsEnabled = true;
                StopButton.IsEnabled = true;
                CaptureButton.IsEnabled = true;
            }
        }


        #endregion

        #region Arduino Control Buttons
        private void ConnectUSB_Click(object sender, RoutedEventArgs e)
        {
            if (arduinoStuff == null)
            {
                this.arduinoStuff = new ArduinoStuff(Dispatcher);
                ArduinoGrid.DataContext = this.arduinoStuff;
                PressuresItemsControl.ItemsSource = this.arduinoStuff.Pressures; // cuz I can't figure out how to bind it in XAML
                
            }

            arduinoStuff?.Connect(PortSelector);
        }

        private void FanMinus_Click(object sender, RoutedEventArgs e)
        {
            if (this.arduinoStuff != null && arduinoStuff.Fan <= 20)
            {
                arduinoStuff.Fan = 0;
            } else
            {
                arduinoStuff.Fan -= 5;
            }
        }

        private void FanPlus_Click(object sender, RoutedEventArgs e)
        {
            if (this.arduinoStuff != null) arduinoStuff.Fan += 5;
        }

        private void NeedlePositionMinus_Click(object sender, RoutedEventArgs e)
        {
            if (this.arduinoStuff != null) arduinoStuff.MoveStepper("NeedlePositionMotor", -10);
        }

        private void NeedlePositionPlus_Click(object sender, RoutedEventArgs e)
        {
            if (this.arduinoStuff != null) arduinoStuff.MoveStepper("NeedlePositionMotor", 10);
        }

        // following two are from Items Control :
        private void PressurePlus_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            // get button's data context so I can see which Pressure it refers to
            // by default it's "object" but I know it's AnalogValue cuz that's the context it inherits from ItemsControl (because I set it)
            AnalogValue context = (AnalogValue)button.DataContext;

            // move the appropriate motor to increase pressure
            arduinoStuff.MoveStepper(context.DisplayName, -stepsPerClick); // clockwise (-) increases pressure
        }

        private void PressureMinus_Click(object sender, RoutedEventArgs e)
        {   // see Plus method for comments
            Button button = (Button)sender;
            AnalogValue context = (AnalogValue)button.DataContext;
            arduinoStuff.MoveStepper(context.DisplayName, stepsPerClick); // ccw(+) decreases pressure
        }
        #endregion



        // command line
        private void CommandLine_KeyDown(object sender, KeyEventArgs e)
        {
            // when "enter" is hit
            if (Key.Return == e.Key)
            {
                // get whatever was typed in text box
                string command = CommandLine.Text;
                // clear text box
                CommandLine.Clear();

                // toggle LED Ring
                if (command.ToLower().Equals("led") && this.arduinoStuff != null)
                {
                    this.arduinoStuff.LEDring = !arduinoStuff.LEDring;
                }
                
                // solenoid ON/OFF
                if (command.ToLower().StartsWith("sol") && this.arduinoStuff != null)
                {
                    if (command.Contains("on"))
                    {   // have to explicitly turn it on
                        this.arduinoStuff.Solenoid = true;
                    }
                    else
                    {   // off by default (for safety)
                        this.arduinoStuff.Solenoid = false;
                    }
                }


            }
        }


    }
}

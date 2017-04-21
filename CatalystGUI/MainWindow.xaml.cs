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
            if (cameraStuff != null) cameraStuff.ExposureTime = (uint)(cameraStuff.ExposureTime * plusMinusMultiplier);
        }

        private void ExposureMinus(object sender, RoutedEventArgs e)
        {
            if (cameraStuff != null) cameraStuff.ExposureTime = (uint)(cameraStuff.ExposureTime / plusMinusMultiplier);
        }

        private void FrameRateMinus(object sender, RoutedEventArgs e)
        {
            if (cameraStuff != null) cameraStuff.FrameRate /= plusMinusMultiplier;
        }

        private void FrameRatePlus(object sender, RoutedEventArgs e)
        {
            if (cameraStuff != null) cameraStuff.FrameRate *= plusMinusMultiplier;
        }

        private void FrameCountMinus(object sender, RoutedEventArgs e)
        {
            if (cameraStuff != null) cameraStuff.FrameCount -= 5;
        }

        private void FrameCountPlus(object sender, RoutedEventArgs e)
        {
            if (cameraStuff != null) cameraStuff.FrameCount += 5;

        }
        #endregion

        #region Buttons: Capture, Live, Stop, Start Cam
        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            //CaptureButton.IsEnabled = false; // don't know how to re-enable through binding
            cameraStuff.Capture();
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
            cameraStuff = new CameraStuff(Dispatcher);
            CameraGrid.DataContext = cameraStuff; // every child in "Grid" gets properties from this object

            StartCamButton.IsEnabled = false;
            LiveButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            CaptureButton.IsEnabled = true;
        }


        #endregion

        #region Buttons: Pressures, Fan, Needle-position +/-
        private void MainPressureMinus_Click(object sender, RoutedEventArgs e)
        {
            arduinoStuff.MoveStepper(0, -10);
        }

        private void MainPressurePlus_Click(object sender, RoutedEventArgs e)
        {
            arduinoStuff.MoveStepper(0, 10);

        }

        private void NeedlePressureMinus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void NeedlePressurePlus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void LiquidPressureMinus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void LiquidPressurePlus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void FanSpeedMinus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void FanSpeedPlus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void NeedlePositionMinus_Click(object sender, RoutedEventArgs e)
        {

        }

        private void NeedlePositionPlus_Click(object sender, RoutedEventArgs e)
        {

        }
        #endregion

        private void ConnectUSB_Click(object sender, RoutedEventArgs e)
        {
            if (arduinoStuff == null)
            {
                arduinoStuff = new ArduinoStuff(Dispatcher);
                ArduinoGrid.DataContext = arduinoStuff;
            }

            arduinoStuff.Connect(PortSelector);
            PortSelector.SelectedIndex = 0;
        }


    }
}

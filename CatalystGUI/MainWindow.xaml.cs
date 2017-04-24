﻿using System;
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
            //cameraStuff.Capture();
            // debug openCV
            cameraStuff.GetImage();
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
                arduinoStuff = new ArduinoStuff(Dispatcher);
                ArduinoGrid.DataContext = arduinoStuff;
                PressuresItemsControl.ItemsSource = arduinoStuff.Pressures; // cuz I can't figure out how to bind it in XAML

            }

            arduinoStuff.Connect(PortSelector);
        }

        private void FanMinus_Click(object sender, RoutedEventArgs e)
        {
            if (arduinoStuff.Fan <= 20)
            {
                arduinoStuff.Fan = 0;
            } else
            {
                arduinoStuff.Fan -= 5;
            }
        }

        private void FanPlus_Click(object sender, RoutedEventArgs e)
        {
            arduinoStuff.Fan += 5;
        }
        #endregion

    }
}

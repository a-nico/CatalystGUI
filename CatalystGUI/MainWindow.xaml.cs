using System;
using System.Collections.Generic;
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

        public MainWindow()
        {
            InitializeComponent();
            cameraStuff = new CameraStuff(Dispatcher);
            CameraGrid.DataContext = cameraStuff; // every child in "Grid" gets properties from this object
            //cameraStuff.GetImage();
        }


        #region Buttons: Exposure, Frame Rate, Count
        // when clicked, the value gets multiplied/divided by this amount:
        private const double plusMinusMultiplier = 1.5;

        private void ExposurePlus(object sender, RoutedEventArgs e)
        {
            cameraStuff.ExposureTime = (uint)(cameraStuff.ExposureTime * plusMinusMultiplier);
        }

        private void ExposureMinus(object sender, RoutedEventArgs e)
        {
            cameraStuff.ExposureTime = (uint)(cameraStuff.ExposureTime / plusMinusMultiplier);
        }

        private void FrameRateMinus(object sender, RoutedEventArgs e)
        {
            cameraStuff.FrameRate /= plusMinusMultiplier;
        }

        private void FrameRatePlus(object sender, RoutedEventArgs e)
        {
            cameraStuff.FrameRate *= plusMinusMultiplier;
        }

        private void FrameCountMinus(object sender, RoutedEventArgs e)
        {
            cameraStuff.FrameCount -= 5;
        }

        private void FrameCountPlus(object sender, RoutedEventArgs e)
        {
            cameraStuff.FrameCount += 5;

        }
        #endregion

        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            //CaptureButton.IsEnabled = false; // don't know how to re-enable through binding
            cameraStuff.Capture();
        }

        private void Live_Click(object sender, RoutedEventArgs e)
        {
            cameraStuff.GetImage();
            //if (!cameraStuff.liveMode)
            //{
            //    // Let live acquisition run on different thread
            //    Task.Run(new Action(cameraStuff.Live));
            //}

        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            cameraStuff.liveMode = false;
        }
    }
}

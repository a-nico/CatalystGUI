using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpinnakerNET;
using SpinnakerNET.GenApi;

namespace CatalystGUI
{
    internal class CameraStuff : INotifyPropertyChanged

    {
        private IManagedCamera currentCamera;
        private ManagedSystem spinnakerSystem; // spinnaker class
        private Dispatcher UIDispatcher; // invokes actions to run on UI thread
        // this is the pic that gets posted on the UI:
        private ImageSource _UIimage;
        public ImageSource UIimage {
            get{ return _UIimage; }
            set
            {
                _UIimage = value;
                NotifyPropertyChanged("image");
            }

        }

        public CameraStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;
            InitializeCamera();
        }

        public void SetTestImage()
        {
            UIimage = new BitmapImage(new Uri(@"C:\Users\Bubble\Pictures\BitmapSmiley.bmp"));
        }

        // Camera initialization stuff:
        public void InitializeCamera()
        {
            spinnakerSystem = new ManagedSystem();
            // get list of cams plugged in:
            {
                List<IManagedCamera> camList = spinnakerSystem.GetCameras();
                if (0 == camList.Count) System.Windows.MessageBox.Show("No camera connected.");
                else currentCamera = camList[0]; // get the first one
            } // camlist is garbage collected
            currentCamera.Init(); // don't know what this does
        }

        #region PropertyChanged stuff
        // call this method invokes event to update UI elements which use Binding
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion

        // for now gets one image and puts it on UI using binding
        // should be made into a Task
        public void GetImage()
        {
            SetAcqusitionMode(AqMode.Continuous); // maybe allow client to call this class

            currentCamera.BeginAcquisition(); // need to start this every time
            currentCamera.GetNextImage();
            currentCamera.GetNextImage();
            IManagedImage rawImage = currentCamera.GetNextImage();
            currentCamera.EndAcquisition(); // need to end after every run

            // convert raw image to bitmap, then set the class property for the UI
            UIimage = ConvertRawToBitmapSource(rawImage);

        }

        // convert raw image (IManagedImage) to BitmapSource
        private BitmapSource ConvertRawToBitmapSource(IManagedImage rawImage)
        {
            // convert and copy raw bytes into compatible image type
            using (IManagedImage convertedImage = rawImage.Convert(PixelFormatEnums.Mono8))
            {
                byte[] bytes = convertedImage.ManagedData;

                PixelFormat format = PixelFormats.Gray8;

                return BitmapSource.Create((int)rawImage.Width,
                        (int)rawImage.Height,
                        96d,
                        96d,
                        format,
                        null,
                        bytes,
                        ((int)rawImage.Width * format.BitsPerPixel + 7) / 8
                    );
            }
        }

        #region Acquisition Mode
        public enum AqMode {Single, Multi, Continuous }; // made this for kicks
        public void SetAcqusitionMode(AqMode mode)
        {
            switch (mode)
            {
                case AqMode.Continuous:
                {
                    // get the handle on the properties (called "nodes")
                    INodeMap nodeMap = this.currentCamera.GetNodeMap();
                    // Retrieve enumeration node from nodemap
                    IEnum iAcquisitionMode = nodeMap.GetNode<IEnum>("AcquisitionMode");
                    // Retrieve entry node from enumeration node
                    IEnumEntry iAcquisitionModeContinuous = iAcquisitionMode.GetEntryByName("Continuous");
                    //IEnumEntry iAcquisitionModeSingle = iAcquisitionMode.GetEntryByName("SingleFrame");
                    // Set symbolic from entry node as new value for enumeration node (no idea wtf this is necessary)
                    iAcquisitionMode.Value = iAcquisitionModeContinuous.Symbolic;
                    break;
                }
                case AqMode.Single:
                    break; // to do
                case AqMode.Multi:
                    break; // to do

            }
        }
        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpinnakerNET;
using SpinnakerNET.GenApi;

namespace CatalystGUI
{
    // TO DO: implement IDisposable interface, DeInit() camera, clean up everything
    internal class CameraStuff : INotifyPropertyChanged
    {
        #region Misc. Class Fields
        private IManagedCamera currentCam; // the blackfly
        private ManagedSystem spinnakerSystem; // spinnaker class
        private Dispatcher UIDispatcher; // invokes actions to run on UI thread
        // this is the pic that gets posted on the UI:
        private ImageSource _UIimage; public ImageSource UIimage
        {
            get { return _UIimage; }
            set
            {
                _UIimage = value;
                NotifyPropertyChanged("UIimage");
            }

        }
        public bool liveMode; // live mode or frame capture on UI
        # endregion

        public CameraStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;
            InitializeCamera();
        }

        public void InitializeCamera()
        { // call only once
            spinnakerSystem = new ManagedSystem();
            // get list of cams plugged in:
            {
                List<IManagedCamera> camList = spinnakerSystem.GetCameras();
                if (0 == camList.Count) System.Windows.MessageBox.Show("No camera connected.");
                else currentCam = camList[0]; // get the first one
            } // camlist is garbage collected
            currentCam.Init(); // don't know what this does
        }

        // Called when Live button is clicked. Should run as task
        public void Live()
        {
            SetAcqusitionMode(AcquisitionMode.Continuous, 0);
            currentCam.BeginAcquisition();
            liveMode = true;

            while (liveMode)
            {
                UIimage = ConvertRawToBitmapSource(currentCam.GetNextImage());
            }

            currentCam.EndAcquisition();
        }

        // gets called when "Capture" UI button is clicked
        public ObservableCollection<ImageSource> ImageSourceFrames { get; set; }
        public void Capture()
        {
            SetAcqusitionMode(AcquisitionMode.Multi, FrameCount);
            currentCam.BeginAcquisition();

            // grab image from camera, convert to ImageSource, add to collection which is Bind to listbox
            for (int k = 0; k < FrameCount; k++)
            {
                ImageSourceFrames.Add(ConvertRawToBitmapSource(currentCam.GetNextImage()));
            }
            UIimage = ImageSourceFrames[0]; // put first one on screen
            currentCam.EndAcquisition();
        }

        // for now gets one image and puts it on UI using binding
        // should be made into a Task
        public void GetImage()
        {
            uint numFrames = 4;
            SetAcqusitionMode(AcquisitionMode.Multi, numFrames); // maybe allow client to call this method

            currentCam.BeginAcquisition(); // need to start this every time
            IManagedImage[] rawImage = new IManagedImage[numFrames];
            for (int k = 0; k < numFrames; k++)
            {
                rawImage[k] = currentCam.GetNextImage();
                //rawImage[k].Save(String.Format("C:\\Users\\Bubble\\Pictures\\Raw{0}.bmp", k)); // don't need to convert to Mono8
                //UIimage = ConvertRawToBitmapSource(rawImage[k]); // convert raw image to bitmap, then set the class property for the UI
            }

            // get time stamps
            //long[] timeStamps = new long[numFrames];
            ulong t0 = rawImage[0].TimeStamp;
            for (int k = 0; k < numFrames; k++)
            {
                Console.WriteLine(rawImage[k].TimeStamp - t0);
                //rawImage[k].Save(String.Format("C:\\Users\\Bubble\\Pictures\\Raw{0}.bmp", k)); // don't need to convert to Mono8
                
                //UIimage = ConvertRawToBitmapSource(rawImage[k]); // convert raw image to bitmap, then set the class property for the UI
            }

            currentCam.EndAcquisition(); // end AFTER messing with IManagedImage rawimage or else throws that werid corrupt memory exception

        }

        #region Acquisition Mode
        public enum AcquisitionMode {Single, Multi, Continuous }; // made this for kicks
        public void SetAcqusitionMode(AcquisitionMode mode, uint numFrames)
        {
            // get the handle on the properties (called "nodes")
            INodeMap nodeMap = this.currentCam.GetNodeMap();
            // Retrieve enumeration node from nodemap
            IEnum iAcquisitionMode = nodeMap.GetNode<IEnum>("AcquisitionMode");

            switch (mode)
            {
                case AcquisitionMode.Continuous:
                {
                    // Retrieve entry node from enumeration node
                    IEnumEntry iAqContinuous = iAcquisitionMode.GetEntryByName("Continuous");
                    // Set symbolic from entry node as new value for enumeration node (no idea wtf this is necessary)
                    iAcquisitionMode.Value = iAqContinuous.Symbolic;
                    break;
                }
                case AcquisitionMode.Single:
                {
                    IEnumEntry iAqSingle = iAcquisitionMode.GetEntryByName("SingleFrame");
                    iAcquisitionMode.Value = iAqSingle.Symbolic;
                    break;
                }
                case AcquisitionMode.Multi:
                {
                    IEnumEntry iAqMultiFrame = iAcquisitionMode.GetEntryByName("MultiFrame");
                    iAcquisitionMode.Value = iAqMultiFrame.Symbolic;
                    // set burst
                    IInteger frameCount = nodeMap.GetNode<IInteger>("AcquisitionFrameCount");
                    frameCount.Value = numFrames;
                    break; // to do
                }

            }
        }
        #endregion

        #region Exposure Time
        public uint ExposureTime
        {
            get
            {
                if (currentCam != null) return (uint)currentCam.ExposureTime.Value;
                return 0;
            }
            set
            {
                SetExposure(value); // when loses focus, sets camera to what user inputed
                NotifyPropertyChanged("ExposureTime"); // update UI box (calls get)
            }
        }
        public void SetExposure(uint micros)
        {
            try
            {
                currentCam.ExposureAuto.Value = ExposureAutoEnums.Off.ToString();
                // set exposure, make sure it's within limits
                if (micros > currentCam.ExposureTime.Max) // ~30 sec for blackfly
                {
                    currentCam.ExposureTime.Value = currentCam.ExposureTime.Max;
                }
                else if (micros < currentCam.ExposureTime.Min) // ~6 micros for blackfly
                {
                    currentCam.ExposureTime.Value = currentCam.ExposureTime.Min;
                }
                else
                {
                    currentCam.ExposureTime.Value = micros;
                }
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show("Exception thrown in SetExposure(uint micros) method. Exception message: " + e.Message);
            }
        }
        #endregion

        #region Frame Rate
        public double FrameRate
        {
            get // in future maybe do private/public pair to avoid long decimals
            {
                if (currentCam != null) return (uint)currentCam.AcquisitionFrameRate.Value;
                return 0.0;
            }
            set
            {
                SetFramerate(value); // when loses focus, sets camera to what user inputed
                NotifyPropertyChanged("FrameRate"); // update UI box (calls get)
            }
        }
        public void SetFramerate(double Hz)
        {
            try
            {
                // frame rate enable property (otherwise won't let you change it)
                IBool iAqFrameRateEnable = currentCam.GetNodeMap().GetNode<IBool>("AcquisitionFrameRateEnable");
                iAqFrameRateEnable.Value = true;

                // set frame rate, make sure it's within limits 
                if (Hz > currentCam.AcquisitionFrameRate.Max) // ~170 fps for blackfly
                {
                    currentCam.AcquisitionFrameRate.Value = currentCam.AcquisitionFrameRate.Max;
                }
                else if (Hz < currentCam.AcquisitionFrameRate.Min)
                {
                    currentCam.AcquisitionFrameRate.Value = currentCam.AcquisitionFrameRate.Min;
                }
                else
                {
                    currentCam.AcquisitionFrameRate.Value = Hz;

                }
            } catch (Exception e)
            {
                System.Windows.MessageBox.Show("Exception throw in SetFramerate(double Hz) method. Exception message: " + e.Message);

            }
        }
        #endregion

        #region Frame Count
        private uint frameCount;
        public uint FrameCount
        {
            get { return frameCount; }
            set
            {
                frameCount = value;
                NotifyPropertyChanged("FrameCount");
            }
        }
        #endregion

        // convert raw image (IManagedImage) to BitmapSource
        private BitmapSource ConvertRawToBitmapSource(IManagedImage rawImage)
        {
            // convert and copy raw bytes into compatible image type
            using (IManagedImage convertedImage = rawImage.Convert(PixelFormatEnums.Mono8))
            {
                byte[] bytes = convertedImage.ManagedData;

                System.Windows.Media.PixelFormat format = System.Windows.Media.PixelFormats.Gray8;

                return BitmapSource.Create(
                        (int)rawImage.Width,
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

        #region PropertyChanged stuff
        // call this method invokes event to update UI elements which use Binding
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion

    }
}
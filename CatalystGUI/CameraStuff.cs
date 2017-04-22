using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpinnakerNET;
using SpinnakerNET.GenApi;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;
using System.Drawing;

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
        # endregion

        public CameraStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;

            // create collecton on UI thread so I won't have any problems with scope BS
            UIDispatcher.BeginInvoke( new Action( () => 
            {
                ImageSourceFrames = new ObservableCollection<ImageSource>();
            }));

            RawImages = new List<IManagedImage>();
        }

        public bool InitializeCamera()
        { // call only once
            spinnakerSystem = new ManagedSystem();
            // get list of cams plugged in:
            {
                List<IManagedCamera> camList = spinnakerSystem.GetCameras();
                if (0 == camList.Count)
                {
                    System.Windows.MessageBox.Show("No camera connected.");
                    return false;
                }
                else currentCam = camList[0]; // get the first one
            } // camlist is garbage collected
            currentCam.Init(); // don't know what this does
            return true;
        }

        // Called when Live button is clicked. Should run as task
        public bool liveMode; // live mode or frame capture on UI
        public void Live()
        {
            SetAcqusitionMode(AcquisitionMode.Continuous, 0);
            currentCam.BeginAcquisition();
            liveMode = true;

            Task.Run(() =>
            {
                while (liveMode)
                {
                    // if don't use "using" the frame freezes
                    using (var rawImage = currentCam.GetNextImage())
                    {
                        // updating UI image has to be done on UI thread. Use Dispatcher
                        UIDispatcher.Invoke(new Action(() =>
                        {
                            UIimage = ConvertRawToBitmapSource(rawImage);
                        }));
                    }
                }

                currentCam.EndAcquisition();
            });
        }


        // gets called when "Capture" UI button is clicked
        private ObservableCollection<ImageSource> _imageSourceFrames;
        public ObservableCollection<ImageSource> ImageSourceFrames
        {   //"observable collection" automatically notifies UI when changed
            get
            {
                return _imageSourceFrames;
            }

            set
            {
                _imageSourceFrames = value;
                NotifyPropertyChanged("ImageSourceFrames");
            }
        }
        List<IManagedImage> RawImages; // keeps raws
        public void Capture()
        {
            ImageSourceFrames.Clear();
            Task.Run(new Action(() =>
            {
                 
                 SetAcqusitionMode(AcquisitionMode.Multi, FrameCount);
                 currentCam.BeginAcquisition();

                    // grab image from camera, convert to ImageSource, add to collection which is Bind to listbox
                    for (uint k = 0; k < FrameCount; k++)
                    {
                        using (var rawImage = currentCam.GetNextImage())
                        {
                        RawImages.Add(rawImage);
                        UIDispatcher.Invoke( () => 
                            { // needs to be done with Dispatcher or else it doesn't get a chance to updat UI cuz this task hogs the thread
                                ImageSourceFrames.Add(ConvertRawToBitmapSource(rawImage));
                                if (ImageSourceFrames.Count == 1)  UIimage = ImageSourceFrames[0]; // put first one on screen
                            });
                        }

                        // doesn't work in real time: 
                        //NotifyPropertyChanged("ImageSourceFrames"); // if I wanna see them come in real time

                    }
                 currentCam.EndAcquisition();
             }));
        }

        public void GetImage()
        {
            IManagedImage rawImage = null;
            SetAcqusitionMode(AcquisitionMode.Single, 0); // maybe allow client to call this method
            currentCam.BeginAcquisition(); // need to start this every time
            rawImage = currentCam.GetNextImage().Convert(PixelFormatEnums.Mono8);

            // Image OpenCV type of image (matrix or something)
            Image <Gray, Byte> cvImage = new Image<Gray, byte>((int)rawImage.Width, (int)rawImage.Height);

            cvImage.Bytes = rawImage.ManagedData; // ManagedData is byte[] of rawImage


            Point pt1 = new Point(300, 300);
            Point pt2 = new Point(800, 800);
            LineSegment2D line = new LineSegment2D(pt1, pt2);
            cvImage.Draw(line, new Gray(1), 3); // changes bytes in cvImage
            //cvImage.Save( file path );            
            UIimage = ConvertBytesToBitmapSource(cvImage.Bytes, rawImage.Width, rawImage.Height);

            // now back to UI thread no?
            //rawImage.Save("C:/afterTask.bmp");
            //ImageSource thing = new ImageSource(convertedImage);
            currentCam.EndAcquisition();
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
                    break;
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
            get
            {
                return frameCount < 2 ? 2 : frameCount; // can't be <2 for MultiFrame mode
            }
            set
            {
                frameCount = value;
                NotifyPropertyChanged("FrameCount");
            }
        }
        #endregion

        BitmapSource ConvertBytesToBitmapSource(byte[] imageBytes, uint width, uint height)
        {
            System.Windows.Media.PixelFormat format = System.Windows.Media.PixelFormats.Gray8;

            return BitmapSource.Create(
                       (int)width,
                       (int)height,
                       96d,
                       96d,
                       format,
                       null,
                       imageBytes,
                       ((int)width * format.BitsPerPixel + 7) / 8
                       );

        }

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
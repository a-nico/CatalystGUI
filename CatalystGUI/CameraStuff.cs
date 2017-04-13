using System.Collections.Generic;
using System.Windows.Threading;
using SpinnakerNET;

namespace CatalystGUI
{
    internal class CameraStuff
    {
        private IManagedCamera currentCamera;
        private ManagedSystem spinnakerSystem; // spinnaker class
        private Dispatcher UIDispatcher; // invokes actions to run on UI thread

        public CameraStuff(Dispatcher UIDispatcher)
        {
            this.UIDispatcher = UIDispatcher;

            // Camera initialization stuff:
            spinnakerSystem = new ManagedSystem();
            // get list of cams plugged in:
            List<IManagedCamera> camList = spinnakerSystem.GetCameras();
            if (0 == camList.Count)     System.Windows.MessageBox.Show("No camera connected.");
            else currentCamera = camList[0]; // get the first one

        }
    }
}
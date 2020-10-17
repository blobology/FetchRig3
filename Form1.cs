using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpinnakerNET;
using SpinnakerNET.GenApi;
using Emgu.CV;
using Emgu.CV.UI;
using System.Windows.Forms;
using Emgu.CV.Structure;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Drawing.Text;
using SharpDX;

namespace FetchRig3
{
    public partial class Form1 : Form
    {
        private ManagedSystem system;
        private IList<IManagedCamera> managedCameras;
        private int nCameras;
        private Util.OryxSetupInfo[] oryxSetupInfos;
        private ConcurrentQueue<ButtonCommands>[] camControlMessageQueues;
        private string[] sessionPaths;
        private Thread[] oryxCameraThreads;

        private int streamEnqueueDutyCycle = 2;
        private int streamDisplayDutyCycle = 2;

        private ConcurrentQueue<RawMat>[] rawImageQueues;
        private Thread[] processRawImageThreads;

        private ConcurrentQueue<Tuple<RawMat, Mat>>[] processedImageQueues;
        private Thread mergeStreamsThread;

        private ConcurrentQueue<Tuple<RawMat, Mat>> displayQueue;


        public Form1()
        {
            InitializeComponent();

            // initialize multi-camera system:
            system = new ManagedSystem();

            // Print current Spinnaker library version info:
            LibraryVersion spinVersion = system.GetLibraryVersion();
            Console.WriteLine(
                "Spinnaker library version: {0}.{1}.{2}.{3}\n\n",
                spinVersion.major,
                spinVersion.minor,
                spinVersion.type,
                spinVersion.build);

            // Find all Flir cameras on the system:
            managedCameras = system.GetCameras();
            nCameras = managedCameras.Count;

            // Finish and dispose system if no cameres are detected:
            if (nCameras != 2)
            {
                managedCameras.Clear();
                system.Dispose();
                Console.WriteLine("{0} cameras detected. This application supports exactly 2 cameras. System disposed", nCameras.ToString());
            }

            // Create or select folder to write video data:
            sessionPaths = Util.SetDataWritePaths(animalName: Util.AnimalName.Charlie, nCameras: 2);

            // Initialize OryxSetupInfo Object to pass to camera constructors upon initialization:
            oryxSetupInfos = new Util.OryxSetupInfo[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                oryxSetupInfos[i] = new Util.OryxSetupInfo();

            }

            bool areCamerasSharingSettings = true;
            if (areCamerasSharingSettings)
            {
                Console.WriteLine("These settings will be loaded on both cameras:");
                oryxSetupInfos[0].PrintSettingsToLoad();
                Console.WriteLine("\n\n");
            }

            // Initialize camera control message queues to control cameras from XBox controller:
            camControlMessageQueues = new ConcurrentQueue<ButtonCommands>[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                camControlMessageQueues[i] = new ConcurrentQueue<ButtonCommands>();
            }

            // Initialize raw image stream queues for each camera:
            rawImageQueues = new ConcurrentQueue<RawMat>[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                rawImageQueues[i] = new ConcurrentQueue<RawMat>();
            }

            // Initialize thread to process raw image stream for each camera:
            processRawImageThreads = new Thread[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                int _i = i;
                Size imSize = new Size(width: oryxSetupInfos[i].streamFramesize.Width, height: oryxSetupInfos[i].streamFramesize.Height);
                processRawImageThreads[i] = new Thread(() => ProcessRawImageStreamThreadInit(rawImageQueues[_i], rawImageSize: imSize));
                processRawImageThreads[i].IsBackground = false;
                processRawImageThreads[i].Start();
            }

            // Initialize queues to pass processed image Tuples to MergeProcessedImageStreamsThread
            processedImageQueues = new ConcurrentQueue<Tuple<RawMat, Mat>>[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                processedImageQueues[i] = new ConcurrentQueue<Tuple<RawMat, Mat>>();
            }



            // Initialize thread to merge processedImageQueues for display:
            displayQueue = new ConcurrentQueue<Tuple<RawMat, Mat>>();



            // Initialize XBox Controller
            XBoxController xBoxController = new XBoxController(mainForm: this, camControlMessageQueues: camControlMessageQueues);

        }


        public void ProcessRawImageStreamThreadInit(ConcurrentQueue<RawMat> rawImageQueue, Size rawImageSize)
        {
            
        }

        public void MergeProcessedImageStreamsThreadInit()
        {

        }

        public void ExitButtonPressed()
        {
            Console.WriteLine("Exit Button was pressed");
        }

    }
}

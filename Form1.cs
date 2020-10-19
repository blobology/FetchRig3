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
using System.Runtime.InteropServices;

namespace FetchRig3
{
    public partial class Form1 : Form
    {
        private ManagedSystem system;
        private IList<IManagedCamera> managedCameras;
        private int nCameras;
        private Util.OryxSetupInfo[] oryxSetupInfos;
        private ConcurrentQueue<ButtonCommands>[] camControlMessageQueues;
        public ConcurrentQueue<ButtonCommands> processingThreadMessageQueue;
        private XBoxController xBoxController;
        private string[] sessionPaths;
        private Thread[] oryxCameraThreads;

        private int controllerQueryCtr = 0;
        private int streamEnqueueDutyCycle = 2;
        private int streamDisplayDutyCycle = 2;

        private Size streamFramesize;
        private Size displayFramesize;

        private ConcurrentQueue<RawMat>[] streamQueue0;
        private Thread mergeStreamsThread;

        private ConcurrentQueue<Tuple<byte[], Mat>> displayQueue;
        private Image<Gray, byte> displayImage;

        private System.Windows.Forms.Timer displayTimer;

        public bool isStreaming { get; set; }

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

            // Initialize XBox Controller
            xBoxController = new XBoxController(mainForm: this, camControlMessageQueues: camControlMessageQueues);

            // Initialize queue to connect output from each camera to a thread to merge camera streams:
            streamQueue0 = new ConcurrentQueue<RawMat>[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                streamQueue0[i] = new ConcurrentQueue<RawMat>();
            }

            // Open each camera on its own thread.
            oryxCameraThreads = new Thread[nCameras];
            for (int i = 0; i < nCameras; i++)
            {
                int _i = i;
                string _sessionPath = string.Copy(sessionPaths[i]);

                oryxCameraThreads[i] = new Thread(() => new OryxCamera(camNumber: _i, managedCamera: managedCameras[_i], camControlMessageQueue: camControlMessageQueues[_i],
                    streamOutputQueue: streamQueue0[_i], setupInfo: oryxSetupInfos[_i], sessionPath: _sessionPath));
                oryxCameraThreads[i].IsBackground = false;
                oryxCameraThreads[i].Priority = ThreadPriority.Highest;
                oryxCameraThreads[i].Start();
            }

            // Initialize queue to send combined images to display form:
            displayQueue = new ConcurrentQueue<Tuple<byte[], Mat>>();

            // Initialize Size of camera stream output image and merged image for display:
            streamFramesize = new Size(width: oryxSetupInfos[0].streamFramesize.Width, height: oryxSetupInfos[0].streamFramesize.Height);
            displayFramesize = new Size(width: streamFramesize.Width, height: streamFramesize.Height * 2);

            // Initialize thread to merge camera stream data into a single byte array:
            Size _inputImageSize = new Size(width: streamFramesize.Width, height: streamFramesize.Height);
            processingThreadMessageQueue = new ConcurrentQueue<ButtonCommands>();
            mergeStreamsThread = new Thread(() => MergeStreamsThreadInit(inputQueues: streamQueue0, outputQueue: displayQueue, messageQueue: processingThreadMessageQueue, inputImgSize: _inputImageSize));
            mergeStreamsThread.IsBackground = true;
            mergeStreamsThread.Priority = ThreadPriority.Highest;
            mergeStreamsThread.Start();

            // Initialize streaming state
            isStreaming = false;

            // Initialize Timer:
            displayTimer = new System.Windows.Forms.Timer();
            displayTimer.Interval = 5;
            displayTimer.Tick += DisplayTimerEventProcessor;
            displayTimer.Enabled = true;
        }

        public void DisplayTimerEventProcessor(Object sender, EventArgs e)
        {
            xBoxController.controllerState.Update();
            bool isDequeueSuccess;

            while (isStreaming)
            {
                isDequeueSuccess = displayQueue.TryDequeue(out Tuple<byte[], Mat> result);
                if (isDequeueSuccess)
                {
                    displayTimer.Enabled = false;
                    IntPtr unmanagedPointerItem1 = Marshal.AllocHGlobal(result.Item1.Length);
                    Marshal.Copy(source: result.Item1, startIndex: 0, destination: unmanagedPointerItem1, length: result.Item1.Length);
                    Mat matItem1 = new Mat(size: displayFramesize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1, data: unmanagedPointerItem1, step: displayFramesize.Width);

                    mergedImgBox0.Image = matItem1;
                    matItem1.Dispose();
                    Marshal.FreeHGlobal(unmanagedPointerItem1);

                    mergedImgBox1.Image = result.Item2;
                    result.Item2.Dispose();

                    displayTimer.Enabled = true;
                    return;
                }
            }
        }

        public void MergeStreamsThreadInit(ConcurrentQueue<RawMat>[] inputQueues, ConcurrentQueue<Tuple<byte[], Mat>> outputQueue, ConcurrentQueue<ButtonCommands> messageQueue, Size inputImgSize)
        {
            const int nCameras = 2;
            Size mergeImgSize = new Size(width: inputImgSize.Width, height: inputImgSize.Height * 2);

            int inputImgSizeInBytes = inputImgSize.Width * inputImgSize.Height;
            int mergeImgSizeInBytes = mergeImgSize.Width * mergeImgSize.Height;

            bool go = true;
            ProcessingLoopState loopState = ProcessingLoopState.WaitingForMessages;

            bool resetBackground = true;
            bool[] isDequeueSuccess = new bool[nCameras];

            Mat background = new Mat(size: mergeImgSize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1);

            int frameCtr = 0;

            bool isMessageDequeueSuccess;
            

            while (go)
            {
                isMessageDequeueSuccess = messageQueue.TryDequeue(out ButtonCommands result);
                if (isMessageDequeueSuccess)
                {
                    if (result == ButtonCommands.BeginStreaming)
                    {
                        loopState = ProcessingLoopState.WaitingForMessagesWhileProcessing;
                    }
                    else if (result == ButtonCommands.EndStreaming)
                    {
                        loopState = ProcessingLoopState.WaitingForMessages;
                    }
                    else if (result == ButtonCommands.ResetBackgroundImage)
                    {
                        resetBackground = true;
                    }
                    else if (result == ButtonCommands.Exit)
                    {
                        return;
                    }
                }

                if (loopState == ProcessingLoopState.WaitingForMessages)
                {
                    Thread.Sleep(100);
                    continue;
                }

                while (!isDequeueSuccess[0])
                {
                    isDequeueSuccess[0] = inputQueues[0].TryDequeue(out RawMat result0);
                    if (!isDequeueSuccess[0])
                    {
                        continue;
                    }

                    while (!isDequeueSuccess[1])
                    {
                        isDequeueSuccess[1] = inputQueues[1].TryDequeue(out RawMat result1);
                        if (!isDequeueSuccess[1])
                        {
                            continue;
                        }

                        byte[] outputItem1 = new byte[mergeImgSizeInBytes];
                        Marshal.Copy(source: result0.rawMat.DataPointer, destination: outputItem1, startIndex: 0, length: inputImgSizeInBytes);
                        Marshal.Copy(source: result1.rawMat.DataPointer, destination: outputItem1, startIndex: inputImgSizeInBytes, length: inputImgSizeInBytes);

                        Mat processedMat = new Mat(size: mergeImgSize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1);
                        Marshal.Copy(source: outputItem1, startIndex: 0, destination: processedMat.DataPointer, length: mergeImgSizeInBytes);

                        if (resetBackground)
                        {
                            processedMat.CopyTo(background);
                            resetBackground = false;
                        }

                        ProcessMergedImage(ref processedMat);

                        

                        Tuple<byte[], Mat> output = GetOutput(item1: outputItem1, item2: processedMat);

                        outputQueue.Enqueue(item: output);
                        result0.rawMat.Dispose();
                        result1.rawMat.Dispose();

                        isDequeueSuccess[0] = false;
                        isDequeueSuccess[1] = false;
                        break;
                    }
                    break;
                }
            }

            void ProcessMergedImage(ref Mat mat)
            {
                CvInvoke.AbsDiff(src1: mat, src2: background, dst: mat);
                CvInvoke.Threshold(src: mat, dst: mat, threshold: 15, maxValue: 255, thresholdType: Emgu.CV.CvEnum.ThresholdType.Binary);
            }

            Tuple<byte[], Mat> GetOutput(byte[] item1, Mat item2)
            {
                return Tuple.Create(item1, item2);
            }

        }

        public void ExitButtonPressed()
        {
            Console.WriteLine("Exit Button was pressed");
            xBoxController.controllerState.soundThread.Join();
            Console.WriteLine("soundThread has joined");

            for (int i = 0; i < nCameras; i++)
            {
                oryxCameraThreads[i].Join();
                Console.WriteLine("camera number {0} thread has joined", i.ToString());
            }
        }
    }
}

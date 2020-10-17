using System;
using System.Collections.Generic;
using SpinnakerNET;
using SpinnakerNET.GenApi;
using System.Threading;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.UI;
using System.Windows.Forms;
using Emgu.CV.Structure;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Linq.Expressions;
using Emgu.CV.Ocl;
using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;
using System.Drawing.Text;
using System.Security.Cryptography.X509Certificates;
using Emgu.CV.Stitching;
using Emgu.CV.Cuda;
using Emgu.CV.Util;

namespace FetchRig3
{
    public class OryxCamera
    {
        readonly int camNumber;
        public string settingsFileName;
        private string sessionPath;
        private string encodePipeName;
        private Size frameSize;
        private Size streamFramesize;
        private ConcurrentQueue<ButtonCommands> camControlMessageQueue;

        // These fields will be accessed by an OryxCameraSettings object to set and save camera settings.
        public IManagedCamera managedCamera;
        public Util.OryxSetupInfo setupInfo;
        public INodeMap nodeMapTLDevice;
        public INodeMap nodeMapTLStream;
        public INodeMap nodeMap;

        readonly Size streamFrameSize;
        readonly int streamEnqueueDutyCycle;

        public OryxCamera(int camNumber, IManagedCamera managedCamera, ConcurrentQueue<ButtonCommands> camControlMessageQueue, Util.OryxSetupInfo setupInfo, string sessionPath)
        {
            this.camNumber = camNumber;
            this.managedCamera = managedCamera;
            this.camControlMessageQueue = camControlMessageQueue;
            this.setupInfo = setupInfo;
            this.sessionPath = sessionPath;
            settingsFileName = this.sessionPath + @"\" + "cam" + this.camNumber.ToString() + @"_cameraSettings.txt";
            encodePipeName = "ffpipe" + camNumber.ToString();
            frameSize = new Size(width: setupInfo.maxFramesize.Width, height: setupInfo.maxFramesize.Height);

            streamFrameSize = this.setupInfo.streamFramesize;
            streamEnqueueDutyCycle = 2;

            GetNodeMapsAndInitialize();
            LoadCameraSettings();
            DisplayLoop();
        }

        private void GetNodeMapsAndInitialize()
        {
            nodeMapTLDevice = managedCamera.GetTLDeviceNodeMap();
            nodeMapTLStream = managedCamera.GetTLStreamNodeMap();
            managedCamera.Init();
            nodeMap = managedCamera.GetNodeMap();
            Console.WriteLine("Camera number {0} opened and initialized on thread {1}", camNumber, Thread.CurrentThread.ManagedThreadId);
        }

        private void LoadCameraSettings()
        {
            OryxCameraSettings oryxCameraSettings = new OryxCameraSettings(this);
            oryxCameraSettings.SaveSettings(_printSettings: false);
        }

        public class FFProcess
        {
            private string _pipeName;
            private string _videoFileName;
            private string inputArgs;
            private string outputArgs;
            private string fullArgs;
            public Process process;

            public FFProcess(string pipeName, string videoFileName)
            {
                _pipeName = @"\\.\pipe\" + pipeName;
                _videoFileName = videoFileName;
                inputArgs = "-nostats -y -vsync 0 -f rawvideo -s 3208x2200 -pix_fmt gray -framerate 100 -i " + _pipeName + " -an";
                outputArgs = "-gpu 0 -vcodec h264_nvenc -r 100 -preset fast -qp 20 " + _videoFileName;
                fullArgs = inputArgs + " " + outputArgs;
            }

            public void OpenWithStartInfo()
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "ffmpeg.exe";
                startInfo.Arguments = fullArgs;
                startInfo.CreateNoWindow = true;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardInput = true;
                process = Process.Start(startInfo);
            }
        }

        private void DisplayLoop()
        {
            // Setup StreamDisplayQueue and StreamDisplayThread:
            ConcurrentQueue<RawMat> streamQueue = new ConcurrentQueue<RawMat>();
            Thread streamProcessingThread = null;

            // Setup EncodeQueue and EncodeThread
            ConcurrentQueue<byte[]> encodeQueue = new ConcurrentQueue<byte[]>();
            Thread encodeThread = null;

            // Use enum to indicate loop state and which DisplayLoop block to execute
            DisplayLoopState loopState = DisplayLoopState.WaitingForMessagesWhileNotStreaming;

            int streamImageCtr = 0;
            List<long> skipEvents = new List<long>();
            int rawImageSizeInBytes = frameSize.Width * frameSize.Height;

            bool isMessageDequeueSuccess;
            bool go = true;

            while (go)
            {
                if (loopState == DisplayLoopState.WaitingForMessagesWhileNotStreaming)
                {
                    isMessageDequeueSuccess = camControlMessageQueue.TryDequeue(out ButtonCommands message);
                    if (isMessageDequeueSuccess)
                    {
                        if (message == ButtonCommands.BeginAcquisition)
                        {
                            Console.WriteLine("{0} message received in WaitForMessagesWhileNotStreaming block on camera {1}. Press BeginStreaming Button after memory allocation complete.", message, camNumber.ToString());
                            managedCamera.BeginAcquisition();
                            continue;
                        }
                        else if (message == ButtonCommands.BeginStreaming)
                        {
                            loopState = DisplayLoopState.BeginStreaming;
                            continue;
                        }
                        else if (message == ButtonCommands.PlayInitiateTrialTone || message == ButtonCommands.PlayRewardTone)
                        {
                            continue;
                        }
                        else if (message == ButtonCommands.Exit)
                        {
                            loopState = DisplayLoopState.Exit;
                            continue;
                        }
                        else
                        {
                            Console.WriteLine("Invalid message ({0}) received in WaitForMessagesWhileNotStreaming on camera {1}", message, camNumber.ToString());
                            continue;
                        }
                    }
                    Thread.Sleep(50);
                    continue;
                }

                else if (loopState == DisplayLoopState.BeginStreaming)
                {
                    loopState = DisplayLoopState.StreamingAndWaitingForMessages;

                    if (!managedCamera.IsStreaming())
                    {
                        managedCamera.BeginAcquisition();
                    }

                    streamImageCtr = 0;

                    using (IManagedImage rawImage = managedCamera.GetNextImage())
                    {
                        streamImageCtr++;
                        long currFrameID = rawImage.ChunkData.FrameID;
                        long currFrameTimestamp = rawImage.ChunkData.Timestamp;

                        Mat streamImageMat = new Mat(rows: frameSize.Height, cols: frameSize.Width, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1, data: rawImage.DataPtr, step: frameSize.Width);
                        Mat streamImageMatResized = new Mat(size: streamFrameSize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1);
                        CvInvoke.Resize(src: streamImageMat, dst: streamImageMatResized, dsize: streamFrameSize, interpolation: Emgu.CV.CvEnum.Inter.Linear);

                        RawMat matWithMetaData = new RawMat(frameID: currFrameID, frameTimestamp: currFrameTimestamp,
                                                                              isNewBackgroundImage: true, closeForm: false, rawMat: streamImageMatResized);

                        streamQueue.Enqueue(matWithMetaData);
                        streamImageMat.Dispose();
                    }
                    continue;
                }

                else if (loopState == DisplayLoopState.StreamingAndWaitingForMessages)
                {
                    try
                    {
                        using (IManagedImage rawImage = managedCamera.GetNextImage())
                        {
                            streamImageCtr++;
                            long currFrameTimestamp = rawImage.ChunkData.Timestamp;
                            long currFrameID = rawImage.ChunkData.FrameID;

                            if (streamImageCtr % streamEnqueueDutyCycle == 0)
                            {
                                Mat streamImageMat = new Mat(rows: frameSize.Height, cols: frameSize.Width, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1, data: rawImage.DataPtr, step: frameSize.Width);
                                Mat streamImageMatResized = new Mat(size: streamFrameSize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1);
                                CvInvoke.Resize(src: streamImageMat, dst: streamImageMatResized, dsize: streamFrameSize, interpolation: Emgu.CV.CvEnum.Inter.Linear);

                                RawMat matWithMetaData = new RawMat(frameID: currFrameID, frameTimestamp: currFrameTimestamp,
                                                                                      isNewBackgroundImage: false, closeForm: false, rawMat: streamImageMatResized);

                                streamQueue.Enqueue(matWithMetaData);
                                streamImageMat.Dispose();
                            }
                        }
                    }
                    catch (SpinnakerException ex)
                    {
                        Console.WriteLine("Error in DisplayLoop block on camera {0}:   {1}", camNumber.ToString(), ex.Message);
                    }

                    if (streamImageCtr % 10 == 0)
                    {
                        isMessageDequeueSuccess = camControlMessageQueue.TryDequeue(out ButtonCommands message);
                        if (isMessageDequeueSuccess)
                        {
                            if (message == ButtonCommands.StartRecording)
                            {
                                loopState = DisplayLoopState.InitiateFFProcessAndRecord;
                                continue;
                            }
                            else if (message == ButtonCommands.EndStreaming)
                            {
                                loopState = DisplayLoopState.EndAcquisition;
                                continue;
                            }
                            else
                            {
                                Console.WriteLine("{0} message invalid: LoopState = Streaming.", message);
                                continue;
                            }
                        }
                    }
                }

                else if (loopState == DisplayLoopState.InitiateFFProcessAndRecord)
                {
                    loopState = DisplayLoopState.StreamingAndRecordingWhileWaitingForMessages;
                    encodeThread = new Thread(() => EncodeThreadInit(_camNumber: camNumber, _encodePipeName: encodePipeName, _sessionPath: sessionPath, _count: 7057600, _encodeQueue: encodeQueue));
                    encodeThread.Start();
                }

                else if (loopState == DisplayLoopState.StreamingAndRecordingWhileWaitingForMessages)
                {
                    try
                    {
                        using (IManagedImage rawImage = managedCamera.GetNextImage())
                        {
                            streamImageCtr++;
                            long currFrameTimestamp = rawImage.ChunkData.Timestamp;
                            long currFrameID = rawImage.ChunkData.FrameID;

                            // Write image to pipe for encoding:
                            byte[] encodeImageCopy = new byte[rawImageSizeInBytes];
                            Marshal.Copy(source: rawImage.DataPtr, destination: encodeImageCopy, startIndex: 0, length: rawImageSizeInBytes);
                            encodeQueue.Enqueue(item: encodeImageCopy);

                            if (streamImageCtr % streamEnqueueDutyCycle == 0)
                            {
                                Mat streamImageMat = new Mat(rows: frameSize.Height, cols: frameSize.Width, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1, data: rawImage.DataPtr, step: frameSize.Width);
                                Mat streamImageMatResized = new Mat(size: streamFrameSize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1);
                                CvInvoke.Resize(src: streamImageMat, dst: streamImageMatResized, dsize: streamFrameSize, interpolation: Emgu.CV.CvEnum.Inter.Linear);

                                RawMat matWithMetaData = new RawMat(frameID: currFrameID, frameTimestamp: currFrameTimestamp,
                                                      isNewBackgroundImage: false, closeForm: false, rawMat: streamImageMatResized);

                                streamQueue.Enqueue(matWithMetaData);
                                streamImageMat.Dispose();
                            }
                        }
                    }
                    catch (SpinnakerException ex)
                    {
                        Console.WriteLine("Error in DisplayLoop block on camera {0}:   {1}", camNumber.ToString(), ex.Message);
                    }

                    if (streamImageCtr % 10 == 0)
                    {
                        isMessageDequeueSuccess = camControlMessageQueue.TryDequeue(out ButtonCommands message);
                        if (isMessageDequeueSuccess)
                        {
                            if (message == ButtonCommands.StopRecording)
                            {
                                loopState = DisplayLoopState.InitiateProcessTermination;
                                continue;
                            }
                            else if (message == ButtonCommands.EndStreaming)
                            {
                                loopState = DisplayLoopState.EndAcquisition;
                                continue;
                            }
                            {
                                Console.WriteLine("{0} message invalid: LoopState = StreamingAndRecording.", message);
                                continue;
                            }
                        }
                    }
                }

                else if (loopState == DisplayLoopState.InitiateProcessTermination)
                {
                    Console.WriteLine("InitiateProcessTermination Block reached. Back to StreamingAndWaitingForMessages.");



                    loopState = DisplayLoopState.StreamingAndWaitingForMessages;
                }

                else if (loopState == DisplayLoopState.EndAcquisition)
                {

                    Mat emptyMat = new Mat(size: streamFrameSize, type: Emgu.CV.CvEnum.DepthType.Cv8U, channels: 1);
                    RawMat matWithMetaData = new RawMat(frameID: 0, frameTimestamp: 0,
                                                      isNewBackgroundImage: false, closeForm: true, rawMat: emptyMat);

                    managedCamera.EndAcquisition();
                    loopState = DisplayLoopState.WaitingForMessagesWhileNotStreaming;
                    Console.WriteLine("Acquisition ended on camera {0}. LoopState = WaitingForMessagesWhileNotStreaming.", camNumber.ToString());
                }

                else if (loopState == DisplayLoopState.Exit)
                {
                    go = false;
                    CloseOryxCamera(Util.CloseCameraMethod.DeInitAndDeviceReset);
                }
            }
        }

        



        private void EncodeThreadInit(int _camNumber, string _encodePipeName, string _sessionPath, int _count, ConcurrentQueue<byte[]> _encodeQueue)
        {
            int camNumber = _camNumber;
            string pipeName = _encodePipeName;
            string sessionPath = _sessionPath;
            int count = _count;
            int nFramesWritten = 0;
            ConcurrentQueue<byte[]> encodeQueue = _encodeQueue;

            NamedPipeServerStream pipe = new NamedPipeServerStream(pipeName: pipeName, direction: PipeDirection.Out, maxNumberOfServerInstances: 1,
                transmissionMode: PipeTransmissionMode.Byte, options: PipeOptions.Asynchronous, inBufferSize: 7057600, outBufferSize: 7057600);

            string videoFileName = sessionPath + @"\" + DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss") + ".mp4";
            FFProcess ffProcess = new FFProcess(pipeName: pipeName, videoFileName: videoFileName);
            ffProcess.OpenWithStartInfo();
            pipe.WaitForConnection();
            Console.WriteLine("Pipe connected to ffProcess for camera {0} on thread {1}", camNumber.ToString(), Thread.CurrentThread.ManagedThreadId.ToString());

            bool goEncode = true;
            bool isDequeueSuccess;

            while (goEncode)
            {
                isDequeueSuccess = encodeQueue.TryDequeue(out byte[] result);
                if (isDequeueSuccess)
                {
                    pipe.Write(buffer: result, offset: 0, count: count);
                    nFramesWritten++;

                    if (nFramesWritten % 1000 == 0)
                    {
                        Console.WriteLine("nFramesWritten for cam number {0}:   {1}", camNumber.ToString(), nFramesWritten.ToString());
                    }
                }
                else
                {
                    Thread.Sleep(10);
                    if (encodeQueue.Count == 0)
                    {
                        goEncode = false;
                    }
                }
            }

            pipe.FlushAsync();
            pipe.WaitForPipeDrain();
            pipe.Close();
            ffProcess.process.WaitForExit();
            ffProcess.process.Close();
            Console.WriteLine("EncodeThread for camera {0}: Pipe flushed, drained, and closed. FFProcess exited and closed.", camNumber.ToString());

            if (camNumber == 0)
            {
                GC.Collect();
                Console.WriteLine("Garbage collected on camera 1 EncodeThread. Now exiting.");
            }
        }

        private void CloseOryxCamera(Util.CloseCameraMethod closeMethod)
        {
            if (!managedCamera.IsInitialized())
            {
                Console.WriteLine("Camera number {0} not initialized. Cannot execute DeviceReset or FactoryReset command", camNumber.ToString());
                return;
            }

            if (managedCamera.IsStreaming())
            {
                managedCamera.EndAcquisition();
                Console.WriteLine("EndAcquisition executed from CloseOryxCamera block on camera {0}", camNumber.ToString());
            }

            if (closeMethod == Util.CloseCameraMethod.DeInit)
            {
                managedCamera.DeInit();
                Console.WriteLine("Camera number {0} deinitialized.", camNumber.ToString());
            }
            else if (closeMethod == Util.CloseCameraMethod.DeInitAndDeviceReset)
            {
                nodeMap.GetNode<ICommand>("DeviceReset").Execute();
                Console.WriteLine("DeviceReset command executed on camera number {0}.", camNumber.ToString());
            }
            else if (closeMethod == Util.CloseCameraMethod.DeInitAndFactoryReset)
            {
                nodeMap.GetNode<ICommand>("FactoryReset").Execute();
                Console.WriteLine("FactoryReset command executed on camera number {0}.", camNumber.ToString());
            }
        }
    }
}


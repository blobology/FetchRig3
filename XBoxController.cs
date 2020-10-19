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
using SharpDX.XInput;
using System.Runtime.CompilerServices;
using System.Media;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;

namespace FetchRig3
{
    public enum ControllableButtons
    {
        DPadUp = 0,
        LeftShoulder = 1,
        RightShoulder = 2,
        X = 3,
        Y = 4,
        A = 5,
        B = 6,
        DPadRight = 7,
        DPadLeft = 8
    }

    public enum ButtonCommands
    {
        BeginAcquisition = 0,
        BeginStreaming = 1,
        EndStreaming = 2,
        StartRecording = 3,
        StopRecording = 4,
        PlayRewardTone = 5,
        PlayInitiateTrialTone = 6,
        ResetBackgroundImage = 7,
        Exit = 8
    }

    public class XBoxController
    {
        public const int nControllableButtons = 9;
        private readonly int nCameras;
        private Form1 mainForm;
        private ConcurrentQueue<ButtonCommands>[] camControlMessageQueues;
        private Controller controller;
        public ControllerState controllerState;

        public XBoxController(Form1 mainForm, ConcurrentQueue<ButtonCommands>[] camControlMessageQueues)
        {
            this.mainForm = mainForm;
            this.camControlMessageQueues = camControlMessageQueues;
            controller = new Controller(userIndex: UserIndex.One);
            nCameras = camControlMessageQueues.Length;
            controllerState = new ControllerState(this);
        }

        public class ControllerState
        {
            XBoxController xBoxController;
            State state;
            bool[] prevButtonStates;
            bool[] currButtonStates;
            GamepadButtonFlags[] gamepadButtonFlags;
            string[] controllableButtonNames;
            string[] controllableButtonCommands;
            ConcurrentQueue<ButtonCommands> soundQueue;
            public Thread soundThread;

            ButtonCommands[] soundButtons;
            ButtonCommands[] camButtons;
            ButtonCommands[] displayButtons;

            public ControllerState(XBoxController xBoxController)
            {
                this.xBoxController = xBoxController;
                state = new State();
                prevButtonStates = new bool[nControllableButtons];
                currButtonStates = new bool[nControllableButtons];
                controllableButtonNames = Enum.GetNames(typeof(ControllableButtons));
                controllableButtonCommands = Enum.GetNames(typeof(ButtonCommands));
                gamepadButtonFlags = new GamepadButtonFlags[nControllableButtons];

                for (int i = 0; i < nControllableButtons; i++)
                {
                    gamepadButtonFlags[i] = (GamepadButtonFlags)Enum.Parse(typeof(GamepadButtonFlags), controllableButtonNames[i]);
                }

                soundButtons = new ButtonCommands[3]
                {
                    ButtonCommands.PlayInitiateTrialTone,
                    ButtonCommands.PlayRewardTone,
                    ButtonCommands.Exit
                };

                camButtons = new ButtonCommands[6]
                {
                    ButtonCommands.BeginAcquisition,
                    ButtonCommands.BeginStreaming,
                    ButtonCommands.StartRecording,
                    ButtonCommands.StopRecording,
                    ButtonCommands.EndStreaming,
                    ButtonCommands.Exit
                };

                displayButtons = new ButtonCommands[2]
                {
                    ButtonCommands.BeginStreaming,
                    ButtonCommands.EndStreaming
                };

                soundQueue = new ConcurrentQueue<ButtonCommands>();
                soundThread = new Thread(() => this.xBoxController.SoundThreadInit(soundQueue: soundQueue));
                soundThread.IsBackground = false;
                soundThread.Priority = ThreadPriority.Lowest;
                //soundThread.SetApartmentState(state: ApartmentState.MTA);
                soundThread.Start();
            }

            public void Update()
            {
                xBoxController.controller.GetState(state: out state);
                currButtonStates.CopyTo(array: prevButtonStates, index: 0);
                for (int i = 0; i < nControllableButtons; i++)
                {
                    currButtonStates[i] = state.Gamepad.Buttons.HasFlag(gamepadButtonFlags[i]);
                }

                for (int i = 0; i < nControllableButtons; i++)
                {
                    if (prevButtonStates[i] == false && currButtonStates[i] == true)
                    {
                        ButtonCommands buttonCommand = (ButtonCommands)Enum.Parse(typeof(ButtonCommands), controllableButtonCommands[i]);

                        if (camButtons.Contains(buttonCommand))
                        {
                            for (int j = 0; j < xBoxController.nCameras; j++)
                            {
                                ButtonCommands message = buttonCommand;
                                xBoxController.camControlMessageQueues[j].Enqueue(message);
                            }
                        }

                        if (soundButtons.Contains(buttonCommand))
                        {
                            ButtonCommands message = buttonCommand;
                            Console.WriteLine("{0} message enqueued in soundQueue", message);
                            soundQueue.Enqueue(message);
                        }

                        if (displayButtons.Contains(buttonCommand))
                        {
                            if (buttonCommand == ButtonCommands.BeginStreaming)
                            {
                                xBoxController.mainForm.isStreaming = true;
                            }
                            else if (buttonCommand == ButtonCommands.EndStreaming)
                            {
                                xBoxController.mainForm.isStreaming = false;
                            }
                        }

                        if (buttonCommand == ButtonCommands.Exit)
                        {
                            xBoxController.mainForm.ExitButtonPressed();
                        }
                    }
                }
            }
        }

        public void SoundThreadInit(ConcurrentQueue<ButtonCommands> soundQueue)
        {
            const int nSounds = 2;
            ButtonCommands[] buttonsWithSounds = new ButtonCommands[nSounds]
            {
                ButtonCommands.PlayInitiateTrialTone,
                ButtonCommands.PlayRewardTone
            };

            string[] soundFiles = new string[nSounds]
            {
                @"C:\sounds\dingdingding.wav",
                @"C:\sounds\money.wav"
            };

            SoundPlayer[] soundPlayers = new SoundPlayer[nSounds];

            for (int i = 0; i < nSounds; i++)
            {
                soundPlayers[i] = new SoundPlayer(soundLocation: soundFiles[i]);
            }

            System.Timers.Timer soundTimer = new System.Timers.Timer(interval: 100);
            soundTimer.AutoReset = true;
            soundTimer.Elapsed += OnTimedEvent;
            soundTimer.Enabled = true;

            bool isDequeueSuccess;

            void OnTimedEvent(Object sender, EventArgs e)
            {
                isDequeueSuccess = soundQueue.TryDequeue(out ButtonCommands result);
                if (isDequeueSuccess)
                {
                    if (result == ButtonCommands.Exit)
                    {
                        soundTimer.Enabled = false;
                        Console.WriteLine("soundThread will close");
                        return;
                    }

                    for (int i = 0; i < nSounds; i++)
                    {
                        if (result == buttonsWithSounds[i])
                        {
                            soundTimer.Enabled = false;
                            soundPlayers[i].PlaySync();
                            soundTimer.Enabled = true;
                            break;
                        }
                    }
                }
            }
        }
    }
}

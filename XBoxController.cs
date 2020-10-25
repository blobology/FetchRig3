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
using System.IO.Ports;

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
        DPadLeft = 8,
        DPadDown = 9
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
        Exit = 8,
        SaveThisImageFromProcessingStream = 9
    }

    public class XBoxController
    {
        public const int nControllableButtons = 10;
        private readonly int nCameras;
        private Form1 mainForm;
        private ConcurrentQueue<ButtonCommands>[] camControlMessageQueues;
        private Controller controller;
        public ControllerState controllerState;
        private string serialPortName = "COM3";
        private SerialPort serialPort;

        public XBoxController(Form1 mainForm, ConcurrentQueue<ButtonCommands>[] camControlMessageQueues)
        {
            this.mainForm = mainForm;
            this.camControlMessageQueues = camControlMessageQueues;
            controller = new Controller(userIndex: UserIndex.One);
            nCameras = camControlMessageQueues.Length;
            serialPort = new SerialPort(portName: serialPortName, baudRate: 115200, parity: Parity.None, dataBits: 8, stopBits: StopBits.One);
            serialPort.Open();
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

            ButtonCommands[] soundButtons;
            ButtonCommands[] camButtons;
            ButtonCommands[] displayButtons;
            ButtonCommands[] streamProcessingButtons;

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

                streamProcessingButtons = new ButtonCommands[5]
                {
                    ButtonCommands.BeginStreaming,
                    ButtonCommands.EndStreaming,
                    ButtonCommands.ResetBackgroundImage,
                    ButtonCommands.Exit,
                    ButtonCommands.SaveThisImageFromProcessingStream
                };
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
                            string message;
                            if (buttonCommand == ButtonCommands.PlayInitiateTrialTone)
                            {
                                message = "initiate_trial";
                                xBoxController.serialPort.Write(text: message);
                            }
                            else if (buttonCommand == ButtonCommands.PlayRewardTone)
                            {
                                message = "reward";
                                xBoxController.serialPort.Write(text: message);
                            }
                            else if (buttonCommand == ButtonCommands.Exit)
                            {
                                message = "exit";
                                xBoxController.serialPort.Write(text: message);
                                xBoxController.serialPort.Close();
                            }
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

                        if (streamProcessingButtons.Contains(buttonCommand))
                        {
                            ButtonCommands message = buttonCommand;
                            xBoxController.mainForm.processingThreadMessageQueue.Enqueue(message);
                        }

                        if (buttonCommand == ButtonCommands.Exit)
                        {
                            xBoxController.mainForm.ExitButtonPressed();
                        }
                    }
                }
            }
        }
    }
}

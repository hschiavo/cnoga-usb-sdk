using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CNoga.DeviceDetector.Helper;
using CNoga.Extensions;
using CNoga.FirmwareUpgrader.Types.MeasurementResultsData;
using CNoga.FirmwareUpgrader.Types.SDK_APIs;
using CNoga.Serializers.AiOSerializer.KnownTypes.AiOAttributes;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CNoga.Singular.SDK.DemoConsole
{
    public class SDKDemoTester
    {
        #region Enums

        public enum CommunicationType
        {
            USB,
            BLE
        }

        #endregion

        #region Fields

        private readonly CommunicationType _communicationType;

        private ILogger<SDKDemoTester> _logger;

        private readonly ManualResetEvent _deviceDetectionWaitHandler;
        private bool _startReceivingMeasurements;
        private int _measurementsCounter;

        private readonly IDeviceDetectorManager _deviceDetectorManager;

        private IOPSWDevice _oPSwDevice;

        private int _deviceSelectedNumber;

        private const bool PrintLog = true;

        #endregion

        #region Constructors

        /// <summary>
        /// 
        /// </summary>
        /// <param name="commType"></param>
        public SDKDemoTester(CommunicationType commType)
        {
            _communicationType = commType;
            _deviceDetectionWaitHandler = new ManualResetEvent(false);

            var licesnePath = Path.Combine(Environment.CurrentDirectory, "license.cbd");

            if (commType == CommunicationType.USB)
            {
                _deviceDetectorManager = DeviceDetectorManager<UsbDeviceDetectorManager>.GetManagerInstance(licesnePath);
            }
            else if (commType == CommunicationType.BLE)
            {
                _deviceDetectorManager = DeviceDetectorManager<BleDeviceDetectorManager>.GetManagerInstance(licesnePath);
            }

            SubscribeToDeviceDetectorEvents();

            if (PrintLog)
                CreateMyCustomLogger();
        }

        #endregion

        #region Methods

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task StartTest(CancellationTokenSource ctx)
        {
            var currentCtx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

            PrintUserActionsLegend();

#pragma warning disable 4014
            /*await*/ StartDetect();
#pragma warning restore 4014

            while (true)
            {
                if ((_oPSwDevice != null) && (_oPSwDevice.IsOpen))
                {
                    if (_startReceivingMeasurements == false)
                    {
                        Console.WriteLine("Device with Serial Number: " + _oPSwDevice.DeviceInformation.SerialNumber + " is currently connected." + Environment.NewLine);
                        Console.WriteLine("Insert finger into the device and afterwards press 'M' to start receiving measurements results.");
                        Console.WriteLine("Press 'Q' to stop receiving measurements results.");
                        Console.WriteLine("Press 'C' to close the device.");
                        Console.WriteLine();
                    }
                }

                var k = Console.ReadKey();
                Console.WriteLine();

                // Start scanning operation
                if (k.Key == ConsoleKey.S)
                {
                    if (_communicationType == CommunicationType.BLE)
                    {
                        if ((_oPSwDevice != null) && (_oPSwDevice.IsOpen))
                        {
                            Console.WriteLine("A device is currently connected. Close it first and then press 's' to rescan." + Environment.NewLine);
                        }
                        else if (_deviceDetectorManager.IsDetecting == false)
                        {
                            Console.WriteLine("Restarting the detection operation...");
#pragma warning disable 4014
                            /*await*/ StartDetect();
#pragma warning restore 4014
                        }
                        else
                        {
                            Console.WriteLine("The detection is already running.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Illegal option was selected...");
                    }
                }
                else if (k.Key == ConsoleKey.P)
                {
                    if (_communicationType == CommunicationType.BLE)
                    {
                        if (_deviceDetectorManager.IsDetecting)
                        {
                            await StopDetect();
                        }
                        else
                        {
                            Console.WriteLine("The detection is no longer running.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Illegal option was selected...");
                    }
                }
                // Open selected USB device
                else if ((_communicationType == CommunicationType.USB) && (k.Key == ConsoleKey.O))
                {
                    const int deviceNum = 1;

                    await ConnectToDevice(deviceNum);
                }
                // Open selected BLE device
                else if ((_communicationType == CommunicationType.BLE) && ((k.KeyChar >= 49) && (k.KeyChar <= 57)))
                {
                    if (_deviceDetectorManager.IsDetecting)
                    {
                        Console.WriteLine("Wait for the detection to finish or finish it proactively.");
                        continue;
                    }

                    var deviceNum = int.Parse(k.KeyChar.ToString());

                    await ConnectToDevice(deviceNum);
                }
                // Close device
                else if (k.Key == ConsoleKey.C)
                {
                    if ((_communicationType == CommunicationType.BLE) && (_deviceDetectorManager.IsDetecting))
                    {
                        Console.WriteLine("Wait for the detection to finish or finish it proactively.");
                        continue;
                    }

                    if ((_oPSwDevice == null) || (_oPSwDevice.IsOpen == false))
                    {
                        Console.WriteLine("You cannot close an already closed device." + Environment.NewLine);
                        continue;
                    }

                    await _deviceDetectorManager.CloseDevice(_oPSwDevice);

                    // Wait 2 secs for the device itself to respond with its disconnection indication
                    if (_communicationType == CommunicationType.BLE)
                       await Task.Delay(1000 * 2);

                    Console.WriteLine("Device with Serial Number: " + _oPSwDevice.DeviceInformation.SerialNumber + " was closed." + Environment.NewLine);

                    if (_oPSwDevice != null)
                    {
                        UnsubscribeFromDeviceEvents();
                        _oPSwDevice = null;
                    }

                    _startReceivingMeasurements = false;
                    _deviceSelectedNumber = -1;

                    if (_communicationType == CommunicationType.BLE)
                    {
                        Console.WriteLine("Restarting the detection process." + Environment.NewLine);

#pragma warning disable 4014
                        /*await*/ StartDetect();
#pragma warning restore 4014
                    }
                    else if (_communicationType == CommunicationType.USB)
                    {
                        Console.WriteLine("Reopen the device and start over again." + Environment.NewLine);
                    }
                }
                // Start measuring operation
                else if (k.Key == ConsoleKey.M)
                {
                    if ((_communicationType == CommunicationType.BLE) && (_deviceDetectorManager.IsDetecting))
                    {
                        Console.WriteLine("Wait for the detection to finish or finish it proactively.");
                        continue;
                    }

                    if ((_oPSwDevice == null) || (_oPSwDevice.IsOpen == false))
                    {
                        Console.WriteLine("You cannot start measuring on a unconnected/closed device.");

                        if (_communicationType == CommunicationType.USB)
                            Console.WriteLine("Reconncet/Reopen the device and start over again." + Environment.NewLine);
                        else if (_communicationType == CommunicationType.BLE)
                            Console.WriteLine("Open/Reopen the device and start over again." + Environment.NewLine);

                        continue;
                    }

                    if (_startReceivingMeasurements == false)
                    {
                        SubscribeToDeviceEvents();

                        await _oPSwDevice.StartMeasurement();

                        _startReceivingMeasurements = true;
                    }
                }
                // Stop measuring operation
                else if (k.Key == ConsoleKey.Q)
                {
                    if ((_communicationType == CommunicationType.BLE) && (_deviceDetectorManager.IsDetecting))
                    {
                        Console.WriteLine("Wait for the detection to finish or finish it proactively.");
                        continue;
                    }

                    if ((_oPSwDevice == null) || (_oPSwDevice.IsOpen == false))
                    {
                        Console.WriteLine("You cannot stop measuring on a unconnected/closed device.");

                        if (_communicationType == CommunicationType.USB)
                            Console.WriteLine("Reconncet/Reopen the device and start over again." + Environment.NewLine);
                        else if (_communicationType == CommunicationType.BLE)
                            Console.WriteLine("Open/Reopen the device and start over again." + Environment.NewLine);

                        continue;
                    }

                    await _oPSwDevice.StopMeasurement();

                    UnsubscribeFromDeviceEvents();

                    _startReceivingMeasurements = false;
                }
                // Get device's battery status
                else if (k.Key == ConsoleKey.B)
                {
                    if ((_communicationType == CommunicationType.BLE) && (_deviceDetectorManager.IsDetecting))
                    {
                        Console.WriteLine("Wait for the detection to finish or finish it proactively.");
                        continue;
                    }

                    if ((_oPSwDevice == null) || (_oPSwDevice.IsOpen == false))
                    {
                        Console.WriteLine("You cannot perform this operation on a unconnected/closed device.");

                        if (_communicationType == CommunicationType.USB)
                            Console.WriteLine("Reconncet/Reopen the device and start over again." + Environment.NewLine);
                        else if (_communicationType == CommunicationType.BLE)
                            Console.WriteLine("Open/Reopen the device and start over again." + Environment.NewLine);

                        continue;
                    }
                    try
                    {
                        var batteryStatus = await _oPSwDevice.GetBatteryStatus();
                        var result = string.Concat(batteryStatus, "%");
                        Console.WriteLine("Battery status is " + result + Environment.NewLine);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Battery status is supported for MTX devices only.");
                    }

                }
                // Switch to a other communication type (BLE or USB)
                else if (k.Key == ConsoleKey.E)
                {
                    if ((_communicationType == CommunicationType.BLE) && (_deviceDetectorManager.IsDetecting))
                    {
                        Console.WriteLine("Wait for the detection to finish or finish it proactively.");
                        continue;
                    }

                    if (_oPSwDevice != null)
                    {
                        if (_oPSwDevice.IsOpen)
                        {
                            await _deviceDetectorManager.CloseDevice(_oPSwDevice);

                            // Wait 2 secs for the device itself to respond with its disconnection indication
                            if (_communicationType == CommunicationType.BLE)
                                await Task.Delay(1000 * 2);
                        }

                        UnsubscribeFromDeviceEvents();
                        _oPSwDevice = null;
                    }

                    UnsubscribeFromDeviceDetectorEvents();

                    ctx.Cancel();
                    break;
                }
                else
                {
                    Console.WriteLine("Illegal option was selected...");
                }
            }

            SynchronizationContext.SetSynchronizationContext(currentCtx);
        }

        /// <summary>
        /// User's actions legend
        /// </summary>
        private void PrintUserActionsLegend()
        {
            if (_communicationType == CommunicationType.BLE)
            {
                Console.WriteLine("You have chosen a BLE communication type. These are all the allowed user's actions for this type of communication:");
                Console.WriteLine("=====================================================================================================================");

                Console.WriteLine("'S' - Start scan for BLE devices.");
                Console.WriteLine("'P' - Stop scan for BLE devices.");
                Console.WriteLine("'1' to '9' - Open the selected (detected) BLE device (The digits range varies according the detected devices number).");
            }
            else if (_communicationType == CommunicationType.USB)
            {
                Console.WriteLine("You have chosen a USB communication type. These are all the allowed user's actions for this type of communication:");
                Console.WriteLine("=====================================================================================================================");

                Console.WriteLine("'O' - Open the connected USB device.");
            }

            Console.WriteLine("'B' - Get device's Battery Status.");
            Console.WriteLine("'C' - Close the currently opened device.");
            Console.WriteLine("'M' - Enable measurements data transmission (From the Device To its Host).");
            Console.WriteLine("'Q' - Disable measurements data transmission (From the Device To its Host).");
            Console.WriteLine("'E' - Switch to a different communication type (BLE or USB).");

            Console.WriteLine("=====================================================================================================================");
            Console.WriteLine();
        }

        /// <summary>
        /// 
        /// </summary>
        private void PrintDetectedDevices()
        {
            var totalDiscoveredDevices = _deviceDetectorManager.Devices.Count();
            Console.WriteLine(totalDiscoveredDevices + " devices are currently detected. Press any number from 1 to " + _deviceDetectorManager.Devices.Count() + " to connect to the selected device:" + Environment.NewLine);

            for (var i = 0; i < _deviceDetectorManager.Devices.Count; i++)
            {
                Console.WriteLine(@"Device number: " + (i + 1) + @" -> Device Address: " + _deviceDetectorManager.Devices[i].DeviceAddress + Environment.NewLine);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="deviceNum"></param>
        /// <returns></returns>
        private async Task ConnectToDevice(int deviceNum)
        {
            if ((_oPSwDevice != null) && (_oPSwDevice.IsOpen))
            {
                if (_deviceSelectedNumber == deviceNum)
                {
                    Console.WriteLine("You cannot open an already opened device." + Environment.NewLine);
                    return;
                }
                if (_deviceSelectedNumber != deviceNum)
                {
                    Console.WriteLine("Another device is already open. You need to close it first in order to connect to the other device." + Environment.NewLine);
                    return;
                }
            }
            else if (_deviceDetectorManager.Devices.Any() == false)
            {
                Console.WriteLine("You cannot open a disconnected device.");
                Console.WriteLine("Reconncet the device and start over again." + Environment.NewLine);
                return;
            }

            _oPSwDevice = await OpenDevice((deviceNum - 1));
            _deviceSelectedNumber = deviceNum;
            _startReceivingMeasurements = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="deviceIndex"></param>
        /// <returns></returns>
        private async Task<IOPSWDevice> OpenDevice(int deviceIndex)
        {
            string infoMsg;

            if (deviceIndex >= _deviceDetectorManager.Devices.Count)
            {
                infoMsg = "The selected device number is not exists in the detected devices.";

                Console.WriteLine(infoMsg);
                _logger.LogInformation(infoMsg);

                return null;
            }

            var detectedDevice = _deviceDetectorManager.Devices[deviceIndex];
            if (detectedDevice != null)
            {
                IOPSWDevice device = null;

                device = await _deviceDetectorManager.OpenDevice(detectedDevice);

                _measurementsCounter = 0;

                return device;
            }
            infoMsg = "The selected device suddenly became undetectable. Choose another detected device.";

            Console.WriteLine(infoMsg);
            _logger.LogInformation(infoMsg);

            return null;
        }

        /// <summary>
        /// Start the detection operation process
        /// </summary>
        /// <returns></returns>
        private async Task StartDetect()
        {
            var enabledCommTyoes = _deviceDetectorManager.GetEnabledCommTypes();
            if (!enabledCommTyoes.Any() && !enabledCommTyoes.ContainsKey(CommTypeHelper.CommType.BLE))
            {
                Console.WriteLine("Bluetooth is not supported!");
                return;
            }
            const string infoMsg = "Waiting for devices...";

            if (_communicationType == CommunicationType.USB)
            {
                Console.WriteLine(infoMsg + Environment.NewLine);
                _logger.LogInformation(infoMsg);

                foreach (var detectableDevice in _deviceDetectorManager.Devices)
                {
                    DeviceArrived(this, detectableDevice);
                }

                await _deviceDetectorManager.StartDetection();
            }
            else if (_communicationType == CommunicationType.BLE)
            {
                try
                {
                    while ((_oPSwDevice == null) || (_oPSwDevice.IsOpen == false))
                    {
                        Console.WriteLine(infoMsg + Environment.NewLine);
                        _logger.LogInformation(infoMsg);

                        await _deviceDetectorManager.StartDetection();

                        while (_deviceDetectorManager.IsDetecting)
                        {
                            await _deviceDetectionWaitHandler.WaitOneAsync(200);
                        }

                        if (_deviceDetectorManager.Devices.Any())
                        {
                            PrintDetectedDevices();
                            break;
                        }

                        _deviceDetectionWaitHandler.Reset();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine("Enable your Bluetooth device and start resacan.");
                }
            }
        }

        /// <summary>
        /// Stop the detection operation process
        /// </summary>
        /// <returns></returns>
        private async Task StopDetect()
        {
            await _deviceDetectorManager.StopDetection();

            Console.WriteLine("Devices detection was stopped." + Environment.NewLine);

            _deviceDetectionWaitHandler.Set();
        }

        #endregion

        #region Events Implementation

        private void DevicesDetectionFinished(object sender)
        {
            const string infoMsg = "Detection operation was finished.";

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);

            _deviceDetectionWaitHandler.Set();
        }

        private void DeviceArrived(object sender, IDetectableDevice detectedDevice)
        {
            var infoMsg = "Device with address: " + detectedDevice.DeviceAddress + " was detected.";

            if (_communicationType == CommunicationType.USB)
            {
                infoMsg += Environment.NewLine + "Press 'O' to open the device.";
            }

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);
        }

        private void DeviceRemoved(object sender, IDetectableDevice detectedDevice)
        {
            var infoMsg = "Device with address: " + detectedDevice.DeviceAddress + " was undetected.";

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);
        }

        private void DeviceDisconnected(object sender, IOPSWDevice oPSwDevice)
        {
            _oPSwDevice = null;

            var infoMsg = "Device with Serial Number: " + oPSwDevice.DeviceInformation.SerialNumber + " was suddenly disconnected.";

            Console.WriteLine(infoMsg);
            _logger.LogInformation(infoMsg);

            _startReceivingMeasurements = false;
            _deviceSelectedNumber = -1;

            if (_communicationType == CommunicationType.USB)
            {
                Console.WriteLine("Reconnect the device and start over again." + Environment.NewLine);
            }
            else if (_communicationType == CommunicationType.BLE)
            {
                if (_deviceDetectorManager.Devices.Any())
                {
                    PrintDetectedDevices();
                }
                else
                {
                    Console.WriteLine("No detected devices have left. Press 's' to rescan again for devices." + Environment.NewLine);
                }
            }
        }

        private void DeviceDetectorManagerOnDetectionStateChanged(object sender, bool isDetectionStatusOn)
        {
            if (isDetectionStatusOn)
                Console.WriteLine("Bluetooth is enabled. Press 's' to rescan again for devices." + Environment.NewLine);
        }

        private void MeasurementsStatusChanged(object sender, MeasurementsStatus measurementsStatus)
        {
            string infoMsg = null;

            if (measurementsStatus == MeasurementsStatus.Started)
            {
                infoMsg = "Measurment started";
            }
            else if (measurementsStatus == MeasurementsStatus.Stopped)
            {
                infoMsg = "Measurment stopped";
            }

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);
        }

        [Obsolete("Method MeasurementStarted is deprecated. Use method MeasurementsStatusChanged instead.")]
        private void MeasurementStarted(object sender)
        {
            const string infoMsg = "Measurment started";

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);
        }

        [Obsolete("Method MeasurementStopped is deprecated. Use method MeasurementsStatusChanged instead.")]
        private void MeasurementStopped(object sender)
        {
            const string infoMsg = "Measurment stopped";

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);
        }

        private void MeasurementArrived(object sender, MeasurementsResults message)
        {
            var measurements = message.ClinicalParametersResults;

            var values = "";

            foreach (var measurement in measurements)
            {
                if (measurement.Quality == MedLibResultQuality.NotValid)
                {
                    continue;
                }

                values += $"{measurement.Result}, ";
            }
            values = values.TrimEnd(',', ' ');

            _measurementsCounter++;

            var infoMsg = "Measurement #:" + _measurementsCounter + " : " + values;

            Console.WriteLine(infoMsg + Environment.NewLine);
            _logger.LogInformation(infoMsg);
        }

        #endregion

        #region Utility Methods

        private void SubscribeToDeviceDetectorEvents()
        {
            _deviceDetectorManager.DevicesDetectionFinished += DevicesDetectionFinished;
            _deviceDetectorManager.DeviceArrived += DeviceArrived;
            _deviceDetectorManager.DeviceRemoved += DeviceRemoved;
            _deviceDetectorManager.DeviceDisconnected += DeviceDisconnected;
            _deviceDetectorManager.DetectionStateChanged += DeviceDetectorManagerOnDetectionStateChanged;
        }

        private void UnsubscribeFromDeviceDetectorEvents()
        {
            _deviceDetectorManager.DevicesDetectionFinished -= DevicesDetectionFinished;
            _deviceDetectorManager.DeviceArrived -= DeviceArrived;
            _deviceDetectorManager.DeviceRemoved -= DeviceRemoved;
            _deviceDetectorManager.DeviceDisconnected -= DeviceDisconnected;
            _deviceDetectorManager.DetectionStateChanged -= DeviceDetectorManagerOnDetectionStateChanged;

        }

        private void SubscribeToDeviceEvents()
        {
            _oPSwDevice.MeasurementsStatusChanged += MeasurementsStatusChanged;
            //_oPSwDevice.MeasurementStarted += MeasurementStarted;
            //_oPSwDevice.MeasurementStopped += MeasurementStopped;
            _oPSwDevice.MeasurementArrived += MeasurementArrived;
        }

        private void UnsubscribeFromDeviceEvents()
        {
            _oPSwDevice.MeasurementsStatusChanged -= MeasurementsStatusChanged;
            //_oPSwDevice.MeasurementStarted -= MeasurementStarted;
            //_oPSwDevice.MeasurementStopped -= MeasurementStopped;
            _oPSwDevice.MeasurementArrived -= MeasurementArrived;
        }

        private void CreateMyCustomLogger()
        {
            LoadLogSettings();

            var loggerFactory = _deviceDetectorManager.LoggerFactory;
            loggerFactory.AddSerilog();
            //loggerFactory.AddDebug(LogLevel.Debug);
            _logger = loggerFactory.CreateLogger<SDKDemoTester>();
        }

        private static void LoadLogSettings()
        {
            const string template = "Program Name: {Application}, Machine Name: {MachineName}, Thread ID: {ThreadId} - {Timestamp:HH:mm:ss.fff zzz} [{Level}] ({SourceContext}.{Method}) {ActionName}{RequestPath} {Message:lj}{NewLine}{Exception}";
            const string logFolder = "Log";
            const string logFile = "app_.log";

            var levelSwitch = new LoggingLevelSwitch { MinimumLevel = LogEventLevel.Debug };
#if DEBUG
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
#endif

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "Singular SDK Console Demo")
                .Enrich.WithProperty("MachineName", Environment.MachineName)
                .WriteTo.File(logFolder + "\\" + logFile,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 10485760, // 10Mb
                    outputTemplate: template,
                    retainedFileCountLimit: 30,
                    flushToDiskInterval: TimeSpan.FromDays(1),
                    levelSwitch: levelSwitch)
                .CreateLogger();
        }

        #endregion
    }
}

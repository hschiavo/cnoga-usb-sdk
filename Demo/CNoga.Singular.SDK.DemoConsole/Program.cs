using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DemoConsoleTestsUtilities;

namespace CNoga.Singular.SDK.DemoConsole
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var parentDirPath = Utils.GetLicenseDirectoryPath();
            var exeDirPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            try
            {
                while (true)
                {
                    Console.WriteLine("Choose the communication type you want to work with:");
                    Console.WriteLine("1-BLE" + Environment.NewLine + "2-USB");

                    var key = Console.ReadKey();
                    Console.WriteLine();

                    while ((key.KeyChar != 49) && (key.KeyChar != 50))
                    {
                        Console.WriteLine("Illegal selection. Please choose again.");
                        key = Console.ReadKey();
                        Console.WriteLine();
                    }
                    Console.WriteLine();

                    var userSelection = int.Parse(key.KeyChar.ToString());

                    SDKDemoTester.CommunicationType commType;

                    switch (userSelection)
                    {
                        case 1:
                            commType = SDKDemoTester.CommunicationType.BLE;
                            break;
                        case 2:
                            commType = SDKDemoTester.CommunicationType.USB;
                            break;
                        default: throw new NotSupportedException("Illegal option was selected.");
                    }

                    string licenseFilePath = null;

                    if (commType == SDKDemoTester.CommunicationType.BLE)
                    {
                        licenseFilePath = Path.Combine(parentDirPath, "BLE_License.cbd");
                    }
                    else if (commType == SDKDemoTester.CommunicationType.USB)
                    {
                        licenseFilePath = Path.Combine(parentDirPath, "USB_License.cbd");
                    }

                    try
                    {
                        File.Copy(licenseFilePath, Path.Combine(exeDirPath, "license.cbd"), true);
                    }
                    catch (Exception)
                    {
                        throw new NotSupportedException($"The selected communication type {commType} is not supported.");
                    }

                    var ctx = new CancellationTokenSource();

                    var usbManagerTester = new SDKDemoTester(commType);

                    while (!ctx.IsCancellationRequested)
                    {
                        await usbManagerTester.StartTest(ctx);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            Console.WriteLine("\n\rPress any key to continue... ");
            Console.ReadLine();
        }
    }
}

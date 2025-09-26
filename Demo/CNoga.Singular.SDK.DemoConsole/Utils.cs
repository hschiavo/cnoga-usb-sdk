using System;
using System.IO;

// ReSharper disable once CheckNamespace
namespace DemoConsoleTestsUtilities
{
    public static class Utils
    {
        public static string GetLicenseDirectoryPath()
        {
            var result = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.Parent.FullName;

            return result;
        }
    }
}

using System;
using System.Management;
using System.Net.NetworkInformation;

namespace AssetCollector
{
    public class HardwareCollector
    {
        public static string GetBasicInfo()
        {
            return $"计算机名: {Environment.MachineName} | 用户名: {Environment.UserName}";
        }

        public static string GetCpuInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        return item["Name"]?.ToString().Trim();
                    }
                }
            }
            catch { }
            return "Unknown CPU";
        }

        // 你可以将原来 Python 里的所有采集逻辑都用类似上面的 C# ManagementObjectSearcher 翻译过来
    }
}

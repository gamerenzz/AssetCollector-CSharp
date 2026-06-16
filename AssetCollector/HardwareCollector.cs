using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Linq;

namespace AssetCollector
{
    public class HardwareCollector
    {
        public static string GetOSInfo() { /* 保持之前代码... */ 
            try { using (var s = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem")) { foreach (var i in s.Get()) return $"{i["Caption"]} (Build {i["BuildNumber"]})"; } } catch { } return "Unknown OS"; 
        }
        public static string GetCpuInfo() { /* 保持之前代码... */ 
            try { using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor")) { foreach (var i in s.Get()) return $"{i["Name"]?.ToString().Trim()} ({i["NumberOfCores"]}核/{i["NumberOfLogicalProcessors"]}线程)"; } } catch { } return "Unknown CPU"; 
        }
        public static string GetRamInfo() { /* 保持之前代码... */ 
            try { double t = 0; int c = 0; using (var s = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory")) { foreach (var i in s.Get()) { t += Convert.ToDouble(i["Capacity"]); c++; } } return $"{Math.Round(t / 1073741824, 2)} GB ({c}根)"; } catch { } return "Unknown RAM"; 
        }
        public static string GetDiskInfo() { /* 保持之前代码... */ 
            try { List<string> d = new List<string>(); using (var s = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive")) { foreach (var i in s.Get()) { if (i["Size"] != null) d.Add($"{i["Model"]?.ToString().Trim()} ({Math.Round(Convert.ToDouble(i["Size"]) / 1073741824, 2)} GB)"); } } return string.Join(" | ", d); } catch { } return "Unknown Disk"; 
        }
        public static string GetMotherboardInfo() { /* 保持之前代码... */ 
            try { using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard")) { foreach (var i in s.Get()) return $"制造商: {i["Manufacturer"]} | 型号: {i["Product"]} | 序列号: {i["SerialNumber"]}"; } } catch { } return "Unknown Motherboard"; 
        }

        // 【修复】显卡信息：加入更严格的过滤
        public static string GetGpuInfo()
        {
            try
            {
                List<string> gpus = new List<string>();
                string[] blockList = { "sunlogin", "oray", "gameviewer", "virtual", "radmin" }; // 过滤虚拟显卡

                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        string name = item["Name"]?.ToString().Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            bool isBlocked = blockList.Any(b => name.ToLower().Contains(b));
                            if (!isBlocked) gpus.Add(name);
                        }
                    }
                }
                return gpus.Count > 0 ? string.Join(" | ", gpus) : "Unknown GPU";
            }
            catch { }
            return "Unknown GPU";
        }

        // 【修复】网卡 MAC 与 IP：兼容更多情况
        public static (string MAC, string IP) GetNetworkInfo()
        {
            string mac = "Unknown";
            string ip = "Unknown";
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 只要网卡是开启的，且不是回环网卡，且有 MAC 地址
                    if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                        if (macBytes.Length == 6) // 标准的 MAC 地址长度
                        {
                            mac = string.Join("-", macBytes.Select(b => b.ToString("X2")));
                            
                            foreach (var ipInfo in nic.GetIPProperties().UnicastAddresses)
                            {
                                if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4
                                {
                                    ip = ipInfo.Address.ToString();
                                    return (mac, ip); // 获取到有效的 IP 和 MAC 就立即返回
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return (mac, ip);
        }
    }
}

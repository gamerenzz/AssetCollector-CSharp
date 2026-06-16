using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace AssetCollector
{
    // 定义软件清单模型
    public class SoftwareItem
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string InstallDate { get; set; }
    }

    public class HardwareCollector
    {
        public static string GetOSInfo() { 
            try { using (var s = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem")) { foreach (var i in s.Get()) return $"{i["Caption"]} (Build {i["BuildNumber"]})"; } } catch { } return "Unknown OS"; 
        }
        
        public static string GetCpuInfo() { 
            try { using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor")) { foreach (var i in s.Get()) return $"{i["Name"]?.ToString().Trim()} ({i["NumberOfCores"]}核/{i["NumberOfLogicalProcessors"]}线程)"; } } catch { } return "Unknown CPU"; 
        }
        
        public static string GetRamInfo() { 
            try { double t = 0; int c = 0; using (var s = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory")) { foreach (var i in s.Get()) { t += Convert.ToDouble(i["Capacity"]); c++; } } return $"{Math.Round(t / 1073741824, 2)} GB ({c}根)"; } catch { } return "Unknown RAM"; 
        }
        
        public static string GetDiskInfo() { 
            try { List<string> d = new List<string>(); using (var s = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive")) { foreach (var i in s.Get()) { if (i["Size"] != null) d.Add($"{i["Model"]?.ToString().Trim()} ({Math.Round(Convert.ToDouble(i["Size"]) / 1073741824, 2)} GB)"); } } return string.Join(" | ", d); } catch { } return "Unknown Disk"; 
        }
        
        public static string GetMotherboardInfo() { 
            try { using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard")) { foreach (var i in s.Get()) return $"制造商: {i["Manufacturer"]} | 型号: {i["Product"]} | 序列号: {i["SerialNumber"]}"; } } catch { } return "Unknown Motherboard"; 
        }

        public static string GetGpuInfo()
        {
            try
            {
                List<string> gpus = new List<string>();
                string[] blockList = { "sunlogin", "oray", "gameviewer", "virtual", "radmin", "wjidd" }; 
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        string name = item["Name"]?.ToString().Trim();
                        if (!string.IsNullOrEmpty(name) && !blockList.Any(b => name.ToLower().Contains(b))) gpus.Add(name);
                    }
                }
                return gpus.Count > 0 ? string.Join(" | ", gpus) : "Unknown GPU";
            }
            catch { }
            return "Unknown GPU";
        }

        public static (string MAC, string IP) GetNetworkInfo()
        {
            List<string> macList = new List<string>();
            List<string> ipList = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        string desc = nic.Description.ToLower();
                        if (desc.Contains("vmware") || desc.Contains("virtual") || desc.Contains("hyper-v")) continue;

                        var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                        if (macBytes.Length == 6)
                        {
                            string currentMac = string.Join("-", macBytes.Select(b => b.ToString("X2")));
                            string currentIp = "";
                            foreach (var ipInfo in nic.GetIPProperties().UnicastAddresses)
                            {
                                if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                {
                                    currentIp = ipInfo.Address.ToString();
                                    break;
                                }
                            }
                            if (!string.IsNullOrEmpty(currentIp))
                            {
                                macList.Add($"{nic.Name}: {currentMac}");
                                ipList.Add($"{nic.Name}: {currentIp}");
                            }
                        }
                    }
                }
            }
            catch { }
            return (macList.Count > 0 ? string.Join(" | ", macList) : "Unknown", ipList.Count > 0 ? string.Join(" | ", ipList) : "Unknown");
        }

        public static string GetSystemModel() { 
            try { using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem")) { foreach (var i in s.Get()) return $"{i["Manufacturer"]} {i["Model"]}".Trim(); } } catch { } return "Unknown Model"; 
        }

        public static string GetMonitorInfo()
        {
            List<string> monitors = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT UserFriendlyName, SerialNumberID FROM WmiMonitorID"))
                {
                    foreach (var item in searcher.Get())
                    {
                        string name = DecodeWmiCharArray(item["UserFriendlyName"] as ushort[]);
                        string serial = DecodeWmiCharArray(item["SerialNumberID"] as ushort[]);
                        if (!string.IsNullOrEmpty(name)) monitors.Add($"{name} (SN: {serial})");
                    }
                }
                return monitors.Count > 0 ? string.Join(" | ", monitors) : "未检测到外接显示器";
            }
            catch { }
            return "获取显示器信息失败";
        }

        private static string DecodeWmiCharArray(ushort[] array)
        {
            if (array == null) return "";
            string result = "";
            foreach (var c in array) { if (c > 0) result += (char)c; }
            return result.Trim();
        }

        // 【精细化重写】获取详细软件列表，带版本号和安装日期
        public static List<SoftwareItem> GetInstalledSoftwareList()
        {
            var softwareList = new List<SoftwareItem>();
            string[] registryPaths = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            try
            {
                // 1. 读取 LocalMachine
                using (var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (var path in registryPaths)
                    {
                        using (var key = localMachine.OpenSubKey(path))
                        {
                            if (key == null) continue;
                            foreach (var subkeyName in key.GetSubKeyNames())
                            {
                                using (var subkey = key.OpenSubKey(subkeyName))
                                {
                                    if (subkey == null) continue;
                                    string displayName = subkey.GetValue("DisplayName")?.ToString()?.Trim();
                                    string systemComponent = subkey.GetValue("SystemComponent")?.ToString();
                                    string parentKeyName = subkey.GetValue("ParentKeyName")?.ToString();

                                    if (!string.IsNullOrEmpty(displayName) && systemComponent != "1" && string.IsNullOrEmpty(parentKeyName))
                                    {
                                        string version = subkey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "Unknown";
                                        string rawDate = subkey.GetValue("InstallDate")?.ToString()?.Trim() ?? "Unknown";
                                        
                                        // 规范化日期格式（如果是YYYYMMDD格式，转为 YYYY-MM-DD）
                                        if (rawDate.Length == 8 && double.TryParse(rawDate, out _))
                                        {
                                            rawDate = $"{rawDate.Substring(0, 4)}-{rawDate.Substring(4, 2)}-{rawDate.Substring(6, 2)}";
                                        }

                                        softwareList.Add(new SoftwareItem { Name = displayName, Version = version, InstallDate = rawDate });
                                    }
                                }
                            }
                        }
                    }
                }

                // 2. 读取 CurrentUser
                using (var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    using (var key = currentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (key != null)
                        {
                            foreach (var subkeyName in key.GetSubKeyNames())
                            {
                                using (var subkey = key.OpenSubKey(subkeyName))
                                {
                                    if (subkey == null) continue;
                                    string displayName = subkey.GetValue("DisplayName")?.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(displayName))
                                    {
                                        string version = subkey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "Unknown";
                                        string rawDate = subkey.GetValue("InstallDate")?.ToString()?.Trim() ?? "Unknown";
                                        if (rawDate.Length == 8 && double.TryParse(rawDate, out _))
                                        {
                                            rawDate = $"{rawDate.Substring(0, 4)}-{rawDate.Substring(4, 2)}-{rawDate.Substring(6, 2)}";
                                        }

                                        softwareList.Add(new SoftwareItem { Name = displayName, Version = version, InstallDate = rawDate });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 过滤重复软件名，并按软件名字母排序
            return softwareList
                .GroupBy(s => s.Name)
                .Select(g => g.First())
                .OrderBy(s => s.Name)
                .ToList();
        }
    }
}

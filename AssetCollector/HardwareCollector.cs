using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Linq;

namespace AssetCollector
{
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

        // 显卡信息：加入了 wjidd 过滤
        public static string GetGpuInfo()
        {
            try
            {
                List<string> gpus = new List<string>();
                // 这里是显卡黑名单，包含 sunlogin(向日葵), wjidd(无界虚拟显卡) 等
                string[] blockList = { "sunlogin", "oray", "gameviewer", "virtual", "radmin", "wjidd" }; 

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

        // 【核心修复】网卡 MAC 与 IP：收集所有真实的物理网卡并拼接
        public static (string MAC, string IP) GetNetworkInfo()
        {
            List<string> macList = new List<string>();
            List<string> ipList = new List<string>();

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 过滤掉未连接的网卡、回环网卡（127.0.0.1）和隧道网卡
                    if (nic.OperationalStatus == OperationalStatus.Up && 
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        // 过滤掉常见的虚拟机网卡（VMware, VirtualBox, Hyper-V 等）
                        string desc = nic.Description.ToLower();
                        if (desc.Contains("vmware") || desc.Contains("virtual") || desc.Contains("hyper-v"))
                            continue;

                        var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                        if (macBytes.Length == 6) // 标准 MAC 地址长度
                        {
                            string currentMac = string.Join("-", macBytes.Select(b => b.ToString("X2")));
                            string currentIp = "";

                            foreach (var ipInfo in nic.GetIPProperties().UnicastAddresses)
                            {
                                if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // 仅收集 IPv4
                                {
                                    currentIp = ipInfo.Address.ToString();
                                    break; // 只取该网卡的第一个 IPv4 地址即可
                                }
                            }

                            // 只有当该网卡真正分配到了 IP 时，我们才把它列出来
                            if (!string.IsNullOrEmpty(currentIp))
                            {
                                // 带上网卡名称前缀，例如 "WLAN: 192.168.1.x"
                                macList.Add($"{nic.Name}: {currentMac}");
                                ipList.Add($"{nic.Name}: {currentIp}");
                            }
                        }
                    }
                }
            }
            catch { }

            // 使用 " | " 将多个网卡信息拼接起来
            string finalMac = macList.Count > 0 ? string.Join(" | ", macList) : "Unknown";
            string finalIp = ipList.Count > 0 ? string.Join(" | ", ipList) : "Unknown";

            return (finalMac, finalIp);
        }
    }
}

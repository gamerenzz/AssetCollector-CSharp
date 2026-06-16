using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Collections.Generic;

namespace AssetCollector
{
    public class HardwareCollector
    {
        // 操作系统
        public static string GetOSInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem"))
                {
                    foreach (var item in searcher.Get())
                    {
                        return $"{item["Caption"]} (Build {item["BuildNumber"]})";
                    }
                }
            }
            catch { }
            return "Unknown OS";
        }

        // 处理器
        public static string GetCpuInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        return $"{item["Name"]?.ToString().Trim()} ({item["NumberOfCores"]}核/{item["NumberOfLogicalProcessors"]}线程)";
                    }
                }
            }
            catch { }
            return "Unknown CPU";
        }

        // 物理内存 (简单统计)
        public static string GetRamInfo()
        {
            try
            {
                double totalCapacity = 0;
                int count = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
                {
                    foreach (var item in searcher.Get())
                    {
                        totalCapacity += Convert.ToDouble(item["Capacity"]);
                        count++;
                    }
                }
                double gb = Math.Round(totalCapacity / (1024 * 1024 * 1024), 2);
                return $"{gb} GB ({count}根)";
            }
            catch { }
            return "Unknown RAM";
        }

        // 硬盘信息
        public static string GetDiskInfo()
        {
            try
            {
                List<string> disks = new List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive"))
                {
                    foreach (var item in searcher.Get())
                    {
                        if (item["Size"] != null)
                        {
                            double gb = Math.Round(Convert.ToDouble(item["Size"]) / (1024 * 1024 * 1024), 2);
                            disks.Add($"{item["Model"]?.ToString().Trim()} ({gb} GB)");
                        }
                    }
                }
                return string.Join(" | ", disks);
            }
            catch { }
            return "Unknown Disk";
        }

        // 主板信息
        public static string GetMotherboardInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (var item in searcher.Get())
                    {
                        return $"制造商: {item["Manufacturer"]} | 型号: {item["Product"]} | 序列号: {item["SerialNumber"]}";
                    }
                }
            }
            catch { }
            return "Unknown Motherboard";
        }

        // 显卡信息
        public static string GetGpuInfo()
        {
            try
            {
                List<string> gpus = new List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        string name = item["Name"]?.ToString().Trim();
                        // 过滤掉向日葵等虚拟显卡
                        if (!string.IsNullOrEmpty(name) && !name.ToLower().Contains("sunlogin"))
                        {
                            gpus.Add(name);
                        }
                    }
                }
                return string.Join(" | ", gpus);
            }
            catch { }
            return "Unknown GPU";
        }

        // 获取主网卡 MAC 和 IP
        public static (string MAC, string IP) GetNetworkInfo()
        {
            string mac = "Unknown";
            string ip = "Unknown";
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 过滤掉回环网卡和未连接的网卡
                    if (nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        mac = string.Join("-", BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Split('-'));
                        foreach (var ipInfo in nic.GetIPProperties().UnicastAddresses)
                        {
                            if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4
                            {
                                ip = ipInfo.Address.ToString();
                                return (mac, ip); // 取到第一个活动的物理网卡就返回
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

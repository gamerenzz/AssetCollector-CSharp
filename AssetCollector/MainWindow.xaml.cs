using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Win32;
using ClosedXML.Excel;
using System.IO;

namespace AssetCollector
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private List<ResultItem> currentResults = new List<ResultItem>();
        
        // 本地配置文件路径 (就在 exe 旁边)
        private readonly string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        // 防止启动时多次触发保存
        private bool isLoaded = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig(); // 界面初始化时读取配置
            isLoaded = true;
        }

        // ========== 本地配置保存与读取 ==========
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string json = File.ReadAllText(configFilePath, Encoding.UTF8);
                    var cfg = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (cfg != null)
                    {
                        if (cfg.ContainsKey("server_url")) TxtServerUrl.Text = cfg["server_url"];
                        if (cfg.ContainsKey("building")) TxtBuilding.Text = cfg["building"];
                        if (cfg.ContainsKey("floor")) TxtFloor.Text = cfg["floor"];
                        if (cfg.ContainsKey("department")) TxtDept.Text = cfg["department"];
                        if (cfg.ContainsKey("asset_type")) TxtAssetType.Text = cfg["asset_type"];
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            if (!isLoaded) return; // 界面还没加载完时不保存
            try
            {
                var cfg = new Dictionary<string, string>
                {
                    { "server_url", TxtServerUrl.Text.Trim() },
                    { "building", TxtBuilding.Text.Trim() },
                    { "floor", TxtFloor.Text.Trim() },
                    { "department", TxtDept.Text.Trim() },
                    { "asset_type", TxtAssetType.Text.Trim() }
                };
                File.WriteAllText(configFilePath, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch { }
        }

        // 当文本框改变时：检查按钮状态，并自动保存配置
        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtBuilding == null || TxtFloor == null || TxtDept == null || TxtAssetType == null || BtnScan == null) return;
            BtnScan.IsEnabled = (!string.IsNullOrWhiteSpace(TxtBuilding.Text) && !string.IsNullOrWhiteSpace(TxtFloor.Text) && !string.IsNullOrWhiteSpace(TxtDept.Text) && !string.IsNullOrWhiteSpace(TxtAssetType.Text));
            SaveConfig();
        }

        // ========== 核心扫描功能 ==========
        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false; BtnUpload.IsEnabled = false; BtnExport.IsEnabled = false;
            BtnScan.Content = "正在扫描...";
            ProgBar.Visibility = Visibility.Visible; ProgBar.Value = 0;

            currentResults = await Task.Run(() => PerformScan());
            GridResults.ItemsSource = currentResults;

            ProgBar.Value = 100;
            TxtStatus.Text = $"采集完成 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            BtnScan.Content = "重新扫描";
            BtnScan.IsEnabled = true; BtnUpload.IsEnabled = true; BtnExport.IsEnabled = true;
            
            await Task.Delay(2000);
            ProgBar.Visibility = Visibility.Hidden;
        }

        private List<ResultItem> PerformScan()
        {
            var list = new List<ResultItem>();
            UpdateProgress(10, "正在采集系统与型号...");
            list.Add(new ResultItem { Key = "计算机名称", Value = Environment.MachineName });
            list.Add(new ResultItem { Key = "用户名", Value = Environment.UserName });
            list.Add(new ResultItem { Key = "整机型号", Value = HardwareCollector.GetSystemModel() }); // 新增型号
            list.Add(new ResultItem { Key = "操作系统", Value = HardwareCollector.GetOSInfo() });
            
            UpdateProgress(30, "正在采集处理器和内存...");
            list.Add(new ResultItem { Key = "处理器", Value = HardwareCollector.GetCpuInfo() });
            list.Add(new ResultItem { Key = "内存", Value = HardwareCollector.GetRamInfo() });
            
            UpdateProgress(50, "正在采集磁盘信息...");
            list.Add(new ResultItem { Key = "磁盘", Value = HardwareCollector.GetDiskInfo() });
            
            UpdateProgress(70, "正在采集网络信息...");
            var netInfo = HardwareCollector.GetNetworkInfo();
            list.Add(new ResultItem { Key = "IP地址", Value = netInfo.IP });
            list.Add(new ResultItem { Key = "MAC地址", Value = netInfo.MAC });
            
            UpdateProgress(90, "正在采集显示及外设信息...");
            list.Add(new ResultItem { Key = "主板信息", Value = HardwareCollector.GetMotherboardInfo() });
            list.Add(new ResultItem { Key = "显卡", Value = HardwareCollector.GetGpuInfo() });
            list.Add(new ResultItem { Key = "外接显示器", Value = HardwareCollector.GetMonitorInfo() }); // 新增显示器

            return list;
        }

        private void UpdateProgress(int percent, string message)
        {
            Dispatcher.Invoke(() => { ProgBar.Value = percent; TxtStatus.Text = message; });
        }

        // ========== 数据打包、上传、导出 (逻辑保持不变) ==========
        private Dictionary<string, string> BuildPayload()
        {
            var payload = new Dictionary<string, string>();
            payload["server_url"] = TxtServerUrl.Text.Trim(); // 连同URL一起抓出来方便后续修改
            payload["building"] = TxtBuilding.Text.Trim();
            payload["floor"] = TxtFloor.Text.Trim();
            payload["department"] = TxtDept.Text.Trim();
            payload["type"] = TxtAssetType.Text.Trim();
            foreach (var item in currentResults) payload[item.Key] = item.Value;
            return payload;
        }

        private async void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            BtnUpload.IsEnabled = false; BtnUpload.Content = "上传中..."; TxtStatus.Text = "正在连接服务器...";
            try
            {
                var payload = BuildPayload();
                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string url = TxtServerUrl.Text.Trim().TrimEnd('/') + "/api/upload";
                
                var response = await httpClient.PostAsync(url, content);
                string resBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show("资产数据已成功上传至服务器！\n服务器响应: " + resBody, "上传成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtStatus.Text = "上传成功";
                }
                else
                {
                    MessageBox.Show($"上传失败，服务器返回错误：\n状态码: {response.StatusCode}\n详情: {resBody}", "上传失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtStatus.Text = "上传失败";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"网络请求发生错误：\n\n{ex.Message}", "网络连接错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "上传出错";
            }
            finally
            {
                BtnUpload.Content = "上传至服务器"; BtnUpload.IsEnabled = true;
            }
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var payload = BuildPayload();
            string defaultName = $"{payload["building"]}_{payload["department"]}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "Excel 文件 (*.xlsx)|*.xlsx", FileName = defaultName, Title = "导出资产数据" };

            if (saveFileDialog.ShowDialog() == true)
            {
                BtnExport.IsEnabled = false; BtnExport.Content = "导出中...";
                try
                {
                    await Task.Run(() =>
                    {
                        using (var workbook = new XLWorkbook())
                        {
                            var worksheet = workbook.Worksheets.Add("资产信息");
                            int col = 1;
                            foreach (var key in payload.Keys)
                            {
                                worksheet.Cell(1, col).Value = key;
                                worksheet.Cell(1, col).Style.Font.Bold = true;
                                worksheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
                                col++;
                            }
                            col = 1;
                            foreach (var val in payload.Values)
                            {
                                worksheet.Cell(2, col).Value = val;
                                col++;
                            }
                            worksheet.Columns().AdjustToContents();
                            workbook.SaveAs(saveFileDialog.FileName);
                        }
                    });
                    MessageBox.Show("Excel文件已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出时发生错误：\n\n{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { BtnExport.Content = "导出数据 (Excel)"; BtnExport.IsEnabled = true; }
            }
        }
    }

    public class ResultItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

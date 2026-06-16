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
        
        private readonly string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private bool isLoaded = false;

        // 【新增】保存动态字段的名-值字典
        private readonly Dictionary<string, string> customFields = new Dictionary<string, string>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig(); 
            isLoaded = true;
        }

        // ========== 本地配置导入与窗口恢复 ==========
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string json = File.ReadAllText(configFilePath, Encoding.UTF8);
                    var cfg = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (cfg != null)
                    {
                        // 1. 恢复标准输入框
                        if (cfg.ContainsKey("server_url")) TxtServerUrl.Text = cfg["server_url"].ToString();
                        if (cfg.ContainsKey("building")) TxtBuilding.Text = cfg["building"].ToString();
                        if (cfg.ContainsKey("floor")) TxtFloor.Text = cfg["floor"].ToString();
                        if (cfg.ContainsKey("department")) TxtDept.Text = cfg["department"].ToString();
                        if (cfg.ContainsKey("asset_type")) TxtAssetType.Text = cfg["asset_type"].ToString();

                        // 2. 恢复窗口大小与位置 (UX Polish)
                        if (cfg.ContainsKey("window_width")) this.Width = Convert.ToDouble(cfg["window_width"]);
                        if (cfg.ContainsKey("window_height")) this.Height = Convert.ToDouble(cfg["window_height"]);
                        if (cfg.ContainsKey("window_top")) this.Top = Convert.ToDouble(cfg["window_top"]);
                        if (cfg.ContainsKey("window_left")) this.Left = Convert.ToDouble(cfg["window_left"]);

                        // 3. 恢复动态自定义字段
                        if (cfg.ContainsKey("custom_fields"))
                        {
                            var customObj = JsonConvert.DeserializeObject<Dictionary<string, string>>(cfg["custom_fields"].ToString());
                            if (customObj != null)
                            {
                                foreach (var kv in customObj)
                                {
                                    AddCustomFieldUI(kv.Key, kv.Value);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // ========== 配置与窗口尺寸自动保存 ==========
        private void SaveConfig()
        {
            if (!isLoaded) return; 
            try
            {
                var cfg = new Dictionary<string, object>
                {
                    { "server_url", TxtServerUrl.Text.Trim() },
                    { "building", TxtBuilding.Text.Trim() },
                    { "floor", TxtFloor.Text.Trim() },
                    { "department", TxtDept.Text.Trim() },
                    { "asset_type", TxtAssetType.Text.Trim() },
                    
                    // 保存窗口状态 (UX Polish)
                    { "window_width", this.ActualWidth },
                    { "window_height", this.ActualHeight },
                    { "window_top", this.Top },
                    { "window_left", this.Left },

                    // 保存自定义字段
                    { "custom_fields", JsonConvert.SerializeObject(customFields) }
                };
                File.WriteAllText(configFilePath, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch { }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtBuilding == null || TxtFloor == null || TxtDept == null || TxtAssetType == null || BtnScan == null) return;
            BtnScan.IsEnabled = (!string.IsNullOrWhiteSpace(TxtBuilding.Text) && !string.IsNullOrWhiteSpace(TxtFloor.Text) && !string.IsNullOrWhiteSpace(TxtDept.Text) && !string.IsNullOrWhiteSpace(TxtAssetType.Text));
            SaveConfig();
        }

        // 窗口关闭时，做最后一次保存
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveConfig();
        }

        // ========== 动态自定义字段功能 ==========
        private void BtnAddField_Click(object sender, RoutedEventArgs e)
        {
            // 动态在程序内生成一个小型对话框 Window，避免多余文件
            Window dialog = new Window
            {
                Title = "新增自定义字段",
                Width = 320,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = this.FontFamily
            };

            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 字段名输入
            StackPanel sp1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sp1.Children.Add(new Label { Content = "字段名称:", Width = 70 });
            TextBox txtName = new TextBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp1.Children.Add(txtName);
            Grid.SetRow(sp1, 0);

            // 字段内容输入
            StackPanel sp2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
            sp2.Children.Add(new Label { Content = "默认内容:", Width = 70 });
            TextBox txtValue = new TextBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp2.Children.Add(txtValue);
            Grid.SetRow(sp2, 1);

            // 确定/取消按钮
            StackPanel sp3 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnOk = new Button { Content = "确定", Width = 70, Height = 25, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
            Button btnCancel = new Button { Content = "取消", Width = 70, Height = 25, IsCancel = true };
            sp3.Children.Add(btnOk);
            sp3.Children.Add(btnCancel);
            Grid.SetRow(sp3, 2);

            grid.Children.Add(sp1);
            grid.Children.Add(sp2);
            grid.Children.Add(sp3);
            dialog.Content = grid;

            btnOk.Click += (o, a) =>
            {
                string name = txtName.Text.Trim();
                string val = txtValue.Text.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("字段名称不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (customFields.ContainsKey(name))
                {
                    MessageBox.Show("该字段已经存在！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                AddCustomFieldUI(name, val);
                dialog.DialogResult = true;
            };

            dialog.ShowDialog();
        }

        // 将动态创建的字段渲染到 UI 面板上
        private void AddCustomFieldUI(string name, string value)
        {
            if (customFields.ContainsKey(name) && isLoaded) return;

            // 创建容器
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 15, 5) };
            panel.Children.Add(new Label { Content = name + ":", Width = 60, VerticalAlignment = VerticalAlignment.Center });
            
            TextBox txt = new TextBox { Text = value, Width = 120, VerticalContentAlignment = VerticalAlignment.Center };
            panel.Children.Add(txt);

            Button btnDel = new Button
            {
                Content = "×",
                Width = 20,
                Height = 20,
                Margin = new Thickness(5, 0, 0, 0),
                Background = System.Windows.Media.Brushes.LightCoral,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                ToolTip = "删除此字段"
            };
            panel.Children.Add(btnDel);

            // 文本框改变事件
            txt.TextChanged += (s, e) =>
            {
                customFields[name] = txt.Text.Trim();
                SaveConfig();
            };

            // 删除按钮点击事件
            btnDel.Click += (s, e) =>
            {
                CustomFieldsContainer.Children.Remove(panel);
                customFields.Remove(name);
                SaveConfig();
            };

            CustomFieldsContainer.Children.Add(panel);
            customFields[name] = value;
            SaveConfig();
        }

        // ========== 扫描动作 ==========
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
            list.Add(new ResultItem { Key = "整机型号", Value = HardwareCollector.GetSystemModel() });
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
            
            UpdateProgress(85, "正在采集显示及主板信息...");
            list.Add(new ResultItem { Key = "主板信息", Value = HardwareCollector.GetMotherboardInfo() });
            list.Add(new ResultItem { Key = "显卡", Value = HardwareCollector.GetGpuInfo() });
            list.Add(new ResultItem { Key = "外接显示器", Value = HardwareCollector.GetMonitorInfo() });

            UpdateProgress(95, "正在扫描全机已安装软件(这可能需要1-2秒)...");
            list.Add(new ResultItem { Key = "已安装软件", Value = HardwareCollector.GetInstalledSoftware() }); // 扫描全机软件

            return list;
        }

        private void UpdateProgress(int percent, string message)
        {
            Dispatcher.Invoke(() => { ProgBar.Value = percent; TxtStatus.Text = message; });
        }

        // ========== 数据打包、上传、导出 ==========
        private Dictionary<string, string> BuildPayload()
        {
            var payload = new Dictionary<string, string>();
            payload["server_url"] = TxtServerUrl.Text.Trim();
            payload["building"] = TxtBuilding.Text.Trim();
            payload["floor"] = TxtFloor.Text.Trim();
            payload["department"] = TxtDept.Text.Trim();
            payload["type"] = TxtAssetType.Text.Trim();

            // 连同动态自定义字段打包
            foreach (var kv in customFields)
            {
                payload[kv.Key] = kv.Value;
            }

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

        // ========== UX Polish: 帮助关于弹窗 ==========
        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "终端资产管理平台-客户端 (C# .NET 4.7) v2.0.0\n\n" +
                "开发人员：Tonekey2016\n\n" +
                "功能特点：\n" +
                "1. 高速异步扫描，杜绝界面卡顿假死。\n" +
                "2. 深度检索注册表，完整获取全机安装软件名录。\n" +
                "3. 完美适配有线/无线双物理网卡，自动净化向日葵、ToDesk等虚拟显卡设备。\n" +
                "4. 支持动态增删自定义盘点参数，随存随导。\n" +
                "5. 自动记录配置与窗口大小、位置尺寸（启动时自动还原）。",
                "帮助说明 / 关于软件", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public class ResultItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

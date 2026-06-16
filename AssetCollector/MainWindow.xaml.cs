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
using System.Drawing; // 引入托盘图标所需的绘图库

namespace AssetCollector
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private List<ResultItem> currentResults = new List<ResultItem>();
        private List<SoftwareItem> currentSoftwareResults = new List<SoftwareItem>();
        
        private readonly string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private bool isLoaded = false;
        private readonly Dictionary<string, string> customFields = new Dictionary<string, string>();

        // 【新增】托盘与安全控制变量
        private System.Windows.Forms.NotifyIcon trayIcon;
        private bool isForceExit = false; // 是否通过了密码验证，允许真正退出
        private const string ExitPasswordHash = "admin123"; // 默认安全退出密码

        // 构造函数，支持接收“是否默认隐藏”参数
        public MainWindow(bool startMinimized)
        {
            InitializeComponent();
            InitializeTrayIcon(); // 初始化系统托盘
            LoadConfig(); 
            isLoaded = true;

            // 如果是开机自启参数运行，立刻隐藏到托盘
            if (startMinimized)
            {
                this.WindowState = WindowState.Minimized;
                this.Hide();
                this.ShowInTaskbar = false;
            }
            
            CheckAutoStartStatus(); // 刷新开机自启复选框状态
        }

        // ========== UX Polish: 初始化系统托盘 ==========
        private void InitializeTrayIcon()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            // 直接获取 Windows 默认的系统图标，无需外挂图标文件，防止打包丢失
            trayIcon.Icon = SystemIcons.Shield; 
            trayIcon.Text = "终端资产管理平台";
            trayIcon.Visible = true;

            // 托盘双击事件 -> 恢复并激活窗口
            trayIcon.DoubleClick += (s, e) => RestoreWindow();

            // 创建托盘右键菜单
            var contextMenu = new System.Windows.Forms.ContextMenu();
            contextMenu.MenuItems.Add("打开主界面", (s, e) => RestoreWindow());
            contextMenu.MenuItems.Add("安全退出", (s, e) => TriggerExitWithPassword());
            trayIcon.ContextMenu = contextMenu;
        }

        private void RestoreWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }

        // 主窗口最小化时，自动缩到托盘并隐藏任务栏图标
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        // ========== 安全防护：拦截关闭事件并校验密码 ==========
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!isForceExit)
            {
                // 如果没有输入正确密码，拦截退出事件，展示密码验证遮罩
                e.Cancel = true;
                TriggerExitWithPassword();
            }
        }

        private void TriggerExitWithPassword()
        {
            RestoreWindow(); // 确保窗口拉回前台可见
            PasswordOverlay.Visibility = Visibility.Visible;
            TxtExitPassword.Focus();
        }

        private void BtnConfirmExit_Click(object sender, RoutedEventArgs e)
        {
            if (TxtExitPassword.Password == ExitPasswordHash)
            {
                // 密码正确，解除关闭拦截锁，彻底退出
                isForceExit = true;
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
                SaveConfig();
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show("验证密码错误！无法关闭客户端。", "安全验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtExitPassword.Clear();
            }
        }

        private void BtnCancelExit_Click(object sender, RoutedEventArgs e)
        {
            PasswordOverlay.Visibility = Visibility.Collapsed;
            TxtExitPassword.Clear();
        }

        // ========== 自动自启写入 Windows 注册表 ==========
        private void CheckAutoStartStatus()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("TerminalAssetCollector");
                        ChkAutoStart.IsChecked = (val != null);
                    }
                }
            }
            catch { }
        }

        private void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    // 开机启动时传入 -startup 参数，实现默认隐藏在右下角
                    key.SetValue("TerminalAssetCollector", $"\"{exePath}\" -startup");
                }
            }
            catch { }
        }

        private void ChkAutoStart_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key.DeleteValue("TerminalAssetCollector", false);
                }
            }
            catch { }
        }

        // ========== 本地配置与状态维护 ==========
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
                        if (cfg.ContainsKey("server_url")) TxtServerUrl.Text = cfg["server_url"].ToString();
                        if (cfg.ContainsKey("building")) TxtBuilding.Text = cfg["building"].ToString();
                        if (cfg.ContainsKey("floor")) TxtFloor.Text = cfg["floor"].ToString();
                        if (cfg.ContainsKey("department")) TxtDept.Text = cfg["department"].ToString();
                        if (cfg.ContainsKey("asset_type")) TxtAssetType.Text = cfg["asset_type"].ToString();
                        if (cfg.ContainsKey("window_width")) this.Width = Convert.ToDouble(cfg["window_width"]);
                        if (cfg.ContainsKey("window_height")) this.Height = Convert.ToDouble(cfg["window_height"]);
                        if (cfg.ContainsKey("window_top")) this.Top = Convert.ToDouble(cfg["window_top"]);
                        if (cfg.ContainsKey("window_left")) this.Left = Convert.ToDouble(cfg["window_left"]);

                        if (cfg.ContainsKey("custom_fields"))
                        {
                            var customObj = JsonConvert.DeserializeObject<Dictionary<string, string>>(cfg["custom_fields"].ToString());
                            if (customObj != null)
                            {
                                foreach (var kv in customObj) AddCustomFieldUI(kv.Key, kv.Value);
                            }
                        }
                    }
                }
            }
            catch { }
        }

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
                    { "window_width", this.ActualWidth },
                    { "window_height", this.ActualHeight },
                    { "window_top", this.Top },
                    { "window_left", this.Left },
                    { "custom_fields", JsonConvert.SerializeObject(customFields) }
                };
                File.WriteAllText(configFilePath, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch { }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtBuilding == null || TxtFloor == null || TxtDept == null || TxtAssetType == null || BtnScan == null || BtnScanSoftware == null) return;
            bool enable = (!string.IsNullOrWhiteSpace(TxtBuilding.Text) && !string.IsNullOrWhiteSpace(TxtFloor.Text) && !string.IsNullOrWhiteSpace(TxtDept.Text) && !string.IsNullOrWhiteSpace(TxtAssetType.Text));
            BtnScan.IsEnabled = enable;
            BtnScanSoftware.IsEnabled = enable;
            SaveConfig();
        }

        // ========== 动态字段 ==========
        private void BtnAddField_Click(object sender, RoutedEventArgs e)
        {
            Window dialog = new Window { Title = "新增自定义字段", Width = 320, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.White, FontFamily = this.FontFamily };
            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel sp1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sp1.Children.Add(new Label { Content = "字段名称:", Width = 70 });
            TextBox txtName = new TextBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp1.Children.Add(txtName);
            Grid.SetRow(sp1, 0);

            StackPanel sp2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
            sp2.Children.Add(new Label { Content = "默认内容:", Width = 70 });
            TextBox txtValue = new TextBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp2.Children.Add(txtValue);
            Grid.SetRow(sp2, 1);

            StackPanel sp3 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnOk = new Button { Content = "确定", Width = 70, Height = 25, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
            Button btnCancel = new Button { Content = "取消", Width = 70, Height = 25, IsCancel = true };
            sp3.Children.Add(btnOk); sp3.Children.Add(btnCancel);
            Grid.SetRow(sp3, 2);

            grid.Children.Add(sp1); grid.Children.Add(sp2); grid.Children.Add(sp3);
            dialog.Content = grid;

            btnOk.Click += (o, a) =>
            {
                string name = txtName.Text.Trim(); string val = txtValue.Text.Trim();
                if (string.IsNullOrEmpty(name)) { MessageBox.Show("字段名称不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (customFields.ContainsKey(name)) { MessageBox.Show("该字段已经存在！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                AddCustomFieldUI(name, val);
                dialog.DialogResult = true;
            };
            dialog.ShowDialog();
        }

        private void AddCustomFieldUI(string name, string value)
        {
            if (customFields.ContainsKey(name) && isLoaded) return;
            StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 15, 5) };
            panel.Children.Add(new Label { Content = name + ":", Width = 60, VerticalAlignment = VerticalAlignment.Center });
            TextBox txt = new TextBox { Text = value, Width = 120, VerticalContentAlignment = VerticalAlignment.Center };
            panel.Children.Add(txt);
            Button btnDel = new Button { Content = "×", Width = 20, Height = 20, Margin = new Thickness(5, 0, 0, 0), Background = System.Windows.Media.Brushes.LightCoral, Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), ToolTip = "删除此字段" };
            panel.Children.Add(btnDel);

            txt.TextChanged += (s, e) => { customFields[name] = txt.Text.Trim(); SaveConfig(); };
            btnDel.Click += (s, e) => { CustomFieldsContainer.Children.Remove(panel); customFields.Remove(name); SaveConfig(); };

            CustomFieldsContainer.Children.Add(panel);
            customFields[name] = value;
            SaveConfig();
        }

        // ========== 1. 扫描硬件动作 ==========
        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            LockUI(true);
            BtnScan.Content = "正在扫描...";
            ProgBar.Visibility = Visibility.Visible; ProgBar.Value = 0;

            TabResultContainer.SelectedItem = TabHardware;

            currentResults = await Task.Run(() => PerformScanHardware());
            GridResults.ItemsSource = currentResults;

            ProgBar.Value = 100;
            TxtStatus.Text = $"硬件采集完成 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            BtnScan.Content = "重新扫描硬件";
            LockUI(false);
            
            await Task.Delay(2000);
            ProgBar.Visibility = Visibility.Hidden;
        }

        private List<ResultItem> PerformScanHardware()
        {
            var list = new List<ResultItem>();
            UpdateProgress(10, "正在采集系统与型号...");
            list.Add(new ResultItem { Key = "计算机名称", Value = Environment.MachineName });
            list.Add(new ResultItem { Key = "用户名", Value = Environment.UserName });
            list.Add(new ResultItem { Key = "整机型号", Value = HardwareCollector.GetSystemModel() });
            list.Add(new ResultItem { Key = "操作系统", Value = HardwareCollector.GetOSInfo() });
            UpdateProgress(40, "正在采集处理器和内存...");
            list.Add(new ResultItem { Key = "处理器", Value = HardwareCollector.GetCpuInfo() });
            list.Add(new ResultItem { Key = "内存", Value = HardwareCollector.GetRamInfo() });
            UpdateProgress(70, "正在采集磁盘信息...");
            list.Add(new ResultItem { Key = "磁盘", Value = HardwareCollector.GetDiskInfo() });
            UpdateProgress(85, "正在采集网络信息...");
            var netInfo = HardwareCollector.GetNetworkInfo();
            list.Add(new ResultItem { Key = "IP地址", Value = netInfo.IP });
            list.Add(new ResultItem { Key = "MAC地址", Value = netInfo.MAC });
            UpdateProgress(95, "正在采集显示及主板信息...");
            list.Add(new ResultItem { Key = "主板信息", Value = HardwareCollector.GetMotherboardInfo() });
            list.Add(new ResultItem { Key = "显卡", Value = HardwareCollector.GetGpuInfo() });
            list.Add(new ResultItem { Key = "外接显示器", Value = HardwareCollector.GetMonitorInfo() });
            return list;
        }

        // ========== 2. 扫描软件动作 ==========
        private async void BtnScanSoftware_Click(object sender, RoutedEventArgs e)
        {
            LockUI(true);
            BtnScanSoftware.Content = "正在扫描...";
            ProgBar.Visibility = Visibility.Visible; ProgBar.Value = 0;

            TabResultContainer.SelectedItem = TabSoftware;

            UpdateProgress(30, "正在连接并解析系统注册表...");
            currentSoftwareResults = await Task.Run(() => HardwareCollector.GetInstalledSoftwareList());
            GridSoftwareResults.ItemsSource = currentSoftwareResults;

            ProgBar.Value = 100;
            TxtStatus.Text = $"软件扫描完成，共找到 {currentSoftwareResults.Count} 款应用 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            BtnScanSoftware.Content = "重新扫描软件";
            LockUI(false);

            await Task.Delay(2000);
            ProgBar.Visibility = Visibility.Hidden;
        }

        private void LockUI(bool isLocked)
        {
            if (isLocked)
            {
                BtnScan.IsEnabled = false; BtnScanSoftware.IsEnabled = false;
                BtnUpload.IsEnabled = false; BtnExport.IsEnabled = false;
            }
            else
            {
                BtnScan.IsEnabled = true; BtnScanSoftware.IsEnabled = true;
                BtnUpload.IsEnabled = true; BtnExport.IsEnabled = true;
            }
        }

        private void UpdateProgress(int percent, string message)
        {
            Dispatcher.Invoke(() => { ProgBar.Value = percent; TxtStatus.Text = message; });
        }

        // ========== 数据打包、上传、导出 ==========
        private Dictionary<string, object> BuildPayload()
        {
            var payload = new Dictionary<string, object>();
            payload["server_url"] = TxtServerUrl.Text.Trim();
            payload["building"] = TxtBuilding.Text.Trim();
            payload["floor"] = TxtFloor.Text.Trim();
            payload["department"] = TxtDept.Text.Trim();
            payload["type"] = TxtAssetType.Text.Trim();

            foreach (var kv in customFields) payload[kv.Key] = kv.Value;
            foreach (var item in currentResults) payload[item.Key] = item.Value;
            
            if (currentSoftwareResults != null && currentSoftwareResults.Count > 0)
            {
                payload["software_list"] = currentSoftwareResults;
            }
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
                    MessageBox.Show("资产数据已成功上传！\n服务器响应: " + resBody, "上传成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtStatus.Text = "上传成功";
                }
                else
                {
                    MessageBox.Show($"上传失败：\n状态码: {response.StatusCode}\n详情: {resBody}", "上传失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtStatus.Text = "上传失败";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"网络请求错误：\n\n{ex.Message}", "网络连接错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "上传出错";
            }
            finally { BtnUpload.Content = "上传至服务器"; BtnUpload.IsEnabled = true; }
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
                            var wsHardware = workbook.Worksheets.Add("硬件信息");
                            int col = 1;
                            foreach (var key in payload.Keys)
                            {
                                if (key == "software_list") continue;
                                wsHardware.Cell(1, col).Value = key;
                                wsHardware.Cell(1, col).Style.Font.Bold = true;
                                wsHardware.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
                                col++;
                            }
                            col = 1;
                            foreach (var kv in payload)
                            {
                                if (kv.Key == "software_list") continue;
                                wsHardware.Cell(2, col).Value = kv.Value?.ToString() ?? "";
                                col++;
                            }
                            wsHardware.Columns().AdjustToContents();

                            if (currentSoftwareResults != null && currentSoftwareResults.Count > 0)
                            {
                                var wsSoftware = workbook.Worksheets.Add("软件清单");
                                string[] headers = { "软件名称", "版本号", "安装日期" };
                                for (int i = 0; i < headers.Length; i++)
                                {
                                    wsSoftware.Cell(1, i + 1).Value = headers[i];
                                    wsSoftware.Cell(1, i + 1).Style.Font.Bold = true;
                                    wsSoftware.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightSlateGray;
                                    wsSoftware.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
                                }
                                int rowIdx = 2;
                                foreach (var sw in currentSoftwareResults)
                                {
                                    wsSoftware.Cell(rowIdx, 1).Value = sw.Name;
                                    wsSoftware.Cell(rowIdx, 2).Value = sw.Version;
                                    wsSoftware.Cell(rowIdx, 3).Value = sw.InstallDate;
                                    rowIdx++;
                                }
                                wsSoftware.Columns().AdjustToContents();
                            }

                            workbook.SaveAs(saveFileDialog.FileName);
                        }
                    });
                    MessageBox.Show("资产账册已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出时发生错误：\n\n{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { BtnExport.Content = "导出数据 (Excel)"; BtnExport.IsEnabled = true; }
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "终端资产管理平台-客户端 (C# .NET 4.7) v2.0.0\n\n" +
                "开发人员：Tonekey2016\n\n" +
                "安全防护与控制说明：\n" +
                "1. 单实例防护：系统仅允许运行一个客户端。双击新程序会自动唤醒现有进程并拉回前台置顶。\n" +
                "2. 托盘最小化：程序默认启动或点击最小化时将完全缩入右下角托盘盾牌图标中。\n" +
                "3. 密码防退出：拦截所有常规关闭、Alt+F4。退出必须通过托盘右键/窗口关闭弹出遮罩校验密码(默认：admin123)。\n" +
                "4. 智能开机启动：勾选开机启动写入注册表，随Windows自动隐式后台启动(-startup)，对终端用户静默无感。\n" +
                "5. 极致体验：支持自动记忆窗口的大小与显示位置；支持导出硬件、软件分立的专业级多Sheet Excel表格。",
                "帮助说明 / 安全与托盘控制", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public class ResultItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

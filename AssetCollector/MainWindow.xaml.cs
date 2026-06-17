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
using System.Drawing;
using System.Timers;

namespace AssetCollector
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) }; 
        private List<ResultItem> currentResults = new List<ResultItem>();
        private List<SoftwareItem> currentSoftwareResults = new List<SoftwareItem>();
        
        private readonly string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private readonly string queueFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "queue.dat");
        
        private bool isLoaded = false;
        private readonly Dictionary<string, string> customFields = new Dictionary<string, string>();

        private System.Windows.Forms.NotifyIcon trayIcon;
        private bool isForceExit = false; 
        private string ExitPassword = "admin123"; 

        private Timer syncTimer;
        private static readonly object syncLock = new object();
        private bool isSyncing = false;

        private Window logWindow; 
        private TextBox logTextBox;

        public MainWindow(bool startMinimized)
        {
            InitializeComponent();
            DebugLogger.Log("INFO", "=== 终端资产管理客户端初始化启动 ===");
            InitializeTrayIcon(); 
            LoadConfig(); 
            isLoaded = true;

            if (startMinimized)
            {
                DebugLogger.Log("INFO", "检测到开机自启参数 -startup，静默缩入右下角托盘");
                this.WindowState = WindowState.Minimized;
                this.Hide();
                this.ShowInTaskbar = false;
            }
            
            CheckAutoStartStatus();
            InitializeSyncTimer(); 
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DebugLogger.Log("INFO", "客户端主界面渲染加载完成。");
            await Task.Run(() => SyncPolicyFromServerAsync());

            if (!string.IsNullOrWhiteSpace(TxtBuilding.Text) && 
                !string.IsNullOrWhiteSpace(TxtFloor.Text) && 
                !string.IsNullOrWhiteSpace(TxtDept.Text))
            {
                var queue = LoadQueue();
                if (CurrentPolicy.CollectHardware || CurrentPolicy.CollectSoftware || queue.Count > 0)
                {
                    UpdateStatusText("正在执行自动上报任务...");
                    DebugLogger.Log("INFO", "检测到位置信息齐备，开始触发启动时后台自动静默采集任务。");
                    await Task.Run(() => PerformSilentScanAndEnqueue());
                }
            }
        }

        private void PerformSilentScanAndEnqueue()
        {
            try
            {
                var hwList = new List<ResultItem>();
                var swList = new List<SoftwareItem>();

                if (CurrentPolicy.CollectHardware)
                {
                    DebugLogger.Log("INFO", "策略控制：执行后台静默硬件扫描...");
                    hwList = PerformScanHardware();
                }
                else
                {
                    DebugLogger.Log("WARN", "策略控制：服务器要求禁止扫描硬件，已跳过。");
                }

                if (CurrentPolicy.CollectSoftware)
                {
                    DebugLogger.Log("INFO", "策略控制：执行后台静默软件明细解析...");
                    swList = HardwareCollector.GetInstalledSoftwareList();
                }
                else
                {
                    DebugLogger.Log("WARN", "策略控制：服务器要求禁止扫描软件清单，已跳过。");
                }

                var payload = BuildPayloadInternal(hwList, swList);
                EnqueuePayload(payload);
                Task.Run(() => FlushQueueSequentialAsync());
            }
            catch (Exception ex)
            {
                DebugLogger.Log("ERROR", "执行后台静默扫描入队时发生异常。", ex);
            }
        }

        private List<Dictionary<string, object>> LoadQueue()
        {
            lock (syncLock)
            {
                try
                {
                    if (File.Exists(queueFilePath))
                    {
                        string base64 = File.ReadAllText(queueFilePath, Encoding.UTF8);
                        if (!string.IsNullOrEmpty(base64))
                        {
                            string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                            var list = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                            return list ?? new List<Dictionary<string, object>>();
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("ERROR", "从本地 queue.dat 加载离线缓存失败，可能是缓存损坏。", ex);
                }
                return new List<Dictionary<string, object>>();
            }
        }

        private void SaveQueue(List<Dictionary<string, object>> queue)
        {
            lock (syncLock)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(queue);
                    string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                    File.WriteAllText(queueFilePath, base64, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("ERROR", "写本地离线 queue.dat 缓存失败！", ex);
                }
            }
        }

        private void EnqueuePayload(Dictionary<string, object> payload)
        {
            var queue = LoadQueue();
            if (queue.Count > 20)
            {
                DebugLogger.Log("WARN", "离线缓存队列超过 20 条，抛弃最老的一条。");
                queue.RemoveAt(0);
            }

            queue.Add(payload);
            SaveQueue(queue);
            DebugLogger.Log("INFO", "成功将 1 条最新扫描数据安全缓存在本地队列中。");
            UpdateStatusText($"本地已缓存 1 条数据");
        }

        private void InitializeSyncTimer()
        {
            syncTimer = new Timer(120000); 
            syncTimer.Elapsed += async (s, e) =>
            {
                DebugLogger.Log("INFO", "=== 后台定时重传轮询触发 ===");
                await SyncPolicyFromServerAsync(); 
                await FlushQueueSequentialAsync(); 
            };
            syncTimer.AutoReset = true;
            syncTimer.Start();
            DebugLogger.Log("INFO", "定时同步器已加载 (Interval: 120秒)。");

            Task.Run(async () =>
            {
                await SyncPolicyFromServerAsync();
                await FlushQueueSequentialAsync();
            });
        }

        private async Task SyncPolicyFromServerAsync()
        {
            try
            {
                var netInfo = HardwareCollector.GetNetworkInfo();
                string primaryMac = netInfo.MAC;
                
                if (primaryMac.Contains("|")) primaryMac = primaryMac.Split('|')[0].Trim();
                if (primaryMac.Contains(":")) primaryMac = primaryMac.Split(':')[1].Trim();

                if (string.IsNullOrEmpty(primaryMac) || primaryMac == "Unknown")
                {
                    DebugLogger.Log("WARN", "无法获取主网卡 MAC 地址，安全对齐机制挂起。");
                    return;
                }

                var reqObj = new { MacAddress = primaryMac };
                string json = JsonConvert.SerializeObject(reqObj);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string url = TxtServerUrl.Text.Trim().TrimEnd('/') + "/api/heartbeat";

                DebugLogger.Log("INFO", $"正在向服务器对齐最新上报策略。API: {url}");
                var response = await httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    string resBody = await response.Content.ReadAsStringAsync();
                    var policy = JsonConvert.DeserializeObject<Dictionary<string, object>>(resBody);
                    if (policy != null)
                    {
                        CurrentPolicy.CollectHardware = Convert.ToBoolean(policy["collect_hardware"]);
                        CurrentPolicy.CollectSoftware = Convert.ToBoolean(policy["collect_software"]);
                        int intervalMinutes = Convert.ToInt32(policy["scan_interval_minutes"]);

                        DebugLogger.Log("INFO", $"策略对齐成功：收集硬件={CurrentPolicy.CollectHardware} | 收集软件={CurrentPolicy.CollectSoftware} | 周期={intervalMinutes}分钟");

                        if (intervalMinutes == 0)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (syncTimer.Interval != 300000)
                                {
                                    syncTimer.Interval = 300000;
                                    DebugLogger.Log("INFO", "策略控制：已切入[单次上报静默休眠]状态，主定时器降为 5分钟 缓慢心跳。");
                                }
                            });
                        }
                        else
                        {
                            double intervalMs = intervalMinutes * 60.0 * 1000.0;
                            if (intervalMs < 120000) intervalMs = 120000;

                            Dispatcher.Invoke(() =>
                            {
                                if (Math.Abs(syncTimer.Interval - intervalMs) > 100)
                                {
                                    syncTimer.Interval = intervalMs;
                                    DebugLogger.Log("INFO", $"策略控制：策略上报周期已动态变更为 {intervalMinutes} 分钟一次。");
                                }
                            });
                        }
                    }
                }
                else
                {
                    DebugLogger.Log("WARN", $"心跳策略对齐失败：服务器返回了错误的状态码 [{(int)response.StatusCode} {response.ReasonPhrase}]。");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("ERROR", "网络异常：向服务器拉取策略规则失败。请检查服务器 IP 端口及防火墙策略！", ex);
            } 
        }

        private async Task FlushQueueSequentialAsync()
        {
            lock (syncLock)
            {
                if (isSyncing) return;
                isSyncing = true;
            }

            try
            {
                var queue = LoadQueue();
                if (queue.Count == 0)
                {
                    UpdateStatusText("准备就绪");
                    return;
                }

                UpdateStatusText($"正在后台同步队列（剩余：{queue.Count} 个待上报）...");
                DebugLogger.Log("INFO", $"开始按序清理并发送积压队列中的 {queue.Count} 个包...");

                while (queue.Count > 0)
                {
                    var item = queue[0];
                    string serverUrl = item.ContainsKey("server_url") ? item["server_url"].ToString() : TxtServerUrl.Text;

                    bool isSuccess = await UploadSinglePayloadAsync(serverUrl, item);

                    if (isSuccess)
                    {
                        queue.RemoveAt(0);
                        SaveQueue(queue);
                        DebugLogger.Log("INFO", "上报：1个历史缓存包成功送达服务器并已安全销毁。");
                        UpdateStatusText($"同步成功 | 剩余缓存: {queue.Count} 个");
                    }
                    else
                    {
                        UpdateStatusText($"服务器不在线，队列已妥善暂存 | 剩余: {queue.Count} 个");
                        break;
                    }

                    await Task.Delay(1500);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("ERROR", "队列轮询重传模块发生严重意外崩溃。", ex);
            }
            finally
            {
                lock (syncLock)
                {
                    isSyncing = false;
                }
            }
        }

        private async Task<bool> UploadSinglePayloadAsync(string baseUrl, Dictionary<string, object> payload)
        {
            string url = baseUrl.Trim().TrimEnd('/') + "/api/upload";
            try
            {
                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                DebugLogger.Log("INFO", $"发送数据包至网关: {url}");
                var response = await httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    DebugLogger.Log("INFO", "数据包被网关成功接收，返回 200 OK。");
                    return true;
                }
                else
                {
                    string body = await response.Content.ReadAsStringAsync();
                    DebugLogger.Log("WARN", $"数据包发送失败。网关状态码: [{(int)response.StatusCode}]。服务器详情: {body}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("ERROR", $"网络故障：尝试连接服务器失败！API: {url}", ex);
                return false;
            }
        }

        private void UpdateStatusText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                var queue = LoadQueue();
                if (queue.Count > 0)
                {
                    TxtStatus.Text = $"{text} [离线积压: {queue.Count}个]";
                }
                else
                {
                    TxtStatus.Text = text;
                }
            });
        }

        private async void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            BtnUpload.IsEnabled = false;
            BtnUpload.Content = "上传中...";
            UpdateStatusText("正在准备数据入队...");

            try
            {
                var payload = BuildPayload();
                await Task.Run(() => EnqueuePayload(payload));
                await Task.Run(() => FlushQueueSequentialAsync());

                MessageBox.Show("数据已成功入队并开始尝试上报！", "入队上报完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加入发送队列失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnUpload.Content = "上传至服务器";
                BtnUpload.IsEnabled = true;
            }
        }

        private void InitializeTrayIcon()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Icon = SystemIcons.Shield; 
            trayIcon.Text = "终端资产管理平台";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => RestoreWindow();

            var contextMenu = new System.Windows.Forms.ContextMenu();
            contextMenu.MenuItems.Add("打开主界面", (s, e) => RestoreWindow());
            contextMenu.MenuItems.Add("安全退出", (s, e) => TriggerExitWithPassword());
            trayIcon.ContextMenu = contextMenu;
        }

        private void RestoreWindow()
        {
            try
            {
                if (this.IsActive == false || this.Visibility != Visibility.Visible)
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.ShowInTaskbar = true;
                    this.Activate();
                }
            }
            catch { }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!isForceExit)
            {
                e.Cancel = true; 
                TriggerExitWithPassword();
            }
        }

        private void TriggerExitWithPassword()
        {
            RestoreWindow(); 
            PasswordOverlay.Visibility = Visibility.Visible;
            TxtExitPassword.Focus();
        }

        private void BtnConfirmExit_Click(object sender, RoutedEventArgs e)
        {
            if (TxtExitPassword.Password == ExitPassword) 
            {
                isForceExit = true; 
                PasswordOverlay.Visibility = Visibility.Collapsed;
                
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
                if (syncTimer != null)
                {
                    syncTimer.Stop();
                    syncTimer.Dispose();
                }
                
                SaveConfig();
                this.Close(); 
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

        private void BtnChangeExitPassword_Click(object sender, RoutedEventArgs e)
        {
            Window dialog = new Window { Title = "修改客户端安全退出密码", Width = 320, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.White, FontFamily = this.FontFamily };
            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel sp1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sp1.Children.Add(new Label { Content = "旧密码:", Width = 70 });
            PasswordBox txtOld = new PasswordBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp1.Children.Add(txtOld);
            Grid.SetRow(sp1, 0);

            StackPanel sp2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sp2.Children.Add(new Label { Content = "新密码:", Width = 70 });
            PasswordBox txtNew = new PasswordBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp2.Children.Add(txtNew);
            Grid.SetRow(sp2, 1);

            StackPanel sp3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
            sp3.Children.Add(new Label { Content = "重复新密码:", Width = 70 });
            PasswordBox txtConfirm = new PasswordBox { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
            sp3.Children.Add(txtConfirm);
            Grid.SetRow(sp3, 2);

            StackPanel sp4 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnOk = new Button { Content = "确认修改", Width = 80, Height = 25, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
            Button btnCancel = new Button { Content = "取消", Width = 70, Height = 25, IsCancel = true };
            sp4.Children.Add(btnOk); sp4.Children.Add(btnCancel);
            Grid.SetRow(sp4, 3);

            grid.Children.Add(sp1); grid.Children.Add(sp2); grid.Children.Add(sp3); grid.Children.Add(sp4);
            dialog.Content = grid;

            btnOk.Click += (o, a) =>
            {
                if (txtOld.Password != ExitPassword)
                {
                    MessageBox.Show("旧密码验证不正确！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrEmpty(txtNew.Password))
                {
                    MessageBox.Show("新密码不能为空！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (txtNew.Password != txtConfirm.Password)
                {
                    MessageBox.Show("两次输入的新密码不一致！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ExitPassword = txtNew.Password; 
                SaveConfig(); 
                MessageBox.Show("客户端安全退出密码修改成功！并已安全保存在本地。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                dialog.DialogResult = true;
            };

            dialog.ShowDialog();
        }

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
                        if (cfg.ContainsKey("exit_password")) ExitPassword = cfg["exit_password"].ToString();

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
                    { "exit_password", ExitPassword }, 
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

        // ========== 【核心新增】封装辅助的调试日志写入，供前台 status 和 调试黑窗口 实时跟踪 ==========
        private void WriteDebugLog(string text, string level = "INFO")
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss}] [{level}] {text}";
            System.Diagnostics.Debug.WriteLine(formatted); 
            UpdateStatusText(text); 
        }

        // 【新增】供外部调用的静默上报 Payload 构造
        private Dictionary<string, object> BuildPayloadInternal(List<ResultItem> hw, List<SoftwareItem> sw)
        {
            var payload = new Dictionary<string, object>();
            payload["server_url"] = TxtServerUrl.Text.Trim();
            payload["building"] = TxtBuilding.Text.Trim();
            payload["floor"] = TxtFloor.Text.Trim();
            payload["department"] = TxtDept.Text.Trim();
            payload["type"] = TxtAssetType.Text.Trim();

            foreach (var kv in customFields) payload[kv.Key] = kv.Value;
            foreach (var item in hw) payload[item.Key] = item.Value;
            if (sw != null && sw.Count > 0) payload["software_list"] = sw;
            return payload;
        }

        private Dictionary<string, object> BuildPayload()
        {
            return BuildPayloadInternal(currentResults, currentSoftwareResults);
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
                                if (key == "software_list" || key == "report_type") continue;
                                wsHardware.Cell(1, col).Value = key;
                                wsHardware.Cell(1, col).Style.Font.Bold = true;
                                wsHardware.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
                                col++;
                            }
                            col = 1;
                            foreach (var kv in payload)
                            {
                                if (kv.Key == "software_list" || kv.Key == "report_type") continue;
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
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AssetCollector
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // 文本框内容改变时，检查是否填写完整，完整才启用“扫描”按钮
        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 【关键修复】防止在初始化界面时触发事件，导致拿到还未创建(null)的控件引发静默崩溃
            if (TxtBuilding == null || TxtFloor == null || TxtDept == null || TxtAssetType == null || BtnScan == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(TxtBuilding.Text) &&
                !string.IsNullOrWhiteSpace(TxtFloor.Text) &&
                !string.IsNullOrWhiteSpace(TxtDept.Text) &&
                !string.IsNullOrWhiteSpace(TxtAssetType.Text))
            {
                BtnScan.IsEnabled = true;
            }
            else
            {
                BtnScan.IsEnabled = false;
            }
        }

        // 扫描按钮点击事件
        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            // 锁定界面状态
            BtnScan.IsEnabled = false;
            BtnUpload.IsEnabled = false;
            BtnExport.IsEnabled = false;
            BtnScan.Content = "正在扫描...";
            ProgBar.Visibility = Visibility.Visible;
            ProgBar.Value = 0;

            // 后台异步执行采集，防止 UI 卡顿
            var results = await Task.Run(() => PerformScan());

            // 绑定数据到表格
            GridResults.ItemsSource = results;

            // 恢复界面状态
            ProgBar.Value = 100;
            TxtStatus.Text = $"采集完成 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            BtnScan.Content = "重新扫描";
            BtnScan.IsEnabled = true;
            BtnUpload.IsEnabled = true;  // 激活上传按钮
            BtnExport.IsEnabled = true;  // 激活导出按钮
            
            // 延迟2秒后隐藏进度条
            await Task.Delay(2000);
            ProgBar.Visibility = Visibility.Hidden;
        }

        // 具体的扫描任务
        private List<ResultItem> PerformScan()
        {
            var list = new List<ResultItem>();

            UpdateProgress(10, "正在采集系统信息...");
            list.Add(new ResultItem { Key = "计算机名称", Value = Environment.MachineName });
            list.Add(new ResultItem { Key = "用户名", Value = Environment.UserName });
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

            UpdateProgress(90, "正在采集主板和显卡信息...");
            list.Add(new ResultItem { Key = "主板信息", Value = HardwareCollector.GetMotherboardInfo() });
            list.Add(new ResultItem { Key = "显卡", Value = HardwareCollector.GetGpuInfo() });

            return list;
        }

        // 帮助更新进度条和状态文字
        private void UpdateProgress(int percent, string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgBar.Value = percent;
                TxtStatus.Text = message;
            });
        }
    }

    // 表格数据模型
    public class ResultItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

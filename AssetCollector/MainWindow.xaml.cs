using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace AssetCollector
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // 按钮点击事件：加上 async 关键字实现异步防卡顿
        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false;
            BtnScan.Content = "扫描中...";

            // 使用 Task.Run 在后台线程执行 WMI 采集，避免 UI 卡死
            var results = await Task.Run(() => 
            {
                var list = new List<ResultItem>();
                list.Add(new ResultItem { Key = "基础信息", Value = HardwareCollector.GetBasicInfo() });
                list.Add(new ResultItem { Key = "处理器", Value = HardwareCollector.GetCpuInfo() });
                // 后续可以补充内存、显卡、MAC地址等
                return list;
            });

            // 绑定到界面表格
            GridResults.ItemsSource = results;

            BtnScan.Content = "扫描完成";
            BtnScan.IsEnabled = true;
            BtnUpload.IsEnabled = true;
        }
    }

    // 用于绑定 DataGrid 的简单数据模型
    public class ResultItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

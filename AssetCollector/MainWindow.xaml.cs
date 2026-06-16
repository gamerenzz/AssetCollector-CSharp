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

namespace AssetCollector
{
    public partial class MainWindow : Window
    {
        // 创建一个全局 HttpClient 实例，推荐的最佳实践
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private List<ResultItem> currentResults = new List<ResultItem>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtBuilding == null || TxtFloor == null || TxtDept == null || TxtAssetType == null || BtnScan == null) return;
            BtnScan.IsEnabled = (!string.IsNullOrWhiteSpace(TxtBuilding.Text) && !string.IsNullOrWhiteSpace(TxtFloor.Text) && !string.IsNullOrWhiteSpace(TxtDept.Text) && !string.IsNullOrWhiteSpace(TxtAssetType.Text));
        }

        // 1. 扫描功能
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
            UpdateProgress(10, "正在采集系统...");
            list.Add(new ResultItem { Key = "计算机名称", Value = Environment.MachineName });
            list.Add(new ResultItem { Key = "用户名", Value = Environment.UserName });
            list.Add(new ResultItem { Key = "操作系统", Value = HardwareCollector.GetOSInfo() });
            UpdateProgress(30, "正在采集芯片...");
            list.Add(new ResultItem { Key = "处理器", Value = HardwareCollector.GetCpuInfo() });
            list.Add(new ResultItem { Key = "内存", Value = HardwareCollector.GetRamInfo() });
            UpdateProgress(50, "正在采集磁盘...");
            list.Add(new ResultItem { Key = "磁盘", Value = HardwareCollector.GetDiskInfo() });
            UpdateProgress(70, "正在采集网络...");
            var netInfo = HardwareCollector.GetNetworkInfo();
            list.Add(new ResultItem { Key = "IP地址", Value = netInfo.IP });
            list.Add(new ResultItem { Key = "MAC地址", Value = netInfo.MAC });
            UpdateProgress(90, "正在采集板卡...");
            list.Add(new ResultItem { Key = "主板信息", Value = HardwareCollector.GetMotherboardInfo() });
            list.Add(new ResultItem { Key = "显卡", Value = HardwareCollector.GetGpuInfo() });
            return list;
        }

        private void UpdateProgress(int percent, string message)
        {
            Dispatcher.Invoke(() => { ProgBar.Value = percent; TxtStatus.Text = message; });
        }

        // 2. 构造要提交/导出的数据包 (Payload)
        private Dictionary<string, string> BuildPayload()
        {
            var payload = new Dictionary<string, string>();
            // 位置信息
            payload["building"] = TxtBuilding.Text.Trim();
            payload["floor"] = TxtFloor.Text.Trim();
            payload["department"] = TxtDept.Text.Trim();
            payload["type"] = TxtAssetType.Text.Trim();
            // 硬件信息 (从表格读取)
            foreach (var item in currentResults)
            {
                payload[item.Key] = item.Value;
            }
            return payload;
        }

        // 3. 上传功能
        private async void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            BtnUpload.IsEnabled = false;
            BtnUpload.Content = "上传中...";
            TxtStatus.Text = "正在连接服务器...";

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
                MessageBox.Show($"网络请求发生错误，请检查服务器地址是否正确及网络是否通畅：\n\n{ex.Message}", "网络连接错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "上传出错";
            }
            finally
            {
                BtnUpload.Content = "上传至服务器";
                BtnUpload.IsEnabled = true;
            }
        }

        // 4. 导出 Excel 功能
        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var payload = BuildPayload();
            
            // 默认文件名：楼号_科室_日期.xlsx
            string defaultName = $"{payload["building"]}_{payload["department"]}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                FileName = defaultName,
                Title = "导出资产数据"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                BtnExport.IsEnabled = false;
                BtnExport.Content = "导出中...";

                try
                {
                    await Task.Run(() =>
                    {
                        // 使用 ClosedXML 创建 Excel
                        using (var workbook = new XLWorkbook())
                        {
                            var worksheet = workbook.Worksheets.Add("资产信息");

                            // 写表头
                            int col = 1;
                            foreach (var key in payload.Keys)
                            {
                                worksheet.Cell(1, col).Value = key;
                                worksheet.Cell(1, col).Style.Font.Bold = true;
                                worksheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
                                col++;
                            }

                            // 写数据
                            col = 1;
                            foreach (var val in payload.Values)
                            {
                                worksheet.Cell(2, col).Value = val;
                                col++;
                            }

                            // 自适应列宽
                            worksheet.Columns().AdjustToContents();

                            workbook.SaveAs(saveFileDialog.FileName);
                        }
                    });

                    MessageBox.Show("Excel文件已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出时发生错误，文件可能被其他程序占用：\n\n{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnExport.Content = "导出数据 (Excel)";
                    BtnExport.IsEnabled = true;
                }
            }
        }
    }

    public class ResultItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

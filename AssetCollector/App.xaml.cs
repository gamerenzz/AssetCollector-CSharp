using System;
using System.Windows;

namespace AssetCollector
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 拦截所有未处理的异常，弹出错误提示框
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"发生 UI 错误，程序即将恢复或退出：\n\n{e.Exception.Message}\n\n位置：\n{e.Exception.StackTrace}", 
                "系统拦截到错误", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // 尝试阻止程序崩溃退出
            e.Handled = true; 
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"发生底层严重错误，程序必须退出：\n\n{ex.Message}\n\n位置：\n{ex.StackTrace}", 
                    "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

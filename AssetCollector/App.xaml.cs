using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AssetCollector
{
    public partial class App : Application
    {
        // 系统级互斥锁，必须是全局唯一的命名
        private static Mutex appMutex;
        private const string MutexName = "Global\\AssetCollector_Mutex_Lock_Tonekey2016";

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 检查单实例运行
            appMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // 已有程序在运行，尝试将其置顶唤醒
                BringExistingInstanceToForeground();
                Application.Current.Shutdown();
                return;
            }

            // 2. 检查启动参数是否包含开机自启命令
            bool startMinimized = false;
            foreach (var arg in e.Args)
            {
                if (arg.ToLower() == "-startup")
                {
                    startMinimized = true;
                    break;
                }
            }

            // 3. 将启动参数传递给主窗体
            MainWindow mainWindow = new MainWindow(startMinimized);
            mainWindow.Show();

            // 4. 拦截全局异常
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);
        }

        private void BringExistingInstanceToForeground()
        {
            Process current = Process.GetCurrentProcess();
            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id != current.Id)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                    }
                    break;
                }
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"发生 UI 错误：\n\n{e.Exception.Message}", "系统异常", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; 
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"发生底层严重错误：\n\n{ex.Message}", "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (appMutex != null)
            {
                appMutex.ReleaseMutex();
                appMutex.Close();
            }
            base.OnExit(e);
        }
    }
}

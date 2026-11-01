using System;
using Screenlocker.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Screenlocker
{
    /// <summary>
    /// 应用入口。启动时顺手把磁贴刷一遍，并确保后台定时任务已注册。
    /// </summary>
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += OnUnhandledException;
        }

        /// <summary>未处理异常写日志：手机上没法挂调试器，只能靠日志文件排查。</summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Diagnostics.WriteError("未处理异常", e.Exception);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (!(Window.Current.Content is Frame rootFrame))
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            if (!e.PrelaunchActivated)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(Views.MainPage));
                }

                Window.Current.Activate();
                RefreshInBackground();
            }
        }

        /// <summary>启动时的刷新：失败不影响 UI，错误写进设置与日志。</summary>
        private async void RefreshInBackground()
        {
            try
            {
                var taskError = await BackgroundTaskService.EnsureRegisteredAsync(AppSettings.RefreshMinutes);
                if (!string.IsNullOrEmpty(taskError))
                {
                    Diagnostics.Write("后台任务注册：" + taskError);
                }

                var result = await TileService.RefreshAsync(AppSettings.AllowNetwork);
                Diagnostics.Write("启动刷新：" + string.Join(" | ", result.Lines) + (string.IsNullOrEmpty(result.Error) ? "" : "  [" + result.Error + "]"));
            }
            catch (Exception ex)
            {
                AppSettings.LastError = ex.Message;
                Diagnostics.WriteError("启动刷新失败", ex);
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("页面加载失败：" + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}

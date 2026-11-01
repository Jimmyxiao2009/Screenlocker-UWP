using System;
using System.Threading.Tasks;
using Screenlocker.Services;
using Windows.ApplicationModel.Background;

namespace Screenlocker.Tasks
{
    /// <summary>
    /// 后台定时刷新磁贴与锁屏详细状态。
    /// 这个类由 Package.appxmanifest 的 backgroundTasks 扩展按 EntryPoint 反射激活，
    /// 改动类名或命名空间时必须同步改清单。
    /// </summary>
    public sealed class TileRefreshTask : IBackgroundTask
    {
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            var deferral = taskInstance.GetDeferral();

            try
            {
                // 后台任务宿主里没有 UI 同步上下文，这里包一层 Task.Run 再等待，避免死锁
                var task = Task.Run(() => TileService.RefreshAsync(AppSettings.AllowNetwork));
                if (!task.Wait(TimeSpan.FromSeconds(45)))
                {
                    AppSettings.LastError = "后台刷新超时（可能是定位或天气请求太慢）";
                    Diagnostics.Write(AppSettings.LastError);
                }
                else if (task.IsFaulted && task.Exception != null)
                {
                    AppSettings.LastError = task.Exception.GetBaseException().Message;
                    Diagnostics.WriteError("后台刷新失败", task.Exception);
                }
            }
            catch (Exception ex)
            {
                AppSettings.LastError = ex.Message;
                Diagnostics.WriteError("后台任务异常", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}

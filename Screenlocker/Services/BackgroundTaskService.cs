using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

namespace Screenlocker.Services
{
    /// <summary>
    /// 后台定时任务的注册 / 注销。定时触发器最短 15 分钟（系统限制）。
    /// </summary>
    public static class BackgroundTaskService
    {
        public const string TaskName = "ScreenlockerTileRefresh";

        public static bool IsRegistered
        {
            get
            {
                foreach (var pair in BackgroundTaskRegistration.AllTasks)
                {
                    if (pair.Value.Name == TaskName)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 注册定时刷新任务（已存在会先注销再按新间隔注册）。
        /// 返回空字符串表示成功，否则是给用户看的原因。
        /// </summary>
        public static async Task<string> EnsureRegisteredAsync(int minutes)
        {
            var interval = Math.Max(AppSettings.MinRefreshMinutes, minutes);
            Unregister();

            BackgroundAccessStatus access;
            try
            {
                access = await BackgroundExecutionManager.RequestAccessAsync();
            }
            catch (Exception ex)
            {
                Diagnostics.WriteError("申请后台权限失败", ex);
                return "申请后台权限失败：" + ex.Message;
            }

            // 枚举值随系统版本变多（Denied / DeniedByUser / DeniedBySystemPolicy …），
            // 只比对过时的 Denied 会漏掉后面几种，用户点了"不允许"却被当成功。
            // 锁屏详细状态必须有后台任务才会出现在系统列表里，所以这里必须如实上报。
            var statusText = access.ToString();
            Diagnostics.Write("后台权限状态：" + statusText);
            if (statusText.StartsWith("Denied", StringComparison.OrdinalIgnoreCase))
            {
                return "系统没给后台运行权限（" + statusText + "）。去 设置 → 隐私 → 后台应用 里允许“锁屏文字”，"
                     + "锁屏详细状态需要后台任务才会出现";
            }

            try
            {
                var builder = new BackgroundTaskBuilder
                {
                    Name = TaskName,
                    TaskEntryPoint = typeof(Tasks.TileRefreshTask).FullName,
                };
                builder.SetTrigger(new TimeTrigger((uint)interval, false));
                builder.Register();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return "注册后台任务失败：" + ex.Message;
            }
        }

        public static void Unregister()
        {
            try
            {
                foreach (var pair in BackgroundTaskRegistration.AllTasks)
                {
                    if (pair.Value.Name == TaskName)
                    {
                        pair.Value.Unregister(true);
                    }
                }
            }
            catch
            {
                // 注销失败无所谓，下次注册会再试
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using Windows.Storage;

namespace Screenlocker.Services
{
    /// <summary>
    /// 把异常和刷新结果写到 LocalFolder\screenlocker.log。
    /// 手机上看不了调试器，出问题时这个文件是唯一的线索；桌面调试同样好用。
    /// </summary>
    public static class Diagnostics
    {
        private const string FileName = "screenlocker.log";
        private const int MaxBytes = 64 * 1024;

        /// <summary>日志文件完整路径，设置页会显示它。</summary>
        public static string LogPath
        {
            get
            {
                try
                {
                    return Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public static void Write(string message)
        {
            try
            {
                var path = LogPath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                // 用同步 System.IO 而不是 WinRT 异步 API：后台任务和 UI 线程都不会因为等待而卡住
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(
                    path,
                    DateTimeOffset.Now.ToString("MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // 日志失败不能影响主流程
            }
        }

        public static void WriteError(string context, Exception ex)
        {
            if (ex == null)
            {
                Write(context + " → 未知错误");
                return;
            }

            // AggregateException 只写最内层才有用
            var inner = ex is AggregateException aggregate ? aggregate.GetBaseException() : ex;
            Write(context + " → " + inner.GetType().FullName + ": " + inner.Message);
        }
    }
}

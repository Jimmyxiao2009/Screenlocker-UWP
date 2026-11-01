using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Screenlocker.Services
{
    /// <summary>
    /// 磁贴 + 锁屏详细状态。
    /// 锁屏"详细状态"的文字来自宽版磁贴通知里的 hint-lockDetailedStatus1..3（最多三行），
    /// 同时在宽版 binding 里放 text id=1..3，两条路都写上，保证系统两种取值方式都能拿到。
    /// </summary>
    public static class TileService
    {
        public sealed class TileRefreshResult
        {
            public List<string> Lines { get; set; } = new List<string>();
            public InfoSnapshot Snapshot { get; set; }
            public string Error { get; set; } = string.Empty;
            public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.Now;
        }

        /// <summary>采集数据 → 渲染模板 → 更新磁贴与锁屏详细状态。</summary>
        public static async Task<TileRefreshResult> RefreshAsync(bool allowNetwork)
        {
            var result = new TileRefreshResult();

            try
            {
                var snapshot = await InfoSnapshot.CaptureAsync(allowNetwork);
                result.Snapshot = snapshot;
                result.Lines = TemplateEngine.RenderLines(AppSettings.Template, snapshot.Values);

                var tileError = ApplyTile(result.Lines);
                ApplyBadge(snapshot);

                if (!string.IsNullOrEmpty(tileError))
                {
                    result.Error = tileError;
                }
                else if (!string.IsNullOrEmpty(snapshot.Note))
                {
                    result.Error = snapshot.Note;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            AppSettings.LastRefreshTicks = DateTimeOffset.UtcNow.Ticks;
            AppSettings.LastError = result.Error ?? string.Empty;
            Diagnostics.Write("刷新：" + string.Join(" | ", result.Lines)
                + (string.IsNullOrEmpty(result.Error) ? string.Empty : "  [" + result.Error + "]"));
            return result;
        }

        /// <summary>清掉磁贴与徽章（改成空模板时用）。</summary>
        public static void Clear()
        {
            TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
        }

        /// <summary>生成磁贴通知 XML。独立出来是为了方便排查（设置页不显示，但日志/调试能看到）。</summary>
        public static string BuildTileXml(IReadOnlyList<string> lines)
        {
            var sb = new StringBuilder(512);
            sb.Append("<tile><visual version=\"3\">");

            // 中磁贴：四行不换行文本
            sb.Append("<binding template=\"TileSquare150x150Text03\" fallback=\"TileSquareText03\">");
            AppendTextElements(sb, lines, 4);
            sb.Append("</binding>");

            // 宽磁贴：五行不换行文本 + 锁屏详细状态三行
            sb.Append("<binding template=\"TileWide310x150Text05\" fallback=\"TileWideText05\"");
            for (var i = 0; i < lines.Count && i < TemplateEngine.MaxLines; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                {
                    continue;
                }

                sb.Append(" hint-lockDetailedStatus").Append(i + 1).Append("=\"")
                  .Append(Escape(lines[i])).Append('"');
            }

            sb.Append('>');
            AppendTextElements(sb, lines, 5);
            sb.Append("</binding>");

            sb.Append("</visual></tile>");
            return sb.ToString();
        }

        private static void AppendTextElements(StringBuilder sb, IReadOnlyList<string> lines, int maxFields)
        {
            var index = 1;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (index > maxFields)
                {
                    break;
                }

                sb.Append("<text id=\"").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append("\">").Append(Escape(line)).Append("</text>");
                index++;
            }
        }

        private static string ApplyTile(IReadOnlyList<string> lines)
        {
            var updater = TileUpdateManager.CreateTileUpdaterForApplication();

            // Setting != Enabled 时 Update() 不抛异常、静默丢弃，磁贴永远不变。
            // 这必须写进日志，否则表面上"刷新成功"其实什么都没发生。
            var setting = updater.Setting;
            Diagnostics.Write("磁贴通知状态：" + setting);

            var hasText = false;
            foreach (var line in lines)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    hasText = true;
                    break;
                }
            }

            if (!hasText)
            {
                updater.Clear();
                return string.Empty;
            }

            if (setting != NotificationSetting.Enabled)
            {
                return "系统关掉了本应用的通知（" + setting + "），磁贴不会更新。"
                     + "去 设置 → 系统 → 通知 里允许“锁屏文字”";
            }

            var xml = BuildTileXml(lines);
            Diagnostics.Write("磁贴 XML：" + xml);

            var document = new XmlDocument();
            document.LoadXml(xml);

            updater.Clear();
            updater.Update(new TileNotification(document) { Tag = "screenlocker" });
            return string.Empty;
        }

        private static void ApplyBadge(InfoSnapshot snapshot)
        {
            // 注意：BadgeUpdater 没有 Setting 属性，只有 TileUpdater 有
            var updater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();

            if (!AppSettings.BadgeShowsBattery || snapshot?.BatteryPercent == null)
            {
                updater.Clear();
                return;
            }

            // 徽章只接受 1~99，超范围会抛异常
            var percent = Math.Min(99, Math.Max(1, snapshot.BatteryPercent.Value));
            var document = new XmlDocument();
            document.LoadXml("<badge value=\"" + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"/>");
            updater.Update(new BadgeNotification(document));
        }

        /// <summary>
        /// XML 转义（文本和双引号属性共用）。
        /// 只转义 &amp; &lt; &gt; &quot;：这四个是磁贴渲染器认的实体。
        /// 单引号在文本和双引号属性里都不需要转义，转成 &amp;apos; 反而会被渲染器丢弃整条通知
        /// （磁贴会静默退回默认图标，Update() 却不报错）。
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 16);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(ch); break;
                }
            }

            return sb.ToString();
        }
    }
}

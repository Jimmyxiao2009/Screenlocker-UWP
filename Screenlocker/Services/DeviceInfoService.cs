using System;
using Windows.Foundation.Metadata;

namespace Screenlocker.Services
{
    /// <summary>
    /// 本机数据：电量、充电状态、时间日期。
    /// 电量走三层兜底：PowerManager → Battery 报告 → Windows Phone 专用 API。
    /// </summary>
    public static class DeviceInfoService
    {
        private static readonly string[] WeekdayNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        public static string WeekdayName(DateTimeOffset time)
        {
            return WeekdayNames[(int)time.DayOfWeek];
        }

        /// <summary>电量百分比；取不到返回 null。</summary>
        public static int? TryGetBatteryPercent()
        {
            // 1) Windows 10 Mobile 上最靠谱的一个
            if (ApiInformation.IsTypePresent("Windows.System.Power.PowerManager"))
            {
                try
                {
                    return Windows.System.Power.PowerManager.RemainingChargePercent;
                }
                catch
                {
                    // 忽略，继续兜底
                }
            }

            // 2) 通用电池报告：只给毫瓦时，自己折算百分比
            try
            {
                var report = Windows.Devices.Power.Battery.AggregateBattery?.GetReport();
                var remaining = report?.RemainingCapacityInMilliwattHours;
                var full = report?.FullChargeCapacityInMilliwattHours;
                if (remaining.HasValue && full.HasValue && full.Value > 0)
                {
                    return (int)Math.Round(100.0 * remaining.Value / full.Value);
                }
            }
            catch
            {
                // 忽略，继续兜底
            }

            // 3) Windows Phone 8.1 时代的电池 API。
            // 注意：这个类型来自"Windows Mobile Extensions"扩展 SDK，桌面等设备族上不存在，
            // 直接写在同一方法里会让 JIT 在桌面加载类型失败而崩溃（ApiInformation 守卫救不了）。
            // 所以必须单独放一个方法，只有确认类型存在时才调用它。
            if (ApiInformation.IsTypePresent("Windows.Phone.Devices.Power.Battery"))
            {
                try
                {
                    return PhoneBatteryPercent();
                }
                catch
                {
                    // 忽略
                }
            }

            return null;
        }

        /// <summary>只在确认 Windows.Phone.Devices.Power 存在后调用，隔离设备专有类型引用。</summary>
        private static int PhoneBatteryPercent()
        {
            return Windows.Phone.Devices.Power.Battery.GetDefault().RemainingChargePercent;
        }

        /// <summary>是否正在充电；取不到状态时按 false 处理。</summary>
        public static bool IsCharging()
        {
            try
            {
                if (ApiInformation.IsTypePresent("Windows.System.Power.PowerManager"))
                {
                    return Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.Charging;
                }
            }
            catch
            {
                // 忽略
            }

            try
            {
                var status = Windows.Devices.Power.Battery.AggregateBattery?.GetReport()?.Status;
                return status == Windows.System.Power.BatteryStatus.Charging;
            }
            catch
            {
                return false;
            }
        }
    }
}

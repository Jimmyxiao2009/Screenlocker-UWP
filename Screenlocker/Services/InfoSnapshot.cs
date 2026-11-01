using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Screenlocker.Services
{
    /// <summary>
    /// 一次采集的结果：模板里所有 {占位符} 对应的值。
    /// </summary>
    public sealed class InfoSnapshot
    {
        private InfoSnapshot()
        {
        }

        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int? BatteryPercent { get; private set; }

        public bool IsCharging { get; private set; }

        /// <summary>采集说明（定位/天气的问题），设置页会显示。</summary>
        public string Note { get; private set; } = string.Empty;

        public DateTimeOffset CapturedAt { get; private set; } = DateTimeOffset.Now;

        /// <summary>只读缓存版本，界面预览用，不联网、不定位。</summary>
        public static InfoSnapshot CaptureFromCache()
        {
            var snapshot = new InfoSnapshot();
            snapshot.FillLocal();
            snapshot.FillWeather(WeatherService.FromCacheOnly());
            return snapshot;
        }

        /// <summary>完整采集：本地数据 + 定位 + 天气。</summary>
        public static async Task<InfoSnapshot> CaptureAsync(bool allowNetwork)
        {
            var snapshot = new InfoSnapshot();
            snapshot.FillLocal();

            if (allowNetwork)
            {
                var weather = await WeatherService.GetAsync(AppSettings.UseLocation, AppSettings.ManualCity);
                snapshot.FillWeather(weather);
            }
            else
            {
                snapshot.FillWeather(WeatherService.FromCacheOnly());
                snapshot.Note = "已关闭联网天气";
            }

            return snapshot;
        }

        private void FillLocal()
        {
            var now = DateTimeOffset.Now;
            CapturedAt = now;

            Values["time"] = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            Values["time12"] = now.ToString("h:mm", CultureInfo.InvariantCulture);
            Values["date"] = now.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
            Values["md"] = now.ToString("M/d", CultureInfo.InvariantCulture);
            Values["weekday"] = DeviceInfoService.WeekdayName(now);

            BatteryPercent = DeviceInfoService.TryGetBatteryPercent();
            IsCharging = DeviceInfoService.IsCharging();

            Values["battery"] = BatteryPercent.HasValue ? BatteryPercent.Value.ToString(CultureInfo.InvariantCulture) + "%" : string.Empty;
            Values["batteryNum"] = BatteryPercent.HasValue ? BatteryPercent.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            Values["charging"] = IsCharging ? "⚡" : string.Empty;
        }

        private void FillWeather(WeatherService.WeatherResult weather)
        {
            if (weather == null)
            {
                return;
            }

            if (weather.HasFix)
            {
                Values["lat"] = weather.Latitude.ToString("0.###", CultureInfo.InvariantCulture);
                Values["lon"] = weather.Longitude.ToString("0.###", CultureInfo.InvariantCulture);
            }

            Values["city"] = weather.City ?? string.Empty;
            Values["temp"] = weather.TemperatureC.HasValue
                ? weather.TemperatureC.Value.ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty;
            Values["weather"] = weather.Condition ?? string.Empty;
            Values["humidity"] = weather.Humidity.HasValue
                ? weather.Humidity.Value.ToString(CultureInfo.InvariantCulture) + "%"
                : string.Empty;

            if (!string.IsNullOrEmpty(weather.Note))
            {
                Note = weather.Note;
            }
        }
    }
}

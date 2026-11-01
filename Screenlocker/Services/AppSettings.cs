using System;
using Windows.Storage;

namespace Screenlocker.Services
{
    /// <summary>
    /// 本地设置（LocalSettings）读写。前台页面与后台任务共用，所以只放静态属性。
    /// </summary>
    public static class AppSettings
    {
        private static ApplicationDataContainer Store => ApplicationData.Current.LocalSettings;

        /// <summary>默认模板：时间 / 电量 / 天气各一行。</summary>
        public const string DefaultTemplate = "{time}  {weekday}\n电量 {battery}{charging}\n{city} {temp}°C {weather}";

        /// <summary>系统定时器后台任务最短 15 分钟，低于此值没有意义。</summary>
        public const int MinRefreshMinutes = 15;

        // ── 用户设置 ───────────────────────────────────────────────
        public static string Template
        {
            get => GetString(nameof(Template), DefaultTemplate);
            set => Set(nameof(Template), value ?? string.Empty);
        }

        /// <summary>后台自动刷新间隔（分钟）。</summary>
        public static int RefreshMinutes
        {
            get => Math.Max(MinRefreshMinutes, GetInt(nameof(RefreshMinutes), 30));
            set => Set(nameof(RefreshMinutes), Math.Max(MinRefreshMinutes, value));
        }

        public static bool AutoRefresh
        {
            get => GetBool(nameof(AutoRefresh), true);
            set => Set(nameof(AutoRefresh), value);
        }

        /// <summary>是否联网取天气（关掉就只显示本地电量/时间）。</summary>
        public static bool AllowNetwork
        {
            get => GetBool(nameof(AllowNetwork), true);
            set => Set(nameof(AllowNetwork), value);
        }

        /// <summary>是否用定位确定城市。</summary>
        public static bool UseLocation
        {
            get => GetBool(nameof(UseLocation), true);
            set => Set(nameof(UseLocation), value);
        }

        /// <summary>手动指定的城市名；非空时优先于定位反查。</summary>
        public static string ManualCity
        {
            get => GetString(nameof(ManualCity), string.Empty);
            set => Set(nameof(ManualCity), value ?? string.Empty);
        }

        /// <summary>锁屏徽章是否显示电量百分比。</summary>
        public static bool BadgeShowsBattery
        {
            get => GetBool(nameof(BadgeShowsBattery), true);
            set => Set(nameof(BadgeShowsBattery), value);
        }

        // ── 缓存（定位 / 天气 / 最近一次结果）─────────────────────
        public static double LastLat
        {
            get => GetDouble(nameof(LastLat), double.NaN);
            set => Set(nameof(LastLat), value);
        }

        public static double LastLon
        {
            get => GetDouble(nameof(LastLon), double.NaN);
            set => Set(nameof(LastLon), value);
        }

        public static long LastFixTicks
        {
            get => GetLong(nameof(LastFixTicks), 0);
            set => Set(nameof(LastFixTicks), value);
        }

        public static string LastCity
        {
            get => GetString(nameof(LastCity), string.Empty);
            set => Set(nameof(LastCity), value ?? string.Empty);
        }

        public static long LastCityTicks
        {
            get => GetLong(nameof(LastCityTicks), 0);
            set => Set(nameof(LastCityTicks), value);
        }

        public static double LastTempC
        {
            get => GetDouble(nameof(LastTempC), double.NaN);
            set => Set(nameof(LastTempC), value);
        }

        public static string LastWeather
        {
            get => GetString(nameof(LastWeather), string.Empty);
            set => Set(nameof(LastWeather), value ?? string.Empty);
        }

        public static int LastHumidity
        {
            get => GetInt(nameof(LastHumidity), -1);
            set => Set(nameof(LastHumidity), value);
        }

        public static long LastWeatherTicks
        {
            get => GetLong(nameof(LastWeatherTicks), 0);
            set => Set(nameof(LastWeatherTicks), value);
        }

        public static long LastRefreshTicks
        {
            get => GetLong(nameof(LastRefreshTicks), 0);
            set => Set(nameof(LastRefreshTicks), value);
        }

        public static string LastError
        {
            get => GetString(nameof(LastError), string.Empty);
            set => Set(nameof(LastError), value ?? string.Empty);
        }

        // ── 基础读写 ───────────────────────────────────────────────
        private static string GetString(string key, string fallback)
        {
            return Store.Values.TryGetValue(key, out var v) && v is string s ? s : fallback;
        }

        private static void Set(string key, object value)
        {
            Store.Values[key] = value;
        }

        private static bool GetBool(string key, bool fallback)
        {
            return Store.Values.TryGetValue(key, out var v) && v is bool b ? b : fallback;
        }

        private static int GetInt(string key, int fallback)
        {
            return Store.Values.TryGetValue(key, out var v) && v is int i ? i : fallback;
        }

        private static void Set(string key, int value)
        {
            Store.Values[key] = value;
        }

        private static long GetLong(string key, long fallback)
        {
            return Store.Values.TryGetValue(key, out var v) && v is long l ? l : fallback;
        }

        private static double GetDouble(string key, double fallback)
        {
            return Store.Values.TryGetValue(key, out var v) && v is double d ? d : fallback;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Geolocation;

namespace Screenlocker.Services
{
    /// <summary>
    /// 定位 + 天气。天气走 open-meteo（免费、不用 API Key），城市名走 BigDataCloud 反查。
    /// 结果都写进 LocalSettings 缓存，断网或定位失败时退回上次的值。
    /// </summary>
    public static class WeatherService
    {
        /// <summary>天气缓存有效期（分钟）：磁贴刷新比这频繁就直接用缓存，少发请求。</summary>
        private const int WeatherCacheMinutes = 10;

        /// <summary>定位缓存有效期（小时）：后台任务拿不到新定位时，用这个时间内的旧坐标。</summary>
        private const int FixCacheHours = 12;

        /// <summary>城市名缓存有效期（小时）。</summary>
        private const int CityCacheHours = 6;

        private static readonly HttpClient Http = CreateClient();

        /// <summary>WMO 天气代码 → 中文说明。</summary>
        private static readonly Dictionary<int, string> WeatherCodes = new Dictionary<int, string>
        {
            { 0, "晴" }, { 1, "晴间多云" }, { 2, "多云" }, { 3, "阴" },
            { 45, "雾" }, { 48, "雾凇" },
            { 51, "毛毛雨" }, { 53, "毛毛雨" }, { 55, "毛毛雨" },
            { 56, "冻毛毛雨" }, { 57, "冻毛毛雨" },
            { 61, "小雨" }, { 63, "中雨" }, { 65, "大雨" },
            { 66, "冻雨" }, { 67, "冻雨" },
            { 71, "小雪" }, { 73, "中雪" }, { 75, "大雪" }, { 77, "雪粒" },
            { 80, "阵雨" }, { 81, "阵雨" }, { 82, "强阵雨" },
            { 85, "阵雪" }, { 86, "阵雪" },
            { 95, "雷阵雨" }, { 96, "雷阵雨伴冰雹" }, { 99, "雷阵雨伴冰雹" },
        };

        public sealed class WeatherResult
        {
            public bool HasFix { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public string City { get; set; } = string.Empty;
            public double? TemperatureC { get; set; }
            public string Condition { get; set; } = string.Empty;
            public int? Humidity { get; set; }
            /// <summary>采集过程中的说明（定位被拒 / 天气请求失败等），用于设置页提示。</summary>
            public string Note { get; set; } = string.Empty;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Screenlocker/1.0 (Windows 10 Mobile)");
            return client;
        }

        /// <summary>
        /// 把网络异常翻译成用户能看懂的话。
        /// .NET Native 会把异常消息剥掉（只剩 TaskCanceledException_ctor_DefaultMessage 这种），
        /// 所以只能按异常类型判断，不能靠 ex.Message。
        /// </summary>
        private static string DescribeNetworkError(Exception ex)
        {
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return "天气请求超时，手机当前网络可能上不了外网（可改用移动数据，或在 WiFi 里设置代理）";
            }

            if (ex is HttpRequestException)
            {
                return "连不上天气服务器，检查手机网络";
            }

            return "天气请求失败：" + ex.GetType().Name;
        }

        /// <summary>
        /// 取定位与天气。forceRefresh=false 且缓存还新鲜时直接返回缓存，不发网络请求。
        /// </summary>
        public static async Task<WeatherResult> GetAsync(bool useLocation, string manualCity, bool forceRefresh = false)
        {
            var result = new WeatherResult();

            var cachedWeatherAt = FromTicks(AppSettings.LastWeatherTicks);
            var weatherFresh = (DateTimeOffset.UtcNow - cachedWeatherAt).TotalMinutes < WeatherCacheMinutes;

            if (!forceRefresh && weatherFresh)
            {
                FillFromCache(result);
                result.Note = "使用缓存";
                return result;
            }

            // 1) 定位
            if (useLocation && (forceRefresh || !IsFixFresh()))
            {
                var fix = await TryGetPositionAsync();
                if (fix != null)
                {
                    AppSettings.LastLat = fix.Latitude;
                    AppSettings.LastLon = fix.Longitude;
                    AppSettings.LastFixTicks = DateTimeOffset.UtcNow.Ticks;
                }
                else
                {
                    result.Note = "定位不可用";
                }
            }

            var lat = AppSettings.LastLat;
            var lon = AppSettings.LastLon;
            result.HasFix = !double.IsNaN(lat) && !double.IsNaN(lon) && IsFixFresh();
            result.Latitude = lat;
            result.Longitude = lon;

            // 2) 天气
            if (result.HasFix)
            {
                try
                {
                    await FetchWeatherAsync(result, lat, lon);
                    AppSettings.LastTempC = result.TemperatureC ?? double.NaN;
                    AppSettings.LastWeather = result.Condition ?? string.Empty;
                    AppSettings.LastHumidity = result.Humidity ?? -1;
                    AppSettings.LastWeatherTicks = DateTimeOffset.UtcNow.Ticks;
                }
                catch (Exception ex)
                {
                    result.TemperatureC = double.IsNaN(AppSettings.LastTempC) ? (double?)null : AppSettings.LastTempC;
                    result.Condition = AppSettings.LastWeather;
                    result.Humidity = AppSettings.LastHumidity >= 0 ? AppSettings.LastHumidity : (int?)null;
                    result.Note = DescribeNetworkError(ex);
                    Diagnostics.WriteError("天气请求失败", ex);
                }
            }

            // 3) 城市名
            result.City = await ResolveCityAsync(manualCity, result.HasFix, lat, lon);

            return result;
        }

        /// <summary>只读缓存，不联网。给设置页预览用。</summary>
        public static WeatherResult FromCacheOnly()
        {
            var result = new WeatherResult();
            FillFromCache(result);
            return result;
        }

        private static void FillFromCache(WeatherResult result)
        {
            var lat = AppSettings.LastLat;
            var lon = AppSettings.LastLon;
            result.HasFix = !double.IsNaN(lat) && !double.IsNaN(lon) && IsFixFresh();
            result.Latitude = lat;
            result.Longitude = lon;
            result.City = AppSettings.LastCity;
            result.TemperatureC = double.IsNaN(AppSettings.LastTempC) ? (double?)null : AppSettings.LastTempC;
            result.Condition = AppSettings.LastWeather;
            result.Humidity = AppSettings.LastHumidity >= 0 ? AppSettings.LastHumidity : (int?)null;
        }

        private static bool IsFixFresh()
        {
            var fixedAt = FromTicks(AppSettings.LastFixTicks);
            return (DateTimeOffset.UtcNow - fixedAt).TotalHours < FixCacheHours;
        }

        private static DateTimeOffset FromTicks(long ticks)
        {
            return ticks <= 0 ? DateTimeOffset.MinValue : new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        /// <summary>一次定位结果。UWP 5.4.7 没有 ValueTuple，所以用一个小类代替。</summary>
        private sealed class GeoFix
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }

        /// <summary>拿一次定位；失败（被拒 / 超时 / 后台任务里不可用）返回 null，原因写进日志。</summary>
        private static async Task<GeoFix> TryGetPositionAsync()
        {
            // RequestAccessAsync 必须在 UI 线程调用，所以权限申请和取坐标分开 try，便于区分原因
            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed)
                {
                    Diagnostics.Write("定位不可用：权限状态 " + access);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.WriteError("申请定位权限失败", ex);
                return null;
            }

            try
            {
                var locator = new Geolocator { DesiredAccuracyInMeters = 2000 };
                var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(8));
                var point = position.Coordinate.Point.Position;
                Diagnostics.Write("定位成功：" + point.Latitude.ToString("0.###") + "," + point.Longitude.ToString("0.###"));
                return new GeoFix { Latitude = point.Latitude, Longitude = point.Longitude };
            }
            catch (Exception ex)
            {
                Diagnostics.WriteError("取定位坐标失败", ex);
                return null;
            }
        }

        private static async Task FetchWeatherAsync(WeatherResult result, double lat, double lon)
        {
            var url = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}&current=temperature_2m,relative_humidity_2m,weather_code&timezone=auto",
                lat,
                lon);

            var json = JsonObject.Parse(await Http.GetStringAsync(url));
            if (!json.TryGetValue("current", out var currentValue) || currentValue.ValueType != JsonValueType.Object)
            {
                throw new InvalidOperationException("返回里没有 current 字段");
            }

            var current = currentValue.GetObject();
            result.TemperatureC = TryGetNumber(current, "temperature_2m");
            result.Humidity = (int?)TryGetNumber(current, "relative_humidity_2m");

            var code = TryGetNumber(current, "weather_code");
            if (code.HasValue)
            {
                var key = (int)code.Value;
                result.Condition = WeatherCodes.TryGetValue(key, out var text) ? text : "天气代码 " + key;
            }
        }

        /// <summary>城市名：手动填写 &gt; 缓存 &gt; 反查定位 &gt; 经纬度。</summary>
        private static async Task<string> ResolveCityAsync(string manualCity, bool hasFix, double lat, double lon)
        {
            if (!string.IsNullOrWhiteSpace(manualCity))
            {
                AppSettings.LastCity = manualCity.Trim();
                return AppSettings.LastCity;
            }

            var cityCachedAt = FromTicks(AppSettings.LastCityTicks);
            if (!string.IsNullOrEmpty(AppSettings.LastCity) && (DateTimeOffset.UtcNow - cityCachedAt).TotalHours < CityCacheHours)
            {
                return AppSettings.LastCity;
            }

            if (hasFix)
            {
                try
                {
                    var url = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={0:0.####}&longitude={1:0.####}&localityLanguage=zh",
                        lat,
                        lon);

                    var json = JsonObject.Parse(await Http.GetStringAsync(url));
                    var name = FirstNonEmpty(
                        TryGetString(json, "city"),
                        TryGetString(json, "locality"),
                        TryGetString(json, "principalSubdivision"));

                    if (!string.IsNullOrEmpty(name))
                    {
                        AppSettings.LastCity = name;
                        AppSettings.LastCityTicks = DateTimeOffset.UtcNow.Ticks;
                        return name;
                    }
                }
                catch
                {
                    // 反查失败就退回经纬度
                }

                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#},{1:0.#}", lat, lon);
            }

            return AppSettings.LastCity;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static double? TryGetNumber(JsonObject obj, string name)
        {
            if (obj != null && obj.TryGetValue(name, out var value) && value.ValueType == JsonValueType.Number)
            {
                return value.GetNumber();
            }

            return null;
        }

        private static string TryGetString(JsonObject obj, string name)
        {
            if (obj != null && obj.TryGetValue(name, out var value) && value.ValueType == JsonValueType.String)
            {
                return value.GetString();
            }

            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Screenlocker.Services;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenlocker.Views
{
    public sealed partial class MainPage : Page
    {
        private bool _loading;

        public MainPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _loading = true;
                TemplateBox.Text = AppSettings.Template;
                AutoRefreshSwitch.IsOn = AppSettings.AutoRefresh;
                NetworkSwitch.IsOn = AppSettings.AllowNetwork;
                LocationSwitch.IsOn = AppSettings.UseLocation;
                BadgeSwitch.IsOn = AppSettings.BadgeShowsBattery;
                CityBox.Text = AppSettings.ManualCity;
                SelectInterval(AppSettings.RefreshMinutes);
                _loading = false;

                UpdatePreview(InfoSnapshot.CaptureFromCache());
                await RefreshAsync(true);
            }
            catch (Exception ex)
            {
                _loading = false;
                Diagnostics.WriteError("设置页加载失败", ex);
                StatusText.Text = "加载失败：" + ex.Message;
            }
        }

        // ── 交互 ───────────────────────────────────────────────────

        private void Placeholder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string token)
            {
                InsertToken(TemplateBox, token);
            }
        }

        private void TemplateBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            UpdatePreview(InfoSnapshot.CaptureFromCache());
        }

        /// <summary>开关 / 下拉框改动即保存，手机上少按一次按钮。</summary>
        private async void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            SaveFromUi();
            await ApplyBackgroundTaskAndRefreshAsync("设置已保存");
        }

        private void Setting_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            AppSettings.ManualCity = CityBox.Text ?? string.Empty;
            UpdatePreview(InfoSnapshot.CaptureFromCache());
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveFromUi();
            await ApplyBackgroundTaskAndRefreshAsync("已保存");
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync(true);
        }

        private async void LockSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri("ms-settings:lockscreen"));
            }
            catch (Exception ex)
            {
                StatusText.Text = "打不开锁屏设置：" + ex.Message;
            }
        }

        // ── 保存与刷新 ─────────────────────────────────────────────

        private void SaveFromUi()
        {
            AppSettings.Template = TemplateBox.Text ?? string.Empty;
            AppSettings.AutoRefresh = AutoRefreshSwitch.IsOn;
            AppSettings.AllowNetwork = NetworkSwitch.IsOn;
            AppSettings.UseLocation = LocationSwitch.IsOn;
            AppSettings.BadgeShowsBattery = BadgeSwitch.IsOn;
            AppSettings.ManualCity = CityBox.Text ?? string.Empty;
            AppSettings.RefreshMinutes = SelectedInterval();
        }

        private async System.Threading.Tasks.Task ApplyBackgroundTaskAndRefreshAsync(string prefix)
        {
            var notes = new List<string> { prefix };

            if (AppSettings.AutoRefresh)
            {
                var error = await BackgroundTaskService.EnsureRegisteredAsync(AppSettings.RefreshMinutes);
                notes.Add(string.IsNullOrEmpty(error)
                    ? "后台每 " + AppSettings.RefreshMinutes + " 分钟刷新"
                    : error);
            }
            else
            {
                BackgroundTaskService.Unregister();
                notes.Add("后台刷新已关闭");
            }

            await RefreshAsync(true);
            StatusText.Text = string.Join("；", notes) + "；" + StatusText.Text;
        }

        private async System.Threading.Tasks.Task RefreshAsync(bool allowNetwork)
        {
            SetBusy(true);
            try
            {
                var result = await TileService.RefreshAsync(allowNetwork);
                UpdatePreview(result.Snapshot, result.Lines);
                StatusText.Text = BuildStatus(result);
            }
            catch (Exception ex)
            {
                StatusText.Text = "刷新失败：" + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private string BuildStatus(TileService.TileRefreshResult result)
        {
            var parts = new List<string>
            {
                "上次刷新 " + result.RefreshedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            };

            if (!string.IsNullOrEmpty(result.Error))
            {
                parts.Add(result.Error);
            }

            if (AppSettings.AutoRefresh)
            {
                parts.Add(BackgroundTaskService.IsRegistered
                    ? "后台任务已注册（" + AppSettings.RefreshMinutes + " 分钟）"
                    : "后台任务没注册上");
            }
            else
            {
                parts.Add("后台刷新已关闭");
            }

            return string.Join("；", parts);
        }

        // ── 预览 ───────────────────────────────────────────────────

        private void UpdatePreview(InfoSnapshot snapshot, List<string> renderedLines = null)
        {
            if (snapshot == null)
            {
                return;
            }

            var lines = renderedLines ?? TemplateEngine.RenderLines(TemplateBox.Text ?? string.Empty, snapshot.Values);
            PreviewLine1.Text = LineAt(lines, 0);
            PreviewLine2.Text = LineAt(lines, 1);
            PreviewLine3.Text = LineAt(lines, 2);

            var note = new List<string>();
            if (snapshot.BatteryPercent == null)
            {
                note.Add("读不到电量");
            }

            if (!string.IsNullOrEmpty(snapshot.Note))
            {
                note.Add(snapshot.Note);
            }

            if (string.IsNullOrEmpty(AppSettings.LastError) == false && AppSettings.LastError != snapshot.Note)
            {
                note.Add(AppSettings.LastError);
            }

            PreviewNote.Text = string.Join("；", note);

            var sourceCount = TemplateEngine.SplitLines(TemplateBox.Text ?? string.Empty).Count;
            LineHint.Text = sourceCount == 0
                ? "模板是空的，磁贴会清空。"
                : sourceCount > TemplateEngine.MaxLines
                    ? "模板有 " + sourceCount + " 行，超过三行的部分会并到第三行。"
                    : "模板共 " + sourceCount + " 行。";
        }

        private static string LineAt(List<string> lines, int index)
        {
            return lines != null && index < lines.Count ? lines[index] : string.Empty;
        }

        // ── 小工具 ─────────────────────────────────────────────────

        private void SetBusy(bool busy)
        {
            BusyRing.IsActive = busy;
            SaveButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;
        }

        private void SelectInterval(int minutes)
        {
            var wanted = minutes.ToString(CultureInfo.InvariantCulture);
            foreach (var item in IntervalBox.Items)
            {
                if (item is ComboBoxItem combo && (combo.Tag as string) == wanted)
                {
                    IntervalBox.SelectedItem = combo;
                    return;
                }
            }

            IntervalBox.SelectedIndex = 1;
        }

        private int SelectedInterval()
        {
            if (IntervalBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag as string, out var minutes))
            {
                return minutes;
            }

            return AppSettings.RefreshMinutes;
        }

        private static void InsertToken(TextBox box, string token)
        {
            var text = box.Text ?? string.Empty;
            var start = Math.Min(box.SelectionStart, text.Length);
            var length = Math.Min(box.SelectionLength, text.Length - start);

            box.Text = text.Substring(0, start) + token + text.Substring(start + length);
            box.SelectionStart = start + token.Length;
            box.SelectionLength = 0;
            box.Focus(FocusState.Programmatic);
        }
    }
}

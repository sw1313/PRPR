using Microsoft.EntityFrameworkCore;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Services;
using PRPR.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace PRPR.BooruViewer.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingPage : Page
    {
        private int _titleTapCount = 0;
        private bool _patEnabled = false;
        private DateTimeOffset _lastTapTime = DateTimeOffset.MinValue;

        public SettingPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingPage_Loaded;
        }

        private void SettingPage_Loaded(object sender, RoutedEventArgs e)
        {
            _patEnabled = !string.IsNullOrWhiteSpace(YandeSettings.Current.GitHubPat);
            UploadTagsButton.Visibility = _patEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            // Change default download folder
            var savePicker = new FolderPicker();
            savePicker.FileTypeFilter.Add("*");
            var newDefaultFolder = await savePicker.PickSingleFolderAsync();
            if (newDefaultFolder != null)
            {
                YandeSettings.Current.DefaultDownloadPath = newDefaultFolder.Path;
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("DefaultDownloadFolder", newDefaultFolder);
            }
        }

        bool IsYandereButtonEnabled
        {
            get
            {
                return YandeSettings.Current.Host != "https://yande.re";
            }
        }

        bool IsKonachanButtonEnabled
        {
            get
            {
                return YandeSettings.Current.Host != "https://konachan.com";
            }
        }


        private async void YandereButton_Click(object sender, RoutedEventArgs e)
        {
            var host = "https://yande.re";
            var passwordHashSalt = "choujin-steiner--your-password--";
            await ChangeHostAsync(host, passwordHashSalt);
        }

        private async void KonachanButton_Click(object sender, RoutedEventArgs e)
        {
            var host = "https://konachan.com";
            var passwordHashSalt = "So-I-Heard-You-Like-Mupkids-?--your-password--";
            await ChangeHostAsync(host, passwordHashSalt);
        }

        private static async Task ChangeHostAsync(string host, string passwordHashSalt)
        {
            YandeClient.SignOut();

            using (var db = new AppDbContext())
            {
                db.Database.ExecuteSqlCommand($"delete from {nameof(AppDbContext.WallpaperRecords)}; delete from {nameof(AppDbContext.LockScreenRecords)}");
            }

            YandeSettings.Current.Host = host;
            YandeSettings.Current.PasswordHashSalt = passwordHashSalt;

            AppRestartFailureReason result = await CoreApplication.RequestRestartAsync("");
        }

        private async void SyncTags_Click(object sender, RoutedEventArgs e)
        {
            SyncTagsButton.IsEnabled = false;
            SyncStatusText.Text = "正在同步...";
            try
            {
                var repo = TagTranslationRepository.Instance;
                await repo.EnsureLoadedAsync();
                var svc = new TagSyncService(repo);
                var count = await svc.DownloadAsync();
                SyncStatusText.Text = $"同步完成，共 {count} 条";
            }
            catch (Exception ex)
            {
                SyncStatusText.Text = $"同步失败: {ex.Message}";
            }
            finally
            {
                SyncTagsButton.IsEnabled = true;
            }
        }

        private async void UploadTags_Click(object sender, RoutedEventArgs e)
        {
            UploadTagsButton.IsEnabled = false;
            SyncStatusText.Text = "正在上传...";
            try
            {
                var pat = YandeSettings.Current.GitHubPat;
                var repo = TagTranslationRepository.Instance;
                await repo.EnsureLoadedAsync();
                var svc = new TagSyncService(repo);
                var msg = await svc.UploadThenDownloadAsync(pat);
                SyncStatusText.Text = msg;
            }
            catch (Exception ex)
            {
                SyncStatusText.Text = $"上传失败: {ex.Message}";
            }
            finally
            {
                UploadTagsButton.IsEnabled = true;
            }
        }

        private async void HeaderPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            var now = DateTimeOffset.Now;
            if ((now - _lastTapTime).TotalSeconds > 3)
                _titleTapCount = 0;
            _lastTapTime = now;

            _titleTapCount++;
            if (_titleTapCount >= 5)
            {
                _titleTapCount = 0;
                if (_patEnabled)
                {
                    YandeSettings.Current.GitHubPat = "";
                    _patEnabled = false;
                    UploadTagsButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    await ShowPatDialogAsync();
                }
            }
        }

        private async Task ShowPatDialogAsync()
        {
            var inputBox = new TextBox { PlaceholderText = "GitHub Personal Access Token" };
            var dialog = new ContentDialog
            {
                Title = "输入 PAT",
                Content = inputBox,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
            {
                YandeSettings.Current.GitHubPat = inputBox.Text.Trim();
                _patEnabled = true;
                UploadTagsButton.Visibility = Visibility.Visible;
            }
        }
    }
}

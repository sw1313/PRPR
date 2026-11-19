using Microsoft.QueryStringDotNET;
using Microsoft.Toolkit.Uwp.Notifications;
using PRPR.BooruViewer.Models;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Services;
using PRPR.Common;
using PRPR.Common.Models.Global;
using PRPR.Common.Services;
using System;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Storage;
using Windows.UI.Notifications;
using Windows.UI.Xaml;

namespace PRPR.BooruViewer.Tasks
{
    public class WallpaperUpdateTask
    {
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            var d = taskInstance.GetDeferral();
            try
            {
                await RunAsync();
            }
            finally
            {
                d.Complete();
            }
        }

        public static async Task RunAsync(bool throwOnError = false)
        {
            var yandeSettings = YandeSettings.Current;
            var appSettings = AppSettings.Current;
            var key = yandeSettings.WallpaperUpdateTaskSearchKey;
            var shuffle = yandeSettings.WallpaperUpdateTaskShuffleCount;
            var filter = yandeSettings.WallpaperPostFilter;

            try
            {
                var metaTags = filter.BuildMetaTags();
                var fullQuery = string.IsNullOrWhiteSpace(metaTags) ? key : (key + " " + metaTags);
                var posts = await Posts.DownloadPostsAsync(1, $"{YandeClient.HOST}/post.xml?tags={ WebUtility.UrlEncode(fullQuery) }");

                var filteredPosts = new FilteredCollection<Post, Posts>(posts, filter);

                // Set wallpaper
                await AnimePersonalization.SetBackgroundImageAsync(filteredPosts, shuffle, yandeSettings.WallpaperUpdateTaskCropMethod, appSettings.ScreenSize, false, yandeSettings.WallpaperUpdateTaskQuality);

                // Motice user about the change
                // TODO: need opt out settings
                //BigImageToast(yandeSettings.WallpaperUpdateTaskCurrentImageID, yandeSettings.WallpaperUpdateTaskCurrentImageFileUri, yandeSettings.AvatarUri);
            }
            catch (Exception ex)
            {
                ToastService.ToastDebug("Cannot Update Wallpaper", ex.StackTrace);
                ToastService.ToastDebug("Cannot Update Wallpaper", ex.Message);
                if (throwOnError)
                {
                    throw;
                }
            }
        }


        public static void BigImageToast(string id, string imageUri, string avatarUri)
        {
            ToastContent toastContent = new ToastContent()
            {
                Launch = "Toast",

                Actions = new ToastActionsCustom()
                {
                    Buttons =
                {
                    new ToastButton("Favorite", new QueryString()
                    {
                        { "action", "favorite" },
                        { "id", id }
                    }.ToString())
                    {
                        ActivationType = ToastActivationType.Background
                    },

                    new ToastButton("Undo", new QueryString()
                    {
                        { "action", "undoWallpaper" },
                        { "id", id }
                    }.ToString()) { ActivationType = ToastActivationType.Background },

                    new ToastButton("Dismiss", "dismiss") { ActivationType = ToastActivationType.Protocol },
                }
                },

                Visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                    {
                        new AdaptiveText()
                        {
                            Text = "Your wallpaper is updated!",
                            HintStyle = AdaptiveTextStyle.Header
                        },

                        new AdaptiveText()
                        {
                            Text = "Happy?",
                            HintStyle = AdaptiveTextStyle.Body
                        },

                        //new AdaptiveImage()
                        //{
                        //    Source = imageUri,
                        //    HintRemoveMargin = true,
                        //    HintCrop = AdaptiveImageCrop.None,
                        //},
                    },

                        HeroImage = new ToastGenericHeroImage()
                        {
                            Source = imageUri
                        },

                        Attribution = new ToastGenericAttributionText()
                        {
                            Text = "prpr"
                        },

                        AppLogoOverride = new ToastGenericAppLogo()
                        {
                            Source = "Assets/Square44x44Logo.targetsize-256_altform-unplated.png",// avatarUri,
                            HintCrop = ToastGenericAppLogoCrop.None,

                        },


                    }
                }
            };
            ToastNotification toast = new ToastNotification(toastContent.GetXml());
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }
        
    }
}

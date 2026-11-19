using PRPR.BooruViewer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System.UserProfile;
using Windows.UI.Xaml.Media.Imaging;

namespace PRPR.BooruViewer.Services
{
    public class PersonalizationHelper
    {
        internal static readonly SemaphoreSlim WallpaperUpdateLock = new SemaphoreSlim(1, 1);
        internal static readonly SemaphoreSlim LockscreenUpdateLock = new SemaphoreSlim(1, 1);

        internal static async Task DeleteImageFilesAsync(IEnumerable<StorageFile> files)
        {
            foreach (var file in files)
            {
                try
                {
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        public static async Task SetLockScreen(Post post, Rect cropRect)
        {
            await LockscreenUpdateLock.WaitAsync();
            StorageFile imageSaved = null;
            var imageApplied = false;
            try
            {
                var imageFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Lockscreens", CreationCollisionOption.OpenIfExists);
                var previousFiles = await imageFolder.GetFilesAsync();
                Uri urlToDownload;
                if (false) // TODO: detect whether the device is a phone
                {
                    // Only select file smaller than 2MB as the system required
                    if (post.FileSize <= 2000000)
                    {
                        urlToDownload = new Uri(post.FileUrl);
                    }
                    else if (post.JpegFileSize <= 2000000)
                    {
                        urlToDownload = new Uri(post.JpegUrl);
                    }
                    else if (post.SampleFileSize <= 2000000)
                    {
                        urlToDownload = new Uri(post.SampleUrl);
                    }
                    else
                    {
                        urlToDownload = new Uri(post.PreviewUrl);
                    }
                }
                else
                {
                    urlToDownload = new Uri(post.JpegUrl);
                }

                // Download the file into local storage
                imageSaved = await DownloadImageFromUri(urlToDownload, BackgroundTaskType.LockScreen);


                // Crop if needed
                //if (cropRect)
                //{
                    await CropBitmap.GetCroppedBitmapAsync(imageSaved, cropRect, 1);
                //}

                // Set the lockScreen
                imageApplied = await ChangeLockScreenBackground(imageSaved);
                if (!imageApplied)
                {
                    throw new InvalidOperationException("System rejected the background image update.");
                }
                await DeleteImageFilesAsync(previousFiles.OrderByDescending(file => file.DateCreated).Skip(1));
            }
            finally
            {
                if (!imageApplied && imageSaved != null)
                {
                    await DeleteImageFilesAsync(new[] { imageSaved });
                }
                LockscreenUpdateLock.Release();
            }
        }

        public static async Task SetWallPaper(Post post)
        {
            await WallpaperUpdateLock.WaitAsync();
            StorageFile imageSaved = null;
            var imageApplied = false;
            try
            {
                var imageFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Wallpapers", CreationCollisionOption.OpenIfExists);
                var previousFiles = await imageFolder.GetFilesAsync();
                Uri urlToDownload;
                if (false) // TODO: detect whether the device is a phone
                {
                    // Only select file smaller than 2MB as the system required
                    if (post.FileSize <= 2000000)
                    {
                        urlToDownload = new Uri(post.FileUrl);
                    }
                    else if (post.JpegFileSize <= 2000000)
                    {
                        urlToDownload = new Uri(post.JpegUrl);
                    }
                    else if (post.SampleFileSize <= 2000000)
                    {
                        urlToDownload = new Uri(post.SampleUrl);
                    }
                    else
                    {
                        urlToDownload = new Uri(post.PreviewUrl);
                    }
                }
                else
                {
                    urlToDownload = new Uri(post.FileUrl);
                }

                // Download the file into local storage
                imageSaved = await DownloadImageFromUri(urlToDownload, BackgroundTaskType.WallPaper);

                // Set the lockScreen
                imageApplied = await ChangeWallPaper(imageSaved);
                if (!imageApplied)
                {
                    throw new InvalidOperationException("System rejected the background image update.");
                }
                await DeleteImageFilesAsync(previousFiles.OrderByDescending(file => file.DateCreated).Skip(1));
            }
            finally
            {
                if (!imageApplied && imageSaved != null)
                {
                    await DeleteImageFilesAsync(new[] { imageSaved });
                }
                WallpaperUpdateLock.Release();
            }
        }

        public static async Task<StorageFile> DownloadImageFromUri(Uri uri, BackgroundTaskType type)
        {
            // Save the image to storage
            Windows.Web.Http.HttpClient client = new Windows.Web.Http.HttpClient();
            var referer = uri.ToString();
            client.DefaultRequestHeaders.Add("Referer", referer);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (IE 11.0; Windows NT 6.3; Trident/7.0; .NET4.0E; .NET4.0C; rv:11.0) like Gecko");


            var downloadTask = client.GetBufferAsync(uri);
            var resultBuffer = (await downloadTask);
            StorageFolder localFolder = ApplicationData.Current.LocalFolder;

            var imageFolder = await localFolder.CreateFolderAsync(type == BackgroundTaskType.LockScreen ? "Lockscreens" : "Wallpapers", CreationCollisionOption.OpenIfExists);


            var imageFile = await imageFolder.CreateFileAsync(Guid.NewGuid().ToString("N") + ".jpg", CreationCollisionOption.FailIfExists);
            try
            {
                await FileIO.WriteBufferAsync(imageFile, resultBuffer);
                using (var stream = await imageFile.OpenAsync(FileAccessMode.Read))
                {
                    await BitmapDecoder.CreateAsync(stream);
                }
                return imageFile;
            }
            catch
            {
                await DeleteImageFilesAsync(new[] { imageFile });
                throw;
            }
        }



        public static async Task<bool> ChangeWallPaper(StorageFile imageFile)
        {
            if (UserProfilePersonalizationSettings.IsSupported())
            {
                UserProfilePersonalizationSettings settings = UserProfilePersonalizationSettings.Current;
                return await settings.TrySetWallpaperImageAsync(imageFile);
            }
            else
            {
                return false;
            }
        }

        public static async Task<bool> ChangeLockScreenBackground(StorageFile imageFile)
        {
            if (UserProfilePersonalizationSettings.IsSupported())
            {
                UserProfilePersonalizationSettings settings = UserProfilePersonalizationSettings.Current;
                return await settings.TrySetLockScreenImageAsync(imageFile);
            }
            else
            {
                return false;
            }
        }







        public enum BackgroundTaskType
        {
            LockScreen = 0,
            WallPaper = 1,
        }
    }
}

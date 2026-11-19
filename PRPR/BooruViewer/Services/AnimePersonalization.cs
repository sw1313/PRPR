using Microsoft.Toolkit.Uwp.Notifications;
using PRPR.BooruViewer.Models;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Controls;
using PRPR.Common;
using PRPR.Common.Models.Global;
using PRPR.Common.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.UserProfile;
using Windows.UI.Notifications;
using Windows.Web.Http;

namespace PRPR.BooruViewer.Services
{
    public class AnimePersonalization
    {
        struct ImageCandidate
        {
            public string Url { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }
        }

        public const string TILE_FOLDER_NAME = "Tile";
        public const string WALLPAPER_FOLDER_NAME = "Wallpaper";
        public const string LOCKSCREEN_FOLDER_NAME = "Lockscreen";

        public static void ResetTile()
        {
            TileUpdateManager.CreateTileUpdaterForApplication().Clear();
        }

        public static async Task SetTileAsync(FilteredCollection<Post, Posts> posts)
        {
            List<string> faces = new List<string>();
            int postPointer = 0;

            // Remove all old face file first
            try
            {
                var imageFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(TILE_FOLDER_NAME, CreationCollisionOption.ReplaceExisting);
            }
            catch (Exception ex)
            {
                
            }

            // Download up to 20 faces
            while (faces.Count < 20 && ((posts.Count - 1 > postPointer) || (posts.HasMoreItems)))
            {
                if ((posts.Count - 1 < postPointer) && (posts.HasMoreItems))
                {
                    // Need to download more posts data
                    await posts.LoadMoreItemsAsync(1);
                }
                else
                {
                    // No need to download more posts data, read next post directly
                    Post post = await GetPostAtAsync(posts, postPointer);
                    var previewBuffer = await DownloadBufferAsync(post.PreviewUrl);
                    var rects = (await DetechFromBufferAsync(previewBuffer, 5, 55)).Faces;
                    await SaveFacesForTileAsync(rects, previewBuffer, post.Id);

                    foreach (var rect in rects)
                    {
                        faces.Add($"{post.Id}-{rects.IndexOf(rect)}.jpg");
                    }

                    postPointer++;
                }
            }

            // Update the rile notification
            if (true)
            {
                UpdateTile(faces);
            }
        }
        
        public static async Task<string> SetBackgroundImageAsync(FilteredCollection<Post, Posts> posts, uint postShuffle, CropMethod method, Size screenSize, bool isLockscreen, int qualityLevel)
        {
            var updateLock = isLockscreen ? PersonalizationHelper.LockscreenUpdateLock : PersonalizationHelper.WallpaperUpdateLock;
            await updateLock.WaitAsync();
            try
            {
                return await SetBackgroundImageCoreAsync(posts, postShuffle, method, screenSize, isLockscreen, qualityLevel);
            }
            finally
            {
                updateLock.Release();
            }
        }

        private static async Task<string> SetBackgroundImageCoreAsync(FilteredCollection<Post, Posts> posts, uint postShuffle, CropMethod method, Size screenSize, bool isLockscreen, int qualityLevel)
        {
            if (!UserProfilePersonalizationSettings.IsSupported())
            {
                throw new InvalidOperationException("System personalization is not supported on this device.");
            }

            var folderName = isLockscreen ? LOCKSCREEN_FOLDER_NAME : WALLPAPER_FOLDER_NAME;
            var yandeSettings = YandeSettings.Current;
            var previousImageUri = isLockscreen
                ? yandeSettings.LockscreenUpdateTaskCurrentImageFileUri
                : yandeSettings.WallpaperUpdateTaskCurrentImageFileUri;
            var previousFileName = Uri.UnescapeDataString(previousImageUri.Substring(previousImageUri.LastIndexOf('/') + 1));
            var previousOriginalName = Path.GetFileNameWithoutExtension(previousFileName) + "-original.jpg";
            var post = await SelectBackgroundPostAsync(posts, postShuffle);
            var previewBuffer = await DownloadBufferAsync(post.PreviewUrl);
            var imageCandidate = GetBestImageCandidate(post, qualityLevel);
            if (String.IsNullOrEmpty(imageCandidate.Url))
            {
                throw new InvalidOperationException("Cannot find a usable image URL for the selected post.");
            }

            var imageFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
            var previousFiles = await imageFolder.GetFilesAsync();
            var largeBuffer = await DownloadBufferAsync(imageCandidate.Url);
            StorageFile imageFile = null;
            StorageFile originalFile = null;
            var imageApplied = false;
            try
            {
                // A retry of the same post must keep the currently displayed file intact.
                var fileStem = $"{post.Id}-{Guid.NewGuid():N}";
                imageFile = await imageFolder.CreateFileAsync(fileStem + ".jpg", CreationCollisionOption.FailIfExists);
                await FileIO.WriteBufferAsync(imageFile, largeBuffer);
                originalFile = await imageFolder.CreateFileAsync(fileStem + "-original.jpg", CreationCollisionOption.FailIfExists);
                await FileIO.WriteBufferAsync(originalFile, largeBuffer);
                await CropImageFile(imageFile, new Size(imageCandidate.Width, imageCandidate.Height), method, screenSize, previewBuffer);
                using (var stream = await imageFile.OpenAsync(FileAccessMode.Read))
                {
                    await BitmapDecoder.CreateAsync(stream);
                }

                var personalizationSettings = UserProfilePersonalizationSettings.Current;
                imageApplied = isLockscreen
                    ? await personalizationSettings.TrySetLockScreenImageAsync(imageFile)
                    : await personalizationSettings.TrySetWallpaperImageAsync(imageFile);
                if (!imageApplied)
                {
                    throw new InvalidOperationException(isLockscreen
                        ? "System rejected the lockscreen image update."
                        : "System rejected the wallpaper image update.");
                }

                var id = post.Id.ToString();
                var imageUri = $"ms-appdata:///local/{folderName}/{imageFile.Name}";
                // Persist the applied file before recording history, which can fail independently.
                if (isLockscreen)
                {
                    yandeSettings.LockscreenUpdateTaskCurrentImageFileUri = imageUri;
                    yandeSettings.LockscreenUpdateTaskCurrentImageID = id;
                }
                else
                {
                    yandeSettings.WallpaperUpdateTaskCurrentImageFileUri = imageUri;
                    yandeSettings.WallpaperUpdateTaskCurrentImageID = id;
                }

                using (var db = new AppDbContext())
                {
                    if (isLockscreen)
                    {
                        db.LockScreenRecords.Add(LockScreenRecord.Create(post));
                    }
                    else
                    {
                        db.WallpaperRecords.Add(WallpaperRecord.Create(post));
                    }
                    db.SaveChanges();
                }

                // Keep the previous successful image as a fallback and retire older files.
                await PersonalizationHelper.DeleteImageFilesAsync(previousFiles.Where(file =>
                    file.Name != previousFileName && file.Name != previousOriginalName));
                return id;
            }
            finally
            {
                if (!imageApplied)
                {
                    await PersonalizationHelper.DeleteImageFilesAsync(new[] { imageFile, originalFile }.Where(file => file != null));
                }
            }
        }

        private static async Task<Post> SelectBackgroundPostAsync(FilteredCollection<Post, Posts> posts, uint postShuffle)
        {
            Random rnd = new Random();
            int pointer = rnd.Next((int)postShuffle);

            // Load as many post as possible until reaching the pointer
            while ((pointer >= posts.Count) && (posts.HasMoreItems))
            {
                await posts.LoadMoreItemsAsync(1);
            }

            if (posts.Count == 0)
            {
                throw new InvalidOperationException("No posts match the current search and filter settings.");
            }

            if (posts.Count <= 1)
            {
                pointer = 0;
            }
            else if (posts.Count - 1 < pointer)
            {
                // Not enough posts to shuffle, shuffle for a lower amount of posts
                pointer = rnd.Next(posts.Count);
            }

            // Select a post
            return posts[pointer];

        }

        private static ImageCandidate GetBestImageCandidate(Post post, int qualityLevel)
        {
            var original = CreateImageCandidate(post.FileUrl, post.Width, post.Height);
            var jpeg = CreateImageCandidate(post.JpegUrl, post.JpegWidth, post.JpegHeight);
            var sample = CreateImageCandidate(post.SampleUrl, post.SampleWidth, post.SampleHeight);
            var preview = CreateImageCandidate(post.PreviewUrl, post.PreviewWidth, post.PreviewHeight);

            switch (qualityLevel)
            {
                case 2:
                    return FirstAvailable(original, jpeg, sample, preview);
                case 1:
                    return FirstAvailable(jpeg, original, sample, preview);
                default:
                    return FirstAvailable(sample, jpeg, original, preview);
            }
        }

        private static ImageCandidate CreateImageCandidate(string url, int width, int height)
        {
            return new ImageCandidate()
            {
                Url = url,
                Width = width,
                Height = height
            };
        }

        private static ImageCandidate FirstAvailable(params ImageCandidate[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!String.IsNullOrEmpty(candidate.Url) && candidate.Width > 0 && candidate.Height > 0)
                {
                    return candidate;
                }
            }

            return default(ImageCandidate);
        }

        private static async Task<IBuffer> DownloadBufferAsync(string url)
        {
            using (var httpClient = YandeClient.CreateHttpClient())
            {
                return await YandeClient.GetBufferAsync(httpClient, new Uri(url));
            }
        }

        private static async Task CropImageFile(StorageFile imageFile, Size imageSize, CropMethod method, Size screenSize, IBuffer previewBuffer)
        {
            switch (method)
            {
                case CropMethod.None:
                    break;
                case CropMethod.Center:
                    {
                        var cropRect = CropBitmap.GetCenterCropRect(imageSize, screenSize);
                        await CropBitmap.GetCroppedBitmapAsync(imageFile, cropRect, 1.0);
                    }
                    break;
                case CropMethod.TopMiddle:
                    {
                        var cropRect = CropBitmap.GetTopMiddleCropRect(imageSize, screenSize);
                        await CropBitmap.GetCroppedBitmapAsync(imageFile, cropRect, 1.0);
                    }
                    break;
                case CropMethod.MostFaces:
                case CropMethod.BiggestFace:
                    var result = await DetechFromBufferAsync(previewBuffer, 3, 25);
                    var faces = ImageCropper.ScaleFaces(result.Faces, imageSize, result.FrameSize);
                    if (faces.Count() > 0)
                    {
                        if (method == CropMethod.BiggestFace)
                        {
                            faces = faces.OrderByDescending(o => o.Width).Take(1);
                        }
                        var cropRect = CropBitmap.GetGreedyCropRect(imageSize, screenSize, faces);
                        await CropBitmap.GetCroppedBitmapAsync(imageFile, cropRect, 1.0);
                    }
                    else
                    {
                        // No face detected, fall back crop
                        var cropRect = CropBitmap.GetTopMiddleCropRect(imageSize, screenSize);
                        await CropBitmap.GetCroppedBitmapAsync(imageFile, cropRect, 1.0);
                    }
                    break;
                default:
                    break;
            }
        }
        



        private static void UpdateTile(List<string> faceFileNames)
        {
            var peopleTile9 = new TileBindingContentPeople();
            var peopleTile15 = new TileBindingContentPeople();
            var peopleTile20 = new TileBindingContentPeople();

            foreach (var faceFileName in faceFileNames)
            {
                if (peopleTile9.Images.Count < 9)
                {
                    peopleTile9.Images.Add(new TileBasicImage() { Source = $"ms-appdata:///local/{TILE_FOLDER_NAME}/{faceFileName}" });
                }

                if (peopleTile15.Images.Count < 15)
                {
                    peopleTile15.Images.Add(new TileBasicImage() { Source = $"ms-appdata:///local/{TILE_FOLDER_NAME}/{faceFileName}" });
                }

                if (peopleTile20.Images.Count < 20)
                {
                    peopleTile20.Images.Add(new TileBasicImage() { Source = $"ms-appdata:///local/{TILE_FOLDER_NAME}/{faceFileName}" });
                }
            }


            TileContent content = new TileContent()
            {
                Visual = new TileVisual()
                {
                    TileMedium = new TileBinding()
                    {
                        Content = peopleTile9
                    },
                    TileWide = new TileBinding()
                    {
                        Content = peopleTile15
                    },
                    TileLarge = new TileBinding()
                    {
                        Content = peopleTile20
                    }
                }
            };

            TileNotification tileNotification = new TileNotification(content.GetXml());
            TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotification);
        }

        private static async Task SaveFacesForTileAsync(List<Rect> faces, IBuffer imageBuffer, int postId)
        {
            foreach (var face in faces)
            {
                var imageFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(TILE_FOLDER_NAME, CreationCollisionOption.OpenIfExists);
                var imageFile = await imageFolder.CreateFileAsync($"{postId}-{faces.IndexOf(face)}.jpg", CreationCollisionOption.ReplaceExisting);

                await FileIO.WriteBufferAsync(imageFile, imageBuffer);

                await CropBitmap.GetCroppedBitmapAsync(imageFile, face, 1);
            }
        }

        private static async Task<Post> GetPostAtAsync(FilteredCollection<Post, Posts> posts, int postPointer)
        {
            while ((postPointer >= posts.Count) && (posts.HasMoreItems))
            {
                await posts.LoadMoreItemsAsync(1);
            }

            return posts[postPointer];
        }

        private static async Task<DetechResult> DetechFromBufferAsync(IBuffer buffer, int minNeighbors = 5, Single minSize = 30)
        {
            // Detect the face(s)
            var c = new Imaging.AnimeFaceDetector();
            BitmapDecoder bd = await BitmapDecoder.CreateAsync(buffer.AsStream().AsRandomAccessStream());
            BitmapFrame bf = await bd.GetFrameAsync(0);
            List<Rect> rects = new List<Rect>();

            await Task.Run(async () =>
            {
                var s = await c.DetectBitmap(bf, 1.05, minNeighbors, new Size(minSize, minSize));
                rects = s.ToList();
            });

            return new DetechResult() {Faces = rects, FrameSize = new Size(bf.PixelWidth, bf.PixelHeight) };
        }
    }

    public class DetechResult
    {
        public List<Rect> Faces;
        public Size FrameSize;
    }

    public enum CropMethod
    {
        None = 0,
        TopMiddle,
        Center,
        BiggestFace,
        MostFaces,
    }
}

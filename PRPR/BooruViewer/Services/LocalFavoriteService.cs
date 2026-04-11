using PRPR.BooruViewer.Models;
using PRPR.Common.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Storage;

namespace PRPR.BooruViewer.Services
{
    public static class LocalFavoriteService
    {
        private const string FileName = "booru_local_favorites.xml";
        private static readonly SemaphoreSlim Locker = new SemaphoreSlim(1, 1);

        public static async Task<bool> ContainsAsync(int postId)
        {
            await Locker.WaitAsync();
            try
            {
                var store = await ReadStoreAsync();
                return store.Items.Any(o => o.Post?.Id == postId);
            }
            finally
            {
                Locker.Release();
            }
        }

        public static async Task AddOrUpdateAsync(Post post)
        {
            if (post == null)
            {
                return;
            }

            await Locker.WaitAsync();
            try
            {
                var store = await ReadStoreAsync();
                var existing = store.Items.FirstOrDefault(o => o.Post?.Id == post.Id);
                if (existing == null)
                {
                    store.Items.Add(new LocalFavoriteItem()
                    {
                        SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                        Post = post
                    });
                }
                else
                {
                    existing.Post = post;
                    existing.SavedAtUtcTicks = DateTime.UtcNow.Ticks;
                }

                await WriteStoreAsync(store);
            }
            finally
            {
                Locker.Release();
            }
        }

        public static async Task RemoveAsync(int postId)
        {
            await Locker.WaitAsync();
            try
            {
                var store = await ReadStoreAsync();
                store.Items.RemoveAll(o => o.Post?.Id == postId);
                await WriteStoreAsync(store);
            }
            finally
            {
                Locker.Release();
            }
        }

        public static async Task<List<Post>> GetPostsAsync(int sortingModeSelectedIndex)
        {
            await Locker.WaitAsync();
            try
            {
                var store = await ReadStoreAsync();
                IEnumerable<LocalFavoriteItem> items = store.Items.Where(o => o.Post != null);
                switch (sortingModeSelectedIndex)
                {
                    case 1:
                        items = items.OrderBy(o => o.Post.Id);
                        break;
                    case 2:
                        items = items.OrderByDescending(o => o.Post.Id);
                        break;
                    case 3:
                        items = items.OrderByDescending(o =>
                        {
                            int score;
                            return int.TryParse(o.Post.Score, out score) ? score : 0;
                        });
                        break;
                    case 4:
                        items = items.OrderBy(o =>
                        {
                            int score;
                            return int.TryParse(o.Post.Score, out score) ? score : 0;
                        });
                        break;
                    default:
                        items = items.OrderByDescending(o => o.SavedAtUtcTicks);
                        break;
                }
                return items.Select(o => o.Post).ToList();
            }
            finally
            {
                Locker.Release();
            }
        }

        private static async Task<LocalFavoriteStore> ReadStoreAsync()
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.OpenIfExists);
            var xml = await FileIO.ReadTextAsync(file);
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new LocalFavoriteStore();
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(LocalFavoriteStore));
                using (StringReader reader = new StringReader(xml))
                {
                    return serializer.Deserialize(reader) as LocalFavoriteStore ?? new LocalFavoriteStore();
                }
            }
            catch
            {
                return new LocalFavoriteStore();
            }
        }

        private static async Task WriteStoreAsync(LocalFavoriteStore store)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
            var xml = SerializationService.SerializeToString(store);
            await FileIO.WriteTextAsync(file, xml);
        }
    }

    [XmlRoot("favorites")]
    public class LocalFavoriteStore
    {
        [XmlElement("favorite")]
        public List<LocalFavoriteItem> Items { get; set; } = new List<LocalFavoriteItem>();
    }

    public class LocalFavoriteItem
    {
        [XmlElement("saved_at_utc_ticks")]
        public long SavedAtUtcTicks { get; set; }

        [XmlElement("post")]
        public Post Post { get; set; }
    }
}

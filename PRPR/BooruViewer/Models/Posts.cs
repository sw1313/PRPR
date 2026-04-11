using PRPR.BooruViewer.Services;
using PRPR.Common.Controls;
using PRPR.Common.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Foundation;
using Windows.UI.Xaml.Data;
using Windows.Web.Http;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;

namespace PRPR.BooruViewer.Models
{
    public class Posts : ObservableCollection<Post>, ISupportIncrementalLoading
    {
        const int limit = 100; 

        public int TotalCount { get; set; }

        public int Offset { get; set; } = 0;

        public string Uri { get; set; }

        public int NextPage { get; set; } = 1;

        public virtual bool HasMoreItems
        {
            get
            {
                return this.Count < this.TotalCount;
            }
        }

        public virtual IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return AsyncInfo.Run((c) => LoadMoreItemsAsync(c, count));
        }

        private async Task<LoadMoreItemsResult> LoadMoreItemsAsync(CancellationToken c, uint count)
        {
            try
            {
                var nextPage = this.NextPage;
                var nextPagePosts = await DownloadPostsAsync(nextPage, this.Uri);
                var existingIds = new HashSet<int>(this.Select(post => post.Id));
                var uniquePosts = nextPagePosts.Where(post => existingIds.Add(post.Id)).ToList();

                bool isListUnchanged = nextPage == this.NextPage;
                if (isListUnchanged)
                {
                    foreach (var item in uniquePosts)
                    {
                        this.Add(item);
                    }

                    this.TotalCount = nextPagePosts.TotalCount;
                    this.Offset = nextPagePosts.Offset;
                    this.Uri = nextPagePosts.Uri;
                    this.NextPage = nextPagePosts.NextPage;

                    return new LoadMoreItemsResult { Count = (uint)uniquePosts.Count };
                }
                else
                {
                    // There are other items loaded during this download
                    // Prevent duplicate items
                    return new LoadMoreItemsResult { Count = 0 };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载更多 Post 时发生错误: {ex.Message}");
                throw;
            }
        }

        public static async Task<Posts> DownloadPostsAsync(int page, string uri)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                YandeClient.ApplyDefaultHeaders(httpClient);
                var normalizedUri = NormalizeUri(uri);
                var fullUri = BuildPageUri(normalizedUri, page);

                // 发送 GET 请求并获取 XML 内容
                var xml = await httpClient.GetStringAsync(new Uri(fullUri));
                var p = Posts.ReadFromXml(xml);
                p.Uri = normalizedUri;
                p.NextPage = page + 1;
                return p;
            }
        }

        private static string NormalizeUri(string uri)
        {
            var normalized = Regex.Replace(uri, @"([?&])page=\d+&?", "$1", RegexOptions.IgnoreCase);
            return normalized.TrimEnd('&', '?');
        }

        private static string BuildPageUri(string uri, int page)
        {
            return uri.Contains("?") ? $"{uri}&page={page}" : $"{uri}?page={page}";
        }

        private static Posts ReadFromXml(string xml)
        {
            XmlSerializer deserializer = new XmlSerializer(typeof(SerializablePosts));
            using (StringReader reader = new StringReader(xml))
            {
                var result = (SerializablePosts)deserializer.Deserialize(reader);
                var p = new Posts() { TotalCount = result.TotalCount, Offset = result.Offset };
                foreach (var item in result.Items)
                {
                    p.Add(item);
                }
                return p;
            }
        }
    }

    [XmlType("posts")]
    public class SerializablePosts
    {
        [XmlElement("post")]
        public List<Post> Items { get; set; }

        [XmlAttribute("count")]
        public int TotalCount { get; set; }

        [XmlAttribute("offset")]
        public int Offset { get; set; } = 0;
    }

    // 定义 AsyncInfo 帮助类（如果 AsyncInfo.Run 仍不可用）
    public static class AsyncInfo
    {
        public static IAsyncOperation<TResult> Run<TResult>(Func<CancellationToken, Task<TResult>> taskFunction)
        {
            return taskFunction(CancellationToken.None).AsAsyncOperation();
        }
    }

    // 自定义 Post 比较器 (根据 Post.Id 判断是否相等)
    public class PostComparer : IEqualityComparer<Post>
    {
        public bool Equals(Post x, Post y)
        {
            return x.Id == y.Id;
        }

        public int GetHashCode(Post obj)
        {
            return obj.Id.GetHashCode();
        }
    }
}
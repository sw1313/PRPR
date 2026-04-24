using PRPR.BooruViewer.Models;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRPR.BooruViewer.ViewModels
{
    public class FeatureViewModel
    {
        public Posts RecentPosts { get; set; }


        public ObservableCollection<Post> TopToday { get; } = new ObservableCollection<Post>();

        public ObservableCollection<FeaturedTag> Tags { get; } = new ObservableCollection<FeaturedTag>();
        
        public ObservableCollection<FeaturedTag> Characters { get; } = new ObservableCollection<FeaturedTag>();

        // 浏览筛选变化时自动跟随刷新
        private static readonly HashSet<string> FilterTriggerProperties = new HashSet<string>
        {
            nameof(PostFilter.IsFilterSafe),
            nameof(PostFilter.IsFilterQuestionable),
            nameof(PostFilter.IsFilterExplicit),
            nameof(PostFilter.IsFilterHorizontal),
            nameof(PostFilter.IsFilterVertical),
            nameof(PostFilter.IsFilterAllowHidden),
            nameof(PostFilter.IsFilterAllowHeld),
            nameof(PostFilter.SortOrder),
            nameof(PostFilter.TimeRange),
            nameof(PostFilter.TagBlacklist),
        };

        private int _refreshToken = 0;
        private bool _hasLoadedOnce = false;

        public FeatureViewModel()
        {
            var filter = YandeSettings.Current?.SearchPostFilter;
            if (filter != null)
            {
                filter.PropertyChanged += OnFilterPropertyChanged;
            }
        }

        private async void OnFilterPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!FilterTriggerProperties.Contains(e.PropertyName)) return;
            // 首次加载前不触发（由 FeatureView.UserControl_Loaded 负责首次加载）
            if (!_hasLoadedOnce) return;

            // 去抖：聚合 300ms 内的连续变更
            var token = ++_refreshToken;
            await Task.Delay(300);
            if (token != _refreshToken) return;

            try
            {
                await Update();
            }
            catch
            {
                // 忽略网络等异常，保持 UI 原有数据
            }
        }

        public async Task Update()
        {
            // 使用浏览页筛选（服务器端评级过滤可大幅提高命中率）
            var searchFilter = YandeSettings.Current.SearchPostFilter;
            var metaTags = searchFilter.BuildMetaTags();
            var url = string.IsNullOrWhiteSpace(metaTags)
                ? $"{YandeClient.HOST}/post.xml"
                : $"{YandeClient.HOST}/post.xml?tags={System.Net.WebUtility.UrlEncode(metaTags)}";

            var posts = await Posts.DownloadPostsAsync(1, url);
            if (posts.Count == 0)
            {
                UpdateTop3(Enumerable.Empty<Post>());
                UpdateFeatureTags(Enumerable.Empty<KeyValuePair<string, TagSummary>>());
                _hasLoadedOnce = true;
                return;
            }

            var filterFunc = searchFilter.Function;
            // 目标：过滤后至少 60 张，保证能凑满 6 张推荐标签
            const int targetFilteredCount = 60;
            const int maxLoads = 6;
            int loads = 0;
            while (posts.HasMoreItems && loads < maxLoads &&
                   posts.Where(filterFunc).Count() < targetFilteredCount)
            {
                var loaded = await posts.LoadMoreItemsAsync(100);
                if (loaded.Count == 0) break;
                loads++;
            }

            var postsToday = posts.Where(filterFunc);

            UpdateTop3(postsToday);

            await TagDataBase.DownloadLatestTagDBAsync();

            var tags = GetAllTags(postsToday);

            UpdateFeatureTags(tags);

            _hasLoadedOnce = true;
        }
        
        private void UpdateTop3(IEnumerable<Post> posts)
        {
            TopToday.Clear();
            foreach (var item in posts.OrderByDescending(o => int.Parse(o.Score)).Take(3))
            {
                TopToday.Add(item);
            }
        }

        private void UpdateFeatureTags(IEnumerable<KeyValuePair<string, TagSummary>> tags)
        {
            var nonCharTags = tags.Where(o => o.Value.Posts.Count >= 2 && 
            (o.Value.Detail.Type == TagType.None || o.Value.Detail.Type == TagType.Copyright));

            var shuffled = nonCharTags.OrderBy(a => Guid.NewGuid());
            Tags.Clear();
            foreach (var item in shuffled)
            {
                // Skip unless tags
                if (item.Key == "male" || item.Key == "tagme" || item.Key == "text")
                {
                    continue;
                }

                // Skip if all posts of it are already featured
                if (item.Value.Posts.Any(p => Tags.Any(o => o.TopPost == p)))
                {
                    continue;
                }

                Tags.Add(new FeaturedTag() {
                    Name = item.Key,
                    TopPost = item.Value.Posts.Where(p => !Tags.Any(o => o.TopPost == p))
                    .OrderBy(o => float.Parse(o.Score) / o.TagItems.Count).First()
                });
            }
        }


        private IEnumerable<KeyValuePair<string, TagSummary>> GetAllTags(IEnumerable<Post> posts)
        {
            // Rank the tags
            Dictionary<string, TagSummary> rank = new Dictionary<string, TagSummary>();
            foreach (var item in posts)
            {
                var tags = item.Tags.Split(' ');
                foreach (var tag in tags)
                {
                    if (rank.ContainsKey(tag))
                    {
                        rank[tag].Posts.Add(item);
                    }
                    else
                    {
                        var n = new TagSummary();
                        try
                        {
                            n.Detail = TagDataBase.AllTags[tag];

                        }
                        catch (Exception ex)
                        {
                            n.Detail = new TagDetail() { Name = tag, Type = TagType.None };
                        }
                        n.Posts.Add(item);
                        rank[tag] = n;
                    }
                }
            }
            var ranked = rank.OrderByDescending(o => o.Value.Posts.Count);
            return ranked;
        }

        private bool IsSolo(Post post)
        {
            // To check whether there is only one character tag in the post
            bool oneChar = false;
            foreach (var item in post.TagItems)
            {
                if (item.Type == TagType.Character)
                {
                    if (oneChar)
                    {
                        return false;
                    }
                    else
                    {
                        oneChar = true;
                    }
                }
            }
            return oneChar;
        }
    }

    public class TagSummary
    {
        public TagDetail Detail { get; set; } = null;
        public List<Post> Posts { get; } = new List<Post>();
    }

    public class FeaturedTag
    {
        public string Name { get; set; }
        
        public Post TopPost { get; set; }
    }

    public class FeaturedCharacter
    {
        public string Name { get; set; }

        public ObservableCollection<Post> SoloPosts { get; set; }


        public object Avatar
        {
            get
            {
                return null;
            }
            set
            {

            }
        }
    }
}

using PRPR.BooruViewer.Models;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Services;
using PRPR.Common;
using PRPR.Common.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.UI.Popups;

namespace PRPR.BooruViewer.ViewModels
{
    public class HomeViewModel : INotifyPropertyChanged
    {
        private const int MinimumInitialVisibleSearchPosts = 12;
        private const int MaximumInitialSearchPrefetchRounds = 4;

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private int _selectedViewIndex = 0;

        public int SelectedViewIndex
        {
            get => _selectedViewIndex;
            set
            {
                if (_selectedViewIndex != value)
                {
                    _selectedViewIndex = value;
                    NotifyPropertyChanged(nameof(SelectedViewIndex));
                }
            }
        }

        public PostFilter SearchPostFilter
        {
            get => YandeSettings.Current.SearchPostFilter;
            set
            {
                YandeSettings.Current.SearchPostFilter = value;
                NotifyPropertyChanged(nameof(SearchPostFilter));
            }
        }

        private FilteredCollection<Post, Posts> _searchPosts = null;

        public FilteredCollection<Post, Posts> SearchPosts
        {
            get => _searchPosts;
            set
            {
                if (_searchPosts != value)
                {
                    _searchPosts = value;
                    NotifyPropertyChanged(nameof(SearchPosts));
                }
            }
        }

        private ObservableCollection<Post> _favoritePosts = null;

        public ObservableCollection<Post> FavoritePosts
        {
            get => _favoritePosts;
            set
            {
                if (_favoritePosts != value)
                {
                    _favoritePosts = value;
                    NotifyPropertyChanged(nameof(FavoritePosts));
                }
            }
        }

        private List<string> FavoriteSortingMode = new List<string>()
        {
            "order:vote",
            "order:id_asc",
            "order:id",
            "order:score",
            "order:score_asc"
        };

        private int _favoriteSortingModeSelectedIndex = 0;

        public int FavoriteSortingModeSelectedIndex
        {
            get => _favoriteSortingModeSelectedIndex;
            set
            {
                if (_favoriteSortingModeSelectedIndex != value)
                {
                    _favoriteSortingModeSelectedIndex = value;
                    NotifyPropertyChanged(nameof(FavoriteSortingModeSelectedIndex));
                    SaveSetting(nameof(FavoriteSortingModeSelectedIndex), value);
                }
            }
        }

        public HomeViewModel()
        {
            LoadSettings();
            SubscribeToFilterChanges();
            // 初始化其他属性和数据
        }

        #region Settings Persistence

        private void LoadSettings()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            // Load IsFilterSafe
            if (localSettings.Values.ContainsKey(nameof(SearchPostFilter.IsFilterSafe)))
            {
                SearchPostFilter.IsFilterSafe = (bool)localSettings.Values[nameof(SearchPostFilter.IsFilterSafe)];
            }
            else
            {
                SearchPostFilter.IsFilterSafe = true; // 默认值
            }

            // Load IsFilterQuestionable
            if (localSettings.Values.ContainsKey(nameof(SearchPostFilter.IsFilterQuestionable)))
            {
                SearchPostFilter.IsFilterQuestionable = (bool)localSettings.Values[nameof(SearchPostFilter.IsFilterQuestionable)];
            }
            else
            {
                SearchPostFilter.IsFilterQuestionable = false; // 默认值
            }

            // Load IsFilterExplicit
            if (localSettings.Values.ContainsKey(nameof(SearchPostFilter.IsFilterExplicit)))
            {
                SearchPostFilter.IsFilterExplicit = (bool)localSettings.Values[nameof(SearchPostFilter.IsFilterExplicit)];
            }
            else
            {
                SearchPostFilter.IsFilterExplicit = false; // 默认值
            }

            // Load FavoriteSortingModeSelectedIndex
            if (localSettings.Values.ContainsKey(nameof(FavoriteSortingModeSelectedIndex)))
            {
                FavoriteSortingModeSelectedIndex = (int)localSettings.Values[nameof(FavoriteSortingModeSelectedIndex)];
            }
            else
            {
                FavoriteSortingModeSelectedIndex = 0; // 默认值
            }
        }

        private void SaveSetting(string key, object value)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values[key] = value;
        }

        private void SubscribeToFilterChanges()
        {
            if (SearchPostFilter != null)
            {
                SearchPostFilter.PropertyChanged += SearchPostFilter_PropertyChanged;
            }
        }

        private void SearchPostFilter_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is PostFilter filter)
            {
                switch (e.PropertyName)
                {
                    case nameof(filter.IsFilterSafe):
                        SaveSetting(nameof(filter.IsFilterSafe), filter.IsFilterSafe);
                        break;
                    case nameof(filter.IsFilterQuestionable):
                        SaveSetting(nameof(filter.IsFilterQuestionable), filter.IsFilterQuestionable);
                        break;
                    case nameof(filter.IsFilterExplicit):
                        SaveSetting(nameof(filter.IsFilterExplicit), filter.IsFilterExplicit);
                        break;
                    // 添加其他需要保存的属性
                }
            }
        }

        #endregion

        public async Task SearchAsync(string keyword)
        {
            // 验证关键词

            // 如果标签数量超过6个，则不进行搜索
            if (keyword.Split(' ').Count(o => !String.IsNullOrWhiteSpace(o)) > 6)
            {
                var resourceLoader = ResourceLoader.GetForCurrentView();
                await new MessageDialog(
                    resourceLoader.GetString("/BooruHomePage/MessageDialogTooManyTags/Content"),
                    resourceLoader.GetString("/BooruHomePage/MessageDialogTooManyTags/Title")
                ).ShowAsync();
                return;
            }

            // 如果关键词中包含任何评级元标签，则不进行搜索
            if (keyword.Split(' ').Any(o => o.Contains("rating:")))
            {
                // 解锁评级过滤器
                if (!YandeSettings.Current.IsRatingFilterUnlocked)
                {
                    YandeSettings.Current.IsRatingFilterUnlocked = true;
                }
                var resourceLoader = ResourceLoader.GetForCurrentView();
                await new MessageDialog(
                    resourceLoader.GetString("/BooruHomePage/MessageDialogRatingTag/Content"),
                    resourceLoader.GetString("/BooruHomePage/MessageDialogRatingTag/Title")
                ).ShowAsync();
                return;
            }

            // 初始化 posts 集合
            Posts posts = new Posts();
            try
            {
                string baseUrl = $"{YandeClient.HOST}/post.xml?tags={WebUtility.UrlEncode(keyword)}";

                // 获取第一页的图片
                posts = await Posts.DownloadPostsAsync(1, baseUrl);

                // 如果返回的图片数量在0-5张之间，则加载第二页的图片
                if (posts.Count <= 5)
                {
                    var additionalPosts = await Posts.DownloadPostsAsync(2, baseUrl);
                    AppendUniquePosts(posts, additionalPosts);
                }

                await EnsureMinimumVisibleSearchPostsAsync(posts);
            }
            catch (Exception ex)
            {
                // 记录异常
                Debug.WriteLine($"Error in SearchAsync: {ex.Message}");
                // 或者使用实际的日志记录框架
                // Logger.LogError(ex, "Error fetching posts");

                // 通知用户
                var resourceLoader = ResourceLoader.GetForCurrentView();
                await new MessageDialog(
                    resourceLoader.GetString("/BooruHomePage/MessageDialogFetchError/Content"),
                    resourceLoader.GetString("/BooruHomePage/MessageDialogFetchError/Title")
                ).ShowAsync();

                posts = new Posts();
            }

            // 使用筛选器更新 SearchPosts 集合
            SearchPosts = new FilteredCollection<Post, Posts>(posts, SearchPostFilter);
        }

        private async Task EnsureMinimumVisibleSearchPostsAsync(Posts posts)
        {
            if (posts == null)
            {
                return;
            }

            var rounds = 0;
            while (posts.HasMoreItems &&
                posts.Where(SearchPostFilter.Function).Count() < MinimumInitialVisibleSearchPosts &&
                rounds < MaximumInitialSearchPrefetchRounds)
            {
                var loaded = await posts.LoadMoreItemsAsync(100);
                if (loaded.Count == 0)
                {
                    break;
                }

                rounds++;
            }
        }

        public async Task UpdateFavoriteListAsync()
        {
            if (YandeSettings.Current.IsLoggedIn)
            {
                Posts favoritePosts = new Posts();
                try
                {
                    // 根据选择的排序模式确定排序字符串
                    string sortString = FavoriteSortingMode.First();
                    if (FavoriteSortingModeSelectedIndex >= 0 && FavoriteSortingModeSelectedIndex < FavoriteSortingMode.Count)
                    {
                        sortString = FavoriteSortingMode[FavoriteSortingModeSelectedIndex];
                    }

                    string baseUrl = $"{YandeClient.HOST}/post.xml?tags=vote:3:{YandeSettings.Current.UserName}+{sortString}";

                    // 获取第一页的收藏图片
                    favoritePosts = await Posts.DownloadPostsAsync(1, baseUrl);

                    // 如果返回的收藏图片数量在0-5张之间，则加载第二页的收藏图片
                    if (favoritePosts.Count <= 5)
                    {
                        var additionalFavorites = await Posts.DownloadPostsAsync(2, baseUrl);
                        AppendUniquePosts(favoritePosts, additionalFavorites);
                    }
                }
                catch (Exception ex)
                {
                    // 记录异常
                    Debug.WriteLine($"Error in UpdateFavoriteListAsync: {ex.Message}");
                    // 或者使用实际的日志记录框架
                    // Logger.LogError(ex, "Error fetching favorite posts");

                    // 通知用户
                    var resourceLoader = ResourceLoader.GetForCurrentView();
                    await new MessageDialog(
                        resourceLoader.GetString("/BooruHomePage/MessageDialogFetchError/Content"),
                        resourceLoader.GetString("/BooruHomePage/MessageDialogFetchError/Title")
                    ).ShowAsync();
                    return;
                }

                // 使用筛选后的收藏图片更新 FavoritePosts 集合
                this.FavoritePosts = new ObservableCollection<Post>(favoritePosts);
            }
            else
            {
                var favoritePosts = await LocalFavoriteService.GetPostsAsync(FavoriteSortingModeSelectedIndex);
                this.FavoritePosts = new ObservableCollection<Post>(favoritePosts);
            }
        }

        private static void AppendUniquePosts(Posts target, Posts source)
        {
            var existingIds = new HashSet<int>(target.Select(post => post.Id));
            foreach (var post in source)
            {
                if (existingIds.Add(post.Id))
                {
                    target.Add(post);
                }
            }

            target.TotalCount = source.TotalCount;
            target.Offset = source.Offset;
            target.NextPage = source.NextPage;
        }
    }
}
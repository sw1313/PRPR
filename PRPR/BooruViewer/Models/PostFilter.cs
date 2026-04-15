using PRPR.BooruViewer.Services;
using PRPR.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PRPR.BooruViewer.Models
{
    public class PostFilter : INotifyPropertyChanged, IConfigableFilter<Post>
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        // 评级过滤相关的字段和属性
        private bool _isFilterSafe = false;
        private bool _isFilterQuestionable = false;
        private bool _isFilterExplicit = false;

        public bool IsFilterSafe
        {
            get => _isFilterSafe;
            set
            {
                if (_isFilterSafe != value)
                {
                    _isFilterSafe = value;
                    NotifyPropertyChanged(nameof(IsFilterSafe));
                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        public bool IsFilterQuestionable
        {
            get => _isFilterQuestionable;
            set
            {
                if (_isFilterQuestionable != value)
                {
                    _isFilterQuestionable = value;
                    NotifyPropertyChanged(nameof(IsFilterQuestionable));
                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        public bool IsFilterExplicit
        {
            get => _isFilterExplicit;
            set
            {
                if (_isFilterExplicit != value)
                {
                    _isFilterExplicit = value;
                    NotifyPropertyChanged(nameof(IsFilterExplicit));
                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        // 方向相关的字段和属性
        private bool _isFilterHorizontal = true;
        private bool _isFilterVertical = true;
        private bool _isFilterAllowHidden = false;
        private bool _isFilterAllowHeld = false;

        public bool IsFilterHorizontal
        {
            get => _isFilterHorizontal;
            set
            {
                if (_isFilterHorizontal != value)
                {
                    _isFilterHorizontal = value;
                    NotifyPropertyChanged(nameof(IsFilterHorizontal));
                    NotifyPropertyChanged(nameof(IsFilterVerticalUnlocked));
                    NotifyPropertyChanged(nameof(IsFilterHorizontalUnlocked));

                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        public bool IsFilterVertical
        {
            get => _isFilterVertical;
            set
            {
                if (_isFilterVertical != value)
                {
                    _isFilterVertical = value;
                    NotifyPropertyChanged(nameof(IsFilterVertical));
                    NotifyPropertyChanged(nameof(IsFilterVerticalUnlocked));
                    NotifyPropertyChanged(nameof(IsFilterHorizontalUnlocked));

                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        public bool IsFilterAllowHidden
        {
            get => _isFilterAllowHidden;
            set
            {
                if (_isFilterAllowHidden != value)
                {
                    _isFilterAllowHidden = value;
                    NotifyPropertyChanged(nameof(IsFilterAllowHidden));
                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        public bool IsFilterAllowHeld
        {
            get => _isFilterAllowHeld;
            set
            {
                if (_isFilterAllowHeld != value)
                {
                    _isFilterAllowHeld = value;
                    NotifyPropertyChanged(nameof(IsFilterAllowHeld));
                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        // 保留方向相关的解锁属性
        public bool IsFilterHorizontalUnlocked => !IsFilterHorizontal || IsFilterVertical;
        public bool IsFilterVerticalUnlocked => IsFilterHorizontal || !IsFilterVertical;

        // 排序：0=按时间, 1=按评分
        private int _sortOrder = 0;
        public int SortOrder
        {
            get => _sortOrder;
            set
            {
                if (_sortOrder != value)
                {
                    _sortOrder = value;
                    NotifyPropertyChanged(nameof(SortOrder));
                }
            }
        }

        // 时间范围：0=不限, 1=今天, 2=本周(7天), 3=本月(30天), 4=今年(365天)
        private int _timeRange = 0;
        public int TimeRange
        {
            get => _timeRange;
            set
            {
                if (_timeRange != value)
                {
                    _timeRange = value;
                    NotifyPropertyChanged(nameof(TimeRange));
                }
            }
        }

        public string BuildMetaTags()
        {
            var parts = new List<string>();
            if (SortOrder == 1) parts.Add("order:score");
            switch (TimeRange)
            {
                case 1: parts.Add($"date:>={DateTime.UtcNow:yyyy-MM-dd}"); break;
                case 2: parts.Add($"date:>={DateTime.UtcNow.AddDays(-7):yyyy-MM-dd}"); break;
                case 3: parts.Add($"date:>={DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}"); break;
                case 4: parts.Add($"date:>={DateTime.UtcNow.AddDays(-365):yyyy-MM-dd}"); break;
            }
            return string.Join(" ", parts);
        }

        // 标签黑名单
        private string _tagBlacklist = String.Join(" ", new List<string> { "bikini", "buruma", "ass", "pantsu", "bra", "torn_clothes", "no_pan" });

        public string TagBlacklist
        {
            get => _tagBlacklist;
            set
            {
                if (_tagBlacklist != value)
                {
                    _tagBlacklist = value;
                    NotifyPropertyChanged(nameof(TagBlacklist));
                    NotifyPropertyChanged(nameof(Function));
                }
            }
        }

        public Func<Post, bool> Function => ToFunc();

        public Func<Post, bool> ToFunc()
        {
            var s = IsFilterSafe;
            var q = IsFilterQuestionable;
            var e = IsFilterExplicit;

            var h = IsFilterHorizontal;
            var v = IsFilterVertical;

            var a = IsFilterAllowHidden;
            var b = IsFilterAllowHeld;

            var tbl = TagBlacklist.Split(' ').ToList();

            // 判断是否需要应用评级过滤
            bool isRatingFilterActive = s || q || e;

            // 定义评级过滤条件
            Func<Post, bool> ratingCondition = o =>
                (o.Rating == "s" && s) ||
                (o.Rating == "q" && q) ||
                (o.Rating == "e" && e);

            return (o =>
                // 仅在评级过滤激活时应用评级条件
                (!isRatingFilterActive || ratingCondition(o)) &&
                // 方向过滤
                ((o.Width >= o.Height && h) || (o.Width < o.Height && v)) &&
                // 是否显示在索引中
                ((o.IsShownInIndex || a)) &&
                // 是否允许持有
                ((b || !o.IsHeld)) &&
                // 标签黑名单过滤
                (o.Tags.Split(' ').All(tag => !tbl.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            );
        }

        // 其他方法和属性...
    }
}
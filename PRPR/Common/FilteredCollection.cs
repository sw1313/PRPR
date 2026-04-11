using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;

namespace PRPR.Common
{
    public class FilteredCollection<T, TSource> : ObservableCollection<T>, ISupportIncrementalLoading where TSource : IEnumerable<T>, ISupportIncrementalLoading
    {
        
        public FilteredCollection(TSource source, IConfigableFilter<T> filter)
        {
            this._source = source;
            this.filter = filter;
            this.filter.PropertyChanged += Filter_PropertyChanged;
            Refilter();


        }

        private void Filter_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Function")
            {
                Refilter();

            }
        }

        private IConfigableFilter<T> filter = null;
        
        private TSource _source = default(TSource);

        public TSource Source
        {
            get
            {
                return _source;
            }
            set
            {
                _source = value;
                Refilter();
            }
        }


        private void Refilter()
        {
            var newSourceItems = Source == null
                ? new List<T>()
                : Source.Where(filter.Function).ToList();

            ReplaceAll(newSourceItems);
        }

        private void ReplaceAll(IList<T> items)
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void Refresh()
        {
            Refilter();
        }

        private void AppendFilteredItems(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                this.Add(item);
            }
        }

        private int GetFilteredSourceCount()
        {
            if (Source != null)
            {
                return Source.Where(filter.Function).Count();
            }

            return 0;
        }

        public bool HasMoreItems
        {
            get
            {
                return (this.Source != null && this.Source.HasMoreItems) || (this.Source != null && GetFilteredSourceCount() > this.Count());
            }
        }



        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return AsyncInfo.Run((c) => LoadMoreItemsAsync(c, count));
        }




        async Task<LoadMoreItemsResult> LoadMoreItemsAsync(CancellationToken c, uint count)
        {
            try
            {
                var oldFilteredCount = this.Count;
                var sourceCountCursor = Source?.Count() ?? 0;
                var pendingFilteredItems = Source == null
                    ? new List<T>()
                    : Source.Where(filter.Function).Skip(oldFilteredCount).ToList();

                int attempts = 0;
                while (pendingFilteredItems.Count == 0 &&
                    Source != null &&
                    Source.HasMoreItems &&
                    attempts < 4)
                {
                    var loadResult = await Source.LoadMoreItemsAsync(count);
                    var newSourceCount = Source.Count();
                    if (newSourceCount <= sourceCountCursor || loadResult.Count == 0)
                    {
                        break;
                    }

                    pendingFilteredItems.AddRange(Source.Skip(sourceCountCursor).Where(filter.Function));
                    sourceCountCursor = newSourceCount;
                    attempts++;
                }

                if (oldFilteredCount == this.Count)
                {
                    AppendFilteredItems(pendingFilteredItems);

                    return new LoadMoreItemsResult { Count = (uint)(this.Count - oldFilteredCount) };
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
                return new LoadMoreItemsResult { Count = 0 };
            }
        }

    }
}

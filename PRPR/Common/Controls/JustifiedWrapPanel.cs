using PRPR.Common.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace PRPR.Common.Controls
{
    public partial class JustifiedWrapPanel : Panel
    {
        public object ItemsSource
        {
            get { return (object)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        private async void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            InvalidateLayoutCache();
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                RecycleAll();

                if (ParentScrollViewer != null && ItemsSource is IList items && items.Count > 0)
                {
                    var parentWidth = DesiredSize.Width - Margin.Left - Margin.Right;
                    if (parentWidth <= 0)
                    {
                        parentWidth = ParentScrollViewer.ViewportWidth - Margin.Left - Margin.Right;
                    }

                    UpdateActiveRange(
                        ParentScrollViewer.VerticalOffset,
                        ParentScrollViewer.ViewportHeight,
                        parentWidth,
                        true);
                    RevirtualizeAll();
                }
                else
                {
                    InvalidateMeasure();
                }
            }
            else
            {
                var parentWidth = DesiredSize.Width - Margin.Left - Margin.Right;
                if (parentWidth <= 0 && ParentScrollViewer != null)
                {
                    parentWidth = ParentScrollViewer.ViewportWidth - Margin.Left - Margin.Right;
                }

                if (ParentScrollViewer != null)
                {
                    var rangeChanged = UpdateActiveRange(
                        ParentScrollViewer.VerticalOffset,
                        ParentScrollViewer.ViewportHeight,
                        parentWidth,
                        true);
                    var shouldRefresh = Containers.Count == 0 || rangeChanged;

                    if (shouldRefresh)
                    {
                        RevirtualizeAll();
                    }
                    else
                    {
                        InvalidateMeasure();
                    }
                }
                else
                {
                    InvalidateMeasure();
                }
            }
        }


        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(JustifiedWrapPanel), new PropertyMetadata(null, OnItemSourceChanged));

        public static async void OnItemSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (d as JustifiedWrapPanel);
            p.InvalidateLayoutCache();


            // Reassign the handler to new item set
            if (e.OldValue is INotifyCollectionChanged)
            {
                (e.OldValue as INotifyCollectionChanged).CollectionChanged -= p.ItemsSource_CollectionChanged;
            }
            if (e.NewValue is INotifyCollectionChanged)
            {
                (e.NewValue as INotifyCollectionChanged).CollectionChanged += p.ItemsSource_CollectionChanged;
            }



            if (e.OldValue is IList itemsSource)
            {
                p.RecycleAll();
            }


            p.CheckParentUpdate();
            if (p.ParentScrollViewer != null)
            {
                p.UpdateActiveRange(p.ParentScrollViewer.VerticalOffset, p.ParentScrollViewer.ViewportHeight, p.DesiredSize.Width - p.Margin.Left - p.Margin.Right, true);
                Debug.WriteLine($"OnItemSourceChanged: New Range {p.FirstActive} ~ {p.LastActive}");
                p.RevirtualizeAll();

                // It is necessary to force an UI refresh before handling the incremental loading
                // The scrollable window size sticks with old item source
                p.UpdateLayout();

                await p.CheckNeedMoreItemAsync();
            }
        }
        
        public DataTemplate ItemTemplate
        {
            get { return (DataTemplate)GetValue(ItemTemplateProperty); }
            set { SetValue(ItemTemplateProperty, value); }
        }

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(JustifiedWrapPanel), new PropertyMetadata(null));
        
        public Style ItemContainerStyle
        {
            get { return (Style)GetValue(ItemContainerStyleProperty); }
            set { SetValue(ItemContainerStyleProperty, value); }
        }

        public static readonly DependencyProperty ItemContainerStyleProperty =
            DependencyProperty.Register(nameof(ItemContainerStyle), typeof(Style), typeof(JustifiedWrapPanel), new PropertyMetadata(null));
        
        public double RowHeight
        {
            get { return (double)GetValue(RowHeightProperty); }
            set { SetValue(RowHeightProperty, value); }
        }

        public static readonly DependencyProperty RowHeightProperty =
            DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(JustifiedWrapPanel), new PropertyMetadata(100.0, OnRowHeightChanged));

        public static async void OnRowHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (d as JustifiedWrapPanel);
            p.InvalidateLayoutCache();
            p.CheckParentUpdate();
            if (p.ParentScrollViewer != null)
            {
                p.UpdateActiveRange(p.ParentScrollViewer.VerticalOffset, p.ParentScrollViewer.ViewportHeight, p.DesiredSize.Width - p.Margin.Left - p.Margin.Right, true);
                p.RevirtualizeAll();
                //p.InvalidateMeasure();
                //p.InvalidateArrange();
                await p.CheckNeedMoreItemAsync();
            }
        }
        
        internal class UvMeasure
        {
            internal double X { get; set; }

            internal double Y { get; set; }

            public UvMeasure()
            {
                X = 0.0;
                Y = 0.0;
            }

            public UvMeasure(double width, double height)
            {
                X = width;
                Y = height;
            }
        }

        sealed class RowLayoutInfo
        {
            public int StartIndex { get; set; }

            public int EndIndex { get; set; }

            public double Top { get; set; }

            public double Height { get; set; }

            public bool IsLastRow { get; set; }
        }


        public ScrollViewer ParentScrollViewer = null;

        List<RowLayoutInfo> _layoutRows = null;
        double _layoutWidth = -1;
        int _layoutItemCount = -1;
        double _layoutTargetRowHeight = -1;

        void InvalidateLayoutCache()
        {
            _layoutRows = null;
            _layoutWidth = -1;
            _layoutItemCount = -1;
            _layoutTargetRowHeight = -1;
        }

        double GetMinRowHeight()
        {
            return Math.Max(1, RowHeight * 0.75);
        }

        double GetMaxRowHeight()
        {
            return Math.Max(GetMinRowHeight(), RowHeight * 1.35);
        }

        double GetPreferredRatio(IJustifiedWrapPanelItem item)
        {
            if (item == null || item.PreferredHeight == 0)
            {
                return 1;
            }

            return item.PreferredWidth / item.PreferredHeight;
        }

        List<RowLayoutInfo> GetLayoutRows(double availableWidth)
        {
            var items = ItemsSource as IList;
            if (items == null || items.Count == 0 || availableWidth <= 0)
            {
                return new List<RowLayoutInfo>();
            }

            if (_layoutRows != null &&
                Math.Abs(_layoutWidth - availableWidth) < 0.1 &&
                _layoutItemCount == items.Count &&
                Math.Abs(_layoutTargetRowHeight - RowHeight) < 0.1)
            {
                return _layoutRows;
            }

            var rows = new List<RowLayoutInfo>();
            var targetRowHeight = RowHeight;
            var minRowHeight = GetMinRowHeight();
            var maxRowHeight = GetMaxRowHeight();
            var currentTop = 0.0;
            var rowStartIndex = 0;
            var currentRowRatio = 0.0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i] as IJustifiedWrapPanelItem;
                var itemRatio = GetPreferredRatio(item);
                currentRowRatio += itemRatio;

                var filledHeight = availableWidth / currentRowRatio;
                var rowItemCount = i - rowStartIndex + 1;

                if (filledHeight < minRowHeight && rowItemCount > 1)
                {
                    var previousRowRatio = currentRowRatio - itemRatio;
                    var previousFilledHeight = availableWidth / previousRowRatio;

                    if (Math.Abs(filledHeight - targetRowHeight) < Math.Abs(previousFilledHeight - targetRowHeight))
                    {
                        rows.Add(new RowLayoutInfo()
                        {
                            StartIndex = rowStartIndex,
                            EndIndex = i,
                            Top = currentTop,
                            Height = filledHeight,
                            IsLastRow = false
                        });
                        currentTop += filledHeight;
                        rowStartIndex = i + 1;
                        currentRowRatio = 0;
                    }
                    else
                    {
                        rows.Add(new RowLayoutInfo()
                        {
                            StartIndex = rowStartIndex,
                            EndIndex = i - 1,
                            Top = currentTop,
                            Height = previousFilledHeight,
                            IsLastRow = false
                        });
                        currentTop += previousFilledHeight;
                        rowStartIndex = i;
                        currentRowRatio = itemRatio;
                    }
                }
                else if (filledHeight <= maxRowHeight)
                {
                    rows.Add(new RowLayoutInfo()
                    {
                        StartIndex = rowStartIndex,
                        EndIndex = i,
                        Top = currentTop,
                        Height = filledHeight,
                        IsLastRow = false
                    });
                    currentTop += filledHeight;
                    rowStartIndex = i + 1;
                    currentRowRatio = 0;
                }
            }

            if (rowStartIndex < items.Count)
            {
                rows.Add(new RowLayoutInfo()
                {
                    StartIndex = rowStartIndex,
                    EndIndex = items.Count - 1,
                    Top = currentTop,
                    Height = targetRowHeight,
                    IsLastRow = true
                });
            }

            _layoutRows = rows;
            _layoutWidth = availableWidth;
            _layoutItemCount = items.Count;
            _layoutTargetRowHeight = RowHeight;
            return rows;
        }

        private void CheckParentUpdate()
        {
            if (ParentScrollViewer != (Parent as ScrollViewer))
            {
                if (ParentScrollViewer != null)
                {
                    ParentScrollViewer.ViewChanging -= ParentScrollViewer_ViewChanging;
                    ParentScrollViewer.SizeChanged -= ParentScrollViewer_SizeChanged;


                }
                ParentScrollViewer = (this.Parent as ScrollViewer);
                if (ParentScrollViewer != null)
                {
                    ParentScrollViewer.ViewChanging += ParentScrollViewer_ViewChanging;
                    ParentScrollViewer.SizeChanged += ParentScrollViewer_SizeChanged;
                }
            }
        }
        
        private async void ParentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            InvalidateLayoutCache();
            var top = (sender as ScrollViewer).HorizontalOffset;

            if (UpdateActiveRange((sender as ScrollViewer).VerticalOffset, (sender as ScrollViewer).ViewportHeight, e.NewSize.Width - this.Margin.Left - this.Margin.Right, true))
            {
                RevirtualizeAll();
            }
            await CheckNeedMoreItemAsync();
        }
        private async void ParentScrollViewer_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            // Update the active range
            var top = e.FinalView.HorizontalOffset;

            var scrollViewer = (sender as ScrollViewer);

            if (UpdateActiveRange(e.NextView.VerticalOffset, scrollViewer.ViewportHeight, this.DesiredSize.Width - this.Margin.Left - this.Margin.Right, false))
            {
                //Debug.WriteLine($"ParentScrollViewer_ViewChanging: New Range {FirstActive} ~ {LastActive}");
                RevirtualizeAll();
            }

            await CheckNeedMoreItemAsync();
        }



        private int FirstActive = -1;
        private int LastActive = -1;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="visibleTop"></param>
        /// <param name="visibleHeight"></param>
        /// <param name="parentWidth"></param>
        /// <param name="layoutChanged"></param>
        /// <param name="activeWindowScale"></param>
        /// <returns>Whether the range is updated</returns>
        bool UpdateActiveRange(double visibleTop, double visibleHeight, double parentWidth, bool layoutChanged, double activeWindowScale = 4)
        {
            var visibleCenter = visibleTop + visibleHeight / 2.0;
            var halfVisibleWindowsSize = (activeWindowScale / 2.0) * visibleHeight;
            var activeTop = visibleCenter - halfVisibleWindowsSize - GetMaxRowHeight();
            var activeBottom = visibleCenter + halfVisibleWindowsSize + GetMaxRowHeight();

            var oldFirst = FirstActive;
            var oldLast = LastActive;

            var rows = GetLayoutRows(parentWidth);
            if (rows.Count > 0)
            {
                FirstActive = rows.First().StartIndex;
                LastActive = rows.First().EndIndex;

                foreach (var row in rows)
                {
                    if (row.Top + row.Height < activeTop)
                    {
                        FirstActive = row.EndIndex;
                    }

                    if (row.Top <= activeBottom)
                    {
                        LastActive = row.EndIndex;
                    }
                }
            }
            else
            {
                FirstActive = LastActive = -1;
            }

            return oldFirst != FirstActive || oldLast != LastActive;
        }


        protected override Size MeasureOverride(Size availableSize)
        {
            // Update the parent ScrollViewer
            CheckParentUpdate();

            var layoutWidth = availableSize.Width;
            if (double.IsInfinity(layoutWidth) && ParentScrollViewer != null)
            {
                layoutWidth = ParentScrollViewer.ViewportWidth;
            }
            if (double.IsInfinity(layoutWidth))
            {
                layoutWidth = 0;
            }

            var rows = GetLayoutRows(layoutWidth);
            foreach (var row in rows)
            {
                MeasureRow(row);
            }

            var totalHeight = rows.Count == 0 ? 0 : rows.Last().Top + rows.Last().Height;
            return new Size(availableSize.Width, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var row in GetLayoutRows(finalSize.Width).Where(o => o.EndIndex >= FirstActive && o.StartIndex <= LastActive))
            {
                ArrangeRow(row);
            }
            CheckNeedMoreItemAsync();
            return finalSize;
        }

        private void MeasureRow(RowLayoutInfo row)
        {
            if (!(ItemsSource is IList items))
            {
                return;
            }

            for (int i = row.StartIndex; i <= row.EndIndex; i++)
            {
                if (ContainerFromIndex(i) is ContentControl container && items[i] is IJustifiedWrapPanelItem item)
                {
                    container.Measure(new Size(GetPreferredRatio(item) * row.Height, row.Height));
                }
            }
        }


        private void ArrangeRow(RowLayoutInfo row)
        {
            double currentX = 0;
            if (!(ItemsSource is IList items))
            {
                return;
            }

            for (int i = row.StartIndex; i <= row.EndIndex; i++)
            {
                if (ContainerFromIndex(i) is ContentControl container && items[i] is IJustifiedWrapPanelItem item)
                {
                    var itemWidth = GetPreferredRatio(item) * row.Height;
                    container.Arrange(new Rect(currentX, row.Top, itemWidth, row.Height));
                    currentX += itemWidth;
                }
            }
        }

        public void ScrollIntoView(object item, ScrollIntoViewAlignment alignment)
        {
            if (ItemsSource is IList items)
            {
                int index = items.IndexOf(item);
                if (index != -1)
                {
                    // Get the top position of the given index

                    // Scroll the scrollviewer
                    switch (alignment)
                    {
                        default:
                        case ScrollIntoViewAlignment.Default:
                            var row = GetRowForIndex(index);
                            double top = row == null ? 0 : row.Top;
                            double bottom = row == null ? RowHeight : row.Top + row.Height;

                            if (ParentScrollViewer.VerticalOffset + ParentScrollViewer.ViewportHeight + ParentScrollViewer.Margin.Top < bottom)
                            {
                                // The target is below the viewport, align the item to the button of viewport
                                ParentScrollViewer.ChangeView(null, bottom - ParentScrollViewer.ViewportHeight - ParentScrollViewer.Margin.Top, null, true);
                            }
                            else if (top < ParentScrollViewer.VerticalOffset)
                            {
                                // The target is above the viewport, align the item to the top of viewport
                                ParentScrollViewer.ChangeView(null, top, null, true);
                            }
                            break;
                        case ScrollIntoViewAlignment.Leading:
                            ParentScrollViewer.ChangeView(null, GetPositionY(index), null, true);
                            break;
                    }
                }
            }
        }
        
        private double GetPositionY(int index)
        {
            var row = GetRowForIndex(index);
            return row == null ? 0 : row.Top;
        }

        private RowLayoutInfo GetRowForIndex(int index)
        {
            if (!(ItemsSource is IList items) || index < 0 || index >= items.Count)
            {
                return null;
            }

            var layoutWidth = this.DesiredSize.Width - this.Margin.Left - this.Margin.Right;
            if (layoutWidth <= 0 && ParentScrollViewer != null)
            {
                layoutWidth = ParentScrollViewer.ViewportWidth - this.Margin.Left - this.Margin.Right;
            }

            return GetLayoutRows(layoutWidth).FirstOrDefault(o => o.StartIndex <= index && o.EndIndex >= index);
        }
        
        public DependencyObject ContainerFromIndex (int index)
        {
            if (ItemsSource is IList items)
            {
                return ContainerFromItem(items[index]);
            }
            else
            {
                return null;
            }
        }


        public JustifiedWrapPanel()
        {
            this.TabFocusNavigation = Windows.UI.Xaml.Input.KeyboardNavigationMode.Once;


            this.KeyDown += JustifiedWrapPanel_KeyDown;
        }

    }
}

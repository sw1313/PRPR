using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;

namespace PRPR.Common.Controls
{
    public partial class JustifiedWrapPanel
    {
        const double LoadMoreTriggerViewportMultiplier = 3.0;
        const uint MinimumPrefetchItemCount = 20;

        bool loading = false;


		public async Task CheckNeedMoreItemAsync()
        {
            if (ParentScrollViewer != null && ParentScrollViewer.VerticalOffset > ParentScrollViewer.ScrollableHeight - LoadMoreTriggerViewportMultiplier * ParentScrollViewer.ViewportHeight &&
                !loading && ItemsSource is ISupportIncrementalLoading && ((ISupportIncrementalLoading)ItemsSource).HasMoreItems)
            {
                await LoadMoreItemsAsync();
            }
        }

        async Task LoadMoreItemsAsync()
        {
            // Preload roughly one active window so row height recalculation
            // happens before the user actually reaches the bottom.
            await LoadMoreItemsAsync(Math.Max((uint)Containers.Count, MinimumPrefetchItemCount));
		}

        async Task LoadMoreItemsAsync(uint count)
        {
            var itemsSourceAtStart = ItemsSource;
            if (ItemsSource is ISupportIncrementalLoading items)
            {
				// Lock the loading so it wont be call repeatedly while waiting new items incoming
                if (!loading)
                {
                    loading = true;

					// Load as many as possible to meet the requested number
                    uint loaded = 0;
					while (items.HasMoreItems && loaded < count && itemsSourceAtStart == ItemsSource)
					{
                        try
                        {
                            var loadResult = await items.LoadMoreItemsAsync(count);
                            if (loadResult.Count == 0)
                            {
                                break;
                            }

                            loaded += loadResult.Count;

                            // When new items are add, must recheck the realization
                            if (UpdateActiveRange(ParentScrollViewer.VerticalOffset, ParentScrollViewer.ViewportHeight, this.DesiredSize.Width - this.Margin.Left - this.Margin.Right, true))
                            {
                                RevirtualizeAll();
                            }
                            Debug.WriteLine($"LoadMoreItemsAsync");
                            //InvalidateMeasure();
                            //InvalidateArrange();
                        }
                        catch (Exception ex)
                        {
							// Stop it if there is any exception
                            break;
                        }
                    }

					
                    loading = false;
                }
            }
        }
    }
}

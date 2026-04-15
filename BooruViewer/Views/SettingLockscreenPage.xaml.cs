using PRPR.BooruViewer.Models;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Services;
using PRPR.BooruViewer.Tasks;
using PRPR.BooruViewer.ViewModels;
using PRPR.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Background;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace PRPR.BooruViewer.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingLockscreenPage : Page
    {
        public SettingLockscreenPage()
        {
            this.InitializeComponent();
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += this.NavigationHelper_LoadState;
            this.navigationHelper.SaveState += this.NavigationHelper_SaveState;
        }

        #region NavigationHelper

        private NavigationHelper navigationHelper;
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            this.navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            this.navigationHelper.OnNavigatedFrom(e);
        }

        #endregion

        private async void NavigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
            await SettingLockscreenViewModel.UpdateRecordsAsync();
        }

        private void NavigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
        }



        public SettingLockscreenViewModel SettingLockscreenViewModel
        {
            get
            {
                return this.DataContext as SettingLockscreenViewModel;
            }
        }


        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LockscreenUpdateTask.RunAsync(true);
                await SettingLockscreenViewModel.UpdateRecordsAsync();
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message, "Lockscreen refresh failed").ShowAsync();
            }
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            // Check the best time span to the users
            var p = await Posts.DownloadPostsAsync(1, $"{YandeClient.HOST}/post.xml?tags={ WebUtility.UrlEncode(YandeSettings.Current.LockscreenUpdateTaskSearchKey) }");

            while (p.Count < 50 && p.HasMoreItems)
            {
                await p.LoadMoreItemsAsync(1);
            }

            List<int> timeSpans = new List<int>();

            if (p.Count < 2)
            {
                await new MessageDialog($"There are not much results for this keyword. Please try some other keys.", $"Result for \"{YandeSettings.Current.LockscreenUpdateTaskSearchKey}\"").ShowAsync();

            }
            else
            {
                for (int i = 0; i < Math.Min(p.Count - 1, 49); i++)
                {
                    var timeSpan = p[i].CreatedAtUtc.Subtract(p[i + 1].CreatedAtUtc);
                    timeSpans.Add((int)Math.Round(timeSpan.TotalMinutes));
                }
                timeSpans = timeSpans.OrderByDescending(o => o).ToList();
                var settingTime = YandeSettings.Current.LockscreenUpdateTaskTimeSpan;
                await new MessageDialog(
$"You have 90% chance to get a new image for {Search(0.90, timeSpans)} minutes.\nYou have 75% chance to get a new image for {Search(0.75, timeSpans)} minutes.\nYou have 50% chance to get a new image for {Search(0.50, timeSpans)} minutes.\nYou have 25% chance to get a new image for {Search(0.25, timeSpans)} minutes.", $"Result for \"{YandeSettings.Current.LockscreenUpdateTaskSearchKey}\"").ShowAsync();
            }

        }


        uint Search(double preferChance, List<int> timeSpans)
        {

            uint lastAttempt = 0;
            uint currentAttempt = 240;

            while (CheckChance(currentAttempt, timeSpans) < preferChance)
            {
                currentAttempt *= 2;
            }

            while (currentAttempt != lastAttempt)
            {
                var gap = Math.Max(lastAttempt, currentAttempt) - Math.Min(lastAttempt, currentAttempt);

                var result = CheckChance(currentAttempt, timeSpans);
                if (result == preferChance)
                {
                    return currentAttempt;
                }
                else if (result > preferChance) // Too long time span
                {
                    lastAttempt = currentAttempt;
                    currentAttempt -= gap / 2;
                }
                else // Too short time span
                {
                    lastAttempt = currentAttempt;
                    currentAttempt += gap / 2;
                }


            }

            return currentAttempt;
        }



        double CheckChance(uint timeSpan, List<int> timeSpans)
        {
            return 1.0 * (timeSpans.Sum(o => Math.Min(timeSpan, o)) + timeSpan) / (timeSpans.Sum() + timeSpan);

        }



        private void FilterReturnItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Flyout.SetAttachedFlyout(FilterButton, this.Resources["FilterMainFlyout"] as Flyout);
            Flyout.ShowAttachedFlyout(FilterButton);
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            Flyout.SetAttachedFlyout(FilterButton, this.Resources["FilterMainFlyout"] as Flyout);
            Flyout.ShowAttachedFlyout(FilterButton);
        }

        private void ListViewItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Flyout.SetAttachedFlyout(FilterButton, this.Resources["FilterRatioFlyout"] as Flyout);
            Flyout.ShowAttachedFlyout(FilterButton);
        }

        private void MenuFlyoutSubItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Flyout.SetAttachedFlyout(FilterButton, this.Resources["FilterRatingFlyout"] as Flyout);
            Flyout.ShowAttachedFlyout(FilterButton);
        }

        private void FilterHiddenPostsListViewItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Flyout.SetAttachedFlyout(FilterButton, this.Resources["FilterHiddenFlyout"] as Flyout);
            Flyout.ShowAttachedFlyout(FilterButton);
        }

        private void FilterBlacklistListViewItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Flyout.SetAttachedFlyout(FilterButton, this.Resources["FilterBlacklistFlyout"] as Flyout);
            Flyout.ShowAttachedFlyout(FilterButton);
        }


        private async void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var record = e.ClickedItem as PersonalizationRecord;
            try
            {
                var posts = await Posts.DownloadPostsAsync(1, $"{YandeClient.HOST}/post.xml?tags={ "id%3A" + record.PostId }");
                ImagePage.PostDataStack.Push(posts);
                this.Frame.Navigate(typeof(ImagePage), posts.First().ToXml());
            }
            catch (Exception ex)
            {
                await new MessageDialog(ex.Message, "Error").ShowAsync();
            }
        }

        private void SearchTagBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            UpdateTagSuggestions(sender);
        }

        private void SearchTagBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is TagDetailInMiddle chosen)
                sender.Text = chosen.ToSearchString().Trim();
        }

        private void BlacklistTagBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            UpdateTagSuggestions(sender);
        }

        private void BlacklistTagBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is TagDetailInMiddle chosen)
                sender.Text = chosen.ToSearchString().Trim();
        }

        private void UpdateTagSuggestions(AutoSuggestBox sender)
        {
            try
            {
                var text = sender.Text ?? "";
                var grid = VisualTreeHelper.GetChild(sender, 0) as Grid;
                var textbox = grid?.Children.FirstOrDefault() as TextBox;
                int pointer = textbox?.SelectionStart ?? text.Length;

                var tags = text.Split(' ');
                int selectedKeyIndex = text.Take(pointer).Count(c => c == ' ');
                if (selectedKeyIndex >= tags.Length) selectedKeyIndex = tags.Length - 1;

                if (tags.Length < 1 || string.IsNullOrEmpty(tags[selectedKeyIndex]))
                {
                    sender.ItemsSource = null;
                    return;
                }

                var keyword = tags[selectedKeyIndex];
                var results = TagDataBase.Search(keyword).Take(20).ToList();
                var transRepo = TagTranslationRepository.Instance;
                foreach (var tag in results)
                {
                    var trans = transRepo.Lookup(tag.Name);
                    if (trans != null && !string.IsNullOrEmpty(trans.ZhName))
                        tag.ZhName = trans.ZhName;
                }

                var transResults = transRepo.Search(keyword, 20);
                var existingNames = new HashSet<string>(results.Select(r => r.Name));
                foreach (var te in transResults)
                {
                    if (existingNames.Contains(te.Name)) continue;
                    TagDetail td;
                    if (!TagDataBase.AllTags.TryGetValue(te.Name, out td))
                        td = new TagDetail { Name = te.Name, Type = (TagType)te.Type };
                    td.ZhName = te.ZhName;
                    results.Add(td);
                    if (results.Count >= 20) break;
                }

                var prefix = string.Join(" ", tags.Take(selectedKeyIndex));
                if (!string.IsNullOrWhiteSpace(prefix)) prefix += " ";
                var suffix = string.Join(" ", tags.Skip(selectedKeyIndex + 1));
                if (!string.IsNullOrWhiteSpace(suffix)) suffix = " " + suffix;

                sender.ItemsSource = results.Select(o => new TagDetailInMiddle(o, prefix, suffix + " "));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTagSuggestions Exception: {ex.Message}");
            }
        }
    }
}

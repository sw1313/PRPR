using Microsoft.QueryStringDotNET;
using PRPR.BooruViewer.Models;
using PRPR.BooruViewer.Models.Global;
using PRPR.BooruViewer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.UI.Notifications;

namespace PRPR.BooruViewer.Tasks
{
    public class FavoriteTask
    {
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            var d = taskInstance.GetDeferral();

            // Get the id
            ToastNotificationActionTriggerDetail details = (ToastNotificationActionTriggerDetail)taskInstance.TriggerDetails;
            var id = QueryString.Parse(details.Argument)["id"];
            await RunAsync(int.Parse(id));

            d.Complete();
        }

        private async Task RunAsync(int id)
        {
            var y = YandeSettings.Current;
            if (y.IsLoggedIn)
            {
                await YandeClient.VoteAsync(id, y.UserName, y.PasswordHash, VoteType.Favorite);
                return;
            }

            var posts = await Posts.DownloadPostsAsync(1, $"{YandeClient.HOST}/post.xml?tags=id%3A{id}");
            var post = posts.FirstOrDefault();
            if (post != null)
            {
                await LocalFavoriteService.AddOrUpdateAsync(post);
            }
        }
    }
}

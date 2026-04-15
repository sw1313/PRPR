using HtmlAgilityPack;
using PRPR.BooruViewer.Models.Global;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;

namespace PRPR.BooruViewer.Services
{
    public class YandeClient
    {
        public const string DEFAULT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) PRPR/1.0 Safari/537.36";

        public static string PASSWORD_HASH_SALT
        {
            get
            {
                try
                {
                    return YandeSettings.Current.PasswordHashSalt;
                }
                catch (Exception ex)
                {
                    return "choujin-steiner--your-password--";
                }
            }
        }

        public static string HOST
        {
            get
            {
                try
                {
                    return YandeSettings.Current.Host;
                }
                catch (Exception ex)
                {
                    return "https://yande.re";
                }
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> BuiltInHosts =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "yande.re", "198.251.89.183" },
                { "files.yande.re", "198.251.89.183" },
                { "assets.yande.re", "198.251.89.183" },
            };

        public static Uri RewriteUri(Uri original)
        {
            try
            {
                if (!YandeSettings.Current.UseBuiltInHosts) return original;
            }
            catch { return original; }

            if (BuiltInHosts.TryGetValue(original.Host, out var ip))
            {
                var builder = new UriBuilder(original) { Host = ip };
                return builder.Uri;
            }
            return original;
        }

        public static string RewriteUrl(string url)
        {
            try
            {
                if (!YandeSettings.Current.UseBuiltInHosts) return url;
                var uri = new Uri(url);
                if (BuiltInHosts.TryGetValue(uri.Host, out var ip))
                {
                    var builder = new UriBuilder(uri) { Host = ip };
                    return builder.Uri.ToString();
                }
            }
            catch { }
            return url;
        }

        public static void ApplyHostHeader(HttpRequestMessage message, Uri originalUri)
        {
            try
            {
                if (YandeSettings.Current.UseBuiltInHosts && BuiltInHosts.ContainsKey(originalUri.Host))
                {
                    message.Headers.Host = new Windows.Networking.HostName(originalUri.Host);
                }
            }
            catch { }
        }

        public static async Task<Windows.Storage.Streams.IBuffer> GetBufferAsync(HttpClient httpClient, Uri originalUri)
        {
            var rewritten = RewriteUri(originalUri);
            if (rewritten == originalUri)
            {
                return await httpClient.GetBufferAsync(originalUri);
            }
            var msg = new HttpRequestMessage(HttpMethod.Get, rewritten);
            ApplyDefaultHeaders(msg);
            ApplyHostHeader(msg, originalUri);
            var resp = await httpClient.SendRequestAsync(msg);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsBufferAsync();
        }

        public static async Task<string> GetStringAsync(HttpClient httpClient, Uri originalUri)
        {
            var rewritten = RewriteUri(originalUri);
            if (rewritten == originalUri)
            {
                return await httpClient.GetStringAsync(originalUri);
            }
            var msg = new HttpRequestMessage(HttpMethod.Get, rewritten);
            ApplyDefaultHeaders(msg);
            ApplyHostHeader(msg, originalUri);
            var resp = await httpClient.SendRequestAsync(msg);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }

        public static HttpClient CreateHttpClient()
        {
            try
            {
                if (YandeSettings.Current.UseBuiltInHosts)
                {
                    var filter = new HttpBaseProtocolFilter();
                    filter.IgnorableServerCertificateErrors.Add(
                        Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
                    var client = new HttpClient(filter);
                    ApplyDefaultHeaders(client);
                    return client;
                }
            }
            catch { }
            var defaultClient = new HttpClient();
            ApplyDefaultHeaders(defaultClient);
            return defaultClient;
        }

        public static void ApplyDefaultHeaders(HttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", DEFAULT_USER_AGENT);
        }

        public static void ApplyDefaultHeaders(HttpRequestMessage message)
        {
            message.Headers.Add("User-Agent", DEFAULT_USER_AGENT);
        }

        private static string HashPassword(string password)
        {
            var passwordBuffer = CryptographicBuffer.ConvertStringToBinary(PASSWORD_HASH_SALT.Replace("your-password", password), BinaryStringEncoding.Utf8);

            var hashAlgorithmProvider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            var bufferHash = hashAlgorithmProvider.HashData(passwordBuffer);
            return CryptographicBuffer.EncodeToHexString(bufferHash);
        }

        private static async Task<string> GetAuthenticityToken()
        {
            try
            {
                // Log out
                using (var logoutClient = CreateHttpClient())
                {
                    var logoutOriginalUri = new Uri($"{YandeClient.HOST}/user/logout");
                    using (var logoutResponse = await logoutClient.GetAsync(RewriteUri(logoutOriginalUri)))
                    {
                        await logoutResponse.Content.ReadAsStringAsync();
                    }
                }
                

                HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
                filter.CacheControl.WriteBehavior = HttpCacheWriteBehavior.NoCache;
                filter.CacheControl.ReadBehavior = HttpCacheReadBehavior.MostRecent;
                filter.AllowUI = false;
                try
                {
                    if (YandeSettings.Current.UseBuiltInHosts)
                        filter.IgnorableServerCertificateErrors.Add(
                            Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
                }
                catch { }
                var hc = new HttpClient(filter);
                ApplyDefaultHeaders(hc);
                var str = await YandeClient.GetStringAsync(hc, new Uri($"{YandeClient.HOST}/user/login"));

                
                HtmlDocument htmlDocument = new HtmlDocument();
                htmlDocument.OptionFixNestedTags = true;
                htmlDocument.LoadHtml(str);
                var metaNode = htmlDocument.DocumentNode.SelectSingleNode("//meta[@name='csrf-token']");

                var token = metaNode.GetAttributeValue("content", "");
                
                return WebUtility.UrlEncode(token);
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        private static async Task<VoteType> GetVoteAsync(int postId, string userName, string passwordHash)
        {
            try
            {
                var originalUri = new Uri($"{YandeClient.HOST}/post/vote.xml?login={userName}&password_hash={passwordHash}&id={postId}");
                using (var httpClient = CreateHttpClient())
                {
                    var rewrittenUri = RewriteUri(originalUri);
                    var msg = new HttpRequestMessage(HttpMethod.Post, rewrittenUri) { Content = new HttpStringContent("") };
                    ApplyHostHeader(msg, originalUri);
                    var response = await httpClient.SendRequestAsync(msg);

                    var str = await response.Content.ReadAsStringAsync();
                    return str.Contains("3") ? VoteType.Favorite : VoteType.None;
                }
            }
            catch (Exception ex)
            {
                return VoteType.None;
            }
        }

        public static async Task VoteAsync(int postId, string userName, string passwordHash, VoteType score)
        {
            var voteOriginalUri = new Uri($"{YandeClient.HOST}/post/vote.xml?login={userName}&password_hash={passwordHash}&id={postId}&score={(int)score}");
            using (var httpClient = CreateHttpClient())
            {
                var message = new HttpRequestMessage(HttpMethod.Post, RewriteUri(voteOriginalUri))
                {
                    Content = new HttpStringContent("")
                };
                ApplyDefaultHeaders(message);
                ApplyHostHeader(message, voteOriginalUri);
                var response = await httpClient.SendRequestAsync(message);
                var responseString = await response.Content.ReadAsStringAsync();
            }
        }
        
        private static Rect Normalize(Size imageSize, Rect cropRect)
        {
            return new Rect(
                cropRect.Left / imageSize.Width,
                cropRect.Top / imageSize.Height,
                cropRect.Width / imageSize.Width,
                cropRect.Height / imageSize.Height);
        }

        public static async Task SetAvatarAsync(string postId, Size imageSize, Rect cropRect)
        {
            string token = "";

            var rect = Normalize(imageSize, cropRect);
            var s = $"authenticity_token={token}post_id={postId}&left={rect.Left}&right={rect.Right}&top={rect.Top}&bottom={rect.Bottom}&commit=Set+avatar";

            var uri = $"{YandeClient.HOST}/user/set_avatar/{postId}";

            // TODO: implement setting avatar
            throw new NotImplementedException();
        }


        public static async Task<bool> SignInAsync(string userName, string password)
        {
            try
            {
                string id = null;
                var token = await GetAuthenticityToken();

                string requestBody = $"authenticity_token={token}&url=&user%5Bname%5D={userName}&user%5Bpassword%5D={password}&commit=Login";
                
                
                var httpClient = CreateHttpClient();
                var authOriginalUri = new Uri($"{YandeClient.HOST}/user/authenticate");
                var message = new HttpRequestMessage(new HttpMethod("POST"), RewriteUri(authOriginalUri))
                {
                    Content = new HttpStringContent(requestBody)
                };
                ApplyDefaultHeaders(message);
                ApplyHostHeader(message, authOriginalUri);
                message.Content.Headers.ContentType = new HttpMediaTypeHeaderValue("application/x-www-form-urlencoded");
                var response = await httpClient.SendRequestAsync(message);
                var responseString = await response.Content.ReadAsStringAsync();

                
                var start = responseString.IndexOf("/user/show/") + "/user/show/".Length;
                if (start < 11)
                {
                    YandeSettings.Current.UserID = id;
                    YandeSettings.Current.UserName = userName;
                    YandeSettings.Current.PasswordHash = HashPassword(password);
                    return false;
                }
                var end = responseString.IndexOf("\">My Profile<", start);
                id = responseString.Substring(start, end - start);


                
                YandeSettings.Current.UserID = id;
                YandeSettings.Current.UserName = userName;
                YandeSettings.Current.PasswordHash = HashPassword(password);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        
        public static void SignOut()
        {
            YandeSettings.Current.UserID = null;
            YandeSettings.Current.UserName = null;
            YandeSettings.Current.PasswordHash = null;
        }
        
        public static async Task SubmitCommentAsync()
        {
            // TODO: submit the comment to the server
            throw new NotImplementedException();
        }
        
        public static async Task AddFavoriteAsync(int postId)
        {
            await VoteAsync(postId, YandeSettings.Current.UserName, YandeSettings.Current.PasswordHash, VoteType.Favorite);
        }

        public static async Task RemoveFavoriteAsync(int postId)
        {
            await VoteAsync(postId, YandeSettings.Current.UserName, YandeSettings.Current.PasswordHash, VoteType.None);
        }

        public static async Task<bool> CheckFavorited(int postId)
        {
            return (await GetVoteAsync(postId, YandeSettings.Current.UserName, YandeSettings.Current.PasswordHash)) == VoteType.Favorite;
        }        
    }


    public enum VoteType
    {
        
        Bad = -1,
        None = 0,
        Good = 1,
        Great = 2,
        Favorite = 3

    }
}

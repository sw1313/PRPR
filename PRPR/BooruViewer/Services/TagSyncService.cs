using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

namespace PRPR.BooruViewer.Services
{
    public class TagSyncService
    {
        private const string REPO = "sw1313/danbooru-tags-translation";
        private const string FILE_PATH = "tags.json";
        private const string API_CONTENTS_URL = "https://api.github.com/repos/" + REPO + "/contents/" + FILE_PATH;
        private static readonly string[] DOWNLOAD_URLS = new[]
        {
            "https://raw.githubusercontent.com/" + REPO + "/main/" + FILE_PATH,
            "https://cdn.jsdelivr.net/gh/" + REPO + "@main/" + FILE_PATH,
            "https://mirror.ghproxy.com/https://raw.githubusercontent.com/" + REPO + "/main/" + FILE_PATH,
            "https://cdn.staticaly.com/gh/" + REPO + "/main/" + FILE_PATH,
        };

        private readonly TagTranslationRepository _repo;

        public TagSyncService(TagTranslationRepository repo)
        {
            _repo = repo;
        }

        public async Task<int> DownloadAsync()
        {
            var remote = await FetchRemoteEntriesAsync();
            await _repo.ReplaceAllAsync(remote);
            return remote.Count;
        }

        public async Task<string> UploadThenDownloadAsync(string pat)
        {
            var remote = await FetchRemoteEntriesAsync();
            var local = await _repo.ExportAllAsync();

            var merged = new Dictionary<string, TagTransEntry>();
            foreach (var e in remote) merged[e.Name] = e;
            foreach (var e in local) merged[e.Name] = e;
            var mergedList = merged.Values.ToList();

            await UploadEntriesAsync(mergedList, pat);
            await _repo.ReplaceAllAsync(mergedList);
            await _repo.ClearUserOverridesAsync();

            return $"同步完成，共 {mergedList.Count} 条";
        }

        public async Task<string> UploadAsync(string pat)
        {
            var remote = await FetchRemoteEntriesAsync();
            var local = await _repo.ExportAllAsync();

            var merged = new Dictionary<string, TagTransEntry>();
            foreach (var e in remote) merged[e.Name] = e;
            foreach (var e in local) merged[e.Name] = e;
            var mergedList = merged.Values.ToList();

            await UploadEntriesAsync(mergedList, pat);
            await _repo.ReplaceAllAsync(mergedList);
            await _repo.ClearUserOverridesAsync();

            return $"上传成功，共 {mergedList.Count} 条";
        }

        private async Task<List<TagTransEntry>> FetchRemoteEntriesAsync()
        {
            var errors = new List<string>();
            foreach (var url in DOWNLOAD_URLS)
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                        request.Headers.Add("Cache-Control", "no-cache");
                        request.Headers.Add("User-Agent", "PRPR-UWP");

                        var response = await client.SendRequestAsync(request);
                        if (!response.IsSuccessStatusCode)
                        {
                            errors.Add($"{url} -> HTTP {(int)response.StatusCode}");
                            continue;
                        }
                        var body = await response.Content.ReadAsStringAsync();
                        return ParseEntries(body);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{url} -> {ex.Message}");
                }
            }
            throw new Exception("所有下载源均失败:\n" + string.Join("\n", errors));
        }

        private List<TagTransEntry> ParseEntries(string json)
        {
            var arr = JsonArray.Parse(json);
            var entries = new List<TagTransEntry>(arr.Count);
            foreach (var item in arr)
            {
                var obj = item.GetObject();
                entries.Add(new TagTransEntry
                {
                    Name = obj.GetNamedString("name"),
                    Type = (int)obj.GetNamedNumber("type", 0),
                    ZhName = obj.ContainsKey("zh") ? obj.GetNamedString("zh", "") : "",
                });
            }
            return entries;
        }

        private async Task UploadEntriesAsync(List<TagTransEntry> entries, string pat)
        {
            var arr = new JsonArray();
            foreach (var e in entries)
            {
                var obj = new JsonObject();
                obj["name"] = JsonValue.CreateStringValue(e.Name);
                obj["type"] = JsonValue.CreateNumberValue(e.Type);
                if (!string.IsNullOrWhiteSpace(e.ZhName))
                    obj["zh"] = JsonValue.CreateStringValue(e.ZhName);
                arr.Add(obj);
            }

            var contentBytes = Encoding.UTF8.GetBytes(arr.Stringify());
            var contentBase64 = Convert.ToBase64String(contentBytes);
            var sha = await FetchCurrentShaAsync(pat);

            var payload = new JsonObject();
            payload["message"] = JsonValue.CreateStringValue("Update tags.json");
            payload["content"] = JsonValue.CreateStringValue(contentBase64);
            if (!string.IsNullOrEmpty(sha))
                payload["sha"] = JsonValue.CreateStringValue(sha);

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Put, new Uri(API_CONTENTS_URL));
                request.Headers.Authorization = new HttpCredentialsHeaderValue("Bearer", pat);
                request.Headers.Add("Accept", "application/vnd.github+json");
                request.Headers.Add("User-Agent", "PRPR-UWP");
                request.Content = new HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                var response = await client.SendRequestAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    throw new Exception($"上传失败: HTTP {(int)response.StatusCode}\n{errBody}");
                }
            }
        }

        private async Task<string> FetchCurrentShaAsync(string pat)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, new Uri(API_CONTENTS_URL));
                    request.Headers.Authorization = new HttpCredentialsHeaderValue("Bearer", pat);
                    request.Headers.Add("Accept", "application/vnd.github+json");
                    request.Headers.Add("User-Agent", "PRPR-UWP");

                    var response = await client.SendRequestAsync(request);
                    if (!response.IsSuccessStatusCode) return null;
                    var body = await response.Content.ReadAsStringAsync();
                    var obj = JsonObject.Parse(body);
                    var sha = obj.GetNamedString("sha", "");
                    return string.IsNullOrWhiteSpace(sha) ? null : sha;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

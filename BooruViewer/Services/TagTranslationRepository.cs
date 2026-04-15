using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace PRPR.BooruViewer.Services
{
    public class TagTransEntry
    {
        public string Name { get; set; }
        public int Type { get; set; }
        public string ZhName { get; set; }
    }

    public class TagTranslationRepository
    {
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private static Dictionary<string, TagTransEntry> _entries;
        private static Dictionary<string, TagTransEntry> _userOverrides;

        private const string SYNCED_FILE = "tag_translations_synced.tsv";
        private const string USER_OVERRIDES_FILE = "tag_user_overrides.tsv";

        public static TagTranslationRepository Instance { get; } = new TagTranslationRepository();

        private TagTranslationRepository() { }

        public async Task EnsureLoadedAsync()
        {
            if (_entries != null) return;
            await _lock.WaitAsync();
            try
            {
                if (_entries != null) return;
                _entries = await LoadSyncedFromDiskAsync() ?? new Dictionary<string, TagTransEntry>();
                _userOverrides = await LoadUserOverridesFromDiskAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public TagTransEntry Lookup(string tagName)
        {
            if (_userOverrides != null && _userOverrides.TryGetValue(tagName, out var ov)) return ov;
            if (_entries != null && _entries.TryGetValue(tagName, out var entry)) return entry;
            return null;
        }

        public List<TagTransEntry> Search(string keyword, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<TagTransEntry>();
            if (_entries == null) return new List<TagTransEntry>();

            bool isChinese = keyword.Any(c => c > 0x2E7F);
            return isChinese ? SearchChinese(keyword, limit) : SearchEnglish(keyword.ToLowerInvariant(), limit);
        }

        private List<TagTransEntry> SearchEnglish(string lower, int limit)
        {
            var result = new List<TagTransEntry>();
            var seen = new HashSet<string>();

            if (_userOverrides != null)
            {
                foreach (var e in _userOverrides.Values)
                {
                    if (result.Count >= limit) return result;
                    if (e.Name.IndexOf(lower, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(e);
                        seen.Add(e.Name);
                    }
                }
            }

            foreach (var e in _entries.Values)
            {
                if (result.Count >= limit) break;
                if (seen.Contains(e.Name)) continue;
                if (e.Name.StartsWith(lower, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(e);
                    seen.Add(e.Name);
                }
            }

            if (result.Count < limit)
            {
                foreach (var e in _entries.Values)
                {
                    if (result.Count >= limit) break;
                    if (seen.Contains(e.Name)) continue;
                    if (e.Name.IndexOf(lower, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(e);
                        seen.Add(e.Name);
                    }
                }
            }

            return result;
        }

        private List<TagTransEntry> SearchChinese(string keyword, int limit)
        {
            var result = new List<TagTransEntry>();
            var seen = new HashSet<string>();

            if (_userOverrides != null)
            {
                foreach (var e in _userOverrides.Values)
                {
                    if (result.Count >= limit) return result;
                    if (!string.IsNullOrEmpty(e.ZhName) && e.ZhName.Contains(keyword))
                    {
                        result.Add(e);
                        seen.Add(e.Name);
                    }
                }
            }

            foreach (var e in _entries.Values)
            {
                if (result.Count >= limit) break;
                if (seen.Contains(e.Name)) continue;
                if (!string.IsNullOrEmpty(e.ZhName) && e.ZhName.Contains(keyword))
                {
                    result.Add(e);
                    seen.Add(e.Name);
                }
            }

            return result;
        }

        public async Task SaveUserOverrideAsync(string tagName, string zhName)
        {
            var clean = tagName?.Trim();
            var cleanZh = zhName?.Trim().Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
            if (string.IsNullOrEmpty(clean)) return;

            await EnsureLoadedAsync();
            await _lock.WaitAsync();
            try
            {
                int type = 0;
                if (_userOverrides.TryGetValue(clean, out var existing)) type = existing.Type;
                else if (_entries.TryGetValue(clean, out var main)) type = main.Type;

                _userOverrides[clean] = new TagTransEntry { Name = clean, Type = type, ZhName = cleanZh };
                await SaveUserOverridesToDiskAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<TagTransEntry>> ExportAllAsync()
        {
            await EnsureLoadedAsync();
            var merged = new Dictionary<string, TagTransEntry>(_entries);
            if (_userOverrides != null)
            {
                foreach (var kv in _userOverrides)
                    merged[kv.Key] = kv.Value;
            }
            return merged.Values.ToList();
        }

        public async Task ReplaceAllAsync(List<TagTransEntry> newEntries)
        {
            await _lock.WaitAsync();
            try
            {
                _entries = new Dictionary<string, TagTransEntry>(newEntries.Count);
                foreach (var e in newEntries)
                    _entries[e.Name] = e;
                await SaveSyncedToDiskAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ClearUserOverridesAsync()
        {
            await _lock.WaitAsync();
            try
            {
                _userOverrides = new Dictionary<string, TagTransEntry>();
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.TryGetItemAsync(USER_OVERRIDES_FILE);
                if (file != null) await file.DeleteAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        private static async Task<Dictionary<string, TagTransEntry>> LoadSyncedFromDiskAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.TryGetItemAsync(SYNCED_FILE);
                if (item == null) return null;
                var file = (StorageFile)item;
                var lines = await FileIO.ReadLinesAsync(file);
                return ParseTsv(lines);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Dictionary<string, TagTransEntry>> LoadUserOverridesFromDiskAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.TryGetItemAsync(USER_OVERRIDES_FILE);
                if (item == null) return new Dictionary<string, TagTransEntry>();
                var file = (StorageFile)item;
                var lines = await FileIO.ReadLinesAsync(file);
                return ParseTsv(lines);
            }
            catch
            {
                return new Dictionary<string, TagTransEntry>();
            }
        }

        private static Dictionary<string, TagTransEntry> ParseTsv(IList<string> lines)
        {
            var dict = new Dictionary<string, TagTransEntry>(lines.Count);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length < 3) continue;
                int.TryParse(parts[1], out int type);
                dict[parts[0]] = new TagTransEntry { Name = parts[0], Type = type, ZhName = parts[2] };
            }
            return dict;
        }

        private async Task SaveSyncedToDiskAsync()
        {
            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync(SYNCED_FILE, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, BuildTsv(_entries));
        }

        private async Task SaveUserOverridesToDiskAsync()
        {
            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync(USER_OVERRIDES_FILE, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, BuildTsv(_userOverrides));
        }

        private static string BuildTsv(Dictionary<string, TagTransEntry> dict)
        {
            var sb = new System.Text.StringBuilder(dict.Count * 40);
            foreach (var e in dict.Values)
                sb.Append(e.Name).Append('\t').Append(e.Type).Append('\t').Append(e.ZhName).Append('\n');
            return sb.ToString();
        }
    }
}

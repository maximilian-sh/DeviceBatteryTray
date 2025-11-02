using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LGSTrayUI
{
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public bool IsNewer { get; set; }
    }

    public class UpdateChecker
    {
        private const string GitHubRepo = "maximilian-sh/DeviceBatteryTray";
        private const string GitHubApiBase = "https://api.github.com";
        private static readonly HttpClient HttpClient = new()
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", "DeviceBatteryTray-Updater" },
                { "Accept", "application/vnd.github.v3+json" }
            },
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// Gets the current application version
        /// </summary>
        public static Version GetCurrentVersion()
        {
            var versionString = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion?.Split('+')[0] ?? "0.0.0";
            
            if (Version.TryParse(versionString, out var version))
            {
                return version;
            }
            return new Version(0, 0, 0);
        }

        /// <summary>
        /// Checks for updates from GitHub Releases
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var apiUrl = $"{GitHubApiBase}/repos/{GitHubRepo}/releases/latest";
                var response = await HttpClient.GetStringAsync(apiUrl, cancellationToken);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    return null;
                }

                // Extract version from tag (e.g., "v4.0.3" -> "4.0.3")
                var versionString = release.TagName.TrimStart('v');
                if (!Version.TryParse(versionString, out var latestVersion))
                {
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                var zipAsset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                if (zipAsset == null)
                {
                    return null;
                }

                return new UpdateInfo
                {
                    Version = versionString,
                    DownloadUrl = zipAsset.BrowserDownloadUrl,
                    ReleaseNotes = release.Body ?? string.Empty,
                    PublishedAt = release.PublishedAt,
                    IsNewer = latestVersion > currentVersion
                };
            }
            catch (HttpRequestException)
            {
                // Network error - fail silently
                return null;
            }
            catch (TaskCanceledException)
            {
                // Timeout - fail silently
                return null;
            }
            catch (Exception)
            {
                // Other errors - fail silently
                return null;
            }
        }

        /// <summary>
        /// Downloads and extracts the update to a temporary directory
        /// </summary>
        public static async Task<string?> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                var zipPath = Path.Combine(tempDir, "update.zip");

                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var bytesDownloaded = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;
                var lastReportedPercent = -1;
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    bytesDownloaded += bytesRead;

                    if (progress != null)
                    {
                        if (totalBytes > 0)
                        {
                            var percentComplete = (int)(bytesDownloaded * 100 / totalBytes);
                            // Only report if percentage changed to avoid spamming
                            if (percentComplete != lastReportedPercent)
                            {
                                progress.Report(percentComplete);
                                lastReportedPercent = percentComplete;
                            }
                        }
                        else
                        {
                            // If total size is unknown, report based on chunks downloaded (rough estimate)
                            var estimatedPercent = (int)Math.Min(99, bytesDownloaded / 10000); // Rough estimate
                            if (estimatedPercent != lastReportedPercent && estimatedPercent % 5 == 0)
                            {
                                progress.Report(estimatedPercent);
                                lastReportedPercent = estimatedPercent;
                            }
                        }
                    }
                }
                
                // Report 100% when done
                if (progress != null)
                {
                    progress.Report(100);
                }

                // Extract zip
                ZipFile.ExtractToDirectory(zipPath, tempDir, true);
                File.Delete(zipPath);

                return tempDir;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonProperty("body")]
            public string? Body { get; set; }

            [JsonProperty("published_at")]
            public DateTime PublishedAt { get; set; }

            [JsonProperty("assets")]
            public GitHubAsset[]? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}


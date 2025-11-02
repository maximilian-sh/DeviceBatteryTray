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
            var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
            
            try
            {
                LogToFile(logFile, "CheckForUpdatesAsync: Starting");
                var apiUrl = $"{GitHubApiBase}/repos/{GitHubRepo}/releases/latest";
                LogToFile(logFile, $"CheckForUpdatesAsync: API URL: {apiUrl}");
                
                LogToFile(logFile, "CheckForUpdatesAsync: Fetching release info from GitHub");
                var response = await HttpClient.GetStringAsync(apiUrl, cancellationToken);
                LogToFile(logFile, $"CheckForUpdatesAsync: Received response, length: {response?.Length ?? 0}");
                
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);
                LogToFile(logFile, $"CheckForUpdatesAsync: Deserialized release, TagName: {release?.TagName ?? "null"}");

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    LogToFile(logFile, "CheckForUpdatesAsync: No release or tag name found");
                    return null;
                }

                // Extract version from tag (e.g., "v4.0.3" -> "4.0.3")
                var versionString = release.TagName.TrimStart('v');
                LogToFile(logFile, $"CheckForUpdatesAsync: Extracted version string: {versionString}");
                
                if (!Version.TryParse(versionString, out var latestVersion))
                {
                    LogToFile(logFile, $"CheckForUpdatesAsync: Failed to parse version: {versionString}");
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                LogToFile(logFile, $"CheckForUpdatesAsync: Current version: {currentVersion}, Latest version: {latestVersion}");
                
                var zipAsset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                LogToFile(logFile, $"CheckForUpdatesAsync: Zip asset: {(zipAsset != null ? zipAsset.Name : "not found")}");

                if (zipAsset == null)
                {
                    LogToFile(logFile, "CheckForUpdatesAsync: No ZIP asset found in release");
                    return null;
                }

                var updateInfo = new UpdateInfo
                {
                    Version = versionString,
                    DownloadUrl = zipAsset.BrowserDownloadUrl,
                    ReleaseNotes = release.Body ?? string.Empty,
                    PublishedAt = release.PublishedAt,
                    IsNewer = latestVersion > currentVersion
                };
                
                LogToFile(logFile, $"CheckForUpdatesAsync: UpdateInfo created - Version: {updateInfo.Version}, IsNewer: {updateInfo.IsNewer}, URL: {updateInfo.DownloadUrl}");
                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                LogToFile(logFile, $"CheckForUpdatesAsync: HttpRequestException - {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                LogToFile(logFile, $"CheckForUpdatesAsync: TaskCanceledException (timeout) - {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogToFile(logFile, $"CheckForUpdatesAsync: Exception - {ex.GetType().Name}: {ex.Message}\n{ex}");
                return null;
            }
        }
        
        private static void LogToFile(string logFile, string message)
        {
            try
            {
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        /// <summary>
        /// Downloads and extracts the update to a temporary directory
        /// </summary>
        public static async Task<string?> DownloadUpdateAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var logFile = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
            
            try
            {
                LogToFile(logFile, "DownloadUpdateAsync: Starting");
                var tempDir = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update", Guid.NewGuid().ToString());
                LogToFile(logFile, $"DownloadUpdateAsync: Creating temp directory: {tempDir}");
                Directory.CreateDirectory(tempDir);
                LogToFile(logFile, "DownloadUpdateAsync: Temp directory created");

                var zipPath = Path.Combine(tempDir, "update.zip");
                LogToFile(logFile, $"DownloadUpdateAsync: ZIP path: {zipPath}");
                LogToFile(logFile, $"DownloadUpdateAsync: Download URL: {downloadUrl}");

                LogToFile(logFile, "DownloadUpdateAsync: Starting HTTP request");
                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                LogToFile(logFile, $"DownloadUpdateAsync: HTTP response status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();
                LogToFile(logFile, "DownloadUpdateAsync: Response status OK");

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                LogToFile(logFile, $"DownloadUpdateAsync: Total bytes to download: {(totalBytes > 0 ? totalBytes.ToString() : "unknown")}");
                var bytesDownloaded = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
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
                    
                    // Explicitly flush and close the file stream
                    LogToFile(logFile, $"DownloadUpdateAsync: Flushing file stream");
                    await fileStream.FlushAsync(cancellationToken);
                    LogToFile(logFile, $"DownloadUpdateAsync: File stream flushed and will be closed");
                } // FileStream is now fully closed here

                // Report 100% when done
                if (progress != null)
                {
                    progress.Report(100);
                }

                // Small delay to ensure file handle is released
                await Task.Delay(100);
                
                // Extract zip - file stream is now closed
                LogToFile(logFile, $"DownloadUpdateAsync: Download complete ({bytesDownloaded} bytes), starting extraction");
                try
                {
                    LogToFile(logFile, $"DownloadUpdateAsync: Extracting ZIP to: {tempDir}");
                    LogToFile(logFile, $"DownloadUpdateAsync: ZIP file should be closed, verifying...");
                    
                    // Verify file is accessible
                    if (!File.Exists(zipPath))
                    {
                        LogToFile(logFile, $"DownloadUpdateAsync: ERROR - ZIP file does not exist at {zipPath}");
                        throw new FileNotFoundException($"ZIP file not found: {zipPath}");
                    }
                    
                    var fileInfo = new FileInfo(zipPath);
                    LogToFile(logFile, $"DownloadUpdateAsync: ZIP file size: {fileInfo.Length} bytes");
                    
                    ZipFile.ExtractToDirectory(zipPath, tempDir, true);
                    LogToFile(logFile, "DownloadUpdateAsync: Extraction successful");
                    
                    LogToFile(logFile, "DownloadUpdateAsync: Deleting ZIP file");
                    File.Delete(zipPath);
                    LogToFile(logFile, "DownloadUpdateAsync: ZIP file deleted");
                    
                    // Verify extracted files
                    var extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                    LogToFile(logFile, $"DownloadUpdateAsync: Extracted {extractedFiles.Length} files");
                    foreach (var file in extractedFiles.Take(10))
                    {
                        LogToFile(logFile, $"DownloadUpdateAsync:   - {Path.GetFileName(file)}");
                    }
                }
                catch (Exception extractEx)
                {
                    LogToFile(logFile, $"DownloadUpdateAsync: ERROR extracting ZIP - {extractEx.GetType().Name}: {extractEx.Message}\n{extractEx}");
                    throw; // Rethrow to be caught by outer catch
                }

                LogToFile(logFile, $"DownloadUpdateAsync: Successfully completed, returning tempDir: {tempDir}");
                return tempDir;
            }
            catch (Exception ex)
            {
                // Log the error if possible
                try
                {
                    var logPath = Path.Combine(Path.GetTempPath(), "DeviceBatteryTray_Update.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: Download failed: {ex.GetType().Name} - {ex.Message}\n{ex}\n");
                }
                catch { }
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


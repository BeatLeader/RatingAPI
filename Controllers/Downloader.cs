using Newtonsoft.Json;
using System.IO.Compression;

namespace RatingAPI.Controllers
{
    public class Downloader
    {
        private string _mapsDirectory = "/home/maps";
        private static readonly HttpClient _sharedHttpClient = new HttpClient();
        private static readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(4, 4);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _hashLocks =
            new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        static Downloader()
        {
            _sharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; BeatSaverDownloader/1.0)");
        }

        public Downloader(string mapsDirectory)
        {
            _mapsDirectory = mapsDirectory;
        }

        public string? Map(string hash)
        {
            return MapAsync(hash).GetAwaiter().GetResult();
        }

        public async Task<string?> MapAsync(string hash)
        {
            var hashKey = hash.ToLowerInvariant();

            // Ensure only one MapAsync operates on the same hash concurrently.
            var hashSemaphore = _hashLocks.GetOrAdd(hashKey, _ => new SemaphoreSlim(1, 1));
            await hashSemaphore.WaitAsync();
            try
            {
                string lowerCaseDir = Path.Combine(_mapsDirectory, hashKey);
            if (Directory.Exists(lowerCaseDir))
            {
                return lowerCaseDir;
            }

                string mapDir = Path.Combine(_mapsDirectory, hash.ToUpper());

            if (Directory.Exists(mapDir))
            {
                return mapDir;
            }

                await _downloadSemaphore.WaitAsync();
                try
                {
                    if (Directory.Exists(lowerCaseDir))
                    {
                        return lowerCaseDir;
                    }

                    if (Directory.Exists(mapDir))
                    {
                        return mapDir;
                    }

                string beatsaverUrl = $"https://beatsaver.com/api/maps/hash/{hash}";
                dynamic? beatsaverData = null;
                string? downloadURL = null;
                
                try
                {
                    var response = await _sharedHttpClient.GetStringAsync(beatsaverUrl);
                    beatsaverData = response != null ? JsonConvert.DeserializeObject(response) : null;
                    downloadURL = string.Empty;
                }
                catch (Exception)
                {
                    return null;
                }

                if (beatsaverData == null)
                {
                    return null;
                }

                foreach (var version in beatsaverData.versions)
                {
                    if (version.hash.ToString().ToLower() == hash.ToLower())
                    {
                        downloadURL = version.downloadURL;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadURL))
                {
                    return null;
                }

                var data = await _sharedHttpClient.GetByteArrayAsync(downloadURL);

                using var zipStream = new MemoryStream(data);
                using var zipArchive = new ZipArchive(zipStream);
                Directory.CreateDirectory(mapDir);
                // Overwrite existing files if they exist to avoid exceptions when extracting.
                zipArchive.ExtractToDirectory(mapDir, true);

                string[] extractedFiles = Directory.GetFiles(mapDir);
                foreach (string extractedFile in extractedFiles)
                {
                    if (!extractedFile.EndsWith(".dat") && !extractedFile.EndsWith(".json") && !extractedFile.EndsWith(".data"))
                    {
                        try
                        {
                            File.Delete(extractedFile);
                        }
                        catch
                        {
                            // Handle exceptions if required or continue
                        }
                    }
                }

                    return mapDir;
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }
            finally
            {
                // Release and try to remove the semaphore to avoid unbounded growth.
                hashSemaphore.Release();
                try
                {
                    if (hashSemaphore.CurrentCount == 1)
                    {
                        _hashLocks.TryRemove(hashKey, out _);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}

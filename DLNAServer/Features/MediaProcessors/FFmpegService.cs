using DLNAServer.Common;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Files;
using DLNAServer.Helpers.Logger;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace DLNAServer.Features.MediaProcessors
{
    public partial class FFmpegService : IFFmpegService
    {
        private readonly ILogger<FFmpegService> _logger;
        public FFmpegService(
            ILogger<FFmpegService> logger)
        {
            _logger = logger;
        }
        private readonly static SemaphoreSlim downloadFFmpegFile = new(1, 1);
        public async Task EnsureFFmpegDownloaded()
        {
            try
            {
                _logger.LogGeneralDebugMessage("Started downloading ffmpeg files");
                _ = await downloadFFmpegFile.WaitAsync(timeout: TimeSpanValues.TimeMin5);

                var executablesPath = Path.Combine([Directory.GetCurrentDirectory(), "Resources", "executables"]);

                DirectoryInfo directoryInfo = new(executablesPath);

                DirectoryHelper.CreateDirectoryIfNoExists(directoryInfo);

                var files = directoryInfo.EnumerateFiles();

                string ffmpegFileName;
                string ffprobeFileName;
                if (OperatingSystem.IsWindows())
                {
                    ffmpegFileName = "ffmpeg.exe";
                    ffprobeFileName = "ffprobe.exe";
                }
                else if (OperatingSystem.IsLinux())
                {
                    ffmpegFileName = "ffmpeg";
                    ffprobeFileName = "ffprobe";
                }
                else
                {
                    throw new NotImplementedException($"Undefined {nameof(OperatingSystem)}");
                }

                var ffmpeg = files.FirstOrDefault(f => f.Name.Equals(ffmpegFileName, StringComparison.InvariantCultureIgnoreCase));
                var ffprobe = files.FirstOrDefault(f => f.Name.Equals(ffprobeFileName, StringComparison.InvariantCultureIgnoreCase));
                var isDownloaded = ffmpeg != null && ffprobe != null;

                if (!isDownloaded)
                {
                    await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, executablesPath).WaitAsync(TimeSpanValues.TimeMin15);
                    files = directoryInfo.EnumerateFiles();
                    ffmpeg = files.First(f => f.Name.Equals(ffmpegFileName, StringComparison.InvariantCultureIgnoreCase));
                    ffprobe = files.First(f => f.Name.Equals(ffprobeFileName, StringComparison.InvariantCultureIgnoreCase));
                }

                FFmpeg.SetExecutablesPath(executablesPath, ffmpeg!.Name, ffprobe!.Name);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
            finally
            {
                _ = downloadFFmpegFile.Release();
            }

            _logger.LogGeneralDebugMessage("Downloading ffmpeg files done");
        }
        public async Task<IMediaInfo?> TryGetMediaInfo(string pathfullName, CancellationToken cancellationToken = default)
        {
            try
            {
                return await FFmpeg.GetMediaInfo(pathfullName, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                LogErrorFFmpegGetMediaInfo(ex.Message);
                return null;
            }
        }
    }
}

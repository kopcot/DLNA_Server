using Xabe.FFmpeg;

namespace DLNAServer.Features.MediaProcessors.Interfaces
{
    public interface IFFmpegService
    {
        Task EnsureFFmpegDownloaded();
        Task<IMediaInfo?> TryGetMediaInfo(string pathfullName, CancellationToken cancellationToken = default);
    }
}

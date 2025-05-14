using DLNAServer.Database.Entities;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Types.DLNA;

namespace DLNAServer.Features.MediaProcessors
{
    public class MediaProcessingService : IMediaProcessingService
    {
        private readonly IAudioProcessor AudioProcessor;
        private readonly IImageProcessor ImageProcessor;
        private readonly IVideoProcessor VideoProcessor;

        public MediaProcessingService(
            IAudioProcessor audioProcessor,
            IImageProcessor imageProcessor,
            IVideoProcessor videoProcessor)
        {
            AudioProcessor = audioProcessor;
            ImageProcessor = imageProcessor;
            VideoProcessor = videoProcessor;
        }

        public async Task FillEmptyInfoAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            if (!fileEntities.Any())
            {
                return;
            }

            var mediaGroup = fileEntities
                .GroupBy(static (fe) => fe.FileDlnaMime.ToDlnaMedia())
                .ToDictionary(static (g) => g.Key, static (g) => g);

            foreach (var group in mediaGroup)
            {
                switch (group.Key)
                {
                    case DlnaMedia.Audio:
                        await AudioProcessor.FillEmptyInfoAsync(group.Value, setCheckedForFailed);
                        break;
                    case DlnaMedia.Image:
                        await ImageProcessor.FillEmptyInfoAsync(group.Value, setCheckedForFailed);
                        break;
                    case DlnaMedia.Video:
                        await VideoProcessor.FillEmptyInfoAsync(group.Value, setCheckedForFailed);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported media type: {group.Key}");
                }
            }
        }

        public async Task FillEmptyMetadataAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            if (!fileEntities.Any())
            {
                return;
            }

            var mediaGroup = fileEntities
                .GroupBy(static (fe) => fe.FileDlnaMime.ToDlnaMedia())
                .ToDictionary(static (g) => g.Key, static (g) => g);

            foreach (var group in mediaGroup)
            {
                switch (group.Key)
                {
                    case DlnaMedia.Audio:
                        await AudioProcessor.FillEmptyMetadataAsync(group.Value, setCheckedForFailed);
                        break;
                    case DlnaMedia.Image:
                        await ImageProcessor.FillEmptyMetadataAsync(group.Value, setCheckedForFailed);
                        break;
                    case DlnaMedia.Video:
                        await VideoProcessor.FillEmptyMetadataAsync(group.Value, setCheckedForFailed);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported media type: {group.Key}");
                }
            }
        }
        public async Task FillEmptyThumbnailsAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            if (!fileEntities.Any())
            {
                return;
            }

            var mediaGroup = fileEntities
                .GroupBy(static (fe) => fe.FileDlnaMime.ToDlnaMedia())
                .ToDictionary(static (g) => g.Key, static (g) => g);

            foreach (var group in mediaGroup)
            {
                switch (group.Key)
                {
                    case DlnaMedia.Audio:
                        await AudioProcessor.FillEmptyThumbnailsAsync(group.Value, setCheckedForFailed);
                        break;
                    case DlnaMedia.Image:
                        await ImageProcessor.FillEmptyThumbnailsAsync(group.Value, setCheckedForFailed);
                        break;
                    case DlnaMedia.Video:
                        await VideoProcessor.FillEmptyThumbnailsAsync(group.Value, setCheckedForFailed);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported media type: {group.Key}");
                }
            }
        }
        public Task InitializeAsync()
        {
            throw new NotImplementedException();
        }
        public Task TerminateAsync()
        {
            throw new NotImplementedException();
        }
    }
}

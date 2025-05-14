using SkiaSharp;

namespace DLNAServer.Features.MediaProcessors
{
    public partial class ImageProcessor
    {
        [LoggerMessage(1, LogLevel.Information, "Set thumbnail for file: '{file}'")]
        partial void InformationSetThumbnail(string file);
        [LoggerMessage(2, LogLevel.Debug, "Created thumbnail as '{file}'")]
        partial void DebugCreateThumbnail(string file);
        [LoggerMessage(3, LogLevel.Error, "Unable to create SKCodec for file '{fileFullPath}'. Codec result: {codecResult}")]
        partial void ErrorSKCodec(string fileFullPath, SKCodecResult codecResult);
    }
}

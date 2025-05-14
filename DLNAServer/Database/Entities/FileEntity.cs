using DLNAServer.Helpers.Attributes;
using DLNAServer.Types.DLNA;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DLNAServer.Database.Entities
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [Index(propertyName: nameof(FilePhysicalFullPath), IsUnique = true)]
    [Index(propertyName: nameof(LC_FilePhysicalFullPath), IsUnique = true)]
    [Index(propertyName: nameof(DirectoryId), IsUnique = false)]
    [Index(propertyName: nameof(ThumbnailId), IsUnique = true)]
    [Table(nameof(DlnaDbContext.FileEntities))] // needed as in DlnaDbContext is in plural
    public class FileEntity : BaseEntity
    {
        // File
        [MaxLength(4096, ErrorMessage = $"File name cannot exceed 4096 characters. Property {nameof(FileName)}")]
        [StringCache]
        public string FileName { get; set; }
        [Lowercase(nameof(FileName))]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [MaxLength(4096, ErrorMessage = $"File name cannot exceed 4096 characters. Property {nameof(LC_FileName)}")]
        [StringCache]
        public string LC_FileName { get; set; }
        [MaxLength(4096, ErrorMessage = $"Folder full path cannot exceed 4096 characters. Property {nameof(Folder)}")]
        [StringCache]
        public string? Folder { get; set; }
        [Lowercase(nameof(Folder))]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [MaxLength(4096, ErrorMessage = $"Folder full path cannot exceed 4096 characters. Property {nameof(LC_Folder)}")]
        [StringCache]
        public string LC_Folder { get; set; }
        [ForeignKey(nameof(Directory))]
        public Guid? DirectoryId { get; set; }
        public virtual DirectoryEntity? Directory { get; set; }
        public DlnaMime FileDlnaMime { get; set; }
        [MaxLength(128, ErrorMessage = $"Dlna Profile Name cannot exceed 128 characters. Property {nameof(FileDlnaProfileName)}")]
        [InternString]
        public string? FileDlnaProfileName { get; set; }
        [MaxLength(4096, ErrorMessage = $"File full path cannot exceed 4096 characters. Property {nameof(FilePhysicalFullPath)}")]
        [StringCache]
        public string FilePhysicalFullPath { get; set; }
        [Lowercase(nameof(FilePhysicalFullPath))]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [MaxLength(4096, ErrorMessage = $"File full path cannot exceed 4096 characters. Property {nameof(LC_FilePhysicalFullPath)}")]
        [StringCache]
        public string LC_FilePhysicalFullPath { get; set; }
        [MaxLength(32, ErrorMessage = $"File extension cannot exceed 32 characters. Property {nameof(FileExtension)}")]
        [InternString]
        public string FileExtension { get; set; }
        public long FileSizeInBytes { get; set; }
        public DateTime FileCreateDate { get; set; }
        public DateTime FileModifiedDate { get; set; }
        public bool FileUnableToCache { get; set; }
        [MaxLength(4096, ErrorMessage = $"Title cannot exceed 4096 characters. Property {nameof(Title)}")]
        [StringCache]
        public string Title { get; set; }
        [Lowercase(nameof(Title))]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [MaxLength(4096, ErrorMessage = $"Title cannot exceed 4096 characters. Property {nameof(LC_Title)}")]
        [StringCache]
        public string LC_Title { get; set; }
        //public Guid[]? SubtitlesFileIds { get; set; }
        public DlnaItemClass UpnpClass { get; set; }
        // Thumbnail file
        public bool IsThumbnailChecked { get; set; }
        public Guid? ThumbnailId { get; set; }
        [ForeignKey(nameof(ThumbnailId))]
        public ThumbnailEntity? Thumbnail { get; set; }
        // Metadata
        public bool IsMetadataChecked { get; set; }
        public Guid? AudioMetadataId { get; set; }
        [ForeignKey(nameof(AudioMetadataId))]
        public MediaAudioEntity? AudioMetadata { get; set; }
        public Guid? VideoMetadataId { get; set; }
        [ForeignKey(nameof(VideoMetadataId))]
        public MediaVideoEntity? VideoMetadata { get; set; }
        public Guid? SubtitleMetadataId { get; set; }
        [ForeignKey(nameof(SubtitleMetadataId))]
        public MediaSubtitleEntity? SubtitleMetadata { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
}

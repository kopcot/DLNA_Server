using DLNAServer.Helpers.Attributes;
using DLNAServer.Types.DLNA;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DLNAServer.Database.Entities
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    [Index(propertyName: nameof(FilePhysicalFullPath), IsUnique = true)]
    [Index(propertyName: nameof(ThumbnailDataId), IsUnique = true)]
    [Index(propertyName: nameof(ThumbnailFilePhysicalFullPath), IsUnique = true)]
    [Index(propertyName: nameof(LC_ThumbnailFilePhysicalFullPath), IsUnique = true)]
    [Table(nameof(DlnaDbContext.ThumbnailEntities))] // needed as in DlnaDbContext is in plural 
    public class ThumbnailEntity : BaseEntity
    {
        [MaxLength(4096, ErrorMessage = $"File full path cannot exceed 4096 characters. Property {nameof(FilePhysicalFullPath)}")]
        [StringCache]
        public string FilePhysicalFullPath { get; set; }
        [MaxLength(4096, ErrorMessage = $"Thumbnail file full path cannot exceed 4096 characters. Property {nameof(ThumbnailFilePhysicalFullPath)}")]
        [StringCache]
        public string ThumbnailFilePhysicalFullPath { get; set; }
        [Lowercase(nameof(ThumbnailFilePhysicalFullPath))]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [MaxLength(4096, ErrorMessage = $"Thumbnail file full path cannot exceed 4096 characters. Property {nameof(LC_ThumbnailFilePhysicalFullPath)}")]
        [StringCache]
        public string LC_ThumbnailFilePhysicalFullPath { get; set; }
        public DlnaMime ThumbnailFileDlnaMime { get; set; }
        [MaxLength(128, ErrorMessage = $"Dlna Profile name cannot exceed 128 characters. Property {nameof(ThumbnailFileDlnaProfileName)}")]
        [InternString]
        public string? ThumbnailFileDlnaProfileName { get; set; }
        [MaxLength(32, ErrorMessage = $"File extension cannot exceed 32 characters. Property {nameof(ThumbnailFileExtension)}")]
        [InternString]
        public string ThumbnailFileExtension { get; set; }
        public long ThumbnailFileSizeInBytes { get; set; }
        public Guid? ThumbnailDataId { get; set; }
        [ForeignKey(nameof(ThumbnailDataId))]
        public ThumbnailDataEntity? ThumbnailData { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
}

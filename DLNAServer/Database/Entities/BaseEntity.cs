using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DLNAServer.Database.Entities
{
    [Index(propertyName: nameof(Id), IsUnique = true)]
    public abstract class BaseEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Key]
        [Required]
        public Guid Id { get; set; } // GUID used for DLNA compliance as ObjectID from SOAP-reuqest
        /// <summary>
        /// <see cref="DateTime.Now"/> as default
        /// </summary>
        public DateTime CreatedInDB { get; set; } = DateTime.Now;
        /// <summary>
        /// <see cref="DateTime.Now"/> as default
        /// </summary>
        public DateTime? ModifiedInDB { get; set; }
    }
}

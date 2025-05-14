using DLNAServer.Helpers.Attributes;
using DLNAServer.Helpers.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using System.Reflection;

namespace DLNAServer.Database.Entities.Configurations
{
    public class BaseEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity> where TEntity : BaseEntity
    {
        public void Configure(EntityTypeBuilder<TEntity> builder)
        {
            ArgumentNullException.ThrowIfNull(nameof(builder));

            _ = builder.HasKey(static (e) => e.Id);

            // Configure Sequential Guid for Id
            _ = builder.Property(static (e) => e.Id)
                .IsRequired(true)
                .HasValueGenerator<SequentialGuidValueGenerator>()
                .ValueGeneratedOnAdd();
            _ = builder.Property(static (e) => e.CreatedInDB)
                .IsRequired(true)
                .ValueGeneratedOnAdd();
            _ = builder.Property(static (e) => e.ModifiedInDB)
                .IsRequired(false)
                .ValueGeneratedOnUpdate();

            // Configure properties of the entity
            var properties = typeof(TEntity).GetProperties();
            foreach (var property in properties)
            {
                {
                    // Check if the property has the LowercaseAttribute
                    var lowercaseAttribute = property.GetCustomAttribute<LowercaseAttribute>();
                    if (lowercaseAttribute != null
                        && lowercaseAttribute.PropertyName is string propertyName
                        && properties.Any(p => p.Name == propertyName))
                    {
                        // If the attribute is present, apply the computed column logic to convert it to lowercase
                        _ = builder.Property(property.Name)
                            //.HasComputedColumnSql($"LOWER([{lowercaseAttribute.PropertyName}])", stored: true)
                            .HasComputedColumnSql($"LOWER(`{lowercaseAttribute.PropertyName}`)", stored: true)
                            .ValueGeneratedOnAddOrUpdate();
                    }

                    // Check if the property has the InternStringAttribute
                    var internStringAttribute = property.GetCustomAttribute<InternStringAttribute>();
                    if (internStringAttribute != null
                        && (property.PropertyType == typeof(string)))
                    {
                        _ = builder.Property(property.Name)
                            .HasConversion(new InternStringConverter());
                    }

                    // Check if the property has the StringCacheAttribute
                    var cacheStringAttribute = property.GetCustomAttribute<StringCacheAttribute>();
                    if (cacheStringAttribute != null
                        && (property.PropertyType == typeof(string)))
                    {
                        _ = builder.Property(property.Name)
                            .HasConversion(new StringCacheConverter());
                    }
                }
            }
        }
    }
}

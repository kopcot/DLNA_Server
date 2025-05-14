using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DLNAServer.Helpers.Database.Conversions
{
    public class InternStringConverter : ValueConverter<string?, string?>
    {
        public InternStringConverter()
            : base(
                  convertToProviderExpression: static (value) => value != null ? string.Intern(value) : null,
                  convertFromProviderExpression: static (value) => value != null ? string.Intern(value) : null)
        {
        }
    }
}

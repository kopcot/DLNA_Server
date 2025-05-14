namespace DLNAServer.Helpers.Attributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class LowercaseAttribute : Attribute
    {
        public string PropertyName { get; }
        public LowercaseAttribute(string propertyName)
        {
            PropertyName = propertyName;
        }
    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class InternStringAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class StringCacheAttribute : Attribute
    {
    }
}

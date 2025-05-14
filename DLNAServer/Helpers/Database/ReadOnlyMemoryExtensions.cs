using System.Runtime.InteropServices;

namespace DLNAServer.Helpers.Database
{
    public static class MemoryExtensions
    {
        public static TResult[] AsArray<TResult>(this ReadOnlyMemory<TResult> memory)
        {
            if (MemoryMarshal.TryGetArray(memory, out ArraySegment<TResult> segment)
                && segment.Array is TResult[] array)
            {
                return array;
            }
            return memory.IsEmpty ? [] : memory.ToArray();
        }
    }
}

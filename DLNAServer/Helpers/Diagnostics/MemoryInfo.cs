using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DLNAServer.Helpers.Diagnostics
{
    public static partial class MemoryInfo
    {
        public static int GetObjectSize(object obj)
        {
            if (obj == null)
            {
                return 0;
            }

            try
            {
                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(obj);
                return jsonBytes.Length; // Returns size in bytes
            }
            catch
            {
                return -1;
            }
        }
        public static Dictionary<string, object> ProcessMemoryInfo()
        {
            const double fromBtoMB = 1024 * 1024;
            double allocated = GC.GetTotalMemory(forceFullCollection: false) / fromBtoMB;
            double totalCommittedBytes = GC.GetGCMemoryInfo().TotalCommittedBytes / fromBtoMB;
            double totalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / fromBtoMB;
            double memoryLoadBytes = GC.GetGCMemoryInfo().MemoryLoadBytes / fromBtoMB;
            double heapSizeBytes = GC.GetGCMemoryInfo().HeapSizeBytes / fromBtoMB;
            long pinnedObjectsCount = GC.GetGCMemoryInfo().PinnedObjectsCount;

            using var process = Process.GetCurrentProcess();
            double privateMemorySize64 = process.PrivateMemorySize64 / fromBtoMB;
            int pid = process.Id;
            double workingSet64 = process.WorkingSet64 / fromBtoMB;
            double nonpagedSystemMemorySize64 = process.NonpagedSystemMemorySize64 / fromBtoMB;
            double pagedMemorySize64 = process.PagedMemorySize64 / fromBtoMB;
            double pagedSystemMemorySize64 = process.PagedSystemMemorySize64 / fromBtoMB;
            double virtualMemorySize64 = process.VirtualMemorySize64 / fromBtoMB;
            string mainWindowTitle = process.MainWindowTitle;
            DateTime startTime = process.StartTime;
            int threadCount = process.Threads.Count;
            Dictionary<System.Diagnostics.ThreadState, Dictionary<string, int>> threadStatusWaitReason = process
                    .Threads
                    .Cast<ProcessThread>()
                    .GroupBy(static (pt) => pt.ThreadState)
                    .ToDictionary(
                        static (ptg) => ptg.Key,
                        static (ptg) => ptg.GroupBy(static (pt) => pt.ThreadState == System.Diagnostics.ThreadState.Wait
                            ? $"Wait reason - {pt.WaitReason}"
                            : $"{pt.ThreadState}")
                            .ToDictionary(
                                static (wrg) => wrg.Key,
                                static (wrg) => wrg.Count()
                            )
                    );

            Dictionary<string, object> data = new(32)
            {
                { "Main window title", mainWindowTitle },
                { "Process ID", pid },
                { "Start time", startTime },
                { "Utc time", DateTime.UtcNow },
                { "Local time", DateTime.Now },
                { "GC-collection Gen0 (count)", GC.CollectionCount(0) },
                { "GC-collection Gen1 (count)", GC.CollectionCount(1) },
                { "GC-collection Gen2 (count)", GC.CollectionCount(2) },
                { "Pinned objects count", pinnedObjectsCount },
                { "Allocated (MB)", allocated },
                { "Total committed bytes (MB)", totalCommittedBytes },
                { "Total available memory bytes (MB)", totalAvailableMemoryBytes },
                { "Memory load bytes (MB)", memoryLoadBytes },
                { "Heap size bytes (MB)", heapSizeBytes },
                { "Working set 64-bit (MB)", workingSet64 },
                { "Private memory size 64-bit (MB)", privateMemorySize64 },
                { "Virtual memory set 64-bit (MB)", virtualMemorySize64 },
                { "Paged memory size 64-bit (MB)", pagedMemorySize64 },
                { "Paged system memory size 64-bit (MB)", pagedSystemMemorySize64 },
                { "Nonpaged system memory size 64-bit (MB)", nonpagedSystemMemorySize64 },
                { "GC - Is Server GC", GCSettings.IsServerGC },
                { "GC - Large Object Heap (LOH) compaction mode", $"{GCSettings.LargeObjectHeapCompactionMode}"},
                { "GC - Latency mode ", $"{GCSettings.LatencyMode}" },
                { "GC - Is Concurrent (background) GC", GC.GetGCMemoryInfo().Concurrent },
                { "GC - Index of this GC", GC.GetGCMemoryInfo().Index },
                { "GC - Generation of this GC", GC.GetGCMemoryInfo().Generation },
                { "Threads", threadCount }
            };
            foreach (var threadState in threadStatusWaitReason)
            {
                data.Add($"Threads in state {threadState.Key}", threadState.Value);
            }

            var index = 0;
            foreach (var generationInfo in GC.GetGCMemoryInfo().GenerationInfo)
            {
                data.Add("Generation Info " + index + " - Fragmentation before bytes", $"{generationInfo.FragmentationBeforeBytes}");
                data.Add("Generation Info " + index + " - Fragmentation after bytes", $"{generationInfo.FragmentationAfterBytes}");
                data.Add("Generation Info " + index + " - Size before bytes", $"{generationInfo.SizeBeforeBytes}");
                data.Add("Generation Info " + index + " - Size after bytes", $"{generationInfo.SizeAfterBytes}");
                index++;
            }
            return data;
        }
        public static void LogMemoryInfo<T>(ILogger<T> logger, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string methodName = "")
        {
            using (var process = Process.GetCurrentProcess())
            {
                const double fromBtoMB = 1024 * 1024;
                double allocated = GC.GetTotalMemory(forceFullCollection: false) / fromBtoMB;
                double heapSizeBytes = GC.GetGCMemoryInfo().HeapSizeBytes / fromBtoMB;
                double workingSet64 = process.WorkingSet64 / fromBtoMB;
                double privateMemorySize64 = process.PrivateMemorySize64 / fromBtoMB;

                LogMemoryInfo(logger, methodName, lineNumber, allocated, heapSizeBytes, workingSet64, privateMemorySize64);
            }
        }
    }
}

namespace FortniteCompanion.Core.Replay;

/// <summary>
/// Bir replay dosyasını diskten salt okunur paylaşımla okur ve en düşük öncelikli arka plan iş parçacığında,
/// zaman aşımıyla çözümler. Kütüphane statik zaman alanı kullandığı için aynı anda tek analiz yapılır.
/// </summary>
public static class ReplayProcessor
{
    private static readonly object Gate = new();

    public static ReplayMatchResult Analyze(string path, TimeSpan timeout)
    {
        byte[] bytes;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }

        lock (Gate)
        {
            ReplayMatchResult? result = null;
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    result = new ReplayAnalyzer().Analyze(ms, Path.GetFileName(path));
                }
                catch (Exception e)
                {
                    failure = e;
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest,
                Name = "ReplayAnalysis",
            };
            worker.Start();
            if (!worker.Join(timeout))
            {
                throw new TimeoutException("Replay analizi zaman aşımına uğradı.");
            }

            bytes = Array.Empty<byte>();
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result!;
        }
    }
}

namespace iBarter.View;

using System.IO;
using System.Windows.Media.Imaging;

internal sealed class LocalTileBitmapCache {
    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<string, CacheEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<BitmapSource?>> inFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> lru = new();

    public LocalTileBitmapCache(int capacity = 256) {
        this.capacity = Math.Max(16, capacity);
    }

    public Task<BitmapSource?> GetAsync(string path) {
        lock (gate) {
            if (entries.TryGetValue(path, out var cached)) {
                Touch(cached);
                return Task.FromResult<BitmapSource?>(cached.Bitmap);
            }
            if (inFlight.TryGetValue(path, out var existing)) return existing;

            Task<BitmapSource?> task = Task.Run(() => Load(path));
            inFlight[path] = task;
            _ = task.ContinueWith(
                completed => Complete(path, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    public void Clear() {
        lock (gate) {
            entries.Clear();
            lru.Clear();
        }
    }

    private void Complete(string path, Task<BitmapSource?> task) {
        lock (gate) {
            inFlight.Remove(path);
            if (task.Status != TaskStatus.RanToCompletion || task.Result is null) return;
            if (entries.TryGetValue(path, out var existing)) {
                Touch(existing);
                return;
            }

            var node = lru.AddFirst(path);
            entries[path] = new CacheEntry(task.Result, node);
            while (entries.Count > capacity && lru.Last is { } last) {
                entries.Remove(last.Value);
                lru.RemoveLast();
            }
        }
    }

    private void Touch(CacheEntry entry) {
        lru.Remove(entry.Node);
        lru.AddFirst(entry.Node);
    }

    private static BitmapSource? Load(string path) {
        try {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException) {
            return null;
        }
    }

    private sealed record CacheEntry(BitmapSource Bitmap, LinkedListNode<string> Node);
}

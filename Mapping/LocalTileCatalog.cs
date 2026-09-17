namespace iBarter.Mapping;

using System.IO;

public sealed class LocalTileCatalog {
    private readonly string mapRoot;
    private readonly string extension;

    public LocalTileCatalog(string mapRoot, string extension) {
        this.mapRoot = Path.GetFullPath(mapRoot);
        this.extension = extension.StartsWith('.') ? extension : "." + extension;
    }

    public string? TryResolve(TileAddress address) {
        string path = Path.Combine(
            mapRoot,
            "base",
            $"z{address.Zoom}",
            $"x{address.X}",
            $"y{address.Y}{extension}");
        return File.Exists(path) ? path : null;
    }
}

namespace iBarter.Mapping;

public static class VisibleTileEnumerator {
    public static VisibleTileLayout Enumerate(
        XyzViewportCamera camera,
        double viewportWidth,
        double viewportHeight,
        int tileSize) {
        if (viewportWidth <= 0 || viewportHeight <= 0 || tileSize <= 0)
            return new VisibleTileLayout(
                Math.Clamp((int)Math.Floor(camera.Zoom), camera.MinZoom, camera.MaxZoom),
                []);

        int tileZoom = Math.Clamp(
            (int)Math.Floor(camera.Zoom), camera.MinZoom, camera.MaxZoom);
        double scale = Math.Pow(2, camera.Zoom - tileZoom);
        double worldSize = WebMercatorProjection.WorldSize(tileZoom, tileSize);
        double centerX = camera.Center.X * worldSize;
        double centerY = camera.Center.Y * worldSize;
        double halfWidth = viewportWidth / (2 * scale);
        double halfHeight = viewportHeight / (2 * scale);
        double left = centerX - halfWidth;
        double top = centerY - halfHeight;
        double right = centerX + halfWidth;
        double bottom = centerY + halfHeight;
        int tileCount = 1 << tileZoom;
        int minX = Math.Clamp((int)Math.Floor(left / tileSize), 0, tileCount - 1);
        int maxX = Math.Clamp((int)Math.Floor((right - 0.000001) / tileSize), 0, tileCount - 1);
        int minY = Math.Clamp((int)Math.Floor(top / tileSize), 0, tileCount - 1);
        int maxY = Math.Clamp((int)Math.Floor((bottom - 0.000001) / tileSize), 0, tileCount - 1);
        double displaySize = tileSize * scale;
        var result = new List<VisibleTilePlacement>();

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                double screenX = (x * tileSize - centerX) * scale + viewportWidth / 2;
                double screenY = (y * tileSize - centerY) * scale + viewportHeight / 2;
                result.Add(new VisibleTilePlacement(
                    new TileAddress(tileZoom, x, y),
                    screenX,
                    screenY,
                    displaySize));
            }
        }
        return new VisibleTileLayout(tileZoom, result);
    }
}

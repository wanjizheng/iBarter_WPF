using iBarter.Routing;
using Xunit;
namespace AutomaticRoutePlanningTests;
public class TaggedPortCatalogTests {
    [Fact] public void MissingWharfsMergeWithoutChangingUserChoicesOrInventingWarehouses() {
        var settings = new TaggedTransportSettings { Ports = [new() { IslandId = "ShakatuPier", Enabled = false, Npc = "Purio" }] };
        TaggedPortCatalog.Merge(settings);
        TaggedPortCatalog.Merge(settings);
        Assert.Single(settings.Ports, p => p.IslandId == "ShakatuPier");
        Assert.False(settings.Ports.Single(p => p.IslandId == "ShakatuPier").Enabled);
        Assert.True(settings.Ports.Single(p => p.IslandId == "RunnPier").Enabled);
        foreach (string id in new[] { "ShakatuPier", "RunnPier", "Altinova", "SanctuaryCoastalOutpost", "AulisCoast" }) {
            var port = settings.Ports.Single(p => p.IslandId == id);
            Assert.Empty(port.WarehouseId);
            Assert.NotEqual(port.Npc, TaggedPortCatalog.ChineseNpc(port.Npc));
        }
        Assert.False(settings.Ports.Single(p => p.IslandId == "AulisCoast").Enabled);
    }
}

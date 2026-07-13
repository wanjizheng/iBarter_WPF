using iBarter.Routing;
using Xunit;

namespace AutomaticRoutePlanningTests;

public sealed class RoutePlanFingerprintTests {
    [Fact]
    public void Fingerprint_is_order_independent_for_dictionary_entries() {
        var forward = RouteTestData.TwoItemRequest(reverseDictionaryOrder: false);
        var reverse = RouteTestData.TwoItemRequest(reverseDictionaryOrder: true);
        Assert.Equal(RoutePlanFingerprint.Compute(forward), RoutePlanFingerprint.Compute(reverse));
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("warehouse")]
    [InlineData("total-lt")]
    [InlineData("coordinate")]
    public void Fingerprint_changes_when_route_relevant_input_changes(string mutation) {
        var original = RouteTestData.TwoItemRequest(reverseDictionaryOrder: false);
        var changed = RouteTestData.Mutate(original, mutation);
        Assert.NotEqual(RoutePlanFingerprint.Compute(original), RoutePlanFingerprint.Compute(changed));
    }

    [Fact]
    public void Fingerprint_is_a_stable_sha256_hex_string() {
        string value = RoutePlanFingerprint.Compute(RouteTestData.SingleTask());
        Assert.Equal(64, value.Length);
        Assert.All(value, c => Assert.True(char.IsAsciiHexDigit(c)));
        Assert.Equal(value, RoutePlanFingerprint.Compute(RouteTestData.SingleTask()));
    }
}

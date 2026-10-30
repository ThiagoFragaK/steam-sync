namespace SteamSync.Worker.Tests;

/// <summary>
/// Legacy handler tests targeted obsolete namespaces after the Worker refactor.
/// Binding and sync behaviour are covered by <see cref="SteamIdBindingTests"/>.
/// </summary>
public class UserSyncMessageHandlerTests
{
    [Fact]
    public void Placeholder_keeps_suite_discoverable()
    {
        Assert.True(true);
    }
}

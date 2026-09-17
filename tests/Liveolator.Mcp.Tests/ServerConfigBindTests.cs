using Liveolator.Mcp;
using Xunit;

namespace Liveolator.Mcp.Tests;

/// <summary>
/// The HTTP transport's bind address. It carries the whole library API and has no authentication of
/// its own, so the default must stay loopback: the failure mode of getting this wrong is not a broken
/// server but a silently reachable one.
/// </summary>
public sealed class ServerConfigBindTests
{
    [Fact]
    public void Defaults_to_loopback()
    {
        Assert.Equal("127.0.0.1", ServerConfig.Parse(new[] { "--http" }).BindAddress);
        Assert.Equal("127.0.0.1", ServerConfig.Parse(new[] { "--stdio" }).BindAddress);
    }

    [Fact]
    public void Explicit_bind_is_honoured()
    {
        // A container needs this: binding the CONTAINER's loopback makes a published port forward to
        // nothing, because Docker forwards to the container's network interface instead.
        ServerConfig config = ServerConfig.Parse(new[] { "--http", "--port", "5175", "--bind", "0.0.0.0" });

        Assert.Equal("0.0.0.0", config.BindAddress);
        Assert.Equal(5175, config.Port);
        Assert.Equal(ServerMode.Http, config.Mode);
    }

    [Fact]
    public void Blank_bind_falls_back_to_loopback_rather_than_binding_everything()
    {
        Assert.Equal("127.0.0.1", ServerConfig.Parse(new[] { "--http", "--bind", "   " }).BindAddress);
    }

    [Fact]
    public void A_missing_bind_value_is_rejected_instead_of_silently_ignored()
    {
        Assert.ThrowsAny<System.Exception>(() => ServerConfig.Parse(new[] { "--http", "--bind" }));
    }
}

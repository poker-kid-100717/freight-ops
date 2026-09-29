using Npgsql;
using Portfolio.Freight.Api.Data;

namespace Portfolio.Freight.Api.Tests;

public sealed class DatabaseUrlTests
{
    [Fact]
    public void NeonStyleUrlBecomesNpgsqlConnectionString()
    {
        var value = Database.ToNpgsql("postgresql://app%40user:p%40ss@ep-example.us-east-2.aws.neon.tech/freight?sslmode=require&channel_binding=require");
        var parsed = new NpgsqlConnectionStringBuilder(value);

        Assert.Equal("ep-example.us-east-2.aws.neon.tech", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("freight", parsed.Database);
        Assert.Equal("app@user", parsed.Username);
        Assert.Equal("p@ss", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.Equal(ChannelBinding.Require, parsed.ChannelBinding);
    }

    [Fact]
    public void ConnectionStringsPassThroughUnchanged()
    {
        const string value = "Host=localhost;Database=freight;Username=u;Password=p";
        Assert.Equal(value, Database.ToNpgsql(value));
    }
}

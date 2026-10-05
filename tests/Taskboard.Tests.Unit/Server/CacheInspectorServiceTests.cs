using System.Net;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;
using Taskboard.Server.Services;
using Xunit;

namespace Taskboard.Tests.Unit.Server;

/// <summary>
/// Inspeção de cache da aba Settings → Configuration: o decorator
/// <see cref="TrackingHybridCache"/> alimenta o registry e o
/// <see cref="CacheInspectorService"/> reporta provider + keys. Caminho
/// memory é determinístico; o caminho redis sem multiplexer registrado
/// reporta provider=redis não conectado sem tentar rede.
/// </summary>
public class CacheInspectorServiceTests
{
    private sealed class FakeHybridCache : HybridCache
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _tags = new(StringComparer.Ordinal);

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(key, out var existing))
            {
                return (T)existing!;
            }

            var created = await factory(state, cancellationToken).ConfigureAwait(false);
            _store[key] = created;
            _tags[key] = tags?.ToArray() ?? [];
            return created;
        }

        public override ValueTask SetAsync<T>(
            string key, T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            _tags[key] = tags?.ToArray() ?? [];
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            _tags.Remove(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            foreach (var kvp in _tags.Where(t => t.Value.Contains(tag, StringComparer.Ordinal)).ToList())
            {
                _store.Remove(kvp.Key);
                _tags.Remove(kvp.Key);
            }

            return ValueTask.CompletedTask;
        }
    }

    private static CacheInspectorService Inspector(
        CacheKeyRegistry registry,
        params (string Key, string Value)[] settings) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build(),
            registry,
            NullLogger<CacheInspectorService>.Instance);

    [Fact]
    public async Task Dado_GetOrCreate_Quando_Escreve_Entao_KeyRastreadaComTags()
    {
        var registry = new CacheKeyRegistry();
        var cache = new TrackingHybridCache(new FakeHybridCache(), registry);

        var value = await cache.GetOrCreateAsync(
            "providers:list",
            42,
            static (s, _) => ValueTask.FromResult(s.ToString()),
            tags: ["providers", "models"]);

        value.ShouldBe("42");
        var tracked = registry.Snapshot().ShouldHaveSingleItem();
        tracked.Key.ShouldBe("providers:list");
        tracked.Tags.ShouldBe("providers,models");
    }

    [Fact]
    public async Task Dado_SetERemove_Quando_Chama_Entao_RegistryAcompanha()
    {
        var registry = new CacheKeyRegistry();
        var cache = new TrackingHybridCache(new FakeHybridCache(), registry);

        await cache.SetAsync("agents:custom", new List<string> { "a" });
        registry.Snapshot().ShouldHaveSingleItem().Key.ShouldBe("agents:custom");

        await cache.RemoveAsync("agents:custom");
        registry.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_RemoveByTag_Quando_Chama_Entao_SomenteKeysDaTagSomem()
    {
        var registry = new CacheKeyRegistry();
        var cache = new TrackingHybridCache(new FakeHybridCache(), registry);

        await cache.SetAsync("providers:list", 1, tags: ["providers"]);
        await cache.SetAsync("models:abc", 2, tags: ["models", "providers"]);
        await cache.SetAsync("other:key", 3, tags: ["other"]);

        await cache.RemoveByTagAsync("providers");

        var remaining = registry.Snapshot();
        remaining.ShouldHaveSingleItem().Key.ShouldBe("other:key");
    }

    [Fact]
    public async Task Dado_SemRedis_Quando_GetStats_Entao_ProviderMemoryComKeysDoRegistry()
    {
        var registry = new CacheKeyRegistry();
        var cache = new TrackingHybridCache(new FakeHybridCache(), registry);
        await cache.SetAsync("chat:providers", 1);

        var stats = await Inspector(registry).GetStatsAsync();

        stats.Provider.ShouldBe("memory");
        stats.RedisConfigured.ShouldBeFalse();
        stats.RedisConnected.ShouldBeFalse();
        stats.ServerKeys.ShouldBeNull();
        stats.DefaultExpirationSeconds.ShouldBe(300);
        stats.LocalCacheExpirationSeconds.ShouldBe(60);
        var key = stats.Keys.ShouldHaveSingleItem();
        key.Key.ShouldBe("chat:providers");
        key.Source.ShouldBe("registry");
    }

    [Fact]
    public async Task Dado_RedisConfigurado_Quando_SemMultiplexer_Entao_ProviderRedisSemProbe()
    {
        var registry = new CacheKeyRegistry();

        var stats = await Inspector(
            registry,
            ("Taskboard:Cache:Redis:ConnectionString", "localhost:6379"),
            ("Taskboard:Cache:DefaultExpiration", "00:10:00"))
            .GetStatsAsync();

        stats.Provider.ShouldBe("redis");
        stats.RedisConfigured.ShouldBeTrue();
        stats.RedisConnected.ShouldBeFalse();
        stats.InstanceName.ShouldBe("harness:");
        stats.DefaultExpirationSeconds.ShouldBe(600);
        // TTL local é clampado ao default quando maior que ele.
        stats.LocalCacheExpirationSeconds.ShouldBe(60);
    }

    [Fact]
    public async Task Dado_RedisComMultiplexer_Quando_DoisGetStats_Entao_ProbeSoUmaVez()
    {
        // O probe (PING+SCAN) é reaproveitado por 5s — chamadas repetidas da
        // tela não pagam RTTs de Redis a cada refresh.
        var db = Substitute.For<IDatabase>();
        db.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(1));
        var server = Substitute.For<IServer>();
        server.IsConnected.Returns(true);
        server.Keys(
                Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(),
                Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(Enumerable.Empty<RedisKey>());
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        mux.GetEndPoints(Arg.Any<bool>()).Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
        mux.GetServer(Arg.Any<EndPoint>(), Arg.Any<object?>()).Returns(server);

        var inspector = new CacheInspectorService(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Taskboard:Cache:Redis:ConnectionString"] = "localhost:6379",
                }).Build(),
            new CacheKeyRegistry(),
            NullLogger<CacheInspectorService>.Instance,
            mux);

        var first = await inspector.GetStatsAsync();
        var second = await inspector.GetStatsAsync();

        first.RedisConnected.ShouldBeTrue();
        second.RedisConnected.ShouldBeTrue();
        second.ServerKeys.ShouldBe(0);
        await db.Received(1).PingAsync(Arg.Any<CommandFlags>());
    }
}

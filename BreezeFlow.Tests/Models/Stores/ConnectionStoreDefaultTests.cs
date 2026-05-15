using System;
using System.Collections.Generic;
using SampleELT.Models;
using SampleELT.Models.Stores;
using Xunit;

namespace SampleELT.Tests.Models.Stores
{
    /// <summary>
    /// IConnectionStore.Default にモックを差し替えてテストできることを確認する。
    /// </summary>
    [Collection("ConnectionStore")]
    public class ConnectionStoreDefaultTests : IDisposable
    {
        private readonly IConnectionStore _original;

        public ConnectionStoreDefaultTests()
        {
            _original = IConnectionStore.Default;
        }

        public void Dispose()
        {
            // テスト終了時に必ずデフォルトを戻す
            IConnectionStore.Default = _original;
        }

        [Fact]
        public void Default_DefaultsToConnectionRegistryInstance()
        {
            Assert.Same(ConnectionRegistry.Instance, _original);
        }

        [Fact]
        public void Default_CanBeReplacedWithMock()
        {
            var fake = new FakeStore();
            IConnectionStore.Default = fake;

            Assert.Same(fake, IConnectionStore.Default);

            var settings = new Dictionary<string, object?>();
            Assert.Equal("FAKE-CONN-STRING", IConnectionStore.Default.ResolveConnectionString(settings));
        }

        private class FakeStore : IConnectionStore
        {
            public DbConnectionInfo? GetById(Guid id) => null;
            public string? GetConnectionString(Guid id) => null;
            public DbConnectionInfo? FindConnection(Dictionary<string, object?> settings) => null;
            public string ResolveConnectionString(Dictionary<string, object?> settings) => "FAKE-CONN-STRING";
        }
    }
}

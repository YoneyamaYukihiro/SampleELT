using System;
using System.Collections.Generic;
using SampleELT.Models;
using Xunit;

namespace SampleELT.Tests.Models
{
    /// <summary>
    /// ResolveConnectionString のサイレントフォールバック削除を検証する。
    /// ConnectionId 指定があるのに解決できないケースは即時例外で、
    /// 旧 ConnectionString キーへサイレントに切り替わって誤った DB を叩くことを防ぐ。
    /// </summary>
    public class ConnectionResolutionTests
    {
        [Fact]
        public void NoConnectionId_FallsBackToLegacyConnectionString()
        {
            var settings = new Dictionary<string, object?>
            {
                ["ConnectionString"] = "Server=legacy;Database=old;"
            };
            var result = ConnectionRegistry.Instance.ResolveConnectionString(settings);
            Assert.Equal("Server=legacy;Database=old;", result);
        }

        [Fact]
        public void NoConnectionId_NoLegacy_ReturnsEmpty()
        {
            var settings = new Dictionary<string, object?>();
            var result = ConnectionRegistry.Instance.ResolveConnectionString(settings);
            Assert.Equal("", result);
        }

        [Fact]
        public void UnresolvedConnectionId_Throws_NoLegacyFallback()
        {
            // Guid 形式だが Registry に存在しない ID。レガシー ConnectionString があっても
            // 「ConnectionId が指定されているのに解決できない」ケースは即時例外にすべき。
            var ghostId = Guid.NewGuid();
            var settings = new Dictionary<string, object?>
            {
                ["ConnectionId"] = ghostId.ToString(),
                ["ConnectionString"] = "Server=should-not-be-used;Database=danger;"
            };

            Assert.Throws<ConnectionResolutionException>(
                () => ConnectionRegistry.Instance.ResolveConnectionString(settings));
        }

        [Fact]
        public void MalformedConnectionId_Throws()
        {
            var settings = new Dictionary<string, object?>
            {
                ["ConnectionId"] = "not-a-guid"
            };
            Assert.Throws<ConnectionResolutionException>(
                () => ConnectionRegistry.Instance.ResolveConnectionString(settings));
        }
    }
}

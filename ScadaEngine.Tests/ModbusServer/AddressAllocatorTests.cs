using ScadaEngine.ModbusServer.Core;
using ScadaEngine.ModbusServer.Models;

namespace ScadaEngine.Tests.ModbusServer;

/// <summary>
/// AddressMap append-only 配址核心測試（docs/plans 決策 2）：
/// 首配排序穩定、重載 append-only、刪點不回收、段溢位擲錯。
/// </summary>
public class AddressAllocatorTests
{
    private static Dictionary<string, AddressSegmentModel> DefaultSegments() => new()
    {
        ["Modbus"] = new AddressSegmentModel { nStart = 0, nCapacityPoints = 5000 },
        ["Calc"] = new AddressSegmentModel { nStart = 10000, nCapacityPoints = 2000 },
        ["Db"] = new AddressSegmentModel { nStart = 14000, nCapacityPoints = 3000 },
        ["OpcUa"] = new AddressSegmentModel { nStart = 20000, nCapacityPoints = 3000 },
    };

    private static CatalogPointModel Point(string szSid, PointSource source, string szName = "") => new()
    {
        szSID = szSid,
        szName = szName,
        Source = source,
    };

    [Fact]
    public void 首次配址_依SID自然排序_自段首偶數遞增()
    {
        var map = new AddressMapModel();
        // 故意亂序輸入，且含 S2 vs S10 自然排序陷阱
        var catalog = new List<CatalogPointModel>
        {
            Point("196865-S10", PointSource.Modbus),
            Point("196865-S2", PointSource.Modbus),
            Point("196865-S1", PointSource.Modbus),
        };

        var nAdded = AddressAllocator.Sync(map, catalog, DefaultSegments());

        Assert.Equal(3, nAdded);
        Assert.Equal(0, map.Entries.Single(e => e.szSID == "196865-S1").nAddress);
        Assert.Equal(2, map.Entries.Single(e => e.szSID == "196865-S2").nAddress);
        Assert.Equal(4, map.Entries.Single(e => e.szSID == "196865-S10").nAddress);
    }

    [Fact]
    public void 各來源類型_配在各自段起點()
    {
        var map = new AddressMapModel();
        var catalog = new List<CatalogPointModel>
        {
            Point("196865-S1", PointSource.Modbus),
            Point("CALC-S1", PointSource.Calc),
            Point("DB1-S1", PointSource.Db),
            Point("OPC1-S1", PointSource.OpcUa),
        };

        AddressAllocator.Sync(map, catalog, DefaultSegments());

        Assert.Equal(0, map.Entries.Single(e => e.szSID == "196865-S1").nAddress);
        Assert.Equal(10000, map.Entries.Single(e => e.szSID == "CALC-S1").nAddress);
        Assert.Equal(14000, map.Entries.Single(e => e.szSID == "DB1-S1").nAddress);
        Assert.Equal(20000, map.Entries.Single(e => e.szSID == "OPC1-S1").nAddress);
    }

    [Fact]
    public void 重載_新點只附加段尾_既有位址不變()
    {
        var map = new AddressMapModel();
        var segments = DefaultSegments();
        AddressAllocator.Sync(map, new List<CatalogPointModel>
        {
            Point("196865-S2", PointSource.Modbus),
            Point("196865-S3", PointSource.Modbus),
        }, segments);

        // 新點 196865-S1 排序在最前，但只能 append 到段尾（位址永不重排）
        var nAdded = AddressAllocator.Sync(map, new List<CatalogPointModel>
        {
            Point("196865-S1", PointSource.Modbus),
            Point("196865-S2", PointSource.Modbus),
            Point("196865-S3", PointSource.Modbus),
        }, segments);

        Assert.Equal(1, nAdded);
        Assert.Equal(0, map.Entries.Single(e => e.szSID == "196865-S2").nAddress);
        Assert.Equal(2, map.Entries.Single(e => e.szSID == "196865-S3").nAddress);
        Assert.Equal(4, map.Entries.Single(e => e.szSID == "196865-S1").nAddress);
    }

    [Fact]
    public void 刪點_位址保留不回收_新點不填空洞()
    {
        var map = new AddressMapModel();
        var segments = DefaultSegments();
        AddressAllocator.Sync(map, new List<CatalogPointModel>
        {
            Point("196865-S1", PointSource.Modbus),
            Point("196865-S2", PointSource.Modbus),
            Point("196865-S3", PointSource.Modbus),
        }, segments);

        // S2 從點位表消失（刪除），再加入新點 S4
        var nAdded = AddressAllocator.Sync(map, new List<CatalogPointModel>
        {
            Point("196865-S1", PointSource.Modbus),
            Point("196865-S3", PointSource.Modbus),
            Point("196865-S4", PointSource.Modbus),
        }, segments);

        Assert.Equal(1, nAdded);
        // 被刪的 S2 條目仍在（位址空洞保留）
        Assert.Equal(2, map.Entries.Single(e => e.szSID == "196865-S2").nAddress);
        // 新點配到段尾 6，不是回收 S2 的 2
        Assert.Equal(6, map.Entries.Single(e => e.szSID == "196865-S4").nAddress);
        Assert.Equal(4, map.Entries.Count);
    }

    [Fact]
    public void 段溢位_擲AddressSegmentFullException()
    {
        var segments = new Dictionary<string, AddressSegmentModel>
        {
            ["Modbus"] = new AddressSegmentModel { nStart = 0, nCapacityPoints = 2 },
        };
        var map = new AddressMapModel();
        var catalog = new List<CatalogPointModel>
        {
            Point("A-S1", PointSource.Modbus),
            Point("A-S2", PointSource.Modbus),
            Point("A-S3", PointSource.Modbus),
        };

        Assert.Throws<AddressSegmentFullException>(() => AddressAllocator.Sync(map, catalog, segments));
    }

    [Fact]
    public void 重掃_刷新名稱單位_位址不動()
    {
        var map = new AddressMapModel();
        var segments = DefaultSegments();
        AddressAllocator.Sync(map, new List<CatalogPointModel>
        {
            Point("196865-S1", PointSource.Modbus, "舊名稱"),
        }, segments);

        AddressAllocator.Sync(map, new List<CatalogPointModel>
        {
            Point("196865-S1", PointSource.Modbus, "新名稱"),
        }, segments);

        var entry = map.Entries.Single();
        Assert.Equal("新名稱", entry.szName);
        Assert.Equal(0, entry.nAddress);
    }

    [Fact]
    public void 首配結果_與輸入順序無關_可重現()
    {
        var segments = DefaultSegments();
        var catalog = new List<CatalogPointModel>
        {
            Point("DB2-S1", PointSource.Db),
            Point("DB1-S10", PointSource.Db),
            Point("DB1-S9", PointSource.Db),
        };

        var map1 = new AddressMapModel();
        AddressAllocator.Sync(map1, catalog, segments);

        catalog.Reverse();
        var map2 = new AddressMapModel();
        AddressAllocator.Sync(map2, catalog, segments);

        foreach (var e1 in map1.Entries)
        {
            Assert.Equal(e1.nAddress, map2.Entries.Single(e => e.szSID == e1.szSID).nAddress);
        }
        // 自然排序：DB1-S9 < DB1-S10 < DB2-S1
        Assert.Equal(14000, map1.Entries.Single(e => e.szSID == "DB1-S9").nAddress);
        Assert.Equal(14002, map1.Entries.Single(e => e.szSID == "DB1-S10").nAddress);
        Assert.Equal(14004, map1.Entries.Single(e => e.szSID == "DB2-S1").nAddress);
    }
}

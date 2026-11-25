using Snug.Windows.Core;

namespace Snug.Windows.Core.Tests;

public sealed class LeftBehindBookTests
{
    private static WindowRestoreRecord Record(
        uint pid = 123, long handle = 101, string title = "Existing workspace",
        string path = @"C:\Tools\Code.exe", WindowBounds? filled = null) =>
        new(1, new WindowKey(pid, handle), "Code", path,
            new WindowPlacementData(0, 1, 0, 0, 0, 0, new WindowBounds(60, 80, 1000, 700)),
            2, DateTimeOffset.Parse("2026-11-16T12:00:00Z"), title,
            filled ?? new WindowBounds(1920, 0, 1080, 1750));

    private static WindowInfo Returning(
        uint pid = 123, long handle = 202, string title = "Existing workspace",
        string path = @"C:\Tools\Code.exe") =>
        new(new WindowKey(pid, handle), "Code", title, path, false, false);

    private static string PathForTest() =>
        Path.Combine(Path.GetTempPath(), "SnugLeftBehindTests", Guid.NewGuid().ToString("N"), "left-behind.json");

    [Fact]
    public void MissingFillDoesNotCreateDebtAndHistorySurvivesRestart()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            Assert.False(book.Add(Record() with { FilledBounds = null }, now));
            Assert.Empty(book.Entries);
            Assert.True(book.Add(Record(), now));
            Assert.True(File.Exists(path));
            var loaded = new LeftBehindBook(path);
            Assert.Single(loaded.Entries);
            Assert.Equal("Existing workspace", loaded.Entries[0].Record.Title);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void MatchRequiresSameExecutableAndActualSnugFilledSize()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            book.Add(Record(), now);
            Assert.Null(book.Match(Returning(path: @"C:\Other\Code.exe"), new WindowBounds(1920, 0, 1080, 1750), now));
            Assert.Null(book.Match(Returning(), new WindowBounds(1920, 0, 1190, 1750), now));
            Assert.Null(book.Match(Returning(), new WindowBounds(1920, 0, 1080, 1748), now));
            Assert.NotNull(book.Match(Returning(), new WindowBounds(2000, 50, 1081, 1751), now));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void SameWindowThenTitleThenNewestHasSamePriorityAsUpstream()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            book.Add(Record(123, 10, "Old workspace"), now.AddMinutes(-10));
            book.Add(Record(456, 20, "New workspace"), now.AddMinutes(-5));
            var filled = new WindowBounds(1920, 0, 1080, 1750);
            Assert.Equal(10L, book.Match(Returning(123, 10, "Else"), filled, now)!.Record.Key.Handle);
            Assert.Equal(20L, book.Match(Returning(456, 40, "New workspace"), filled, now)!.Record.Key.Handle);
            Assert.Equal(20L, book.Match(Returning(456, 40, "Unknown"), filled, now)!.Record.Key.Handle);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void ExpirationAndTwentyEntryLimitPreventStaleWindowRestoration()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            book.Add(Record(pid: 100, handle: 100), now.AddDays(-15));
            Assert.Null(book.Match(Returning(), new WindowBounds(1920, 0, 1080, 1750), now));
            for (var i = 0; i < 22; i++)
                book.Add(Record(pid: (uint)(200 + i), handle: 200 + i), now.AddMinutes(i));
            Assert.Equal(20, book.Entries.Count);
            Assert.DoesNotContain(book.Entries, entry => entry.Record.Key.Handle == 200);
            Assert.Contains(book.Entries, entry => entry.Record.Key.Handle == 221);
            book.Prune(now.AddDays(16));
            Assert.Empty(book.Entries);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void IdenticalWindowIdSurvivesResizeFullscreenOrMovedDimensions()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            var record = Record(pid: 123, handle: 444, title: "Original title") with
                { ProcessStartedUtcTicks = 112233L };
            Assert.True(book.Add(record, now));
            var window = Returning(pid: 123, handle: 444, title: "Changed title");
            Assert.Equal(record.Key, book.Match(window,
                new WindowBounds(-700, 20, 1200, 800), now,
                processStartedUtcTicks: 112233L)!.Record.Key);
            Assert.Null(book.Match(window, new WindowBounds(-700, 20, 1200, 800),
                now, processStartedUtcTicks: 998877L));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void RecreatedWindowStillNeedsFilledDimensionsAndExecutableMatch()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            book.Add(Record(pid: 123, handle: 444), now);
            Assert.Null(book.Match(Returning(pid: 123, handle: 445),
                new WindowBounds(-700, 20, 1200, 800), now));
            Assert.Null(book.Match(Returning(pid: 123, handle: 444,
                    path: @"C:\Other\Code.exe"),
                new WindowBounds(-700, 20, 1080, 1750), now));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void ConsumeRemovesOnlyTheMatchedDebt()
    {
        var path = PathForTest();
        var now = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        try
        {
            var book = new LeftBehindBook(path);
            book.Add(Record(123, 10, "A"), now);
            book.Add(Record(123, 20, "B"), now.AddSeconds(1));
            var matched = book.Match(Returning(123, 30, "B"), new WindowBounds(1920, 0, 1080, 1750), now.AddSeconds(2));
            Assert.NotNull(matched);
            book.Remove(matched!);
            Assert.Single(new LeftBehindBook(path).Entries);
            Assert.Equal("A", new LeftBehindBook(path).Entries[0].Record.Title);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
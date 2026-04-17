using System.IO;
using System.Text;
using System.Threading;
using FastEdit.Core.LargeFile;

namespace FastEdit.Tests;

public class LargeFileDocumentTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    private string CreateTempFile(byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTextFile(string content, Encoding encoding, bool includeBom)
    {
        // For UTF-8 specifically, `new UTF8Encoding(false)` returns an empty preamble —
        // so force the BOM-emitting variant when the caller asks for one.
        Encoding effective = encoding;
        if (includeBom && encoding is UTF8Encoding) effective = new UTF8Encoding(true);

        var path = Path.GetTempFileName();
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            if (includeBom)
            {
                var preamble = effective.GetPreamble();
                fs.Write(preamble, 0, preamble.Length);
            }
            var bytes = effective.GetBytes(content);
            fs.Write(bytes, 0, bytes.Length);
        }
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void FileSize_Returns_File_Length()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3, 4, 5 });
        using var doc = new LargeFileDocument(path);
        Assert.Equal(5, doc.FileSize);
    }

    [Fact]
    public void Detects_Utf8_Bom()
    {
        var path = CreateTextFile("hello\nworld", new UTF8Encoding(false), includeBom: true);
        using var doc = new LargeFileDocument(path);
        Assert.Equal(3, doc.BomLength);
        Assert.Contains("UTF-8", doc.EncodingDisplayName);
    }

    [Fact]
    public void Detects_Utf16_Le_Bom()
    {
        var path = CreateTextFile("hello\nworld", Encoding.Unicode, includeBom: true);
        using var doc = new LargeFileDocument(path);
        Assert.Equal(2, doc.BomLength);
        Assert.Equal("UTF-16 LE", doc.EncodingDisplayName);
    }

    [Fact]
    public void Detects_Utf16_Be_Bom()
    {
        var path = CreateTextFile("hello\nworld", Encoding.BigEndianUnicode, includeBom: true);
        using var doc = new LargeFileDocument(path);
        Assert.Equal(2, doc.BomLength);
        Assert.Equal("UTF-16 BE", doc.EncodingDisplayName);
    }

    [Fact]
    public void Detects_Utf8_Without_Bom_On_Ascii_Content()
    {
        var path = CreateTextFile("hello\nworld", new UTF8Encoding(false), includeBom: false);
        using var doc = new LargeFileDocument(path);
        Assert.Equal(0, doc.BomLength);
        Assert.Equal("UTF-8", doc.EncodingDisplayName);
    }

    [Fact]
    public async Task BuildIndex_Counts_Lines_Correctly()
    {
        var content = "line1\nline2\nline3\nline4\nline5";
        var path = CreateTextFile(content, new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);
        Assert.Equal(5, doc.TotalLines);
    }

    [Fact]
    public async Task BuildIndex_Handles_Trailing_Newline()
    {
        var content = "a\nb\nc\n";
        var path = CreateTextFile(content, new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);
        // 3 complete lines + 1 trailing empty line = 4
        Assert.Equal(4, doc.TotalLines);
    }

    [Fact]
    public async Task BuildIndex_Handles_CRLF()
    {
        var content = "alpha\r\nbeta\r\ngamma";
        var path = CreateTextFile(content, new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);
        Assert.Equal(3, doc.TotalLines);
    }

    [Fact]
    public async Task GetLine_Returns_Correct_Content()
    {
        var path = CreateTextFile("first line\nsecond line\nthird line", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        Assert.Equal("first line", doc.GetLine(1));
        Assert.Equal("second line", doc.GetLine(2));
        Assert.Equal("third line", doc.GetLine(3));
    }

    [Fact]
    public async Task GetLine_Strips_CR_From_CRLF()
    {
        var path = CreateTextFile("one\r\ntwo\r\nthree", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        Assert.Equal("one", doc.GetLine(1));
        Assert.Equal("two", doc.GetLine(2));
        Assert.Equal("three", doc.GetLine(3));
    }

    [Fact]
    public async Task GetLine_Handles_UTF8_Multibyte()
    {
        var path = CreateTextFile("café\nnaïve\n日本語", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        Assert.Equal("café", doc.GetLine(1));
        Assert.Equal("naïve", doc.GetLine(2));
        Assert.Equal("日本語", doc.GetLine(3));
    }

    [Fact]
    public async Task GetLine_Handles_UTF16_LE()
    {
        var path = CreateTextFile("hello\nworld\nfoo", Encoding.Unicode, includeBom: true);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        Assert.Equal(3, doc.TotalLines);
        Assert.Equal("hello", doc.GetLine(1));
        Assert.Equal("world", doc.GetLine(2));
        Assert.Equal("foo", doc.GetLine(3));
    }

    [Fact]
    public async Task GetLine_Returns_Empty_For_Invalid_Line()
    {
        var path = CreateTextFile("only\nline", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        Assert.Equal(string.Empty, doc.GetLine(0));
        Assert.Equal(string.Empty, doc.GetLine(100));
        Assert.Equal(string.Empty, doc.GetLine(-1));
    }

    [Fact]
    public async Task GetLine_Truncates_Extremely_Long_Lines()
    {
        var longLine = new string('x', 50_000);
        var path = CreateTextFile($"short\n{longLine}\nshort", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var line2 = doc.GetLine(2);
        Assert.True(line2.Length < longLine.Length);
        Assert.Contains("truncated", line2);
    }

    [Fact]
    public async Task Search_Finds_All_Matches_Case_Insensitive()
    {
        var content = "Alpha\nBRAVO\ncharlie Alpha\nalpha delta\nEnd";
        var path = CreateTextFile(content, new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var results = await doc.SearchAsync("alpha", caseSensitive: false, maxResults: 100, null, CancellationToken.None);
        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].LineNumber);
        Assert.Equal(3, results[1].LineNumber);
        Assert.Equal(4, results[2].LineNumber);
    }

    [Fact]
    public async Task Search_Case_Sensitive_Matches_Only_Exact_Case()
    {
        var content = "Alpha\nalpha\nALPHA";
        var path = CreateTextFile(content, new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var results = await doc.SearchAsync("alpha", caseSensitive: true, maxResults: 100, null, CancellationToken.None);
        Assert.Single(results);
        Assert.Equal(2, results[0].LineNumber);
    }

    [Fact]
    public async Task Search_Honors_MaxResults()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++) sb.AppendLine("needle");
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var results = await doc.SearchAsync("needle", false, maxResults: 10, null, CancellationToken.None);
        Assert.Equal(10, results.Count);
    }

    [Fact]
    public async Task Search_Returns_Empty_For_Empty_Needle()
    {
        var path = CreateTextFile("whatever", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var results = await doc.SearchAsync("", false, 100, null, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Empty_File_Has_Zero_Lines()
    {
        var path = CreateTempFile(Array.Empty<byte>());
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);
        Assert.Equal(0, doc.TotalLines);
    }

    [Fact]
    public async Task Single_Line_No_Newline_Counts_As_One()
    {
        var path = CreateTextFile("solo", new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);
        Assert.Equal(1, doc.TotalLines);
        Assert.Equal("solo", doc.GetLine(1));
    }

    [Fact]
    public async Task Progress_Is_Reported_And_Completes_At_One()
    {
        // Need a file larger than the progress interval (8MB) to see intermediate reports.
        var sb = new StringBuilder();
        string line = new string('a', 100);
        for (int i = 0; i < 100_000; i++) sb.AppendLine(line); // ~10MB
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);

        var reports = new List<double>();
        var progress = new Progress<double>(v => reports.Add(v));
        await doc.BuildIndexAsync(progress, CancellationToken.None);
        // Give Progress<T> a moment to flush any queued reports.
        await Task.Delay(50);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports.Last(), precision: 3);
    }

    [Fact]
    public async Task BuildIndex_Can_Be_Cancelled()
    {
        // 50MB of data so cancellation has time to fire.
        var sb = new StringBuilder();
        for (int i = 0; i < 500_000; i++) sb.AppendLine("some line content here");
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(1); // cancel almost immediately
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await doc.BuildIndexAsync(null, cts.Token));
    }

    [Fact]
    public async Task GetLine_Works_For_Many_Lines_Across_Checkpoints()
    {
        // Exceed the line-checkpoint interval (512) to exercise checkpoint jumps.
        var sb = new StringBuilder();
        for (int i = 1; i <= 2000; i++) sb.Append($"line-{i}\n");
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        Assert.Equal(2001, doc.TotalLines); // trailing \n adds an empty line
        Assert.Equal("line-1", doc.GetLine(1));
        Assert.Equal("line-500", doc.GetLine(500));
        Assert.Equal("line-1024", doc.GetLine(1024));
        Assert.Equal("line-1999", doc.GetLine(1999));
        Assert.Equal("line-2000", doc.GetLine(2000));
    }

    [Fact]
    public async Task Search_Finds_Matches_At_Buffer_Boundaries()
    {
        // Build content large enough to cross the 4MB internal scan buffer at least once,
        // with a match straddling the boundary region.
        var sb = new StringBuilder();
        string filler = new string('f', 4095);
        // ~5MB of filler lines
        for (int i = 0; i < 1250; i++) sb.Append(filler).Append('\n');
        sb.Append("boundary-needle\n");
        sb.Append(filler).Append('\n');
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var results = await doc.SearchAsync("boundary-needle", false, 10, null, CancellationToken.None);
        Assert.Single(results);
        Assert.Equal(1251, results[0].LineNumber);
    }

    /// <summary>
    /// Stress/smoke test: generate a ~200MB file and verify index, random line reads,
    /// and searches complete in a sane amount of time. Skipped by default to keep CI fast.
    /// Enable by setting the env var FASTEDIT_RUN_LARGE_FILE_TEST=1.
    /// </summary>
    [Fact]
    public async Task LargeFile_Stress_200MB_SmokeTest()
    {
        if (Environment.GetEnvironmentVariable("FASTEDIT_RUN_LARGE_FILE_TEST") != "1")
            return; // opt-in

        var path = Path.GetTempFileName();
        _tempFiles.Add(path);

        const long targetBytes = 200L * 1024 * 1024;
        const int linesPerBatch = 10_000;
        long written = 0;
        long lineNo = 0;
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            while (written < targetBytes)
            {
                for (int i = 0; i < linesPerBatch; i++)
                {
                    lineNo++;
                    // One "needle-marker" every 100k lines
                    if (lineNo % 100_000 == 0)
                        sw.WriteLine($"line {lineNo} needle-marker payload");
                    else
                        sw.WriteLine($"line {lineNo} padding padding padding padding padding padding");
                }
                sw.Flush();
                written = fs.Position;
            }
        }

        using var doc = new LargeFileDocument(path);
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        await doc.BuildIndexAsync(null, CancellationToken.None);
        sw1.Stop();

        Assert.True(doc.TotalLines > 1_000_000, $"expected >1M lines, got {doc.TotalLines}");

        // Random line reads should be fast.
        var rnd = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            long ln = 1 + (long)(rnd.NextDouble() * (doc.TotalLines - 1));
            var text = doc.GetLine(ln);
            Assert.StartsWith($"line {ln} ", text);
        }

        // Search should find the sparse markers.
        var results = await doc.SearchAsync("needle-marker", false, 1000, null, CancellationToken.None);
        Assert.True(results.Count >= 10, $"expected >=10 markers, got {results.Count}");

        // Sanity: index build well under 60s on any modern dev machine.
        Assert.True(sw1.Elapsed.TotalSeconds < 60, $"index build too slow: {sw1.Elapsed}");
    }


    [Fact]
    public async Task FindMatchingLines_Streams_And_Returns_Correct_Lines()
    {
        var content = "apple\nbanana\ncherry apple\ndate\nApple pie";
        var path = CreateTextFile(content, new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var lines = await doc.FindMatchingLinesAsync(
            l => l.IndexOf("apple", StringComparison.OrdinalIgnoreCase) >= 0,
            maxResults: 100, null, CancellationToken.None);

        Assert.Equal(new long[] { 1, 3, 5 }, lines);
    }

    [Fact]
    public async Task FindMatchingLines_Handles_Line_Across_4MB_Buffer_Boundary()
    {
        var sb = new StringBuilder();
        string filler = new string('f', 4095);
        for (int i = 0; i < 1250; i++) sb.Append(filler).Append('\n');
        sb.Append("BOUNDARY\n");
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var lines = await doc.FindMatchingLinesAsync(
            l => l == "BOUNDARY", 100, null, CancellationToken.None);
        Assert.Single(lines);
        Assert.Equal(1251, lines[0]);
    }

    [Fact]
    public async Task FindMatchingLines_Is_Fast_On_20MB_File()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= 200_000; i++) sb.Append($"line-{i} content padding padding padding\n");
        var path = CreateTextFile(sb.ToString(), new UTF8Encoding(false), false);
        using var doc = new LargeFileDocument(path);
        await doc.BuildIndexAsync(null, CancellationToken.None);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var matches = await doc.FindMatchingLinesAsync(
            l => l.Contains("line-100000"), int.MaxValue, null, CancellationToken.None);
        sw.Stop();
        Assert.Single(matches);
        Assert.True(sw.Elapsed.TotalSeconds < 3, $"bulk scan too slow: {sw.Elapsed.TotalSeconds:0.0}s");
    }
}
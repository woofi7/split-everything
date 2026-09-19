using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace SplitEverything.Infrastructure.Import;

public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, string Delimiter);

public static class SettleUpCsvReader
{
    private static readonly string[] CandidateDelimiters = [",", ";", "\t", "|"];

    public static CsvTable Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return Parse(text);
    }

    public static CsvTable Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CsvTable([], [], ",");

        var delimiter = SniffDelimiter(text);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            DetectDelimiter = false
        };

        using var stringReader = new StringReader(text);
        using var csv = new CsvReader(stringReader, config);

        var rows = new List<string[]>();
        while (csv.Read())
        {
            var row = new string[csv.Parser.Count];
            for (var i = 0; i < csv.Parser.Count; i++) row[i] = csv.Parser[i] ?? string.Empty;
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(row);
        }

        if (rows.Count == 0) return new CsvTable([], [], delimiter);

        var headers = rows[0].Select(h => h.Trim()).ToList();
        return new CsvTable(headers, rows.Skip(1).ToList(), delimiter);
    }

    private static string SniffDelimiter(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(5).ToList();
        if (lines.Count == 0) return ",";

        var best = ",";
        var bestScore = -1;

        foreach (var candidate in CandidateDelimiters)
        {
            var counts = lines.Select(line => line.Split(candidate).Length).ToList();
            var columns = counts[0];
            if (columns < 2) continue;

            var consistent = counts.Count(c => c == columns);
            var score = consistent * 100 + columns;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}

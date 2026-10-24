using System.Globalization;
using System.Text.RegularExpressions;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Разбор упрощённого DAX: SUM/AVERAGE/…, [Мера], CALCULATE(..., KEEPFILTERS(...)).</summary>
public static partial class MeasureParser
{
    public static MeasureExpr Parse(string expression)
    {
        var text = Normalize(expression);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Пустое выражение меры.");
        var (expr, rest) = ParseExpr(text, 0);
        SkipWs(rest, out var i);
        if (i < rest.Length)
            throw new InvalidOperationException($"Лишний текст в выражении: «{rest[i..]}».");
        return expr;
    }

    private static string Normalize(string expression) =>
        expression.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    private static (MeasureExpr Expr, string Remaining) ParseExpr(string text, int start)
    {
        SkipWs(text.AsSpan(start), out var offset);
        var i = start + offset;
        if (i >= text.Length)
            throw new InvalidOperationException("Ожидалось выражение.");

        if (text[i] == '[')
            return ParseMeasureRef(text, i);

        if (StartsWithIgnoreCase(text, i, "CALCULATE"))
            return ParseCalculate(text, i);

        foreach (var (name, agg) in Aggregates)
        {
            if (StartsWithIgnoreCase(text, i, name))
                return ParseAggregate(text, i, name.Length, agg);
        }

        throw new InvalidOperationException(
            "Ожидалось SUM(...), [Мера] или CALCULATE(..., KEEPFILTERS(...)).");
    }

    private static readonly (string Name, Aggregation Agg)[] Aggregates =
    [
        ("SUM", Aggregation.Sum),
        ("AVERAGE", Aggregation.Average),
        ("AVG", Aggregation.Average),
        ("COUNT", Aggregation.Count),
        ("DISTINCTCOUNT", Aggregation.DistinctCount),
        ("MIN", Aggregation.Min),
        ("MAX", Aggregation.Max),
    ];

    private static (MeasureExpr Expr, string Remaining) ParseMeasureRef(string text, int i)
    {
        var close = text.IndexOf(']', i + 1);
        if (close < 0)
            throw new InvalidOperationException("Не закрыта ссылка на меру [...]");
        var name = text[(i + 1)..close].Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Пустое имя меры.");
        return (new MeasureRefExpr { MeasureName = name }, text[(close + 1)..]);
    }

    private static (MeasureExpr Expr, string Remaining) ParseAggregate(string text, int i, int nameLen, Aggregation agg)
    {
        var afterName = i + nameLen;
        SkipWs(text.AsSpan(afterName), out var ws);
        var open = afterName + ws;
        if (open >= text.Length || text[open] != '(')
            throw new InvalidOperationException($"После {agg} ожидалась «(».");
        var (table, column, rest) = ParseColumnRef(text, open + 1);
        SkipWs(rest.AsSpan(), out var ws2);
        if (ws2 >= rest.Length || rest[ws2] != ')')
            throw new InvalidOperationException("Ожидалась «)» после столбца.");
        MeasureExpr expr = agg == Aggregation.Sum
            ? new SumColumnExpr { Table = table, Column = column }
            : new AggregateColumnExpr { Aggregation = agg, Table = table, Column = column };
        return (expr, rest[(ws2 + 1)..]);
    }

    private static (MeasureExpr Expr, string Remaining) ParseCalculate(string text, int i)
    {
        var after = i + "CALCULATE".Length;
        SkipWs(text.AsSpan(after), out var ws);
        var open = after + ws;
        if (open >= text.Length || text[open] != '(')
            throw new InvalidOperationException("После CALCULATE ожидалась «(».");

        var (inner, rest) = ParseExpr(text, open + 1);
        SkipWs(rest.AsSpan(), out var ws2);
        rest = rest[ws2..];
        var keep = new List<KeepFiltersClause>();
        while (rest.StartsWith(','))
        {
            rest = rest[1..];
            SkipWs(rest.AsSpan(), out var ws3);
            rest = rest[ws3..];
            if (!StartsWithIgnoreCase(rest, 0, "KEEPFILTERS"))
                throw new InvalidOperationException("В CALCULATE поддерживается только KEEPFILTERS(...).");
            var afterKeep = "KEEPFILTERS".Length;
            SkipWs(rest.AsSpan(afterKeep), out var ws4);
            var kOpen = afterKeep + ws4;
            if (kOpen >= rest.Length || rest[kOpen] != '(')
                throw new InvalidOperationException("После KEEPFILTERS ожидалась «(».");
            var (clause, afterClause) = ParseKeepFiltersBody(rest, kOpen + 1);
            keep.Add(clause);
            SkipWs(afterClause.AsSpan(), out var ws5);
            afterClause = afterClause[ws5..];
            if (afterClause.Length == 0 || afterClause[0] != ')')
                throw new InvalidOperationException("Ожидалась «)» после KEEPFILTERS.");
            rest = afterClause[1..];
            SkipWs(rest.AsSpan(), out var ws6);
            rest = rest[ws6..];
        }

        if (rest.Length == 0 || rest[0] != ')')
            throw new InvalidOperationException("Ожидалась «)» после CALCULATE.");
        return (new CalculateExpr { Inner = inner, KeepFilters = keep }, rest[1..]);
    }

    private static (KeepFiltersClause Clause, string Remaining) ParseKeepFiltersBody(string text, int start)
    {
        var (table, column, rest) = ParseColumnRef(text, start);
        SkipWs(rest.AsSpan(), out var ws);
        rest = rest[ws..];
        if (rest.Length == 0 || rest[0] != '=')
            throw new InvalidOperationException("В KEEPFILTERS ожидалось «=».");
        rest = rest[1..];
        SkipWs(rest.AsSpan(), out var ws2);
        rest = rest[ws2..];
        var (value, afterValue) = ParseLiteral(rest);
        if (string.IsNullOrEmpty(table))
            throw new InvalidOperationException("В KEEPFILTERS укажите таблицу: Таблица[Столбец] = \"значение\".");
        return (new KeepFiltersClause { Table = table, Column = column, Value = value }, afterValue);
    }

    private static (string? Table, string Column, string Remaining) ParseColumnRef(string text, int start)
    {
        SkipWs(text.AsSpan(start), out var ws);
        var i = start + ws;
        if (i >= text.Length)
            throw new InvalidOperationException("Ожидалась ссылка на столбец.");

        // Table[Column] or [Column] or Column
        var fieldMatch = FieldRefRegex().Match(text[i..]);
        if (fieldMatch.Success)
        {
            var table = fieldMatch.Groups["table"].Success ? fieldMatch.Groups["table"].Value : null;
            var column = fieldMatch.Groups["column"].Value;
            return (table, column, text[(i + fieldMatch.Length)..]);
        }

        var bare = BareColumnRegex().Match(text[i..]);
        if (bare.Success)
            return (null, bare.Groups[1].Value, text[(i + bare.Length)..]);

        throw new InvalidOperationException("Ожидалось Table[Column], [Column] или имя столбца.");
    }

    private static (string Value, string Remaining) ParseLiteral(string text)
    {
        if (text.Length == 0)
            throw new InvalidOperationException("Ожидалось значение.");
        if (text[0] is '"' or '\'')
        {
            var quote = text[0];
            var end = 1;
            while (end < text.Length && text[end] != quote)
                end++;
            if (end >= text.Length)
                throw new InvalidOperationException("Не закрыта строка в кавычках.");
            return (text[1..end], text[(end + 1)..]);
        }

        var m = NumberOrIdentRegex().Match(text);
        if (!m.Success)
            throw new InvalidOperationException("Ожидалось значение в кавычках или число.");
        return (m.Value, text[m.Length..]);
    }

    private static bool StartsWithIgnoreCase(string text, int index, string value) =>
        index + value.Length <= text.Length
        && text.AsSpan(index, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);

    private static void SkipWs(ReadOnlySpan<char> span, out int count)
    {
        count = 0;
        while (count < span.Length && char.IsWhiteSpace(span[count]))
            count++;
    }

    [GeneratedRegex(@"^(?<table>[^\s\[\],()]+)?\[(?<column>[^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex FieldRefRegex();

    [GeneratedRegex(@"^\[(?<column>[^\]]+)\]|^(?<column>[A-Za-z_][\w]*)", RegexOptions.CultureInvariant)]
    private static partial Regex BareColumnRegex();

    [GeneratedRegex(@"^[\w.+-]+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberOrIdentRegex();
}

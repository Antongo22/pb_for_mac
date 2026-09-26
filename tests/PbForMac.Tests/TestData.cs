using System.Data;

namespace PbForMac.Tests;

internal static class TestData
{
    /// <summary>Небольшая таблица продаж для тестов движков.</summary>
    public static DataTable Sales()
    {
        var table = new DataTable("Продажи");
        table.Columns.Add("Дата", typeof(DateTime));
        table.Columns.Add("Регион", typeof(string));
        table.Columns.Add("Кол-во", typeof(long));
        table.Columns.Add("Цена", typeof(double));
        table.Rows.Add(new DateTime(2024, 1, 5), "Север", 2L, 100d);
        table.Rows.Add(new DateTime(2024, 1, 20), "Юг", 1L, 50d);
        table.Rows.Add(new DateTime(2024, 2, 3), "Север", 3L, 10d);
        table.Rows.Add(new DateTime(2024, 4, 1), "Запад", 5L, 20d);
        table.Rows.Add(new DateTime(2024, 4, 1), "Запад", 5L, 20d);
        table.Rows.Add(DBNull.Value, "Юг", DBNull.Value, 70d);
        return table;
    }
}

using System.Data;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Tests;

public class TypeInferenceTests
{
    [Theory]
    [InlineData(ColumnType.Integer, "1", "42", "-7", "")]
    [InlineData(ColumnType.Decimal, "1.5", "2", "3,25")]
    [InlineData(ColumnType.Decimal, "1 234,56", "10")]
    [InlineData(ColumnType.Date, "2024-01-31", "2024-02-01")]
    [InlineData(ColumnType.Date, "31.01.2024", "01.02.2024")]
    [InlineData(ColumnType.Boolean, "true", "False", "да")]
    [InlineData(ColumnType.Text, "007", "008")]
    [InlineData(ColumnType.Text, "1", "abc")]
    [InlineData(ColumnType.Text, "", " ")]
    public void Detect_RecognizesStringValues(ColumnType expected, params string[] values)
    {
        Assert.Equal(expected, TypeInference.Detect(values));
    }

    [Fact]
    public void Detect_UsesNativeValues()
    {
        Assert.Equal(ColumnType.Integer, TypeInference.Detect([1L, 2d, null]));
        Assert.Equal(ColumnType.Decimal, TypeInference.Detect([1L, 2.5d]));
        Assert.Equal(ColumnType.Date, TypeInference.Detect([new DateTime(2024, 1, 1)]));
    }

    [Fact]
    public void Apply_ConvertsColumnsAndEmptyValuesToNull()
    {
        var raw = new DataTable("t");
        raw.Columns.Add("Qty", typeof(object));
        raw.Columns.Add("Price", typeof(object));
        raw.Columns.Add("Name", typeof(object));
        raw.Rows.Add("3", "9.99", "A");
        raw.Rows.Add("", "1,5", "B");

        var table = TypeInference.Apply(raw);

        Assert.Equal(typeof(long), table.Columns["Qty"]!.DataType);
        Assert.Equal(typeof(double), table.Columns["Price"]!.DataType);
        Assert.Equal(typeof(string), table.Columns["Name"]!.DataType);
        Assert.Equal(3L, table.Rows[0]["Qty"]);
        Assert.Equal(DBNull.Value, table.Rows[1]["Qty"]);
        Assert.Equal(1.5, table.Rows[1]["Price"]);
    }
}

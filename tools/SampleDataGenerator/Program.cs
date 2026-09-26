// Генерирует демонстрационные данные в папку samples/ (CSV, Excel, JSON, XML, SQLite).
// Запуск: dotnet run --project tools/SampleDataGenerator [-- <папка>]
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

var output = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "samples");
Directory.CreateDirectory(output);
var random = new Random(42);
var ru = CultureInfo.GetCultureInfo("ru-RU");

var regions = new (string Name, string Code, string Capital, int Population, int Area, double Weight)[]
{
    ("Центр", "CFD", "Москва", 40_300_000, 650_200, 0.34),
    ("Северо-Запад", "NWFD", "Санкт-Петербург", 13_900_000, 1_687_000, 0.14),
    ("Юг", "SFD", "Ростов-на-Дону", 16_600_000, 447_800, 0.12),
    ("Приволжье", "VFD", "Нижний Новгород", 28_900_000, 1_037_000, 0.17),
    ("Урал", "UFD", "Екатеринбург", 12_300_000, 1_818_500, 0.10),
    ("Сибирь", "SibFD", "Новосибирск", 16_800_000, 4_361_800, 0.09),
    ("Дальний Восток", "FEFD", "Владивосток", 7_900_000, 6_952_600, 0.04),
};
var cities = new Dictionary<string, string[]>
{
    ["Центр"] = ["Москва", "Тула", "Воронеж", "Ярославль"],
    ["Северо-Запад"] = ["Санкт-Петербург", "Калининград", "Мурманск"],
    ["Юг"] = ["Ростов-на-Дону", "Краснодар", "Волгоград"],
    ["Приволжье"] = ["Нижний Новгород", "Казань", "Самара", "Уфа"],
    ["Урал"] = ["Екатеринбург", "Челябинск", "Тюмень"],
    ["Сибирь"] = ["Новосибирск", "Красноярск", "Омск"],
    ["Дальний Восток"] = ["Владивосток", "Хабаровск"],
};
var managers = regions
    .SelectMany((r, i) => new[] { (Region: r.Name, Name: Surnames[i * 2] + " А."), (Region: r.Name, Name: Surnames[i * 2 + 1] + " С.") })
    .ToArray();

var products = new (string Id, string Name, string Category, double Price, string Supplier, string Country, double Rating)[]
{
    ("P001", "Ноутбук Air 13", "Ноутбуки", 89_990, "TechLine", "Китай", 4.6),
    ("P002", "Ноутбук Pro 16", "Ноутбуки", 184_990, "TechLine", "Китай", 4.8),
    ("P003", "Ультрабук Slim", "Ноутбуки", 119_990, "Nordic PC", "Тайвань", 4.4),
    ("P004", "Смартфон S23", "Смартфоны", 64_990, "MobiTrade", "Корея", 4.7),
    ("P005", "Смартфон Lite", "Смартфоны", 18_990, "MobiTrade", "Китай", 4.1),
    ("P006", "Смартфон Max", "Смартфоны", 109_990, "MobiTrade", "Китай", 4.5),
    ("P007", "Наушники Buds", "Аксессуары", 7_990, "SoundPro", "Китай", 4.2),
    ("P008", "Наушники Studio", "Аксессуары", 24_990, "SoundPro", "Германия", 4.7),
    ("P009", "Чехол кожаный", "Аксессуары", 1_990, "CaseHouse", "Россия", 3.9),
    ("P010", "Зарядка 65 Вт", "Аксессуары", 3_490, "PowerOne", "Китай", 4.3),
    ("P011", "Монитор 27\" 4K", "Мониторы", 42_990, "ViewTech", "Корея", 4.6),
    ("P012", "Монитор 24\" FHD", "Мониторы", 14_990, "ViewTech", "Китай", 4.2),
    ("P013", "Планшет Tab 11", "Планшеты", 39_990, "MobiTrade", "Китай", 4.4),
    ("P014", "Планшет Mini", "Планшеты", 27_990, "MobiTrade", "Китай", 4.0),
    ("P015", "Умные часы Fit", "Гаджеты", 15_990, "WearIt", "Китай", 4.1),
    ("P016", "Электронная книга", "Гаджеты", 12_490, "ReadMore", "Россия", 4.5),
};
var productWeights = products.Select(p => 1.0 / Math.Sqrt(p.Price / 1000)).ToArray();

// --- sales.csv: русский формат (разделитель «;», десятичная запятая) ---
var start = new DateTime(2024, 1, 1);
var days = (new DateTime(2025, 6, 30) - start).Days + 1;
var csv = new StringBuilder("Дата;Регион;Город;Менеджер;Код товара;Товар;Категория;Количество;Цена;Скидка\n");
for (var i = 0; i < 2400; i++)
{
    var date = start.AddDays(random.Next(days));
    var region = Pick(regions, regions.Select(r => r.Weight * (1 + 0.25 * Math.Sin((date.Month - 3) / 12.0 * Math.PI * 2))).ToArray());
    var city = cities[region.Name][random.Next(cities[region.Name].Length)];
    var manager = managers.Where(m => m.Region == region.Name).ElementAt(random.Next(2)).Name;
    var product = Pick(products, productWeights);
    var quantity = 1 + (int)Math.Floor(-Math.Log(1 - random.NextDouble()) * (product.Price < 10_000 ? 4 : 1.5));
    var growth = 1 + (date - start).Days / 365.0 * 0.08;
    var price = Math.Round(product.Price * growth * (0.95 + random.NextDouble() * 0.1), 2);
    var discount = random.NextDouble() < 0.3 ? Math.Round(random.Next(1, 4) * 0.05, 2) : 0;
    csv.Append(string.Join(';', date.ToString("dd.MM.yyyy"), region.Name, city, manager, product.Id,
        Quote(product.Name), product.Category, quantity, price.ToString("0.00", ru), discount.ToString("0.00", ru))).Append('\n');
}
File.WriteAllText(Path.Combine(output, "sales.csv"), csv.ToString(), new UTF8Encoding(true));

// --- products.json: вложенные объекты и массивы ---
var json = new
{
    catalog = new
    {
        updated = "2025-06-30",
        currency = "RUB",
        products = products.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            category = p.Category,
            price = p.Price,
            rating = p.Rating,
            reviews = (int)(p.Rating * 120 + random.Next(400)),
            supplier = new { name = p.Supplier, country = p.Country },
            tags = p.Price > 50_000 ? new[] { "премиум", p.Category.ToLowerInvariant() } : [p.Category.ToLowerInvariant()],
        }),
    },
};
File.WriteAllText(Path.Combine(output, "products.json"),
    JsonSerializer.Serialize(json, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }));

// --- regions.xml: атрибуты + дочерние элементы ---
new XDocument(
    new XDeclaration("1.0", "utf-8", null),
    new XElement("data",
        new XElement("source", "Справочник федеральных округов (демо)"),
        new XElement("regions", regions.Select(r =>
            new XElement("region",
                new XAttribute("code", r.Code),
                new XElement("name", r.Name),
                new XElement("capital", r.Capital),
                new XElement("population", r.Population),
                new XElement("area", r.Area)))))).Save(Path.Combine(output, "regions.xml"));

// --- sales.xlsx: два листа ---
using (var workbook = new XLWorkbook())
{
    var plan = workbook.AddWorksheet("Планы");
    plan.Cell(1, 1).Value = "Регион";
    plan.Cell(1, 2).Value = "Месяц";
    plan.Cell(1, 3).Value = "План выручки";
    var row = 2;
    foreach (var region in regions)
    {
        for (var month = new DateTime(2024, 1, 1); month <= new DateTime(2025, 6, 1); month = month.AddMonths(1))
        {
            plan.Cell(row, 1).Value = region.Name;
            plan.Cell(row, 2).Value = month;
            plan.Cell(row, 2).Style.DateFormat.Format = "mmm yyyy";
            plan.Cell(row, 3).Value = Math.Round(region.Weight * 14_000 * (0.9 + random.NextDouble() * 0.2)) * 1000;
            row++;
        }
    }
    plan.Columns().AdjustToContents();

    var team = workbook.AddWorksheet("Менеджеры");
    team.Cell(1, 1).Value = "Менеджер";
    team.Cell(1, 2).Value = "Регион";
    team.Cell(1, 3).Value = "Дата найма";
    team.Cell(1, 4).Value = "Оклад";
    for (var i = 0; i < managers.Length; i++)
    {
        team.Cell(i + 2, 1).Value = managers[i].Name;
        team.Cell(i + 2, 2).Value = managers[i].Region;
        team.Cell(i + 2, 3).Value = new DateTime(2016, 1, 1).AddDays(random.Next(3000));
        team.Cell(i + 2, 4).Value = 60_000 + random.Next(40) * 1000;
    }
    team.Columns().AdjustToContents();
    workbook.SaveAs(Path.Combine(output, "sales.xlsx"));
}

// --- shop.db: интернет-магазин ---
var dbPath = Path.Combine(output, "shop.db");
File.Delete(dbPath);
using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
{
    connection.Open();
    Execute(connection, """
        CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, city TEXT, segment TEXT, registered TEXT);
        CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER REFERENCES customers(id), order_date TEXT,
                             channel TEXT, status TEXT, total REAL);
        """);
    using var transaction = connection.BeginTransaction();
    var segments = new[] { "Розница", "Розница", "Малый бизнес", "Корпоративный" };
    var allCities = cities.Values.SelectMany(c => c).ToArray();
    for (var id = 1; id <= 300; id++)
    {
        Execute(connection, "INSERT INTO customers VALUES ($id, $name, $city, $segment, $registered)",
            ("$id", id), ("$name", $"Клиент {id:000}"), ("$city", allCities[random.Next(allCities.Length)]),
            ("$segment", segments[random.Next(segments.Length)]),
            ("$registered", new DateTime(2021, 1, 1).AddDays(random.Next(1400)).ToString("yyyy-MM-dd")));
    }
    var channels = new[] { "Сайт", "Сайт", "Приложение", "Маркетплейс" };
    var statuses = new[] { "Доставлен", "Доставлен", "Доставлен", "Доставлен", "В пути", "Отменён" };
    for (var id = 1; id <= 1500; id++)
    {
        Execute(connection, "INSERT INTO orders VALUES ($id, $customer, $date, $channel, $status, $total)",
            ("$id", id), ("$customer", random.Next(1, 301)),
            ("$date", start.AddDays(random.Next(days)).ToString("yyyy-MM-dd")),
            ("$channel", channels[random.Next(channels.Length)]), ("$status", statuses[random.Next(statuses.Length)]),
            ("$total", Math.Round(500 + Math.Exp(random.NextDouble() * 5) * 900, 2)));
    }
    transaction.Commit();
    Execute(connection, """
        CREATE VIEW orders_by_customer AS
        SELECT c.name, c.city, c.segment, COUNT(o.id) AS orders, ROUND(SUM(o.total), 2) AS total
        FROM customers c LEFT JOIN orders o ON o.customer_id = c.id GROUP BY c.id;
        """);
}

Console.WriteLine($"Демо-данные записаны в {output}");
return;

T Pick<T>(IReadOnlyList<T> items, double[] weights)
{
    var roll = random.NextDouble() * weights.Sum();
    for (var i = 0; i < items.Count; i++)
    {
        roll -= weights[i];
        if (roll <= 0)
            return items[i];
    }
    return items[^1];
}

static string Quote(string value) => value.Contains('"') || value.Contains(';') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
        command.Parameters.AddWithValue(name, value);
    command.ExecuteNonQuery();
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "PbForMac.sln")))
            return dir.FullName;
    }
    return Directory.GetCurrentDirectory();
}

internal partial class Program
{
    private static readonly string[] Surnames =
    [
        "Смирнова", "Кузнецов", "Попова", "Васильев", "Соколова", "Михайлов", "Новикова",
        "Фёдоров", "Морозова", "Волков", "Алексеева", "Лебедев", "Семёнова", "Егоров",
    ];
}

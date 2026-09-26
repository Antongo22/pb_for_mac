<p align="center">
  <img src="src/PbForMac/Assets/app.png" width="96" alt="PbForMac">
</p>

<h1 align="center">PbForMac</h1>

<p align="center">
  Простой настольный аналог Power BI для macOS (а также Windows и Linux) на C# и Avalonia UI.<br>
  <em>A lightweight Power BI–style data analysis app built with .NET 10 and Avalonia.</em>
</p>

<p align="center">
  <a href="https://github.com/Antongo22/pb_for_mac/actions/workflows/build.yml"><img src="https://github.com/Antongo22/pb_for_mac/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/Avalonia-11.3-8B44AC" alt="Avalonia 11.3">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT"></a>
</p>

![Дашборд демо-отчёта](docs/report.png)

## Возможности

- **Получение данных** из CSV/TSV, Excel (`.xlsx`), JSON, XML и SQLite. Файлы можно выбрать через диалог или просто перетащить в окно.
  - CSV: автоопределение разделителя (`,` `;` `\t` `|`) и кодировки (UTF-8 / Windows-1251), русский формат чисел `1 234,56`.
  - Excel и SQLite: выбор листов / таблиц и представлений, несколько сразу.
  - JSON: массив объектов на любом уровне вложенности, вложенные объекты разворачиваются в столбцы `supplier.country`.
  - XML: строки находятся автоматически (повторяющиеся элементы), атрибуты и дочерние элементы становятся столбцами.
  - Типы столбцов (целое, десятичное, дата, логический, текст) определяются автоматически.
- **Данные** — просмотр таблиц с виртуализацией, сортировка по клику на заголовок, поиск по всем столбцам, быстрые фильтры, профиль столбца (пустые, уникальные, мин/макс, сумма, среднее, частое значение), экспорт в CSV.
- **Модель** — преобразования в стиле Power Query с журналом «Применённые шаги»:
  переименование, удаление и смена типа столбцов, вычисляемые столбцы, фильтр строк, удаление дубликатов,
  группировка с агрегатами в новую таблицу. Шаги сохраняются в отчёте и повторяются при обновлении данных.
- **Отчёт** — дашборд из плиток, которые можно перетаскивать и растягивать:
  гистограмма, линейчатая, график, диаграмма с областями, круговая, точечная, KPI-карточка, таблица (сводная) и срез.
  Агрегации: сумма, среднее, количество, количество уникальных, минимум, максимум; детализация дат (день/месяц/квартал/год), Топ-N.
- **Фильтры страницы и срезы** перекрёстно фильтруют все визуалы соответствующей таблицы.
- **Отчёты `.pbm`** (JSON) хранят источники, шаги, визуалы и фильтры; пути к данным сохраняются относительно отчёта, поэтому папку можно переносить.
- Кнопка **«Обновить»** перечитывает все источники с диска, **экспорт дашборда в PNG**, светлая и тёмная тема (по системе), меню macOS и горячие клавиши.

| Данные | Модель |
|---|---|
| ![Страница «Данные»](docs/data.png) | ![Страница «Модель»](docs/model.png) |

<details>
<summary>Тёмная тема</summary>

![Тёмная тема](docs/report-dark.png)
</details>

## Быстрый старт

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/Antongo22/pb_for_mac.git
cd pb_for_mac
dotnet run --project src/PbForMac
```

Нажмите **«Пример»** на панели инструментов — откроется демо-отчёт `samples/demo.pbm`, построенный на данных всех пяти форматов.
Отчёт или файл данных можно передать и аргументом: `dotnet run --project src/PbForMac -- samples/demo.pbm`.

### Как пользоваться

1. **Получить данные** → выберите один или несколько файлов. Таблица появится на странице «Данные».
2. На странице **«Модель»** добавьте нужные преобразования, например вычисляемый столбец `Выручка = [Количество] * [Цена]`.
3. На странице **«Отчёт»** нажмите на тип визуализации в правой панели — визуал появится на холсте с автоматически подобранными полями.
   Поменяйте поля, агрегацию и заголовок в панели **«Поля»**. Если визуал выбран, нажатие на другой тип меняет его вид.
4. Добавьте **срез** или **фильтр страницы**, чтобы фильтровать все визуалы таблицы.
5. **Сохранить** → файл `.pbm`. **Экспорт PNG** сохраняет дашборд картинкой.

### Выражения вычисляемых столбцов

Используется синтаксис [`DataColumn.Expression`](https://learn.microsoft.com/dotnet/api/system.data.datacolumn.expression):

```text
[Цена] * [Количество] * (1 - [Скидка])
IIF([Сумма] > 100000, 'Крупный', 'Обычный')
Len([Товар])
Substring([Код товара], 1, 2)
IsNull([Скидка], 0)
Convert([Количество], 'System.Double') / 12
```

### Горячие клавиши

| Действие | macOS | Windows / Linux |
|---|---|---|
| Новый / открыть / сохранить отчёт | ⌘N / ⌘O / ⌘S | Ctrl+N / Ctrl+O / Ctrl+S |
| Сохранить как | ⇧⌘S | Ctrl+Shift+S |
| Получить данные / обновить | ⌘I / ⌘R | Ctrl+I / Ctrl+R |
| Экспорт в PNG | ⌘E | Ctrl+E |
| Страницы Отчёт / Данные / Модель | ⌘1 / ⌘2 / ⌘3 | Ctrl+1 / Ctrl+2 / Ctrl+3 |
| Удалить выбранный визуал | ⌫ | Delete |

## Сборка приложения

**macOS (.app):**

```bash
scripts/publish-macos.sh            # Apple Silicon (osx-arm64)
scripts/publish-macos.sh osx-x64    # Intel
```

Готовый `artifacts/<rid>/PbForMac.app` можно перенести в «Программы». Приложение подписано ad-hoc,
поэтому при первом запуске откройте его через контекстное меню → «Открыть».

**Windows / Linux:**

```bash
dotnet publish src/PbForMac -c Release -r win-x64 --self-contained -o artifacts/win-x64
dotnet publish src/PbForMac -c Release -r linux-x64 --self-contained -o artifacts/linux-x64
```

GitHub Actions собирает и тестирует проект на macOS, Linux и Windows; при публикации тега `v*` дополнительно
собираются архивы `PbForMac.app` для arm64 и x64 (артефакты workflow).

## Структура проекта

```text
src/PbForMac/
  Models/            описания отчёта: источники, шаги преобразований, визуалы, фильтры
  Services/
    Importers/       CSV, Excel, JSON, XML, SQLite → DataTable
    TypeInference    определение типов столбцов
    QueryEngine      фильтры, агрегации, группировка
    TransformEngine  применение шагов преобразования
    DataModel        исходные таблицы + шаги → итоговые таблицы модели
    ReportSerializer сохранение/загрузка .pbm
  ViewModels/        MVVM (CommunityToolkit.Mvvm): страницы и визуалы
  Views/             Avalonia XAML: главное окно, страницы, плитки дашборда, диалоги
  Controls/          DataTableGrid — DataGrid для произвольной DataTable
tests/PbForMac.Tests/   xUnit: импорт, типы, движки, модель, демо-отчёт
tools/SampleDataGenerator/  генератор демо-данных в samples/
tools/Screenshots/          headless-скриншоты для README и иконка приложения
samples/               демо-данные и отчёт demo.pbm
scripts/               сборка .app для macOS
```

Внутренняя модель данных — `System.Data.DataTable`: импортёры возвращают «сырые» значения, `TypeInference` приводит
их к типам, `DataModel` применяет шаги к копиям исходных таблиц, а визуалы строятся через `QueryEngine` с учётом фильтров и срезов.

## Разработка

```bash
dotnet test                                         # тесты
dotnet run --project tools/SampleDataGenerator      # пересоздать samples/
dotnet run --project tools/Screenshots              # обновить скриншоты в docs/
dotnet run --project tools/Screenshots -- docs dark # скриншоты тёмной темы
dotnet run --project tools/Screenshots -- icon      # перерисовать иконку
```

## Стек

[Avalonia UI](https://avaloniaui.net) 11.3 · [LiveCharts2](https://livecharts.dev) · [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) ·
[ClosedXML](https://github.com/ClosedXML/ClosedXML) · [CsvHelper](https://joshclose.github.io/CsvHelper/) · [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/)

## Лицензия

[MIT](LICENSE). Часть иконок интерфейса — [Material Design Icons](https://github.com/google/material-design-icons) (Apache License 2.0).

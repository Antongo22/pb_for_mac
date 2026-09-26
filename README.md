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

## Установка

Готовые сборки лежат на странице **[Releases](https://github.com/Antongo22/pb_for_mac/releases/latest)**.
Приложение самодостаточное (self-contained): устанавливать .NET не нужно.

| Система | Файл |
|---|---|
| macOS на Apple Silicon (M1 и новее) | `PbForMac-osx-arm64.zip` |
| macOS на Intel | `PbForMac-osx-x64.zip` |
| Windows 10/11 (x64) | `PbForMac-win-x64.zip` |
| Windows на ARM | `PbForMac-win-arm64.zip` |
| Linux x64 | `PbForMac-linux-x64.tar.gz` |
| Linux ARM64 | `PbForMac-linux-arm64.tar.gz` |

### macOS

Требуется macOS 11 Big Sur или новее.

1. Узнайте тип процессора:  → «Об этом Mac». «Чип Apple M…» — берите `osx-arm64`, «Процессор Intel» — `osx-x64`.
2. Скачайте архив и распакуйте его (Safari делает это сам), затем перетащите **PbForMac.app** в папку **«Программы»**.
3. Первый запуск. Приложение не нотаризовано Apple, поэтому macOS заблокирует его при первом открытии:
   - **macOS 15 Sequoia и новее:** откройте PbForMac → в окне предупреждения нажмите «Готово» →
     **Системные настройки → Конфиденциальность и безопасность** → внизу «PbForMac заблокировано» → **«Всё равно открыть»** → введите пароль.
   - **macOS 14 и старше:** правый клик по PbForMac.app → **«Открыть»** → «Открыть».
   - Если macOS пишет, что приложение **«повреждено»**, или вы предпочитаете Терминал, снимите карантин одной командой:

     ```bash
     xattr -dr com.apple.quarantine /Applications/PbForMac.app
     ```

4. Дальше приложение запускается как обычно: из Launchpad, Spotlight или Dock. Файлы отчётов `.pbm` открываются двойным кликом.

**Удаление:** перетащите PbForMac.app из «Программ» в Корзину.

### Windows

Требуется Windows 10 (1809+) или Windows 11.

1. Скачайте архив, откройте правым кликом → **«Извлечь всё…»**.
2. В распакованной папке запустите **`install.cmd`**. Программа установится для текущего пользователя
   в `%LOCALAPPDATA%\Programs\PbForMac`, права администратора не нужны. Установщик добавит ярлык в меню «Пуск»
   и свяжет файлы `.pbm` с PbForMac. Чтобы добавить ярлык и на рабочий стол, выполните в этой папке `install.cmd -DesktopShortcut`.
3. Если появится **«Windows защитил ваш компьютер»** (SmartScreen), нажмите **«Подробнее» → «Выполнить в любом случае»**:
   у приложения нет платной цифровой подписи.
4. Запускайте PbForMac из меню «Пуск».

**Без установки (portable):** просто запустите `app\PbForMac.exe` из распакованной папки.

**Удаление:** «Параметры → Приложения → Установленные приложения → PbForMac → Удалить» или `uninstall.cmd` из архива.

### Linux

Нужен графический сеанс X11 или Wayland (через XWayland). Пакеты обычно уже установлены в дистрибутивах с рабочим столом.
Если приложение не запускается, установите зависимости:

```bash
# Debian / Ubuntu / Mint
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libicu-dev
# Fedora
sudo dnf install libX11 libICE libSM fontconfig libicu
# Arch / Manjaro
sudo pacman -S libx11 libice libsm fontconfig icu
```

Установка для текущего пользователя (без `sudo`):

```bash
tar -xzf PbForMac-linux-x64.tar.gz
cd PbForMac-linux-x64
./install.sh
```

Скрипт копирует приложение в `~/.local/share/pbformac`, добавляет пункт в меню приложений, команду `pbformac`
(в `~/.local/bin`) и тип файлов `.pbm`. Для установки в другой каталог укажите префикс: `PREFIX=/opt/pbformac ./install.sh`.

**Без установки (portable):** `./app/PbForMac`.

**Удаление:** `./install.sh --uninstall` из распакованной папки.

### Из исходного кода

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```bash
git clone https://github.com/Antongo22/pb_for_mac.git
cd pb_for_mac
dotnet run --project src/PbForMac
```

## Быстрый старт

Запустите PbForMac и нажмите **«Пример»** на панели инструментов. Откроется демо-отчёт `samples/demo.pbm`,
построенный на данных всех пяти форматов. Отчёт или файл данных можно открыть и из командной строки:
`dotnet run --project src/PbForMac -- samples/demo.pbm` (или `pbformac файл.pbm` после установки на Linux).

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

## Сборка пакетов

Скрипт `scripts/package.sh` собирает тот же пакет, что публикуется в Releases, для любой платформы
(на macOS/Linux, а на Windows — в Git Bash):

```bash
scripts/package.sh osx-arm64     # artifacts/PbForMac-osx-arm64.zip  (PbForMac.app, только на macOS)
scripts/package.sh osx-x64
scripts/package.sh win-x64       # artifacts/PbForMac-win-x64.zip    (app\ + install.cmd)
scripts/package.sh win-arm64
scripts/package.sh linux-x64     # artifacts/PbForMac-linux-x64.tar.gz (app/ + install.sh)
scripts/package.sh linux-arm64
```

Только `.app` без архива: `scripts/publish-macos.sh [osx-arm64|osx-x64]` → `artifacts/<rid>/PbForMac.app`.

**Выпуск релиза.** GitHub Actions на каждый push собирает и тестирует проект на macOS, Linux и Windows.
Если отправить тег `v*`, он соберёт все шесть пакетов и создаст GitHub Release с ними:

```bash
git tag v0.1.0
git push origin v0.1.0
```

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
scripts/               сборка пакетов (package.sh) и .app для macOS
packaging/             установщики для Windows (install.cmd/.ps1) и Linux (install.sh, .desktop)
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

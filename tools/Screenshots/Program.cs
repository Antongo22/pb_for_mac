// Снимает скриншоты страниц приложения без экрана (Avalonia.Headless + Skia).
// Запуск: dotnet run --project tools/Screenshots [-- <папка> [light|dark]]
//         dotnet run --project tools/Screenshots -- icon   — перерисовать иконку приложения
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PbForMac;
using PbForMac.Services;
using PbForMac.ViewModels;
using PbForMac.Views;

var root = FindRepoRoot();
if (args is ["icon"])
{
    AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    RenderIcon(System.IO.Path.Combine(root, "src", "PbForMac", "Assets"));
    return;
}

var output = args.Length > 0 ? System.IO.Path.GetFullPath(args[0]) : System.IO.Path.Combine(root, "docs");
var theme = args.Length > 1 && args[1] == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();
Application.Current!.RequestedThemeVariant = theme;

var window = new MainWindow { Width = 1440, Height = 900 };
var viewModel = new MainWindowViewModel(new DialogService(window), new ThemeService(Application.Current), new AppSettings());
window.DataContext = viewModel;
window.Show();

var open = viewModel.OpenReportFileAsync(System.IO.Path.Combine(root, "samples", "demo.pbm"));
Pump(() => open.IsCompleted);
Console.WriteLine(viewModel.Status);

var suffix = theme == ThemeVariant.Dark ? "-dark" : "";
Capture("report");
viewModel.ShowDataCommand.Execute(null);
Capture("data");
viewModel.ShowModelCommand.Execute(null);
Capture("model");
// Вкладка «Связи» на странице «Модель».
var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string?)t.Header == "Связи");
Capture("relationships");
return;

void Capture(string name)
{
    // Даём графикам закончить анимацию.
    for (var i = 0; i < 30; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(40);
    }
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Кадр не получен.");
    var path = System.IO.Path.Combine(output, $"{name}{suffix}.png");
    frame.Save(path);
    Console.WriteLine(path);
}

// Иконка: три жёлтые колонки на тёмной скруглённой плашке.
static void RenderIcon(string directory)
{
    const int size = 1024;
    var canvas = new Canvas { Width = size, Height = size };
    canvas.Children.Add(new Border { Width = size, Height = size, CornerRadius = new CornerRadius(220), Background = new SolidColorBrush(Color.Parse("#252423")) });
    (double X, double Top, string Color)[] bars = [(232, 520, "#F2C811"), (437, 360, "#E8B200"), (642, 200, "#C79A00")];
    foreach (var (x, top, color) in bars)
    {
        var bar = new Rectangle { Width = 150, Height = 824 - top, RadiusX = 28, RadiusY = 28, Fill = new SolidColorBrush(Avalonia.Media.Color.Parse(color)) };
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, top);
        canvas.Children.Add(bar);
    }
    canvas.Measure(new Size(size, size));
    canvas.Arrange(new Rect(0, 0, size, size));

    using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
    bitmap.Render(canvas);
    var pngPath = System.IO.Path.Combine(directory, "app.png");
    bitmap.Save(pngPath);

    // ICO с PNG внутри (поддерживается Windows Vista+): 256×256.
    using var small = new RenderTargetBitmap(new PixelSize(256, 256), new Vector(24, 24));
    small.Render(canvas);
    using var png = new MemoryStream();
    small.Save(png);
    using var ico = new BinaryWriter(File.Create(System.IO.Path.Combine(directory, "app.ico")));
    ico.Write((short)0); ico.Write((short)1); ico.Write((short)1);
    ico.Write((byte)0); ico.Write((byte)0); ico.Write((byte)0); ico.Write((byte)0);
    ico.Write((short)1); ico.Write((short)32); ico.Write((int)png.Length); ico.Write(22);
    ico.Write(png.ToArray());
    Console.WriteLine(pngPath);
}

static void Pump(Func<bool> done)
{
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (!done() && DateTime.UtcNow < deadline)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(System.IO.Path.Combine(dir.FullName, "PbForMac.sln")))
            return dir.FullName;
    }
    return Directory.GetCurrentDirectory();
}

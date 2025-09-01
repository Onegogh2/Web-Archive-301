using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WebArchiv
{
    internal class Program
    {
        static async Task Main()
        {
            // Читаем список URL из файла
            var urlListPath = "urls.txt";
            var outputPath = "output.csv";
            if (!File.Exists(urlListPath))
            {
                Console.WriteLine($"Файл {urlListPath} не найден.");
                return;
            }
            Console.WriteLine($"Чтение списка URL из {urlListPath}...");
            var urls = File.ReadAllLines(urlListPath).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            Console.WriteLine($"Найдено {urls.Count} URL для обработки.");
            using var httpClient = new HttpClient();
            Console.OutputEncoding = Encoding.UTF8;

            // Создаём/перезаписываем файл с заголовком
            File.WriteAllText(outputPath, "URL;Title;H1\n", Encoding.UTF8);

            int processed = 0;
            foreach (var url in urls)
            {
                processed++;
                Console.WriteLine($"\n[{processed}/{urls.Count}] Обработка: {url}");
                // Получаем свежий снимок из CDX API
                string cdxApiUrl = $"https://web.archive.org/cdx/search/cdx?url={Uri.EscapeDataString(url)}&output=json&fl=timestamp,original&limit=1&filter=statuscode:200&sort=reverse";
                try
                {
                    Console.WriteLine($"  Запрос к CDX API...");
                    var cdxResponse = await httpClient.GetStringAsync(cdxApiUrl);
                    var cdxJson = JsonDocument.Parse(cdxResponse);
                    var arr = cdxJson.RootElement.EnumerateArray().ToList();
                    if (arr.Count < 2)
                    {
                        Console.WriteLine($"  Нет снимков в архиве для этого URL.");
                        File.AppendAllText(outputPath, $"{url};NO SNAPSHOT;NO SNAPSHOT\n", Encoding.UTF8);
                        continue;
                    }
                    var snap = arr[1];
                    var timestamp = snap[0].GetString();
                    var original = snap[1].GetString();
                    string archiveUrl = $"https://web.archive.org/web/{timestamp}/{original}";
                    Console.WriteLine($"  Найден снимок: {archiveUrl}");

                    Console.WriteLine($"  Скачивание страницы...");
                    var response = await httpClient.GetAsync(archiveUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"  Ошибка HTTP: {response.StatusCode}");
                        File.AppendAllText(outputPath, $"{url};HTTP {response.StatusCode};HTTP {response.StatusCode}\n", Encoding.UTF8);
                        continue;
                    }
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var html = Encoding.UTF8.GetString(bytes);

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";
                    var h1 = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim() ?? "";

                    // Если кракозябры, пробуем windows-1251
                    if (!string.IsNullOrEmpty(title) && title.Any(c => c == '�'))
                    {
                        Console.WriteLine($"  Обнаружены кракозябры, пробуем windows-1251...");
                        html = Encoding.GetEncoding("windows-1251").GetString(bytes);
                        doc.LoadHtml(html);
                        title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";
                        h1 = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim() ?? "";
                    }

                    // Экранируем кавычки и переносы
                    title = title.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");
                    h1 = h1.Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");
                    Console.WriteLine($"  Title: {title}");
                    Console.WriteLine($"  H1: {h1}");
                    File.AppendAllText(outputPath, $"{url};{title};{h1}\n", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Ошибка: {ex.Message}");
                    File.AppendAllText(outputPath, $"{url};ERROR: {ex.Message};ERROR\n", Encoding.UTF8);
                }
            }
            Console.WriteLine($"\nГотово! Результат в {outputPath}");
        }
    }
}

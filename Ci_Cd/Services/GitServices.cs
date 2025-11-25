using System.Diagnostics;

namespace Ci_Cd.Services;

public class GitServices: IGitServices
{

public string CloneRepository(string repoUrl)
{
    var tempPath = Path.Combine(Path.GetTempPath(), "PipelineGen_" + Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempPath);

    // Используем системный git с флагом --depth 1 (качает только последний коммит)
    var processInfo = new ProcessStartInfo("git", $"clone --depth 1 {repoUrl} .")
    {
        WorkingDirectory = tempPath,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    
    Console.WriteLine($"[Git] Запускаю клонирование: {repoUrl}");
    
    using (var process = Process.Start(processInfo))
    {
        if (process == null)
        {
            throw new Exception("Failed to start git process");
        }
        
        process.WaitForExit(); // Ждем окончания
        
        if (process.ExitCode != 0)
        {
            string error = process.StandardError.ReadToEnd();
            throw new Exception($"Git clone failed: {error}");
        }
    }

    Console.WriteLine("[Git] Готово.");
    return tempPath;
}
    
    public void DeleteRepository(string path)
    {
        if(Directory.Exists(path))
        {
            //создаем файлы только для чтения при клонировании
            //сначала нужно снаять атрибуты 
            var direct = new DirectoryInfo(path);
            SetAttributesNormal(direct);
            direct.Delete( true);
            
        }
    }

    private void SetAttributesNormal(DirectoryInfo dir)//рекурсивно снимаем защиту с файлов
        {
            foreach (var subDir in dir.GetDirectories())
            {
                SetAttributesNormal(subDir);
            }

            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
}
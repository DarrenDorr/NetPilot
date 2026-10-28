using System.Diagnostics;

namespace NetPilot.Engine.Zapret.Windows;

public static class ZapretProcessStartInfoFactory
{
    private const int MaxArguments = 512;
    private const int MaxArgumentCharacters = 16 * 1024;

    public static ProcessStartInfo Create(ZapretLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows-адаптер Zapret может использоваться только в Windows.");
        }

        var executablePath = Path.GetFullPath(plan.ExecutablePath);
        var workingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.WorkingDirectory));

        if (!Path.IsPathFullyQualified(executablePath)
            || !Path.IsPathFullyQualified(workingDirectory))
        {
            throw new InvalidOperationException("Путь запуска Zapret должен быть абсолютным.");
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException("Рабочий каталог Zapret не найден.");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Исполняемый файл Zapret не найден.", executablePath);
        }

        var executableDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(executablePath)
                ?? throw new InvalidOperationException("Не удалось определить каталог winws.exe."));

        if (!string.Equals(executableDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(executablePath), "winws.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Запуск разрешён только для winws.exe из проверенного рабочего каталога.");
        }

        RejectReparsePoint(workingDirectory);
        RejectReparsePoint(executablePath);

        if (plan.ArgumentList.Count is 0 or > MaxArguments)
        {
            throw new InvalidOperationException("Количество аргументов запуска Zapret недопустимо.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        foreach (var argument in plan.ArgumentList)
        {
            ValidateArgument(argument);
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void ValidateArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument)
            || argument.Length > MaxArgumentCharacters
            || !argument.StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Аргумент запуска Zapret имеет недопустимый формат.");
        }

        foreach (var character in argument)
        {
            if (character is '\0' or '\r' or '\n')
            {
                throw new InvalidOperationException("Аргумент запуска Zapret содержит недопустимый управляющий символ.");
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Запуск через symlink/reparse-point запрещён.");
        }
    }
}

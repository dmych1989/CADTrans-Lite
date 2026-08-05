using System;
using System.Diagnostics;

class Program
{
    static void Main()
    {
        var psi = new ProcessStartInfo
        {
            FileName = @"C:\Program Files\dotnet\dotnet.exe",
            Arguments = "restore CADTransLite.Core\\CADTransLite.Core.csproj",
            WorkingDirectory = @"E:\CADTrans Lite\src",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        // Copy all current environment variables
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            psi.Environment[entry.Key.ToString()] = entry.Value?.ToString() ?? "";
        }
        
        // Ensure critical variables
        psi.Environment["PROGRAMDATA"] = @"C:\ProgramData";
        psi.Environment["HOME"] = @"C:\Users\Administrator";
        psi.Environment["NUGET_PACKAGES"] = @"C:\Users\Administrator\.nuget\packages";
        psi.Environment["USERPROFILE"] = @"C:\Users\Administrator";
        
        var proc = Process.Start(psi);
        Console.WriteLine(proc.StandardOutput.ReadToEnd());
        Console.Error.WriteLine(proc.StandardError.ReadToEnd());
        proc.WaitForExit();
        Console.WriteLine($"Exit: {proc.ExitCode}");
    }
}

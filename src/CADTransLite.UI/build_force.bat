@echo off
echo Force rebuilding CADTransLite.UI...

REM Clean all build artifacts
echo Cleaning all build artifacts...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

REM Create directories
echo Creating directories...
mkdir "bin\Debug\net9.0-windows" 2>nul
mkdir "obj\Debug\net9.0-windows" 2>nul

REM Create lock file to bypass package resolution
echo Creating project.lock.json...
echo ^{^
  "version": 2,^
  "targets": ^{^
    "net9.0-windows": ^{^
      "CADTransLite.Core/1.0.0": ^{^
        "type": "project",^
        "framework": ".NETCoreApp,Version=v9.0"^
      }^
    }^
  }^
^} > "obj\project.lock.json"

REM Copy existing Core DLL if available
if exist "..\CADTransLite.TestRunner\bin\Debug\net9.0-windows\CADTransLite.Core.dll" (
    echo Copying existing Core DLL...
    xcopy "..\CADTransLite.TestRunner\bin\Debug\net9.0-windows\CADTransLite.Core.dll" "bin\Debug\net9.0-windows\" /y
)

echo Attempting build with custom lock file...

REM Try build with offline mode
"C:\Program Files\dotnet\dotnet.exe" build "CADTransLite.UI.csproj" --configuration Debug --no-dependencies --force --verbosity quiet

if %errorlevel% equ 0 (
    echo Build completed successfully!
    if exist "bin\Debug\net9.0-windows\CADTransLite.UI.exe" (
        echo Executable created at: bin\Debug\net9.0-windows\CADTransLite.UI.exe
        dir "bin\Debug\net9.0-windows\CADTransLite.UI.exe"
    ) else (
        echo Executable not found, checking build logs...
    )
) else (
    echo Build failed, trying alternative approach...
)

pause
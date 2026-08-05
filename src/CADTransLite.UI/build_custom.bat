@echo off
echo Building CADTransLite.UI with custom process...

REM Clean previous builds
echo Cleaning previous builds...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

REM Create directories
echo Creating directories...
mkdir "bin\Debug\net8.0-windows" 2>nul
mkdir "obj\Debug\net8.0-windows" 2>nul

REM Copy core library reference
echo Copying core library...
xcopy "..\CADTransLite.Core\bin\Debug\net8.0-windows\CADTransLite.Core.dll" "bin\Debug\net8.0-windows\" /y /i

REM Create minimal project.assets.json
echo Creating project.assets.json...
echo {"version": 2, "targets": {"net8.0-windows": {^} }, "libraries": {^}, "projectFileDependencyGroups": {"net8.0-windows": []}} > "obj\project.assets.json"

REM Use MSBuild directly if available
where msbuild >nul 2>&1
if %errorlevel% equ 0 (
    echo Using MSBuild...
    msbuild "CADTransLite.UI.csproj" /p:Configuration=Debug /p:TargetFramework=net8.0-windows /p:RestorePackages=false /p:EnableNuGetPackageRestore=false /v:minimal
    goto :end
)

REM Try using dotnet CLI with specific configuration
echo Using dotnet CLI...
"C:\Program Files\dotnet\dotnet.exe" build "CADTransLite.UI.csproj" --configuration Debug --no-restore --force --verbosity quiet

:end
if %errorlevel% equ 0 (
    echo Build completed successfully!
    echo Executable created at: bin\Debug\net8.0-windows\CADTransLite.UI.exe
) else (
    echo Build failed.
)

pause
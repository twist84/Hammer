@echo off
REM Build (if needed) and run Hammer; every argument is forwarded to Hammer.
REM Examples:
REM   run.bat gamefile-scenario-file-make-from-xml some_map.xml
REM   run.bat --help
dotnet run --project "%~dp0Hammer.csproj" -c Debug -- %*

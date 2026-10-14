@echo off
REM Build the standalone Hammer console tool (needs the .NET SDK).
REM Any extra arguments are forwarded to `dotnet build` (e.g. -c Release).
dotnet build "%~dp0Hammer.csproj" -c Debug %*

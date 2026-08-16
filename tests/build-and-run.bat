@echo off
setlocal
rem Build and run the integration test suite (requires Node.js on PATH or auto-detected).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

echo [1/2] compiling test runner...
"%CSC%" @runner.rsp
if errorlevel 1 ( echo COMPILE FAILED & exit /b 1 )

echo [2/2] running integration tests...
tests\runner.exe
if errorlevel 1 ( echo TESTS FAILED & exit /b 1 )

echo ALL TESTS PASSED

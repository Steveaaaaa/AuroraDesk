$ErrorActionPreference = "Stop"

$srcDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $srcDir "bin"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$vcvars = "D:\vs\VC\Auxiliary\Build\vcvars64.bat"
$sdkInc = "D:\Windows Kits\10\Include\10.0.19041.0"
$sdkLib = "D:\Windows Kits\10\Lib\10.0.19041.0"

$outDll = Join-Path $outDir "AuroraDesk.TaskbarTap.dll"
$objFile = Join-Path $outDir "TaskbarTap.obj"
$srcCpp = Join-Path $srcDir "TaskbarTap.cpp"
$defFile = Join-Path $srcDir "TaskbarTap.def"
$batFile = Join-Path $outDir "build_step.cmd"

$batContent = @"
@echo off
call "$vcvars" >nul
cl.exe /nologo /O2 /LD /EHsc /MT /std:c++17 /DUNICODE /D_UNICODE /I"$sdkInc\um" /I"$sdkInc\shared" /I"$sdkInc\winrt" /I"$sdkInc\ucrt" /Fo"$objFile" "$srcCpp" /link /DEF:"$defFile" /OUT:"$outDll" /LIBPATH:"$sdkLib\um\x64" /LIBPATH:"$sdkLib\ucrt\x64" ole32.lib oleaut32.lib runtimeobject.lib user32.lib
exit /b %ERRORLEVEL%
"@

Set-Content -Path $batFile -Value $batContent -Encoding ASCII
& cmd.exe /c "`"$batFile`""
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Get-Item $outDll | Select-Object FullName, @{N="SizeKB";E={[math]::Round($_.Length/1KB, 1)}} | Format-Table -AutoSize

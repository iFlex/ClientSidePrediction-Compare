@echo off
setlocal

:: --- CONFIGURATION ---
:: Pair 1 (Incoming)
set "SRC1=.\PredictionDemo-Ursitoare\build"
set "DST1=.\PredictionDemo\Ursitoare"

:: Pair 2
set "SRC2=D:\gamedev\PredictionCompare\PredictionDemo-FishNet\build"
set "DST2=D:\gamedev\PredictionCompare\PredictionDemo\FishNet"

:: Pair 3
set "SRC3=D:\gamedev\PredictionCompare\PredictionDemo-PurrNet\build"
set "DST3=D:\gamedev\PredictionCompare\PredictionDemo\PurrNet"

:: Pair 4
set "SRC4=D:\gamedev\PredictionCompare\PredictionDemo-Mirror\build"
set "DST4=D:\gamedev\PredictionCompare\PredictionDemo\Mirror"
:: ---------------------

echo Copying Pair 1 (Incoming)...
:: /E copies all subfolders. /MT:8 speeds up copying using multi-threading.
robocopy "%SRC1%" "%DST1%" /E /MT:8

echo.
echo Copying Pair 2...
robocopy "%SRC2%" "%DST2%" /E /MT:8

echo.
echo Copying Pair 3...
robocopy "%SRC3%" "%DST3%" /E /MT:8

echo.
echo Copying Pair 4...
robocopy "%SRC4%" "%DST4%" /E /MT:8

echo.
echo All copying operations completed!
pause
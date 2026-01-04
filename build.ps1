$publishArgs = @(
    "publish"
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained", "true"
    "-p:PublishSingleFile=true"
    "-p:PublishTrimmed=false"
    "-p:EnableCompressionInSingleFile=true"
    "-p:PublishReadyToRun=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)

if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks | Select-String "^10\."))) {
    dotnet @publishArgs
} else {
    .\dotnet\dotnet.exe @publishArgs
}

if ($LASTEXITCODE -eq 0) {
    Copy-Item -Path "bin\Release\net10.0-windows\win-x64\publish\WebcamSettings.exe" -Destination "WebcamSettings.exe"
} else {
    exit 1
}

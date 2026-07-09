# === Viewers v2: whole-sheet grid + all-sheet tabs + style-ready cells + header/footer wiring ===
Expand-Archive -Path "$HOME\Downloads\delta-viewers2.zip" -DestinationPath . -Force
dotnet build src\ChuvadiReader.Core\ChuvadiReader.Core.csproj -c Release --configfile nuget.config
dotnet build src\ChuvadiReader.Ui\ChuvadiReader.Ui.csproj   -c Release --configfile nuget.config
dotnet run --project .\src\ChuvadiReader.Windows

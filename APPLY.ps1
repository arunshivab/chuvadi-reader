# === Flatten to static PDF (XFA / form fields / markup) ===
# Complete files; run from the repo root. You're already on chuvadi-pdf 3.17.1.

# 1) extract the complete files into place
Expand-Archive -Path "$HOME\Downloads\delta-flatten.zip" -DestinationPath . -Force

# 2) build Core + Ui
dotnet build src\ChuvadiReader.Core\ChuvadiReader.Core.csproj -c Release --configfile nuget.config
dotnet build src\ChuvadiReader.Ui\ChuvadiReader.Ui.csproj   -c Release --configfile nuget.config

# 3) restore + test
dotnet restore tests\ChuvadiReader.Tests\ChuvadiReader.Tests.csproj --configfile nuget.config
dotnet test  tests\ChuvadiReader.Tests\ChuvadiReader.Tests.csproj -c Release --no-restore

# 4) run
dotnet run --project .\src\ChuvadiReader.Windows

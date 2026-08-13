$env:USERPROFILE = 'C:\Users\Administrator'
$env:APPDATA = 'C:\Users\Administrator\AppData\Roaming'
$env:LOCALAPPDATA = 'C:\Users\Administrator\AppData\Local'
$env:NUGET_PACKAGES = 'C:\Users\Administrator\.nuget\packages'
Set-Location 'E:\CADTrans Lite\src'
& 'C:\Program Files\dotnet\dotnet.exe' build CADTransLite.sln

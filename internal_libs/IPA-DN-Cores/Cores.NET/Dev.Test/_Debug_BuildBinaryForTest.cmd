cd /d C:\git\IPA-DN-Cores\Cores.NET\Dev.Test\

mkdir c:\tmp\cores_built_binary_debug\

mkdir bld\

dotnet publish -c Debug -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\win-x64\Dev.Test

copy /y bld\win-x64\Dev.Test\Dev.Test.exe c:\tmp\cores_built_binary_debug\Dev.Test.Win.x86_64.exe



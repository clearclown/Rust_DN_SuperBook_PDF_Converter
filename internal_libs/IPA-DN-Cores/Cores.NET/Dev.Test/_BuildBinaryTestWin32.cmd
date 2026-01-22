cd /D "%~dp0"

rmdir /s /q bld\

rmdir /s /q c:\tmp\cores_built_binary\
mkdir c:\tmp\cores_built_binary\

mkdir bld\

..\Dev.Tools\CompiledBin\WriteTimeStamp.exe > c:\tmp\cores_built_binary\TimeStamp.txt

dotnet clean -c Release

dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\win-x64\Dev.Test

dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o bld\linux-x64\Dev.Test

copy /y bld\win-x64\Dev.Test\Dev.Test.exe c:\tmp\cores_built_binary\Dev.Test.Win.x86_64.exe
copy /y bld\linux-x64\Dev.Test\Dev.Test c:\tmp\cores_built_binary\Dev.Test.Linux.x86_64

cd /d c:\tmp\cores_built_binary\

c:\tmp\cores_built_binary\Dev.Test.Win.x86_64.exe Hello

start c:\tmp\cores_built_binary\

IF NOT "%ERRORLEVEL%" == "0" GOTO L_END


:L_END
pause




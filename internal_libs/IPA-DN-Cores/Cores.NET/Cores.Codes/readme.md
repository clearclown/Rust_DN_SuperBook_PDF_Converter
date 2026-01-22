## Cores.Codes ディレクトリについて
このディレクトリには C# コードおよびリソースファイルが格納されています。


- 再利用可能なコードであって、
- 開発するアプリケーションのアセンブリ内で直接コンパイル・リンクされたほうが良いもの  
  (すなわち、Cores.Basic 内に入れるべきでない雑多なコードなど。都度、外部依存ライブラリを呼び出すようなもの)
- コードサイズの大きいもの

という条件を満たすものは、Cores.Codes に入れることにしておるのです。


したがって、あるライブラリがリンクされているときだけリンクされるべきソースコードが多数入っています。この場合は、ソースコードの最初と最後に `#if` および `#endif` を用いることで不要なコードは除外することができるようにいたします。


## 使い方
アプリケーションの csproj ファイル内に以下のように記述をして、直接 include してください。
### 直接の場合
```
  <ItemGroup>
    <Compile Include="../../../../Cores.NET/Cores.Codes/**/*.cs" />
    <Content Include="../../../../Cores.NET/Cores.Codes/**/*.cshtml" />
    <EmbeddedResource Include="../../../../Cores.NET/Cores.Codes/Resources/**/*" />
  </ItemGroup>
```
### submodules 経由の場合
```
  <ItemGroup>
    <Compile Include="../submodules/IPA-DN-Cores/Cores.NET/Cores.Codes/**/*.cs" />
    <Content Include="../submodules/IPA-DN-Cores/Cores.NET/Cores.Codes/**/*.cshtml" />
    <EmbeddedResource Include="../submodules/IPA-DN-Cores/Cores.NET/Cores.Codes/Resources/**/*" />
  </ItemGroup>
```



### ご注意
`Cores.Codes.shproj` は Visual Studio のソリューションエクスプローラでファイルのブラウズや編集を容易にするためだけのものであり、これに対してアプリケーションプロジェクトから依存してはいけません。


## コンパイルスイッチ一覧と必要なライブラリの列挙
### CORES_CODES_ASPNETMVC
- ASP&#46;NET MVC 関係

### CORES_CODES_THINCONTROLLER
- シン・テレワークシステム関係

### CORES_CODES_AWS
- AWS 関係

### CORES_CODES_IISUTIL
- IIS 用ユーティリティ関係

### CORES_CODES_EXCELUTIL
- Excel ユーティリティ関係







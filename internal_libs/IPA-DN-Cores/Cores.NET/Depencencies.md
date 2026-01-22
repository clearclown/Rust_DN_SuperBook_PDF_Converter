## Cores.Basic.Common
```
  <ItemGroup>
    <PackageReference Include="System.Text.Encoding.CodePages" Version="4.5.0" />
  </ItemGroup>
```

## Cores.Basic.Database
```
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="2.2.3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="2.2.3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="2.2.3" />
    <PackageReference Include="Dapper" Version="1.60.1" />
  </ItemGroup>
```

## Cores.Basic.Security
```
  <ItemGroup>
    <PackageReference Include="BouncyCastle.NetCore" Version="1.8.3" />
  </ItemGroup>
```

## Cores.Basic.WebServer `->  Cores.Basic.Json`
```
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore" Version="2.2.0" />
    <PackageReference Include="Microsoft.AspNetCore.StaticFiles" Version="2.2.0" />
    <PackageReference Include="Castle.Core" Version="4.3.1" />
  </ItemGroup>
```

## Cores.Basic.Json
```
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
  </ItemGroup>
```

## Cores.Basic.Config `->  Cores.Basic.Json`
```
  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="6.0.0" />
  </ItemGroup>
```

## Cores.Basic.Html
```
  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.9.2" />
  </ItemGroup>
```


// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

#if CORES_BASIC_DATABASE

using System;
using System.Data;
using System.Data.Common;
//using System.Data.SqlClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using Dapper;
using Dapper.Contrib;
using Dapper.Contrib.Extensions;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class Database
    {
        public static readonly Copenhagen<IsolationLevel> DefaultIsolationLevel = IsolationLevel.Serializable;

        public static readonly Copenhagen<int> DefaultDatabaseTransactionRetryAverageIntervalSecs = 200;
        public static readonly Copenhagen<int> DefaultDatabaseTransactionRetryCount = 10;
        public static readonly Copenhagen<int> DefaultDatabaseTransactionRetryIntervalMaxFactor = 5;

        public static readonly Copenhagen<int> DefaultOpenTryCount = 2;
    }
}

public class SqlDatabaseConnectionSetting
{
    public string DataSource { get; }
    public string InitialDatalog { get; }
    public string UserId { get; }
    public string Password { get; }
    public bool Pooling { get; }
    public int Port { get; }
    public bool Encrypt { get; }
    public bool TrustServerCertificate { get; }

    public SqlDatabaseConnectionSetting(string dataSource, string initialCatalog, string userId, string password, bool pooling = false /* Linux 版 SQL Client では true にすると動作不良を引き起こすため、false を推奨 */
        , int port = Consts.Ports.MsSqlServer, bool encrypt = false, bool trustServerCertificate = true)
    {
        this.DataSource = dataSource;
        this.InitialDatalog = initialCatalog;
        this.UserId = userId;
        this.Password = password;
        this.Pooling = pooling;
        this.Port = port._IsInTcpUdpPortRange() ? Consts.Ports.MsSqlServer : port;
        this.Encrypt = encrypt;
        this.TrustServerCertificate = trustServerCertificate;
    }

    public static implicit operator string(SqlDatabaseConnectionSetting config)
        => config.ToString();

    public override string ToString()
        => $"Data Source={this.DataSource}{(this.Port == Consts.Ports.MsSqlServer ? "" : "," + this.Port.ToString())};Initial Catalog={this.InitialDatalog};Persist Security Info=True;Pooling={this.Pooling._ToBoolStr()};User ID={this.UserId};Password={this.Password};Encrypt={this.Encrypt};TrustServerCertificate={this.TrustServerCertificate};";


    // SQL データベースライブラリのバージョンアップに伴い SQL 接続文字列を互換性を実現する目的で正規化する。
    // 参考: https://techcommunity.microsoft.com/t5/sql-server-blog/released-general-availability-of-microsoft-data-sqlclient-4-0/ba-p/2983346
    // TODO: 実装が適当である。そのうち、接続文字列の文法を厳密に認識した実装に変更することが推奨される。
    public static string NormalizeSqlDatabaseConnectionStringForCompabitility(string src)
    {
        string tmp = "";
        string src2 = src._ReplaceStr(" ", "");
        if (src2._InStri("Encrypt=") == false)
        {
            tmp += "Encrypt=False;";
        }
        if (src2._InStri("TrustServerCertificate=") == false)
        {
            tmp += "TrustServerCertificate=True;";
        }
        if (src.LastOrDefault() != ';')
        {
            src += ";";
        }
        src += tmp;
        return src;
    }
}

public class EasyTable : TableAttribute
{
    public EasyTable(string tableName) : base(tableName) { }
}
public class EasyKey : KeyAttribute { }
public class EasyManualKey : ExplicitKeyAttribute { }
public class EasyReadOnly : WriteAttribute { public EasyReadOnly() : base(false) { } }

public class DapperColumn : Attribute
{
    public string Name;
    public DapperColumn(string name) { this.Name = name; }
}

public sealed class DbConsoleDebugPrinterProvider : ILoggerProvider
{
    Ref<bool> EnableConsoleLogger;

    public DbConsoleDebugPrinterProvider(Ref<bool> enableConsoleLogger)
    {
        this.EnableConsoleLogger = enableConsoleLogger;
    }

    public ILogger CreateLogger(string categoryName) => new DbConsoleDebugPrinter(this.EnableConsoleLogger);
    public void Dispose() { }
}

public class DbConsoleDebugPrinter : ILogger
{
    Ref<bool> EnableConsoleLogger;

    public DbConsoleDebugPrinter(Ref<bool> enableConsoleLogger)
    {
        this.EnableConsoleLogger = enableConsoleLogger;
    }

    public IDisposable BeginScope<TState>(TState state) => new EmptyDisposable();
    public bool IsEnabled(LogLevel logLevel)
    {
        if (EnableConsoleLogger == false) return false;

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Information:
            case LogLevel.None:
                return false;
        }
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception, string> formatter)
    {
        if (eventId.ToString().IndexOf("CommandExecuting", StringComparison.OrdinalIgnoreCase) == -1)
        {
            return;
        }
        StringWriter w = new StringWriter();
        w.WriteLine("----------");
        if (state != null) w.Write($"State: {state}");
        if (exception != null) w.Write($", Exception: {exception.Message}");
        w.WriteLine();
        w.WriteLine("----------");
        string str = w.ToString();
        Dbg.WriteLine(w.ToString()._TrimCrlf());
    }
}

// データベース値
public class DatabaseValue
{
    public bool IsNull
    {
        get
        {
            if (Object == null) return true;
            if (Object == (object)DBNull.Value) return true;
            return false;
        }
    }

    public DateTime DateTime => (this.IsNull ? Util.ZeroDateTimeValue : ((DateTime)Object!)._NormalizeDateTime());
    public DateTimeOffset DateTimeOffset => (this.IsNull ? Util.ZeroDateTimeOffsetValue : ((DateTimeOffset)Object!)._NormalizeDateTimeOffset());
    public string String => this.IsNull ? "" : ((string?)Object)._NonNull();
    public double Double => this.IsNull ? 0.0 : (double)Object!;
    public int Int => this.IsNull ? 0 : (int)Object!;
    public uint UInt => this.IsNull ? 0 : (uint)Object!;
    public long Int64 => this.IsNull ? 0 : (long)Object!;
    public ulong UInt64 => this.IsNull ? 0 : (ulong)Object!;
    public bool Bool => this.IsNull ? false : (bool)Object!;
    public byte[]? Data => this.IsNull ? null : (byte[])Object!;
    public short Short => this.IsNull ? (short)0 : (short)Object!;
    public ushort UShort => this.IsNull ? (ushort)0 : (ushort)Object!;
    public byte Byte => this.IsNull ? (byte)0 : (byte)Object!;
    public sbyte SByte => this.IsNull ? (sbyte)0 : (sbyte)Object!;

    public object? Object { get; }

    [Obsolete]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Int_NullAsZero => Int;

    [Obsolete]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public long Int64_NullAsZero => (this.IsNull ? 0 : Int64);

    public DatabaseValue(object? value)
    {
        this.Object = value;
    }

    public override string? ToString()
    {
        return this.Object?.ToString() ?? "";
    }
}

// 行
public class Row
{
    public readonly DatabaseValue[] ValueList;
    public readonly string[] FieldList;

    public Row(DatabaseValue[] ValueList, string[] FieldList)
    {
        this.ValueList = ValueList;
        this.FieldList = FieldList;
    }

    public DatabaseValue this[string name]
    {
        get
        {
            int i;
            for (i = 0; i < this.FieldList.Length; i++)
            {
                if (this.FieldList[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return this.ValueList[i];
                }
            }

            throw new ApplicationException("Field \"" + name + "\" not found.");
        }
    }

    public override string ToString()
    {
        string?[] strs = new string[this.ValueList.Length];
        int i;
        for (i = 0; i < this.ValueList.Length; i++)
        {
            strs[i] = this.ValueList[i].ToString();
        }

        return Str.CombineStringArray(strs, ", ");
    }

    public JObject ToJsonObject()
    {
        JObject ret = Json.NewJsonObject();

        int i;
        for (i = 0; i < this.FieldList.Length; i++)
        {
            string fieldName = FieldList[i];
            object? value = ValueList[i].Object;

            ret.Add(fieldName, new JValue(value));
        }

        return ret;
    }
}

// データ
public class Data : IEnumerable, IEmptyChecker
{
    public Row[]? RowList { get; private set; } = null;
    public string[]? FieldList { get; private set; } = null;

    public bool IsEmpty => IsThisEmpty();

    public Data() { }

    public bool IsThisEmpty()
    {
        return (RowList?.Length ?? 0) == 0;
    }

    public Data(Database db)
    {
        DbDataReader r = db.DataReader._NullCheck();

        int i;
        int num = r.FieldCount;

        List<string> fieldsList = new List<string>();

        for (i = 0; i < num; i++)
        {
            fieldsList.Add(r.GetName(i));
        }

        this.FieldList = fieldsList.ToArray();

        List<Row> row_list = new List<Row>();
        while (db.ReadNext())
        {
            DatabaseValue[] values = new DatabaseValue[this.FieldList.Length];

            for (i = 0; i < this.FieldList.Length; i++)
            {
                values[i] = db[this.FieldList[i]];
            }

            row_list.Add(new Row(values, this.FieldList));
        }

        this.RowList = row_list.ToArray();
    }

    public async Task ReadFromDbAsync(Database db, CancellationToken cancel = default)
    {
        DbDataReader r = db.DataReader._NullCheck();

        int i;
        int num = r.FieldCount;

        List<string> fieldsList = new List<string>();

        for (i = 0; i < num; i++)
        {
            fieldsList.Add(r.GetName(i));
        }

        this.FieldList = fieldsList.ToArray();

        List<Row> row_list = new List<Row>();
        while (await db.ReadNextAsync(cancel))
        {
            DatabaseValue[] values = new DatabaseValue[this.FieldList.Length];

            for (i = 0; i < this.FieldList.Length; i++)
            {
                values[i] = db[this.FieldList[i]];
            }

            row_list.Add(new Row(values, this.FieldList));
        }

        this.RowList = row_list.ToArray();
    }

    public IEnumerator GetEnumerator()
    {
        if (this.RowList != null)
        {
            for (int i = 0; i < this.RowList.Length; i++)
            {
                yield return this.RowList[i];
            }
        }
    }
}

// Using トランザクション
public struct UsingTran : IDisposable, IAsyncDisposable
{
    Database db;
    Once Once;

    public UsingTran(Database db)
    {
        this.db = db;
        this.Once = new Once();
    }

    public async Task CommitAsync(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        await this.db.CommitAsync(cancel);
    }

    public void Commit(CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        this.db.Commit();
    }

    public void Dispose()
    {
        if (Once.IsFirstCall() == false) return;

        if (db != null)
        {
            db.Rollback();
            db = null!;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Once.IsFirstCall() == false) return;

        if (db != null)
        {
            await db.RollbackAsync();
            db = null!;
        }
    }
}

// デッドロック再試行設定
public class DeadlockRetryConfig
{
    public int RetryAverageInterval { get; }
    public int RetryCount { get; }
    public int RetryIntervalMaxFactor { get; }

    public DeadlockRetryConfig(int RetryAverageInterval, int RetryCount, int RetryIntervalMaxFactor = 1)
    {
        this.RetryAverageInterval = RetryAverageInterval;
        this.RetryCount = RetryCount;
        this.RetryIntervalMaxFactor = Math.Max(RetryIntervalMaxFactor, 1);
    }
}

// データベースの種類
[Flags]
public enum DatabaseServerType
{
    SQLServer = 0,
    SQLite,
}

// データベースアクセス
public sealed class Database : AsyncService
{
    static Database()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public DbConnection Connection { get; private set; }
    public DbTransaction? Transaction { get; private set; } = null;
    public DbDataReader? DataReader { get; private set; } = null;
    public bool IsOpened { get; private set; } = false;

    public const int DefaultCommandTimeoutSecs = 60;

    public int CommandTimeoutSecs { get; set; } = DefaultCommandTimeoutSecs;

    public static readonly DeadlockRetryConfig DefaultDeadlockRetryConfig
        = new DeadlockRetryConfig(CoresConfig.Database.DefaultDatabaseTransactionRetryAverageIntervalSecs, CoresConfig.Database.DefaultDatabaseTransactionRetryCount, CoresConfig.Database.DefaultDatabaseTransactionRetryIntervalMaxFactor);

    public DeadlockRetryConfig DeadlockRetryConfig { get; } = DefaultDeadlockRetryConfig;

    public IsolationLevel DefaultIsolationLevel { get; } = CoresConfig.Database.DefaultIsolationLevel;

    public int OpenTryCount = CoresConfig.Database.DefaultOpenTryCount;

    public static void RunStartupTest()
    {
        SqlDatabaseConnectionSetting sqlSettings = new SqlDatabaseConnectionSetting("127.0.0.1", "abc", "sa", "1234");

        using var conn = new SqlConnection(sqlSettings);

        using var conn2 = new SqliteConnection(@"Data Source=/tmp/1.sqlite");

        Dapper.Contrib.Extensions.TableAttribute test1 = new TableAttribute("abc");
    }

    // コンストラクタ
    public Database(string dbServerConnectionString, IsolationLevel? defaultIsolationLevel = null, DeadlockRetryConfig? deadlockRetryConfig = null, DatabaseServerType serverType = DatabaseServerType.SQLServer,
        int openTryCount = 0)
    {
        try
        {
            if (deadlockRetryConfig != null) DeadlockRetryConfig = deadlockRetryConfig;
            if (defaultIsolationLevel != null) this.DefaultIsolationLevel = defaultIsolationLevel.Value;
            if (openTryCount <= 0) openTryCount = CoresConfig.Database.DefaultOpenTryCount;

            this.OpenTryCount = openTryCount;

            DbConnection? conn;

            dbServerConnectionString = SqlDatabaseConnectionSetting.NormalizeSqlDatabaseConnectionStringForCompabitility(dbServerConnectionString);

            switch (serverType)
            {
                case DatabaseServerType.SQLite:
                    conn = new SqliteConnection(dbServerConnectionString);
                    break;

                default:
                    conn = new SqlConnection(dbServerConnectionString);
                    break;
            }

            this.Connection = conn;
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    public Database(DbConnection dbConnection, IsolationLevel? defaultIsolationLevel = null, DeadlockRetryConfig? deadlockRetryConfig = null,
        int openTryCount = 0)
    {
        if (deadlockRetryConfig != null) DeadlockRetryConfig = deadlockRetryConfig;
        if (defaultIsolationLevel != null) this.DefaultIsolationLevel = defaultIsolationLevel.Value;
        if (openTryCount <= 0) openTryCount = CoresConfig.Database.DefaultOpenTryCount;

        this.OpenTryCount = openTryCount;

        Connection = dbConnection;
    }

    public void EnsureOpen(CancellationToken cancel = default) => EnsureOpenAsync(cancel)._GetResult();

    AsyncLock OpenCloseLock = new AsyncLock();

    public async Task EnsureOpenAsync(CancellationToken cancel = default)
    {
        if (IsOpened) return;
        using (await OpenCloseLock.LockWithAwait(cancel))
        {
            if (IsOpened == false)
            {
                await RetryHelper.RunAsync(async () =>
                {
                    await Connection.OpenAsync(cancel);
                },
                0,
                this.OpenTryCount,
                cancel);

                IsOpened = true;

            }
        }
    }

    // バルク書き込み
    public void SqlBulkWrite(string tableName, DataTable dt, CancellationToken cancel = default)
    {
        EnsureOpen(cancel);

        using (SqlBulkCopy bc = new SqlBulkCopy((SqlConnection)this.Connection, SqlBulkCopyOptions.Default, (SqlTransaction)Transaction!))
        {
            bc.BulkCopyTimeout = CommandTimeoutSecs;
            bc.DestinationTableName = tableName;
            bc.WriteToServer(dt);
        }
    }

    public async Task SqlBulkWriteAsync(string tableName, DataTable dt, CancellationToken cancel = default)
    {
        await EnsureOpenAsync(cancel);

        using (SqlBulkCopy bc = new SqlBulkCopy((SqlConnection)this.Connection, SqlBulkCopyOptions.Default, (SqlTransaction)Transaction!))
        {
            bc.BulkCopyTimeout = CommandTimeoutSecs;
            bc.DestinationTableName = tableName;
            await bc.WriteToServerAsync(dt, cancel);
        }
    }

    // クエリの実行
    public void Query(string commandStr, params object[] args)
    {
        EnsureOpen();

        CloseQuery();
        DbCommand cmd = buildCommand(commandStr, args);

        DataReader = cmd.ExecuteReader();
    }

    public async Task QueryAsync(string commandStr, params object[] args)
    {
        await EnsureOpenAsync();

        await CloseQueryAsync();

        DbCommand cmd = buildCommand(commandStr, args);

        DataReader = await cmd.ExecuteReaderAsync();
    }

    public async Task QueryAsync(string commandStr, CancellationToken cancel, params object[] args)
    {
        await EnsureOpenAsync(cancel);

        await CloseQueryAsync();

        DbCommand cmd = buildCommand(commandStr, args);

        DataReader = await cmd.ExecuteReaderAsync(cancel);
    }

    readonly static CriticalSection DapperTypeMapLock = new CriticalSection<Database>();
    static HashSet<Type> DapperInstalledTypes = new HashSet<Type>();
    static void EnsureDapperTypeMapping(Type? t)
    {
        if (t == null) return;
        lock (DapperTypeMapLock)
        {
            if (DapperInstalledTypes.Contains(t) == false)
            {
                DapperInstalledTypes.Add(t);
                Dapper.SqlMapper.SetTypeMap(t,
                    new CustomPropertyTypeMap(t, (type, columnName) =>
                    {
                        var properties = type.GetProperties();

                        // UserName == USER_NAME
                        var c1 = properties.SingleOrDefault(p => p.Name._IsSameiIgnoreUnderscores(columnName)
                        || p.GetCustomAttributes(true).OfType<DapperColumn>().Any(a => a.Name._IsSameiIgnoreUnderscores(columnName)));
                        if (c1 != null) return c1;

                        int i = columnName.IndexOf("_");
                        if (i >= 2)
                        {
                            // UserName == USERS_USERNAME
                            string columnName2 = columnName.Substring(i + 1);

                            var c2 = properties.SingleOrDefault(p => p.Name._IsSameiIgnoreUnderscores(columnName2)
                                || p.GetCustomAttributes(true).OfType<DapperColumn>().Any(a => a.Name._IsSameiIgnoreUnderscores(columnName2)));
                            if (c2 != null) return c2;
                        }

                        return null;
                    }));
            }
        }
    }

    //async Task<CommandDefinition> SetupDapperAsync(string commandStr, object? param, Type? type = null, CancellationToken cancel = default)
    //{
    //    await EnsureOpenAsync(cancel);

    //    EnsureDapperTypeMapping(type);

    //    if (param != null)
    //        EnsureDapperTypeMapping(param.GetType());

    //    // 2021/09/04 注意: Dapper は Cancel に対応していない。少なくとも 2.0.90 の async メソッドで確認。
    //    CommandDefinition cmd = new CommandDefinition(commandStr, param, this.Transaction, commandTimeout: this.CommandTimeoutSecs);

    //    return cmd;
    //}

    async Task SetupDapperAsync(object? param, Type? type = null, CancellationToken cancel = default)
    {
        await EnsureOpenAsync(cancel);

        EnsureDapperTypeMapping(type);

        if (param != null)
            EnsureDapperTypeMapping(param.GetType());
    }

    public async Task<T> GetOrInsertIfEmptyAsync<T>(string selectStr, object? selectParam, string insertStr, object insertParam, string newCreatedRowSelectWithIdCmd)
    {
        if (selectParam == null) selectParam = new { };

        IEnumerable<T> ret = await EasyQueryAsync<T>(selectStr, selectParam);
        T? t = ret.SingleOrDefault();
        if (t._IsNullOrDefault())
        {
            await ExecuteScalarAsync<T>(insertStr, insertParam);

            long newId = await this.GetLastID64Async();

            ret = await EasyQueryAsync<T>(newCreatedRowSelectWithIdCmd, new { id = newId });

            t = ret.Single();
        }

        return t;
    }

    public async Task<IEnumerable<T>> EasyQueryAsync<T>(string commandStr, object? param = null)
    {
        //var ret = await Connection.QueryAsync<T>(await SetupDapperAsync(commandStr, param, typeof(T)));

        await SetupDapperAsync(param, typeof(T));
        var ret = await Connection.QueryAsync<T>(commandStr, param, Transaction, this.CommandTimeoutSecs);

        ret._TryNormalizeAll();

        return ret;
    }

    public async Task<int> EasyExecuteAsync(string commandStr, object? param = null)
    {
        await SetupDapperAsync(param);

        return await Connection.ExecuteAsync(commandStr, param, Transaction, CommandTimeoutSecs);
    }

    public async Task<T> ExecuteScalarAsync<T>(string commandStr, object? param = null)
    {
        await SetupDapperAsync(param);

        return await Connection.ExecuteScalarAsync<T>(commandStr, param, Transaction, CommandTimeoutSecs);
    }

    public IEnumerable<T> EasyQuery<T>(string commandStr, object? param = null)
        => EasyQueryAsync<T>(commandStr, param)._GetResult();

    public int EasyExecute(string commandStr, object? param = null)
        => EasyExecuteAsync(commandStr, param)._GetResult();

    public T ExecuteScalar<T>(string commandStr, object? param = null)
        => ExecuteScalarAsync<T>(commandStr, param)._GetResult();

    public async Task<T?> EasySelectSingleAsync<T>(string selectStr, object? selectParam = null, bool throwErrorIfNotFound = false, bool throwErrorIfMultipleFound = false, CancellationToken cancel = default) where T : class
    {
        IEnumerable<T> ret = await EasySelectAsync<T>(selectStr, selectParam, false, cancel);

        if (throwErrorIfNotFound && ret.Count() == 0)
        {
            throw new CoresLibException($"throwErrorIfNotFound == true and no elements returned from the database.");
        }

        if (throwErrorIfMultipleFound && ret.Count() >= 2)
        {
            throw new CoresLibException($"throwErrorIfMultipleFound == true and {ret.Count()} elements returned from the database.");
        }

        return ret.FirstOrDefault();
    }

    public async Task<IEnumerable<T>> EasySelectAsync<T>(string selectStr, object? selectParam = null, bool throwErrorIfNotFound = false, CancellationToken cancel = default) where T : class
    {
        if (selectParam == null) selectParam = new { };

        await EnsureOpenAsync(cancel);
        EnsureDapperTypeMapping(typeof(T));

        var ret = await EasyQueryAsync<T>(selectStr, selectParam);

        if (throwErrorIfNotFound && ret.Count() == 0)
        {
            throw new CoresLibException($"throwErrorIfNotFound == true and no elements returned from the database.");
        }

        return ret;
    }

    public IEnumerable<T> EasySelect<T>(string selectStr, object? selectParam = null, bool throwErrorIfNotFound = false, CancellationToken cancel = default) where T : class
        => EasySelectAsync<T>(selectStr, selectParam, throwErrorIfNotFound, cancel)._GetResult();

    public async Task<T?> EasyGetAsync<T>(dynamic id, bool throwErrorIfNotFound = true, CancellationToken cancel = default) where T : class
    {
        await EnsureOpenAsync(cancel);
        EnsureDapperTypeMapping(typeof(T));

        var ret = await SqlMapperExtensions.GetAsync<T>(Connection, id, Transaction);

        if (throwErrorIfNotFound && ret == null)
        {
            throw new CoresLibException($"throwErrorIfNotFound == true and no elements returned from the database.");
        }

        BasicHelper._TryNormalize(ret);

        return ret;
    }

    public async Task<IEnumerable<T>> EasyGetAllAsync<T>(bool throwErrorIfNotFound = false, CancellationToken cancel = default) where T : class
    {
        await EnsureOpenAsync(cancel);
        EnsureDapperTypeMapping(typeof(T));

        var ret = await SqlMapperExtensions.GetAllAsync<T>(Connection, Transaction);

        ret._TryNormalizeAll();

        if (throwErrorIfNotFound && ret.Count() == 0)
        {
            throw new CoresLibException($"throwErrorIfNotFound == true and no elements returned from the database.");
        }

        return ret;
    }


    public async Task<int> EasyInsertAsync<T>(T data, CancellationToken cancel = default) where T : class
    {
        await EnsureOpenAsync(cancel);
        EnsureDapperTypeMapping(typeof(T));

        data._TryNormalize();

        return await SqlMapperExtensions.InsertAsync<T>(Connection, data, Transaction);
    }

    public async Task<bool> EasyUpdateAsync<T>(T data, bool throwErrorIfNotFound = true, CancellationToken cancel = default) where T : class
    {
        await EnsureOpenAsync(cancel);
        EnsureDapperTypeMapping(typeof(T));

        data._TryNormalize();

        bool ret = await SqlMapperExtensions.UpdateAsync<T>(Connection, data, Transaction);

        if (throwErrorIfNotFound && ret == false)
            throw new KeyNotFoundException();

        return ret;
    }

    public async Task<bool> EasyDeleteAsync<T>(T data, bool throwErrorIfNotFound = true, CancellationToken cancel = default) where T : class
    {
        await EnsureOpenAsync(cancel);
        EnsureDapperTypeMapping(typeof(T));

        data._TryNormalize();

        bool ret = await SqlMapperExtensions.DeleteAsync<T>(Connection, data, Transaction);

        if (throwErrorIfNotFound && ret == false)
        {
            throw new CoresLibException($"throwErrorIfNotFound == true and no elements returned from the database.");
        }


        return ret;
    }

    public IEnumerable<T> EasyGetAll<T>(bool throwErrorIfNotFound = false) where T : class
        => EasyGetAllAsync<T>(throwErrorIfNotFound)._GetResult();

    public int EasyInsert<T>(T data) where T : class
        => EasyInsertAsync(data)._GetResult();

    public bool EasyUpdate<T>(T data, bool throwErrorIfNotFound = false) where T : class
        => EasyUpdateAsync(data, throwErrorIfNotFound)._GetResult();

    public bool EasyDelete<T>(T data, bool throwErrorIfNotFound = false) where T : class
        => EasyDeleteAsync(data, throwErrorIfNotFound)._GetResult();

    public async Task<dynamic?> EasyFindIdAsync<T>(string selectStr, object? selectParam = null) where T : class
    {
        if (selectParam == null) selectParam = new { };

        var list = await EasyQueryAsync<T>(selectStr, selectParam);
        var entity = list.SingleOrDefault();

        if (entity == null) return null;

        Type type = typeof(T);
        var keyProperty = type.GetProperties().Where(p => p.GetCustomAttributes().OfType<KeyAttribute>().Any())
            .Concat(type.GetProperties().Where(p => p.GetCustomAttributes().OfType<ExplicitKeyAttribute>().Any()))
            .Single();
        return keyProperty.GetValue(entity);
    }

    public async Task<T> EasyFindOrInsertAsync<T>(string selectStr, object? selectParam, T newEntity) where T : class
    {
        newEntity._NullCheck(nameof(newEntity));

        newEntity._TryNormalize();

        if (selectParam == null) selectParam = new { };

        dynamic? id = await EasyFindIdAsync<T>(selectStr, selectParam);

        if (id == null)
        {
            await EasyInsertAsync(newEntity);

            Type type = typeof(T);
            var explicitKeyProperty = type.GetProperties().Where(p => p.GetCustomAttributes().OfType<ExplicitKeyAttribute>().Any()).SingleOrDefault();

            if (explicitKeyProperty != null)
            {
                id = explicitKeyProperty.GetValue(newEntity);
            }
            else
            {
                var autoKeyProperty = type.GetProperties().Where(p => p.GetCustomAttributes().OfType<KeyAttribute>().Any()).SingleOrDefault();
                autoKeyProperty._NullCheck();
                if (autoKeyProperty.DeclaringType == typeof(long))
                {
                    id = await this.GetLastID64Async();
                }
                else
                {
                    id = await this.GetLastIDAsync();
                }
            }
        }

        return await EasyGetAsync<T>(id, true);
    }

    public async Task<T?> EasyFindAsync<T>(string selectStr, object? selectParam = null, bool throwErrorIfNotFound = true) where T : class
    {
        if (selectParam == null) selectParam = new { };

        dynamic? id = await EasyFindIdAsync<T>(selectStr, selectParam);

        if (id == null)
        {
            if (throwErrorIfNotFound)
                throw new KeyNotFoundException();
            else
                return null;
        }

        return await EasyGetAsync<T>(id, true);
    }

    public int QueryWithNoReturn(string commandStr, params object[] args)
    {
        EnsureOpen();

        CloseQuery();
        DbCommand cmd = buildCommand(commandStr, args);

        return cmd.ExecuteNonQuery();
    }

    public async Task<int> QueryWithNoReturnAsync(string commandStr, params object[] args)
    {
        await EnsureOpenAsync();

        await CloseQueryAsync();

        DbCommand cmd = buildCommand(commandStr, args);

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> QueryWithNoReturnAsync(string commandStr, CancellationToken cancel, params object[] args)
    {
        await EnsureOpenAsync(cancel);

        await CloseQueryAsync();

        DbCommand cmd = buildCommand(commandStr, args);

        return await cmd.ExecuteNonQueryAsync(cancel);
    }

    public DatabaseValue QueryWithValue(string commandStr, params object[] args)
    {
        EnsureOpen();

        CloseQuery();
        DbCommand cmd = buildCommand(commandStr, args);

        return new DatabaseValue(cmd.ExecuteScalar());
    }

    public async Task<DatabaseValue> QueryWithValueAsync(string commandStr, CancellationToken cancel, params object[] args)
    {
        await EnsureOpenAsync(cancel);

        await CloseQueryAsync();

        DbCommand cmd = buildCommand(commandStr, args);

        return new DatabaseValue(await cmd.ExecuteScalarAsync(cancel));
    }

    public async Task<DatabaseValue> QueryWithValueAsync(string commandStr, params object[] args)
    {
        await EnsureOpenAsync();

        await CloseQueryAsync();

        DbCommand cmd = buildCommand(commandStr, args);

        return new DatabaseValue(await cmd.ExecuteScalarAsync());
    }

    // 値の取得
    public DatabaseValue this[string name]
    {
        get
        {
            DataReader._NullCheck();

            object o = DataReader[name];

            return new DatabaseValue(o);
        }
    }

    // 最後に挿入した ID
    public int LastID
    {
        get
        {
            EnsureOpen();
            return (int)((decimal)this.QueryWithValue("SELECT @@@@IDENTITY").Object!);
        }
    }
    public long LastID64
    {
        get
        {
            EnsureOpen();
            return (long)((decimal)this.QueryWithValue("SELECT @@@@IDENTITY").Object!);
        }
    }

    public async Task<int> GetLastIDAsync()
    {
        await EnsureOpenAsync();
        return (int)((decimal)((await this.QueryWithValueAsync("SELECT @@@@IDENTITY")).Object!));
    }

    public async Task<long> GetLastID64Async()
    {
        await EnsureOpenAsync();
        return (long)((decimal)((await this.QueryWithValueAsync("SELECT @@@@IDENTITY")).Object!));
    }


    // すべて読み込み
    public Data ReadAllData()
    {
        return new Data(this);
    }

    public async Task<Data> ReadAllDataAsync(CancellationToken cancel = default)
    {
        Data ret = new Data();
        await ret.ReadFromDbAsync(this, cancel);
        return ret;
    }

    // 次の行の取得
    public bool ReadNext()
    {
        if (DataReader == null)
        {
            return false;
        }
        if (DataReader.Read() == false)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> ReadNextAsync(CancellationToken cancel = default)
    {
        if (DataReader == null)
        {
            return false;
        }
        if ((await DataReader.ReadAsync(cancel)) == false)
        {
            return false;
        }

        return true;
    }

    // クエリの終了
    async Task CloseQueryAsync()
    {
        if (DataReader != null)
        {
            //await DataReader.CloseAsync();
            await DataReader._DisposeSafeAsync();
            DataReader = null;
        }
    }
    void CloseQuery()
    {
        if (DataReader != null)
        {
            //DataReader.Close();
            DataReader._DisposeSafe();
            DataReader = null;
        }
    }

    // コマンドの構築
    DbCommand buildCommand(string commandStr, params object[] args)
    {
        StringBuilder b = new StringBuilder();
        int i, len, n;
        len = commandStr.Length;
        List<DbParameter> dbParams = new List<DbParameter>();

        DbCommand cmd = Connection.CreateCommand();

        n = 0;
        for (i = 0; i < len; i++)
        {
            char c = commandStr[i];

            if (c == '@')
            {
                if ((commandStr.Length > (i + 1)) && commandStr[i + 1] == '@')
                {
                    b.Append(c);
                    i++;
                }
                else
                {
                    string argName = "@ARG_" + n;
                    b.Append(argName);

                    DbParameter p = buildParameter(cmd, argName, args[n++]);
                    dbParams.Add(p);
                }
            }
            else
            {
                b.Append(c);
            }
        }

        foreach (DbParameter p in dbParams)
        {
            cmd.Parameters.Add(p);
        }

        if (Transaction != null)
            cmd.Transaction = Transaction;

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
        cmd.CommandText = b.ToString();
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
        cmd.CommandTimeout = this.CommandTimeoutSecs;

        return cmd;
    }

    // オブジェクトを SQL パラメータに変換
    DbParameter buildParameter(DbCommand cmd, string name, object o)
    {
        if (o == null)
        {
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = DBNull.Value;
            return p;
        }

        Type t = o.GetType();

        if (t == typeof(System.String))
        {
            string s = (string)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.String;
            p.Size = s.Length;
            p.Value = s;
            return p;
        }
        else if (t == typeof(System.Int16) || t == typeof(System.UInt16))
        {
            short s = (short)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.Int16;
            p.Value = s;
            return p;
        }
        else if (t == typeof(System.Byte))
        {
            byte b = (byte)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.Byte;
            p.Value = b;
            return p;
        }
        else if (t == typeof(System.Int32) || t == typeof(System.UInt32))
        {
            int i = (int)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.Int32;
            p.Value = i;
            return p;
        }
        else if (t == typeof(System.Int64) || t == typeof(System.UInt64))
        {
            long i = (long)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.Int64;
            p.Value = i;
            return p;
        }
        else if (t == typeof(System.Boolean))
        {
            bool b = (bool)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.Boolean;
            p.Value = b;
            return p;
        }
        else if (t == typeof(System.Byte[]))
        {
            byte[] b = (byte[])o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.Binary;
            p.Value = b;
            p.Size = b.Length;
            return p;
        }
        else if (t == typeof(System.DateTime))
        {
            DateTime d = (DateTime)o;
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.DateTime;
            p.Value = d;
            return p;
        }
        else if (t == typeof(System.DateTimeOffset))
        {
            DateTimeOffset d = (DateTimeOffset)o;
            d = d._NormalizeDateTimeOffset();
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.DbType = DbType.DateTimeOffset;
            p.Value = d;
            return p;
        }

        throw new ArgumentException($"Unsupported type: '{t.Name}'");
    }

    // リソースの解放
    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await CloseQueryAsync();

            await RollbackAsync();

            if (Connection != null)
            {
                IsOpened = false;
                //await Connection.CloseAsync();
                await Connection._DisposeSafeAsync();
                Connection = null!;
            }
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public delegate bool TransactionalTask();
    public delegate void TransactionalReadonlyTask();

    public delegate Task<bool> TransactionalTaskAsync();
    public delegate Task TransactionalReadOnlyTaskAsync();
    public delegate Task<T> TransactionalReadOnlyTaskAsync<T>();

    // トランザクションの実行 (匿名デリゲートを用いた再試行処理も実施)
    public void Tran(TransactionalTask task) => Tran(null, null, task);
    public void Tran(IsolationLevel? isolationLevel, TransactionalTask task) => Tran(isolationLevel, null, task);
    public void Tran(IsolationLevel? isolationLevel, DeadlockRetryConfig? retryConfig, TransactionalTask task)
    {
        EnsureOpen();
        if (retryConfig == null)
        {
            retryConfig = this.DeadlockRetryConfig;
        }

        int numRetry = 0;

        LABEL_RETRY:
        try
        {
            using (UsingTran u = this.UsingTran(isolationLevel))
            {
                if (task())
                {
                    u.Commit();
                }
            }
        }
        catch (Exception ex)
        {
            if (ex._IsDeadlockException())
            {
                // デッドロック発生
                numRetry++;
                if (numRetry <= retryConfig.RetryCount)
                {
                    int nextInterval = Util.GenRandIntervalWithRetry(retryConfig.RetryAverageInterval, numRetry, retryConfig.RetryAverageInterval * retryConfig.RetryIntervalMaxFactor, 60.0);

                    $"Deadlock retry occured. numRetry = {numRetry}. Waiting for {nextInterval} msecs. {ex.ToString()}"._Debug();

                    Kernel.SleepThread(nextInterval);

                    goto LABEL_RETRY;
                }

                throw;
            }
            else
            {
                throw;
            }
        }
    }

    public void TranReadSnapshot(TransactionalReadonlyTask task)
    {
        Tran(IsolationLevel.Snapshot, () =>
        {
            task();
            return false;
        });
    }

    public Task<bool> TranAsync(TransactionalTaskAsync task) => TranAsync(null, null, task, default);
    public Task<bool> TranAsync(IsolationLevel? isolationLevel, TransactionalTaskAsync task) => TranAsync(isolationLevel, null, task, default);
    public async Task<bool> TranAsync(IsolationLevel? isolationLevel, DeadlockRetryConfig? retryConfig, TransactionalTaskAsync task, CancellationToken cancel = default)
    {
        await EnsureOpenAsync();
        if (retryConfig == null)
        {
            retryConfig = this.DeadlockRetryConfig;
        }

        int numRetry = 0;

        LABEL_RETRY:
        try
        {
            await using (UsingTran u = await this.UsingTranAsync(isolationLevel, cancel))
            {
                if (await task())
                {
                    await u.CommitAsync(cancel);

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            //sqlex._Debug();
            if (ex._IsDeadlockException())
            {
                // デッドロック発生
                numRetry++;
                if (numRetry <= retryConfig.RetryCount)
                {
                    int nextInterval = Util.GenRandIntervalWithRetry(retryConfig.RetryAverageInterval, numRetry, retryConfig.RetryAverageInterval * retryConfig.RetryIntervalMaxFactor, 60.0);

                    $"Deadlock retry occured. numRetry = {numRetry}. Waiting for {nextInterval} msecs. {ex.ToString()}"._Debug();

                    await Task.Delay(nextInterval);

                    goto LABEL_RETRY;
                }

                throw;
            }
            else
            {
                throw;
            }
        }
    }

    public async Task TranReadSnapshotAsync(TransactionalReadOnlyTaskAsync task)
    {
        await TranAsync(IsolationLevel.Snapshot, async () =>
        {
            await task();
            return false;
        });
    }

    public async Task<T> TranReadSnapshotAsync<T>(TransactionalReadOnlyTaskAsync<T> task)
    {
        T? ret = default;

        await TranAsync(IsolationLevel.Snapshot, async () =>
        {
            ret = await task();
            return false;
        });

        return ret!;
    }

    public async Task TranReadSnapshotIfNecessaryAsync(TransactionalReadOnlyTaskAsync task)
    {
        if (this.Transaction == null)
        {
            await TranAsync(IsolationLevel.Snapshot, async () =>
            {
                await task();

                return false;
            });
        }
        else
        {
            await task();
        }
    }

    public async Task<T> TranReadSnapshotIfNecessaryAsync<T>(TransactionalReadOnlyTaskAsync<T> task)
    {
        T? ret = default;

        if (this.Transaction == null)
        {
            await TranAsync(IsolationLevel.Snapshot, async () =>
            {
                ret = await task();
                return false;
            });
        }
        else
        {
            ret = await task();
        }

        return ret!;
    }

    // トランザクションの開始 (UsingTran オブジェクト作成)
    public UsingTran UsingTran(IsolationLevel? isolationLevel = null)
    {
        UsingTran t = new UsingTran(this);

        Begin(isolationLevel);

        return t;
    }

    // トランザクションの開始
    public void Begin(IsolationLevel? isolationLevel = null)
    {
        EnsureOpen();

        CloseQuery();

        if (isolationLevel == IsolationLevel.Unspecified)
        {
            Transaction = Connection.BeginTransaction();
        }
        else
        {
            Transaction = Connection.BeginTransaction(isolationLevel ?? this.DefaultIsolationLevel);
        }
    }

    // トランザクションの開始 (UsingTran オブジェクト作成)
    public async Task<UsingTran> UsingTranAsync(IsolationLevel? isolationLevel = null, CancellationToken cancel = default)
    {
        UsingTran t = new UsingTran(this);

        await BeginAsync(isolationLevel, cancel);

        return t;
    }

    // トランザクションの開始
    public async Task BeginAsync(IsolationLevel? isolationLevel = null, CancellationToken cancel = default)
    {
        await EnsureOpenAsync(cancel);

        await CloseQueryAsync();

        if (isolationLevel == IsolationLevel.Unspecified)
        {
            Transaction = await Connection.BeginTransactionAsync(cancel);
        }
        else
        {
            Transaction = await Connection.BeginTransactionAsync(isolationLevel ?? this.DefaultIsolationLevel, cancel);
        }
    }

    // トランザクションのコミット
    public void Commit()
    {
        if (Transaction == null)
        {
            return;
        }

        CloseQuery();
        Transaction.Commit();
        Transaction.Dispose();
        Transaction = null;
    }
    public async Task CommitAsync(CancellationToken cancel = default)
    {
        if (Transaction == null)
        {
            return;
        }

        await CloseQueryAsync();

        await Transaction.CommitAsync(cancel);

        await Transaction._DisposeSafeAsync();
        Transaction = null;
    }

    // トランザクションのロールバック
    public void Rollback()
    {
        if (Transaction == null)
        {
            return;
        }

        CloseQuery();
        try
        {
            Transaction.Dispose();
        }
        catch (Exception ex)
        {
            // Rollback 時に System.InvalidCastException: Unable to cast object of type 'Microsoft.Data.SqlClient.SqlDataReader' to type 'Microsoft.Data.SqlClient.SqlTransaction'.
            // という謎のエラーが Microsoft.Data.SqlClient.SqlInternalTransaction ライブラリの CheckTransactionLevelAndZombie() -> Zombie() -> ZombieParent()
            // で発生することがある。これはおそらく Microsoft のライブラリのバグであるが、これが発生した場合は無視する必要がある。
            ex._Error();
        }
        //Transaction._DisposeSafe();
        Transaction = null;
    }
    public async Task RollbackAsync(CancellationToken cancel = default)
    {
        if (Transaction == null)
        {
            return;
        }

        await CloseQueryAsync();

        try
        {
            await Transaction.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Rollback 時に System.InvalidCastException: Unable to cast object of type 'Microsoft.Data.SqlClient.SqlDataReader' to type 'Microsoft.Data.SqlClient.SqlTransaction'.
            // という謎のエラーが Microsoft.Data.SqlClient.SqlInternalTransaction ライブラリの CheckTransactionLevelAndZombie() -> Zombie() -> ZombieParent()
            // で発生することがある。これはおそらく Microsoft のライブラリのバグであるが、これが発生した場合は無視する必要がある。
            ex._Error();
        }

        //await Transaction._DisposeSafeAsync();
        Transaction = null;
    }

    public static bool IsDeadlockException(Exception? ex)
    {
        if (ex == null) return false;

        if (ex is SqlException sql)
        {
            if (sql.Number == 1205)
            {
                return true;
            }
        }

        return false;
    }
}

#endif // CORES_BASIC_DATABASE


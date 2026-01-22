// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Description

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
//using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace IPA.Cores.Basic;

public class ShellResult
{
    public string CommandLine = "";
    public List<string> StringList = new List<string>();
    public int ExitCode = 0;

    public void ThrowIfError(string prefix = "")
    {
        if (ExitCode != 0)
        {
            string tmp = "";
            if (prefix._IsFilled()) tmp = $"{prefix}: ";

            throw new CoresLibException($"{tmp}Exit code = {ExitCode}, CommandLine = {CommandLine._OneLine(" / ")._TruncStrEx(Consts.MaxLens.NormalStringTruncateLen)}, Results = {StringList._Combine(" / ")._TruncStrEx(Consts.MaxLens.NormalStringTruncateLen)}");
        }
    }
}

public class ShellProcessorSettings
{
    public Encoding Encoding { get; set; } = Str.Utf8Encoding;
    public int RecvTimeout { get; set; } = Consts.Timeouts.DefaultShellPromptRecvTimeout;
    public int SendTimeout { get; set; } = Consts.Timeouts.DefaultShellPromptSendTimeout;
    public string SendNewLineStr { get; set; } = Str.NewLine_Str_Unix;
    public bool PrintDebug { get; set; } = false;

    public string TargetHostName { get; set; } = "";
}

public class ShellProcessor : AsyncService
{
    public ShellClientSock Sock { get; }
    public ShellProcessorSettings Settings { get; }
    bool DisposeObject { get; }

    BinaryLineReader Reader { get; }

    public ShellProcessor(ShellClientSock sock, bool disposeObject = false, ShellProcessorSettings? settings = null)
    {
        try
        {
            if (settings == null) settings = new ShellProcessorSettings();

            this.Sock = sock;
            this.DisposeObject = disposeObject;
            this.Settings = settings;

            this.Reader = new BinaryLineReader(this.Sock.Stream);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            if (this.DisposeObject)
            {
                await this.Sock._DisposeSafeAsync();
            }
        }
        catch
        {
            await base.CleanupImplAsync(ex);
        }
    }

    protected string MemoryToString(MemoryStream ms)
    {
        return Settings.Encoding.GetString(ms.ToArray());
    }

    protected async Task<List<string>> ReadLinesInternalAsync(Func<MemoryStream, object?, bool>? finishDeterminer = null, CancellationToken cancel = default)
    {
        this.Sock.Stream.ReadTimeout = Settings.RecvTimeout;

        BinaryLineReaderOption opt = new BinaryLineReaderOption(finishDeterminer, (ms, p, c) =>
        {
            string previewLine = MemoryToString(ms);

            string tmp = "";
            string? hostname = this.Settings.TargetHostName._Split(StringSplitOptions.RemoveEmptyEntries, ".").FirstOrDefault();
            if (hostname._IsFilled())
            {
                tmp = hostname + ":";
            }

            if (Settings.PrintDebug)
            {
                Con.WriteInfo($"[{tmp}RX] {previewLine}");
            }
        });

        RefBool isEof = new RefBool();

        var result = await Reader.ReadLinesUntilEofAsync(isEof, cancel: cancel, option: opt);

        if (result == null || isEof)
        {
            // 期待されたプロンプト等が現われず EOF に達してしまったので、途中のごみは出さずに切断されたことにする
            await this.Sock.DisconnectAsync();
            throw new DisconnectedException();
        }

        return result._ToStringList(this.Settings.Encoding);
    }

    protected async Task SendLineInternalAsync(string line, CancellationToken cancel = default)
    {
        this.Sock.Stream.WriteTimeout = Settings.SendTimeout;

        line = line._GetLines(false)._LinesToStr(Settings.SendNewLineStr);

        if (line == "") line = Settings.SendNewLineStr;

        string tmp = "";
        string? hostname = this.Settings.TargetHostName._Split(StringSplitOptions.RemoveEmptyEntries, ".").FirstOrDefault();

        if (hostname._IsFilled())
        {
            tmp = hostname + ":";
        }

        if (Settings.PrintDebug)
        {
            foreach (var oneLine in line._GetLines())
            {
                Con.WriteInfo($"[{tmp}TX] {oneLine}");
            }
        }

        await this.Sock.Stream.SendAsync(this.Settings.Encoding.GetBytes(line), cancel);
        await this.Sock.Stream.FlushAsync(cancel);
    }
}

public class UnixShellProcessorSettings : ShellProcessorSettings
{
}

public class UnixShellProcessor : ShellProcessor
{
    public new ShellProcessorSettings Settings => (ShellProcessorSettings)base.Settings;

    public UnixShellProcessor(ShellClientSock sock, bool disposeObject = false, UnixShellProcessorSettings? settings = null)
        : base(sock, disposeObject, settings ?? new UnixShellProcessorSettings())
    {
        try
        {
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    Once BashInited;

    string BashPromptIdStrPlainText = null!;
    string BashPromptIdStrExportPs1 = null!;

    string BashNextInlinePromptStrPlainText = null!;

    // bash 上でコマンドを実行し、その結果を返す
    public async Task<ShellResult> ExecBashCommandAsync(string cmdLine, bool throwIfError = true, CancellationToken cancel = default)
    {
        string? multiLineEof = null;

        string cmdLineForResult;

        string[] tmp = cmdLine._GetLines(true);
        if (tmp.Length <= 0)
        {
            // 何も実行しません
            return new ShellResult { CommandLine = cmdLine, ExitCode = 0, StringList = new List<string>() };
        }
        if (tmp.Length >= 2)
        {
            // 2 行以上の場合
            string contents = cmdLine._GetLines(false)._LinesToStr(Settings.SendNewLineStr);
            StringWriter w = new StringWriter();
            multiLineEof = "EOF_" + Str.GenRandStr().Substring(16);
            w.NewLine = Settings.SendNewLineStr;
            w.WriteLine($"bash << \\{multiLineEof}");
            w.WriteLine(contents);
            w.WriteLine(multiLineEof);
            cmdLine = w.ToString();
            cmdLineForResult = contents;
        }
        else
        {
            // 1 行のみの場合
            cmdLine = tmp[0];
            cmdLineForResult = cmdLine;
        }

        string exitCodeBefore = Str.GenRandStr().Substring(16);

        StringBuilder exitCodeBeforeHex = new StringBuilder();
        foreach (char c in exitCodeBefore)
        {
            exitCodeBeforeHex.Append("\\x");
            exitCodeBeforeHex.Append(((uint)c).ToString("X2"));
        }

        string printExitCodeCommand = $"echo -e \"{exitCodeBeforeHex} exitcode = $?\"";

        await EnsureInitBashAsync(cancel);

        // コマンド送信
        if (multiLineEof._IsEmpty())
        {
            // 1 行コマンド
            await SendLineInternalAsync(cmdLine, cancel);
        }
        else
        {
            // 2 行以上のコマンド
            foreach (string oneLine in cmdLine._GetLines())
            {
                $"oneLine = {oneLine}"._Print();

                // 1 行ずつ送信する
                await SendLineInternalAsync(oneLine, cancel);

                if (oneLine != multiLineEof)
                {
                    // 途中行の場合、インライン入力プロンプト (PS2) が返ってくるまで待つ
                    await RecvLinesUntilSpecialPs2PromptAsync(cancel);
                }
            }
        }

        // コマンド実行結果とプロンプトが返ってくるまでのすべての結果を得る
        List<string> retStrList = await RecvLinesUntilSpecialPs1PromptAsync(cancel);

        if (multiLineEof._IsFilled())
        {
            // 複数行の場合は、ランダム EOF 文字が含む行が 1 個出現する点までを削除
            List<string> tmp2 = new List<string>();
            int nums = 0;
            foreach (string line in retStrList)
            {
                if (nums >= 1)
                {
                    tmp2.Add(line);
                }

                if (line._InStr(multiLineEof))
                {
                    nums++;
                }
            }
            retStrList = tmp2;
        }

        if (retStrList.Count >= 1 && retStrList[0]._IsSameTrim(cmdLine))
        {
            retStrList.RemoveAt(0);
        }

        // 終了コード取得コマンド送信
        await SendLineInternalAsync(printExitCodeCommand, cancel);

        // 終了コードとプロンプトが返ってくるまでのすべての結果を得る
        List<string> exitCodeStrList = await RecvLinesUntilSpecialPs1PromptAsync(cancel);

        // 終了コードのパース
        int? exitCode = null;
        foreach (string line in exitCodeStrList)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');
            if (tokens.Length == 4)
            {
                if (tokens[0] == exitCodeBefore && tokens[1] == "exitcode" && tokens[2] == "=")
                {
                    exitCode = tokens[3]._ToInt();
                    break;
                }
            }
        }

        if (exitCode.HasValue == false)
        {
            throw new CoresLibException($"Failed to parse the exit code of the command.");
        }

        ShellResult result = new ShellResult()
        {
            StringList = retStrList,
            ExitCode = exitCode.Value,
            CommandLine = cmdLineForResult,
        };

        if (throwIfError)
        {
            result.ThrowIfError(Settings.TargetHostName);
        }

        return result;
    }

    // bash を初期化する
    public async Task EnsureInitBashAsync(CancellationToken cancel = default)
    {
        if (BashInited.IsSet == false)
            await InitBashAsync(cancel);
    }

    public async Task InitBashAsync(CancellationToken cancel = default)
    {
        if (BashInited.IsFirstCall() == false) throw new CoresLibException("InitBashAsync is already called.");

        // プロンプト文字列の生成
        string randstr = "prompt_" + Str.GenRandStr().ToLowerInvariant().Substring(0, 12);
        BashPromptIdStrExportPs1 = "export PS1=\"\\133" + randstr + "\\041\\!\\041\\w\\041\\u\\135\"";
        BashPromptIdStrPlainText = "[" + randstr + "!";

        randstr = "inline_" + Str.GenRandStr().ToLowerInvariant().Substring(0, 12);
        BashNextInlinePromptStrPlainText = "[" + randstr + "!";
        string nextInlineExportPs1 = "export PS2=\"\\133" + randstr + "\\041\\!\\041\\w\\041\\u\\135 \"";

        // 最初のプロンプトを待つ
        await RecvLinesUntilBasicUnixPromptAsync(cancel);

        // "bash" を実行する
        await SendLineInternalAsync("bash", cancel);

        // bash 起動後のプロンプトを待つ
        await RecvLinesUntilBasicUnixPromptAsync(cancel);

        // PS1 文字列を設定する
        await SendLineInternalAsync(BashPromptIdStrExportPs1, cancel);

        // PS1 設定後のプロンプトを待つ
        await RecvLinesUntilSpecialPs1PromptAsync(cancel);

        // PS2 文字列を設定する
        await SendLineInternalAsync(nextInlineExportPs1, cancel);

        // PS2 設定後のプロンプトを待つ
        await RecvLinesUntilSpecialPs1PromptAsync(cancel);
    }

    // PS1 環境変数で指定されている特殊なプロンプトが出てくるまで行を読み進める
    public async Task<List<string>> RecvLinesUntilSpecialPs1PromptAsync(CancellationToken cancel = default)
    {
        List<string> ret = await this.ReadLinesInternalAsync(UnixSpecialPs1PromptDeterminer, cancel);

        List<string> ret2 = new List<string>();

        foreach (string line in ret)
        {
            // プロンプトそのものは除外
            if (line._InStr(BashPromptIdStrPlainText) == false)
            {
                ret2.Add(line);
            }
        }

        return ret2;
    }

    // PS2 環境変数で指定されている特殊なプロンプトが出てくるまで行を読み進める
    public async Task<List<string>> RecvLinesUntilSpecialPs2PromptAsync(CancellationToken cancel = default)
    {
        List<string> ret = await this.ReadLinesInternalAsync(UnixSpecialPs2PromptDeterminer, cancel);

        List<string> ret2 = new List<string>();

        foreach (string line in ret)
        {
            // プロンプトそのものは除外
            if (line._InStr(BashPromptIdStrPlainText) == false)
            {
                ret2.Add(line);
            }
        }

        return ret2;
    }

    // UNIX 用の標準的なプロンプトが出てくるまで行を読み進める
    public async Task<List<string>> RecvLinesUntilBasicUnixPromptAsync(CancellationToken cancel = default)
    {
        return await this.ReadLinesInternalAsync(BasicUnixPromptDeterminer, cancel);
    }

    // PS1 プロンプトの行終了識別関数
    protected virtual bool UnixSpecialPs1PromptDeterminer(MemoryStream ms, object? param)
    {
        string str = MemoryToString(ms);

        if (str._InStr(BashPromptIdStrPlainText))
        {
            return true;
        }

        return false;
    }

    // PS2 プロンプトの行終了識別関数
    protected virtual bool UnixSpecialPs2PromptDeterminer(MemoryStream ms, object? param)
    {
        string str = MemoryToString(ms);

        if (str._InStr(BashNextInlinePromptStrPlainText))
        {
            return true;
        }

        return false;
    }

    // UNIX 用の標準的なシェルの行終了識別関数
    // "#", "$", "# ", "$ " のいずれかで終わるとプロンプトとみなす
    protected virtual bool BasicUnixPromptDeterminer(MemoryStream ms, object? param)
    {
        string str = MemoryToString(ms);

        if (str.EndsWith("#") ||
            str.EndsWith("$") ||
            str.EndsWith("# ") ||
            str.EndsWith("$ "))
        {
            return true;
        }

        return false;
    }
}

#endif


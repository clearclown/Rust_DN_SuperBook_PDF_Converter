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

using System;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Data;

namespace IPA.TestDev;

partial class TestDevCommands
{
    [ConsoleCommand(
        "Test command",
        "Test [arg]",
        "This is a test command.",
        "[arg]:You can specify an argument.")]
    static int Test(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[arg]", null, null, null, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        TestClass.Test();

        return 0;
    }

    [ConsoleCommand(
    "DownloadGitLabCePackages command",
    "DownloadGitLabCePackages [searchBaseUrl] /dir:dest_dir_path /versions:16.8.1,16.8.10",
    "DownloadGitLabCePackages command")]
    public static async Task<int> DownloadGitLabCePackages(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[searchBaseUrl]", ConsoleService.Prompt, "Search Base URL: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dir", ConsoleService.Prompt, "Dest dir path: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("versions", ConsoleService.Prompt, "Versions: ", ConsoleService.EvalNotEmpty, null),
            };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        var rootDirPath = new DirectoryPath(vl["dir"].StrValue);

        await using var downloader = new GitLabDownloader();

        var pageInfoList = await downloader.EnumeratePackagePagesAsync(new(vl.DefaultParam.StrValue, vl["versions"].StrValue._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, ",", "/", " ", "\t", ";")));

        int numOk = 0;
        int numError = 0;
        long totalSize = 0;

        Con.WriteLine(pageInfoList._PrintAsJson());

        foreach (var pageInfo in pageInfoList)
        {
            try
            {
                var fileinfo = (await downloader.GetDownloadUrlFromPageAsync(pageInfo));

                var uri = fileinfo.Url._ParseUrl();
                string tmp1 = uri.AbsolutePath;
                int j = tmp1._Search(fileinfo.FileName);
                if (j != -1) tmp1 = tmp1.Substring(0, j);
                string tmp2 = tmp1._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.RemoveEmptyEntries, "/")._Combine("_");
                tmp2 = PP.MakeSafeFileName(tmp2, true, true, true);
                if (tmp2._IsEmpty()) tmp2 = "_unknown_dir_";

                var destDir = rootDirPath.GetSubDirectory(PPWin.MakeSafeFileName(fileinfo.SpecifiedVersion, true, true, true), tmp2);

                RefLong size = new();
                var result = await downloader.DownloadPackageFileAsync(destDir, fileinfo, size);

                totalSize += size.Value;

                numOk++;
            }
            catch (Exception ex)
            {
                Con.WriteError($"Error: '{pageInfo.Url}'");
                ex._Error();

                numError++;
            }
        }

        Con.WriteError(
            $"Result: Num_OK: {numOk}, NumError: {numError}, Total Size = {totalSize._ToString3()} bytes.");

        return 0;
    }

    [ConsoleCommand(
        "JunkSample command",
        "JunkSample [filename] /abc:def",
        "JunkSample command")]
    static int JunkSample(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("abc", ConsoleService.Prompt, "abc: ", ConsoleService.EvalNotEmpty, null),
            };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        return 0;
    }

    [ConsoleCommand(
        "PrependLineNumber command",
        "PrependLineNumber [srcFile] [/DEST:destfile]",
        "Prepend Line Number.")]
    static int PrependLineNumber(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[srcFile]", ConsoleService.Prompt, "Src File Path: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("DEST", ConsoleService.Prompt, "Dest File Path: ", ConsoleService.EvalNotEmpty, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string src = vl.DefaultParam.StrValue;
        string dest = vl["DEST"].StrValue;

        if (src._IsSamei(dest))
        {
            Con.WriteError("Source is Dest.");
            return 1;
        }

        var body = Lfs.ReadStringFromFile(src);

        string[] lines = body._GetLines(singleLineAtLeast: true);

        int keta = lines.Length.ToString().Length;

        StringWriter w = new StringWriter();

        for (int i = 0; i < lines.Length; i++)
        {
            string lineStr = (i + 1).ToString("D" + keta);
            string tmp = lineStr + ": " + lines[i];

            w.WriteLine(tmp);
        }

        Lfs.WriteStringToFile(dest, w.ToString(), writeBom: true, flags: FileFlags.AutoCreateDirectory);

        return 0;
    }

    [ConsoleCommand(
        "PrintRegKeys",
        "PrintRegKeys [root] /key:[baseKey]",
        "PrintRegKeys")]
    static int PrintRegKeys(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[root]", null, null, null, null),
            new ConsoleParam("key", null, null, null, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string root = vl.DefaultParam.StrValue;
        string key = vl["key"].StrValue;

        if (Env.IsWindows == false)
        {
            Con.WriteLine("Windows required.");
            return 0;
        }

        if (root._IsEmpty() || key._IsEmpty())
        {
            Con.WriteLine("Canceled.");
            return Consts.ExitCodes.GenericError;
        }

        var root2  = RegRoot.LocalMachine.ParseAsDefault(root);

        MsReg.PrintRegKeys(root2, key);

        return 0;
    }



    static int Seed_TestHadbSuite = 0;

    [ConsoleCommand(
        "Execute HADB Test Suite",
        "TestHadbSuite [/THREADS:num_threads=10] [/LOOP1=loop1_in_thread=3] [/LOOP2=loop2_whole=1]",
        "Execute HADB Test Suite")]
    static int TestHadbSuite(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("THREADS"),
            new ConsoleParam("LOOP1"),
            new ConsoleParam("LOOP2"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        int numThreads = vl["THREADS"].IntValue;
        int loop1 = vl["LOOP1"].IntValue;
        int loop2 = vl["LOOP2"].IntValue;

        if (numThreads <= 0) numThreads = 10;
        if (loop1 <= 0) loop1 = 3;
        if (loop2 <= 0) loop2 = 1;

        const string TestDbServer = "dn-mssql2019dev1.ipantt.net,7012"; // dn-mssql2019dev1
        const string TestDbName = "HADB001";
        const string TestDbReadUser = "sql_hadb001_reader";
        const string TestDbWriteUser = "sql_hadb001_writer";
        const string TestDbReadPassword = "DnTakosanPass8931Dx";
        const string TestDbWritePassword = "DnTakosanPass8931Dx";


        for (int k = 0; k < loop2; k++)
        {
            try
            {
                for (int i = 0; i < loop1; i++)
                {
                    $"=========== try i = {i} ============="._Print();

                    bool error = false;

                    Async(async () =>
                    {
                        AsyncManualResetEvent start = new AsyncManualResetEvent();
                        List<Task> taskList = new List<Task>();

                        for (int i = 0; i < numThreads; i++)
                        {
                            var task = TaskUtil.StartAsyncTaskAsync(async () =>
                            {
                                await Task.Yield();
                                await start.WaitAsync();

                                try
                                {
                                    int seed = Interlocked.Increment(ref Seed_TestHadbSuite);

                                    string systemName = ("HADB_CODE_TEST_" + Str.DateTimeToYymmddHHmmssLong(DtNow) + "_" + Env.MachineName + "_" + Str.GenerateRandomDigit(8) + "_" + seed.ToString("D8")).ToUpperInvariant();

                                    systemName = "" + (char)('A' + Secure.RandSInt31() % 26) + "_" + systemName;

                                    var flags = HadbOptionFlags.NoAutoDbReloadAndUpdate | HadbOptionFlags.BuildFullTextSearchText;

                                    flags |= HadbOptionFlags.NoLocalBackup;

                                    flags |= HadbOptionFlags.DataUidForPartitioningByUidOptimized;

                                    if (numThreads >= 2)
                                    {
                                        flags |= HadbOptionFlags.NoInitConfigDb | HadbOptionFlags.NoInitSnapshot | HadbOptionFlags.DoNotTakeSnapshotAtAll | HadbOptionFlags.DoNotSaveStat;
                                    }

                                    HadbSqlSettings settings = new HadbSqlSettings(systemName,
                                        new SqlDatabaseConnectionSetting(TestDbServer, TestDbName, TestDbReadUser, TestDbReadPassword, true),
                                        new SqlDatabaseConnectionSetting(TestDbServer, TestDbName, TestDbWriteUser, TestDbWritePassword, true),
                                        IsolationLevel.Snapshot, IsolationLevel.Serializable,
                                        flags);

                                    await HadbCodeTest.Test1Async(settings, systemName, flags.Bit(HadbOptionFlags.NoLocalBackup) == false, numThreads >= 2);
                                }
                                catch (Exception ex)
                                {
                                    "--------------------- ERROR !!! ---------------"._Error();
                                    ex._Error();
                                    throw;
                                }
                            }
                            );

                            taskList.Add(task);
                        }

                        start.Set(true);

                        foreach (var task in taskList)
                        {
                            var ret = await task._TryAwaitAndRetBool();
                            if (ret.IsError) error = true;
                        }
                    });

                    if (error)
                    {
                        throw new CoresException("Error occured.");
                    }
                }

                $"--- Whole Loop #{loop2}: All OK! ---"._Print();
            }
            catch (Exception ex)
            {
                $"--- Whole Loop #{loop2}: Error! ---"._Print();
                ex._Error();
                $"--- Whole Loop #{loop2}: Error! ---"._Print();
                throw;
            }
        }

        return 0;
    }



    [ConsoleCommand(
        "Execute SSL Test Suite",
        "SslTestSuite [host:port|self] [/parallel:num=1] [/interval:msecs=0] [/ignore:ignore_list] [/expectedcertstr:sni1=str1,str2;sni1=str3,str4,...]",
        "Execute SSL Test Suite")]
    static int SslTestSuite(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[host:port]", ConsoleService.Prompt, "<Host:port>: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("parallel"),
            new ConsoleParam("interval"),
            new ConsoleParam("ignore"),
            new ConsoleParam("expectedcertstr"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string hostAndPort = vl.DefaultParam.StrValue;
        int parallel = vl["parallel"].IntValue;
        int interval = vl["interval"].IntValue;
        string ignList = vl["ignore"].StrValue;
        string expectedcertstr = vl["expectedcertstr"].StrValue;

        EnvInfoSnapshot envInfo = new EnvInfoSnapshot();

        envInfo._PrintAsJson();


        if (Env.IsMac)
        {
            Con.WriteLine();
            Con.WriteLine("Mac OS is not suppoerted. Skip.");
            return 0;
        }

        bool ret = false;

        Async(async () =>
        {
            ret = await LtsOpenSslTool.TestSuiteAsync(hostAndPort, parallel, interval, ignList, default, expectedcertstr);
        });

        if (ret == false)
        {
            Con.WriteLine();
            Con.WriteLine("Error occured.");
            return 1;
        }

        return 0;
    }

    [ConsoleCommand(
        "Execute SecureCompressTest Suite",
        "SecureCompressTest",
        "Execute SecureCompressTest Suite")]
    static int SecureCompressTest(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        Cores.Basic.SecureCompresTest.DoTestAsync()._GetResult();

        ""._Print();
        $"Test Ok."._Print();

        return 0;
    }

    [ConsoleCommand(
    "Normalize Src Code Text with BOM and UTF-8",
    "NormalizeSrcCodeText [dir]",
    "Normalize Src Code Text with BOM and UTF-8",
    "[dir]: Dir name")]
    static int NormalizeSrcCodeText(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory name: ", ConsoleService.EvalNotEmpty, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dir = vl.DefaultParam.StrValue;

        DirectoryWalker walk = new DirectoryWalker();

        walk.WalkDirectory(dir,
            (pathInfo, entities, cancel) =>
            {
                foreach (FileSystemEntity entity in entities)
                {
                    if (entity.IsDirectory == false && entity.Name._MultipleWildcardMatch(Consts.Extensions.Wildcard_SourceCode_NormalizeBomUtf8_Include, Consts.Extensions.Wildcard_SourceCode_NormalizeBomUtf8_Exclude))
                    {
                        var body = pathInfo.FileSystem.ReadDataFromFile(entity.FullPath);

                        string str = body._GetString();

                        str = str._NormalizeCrlf(CrlfStyle.CrLf);

                        pathInfo.FileSystem.WriteStringToFile(entity.FullPath, str, FileFlags.WriteOnlyIfChanged, encoding: Str.Utf8Encoding, writeBom: true);
                    }
                }
                return true;
            },
            exceptionHandler: (pathInfo, err, cancel) =>
            {
                return true;
            });

        return 0;
    }

    [ConsoleCommand(
        "IPA NTT East Source Code Header Setter",
        "IpaNttSrcHeaderSet [dir]",
        "IPA NTT East Source Code Header Setter.",
        "[dir]: Dir name")]
    static int IpaNttSrcHeaderSet(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory name: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dir = vl.DefaultParam.StrValue;

        DirectoryWalker walk = new DirectoryWalker();

        string existingCommentLine = "// Copyright (c) IPA CyberLab of Industrial Cyber Security Center.";
        string existingCommentLineOld = "// Copyright (c) NTT East Special Affairs Bureau.";
        string newCommentLine = "// Copyright (c) NTT-East Impossible Telecom Mission Group.";

        string old1 = "// AGAINST US (IPA CYBERLAB, SOFTETHER PROJECT, SOFTETHER CORPORATION, DAIYUU NOBORI";
        string new1 = "// AGAINST US (IPA, NTT-EAST, SOFTETHER PROJECT, SOFTETHER CORPORATION, DAIYUU NOBORI";

        string old2 = "// COMPLETELY AT YOUR OWN RISK. THE IPA CYBERLAB HAS DEVELOPED AND";
        string new2 = "// COMPLETELY AT YOUR OWN RISK. IPA AND NTT-EAST HAS DEVELOPED AND";

        walk.WalkDirectory(dir,
            (pathInfo, entities, cancel) =>
            {
                foreach (FileSystemEntity entity in entities)
                {
                    if (entity.IsDirectory == false && entity.Name._IsExtensionMatch(Consts.Extensions.Filter_SourceCodes))
                    {
                        var body = pathInfo.FileSystem.ReadDataFromFile(entity.FullPath);

                        string str = body._GetString();
                        string[] lines = str._GetLines();

                        bool updated = false;
                        bool ok = true;

                        List<string> tmp = new List<string>();

                        foreach (var line2 in lines)
                        {
                            string line = line2;

                            if (line._IsSameiTrim(old1))
                            {
                                line = new1;
                                updated = true;
                            }
                            if (line._IsSameiTrim(old2))
                            {
                                line = new2;
                                updated = true;
                            }

                            if (line._IsSameiTrim(existingCommentLineOld) == false)
                            {
                                tmp.Add(line);

                                if (line._IsSameiTrim(existingCommentLine))
                                {
                                    tmp.Add(newCommentLine);
                                    updated = true;
                                }
                                else if (line._IsSameiTrim(newCommentLine))
                                {
                                    ok = false;
                                }
                            }
                        }

                        if (ok && updated)
                        {
                            string newStr = tmp._LinesToStr(Str.NewLine_Str_Windows);
                            entity.FullPath._Print();
                            pathInfo.FileSystem.WriteStringToFile(entity.FullPath, newStr, writeBom: true);
                        }
                    }
                }
                return true;
            },
            exceptionHandler: (pathInfo, err, cancel) =>
            {
                return true;
            });

        return 0;
    }
}


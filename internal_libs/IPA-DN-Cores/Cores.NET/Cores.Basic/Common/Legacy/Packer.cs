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
using System.Collections.Generic;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.Legacy;

public enum PackerFileFormat
{
    ZipRaw,
    ZipCompressed,
    Tar,
    TarGZip,
}

public delegate bool ProgressDelegate(string fileNameFullPath, string fileNameRelative, int currentFileNum, int totalFileNum);

public static class Packer
{
    public static byte[] PackDir(PackerFileFormat format, string topDirPath, string appendPrefixDirName, ProgressDelegate? proc = null)
    {
        // ディレクトリ列挙
        string[] fileList = Directory.GetFiles(topDirPath, "*", SearchOption.AllDirectories);
        List<string> relativeFileList = new List<string>();

        foreach (string fileName in fileList)
        {
            string relativePath = IO.GetRelativeFileName(fileName, topDirPath);

            if (Str.IsEmptyStr(appendPrefixDirName) == false)
            {
                relativePath = IO.RemoveLastEnMark(appendPrefixDirName) + "\\" + relativePath;
            }

            relativeFileList.Add(relativePath);
        }

        return PackFiles(format, fileList, relativeFileList.ToArray(), proc);
    }

    public static byte[] PackFiles(PackerFileFormat format, string[] srcFileNameList, string[] relativeNameList, ProgressDelegate? proc = null)
    {
        if (srcFileNameList.Length != relativeNameList.Length)
        {
            throw new ApplicationException("srcFileNameList.Length != relativeNameList.Length");
        }

        int num = srcFileNameList.Length;
        int i;

        ZipPacker zip = new ZipPacker();
        TarPacker tar = new TarPacker();

        for (i = 0; i < num; i++)
        {
            if (proc != null)
            {
                bool ret = proc(srcFileNameList[i], relativeNameList[i], i, num);

                if (ret == false)
                {
                    continue;
                }
            }

            byte[] srcData = IO.ReadFile(srcFileNameList[i]);
            DateTime date = File.GetLastWriteTime(srcFileNameList[i]);

            switch (format)
            {
                case PackerFileFormat.Tar:
                case PackerFileFormat.TarGZip:
                    tar.AddFileSimple(relativeNameList[i], srcData, 0, srcData.Length, date);
                    break;

                case PackerFileFormat.ZipRaw:
                case PackerFileFormat.ZipCompressed:
                    zip.AddFileSimple(relativeNameList[i], date, FileAttributes.Normal, srcData, (format == PackerFileFormat.ZipCompressed));
                    break;
            }
        }

        switch (format)
        {
            case PackerFileFormat.Tar:
                tar.Finish();
                return tar.GeneratedData.Read();

            case PackerFileFormat.TarGZip:
                tar.Finish();
                return tar.CompressToGZip();

            case PackerFileFormat.ZipCompressed:
            case PackerFileFormat.ZipRaw:
                zip.Finish();
                return zip.GeneratedData.Read();

            default:
                throw new ApplicationException("format");
        }
    }
}

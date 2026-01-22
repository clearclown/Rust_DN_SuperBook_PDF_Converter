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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Runtime.Serialization.Json;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.IO.Pipelines;
using System.Text;
//using Microsoft.EntityFrameworkCore.Internal;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class DeepCloneSettings
        {
            public static readonly Copenhagen<DeepCloneMethod> DefaultDeepCloneMethod = DeepCloneMethod.DeepCloner;
        }
    }


    namespace Legacy
    {
        // 言語一覧
        public enum CoreLanguage
        {
            Japanese = 0,
            English = 1,
        }

        // 言語クラス
        public class CoreLanguageClass
        {
            public readonly CoreLanguage Language;
            public readonly int Id;
            readonly string name;
            public string Name
            {
                get
                {
                    if (name == "ja")
                    {
                        if (CoreLanguageList.RegardsJapanAsJP)
                        {
                            return "jp";
                        }
                    }

                    return name;
                }
            }
            public readonly string TitleInEnglish;
            public readonly string TitleInNative;

            public CoreLanguageClass(CoreLanguage lang, int id, string name,
                string titleInEnglish, string titleInNative)
            {
                this.Language = lang;
                this.Id = id;
                this.name = name;
                this.TitleInEnglish = titleInEnglish;
                this.TitleInNative = titleInNative;
            }

            public static void SetCurrentThreadLanguageClass(CoreLanguageClass lang)
            {
                ThreadLocalStorage.CurrentThreadData["current_thread_language"] = lang;
            }

            public static CoreLanguageClass CurrentThreadLanguageClass
            {
                get
                {
                    return GetCurrentThreadLanguageClass();
                }

                set
                {
                    SetCurrentThreadLanguageClass(value);
                }
            }

            public static CoreLanguage CurrentThreadLanguage
            {
                get
                {
                    return CurrentThreadLanguageClass.Language;
                }
            }

            public static CoreLanguageClass GetCurrentThreadLanguageClass()
            {
                CoreLanguageClass? lang = null;

                try
                {
                    lang = (CoreLanguageClass)ThreadLocalStorage.CurrentThreadData["current_thread_language"];
                }
                catch
                {
                }

                if (lang == null)
                {
                    lang = CoreLanguageList.DefaultLanguage;

                    SetCurrentThreadLanguageClass(lang);
                }

                return lang;
            }
        }

        // 言語リスト
        public static class CoreLanguageList
        {
            public static readonly CoreLanguageClass DefaultLanguage;
            public static readonly CoreLanguageClass Japanese;
            public static readonly CoreLanguageClass English;
            public static bool RegardsJapanAsJP = false;

            public static readonly List<CoreLanguageClass> LanguageList = new List<CoreLanguageClass>();

            static CoreLanguageList()
            {
                CoreLanguageList.LanguageList = new List<CoreLanguageClass>();

                CoreLanguageList.Japanese = new CoreLanguageClass(CoreLanguage.Japanese,
                    0, "ja", "Japanese", "日本語");
                CoreLanguageList.English = new CoreLanguageClass(CoreLanguage.English,
                    1, "en", "English", "English");

                CoreLanguageList.DefaultLanguage = CoreLanguageList.Japanese;

                CoreLanguageList.LanguageList.Add(CoreLanguageList.Japanese);
                CoreLanguageList.LanguageList.Add(CoreLanguageList.English);
            }

            public static CoreLanguageClass GetLanguageClassByName(string name)
            {
                Str.NormalizeStringStandard(ref name);

                foreach (CoreLanguageClass c in LanguageList)
                {
                    if (Str.StrCmpi(c.Name, name))
                    {
                        return c;
                    }
                }

                return DefaultLanguage;
            }

            public static CoreLanguageClass GetLangugageClassByEnum(CoreLanguage lang)
            {
                foreach (CoreLanguageClass c in LanguageList)
                {
                    if (c.Language == lang)
                    {
                        return c;
                    }
                }

                return DefaultLanguage;
            }
        }
    }

    public class RefInt : IEquatable<RefInt>, IComparable<RefInt>
    {
        public RefInt(int value = 0)
        {
            this.Value = value;
        }
        public volatile int Value;
        public void Set(int value) => this.Value = value;
        public int Get() => this.Value;
        public override string ToString() => this.Value.ToString();
        public int Increment() => Interlocked.Increment(ref this.Value);
        public int Decrement() => Interlocked.Decrement(ref this.Value);
        public long Add(int v) => Interlocked.Add(ref this.Value, v);
        public long Sub(int v) => Interlocked.Add(ref this.Value, -v);

        public override bool Equals(object? obj) => obj is RefInt x && this.Value == x.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public bool Equals(RefInt? other) => this.Value.Equals(other!.Value);
        public int CompareTo(RefInt? other) => this.Value.CompareTo(other!.Value);

        public static bool operator ==(RefInt left, int right) => left.Value == right;
        public static bool operator !=(RefInt left, int right) => left.Value != right;
        public static implicit operator int(RefInt r) => r.Value;
        public static implicit operator RefInt(int value) => new RefInt(value);
    }

    public class RefLong : IEquatable<RefLong>, IComparable<RefLong>
    {
        public RefLong(long value = 0)
        {
            this.Value = value;
        }
        long _value;
        public long Value { get => Get(); set => Set(value); }
        public void Set(long value) => Interlocked.Exchange(ref this._value, value);
        public long Get() => Interlocked.Read(ref this._value);
        public override string ToString() => this.Value.ToString();
        public long Increment() => Interlocked.Increment(ref this._value);
        public long Decrement() => Interlocked.Decrement(ref this._value);
        public long Add(long v) => Interlocked.Add(ref this._value, v);
        public long Sub(long v) => Interlocked.Add(ref this._value, -v);

        public override bool Equals(object? obj) => obj is RefLong x && this.Value == x.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public bool Equals(RefLong? other) => this.Value.Equals(other!.Value);
        public int CompareTo(RefLong? other) => this.Value.CompareTo(other!.Value);

        public static bool operator ==(RefLong left, long right) => left.Value == right;
        public static bool operator !=(RefLong left, long right) => left.Value != right;
        public static implicit operator long(RefLong r) => r.Value;
        public static implicit operator RefLong(long value) => new RefLong(value);
    }

    public class RefBool : IEquatable<RefBool>, IComparable<RefBool>
    {
        public RefBool(bool value = false)
        {
            this.Value = value;
        }
        public volatile bool Value;
        public void Set(bool value) => this.Value = value;
        public bool Get() => this.Value;
        public override string ToString() => this.Value.ToString();

        public override bool Equals(object? obj) => obj is RefBool x && this.Value == x.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public bool Equals(RefBool? other) => this.Value.Equals(other!.Value);
        public int CompareTo(RefBool? other) => this.Value.CompareTo(other!.Value);

        public static bool operator ==(RefBool left, bool right) => left.Value == right;
        public static bool operator !=(RefBool left, bool right) => left.Value != right;
        public static implicit operator bool(RefBool r) => r.Value;
        public static implicit operator RefBool(bool value) => new RefBool(value);
    }

    public class ValueRef<T> where T : struct
    {
        public T Value;
        public readonly int Size = Marshal.SizeOf<T>();

        public ValueRef(T value = default)
        {
            Value = value;
        }
    }

    public class Ref<T> : IEmptyChecker
    {
        public Ref(T? value = default)
        {
            Value = value;
        }

        public T? Value { get; set; }
        public void Set(T value) => this.Value = value;
        public T? Get() => this.Value;
        public bool IsTrue()
        {
            switch (this.Value)
            {
                case bool b:
                    return b;
                case int i:
                    return (i != 0);
                case string s:
                    return bool.TryParse(s, out bool ret2) ? ret2 : false;
            }
            return bool.TryParse(this.Value?.ToString(), out bool ret) ? ret : false;
        }
        public override string? ToString() => Value?.ToString() ?? null;

        public override bool Equals(object? obj)
        {
            var refObj = obj as Ref<T>;
            return refObj != null &&
                   EqualityComparer<T>.Default.Equals(Value, refObj.Value);
        }

        public override int GetHashCode()
        {
            return -1937169414 + EqualityComparer<T>.Default.GetHashCode(Value!);
        }

        public bool IsThisEmpty() => this.Value._IsEmpty();

        public static bool operator true(Ref<T> r) { return r.IsTrue(); }
        public static bool operator false(Ref<T> r) { return !r.IsTrue(); }
        public static bool operator ==(Ref<T> r, bool b) { return r.IsTrue() == b; }
        public static bool operator !=(Ref<T> r, bool b) { return r.IsTrue() != b; }
        public static bool operator !(Ref<T> r) { return !r.IsTrue(); }

        [return: NotNullIfNotNull("r.Value")]
        public static implicit operator T?(Ref<T> r) => r.Value;
        public static implicit operator Ref<T>(T value) => new Ref<T>(value);
    }

    public abstract class MemberwiseClonable : ICloneable
    {
        public virtual object Clone() => this.MemberwiseClone();
    }

    // Readonly values holder
    // See https://en.wikipedia.org/wiki/Copenhagen_interpretation
    public class Copenhagen<T>
    {
        T _Value;
        T _InitialValue;
        CriticalSection LockObj;
        volatile bool Determined;
        public bool IsDetermined => Determined;
        bool IsValueType;

        public Copenhagen(T initialValue)
        {
            this._Value = initialValue;
            this._InitialValue = initialValue;
            this.LockObj = new CriticalSection<Copenhagen<T>>();
            this.Determined = false;
            IsValueType = !(typeof(T).IsClass);
        }

        public T Value { get => GetValue(); set => SetValue(value); }

        public void RestoreToDefault() => this.Set(this._InitialValue);

        public T Get() => GetValue();
        public T GetValue()
        {
            if (Determined)
            {
                if (IsValueType)
                    return this._Value;
                return this._Value._CloneIfClonable();
            }
            lock (LockObj)
            {
                Determined = true;
                if (IsValueType)
                    return this._Value;
                return this._Value._CloneIfClonable();
            }
        }

        public void Set(T value) => SetValue(value);
        public void SetValue(T value)
        {
            if (TrySetValue(value) == false)
            {
                throw new ApplicationException($"The value '{this.GetType()}' is readonly becasue it is already determined.");
            }
        }

        public bool TrySet(T value) => TrySetValue(value);
        public bool TrySetValue(T value)
        {
            lock (LockObj)
            {
                if (Determined == false)
                {
                    this._Value = value._CloneIfClonable();
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public override bool Equals(object? obj)
        {
            var refObj = obj as Ref<T>;
            return refObj != null &&
                   EqualityComparer<T>.Default.Equals(Value, refObj.Value);
        }

        public override int GetHashCode() => -1937169414 + EqualityComparer<T>.Default.GetHashCode(Value!);

        public override string? ToString() => Value?.ToString() ?? null;

        public static implicit operator T(Copenhagen<T> r) => r.Value;
        public static implicit operator Copenhagen<T>(T value) => new Copenhagen<T>(value);
    }

    public interface IEmptyChecker
    {
        bool IsThisEmpty();
    }

    [Flags]
    public enum MultipleActionsFlag
    {
        IgnoreError,
        AllOk,
        AnyOk,
        AnyOkContinueAll,
        AnyOkContinueAllRetLast,
    }

    [Flags]
    public enum DeepCloneMethod
    {
        Default = 0,
        BinaryFormatter,
        DeepCloner,
    }

    // ユーティリティクラス
    public static partial class Util
    {
        // Do not change
        public static readonly DateTime ZeroDateTimeValue = new DateTime(1800, 1, 1);
        public static readonly DateTimeOffset ZeroDateTimeOffsetValue = new DateTimeOffset(1800, 1, 1, 0, 0, 0, new TimeSpan(0, 0, 0));

        // Do not change
        public static readonly DateTime MaxDateTimeValue = new DateTime(9931, 12, 21, 0, 0, 0);
        public static readonly DateTimeOffset MaxDateTimeOffsetValue = new DateTimeOffset(9931, 12, 21, 0, 0, 0, new TimeSpan(0, 0, 0));

        public static readonly object EmptyObject = Limbo.EmptyObject;

        static readonly Random RandomShared = new Random();

        static Util()
        {
            using (SHA1 sha = SHA1.Create())
            {
                Span<byte> rand = new byte[20];
                sha.TryComputeHash(System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString()), rand, out _);

                int seed = BitConverter.ToInt32(rand.Slice(0, 4)) & 0x7fffffff;
                RandomShared = new Random(seed);
            }
        }

        // サイズ定数
        public const int SizeOfInt32 = 4;
        public const int SizeOfInt16 = 2;
        public const int SizeOfInt64 = 8;
        public const int SizeOfInt8 = 1;

        // 内容がゼロクリアされた共有バッファを返す
        private class ZeroBufferSharedClass<T>
        {
            public static ReadOnlyMemory<T> ZeroBufferShared = new T[0];
        }
        public static ReadOnlyMemory<T> GetZeroedSharedBuffer<T>(int size)
        {
            ReadOnlyMemory<T> current = ZeroBufferSharedClass<T>.ZeroBufferShared;

            if (current.Length >= size)
            {
                return current.Slice(0, size);
            }

            current = new T[size];

            ZeroBufferSharedClass<T>.ZeroBufferShared = current;

            return current.Slice(0, size);
        }

        // 数値処理
        public static byte[] ToByte(ushort i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(short i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(uint i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(int i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(ulong i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(long i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static ushort ByteToUShort(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToUInt16(c, 0);
        }
        public static short ByteToShort(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToInt16(c, 0);
        }
        public static uint ByteToUInt(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToUInt32(c, 0);
        }
        public static int ByteToInt(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToInt32(c, 0);
        }
        public static ulong ByteToULong(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToUInt64(c, 0);
        }
        public static long ByteToLong(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToInt64(c, 0);
        }

        public unsafe static long GetObjectAddress(object o)
        {
            return (long)(*(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref o));
        }

        public static string GetGitForWindowsExeFileName()
        {
            if (Env.IsWindows == false)
            {
                throw new NotSupportedException();
            }

            string value = MsReg.ReadStr(RegRoot.LocalMachine, @"SOFTWARE\GitForWindows", "InstallPath");
            if (value._IsFilled())
            {
                return Lfs.PathParser.Combine(value, @"bin\git.exe");
            }
            else
            {
                throw new CoresException(@"Git for Windows is not installed.");
            }
        }

        // オブジェクト内の public なフィールドとプロパティを再帰的に全部読み取る
        public static List<WalkObjectItem> WalkObject(object? obj, bool fastMode = false)
        {
            List<WalkObjectItem> ret = new List<WalkObjectItem>();

            if (obj != null)
            {
                if (fastMode == False)
                {
                    // 通常モード (遅い)
                    ImmutableHashSet<object> hashSet = ImmutableHashSet<object>.Empty;

                    var vars = Dbg.GetVarsFromClass(obj.GetType(), "", false, "", obj, hashSet);

                    Walk(vars);

                    void Walk(DebugVars targetVars)
                    {
                        foreach (var v in targetVars.Vars.OrderBy(x => x.order))
                        {
                            WalkObjectItem item = new WalkObjectItem
                            {
                                Object = v.targetObject,
                                Name = v.memberInfo.Name,
                                MemberInfo = v.memberInfo,
                                Data = v.data,
                            };

                            ret.Add(item);
                        }

                        foreach (var child in targetVars.Childlen.OrderBy(x => x.order))
                        {
                            Walk(child.child);
                        }
                    }
                }
                else
                {
                    // 高速モード (階層構造を考慮しない)
                    var rw = obj.GetType()._GetFieldReaderWriter();

                    foreach (var name in rw.OrderedPublicFieldOrPropertyNamesList)
                    {
                        {
                            var data = rw.GetValue(obj, name);

                            WalkObjectItem item = new WalkObjectItem
                            {
                                Object = obj,
                                Name = name,
                                MemberInfo = null,
                                Data = data,
                            };

                            ret.Add(item);
                        }

                    }
                }
            }

            return ret;
        }

        // IEnumerable から Array List を作成
        public static T[] IEnumerableToArrayList<T>(IEnumerable<T> i) => i.ToArray();

        // ストリームからすべて読み出す
        public static byte[] ReadAllFromStream(Stream st)
        {
            byte[] tmp = new byte[32 * 1024];
            Buf b = new Buf();

            while (true)
            {
                int r = st.Read(tmp, 0, tmp.Length);

                if (r == 0)
                {
                    break;
                }

                b.Write(tmp, 0, r);
            }

            return b.ByteData;
        }

        // リストのクローン
        public static List<T> CloneList<T>(List<T> src)
        {
            List<T> ret = new List<T>();
            foreach (T t in src)
            {
                ret.Add(t);
            }
            return ret;
        }

        // byte 配列から一部分だけ抜き出す
        public static byte[] ExtractByteArray(byte[] data, int pos, int len)
        {
            byte[] ret = new byte[len];

            Util.CopyByte(ret, 0, data, pos, len);

            return ret;
        }

        // 配列を結合する
        public static T[] CombineArray<T>(params T[][] arrays)
        {
            List<T> o = new List<T>();
            foreach (T[] array in arrays)
            {
                foreach (T element in array)
                {
                    o.Add(element);
                }
            }
            return o.ToArray();
        }

        // byte 配列を結合する
        [MethodImpl(Inline)]
        public static byte[] CombineByteArray(byte[]? b1, byte[]? b2)
        {
            if (b1 == null && b2 == null) return new byte[0];
            if (b1 == null || b1.Length == 0) return b2 == null ? new byte[0] : b2;
            if (b2 == null || b2.Length == 0) return b1 == null ? new byte[0] : b1;
            byte[] ret = new byte[b1.Length + b2.Length];
            Array.Copy(b1, 0, ret, 0, b1.Length);
            Array.Copy(b2, 0, ret, b1.Length, b2.Length);
            return ret;
        }

        // byte 配列の先頭を削除する
        public static byte[] RemoveStartByteArray(byte[] src, int numBytes)
        {
            if (numBytes == 0)
            {
                return src;
            }
            int num = src.Length - numBytes;
            byte[] ret = new byte[num];
            Util.CopyByte(ret, 0, src, numBytes, num);
            return ret;
        }

        // 2 つの値を引き算する return (a - b)
        public static object? Subtract(object a, object b)
        {
            if (a == null) return null;
            if (b == null) return a;
            if (a.GetType() != b.GetType()) throw new ArgumentException("a.GetType() != b.GetType()");

            switch (a)
            {
                case byte aa: return aa - (byte)b;
                case sbyte aa: return aa - (sbyte)b;
                case short aa: return aa - (short)b;
                case ushort aa: return aa - (ushort)b;
                case int aa: return aa - (int)b;
                case uint aa: return aa - (uint)b;
                case long aa: return aa - (long)b;
                case ulong aa: return aa - (ulong)b;
                case char aa: return aa - (char)b;
                case float aa: return aa - (float)b;
                case double aa: return aa - (double)b;
                case decimal aa: return aa - (decimal)b;
                case bool aa: return aa != (bool)b;
                case BigNumber aa: return aa - (BigNumber)b;
                case BigInteger aa: return aa - (BigInteger)b;
                default: return a;
            }
        }

        // 割り算する (return a / b)
        public static object? Divide(object a, double b)
        {
            if (a == null) return null;

            switch (a)
            {
                case byte aa: return (byte)((double)aa / b);
                case sbyte aa: return (sbyte)((double)aa / b);
                case short aa: return (short)((double)aa / b);
                case ushort aa: return (ushort)((double)aa / b);
                case int aa: return (int)((double)aa / b);
                case uint aa: return (uint)((double)aa / b);
                case long aa: return (long)((double)aa / b);
                case ulong aa: return (ulong)((double)aa / b);
                case char aa: return (char)((double)aa / b);
                case float aa: return (float)((double)aa / b);
                case double aa: return (double)((double)aa / b);
                case decimal aa: return (decimal)((double)aa / b);
                case bool aa: return aa;
                case BigNumber aa: return aa / (long)b;
                case BigInteger aa: return aa / (long)b;
                default: return a;
            }
        }

        // 2 つの年をはさむ年度のリストを取得する
        public static DateTime[] GetYearNendoList(DateTime startYear, DateTime endYear)
        {
            startYear = GetStartOfNendo(startYear);
            endYear = GetEndOfNendo(endYear);

            if (startYear > endYear)
            {
                throw new ArgumentException();
            }

            List<DateTime> ret = new List<DateTime>();

            DateTime dt;
            for (dt = startYear; dt <= endYear; dt = GetStartOfNendo(dt.AddYears(1)))
            {
                ret.Add(dt);
            }

            return ret.ToArray();
        }

        // 2 つの年をさはむ年のリストを取得する
        public static DateTime[] GetYearList(DateTime startYear, DateTime endYear)
        {
            startYear = GetStartOfYear(startYear);
            endYear = GetEndOfYear(endYear);

            if (startYear > endYear)
            {
                throw new ArgumentException();
            }

            List<DateTime> ret = new List<DateTime>();

            DateTime dt;
            for (dt = startYear; dt <= endYear; dt = GetStartOfYear(dt.AddYears(1)))
            {
                ret.Add(dt);
            }

            return ret.ToArray();
        }

        // 2 つの月をはさむ月のリストを取得する
        public static DateTime[] GetMonthList(DateTime startMonth, DateTime endMonth)
        {
            startMonth = GetStartOfMonth(startMonth);
            endMonth = GetEndOfMonth(endMonth);

            if (startMonth > endMonth)
            {
                throw new ArgumentException();
            }

            List<DateTime> ret = new List<DateTime>();

            DateTime dt;
            for (dt = startMonth; dt <= endMonth; dt = GetStartOfMonth(dt.AddMonths(1)))
            {
                ret.Add(dt);
            }

            return ret.ToArray();
        }

        // ある日における年齢を取得する
        public static int GetAge(DateTime birthDay, DateTime now)
        {
            birthDay = birthDay.Date;
            now = now.Date;

            DateTime dayBirthDay = new DateTime(2000, birthDay.Month, birthDay.Day);
            DateTime dayNow = new DateTime(2000, now.Month, now.Day);

            int ret = now.Year - birthDay.Year;

            if (dayBirthDay > dayNow)
            {
                ret -= 1;
            }

            return Math.Max(ret, 0);
        }

        // 特定の月の日数を取得する
        public static int GetNumOfDaysInMonth(DateTime dt)
        {
            DateTime dt1 = new DateTime(dt.Year, dt.Month, dt.Day);
            DateTime dt2 = dt1.AddMonths(1);
            TimeSpan span = dt2 - dt1;

            return span.Days;
        }

        // 2 個の日付の間に存在する月数を取得する
        public static int GetNumMonthSpan(DateTime dt1, DateTime dt2, bool kiriage)
        {
            if (dt1 > dt2)
            {
                DateTime dtt = dt2;
                dt2 = dt1;
                dt1 = dtt;
            }

            int i;
            DateTime dt = dt1;
            for (i = 0; ; i++)
            {
                if (kiriage)
                {
                    if (dt >= dt2)
                    {
                        return i;
                    }
                }
                else
                {
                    if (dt >= dt2.AddMonths(1).AddTicks(-1))
                    {
                        return i;
                    }
                }

                dt = dt.AddMonths(1);
            }
        }

        // 特定の月の初日を取得する
        public static DateTime GetStartOfMonth(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1);
        }

        // 特定の月の末日を取得する
        public static DateTime GetEndOfMonth(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1).AddMonths(1).AddSeconds(-1).Date;
        }

        // 特定の年の初日を取得する
        public static DateTime GetStartOfYear(DateTime dt)
        {
            return new DateTime(dt.Year, 1, 1, 0, 0, 0);
        }

        // 特定の年の年末を取得する
        public static DateTime GetEndOfYear(DateTime dt)
        {
            return GetStartOfYear(dt).AddYears(1).AddSeconds(-1).Date;
        }

        // 特定の月の末日 (月末が休業日の場合は翌営業日) を取得する
        public static DateTime GetEndOfMonthForSettle(DateTime dt)
        {
            dt = new DateTime(dt.Year, dt.Month, 1).AddMonths(1).AddSeconds(-1).Date;
            if (dt.Month == 4 && (new DateTime(dt.Year, 4, 29).DayOfWeek == DayOfWeek.Sunday))
            {
                dt = dt.AddDays(1);
            }
            while ((dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday) ||
                (dt.Month == 12 && dt.Day >= 29) ||
                (dt.Month == 1 && dt.Day <= 3))
            {
                dt = dt.AddDays(1);
            }
            return dt;
        }

        // 特定の日の開始時刻を取得する
        public static DateTime GetStartOfDay(DateTime dt)
        {
            return dt.Date;
        }

        // 特定の日の終了時刻を取得する
        public static DateTime GetEndOfDate(DateTime dt)
        {
            return GetStartOfDay(dt).AddDays(1).AddTicks(-1);
        }

        // 年度を取得する
        public static int GetNendo(DateTime dt)
        {
            if (dt.Month >= 4)
            {
                return dt.Year;
            }
            else
            {
                return dt.Year - 1;
            }
        }

        // 年度の開始日を指定する
        public static DateTime GetStartOfNendo(DateTime dt)
        {
            return GetStartOfNendo(GetNendo(dt));
        }
        public static DateTime GetStartOfNendo(int nendo)
        {
            return new DateTime(nendo, 4, 1, 0, 0, 0).Date;
        }

        // 年度の終了日を指定する
        public static DateTime GetEndOfNendo(DateTime dt)
        {
            return GetEndOfNendo(GetNendo(dt));
        }
        public static DateTime GetEndOfNendo(int nendo)
        {
            return new DateTime(nendo + 1, 3, 31, 0, 0, 0).Date;
        }

        // エンディアン変換処理
        public static void Endian(byte[] b)
        {
            if (Env.IsLittleEndian)
            {
                Array.Reverse(b);
            }
        }
        public static byte[] EndianRetByte(byte[] b)
        {
            b = Util.CloneByteArray(b);

            Endian(b);

            return b;
        }
        public static UInt16 Endian(UInt16 v)
        {
            return Util.ByteToUShort(Util.EndianRetByte(Util.ToByte(v)));
        }
        public static UInt32 Endian(UInt32 v)
        {
            return Util.ByteToUInt(Util.EndianRetByte(Util.ToByte(v)));
        }
        public static UInt64 Endian(UInt64 v)
        {
            return Util.ByteToULong(Util.EndianRetByte(Util.ToByte(v)));
        }

        // ドメイン名に使用できる文字列に変換する
        public static string SafeDomainStr(string str)
        {
            string ret = str.Replace("(", "").Replace(")", "").Replace(" ", "").Replace("-", "").Replace("#", "")
                .Replace("%", "").Replace("%", "").Replace("&", "").Replace(".", "");
            if (ret == "")
            {
                ret = "host";
            }

            return ret;
        }

        [MethodImpl(Inline)]
        public static void ZeroMemory(Memory<byte> dst)
        {
            checked
            {
                Unsafe.InitBlock(ref dst.Span[0], 0, (uint)dst.Length);
            }
        }

        [MethodImpl(Inline)]
        public static void ZeroMemory(Span<byte> dst)
        {
            checked
            {
                Unsafe.InitBlock(ref dst[0], 0, (uint)dst.Length);
            }
        }

        [MethodImpl(Inline)]
        public static void ZeroMemory(byte[] dst, int offset = 0, int size = DefaultSize)
        {
            checked
            {
                if (offset < 0 || offset >= dst.Length) throw new ArgumentOutOfRangeException(nameof(offset));

                if (size == DefaultSize)
                {
                    size = dst.Length - offset;
                }
                else
                {
                    if (size <= 0) return;
                }

                if ((offset + size) > dst.Length) throw new ArgumentOutOfRangeException(nameof(size));

                Unsafe.InitBlock(ref dst[offset], 0, (uint)size);
            }
        }

        [MethodImpl(Inline)]
        public static byte[] CopyByte(byte[] src)
        {
            if (src == null) return new byte[0];

            byte[] ret = new byte[src.Length];

            CopyByte(ret, 0, src);

            return ret;
        }

        [MethodImpl(Inline)]
        public static byte[] CopyByte(byte[] src, int srcOffset)
        {
            return CopyByte(src, srcOffset, src.Length - srcOffset);
        }

        [MethodImpl(Inline)]
        public static byte[] CopyByte(byte[] src, int srcOffset, int size)
        {
            byte[] ret = new byte[size];
            CopyByte(ret, 0, src, srcOffset, size);
            return ret;
        }

        [MethodImpl(Inline)]
        public static void CopyByte(byte[] dst, byte[] src, int srcOffset, int size)
        {
            CopyByte(dst, 0, src, srcOffset, size);
        }

        [MethodImpl(Inline)]
        public static void CopyByte(byte[] dst, int dstOffset, byte[] src)
        {
            CopyByte(dst, dstOffset, src, 0, src.Length);
        }

        [MethodImpl(Inline)]
        public static void CopyByte(byte[] dst, int dstOffset, byte[] src, int srcOffset, int size)
        {
            Span<byte> dstSpan = dst.AsSpan(dstOffset, size);
            Span<byte> srcSpan = src.AsSpan(srcOffset, size);
            srcSpan.CopyTo(dstSpan);
        }

        [MethodImpl(Inline)]
        public static void CopyByte(ref byte dst, in byte src, int size)
        {
            Unsafe.CopyBlock(ref dst, ref Unsafe.AsRef(in src), (uint)size);
        }

        [MethodImpl(Inline)]
        public static unsafe void CopyByte(ref byte dst, byte* src, int size)
        {
            ref byte srcref = ref Unsafe.AsRef<byte>((void*)src);

            Unsafe.CopyBlock(ref dst, ref srcref, (uint)size);
        }

        [MethodImpl(Inline)]
        public static unsafe void CopyByte(byte* dst, in byte src, int size)
        {
            ref byte dstref = ref Unsafe.AsRef<byte>((void*)dst);

            Unsafe.CopyBlock(ref dstref, ref Unsafe.AsRef(src), (uint)size);
        }

        [MethodImpl(Inline)]
        public static unsafe void CopyByte(byte* dst, ReadOnlySpan<byte> src)
        {
            Span<byte> dstSpan = new Span<byte>(dst, src.Length);
            src.CopyTo(dstSpan);
        }

        [MethodImpl(Inline)]
        public static unsafe void CopyByte(ref byte dst, ReadOnlySpan<byte> src)
        {
            Span<byte> dstSpan = new Span<byte>(Unsafe.AsPointer(ref dst), src.Length);
            src.CopyTo(dstSpan);
        }

        [MethodImpl(Inline)]
        public static unsafe void CopyByte(Span<byte> dst, byte* src)
        {
            ReadOnlySpan<byte> srcSpan = new ReadOnlySpan<byte>(src, dst.Length);
            srcSpan.CopyTo(dst);
        }

        [MethodImpl(Inline)]
        public static unsafe void CopyByte(Span<byte> dst, in byte src)
        {
            ReadOnlySpan<byte> srcSpan = new ReadOnlySpan<byte>(Unsafe.AsPointer(ref Unsafe.AsRef(in src)), dst.Length);
            srcSpan.CopyTo(dst);
        }

        [MethodImpl(Inline)]
        public static void BlockCopy(ReadOnlySpan<byte> src, int srcOffset, Span<byte> dst, int dstOffset, int count)
        {
            src.Slice(srcOffset, count).CopyTo(dst.Slice(dstOffset, count));
        }

        public static DateTime NormalizeDateTime(DateTime dt)
        {
            if (IsZero(dt)) return Util.ZeroDateTimeValue;
            return dt;
        }

        public static DateTimeOffset NormalizeDateTime(DateTimeOffset dt)
        {
            if (IsZero(dt)) return Util.ZeroDateTimeOffsetValue;
            return dt;
        }

        public static DateTime NormalizeDateTimeForFileSystem(DateTime dt)
        {
            if (IsZeroForFileSystem(dt)) return FileSystem.ZeroDateTimeForFileSystem;
            return dt;
        }

        public static DateTimeOffset NormalizeDateTimeForFileSystem(DateTimeOffset dt)
        {
            if (IsZeroForFileSystem(dt)) return FileSystem.ZeroDateTimeOffsetForFileSystem;
            return dt;
        }

        // 指定されたオブジェクトが Null、0 または空データであるかどうか判別する
        public static bool IsEmpty<T>([NotNullWhen(false)] T data, bool zeroValueIsEmpty = false)
        {
            if (data._IsNullOrDefault()) return true;

            try
            {
                if (data is IEmptyChecker emptyChecker) return emptyChecker.IsThisEmpty();

                if (zeroValueIsEmpty)
                {
                    switch (data)
                    {
                        case char c: return c == 0;
                        case byte b: return b == 0;
                        case sbyte sb: return sb == 0;
                        case ushort us: return us == 0;
                        case short s: return s == 0;
                        case uint ui: return ui == 0;
                        case int i: return i == 0;
                        case ulong ul: return ul == 0;
                        case long l: return l == 0;
                        case float f: return f == 0;
                        case double d: return d == 0;
                        case decimal v: return v == 0;
                        case bool b: return b == false;
                        case BigNumber bn: return bn == 0;
                        case BigInteger bi: return bi == 0;
                        case Memory<byte> m: return m._IsZero();
                        case byte[] x: return Util.IsZero(x);
                    }
                }
                switch (data)
                {
                    case Array a: return a.Length == 0;
                    case IntPtr p: return p == IntPtr.Zero;
                    case UIntPtr up: return up == UIntPtr.Zero;
                    case DateTime dt: return Util.IsZero(dt);
                    case DateTimeOffset dt: return Util.IsZero(dt);
                    case ICollection c: return c.Count == 0;
                    case string s:
                        return string.IsNullOrWhiteSpace(s);
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
        public static bool IsFilled<T>([NotNullWhen(true)] T data, bool zeroValueIsEmpty = false) => !IsEmpty(data, zeroValueIsEmpty);

        // DateTime がゼロかどうか検査する
        public static bool IsZero(DateTime dt)
        {
            if (dt.Year < 1850)
            {
                return true;
            }
            return false;
        }
        public static bool IsZero(DateTimeOffset dt)
        {
            if (dt.Year < 1850)
            {
                return true;
            }
            return false;
        }

        public static bool IsZeroForFileSystem(DateTime dt)
        {
            if (dt.Year < 1980)
            {
                return true;
            }
            return false;
        }
        public static bool IsZeroForFileSystem(DateTimeOffset dt)
        {
            if (dt.Year < 1980)
            {
                return true;
            }
            return false;
        }


        // byte[] 配列がオールゼロかどうか検査する
        [MethodImpl(Inline)]
        public static bool IsZero(byte[] data)
        {
            if (data == null) return true;
            return IsZero(data, 0, data.Length);
        }

        [MethodImpl(Inline)]
        public static bool IsZero(byte[] data, int offset, int size)
        {
            if (data == null) return true;
            return IsZero(new ReadOnlySpan<byte>(data, offset, size));
        }

        [MethodImpl(Inline)]
        public static bool IsZero(ReadOnlySpan<byte> data)
        {
            int i;
            if (data.Length <= 0) return true;
            if (data[0] != 0) return false;
            for (i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        [MethodImpl(Inline)]
        public static bool IsZero(ReadOnlyMemory<byte> data) => IsZero(data.Span);

        [MethodImpl(Inline)]
        public static unsafe bool IsZero(byte* ptr, int size)
        {
            int i;
            if (size <= 0) return true;
            if (ptr[0] != 0) return false;
            for (i = 0; i < size; i++)
            {
                if (ptr[i] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        [MethodImpl(Inline)]
        public static unsafe bool IsZeroStruct<T>(in T value, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));
            byte* ptr = (byte*)Unsafe.AsPointer<T>(ref Unsafe.AsRef(in value));
            return IsZero(ptr, size);
        }

        [MethodImpl(Inline)]
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static unsafe bool IsZeroFast(byte* ptr, int size)
        {
            int skipSize = 0;

            if (size <= 0) return true;
            if (size >= 8)
            {
                if (*((ulong*)&ptr[0]) != 0) return false;
                skipSize = 8;
            }
            else if (size >= 4)
            {
                if (*((uint*)&ptr[0]) != 0) return false;
                skipSize = 4;
            }
            else
            {
                if (ptr[0] != 0) return false;
                skipSize = 1;
            }

            byte* srcPointer = ptr;
            long startLong = (long)srcPointer;
            long srcLen = size;
            long endLong = startLong + srcLen;

            startLong = ((startLong + 7) / 8) * 8;
            endLong = (endLong / 8) * 8;

            for (byte* p = srcPointer + skipSize; p < (byte*)startLong; p++)
                if (*p != 0) return false;

            byte* endP = srcPointer + srcLen;

            for (byte* p = (byte*)endLong; p < endP; p++)
                if (*p != 0) return false;

            for (long i = startLong; i < endLong; i += 8)
            {
                if (*((long*)i) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(Inline)]
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static unsafe bool IsZeroFast(ReadOnlySpan<byte> srcMemory)
        {
            fixed (byte* srcPointer = srcMemory)
            {
                return IsZeroFast(srcPointer, srcMemory.Length);
            }
        }

        [MethodImpl(Inline)]
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool IsZeroFast(ReadOnlyMemory<byte> data) => IsZeroFast(data.Span);

        [MethodImpl(Inline)]
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool IsZeroFast(byte[] data)
        {
            if (data == null) return true;
            return IsZeroFast(data, 0, data.Length);
        }

        [MethodImpl(Inline)]
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool IsZeroFast(byte[] data, int offset, int size)
        {
            if (data == null) return true;
            return IsZeroFast(new ReadOnlySpan<byte>(data, offset, size));
        }

        [MethodImpl(Inline)]
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static unsafe bool IsZeroFastStruct<T>(in T value, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));
            byte* ptr = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in value));
            return IsZeroFast(ptr, size);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals(ReadOnlySpan<byte> b1, ReadOnlySpan<byte> b2)
        {
            return b1.SequenceEqual(b2);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals(ReadOnlyMemory<byte> b1, ReadOnlyMemory<byte> b2)
        {
            return b1.Span.SequenceEqual(b2.Span);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals(byte[] a, byte[] b)
        {
            long lenA = a.Length;
            long lenB = b.Length;
            if (lenA != lenB) return false;
            if (lenA == 0) return True;
            return MemEquals(in a[0], in b[0], lenA);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals(in byte b1, in byte b2, long length)
        {
            return FastMemOperations.SequenceEqual(ref Unsafe.AsRef(in b1), ref Unsafe.AsRef(in b2), length);
        }

        [MethodImpl(Inline)]
        public static int MemCompare(ReadOnlySpan<byte> b1, ReadOnlySpan<byte> b2)
        {
            return b1.SequenceCompareTo(b2);
        }

        [MethodImpl(Inline)]
        public static int MemCompare(ReadOnlyMemory<byte> b1, ReadOnlyMemory<byte> b2)
        {
            return b1.Span.SequenceCompareTo(b2.Span);
        }

        [MethodImpl(Inline)]
        public static int MemCompare(byte[] a, byte[] b)
        {
            return MemCompare(in a[0], a.Length, in b[0], b.Length);
        }

        [MethodImpl(Inline)]
        public static int MemCompare(in byte b1, int b1Length, in byte b2, int b2Length)
        {
            return FastMemOperations.SequenceCompareTo(ref Unsafe.AsRef(in b1), b1Length, ref Unsafe.AsRef(in b2), b2Length);
        }






        [MethodImpl(Inline)]
        public static bool MemEquals<T>(ReadOnlySpan<T> b1, ReadOnlySpan<T> b2) where T : IEquatable<T>
        {
            return b1.SequenceEqual(b2);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals<T>(ReadOnlyMemory<T> b1, ReadOnlyMemory<T> b2) where T : IEquatable<T>
        {
            return b1.Span.SequenceEqual(b2.Span);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            int lenA = a.Length;
            int lenB = b.Length;
            if (lenA != lenB) return false;
            return MemEquals<T>(in a[0], in b[0], lenA);
        }

        [MethodImpl(Inline)]
        public static bool MemEquals<T>(in T b1, in T b2, int length) where T : IEquatable<T>
        {
            return FastMemOperations.SequenceEqual<T>(ref Unsafe.AsRef(in b1), ref Unsafe.AsRef(in b2), length);
        }

        [MethodImpl(Inline)]
        public static int MemCompare<T>(ReadOnlySpan<T> b1, ReadOnlySpan<T> b2) where T : IComparable<T>
        {
            return b1.SequenceCompareTo(b2);
        }

        [MethodImpl(Inline)]
        public static int MemCompare<T>(ReadOnlyMemory<T> b1, ReadOnlyMemory<T> b2) where T : IComparable<T>
        {
            return b1.Span.SequenceCompareTo(b2.Span);
        }

        [MethodImpl(Inline)]
        public static int MemCompare<T>(T[] a, T[] b) where T : IComparable<T>
        {
            return MemCompare(in a[0], a.Length, in b[0], b.Length);
        }

        [MethodImpl(Inline)]
        public static int MemCompare<T>(in T b1, int b1Length, in T b2, int b2Length) where T : IComparable<T>
        {
            return FastMemOperations.SequenceCompareTo(ref Unsafe.AsRef(in b1), b1Length, ref Unsafe.AsRef(in b2), b2Length);
        }

        [MethodImpl(Inline)]
        [Obsolete]
        public static bool CompareByte(byte[] b1, byte[] b2)
        {
            return MemEquals(b1, b2);
        }


        [MethodImpl(Inline)]
        public static unsafe bool StructBitEquals<T>(in T s1, in T s2) where T : unmanaged
        {
            int size = sizeof(T);

            ref T s1ref = ref Unsafe.AsRef(in s1);
            ref byte b1ref = ref Unsafe.As<T, byte>(ref s1ref);

            ref T s2ref = ref Unsafe.AsRef(in s2);
            ref byte b2ref = ref Unsafe.As<T, byte>(ref s2ref);

            return MemEquals(in b1ref, in b2ref, size);
        }


        [MethodImpl(Inline)]
        public static unsafe int StructBitCompare<T>(in T s1, in T s2) where T : unmanaged
        {
            int size = sizeof(T);

            ref T s1ref = ref Unsafe.AsRef(in s1);
            ref byte b1ref = ref Unsafe.As<T, byte>(ref s1ref);

            ref T s2ref = ref Unsafe.AsRef(in s2);
            ref byte b2ref = ref Unsafe.As<T, byte>(ref s2ref);

            return MemCompare(in b1ref, size, in b2ref, size);
        }



        // byte[] 配列同士を比較する
        [MethodImpl(Inline)]
        [Obsolete]
        public static int CompareByteRetInt(byte[] b1, byte[] b2)
        {
            return MemCompare(b1, b2);
        }

        // byte[] 配列のコピー
        public static byte[] CloneByteArray(byte[] src)
        {
            byte[] ret = new byte[src.Length];

            Util.CopyByte(ret, src, 0, src.Length);

            return ret;
        }

        // UNIX 時間を DateTime に変換
        public static DateTime UnixTimeToDateTime(ulong t)
        {
            return new DateTime(1970, 1, 1).AddSeconds(t);
        }

        public static DateTime UnixTimeToDateTime(decimal t)
        {
            return new DateTime(1970, 1, 1).AddSeconds((double)t);
        }

        // DateTime を UNIX 時間に変換
        public static long DateTimeToUnixTime(DateTime dt)
        {
            TimeSpan ts = dt - new DateTime(1970, 1, 1);
            if (ts.Ticks < 0)
            {
                throw new InvalidDataException("dt");
            }

            return (long)ts.TotalSeconds;
        }
        public static decimal DateTimeToUnixTimeDecimal(DateTime dt)
        {
            TimeSpan ts = dt - new DateTime(1970, 1, 1);
            if (ts.Ticks < 0)
            {
                throw new InvalidDataException("dt");
            }

            return (decimal)ts.TotalSeconds;
        }


        // Convert to a time to be used safely in the current POSIX implementation
        public static long SafeTime64(long time64) => (long)SafeTime64((ulong)time64);
        public static ulong SafeTime64(ulong time64)
        {
            time64 = Math.Max(time64, 0);
            time64 = Math.Min(time64, 253370732400000UL);
            return time64;
        }

        // long を DateTime に変換
        public static DateTime ConvertDateTime(long time64) => ConvertDateTime((ulong)time64);
        public static DateTime ConvertDateTime(ulong time64)
        {
            time64 = SafeTime64(time64);
            if (time64 == 0)
            {
                return new DateTime(0);
            }
            return new DateTime(((long)time64 + 62135629200000L) * 10000L);
        }

        // DateTime を long に変換
        public static long ConvertDateTime(DateTime dt)
        {
            if (dt.Ticks == 0)
            {
                return 0;
            }
            return SafeTime64(dt.Ticks / 10000L - 62135629200000L);
        }

        // ulong を TimeSpan に変換
        public static TimeSpan ConvertTimeSpan(long tick)
        {
            return new TimeSpan(tick * 10000);
        }

        // TimeSpan を long に変換
        public static long ConvertTimeSpan(TimeSpan span)
        {
            return span.Ticks / 10000;
        }

        // DateTime を DOS の日付に変換
        public static ushort DateTimeToDosDate(DateTime dt)
        {
            if (dt == default) return 0;

            return (ushort)(
                ((uint)(dt.Year - 1980) << 9) |
                ((uint)dt.Month << 5) |
                (uint)dt.Day);
        }

        // DateTime を DOS の時刻に変換
        public static ushort DateTimeToDosTime(DateTime dt)
        {
            if (dt == default) return 0;

            return (ushort)(
                ((uint)dt.Hour << 11) |
                ((uint)dt.Minute << 5) |
                ((uint)dt.Second >> 1));
        }

        // Array が Null か Empty かどうか
        public static bool IsNullOrEmpty(object o)
        {
            if (o == null)
            {
                return true;
            }

            if (o is string)
            {
                string s = (string)o;

                return Str.IsEmptyStr(s);
            }

            if (o is Array)
            {
                Array a = (Array)o;
                if (a.Length == 0)
                {
                    return true;
                }
            }

            return false;
        }

        // 型から XML スキーマを生成
        public static byte[] GetXmlSchemaFromType_PublicLegacy(Type type)
        {
            XmlSchemas sms = new XmlSchemas();
            XmlSchemaExporter ex = new XmlSchemaExporter(sms);
            XmlReflectionImporter im = new XmlReflectionImporter();
            XmlTypeMapping map = im.ImportTypeMapping(type);
            ex.ExportTypeMapping(map);
            sms.Compile(null, false);

            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms);
            foreach (System.Xml.Schema.XmlSchema? sm in sms)
            {
                sm?.Write(sw);
            }
            sw.Close();
            ms.Flush();

            byte[] data = ms.ToArray();
            return data;
        }
        public static string GetXmlSchemaFromTypeString_PublicLegacy(Type type)
        {
            byte[] data = GetXmlSchemaFromType_PublicLegacy(type);

            return Str.Utf8Encoding.GetString(data);
        }

        // オブジェクトを XML に変換
        public static string ObjectToXmlString_PublicLegacy(object o)
        {
            byte[]? data = ObjectToXml_PublicLegacy(o);

            if (data == null) return "";

            return Str.Utf8Encoding.GetString(data);
        }

        [return: NotNullIfNotNull("o")]
        public static byte[]? ObjectToXml_PublicLegacy(object o)
        {
            if (o == null)
            {
                return null;
            }
            Type t = o.GetType();

            return ObjectToXml_PublicLegacy(o, t);
        }
        public static string ObjectToXmlString_PublicLegacy(object o, Type t)
        {
            byte[]? data = ObjectToXml_PublicLegacy(o, t);

            if (data == null) return "";

            return Str.Utf8Encoding.GetString(data);
        }

        [return: NotNullIfNotNull("o")]
        public static byte[]? ObjectToXml_PublicLegacy(object o, Type t)
        {
            if (o == null)
            {
                return null;
            }

            MemoryStream ms = new MemoryStream();
            XmlSerializer x = new XmlSerializer(t);

            x.Serialize(ms, o);

            return ms.ToArray();
        }

        public static DataContractJsonSerializerSettings NewDefaultRuntimeJsonSerializerSettings()
        {
            return new DataContractJsonSerializerSettings()
            {
                DateTimeFormat = new DateTimeFormat("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK"),
                UseSimpleDictionaryFormat = true,
            };
        }

        public static void ObjectToRuntimeJson(object obj, MemoryBuffer<byte> dst, DataContractJsonSerializerSettings? settings = null)
        {
            if (settings == null) settings = NewDefaultRuntimeJsonSerializerSettings();
            DataContractJsonSerializer d = new DataContractJsonSerializer(obj.GetType(), settings);
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(dst._AsDirectStream(), Str.Utf8Encoding, false, true, "  "))
            {
                d.WriteObject(writer, obj);
            }
        }
        public static byte[] ObjectToRuntimeJson(object obj, DataContractJsonSerializerSettings? settings = null)
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
            ObjectToRuntimeJson(obj, buf, settings);
            return buf.Span.ToArray();
        }

        public static object? RuntimeJsonToObject(MemoryBuffer<byte> src, Type type, DataContractJsonSerializerSettings? settings = null)
        {
            if (settings == null) settings = NewDefaultRuntimeJsonSerializerSettings();
            DataContractJsonSerializer d = new DataContractJsonSerializer(type, settings);
            return d.ReadObject(src._AsDirectStream());
        }
        public static T? RuntimeJsonToObject<T>(MemoryBuffer<byte> src, DataContractJsonSerializerSettings? settings = null) => (T?)RuntimeJsonToObject(src, typeof(T), settings);

        public static object? RuntimeJsonToObject(byte[] src, Type type, DataContractJsonSerializerSettings? settings = null) => RuntimeJsonToObject(src._AsMemoryBuffer(), type, settings);
        public static T? RuntimeJsonToObject<T>(byte[] src, DataContractJsonSerializerSettings? settings = null) => RuntimeJsonToObject<T>(src._AsMemoryBuffer(), settings);



        public static void ObjectToXml(object obj, MemoryBuffer<byte> dst, DataContractSerializerSettings? settings = null)
        {
            if (settings == null)
            {
                settings = new DataContractSerializerSettings();
                settings.PreserveObjectReferences = true;
            }
            DataContractSerializer d = new DataContractSerializer(obj.GetType(), settings);
            d.WriteObject(dst._AsDirectStream(), obj);
        }
        public static byte[] ObjectToXml(object obj, DataContractSerializerSettings? settings = null)
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
            ObjectToXml(obj, buf, settings);
            return buf.Span.ToArray();
        }

        public static object? XmlToObject(MemoryBuffer<byte> src, Type type, DataContractSerializerSettings? settings = null)
        {
            if (settings == null)
            {
                settings = new DataContractSerializerSettings();
                settings.PreserveObjectReferences = true;
            }
            DataContractSerializer d = new DataContractSerializer(type, settings);
            return d.ReadObject(src._AsDirectStream());
        }
        public static T? XmlToObject<T>(MemoryBuffer<byte> src, DataContractSerializerSettings? settings = null) => (T?)XmlToObject(src, typeof(T), settings);

        public static object? XmlToObject(byte[] src, Type type, DataContractSerializerSettings? settings = null) => XmlToObject(src._AsMemoryBuffer(), type, settings);
        public static T? XmlToObject<T>(byte[] src, DataContractSerializerSettings? settings = null) => XmlToObject<T>(src._AsMemoryBuffer(), settings);

        // オブジェクトをクローンする
        [return: NotNullIfNotNull("o")]
        public static object? CloneObjectDeep(object? o, DeepCloneMethod method = DeepCloneMethod.Default)
        {
            if (o == null) return null;
            if (method == DeepCloneMethod.Default)
            {
                method = CoresConfig.DeepCloneSettings.DefaultDeepCloneMethod;
            }

            if (method == DeepCloneMethod.BinaryFormatter)
            {
                return BinaryToObject(ObjectToBinary(o));
            }
            else
            {
                return IPA.Cores.Basic.Internal.DeepCloner.Helpers.DeepClonerGenerator.CloneObject2(o);
            }
        }

        // オブジェクトをバイナリに変換する
        public static byte[] ObjectToBinary(object o)
        {
            BinaryFormatter f = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
#pragma warning disable SYSLIB0011 // 型またはメンバーが旧型式です
            f.Serialize(ms, o);
#pragma warning restore SYSLIB0011 // 型またはメンバーが旧型式です

            return ms.ToArray();
        }

        // バイナリをオブジェクトに変換する
        public static object BinaryToObject(ReadOnlySpan<byte> data)
        {
            BinaryFormatter f = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            ms.Write(data);
            ms.Position = 0;

#pragma warning disable SYSLIB0011 // 型またはメンバーが旧型式です
            return f.Deserialize(ms);
#pragma warning restore SYSLIB0011 // 型またはメンバーが旧型式です
        }

        // オブジェクトの内容をクローンする
        public static object CloneObject_UsingXml_PublicLegacy(object o)
        {
            byte[] data = Util.ObjectToXml_PublicLegacy(o);

            return Util.XmlToObject_PublicLegacy(data, o.GetType())!;
        }

        // XML をオブジェクトに変換
        public static object? XmlToObject_PublicLegacy(string? str, Type t)
        {
            if (Str.IsEmptyStr(str))
            {
                return null;
            }

            byte[] data = Str.Utf8Encoding.GetBytes(str);

            return XmlToObject_PublicLegacy(data, t);
        }

        public static object? XmlToObject_PublicLegacy(byte[] data, Type t)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Position = 0;

            XmlSerializer x = new XmlSerializer(t);

            return x.Deserialize(ms);
        }

        // 何もしない
        public static void NoOP(object o)
        {
        }
        public static void NoOP()
        {
        }
        public static void DoNothing()
        {
        }
        public static void DoNothing(object o)
        {
        }
        public static System.Threading.Tasks.Task DoNothingAsync()
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public static int RetZero()
        {
            return 0;
        }

        // false
        public static bool False => false;

        // true
        public static bool True => true;

        // Zero
        public static int Zero => 0;

        // バイト配列から構造体にコピー
        public static object Legacy_ByteToStruct(byte[] src, Type type)
        {
            int size = src.Length;
            if (size != SizeOfStruct(type))
            {
                throw new SystemException("size error");
            }

            IntPtr p = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.Copy(src, 0, p, size);

                var ret = Marshal.PtrToStructure(p, type);

                if (ret == null) throw new NullReferenceException();

                return ret;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        // 構造体をバイト配列にコピー
        public static byte[] Legacy_StructToByte(object obj)
        {
            int size = SizeOfStruct(obj);
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(obj, p, false);

                byte[] ret = new byte[size];

                Marshal.Copy(p, ret, 0, size);

                return ret;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        // 構造体のサイズの取得
        [MethodImpl(Inline)]
        public static int SizeOfStruct(object obj)
            => Marshal.SizeOf(obj);

        [MethodImpl(Inline)]
        public static int SizeOfStruct(Type type)
            => Marshal.SizeOf(type);

        [MethodImpl(Inline)]
        public static unsafe int SizeOfStruct<T>() where T : unmanaged
            => sizeof(T);

        [MethodImpl(Inline)]
        public static unsafe ReadOnlySpan<byte> BytePtrToReadOnlySpan(byte* ptr, int size)
        {
            return new ReadOnlySpan<byte>(ptr, size);
        }

        [MethodImpl(Inline)]
        public static unsafe Span<byte> BytePtrToSpan(byte* ptr, int size)
        {
            return new Span<byte>(ptr, size);
        }

        // オブジェクトから XML とスキーマを生成
        public static XmlAndXsd GenerateXmlAndXsd(object obj)
        {
            Type type = obj.GetType();

            string xsdFileName = Str.MakeSafeFileName(type.Name + ".xsd");
            byte[] xsdData = GetXmlSchemaFromType_PublicLegacy(type);

            string xmlFileName = Str.MakeSafeFileName(type.Name + ".xml");
            string str = Util.ObjectToXmlString_PublicLegacy(obj);
            str = str.Replace(
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"",
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xsi:noNamespaceSchemaLocation=\""
                + xsdFileName
                + "\"");
            byte[] xmlData = Str.Utf8Encoding.GetBytes(str);

            return new XmlAndXsd(
                xmlFileName: xmlFileName,
                xmlData: xmlData,
                xsdFileName: xsdFileName,
                xsdData: xsdData
                );
        }

        // 何でも登録するリスト
        readonly static LinkedList<object> blackhole = new LinkedList<object>();
        public static void AddToBlackhole(object obj)
        {
            if (obj == null) return;

            lock (blackhole)
            {
                blackhole.AddLast(obj);
            }
        }

        // Enum の値一覧を取得する
        public static T[] GetEnumValuesList<T>() where T : unmanaged, Enum
        {
            List<T> ret = new List<T>();
            return (T[])Enum.GetValues(typeof(T));
        }

        public static T GetMaxEnumValue<T>() where T : unmanaged, Enum
        {
            return GetEnumValuesList<T>().OrderBy(x => x).LastOrDefault();
        }

        public static int GetMaxEnumValueSInt32<T>() where T : unmanaged, Enum
        {
            T maxValue = GetMaxEnumValue<T>();
            return maxValue._RawReadValueSInt32();
        }


        public static byte[] Rand(int size) { byte[] r = new byte[size]; Rand(r); return r; }

        public static double RandDouble0To1()
        {
            lock (RandomShared)
            {
                return RandomShared.NextDouble();
            }
        }

        public static void Rand(Span<byte> dest)
        {
            if (Env.IsWindows)
            {
                // In the Windows system, Secure.Rand() is faster.
                Secure.Rand(dest);
                return;
            }

            // In UNIX systems, Util.Rand() is faster.
            lock (RandomShared)
            {
                RandomShared.NextBytes(dest);
            }
        }

        public static double RandDouble(double a, double b)
        {
            double minimum = Math.Min(a, b);
            double maximum = Math.Max(a, b);

            return RandDouble0To1() * (maximum - minimum) + minimum;
        }

        [SkipLocalsInit]
        public static byte RandUInt8()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            return mem._GetUInt8();
        }

        [SkipLocalsInit]
        public static ushort RandUInt16()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            return mem._GetUInt16();
        }

        [SkipLocalsInit]
        public static uint RandUInt32()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            return mem._GetUInt32();
        }

        [SkipLocalsInit]
        public static ulong RandUInt64()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            return mem._GetUInt64();
        }

        [SkipLocalsInit]
        public static byte RandUInt7()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt8();
        }

        [SkipLocalsInit]
        public static ushort RandUInt15()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt16();
        }

        [SkipLocalsInit]
        public static uint RandUInt31()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt32();
        }

        [SkipLocalsInit]
        public static ulong RandUInt63()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt64();
        }

        [SkipLocalsInit]
        public static sbyte RandSInt8_Caution()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            return mem._GetSInt8();
        }

        [SkipLocalsInit]
        public static short RandSInt16_Caution()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            return mem._GetSInt16();
        }

        [SkipLocalsInit]
        public static int RandSInt32_Caution()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            return mem._GetSInt32();
        }

        [SkipLocalsInit]
        public static long RandSInt64_Caution()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            return mem._GetSInt64();
        }

        [SkipLocalsInit]
        public static sbyte RandSInt7()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt8();
        }

        [SkipLocalsInit]
        public static short RandSInt15()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt16();
        }

        [SkipLocalsInit]
        public static int RandSInt31()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt32();
        }

        [SkipLocalsInit]
        public static long RandSInt63()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt64();
        }

        public static bool RandBool()
        {
            return (RandUInt32() % 2) == 0;
        }

        public static int GetRandWithPercentageInt(int standard, double plusMinusPercentage = 30.0)
        {
            if (standard == 0) return 0;
            if (standard < 0) throw new ArgumentOutOfRangeException(nameof(standard));
            double rate = plusMinusPercentage / 100.0;
            int v = (int)((double)standard * rate);
            if (v == 0) return standard;
            bool b = Util.RandBool();
            v = Util.RandSInt31() % v;
            long ret;
            if (b)
                ret = standard + v;
            else
                ret = standard - v;
            ret = Math.Min(ret, int.MaxValue);
            return Math.Max((int)ret, 1);
        }

        public static int GenRandIntervalWithRetry(int standard, int numRetry, int max, double plusMinusPercentage = 30.0)
        {
            numRetry = Math.Max(numRetry, 1);

            int v = standard * numRetry;
            v = Math.Min(v, max);

            return GenRandInterval(v, plusMinusPercentage);
        }

        public static TimeSpan GenRandInterval(TimeSpan standard, double plusMinusPercentage = 30.0)
            => TimeSpan.FromMilliseconds(GenRandInterval((int)standard.TotalMilliseconds, plusMinusPercentage));

        public static int GenRandInterval(int standard, double plusMinusPercentage = 30.0)
        {
            double rate = plusMinusPercentage / 100.0;
            int v = (int)((double)standard * rate);
            if (v == 0) return standard;
            bool b = Util.RandBool();
            v = Util.RandSInt31() % v;
            int ret;
            if (b)
                ret = standard + v;
            else
                ret = standard - v;
            return Math.Max(ret, 1);
        }

        public static int GenRandInterval(int min, int max)
        {
            int a = Math.Min(min, max);
            int b = Math.Max(min, max);

            if (a == b)
            {
                return a;
            }

            return (RandSInt31() % (b - 1)) + a;
        }

        public static object CreateNewSampleObjectFromTypeWithoutParam(Type t)
        {
            if (t == typeof(string)) return "Hello World";

            if (t == typeof(SByte)) return (SByte)12;
            if (t == typeof(Int16)) return (Int16)123;
            if (t == typeof(Int32)) return (Int32)123;
            if (t == typeof(Int64)) return (Int64)123;

            if (t == typeof(Byte)) return (Byte)123;
            if (t == typeof(UInt16)) return (UInt16)123;
            if (t == typeof(UInt32)) return (UInt32)123;
            if (t == typeof(UInt64)) return (UInt64)123;

            return CreateObjectFromType(t);
        }

        public static T CreateObjectFromType<T>(params object?[]? args)
            => (T)CreateObjectFromType(typeof(T), args);

        public static object CreateObjectFromType(Type t, params object?[]? args)
        {
            object? ret = Activator.CreateInstance(t, args);
            ret._NullCheck();
            return ret;
        }

        public static T NewWithoutConstructor<T>()
            => (T)NewWithoutConstructor(typeof(T));

        public static object NewWithoutConstructor(Type t)
            => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t);

        // baseData に overwriteData の値 (NULL 以外の場合) を上書きしたオブジェクトを返す
        public static T DbOverwriteValues<T>(T baseData, T overwriteData) where T : new()
        {
            T ret = new T();
            Type t = typeof(T);
            var propertyList = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in propertyList)
            {
                var propertyType = p.PropertyType;
                object? value = p.GetValue(overwriteData);
                if (value._IsEmpty())
                {
                    value = p.GetValue(baseData);
                }
                p.SetValue(ret, value);
            }

            DbEnforceNonNull(ret);

            return ret;
        }

        // データベースのテーブルのクラスで Non NULL を強制する
        public static void DbEnforceNonNull(object obj)
        {
            Type t = obj.GetType();

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                var ptype = p.PropertyType;
                if (ptype._IsNullable() == false)
                {
                    if (ptype == typeof(string))
                    {
                        string? s = (string?)p.GetValue(obj);
                        if (s == null) p.SetValue(obj, "");
                    }
                    else if (ptype == typeof(DateTime))
                    {
                        DateTime? d = (DateTime?)p.GetValue(obj);
                        if (d?._IsZeroDateTime() ?? true) p.SetValue(obj, Util.ZeroDateTimeValue);
                    }
                    else if (ptype == typeof(DateTimeOffset))
                    {
                        DateTimeOffset? d = (DateTimeOffset?)p.GetValue(obj);
                        if (d?._IsZeroDateTime() ?? true) p.SetValue(obj, Util.ZeroDateTimeOffsetValue);
                    }
                    else if (ptype == typeof(byte[]))
                    {
                        byte[]? b = (byte[]?)p.GetValue(obj);
                        if (b == null) p.SetValue(obj, new byte[0]);
                    }
                }
            }
        }

        public static T CloneIfClonable<T>(T obj)
        {
            if (obj._IsNullOrDefault()) return default!;
            if (typeof(T).IsClass == false) return obj;
            if (obj is ICloneable clonable) return (T)clonable.Clone();
            return obj;
        }

        public static TResult DoMultipleFuncs<TResult>(MultipleActionsFlag flag, CancellationToken cancel, params Func<TResult>[] funcs)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception? firstException = null;

            TResult firstRet = default!;
            bool? isFirstRetSet = false;
            TResult lastRet = default!;

            if (funcs != null)
            {
                foreach (Func<TResult> func in funcs)
                {
                    if (func != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            lastRet = func();

                            if (isFirstRetSet == false)
                            {
                                firstRet = lastRet;
                                isFirstRetSet = true;
                            }

                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return firstRet;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        throw firstException!;
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                    if (anyOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return lastRet;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        public static async Task<TResult> DoMultipleFuncsAsync<TResult>(MultipleActionsFlag flag, CancellationToken cancel, params Func<Task<TResult>>[] funcs)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception? firstException = null;

            TResult firstRet = default!;
            bool isFirstRetSet = false;
            TResult lastRet = default!;

            if (funcs != null)
            {
                foreach (Func<Task<TResult>> func in funcs)
                {
                    if (func != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            lastRet = await func();

                            if (isFirstRetSet == false)
                            {
                                firstRet = lastRet;
                                isFirstRetSet = true;
                            }

                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return firstRet;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        throw firstException!;
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                    if (anyOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return lastRet;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        public static bool DoMultipleActions(MultipleActionsFlag flag, CancellationToken cancel, params Action[] actions)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception? firstException = null;

            if (actions != null)
            {
                foreach (Action action in actions)
                {
                    if (action != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            action();
                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return anyOk;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return true;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return allOk;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }


        public static async Task<bool> DoMultipleActionsAsync(MultipleActionsFlag flag, CancellationToken cancel, params Func<Task>[] actions)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception? firstException = null;

            if (actions != null)
            {
                foreach (Func<Task> action in actions)
                {
                    if (action != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            await action();
                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return anyOk;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return true;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException!._ReThrow();
                    }
                    return allOk;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        public static unsafe bool IsSpanAllZero(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return true;

            fixed (byte* srcPointer = span)
            {
                long srcLen = span.Length;
                long startLong = (long)srcPointer;
                long endLong = startLong + srcLen;

                startLong = ((startLong + 7) / 8) * 8;
                endLong = (endLong / 8) * 8;

                for (long i = startLong; i < endLong; i += 8)
                {
                    if (*((long*)i) != 0)
                    {
                        return false;
                    }
                }

                for (byte* p = srcPointer; p < (byte*)startLong; p++)
                    if (*p != 0) return false;

                byte* endp = srcPointer + srcLen;
                for (byte* p = (byte*)endLong; p < endp; p++)
                    if (*p != 0) return false;

                return true;
            }
        }

        [Flags]
        public enum SpanSparseInfo
        {
            NotSparse,
            ContainsSparse,
            AllZero,
        }

        public static unsafe SpanSparseInfo GetSpanSparseInfo(ReadOnlySpan<byte> srcMemory, int minZeroBlockSize)
        {
            if (srcMemory.Length == 0) return SpanSparseInfo.AllZero;
            if (minZeroBlockSize <= 7 || minZeroBlockSize == int.MaxValue) return SpanSparseInfo.NotSparse;

            fixed (byte* srcPointer = srcMemory)
            {
                long srcLen = srcMemory.Length;
                long startLong = (long)srcPointer;
                long endLong = startLong + srcLen;

                startLong = ((startLong + 7) / 8) * 8;
                endLong = (endLong / 8) * 8;

                int blockSizeCount = (minZeroBlockSize / 8);

                int count = 0;

                bool hasSparse = false;
                bool hasNonZero = false;

                for (long i = startLong; i < endLong; i += 8)
                {
                    if (*((long*)i) == 0)
                    {
                        count++;
                        if (count >= blockSizeCount)
                        {
                            hasSparse = true;
                        }
                    }
                    else
                    {
                        count = 0;
                        hasNonZero = true;
                    }
                }

                if (hasSparse)
                {
                    if (hasNonZero)
                    {
                        return SpanSparseInfo.ContainsSparse;
                    }
                    else
                    {
                        for (byte* p = srcPointer; p < (byte*)startLong; p++)
                            if (*p != 0) return SpanSparseInfo.ContainsSparse;

                        byte* endp = srcPointer + srcLen;
                        for (byte* p = (byte*)endLong; p < endp; p++)
                            if (*p != 0) return SpanSparseInfo.ContainsSparse;

                        return SpanSparseInfo.AllZero;
                    }
                }
                else
                {
                    return SpanSparseInfo.NotSparse;
                }
            }
        }

        public static unsafe IReadOnlyList<SparseChunk<byte>> GetSparseChunks(ReadOnlyMemory<byte> srcMemory, int minZeroBlockSize)
        {
            checked
            {
                List<SparseChunk<byte>> ret = new List<SparseChunk<byte>>();

                if (srcMemory.Length == 0)
                {
                    return ret;
                }

                SpanSparseInfo sparseInfo = GetSpanSparseInfo(srcMemory.Span, minZeroBlockSize);

                if (sparseInfo == SpanSparseInfo.NotSparse)
                {
                    ret.Add(new SparseChunk<byte>(0, srcMemory));
                    return ret;
                }
                else if (sparseInfo == SpanSparseInfo.AllZero)
                {
                    ret.Add(new SparseChunk<byte>(0, srcMemory.Length));
                    return ret;
                }

                int mode = -1;
                int lastMode = -1;
                int pos1 = 0, pos2 = 0;
                long offset = 0;

                minZeroBlockSize = Math.Max(minZeroBlockSize, 1);

                ReadOnlySpan<byte> srcSpan = srcMemory.Span;
                int length = srcSpan.Length;

                int currentPos;

                for (currentPos = 0; currentPos < length; currentPos++)
                {
                    mode = (srcSpan[currentPos] == 0) ? 0 : 1;

                    if (lastMode != mode)
                    {
                        lastMode = mode;
                        if (mode == 1)
                        {
                            if ((currentPos - pos2) >= minZeroBlockSize)
                            {
                                int size1 = pos2 - pos1;
                                if (size1 >= 1)
                                {
                                    var chunk1 = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size1));
                                    offset += size1;
                                    ret.Add(chunk1);
                                }

                                int size2 = currentPos - pos2;
                                if (size2 >= 1)
                                {
                                    var chunk2 = new SparseChunk<byte>(offset, size2);
                                    offset += size2;
                                    ret.Add(chunk2);
                                }

                                pos1 = currentPos;
                                pos2 = currentPos;
                            }
                        }
                        else if (mode == 0)
                        {
                            pos2 = currentPos;
                        }
                    }
                }

                currentPos = length;

                if (mode == 0)
                {
                    if ((currentPos - pos2) >= minZeroBlockSize)
                    {
                        int size1 = pos2 - pos1;
                        if (size1 >= 1)
                        {
                            var chunk1 = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size1));
                            offset += size1;
                            ret.Add(chunk1);
                        }

                        int size2 = currentPos - pos2;
                        if (size2 >= 1)
                        {
                            var chunk2 = new SparseChunk<byte>(offset, size2);
                            offset += size2;
                            ret.Add(chunk2);
                        }
                    }
                    else
                    {
                        int size = currentPos - pos1;
                        if (size >= 1)
                        {
                            var chunk = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size));
                            offset += size;
                            ret.Add(chunk);
                        }
                    }
                }
                else if (mode == 1)
                {
                    int size = currentPos - pos1;
                    if (size >= 1)
                    {
                        var chunk = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size));
                        offset += size;
                        ret.Add(chunk);
                    }
                }

                return ret;
            }
        }

        public static int CopySparseChunkListToSpan<T>(IReadOnlyList<SparseChunk<T>> chunkList, Span<T> dest)
        {
            Span<T> pos = dest;

            int size = 0;

            foreach (var chunk in chunkList)
            {
                if (chunk.IsSparse == false)
                {
                    pos._WalkWrite(chunk.Memory.Span);
                    size += chunk.Memory.Span.Length;
                }
                else
                {
                    pos._Walk(chunk.Size).Fill(default!);
                    size += chunk.Size;
                }
            }

            return size;
        }

        public static ReadOnlySpan<ReadOnlyMemory<T>> GetTailOfReadOnlyMemoryArray<T>(ReadOnlySpan<ReadOnlyMemory<T>> itemList, long maxSize, out long returnTotalSize, bool doNotSplitSegment = false)
        {
            checked
            {
                returnTotalSize = 0;

                if (maxSize <= 0)
                {
                    return new ReadOnlySpan<ReadOnlyMemory<T>>();
                }

                Span<ReadOnlyMemory<T>> ret = new ReadOnlyMemory<T>[itemList.Length];

                int index = 0;

                for (int i = itemList.Length - 1; i >= 0; i--)
                {
                    ref readonly ReadOnlyMemory<T> item = ref itemList[i];
                    if (item.Length >= 1)
                    {
                        long sizeToRead;

                        if (doNotSplitSegment == false)
                        {
                            sizeToRead = Math.Min(item.Length, maxSize);
                            ret[index++] = item.Slice(item.Length - (int)sizeToRead);
                        }
                        else
                        {
                            sizeToRead = item.Length;
                            ret[index++] = item;
                        }

                        maxSize -= sizeToRead;
                        returnTotalSize += sizeToRead;
                        if (maxSize <= 0)
                        {
                            break;
                        }
                    }
                }

                ret = ret.Slice(0, index);

                ret.Reverse();

                return ret;
            }
        }

        public static ReadOnlySpan<ReadOnlyMemory<T>> GetHeadOfReadOnlyMemoryArray<T>(ReadOnlySpan<ReadOnlyMemory<T>> itemList, long maxSize, out long returnTotalSize, bool doNotSplitSegment = false)
        {
            checked
            {
                returnTotalSize = 0;

                if (maxSize <= 0)
                {
                    return new ReadOnlySpan<ReadOnlyMemory<T>>();
                }

                Span<ReadOnlyMemory<T>> ret = new ReadOnlyMemory<T>[itemList.Length];

                int index = 0;

                for (int i = 0; i < itemList.Length; i++)
                {
                    ref readonly ReadOnlyMemory<T> item = ref itemList[i];
                    if (item.Length >= 1)
                    {
                        long sizeToRead;
                        if (doNotSplitSegment == false)
                        {
                            sizeToRead = Math.Min(item.Length, maxSize);
                            ret[index++] = item.Slice(0, (int)sizeToRead);
                        }
                        else
                        {
                            sizeToRead = item.Length;
                            ret[index++] = item;
                        }

                        maxSize -= sizeToRead;
                        returnTotalSize += sizeToRead;
                        if (maxSize <= 0)
                        {
                            break;
                        }
                    }
                }

                return ret.Slice(0, index);
            }
        }

        // Stream を最後まで読む
        public static byte[] ReadStreamToEnd(Stream s, int maxSize = 0)
        {
            if (maxSize <= 0) maxSize = int.MaxValue;
            MemoryStream ms = new MemoryStream();

            byte[] tmp = MemoryHelper.FastAllocMoreThan<byte>(Consts.Numbers.DefaultLargeBufferSize);
            try
            {
                while (true)
                {
                    int r = s.Read(tmp, 0, tmp.Length);
                    if (r == 0)
                    {
                        break;
                    }
                    ms.Write(tmp, 0, r);
                    if (ms.Length > maxSize) throw new OverflowException();
                }
            }
            finally
            {
                MemoryHelper.FastFree(tmp);
            }

            return ms.ToArray();
        }

        public static async Task<HugeMemoryBuffer<byte>> ReadStreamToEndHugeAsync(Stream s, long maxSize = 0, CancellationToken cancel = default(CancellationToken))
        {
            if (maxSize <= 0) maxSize = long.MaxValue;
            HugeMemoryBuffer<byte> ret = new();

            byte[] tmp = MemoryHelper.FastAllocMoreThan<byte>(Consts.Numbers.DefaultLargeBufferSize);
            try
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    int r = await s.ReadAsync(tmp, 0, tmp.Length, cancel);
                    if (r == 0)
                    {
                        break;
                    }
                    ret.Write(tmp, 0, r);
                    if (ret.Length > maxSize) throw new OverflowException("ReadStreamToEndHugeAsync: too large data");
                }
            }
            finally
            {
                MemoryHelper.FastFree(tmp);
            }

            ret.SeekToBegin();

            return ret;
        }

        public static async Task<byte[]> ReadStreamToEndAsync(Stream s, int maxSize = 0, CancellationToken cancel = default(CancellationToken))
        {
            if (maxSize <= 0) maxSize = int.MaxValue;
            MemoryStream ms = new MemoryStream();

            byte[] tmp = MemoryHelper.FastAllocMoreThan<byte>(Consts.Numbers.DefaultLargeBufferSize);
            try
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    int r = await s.ReadAsync(tmp, 0, tmp.Length, cancel);
                    if (r == 0)
                    {
                        break;
                    }
                    ms.Write(tmp, 0, r);
                    if (ms.Length > maxSize) throw new OverflowException("ReadStreamToEndAsync: too large data");
                }
            }
            finally
            {
                MemoryHelper.FastFree(tmp);
            }

            return ms.ToArray();
        }

        public static async Task<long> CopyHugeMemoryBufferToStreamAsync(Stream stream, HugeMemoryBuffer<byte> hugeMemoryBuffer, CancellationToken cancel = default)
        {
            long size = hugeMemoryBuffer.LongLength;
            byte[] tmp = MemoryHelper.FastAllocMoreThan<byte>(Consts.Numbers.DefaultLargeBufferSize);
            long writtenSize = 0;
            try
            {
                while (true)
                {
                    int sz = hugeMemoryBuffer.Read(tmp, allowPartial: true);

                    if (sz <= 0)
                    {
                        break;
                    }

                    await stream.WriteAsync(tmp, 0, sz, cancellationToken: cancel);
                    writtenSize += sz;
                }

                return writtenSize;
            }
            finally
            {
                MemoryHelper.FastFree(tmp);
            }
        }

        // Stream 間のデータ中継 (双方向)
        public static async Task RelayDuplexStreamAsync(Stream st1, Stream st2, CancellationToken cancel = default, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalBytes = null, Func<Memory<byte>, bool, Task>? peakCallbackAsync = null)
        {
            await using CancelWatcher w = new CancelWatcher(cancel);

            Task relay1to2 = RelaySimplexStreamAsync(st1, st2, w.CancelToken, bufferSize, totalBytes, true, peakCallbackAsync);
            Task relay2to1 = RelaySimplexStreamAsync(st2, st1, w.CancelToken, bufferSize, totalBytes, false, peakCallbackAsync);

            await TaskUtil.WaitObjectsAsync(new Task[] { relay1to2, relay2to1 }, cancel._SingleArray());

            w.Cancel();

            await relay1to2._TryAwait();
            await relay2to1._TryAwait();

            if (relay1to2.Exception != null) throw relay1to2.Exception;
            if (relay2to1.Exception != null) throw relay2to1.Exception;
        }

        // Stream 間のデータ中継 (一方向)
        public static async Task RelaySimplexStreamAsync(Stream src, Stream dest, CancellationToken cancel = default, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? totalBytes = null, bool st1Tost2 = false, Func<Memory<byte>, bool, Task>? peakCallbackAsync = null)
        {
            await Task.Yield();

            Memory<byte> buffer = new byte[bufferSize];
            while (true)
            {
                int sz = await src.ReadAsync(buffer, cancel);
                if (sz <= 0)
                {
                    break;
                }

                var recvData = buffer.Slice(0, sz);

                if (peakCallbackAsync != null)
                {
                    await peakCallbackAsync(recvData, st1Tost2);
                }

                await dest.WriteAsync(recvData, cancel);

                totalBytes?.Add(sz);
            }
        }

        // Stream から Stream へのコピー (ファイルのダウンロードなど)
        public static async Task<long> CopyBetweenStreamAsync(Stream src, Stream dest, CopyFileParams? param = null, ProgressReporterBase? reporter = null,
            long estimatedSize = -1, CancellationToken cancel = default, Ref<uint>? srcZipCrc = null, long truncateSize = -1, bool flush = false, int readTimeout = Timeout.Infinite, int writeTimeout = Timeout.Infinite)
        {
            if (param == null) param = new CopyFileParams();
            if (reporter == null) reporter = new NullProgressReporter(null);
            if (param.IgnoreReadError) throw new ArgumentException(nameof(param.IgnoreReadError));
            if (srcZipCrc == null) srcZipCrc = new Ref<uint>();

            if (readTimeout <= 0) readTimeout = Timeout.Infinite;
            if (writeTimeout <= 0) writeTimeout = Timeout.Infinite;

            if (truncateSize >= 0)
            {
                estimatedSize = Math.Min(estimatedSize, truncateSize);
            }

            ZipCrc32 srcCrc = new ZipCrc32();

            checked
            {
                long currentPosition = 0;

                if (param.AsyncCopy == false)
                {
                    // Normal copy
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
                    {
                        while (true)
                        {
                            Memory<byte> thisTimeBuffer = buffer;

                            if (truncateSize >= 0)
                            {
                                // Truncate
                                long remainSize = Math.Max(truncateSize - currentPosition, 0);

                                if (thisTimeBuffer.Length > remainSize)
                                {
                                    thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                                }

                                if (remainSize == 0) break;
                            }


                            int readSize;

                            if (readTimeout == Timeout.Infinite)
                            {
                                readSize = await src.ReadAsync(thisTimeBuffer, cancel);
                            }
                            else
                            {
                                readSize = await src._ReadAsyncWithTimeout(thisTimeBuffer, readTimeout, cancel: cancel, allowEof: true);
                            }

                            Debug.Assert(readSize <= thisTimeBuffer.Length);

                            if (readSize <= 0) break;

                            ReadOnlyMemory<byte> sliced = thisTimeBuffer.Slice(0, readSize);

                            if (param.Flags.Bit(FileFlags.CopyFile_Verify))
                            {
                                srcCrc.Append(sliced.Span);
                            }

                            if (writeTimeout == Timeout.Infinite)
                            {
                                await dest.WriteAsync(sliced, cancel);
                            }
                            else
                            {
                                await dest._WriteAsyncWithTimeout(sliced, writeTimeout, cancel);
                            }

                            currentPosition += readSize;
                            reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                        }
                    }
                }
                else
                {
                    // Async copy
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer1))
                    {
                        using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer2))
                        {
                            Task? lastWriteTask = null;
                            int number = 0;
                            int writeSize = 0;

                            long currentReadPosition = 0;

                            Memory<byte>[] buffers = new Memory<byte>[2] { buffer1, buffer2 };

                            while (true)
                            {
                                Memory<byte> buffer = buffers[(number++) % 2];

                                Memory<byte> thisTimeBuffer = buffer;

                                if (truncateSize >= 0)
                                {
                                    // Truncate
                                    long remainSize = Math.Max(truncateSize - currentReadPosition, 0);

                                    if (thisTimeBuffer.Length > remainSize)
                                    {
                                        thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                                    }
                                }

                                int readSize;

                                if (readTimeout == Timeout.Infinite)
                                {
                                    readSize = await src.ReadAsync(thisTimeBuffer, cancel);
                                }
                                else
                                {
                                    readSize = await src._ReadAsyncWithTimeout(thisTimeBuffer, readTimeout, cancel: cancel, allowEof: true);
                                }

                                Debug.Assert(readSize <= buffer.Length);

                                if (lastWriteTask != null)
                                {
                                    await lastWriteTask;
                                    currentPosition += writeSize;
                                    reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                                }

                                if (readSize <= 0) break;

                                currentReadPosition += readSize;

                                writeSize = readSize;

                                ReadOnlyMemory<byte> sliced = buffer.Slice(0, writeSize);

                                if (param.Flags.Bit(FileFlags.CopyFile_Verify))
                                {
                                    srcCrc.Append(sliced.Span);
                                }

                                lastWriteTask = AsyncAwait(async () =>
                                {
                                    if (writeTimeout == Timeout.Infinite)
                                    {
                                        await dest.WriteAsync(sliced, cancel);
                                    }
                                    else
                                    {
                                        await dest._WriteAsyncWithTimeout(sliced, writeTimeout, cancel);
                                    }
                                });
                            }

                            reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                        }
                    }
                }

                srcZipCrc.Set(srcCrc.Value);

                if (flush)
                {
                    try
                    {
                        await dest.FlushAsync(cancel);
                    }
                    catch { }
                }

                return currentPosition;
            }
        }

        public static IReadOnlyList<ReadOnlyMemory<T>> DefragmentMemoryArrays<T>(IEnumerable<ReadOnlyMemory<T>> srcDataList, int minBlockSize = 0)
        {
            minBlockSize = Math.Max(minBlockSize, 1);

            List<ReadOnlyMemory<T>> ret = new List<ReadOnlyMemory<T>>();

            MemoryBuffer<T>? current = null;

            foreach (ReadOnlyMemory<T> src in srcDataList)
            {
                if (src.Length >= 1)
                {
                    if (src.Length >= minBlockSize)
                    {
                        if (current != null)
                        {
                            if (current.Length >= 1)
                            {
                                ret.Add(current);
                            }
                            current = null;
                        }
                        ret.Add(src);
                    }
                    else
                    {
                        if (current == null)
                        {
                            current = new MemoryBuffer<T>();
                        }

                        current.Write(src);

                        if (current.Length >= minBlockSize)
                        {
                            ret.Add(current);
                            current = null;
                        }
                    }
                }
            }

            if (current != null)
            {
                if (current.Length >= 1)
                {
                    ret.Add(current);
                }
            }

            return ret.ToArray();
        }

        public static void PutGitIgnoreFileOnDirectory(DirectoryPath dir, FileFlags flags = FileFlags.None)
        {
            try
            {
                var srcFilePath = new FilePath(Res.Cores, "CoresInternal/190521_LocalGitIgnore.txt");
                var destFilePath = dir.Combine(".gitignore");

                if (destFilePath.IsFileExists() == false)
                {
                    FileUtil.CopyFile(srcFilePath, destFilePath, new CopyFileParams(overwrite: false, flags: flags, retryCount: 0));
                }
            }
            catch { }
        }

        [MethodImpl(Inline)]
        public static int ComputeGoldenHash32(int src) => src * Consts.GoldenRatioPrime.S32;

        [MethodImpl(Inline)]
        public static uint ComputeGoldenHash32(uint src) => src * Consts.GoldenRatioPrime.U32;

        [MethodImpl(Inline)]
        public static int ComputeGoldenHash32(int src, int bits) => (src * Consts.GoldenRatioPrime.S32) >> (32 - bits);

        [MethodImpl(Inline)]
        public static uint ComputeGoldenHash32(uint src, int bits) => (src * Consts.GoldenRatioPrime.U32) >> (32 - bits);


        [MethodImpl(Inline)]
        public static long ComputeGoldenHash64(long src) => src * Consts.GoldenRatioPrime.S64;

        [MethodImpl(Inline)]
        public static ulong ComputeGoldenHash64(ulong src) => src * Consts.GoldenRatioPrime.U64;

        [MethodImpl(Inline)]
        public static long ComputeGoldenHash64(long src, int bits) => (src * Consts.GoldenRatioPrime.S64) >> (64 - bits);

        [MethodImpl(Inline)]
        public static ulong ComputeGoldenHash64(ulong src, int bits) => (src * Consts.GoldenRatioPrime.U64) >> (64 - bits);


        [MethodImpl(Inline)]
        public static unsafe int ComputeGoldenHash(void* src, int size)
        {
            return ComputeGoldenHash32(IPUtil.IpChecksum(src, size, 0xdead));
        }

        [MethodImpl(Inline)]
        public static unsafe int ComputeGoldenHash<T>(in T data, int size = DefaultSize) where T : unmanaged
        {
            size = size._DefaultSize(sizeof(T));
            return ComputeGoldenHash(Unsafe.AsPointer(ref Unsafe.AsRef(in data)), size);
        }

        [MethodImpl(Inline)]
        public static unsafe int ComputeGoldenHash<T>(ReadOnlySpan<T> span) where T : unmanaged
        {
            fixed (T* ptr = span)
            {
                return ComputeGoldenHash((void*)ptr, span.Length * sizeof(T));
            }
        }

        [MethodImpl(Inline)]
        public static ulong GetNextPowerOf2(ulong n)
        {
            --n;
            n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16; n |= n >> 32;
            return n + 1;
        }

        [MethodImpl(Inline)]
        public static uint GetNextPowerOf2(uint n)
        {
            --n;
            n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16;
            return n + 1;
        }

        [MethodImpl(Inline)]
        public static int GetGreaterOrEqualOptimiedSizePowerOf2(int equalOrLargeThan)
        {
            if (equalOrLargeThan >= 1073741824) return 2147483647;
            if (equalOrLargeThan <= 0) return 1;
            uint n = (uint)equalOrLargeThan;
            --n;
            n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16;
            return (int)(n + 1);
        }

        public static string NormalizeHttpProtocolString(string protocol)
        {
            if (protocol._IsSamei(Consts.HttpProtocolSchemes.Http))
            {
                return Consts.HttpProtocolSchemes.Http;
            }
            else if (protocol._IsSamei(Consts.HttpProtocolSchemes.Https))
            {
                return Consts.HttpProtocolSchemes.Https;
            }

            throw new ArgumentException(nameof(protocol));
        }

        public static int GetHttpProtocolDefaultPort(string protocol)
        {
            if (protocol._IsSamei(Consts.HttpProtocolSchemes.Http))
            {
                return Consts.Ports.Http;
            }
            else if (protocol._IsSamei(Consts.HttpProtocolSchemes.Https))
            {
                return Consts.Ports.Https;
            }

            throw new ArgumentException(nameof(protocol));
        }

        public static long HashDynamicLongValueWithSeed(string seed, long min, long max)
        {
            max._SetMax(min);

            long count = max - min + 1;

            if (count <= 1) return min;

            count = Math.Min(count, long.MaxValue);

            seed = seed._NonNullTrim().ToLowerInvariant();

            long rand = (long)(Secure.HashSHA1AsLong(seed._GetBytes_UTF8()) & long.MaxValue);

            return rand % count + min;
        }

        public static int GenerateDynamicListenableTcpPortWithSeed(string seed, int minPort = Consts.Ports.DynamicPortMin, int maxPort = Consts.Ports.DynamicPortMax, IEnumerable<int>? excludePorts = null)
        {
            if (excludePorts == null) excludePorts = EmptyEnumerable<int>.Empty;

            for (int i = 0; ; i++)
            {
                int port = (int)HashDynamicLongValueWithSeed(i.ToString() + seed, minPort, maxPort);

                if (i >= Consts.Ports.DynamicPortCheckRetryMaxCount)
                {
                    return port;
                }

                if (excludePorts.Contains(port) == false)
                {
                    if (PalSocket.CheckIsTcpPortListenable(port))
                    {
                        return port;
                    }
                }
            }
        }

        public static void DoSortBy<T>(List<T> list, Func<List<T>, IEnumerable<T>> sortFunc)
        {
            var result = sortFunc(list).ToList();

            if (result.Count != list.Count())
            {
                throw new CoresLibException("result.Count != list.Count()");
            }

            list.Clear();
            list.AddRange(result);
        }

        // fieldname1, fieldname2, obj.fieldname3 のような感じでソートする。
        // + を付けると正順ソート、- または ! を付けると逆順ソートになる。
        public static IEnumerable<T> OrderByFieldNames<T>(IEnumerable<T> obj, string fieldNames, int maxFields = 4)
        {
            var tokens = fieldNames._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', ',', '\t', '\r', '\n', '　');

            IEnumerable<T> current = obj;

            int num = 0;

            foreach (var token in tokens)
            {
                bool asc = true;

                string token2 = token;

                if (token2.StartsWith("+"))
                {
                    token2 = token2.Substring(1);
                }
                else if (token.StartsWith("!") || token.StartsWith("-"))
                {
                    token2 = token2.Substring(1);
                    asc = false;
                }

                if (token2._IsFilled())
                {
                    if (num >= maxFields)
                    {
                        throw new CoresLibException($"num ({num}) >= maxFields ({maxFields})");
                    }

                    if (num == 0)
                    {
                        if (asc)
                        {
                            current = current.OrderBy(x => GetFieldValueByDottedFieldNamesHelper_HadbSpecial(x, token2, true, true));
                        }
                        else
                        {
                            current = current.OrderByDescending(x => GetFieldValueByDottedFieldNamesHelper_HadbSpecial(x, token2, true, true));
                        }
                    }
                    else
                    {
                        if (asc)
                        {
                            current = ((IOrderedEnumerable<T>)current).ThenBy(x => GetFieldValueByDottedFieldNamesHelper_HadbSpecial(x, token2, true, true));
                        }
                        else
                        {
                            current = ((IOrderedEnumerable<T>)current).ThenByDescending(x => GetFieldValueByDottedFieldNamesHelper_HadbSpecial(x, token2, true, true));
                        }
                    }

                    num++;
                }
            }

            static object? GetFieldValueByDottedFieldNamesHelper_HadbSpecial(object? targetObject, string fieldName, bool ignoreCase = false, bool returnNullIfError = false, string? defaultFirstFieldName = null)
            {
                fieldName = fieldName._NonNullTrim();

                if (fieldName._TryTrimStartWith(out fieldName, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, ".meta.", "meta.", ".metadata.", "metadata.", "_.", "._.", ".obj.", "obj.", ".hadb.", "hadb."))
                {
                    return targetObject._GetValueByDottedFieldNames(fieldName, ignoreCase, returnNullIfError, "");
                }
                else
                {
                    return targetObject._GetValueByDottedFieldNames(fieldName, ignoreCase, returnNullIfError, nameof(HadbObject.UserData));
                }
            }

            return current;
        }

        public static void DoSortBy<T>(List<T> list, string fieldNames, int maxFields = 4)
        {
            DoSortBy(list, a => OrderByFieldNames(a, fieldNames, maxFields));
        }
    }


    public static class Marvin
    {
        // From: https://github.com/dotnet/corefx/blob/master/src/Common/src/System/Marvin.cs
        /* The MIT License (MIT)
         * Copyright (c) .NET Foundation and Contributors
         * All rights reserved.
         * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal
         * in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
         * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
         * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
         * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
         * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
         * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
         * SOFTWARE. */

        [MethodImpl(Inline)]
        public static int ComputeHash32(ReadOnlySpan<byte> data, ulong seed = DefaultSeed)
        {
            if (data.Length <= sizeof(uint))
            {
                return (int)((uint)data._RawReadValueUInt32() ^ (uint)seed);
            }

            long hash64 = ComputeHash(data, seed);
            return ((int)(hash64 >> 32)) ^ (int)hash64;
        }

        static long ComputeHash(ReadOnlySpan<byte> data, ulong seed)
        {
            uint p0 = (uint)seed;
            uint p1 = (uint)(seed >> 32);

            if (data.Length >= sizeof(uint))
            {
                ReadOnlySpan<uint> uData = MemoryMarshal.Cast<byte, uint>(data);

                for (int i = 0; i < uData.Length; i++)
                {
                    p0 += uData[i];
                    Block(ref p0, ref p1);
                }

                int byteOffset = data.Length & (~3);
                data = data.Slice(byteOffset);
            }

            switch (data.Length)
            {
                case 0:
                    p0 += 0x80u;
                    break;

                case 1:
                    p0 += 0x8000u | data[0];
                    break;

                case 2:
                    p0 += 0x800000u | MemoryMarshal.Cast<byte, ushort>(data)[0];
                    break;

                case 3:
                    p0 += 0x80000000u | (((uint)data[2]) << 16) | (uint)(MemoryMarshal.Cast<byte, ushort>(data)[0]);
                    break;
            }

            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (((long)p1) << 32) | p0;
        }

        public static long ComputeHash(ReadOnlySpan<byte> data) => ComputeHash(data, DefaultSeed);

        [MethodImpl(Inline)]
        private static void Block(ref uint rp0, ref uint rp1)
        {
            uint p0 = rp0;
            uint p1 = rp1;

            p1 ^= p0;
            p0 = _rotl(p0, 20);

            p0 += p1;
            p1 = _rotl(p1, 9);

            p1 ^= p0;
            p0 = _rotl(p0, 27);

            p0 += p1;
            p1 = _rotl(p1, 19);

            rp0 = p0;
            rp1 = p1;
        }

        [MethodImpl(Inline)]
        private static uint _rotl(uint value, int shift)
        {
            return (value << shift) | (value >> (32 - shift));
        }

        public const ulong DefaultSeed = 0;

        private static ulong GenerateSeed()
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[sizeof(ulong)];
                rng.GetBytes(bytes);
                return BitConverter.ToUInt64(bytes, 0);
            }
        }

        #region AutoGenerated

        [MethodImpl(Inline)] public static int Xor(int a) => (a);
        [MethodImpl(Inline)] public static int Xor(int a, int b) => (a ^ b);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c) => (a ^ b ^ c);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d) => (a ^ b ^ c ^ d);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e) => (a ^ b ^ c ^ d ^ e);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f) => (a ^ b ^ c ^ d ^ e ^ f);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g) => (a ^ b ^ c ^ d ^ e ^ f ^ g);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w, int x) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w ^ x);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w, int x, int y) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w ^ x ^ y);
        [MethodImpl(Inline)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w, int x, int y, int z) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w ^ x ^ y ^ z);

        #endregion

        public static int Xor(params int[] hashList)
        {
            int ret = 0;
            foreach (var i in hashList)
                ret ^= i;
            return ret;
        }

    }

    public class SparseChunk
    {
        public readonly ReadOnlyMemory<byte> Memory;
        public readonly bool IsSparse;
        public readonly long Offset;
        public readonly int Size;

        public SparseChunk(long offset, int size)
        {
            this.IsSparse = true;
            this.Size = size;
            this.Memory = default;
            this.Offset = offset;
        }

        public SparseChunk(long offset, ReadOnlyMemory<byte> memory)
        {
            this.IsSparse = false;
            this.Memory = memory;
            this.Size = memory.Length;
            this.Offset = offset;
        }

        public ReadOnlyMemory<byte> GetMemoryOrGenerateSparse()
        {
            if (this.IsSparse == false)
                return this.Memory;
            else
                return ZeroedMemory<byte>.GetZeroFilledMemory(this.Size);
        }
    }

    public class SparseChunk<T>
    {
        public readonly ReadOnlyMemory<T> Memory;
        public readonly bool IsSparse;
        public readonly long Offset;
        public readonly int Size;

        public SparseChunk(long offset, int size)
        {
            this.IsSparse = true;
            this.Size = size;
            this.Memory = default;
            this.Offset = offset;
        }

        public SparseChunk(long offset, ReadOnlyMemory<T> memory)
        {
            this.IsSparse = false;
            this.Memory = memory;
            this.Size = memory.Length;
            this.Offset = offset;
        }

        public ReadOnlyMemory<T> GetMemoryOrGenerateSparse()
        {
            if (this.IsSparse == false)
                return this.Memory;
            else
                return ZeroedMemory<T>.GetZeroFilledMemory(this.Size);
        }
    }

    public readonly struct DividedSegment
    {
        public readonly long AbsolutePosition;
        public readonly long RelativePosition;
        public readonly long InSegmentOffset;
        public readonly long Size;
        public readonly long SegmentIndex;

        public DividedSegment(long absolutePosition, long relativePosition, long inSegmentOffset, long size, long segmentIndex)
        {
            this.AbsolutePosition = absolutePosition;
            this.RelativePosition = relativePosition;
            this.InSegmentOffset = inSegmentOffset;
            this.Size = size;
            this.SegmentIndex = segmentIndex;
        }
    }

    public class DividedSegmentList
    {
        public readonly long SegmentSize;
        public readonly long Start;
        public readonly long Size;

        public readonly DividedSegment[] SegmentList;

        public DividedSegmentList(long start, long size, long segmentSize)
        {
            this.Start = start;
            this.Size = size;
            this.SegmentSize = segmentSize;
            this.SegmentList = CalcSegmentInfo(start, size, segmentSize);
        }

        DividedSegment[] CalcSegmentInfo(long start, long size, long segmentSize)
        {
            checked
            {
                if (start < 0) throw new ArgumentOutOfRangeException("start");
                if (size < 0) throw new ArgumentOutOfRangeException("size");
                if (segmentSize <= 0) throw new ArgumentOutOfRangeException("size");

                long end = start + size;

                if (end == start)
                    return new DividedSegment[0];

                long startIndex = start / segmentSize;
                long endIndex = (end - 1) / segmentSize;
                long numIndex = endIndex - startIndex + 1;
                Debug.Assert(numIndex >= 1);

                DividedSegment[] ret = new DividedSegment[numIndex];

                for (long i = startIndex; i <= endIndex; i++)
                {
                    long startOfThisSegment = i * segmentSize;
                    long endOfThisSegment = (i + 1) * segmentSize;

                    long absoluteStart = Math.Max(startOfThisSegment, start);
                    long absoluteEnd = Math.Min(endOfThisSegment, end);
                    long sizeInThisSegment = absoluteEnd - absoluteStart;

                    Debug.Assert(sizeInThisSegment >= 1);
                    Debug.Assert(sizeInThisSegment <= segmentSize);

                    ret[i - startIndex] = new DividedSegment(absoluteStart, absoluteStart - start, absoluteStart - startOfThisSegment, sizeInThisSegment, i);
                }

                return ret;
            }
        }
    }

    public class XmlAndXsd
    {
        public byte[] XmlData;
        public byte[] XsdData;
        public string XmlFileName;
        public string XsdFileName;

        public XmlAndXsd(string xmlFileName, byte[] xmlData, string xsdFileName, byte[] xsdData)
        {
            XmlFileName = xmlFileName;
            XmlData = xmlData;
            XsdFileName = xsdFileName;
            XsdData = xsdData;
        }
    }

    // 1 人しか入れないリージョン (高速な非ブロッキング Mutex のようなもの)
    public class SingleEntryDoor
    {
        volatile private int flag;

        public IHolder Enter(Exception? ex = null)
        {
            if (Interlocked.CompareExchange(ref this.flag, 1, 0) != 0)
            {
                throw ex ?? new CoresException("Only single process can be invoked.");
            }

            return new ValueHolder(() =>
            {
                int r = Interlocked.CompareExchange(ref this.flag, 0, 1);

                if (r != 1)
                {
                    throw new CoresException($"Bad state: r = {r}");
                }
            },
            LeakCounterKind.SingleEntry);
        }
    }

    // 1 度しか実行しない処理を実行しやすくするための構造体
    public struct Once
    {
        volatile private int flag;
        public void Set() => IsFirstCall();

        [MethodImpl(Inline)]
        public bool IsFirstCall() => (Interlocked.CompareExchange(ref this.flag, 1, 0) == 0);

        public bool IsSet
        {
            [MethodImpl(Inline)]
            get => (this.flag != 0);
        }

        [MethodImpl(Inline)]
        public static implicit operator bool(Once once) => once.flag != 0;
        public void Reset() => this.flag = 0;

        public override string ToString() => IsSet.ToString();

        public void FirstCallOrThrowException(Exception? ex = null)
        {
            if (IsFirstCall() == false)
            {
                if (ex == null) ex = new ApplicationException("The flag is already set.");

                throw ex;
            }
        }
    }


    public class CoresRetryableException : CoresException
    {
        public CoresRetryableException(string? message) : base(message)
        {
        }
    }

    // 再試行ヘルパー
    public class RetryHelper<T>
    {
        public int DefaultRetryInterval { get; set; }
        public int DefaultTryCount { get; set; }
        public bool RandomInterval { get; set; }
        public bool NoDebugMessage { get; set; }
        public bool OnlyRetryableException { get; set; }

        public RetryHelper(int defaultRetryInterval, int defaultTryCount, bool randomInterval = false, bool noDebugMessage = false, bool onlyRetryableException = false)
        {
            this.DefaultRetryInterval = defaultRetryInterval;
            this.DefaultTryCount = defaultTryCount;
            this.RandomInterval = randomInterval;
            this.NoDebugMessage = noDebugMessage;
            this.OnlyRetryableException = onlyRetryableException;
        }

        public async Task<T> RunAsync(Func<CancellationToken, Task<T>> proc, int? retryInterval = null, int? tryCount = null, CancellationToken cancel = default)
        {
            if (retryInterval == null) retryInterval = DefaultRetryInterval;
            if (tryCount == null) tryCount = DefaultTryCount;
            tryCount = Math.Max((int)tryCount, 1);
            retryInterval = Math.Max((int)retryInterval, 0);

            Exception? firstException = null;

            for (int i = 0; i < tryCount; i++)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    if (NoDebugMessage == false && tryCount >= 2)
                    {
                        if (i >= 1) Dbg.WriteLine("Retrying...");
                    }
                    T ret = await proc(cancel);
                    return ret;
                }
                catch (Exception ex) when (this.OnlyRetryableException == false || ex is CoresRetryableException)
                {
                    if (firstException == null)
                    {
                        firstException = ex;
                    }

                    cancel.ThrowIfCancellationRequested();

                    if (i < (tryCount - 1))
                    {
                        int interval = (int)retryInterval;

                        if (this.RandomInterval)
                        {
                            interval = Util.GenRandInterval(interval);
                        }

                        if (NoDebugMessage == false && tryCount >= 2)
                        {
                            Dbg.WriteLine($"RetryHelper: round {i} error. {ex._GetSingleException().Message} Retrying in {interval} msecs...");
                        }

                        await Task.Delay(interval);
                    }
                    else
                    {
                        if (NoDebugMessage == false && tryCount >= 2)
                        {
                            Dbg.WriteLine($"RetryHelper: round {i} error. {ex._GetSingleException().Message}");
                        }
                    }
                }
            }

            throw firstException!;
        }

        public Task<T> RunAsync(Func<Task<T>> proc, int? retryInterval = null, int? tryCount = null, CancellationToken cancel = default)
            => RunAsync((c) => proc(), retryInterval, tryCount, cancel);
    }

    public static class RetryHelper
    {
        public static async Task RunAsync(Func<Task> proc, int retryInterval = 0, int tryCount = 1, CancellationToken cancel = default, bool randomInterval = false, bool noDebugMessage = false)
        {
            RetryHelper<int> helper = new RetryHelper<int>(retryInterval, tryCount, randomInterval, noDebugMessage);

            await helper.RunAsync(async () =>
            {
                await proc();
                return 0;
            }, cancel: cancel);
        }

        public static async Task<T> RunAsync<T>(Func<Task<T>> proc, int retryInterval = 0, int tryCount = 1, CancellationToken cancel = default, bool randomInterval = false, bool noDebugMessage = false)
        {
            RetryHelper<T> helper = new RetryHelper<T>(retryInterval, tryCount, randomInterval, noDebugMessage);

            return await helper.RunAsync(proc, cancel: cancel);
        }
    }

    public class StaticModule : StaticModule<int, int>
    {
        public StaticModule(Action initProc, Action freeProc)
            : base(_ => initProc(), () => { freeProc(); return default; })
        {
        }

        public void Init() => base.Init(0);
    }

    public class StaticModule<TResult> : StaticModule<int, TResult>
    {
        public StaticModule(Action initProc, Func<TResult> freeProc)
            : base(_ => initProc(), freeProc)
        {
        }

        public void Init() => base.Init(0);
    }

    public class StaticModule<TOptions, TResult>
    {
        readonly Action<TOptions> InitProc;
        readonly Func<TResult> FreeProc;
        readonly CriticalSection LockObj = new CriticalSection<StaticModule<TOptions, TResult>>();

        readonly List<Action> ActionListAfterInit = new List<Action>();

        public bool Initialized { get; private set; } = false;
        public bool Freeing { get; private set; } = false;

        public StaticModule(Action<TOptions> initProc, Func<TResult> freeProc)
        {
            this.InitProc = initProc;
            this.FreeProc = freeProc;
        }

        public void Init(TOptions options)
        {
            lock (LockObj)
            {
                if (Initialized)
                    throw new ApplicationException("The StaticModule object is already initialized.");

                this.InitProc(options);

                Initialized = true;

                foreach (Action action in ActionListAfterInit)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }

                ActionListAfterInit.Clear();
            }
        }

        public void AddAfterInitAction(Action action)
        {
            if (action != null)
                ActionListAfterInit.Add(action);
        }

        public TResult Free()
        {
            lock (LockObj)
            {
                Freeing = true;

                ActionListAfterInit.Clear();

                Interlocked.MemoryBarrierProcessWide();

                try
                {
                    Interlocked.MemoryBarrierProcessWide();
                    if (Initialized == false)
                        throw new ApplicationException("The StaticModule object is not initialized.");

                    TResult ret = this.FreeProc();

                    Interlocked.MemoryBarrierProcessWide();

                    Initialized = false;

                    Interlocked.MemoryBarrierProcessWide();

                    return ret;
                }
                finally
                {
                    Freeing = false;
                }
            }
        }

        public void CheckInitalized()
        {
            if (this.Initialized == false || Freeing)
                throw new ApplicationException("The StaticModule object is not initialized.");
        }
    }

    public class SingletonFastArraySlim<TKey, TObject>
        where TObject : class
        where TKey : unmanaged, Enum
    {
        public const int MaxElements = 8_000_000; // Max 64Mbytes

        readonly Func<TKey, TObject> CreateProc;
        TObject[] ObjectList;
        public int Count { get; private set; }

        int MaxValue;

        public SingletonFastArraySlim(Func<TKey, TObject> createProc)
            : this(createProc, Util.GetMaxEnumValue<TKey>()) { }

        public SingletonFastArraySlim(Func<TKey, TObject> createProc, TKey maxValue)
        {
            this.CreateProc = createProc;

            ulong tmp = maxValue._RawReadValueUInt64();

            if (tmp >= MaxElements)
                throw new ArgumentException($"The maxValue is {tmp} while the allowed max elements is {MaxElements}.");

            this.MaxValue = (int)tmp;

            this.ObjectList = new TObject[this.MaxValue + 1];

            this.Count = 0;
        }

        public TObject this[TKey key] => CreateOrGet(key);

        [MethodImpl(Inline)]
        public TObject CreateOrGet(TKey key)
        {
            ulong value = key._RawReadValueUInt64();
            if (value > (ulong)MaxValue)
                throw new ArgumentException($"The key value is {value} while the allowed max value is {MaxValue}.");
            int valueInt = (int)value;

            if (this.ObjectList[valueInt] != null)
            {
                return this.ObjectList[valueInt];
            }
            else
            {
                this.ObjectList[valueInt] = this.CreateProc(key);
                this.Count++;
            }
            return this.ObjectList[valueInt];
        }

        public void Clear()
        {
            Array.Clear(this.ObjectList, 0, this.ObjectList.Length);

            this.Count = 0;
        }
    }


    public class SingletonFastArray<TKey, TObject> : IDisposable
        where TObject : class
        where TKey : unmanaged, Enum
    {
        public const int MaxElements = 8_000_000; // Max 64Mbytes

        readonly CriticalSection LockObj = new CriticalSection<SingletonFastArray<TKey, TObject>>();
        readonly Func<TKey, TObject> CreateProc;
        TObject[] ObjectList;
        public int Count { get; private set; }

        int MaxValue;

        public SingletonFastArray(Func<TKey, TObject> createProc)
            : this(createProc, Util.GetMaxEnumValue<TKey>()) { }

        public SingletonFastArray(Func<TKey, TObject> createProc, TKey maxValue)
        {
            this.CreateProc = createProc;

            ulong tmp = maxValue._RawReadValueUInt64();

            if (tmp >= MaxElements)
                throw new ArgumentException($"The maxValue is {tmp} while the allowed max elements is {MaxElements}.");

            this.MaxValue = (int)tmp;

            this.ObjectList = new TObject[this.MaxValue + 1];

            this.Count = 0;
        }

        public TObject this[TKey key] => CreateOrGet(key);

        [MethodImpl(Inline)]
        public TObject CreateOrGet(TKey key)
        {
            ulong value = key._RawReadValueUInt64();
            if (value > (ulong)MaxValue)
                throw new ArgumentException($"The key value is {value} while the allowed max value is {MaxValue}.");
            int valueInt = (int)value;

            if (DisposeFlag) throw new ObjectDisposedException("SingletonFastArray");

            if (this.ObjectList[valueInt] != null) return this.ObjectList[valueInt];

            lock (LockObj)
            {
                if (DisposeFlag) throw new ObjectDisposedException("SingletonFastArray");

                if (this.ObjectList[valueInt] == null)
                {
                    this.ObjectList[valueInt] = this.CreateProc(key);
                    this.Count++;
                }
                return this.ObjectList[valueInt];
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Clear();
        }

        public void Clear()
        {
            List<IDisposable> disposalList = new List<IDisposable>();

            lock (LockObj)
            {
                for (int i = 0; i < this.ObjectList.Length; i++)
                    if (this.ObjectList[i] != null)
                        if (this.ObjectList[i] is IDisposable disposable)
                            disposalList.Add(disposable);

                Array.Clear(this.ObjectList, 0, this.ObjectList.Length);

                this.Count = 0;
            }

            foreach (var disposable in disposalList)
                disposable._DisposeSafe();
        }
    }

    public class SingletonSlim<TObject> where TObject : class
    {
        readonly Func<TObject> CreateProc;
        TObject? Object = null;
        public bool IsCreated { get; private set; }

        public SingletonSlim(Func<TObject> createProc)
        {
            this.CreateProc = createProc;
        }

        public static implicit operator TObject(SingletonSlim<TObject> singleton) => singleton.CreateOrGet();

        [MethodImpl(Inline)]
        public TObject CreateOrGet()
        {
            if (this.Object != null)
            {
                return this.Object;
            }
            else
            {
                this.Object = this.CreateProc();
                this.IsCreated = true;
                return this.Object;
            }
        }

        public void Clear()
        {
            this.Object = null;
            IsCreated = false;
        }
    }

    public class Singleton<TObject> : IDisposable, IAsyncDisposable
        where TObject : class
    {
        readonly CriticalSection LockObj = new CriticalSection<Singleton<TObject>>();
        readonly Func<TObject> CreateProc;
        TObject? Object = null;
        public bool IsCreated { get; private set; }

        public Singleton(Func<TObject> createProc)
        {
            this.CreateProc = createProc;
        }

        public static implicit operator TObject(Singleton<TObject> singleton) => singleton.CreateOrGet();

        [MethodImpl(Inline)]
        public TObject CreateOrGet()
        {
            if (DisposeFlag) throw new ObjectDisposedException("Singleton");

            if (this.Object != null) return this.Object;

            lock (LockObj)
            {
                if (DisposeFlag) throw new ObjectDisposedException("Singleton");

                if (this.Object == null)
                {
                    this.Object = this.CreateProc();
                    this.IsCreated = true;
                }
                return this.Object;
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        public virtual async ValueTask DisposeAsync()
        {
            if (DisposeFlag.IsFirstCall() == false) return;
            await DisposeInternalAsync();
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            DisposeInternalAsync()._GetResult();
        }
        async Task DisposeInternalAsync()
        {
            await ClearAsync()._TryAwait(true);
        }

        public async Task ClearAsync()
        {
            TObject? obj = null;
            lock (LockObj)
            {
                obj = this.Object;
                this.Object = null;
            }

            if (obj != null)
            {
                if (obj is IAsyncDisposable asyncDisposableTarget)
                {
                    await asyncDisposableTarget._DisposeSafeAsync();
                }
                else if (obj is IDisposable disposeTarget)
                {
                    disposeTarget._DisposeSafe();
                }
            }

            IsCreated = false;
        }
    }

    public class Singleton<TKey, TObject> : IDisposable where TObject : class
        where TKey : notnull
    {
        readonly CriticalSection LockObj = new CriticalSection<Singleton<TKey, TObject>>();
        readonly Func<TKey, TObject> CreateProc;
        readonly Dictionary<TKey, TObject> Table;
        public IEnumerable<TKey> Keys => this.Table.Keys;

        public Singleton(Func<TKey, TObject> createProc, IEqualityComparer<TKey>? keyComparer = null)
        {
            this.CreateProc = createProc;

            if (keyComparer == null)
                this.Table = new Dictionary<TKey, TObject>();
            else
                this.Table = new Dictionary<TKey, TObject>(keyComparer);
        }

        public TObject this[TKey key] => CreateOrGet(key);

        public TObject CreateOrGet(TKey key)
        {
            if (DisposeFlag) throw new ObjectDisposedException("Singleton");

            lock (LockObj)
            {
                if (DisposeFlag) throw new ObjectDisposedException("Singleton");

                if (this.Table.TryGetValue(key, out TObject? obj) == false)
                {
                    obj = this.CreateProc(key);
                    this.Table.Add(key, obj);
                }
                return obj!;
            }
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Clear();
        }

        public void Clear()
        {
            TObject[] list;

            lock (LockObj)
            {
                list = this.Table.Values.ToArray();
                this.Table.Clear();
            }

            foreach (var obj in list)
            {
                if (obj is IDisposable disposeTarget)
                    disposeTarget._DisposeSafe();
            }
        }
    }

    public class SingletonSlim<TKey, TObject> where TObject : class where TKey : notnull
    {
        readonly Func<TKey, TObject> CreateProc;
        readonly Dictionary<TKey, TObject> Table;
        public IEnumerable<TKey> Keys => this.Table.Keys;

        public SingletonSlim(Func<TKey, TObject> createProc, IEqualityComparer<TKey>? keyComparer = null)
        {
            this.CreateProc = createProc;

            if (keyComparer == null)
                this.Table = new Dictionary<TKey, TObject>();
            else
                this.Table = new Dictionary<TKey, TObject>(keyComparer);
        }

        public TObject this[TKey key] => CreateOrGet(key);

        public TObject CreateOrGet(TKey key)
        {
            if (this.Table.TryGetValue(key, out TObject? obj) == false)
            {
                obj = this.CreateProc(key);
                this.Table.Add(key, obj);
            }
            return obj!;
        }

        public void Clear()
        {
            TObject[] list;

            list = this.Table.Values.ToArray();
            this.Table.Clear();

            foreach (var obj in list)
            {
                if (obj is IDisposable disposeTarget)
                    disposeTarget._DisposeSafe();
            }
        }
    }

    [Flags]
    public enum CacheFlags
    {
        None = 0,
        IgnoreUpdateError = 1,
        NoGc = 2,
    }


    public class SyncCache<TData> : SyncCache<int, TData> where TData : class
    {
        public SyncCache(int lifeTime) : base(lifeTime) { }

        public SyncCache(int lifeTime, CacheFlags flags, Func<TData?> getProc) : base(lifeTime, flags, x => getProc())
        {
        }

        public SyncCache(int lifeTime, CacheFlags flags, Func<CancellationToken, TData?> getProc) : base(lifeTime, flags, (x, c) => getProc(c))
        {
        }

        public TData? Get(CancellationToken cancel = default)
            => base.Get(0, cancel);

        public void Set(TData? data)
            => base.Set(0, data);

        public static implicit operator TData?(SyncCache<TData> cache) => cache.Get();
    }

    public class SyncCache<TKey, TData> where TData : class where TKey : notnull
    {
        class Entry
        {
            public long Expires;
            public TData? Data;
        }

        public CacheFlags Flags { get; }
        public long LifeTime { get; }
        public Func<TKey, CancellationToken, TData?>? GetProc { get; }

        long NextGcTime;

        readonly Dictionary<TKey, Entry> Table = new Dictionary<TKey, Entry>();

        readonly CriticalSection SyncLock = new CriticalSection<SyncCache<TKey, TData>>();
        readonly CriticalSection Lock = new CriticalSection<SyncCache<TKey, TData>>();

        public SyncCache(int lifeTime)
            : this(lifeTime, CacheFlags.None, (Func<TKey, CancellationToken, TData?>)null!) { }

        public SyncCache(int lifeTime, CacheFlags flags, Func<TKey, TData?> getProc)
            : this(lifeTime, flags, (key, cancel) => getProc(key)) { }

        public SyncCache(int lifeTime, CacheFlags flags, Func<TKey, CancellationToken, TData?>? getProc)
        {
            this.LifeTime = Math.Max(0, lifeTime);
            if (lifeTime < 0 || lifeTime == int.MaxValue)
            {
                this.LifeTime = long.MaxValue;
            }
            this.Flags = flags;
            this.GetProc = getProc;
        }

        public TData? this[TKey key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        void Gc(long now)
        {
            if (this.Flags.Bit(CacheFlags.NoGc)) return;

            if (now > this.NextGcTime && this.LifeTime != long.MaxValue)
            {
                // GC
                this.NextGcTime = now + this.LifeTime;

                List<TKey> deleteList = new List<TKey>();

                foreach (var kv in this.Table)
                {
                    if (now > kv.Value.Expires)
                    {
                        deleteList.Add(kv.Key);
                    }
                }

                foreach (TKey keyToDelete in deleteList)
                {
                    this.Table.Remove(keyToDelete);
                }
            }
        }

        public TData? Get(TKey key, CancellationToken cancel = default)
        {
            long now = Tick64.Now;

            lock (Lock)
            {
                Gc(now);

                if (Table.TryGetValue(key, out Entry? entry) && now <= entry.Expires)
                {
                    return entry.Data;
                }
            }

            TData? data = null;

            if (this.GetProc == null) return null;

            try
            {
                data = this.GetProc(key, cancel);
            }
            catch
            {
                if (this.Flags.Bit(CacheFlags.IgnoreUpdateError) == false)
                    throw;

                if (Table.TryGetValue(key, out Entry? entry) == false)
                    throw;

                data = entry!.Data;
            }

            now = Tick64.Now;

            lock (Lock)
            {
                Table[key] = new Entry
                {
                    Data = data,
                    Expires = (this.LifeTime == long.MaxValue) ? long.MaxValue : now + this.LifeTime,
                };
            }
            ;

            return data;
        }

        public void Set(TKey key, TData? data)
        {
            long now = Tick64.Now;

            lock (Lock)
            {
                Gc(now);

                this.Table[key] = new Entry
                {
                    Data = data,
                    Expires = (this.LifeTime == long.MaxValue) ? long.MaxValue : now + this.LifeTime,
                };
            }
        }
    }


    public class AsyncCache<TData> : AsyncCache<int, TData> where TData : class
    {
        public AsyncCache(int lifeTime) : base(lifeTime) { }

        public AsyncCache(int lifeTime, CacheFlags flags, Func<Task<TData?>> getProcAsync) : base(lifeTime, flags, x => getProcAsync())
        {
        }

        public AsyncCache(int lifeTime, CacheFlags flags, Func<CancellationToken, Task<TData?>> getProcAsync) : base(lifeTime, flags, (x, c) => getProcAsync(c))
        {
        }

        public Task<TData?> GetAsync(CancellationToken cancel = default)
            => base.GetAsync(0, cancel);

        public void Set(TData? data)
            => base.Set(0, data);

        public static implicit operator TData?(AsyncCache<TData> cache) => cache.GetAsync()._GetResult();
    }

    public class AsyncCache<TKey, TData> where TData : class where TKey : notnull
    {
        class Entry
        {
            public long Expires;
            public TData? Data;
        }

        public CacheFlags Flags { get; }
        public long LifeTime { get; }
        public Func<TKey, CancellationToken, Task<TData?>>? GetProcAsync { get; }

        long NextGcTime;

        readonly Dictionary<TKey, Entry> Table = new Dictionary<TKey, Entry>();

        readonly AsyncLock AsyncLock = new AsyncLock();
        readonly CriticalSection Lock = new CriticalSection<AsyncCache<TKey, TData>>();

        public AsyncCache(int lifeTime)
            : this(lifeTime, CacheFlags.None, (Func<TKey, CancellationToken, Task<TData?>>)null!) { }

        public AsyncCache(int lifeTime, CacheFlags flags, Func<TKey, Task<TData?>> getProcAsync)
            : this(lifeTime, flags, (key, cancel) => getProcAsync(key)) { }

        public AsyncCache(int lifeTime, CacheFlags flags, Func<TKey, CancellationToken, Task<TData?>>? getProcAsync)
        {
            this.LifeTime = Math.Max(0, lifeTime);
            if (lifeTime < 0 || lifeTime == int.MaxValue)
            {
                this.LifeTime = long.MaxValue;
            }
            this.Flags = flags;
            this.GetProcAsync = getProcAsync;
        }

        public TData? this[TKey key]
        {
            get => GetAsync(key)._GetResult();
            set => Set(key, value);
        }

        void Gc(long now)
        {
            if (this.Flags.Bit(CacheFlags.NoGc)) return;

            if (now > this.NextGcTime && this.LifeTime != long.MaxValue)
            {
                // GC
                this.NextGcTime = now + this.LifeTime;

                List<TKey> deleteList = new List<TKey>();

                foreach (var kv in this.Table)
                {
                    if (now > kv.Value.Expires)
                    {
                        deleteList.Add(kv.Key);
                    }
                }

                foreach (TKey keyToDelete in deleteList)
                {
                    this.Table.Remove(keyToDelete);
                }
            }
        }

        public async Task<TData?> GetAsync(TKey key, CancellationToken cancel = default)
        {
            long now = Tick64.Now;

            lock (Lock)
            {
                Gc(now);

                if (Table.TryGetValue(key, out Entry? entry) && now <= entry.Expires)
                {
                    return entry.Data;
                }
            }

            TData? data = null;

            if (this.GetProcAsync == null) return default;

            try
            {
                data = await this.GetProcAsync(key, cancel);
            }
            catch
            {
                if (this.Flags.Bit(CacheFlags.IgnoreUpdateError) == false)
                    throw;

                if (Table.TryGetValue(key, out Entry? entry) == false)
                {
                    throw;
                }

                data = entry!.Data;
            }

            now = Tick64.Now;

            lock (Lock)
            {
                Table[key] = new Entry
                {
                    Data = data,
                    Expires = (this.LifeTime == long.MaxValue) ? long.MaxValue : now + this.LifeTime,
                };
            }
            ;

            return data;
        }

        public void Set(TKey key, TData? data)
        {
            long now = Tick64.Now;

            lock (Lock)
            {
                Gc(now);

                this.Table[key] = new Entry
                {
                    Data = data,
                    Expires = (this.LifeTime == long.MaxValue) ? long.MaxValue : now + this.LifeTime,
                };
            }
        }
    }

    namespace Legacy
    {
        // シングルトン
        public struct OldSingleton<T> where T : class
        {
            static object lockobj = new object();
            T obj;

            public T CreateOrGet(Func<T> createProc)
            {
                lock (lockobj)
                {
                    if (obj == null)
                        obj = createProc();
                    return obj;
                }
            }
        }
    }

    public static class GlobalObjectExchange
    {
        static Dictionary<string, object?> table = new Dictionary<string, object?>();

        public static bool TryWithdraw(string? token, out object? ret)
        {
            try
            {
                if (token._IsEmpty())
                {
                    ret = null;
                    return false;
                }

                lock (table)
                {
                    if (table.TryGetValue(token!, out ret) == false)
                    {
                        return false;
                    }

                    table.Remove(token!);

                    return true;
                }
            }
            catch
            {
                ret = null;
                return false;
            }
        }

        public static object? Withdraw(string? token)
        {
            if (TryWithdraw(token, out object? ret) == false)
            {
                throw new ApplicationException("invalid token");
            }
            return ret;
        }

        public static string Deposit(object? o)
        {
            string id = Str.NewGuid();
            lock (table) table.Add(id, o);
            return id;
        }
    }

    public class IntervalManager
    {
        long LastTick = 0;
        public int Interval { get; private set; }
        int LastInterval;

        public IntervalManager(int interval)
        {
            LastTick = Time.Tick64;
            this.LastInterval = this.Interval = interval;
        }

        public int GetNextInterval(int? nextInterval = null)
            => GetNextInterval(out _, nextInterval);

        int Count = 0;

        public int GetNextInterval(out int lastTimediff, int? nextInterval = null)
        {
            long now = Time.Tick64;
            lastTimediff = (int)(now - LastTick);
            int over = lastTimediff - this.LastInterval;
            if (nextInterval != null) this.Interval = (int)nextInterval;
            this.LastInterval = this.Interval;
            int ret = this.Interval;
            if (over > 0)
            {
                ret = this.Interval - over;
            }
            LastTick = now;
            if (ret <= 0) ret = 1;
            if (this.Interval == Timeout.Infinite) ret = Timeout.Infinite;
            if (lastTimediff <= 0) lastTimediff = 1;
            if (Count == 0) lastTimediff = this.LastInterval;
            Count++;
            return ret;
        }
    }

    public class Distinct<T>
        where T : notnull
    {
        Dictionary<T, T> d = new Dictionary<T, T>();

        public T AddOrGet(T obj)
        {
            if (d.ContainsKey(obj))
            {
                return d[obj];
            }
            else
            {
                d.Add(obj, obj);
                return obj;
            }
        }

        public T[] Values { get => d.Keys._ToArrayList(); }
    }

    namespace Legacy
    {
        public class DelayLoader<T> : IDisposable
            where T : class
        {
            Func<long, (T, long)> LoadProc;
            int RetryInterval;
            int UpdateInterval;
            ThreadObj Thread;
            ManualResetEventSlim HaltEvent = new ManualResetEventSlim();
            public readonly ManualResetEventSlim LoadCompleteEvent = new ManualResetEventSlim();
            bool HaltFlag = false;

            public T? Data { get; private set; }

            public DelayLoader(Func<long, (T data, long dataTimeStamp)> loadProc, int retryInterval = 1000, int updateInterval = 1000)
            {
                this.LoadProc = loadProc;
                this.RetryInterval = retryInterval;
                this.UpdateInterval = updateInterval;

                this.Thread = new ThreadObj(ThreadProc, isBackground: true);
            }

            void ThreadProc(object? param)
            {
                long lastTimeStamp = 0;

                (T data, long dataTimeStamp) ret;

                LABEL_RETRY:
                try
                {
                    // データの読み込み
                    ret = LoadProc(lastTimeStamp);
                }
                catch (Exception ex)
                {
                    Dbg.WriteLine(ex.ToString());

                    HaltEvent.Wait(RetryInterval);

                    if (HaltFlag)
                    {
                        return;
                    }

                    goto LABEL_RETRY;
                }

                if (ret.data != null)
                {
                    // 読み込んだデータをグローバルにセット
                    lastTimeStamp = ret.dataTimeStamp;
                    this.Data = ret.data;
                    LoadCompleteEvent.Set();
                }

                // 次回まで待機
                HaltEvent.Wait(UpdateInterval);
                if (HaltFlag)
                {
                    return;
                }

                goto LABEL_RETRY;
            }

            Once DisposeFlag;
            public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;

                HaltFlag = true;
                HaltEvent.Set();
                Thread.WaitForEnd();
            }
        }
    }

    public class CachedProperty<T>
    {
        object LockObj = new object();
        volatile bool IsCached = false;
        T CachedValue = default!;

        readonly Func<T>? Getter;
        readonly Func<T, T>? Setter;
        readonly Func<T, T>? Normalizer;

        public long ExpiresInterval { get; }
        public bool HasLifeTime { get; }
        public long CurrentLifeTime { get; private set; }

        public CachedProperty(Func<T, T>? setter = null, Func<T>? getter = null, Func<T, T>? normalizer = null, int expiresLifeTimeMsecs = Timeout.Infinite)
        {
            if (expiresLifeTimeMsecs <= 0) expiresLifeTimeMsecs = Timeout.Infinite;

            Setter = setter;
            Getter = getter;
            Normalizer = normalizer;

            if (expiresLifeTimeMsecs != Timeout.Infinite)
            {
                HasLifeTime = true;
                this.ExpiresInterval = expiresLifeTimeMsecs;
            }
        }

        public bool TrySet(T value)
        {
            try
            {
                Set(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Set(T value)
        {
            if (Setter == null) throw new NotImplementedException();
            if (Normalizer != null)
                value = Normalizer(value);

            lock (LockObj)
            {
                value = Setter(value);
                CachedValue = value;
                IsCached = true;

                if (this.HasLifeTime)
                {
                    this.CurrentLifeTime = Time.Tick64 + this.ExpiresInterval;
                }
            }
        }

        public T Get()
        {
            if (IsCached && (this.HasLifeTime == false || Time.Tick64 <= this.CurrentLifeTime))
                return CachedValue;

            lock (LockObj)
            {
                if (IsCached && (this.HasLifeTime == false || Time.Tick64 <= this.CurrentLifeTime))
                    return CachedValue;

                if (Getter == null) throw new NotImplementedException("The value is undefined yet.");

                T value = Getter();

                if (Normalizer != null)
                    value = Normalizer(value);

                CachedValue = value;
                IsCached = true;

                if (this.HasLifeTime)
                {
                    this.CurrentLifeTime = Time.Tick64 + this.ExpiresInterval;
                }

                return value;
            }
        }

        public void Flush()
        {
            lock (LockObj)
            {
                IsCached = false;
                CachedValue = default!;

                if (this.HasLifeTime)
                {
                    this.CurrentLifeTime = 0;
                }
            }
        }

        public T Value { get => Get(); set => Set(value); }

        public static implicit operator T(CachedProperty<T> r) => r.Value;
    }

    public static class Limbo
    {
        public static bool Bool = false;
        public static long SInt64 = 0;
        public static ulong UInt64 = 0;
        public static int SInt32 = 0;
        public static uint UInt32 = 0;
        public static double Double = 0.0;
        public static double Double2 = 0.0;
        public static double Double3 = 0.0;
        public static float Float = 0.0f;
        public static float Float2 = 0.0f;
        public static float Float3 = 0.0f;
        public static object? ObjectSlow = null;

        public volatile static bool BoolVolatile = false;
        public volatile static int SInt32Volatile = 0;
        public volatile static uint UInt32Volatile = 0;
        public volatile static object? ObjectVolatileSlow = null;

        public static readonly object EmptyObject = new object();

        public static readonly ConcurrentBag<object?> Bag = new ConcurrentBag<object?>();
    }

    public static class GlobalMicroBenchmark
    {
        const int DefaultOfDefaultDurationMSecs = 250;
        const LogSwitchType SwitchType = LogSwitchType.Month;

        public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

        static void InitModule()
        {
            DefaultDurationMSecs = DefaultOfDefaultDurationMSecs;
        }

        static void FreeModule()
        {
            if (LogRouter != null)
            {
                LogRouter._DisposeSafe();
                LogRouter = null;

                OnceFlag.Reset();
            }
        }

        public static int DefaultDurationMSecs { get; private set; }
        static LogRouter? LogRouter = null;
        static Once OnceFlag;

        public static void SetParameters(bool recordLog, int? defaultDurationMsecs = null)
        {
            if (OnceFlag.IsFirstCall() == false) return;

            if (defaultDurationMsecs.HasValue)
                DefaultDurationMSecs = defaultDurationMsecs.Value;

            if (recordLog)
            {
                var logRouter = new LogRouter();

                logRouter.InstallLogRoute(new LoggerLogRoute("data", LogPriority.Trace, "benchmark", Path.Combine(Env.AppRootDir, "BenchmarkRecords/Json"),
                    SwitchType,
                    new LogInfoOptions()
                    {
                        WithTypeName = false,
                        WriteAsJsonFormat = true,
                        WithTag = true
                    },
                    long.MaxValue));

                logRouter.InstallLogRoute(new LoggerLogRoute("text", LogPriority.Trace, "benchmark", Path.Combine(Env.AppRootDir, "BenchmarkRecords/Text"),
                    SwitchType,
                    new LogInfoOptions()
                    {
                        WithTypeName = false,
                        WriteAsJsonFormat = false,
                        WithTag = false
                    },
                    long.MaxValue));

                LogRouter = logRouter;
            }
        }

        public static void RecordStart()
        {
            WriteTextLog($"----- Start new benchmark -----");
            WriteTextLog($"Commit: {Dbg.GetCurrentGitCommitId()}");
            WriteTextLog($"ExeFileName: {Env.AppExecutableExeOrDllFileName}");
            WriteTextLog($"OS: {Env.OsInfoString}");
            WriteTextLog($"Framework: {Env.FrameworkInfoString}");
            WriteTextLog($"Host: {Env.MachineName}");
        }

        public static void WriteTextLog(string text)
        {
            LogRouter?.PostLog(new LogRecord(text, tag: "text"), "data");
            LogRouter?.PostLog(new LogRecord(text, tag: "text"), "text");
        }

        public static void WriteDataLog(object data, string text)
        {
            LogRouter?.PostLog(new LogRecord(data, tag: "result"), "data");
            LogRouter?.PostLog(new LogRecord(text, tag: "result"), "text");
        }
    }

    public class MicroBenchmarkQueue
    {
        List<(IMicroBenchmark bm, int priority, int index)> List = new List<(IMicroBenchmark bm, int priority, int index)>();

        public MicroBenchmarkQueue Add(IMicroBenchmark benchmark, bool enabled = true, int priority = 0)
        {
            if (enabled)
                List.Add((benchmark, priority, List.Count));
            return this;
        }

        public void Run()
        {
            foreach (var a in List.OrderBy(x => x.index).OrderBy(x => -x.priority).Select(x => x.bm))
                a.StartAndPrint();
        }
    }

    public interface IMicroBenchmark
    {
        double Start(int duration = 0);
        double StartAndPrint(int duration = 0);
    }

    public class MicroBenchmark : MicroBenchmark<int>
    {
        public MicroBenchmark(string name, int iterations, Action<int> proc, Func<int>? init = null)
            : base(name, iterations, (_state, _iterations) => proc(_iterations), init) { }
    }

    public class MicroBenchmark<TUserVariable> : IMicroBenchmark
    {
        public readonly string Name;
        volatile bool StopFlag;
        object LockObj = new object();
        public readonly int Iterations;

        public readonly Func<TUserVariable>? Init;
        public readonly Action<TUserVariable, int> Proc;

        readonly Action<TUserVariable, int> DummyLoopProc = (state, count) =>
        {
            for (int i = 0; i < count; i++) Limbo.SInt64++;
        };

        public MicroBenchmark(string name, int iterations, Action<TUserVariable, int> proc, Func<TUserVariable>? init = null)
        {
            Name = name;
            Init = init;
            Proc = proc;
            Iterations = Math.Max(iterations, 1);
        }

        public double StartAndPrint(int duration = 0)
        {
            double ret = Start(duration);

            double perSecond = 1000000000.0 / ret;
            if (ret == 0.0)
            {
                perSecond = 9_9999_9999_9999.0;
            }

            string str = $"{Name}: {ret.ToString("#,0.00")} ns, {((long)perSecond)._ToString3()} / sec";

            Con.WriteLine(str);

            GlobalMicroBenchmark.WriteDataLog(new { TestName = Name, Ret = ret, PerSecond = (long)perSecond }, str);

            return ret;
        }

        double MeasureInternal(int duration, TUserVariable state, Action<TUserVariable, int> proc, int interationsPassValue)
        {
            bool useThread = !CoresLib.Caps.Bit(CoresCaps.BlazorApp);
            StopFlag = false;

            ManualResetEventSlim ev = new ManualResetEventSlim();

            ThreadObj thread = null!;

            if (useThread)
            {
                thread = new ThreadObj((par) =>
                {
                    ev.Wait();
                    Thread.Sleep(duration);
                    StopFlag = true;
                }, isBackground: true, priority: ThreadPriority.Highest);
            }

            long count = 0;
            Stopwatch sw = new Stopwatch();
            ev.Set();
            sw.Start();
            TimeSpan ts1 = sw.Elapsed;
            while (StopFlag == false)
            {
                if (Init == null && interationsPassValue == 0)
                {
                    DummyLoopProc(state, Iterations);
                }
                else
                {
                    proc(state, interationsPassValue);
                    if (interationsPassValue == 0)
                    {
                        for (int i = 0; i < Iterations; i++) Limbo.SInt64++;
                    }
                }
                count += Iterations;

                if (useThread == false)
                {
                    if (sw.ElapsedMilliseconds >= duration)
                    {
                        StopFlag = true;
                    }
                }
            }
            TimeSpan ts2 = sw.Elapsed;
            TimeSpan ts = ts2 - ts1;

            if (useThread)
            {
                thread.WaitForEnd();
            }

            double nano = (double)ts.Ticks * 100.0;
            double nanoPerCall = nano / (double)count;

            if (nanoPerCall < 0.001)
                nanoPerCall = 0.0;

            Dbg.GcCollect();

            return nanoPerCall;
        }

        public double Start(int duration = 0)
        {
            lock (LockObj)
            {
                if (duration <= 0) duration = GlobalMicroBenchmark.DefaultDurationMSecs;

                TUserVariable state = default(TUserVariable)!;

                double v1 = 0;
                double v2 = 0;

                if (Init != null) state = Init();
                v2 = MeasureInternal(duration, state, Proc, 0);

                if (Init != null) state = Init();
                v1 = MeasureInternal(duration, state, Proc, Iterations);

                double v = Math.Max(v1 - v2, 0);
                //v -= EmptyBaseLine;
                v = Math.Max(v, 0);
                return v;
            }
        }
    }

    public struct EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public class EtaCalculator
    {
        class Data
        {
            public long Tick { get; }
            public double Percentage { get; }

            public Data(long tick, double percentage)
            {
                this.Tick = tick;
                this.Percentage = Math.Min(Math.Max(percentage, 0.0), 100.0);
            }
        }

        readonly long ShorterSample, LongerSample;

        public EtaCalculator(long shorterSample = 500, long longerSample = 5000)
        {
            shorterSample = Math.Max(shorterSample, 1);
            longerSample = Math.Max(longerSample, 1);

            longerSample = Math.Max(longerSample, shorterSample * 2);

            ShorterSample = shorterSample;
            LongerSample = longerSample;
        }

        readonly CriticalSection LockObj = new CriticalSection<EtaCalculator>();

        Data? ShorterData1 = null;
        Data? ShorterData2 = null;

        Data? LongerData1 = null;
        Data? LongerData2 = null;

        public long? Calculate(long tick, double? percentage)
        {
            try
            {
                if (percentage == null)
                    return null;

                if (percentage >= 100.0)
                    return 0;

                lock (LockObj)
                {
                    long? etaFromShorterSample = null;
                    long? etaFromLongerSample = null;
                    double remainPercentage = 100 - percentage ?? 0.0;

                    Data d = new Data(tick, percentage ?? 0.0);
                    long tickDelta;
                    double percentageDelta;

                    if (ShorterData1 == null)
                    {
                        ShorterData1 = d;
                        ShorterData2 = d;
                    }
                    else
                    {
                        if ((ShorterData1.Tick + ShorterSample) <= d.Tick)
                        {
                            //                        ShorterData2 = ShorterData1;
                            ShorterData1 = d;
                        }
                    }

                    tickDelta = ShorterData1.Tick - ShorterData2!.Tick;
                    if (tickDelta > 0)
                    {
                        percentageDelta = ShorterData1.Percentage - ShorterData2!.Percentage;
                        if (percentageDelta > 0)
                        {
                            etaFromShorterSample = (long)((double)tickDelta * remainPercentage / (double)percentageDelta);
                        }
                    }

                    if (LongerData1 == null)
                    {
                        LongerData1 = d;
                        LongerData2 = d;
                    }
                    else
                    {
                        if ((LongerData1.Tick + LongerSample) <= d.Tick)
                        {
                            LongerData2 = LongerData1;
                            LongerData1 = d;
                        }
                    }

                    tickDelta = LongerData1.Tick - LongerData2!.Tick;
                    if (tickDelta > 0)
                    {
                        percentageDelta = LongerData1.Percentage - LongerData2.Percentage;
                        if (percentageDelta > 0)
                        {
                            etaFromLongerSample = (long)((double)tickDelta * remainPercentage / (double)percentageDelta);
                        }
                    }

                    if (etaFromLongerSample != null) return (long)etaFromLongerSample;
                    if (etaFromShorterSample != null) return (long)etaFromShorterSample;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    [Flags]
    public enum ProgressReportType
    {
        Start,
        InProgress,
        Abort,
        Finish,
    }

    public class ProgressData
    {
        public long CurrentCount { get; }
        public long? TotalCount { get; }
        public bool IsFinish { get; }
        public string AdditionalInfo { get; }

        public ProgressData(long currentCount, long? totalCount = null, bool isFinish = false, string additionalInfo = "")
        {
            if (totalCount.HasValue && totalCount.Value < 0) totalCount = null;
            this.CurrentCount = currentCount;
            this.TotalCount = totalCount;
            this.IsFinish = isFinish;
            this.AdditionalInfo = additionalInfo;
            if (this.IsFinish && this.TotalCount != null)
            {
                this.CurrentCount = Math.Max((long)this.TotalCount, this.CurrentCount);
            }
            if (this.TotalCount != null)
            {
                if (this.CurrentCount >= this.TotalCount)
                {
                    this.IsFinish = true;
                }
            }
        }

        public static ProgressData Empty { get; } = new ProgressData(0, null, false);

        string LongValueToString(long? value, bool tostr3, string unitString = "", bool fileStr = false)
        {
            if (fileStr)
            {
                return value == null ? "??" : Str.GetFileSizeStr((long)value);
            }

            string s = value == null ? "??" : (tostr3 ? ((long)value)._ToString3() : value.ToString()._NonNull());
            if (unitString._IsFilled()) s += " " + unitString;
            return s;
        }

        public virtual string GetPercentageStr(ProgressReporterSettingBase setting, double? value)
        {
            if (value == null) return "?? %";

            return $"{value:F1}%";
        }

        public virtual string GenerateStatusStr(ProgressReporterSettingBase setting, ProgressReport report, ProgressThroughput? throughput = null)
        {
            string statusStr = ":";
            if (report.Type == ProgressReportType.Start) statusStr = ": Started:";
            if (report.Type == ProgressReportType.Finish) statusStr = ": Finished:";
            if (report.Type == ProgressReportType.Abort) statusStr = ": Aborted:";
            string etaStr = "";

            if (setting.ShowEta)
            {
                if (report.Type == ProgressReportType.InProgress && report.Eta != null)
                {
                    TimeSpan timeSpan = Util.ConvertTimeSpan(report.Eta ?? 0);

                    etaStr = $" ETA {timeSpan._ToTsStr()}";
                }
            }

            string throughputStr;

            if (setting.Options.Bit(ProgressReporterOptions.EnableThroughput) && throughput != null)
            {
                double value = throughput.Throughput;

                if (setting.Options.Bit(ProgressReporterOptions.ShowThroughputBps))
                {
                    value = throughput.Throughput * 8.0;
                }

                if (setting.Options.Bit(ProgressReporterOptions.ShowThroughputAsInt) == false)
                {
                    if (setting.Options.Bit(ProgressReporterOptions.ShowThroughputBps))
                    {
                        throughputStr = " " + ((long)value)._GetBpsStr();
                    }
                    else
                    {
                        throughputStr = " " + ((long)value)._GetFileSizeStr() + "/s";
                    }
                }
                else
                {
                    string s;

                    if (setting.ToStr3 == false)
                    {
                        s = value.ToString("F3");
                    }
                    else
                    {
                        s = ((long)value)._ToString3();
                    }

                    if (setting.Options.Bit(ProgressReporterOptions.ShowThroughputBps))
                    {
                        throughputStr = $" {s}bps";
                    }
                    else
                    {
                        throughputStr = $" {s}/s";
                    }
                }
            }
            else
            {
                throughputStr = "";
            }

            string addInfo2 = "";
            if (this.AdditionalInfo._IsFilled()) addInfo2 = " " + this.AdditionalInfo;

            return $"{setting.Title}{statusStr} {GetPercentageStr(setting, report.Percentage)} " +
                $"({LongValueToString(report.Data.CurrentCount, setting.ToStr3, "", setting.FileSizeStr)}/{LongValueToString(report.Data.TotalCount, setting.ToStr3, setting.Unit, setting.FileSizeStr)}){etaStr}{throughputStr}{addInfo2}";
        }
    }

    public sealed class ProgressReport
    {
        public ProgressReportType Type { get; }

        public ProgressData Data { get; }
        public double? Percentage { get; }
        public long? Eta { get; }

        public long Tick { get; }

        public ProgressReport(ProgressReportType type, ProgressData data, double? percentage, long? eta)
        {
            this.Data = data;
            this.Percentage = percentage;
            this.Tick = Tick64.Now;
            this.Type = type;
            this.Eta = eta;
        }
    }

    public sealed class ProgressThroughput
    {
        public long DurationMsecs;
        public long Volume;
        public double Throughput;

        public ProgressThroughput(long durationMsecs, long volume)
        {
            durationMsecs = Math.Max(durationMsecs, 0);

            this.DurationMsecs = durationMsecs;
            this.Volume = volume;

            if (this.DurationMsecs != 0)
            {
                this.Throughput = (double)this.Volume / ((double)this.DurationMsecs / 1000.0);
            }
        }
    }

    public class ProgressReportTimingSetting
    {
        public bool ReportEveryTiming { get; }
        public int ReportTimingIntervalMsecs { get; }
        public double ReportTimingPercentageSpace { get; }

        public ProgressReportTimingSetting(bool reportEveryTiming = false, int reportTimingMsecs = 1000, double reportTimingPercentageSpace = 10.0)
        {
            this.ReportEveryTiming = reportEveryTiming;
            this.ReportTimingIntervalMsecs = Math.Max(reportTimingMsecs, 0);
            this.ReportTimingPercentageSpace = Math.Max(0.0, reportTimingPercentageSpace);
        }
    }

    [Flags]
    public enum ProgressReporterOptions : long
    {
        None = 0,
        EnableThroughput = 1,
        ShowThroughputAsInt = 2,
        ShowThroughputBps = 4,
    }

    public class ProgressReporterSettingBase
    {
        public string Title { get; set; }

        public bool FileSizeStr { get; set; }
        public string Unit { get; set; }
        public bool ToStr3 { get; set; }

        public bool ShowEta { get; set; }

        public ProgressReporterOptions Options { get; }

        public ProgressReportTimingSetting ReportTimingSetting { get; set; }

        public ProgressReporterSettingBase(string title = "Ok", string unit = "", bool toStr3 = false, bool showEta = true, bool fileSizeStr = false, ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
        {
            if (reportTimingSetting == null)
            {
                reportTimingSetting = new ProgressReportTimingSetting();
            }
            this.Title = title._NonNullTrim();
            this.Unit = unit._NonNullTrim();
            this.ToStr3 = toStr3;
            this.ShowEta = showEta;
            this.FileSizeStr = fileSizeStr;

            this.ReportTimingSetting = reportTimingSetting;
            this.Options = options;
        }
    }

    public class NullProgressReporter : ProgressReporterBase
    {
        public NullProgressReporter(object? state = null) : base(new ProgressReporterSettingBase(), state) { }

        public override void ReportProgress(ProgressData data) { }

        protected override void ReceiveProgressInternal(ProgressData data, ProgressReportType type) { }

        protected override void ReportedImpl(ProgressReport report, ProgressThroughput? throughtput) { }
    }

    public abstract class ProgressReporterBase : IDisposable
    {
        readonly CriticalSection LockObj = new CriticalSection<ProgressReporterBase>();

        readonly EtaCalculator EtaCalc = new EtaCalculator();

        ProgressReport? ReportOnStart = null;
        ProgressReport? LastReport = null;
        ProgressReport? LastReportForThroughput = null;
        ProgressReport? ReportOnFinishOrAbort = null;

        protected abstract void ReportedImpl(ProgressReport report, ProgressThroughput? throughput);

        public ProgressReporterSettingBase Setting { get; }

        public object? State { get; }

        public ProgressReporterBase(ProgressReporterSettingBase setting, object? state)
        {
            this.Setting = setting;
            this.State = state;
        }

        public ProgressReport LastStatus
        {
            get
            {
                lock (LockObj)
                {
                    if (LastReport != null)
                    {
                        return LastReport;
                    }
                    else
                    {
                        return new ProgressReport(ProgressReportType.Start, ProgressData.Empty, null, null);
                    }
                }
            }
        }

        public virtual void ReportProgress(ProgressData data)
        {
            try
            {
                if (data == null) return;

                if (data.IsFinish)
                {
                    DoNothing();
                }

                lock (LockObj)
                {
                    if (ReportOnStart == null)
                    {
                        ReceiveProgressInternal(data, data.IsFinish ? ProgressReportType.Finish : ProgressReportType.Start);
                    }
                    else
                    {
                        ReceiveProgressInternal(data, data.IsFinish ? ProgressReportType.Finish : ProgressReportType.InProgress);
                    }
                }
            }
            catch
            {
            }
        }

        protected virtual void ReceiveProgressInternal(ProgressData data, ProgressReportType type)
        {
            double? percentage = null;

            long initialPosition = this.ReportOnStart?.Data.CurrentCount ?? 0;

            if (data.TotalCount != null)
            {
                long totalCount = Math.Max(data.TotalCount ?? 0, 1);
                long currentCount = Math.Min(Math.Max(data.CurrentCount, 0), totalCount);

                if (type == ProgressReportType.Finish)
                {
                    percentage = 100.0;
                }
                else
                {
                    percentage = (double)currentCount * 100.0 / (double)totalCount;
                    if (currentCount >= totalCount)
                    {
                        percentage = 100.0;
                    }
                }
            }
            else
            {
                if (type == ProgressReportType.Finish)
                {
                    percentage = 100.0;
                }
                else if (type == ProgressReportType.Start)
                {
                    percentage = 0.0;
                }
                else if (type == ProgressReportType.Abort)
                {
                }
                else
                {
                }
            }

            if (ReportOnFinishOrAbort != null)
            {
                return;
            }

            long? eta = EtaCalc.Calculate(Time.Tick64, percentage);

            ProgressReport report = new ProgressReport(type, data, percentage, eta);

            if ((type == ProgressReportType.Start || type == ProgressReportType.Finish) || ReportOnStart == null)
            {
                ReportOnStart = report;
            }

            if (type == ProgressReportType.Finish || type == ProgressReportType.Abort)
            {
                ReportOnFinishOrAbort = report;
            }

            LastReport = report;

            if (this.LastReportForThroughput == null) this.LastReportForThroughput = report;

            if (DetermineToReportOrNot(report))
            {
                ProgressThroughput? throughtput = null;

                if (this.Setting.Options.Bit(ProgressReporterOptions.EnableThroughput))
                {
                    var a = report;
                    var b = this.LastReportForThroughput;

                    throughtput = new ProgressThroughput(a.Tick - b.Tick, a.Data.CurrentCount - b.Data.CurrentCount);

                    this.LastReportForThroughput = report;

                    if (throughtput.DurationMsecs == 0) throughtput = null;
                }

                ReportedImpl(report, throughtput);
            }
        }

        ProgressReport? LastReportedReport = null;

        protected virtual bool DetermineToReportOrNot(ProgressReport report)
        {
            bool ret = false;
            if (LastReportedReport == null)
            {
                ret = true;
            }
            else
            {
                if (report.Type == ProgressReportType.Abort || report.Type == ProgressReportType.Finish || report.Type == ProgressReportType.Start)
                {
                    ret = true;
                }
                else
                {
                    if (Setting.ReportTimingSetting.ReportEveryTiming)
                        ret = true;

                    if (Setting.ReportTimingSetting.ReportTimingIntervalMsecs > 0)
                    {
                        if ((LastReportedReport.Tick + Setting.ReportTimingSetting.ReportTimingIntervalMsecs) <= report.Tick)
                            ret = true;
                    }

                    if (LastReportedReport.Percentage == null && report.Percentage != null)
                        ret = true;

                    if (LastReportedReport.Percentage != null && report.Percentage == null)
                        ret = true;

                    if (Setting.ReportTimingSetting.ReportTimingPercentageSpace != 0.0)
                        if (LastReportedReport.Percentage != null && report.Percentage != null)
                            if ((LastReportedReport.Percentage + Setting.ReportTimingSetting.ReportTimingPercentageSpace) <= report.Percentage)
                                ret = true;
                }
            }

            if (ret)
            {
                LastReportedReport = report;
            }

            return ret;
        }

        public void Abort() => Dispose();

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            try
            {
                lock (LockObj)
                {
                    if (this.LastReport != null)
                    {
                        ReceiveProgressInternal(this.LastReport.Data, ProgressReportType.Abort);
                    }
                    else
                    {
                        ReceiveProgressInternal(ProgressData.Empty, ProgressReportType.Abort);
                    }
                }
            }
            catch
            {
            }
        }
    }

    [Flags]
    public enum ProgressReporterOutputs
    {
        None = 0,
        Console = 1,
        Debug = 2,
        ConsoleAndDebug = Console | Debug,
    }

    public delegate void ProgressReportListener(ProgressReport report, string str);

    public class ProgressReporterSetting : ProgressReporterSettingBase
    {
        public ProgressReportListener? Listener { get; set; }
        public ProgressReporterOutputs AdditionalOutputs { get; }

        public ProgressReporterSetting(ProgressReporterOutputs outputs = ProgressReporterOutputs.Console, ProgressReportListener? listener = null, string title = "Ok", string unit = "", bool toStr3 = false, bool showEta = true,
            bool fileSizeStr = false, ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
            : base(title, unit, toStr3, showEta, fileSizeStr, reportTimingSetting, options)
        {
            this.AdditionalOutputs = outputs;
        }
    }

    public class ProgressReporter : ProgressReporterBase
    {
        public ProgressReporterSetting MySetting => (ProgressReporterSetting)this.Setting;

        public ProgressReporter(ProgressReporterSetting setting, object? state = null) : base(setting, state)
        {
        }

        protected override void ReportedImpl(ProgressReport report, ProgressThroughput? throughtput)
        {
            string str = report.Data.GenerateStatusStr(MySetting, report, throughtput);

            if (str._IsFilled())
            {
                var outputs = MySetting.AdditionalOutputs;

                try
                {
                    if (outputs.Bit(ProgressReporterOutputs.Console))
                    {
                        bool skip = false;
                        if (outputs.Bit(ProgressReporterOutputs.Debug))
                        {
                            if (Dbg.IsDebugMode)
                            {
                                skip = true;
                            }
                        }

                        if (skip == false)
                        {
                            lock (Con.ConsoleWriteLock)
                            {
                                Console.WriteLine(str);
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    if (outputs.Bit(ProgressReporterOutputs.Debug))
                        Con.WriteDebug(str);
                }
                catch { }

                try
                {
                    MySetting.Listener?.Invoke(report, str);
                }
                catch { }
            }
        }
    }

    public class ProgressFileProcessingReporter : ProgressReporter
    {
        public ProgressFileProcessingReporter(object? state, ProgressReporterOutputs outputs, ProgressReportListener? listener = null,
            string title = "Processing a file", ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
            : base(new ProgressReporterSetting(outputs, listener, title, "", false, true, true, reportTimingSetting, options), state) { }
    }

    public class ProgressFileDownloadingReporter : ProgressReporter
    {
        public ProgressFileDownloadingReporter(object? state, ProgressReporterOutputs outputs, ProgressReportListener? listener = null,
            string title = "Downloading a file", ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
            : base(new ProgressReporterSetting(outputs, listener, title, "", false, true, true, reportTimingSetting, options), state) { }
    }

    public abstract class ProgressReporterFactoryBase
    {
        public ProgressReporterOutputs Outputs { get; set; }
        public ProgressReportListener? Listener { get; set; }
        public ProgressReportTimingSetting? ReportTimingSetting { get; set; }
        public ProgressReporterOptions Options { get; set; }

        public ProgressReporterFactoryBase(ProgressReporterOutputs outputs, ProgressReportListener? listener = null, ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
        {
            this.Outputs = outputs;
            this.Listener = listener;
            this.ReportTimingSetting = reportTimingSetting;
            this.Options = options;
        }

        public abstract ProgressReporterBase CreateNewReporter(string title, object? state = null);
    }

    public class ProgressFileProcessingReporterFactory : ProgressReporterFactoryBase
    {
        public ProgressFileProcessingReporterFactory(ProgressReporterOutputs outputs, ProgressReportListener? listener = null, ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
            : base(outputs, listener, reportTimingSetting, options) { }

        public override ProgressReporterBase CreateNewReporter(string title, object? state = null)
        {
            return new ProgressFileProcessingReporter(state, this.Outputs, this.Listener, title, this.ReportTimingSetting, this.Options);
        }
    }

    public class ProgressFileDownloadingReporterFactory : ProgressReporterFactoryBase
    {
        public ProgressFileDownloadingReporterFactory(ProgressReporterOutputs outputs = ProgressReporterOutputs.Console, ProgressReportListener? listener = null, ProgressReportTimingSetting? reportTimingSetting = null, ProgressReporterOptions options = ProgressReporterOptions.None)
            : base(outputs, listener, reportTimingSetting, options) { }

        public override ProgressReporterBase CreateNewReporter(string title, object? state = null)
        {
            return new ProgressFileDownloadingReporter(state, this.Outputs, this.Listener, title, this.ReportTimingSetting, this.Options);
        }
    }

    public class NullReporterFactory : ProgressReporterFactoryBase
    {
        public NullReporterFactory()
            : base(ProgressReporterOutputs.None, null, null, ProgressReporterOptions.None) { }

        public override ProgressReporterBase CreateNewReporter(string title, object? state = null)
        {
            return new NullProgressReporter(state);
        }
    }

    public static class ZeroedReadOnlyStruct<T> where T : struct
    {
        static readonly T ZeroedValue;

        public static unsafe ref readonly T Value
        {
            [MethodImpl(Inline)]
            get
            {
                return ref ZeroedValue;
            }
        }

    }

    public static class ZeroedMemory<T>
    {
        const int Size = 65536;
        public static readonly ReadOnlyMemory<T> Memory = new T[Size];
        public static ReadOnlyMemory<T> GetZeroFilledMemory(int size)
        {
            if (size < Size)
                return Memory.Slice(0, size);
            else
                return new T[size];
        }
    }

    public class WeightedExceptionList
    {
        static long IdSeed = 0;

        class Entry
        {
            public long Id;
            public int Weight;
            public Exception? Exception;
        }

        readonly List<Entry> List = new List<Entry>();

        readonly CriticalSection LockObj = new CriticalSection<WeightedExceptionList>();

        public void Add(Exception exception, int weight)
        {
            lock (LockObj)
            {
                List.Add(new Entry() { Exception = exception, Weight = weight, Id = Interlocked.Increment(ref IdSeed) });
            }
        }

        public Exception? GetException()
        {
            lock (LockObj)
            {
                return this.List.OrderByDescending(x => x.Weight).ThenBy(x => IdSeed).FirstOrDefault()?.Exception ?? null;
            }
        }
    }

    public class FieldReaderWriter
    {
        // 高速検索用ハッシュテーブル
        readonly Dictionary<string, MemberInfo> MetadataTableInternal = new Dictionary<string, MemberInfo>();
        public IReadOnlyDictionary<string, MemberInfo> MetadataTable => MetadataTableInternal;

        readonly Dictionary<string, MemberInfo> MetadataTableIgnoreCaseInternal = new Dictionary<string, MemberInfo>();
        public IReadOnlyDictionary<string, MemberInfo> MetadataTableIgnoreCase => MetadataTableIgnoreCaseInternal;

        public Type TargetType { get; }

        public IReadOnlyList<string> FieldOrPropertyNamesList { get; }
        public IReadOnlyList<string> MethodNamesList { get; }

        public IReadOnlyList<string> OrderedPublicFieldOrPropertyNamesList { get; }

        static readonly Singleton<Type, FieldReaderWriter> _PublicSingleton = new Singleton<Type, FieldReaderWriter>(t => new FieldReaderWriter(t, false));
        static readonly Singleton<Type, FieldReaderWriter> _PrivateSingleton = new Singleton<Type, FieldReaderWriter>(t => new FieldReaderWriter(t, true));

        public FieldReaderWriter(Type targetType, bool includePrivate = false)
        {
            this.TargetType = targetType;

            var fields = TargetType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Concat(includePrivate ? TargetType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic) : new FieldInfo[0]);

            var properties = TargetType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.SetProperty)
                .Concat(includePrivate ? TargetType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty) : new PropertyInfo[0]);

            var methods = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod)
                .Concat(includePrivate ? TargetType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod) : new MethodInfo[0]);

            var m1 = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod);
            var m2 = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod);

            List<string> fieldOrPropertyNamesList = new List<string>();

            var members = TargetType.GetMembers();

            List<string> methodNamesList = new List<string>();

            foreach (MemberInfo info in fields.Cast<MemberInfo>().Concat(properties.Cast<MemberInfo>().Concat(methods.Cast<MemberInfo>())))
            {
                if (MetadataTableInternal.TryAdd(info.Name, info))
                {
                    if (info is FieldInfo || info is PropertyInfo)
                        fieldOrPropertyNamesList.Add(info.Name);

                    if (info is MethodInfo)
                        methodNamesList.Add(info.Name);
                }

                string nameIgnoreCase = info.Name.ToLowerInvariant();

                MetadataTableIgnoreCaseInternal.TryAdd(nameIgnoreCase, info);
            }

            this.FieldOrPropertyNamesList = fieldOrPropertyNamesList;
            this.MethodNamesList = methodNamesList;

            // 登場順に並べられたフィールドおよびプロパティのリストを生成
            List<string> orderedFieldOrPropertyNamesList = new List<string>();
            TargetType.GetMembers().Where(x => x.MemberType.BitAny(MemberTypes.Field | MemberTypes.Property))
                .Where(x => FieldOrPropertyNamesList.Contains(x.Name))
                ._DoForEach(x => orderedFieldOrPropertyNamesList.Add(x.Name));
            this.OrderedPublicFieldOrPropertyNamesList = orderedFieldOrPropertyNamesList;
        }

        public static FieldReaderWriter GetCached(Type type) => _PublicSingleton.CreateOrGet(type);
        public static FieldReaderWriter GetCached<T>() => GetCached(typeof(T));

        public static FieldReaderWriter GetCachedPrivate(Type type) => _PrivateSingleton.CreateOrGet(type);
        public static FieldReaderWriter GetCachedPrivate<T>() => GetCachedPrivate(typeof(T));

        public object? Invoke(object targetObject, string name, params object?[] parameters)
        {
            if (parameters == null) parameters = new object?[1] { null };
            if (targetObject.GetType()._IsSubClassOfOrSame(this.TargetType) == false) throw new ArgumentException("Type of targetObject is different from TargetType.");

            if (this.MetadataTable.TryGetValue(name, out MemberInfo? info) == false)
                throw new ArgumentException($"The member \"{name}\" not found.");

            switch (info)
            {
                case MethodInfo method:
                    try
                    {
                        return method.Invoke(targetObject, parameters);
                    }
                    catch (Exception ex)
                    {
                        throw ex._GetSingleException();
                    }

                default:
                    throw new ApplicationException($"The member \"{name}\" is not a method.");
            }
        }

        public object? GetValue(object targetObject, string name, bool ignoreCase = false, bool returnNullIfError = false)
        {
            try
            {
                if (targetObject.GetType()._IsSubClassOfOrSame(this.TargetType) == false)
                {
                    if (returnNullIfError == false)
                    {
                        throw new ArgumentException("Type of targetObject is different from TargetType.");
                    }
                    else
                    {
                        return null;
                    }
                }

                MemberInfo? info;

                if (ignoreCase == false)
                {
                    if (this.MetadataTable.TryGetValue(name, out info) == false)
                    {
                        if (returnNullIfError == false)
                        {
                            throw new ArgumentException($"The member \"{name}\" not found.");
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
                else
                {
                    if (this.MetadataTableIgnoreCase.TryGetValue(name.ToLowerInvariant(), out info) == false)
                    {
                        if (returnNullIfError == false)
                        {
                            throw new ArgumentException($"The member \"{name}\" not found.");
                        }
                        else
                        {
                            return null;
                        }
                    }
                }

                switch (info)
                {
                    case FieldInfo field:
                        return field.GetValue(targetObject);

                    case PropertyInfo property:
                        return property.GetValue(targetObject);

                    default:
                        if (returnNullIfError == false)
                        {
                            throw new ApplicationException($"The member \"{name}\" is not a field or a property.");
                        }
                        else
                        {
                            return null;
                        }
                }
            }
            catch
            {
                if (returnNullIfError == false)
                {
                    throw;
                }
                else
                {
                    return null;
                }
            }
        }

        public void SetValue(object targetObject, string name, object? value, bool ignoreCase = false)
        {
            if (targetObject.GetType()._IsSubClassOfOrSame(this.TargetType) == false) throw new ArgumentException("Type of targetObject is different from TargetType.");

            MemberInfo? info;

            if (ignoreCase == false)
            {
                if (this.MetadataTable.TryGetValue(name, out info) == false || ((info as PropertyInfo)?.CanWrite ?? true) == false)
                {
                    if (this.MetadataTable.TryGetValue($"<{name}>k__BackingField", out info) == false)
                    {
                        throw new ArgumentException($"The member \"{name}\" not found.");
                    }
                }
            }
            else
            {
                if (this.MetadataTableIgnoreCase.TryGetValue(name.ToLowerInvariant(), out info) == false || ((info as PropertyInfo)?.CanWrite ?? true) == false)
                {
                    if (this.MetadataTableIgnoreCase.TryGetValue($"<{name}>k__BackingField".ToLowerInvariant(), out info) == false)
                    {
                        throw new ArgumentException($"The member \"{name}\" not found.");
                    }
                }
            }

            switch (info)
            {
                case FieldInfo field:
                    field.SetValue(targetObject, value);
                    return;

                case PropertyInfo property:
                    property.SetValue(targetObject, value);
                    return;

                default:
                    throw new ApplicationException($"The member \"{name}\" is not a field or a property.");
            }
        }

        public T CalcDiff<T>([DisallowNull] T current, [DisallowNull] T prev)
        {
            T ret = (T)Util.NewWithoutConstructor(this.TargetType);

            CalcDiff(ret, current!, prev!);

            return ret;
        }

        public void CalcDiff(object dest, object current, object prev)
        {
            foreach (string name in this.FieldOrPropertyNamesList)
            {
                object prevValue = this.GetValue(prev, name)!;
                object currentValue = this.GetValue(current, name)!;
                object destValue = Util.Subtract(currentValue, prevValue)!;
                this.SetValue(dest, name, destValue);
            }
        }

        public T DivideBy<T>([DisallowNull] T target, double by)
        {
            T ret = (T)Util.NewWithoutConstructor(this.TargetType);

            DivideBy(ret, target!, by);

            return ret;
        }

        public void DivideBy(object dest, object target, double by)
        {
            foreach (string name in this.FieldOrPropertyNamesList)
            {
                object currentValue = this.GetValue(target, name)!;
                object destValue = Util.Divide(currentValue, by)!;
                this.SetValue(dest, name, destValue);
            }
        }

        public void CopyValues(object dest, object src)
        {
            foreach (string name in this.FieldOrPropertyNamesList)
            {
                object srcValue = this.GetValue(src, name)!;
                this.SetValue(dest, name, srcValue);
            }
        }

        public object CreateClone(object targetObject)
        {
            if (targetObject.GetType() != this.TargetType) throw new ArgumentException("Type of targetObject is different from TargetType.");

            object ret = Util.NewWithoutConstructor(this.TargetType);

            CopyValues(ret, targetObject);

            return ret;
        }

        public T CreateClone<T>([DisallowNull] T targetObject) => (T)CreateClone((object)targetObject!);

        // object.str のような文字列でフィールドへのアクセスを許容する
        public static object? GetFieldValueByDottedFieldNames(object? targetObject, string fieldName, bool ignoreCase = false, bool returnNullIfError = false, string? defaultFirstFieldName = null)
        {
            if (defaultFirstFieldName._IsFilled())
            {
                if (returnNullIfError) targetObject._NullCheck(nameof(targetObject));

                if (targetObject == null) return null;

                var rw = FieldReaderWriter.GetCached(targetObject.GetType());

                var a = rw.GetValue(targetObject, defaultFirstFieldName, ignoreCase, true);

                if (a != null)
                {
                    targetObject = a;
                }
            }

            var namesList = fieldName._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '.', '\t', '\r', '\n', ' ', '　');

            foreach (string name in namesList)
            {
                if (returnNullIfError) targetObject._NullCheck(nameof(targetObject));

                if (targetObject == null) return null;

                var rw = FieldReaderWriter.GetCached(targetObject.GetType());

                targetObject = rw.GetValue(targetObject, name, ignoreCase, returnNullIfError);
            }

            return targetObject;
        }
    }

    [Flags]
    public enum StatisticsReporterLogTypes
    {
        None = 0,
        Snapshot = 1,
        Diffs = 2,
        Velocity = 4,
        All = 0x7fffffff,
    }

    public class StatisticsReporter<T> : AsyncServiceWithMainLoop
        where T : class
    {
        public readonly T CurrentValues;

        public int Interval { get; }

        readonly CriticalSection LockObj = new CriticalSection<StatisticsReporter<T>>();

        public readonly AsyncEventListenerList<T, NonsenseEventType> ListenerList = new AsyncEventListenerList<T, NonsenseEventType>();

        readonly FieldReaderWriter ReaderWriter;

        readonly Func<T, T, T, Task>? ReceiverAsync; // async (snapshot, diff, velocity)

        readonly StatisticsReporterLogTypes LogTypes;

        public StatisticsReporter(int interval, StatisticsReporterLogTypes logTypes, Func<T, T, T, Task>? receiverProc = null, params AsyncEventCallback<T, NonsenseEventType>[] initialListenerProcs)
            : base(default)
        {
            this.Interval = interval;
            this.LogTypes = logTypes;
            this.ReaderWriter = new FieldReaderWriter(typeof(T));
            this.CurrentValues = Util.NewWithoutConstructor<T>();
            this.ReceiverAsync = receiverProc;

            foreach (var proc in initialListenerProcs)
            {
                ListenerList.RegisterCallback(proc);
            }

            StartMainLoop(MainLoopAsync, true);
        }

        async Task FireEventAsync()
        {
            await ListenerList.FireAsync(this.CurrentValues, NonsenseEventType.Nonsense);
        }

        async Task MainLoopAsync(CancellationToken cancel)
        {
            T prevValues = Util.NewWithoutConstructor<T>();
            double prevTime = Time.NowHighResDouble;
            int num = 0;

            while (true)
            {
                this.GrandCancel.ThrowIfCancellationRequested();

                await FireEventAsync();

                double nowTime = Time.NowHighResDouble;
                double timeDiff = nowTime - prevTime;
                prevTime = nowTime;

                try
                {
                    T snapshot = this.ReaderWriter.CreateClone(this.CurrentValues);
                    T diffs = this.ReaderWriter.CalcDiff(snapshot, prevValues);
                    T velocity = (num == 0 ? Util.NewWithoutConstructor<T>() : this.ReaderWriter.DivideBy(diffs, timeDiff));

                    prevValues = snapshot;

                    if (LogTypes.Bit(StatisticsReporterLogTypes.Snapshot))
                        LocalLogRouter.PostStat(snapshot, tag: "Snapshot");

                    if (LogTypes.Bit(StatisticsReporterLogTypes.Diffs))
                        LocalLogRouter.PostStat(diffs, tag: "Diffs");

                    if (LogTypes.Bit(StatisticsReporterLogTypes.Velocity))
                        LocalLogRouter.PostStat(velocity, tag: "Velocity");

                    if (this.ReceiverAsync != null)
                        await this.ReceiverAsync(snapshot, diffs, velocity);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), timeout: this.Interval);

                num++;
            }
        }
    }


    public class InternalOverrideClassTypeBuilder
    {
        static readonly MethodInfo MethodInfoForMethodInvoke = typeof(MethodInfo).GetMethods().Where(x => x.Name == "Invoke").Where(x => x.GetParameters().Length == 2).Single();
        static readonly MethodInfo MethodInfoForMethodInvoke2 = typeof(MethodBase).GetMethods().Where(x => x.Name == "Invoke").Where(x => x.GetParameters().Length == 2).Single();

        readonly TypeBuilder TypeBuilder;

        const string AppStateFieldName = "__AppState__";

        Type? BuiltType = null;

        public InternalOverrideClassTypeBuilder(Type originalType, string typeNameSuffix = "Ex")
        {
            Assembly originalAsm = originalType.Assembly;
            string friendAssemblyName = originalAsm.GetCustomAttributes<InternalsVisibleToAttribute>().Where(x => x.AllInternalsVisible).First().AssemblyName;

            AssemblyName asmName = new AssemblyName(friendAssemblyName);
            AssemblyBuilder asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = asmBuilder.DefineDynamicModule(Guid.NewGuid().ToString());

            TypeBuilder = moduleBuilder.DefineType(originalType.Name + typeNameSuffix, TypeAttributes.Public | TypeAttributes.Class, originalType);

            ConstructorInfo[] baseConstructorsList = originalType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Concat(originalType.GetConstructors(BindingFlags.Instance | BindingFlags.Public)).ToArray();

            // App state field
            this.TypeBuilder.DefineField(AppStateFieldName, typeof(object), FieldAttributes.Public);

            // Create all new constructors for each of base constructors
            foreach (ConstructorInfo baseCtor in baseConstructorsList)
            {
                Type[] parameterTypes = baseCtor.GetParameters().Select(x => x.ParameterType).ToArray();

                ConstructorBuilder newCtor = TypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameterTypes);
                ILGenerator il = newCtor.GetILGenerator();

                for (int i = 0; i < parameterTypes.Length + 1; i++)
                {
                    il.Emit(OpCodes.Ldarg, i);
                }

                il.Emit(OpCodes.Call, baseCtor);
                il.Emit(OpCodes.Ret);
            }
        }

        public static int FieldIdSeed = 0;

        class FieldEntry
        {
            public FieldInfo? Field;
            public object? Value;
        }

        List<FieldEntry> DynamicFieldList = new List<FieldEntry>();

        public void AddOverloadMethod(string name, Delegate m, Type retType, params Type[] argsType)
            => AddOverloadMethod(name, m.GetMethodInfo()!, retType, argsType);

        public void AddOverloadMethod(string name, MethodInfo methodInfoToCall, Type retType, params Type[] argsType)
        {
            if (BuiltType != null) throw new ApplicationException("BuildType() has been already called.");

            if (argsType == null) argsType = new Type[0];

            FieldBuilder fieldForMethodToCall = this.TypeBuilder.DefineField($"__method_to_call_{Interlocked.Increment(ref FieldIdSeed)}_{name}", typeof(MethodInfo), FieldAttributes.Public);

            DynamicFieldList.Add(new FieldEntry() { Field = fieldForMethodToCall, Value = methodInfoToCall });

            FieldBuilder? fieldAsThisPointer = null;

            if (methodInfoToCall.IsStatic == false)
            {
                fieldAsThisPointer = this.TypeBuilder.DefineField($"__method_as_this_for_{Interlocked.Increment(ref FieldIdSeed)}_{name}", methodInfoToCall.DeclaringType!, FieldAttributes.Public);

                DynamicFieldList.Add(new FieldEntry() { Field = fieldAsThisPointer, Value = Util.NewWithoutConstructor(methodInfoToCall.DeclaringType!) });
            }

            MethodBuilder newMethod = this.TypeBuilder.DefineMethod(name,
                MethodAttributes.Virtual | MethodAttributes.Public,
                retType,
                argsType);

            ILGenerator il = newMethod.GetILGenerator();

            // Generate IL for:
            // ----
            // public virtual retType NAME(argsType[0] arg0, argsType[1] arg1, argsType[2] arg2, ...)
            // {
            //    return methodInfoToCall.Invoke(this, new object[] { arg0, arg1, arg2, ... });
            // }
            // ----

            // The target object for MethodInfo.Invoke()
            il.Emit(OpCodes.Ldarg, 0); // to execute Ldfld, the target container object of the field must be loaded onto the stack
            il.Emit(OpCodes.Ldfld, fieldForMethodToCall);

            // this (The first argument for MethodInfo.Invoke())
            if (methodInfoToCall.IsStatic)
            {
                // Set null since the method to call is a static method
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                // Set the dummy instance as the this pointer of the dynamic method to call
                il.Emit(OpCodes.Ldarg, 0); // to execute Ldfld, the target container object of the field must be loaded onto the stack
                il.Emit(OpCodes.Ldfld, fieldAsThisPointer!);
            }

            // create new object[argsType.Length] array
            il.Emit(OpCodes.Ldc_I4, argsType.Length + 1);
            il.Emit(OpCodes.Newarr, typeof(object));

            // array_copy = array
            il.Emit(OpCodes.Dup);

            // array_copy[0] = arg[0]
            il.Emit(OpCodes.Ldc_I4, 0);
            il.Emit(OpCodes.Ldarg, 0);
            il.Emit(OpCodes.Stelem_Ref);

            for (int i = 0; i < argsType.Length; i++)
            {
                // array_copy = array
                il.Emit(OpCodes.Dup);

                // array_copy[i + 1] = arg[i + 1]
                il.Emit(OpCodes.Ldc_I4, i + 1);
                il.Emit(OpCodes.Ldarg, i + 1);
                il.Emit(OpCodes.Stelem_Ref);
            }

            // Call MethodInfo.Invoke()
            il.Emit(OpCodes.Call, MethodInfoForMethodInvoke);

            // return with cast
            il.Emit(OpCodes.Castclass, retType);
            il.Emit(OpCodes.Ret);
        }

        public Type BuildType()
        {
            if (BuiltType == null)
                BuiltType = this.TypeBuilder.CreateType();

            return BuiltType!;
        }

        public object? CreateInstance(object appState, params object?[] parameters)
        {
            if (parameters == null)
                parameters = new object?[] { null };

            object? ret;

            try
            {
                if (parameters.Length == 0)
                    ret = Activator.CreateInstance(this.BuildType());
                else
                    ret = Activator.CreateInstance(this.BuildType(), parameters);
            }
            catch (Exception ex)
            {
                throw ex._GetSingleException();
            }

            foreach (var fieldEntry in this.DynamicFieldList)
            {
                ret!._PrivateSet(fieldEntry.Field!.Name, fieldEntry!.Value!);
            }

            SetAppState(ret!, appState);

            return ret;
        }

        public static object? GetAppState(object targetObject) => targetObject._PrivateGet(AppStateFieldName);

        public static void SetAppState(object targetObject, object? state) => targetObject._PrivateSet(AppStateFieldName, state);
    }

    [Flags]
    public enum DaemonStatus
    {
        Stopped = 0,
        Starting,
        Running,
        Stopping,
    }

    public class DaemonOptions
    {
        public string Name { get; }
        public string FriendlyName { get; }
        public int StopTimeout { get; }
        public bool SingleInstance { get; }

        public DaemonOptions(string name, string friendlyName, bool singleInstance, int stopTimeout = 0)
        {
            if (stopTimeout == 0)
                stopTimeout = CoresConfig.Timeouts.DaemonDefaultStopTimeout;

            if (stopTimeout < 0) stopTimeout = Timeout.Infinite;

            this.Name = name;
            this.FriendlyName = friendlyName;
            this.StopTimeout = stopTimeout;

            this.SingleInstance = singleInstance;
        }
    }

    public class WPair2<TA, TB>
    {
        public TA A { get; set; }
        public TB B { get; set; }

        public WPair2(TA a, TB b)
        {
            this.A = a;
            this.B = b;
        }
    }

    public class WPair3<TA, TB, TC>
    {
        public TA A { get; set; }
        public TB B { get; set; }
        public TC C { get; set; }

        public WPair3(TA a, TB b, TC c)
        {
            this.A = a;
            this.B = b;
            this.C = c;
        }
    }

    public class WPair4<TA, TB, TC, TD>
    {
        public TA A { get; set; }
        public TB B { get; set; }
        public TC C { get; set; }
        public TD D { get; set; }

        public WPair4(TA a, TB b, TC c, TD d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }
    }

    public class WPair5<TA, TB, TC, TD, TE>
    {
        public TA A { get; set; }
        public TB B { get; set; }
        public TC C { get; set; }
        public TD D { get; set; }
        public TE E { get; set; }

        public WPair5(TA a, TB b, TC c, TD d, TE e)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
            this.E = e;
        }
    }


    public class Pair2<TA, TB>
    {
        public TA A { get; }
        public TB B { get; }

        public Pair2(TA a, TB b)
        {
            this.A = a;
            this.B = b;
        }
    }

    public class Pair3<TA, TB, TC>
    {
        public TA A { get; }
        public TB B { get; }
        public TC C { get; }

        public Pair3(TA a, TB b, TC c)
        {
            this.A = a;
            this.B = b;
            this.C = c;
        }
    }

    public class Pair4<TA, TB, TC, TD>
    {
        public TA A { get; }
        public TB B { get; }
        public TC C { get; }
        public TD D { get; }

        public Pair4(TA a, TB b, TC c, TD d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }
    }

    public class Pair5<TA, TB, TC, TD, TE>
    {
        public TA A { get; }
        public TB B { get; }
        public TC C { get; }
        public TD D { get; }
        public TE E { get; }

        public Pair5(TA a, TB b, TC c, TD d, TE e)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
            this.E = e;
        }
    }

    public class SizedDataQueue<T>
    {
        List<T> InternalList;

        public long CurrentTotalSize { get; private set; }

        public IReadOnlyList<T> GetList()
        {
            return this.InternalList;
        }

        public SizedDataQueue()
        {
            this.InternalList = new List<T>();
        }

        public void Add(T t, long size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            this.InternalList.Add(t);
            this.CurrentTotalSize += size;
        }

        public void Clear()
        {
            this.InternalList = new List<T>();
            this.CurrentTotalSize = 0;
        }
    }

    public class ConcurrentHashSet<TKey> : ConcurrentDictionary<TKey, int>
        where TKey : notnull
    {
        public ConcurrentHashSet() { }

        public ConcurrentHashSet(IEqualityComparer<TKey> comparer) : base(comparer) { }

        public bool Add(TKey item) => TryAdd(item, 0);
        public bool Remove(TKey item) => TryRemove(item, out _);
        public bool Contains(TKey item) => ContainsKey(item);

        public new TKey[] ToArray() => base.Keys.ToArray();
        public List<TKey> ToList() => base.Keys.ToList();
    }

    public class SystemAndUser<TSystem, TUser>
    {
        public TSystem System { get; }
        public TUser User { get; }

        public SystemAndUser(TSystem system, TUser user)
        {
            this.System = system;
            this.User = user;
        }
    }

    public class HashSetDictionary<TKey, TValue> : Dictionary<TKey, HashSet<TValue>>
        where TKey : notnull
    {
        readonly IEqualityComparer<TValue>? ValueComparer;
        readonly HashSet<TValue> EmptyValueSet;

        public HashSetDictionary() : base() // for JSON serializer
        {
            this.ValueComparer = null;
            this.EmptyValueSet = new HashSet<TValue>(this.ValueComparer);
        }

        public HashSetDictionary(IEqualityComparer<TKey>? keyComparer, IEqualityComparer<TValue>? valueComparer = null) : base(keyComparer)
        {
            this.ValueComparer = valueComparer;
            this.EmptyValueSet = new HashSet<TValue>(this.ValueComparer);
        }

        public bool Add(TKey key, TValue value)
        {
            if (this.TryGetValue(key, out HashSet<TValue>? hashSet) == false)
            {
                hashSet = new HashSet<TValue>(this.ValueComparer);

                base[key] = hashSet;
            }

            return hashSet!.Add(value);
        }

        public new IReadOnlyCollection<TValue> this[TKey key]
        {
            get
            {
                if (this.TryGetValue(key, out HashSet<TValue>? hashSet))
                {
                    return hashSet;
                }
                else
                {
                    return this.EmptyValueSet;
                }
            }
        }
    }

    public class ConcurrentStrDictionary<TValue> : ConcurrentDictionary<string, TValue>
    {
        public ConcurrentStrDictionary() : base(StrComparer.IgnoreCaseComparer) { } // for JSON serializer
        public ConcurrentStrDictionary(IEqualityComparer<string>? comparer) : base(comparer ?? StrComparer.IgnoreCaseComparer) { }
    }

    public class StrDictionary<TValue> : Dictionary<string, TValue>
    {
        public StrDictionary() : base(StrComparer.IgnoreCaseComparer) { } // for JSON serializer
        public StrDictionary(IEqualityComparer<string>? comparer) : base(comparer ?? StrComparer.IgnoreCaseComparer) { }
    }

    public class KeyValueList<TKey, TValue> : List<KeyValuePair<TKey, TValue>>
    {
        public KeyValueList() { } // for JSON serializer

        public KeyValueList(IEnumerable<KeyValuePair<TKey, TValue>> srcData)
        {
            foreach (var kv in srcData)
            {
                this.Add(kv.Key, kv.Value);
            }
        }

        public void Add(TKey key, TValue value)
        {
            this.Add(new KeyValuePair<TKey, TValue>(key, value));
        }

        public KeyValueList<TKey, TValue> Clone()
        {
            KeyValueList<TKey, TValue> ret = new KeyValueList<TKey, TValue>();

            foreach (var kv in this)
            {
                ret.Add(kv.Key, kv.Value);
            }

            return ret;
        }

        public void RemoveWhen(Func<KeyValuePair<TKey, TValue>, bool> condition)
        {
            List<KeyValuePair<TKey, TValue>> toRemove = new List<KeyValuePair<TKey, TValue>>();

            foreach (var kv in this)
                if (condition(kv))
                    toRemove.Add(kv);

            foreach (var kv in toRemove)
                this.Remove(kv);
        }

        public void RemoveWhenKey(TKey key, IEqualityComparer<TKey> comparer)
        {
            this.RemoveWhen(kv => comparer.Equals(kv.Key, key));
        }

        public void AddOrUpdateKeyValueSingle(TKey key, TValue value, IEqualityComparer<TKey> comparer)
        {
            this.RemoveWhenKey(key, comparer);

            this.Add(key, value);
        }

        public TValue GetSingleOrNew(TKey key, TValue newValue, IEqualityComparer<TKey>? comparer = null)
        {
            var exists = this.Where(x => comparer?.Equals(x.Key, key) ?? object.Equals(x.Key, key));
            if (exists.Any()) return exists.Single().Value;

            this.Add(key, newValue);
            return newValue;
        }

        public TValue GetSingleOrNew(TKey key, Func<TValue> newValueProc, IEqualityComparer<TKey>? comparer = null)
        {
            var exists = this.Where(x => comparer?.Equals(x.Key, key) ?? object.Equals(x.Key, key));
            if (exists.Any()) return exists.Single().Value;

            TValue newValue = newValueProc();
            this.Add(key, newValue);
            return newValue;
        }
    }

    [Flags]
    public enum RateLimiterMode
    {
        Penalty = 0, // 流量を超えて流入する場合でもペナルティ計測をする
        NoPenalty, // ペナルティ計測をしない
    }

    public class RateLimiterOptions
    {
        public double Burst { get; }
        public double LimitPerSecond { get; }
        public int ExpiresMsec { get; }
        public int MaxEntries { get; }
        public RateLimiterMode Mode { get; }
        public int GcIntervalMsec { get; }

        public RateLimiterOptions(double burst = Consts.RateLimiter.DefaultBurst, double limitPerSecond = Consts.RateLimiter.DefaultLimitPerSecond,
            int expiresMsec = Consts.RateLimiter.DefaultExpiresMsec, RateLimiterMode mode = RateLimiterMode.Penalty, int maxEntries = Consts.RateLimiter.DefaultMaxEntries,
            int gcIntervalMsec = Consts.RateLimiter.DefaultGcIntervalMsec)
        {
            if (burst < 0.0) throw new ArgumentOutOfRangeException(nameof(burst));
            if (limitPerSecond < 0.0) throw new ArgumentOutOfRangeException(nameof(limitPerSecond));
            if (expiresMsec <= 0) throw new ArgumentOutOfRangeException(nameof(expiresMsec));
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            if (gcIntervalMsec <= 0) throw new ArgumentOutOfRangeException(nameof(gcIntervalMsec));

            this.Burst = burst;
            this.LimitPerSecond = limitPerSecond;
            this.ExpiresMsec = expiresMsec;
            this.MaxEntries = maxEntries;
            this.Mode = mode;
            this.GcIntervalMsec = gcIntervalMsec;
        }
    }

    public class RateLimiterEntry
    {
        public RateLimiterOptions Options { get; }
        public readonly CriticalSection LockObj = new CriticalSection<RateLimiterEntry>();

        public long CreatedTick { get; }
        public long ExpiresTick => this.LastInputTick + Options.ExpiresMsec;
        public double CurrentAmount { get; private set; } = 0.0;

        public long LastInputTick { get; private set; }

        public long LastErrorReportedTick = 0;

        internal RateLimiterEntry(EnsureInternal yes, RateLimiterOptions options, long createdTick)
        {
            this.Options = options;
            this.CreatedTick = createdTick;
            this.LastInputTick = createdTick;
        }

        // Amount の値を計算して更新する
        double CalcAndUpdateAmount(long now, double amount)
        {
            amount = amount._NonNegative();

            //lock (LockObj)
            {
                now = now._Max(this.LastInputTick);

                double current = this.CurrentAmount;

                // 前回からの経過秒数を計測する
                long timeDiff = now - this.LastInputTick;

                this.LastInputTick = now;

                if (timeDiff != 0)
                {
                    // わずかでも時間が経過していた場合
                    double timeDiffDouble = (double)timeDiff / 1000.0;

                    // 前回からの流出量を計算する
                    double flowOut = (Options.LimitPerSecond * timeDiffDouble)._NonNegative();

                    // 流出をさせる
                    current -= flowOut;
                    current = current._NonNegative();
                }

                double ret = current;

                // 許容値を超えた流入の記録は Penalty モードの場合のみ行なう
                if (Options.Mode == RateLimiterMode.Penalty || (current <= Options.Burst))
                {
                    // 流入をさせる
                    current += amount;
                    current = current._NonNegative();
                }

                this.CurrentAmount = current;

                return ret;
            }
        }

        // 流入
        public bool TryInput(long now, double amount)
        {
            // 流入計算
            double current = CalcAndUpdateAmount(now, amount);

            // 許容バースト量よりも現在の量のほうが少ないかどうか検査
            return current <= Options.Burst;
        }
    }

    public interface IConcurrentLimiter
    {
        bool TryEnter(object key, out int currentCount, int maxConcurrentRequestsOverride = 0);
        void Exit(object key, out int currentCount);
    }

    public class ConcurrentLimiter<TKey> : IConcurrentLimiter
        where TKey : notnull
    {
        class Entry
        {
            public int Count = 0;
            public long LastErrorReportTick = 0;
        }

        public int MaxConcurrentRequests { get; }

        readonly CriticalSection LockObj = new CriticalSection<ConcurrentLimiter<TKey>>();

        readonly Dictionary<TKey, Entry> Table = new Dictionary<TKey, Entry>();

        public FastEventListenerList<ConcurrentLimiter<TKey>, NonsenseEventType> EventListener { get; } = new FastEventListenerList<ConcurrentLimiter<TKey>, NonsenseEventType>();

        public ConcurrentLimiter(int maxConcurrentRequests = int.MaxValue)
        {
            this.MaxConcurrentRequests = maxConcurrentRequests._NonNegative();
        }

        public IHolder EnterWithUsing(TKey key, out int currentCount, int maxConcurrentRequestsOverride = 0)
        {
            if (this.TryEnter(key, out currentCount, maxConcurrentRequestsOverride) == false)
            {
                int maxValue = maxConcurrentRequestsOverride >= 1 ? maxConcurrentRequestsOverride : this.MaxConcurrentRequests;

                throw new CoresException($"ConcurrentLimiter exceeded: Key = '{key.ToString()}', Current = {currentCount}, Max = {maxValue}");
            }

            return new Holder(() =>
            {
                this.Exit(key, out _);
            });
        }

        public bool TryEnter(TKey key, out int currentCount, int maxConcurrentRequestsOverride = 0)
        {
            if (this.MaxConcurrentRequests == 0)
            {
                currentCount = 0;
                return true;
            }

            int maxValue = maxConcurrentRequestsOverride >= 1 ? maxConcurrentRequestsOverride : this.MaxConcurrentRequests;

            lock (LockObj)
            {
                if (this.Table.TryGetValue(key, out Entry? entry) == false)
                {
                    currentCount = 0;
                }
                else
                {
                    currentCount = entry!.Count;
                    Debug.Assert(currentCount >= 1);
                }

                if (currentCount >= maxValue)
                {
                    if (entry != null)
                    {
                        long now = Time.Tick64;

                        if (entry.LastErrorReportTick == 0 || (now >= (entry.LastErrorReportTick + 1000L)))
                        {
                            entry.LastErrorReportTick = now;
                            this.EventListener.Fire(this, NonsenseEventType.Nonsense, $"ConcurrentLimiter exceeded: Key = '{key.ToString()}', Current = {currentCount}, Max = {maxValue}");
                        }
                    }

                    return false;
                }

                currentCount++;

                if (entry == null)
                {
                    entry = new Entry();
                    this.Table[key] = entry;
                }

                this.Table[key].Count = currentCount;

                return true;
            }
        }

        public void Exit(TKey key, out int currentCount)
        {
            if (this.MaxConcurrentRequests == 0)
            {
                currentCount = 0;
                return;
            }

            lock (LockObj)
            {
                if (this.Table.TryGetValue(key, out Entry? entry) == false)
                {
                    // Error!
                    Debug.Assert(false);
                }

                Debug.Assert(entry!.Count >= 1);

                entry.Count--;

                if (entry.Count <= 0)
                {
                    this.Table.Remove(key);
                }

                currentCount = Math.Max(entry.Count, 0);
            }
        }

        public bool TryEnter(object key, out int currentCount, int maxConcurrentRequestsOverride = 0)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return TryEnter((TKey)key, out currentCount, maxConcurrentRequestsOverride);
        }

        public void Exit(object key, out int currentCount)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Exit((TKey)key, out currentCount);
        }
    }

    public interface IRateLimiter
    {
        bool TryInput(object key, out RateLimiterEntry entry, double amount = 1.0, long now = 0);
    }

    public class RateLimiter<TKey> : IRateLimiter
        where TKey : notnull
    {
        public RateLimiterOptions Options { get; }

        readonly Dictionary<TKey, RateLimiterEntry> Table = new Dictionary<TKey, RateLimiterEntry>();
        readonly CriticalSection LockObj = new CriticalSection<RateLimiter<TKey>>();

        long NextGcTick = 0;

        public FastEventListenerList<RateLimiter<TKey>, NonsenseEventType> EventListener { get; } = new FastEventListenerList<RateLimiter<TKey>, NonsenseEventType>();

        long exceededErrorLastReport = 0;

        public RateLimiter(RateLimiterOptions options)
        {
            this.Options = options;

            if (options.GcIntervalMsec > 0)
                this.NextGcTick = Tick64.Now + options.GcIntervalMsec;
        }

        // 有効期限が経過したエントリを削除する
        void GcCollect(long now)
        {
            List<TKey> deletes = new List<TKey>();

            foreach (var kv in this.Table)
            {
                if (now > kv.Value.ExpiresTick)
                {
                    deletes.Add(kv.Key);
                }
            }

            foreach (var key in deletes)
            {
                Table.Remove(key);
            }
        }

        public bool TryInput(TKey key, [NotNullWhen(true)] out RateLimiterEntry? entry, double amount = 1.0, long now = 0)
        {
            // now が供給されていない場合は計測する
            if (now == 0) now = Time.Tick64;

            // テーブル全体のロックはできるだけ最小限にする
            lock (LockObj)
            {
                // GC の実行
                if (this.NextGcTick != 0 && this.NextGcTick <= now)
                {
                    this.NextGcTick = now + Options.GcIntervalMsec;

                    GcCollect(now);
                }

                // エントリが作成されていない場合は作成する
                if (this.Table.TryGetValue(key, out entry) == false)
                {
                    if (Table.Count >= this.Options.MaxEntries)
                    {
                        // 最大登録可能数を超過しているため作成できない
                        entry = null;

                        if (exceededErrorLastReport == 0 || (now >= (exceededErrorLastReport + 1000L)))
                        {
                            exceededErrorLastReport = now;

                            this.EventListener.Fire(this, NonsenseEventType.Nonsense, $"RateLimiter entry count exceeded. Current = {Table.Count}, Max = {this.Options.MaxEntries}");
                        }

                        return false;
                    }

                    entry = new RateLimiterEntry(EnsureInternal.Yes, this.Options, now);
                    this.Table.Add(key, entry);
                }
            }

            // 取得されたエントリに対して流入操作を実行する
            bool ret = entry!.TryInput(now, amount);

            if (ret == false)
            {
                lock (entry.LockObj)
                {
                    if (entry.LastErrorReportedTick == 0 || (now >= (entry.LastErrorReportedTick + 1000L)))
                    {
                        entry.LastErrorReportedTick = now;

                        this.EventListener.Fire(this, NonsenseEventType.Nonsense, $"RateLimiter input flow exceeded. Key = '{key.ToString()}', Current = {entry.CurrentAmount:F2}, LimitPerSec = {entry.Options.LimitPerSecond:F2}, Burst = {entry.Options.Burst:F2}");
                    }
                }
            }

            return ret;
        }

        public bool TryInput(object key, [NotNullWhen(true)] out RateLimiterEntry entry, double amount = 1, long now = 0)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return TryInput((TKey)key, out entry!, amount, now);
        }
    }

    // 戻り値とエラー (戻り値およびエラーは独立しており、エラーの場合も戻り値は返すことができる)
    public class ResultAndError<T>
    {
        [AllowNull]
        readonly T ResultInternal = default!;
        readonly bool IsErrorInternal = false;

        [MaybeNull]
        public T Value
        {
            get
            {
                return this.ResultInternal;
            }
        }

        public bool IsError => IsErrorInternal;
        public bool IsOk => (!IsError);

        public ResultAndError([AllowNull] T result, bool ok)
        {
            this.ResultInternal = result;
            this.IsErrorInternal = !ok;
        }

        public void ThrowIfError()
        {
            if (IsErrorInternal) throw new ApplicationException("ResultAndError: Error was occured.");
        }

        public static implicit operator ResultAndError<T>(bool boolValue)
        {
            if (boolValue != false) throw new ArgumentException($"{nameof(boolValue)} must be false.");

            return new ResultAndError<T>(default, false);
        }

        [return: MaybeNull]
        public static implicit operator T(ResultAndError<T> resultOrException) => resultOrException.Value;

        public static implicit operator bool(ResultAndError<T>? resultOrException) => resultOrException != null && resultOrException.IsOk;
    }

    // 戻り値またはエラー (エラーのときは戻り値がなく、戻り値があるときはエラーはない)
    public class ResultOrError<T>
    {
        [AllowNull]
        readonly T ResultInternal = default!;
        readonly bool IsErrorInternal = false;

        [MaybeNull]
        public T Value
        {
            get
            {
                if (IsErrorInternal) throw new ApplicationException("ResultOrError: Error was occured.");
                return this.ResultInternal;
            }
        }

        public bool IsError => IsErrorInternal;
        public bool IsOk => (!IsError);

        public ResultOrError([AllowNull] T result)
        {
            this.ResultInternal = result;
            this.IsErrorInternal = false;
        }

        public ResultOrError(EnsureError error)
        {
            this.ResultInternal = default!;
            this.IsErrorInternal = true;
        }

        public void ThrowIfError()
        {
            if (IsErrorInternal) throw new ApplicationException("ResultOrError: Error was occured.");
        }

        public static implicit operator ResultOrError<T>([AllowNull] T result) => new ResultOrError<T>(result);
        public static implicit operator ResultOrError<T>(bool boolValue)
        {
            if (boolValue != false) throw new ArgumentException($"{nameof(boolValue)} must be false.");

            return new ResultOrError<T>(EnsureError.Error);
        }

        [return: MaybeNull]
        public static implicit operator T(ResultOrError<T> resultOrException) => resultOrException.Value;

        public static implicit operator bool(ResultOrError<T>? resultOrException) => resultOrException != null && resultOrException.IsOk;
    }

    public class OkOrExeption : ResultOrExeption<bool>
    {
        public OkOrExeption() : base(true)
        {
        }

        public OkOrExeption(Exception ex) : base(ex)
        {
        }

        public static implicit operator OkOrExeption(Exception ex) => new OkOrExeption(ex);

        public static implicit operator bool(OkOrExeption? resultOrException) => resultOrException != null && resultOrException.IsOk;
    }

    public class ResultOrExeption<T> : IHasError
    {
        [AllowNull]
        readonly T ResultInternal = default!;

        readonly Exception ExceptionInternal = null!;

        [MaybeNull]
        public T Value
        {
            get
            {
                if (ExceptionInternal != null)
                {
                    this.ExceptionInternal._ReThrow();
                    throw this.ExceptionInternal; // 予備
                }

                return this.ResultInternal;
            }
        }

        public Exception Exception => this.ExceptionInternal;

        public bool IsError => ExceptionInternal != null;

        public bool IsOk => (!IsError);

        public Exception? LastError => this.ExceptionInternal;

        public ResultOrExeption([AllowNull] T result)
        {
            this.ResultInternal = result;
        }

        public ResultOrExeption(Exception ex)
        {
            this.ExceptionInternal = ex ?? throw new ArgumentNullException(nameof(ex));
        }

        public void ThrowIfException()
        {
            var ex = this.Exception;
            if (ex != null)
            {
                ex._ReThrow();
                throw ex; // 予備
            }
        }

        public static implicit operator ResultOrExeption<T>([AllowNull] T result) => new ResultOrExeption<T>(result);
        public static implicit operator ResultOrExeption<T>(Exception ex) => new ResultOrExeption<T>(ex);

        public static implicit operator bool(ResultOrExeption<T>? resultOrException) => resultOrException != null && resultOrException.IsOk;

        [return: MaybeNull]
        public static implicit operator T(ResultOrExeption<T> resultOrException) => resultOrException.Value;
    }

    public class PipelineDuplex : IDuplexPipe
    {
        public PipelineDuplex(PipeReader reader, PipeWriter writer)
        {
            Input = reader;
            Output = writer;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        public static PipelineDuplexPair CreateConnectionPair(PipeOptions inputOptions, PipeOptions outputOptions)
        {
            var input = new Pipe(inputOptions);
            var output = new Pipe(outputOptions);

            var transportToApplication = new PipelineDuplex(output.Reader, input.Writer);
            var applicationToTransport = new PipelineDuplex(input.Reader, output.Writer);

            return new PipelineDuplexPair(applicationToTransport, transportToApplication);
        }

        // This class exists to work around issues with value tuple on .NET Framework
        public readonly struct PipelineDuplexPair
        {
            public IDuplexPipe Transport { get; }
            public IDuplexPipe Application { get; }

            public PipelineDuplexPair(IDuplexPipe transport, IDuplexPipe application)
            {
                Transport = transport;
                Application = application;
            }
        }
    }

    // 複数の Stream を積み重ねて読み書きするためのクラス
    // コンストラクタで一番下のレイヤ (たいていは物理ファイルやネットワークソケット) を指定する。
    // その後、下から上に向かって Add() で順に中間ストリームを積み重ねていく。
    // アプリケーションからの書き込み要求は、最後に追加されたレイヤーから順に処理される。
    // たとえば、圧縮と暗号化を積み重ねる場合、まず暗号化を Add() して、その後に圧縮を Add() する必要がある。(逆にすると圧縮されない)
    public class StreamsStack : StreamImplBase, IHasError
    {
        public readonly StreamImplBaseOptions ImplBaseOptions;

        class Layer
        {
            public readonly Stream Stream;
            public readonly bool AutoDispose;

            public Layer(Stream stream, bool autoDispose)
            {
                stream._NullCheck();

                Stream = stream;
                AutoDispose = autoDispose;
            }
        }

        readonly List<Layer> LayerList = new List<Layer>(); // このリスト上は逆順 Bottom -> Top に並んでいるので注意
        readonly Layer BottomLayer;
        Layer TopLayer = null!;

        public Exception? LastError => throw new NotImplementedException();

        public StreamsStack(Stream bottomStream, StreamImplBaseOptions options, bool autoDispose = false)
        {
            try
            {
                if (options.CanSeek) throw new ArgumentOutOfRangeException(nameof(options), "Cannot support seek.");

                this.ImplBaseOptions = new StreamImplBaseOptions(options.CanRead, options.CanWrite, false); // Seek はサポートしなくてよい

                AddInternal(bottomStream, autoDispose);

                this.BottomLayer = this.TopLayer;
            }
            catch
            {
                throw;
            }
        }

        // WrapperStreamImplBase ストリームレイヤを追加
        public async Task AddWraapperStreamAsync(Func<Stream, WrapperStreamImplBase> newStream, bool autoDispose = false, CancellationToken cancel = default)
        {
            newStream._NullCheck();

            var newSt = newStream(this.LayerList.Last().Stream);

            try
            {
                AddInternal(newSt, autoDispose);

                await newSt.InitAsync(cancel);
            }
            catch
            {
                await newSt._DisposeSafeAsync();

                throw;
            }
        }

        // ストリームレイヤを追加
        public void Add(Func<Stream, Stream> newStream, bool autoDispose = false)
        {
            newStream._NullCheck();

            Stream newSt = newStream(this.LayerList.Last().Stream);

            try
            {
                AddInternal(newSt, autoDispose);
            }
            catch
            {
                newSt._DisposeSafe();

                throw;
            }
        }

        void AddInternal(Stream newStream, bool autoDispose)
        {
            newStream._NullCheck();

            if (ImplBaseOptions.CanRead && newStream.CanRead == false) throw new ArgumentException("ImplBaseOptions.CanRead && newStream.CanRead == false", nameof(newStream));
            if (ImplBaseOptions.CanWrite && newStream.CanWrite == false) throw new ArgumentException("ImplBaseOptions.CanWrite && newStream.CanWrite == false", nameof(newStream));

            Layer layer = new Layer(newStream, autoDispose);
            LayerList.Add(layer);

            this.TopLayer = layer;
        }

        Once DisposeFlag;

        public override bool DataAvailable => throw new NotImplementedException();

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (DisposeFlag.IsFirstCall() == false) return;
                await DisposeInternalAsync();
            }
            finally
            {
                await base.DisposeAsync();
            }
        }
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                DisposeInternalAsync()._GetResult();
            }
            finally { base.Dispose(disposing); }
        }
        async Task DisposeInternalAsync()
        {
            // Top から Bottom に向かってすべての Stream を Dispose する
            foreach (Layer a in this.LayerList.Reverse<Layer>())
            {
                if (a.AutoDispose)
                {
                    await a.Stream._DisposeSafeAsync();
                }
            }
        }

        protected override long GetLengthImpl() => throw new NotImplementedException();
        protected override void SetLengthImpl(long length) => throw new NotImplementedException();
        protected override long GetPositionImpl() => throw new NotImplementedException();
        protected override void SetPositionImpl(long position) => throw new NotImplementedException();
        protected override long SeekImpl(long offset, SeekOrigin origin) => throw new NotImplementedException();

        protected override async Task FlushImplAsync(CancellationToken cancellationToken = default)
        {
            // Top から Bottom に向かってすべての Stream を Flush する
            foreach (Layer a in this.LayerList.Reverse<Layer>())
            {
                await a.Stream.FlushAsync(cancellationToken);
            }
        }

        protected override async ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await this.TopLayer.Stream.ReadAsync(buffer, cancellationToken);
        }

        protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await this.TopLayer.Stream.WriteAsync(buffer, cancellationToken);
        }
    }

    // BinaryLineReader オプション
    public class BinaryLineReaderOption
    {
        public object? Param { get; }
        public Func<MemoryStream, object?, bool>? FinishDeterminer { get; }
        public Action<MemoryStream, object?, CancellationToken>? Preview { get; }

        public BinaryLineReaderOption(Func<MemoryStream, object?, bool>? finishDeterminer = null, Action<MemoryStream, object?, CancellationToken>? preview = null, object? param = null)
        {
            Param = param;
            FinishDeterminer = finishDeterminer;
            Preview = preview;
        }
    }

    // Stream からバイナリを読み込み、CR, LF を検出して行に分解する。高速化のためにバッファリングを使用する
    public class BinaryLineReader
    {
        public int BufferSize { get; }
        public Stream Stream { get; }

        public long CurrentRelativePosition { get; private set; }

        readonly Memory<byte> Buffer;
        int CurrentPositionInBuffer = 0;
        int CurrentSizeInBuffer = 0;

        MemoryStream CurrentLine = new MemoryStream();

        public BinaryLineReader(Stream stream, int bufferSize = Consts.Numbers.DefaultLargeBufferSize)
        {
            this.BufferSize = bufferSize;
            this.Stream = stream;

            this.Buffer = new byte[bufferSize + 1]; // 最後の文字が CR の場合は追加で 1 文字読むため、1 バイト多めにしてある
        }

        readonly Queue<Memory<byte>> SingleLineQueue = new Queue<Memory<byte>>();

        public async Task<string?> ReadSingleLineStringAsync(int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, Encoding? encoding = null, CancellationToken cancel = default)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;

            var memory = await ReadSingleLineAsync(maxBytesPerLine, cancel);
            if (memory == null) return null;

            return encoding.GetString(memory.Value.Span);
        }

        public async Task<Memory<byte>?> ReadSingleLineAsync(int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            if (SingleLineQueue.Count >= 1)
            {
                return SingleLineQueue.Dequeue();
            }

            List<Memory<byte>>? multiLines = await ReadLinesAsync(maxBytesPerLine, cancel);

            if (multiLines == null)
            {
                return null;
            }

            Debug.Assert(multiLines.Count >= 1);

            if (multiLines.Count == 1) return multiLines[0];

            foreach (var line in multiLines)
            {
                this.SingleLineQueue.Enqueue(line);
            }

            return SingleLineQueue.Dequeue();
        }

        // FinishDeterminer で終わりと判定されるか、EOF に当たるまで、無限に Stream から行を読む。
        // ただし、メモリ制限値 DefaultMaxBytesTotalLine を超えた場合はエラーにする。
        public async Task<List<Memory<byte>>?> ReadLinesUntilEofAsync(RefBool isEof, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int maxBytesTotal = Consts.Numbers.DefaultMaxBytesTotalLine, CancellationToken cancel = default, BinaryLineReaderOption? option = null)
        {
            int currentSize = 0;

            isEof._NullCheck(nameof(isEof));

            List<Memory<byte>> ret = new List<Memory<byte>>();

            isEof.Set(false);

            while (true)
            {
                RefBool finisherMatched = new RefBool();

                var r = await ReadLinesAsync(maxBytesPerLine, cancel, option, finisherMatched);

                if (r != null)
                {
                    r._DoForEach(x =>
                    {
                        ret.Add(x);

                        currentSize += x.Length;

                        if (currentSize > maxBytesTotal)
                        {
                            throw new CoresLibException($"currentSize ({currentSize}) > maxBytesTotal ({maxBytesTotal})");
                        }
                    });
                }

                if (r == null)
                {
                    // EOF
                    isEof.Set(true);
                    break;
                }
                else if (finisherMatched)
                {
                    // 期待された行の終わりに到達
                    break;
                }
            }

            return ret;
        }

        // 内部バッファサイズを目標として、読めるだけ Stream から行を読む。
        // EOF に当たり、これ以上読むべき行がない場合は null を返す。
        public async Task<List<Memory<byte>>?> ReadLinesAsync(int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, CancellationToken cancel = default, BinaryLineReaderOption? option = null, RefBool? finisherMatched = null)
        {
            finisherMatched?.Set(false);

            List<Memory<byte>> ret = new List<Memory<byte>>();

            while (true)
            {
                bool isEof = false;

                Debug.Assert(CurrentSizeInBuffer >= CurrentPositionInBuffer);
                int remain = CurrentSizeInBuffer - CurrentPositionInBuffer;
                if (remain == 0)
                {
                    // バッファが空の場合は読み込む
                    CurrentPositionInBuffer = 0;

                    cancel.ThrowIfCancellationRequested();

                    int r = await Stream.ReadAsync(this.Buffer.Slice(0, this.BufferSize), cancel);

                    if (r == 0)
                    {
                        // 最後まで読んだ
                        isEof = true;
                    }

                    CurrentSizeInBuffer = r;

                    if (r >= 1)
                    {
                        if (this.Buffer.Span[r - 1] == 13)
                        {
                            // 最後の文字が CR の場合は次の 1 文字を追加で読む
                            int r2 = await Stream.ReadAsync(this.Buffer.Slice(r, 1), cancel);
                            CurrentSizeInBuffer += r2;
                        }
                    }
                }

                // EOF 処理
                if (isEof)
                {
                    // 現在バッファに入っているデータ全体の取得
                    Debug.Assert(CurrentSizeInBuffer <= CurrentPositionInBuffer);
                    int remain2 = CurrentSizeInBuffer - CurrentPositionInBuffer;

                    if (remain2 <= 0)
                    {
                        // バッファには 1 バイトも入っていない。
                    }
                    else
                    {
                        // バッファに入っている最後のデータには改行は含まれていないはずである。
                        // したがってこのデータをそのまま最後の行として出力する。

                        if (((long)CurrentLine.Length + (long)remain2) > maxBytesPerLine)
                        {
                            // 1 行あたり最大読み取り可能文字数を超えた
                            throw new CoresLibException($"Line character length overflow. {((long)CurrentLine.Length + (long)remain2)} > {maxBytesPerLine}");
                        }
                    }

                    // 読み取ったデータを CurrentLine に追記する
                    CurrentLine.Write(this.Buffer.Span.Slice(CurrentPositionInBuffer, remain2));

                    CurrentPositionInBuffer += remain2;

                    if (CurrentLine.Length >= 1)
                    {
                        // 行の終わりとみなし、この行をリストに追加する
                        ret.Add(CurrentLine.ToArray());

                        CurrentRelativePosition += CurrentLine.Length;

                        CurrentLine = new MemoryStream();
                    }

                    break;
                }

                Sync(() =>
                {
                    Span<byte> bufferSpan = this.Buffer.Span;

                    while (true)
                    {
                        Debug.Assert(CurrentSizeInBuffer >= CurrentPositionInBuffer);
                        int remain3 = CurrentSizeInBuffer - CurrentPositionInBuffer;

                        if (remain3 <= 0)
                        {
                            // バッファが空になった。一旦 Sync を出て、再度バッファを読み直す
                            return;
                        }

                        // 現在バッファに入っているデータ全体の Span の取得
                        Span<byte> bufferCurrentRange = bufferSpan.Slice(CurrentPositionInBuffer, CurrentSizeInBuffer - CurrentPositionInBuffer);

                        string str2 = bufferCurrentRange.ToArray()._GetString();

                        // 最初の改行コード (行の末尾) にぶつかるまで 1 文字ずつ読む
                        bool newLineFound = false;
                        int sizeRead = 0;
                        int sizeOfLineData = 0;
                        int crlfSize = 0;

                        for (int i = 0; i < bufferCurrentRange.Length; i++)
                        {
                            byte c = bufferCurrentRange[i];

                            if (c == 13 || c == 10)
                            {
                                // 改行コードを発見
                                newLineFound = true;
                                sizeOfLineData = i;
                                sizeRead = i + 1;
                                crlfSize = 1;
                                if (c == 13 && (i < (bufferCurrentRange.Length - 1)) && bufferCurrentRange[i + 1] == 10)
                                {
                                    sizeRead = i + 2;
                                    crlfSize = 2;
                                }
                                break;
                            }
                        }

                        if (newLineFound == false)
                        {
                            // 改行コード (行の末尾) が見つからなかった場合、バッファに入っている全体を読み取る
                            sizeOfLineData = sizeRead = bufferCurrentRange.Length;
                        }

                        // 現在のバッファ中の position を進める
                        CurrentPositionInBuffer += sizeRead;

                        if (((long)CurrentLine.Length + (long)sizeOfLineData) > maxBytesPerLine)
                        {
                            // 1 行あたり最大読み取り可能文字数を超えた
                            throw new CoresException($"Line character length overflow. {((long)CurrentLine.Length + (long)sizeOfLineData)} > {maxBytesPerLine}");
                        }

                        // 読み取ったデータを CurrentLine に追記する
                        CurrentLine.Write(bufferCurrentRange.Slice(0, sizeOfLineData));

                        if (newLineFound == false)
                        {
                            if (CurrentLine.Length >= 1)
                            {
                                // 改行コードによる行の終わりには達していないが、これ以上読むべきデータがない場合は、
                                // FinishDeterminer により行の終わりと判定されるかどうか試してみる
                                if (option?.FinishDeterminer?.Invoke(CurrentLine, option.Param) ?? false)
                                {
                                    string str = CurrentLine.ToArray()._GetString();

                                    // 行の終わりとみなす
                                    newLineFound = true;

                                    finisherMatched?.Set(true);
                                }
                            }
                        }

                        if (newLineFound)
                        {
                            string str = CurrentLine.ToArray()._GetString();

                            option?.Preview?.Invoke(CurrentLine, option.Param, cancel);
                        }

                        if (newLineFound)
                        {
                            // 行の終わりに達していたらこの行をリストに追加する
                            byte[] data = CurrentLine.ToArray();
                            ret.Add(data);

                            CurrentRelativePosition += data.Length + crlfSize;

                            CurrentLine = new MemoryStream();
                        }
                    }
                });

                // バッファが空になったらここに飛んでくる。
                // 1 行以上読み取りに成功している場合は関数を抜ける。
                if (ret.Count >= 1) break;

                // まだ 1 行も読み取りに成功していない場合はループしてバッファの読み出しを試行する。
            }

            if (ret.Count == 0)
            {
                // 1 行も読み取ることができなかった場合は EOF を意味するので null を返す。
                return null;
            }

            return ret;
        }

        public List<Memory<byte>>? ReadLines(int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, CancellationToken cancel = default)
            => this.ReadLinesAsync(maxBytesPerLine, cancel)._GetResult();

        // タイムアウト付き 1 行取得
        // PipeStream 専用
        // タイムアウトした場合は、改行コードがなくてもひとまず読めた部分まで返す (1 バイト以上読めている場合のみ)
        // この場合、Item2 は false となる。
        // Eof の場合は null を返す。
        public async Task<List<Tuple<Memory<byte>, bool>>?> ReadLinesWithTimeoutAsync(int timeout, int maxBytesPerLine = Consts.Numbers.DefaultMaxBytesPerLine, int maxLines = int.MaxValue, CancellationToken cancel = default)
        {
            if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));

            List<Tuple<Memory<byte>, bool>> ret = new List<Tuple<Memory<byte>, bool>>();

            if ((this.Stream is PipeStream) == false)
            {
                throw new CoresException("Stream is not PipeStream");
            }

            PipeStream pipeStream = (PipeStream)this.Stream;

            while (true)
            {
                bool isEof = false;
                bool isTimeout = false;

                if (ret.Count < maxLines)
                {
                    Debug.Assert(CurrentSizeInBuffer >= CurrentPositionInBuffer);
                    int remain = CurrentSizeInBuffer - CurrentPositionInBuffer;
                    if (remain == 0)
                    {
                        // バッファが空の場合は読み込む
                        CurrentPositionInBuffer = 0;

                        int r = 0;

                        try
                        {
                            await pipeStream.WaitReadyToReceiveAsync(cancel, timeout);
                            r = await pipeStream.ReceiveAsync(this.Buffer.Slice(0, this.BufferSize), cancel);

                            if (r == 0)
                            {
                                // 最後まで読んだ
                                isEof = true;
                            }
                        }
                        catch (TimeoutException)
                        {
                            // タイムアウトが発生した

                            isTimeout = true;
                        }

                        CurrentSizeInBuffer = r;

                        if (r >= 1)
                        {
                            if (this.Buffer.Span[r - 1] == 13)
                            {
                                // 最後の文字が CR の場合は次の 1 文字を追加で読む
                                int r2 = await pipeStream.ReadAsync(this.Buffer.Slice(r, 1), cancel);
                                CurrentSizeInBuffer += r2;
                            }
                        }
                    }
                }

                // EOF 処理 または タイムアウト処理
                if (isEof || isTimeout)
                {
                    // 現在バッファに入っているデータ全体の取得
                    Debug.Assert(CurrentSizeInBuffer <= CurrentPositionInBuffer);
                    int remain2 = CurrentSizeInBuffer - CurrentPositionInBuffer;

                    if (remain2 <= 0)
                    {
                        // バッファには 1 バイトも入っていない。
                    }
                    else
                    {
                        // バッファに入っている最後のデータには改行は含まれていないはずである。
                        // したがってこのデータをそのまま最後の行として出力する。

                        if (((long)CurrentLine.Length + (long)remain2) > maxBytesPerLine)
                        {
                            // 1 行あたり最大読み取り可能文字数を超えた
                            throw new CoresException($"Line character length overflow. {((long)CurrentLine.Length + (long)remain2)} > {maxBytesPerLine}");
                        }
                    }

                    // 読み取ったデータを CurrentLine に追記する
                    CurrentLine.Write(this.Buffer.Span.Slice(CurrentPositionInBuffer, remain2));

                    CurrentPositionInBuffer += remain2;

                    if (CurrentLine.Length >= 1)
                    {
                        // 行の終わりとみなし、この行をリストに追加する
                        ret.Add(new Tuple<Memory<byte>, bool>(CurrentLine.ToArray(), !isTimeout));

                        CurrentRelativePosition += CurrentLine.Length;

                        CurrentLine = new MemoryStream();
                    }

                    if (isEof)
                    {
                        break;
                    }
                }

                Sync(() =>
                {
                    Span<byte> bufferSpan = this.Buffer.Span;

                    while (ret.Count < maxLines)
                    {
                        // 現在バッファに入っているデータ全体の Span の取得
                        Debug.Assert(CurrentSizeInBuffer >= CurrentPositionInBuffer);
                        int remain3 = CurrentSizeInBuffer - CurrentPositionInBuffer;

                        if (remain3 <= 0)
                        {
                            // バッファが空になった。一旦 Sync を出て、再度バッファを読み直す
                            return;
                        }

                        Span<byte> bufferCurrentRange = bufferSpan.Slice(CurrentPositionInBuffer, CurrentSizeInBuffer - CurrentPositionInBuffer);

                        // 最初の改行コードにぶつかるまで 1 文字ずつ読む
                        bool newLineFound = false;
                        int sizeRead = 0;
                        int sizeOfLineData = 0;
                        int crlfSize = 0;

                        for (int i = 0; i < bufferCurrentRange.Length; i++)
                        {
                            byte c = bufferCurrentRange[i];

                            if (c == 13 || c == 10)
                            {
                                // 改行コードを発見
                                newLineFound = true;
                                sizeOfLineData = i;
                                sizeRead = i + 1;
                                crlfSize = 1;
                                if (c == 13 && (i < (bufferCurrentRange.Length - 1)) && bufferCurrentRange[i + 1] == 10)
                                {
                                    sizeRead = i + 2;
                                    crlfSize = 2;
                                }
                                break;
                            }
                        }

                        if (newLineFound == false)
                        {
                            sizeOfLineData = sizeRead = bufferCurrentRange.Length;
                        }

                        // 現在のバッファ中の position を進める
                        CurrentPositionInBuffer += sizeRead;

                        if (((long)CurrentLine.Length + (long)sizeOfLineData) > maxBytesPerLine)
                        {
                            // 1 行あたり最大読み取り可能文字数を超えた
                            throw new CoresException($"Line character length overflow. {((long)CurrentLine.Length + (long)sizeOfLineData)} > {maxBytesPerLine}");
                        }

                        // 読み取ったデータを CurrentLine に追記する
                        CurrentLine.Write(bufferCurrentRange.Slice(0, sizeOfLineData));

                        if (newLineFound)
                        {
                            // 行の終わりに達していたらこの行をリストに追加する
                            byte[] data = CurrentLine.ToArray();
                            ret.Add(new Tuple<Memory<byte>, bool>(data, true));

                            CurrentRelativePosition += data.Length + crlfSize;

                            CurrentLine = new MemoryStream();
                        }
                    }
                });

                // バッファが空になったらここに飛んでくる。
                // 1 行以上読み取りに成功している場合は関数を抜ける。
                if (ret.Count >= 1) break;

                // まだ 1 行も読み取りに成功していない場合はループしてバッファの読み出しを試行する。
            }

            if (ret.Count == 0)
            {
                // 1 行も読み取ることができなかった場合は EOF を意味するので null を返す。
                return null;
            }

            return ret;
        }


    }

    public static class GenericInfo<T>
    {
        public static readonly T SampleElement = default!;

        public static int Size
        {
            [MethodImpl(Inline)]
            get => Unsafe.SizeOf<T>();
        }
        public static readonly Type Type;

        public static bool IsByte
        {
            [MethodImpl(Inline)]
            get => typeof(T) == typeof(byte);
        }

        static GenericInfo()
        {
            Type = typeof(T);
        }
    }

    public class SharedObjectHolder<T> : IDisposable where T : class
    {
        public T Object { get; }

        readonly SharedObjectFactory<T> Factory;

        public SharedObjectHolder(SharedObjectFactory<T> factory, T obj)
        {
            this.Factory = factory;
            this.Object = obj;
        }

        public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            this.Factory.Release();
        }

        public static implicit operator T(SharedObjectHolder<T> holder) => holder.Object;
    }

    public abstract class SharedObjectFactory<T> where T : class
    {
        public static int ReferenceCounter { get; private set; }

        readonly object LockObj = new object();
        T? Instance = null;

        protected SharedObjectFactory() { }

        protected abstract T CreateNewImpl();

        public SharedObjectHolder<T> CreateOrGet()
        {
            lock (LockObj)
            {
                if (ReferenceCounter == 0)
                {
                    T newObject = CreateNewImpl();

                    this.Instance = newObject;
                }

                ReferenceCounter++;

                return new SharedObjectHolder<T>(this, this.Instance!);
            }
        }

        public void Release()
        {
            T? instanceToRelease = null;

            lock (LockObj)
            {
                if (ReferenceCounter >= 1)
                {
                    ReferenceCounter--;

                    if (ReferenceCounter == 0)
                    {
                        instanceToRelease = this.Instance;
                        this.Instance = null;
                    }
                }
            }

            if (instanceToRelease != null)
            {
                if (instanceToRelease is IDisposable disposable)
                {
                    disposable._DisposeSafe();
                }
            }
        }
    }

    public class ThroughputMeasuse
    {
        public long BaseUnitMsecs { get; }
        public long InitialMinUnitMsecs { get; }
        public long StartTick { get; }
        public double CurrentThroughput => GetCurrentThroughput();

        readonly CriticalSection LockObj = new CriticalSection<ThroughputMeasuse>();

        public ThroughputMeasuse(int baseUnitMsecs = Consts.Intervals.DefaultThroughtputMeasutementUnitMsecs, int initialMinUnitMsecs = Consts.Intervals.DefaultThroughtputInitialMinMeasutementUnitMsecs)
        {
            if (baseUnitMsecs <= 0) baseUnitMsecs = Consts.Intervals.DefaultThroughtputMeasutementUnitMsecs;
            if (initialMinUnitMsecs <= 0) baseUnitMsecs = Consts.Intervals.DefaultThroughtputMeasutementUnitMsecs;

            this.BaseUnitMsecs = baseUnitMsecs;
            this.InitialMinUnitMsecs = initialMinUnitMsecs;
            this.StartTick = TickNow;
        }

        long LastCycle = 0;
        long LastTotal = 0;
        double CurrentThroughputInternal = 0.0;

        long AddFastCurrentValue = 0;
        int AddFastCurrentCount = 0;

        [MethodImpl(Inline)]
        public void AddFast(int amount, int countUnit = 1024)
        {
            Interlocked.Add(ref AddFastCurrentValue, Math.Max(amount, 0));
            if ((Interlocked.Increment(ref AddFastCurrentCount) % countUnit) == 0)
            {
                long v = Interlocked.Exchange(ref AddFastCurrentValue, 0);
                Add(v);
            }
        }

        public void Add(long amount)
        {
            amount = Math.Max(amount, 0);

            long now = TickNow;
            long pastTick = now - StartTick;
            long cycle = pastTick / BaseUnitMsecs;

            Interlocked.Add(ref LastTotal, amount);

            if ((cycle > LastCycle) || (cycle == 0 && LastCycle == 0))
            {
                lock (LockObj)
                {
                    if ((cycle >= 1) && (cycle > LastCycle))
                    {
                        long lastTotal = Interlocked.Exchange(ref LastTotal, 0);
                        //if (cycle != (LastCycle + 1))
                        //{
                        //    lastTotal = 0;
                        //}

                        CurrentThroughputInternal = (double)lastTotal / ((double)BaseUnitMsecs / 1000.0);
                        LastCycle = cycle;
                    }
                    else if (cycle == 0 && LastCycle == 0 && pastTick >= InitialMinUnitMsecs)
                    {
                        long lastTotal = Interlocked.Read(ref LastTotal);
                        CurrentThroughputInternal = (double)lastTotal / ((double)BaseUnitMsecs / 1000.0);
                    }
                }
            }
        }

        public double GetCurrentThroughput()
             => GetCurrentThroughput(out _);

        public double GetCurrentThroughput(out long currentCycle)
        {
            Add(0);

            long now = TickNow;
            long pastTick = now - StartTick;
            long cycle = pastTick / BaseUnitMsecs;

            currentCycle = cycle;

            lock (LockObj)
            {
                if (cycle == LastCycle)
                {
                    return CurrentThroughputInternal;
                }
                else
                {
                    return 0;
                }
            }
        }

        public static implicit operator double(ThroughputMeasuse m) => m.GetCurrentThroughput();

        public AsyncOneShotTester StartPrinter(string prefix = "", int intervalMsecs = Consts.Intervals.DefaultThroughtputMeasutementPrintMsecs, bool toStr3 = false)
        {
            if (intervalMsecs <= 0) intervalMsecs = 1;

            AsyncOneShotTester ret = new AsyncOneShotTester(async c =>
            {
                long lastCycle = -1;
                while (c.IsCancellationRequested == false)
                {
                    double value = this.GetCurrentThroughput(out long currentCycle);
                    if (currentCycle != lastCycle)
                    {
                        lastCycle = currentCycle;

                        string s;
                        if (toStr3 == false)
                        {
                            s = value.ToString("F3");
                        }
                        else
                        {
                            s = ((long)value)._ToString3();
                        }
                        $"{prefix}{s}"._Print();
                    }

                    await c._WaitUntilCanceledAsync(Util.GenRandInterval(intervalMsecs));
                }
            });

            return ret;
        }
    }

    [Flags]
    public enum VlanRangeStyle
    {
        Cisco = 0,
        Apresia = 1,
    }

    public class VlanRange
    {
        readonly Memory<bool> VlanArray = new bool[Consts.Numbers.VlanMax + 1];

        public VlanRange()
        {
        }

        public VlanRange(string rangeString)
        {
            Add(rangeString);
        }

        public void Add(string rangeString)
        {
            var span = VlanArray.Span;

            string[] tokens = rangeString._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ',', ';', ' ', '　', '\t');

            foreach (var token in tokens)
            {
                string[] tokens2 = token._Split(StringSplitOptions.TrimEntries, '-');
                if (tokens2.Length == 1)
                {
                    int number = tokens2[0]._ToInt();

                    if (number._IsValidVlanId())
                    {
                        span[number] = true;
                    }
                }
                else if (tokens2.Length == 2)
                {
                    int number1 = tokens2[0]._ToInt();
                    int number2 = tokens2[1]._ToInt();
                    int start = Math.Min(number1, number2);
                    int end = Math.Max(number1, number2);
                    if (start._IsValidVlanId() && end._IsValidVlanId())
                    {
                        for (int i = start; i <= end; i++)
                        {
                            span[i] = true;
                        }
                    }
                }
            }
        }

        public override string ToString() => ToString(VlanRangeStyle.Cisco);

        public string ToString(VlanRangeStyle style)
        {
            var span = VlanArray.Span;

            List<WPair2<int, int>> segments = new List<WPair2<int, int>>();

            WPair2<int, int>? current = null;

            for (int i = Consts.Numbers.VlanMin; i <= Consts.Numbers.VlanMax; i++)
            {
                if (span[i])
                {
                    if (current == null)
                    {
                        current = new WPair2<int, int>(i, i);
                        segments.Add(current);
                    }
                    else
                    {
                        current.B = i;
                    }
                }
                else
                {
                    if (current != null)
                    {
                        current = null;
                    }
                }
            }

            switch (style)
            {
                case VlanRangeStyle.Cisco:
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (var segment in segments)
                        {
                            string str;

                            if (segment.A == segment.B)
                            {
                                str = segment.A.ToString();
                            }
                            else
                            {
                                str = $"{segment.A}-{segment.B}";
                            }

                            sb.Append(str);

                            sb.Append(",");
                        }

                        return sb.ToString().TrimEnd(',');
                    }

                case VlanRangeStyle.Apresia:
                    {
                        StringWriter w = new StringWriter();
                        foreach (var segment in segments)
                        {
                            for (int i = segment.A; i <= segment.B; i++)
                            {
                                w.WriteLine($"vlan {i} name {i}");
                            }
                        }

                        return w.ToString();
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }
    }

    public static class SampleDataUtil
    {
        static readonly FastCache<string, object?> CacheData = new FastCache<string, object?>(int.MaxValue);

        public static object? Get(Type type, string additionalTag1 = "", string additionalTag2 = "", RefBool? isGoodSample = null)
        {
            if (Dbg.IsPrimitiveType(type)) return null;

            additionalTag1 = additionalTag1._NonNullTrim();
            if (additionalTag1.StartsWith("_"))
            {
                additionalTag1 = additionalTag1.Substring(1);
            }

            additionalTag2 = additionalTag1._NonNullTrim();
            if (additionalTag2.StartsWith("_"))
            {
                additionalTag2 = additionalTag2.Substring(1);
            }

            string cacheTag = type.FullName + "@" + additionalTag1 + "@" + additionalTag2;

            return CacheData.GetOrCreate(cacheTag, t =>
            {
                try
                {
                    if (type.IsArray)
                    {
                        var arrayItemType = type.GetElementType()!;

                        object? arrayItemSample = SampleDataUtil.Get(arrayItemType, additionalTag1, additionalTag2, isGoodSample);

                        int count = 3;

                        var sampleArray = Array.CreateInstance(arrayItemType, count);
                        for (int i = 0; i < count; i++)
                        {
                            sampleArray.SetValue(arrayItemSample, i);
                        }

                        return sampleArray;
                    }

                    PropertyInfo? method = null;

                    if (additionalTag1._IsFilled())
                    {
                        additionalTag1 = "_" + additionalTag1;

                        if (method == null) method = type.GetProperty("_SampleData" + additionalTag1, BindingFlags.Static | BindingFlags.Public);
                        if (method == null) method = type.GetProperty("_Sample" + additionalTag1, BindingFlags.Static | BindingFlags.Public);
                        if (method == null) method = type.GetProperty("_GetSample" + additionalTag1, BindingFlags.Static | BindingFlags.Public);
                        if (method == null) method = type.GetProperty("_GetSampleData" + additionalTag1, BindingFlags.Static | BindingFlags.Public);

                        if (additionalTag2._IsFilled())
                        {
                            additionalTag2 = "_" + additionalTag2;

                            if (method == null) method = type.GetProperty("_SampleData" + additionalTag1 + additionalTag2, BindingFlags.Static | BindingFlags.Public);
                            if (method == null) method = type.GetProperty("_Sample" + additionalTag1 + additionalTag2, BindingFlags.Static | BindingFlags.Public);
                            if (method == null) method = type.GetProperty("_GetSample" + additionalTag1 + additionalTag2, BindingFlags.Static | BindingFlags.Public);
                            if (method == null) method = type.GetProperty("_GetSampleData" + additionalTag1 + additionalTag2, BindingFlags.Static | BindingFlags.Public);
                        }
                    }

                    if (method == null) method = type.GetProperty("_SampleData", BindingFlags.Static | BindingFlags.Public);
                    if (method == null) method = type.GetProperty("_Sample", BindingFlags.Static | BindingFlags.Public);
                    if (method == null) method = type.GetProperty("_GetSample", BindingFlags.Static | BindingFlags.Public);
                    if (method == null) method = type.GetProperty("_GetSampleData", BindingFlags.Static | BindingFlags.Public);

                    if (method != null)
                    {
                        object? ret = method.GetValue(null);

                        if (ret != null)
                        {
                            if (ret is INormalizable n) n._TryNormalize();
                            isGoodSample?.Set(true);
                            return ret;
                        }
                    }
                }
                catch { }

                try
                {
                    return Util.CreateNewSampleObjectFromTypeWithoutParam(type);
                }
                catch { }
                return null;
            });
        }
    }


    public class WalkObjectItem
    {
        public object? Object { get; init; }
        public string Name { get; init; } = "";
        public MemberInfo? MemberInfo { get; init; } = null;
        public object? Data { get; init; }
    }


    public static class EmptyEnumerable<T>
    {
        public static IEnumerable<T> Empty { get; } = new List<T>();
    }

    public class CoresLibException : CoresException
    {
        [MethodImpl(NoInline | NoOptimization)]
        public CoresLibException(string? message = "", [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
            : base(GenerateMessage(message, filename, line, caller))
        {
        }

        [MethodImpl(NoInline | NoOptimization)]
        public CoresLibException(string? message, Exception? innerException, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
            : base(GenerateMessage(message, filename, line, caller), innerException)
        {
        }

        [MethodImpl(NoInline | NoOptimization)]
        static string GenerateMessage(string? srcMessage, string filename, int line, string? caller)
        {
            StackTrace stackTrace = new StackTrace(1, false);
            Type? type = stackTrace?.GetFrame(1)?.GetMethod()?.DeclaringType;

            string className = type?.Name ?? "UnknownClass";
            string functionName = caller._FilledOrDefault("UnknownFunction");
            if (srcMessage._IsEmpty()) srcMessage = nameof(CoresLibException);

            string fileInfo = $"{Path.GetFileName(filename)}:{line}";

            return $"{className} - {functionName}() <{fileInfo}>: {srcMessage}";
        }
    }

    public class CoresException : ApplicationException
    {
        public CoresException()
        {
        }

        public CoresException(string? message) : base(message)
        {
        }

        public CoresException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    public class CoresEmptyException : ArgumentNullException
    {
        public CoresEmptyException()
        {
        }

        public CoresEmptyException(string? paramName) : base(paramName)
        {
        }

        public CoresEmptyException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public CoresEmptyException(string? paramName, string? message) : base(paramName, message)
        {
        }
    }

    public class None { }

    public class StrKeyValueItem
    {
        public string Key { get; }
        public string Value { get; }

        public StrKeyValueItem(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

    [Flags]
    public enum EnsureCtor
    {
        Yes = 0,
    }

    [Flags]
    public enum EnsureCopy
    {
        Yes = 0,
    }

    [Flags]
    public enum EnsureSpecial
    {
        Yes = 0,
    }

    [Flags]
    public enum EnsureInternal
    {
        Yes = 0,
    }

    [Flags]
    public enum EnsureOk
    {
        Ok = 0,
    }

    [Flags]
    public enum EnsureError
    {
        Error = 0,
    }

    [Flags]
    public enum EnsurePresentInterface
    {
        Yes = 0,
    }

    [Flags]
    public enum Direction
    {
        Send = 0,
        Recv,
    }
}


[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SimpleCommentAttribute : Attribute
{
    public string Comment { get; }

    public SimpleCommentAttribute(string comment)
    {
        this.Comment = comment._NonNullTrim();
    }
}

public class ShouldWarn
{
    public int IntervalMsecs { get; }
    public bool RandInterval { get; }
    public double PlusMinusPercentage { get; }

    long NextTick = 0;

    public ShouldWarn(int intervalMsecs, bool randInterval = false, double plusMinusPercentage = 30)
    {
        this.IntervalMsecs = intervalMsecs;
        this.RandInterval = randInterval;
        this.PlusMinusPercentage = plusMinusPercentage;

        this.NextTick = 0;
    }

    public bool IsGoodTimeToWarn()
    {
        long now = Time.Tick64;
        bool ret = false;

        if (now >= this.NextTick || this.NextTick == 0)
        {
            ret = true;

            this.NextTick = now + Util.GenRandInterval(this.IntervalMsecs, this.PlusMinusPercentage);
        }

        return ret;
    }
}

public interface IEndlessQueue<T>
{
    public T Dequeue();
    public T Peek();
    public int Count { get; }
    public int NumRotate { get; }
}

public class ShuffledEndlessQueue<T> : IEndlessQueue<T>
{
    readonly List<T> AllItemsList;
    readonly Queue<T> CurrentQueue = new Queue<T>();
    readonly int RandContinueCount = 0;

    public int NumRotate { get; private set; } = -1;

    public bool AvoidLastSame { get; }

    public int Count => (this.AllItemsList.Count == 0 ? 0 : int.MaxValue);

    public int RealCount => this.AllItemsList.Count;

    T? LastOne = default;

    public ShuffledEndlessQueue(IEnumerable<T> elements, int randContinueCount = 0, bool avoidLastSame = false)
    {
        this.RandContinueCount = randContinueCount;
        this.AllItemsList = new List<T>(elements.ToArray());
        this.AvoidLastSame = avoidLastSame;

        if (this.AllItemsList.Any() == false)
        {
            throw new CoresLibException("elements is empty.");
        }
    }

    public T Dequeue()
    {
        if (this.CurrentQueue.Count == 0)
        {
            T[] shuffled = null!;

            if (AvoidLastSame == false)
            {
                shuffled = this.AllItemsList.ToArray()._Shuffle().ToArray();
            }
            else
            {
                for (int i = 0; i < 16; i++)
                {
                    // できるだけ直前のリピートを避ける
                    shuffled = this.AllItemsList.ToArray()._Shuffle().ToArray();

                    if (LastOne == null || shuffled.Length <= 0 || shuffled[0] == null || shuffled[0]!.Equals(LastOne) == false)
                    {
                        break;
                    }
                }
            }

            if (shuffled.Length >= 1)
            {
                this.LastOne = shuffled.LastOrDefault();
            }

            if (RandContinueCount <= 1)
            {
                foreach (var item in shuffled)
                {
                    this.CurrentQueue.Enqueue(item);
                }
            }
            else
            {
                foreach (var item in shuffled)
                {
                    int count = (Secure.RandSInt31() % RandContinueCount) + 1;

                    for (int j = 0; j < count; j++)
                    {
                        this.CurrentQueue.Enqueue(item);
                    }
                }
            }

            if (shuffled.Length >= 1)
            {
                NumRotate++;
            }
        }

        var ret = this.CurrentQueue.Dequeue();

        return ret;
    }

    public T Peek()
    {
        if (this.CurrentQueue.Count == 0)
        {
            T[] shuffled = null!;

            if (AvoidLastSame == false)
            {
                shuffled = this.AllItemsList.ToArray()._Shuffle().ToArray();
            }
            else
            {
                for (int i = 0; i < 16; i++)
                {
                    // できるだけ直前のリピートを避ける
                    shuffled = this.AllItemsList.ToArray()._Shuffle().ToArray();

                    if (LastOne == null || shuffled.Length <= 0 || shuffled[0] == null || shuffled[0]!.Equals(LastOne) == false)
                    {
                        break;
                    }
                }
            }

            if (shuffled.Length >= 1)
            {
                this.LastOne = shuffled.LastOrDefault();
            }

            if (RandContinueCount <= 1)
            {
                foreach (var item in shuffled)
                {
                    this.CurrentQueue.Enqueue(item);
                }
            }
            else
            {
                foreach (var item in shuffled)
                {
                    int count = (Secure.RandSInt31() % RandContinueCount) + 1;

                    for (int j = 0; j < count; j++)
                    {
                        this.CurrentQueue.Enqueue(item);
                    }
                }
            }

            if (shuffled.Length >= 1)
            {
                NumRotate++;
            }
        }

        return this.CurrentQueue.Peek();
    }
}

public class EndlessQueue<T> : IEndlessQueue<T>
{
    readonly List<T> AllItemsList;
    readonly Queue<T> CurrentQueue = new Queue<T>();

    public int NumRotate { get; private set; } = -1;

    public int Count => (this.AllItemsList.Count == 0 ? 0 : int.MaxValue);

    public EndlessQueue(IEnumerable<T> elements)
    {
        this.AllItemsList = new List<T>(elements.ToArray());
    }

    public T Dequeue()
    {
        if (this.CurrentQueue.Count == 0)
        {
            var tmp = this.AllItemsList.ToArray().ToArray();

            foreach (var item in tmp)
            {
                this.CurrentQueue.Enqueue(item);
            }

            if (tmp.Length >= 1)
            {
                NumRotate++;
            }
        }

        return this.CurrentQueue.Dequeue();
    }

    public T Peek()
    {
        if (this.CurrentQueue.Count == 0)
        {
            var tmp = this.AllItemsList.ToArray().ToArray();

            foreach (var item in tmp)
            {
                this.CurrentQueue.Enqueue(item);
            }

            if (tmp.Length >= 1)
            {
                NumRotate++;
            }
        }

        return this.CurrentQueue.Peek();
    }
}

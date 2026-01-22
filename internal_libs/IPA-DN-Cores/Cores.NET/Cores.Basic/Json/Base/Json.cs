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

#if CORES_BASIC_JSON

using System.Diagnostics.CodeAnalysis;

using System;
using System.Threading.Tasks;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Newtonsoft.Json.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

[Flags]
public enum JsonFlags : ulong
{
    None = 0,
    AllEnumToStr = 1,
}

public static class Json
{
    public const int DefaultMaxDepth = 8;

    public class InternalTestClass
    {
        public int A;
        public string B = "";
    }

    public static void RunStartupTest()
    {
        try
        {
            InternalTestClass c = new InternalTestClass();

            c.A = 123;
            c.B = "Hello";

            string s = c._ObjectToJson();

            if (s._InStri("Hello") == false)
            {
                throw new CoresException("s._InStri(\"Hello\") == false");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Json.RunTest Fatal Error: " + ex.ToString());
            ThreadObj.Sleep(1000);
            Environment.Exit(-1);
        }
    }

    public static string SerializeLog(IEnumerable itemArray, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None)
    {
        StringWriter w = new StringWriter();
        SerializeLogToTextWriterAsync(w, itemArray, includeNull, escapeHtml, maxDepth, jsonFlags: jsonFlags)._GetResult();
        return w.ToString();
    }

    public static async Task SerializeLogToTextWriterAsync(TextWriter w, IEnumerable itemArray, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        foreach (var item in itemArray)
        {
            await w.WriteLineAsync(Serialize(item, includeNull, escapeHtml, maxDepth, true, type: type, jsonFlags: jsonFlags));
        }
    }

    public class InterfaceContractResolver : DefaultContractResolver
    {
        private readonly Type[] _interfaceTypes;

        private readonly ConcurrentDictionary<Type, Type> _typeToSerializeMap;

        public InterfaceContractResolver(params Type[] interfaceTypes)
        {
            _interfaceTypes = interfaceTypes;

            _typeToSerializeMap = new ConcurrentDictionary<Type, Type>();
        }

        protected override IList<JsonProperty> CreateProperties(
            Type type,
            MemberSerialization memberSerialization)
        {
            var typeToSerialize = _typeToSerializeMap.GetOrAdd(
                type,
                t => _interfaceTypes.FirstOrDefault(
                    it => it.IsAssignableFrom(t)) ?? t);

            return base.CreateProperties(typeToSerialize, memberSerialization);
        }
    }

    public class ClassWithIToJsonJsonConverter : JsonConverter
    {
        public static readonly ClassWithIToJsonJsonConverter Singleton = new ClassWithIToJsonJsonConverter();

        public override bool CanConvert(Type objectType) => objectType._HasInterface(typeof(IToJsonString));

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            IToJsonString i = (IToJsonString)value!;

            var jobject = i.ToJsonString()._JsonToObject<JObject>();

            if (jobject != null)
            {
                jobject.WriteTo(writer);
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    public class IPAddressJsonConverter : JsonConverter
    {
        public static readonly IPAddressJsonConverter Singleton = new IPAddressJsonConverter();

        public override bool CanConvert(Type objectType) => objectType == typeof(IPAddress);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => writer.WriteValue(value!.ToString());

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            => IPAddress.Parse((string)reader.Value!);
    }

    public class IPEndPointJsonConverter : JsonConverter
    {
        public static readonly IPEndPointJsonConverter Singleton = new IPEndPointJsonConverter();

        public override bool CanConvert(Type objectType) => objectType == typeof(IPEndPoint);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => writer.WriteValue(value!.ToString());

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            => IPEndPoint.Parse((string)reader.Value!);
    }

    static readonly StringEnumConverter JsonNet_StringEnumConverter = new StringEnumConverter();

    public static void AddStandardSettingsToJsonConverter(JsonSerializerSettings settings, JsonFlags flags = JsonFlags.None, IList<JsonConverter>? converters = null)
    {
        settings.Converters.Add(IPAddressJsonConverter.Singleton);
        settings.Converters.Add(IPEndPointJsonConverter.Singleton);
        settings.Converters.Add(ClassWithIToJsonJsonConverter.Singleton);

        if (flags.Bit(JsonFlags.AllEnumToStr))
        {
            settings.Converters.Add(JsonNet_StringEnumConverter);
        }

        if (converters != null)
        {
            foreach (var c in converters)
            {
                settings.Converters.Add(c);
            }
        }
    }

    public static string Serialize(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        //if (obj is IToJsonString specialInterface)
        //{
        //    return specialInterface.ToJsonString(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);
        //}

        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
            StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
            Formatting = compact ? Formatting.None : Formatting.Indented,
        };

        AddStandardSettingsToJsonConverter(setting, jsonFlags, null);

        if (type != null)
        {
            setting.ContractResolver = new InterfaceContractResolver(type);
        }

        string ret = JsonConvert.SerializeObject(obj, compact ? Formatting.None : Formatting.Indented, setting);

        if (compact == false)
        {
            ret += Str.CrLf_Str;
        }

        if (base64url)
        {
            ret = ret._GetBytes_UTF8()._Base64UrlEncode();
        }

        return ret;
    }

    public static byte[] GetDigest(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = true, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        using HashCalcStream hashStream = new HashCalcStream(SHA1.Create());
        using StreamWriter writer = new StreamWriter(hashStream, System.Text.Encoding.UTF8); // ここでは必ず  System.Text.Encoding.UTF8 を作成する (互換性の問題。挙動変更しないため)

        Serialize(writer, obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type, jsonFlags);

        writer.Flush();

        return hashStream.GetFinalHash();
    }

    public static void Serialize(TextWriter destTextWriter, object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        //if (obj is IToJsonString specialInterface)
        //{
        //    string tmp = specialInterface.ToJsonString(includeNull, escapeHtml, maxDepth, compact, referenceHandling, false, type);
        //    destTextWriter.Write(tmp);
        //    return;
        //}

        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
            StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
            Formatting = compact ? Formatting.None : Formatting.Indented,
        };

        AddStandardSettingsToJsonConverter(setting, jsonFlags, null);

        if (type != null)
        {
            setting.ContractResolver = new InterfaceContractResolver(type);
        }

        JsonSerializer.Create(setting).Serialize(destTextWriter, obj);

        if (compact == false)
        {
            destTextWriter.Write(Str.CrLf_Str);
        }
    }

    public static JsonSerializer CreateSerializer(bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
    {
        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
            StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
            Formatting = compact ? Formatting.None : Formatting.Indented,
        };

        AddStandardSettingsToJsonConverter(setting, jsonFlags, null);

        return JsonSerializer.Create(setting);
    }

    [return: NotNullIfNotNull("obj")]
    public static T CloneWithJson<T>([AllowNull] T obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        return (T)CloneObjectWithJson((object?)obj, escapeHtml, maxDepth, referenceHandling, type ?? typeof(T), jsonFlags)!;
    }

    [return: NotNullIfNotNull("obj")]
    public static object? CloneObjectWithJson(object? obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        if (obj == null) return null;

        type = type ?? obj.GetType();

        string data = Serialize(obj, true, escapeHtml, maxDepth, true, referenceHandling, false, type, jsonFlags);

        object? ret = Deserialize(data, type, true, maxDepth, false, jsonFlags);

        ret._NullCheck();

        return ret;
    }

    [return: MaybeNull]
    public static T Deserialize<T>(string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
        => (T)Deserialize(str, typeof(T), includeNull, maxDepth, base64url, jsonFlags, converters)!;

    public static object? Deserialize(string str, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
    {
        if (base64url)
        {
            str = str._Base64UrlDecode()._GetString_UTF8();
        }

        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
        };

        AddStandardSettingsToJsonConverter(setting, jsonFlags, converters);

        return JsonConvert.DeserializeObject(str, type, setting);
    }

    public static object? Deserialize(TextReader srcTextReader, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
    {
        JsonSerializerSettings setting = new JsonSerializerSettings()
        {
            MaxDepth = maxDepth,
            NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
        };

        AddStandardSettingsToJsonConverter(setting, jsonFlags, converters);

        return JsonSerializer.Create(setting).Deserialize(srcTextReader, type);
    }

    public static async Task<bool> IsJsonTextAsync(TextReader srcTextReader)
    {
        try
        {
            JsonTextReader reader = new JsonTextReader(srcTextReader);

            while (await reader.ReadAsync())
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    [return: MaybeNull]
    public static T Deserialize<T>(TextReader srcTextReader, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None, IList<JsonConverter>? converters = null)
        => (T)Deserialize(srcTextReader, typeof(T), includeNull, maxDepth, jsonFlags, converters)!;

    [return: MaybeNull]
    public static T ConvertObject<T>(object? src, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
        => (T)ConvertObject(src, typeof(T), includeNull, maxDepth, referenceHandling, jsonFlags)!;

    public static object? ConvertObject(object? src, Type destType, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, JsonFlags jsonFlags = JsonFlags.None)
    {
        string str = Serialize(src, includeNull, false, maxDepth, true, referenceHandling, jsonFlags: jsonFlags);
        return Deserialize(str, destType, maxDepth: maxDepth, jsonFlags: jsonFlags);
    }

    public static async Task<bool> DeserializeLargeArrayAsync<T>(TextReader txt, Func<T?, bool> itemReadCallback,
        Func<string, Exception, bool>? parseErrorCallback = null, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, JsonFlags jsonFlags = JsonFlags.None)
        where T : class
    {
        while (true)
        {
            string? line = await txt.ReadLineAsync();
            if (line == null)
            {
                return true;
            }
            if (line._IsFilled())
            {
                object? obj = null;
                try
                {
                    obj = (object?)Deserialize<T>(line, includeNull, maxDepth, jsonFlags: jsonFlags);
                }
                catch (Exception ex)
                {
                    if (parseErrorCallback != null && parseErrorCallback(line, ex) == false)
                    {
                        return false;
                    }
                }

                if (itemReadCallback((T?)obj) == false)
                {
                    return false;
                }
            }
        }
    }

    public static string SerializeJObject(JObject o)
    {
        return o.ToString();
    }

    public static string SerializeDynamic(dynamic d)
    {
        JObject o = (JObject)d;

        return o.ToString();
    }

    public static dynamic? DeserializeDynamic(string str)
    {
        dynamic? ret = JObject.Parse(str);
        return ret;
    }

    public static JObject DeserializeJObject(string str)
    {
        JObject ret = JObject.Parse(str);
        return ret;
    }

    public static dynamic NewDynamicObject()
    {
        JObject o = new JObject();

        return o;
    }

    public static JObject NewJsonObject()
    {
        return new JObject();
    }

    public static string Normalize(string str)
    {
        dynamic? d = DeserializeDynamic(str);

        return SerializeDynamic(d);
    }

    public static string NormalizeArray(string str, int? maxDepth = 8)
    {
        JArray a = JArray.Parse(str);

        return a._ObjectToJson(maxDepth: maxDepth);
    }
}

public interface IToJsonString
{
    string ToJsonString(bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None);
}

public class EasyJsonStrAttributes
    : ICollection<KeyValuePair<string, string>>, IEnumerable<KeyValuePair<string, string>>, IEnumerable, IDictionary<string, string>, IReadOnlyCollection<KeyValuePair<string, string>>, IReadOnlyDictionary<string, string>, ICollection, IDictionary, IToJsonString
{
    public readonly SortedDictionary<string, string> Dict;

    public EasyJsonStrAttributes(string? text = null, IComparer<string>? comparer = null)
    {
        comparer = comparer ?? StrComparer.IgnoreCaseTrimComparer;

        this.Dict = new SortedDictionary<string, string>(comparer);

        if (text._IsFilled())
        {
            JObject? obj = text._JsonToObject<JObject>();

            if (obj != null)
            {
                foreach (var item in obj)
                {
                    string name = item.Key._NonNullTrim();
                    string value = item.Value!.ToString()._NonNullTrim();

                    Dict.TryAdd(name, value);
                }
            }
        }
    }

    public EasyJsonStrAttributes(JObject? obj, IComparer<string>? comparer = null)
    {
        comparer = comparer ?? StrComparer.IgnoreCaseTrimComparer;

        this.Dict = new SortedDictionary<string, string>(comparer);

        if (obj != null)
        {
            foreach (var item in obj)
            {
                string name = item.Key._NonNullTrim();
                string value = item.Value!.ToString()._NonNullTrim();

                Dict.TryAdd(name, value);
            }
        }
    }

    public static implicit operator string(EasyJsonStrAttributes attributesObj) => attributesObj.ToJsonString();
    public static implicit operator EasyJsonStrAttributes(string? str) => new EasyJsonStrAttributes(str);

    public override string ToString() => ToJsonString();

    public static JObject NormalizeJsonObject(JObject? src, IComparer<string>? comparer = null)
    {
        var tmp = new EasyJsonStrAttributes(src, comparer);

        return tmp.ToJsonObject();
    }

    public JObject ToJsonObject()
    {
        var json = Json.NewJsonObject();

        foreach (var item in this.Dict)
        {
            json.Add(item.Key, JToken.FromObject(item.Value));
        }

        return json;
    }

    public string ToJsonString(bool includeNull = false, bool escapeHtml = false, int? maxDepth = 8, bool compact = true, bool referenceHandling = false, bool base64url = false, Type? type = null, JsonFlags jsonFlags = JsonFlags.None)
    {
        if (this.Dict.Count == 0) return "";

        return ToJsonObject()._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type, jsonFlags);
    }

    public void Set(string key, object? value)
    {
        string tmp = "";
        if (value is double d)
        {
            tmp = d.ToString("F3");
        }
        else
        {
            tmp = value?.ToString() ?? "";
        }

        this.Set(key, tmp);
    }

    public void Set(string key, string? value)
    {
        value = value._NonNull();

        this[key] = value;
    }

    public string this[string key]
    {
        get => this.Dict._GetOrDefault(key._NonNullTrim(), "")._NonNullTrim();
        set
        {
            if (value._IsEmpty())
            {
                this.Dict.Remove(key._NonNullTrim());
            }
            else
            {
                this.Dict[key._NonNullTrim()] = value._NonNullTrim();
            }
        }
    }

    public object? this[object key]
    {
        get => this.Dict._GetOrDefault(key?.ToString()._NonNullTrim() ?? "", "")._NonNullTrim();

        set
        {
            if (value == null || value.ToString()._IsEmpty())
            {
                this.Dict.Remove(key?.ToString()._NonNullTrim() ?? "");
            }
            else
            {
                this.Dict[key?.ToString()._NonNullTrim() ?? ""] = value!.ToString()._NonNullTrim() ?? "";
            }
        }
    }

    public int Count => Dict.Count;

    public bool IsReadOnly => false;

    public ICollection<string> Keys => Dict.Keys;

    public ICollection<string> Values => Dict.Values;

    public bool IsSynchronized => true;

    readonly CriticalSection LockObj = new CriticalSection<EasyJsonStrAttributes>();
    public object SyncRoot => LockObj;

    public bool IsFixedSize => false;

    IEnumerable<string> IReadOnlyDictionary<string, string>.Keys => Dict.Keys;

    ICollection IDictionary.Keys => Dict.Keys;

    IEnumerable<string> IReadOnlyDictionary<string, string>.Values => Dict.Values;

    ICollection IDictionary.Values => Dict.Values;

    public void Add(KeyValuePair<string, string> item) => this[item.Key] = item.Value;

    public void Add(string key, string value) => this[key] = value;

    public void Add(object key, object? value) => this[key] = value;

    public void Clear() => Dict.Clear();

    public bool Contains(KeyValuePair<string, string> item)
    {
        string key = item.Key._NonNullTrim();
        string value = item.Value._NonNullTrim();
        if (value._IsEmpty()) return false;

        return this.Dict.Where(x => this.Dict.Comparer.Compare(key, x.Key) == 0 && value == x.Value).Any();
    }

    public bool Contains(object key)
    {
        string keystr = key!.ToString()._NonNullTrim() ?? "";
        return this.Dict.Keys.Where(x => this.Dict.Comparer.Compare(keystr, x) == 0).Any();
    }

    public bool ContainsKey(string key)
    {
        key = key._NonNullTrim();
        return this.Dict.Keys.Where(x => this.Dict.Comparer.Compare(key, x) == 0).Any();
    }

    public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        => Dict.CopyTo(array, arrayIndex);

    public void CopyTo(Array array, int index)
        => throw new NotImplementedException();

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        => this.Dict.GetEnumerator();

    public bool Remove(KeyValuePair<string, string> item)
        => throw new NotImplementedException();

    public bool Remove(string key)
        => this.Dict.Remove(key._NonNullTrim());

    public void Remove(object key)
        => this.Dict.Remove(key?.ToString()._NonNullTrim() ?? "");

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
    {
        bool ret = this.Dict.TryGetValue(key._NonNullTrim(), out value);

        value = value._NonNullTrim();

        return ret;
    }

    IEnumerator IEnumerable.GetEnumerator()
        => this.Dict.GetEnumerator();

    IDictionaryEnumerator IDictionary.GetEnumerator()
        => this.Dict.GetEnumerator();
}

public static partial class Dbg
{
    static partial void InternalConvertToJsonStringIfPossible(ref string? ret, object obj, bool includeNull, bool escapeHtml, int? maxDepth, bool compact, bool referenceHandling, JsonFlags jsonFlags)
    {
        ret = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, jsonFlags: jsonFlags);
    }

    static partial void InternalIsJsonSupported(ref bool ret)
    {
        ret = true;
    }
}

#endif // CORES_BASIC_JSON

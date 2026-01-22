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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Net;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class JsonRpcServerSettings
    {
        public static readonly Copenhagen<int> DefaultMaxRequestBodyLen = 100 * 1024 * 1024; // 100 MB
    }
}

public class JsonRpcException : Exception
{
    public JsonRpcError RpcError { get; }
    public JsonRpcException(JsonRpcError err)
        : base($"Code={err.Code}, Message={err.Message._NonNull()}" +
              (err == null || err.Data == null ? "" : $", Data={err.Data._ObjectToJson(compact: true)}"))
    {
        this.RpcError = err!;
    }
}


public class JsonRpcRequestForHelp
{
    [JsonProperty("jsonrpc")]
    public string? Version { get; set; } = "2.0";

    [JsonProperty("method")]
    public string? Method { get; set; } = "";

    [JsonProperty("params")]
    public object? Params { get; set; } = null;

    //[JsonProperty("id")]
    //public string? Id { get; set; } = null;
}

public class JsonRpcResponseForHelp_Ok
{
    [JsonProperty("jsonrpc")]
    public string? Version { get; set; } = "2.0";

    [JsonProperty("result")]
    public object? Result { get; set; } = null;

    //[JsonProperty("error")]
    //public JsonRpcError? Error { get; set; } = null;

    //[JsonProperty("id", NullValueHandling = NullValueHandling.Include)]
    //public virtual string? Id { get; set; } = null;
}

public class JsonRpcResponseForHelp_Error
{
    [JsonProperty("jsonrpc")]
    public string? Version { get; set; } = "2.0";

    //[JsonProperty("result")]
    //public object? Result { get; set; } = null;

    [JsonProperty("error")]
    public JsonRpcError? Error { get; set; } = null;

    //[JsonProperty("id", NullValueHandling = NullValueHandling.Include)]
    //public virtual string? Id { get; set; } = null;
}


public class JsonRpcRequest
{
    [JsonProperty("jsonrpc")]
    public string? Version { get; set; } = "2.0";

    [JsonProperty("method")]
    public string? Method { get; set; } = "";

    [JsonProperty("params")]
    public object? Params { get; set; } = null;

    [JsonProperty("id")]
    public string? Id { get; set; } = null;

    public JsonRpcRequest() { }

    public JsonRpcRequest(string method, object param, string id)
    {
        this.Method = method;
        this.Params = param;
        this.Id = id;
    }
}

public class JsonRpcResponse<TResult> : JsonRpcResponse
    where TResult : class
{
    [JsonIgnore]
    public TResult? ResultData
    {
        get => Result == null ? null : base.Result._ConvertJsonObject<TResult>();
        set => Result = value;
    }
}

public class JsonRpcResponseOk : JsonRpcResponse
{
    [JsonIgnore]
    public override JsonRpcError? Error { get => null; set { } }

    [JsonProperty("result", NullValueHandling = NullValueHandling.Include)]
    public override object? Result { get; set; } = null;
}

public class JsonRpcResponseError : JsonRpcResponse
{
    [JsonIgnore]
    public override object? Result { get => null; set { } }

    [JsonProperty("error", NullValueHandling = NullValueHandling.Include)]
    public override JsonRpcError? Error { get; set; } = null;
}

public class JsonRpcResponse
{
    [JsonProperty("jsonrpc")]
    public virtual string? Version { get; set; } = "2.0";

    [JsonProperty("result")]
    public virtual object? Result { get; set; } = null;

    [JsonProperty("error")]
    public virtual JsonRpcError? Error { get; set; } = null;

    [JsonProperty("id", NullValueHandling = NullValueHandling.Include)]
    public virtual string? Id { get; set; } = null;

    [JsonIgnore]
    public virtual bool IsError => this.Error != null;

    [JsonIgnore]
    public virtual bool IsOk => !IsError;

    public virtual void ThrowIfError()
    {
        if (this.IsError) throw new JsonRpcException(this.Error!);
    }

    public override string ToString()
    {
        return this._ObjectToJson(compact: true);
    }
}

public class JsonRpcError
{
    public JsonRpcError() { }
    public JsonRpcError(int code, string message, object? data = null)
    {
        this.Code = code;
        this.Message = message._NonNull();
        if (this.Message._IsEmpty()) this.Message = $"JSON-RPC Error {code}";
        this.Data = data;
    }

    [JsonProperty("code")]
    public int Code { get; set; } = 0;

    [JsonProperty("message")]
    public string? Message { get; set; } = null;

    [JsonProperty("data")]
    public object? Data { get; set; } = null;
}

[AttributeUsage(AttributeTargets.Interface)]
public class RpcInterfaceAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class RpcRequireAuthAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class RpcMethodHelpAttribute : Attribute
{
    public string Description { get; }
    public object? SampleReturnValueIfPrimitive { get; }

    public RpcMethodHelpAttribute(string description, object? sampleReturnValueIfPrimitive = null)
    {
        this.Description = description._NonNullTrim();
        this.SampleReturnValueIfPrimitive = sampleReturnValueIfPrimitive;
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public class RpcParamHelpAttribute : Attribute
{
    public string Description { get; }
    public object? SampleValueIfPrimitive { get; }

    public RpcParamHelpAttribute(string description, object? sampleValueIfPrimitive = null)
    {
        this.Description = description._NonNullTrim();
        this.SampleValueIfPrimitive = sampleValueIfPrimitive;
    }
}

public class RpcParameterHelp
{
    public string Name { get; }
    public Type Type { get; }
    public string TypeName { get; }
    public bool IsPrimitiveType { get; }
    public bool IsEnumType { get; }
    public string Description { get; } = "";
    public object? SampleValueObject { get; }
    public string SampleValueOneLineStr { get; } = "";
    public string SampleValueMultiLineStr { get; } = "";
    public bool Mandatory { get; }
    public object? DefaultValue { get; }
    public SortedDictionary<long, string>? EnumValuesList { get; }

    public RpcParameterHelp(ParameterInfo info, string methodNameHint)
    {
        this.Name = info.Name._NonNull();
        this.Type = info.ParameterType;
        this.TypeName = this.Type.Name;
        this.IsPrimitiveType = Dbg.IsPrimitiveType(this.Type);
        this.SampleValueObject = SampleDataUtil.Get(this.Type, methodNameHint);

        if (this.Type.IsEnum)
        {
            this.IsEnumType = true;

            this.EnumValuesList = new SortedDictionary<long, string>();
            foreach (var kv in Str.GetEnumValuesList(this.Type))
            {
                this.EnumValuesList.Add(Convert.ToInt64(kv.Value), kv.Key);
            }
        }

        var attr = info.GetCustomAttribute<RpcParamHelpAttribute>();
        if (attr != null)
        {
            this.Description = attr.Description;
            if (this.IsPrimitiveType && attr.SampleValueIfPrimitive != null)
            {
                this.SampleValueObject = attr.SampleValueIfPrimitive;
            }
            else if (this.Type == typeof(JObject) && attr.SampleValueIfPrimitive != null && attr.SampleValueIfPrimitive.GetType() == typeof(string))
            {
                this.SampleValueObject = ((string)attr.SampleValueIfPrimitive)._JsonToJsonObject();
            }
        }

        if (this.SampleValueObject == null)
        {
            this.SampleValueObject = Util.CreateNewSampleObjectFromTypeWithoutParam(this.Type);
        }

        JsonFlags jsonFlags = JsonFlags.None;

        if (this.IsEnumType) jsonFlags |= JsonFlags.AllEnumToStr;

        this.SampleValueOneLineStr = this.SampleValueObject?._ObjectToJson(includeNull: true, compact: true, jsonFlags: jsonFlags) ?? "";

        this.SampleValueMultiLineStr = this.SampleValueObject?._ObjectToJson(includeNull: true, compact: false, jsonFlags: jsonFlags) ?? "";

        if (info.HasDefaultValue == false)
        {
            this.Mandatory = true;
        }
        else
        {
            this.Mandatory = false;
            this.DefaultValue = info.DefaultValue;
        }
    }
}

public class RpcMethodInfo
{
    public string Name { get; }
    public MethodInfo Method { get; }
    public ParameterInfo[] ParametersByIndex { get; }
    public ParameterInfo ReturnParameter { get; }
    public bool IsTask { get; }
    public Type? TaskType { get; }
    public bool IsGenericTask { get; }
    public Type? GeneticTaskType { get; }
    public List<RpcParameterHelp> _ParametersHelpList = new List<RpcParameterHelp>();
    public IReadOnlyList<RpcParameterHelp> ParametersHelpList => _ParametersHelpList;
    public bool HasRetValue { get; }
    public Type? RetValueType => this.GeneticTaskType;
    public string RetValueTypeName => this.RetValueType?.Name ?? "void";
    public string Description { get; } = "";
    public object? RetValueSampleValueObject { get; }
    public string RetValueSampleValueJsonMultilineStr { get; } = "";
    public bool IsRetValuePrimitiveType { get; }
    public bool RequireAuth { get; }

    public RpcMethodInfo(IEnumerable<Type> targetClassList, string methodName)
    {
        MethodInfo? methodInfo = null;

        foreach (var targetClass in targetClassList)
        {
            methodInfo = targetClass.GetMethod(methodName);

            if (methodInfo != null)
            {
                break;
            }
        }

        if (methodInfo == null)
        {
            throw new JsonRpcException(new JsonRpcError(-32601, "Method not found"));
        }

        var r = methodInfo.ReturnParameter;
        bool isTask = false;
        if (r.ParameterType._IsSubClassOfOrSame(typeof(Task))) isTask = true;

        if (isTask == false)
        {
            throw new ApplicationException($"The return value of the function '{methodInfo.Name}' is not a Task.");
        }

        if (isTask)
        {
            this.TaskType = r.ParameterType;
            Type[] genericTypes = TaskType.GenericTypeArguments;
            if (genericTypes.Length == 1)
            {
                this.IsGenericTask = true;
                this.GeneticTaskType = genericTypes[0];
            }
            else if (genericTypes.Length >= 2)
            {
                throw new ApplicationException("generic_types.Length >= 2");
            }
        }

        this.IsTask = isTask;
        this.Method = methodInfo;
        this.Name = methodName;
        this.ReturnParameter = r;
        this.ParametersByIndex = methodInfo.GetParameters();

        foreach (var param in this.ParametersByIndex)
        {
            this._ParametersHelpList.Add(new RpcParameterHelp(param, this.Name));
        }

        RefBool isGoodSample = new RefBool(false);

        if (this.RetValueType != null)
        {
            // 戻り値あり
            this.RetValueSampleValueObject = SampleDataUtil.Get(this.RetValueType, this.Name, isGoodSample: isGoodSample);
            this.IsRetValuePrimitiveType = Dbg.IsPrimitiveType(this.RetValueType);
            this.HasRetValue = true;
        }
        else
        {
            // 戻り値なし: true にする
            this.RetValueSampleValueObject = true;

            this.IsRetValuePrimitiveType = true;
            this.HasRetValue = false;
        }

        var attr = methodInfo.GetCustomAttribute<RpcMethodHelpAttribute>();
        if (attr != null)
        {
            this.Description = attr.Description;
            if (this.IsRetValuePrimitiveType && attr.SampleReturnValueIfPrimitive != null)
            {
                this.RetValueSampleValueObject = attr.SampleReturnValueIfPrimitive;
            }
        }

        this.RetValueSampleValueJsonMultilineStr = this.RetValueSampleValueObject?._ObjectToJson(includeNull: !isGoodSample.Value, compact: false) ?? "";

        this.RequireAuth = (methodInfo.GetCustomAttribute<RpcRequireAuthAttribute>() != null) ? true : false;
    }

    public async Task<object?> InvokeMethod(object targetInstance, string methodName, JObject param, StringComparison paramsStrComparison = StringComparison.Ordinal)
    {
        object?[] inParams = new object[this.ParametersByIndex.Length];
        if (this.ParametersByIndex.Length == 1 && this.ParametersByIndex[0].ParameterType == typeof(System.Object))
        {
            inParams = new object[1] { param };
        }
        else
        {
            for (int i = 0; i < this.ParametersByIndex.Length; i++)
            {
                ParameterInfo pi = this.ParametersByIndex[i];
                JToken? value;
                if (param != null && (param.TryGetValue(pi.Name!, out value) || param.TryGetValue(pi.Name!, paramsStrComparison, out value)))
                {
                    if (pi.ParameterType._IsSubClassOfOrSame(typeof(JObject)) && value.Type == JTokenType.String && value.Value<string>() == "")
                    {
                        value = new JObject();
                    }
                    inParams[i] = value.ToObject(pi.ParameterType);
                }
                else if (pi.HasDefaultValue)
                {
                    inParams[i] = pi.DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"The parameter '{pi.Name}' is missing.");
                }
            }
        }

        object? retobj = this.Method.Invoke(targetInstance, inParams);

        if (this.IsTask == false)
            return Task.FromResult<object>(retobj!);
        else
        {
            Type t = retobj!.GetType();
            Task task = (Task)retobj;

            //Dbg.WhereThread();

            await task;

            //Dbg.WhereThread();

            var propertyMethodInfo = t.GetProperty("Result");
            object? retvalue = propertyMethodInfo!.GetValue(retobj);

            return retvalue;
        }
    }
}

public class JsonRpcAuthErrorException : CoresException
{
    public string HttpBasicAuthErrorRealm { get; }

    public JsonRpcAuthErrorException(string httpBasicAuthErrorRealm = "") : base("JSON-RPC Authentication Error")
    {
        this.HttpBasicAuthErrorRealm = httpBasicAuthErrorRealm._NonNullTrim();
    }
}

public class JsonRpcServerApi : AsyncService
{
    public List<Type> RpcInterface { get; }
    public object TargetObject { get; }

    public JsonRpcServerApi(CancellationToken cancel = default, object? targetObject = null) : base(cancel)
    {
        try
        {
            this.TargetObject = targetObject ?? this;

            this.RpcInterface = GetRpcInterface();
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected JsonRpcClientInfo ClientInfo { get => TaskVar<JsonRpcClientInfo>.Value; }

    FastCache<string, RpcMethodInfo> MethodInfoCache = new FastCache<string, RpcMethodInfo>(expireMsecs: int.MaxValue);
    public RpcMethodInfo GetMethodInfo(string methodName)
    {
        return MethodInfoCache.GetOrCreate(methodName, GetMethodInfoMain)!;
    }
    RpcMethodInfo GetMethodInfoMain(string methodName)
    {
        RpcMethodInfo mi = new RpcMethodInfo(this.RpcInterface, methodName);
        //if (this.RpcInterface.GetMethod(mi.Name) == null)
        //{
        //    throw new ApplicationException($"The method '{methodName}' is not defined on the interface '{this.RpcInterface.Name}'.");
        //}
        return mi;
    }

    public Task<object?> InvokeMethod(string methodName, JObject param, RpcMethodInfo? methodInfo = null, StringComparison paramsStrComparison = StringComparison.Ordinal)
    {
        if (methodInfo == null) methodInfo = GetMethodInfo(methodName);
        return methodInfo!.InvokeMethod(this.TargetObject, methodName, param, paramsStrComparison);
    }

    protected List<Type> GetRpcInterface()
    {
        List<Type> ret = new List<Type>();
        Type t = this.TargetObject.GetType();
        var ints = t.GetTypeInfo().GetInterfaces();
        int num = 0;
        foreach (var f in ints)
            if (f.GetCustomAttribute<RpcInterfaceAttribute>() != null)
            {
                ret.Add(f);
                num++;
            }
        if (num == 0) throw new ApplicationException($"The class '{t.Name}' has no interface with the RpcInterface attribute.");
        //if (num >= 2) throw new ApplicationException($"The class '{t.Name}' has two or mode interfaces with the RpcInterface attribute.");
        return ret;
    }

    public virtual object? StartCall(JsonRpcClientInfo clientInfo) { return null; }

    public virtual async Task<object?> StartCallAsync(JsonRpcClientInfo clientInfo, object? param) => await Task.FromResult<object?>(null);

    public virtual void FinishCall(object? param) { }

    public virtual Task FinishCallAsync(object? param) => Task.CompletedTask;

    public List<RpcMethodInfo> EnumMethodsForHelp()
    {
        List<RpcMethodInfo> ret = new List<RpcMethodInfo>();

        List<MethodInfo> methodInfoList = new List<MethodInfo>();

        foreach (var rpcInt in this.RpcInterface)
        {
            var tmp = rpcInt.GetMethods(BindingFlags.Instance | BindingFlags.Public);

            tmp._DoForEach(x => methodInfoList.Add(x));
        }

        HashSet<string> nameSet = new HashSet<string>();

        foreach (var m in methodInfoList)
        {
            try
            {
                string name = m.Name;

                if (nameSet.Add(name))
                {
                    var info = GetMethodInfo(name);

                    ret.Add(info);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        ret._DoSortBy(x => x.OrderBy(i => i.Name));

        return ret;
    }

    public static JsonRpcClientInfo GetCurrentRpcClientInfo()
    {
        var clientInfo = TaskVar.Get<JsonRpcClientInfo>();

        if (clientInfo == null)
        {
            throw new CoresLibException("JsonRpcClientInfo task context is null. Perhaps this function is not called inside JSON RPC server.");
        }

        return clientInfo;
    }

    public static void TryAuth(Func<string, string, bool> callback, string basicAuthErrorRealm = "")
    {
        try
        {
            var clientInfo = GetCurrentRpcClientInfo();

            if (clientInfo != null && clientInfo.IsBasicAuthCredentialSupplied)
            {
                bool ok = callback(clientInfo.BasicAuthUsername, clientInfo.BasicAuthPassword);

                if (ok)
                {
                    // 認証成功
                    var log = new
                    {
                        BasicAuthUserName = clientInfo.BasicAuthUsername,
                        BasicAuthPassword = clientInfo.BasicAuthPassword._MaskPassword(),
                        ClientIpAddress = clientInfo.RemoteIP.ToString(),
                        ClientPort = clientInfo.RemotePort,
                        ServerIpAddresss = clientInfo.LocalIP.ToString(),
                        ServerPort = clientInfo.LocalPort,
                        AuthResult = true,
                    };

                    log._PostAccessLog("TryAuth");
                    return;
                }
                else
                {
                    // 認証失敗
                    var log = new
                    {
                        BasicAuthUserName = clientInfo.BasicAuthUsername,
                        BasicAuthPassword = clientInfo.BasicAuthPassword._MaskPassword(),
                        ClientIpAddress = clientInfo.RemoteIP.ToString(),
                        ClientPort = clientInfo.RemotePort,
                        ServerIpAddresss = clientInfo.LocalIP.ToString(),
                        ServerPort = clientInfo.LocalPort,
                        AuthResult = false,
                    };

                    log._PostAccessLog("TryAuth");
                }
            }
        }
        catch (Exception ex)
        {
            // ヘンなエラー発生
            ex._Debug();
        }


        // 認証失敗 例外発生させる
        throw new JsonRpcAuthErrorException(basicAuthErrorRealm);
    }
}

public class JsonRpcLocalClient<TInterface> where TInterface : class
{
    public JsonRpcClientInfo RpcClientInfo { get; }
    public JsonRpcServerApi Api { get; }

    public JsonRpcLocalClient(JsonRpcServerApi api, JsonRpcClientInfo clientInfo)
    {
        this.Api = api;
        this.RpcClientInfo = clientInfo;
    }

    public async Task<T> CallAsync<T>(Func<TInterface, Task<T>> proc)
    {
        JsonRpcClientInfo clientInfo = this.RpcClientInfo;

        TInterface? targetInterface = this.Api as TInterface;

        if (targetInterface == null)
        {
            throw new CoresLibException($"The RPC Server doesn't implement the '{typeof(TInterface).Name}' interface.");
        }

        T? ret;

        TaskVar.Set<JsonRpcClientInfo>(clientInfo);
        try
        {
            object? param1 = null;
            try
            {
                param1 = this.Api.StartCall(clientInfo);

                object? param2 = null;
                try
                {
                    param2 = await this.Api.StartCallAsync(clientInfo, param1);

                    ret = await proc(targetInterface);
                }
                finally
                {
                    try
                    {
                        await this.Api.FinishCallAsync(param2);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                try
                {
                    this.Api.FinishCall(param1);
                }
                catch
                {
                }
            }
        }
        finally
        {
            TaskVar.Set<JsonRpcClientInfo>(null);
        }

        return ret;
    }
}

public sealed class JsonRpcCallResult
{
    public string ResultString { get; private set; }
    public bool AllError { get; }
    public bool Error_AuthRequired { get; }
    public string Error_AuthRequiredRealmName { get; }
    public string Error_AuthRequiredMethodName { get; }
    public string SingleErrorMessage { get; }

    public JsonRpcCallResult(string resultString, bool allError, string singleErrorMessage = "", bool errAuthRequired = false, string authRequiredMethodName = "", string authRequiredRealmName = "")
    {
        this.ResultString = resultString._NonNull();
        this.AllError = allError;
        this.Error_AuthRequired = errAuthRequired;
        this.Error_AuthRequiredMethodName = authRequiredMethodName;
        this.Error_AuthRequiredRealmName = authRequiredRealmName;
        this.SingleErrorMessage = singleErrorMessage._NonNull();
    }

    public void RemoveErrorDetailsFromResultString()
    {
        if (this.AllError)
        {
            ResultString = JsonRpcServer.HideJsonRpcResponseStringErrorDetails(ResultString);
        }
    }
}

public abstract class JsonRpcServer
{
    public JsonRpcServerApi Api { get; }
    public JsonRpcServerConfig Config { get; }
    public CancellationToken CancelToken { get => this.Api.GrandCancel; }
    public virtual string ServerFriendlyNameHtml
    {
        get
        {
            string s = "";
            try
            {
                s = this.Config.HadbBasedServicePoint?.AdminForm_GetCurrentDynamicConfig()?.Service_FriendlyName ?? "";
            }
            catch { }
            if (s._IsEmpty())
            {
                s = Env.ApplicationNameSupposed;
            }
            return s._EncodeHtml();
        }
    }

    public JsonRpcServer(JsonRpcServerApi api, JsonRpcServerConfig? cfg = null)
    {
        this.Api = api;
        this.Config = cfg ?? new JsonRpcServerConfig();
    }

    public static string HideJsonRpcResponseStringErrorDetails(string str)
    {
        try
        {
            var jobj = str._JsonToObject<JObject>();

            if (jobj != null)
            {
                if (jobj.ContainsKey("error") && jobj.ContainsKey("result") == false)
                {
                    var res = str._JsonToObject<JsonRpcResponseError>();

                    if (res != null)
                    {
                        if (res.IsError && res.Error != null)
                        {
                            res.Error.Data = null;

                            return res._ObjectToJson();
                        }
                    }
                }
            }
        }
        catch { }

        return str;
    }

    public async Task<JsonRpcResponse> CallMethod(JsonRpcRequest req, Ref<Exception?> exception, StringComparison paramsStrComparison = StringComparison.Ordinal)
    {
        try
        {
            this.CancelToken.ThrowIfCancellationRequested();
            RpcMethodInfo? method = this.Api.GetMethodInfo(req.Method!);
            JObject inObj;

            if (req.Params is JObject)
            {
                inObj = (JObject)req.Params;
            }
            else
            {
                inObj = new JObject();
            }

            try
            {
                object? retObj = await this.Api.InvokeMethod(req.Method!, inObj, method, paramsStrComparison);
                if (method!.IsGenericTask == false)
                {
                    // 戻り値が void 型の場合、代わりに true という bool 値を返す
                    retObj = true;
                }
                return new JsonRpcResponseOk()
                {
                    Id = req.Id,
                    Error = null,
                    Result = retObj,
                };
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }
        }
        catch (JsonRpcException ex)
        {
            exception.Set(ex);
            return new JsonRpcResponseError()
            {
                Id = req.Id,
                Error = ex.RpcError,
                Result = null,
            };
        }
        catch (Exception ex)
        {
            ex = ex._GetSingleException();
            exception.Set(ex);
            return new JsonRpcResponseError()
            {
                Id = req.Id,
                Error = new JsonRpcError(-32603, ex.Message, ex.ToString()),
                Result = null,
            };
        }
    }

    public async Task<JsonRpcCallResult> CallMethods(string inputStr, JsonRpcClientInfo clientInfo, bool simpleResultWhenOk, StringComparison paramsStrComparison = StringComparison.Ordinal)
    {
        bool isSingle = false;
        List<JsonRpcRequest> requestList = new List<JsonRpcRequest>();
        try
        {
            if (inputStr.StartsWith("{") || this.Config.MultiRequestAllowed == false)
            {
                isSingle = true;
                JsonRpcRequest r = inputStr._JsonToObject<JsonRpcRequest>()!;
                if (r.Id._IsEmpty())
                {
                    r.Id = "REQ_" + Str.NewGuid().ToUpperInvariant();
                }
                requestList.Add(r);
            }
            else
            {
                JsonRpcRequest[] rr = inputStr._JsonToObject<JsonRpcRequest[]>()!;
                requestList = new List<JsonRpcRequest>(rr);
            }
        }
        catch
        {
            throw new JsonRpcException(new JsonRpcError(-32700, "Parse error"));
        }

        bool anyAuthRequiredMethod = false;
        string authRequestedMethodName = "";

        foreach (JsonRpcRequest req in requestList)
        {
            // Basic 認証が必要なメソッドが 1 つ以上存在するかチェック
            RpcMethodInfo? method = this.Api.GetMethodInfo(req.Method!);
            if (method != null)
            {
                if (method.RequireAuth)
                {
                    anyAuthRequiredMethod = true;
                    authRequestedMethodName = method.Name;
                    break;
                }
            }
        }

        if (anyAuthRequiredMethod && clientInfo.IsBasicAuthCredentialSupplied == false)
        {
            // Basic 認証が必要であるにもかかわらずクレデンシャルが提供されていない
            string errMsg = $"Basic Authentication credential is required by method '{authRequestedMethodName}'. Retry HTTP request with a Basic Authentication header.";
            string retStr = new JsonRpcResponseError()
            {
                Error = new JsonRpcError(-32600, errMsg),
                Id = null,
                Result = null,
            }._ObjectToJson();

            return new JsonRpcCallResult(retStr, true, errMsg, true, authRequestedMethodName);
        }

        bool authError_WhenSingle = false;
        string authErrorMethodName_WhenSingle = "";
        string authErrorRealm_WhenSingle = "";

        List<JsonRpcResponse> response_list = new List<JsonRpcResponse>();

        TaskVar.Set<JsonRpcClientInfo>(clientInfo);
        try
        {
            object? param1 = null;
            try
            {
                param1 = this.Api.StartCall(clientInfo);

                object? param2 = null;
                try
                {
                    param2 = await this.Api.StartCallAsync(clientInfo, param1);

                    foreach (JsonRpcRequest req in requestList)
                    {
                        LogDefJsonRpc log = new LogDefJsonRpc()
                        {
                            EndPoints = new LogDefIPEndPoints()
                            {
                                LocalIP = clientInfo.LocalIP,
                                LocalPort = clientInfo.LocalPort,
                                RemoteIP = clientInfo.RemoteIP,
                                RemotePort = clientInfo.RemotePort,
                            },
                            ConnectedDateTime = clientInfo.ConnectedDateTime,
                            RpcMethodName = req.Method,
                            SuppliedUsername = clientInfo.BasicAuthUsername._NullIfEmpty(),
                            SuppliedPassword = clientInfo.BasicAuthPassword._MaskPassword()._NullIfEmpty(),
                        };

                        try
                        {
                            Ref<Exception?> applicationException = new Ref<Exception?>();

                            JsonRpcResponse res = await CallMethod(req, applicationException, paramsStrComparison);
                            if (req.Id != null) response_list.Add(res);

                            log.RpcResultOk = res.IsOk;
                            if (res.IsOk == false)
                            {
                                log.RpcError = res.Error;
                                var ex = applicationException.Value;

                                if (ex is JsonRpcAuthErrorException)
                                {
                                    // 認証エラー
                                    if (isSingle)
                                    {
                                        authError_WhenSingle = true;
                                        authErrorMethodName_WhenSingle = req.Method._NonNull();
                                        authErrorRealm_WhenSingle = ((JsonRpcAuthErrorException)ex).HttpBasicAuthErrorRealm;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            JsonRpcException jsonException;
                            if (ex is JsonRpcException)
                            {
                                jsonException = (JsonRpcException)ex;
                            }
                            else
                            {
                                jsonException = new JsonRpcException(new JsonRpcError(-32603, ex._GetSingleException().Message, ex.ToString()));
                            }
                            JsonRpcResponseError res = new JsonRpcResponseError()
                            {
                                Id = req.Id,
                                Error = jsonException.RpcError,
                                Result = null,
                            };
                            if (req.Id != null) response_list.Add(res);

                            log.RpcResultOk = false;
                            log.RpcError = res.Error;
                        }

                        log._PostAccessLog(LogTag.JsonRpcRequestProcessor, copyToDebug: log.RpcResultOk == false);

                        if (log.RpcResultOk == false)
                        {
                            LocalLogRouter.Post(log, LogPriority.Error, LogKind.Default, LogFlags.NoOutputToConsole, LogTag.JsonRpcRequestProcessor);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await this.Api.FinishCallAsync(param2);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                try
                {
                    this.Api.FinishCall(param1);
                }
                catch
                {
                }
            }
        }
        finally
        {
            TaskVar.Set<JsonRpcClientInfo>(null);
        }

        if (isSingle)
        {
            if (response_list.Count >= 1)
            {
                var resSingle = response_list[0];

                if (resSingle.IsOk && simpleResultWhenOk)
                {
                    return new JsonRpcCallResult(resSingle.Result._ObjectToJson(), false, "", authError_WhenSingle, authErrorMethodName_WhenSingle, authErrorRealm_WhenSingle);
                }
                else
                {
                    return new JsonRpcCallResult(resSingle._ObjectToJson(), resSingle.IsError, resSingle.Error?.Message ?? "Unknown Error", authError_WhenSingle, authErrorMethodName_WhenSingle, authErrorRealm_WhenSingle);
                }
            }
            else
            {
                return new JsonRpcCallResult("", false);
            }
        }
        else
        {
            return new JsonRpcCallResult(response_list._ObjectToJson(), response_list.All(x => x.IsError));
        }
    }
}


public class JsonRpcServerConfig
{
    public int MaxRequestBodyLen { get; set; } = CoresConfig.JsonRpcServerSettings.DefaultMaxRequestBodyLen.Value;
    public bool MultiRequestAllowed { get; set; } = false;
    public bool EnableBuiltinRichWebPages { get; set; } = false;
    public bool EnableGetMyIpServer { get; set; } = false;
    public bool EnableHealthCheckServer { get; set; } = false;
    public bool TopPageRedirectToControlPanel { get; set; } = false;
    public IHadbBasedServicePoint? HadbBasedServicePoint { get; set; }
    public JsonRpcHttpServerHook Hook { get; set; } = new JsonRpcHttpServerHook();
}

public abstract class JsonRpcClient
{
    List<(JsonRpcRequest request, JsonRpcResponse response, Type responseDataType)> StCallQueue = new List<(JsonRpcRequest request, JsonRpcResponse response, Type responseDataType)>();

    public void ST_CallClear() => StCallQueue.Clear();

    protected JsonRpcClient()
    {
        MtBatch = new BatchQueue<MT_QueueItem>(MtBatchProcessProc, 10);
    }

    public JsonRpcResponse<TResponse> ST_CallAdd<TResponse>(string method, object param) where TResponse : class
    {
        var ret = new JsonRpcResponse<TResponse>();

        var add_item = (new JsonRpcRequest(method, param, Str.NewGuid()), ret, typeof(TResponse));

        StCallQueue.Add(add_item);

        return ret;
    }

    public JsonRpcResponse ST_CallAdd(string method, object param, Type resultType)
    {
        JsonRpcResponse ret = (JsonRpcResponse)Activator.CreateInstance(typeof(JsonRpcResponse<>).MakeGenericType(resultType))!;

        var add_item = (new JsonRpcRequest(method, param, Str.NewGuid()), ret, resultType);

        StCallQueue.Add(add_item);

        return ret;
    }

    public async Task ST_CallAll(bool throwEachError = false)
    {
        if (StCallQueue.Count == 0) return;

        try
        {
            string req = "";
            bool isSingle = false;

            Dictionary<string, (JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)> requestsTable = new Dictionary<string, (JsonRpcRequest request, JsonRpcResponse response, Type response_data_type)>();
            List<JsonRpcRequest> requests = new List<JsonRpcRequest>();

            foreach (var o in this.StCallQueue)
            {
                requestsTable.Add(o.request.Id!, (o.request, o.response, o.responseDataType));
                requests.Add(o.request);
            }

            //if (requests_table.Count >= 2)
            //    Dbg.WriteLine($"Num_Requests_per_call: {requests_table.Count}");

            if (requestsTable.Count == 1)
            {
                req = requests[0]._ObjectToJson(compact: true);
            }
            else
            {
                req = requests._ObjectToJson(compact: true);
            }

            //req.Debug();

            string ret = await SendRequestAndGetResponseImplAsync(req);

            if (ret.StartsWith("{")) isSingle = true;

            List<JsonRpcResponse> retList = new List<JsonRpcResponse>();

            if (isSingle)
            {
                JsonRpcResponse r = ret._JsonToObject<JsonRpcResponse>()!;
                retList.Add(r);
            }
            else
            {
                JsonRpcResponse[] r = ret._JsonToObject<JsonRpcResponse[]>()!;
                retList = new List<JsonRpcResponse>(r);
            }

            foreach (var res in retList)
            {
                if (res.Id._IsFilled())
                {
                    if (requestsTable.ContainsKey(res.Id))
                    {
                        var q = requestsTable[res.Id];

                        q.response.Error = res.Error;
                        q.response.Id = res.Id;
                        q.response.Result = res.Result;
                        q.response.Version = res.Version;
                    }
                }
            }

            if (retList.Count == 1)
            {
                foreach (var res in retList)
                {
                    if (res.IsError)
                    {
                        if (res.Id._IsEmpty())
                        {
                            foreach (var q in requestsTable.Values)
                            {
                                q.response.Error = res.Error;
                                q.response.Id = res.Id;
                                q.response.Result = res.Result;
                                q.response.Version = res.Version;
                            }

                            break;
                        }
                    }
                }
            }

            if (throwEachError)
                foreach (var r in retList)
                {
                    r.ThrowIfError();
                }
        }
        finally
        {
            ST_CallClear();
        }
    }

    public async Task<JsonRpcResponse<TResponse>> ST_CallOne<TResponse>(string method, object param, bool throwError = false) where TResponse : class
    {
        ST_CallClear();
        try
        {
            JsonRpcResponse<TResponse> res = ST_CallAdd<TResponse>(method, param);
            await ST_CallAll(throwError);
            return res;
        }
        finally
        {
            ST_CallClear();
        }
    }

    class MT_QueueItem
    {
        public string? Method;
        public object? Param;
        public Type? ResultType;
        public JsonRpcResponse? Response;
    }

    readonly CriticalSection LockMtQueue = new CriticalSection<JsonRpcClient>();
    readonly List<MT_QueueItem> MtQueue = new List<MT_QueueItem>();
    readonly SemaphoreSlim MtSemaphore = new SemaphoreSlim(1, 1);

    BatchQueue<MT_QueueItem> MtBatch;

    void MtBatchProcessProc(BatchQueueItem<MT_QueueItem>[] items)
    {
        try
        {
            foreach (var q in items)
            {
                var item = q.UserItem;
                item.Response = ST_CallAdd(item.Method!, item.Param!, item.ResultType!);
            }

            ST_CallAll(false)._GetResult();
        }
        catch (Exception ex)
        {
            //ex.ToString().Print();
            foreach (var q in items)
            {
                var item = q.UserItem;
                item.Response!.Error = new JsonRpcError(-1, ex._GetSingleException().ToString());
            }
        }
        finally
        {
            foreach (var q in items)
            {
                q.SetCompleted();
            }
        }
    }

    public async Task<JsonRpcResponse<TResponse>> MT_Call<TResponse>(string method, object param, bool throwError = false) where TResponse : class
    {
        var response = new JsonRpcResponse<TResponse>();

        MT_QueueItem q = new MT_QueueItem()
        {
            Method = method,
            Param = param,
            ResultType = typeof(TResponse),
            Response = null,
        };

        var b = MtBatch.Add(q);

        await b.CompletedEvent.WaitAsync();

        return (JsonRpcResponse<TResponse>)q.Response!;
    }

    public abstract Task<string> SendRequestAndGetResponseImplAsync(string req, CancellationToken cancel = default);

    public abstract int TimeoutMsecs { get; set; }

    public abstract void AddHeader(string name, string value);

    class ProxyInterceptor : IInterceptor
    {
        public JsonRpcClient RpcClient { get; }

        public ProxyInterceptor(JsonRpcClient rpcClient)
        {
            this.RpcClient = rpcClient;
        }

        public void Intercept(IInvocation invocation)
        {
            JObject o = new JObject();
            var inParams = invocation.Method.GetParameters();
            if (invocation.Arguments.Length != inParams.Length) throw new ApplicationException("v.Arguments.Length != in_params.Length");
            for (int i = 0; i < inParams.Length; i++)
            {
                var p = inParams[i];
                o.Add(p.Name!, JToken.FromObject(invocation.Arguments[i]));
            }
            Task<JsonRpcResponse<object>> callRet = RpcClient.MT_Call<object>(invocation.Method.Name, o, true);

            //ret.Wait();

            //Dbg.WhereThread(v.Method.Name);
            Task<object?> ret = GetResponseObjectAsync(callRet);

            var returnType = invocation.Method.ReturnType;

            if (returnType == typeof(Task))
            {
                invocation.ReturnValue = ret;
            }
            else
            {
                if (returnType.IsGenericType == false) throw new ApplicationException($"The return type of the method '{invocation.Method.Name}' is not a Task<>.");
                if (returnType.BaseType != typeof(Task)) throw new ApplicationException($"The return type of the method '{invocation.Method.Name}' is not a Task<>.");

                var genericArgs = returnType.GetGenericArguments();
                if (genericArgs.Length != 1) throw new ApplicationException($"The return type of the method '{invocation.Method.Name}' is not a Task<>.");
                var taskReturnType = genericArgs[0];

                invocation.ReturnValue = TaskUtil.ConvertTask(ret, typeof(object), taskReturnType);
                //Dbg.WhereThread(v.Method.Name);
            }
        }

        async Task<object?> GetResponseObjectAsync(Task<JsonRpcResponse<object>> o)
        {
            //Dbg.WhereThread();
            await o;
            var result = o._GetResult();
            result.ThrowIfError();
            //Dbg.WhereThread();
            return result.ResultData;
        }
    }

    public virtual TRpcInterface GenerateRpcInterface<TRpcInterface>() where TRpcInterface : class
    {
        ProxyGenerator g = new ProxyGenerator();
        ProxyInterceptor ic = new ProxyInterceptor(this);

        return g.CreateInterfaceProxyWithoutTarget<TRpcInterface>(ic);
    }
}

public class CastleCoreStartupTester
{
    public interface ITestInterface
    {
        public string Hello(int a);
    }

    class ProxyInterceptor : IInterceptor
    {
        public void Intercept(IInvocation invocation)
        {
            var inParams = invocation.Method.GetParameters();
            var returnType = invocation.Method.ReturnType;
            string methodName = invocation.Method.Name;

            if (methodName == "Hello")
            {
                if (inParams.Length == 1)
                {
                    if (returnType == typeof(string))
                    {
                        int a = (int)invocation.Arguments[0];

                        string ret = $"Hello {a}";

                        invocation.ReturnValue = ret;
                    }
                }
            }
        }
    }

    public static void Test()
    {
        ProxyGenerator g = new ProxyGenerator();
        ProxyInterceptor ic = new ProxyInterceptor();

        ITestInterface obj = g.CreateInterfaceProxyWithoutTarget<ITestInterface>(ic);

        string tmp = obj.Hello(123);

        Dbg.TestTrue(tmp == "Hello 123");
    }
}

public class JsonRpcHttpClient : JsonRpcClient, IDisposable, IAsyncDisposable
{
    public WebApi WebApi { get; private set; }
    public string ApiBaseUrl { get; set; }

    public string LastLocalIp { get; private set; } = "";
    public int LastLocalPort { get; private set; } = 0;

    public string LastRemoteIp { get; private set; } = "";
    public int LastRemotePort { get; private set; } = 0;

    long NextEndPointInfoGetTick = 0;
    int LastNexInfoVer = int.MaxValue;

    public override int TimeoutMsecs { get => WebApi.TimeoutMsecs; set => WebApi.TimeoutMsecs = value; }
    public override void AddHeader(string name, string value) => WebApi.AddHeader(name, value);

    public JsonRpcHttpClient(string apiUrl, WebApiOptions? webApiOptions = null)
    {
        this.ApiBaseUrl = apiUrl;
        this.WebApi = new WebApi(webApiOptions);
    }

    public override async Task<string> SendRequestAndGetResponseImplAsync(string req, CancellationToken cancel = default)
    {
        WebRet ret = await this.WebApi.SimplePostDataAsync(this.ApiBaseUrl, req._GetBytes_UTF8(), cancel, Consts.MimeTypes.Json);

        int networkInfoVer = BackgroundState<PalHostNetInfo>.Current.Version;

        if (networkInfoVer != LastNexInfoVer || NextEndPointInfoGetTick == 0 || NextEndPointInfoGetTick <= Time.Tick64)
        {
            NextEndPointInfoGetTick = Time.Tick64 + Consts.Intervals.JsonRpcClientEndPointInfoUpdateInterval;
            LastNexInfoVer = networkInfoVer;

            // ネットワーク情報を更新する
            await TryToGetEndPointsInfo(cancel);
        }

        return ret.ToString();
    }

    public async Task TryToGetEndPointsInfo(CancellationToken cancel = default)
    {
        try
        {
            Uri uri = new Uri(this.ApiBaseUrl);

            await using (var sock = await LocalNet.ConnectIPv4v6DualAsync(new TcpConnectParam(uri.Host, uri.Port, connectTimeout: Consts.Timeouts.Rapid, dnsTimeout: Consts.Timeouts.Rapid)))
            {
                LastLocalIp = sock.EndPointInfo.LocalIP._NonNull();
                LastLocalPort = sock.EndPointInfo.LocalPort;

                LastRemoteIp = sock.EndPointInfo.RemoteIP._NonNull();
                LastRemotePort = sock.EndPointInfo.RemotePort;
            }
        }
        catch
        { }
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
        this.WebApi.Dispose();
        this.WebApi = null!;
        await Task.CompletedTask;
    }
}

public class JsonRpcHttpClient<TRpcInterface> : JsonRpcHttpClient
    where TRpcInterface : class
{
    public TRpcInterface Call { get; }
    public JsonRpcHttpClient(string apiUrl, WebApiOptions? webApiOptions = null) : base(apiUrl, webApiOptions)
    {
        this.Call = this.GenerateRpcInterface<TRpcInterface>();
    }
}

public class JsonRpcClientInfo
{
    // ログ保存のためシリアル化するので内容に注意せよ
    public string LocalIP { get; }
    public int LocalPort { get; }
    public string RemoteIP { get; }
    public int RemotePort { get; }
    public string HttpProtocol { get; }
    public string HttpHostHeader { get; }
    public string HttpUrl { get; }
    public DateTimeOffset ConnectedDateTime { get; }
    public SortedDictionary<string, string> Headers { get; }

    [JsonIgnore]
    public object? Param1 { get; set; }
    [JsonIgnore]
    public object? Param2 { get; set; }
    [JsonIgnore]
    public object? Param3 { get; set; }

    public string BasicAuthUsername { get; set; }
    public string BasicAuthPassword { get; set; }
    public bool IsBasicAuthCredentialSupplied => BasicAuthUsername._IsFilled() && BasicAuthPassword._IsFilled();
    public bool IsLocalClient { get; }

    public JsonRpcClientInfo(string httpProtocol, string httpHostHeader, string httpUrl, string localIp, int localPort, string remoteIp, int remotePort, SortedDictionary<string, string>? headers = null,
        string? basicAuthUsername = null, string? basicAuthPassword = null, bool isLocalClient = false)
    {
        this.ConnectedDateTime = DateTimeOffset.Now;
        this.LocalIP = localIp._NonNull();
        this.LocalPort = localPort;
        this.RemoteIP = remoteIp._NonNull();
        this.RemotePort = remotePort;
        this.Headers = headers ?? new SortedDictionary<string, string>();
        this.HttpProtocol = httpProtocol;
        this.HttpHostHeader = httpHostHeader;
        this.HttpUrl = httpUrl;

        this.BasicAuthUsername = basicAuthUsername._NonNull();
        this.BasicAuthPassword = basicAuthPassword._NonNull();
        this.IsLocalClient = isLocalClient;
    }

    public override string ToString()
    {
        return $"{this.RemoteIP}:{this.RemotePort}";
    }
}

#endif // CORES_BASIC_JSON


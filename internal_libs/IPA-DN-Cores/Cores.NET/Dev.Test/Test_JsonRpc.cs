using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace IPA.TestDev;

static class JsonRpcTest
{
    public static void TestMain()
    {

        jsonrpc_client_server_both_test();
    }

    public static void jsonrpc_client_server_both_test()
    {
        //jsonrpc_server_invoke_test().Wait();return;

        // start server
        HttpServerOptions http_cfg = new HttpServerOptions()
        {
            DebugKestrelToConsole = false,
        };
        JsonRpcServerConfig rpc_cfg = new JsonRpcServerConfig()
        {
        };

        using (RpcServerApiTest h = new RpcServerApiTest())
        using (var s = JsonRpcHttpServerBuilder.StartServer(http_cfg, rpc_cfg, h))
        {
            Ref<bool> client_stop_flag = new Ref<bool>();

            // start client
            ThreadObj client_thread = ThreadObj.Start(param =>
            {
                    //Kernel.SleepThread(-1);

                    //using ()
                    {
                        //c.AddHeader("X-1", "Hello");

                        rpctmp1 t = new rpctmp1();
                    t.a = new rpc_t()
                    {
                        Int1 = 2,
                        Str1 = "Neko",
                    };

                        //JsonRpcResponse<object> ret = c.CallOne<object>("Test1", t).Result;
                        //JsonRpcResponse<object> ret = c.CallOne<object>("Test2", t).Result;

                        Benchmark b = new Benchmark("rpccall");

                    JsonRpcHttpClient<rpc_server_api_interface_test> c = new JsonRpcHttpClient<rpc_server_api_interface_test>("http://127.0.0.1:88/rpc");
                    var threads = ThreadObj.StartMany(256, par =>
                    {

                        while (client_stop_flag.Value == false)
                        {
                                //c.Call.Divide(8, 2).Wait();
                                TMP1 a = new TMP1() { a = 4, b = 2 };
                            c.MT_Call<object>("Divide", a, true)._GetResult();
                                //c.ST_CallOne<object>("Divide", a, true).Wait();
                                b.IncrementMe++;
                        }
                    }
                    );

                    foreach (var thread in threads)
                    {
                        thread.WaitForEnd();
                    }

                        //c.Call.Divide(8, 2).Result.Print();
                        //c.Call.Divide(8, 2).Result.Print();
                        //c.Call.Test3(1, 2, 3).Result.Print();
                        //c.Call.Test5(1, "2").Result.ObjectToJson().Print();
                        //var fnlist = c.Call.Test6().Result;
                        ////foreach (var fn in fnlist) fn.Print();
                        //c.Call.Test7(fnlist).Result.Print();

                        //Con.WriteLine(ret.ObjectToJson());
                    }
            }, null);

            Con.ReadLine("Enter to quit>");

            client_stop_flag.Set(true);

            client_thread.WaitForEnd();
        }
    }
}

class TMP1
{
    public int a, b;
}

class rpc_t
{
    public string? Str1;
    public int Int1;
}

[RpcInterface]
interface rpc_server_api_interface_test
{
    Task<rpc_t> Test1(rpc_t a);
    Task Test2(rpc_t a);
    Task<string> Test3(int a, int b, int c);
    Task<int> Divide(int a, int b);
    Task<rpc_t> Test5(int a, string b);
    Task<string[]> Test6();
    Task<string> Test7(string[] p);
}

class RpcServerApiTest : JsonRpcServerApi, rpc_server_api_interface_test
{
    public RpcServerApiTest(CancellationToken cancel = default) : base(cancel)
    {
    }

#pragma warning disable CS1998
    public async Task<rpc_t> Test1(rpc_t a)
    {
        //await TaskUtil.PreciseDelay(500);
        return new rpc_t()
        {
            Int1 = a.Int1,
            Str1 = a.Str1,
        };
    }

    public async Task Test2(rpc_t a)
    {
        //await TaskUtil.PreciseDelay(500);
        return;
    }

    public async Task<string> Test3(int a, int b, int c)
    {
        //await TaskUtil.PreciseDelay(500);
        return Str.CombineStringArray(",", a, b, c);
    }

    //static Benchmark bm = new Benchmark();
    public async Task<int> Divide(int a, int b)
    {
        //this.ClientInfo.ToString().Print();
        //bm.IncrementMe++;
        return a / b;
    }
    public async Task<rpc_t> Test5(int a, string b)
    {
        return new rpc_t()
        {
            Int1 = a,
            Str1 = b,
        };
    }

    public async Task<string[]> Test6()
    {
        List<string> ret = new List<string>();
        foreach (var d in IO.EnumDirEx(Env.AppRootDir))
        {
            ret.Add(d.FullPath);
        }
        return ret.ToArray();
    }

    public async Task<string> Test7(string[] p)
    {
        return Str.CombineStringArray(p, ",");
    }

    public override object? StartCall(JsonRpcClientInfo client_info)
    {
        return null;
    }

    public override async Task<object?> StartCallAsync(JsonRpcClientInfo client_info, object? param)
    {
        return null;
    }

    public override void FinishCall(object? param)
    {
        Util.DoNothing();
    }

    public override Task FinishCallAsync(object? param)
    {
        return Task.CompletedTask;
    }
#pragma warning restore CS1998
}

class rpctmp1
{
    public rpc_t? a;
}

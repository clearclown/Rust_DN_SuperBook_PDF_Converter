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
using System.Threading.Tasks;
using System.Reflection;
using System.Threading;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class TaskUtil
{
    public static object ConvertTask(object srcTaskObject, Type oldTaskType, Type newTaskType)
    {
        Type srcTaskDef = typeof(Task<>).MakeGenericType(oldTaskType);

        var contWithMethods = srcTaskDef.GetMethods();
        MethodInfo? contWith = null;
        int num = 0;
        foreach (var m in contWithMethods)
        {
            if (m.Name == "ContinueWith" && m.ContainsGenericParameters)
            {
                var pinfos = m.GetParameters();
                if (pinfos.Length == 1)
                {
                    var pinfo = pinfos[0];
                    var ptype = pinfo.ParameterType;
                    var generic_args = ptype.GenericTypeArguments;
                    if (generic_args.Length == 2)
                    {
                        if (generic_args[0].IsGenericType)
                        {
                            if (generic_args[1].IsGenericParameter)
                            {
                                if (generic_args[0].BaseType == typeof(Task))
                                {
                                    contWith = m;
                                    num++;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (num != 1) throw new ApplicationException("ConvertTask: num != 1");

        contWith._NullCheck();

        object? ret = null;

        var contWithGeneric = contWith.MakeGenericMethod(newTaskType);

        var convertTaskProcMethod = typeof(TaskUtil).GetMethod(nameof(ConvertTaskProc), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(newTaskType);

        var funcType = typeof(Func<,>).MakeGenericType(typeof(Task<>).MakeGenericType(oldTaskType), newTaskType);

        Delegate delegateInstance = convertTaskProcMethod.CreateDelegate(funcType);

        ret = contWithGeneric.Invoke(srcTaskObject, new object[] { delegateInstance })!;

        return ret;
    }

    static TNewResult ConvertTaskProc<TNewResult>(object t)
    {
        Type oldTaskType = t.GetType();
        object? resultOld = oldTaskType.GetProperty("Result")!.GetValue(t);
        TNewResult? resultNew = Json.ConvertObject<TNewResult>(resultOld);
        return resultNew!;
    }
}

#endif // CORES_BASIC_JSON


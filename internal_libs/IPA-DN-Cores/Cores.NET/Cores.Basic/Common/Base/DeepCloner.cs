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


// From: force-net/DeepCloner
// https://github.com/force-net/DeepCloner/tree/8f1404093aee07784b7f1e08927d4c8a9314a7d8
// 
// MIT License
// 
// Copyright (c) 2016 force
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

#nullable disable

#define NETCORE
#define NETCORE20


using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Reflection.Emit;
using System.ComponentModel;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Data.SqlTypes;
using static IPA.Cores.Basic.Internal.DeepCloner.DeepClonerExtensions;
using System.Dynamic;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.Internal.DeepCloner;
using IPA.Cores.Basic.Internal.DeepCloner.Helpers;
using System.Security;

namespace IPA.Cores.Basic
{
}

namespace IPA.Cores.Basic.Internal.DeepCloner
{
    /// <summary>
    /// Extensions for object cloning
    /// </summary>
    public static class DeepClonerExtensions
    {
        /// <summary>
        /// Performs deep (full) copy of object and related graph
        /// </summary>
        public static T DeepClone<T>(this T obj)
        {
            return DeepClonerGenerator.CloneObject(obj);
        }

        /// <summary>
        /// Performs deep (full) copy of object and related graph to existing object
        /// </summary>
        /// <returns>existing filled object</returns>
        /// <remarks>Method is valid only for classes, classes should be descendants in reality, not in declaration</remarks>
        public static TTo DeepCloneTo<TFrom, TTo>(this TFrom objFrom, TTo objTo) where TTo : class, TFrom
        {
            return (TTo)DeepClonerGenerator.CloneObjectTo(objFrom, objTo, true);
        }

        /// <summary>
        /// Performs shallow copy of object to existing object
        /// </summary>
        /// <returns>existing filled object</returns>
        /// <remarks>Method is valid only for classes, classes should be descendants in reality, not in declaration</remarks>
        public static TTo ShallowCloneTo<TFrom, TTo>(this TFrom objFrom, TTo objTo) where TTo : class, TFrom
        {
            return (TTo)DeepClonerGenerator.CloneObjectTo(objFrom, objTo, false);
        }

        /// <summary>
        /// Performs shallow (only new object returned, without cloning of dependencies) copy of object
        /// </summary>
        public static T ShallowClone<T>(this T obj)
        {
            return ShallowClonerGenerator.CloneObject(obj);
        }

        static DeepClonerExtensions()
        {
            if (!PermissionCheck())
            {
                throw new SecurityException("DeepCloner should have enough permissions to run. Grant FullTrust or Reflection permission.");
            }
        }

        private static bool PermissionCheck()
        {
            // best way to check required permission: execute something and receive exception
            // .net security policy is weird for normal usage
            try
            {
                new object().ShallowClone();
            }
            catch (VerificationException)
            {
                return false;
            }
            catch (MemberAccessException)
            {
                return false;
            }

            return true;
        }
    }
}

namespace IPA.Cores.Basic.Internal.DeepCloner.Helpers
{
    internal static class ClonerToExprGenerator
    {
        internal static object GenerateClonerInternal(Type realType, bool isDeepClone)
        {
            if (realType.IsValueType())
                throw new InvalidOperationException("Operation is valid only for reference types");
            return GenerateProcessMethod(realType, isDeepClone);
        }

        private static object GenerateProcessMethod(Type type, bool isDeepClone)
        {
            if (type.IsArray)
            {
                return GenerateProcessArrayMethod(type, isDeepClone);
            }

            var methodType = typeof(object);

            var expressionList = new List<Expression>();

            ParameterExpression from = Expression.Parameter(methodType);
            var fromLocal = from;
            var to = Expression.Parameter(methodType);
            var toLocal = to;
            var state = Expression.Parameter(typeof(DeepCloneState));

            // if (!type.IsValueType())
            {
                fromLocal = Expression.Variable(type);
                toLocal = Expression.Variable(type);
                // fromLocal = (T)from
                expressionList.Add(Expression.Assign(fromLocal, Expression.Convert(from, type)));
                expressionList.Add(Expression.Assign(toLocal, Expression.Convert(to, type)));

                if (isDeepClone)
                {
                    // added from -> to binding to ensure reference loop handling
                    // structs cannot loop here
                    // state.AddKnownRef(from, to)
                    expressionList.Add(Expression.Call(state, typeof(DeepCloneState).GetMethod("AddKnownRef"), from, to));
                }
            }

            List<FieldInfo> fi = new List<FieldInfo>();
            var tp = type;
            do
            {
#if !NETCORE
                // don't do anything with this dark magic!
                if (tp == typeof(ContextBoundObject)) break;
#else
				if (tp.Name == "ContextBoundObject") break;
#endif

                fi.AddRange(tp.GetDeclaredFields());
                tp = tp.BaseType();
            }
            while (tp != null);

            foreach (var fieldInfo in fi)
            {
                if (isDeepClone && !DeepClonerSafeTypes.CanReturnSameObject(fieldInfo.FieldType))
                {
                    var methodInfo = fieldInfo.FieldType.IsValueType()
                        ? typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneStructInternal")
                            .MakeGenericMethod(fieldInfo.FieldType)
                        : typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneClassInternal");

                    var get = Expression.Field(fromLocal, fieldInfo);

                    // toLocal.Field = Clone...Internal(fromLocal.Field)
                    var call = (Expression)Expression.Call(methodInfo, get, state);
                    if (!fieldInfo.FieldType.IsValueType())
                        call = Expression.Convert(call, fieldInfo.FieldType);

                    // should handle specially
                    // todo: think about optimization, but it rare case
                    if (fieldInfo.IsInitOnly)
                    {
                        // var setMethod = fieldInfo.GetType().GetMethod("SetValue", new[] { typeof(object), typeof(object) });
                        // expressionList.Add(Expression.Call(Expression.Constant(fieldInfo), setMethod, toLocal, call));
                        var setMethod = typeof(DeepClonerExprGenerator).GetPrivateStaticMethod("ForceSetField");
                        expressionList.Add(Expression.Call(setMethod, Expression.Constant(fieldInfo),
                            Expression.Convert(toLocal, typeof(object)), Expression.Convert(call, typeof(object))));
                    }
                    else
                    {
                        expressionList.Add(Expression.Assign(Expression.Field(toLocal, fieldInfo), call));
                    }
                }
                else
                {
                    expressionList.Add(Expression.Assign(Expression.Field(toLocal, fieldInfo), Expression.Field(fromLocal, fieldInfo)));
                }
            }

            expressionList.Add(Expression.Convert(toLocal, methodType));

            var funcType = typeof(Func<,,,>).MakeGenericType(methodType, methodType, typeof(DeepCloneState), methodType);

            var blockParams = new List<ParameterExpression>();
            if (from != fromLocal) blockParams.Add(fromLocal);
            if (to != toLocal) blockParams.Add(toLocal);

            return Expression.Lambda(funcType, Expression.Block(blockParams, expressionList), from, to, state).Compile();
        }

        private static object GenerateProcessArrayMethod(Type type, bool isDeep)
        {
            var elementType = type.GetElementType();
            var rank = type.GetArrayRank();

            ParameterExpression from = Expression.Parameter(typeof(object));
            ParameterExpression to = Expression.Parameter(typeof(object));
            var state = Expression.Parameter(typeof(DeepCloneState));

            var funcType = typeof(Func<,,,>).MakeGenericType(typeof(object), typeof(object), typeof(DeepCloneState), typeof(object));

            if (rank == 1 && type == elementType.MakeArrayType())
            {
                if (!isDeep)
                {
                    var callS = Expression.Call(
                        typeof(ClonerToExprGenerator).GetPrivateStaticMethod("ShallowClone1DimArraySafeInternal")
                            .MakeGenericMethod(elementType), Expression.Convert(from, type), Expression.Convert(to, type));
                    return Expression.Lambda(funcType, callS, from, to, state).Compile();
                }
                else
                {
                    var methodName = "Clone1DimArrayClassInternal";
                    if (DeepClonerSafeTypes.CanReturnSameObject(elementType)) methodName = "Clone1DimArraySafeInternal";
                    else if (elementType.IsValueType()) methodName = "Clone1DimArrayStructInternal";
                    var methodInfo = typeof(ClonerToExprGenerator).GetPrivateStaticMethod(methodName).MakeGenericMethod(elementType);
                    var callS = Expression.Call(methodInfo, Expression.Convert(from, type), Expression.Convert(to, type), state);
                    return Expression.Lambda(funcType, callS, from, to, state).Compile();
                }
            }
            else
            {
                // multidim or not zero-based arrays
                var methodInfo = typeof(ClonerToExprGenerator).GetPrivateStaticMethod(
                    rank == 2 && type == elementType.MakeArrayType()
                        ? "Clone2DimArrayInternal"
                        : "CloneAbstractArrayInternal");
                var callS = Expression.Call(methodInfo, Expression.Convert(from, type), Expression.Convert(to, type), state, Expression.Constant(isDeep));
                return Expression.Lambda(funcType, callS, from, to, state).Compile();
            }
        }

        // when we can't use code generation, we can use these methods
        internal static T[] ShallowClone1DimArraySafeInternal<T>(T[] objFrom, T[] objTo)
        {
            var l = Math.Min(objFrom.Length, objTo.Length);
            Array.Copy(objFrom, objTo, l);
            return objTo;
        }

        // when we can't use code generation, we can use these methods
        internal static T[] Clone1DimArraySafeInternal<T>(T[] objFrom, T[] objTo, DeepCloneState state)
        {
            var l = Math.Min(objFrom.Length, objTo.Length);
            state.AddKnownRef(objFrom, objTo);
            Array.Copy(objFrom, objTo, l);
            return objTo;
        }

        internal static T[] Clone1DimArrayStructInternal<T>(T[] objFrom, T[] objTo, DeepCloneState state)
        {
            // not null from called method, but will check it anyway
            if (objFrom == null || objTo == null) return null;
            var l = Math.Min(objFrom.Length, objTo.Length);
            state.AddKnownRef(objFrom, objTo);
            var cloner = DeepClonerGenerator.GetClonerForValueType<T>();
            for (var i = 0; i < l; i++)
                objTo[i] = cloner(objTo[i], state);

            return objTo;
        }

        internal static T[] Clone1DimArrayClassInternal<T>(T[] objFrom, T[] objTo, DeepCloneState state)
        {
            // not null from called method, but will check it anyway
            if (objFrom == null || objTo == null) return null;
            var l = Math.Min(objFrom.Length, objTo.Length);
            state.AddKnownRef(objFrom, objTo);
            for (var i = 0; i < l; i++)
                objTo[i] = (T)DeepClonerGenerator.CloneClassInternal(objFrom[i], state);

            return objTo;
        }

        internal static T[,] Clone2DimArrayInternal<T>(T[,] objFrom, T[,] objTo, DeepCloneState state, bool isDeep)
        {
            // not null from called method, but will check it anyway
            if (objFrom == null || objTo == null) return null;
            var l1 = Math.Min(objFrom.GetLength(0), objTo.GetLength(0));
            var l2 = Math.Min(objFrom.GetLength(1), objTo.GetLength(1));
            state.AddKnownRef(objFrom, objTo);
            if ((!isDeep || DeepClonerSafeTypes.CanReturnSameObject(typeof(T)))
                && objFrom.GetLength(0) == objTo.GetLength(0)
                && objFrom.GetLength(1) == objTo.GetLength(1))
            {
                Array.Copy(objFrom, objTo, objFrom.Length);
                return objTo;
            }

            if (!isDeep)
            {
                for (var i = 0; i < l1; i++)
                    for (var k = 0; k < l2; k++)
                        objTo[i, k] = objFrom[i, k];
                return objTo;
            }

            if (typeof(T).IsValueType())
            {
                var cloner = DeepClonerGenerator.GetClonerForValueType<T>();
                for (var i = 0; i < l1; i++)
                    for (var k = 0; k < l2; k++)
                        objTo[i, k] = cloner(objFrom[i, k], state);
            }
            else
            {
                for (var i = 0; i < l1; i++)
                    for (var k = 0; k < l2; k++)
                        objTo[i, k] = (T)DeepClonerGenerator.CloneClassInternal(objFrom[i, k], state);
            }

            return objTo;
        }

        // rare cases, very slow cloning. currently it's ok
        internal static Array CloneAbstractArrayInternal(Array objFrom, Array objTo, DeepCloneState state, bool isDeep)
        {
            // not null from called method, but will check it anyway
            if (objFrom == null || objTo == null) return null;
            var rank = objFrom.Rank;

            if (objTo.Rank != rank)
                throw new InvalidOperationException("Invalid rank of target array");
            var lowerBoundsFrom = Enumerable.Range(0, rank).Select(objFrom.GetLowerBound).ToArray();
            var lowerBoundsTo = Enumerable.Range(0, rank).Select(objTo.GetLowerBound).ToArray();
            var lengths = Enumerable.Range(0, rank).Select(x => Math.Min(objFrom.GetLength(x), objTo.GetLength(x))).ToArray();
            var idxesFrom = Enumerable.Range(0, rank).Select(objFrom.GetLowerBound).ToArray();
            var idxesTo = Enumerable.Range(0, rank).Select(objTo.GetLowerBound).ToArray();

            state.AddKnownRef(objFrom, objTo);
            while (true)
            {
                if (isDeep)
                    objTo.SetValue(DeepClonerGenerator.CloneClassInternal(objFrom.GetValue(idxesFrom), state), idxesTo);
                else
                    objTo.SetValue(objFrom.GetValue(idxesFrom), idxesTo);
                var ofs = rank - 1;
                while (true)
                {
                    idxesFrom[ofs]++;
                    idxesTo[ofs]++;
                    if (idxesFrom[ofs] >= lowerBoundsFrom[ofs] + lengths[ofs])
                    {
                        idxesFrom[ofs] = lowerBoundsFrom[ofs];
                        idxesTo[ofs] = lowerBoundsTo[ofs];
                        ofs--;
                        if (ofs < 0) return objTo;
                    }
                    else
                        break;
                }
            }
        }
    }

    internal static class DeepClonerCache
    {
        private static readonly ConcurrentDictionary<Type, object> _typeCache = new ConcurrentDictionary<Type, object>();

        private static readonly ConcurrentDictionary<Type, object> _typeCacheDeepTo = new ConcurrentDictionary<Type, object>();

        private static readonly ConcurrentDictionary<Type, object> _typeCacheShallowTo = new ConcurrentDictionary<Type, object>();

        private static readonly ConcurrentDictionary<Type, object> _structAsObjectCache = new ConcurrentDictionary<Type, object>();

        private static readonly ConcurrentDictionary<Tuple<Type, Type>, object> _typeConvertCache = new ConcurrentDictionary<Tuple<Type, Type>, object>();

        public static object GetOrAddClass<T>(Type type, Func<Type, T> adder)
        {
            // return _typeCache.GetOrAdd(type, x => adder(x));

            // this implementation is slightly faster than getoradd
            object value;
            if (_typeCache.TryGetValue(type, out value)) return value;

            // will lock by type object to ensure only one type generator is generated simultaneously
            lock (type)
            {
                value = _typeCache.GetOrAdd(type, t => adder(t));
            }

            return value;
        }

        public static object GetOrAddDeepClassTo<T>(Type type, Func<Type, T> adder)
        {
            object value;
            if (_typeCacheDeepTo.TryGetValue(type, out value)) return value;

            // will lock by type object to ensure only one type generator is generated simultaneously
            lock (type)
            {
                value = _typeCacheDeepTo.GetOrAdd(type, t => adder(t));
            }

            return value;
        }

        public static object GetOrAddShallowClassTo<T>(Type type, Func<Type, T> adder)
        {
            object value;
            if (_typeCacheShallowTo.TryGetValue(type, out value)) return value;

            // will lock by type object to ensure only one type generator is generated simultaneously
            lock (type)
            {
                value = _typeCacheShallowTo.GetOrAdd(type, t => adder(t));
            }

            return value;
        }

        public static object GetOrAddStructAsObject<T>(Type type, Func<Type, T> adder)
        {
            // return _typeCache.GetOrAdd(type, x => adder(x));

            // this implementation is slightly faster than getoradd
            object value;
            if (_structAsObjectCache.TryGetValue(type, out value)) return value;

            // will lock by type object to ensure only one type generator is generated simultaneously
            lock (type)
            {
                value = _structAsObjectCache.GetOrAdd(type, t => adder(t));
            }

            return value;
        }

        public static T GetOrAddConvertor<T>(Type from, Type to, Func<Type, Type, T> adder)
        {
            return (T)_typeConvertCache.GetOrAdd(new Tuple<Type, Type>(from, to), (tuple) => adder(tuple.Item1, tuple.Item2));
        }

        /// <summary>
        /// This method can be used when we switch between safe / unsafe variants (for testing)
        /// </summary>
        public static void ClearCache()
        {
            _typeCache.Clear();
            _typeCacheDeepTo.Clear();
            _typeCacheShallowTo.Clear();
            _structAsObjectCache.Clear();
            _typeConvertCache.Clear();
        }
    }

    internal static class DeepClonerExprGenerator
    {
        private static readonly ConcurrentDictionary<FieldInfo, bool> _readonlyFields = new ConcurrentDictionary<FieldInfo, bool>();

        private static readonly bool _canFastCopyReadonlyFields = false;

        private static readonly MethodInfo _fieldSetMethod;
        static DeepClonerExprGenerator()
        {
            try
            {
                typeof(DeepClonerExprGenerator).GetPrivateStaticField(nameof(_canFastCopyReadonlyFields)).SetValue(null, true);
#if NETCORE13
				_fieldSetMethod = typeof(FieldInfo).GetRuntimeMethod("SetValue", new[] { typeof(object), typeof(object) });
#else
                _fieldSetMethod = typeof(FieldInfo).GetMethod("SetValue", new[] { typeof(object), typeof(object) });
#endif

                if (_fieldSetMethod == null)
                    throw new ArgumentNullException();
            }
            catch (Exception)
            {
                // cannot
            }
        }

        internal static object GenerateClonerInternal(Type realType, bool asObject)
        {
            return GenerateProcessMethod(realType, asObject && realType.IsValueType());
        }

        private static FieldInfo _attributesFieldInfo = typeof(FieldInfo).GetPrivateField("m_fieldAttributes");

        // today, I found that it not required to do such complex things. Just SetValue is enough
        // is it new runtime changes, or I made incorrect assumptions eariler
        // slow, but hardcore method to set readonly field
        internal static void ForceSetField(FieldInfo field, object obj, object value)
        {
            var fieldInfo = field.GetType().GetPrivateField("m_fieldAttributes");

            // TODO: think about it
            // nothing to do :( we should a throw an exception, but it is no good for user
            if (fieldInfo == null)
                return;
            var ov = fieldInfo.GetValue(field);
            if (!(ov is FieldAttributes))
                return;
            var v = (FieldAttributes)ov;

            // protect from parallel execution, when first thread set field readonly back, and second set it to write value
            lock (fieldInfo)
            {
                fieldInfo.SetValue(field, v & ~FieldAttributes.InitOnly);
                field.SetValue(obj, value);
                fieldInfo.SetValue(field, v | FieldAttributes.InitOnly);
            }
        }

        private static object GenerateProcessMethod(Type type, bool unboxStruct)
        {
            if (type.IsArray)
            {
                return GenerateProcessArrayMethod(type);
            }

            if (type.FullName != null && type.FullName.StartsWith("System.Tuple`"))
            {
                // if not safe type it is no guarantee that some type will contain reference to
                // this tuple. In usual way, we're creating new object, setting reference for it
                // and filling data. For tuple, we will fill data before creating object
                // (in constructor arguments)
                var genericArguments = type.GenericArguments();
                // current tuples contain only 8 arguments, but may be in future...
                // we'll write code that works with it
                if (genericArguments.Length < 10 && genericArguments.All(DeepClonerSafeTypes.CanReturnSameObject))
                {
                    return GenerateProcessTupleMethod(type);
                }
            }

            var methodType = unboxStruct || type.IsClass() ? typeof(object) : type;

            var expressionList = new List<Expression>();

            ParameterExpression from = Expression.Parameter(methodType);
            var fromLocal = from;
            var toLocal = Expression.Variable(type);
            var state = Expression.Parameter(typeof(DeepCloneState));

            if (!type.IsValueType())
            {
                var methodInfo = typeof(object).GetPrivateMethod("MemberwiseClone");

                // to = (T)from.MemberwiseClone()
                expressionList.Add(Expression.Assign(toLocal, Expression.Convert(Expression.Call(from, methodInfo), type)));

                fromLocal = Expression.Variable(type);
                // fromLocal = (T)from
                expressionList.Add(Expression.Assign(fromLocal, Expression.Convert(from, type)));

                // added from -> to binding to ensure reference loop handling
                // structs cannot loop here
                // state.AddKnownRef(from, to)
                expressionList.Add(Expression.Call(state, typeof(DeepCloneState).GetMethod("AddKnownRef"), from, toLocal));
            }
            else
            {
                if (unboxStruct)
                {
                    // toLocal = (T)from;
                    expressionList.Add(Expression.Assign(toLocal, Expression.Unbox(from, type)));
                    fromLocal = Expression.Variable(type);
                    // fromLocal = toLocal; // structs, it is ok to copy
                    expressionList.Add(Expression.Assign(fromLocal, toLocal));
                }
                else
                {
                    // toLocal = from
                    expressionList.Add(Expression.Assign(toLocal, from));
                }
            }

            List<FieldInfo> fi = new List<FieldInfo>();
            var tp = type;
            do
            {
#if !NETCORE
                // don't do anything with this dark magic!
                if (tp == typeof(ContextBoundObject)) break;
#else
				if (tp.Name == "ContextBoundObject") break;
#endif

                fi.AddRange(tp.GetDeclaredFields());
                tp = tp.BaseType();
            }
            while (tp != null);

            foreach (var fieldInfo in fi)
            {
                if (!DeepClonerSafeTypes.CanReturnSameObject(fieldInfo.FieldType))
                {
                    var methodInfo = fieldInfo.FieldType.IsValueType()
                                        ? typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneStructInternal")
                                                                    .MakeGenericMethod(fieldInfo.FieldType)
                                        : typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneClassInternal");

                    var get = Expression.Field(fromLocal, fieldInfo);

                    // toLocal.Field = Clone...Internal(fromLocal.Field)
                    var call = (Expression)Expression.Call(methodInfo, get, state);
                    if (!fieldInfo.FieldType.IsValueType())
                        call = Expression.Convert(call, fieldInfo.FieldType);

                    // should handle specially
                    // todo: think about optimization, but it rare case
                    var isReadonly = _readonlyFields.GetOrAdd(fieldInfo, f => f.IsInitOnly);
                    if (isReadonly)
                    {
                        if (_canFastCopyReadonlyFields)
                        {
                            expressionList.Add(Expression.Call(
                                Expression.Constant(fieldInfo),
                                _fieldSetMethod,
                                Expression.Convert(toLocal, typeof(object)),
                                Expression.Convert(call, typeof(object))));
                        }
                        else
                        {
                            var setMethod = typeof(DeepClonerExprGenerator).GetPrivateStaticMethod("ForceSetField");
                            expressionList.Add(Expression.Call(setMethod, Expression.Constant(fieldInfo), Expression.Convert(toLocal, typeof(object)), Expression.Convert(call, typeof(object))));
                        }
                    }
                    else
                    {
                        expressionList.Add(Expression.Assign(Expression.Field(toLocal, fieldInfo), call));
                    }
                }
            }

            expressionList.Add(Expression.Convert(toLocal, methodType));

            var funcType = typeof(Func<,,>).MakeGenericType(methodType, typeof(DeepCloneState), methodType);

            var blockParams = new List<ParameterExpression>();
            if (from != fromLocal) blockParams.Add(fromLocal);
            blockParams.Add(toLocal);

            return Expression.Lambda(funcType, Expression.Block(blockParams, expressionList), from, state).Compile();
        }

        private static object GenerateProcessArrayMethod(Type type)
        {
            var elementType = type.GetElementType();
            var rank = type.GetArrayRank();

            MethodInfo methodInfo;

            // multidim or not zero-based arrays
            if (rank != 1 || type != elementType.MakeArrayType())
            {
                if (rank == 2 && type == elementType.MakeArrayType())
                {
                    // small optimization for 2 dim arrays
                    methodInfo = typeof(DeepClonerGenerator).GetPrivateStaticMethod("Clone2DimArrayInternal").MakeGenericMethod(elementType);
                }
                else
                {
                    methodInfo = typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneAbstractArrayInternal");
                }
            }
            else
            {
                var methodName = "Clone1DimArrayClassInternal";
                if (DeepClonerSafeTypes.CanReturnSameObject(elementType)) methodName = "Clone1DimArraySafeInternal";
                else if (elementType.IsValueType()) methodName = "Clone1DimArrayStructInternal";
                methodInfo = typeof(DeepClonerGenerator).GetPrivateStaticMethod(methodName).MakeGenericMethod(elementType);
            }

            ParameterExpression from = Expression.Parameter(typeof(object));
            var state = Expression.Parameter(typeof(DeepCloneState));
            var call = Expression.Call(methodInfo, Expression.Convert(from, type), state);

            var funcType = typeof(Func<,,>).MakeGenericType(typeof(object), typeof(DeepCloneState), typeof(object));

            return Expression.Lambda(funcType, call, from, state).Compile();
        }

        private static object GenerateProcessTupleMethod(Type type)
        {
            ParameterExpression from = Expression.Parameter(typeof(object));
            var state = Expression.Parameter(typeof(DeepCloneState));

            var local = Expression.Variable(type);
            var assign = Expression.Assign(local, Expression.Convert(from, type));

            var funcType = typeof(Func<object, DeepCloneState, object>);

            var tupleLength = type.GenericArguments().Length;

            var constructor = Expression.Assign(local, Expression.New(type.GetPublicConstructors().First(x => x.GetParameters().Length == tupleLength),
                type.GetPublicProperties().OrderBy(x => x.Name)
                    .Where(x => x.CanRead && x.Name.StartsWith("Item") && char.IsDigit(x.Name[4]))
                    .Select(x => Expression.Property(local, x.Name))));

            return Expression.Lambda(funcType, Expression.Block(new[] { local },
                assign, constructor, Expression.Call(state, typeof(DeepCloneState).GetMethod("AddKnownRef"), from, local),
                    from),
                from, state).Compile();
        }

    }


    internal static class DeepClonerGenerator
    {
        public static T CloneObject<T>(T obj)
        {
            if (obj is ValueType)
            {
                var type = obj.GetType();
                if (typeof(T) == type)
                {
                    if (DeepClonerSafeTypes.CanReturnSameObject(type))
                        return obj;

                    return CloneStructInternal(obj, new DeepCloneState());
                }
            }

            return (T)CloneClassRoot(obj);
        }

        public static object CloneObject2(object obj)
        {
            if (obj is ValueType)
            {
                var type = obj.GetType();
                if (DeepClonerSafeTypes.CanReturnSameObject(type))
                    return obj;

                return CloneStructInternal(obj, new DeepCloneState());
            }

            return CloneClassRoot(obj);
        }


        private static object CloneClassRoot(object obj)
        {
            if (obj == null)
                return null;

            var cloner = (Func<object, DeepCloneState, object>)DeepClonerCache.GetOrAddClass(obj.GetType(), t => GenerateCloner(t, true));

            // null -> should return same type
            if (cloner == null)
                return obj;

            return cloner(obj, new DeepCloneState());
        }

        internal static object CloneClassInternal(object obj, DeepCloneState state)
        {
            if (obj == null)
                return null;

            var cloner = (Func<object, DeepCloneState, object>)DeepClonerCache.GetOrAddClass(obj.GetType(), t => GenerateCloner(t, true));

            // safe ojbect
            if (cloner == null)
                return obj;

            // loop
            var knownRef = state.GetKnownRef(obj);
            if (knownRef != null)
                return knownRef;

            return cloner(obj, state);
        }

        private static T CloneStructInternal<T>(T obj, DeepCloneState state) // where T : struct
        {
            // no loops, no nulls, no inheritance
            var cloner = GetClonerForValueType<T>();

            // safe ojbect
            if (cloner == null)
                return obj;

            return cloner(obj, state);
        }

        // when we can't use code generation, we can use these methods
        internal static T[] Clone1DimArraySafeInternal<T>(T[] obj, DeepCloneState state)
        {
            var l = obj.Length;
            var outArray = new T[l];
            state.AddKnownRef(obj, outArray);
            Array.Copy(obj, outArray, obj.Length);
            return outArray;
        }

        internal static T[] Clone1DimArrayStructInternal<T>(T[] obj, DeepCloneState state)
        {
            // not null from called method, but will check it anyway
            if (obj == null) return null;
            var l = obj.Length;
            var outArray = new T[l];
            state.AddKnownRef(obj, outArray);
            var cloner = GetClonerForValueType<T>();
            for (var i = 0; i < l; i++)
                outArray[i] = cloner(obj[i], state);

            return outArray;
        }

        internal static T[] Clone1DimArrayClassInternal<T>(T[] obj, DeepCloneState state)
        {
            // not null from called method, but will check it anyway
            if (obj == null) return null;
            var l = obj.Length;
            var outArray = new T[l];
            state.AddKnownRef(obj, outArray);
            for (var i = 0; i < l; i++)
                outArray[i] = (T)CloneClassInternal(obj[i], state);

            return outArray;
        }

        // relatively frequent case. specially handled
        internal static T[,] Clone2DimArrayInternal<T>(T[,] obj, DeepCloneState state)
        {
            // not null from called method, but will check it anyway
            if (obj == null) return null;
            var l1 = obj.GetLength(0);
            var l2 = obj.GetLength(1);
            var outArray = new T[l1, l2];
            state.AddKnownRef(obj, outArray);
            if (DeepClonerSafeTypes.CanReturnSameObject(typeof(T)))
            {
                Array.Copy(obj, outArray, obj.Length);
                return outArray;
            }

            if (typeof(T).IsValueType())
            {
                var cloner = GetClonerForValueType<T>();
                for (var i = 0; i < l1; i++)
                    for (var k = 0; k < l2; k++)
                        outArray[i, k] = cloner(obj[i, k], state);
            }
            else
            {
                for (var i = 0; i < l1; i++)
                    for (var k = 0; k < l2; k++)
                        outArray[i, k] = (T)CloneClassInternal(obj[i, k], state);
            }

            return outArray;
        }

        // rare cases, very slow cloning. currently it's ok
        internal static Array CloneAbstractArrayInternal(Array obj, DeepCloneState state)
        {
            // not null from called method, but will check it anyway
            if (obj == null) return null;
            var rank = obj.Rank;

            var lowerBounds = Enumerable.Range(0, rank).Select(obj.GetLowerBound).ToArray();
            var lengths = Enumerable.Range(0, rank).Select(obj.GetLength).ToArray();
            var idxes = Enumerable.Range(0, rank).Select(obj.GetLowerBound).ToArray();

            var outArray = Array.CreateInstance(obj.GetType().GetElementType(), lengths, lowerBounds);
            state.AddKnownRef(obj, outArray);
            while (true)
            {
                outArray.SetValue(CloneClassInternal(obj.GetValue(idxes), state), idxes);
                var ofs = rank - 1;
                while (true)
                {
                    idxes[ofs]++;
                    if (idxes[ofs] >= lowerBounds[ofs] + lengths[ofs])
                    {
                        idxes[ofs] = lowerBounds[ofs];
                        ofs--;
                        if (ofs < 0) return outArray;
                    }
                    else
                        break;
                }
            }
        }

        internal static Func<T, DeepCloneState, T> GetClonerForValueType<T>()
        {
            return (Func<T, DeepCloneState, T>)DeepClonerCache.GetOrAddStructAsObject(typeof(T), t => GenerateCloner(t, false));
        }

        private static object GenerateCloner(Type t, bool asObject)
        {
            if (DeepClonerSafeTypes.CanReturnSameObject(t) && (asObject && !t.IsValueType()))
                return null;

#if !NETCORE
            if (ShallowObjectCloner.IsSafeVariant()) return DeepClonerExprGenerator.GenerateClonerInternal(t, asObject);
            else return DeepClonerMsilGenerator.GenerateClonerInternal(t, asObject);
#else
			return DeepClonerExprGenerator.GenerateClonerInternal(t, asObject);
#endif
        }

        public static object CloneObjectTo(object objFrom, object objTo, bool isDeep)
        {
            if (objTo == null) return null;

            if (objFrom == null)
                throw new ArgumentNullException("objFrom", "Cannot copy null object to another");
            var type = objFrom.GetType();
            if (!type.IsInstanceOfType(objTo))
                throw new InvalidOperationException("From object should be derived from From object, but From object has type " + objFrom.GetType().FullName + " and to " + objTo.GetType().FullName);
            if (objFrom is string)
                throw new InvalidOperationException("It is forbidden to clone strings");
            var cloner = (Func<object, object, DeepCloneState, object>)(isDeep
                ? DeepClonerCache.GetOrAddDeepClassTo(type, t => ClonerToExprGenerator.GenerateClonerInternal(t, true))
                : DeepClonerCache.GetOrAddShallowClassTo(type, t => ClonerToExprGenerator.GenerateClonerInternal(t, false)));
            if (cloner == null) return objTo;
            return cloner(objFrom, objTo, new DeepCloneState());
        }
    }


    internal static class DeepClonerMsilHelper
    {
        public static bool IsConstructorDoNothing(Type type, ConstructorInfo constructor)
        {
            if (constructor == null) return false;
            try
            {
                // will not try to determine body for this types
                if (type.IsGenericType || type.IsContextful || type.IsCOMObject || type.Assembly.IsDynamic) return false;

                var methodBody = constructor.GetMethodBody();

                // this situation can be for com
                if (methodBody == null) return false;

                var ilAsByteArray = methodBody.GetILAsByteArray();
                if (ilAsByteArray.Length == 7
                    && ilAsByteArray[0] == 0x02 // Ldarg_0
                    && ilAsByteArray[1] == 0x28 // newobj
                    && ilAsByteArray[6] == 0x2a // ret
                    && type.Module.ResolveMethod(BitConverter.ToInt32(ilAsByteArray, 2)) == typeof(object).GetConstructor(Type.EmptyTypes)) // call object
                {
                    return true;
                }
                else if (ilAsByteArray.Length == 1 && ilAsByteArray[0] == 0x2a) // ret
                {
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                // no permissions or something similar
                return false;
            }
        }
    }

    /// <summary>
    /// Safe types are types, which can be copied without real cloning. e.g. simple structs or strings (it is immutable)
    /// </summary>
    internal static class DeepClonerSafeTypes
    {
        internal static readonly ConcurrentDictionary<Type, bool> KnownTypes = new ConcurrentDictionary<Type, bool>();

        static DeepClonerSafeTypes()
        {
            foreach (
                var x in
                    new[]
                        {
                            typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
                            typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string), typeof(bool), typeof(DateTime),
                            typeof(IntPtr), typeof(UIntPtr), typeof(Guid),
							// do not clone such native type
							Type.GetType("System.RuntimeType"),
                            Type.GetType("System.RuntimeTypeHandle"),
#if !NETCORE
							typeof(DBNull)
#endif
						}) KnownTypes.TryAdd(x, true);
        }

        private static bool CanReturnSameType(Type type, HashSet<Type> processingTypes)
        {
            bool isSafe;
            if (KnownTypes.TryGetValue(type, out isSafe))
                return isSafe;

            // enums are safe
            // pointers (e.g. int*) are unsafe, but we cannot do anything with it except blind copy
            if (type.IsEnum() || type.IsPointer)
            {
                KnownTypes.TryAdd(type, true);
                return true;
            }

#if !NETCORE
            // do not do anything with remoting. it is very dangerous to clone, bcs it relate to deep core of framework
            if (type.FullName.StartsWith("System.Runtime.Remoting.")
                && type.Assembly == typeof(System.Runtime.Remoting.CustomErrorsModes).Assembly)
            {
                KnownTypes.TryAdd(type, true);
                return true;
            }

            if (type.FullName.StartsWith("System.Reflection.") && type.Assembly == typeof(PropertyInfo).Assembly)
            {
                KnownTypes.TryAdd(type, true);
                return true;
            }

            // catched by previous condition
            /*if (type.FullName.StartsWith("System.Reflection.Emit") && type.Assembly == typeof(System.Reflection.Emit.OpCode).Assembly)
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}*/

            // this types are serious native resources, it is better not to clone it
            if (type.IsSubclassOf(typeof(System.Runtime.ConstrainedExecution.CriticalFinalizerObject)))
            {
                KnownTypes.TryAdd(type, true);
                return true;
            }

            // Better not to do anything with COM
            if (type.IsCOMObject)
            {
                KnownTypes.TryAdd(type, true);
                return true;
            }
#else
			// do not copy db null
			if (type.FullName.StartsWith("System.DBNull"))
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}

			if (type.FullName.StartsWith("System.RuntimeType"))
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}
			
			if (type.FullName.StartsWith("System.Reflection.") && Equals(type.GetTypeInfo().Assembly, typeof(PropertyInfo).GetTypeInfo().Assembly))
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}

			if (type.IsSubclassOfTypeByName("CriticalFinalizerObject"))
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}
			
			// better not to touch ms dependency injection
			if (type.FullName.StartsWith("Microsoft.Extensions.DependencyInjection."))
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}

			if (type.FullName == "Microsoft.EntityFrameworkCore.Internal.ConcurrencyDetector")
			{
				KnownTypes.TryAdd(type, true);
				return true;
			}
#endif

            // classes are always unsafe (we should copy it fully to count references)
            if (!type.IsValueType())
            {
                KnownTypes.TryAdd(type, false);
                return false;
            }

            if (processingTypes == null)
                processingTypes = new HashSet<Type>();

            // structs cannot have a loops, but check it anyway
            processingTypes.Add(type);

            List<FieldInfo> fi = new List<FieldInfo>();
            var tp = type;
            do
            {
                fi.AddRange(tp.GetAllFields());
                tp = tp.BaseType();
            }
            while (tp != null);

            foreach (var fieldInfo in fi)
            {
                // type loop
                var fieldType = fieldInfo.FieldType;
                if (processingTypes.Contains(fieldType))
                    continue;

                // not safe and not not safe. we need to go deeper
                if (!CanReturnSameType(fieldType, processingTypes))
                {
                    KnownTypes.TryAdd(type, false);
                    return false;
                }
            }

            KnownTypes.TryAdd(type, true);
            return true;
        }

        // not used anymore
        /*/// <summary>
		/// Classes with only safe fields are safe for ShallowClone (if they root objects for copying)
		/// </summary>
		private static bool CanCopyClassInShallow(Type type)
		{
			// do not do this anything for struct and arrays
			if (!type.IsClass() || type.IsArray)
			{
				return false;
			}

			List<FieldInfo> fi = new List<FieldInfo>();
			var tp = type;
			do
			{
				fi.AddRange(tp.GetAllFields());
				tp = tp.BaseType();
			}
			while (tp != null);

			if (fi.Any(fieldInfo => !CanReturnSameType(fieldInfo.FieldType, null)))
			{
				return false;
			}

			return true;
		}*/

        public static bool CanReturnSameObject(Type type)
        {
            return CanReturnSameType(type, null);
        }
    }

    internal class DeepCloneState
    {
        private class CustomEqualityComparer : IEqualityComparer<object>, IEqualityComparer
        {
            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            bool IEqualityComparer.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
#pragma warning disable RS1024 // シンボルを正しく比較する
                return RuntimeHelpers.GetHashCode(obj);
#pragma warning restore RS1024 // シンボルを正しく比較する
            }
        }

        private MiniDictionary _loops;

        private readonly object[] _baseFromTo = new object[6];

        private int _idx;

        public object GetKnownRef(object from)
        {
            // this is faster than call Diectionary from begin
            // also, small poco objects does not have a lot of references
            var baseFromTo = _baseFromTo;
            if (ReferenceEquals(from, baseFromTo[0])) return baseFromTo[3];
            if (ReferenceEquals(from, baseFromTo[1])) return baseFromTo[4];
            if (ReferenceEquals(from, baseFromTo[2])) return baseFromTo[5];
            if (_loops == null)
                return null;

            return _loops.FindEntry(from);
        }

        public void AddKnownRef(object from, object to)
        {
            if (_idx < 3)
            {
                _baseFromTo[_idx] = from;
                _baseFromTo[_idx + 3] = to;
                _idx++;
                return;
            }

            if (_loops == null)
                _loops = new MiniDictionary();
            _loops.Insert(from, to);
        }

        private class MiniDictionary
        {
            private struct Entry
            {
                public int HashCode;
                public int Next;
                public object Key;
                public object Value;
            }

            private int[] _buckets;
            private Entry[] _entries;
            private int _count;


            public MiniDictionary() : this(5)
            {
            }

            public MiniDictionary(int capacity)
            {
                if (capacity > 0)
                    Initialize(capacity);
            }

            public object FindEntry(object key)
            {
                if (_buckets != null)
                {
#pragma warning disable RS1024 // シンボルを正しく比較する
                    var hashCode = RuntimeHelpers.GetHashCode(key) & 0x7FFFFFFF;
#pragma warning restore RS1024 // シンボルを正しく比較する
                    var entries1 = _entries;
                    for (var i = _buckets[hashCode % _buckets.Length]; i >= 0; i = entries1[i].Next)
                    {
                        if (entries1[i].HashCode == hashCode && ReferenceEquals(entries1[i].Key, key))
                            return entries1[i].Value;
                    }
                }

                return null;
            }

            private static readonly int[] _primes =
            {
                3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
                1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
                17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
                187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
                1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
            };

            private static int GetPrime(int min)
            {
                for (var i = 0; i < _primes.Length; i++)
                {
                    var prime = _primes[i];
                    if (prime >= min) return prime;
                }

                //outside of our predefined table. 
                //compute the hard way. 
                for (var i = min | 1; i < int.MaxValue; i += 2)
                {
                    if (IsPrime(i) && (i - 1) % 101 != 0)
                        return i;
                }

                return min;
            }

            private static bool IsPrime(int candidate)
            {
                if ((candidate & 1) != 0)
                {
                    var limit = (int)Math.Sqrt(candidate);
                    for (var divisor = 3; divisor <= limit; divisor += 2)
                    {
                        if ((candidate % divisor) == 0)
                            return false;
                    }

                    return true;
                }

                return candidate == 2;
            }

            private static int ExpandPrime(int oldSize)
            {
                var newSize = 2 * oldSize;

                if ((uint)newSize > 0x7FEFFFFD && 0x7FEFFFFD > oldSize)
                {
                    return 0x7FEFFFFD;
                }

                return GetPrime(newSize);
            }

            private void Initialize(int size)
            {
                _buckets = new int[size];
                for (int i = 0; i < _buckets.Length; i++)
                    _buckets[i] = -1;
                _entries = new Entry[size];
            }

            public void Insert(object key, object value)
            {
                if (_buckets == null) Initialize(0);
#pragma warning disable RS1024 // シンボルを正しく比較する
                var hashCode = RuntimeHelpers.GetHashCode(key) & 0x7FFFFFFF;
#pragma warning restore RS1024 // シンボルを正しく比較する
                var targetBucket = hashCode % _buckets.Length;

                var entries1 = _entries;

                // we're always checking for entry before adding new
                // so this loop is useless
                /*for (var i = _buckets[targetBucket]; i >= 0; i = entries1[i].Next)
				{
					if (entries1[i].HashCode == hashCode && ReferenceEquals(entries1[i].Key, key))
					{
						entries1[i].Value = value;
						return;
					}
				}*/

                if (_count == entries1.Length)
                {
                    Resize();
                    entries1 = _entries;
                    targetBucket = hashCode % _buckets.Length;
                }

                var index = _count;
                _count++;

                entries1[index].HashCode = hashCode;
                entries1[index].Next = _buckets[targetBucket];
                entries1[index].Key = key;
                entries1[index].Value = value;
                _buckets[targetBucket] = index;
            }

            private void Resize()
            {
                Resize(ExpandPrime(_count));
            }

            private void Resize(int newSize)
            {
                var newBuckets = new int[newSize];
                for (int i = 0; i < newBuckets.Length; i++)
                    newBuckets[i] = -1;
                var newEntries = new Entry[newSize];
                Array.Copy(_entries, 0, newEntries, 0, _count);

                for (var i = 0; i < _count; i++)
                {
                    if (newEntries[i].HashCode >= 0)
                    {
                        var bucket = newEntries[i].HashCode % newSize;
                        newEntries[i].Next = newBuckets[bucket];
                        newBuckets[bucket] = i;
                    }
                }

                _buckets = newBuckets;
                _entries = newEntries;
            }
        }
    }


    internal static class ReflectionHelper
    {
        public static bool IsEnum(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().IsEnum;
#else
            return t.IsEnum;
#endif
        }

        public static bool IsValueType(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().IsValueType;
#else
            return t.IsValueType;
#endif
        }

        public static bool IsClass(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().IsClass;
#else
            return t.IsClass;
#endif
        }

        public static Type BaseType(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().BaseType;
#else
            return t.BaseType;
#endif
        }

        public static FieldInfo[] GetAllFields(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().DeclaredFields.Where(x => !x.IsStatic).ToArray();
#else
            return t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
#endif
        }

        public static PropertyInfo[] GetPublicProperties(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().DeclaredProperties.ToArray();
#else
            return t.GetProperties(BindingFlags.Instance | BindingFlags.Public);
#endif
        }

        public static FieldInfo[] GetDeclaredFields(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().DeclaredFields.Where(x => !x.IsStatic).ToArray();
#else
            return t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
#endif
        }

        public static ConstructorInfo[] GetPrivateConstructors(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().DeclaredConstructors.ToArray();
#else
            return t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
#endif
        }

        public static ConstructorInfo[] GetPublicConstructors(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().DeclaredConstructors.ToArray();
#else
            return t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
#endif
        }

        public static MethodInfo GetPrivateMethod(this Type t, string methodName)
        {
#if NETCORE
			return t.GetTypeInfo().GetDeclaredMethod(methodName);
#else
            return t.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
#endif
        }

        public static MethodInfo GetMethod(this Type t, string methodName)
        {
#if NETCORE
			return t.GetTypeInfo().GetDeclaredMethod(methodName);
#else
            return t.GetMethod(methodName);
#endif
        }

        public static MethodInfo GetPrivateStaticMethod(this Type t, string methodName)
        {
#if NETCORE
			return t.GetTypeInfo().GetDeclaredMethod(methodName);
#else
            return t.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
#endif
        }

        public static FieldInfo GetPrivateField(this Type t, string fieldName)
        {
#if NETCORE
			return t.GetTypeInfo().GetDeclaredField(fieldName);
#else
            return t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
#endif
        }

        public static FieldInfo GetPrivateStaticField(this Type t, string fieldName)
        {
#if NETCORE
			return t.GetTypeInfo().GetDeclaredField(fieldName);
#else
            return t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
#endif
        }

#if NETCORE
		public static bool IsSubclassOfTypeByName(this Type t, string typeName)
		{
			while (t != null)
			{
				if (t.Name == typeName)
					return true;
				t = t.BaseType();
			}

			return false;
		}
#endif

#if NETCORE
		public static bool IsAssignableFrom(this Type from, Type to)
		{
			return from.GetTypeInfo().IsAssignableFrom(to.GetTypeInfo());
		}

		public static bool IsInstanceOfType(this Type from, object to)
		{
			return from.IsAssignableFrom(to.GetType());
		}
#endif

        public static Type[] GenericArguments(this Type t)
        {
#if NETCORE
			return t.GetTypeInfo().GenericTypeArguments;
#else
            return t.GetGenericArguments();
#endif
        }
    }

    internal static class ShallowClonerGenerator
    {
        public static T CloneObject<T>(T obj)
        {
            // this is faster than typeof(T).IsValueType
            if (obj is ValueType)
            {
                if (typeof(T) == obj.GetType())
                    return obj;

                // we're here so, we clone value type obj as object type T
                // so, we need to copy it, bcs we have a reference, not real object.
                return (T)ShallowObjectCloner.CloneObject(obj);
            }

            if (ReferenceEquals(obj, null))
                return (T)(object)null;

            if (DeepClonerSafeTypes.CanReturnSameObject(obj.GetType()))
                return obj;

            return (T)ShallowObjectCloner.CloneObject(obj);
        }
    }

    /// <summary>
    /// Internal class but due implementation restriction should be public
    /// </summary>
    public abstract class ShallowObjectCloner
    {
        /// <summary>
        /// Abstract method for real object cloning
        /// </summary>
        protected abstract object DoCloneObject(object obj);

        private static readonly ShallowObjectCloner _unsafeInstance;

        private static ShallowObjectCloner _instance;

        /// <summary>
        /// Performs real shallow object clone
        /// </summary>
        public static object CloneObject(object obj)
        {
            return _instance.DoCloneObject(obj);
        }

        internal static bool IsSafeVariant()
        {
            return _instance is ShallowSafeObjectCloner;
        }

        static ShallowObjectCloner()
        {
#if !NETCORE
            _unsafeInstance = GenerateUnsafeCloner();
            _instance = _unsafeInstance;
            try
            {
                _instance.DoCloneObject(new object());
            }
            catch (Exception)
            {
                // switching to safe
                _instance = new ShallowSafeObjectCloner();
            }
#else
			_instance = new ShallowSafeObjectCloner();
			// no unsafe variant for core
			_unsafeInstance = _instance;
#endif
        }

        /// <summary>
        /// Purpose of this method is testing variants
        /// </summary>
        internal static void SwitchTo(bool isSafe)
        {
            DeepClonerCache.ClearCache();
            if (isSafe) _instance = new ShallowSafeObjectCloner();
            else _instance = _unsafeInstance;
        }

#if !NETCORE
        private static ShallowObjectCloner GenerateUnsafeCloner()
        {
            var mb = TypeCreationHelper.GetModuleBuilder();

            var builder = mb.DefineType("ShallowSafeObjectClonerImpl", TypeAttributes.Public, typeof(ShallowObjectCloner));
            var ctorBuilder = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis | CallingConventions.HasThis, Type.EmptyTypes);

            var cil = ctorBuilder.GetILGenerator();
            cil.Emit(OpCodes.Ldarg_0);
            // ReSharper disable AssignNullToNotNullAttribute
            cil.Emit(OpCodes.Call, typeof(ShallowObjectCloner).GetPrivateConstructors()[0]);
            // ReSharper restore AssignNullToNotNullAttribute
            cil.Emit(OpCodes.Ret);

            var methodBuilder = builder.DefineMethod(
                "DoCloneObject",
                MethodAttributes.Public | MethodAttributes.Virtual,
                CallingConventions.HasThis,
                typeof(object),
                new[] { typeof(object) });

            var il = methodBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(object).GetPrivateMethod("MemberwiseClone"));
            il.Emit(OpCodes.Ret);
            var type = builder.CreateType();
            return (ShallowObjectCloner)Activator.CreateInstance(type);
        }
#endif

        private class ShallowSafeObjectCloner : ShallowObjectCloner
        {
            private static readonly Func<object, object> _cloneFunc;

            static ShallowSafeObjectCloner()
            {
                var methodInfo = typeof(object).GetPrivateMethod("MemberwiseClone");
                var p = Expression.Parameter(typeof(object));
                var mce = Expression.Call(p, methodInfo);
                _cloneFunc = Expression.Lambda<Func<object, object>>(mce, p).Compile();
            }

            protected override object DoCloneObject(object obj)
            {
                return _cloneFunc(obj);
            }
        }
    }


}

#endif


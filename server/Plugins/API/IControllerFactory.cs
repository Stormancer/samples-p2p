// MIT License
//
// Copyright (c) 2019 Stormancer
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
using Stormancer.Core;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using Stormancer.Plugins;
using Stormancer;
using System.Linq.Expressions;
using Stormancer.Diagnostics;
using System.IO;

namespace Server.Plugins.API
{
    public class Constants
    {
        public const string ApiRequestTag = "AutofacWebRequest";
    }
    public enum ApiAccess
    {
        Public,
        Scene2Scene
    }
    public enum ApiType
    {
        Rpc,
        FireForget
    }
    public class ApiAttribute : Attribute
    {
        public ApiAttribute(ApiAccess access, ApiType type)
        {
            Access = access;
            Type = type;
        }

        public ApiAccess Access { get; }
        public ApiType Type { get; }
        public string Route { get; set; }
    }

    public interface IControllerFactory
    {
        void RegisterControllers();
        
    }

    public class ControllerFactory<T> : IControllerFactory where T : ControllerBase
    {
        
        private readonly ISceneHost _scene;

        
        public ControllerFactory(ISceneHost scene)
        {
            _scene = scene;

        }


        private async Task ExecuteConnectionRejected(IScenePeerClient client)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag))
            {
                var controller = scope.Resolve<T>();
                await controller.OnConnectionRejected(client);
            }
        }

        private async Task ExecuteConnecting(IScenePeerClient client)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag))
            {
                var controller = scope.Resolve<T>();
                await controller.OnConnecting(client);
            }
        }

        private async Task ExecuteConnected(IScenePeerClient client)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag))
            {
                var controller = scope.Resolve<T>();
                await controller.OnConnected(client);
            }
        }

        private async Task ExecuteDisconnected(DisconnectedArgs args)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag))
            {
                var controller = scope.Resolve<T>();
                await controller.OnDisconnected(args);
            }
        }


        private async Task ExecuteRouteAction(ApiCallContext<Packet<IScenePeerClient>> ctx, Func<T, Packet<IScenePeerClient>, IDependencyResolver, Task> action)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag, ctx.Builder))
            {
                var controller = scope.Resolve<T>();
                controller.Peer = ctx.Context.Connection;

                try
                {
                    if (controller == null)
                    {
                        throw new InvalidOperationException("The controller could not be found. Make sure it has been properly registered in the dependency manager.");
                    }
                    await action(controller, ctx.Context, scope);
                }
                catch (Exception ex)
                {
                    if (controller == null || !await controller.HandleException(new ApiExceptionContext(ctx.Route, ex, ctx.Context)))
                    {
                        scope.Resolve<ILogger>().Log(LogLevel.Error, ctx.Route, $"An exception occurred while executing action '{ctx.Route}' in controller '{typeof(T).Name}'.", ex);
                        throw;
                    }
                }
            }
        }

        private async Task ExecuteRouteAction(ApiCallContext<Packet<IScenePeer>> ctx, Func<T, Packet<IScenePeer>, IDependencyResolver, Task> action)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag, ctx.Builder))
            {
                var controller = scope.Resolve<T>();
                //controller.Request = ctx;
                controller.Peer = ctx.Context.Connection;
                try
                {
                    if (controller == null)
                    {
                        throw new InvalidOperationException("The controller could not be found. Make sure it has been properly registered in the dependency manager.");
                    }
                    await action(controller, ctx.Context, scope);

                }
                catch (Exception ex)
                {

                    scope.Resolve<ILogger>().Log(LogLevel.Error, ctx.Route, $"An exception occurred while executing action '{ctx.Route}' in controller '{typeof(T).Name}'.", ex);
                    throw;

                }
            }
        }

        private async Task ExecuteRpcAction(ApiCallContext<RequestContext<IScenePeerClient>> ctx, Func<T, RequestContext<IScenePeerClient>, IDependencyResolver, Task> action)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag, ctx.Builder))
            {
                var controller = scope.Resolve<T>();
                try
                {
                    if (controller == null)
                    {
                        throw new InvalidOperationException("The controller could not be found. Make sure it has been properly registered in the dependency manager.");
                    }
                    controller.Request = ctx.Context;
                    controller.CancellationToken = ctx.Context.CancellationToken;
                    controller.Peer = ctx.Context.RemotePeer;

                    await action(controller, ctx.Context, scope);
                }
                catch (ClientException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (controller == null || !await controller.HandleException(new ApiExceptionContext(ctx.Route, ex, ctx.Context)))
                    {
                        scope.Resolve<ILogger>().Log(LogLevel.Error, ctx.Route, $"An exception occurred while executing action '{ctx.Route}' in controller '{typeof(T).Name}'.", ex);
                        throw;
                    }
                }
            }
        }

        private async Task ExecuteRpcAction(ApiCallContext<RequestContext<IScenePeer>> ctx, Func<T, RequestContext<IScenePeer>, IDependencyResolver, Task> action)
        {
            using (var scope = _scene.DependencyResolver.CreateChild(Constants.ApiRequestTag, ctx.Builder))
            {
                var controller = scope.Resolve<T>();
                try
                {
                    if (controller == null)
                    {
                        throw new InvalidOperationException("The controller could not be found. Make sure it has been properly registered in the dependency manager.");
                    }
                    //controller.Request = ctx;
                    controller.CancellationToken = ctx.Context.CancellationToken;
                    controller.Peer = ctx.Context.RemotePeer;

                    await action(controller, ctx.Context, scope);
                }
                catch (ClientException)
                {
                    throw;
                }
                catch (Exception ex)
                {

                    scope.Resolve<ILogger>().Log(LogLevel.Error, ctx.Route, $"An exception occurred while executing action '{ctx.Route}' in controller '{typeof(T).Name}'.", ex);
                    throw;

                }
            }
        }

        public void RegisterControllers()
        {
            var type = typeof(T);

            _scene.Connecting.Add(p => ExecuteConnecting(p));
            _scene.ConnectionRejected.Add(p => ExecuteConnectionRejected(p));
            _scene.Connected.Add(p => ExecuteConnected(p));
            _scene.Disconnected.Add(args => ExecuteDisconnected(args));
            foreach (var method in type.GetMethods())
            {
                var procedureName = GetProcedureName(type, method);
                if (IsRawRpc<IScenePeerClient>(method))
                {

                    var ctxParam = Expression.Parameter(typeof(RequestContext<IScenePeerClient>), "ctx");
                    var controllerParam = Expression.Parameter(typeof(T), "controller");
                    var callExpr = Expression.Call(controllerParam, method, ctxParam);



                    var action = Expression.Lambda<Func<T, RequestContext<IScenePeerClient>, Task>>(callExpr, controllerParam, ctxParam).Compile();


                    _scene.AddProcedure(procedureName, ctx =>
                    {
                        var initialApiCallCtx = new ApiCallContext<RequestContext<IScenePeerClient>>(ctx, method, procedureName);

                        Func<ApiCallContext<RequestContext<IScenePeerClient>>, Task> next = apiCallContext => ExecuteRpcAction(initialApiCallCtx, (c, pctx, r) => action(c, pctx));
                        foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                        {
                            next = WrapWithHandler(handler.RunRpc, next);
                        }
                        return next(initialApiCallCtx);
                    });
                }
                else if (IsRawRoute<IScenePeerClient>(method))
                {
                    var ctxParam = Expression.Parameter(typeof(Packet<IScenePeerClient>), "ctx");
                    var controllerParam = Expression.Parameter(typeof(T), "controller");
                    var callExpr = Expression.Call(controllerParam, method, ctxParam);

                    var action = Expression.Lambda<Func<T, Packet<IScenePeerClient>, Task>>(callExpr, controllerParam, ctxParam).Compile();
                    _scene.AddRoute(procedureName, packet =>
                    {
                        var initialApiCallCtx = new ApiCallContext<Packet<IScenePeerClient>>(packet, method, procedureName);
                        Func<ApiCallContext<Packet<IScenePeerClient>>, Task> next = apiCallContext => ExecuteRouteAction(initialApiCallCtx, (c, pctx, r) => action(c, pctx));
                        foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                        {
                            next = WrapWithHandler(handler.RunFF, next);
                        }
                        return next(initialApiCallCtx);
                    }, _ => _);
                }
                else if (IsRawRpc<IScenePeer>(method))
                {
                    var ctxParam = Expression.Parameter(typeof(RequestContext<IScenePeer>), "ctx");
                    var controllerParam = Expression.Parameter(typeof(T), "controller");
                    var callExpr = Expression.Call(controllerParam, method, ctxParam);

                    var action = Expression.Lambda<Func<T, RequestContext<IScenePeer>, Task>>(callExpr, controllerParam, ctxParam).Compile();
                    _scene.Starting.Add(async parameters =>
                    {
                        _scene.DependencyResolver.Resolve<RpcService>().AddInternalProcedure(procedureName, ctx =>
                        {
                            var initialApiCallCtx = new ApiCallContext<RequestContext<IScenePeer>>(ctx, method, procedureName);
                            Func<ApiCallContext<RequestContext<IScenePeer>>, Task> next = apiCallContext => ExecuteRpcAction(initialApiCallCtx, (c, pctx, r) => action(c, pctx));
                            foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                            {
                                next = WrapWithHandler(handler.RunRpc, next);
                            }

                            return next(initialApiCallCtx);
                        }, false);
                        await Task.FromResult(true);
                    });

                }
                else if (IsRawRoute<IScenePeer>(method))
                {
                    var ctxParam = Expression.Parameter(typeof(Packet<IScenePeer>), "ctx");
                    var controllerParam = Expression.Parameter(typeof(T), "controller");
                    var callExpr = Expression.Call(controllerParam, method, ctxParam);

                    var action = Expression.Lambda<Func<T, Packet<IScenePeer>, Task>>(callExpr, controllerParam, ctxParam).Compile();
                    _scene.AddInternalRoute(procedureName, packet =>
                    {

                        var initialApiCallCtx = new ApiCallContext<Packet<IScenePeer>>(packet, method, procedureName);
                        Func<ApiCallContext<Packet<IScenePeer>>, Task> next = apiCallContext => ExecuteRouteAction(initialApiCallCtx, (c, pctx, r) => action(c, pctx));
                        foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                        {
                            next = WrapWithHandler(handler.RunFF, next);
                        }
                        return next(initialApiCallCtx);
                    }, _ => _);
                }
                else
                {
                    TryAddRoute(type, method);
                }
            }

        }

        private bool TryAddRoute(Type controllerType, MethodInfo method)
        {
            var attr = method.GetCustomAttribute<ApiAttribute>();
            if (attr == null)
            {
                return false;
            }
            var route = attr.Route ?? GetProcedureName(controllerType, method, false);
            var returnType = method.ReturnType;
            if (attr.Type == ApiType.FireForget && returnType != typeof(Task) && returnType != typeof(void))
            {
                return false;
            }



            if (attr.Access == ApiAccess.Public)
            {

                if (attr.Type == ApiType.FireForget)
                {

                    var a = CreateExecuteActionFunction<Packet<IScenePeerClient>>(method);
                    _scene.AddRoute(route, packet =>
                    {
                        var initialApiCallCtx = new ApiCallContext<Packet<IScenePeerClient>>(packet, method, route);
                        Func<ApiCallContext<Packet<IScenePeerClient>>, Task> next = apiCallContext => ExecuteRouteAction(initialApiCallCtx, (c, pctx, r) => a(c, pctx, r));
                        foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                        {
                            next = WrapWithHandler(handler.RunFF, next);
                        }
                        return next(initialApiCallCtx);
                    }, _ => _);
                }
                else
                {
                    var a = CreateExecuteActionFunction<RequestContext<IScenePeerClient>>(method);
                    _scene.AddProcedure(route, packet =>
                    {
                        var initialApiCallCtx = new ApiCallContext<RequestContext<IScenePeerClient>>(packet, method, route);
                        Func<ApiCallContext<RequestContext<IScenePeerClient>>, Task> next = apiCallContext => ExecuteRpcAction(initialApiCallCtx, (c, pctx, r) => a(c, pctx, r));
                        foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                        {
                            next = WrapWithHandler(handler.RunRpc, next);
                        }
                        return next(initialApiCallCtx);
                    }, true);
                }

            }
            else //S2S
            {

                if (attr.Type == ApiType.FireForget)
                {
                    var a = CreateExecuteActionFunction<Packet<IScenePeer>>(method);
                    _scene.AddInternalRoute(route, packet =>
                    {
                        var initialApiCallCtx = new ApiCallContext<Packet<IScenePeer>>(packet, method, route);
                        Func<ApiCallContext<Packet<IScenePeer>>, Task> next = apiCallContext => ExecuteRouteAction(initialApiCallCtx, (c, pctx, r) => a(c, pctx, r));
                        foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                        {
                            next = WrapWithHandler(handler.RunFF, next);
                        }
                        return next(initialApiCallCtx);
                    }, _ => _);
                }
                else
                {
                    var a = CreateExecuteActionFunction<RequestContext<IScenePeer>>(method);
                    _scene.Starting.Add(d =>
                    {
                        _scene.DependencyResolver.Resolve<RpcService>().AddInternalProcedure(route, packet =>
                        {
                            var initialApiCallCtx = new ApiCallContext<RequestContext<IScenePeer>>(packet, method, route);
                            Func<ApiCallContext<RequestContext<IScenePeer>>, Task> next = apiCallContext => ExecuteRpcAction(initialApiCallCtx, (c, pctx, r) => a(c, pctx, r));
                            foreach (var handler in _scene.DependencyResolver.Resolve<Func<IEnumerable<IApiHandler>>>()())
                            {
                                next = WrapWithHandler(handler.RunRpc, next);
                            }

                            return next(initialApiCallCtx);
                        }, true);
                        return Task.CompletedTask;
                    });
                }
            }
            return true;
        }

        private Func<ApiCallContext<TCtx>, Task> WrapWithHandler<TCtx>(Func<ApiCallContext<TCtx>, Func<ApiCallContext<TCtx>, Task>, Task> handler, Func<ApiCallContext<TCtx>, Task> current)
        {
            return apiCallCtx => handler(apiCallCtx, current);
        }

        private bool IsRawRpc<TPeer>(MethodInfo method) where TPeer : IScenePeer
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                return false;
            }

            return method.ReturnType.IsAssignableFrom(typeof(Task)) && parameters[0].ParameterType == typeof(RequestContext<TPeer>);
        }
        private bool IsRawRoute<TPeer>(MethodInfo method) where TPeer : IScenePeer
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                return false;
            }
            return method.ReturnType == typeof(Task) && parameters[0].ParameterType == typeof(Packet<TPeer>);
        }




        private string GetProcedureName(Type controller, MethodInfo method, bool toLowerCase = true)
        {
            var root = toLowerCase ? controller.Name.ToLowerInvariant() : controller.Name;
            if (root.EndsWith("controller", StringComparison.InvariantCultureIgnoreCase))
            {
                root = root.Substring(0, root.Length - "Controller".Length);
            }
            return toLowerCase ? (root + "." + method.Name).ToLowerInvariant() : root + "." + method.Name;
        }



        private static Func<T, TRq, IDependencyResolver, Task> CreateExecuteActionFunction<TRq>(MethodInfo method)
        {
            var returnType = method.ReturnType;
            if (returnType == typeof(void) || returnType == typeof(Task))
            {
                return ApiHelpers.CreateExecuteActionFunctionVoid<TRq>(method);
            }
            else
            {
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    returnType = returnType.GetGenericArguments()[0];

                }

                //public static Func<T, TRq, IDependencyResolver, Task<TReturn>> CreateExecuteActionFunction<TRq, TReturn>(MethodInfo method)
                var executeFunction = Expression.Constant(
                    typeof(ApiHelpers).GetRuntimeMethodExt("CreateExecuteActionFunction", p => true).MakeGenericMethod(typeof(TRq), returnType).Invoke(null, new[] { method }),
                    typeof(Func<,,,>).MakeGenericType(typeof(T), typeof(TRq), typeof(IDependencyResolver), typeof(Task<>).MakeGenericType(returnType))
                    );

                //public static Func<TRq, TReturn, IDependencyResolver> CreateWriteResultLambda<TRq, TReturn>()
                var sendResultFunction = Expression.Constant(
                    typeof(ApiHelpers).GetRuntimeMethodExt("CreateWriteResultLambda", p => true).MakeGenericMethod(typeof(TRq), returnType).Invoke(null, new object[] { }),
                    typeof(Func<,,,>).MakeGenericType(typeof(TRq), returnType, typeof(IDependencyResolver),typeof(Task))
                    );

                //public static async Task ExecuteActionAndSendResult<TRq, TReturn>(T controller, TRq ctx, IDependencyResolver resolver, Func<T, TRq, IDependencyResolver, Task<TReturn>> executeControllerActionFunction, Action<TRq, TReturn, IDependencyResolver> sendResultAction)
                var wrapperMethod = typeof(ApiHelpers).GetRuntimeMethodExt("ExecuteActionAndSendResult", p => true).MakeGenericMethod(typeof(TRq), returnType);

                var controller = Expression.Parameter(typeof(T), "controller");
                var ctx = Expression.Parameter(typeof(TRq), "ctx");
                var resolver = Expression.Parameter(typeof(IDependencyResolver), "resolver");

                return Expression.Lambda<Func<T, TRq, IDependencyResolver, Task>>(Expression.Call(wrapperMethod, controller, ctx, resolver, executeFunction, sendResultFunction), controller, ctx, resolver).Compile();
            }
        }



        private static class ApiHelpers
        {
            public static void ReadObject<TData>(Packet<IScenePeer> p, out TData output, IDependencyResolver resolver) => output = resolver.Resolve<ISerializer>().Deserialize<TData>(p.Stream);
            public static void ReadObject<TData>(RequestContext<IScenePeer> ctx, out TData output, IDependencyResolver resolver) => output = resolver.Resolve<ISerializer>().Deserialize<TData>(ctx.InputStream);
            public static void ReadObject<TData>(Packet<IScenePeerClient> ctx, out TData output, IDependencyResolver resolver) => output = ctx.ReadObject<TData>();
            public static void ReadObject<TData>(RequestContext<IScenePeerClient> ctx, out TData output, IDependencyResolver resolver) => output = ctx.ReadObject<TData>();

            public static Task WriteResult<TData>(RequestContext<IScenePeerClient> ctx, TData value, IDependencyResolver resolver)
            {
                return ctx.SendValue(value);
            }
            public static Task WriteResult<TData>(RequestContext<IScenePeer> ctx, TData value, IDependencyResolver resolver)
            {
                var serializer = resolver.Resolve<ISerializer>();
                return ctx.SendValue(s => serializer.Serialize(value, s));
            }

            public static async Task ExecuteActionAndSendResult<TRq, TReturn>(T controller, TRq ctx, IDependencyResolver resolver, Func<T, TRq, IDependencyResolver, Task<TReturn>> executeControllerActionFunction, Func<TRq, TReturn, IDependencyResolver,Task> sendResultAction)
            {
                var result = await executeControllerActionFunction(controller, ctx, resolver);
                await sendResultAction(ctx, result, resolver);
            }

            /// <summary>
            /// Creates a lambda expression that extract the parameters from the request then call the action and returns a Task 
            /// To use with actions that don't return a value.
            /// </summary>
            public static Func<T, TRq, IDependencyResolver, Task> CreateExecuteActionFunctionVoid<TRq>(MethodInfo method)
            {
                var controller = Expression.Parameter(typeof(T), "controller");
                var ctx = Expression.Parameter(typeof(TRq), "ctx");
                var resolver = Expression.Parameter(typeof(IDependencyResolver), "resolver");

                var expressions = new List<Expression>();
                var parameters = AddReadParametersExpressions<TRq>(expressions, ctx, resolver, method.GetParameters());

                var callExpression = Expression.Call(controller, method, parameters);
                expressions.Add(callExpression);
                if (method.ReturnType == typeof(void))
                {
                    //if (returnType != typeof(void))
                    //{
                    //    var sendResultLambda = CreateWriteResultLambda<TRq>(returnType);

                    //    expressions.Add(Expression.Invoke(sendResultLambda, ctx, callExpression, resolver));
                    //}
                    expressions.Add(Expression.Constant(Task.CompletedTask));
                }

                return Expression.Lambda<Func<T, TRq, IDependencyResolver, Task>>(Expression.Block(parameters, expressions), controller, ctx, resolver).Compile();
            }



            public static Func<T, TRq, IDependencyResolver, Task<TReturn>> CreateExecuteActionFunction<TRq, TReturn>(MethodInfo method)
            {
                var controller = Expression.Parameter(typeof(T), "controller");
                var ctx = Expression.Parameter(typeof(TRq), "ctx");
                var resolver = Expression.Parameter(typeof(IDependencyResolver), "resolver");

                var expressions = new List<Expression>();
                var parameters = AddReadParametersExpressions<TRq>(expressions, ctx, resolver, method.GetParameters());

                var callExpression = Expression.Call(controller, method, parameters);

                if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    expressions.Add(callExpression);
                }
                else
                {
                    expressions.Add(Expression.Call(typeof(Task).GetRuntimeMethodExt("FromResult", p => true).MakeGenericMethod(typeof(TReturn)), callExpression));
                }

                return Expression.Lambda<Func<T, TRq, IDependencyResolver, Task<TReturn>>>(Expression.Block(parameters, expressions), controller, ctx, resolver).Compile();
            }

            public static ParameterExpression[] AddReadParametersExpressions<TRq>(List<Expression> block, ParameterExpression ctx, ParameterExpression resolver, ParameterInfo[] actionParameters)
            {

                var length = actionParameters.Length;
                var ctxType = typeof(TRq);


                var variables = new ParameterExpression[actionParameters.Length];


                for (int i = 0; i < actionParameters.Length; i++)
                {
                    var parameterInfo = actionParameters[i];
                    var variable = Expression.Variable(parameterInfo.ParameterType, parameterInfo.Name);

                    variables[i] = variable;

                    if (parameterInfo.ParameterType == ctxType)
                    {
                        block.Add(Expression.Assign(variables[i], ctx)); //output[i] = ctx;
                    }
                    else
                    {
                        var readObjectMethod = typeof(ApiHelpers).GetRuntimeMethodExt("ReadObject", p => p[0].ParameterType == typeof(TRq));//Select good ReadObject overload

                        block.Add(Expression.Call(readObjectMethod.MakeGenericMethod(parameterInfo.ParameterType), ctx, variables[i], resolver));
                    }
                }
                return variables;
            }

            //Action<TRq, TReturn, IDependencyResolver>
            public static Func<TRq, TReturn, IDependencyResolver,Task> CreateWriteResultLambda<TRq, TReturn>()
            {
                var ctxType = typeof(TRq);
                var returnType = typeof(TReturn);

                var ctx = Expression.Parameter(ctxType, "ctx");
                var resolver = Expression.Parameter(typeof(IDependencyResolver), "resolver");
                var data = Expression.Parameter(returnType, "value");

                var writeResultMethod = typeof(ApiHelpers).GetRuntimeMethodExt("WriteResult", p => p[0].ParameterType == typeof(TRq));
                return Expression.Lambda<Func<TRq, TReturn, IDependencyResolver,Task>>(Expression.Call(writeResultMethod.MakeGenericMethod(returnType), ctx, data, resolver), ctx, data, resolver).Compile();
            }
        }
    }
    public static class RuntimeMethodExtensions
    {
        public static MethodInfo GetRuntimeMethodExt(this Type type, string name, params Type[] types)
        {
            // Find potential methods with the correct name and the right number of parameters
            // and parameter names
            var potentials = (from ele in type.GetMethods()
                              where ele.Name.Equals(name)
                              let param = ele.GetParameters()
                              where param.Length == types.Length
                              && param.Select(p => p.ParameterType.Name).SequenceEqual(types.Select(t => t.Name))
                              select ele);

            // Maybe check if we have more than 1? Or not?
            return potentials.FirstOrDefault();
        }
        public static MethodInfo GetRuntimeMethodExt(this Type type, string name, Func<ParameterInfo[], bool> parametersFilter)
        {
            // Find potential methods with the correct name and the right number of parameters
            // and parameter names
            var potentials = (from ele in type.GetMethods()
                              where ele.Name.Equals(name)
                              let param = ele.GetParameters()
                              where parametersFilter(param)
                              select ele);

            // Maybe check if we have more than 1? Or not?
            return potentials.FirstOrDefault();
        }
    }

}

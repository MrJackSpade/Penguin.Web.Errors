using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Penguin.Reflection;
using Penguin.Web.Errors.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Penguin.Web.Errors.Middleware
{
    //https://exceptionnotfound.net/using-middleware-to-log-requests-and-responses-in-asp-net-core/
    public class ExceptionHandling
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo> AllHandlers = new ConcurrentDictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, ExceptionRoute> ErrorHandlers = new Dictionary<Type, ExceptionRoute>();
        private static readonly object ErrorHandlerLock = new object();

        private static readonly bool HandlersSearched = false;
        private readonly RequestDelegate _next;

        //TODO: Learn what this is
        public ExceptionHandling(RequestDelegate next)
        {
            this._next = next;
        }

        static ExceptionHandling()
        {
            foreach (Type t in TypeFactory.GetAllTypes())
            {
                foreach (MethodInfo m in t.GetMethods())
                {
                    if (m.GetCustomAttribute<HandleExceptionAttribute>() is HandleExceptionAttribute handler)
                    {
                        foreach (Type e in handler.ToHandle)
                        {
                            _ = AllHandlers.TryAdd(e, m);
                        }
                    }
                }
            }
        }

        public async Task Invoke(HttpContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            try
            {
                await this._next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    Type exceptionType = ex.GetType();
                    
                    ExceptionRoute selectedRoute = null;

                    Monitor.Enter(ErrorHandlerLock);

                    if (!ErrorHandlers.TryGetValue(exceptionType, out selectedRoute))
                    {
                        Type toCheck = exceptionType;

                        do
                        {
                            if (AllHandlers.TryGetValue(toCheck, out MethodInfo m))
                            {
                                string ControllerName = m.DeclaringType.Name;

                                if (ControllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
                                {
                                    ControllerName = ControllerName.Replace("Controller", "");
                                }

                                string ActionName = m.Name;
                                string AreaName = string.Empty;

                                if (m.DeclaringType.GetCustomAttribute<AreaAttribute>() is AreaAttribute attribute)
                                {
                                    AreaName = attribute.RouteValue;
                                }

                                selectedRoute = new ExceptionRoute()
                                {
                                    Action = ActionName,
                                    Area = AreaName,
                                    Controller = ControllerName
                                };

                                ErrorHandlers.Add(exceptionType, selectedRoute);
                            }

                            toCheck = toCheck.BaseType;
                        } while (toCheck != null && typeof(Exception).IsAssignableFrom(toCheck));

                        ErrorHandlers.Add(exceptionType, null);
                    }

                    if (selectedRoute is null)
                    {
                        throw;
                    }
                    else
                    {
                        context.Response.Redirect(BuildUrl(selectedRoute, context));
                    }
                }
                finally
                {
                    Monitor.Exit(ErrorHandlerLock);
                }
            }

        }

        private static string BuildUrl(ExceptionRoute route, HttpContext context)
        {
            RouteData r = context.GetRouteData();

            ActionContext a = new ActionContext(context, r, new ActionDescriptor());

            UrlHelper helper = new UrlHelper(a);

            return helper.Action(route.Action, route.Controller, new { area = route.Area, Url = context.Request.Path });
        }
    }
}
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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Penguin.Web.Errors.Middleware
{
    //https://exceptionnotfound.net/using-middleware-to-log-requests-and-responses-in-asp-net-core/
    public class ExceptionHandling
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo> AllHandlers = new();
        private static readonly Dictionary<Type, ExceptionRoute> ErrorHandlers = new();
        private static readonly object ErrorHandlerLock = new();
        private readonly RequestDelegate _next;

        //TODO: Learn what this is
        public ExceptionHandling(RequestDelegate next)
        {
            _next = next;
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
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    Type exceptionType = ex.GetType();

                    Monitor.Enter(ErrorHandlerLock);

                    if (!ErrorHandlers.TryGetValue(exceptionType, out ExceptionRoute selectedRoute))
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

                                string HttpGet = null;

                                if (m.GetCustomAttribute<HttpGetAttribute>() is HttpGetAttribute hga)
                                {
                                    HttpGet = hga.Template;
                                }

                                selectedRoute = new ExceptionRoute()
                                {
                                    Action = ActionName,
                                    Area = AreaName,
                                    Controller = ControllerName,
                                    Template = HttpGet
                                };

                                ErrorHandlers.Add(exceptionType, selectedRoute);

                                context.Response.Redirect(BuildUrl(selectedRoute, context));

                                return;
                            }

                            toCheck = toCheck.BaseType;
                        } while (toCheck != null && typeof(Exception).IsAssignableFrom(toCheck));

                        ErrorHandlers.Add(exceptionType, null);
                    }

                    if (selectedRoute != null)
                    {
                        string redirectLocation = BuildUrl(selectedRoute, context);

                        if (redirectLocation != null)
                        {
                            context.Response.Redirect(redirectLocation);
                        }
                        else
                        {
                            throw new RouteCreationException($"Unable to resolve route for exception {exceptionType}");
                        }

                        return;
                    }

                    throw;
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

            ActionContext a = new(context, r, new ActionDescriptor());

            UrlHelper helper = new(a);

            string actionRoute = helper.Action(route.Action, route.Controller, new { area = route.Area, Url = context.Request.Path });

            if (actionRoute != null)
            {
                return actionRoute;
            }

            if (!string.IsNullOrWhiteSpace(route.Template))
            {
                return route.Template;
            }

            StringBuilder sb = new();

            if (!string.IsNullOrWhiteSpace(route.Area))
            {
                _ = sb.Append($"/{route.Area}");
            }

            _ = sb.Append($"/{route.Controller}/{route.Action}");

            return sb.ToString();
        }
    }
}
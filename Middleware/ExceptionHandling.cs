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
using System.Threading.Tasks;

namespace Penguin.Web.Errors.Middleware
{
    //https://exceptionnotfound.net/using-middleware-to-log-requests-and-responses-in-asp-net-core/
    public class ExceptionHandling
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo> AllHandlers = new ConcurrentDictionary<Type, MethodInfo>();
        private static readonly ConcurrentDictionary<Type, ExceptionRoute> ErrorHandlers = new ConcurrentDictionary<Type, ExceptionRoute>();
        private static bool HandlersSearched = false;
        private readonly RequestDelegate _next;

        //TODO: Learn what this is
        public ExceptionHandling(RequestDelegate next)
        {
            this._next = next;
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
                Type exceptionType = ex.GetType();

                if (!ErrorHandlers.TryGetValue(exceptionType, out ExceptionRoute route))
                {
                    if (!HandlersSearched)
                    {
                        HandlersSearched = true;

                        foreach (Type t in TypeFactory.GetAllTypes())
                        {
                            foreach (MethodInfo m in t.GetMethods())
                            {
                                if (m.GetCustomAttribute<HandleExceptionAttribute>() is HandleExceptionAttribute handler)
                                {
                                    foreach (Type e in handler.ToHandle)
                                    {
                                        AllHandlers.TryAdd(e, m);
                                    }
                                }
                            }
                        }
                    }

                    List<Type> searchTypes = new List<Type>();
                    Type toCheck = exceptionType;

                    do
                    {
                        searchTypes.Add(toCheck);
                        toCheck = toCheck.BaseType;
                    } while (toCheck != null && typeof(Exception).IsAssignableFrom(toCheck));

                    List<ExceptionHandler> potentialHandlers = new List<ExceptionHandler>();

                    foreach (Type possibleHandlerType in searchTypes)
                    {
                        if (AllHandlers.TryGetValue(possibleHandlerType, out MethodInfo m))
                        {
                            potentialHandlers.Add(new ExceptionHandler(possibleHandlerType, m));
                        }
                    }

                    Type mostDerivedHandler = TypeFactory.GetMostDerivedType(potentialHandlers.Select(p => p.ExceptionType).ToList(), typeof(Exception));

                    MethodInfo selectedHandler = potentialHandlers.FirstOrDefault(p => p.ExceptionType == mostDerivedHandler)?.Method;

                    if (selectedHandler is null)
                    {
                        ErrorHandlers.TryAdd(exceptionType, null);
                    }
                    else
                    {
                        string ControllerName = selectedHandler.DeclaringType.Name;

                        if (ControllerName.EndsWith("Controller"))
                        {
                            ControllerName = ControllerName.Replace("Controller", "");
                        }

                        string ActionName = selectedHandler.Name;
                        string AreaName = string.Empty;

                        if (selectedHandler.DeclaringType.GetCustomAttribute<AreaAttribute>() is AreaAttribute attribute)
                        {
                            AreaName = attribute.RouteValue;
                        }

                        route = new ExceptionRoute()
                        {
                            Action = ActionName,
                            Area = AreaName,
                            Controller = ControllerName
                        };

                        ErrorHandlers.TryAdd(exceptionType, route);
                    }
                }

                if (route is null)
                {
                    throw;
                }
                else
                {
                    context.Response.Redirect(this.BuildUrl(route, context));
                }
            }
        }

        private string BuildUrl(ExceptionRoute route, HttpContext context)
        {
            RouteData r = context.GetRouteData();

            ActionContext a = new ActionContext(context, r, new ActionDescriptor());

            UrlHelper helper = new UrlHelper(a);

            return helper.Action(route.Action, route.Controller, new { area = route.Area, Url = context.Request.Path });
        }
    }
}
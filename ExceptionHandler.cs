using System;
using System.Reflection;

namespace Penguin.Web.Errors
{
    internal class ExceptionHandler
    {
        public Type ExceptionType { get; set; }

        public MethodInfo Method { get; set; }

        public ExceptionHandler(Type exceptionType, MethodInfo method)
        {
            this.ExceptionType = exceptionType;
            this.Method = method;
        }
    }
}
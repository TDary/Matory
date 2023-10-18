using Matory.DataAO;
using System.Collections.Generic;

namespace Matory.Server
{
    public class MsgProfiler
    {
        public delegate object FunMethod(string remoteIp,string[] param);
        public readonly Dictionary<string, FunMethod> funMethods = new Dictionary<string, FunMethod>();
        private string[] args;
        public void AddMethod(string funName, FunMethod method)
        {
            funMethods.Add(funName, method);
        }

        public object RunMethod(string remoteIp,Dictionary<string, FunMethod> allfun, TransData data)
        {
            FunMethod currentFunc;
            allfun.TryGetValue(data.FuncName, out currentFunc);
            return currentFunc?.Invoke(remoteIp,data.FuncArgs);
        }
    }
}
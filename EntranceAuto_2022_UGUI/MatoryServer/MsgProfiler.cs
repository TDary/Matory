using Matory.DataAO;
using System;
using System.Collections.Generic;
using UnityEngine;

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
            try
            {
                FunMethod currentFunc;
                allfun.TryGetValue(data.FuncName, out currentFunc);
                var res = currentFunc?.Invoke(remoteIp, data.FuncArgs);
                return res;
            }
            catch(Exception ex)
            {
                Debug.LogException(ex);
                return ex;
            }
        }
    }
}
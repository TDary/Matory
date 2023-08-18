using System.Collections.Generic;

public class MsgProfiler
{
    public delegate object FunMethod(string[] param);
    public readonly Dictionary<string, FunMethod> funMethods = new Dictionary<string, FunMethod>();
    private string[] args;
    public void AddMethod(string funName, FunMethod method)
    {
        funMethods.Add(funName, method);
    }

    public object RunMethod(Dictionary<string, FunMethod> allfun,TransData data)
    {
        FunMethod currentFunc;
        allfun.TryGetValue(data.FuncName, out currentFunc);
        return currentFunc?.Invoke(data.FuncArgs);
    }
}

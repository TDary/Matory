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

    public void RunMethod(Dictionary<string, FunMethod> allfun,TransData data)
    {
        FunMethod currentFunc = null;
        allfun.TryGetValue(data.FuncName, out currentFunc);
        currentFunc?.Invoke(data.FuncArgs);
    }
}

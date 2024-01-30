using Matory.Net;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.Profiling;
using System.Reflection;
using LitJson;
using Matory.DataAO;
using Matory.Server;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections;
using System.Linq;
using System.IO;

namespace Matory
{
    public class Mato : MonoBehaviour
    {
        private SocketServer socketServer;
        private int port = 2666;
        private bool startGatherMsg = false;
        private bool isGathering = false;
        private MsgProfiler m_Pro;
        private int frameNum = 0;
        private int fileNum = 0;
        private List<string> profilerDataNames;
        private List<string> profilerDataPaths;
        private string profilerDataName = "";
        private string profilerDataPath = "";
        private GameObject targetObj;
        private ConcurrentQueue<MsgForSend> SendMsgPool = new ConcurrentQueue<MsgForSend>();
        private ConcurrentQueue<TransData> GetMsgPool = new ConcurrentQueue<TransData>();
        private Dictionary<string,TransData> GetTransDataPool = new Dictionary<string,TransData>(20);
        private int requestCount = 0;
        private int sendCount = 0;
        private string Profiler_path;
        private List<string> Collection_item = new List<string>();//存放采集项目
        private IEnumerator ProfileIEnumerator = null;
        public void Init()
        {
            DontDestroyOnLoad(this);
            m_Pro = new MsgProfiler();
            m_Pro.funMethods.Add("GetSdkVersion",GetSdkVersion);
            m_Pro.funMethods.Add("GetUnityVersion", GetUnityVersion);
            m_Pro.funMethods.Add("StopConnection",StopConnection);
            m_Pro.funMethods.Add("Find_Text", FindText);
            m_Pro.funMethods.Add("Find_AllButton", FindAllButton);
            m_Pro.funMethods.Add("Gather_Profiler",GatherProfiler);
            m_Pro.funMethods.Add("Check_Profiler",CheckProfilerData);
            m_Pro.funMethods.Add("Get_Hierarchy",GetHierarchy);
            m_Pro.funMethods.Add("Get_Inspector",GetInspector);
            m_Pro.funMethods.Add("ClickOne", ClickOneButton);
            m_Pro.funMethods.Add("GetScreenShot",GetScreenShot);
            m_Pro.funMethods.Add("Object_Exist",IsObjectExist);
            for (int i = 0; i < 5; i++)
            {
                bool thisport = IsPortInUse(port + i);
                if (thisport)
                {
                    Debug.Log($"This port {port + i} is in used");
                    continue;
                }
                else
                {
                    socketServer = new SocketServer();
                    socketServer.start(port + i);    //监听端口号
                    socketServer.mydelegate = ParseData;
                    Debug.Log($"Matory is Listen success for {port + i}");
                    break;
                }
            }
        }

        void Update()
        {
            if (requestCount > 0 || sendCount > 0)
            {
                while (requestCount > 0 || sendCount > 0)
                {
                    if (GetMsgPool.Count != 0)   //处理函数并执行，返回消息给客户端
                    {
                        TransData data = null;
                        ResData res = null;
                        if (GetMsgPool.TryDequeue(out data))
                        {
                            foreach (var item in GetTransDataPool)
                            {
                                if (item.Value.FuncArgs == data.FuncArgs && item.Value.FuncName == data.FuncName)
                                {
                                    var result = m_Pro.RunMethod(item.Key, m_Pro.funMethods, data);
                                    if (result != null)
                                    {
                                        string resMsg = result.ToString();
                                        res = new ResData(200, true, resMsg);
                                        foreach (var session in socketServer.SessionPool.Values)
                                        {
                                            if (session.IP == item.Key)
                                            {
                                                JsonWriter jw = new JsonWriter();
                                                jw.WriteObjectStart();
                                                jw.WritePropertyName("Code");
                                                jw.Write(res.Code);
                                                jw.WritePropertyName("Msg");
                                                jw.Write(res.Msg);
                                                jw.WritePropertyName("Data");
                                                jw.Write(res.Data);
                                                jw.WriteObjectEnd();
                                                byte[] msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                                session.SockeClient.Send(msgBuffer);
                                                break;
                                            }
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        foreach (var session in socketServer.SessionPool.Values)
                                        {
                                            if (session.IP == item.Key)
                                            {
                                                JsonWriter jw = new JsonWriter();
                                                jw.WriteObjectStart();
                                                jw.WritePropertyName("Code");
                                                jw.Write(200);
                                                jw.WritePropertyName("Msg");
                                                jw.Write(true);
                                                jw.WritePropertyName("Data");
                                                jw.Write("This Function is not found.");
                                                jw.WriteObjectEnd();
                                                byte[] msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                                session.SockeClient.Send(msgBuffer);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            requestCount--;
                        }
                    }
                    else if (SendMsgPool.Count != 0)   //返回消息给客户端
                    {
                        MsgForSend data = null;
                        if (SendMsgPool.TryDequeue(out data))
                        {
                            foreach (var session in socketServer.SessionPool.Values)
                            {
                                if (session.IP == data.Ip)
                                {
                                    JsonWriter jw = new JsonWriter();
                                    jw.WriteObjectStart();
                                    jw.WritePropertyName("Code");
                                    jw.Write(200);
                                    jw.WritePropertyName("Msg");
                                    jw.Write(data.Msg);
                                    jw.WriteObjectEnd();
                                    byte[] msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                    session.SockeClient.Send(msgBuffer);
                                    break;
                                }
                            }
                        }
                        sendCount--;
                    }
                }
            }
        }

        public void ParseData(string ip,string msg)
        {
            TransData data = null;
            var runData = JsonMapper.ToObject<TransData>(msg);
            if (GetTransDataPool.TryGetValue(ip, out data))
                GetTransDataPool[ip] = runData;
            else
                GetTransDataPool.Add(ip, runData);
            GetMsgPool.Enqueue(runData);
            requestCount += 1;
        }

        /// <summary>
        /// IsPortInUse
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public static bool IsPortInUse(int port)
        {
            bool isPortInUse = false;

            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            IPEndPoint[] activeUdpListeners = ipGlobalProperties.GetActiveUdpListeners();

            foreach (IPEndPoint endPoint in activeTcpListeners)
            {
                if (endPoint.Port == port)
                {
                    isPortInUse = true;
                    break;
                }
            }

            if (!isPortInUse)
            {
                foreach (IPEndPoint endPoint in activeUdpListeners)
                {
                    if (endPoint.Port == port)
                    {
                        isPortInUse = true;
                        break;
                    }
                }
            }

            return isPortInUse;
        }
        /// <summary>
        /// 关闭连接
        /// </summary>
        /// <returns></returns>
        private object StopConnection(string ip,string[] args)
        {
            if (socketServer != null)
            {
                socketServer.stop();
            }
            return null;
        }

        public static UnityEngine.Object FindObjectFromInstanceID(int iid)
        {
            return (UnityEngine.Object)typeof(UnityEngine.Object)
                    .GetMethod("FindObjectFromInstanceID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { iid });
        }

        #region 获取Unity面板数据
        private bool ComponentContainProperty(Component component, string propertyName)
        {
            if (component != null && !string.IsNullOrEmpty(propertyName))
            {
                PropertyInfo _findedPropertyInfo = component.GetType().GetProperty(propertyName);
                return (_findedPropertyInfo != null);
            }
            return false;
        }
        private T GetComponentValue<T>(Component component, string propertyName)
        {
            if (component != null && !string.IsNullOrEmpty(propertyName))
            {
                PropertyInfo propertyInfo = component.GetType().GetProperty(propertyName);
                if (propertyInfo != null)
                {
                    MethodInfo mi = propertyInfo.GetGetMethod(true);
                    if (mi != null)
                    {
                        return (T)mi.Invoke(component, null);
                    }

                    //return (T)propertyInfo.GetValue(component, null);
                }
            }
            return default(T);
        }
        private void GetComponentPropertys(Component component, JsonWriter jw)
        {
            try
            {
                PropertyInfo[] propertyInfos = component.GetType().GetProperties(BindingFlags.Public |
                                                                                BindingFlags.Instance |
                                                                                BindingFlags.SetProperty |
                                                                                BindingFlags.GetProperty);

                FieldInfo[] fieldInfos = component.GetType().GetFields(BindingFlags.Public |
                                                                    BindingFlags.Instance |
                                                                    BindingFlags.SetField |
                                                                    BindingFlags.GetField);

                for (int i = 0; i < propertyInfos.Length; ++i)
                {
                    PropertyInfo pi = propertyInfos[i];

                    //Debug.LogError("Property:" + pi.Name);

                    if (pi.CanWrite && pi.CanRead)
                    {
                        // call getter with these Property name will create new object;
                        if (pi.Name == "mesh" || pi.Name == "material" || pi.Name == "materials")
                        {
                            continue;
                        }

                        System.Object obj = pi.GetValue(component, null);
                        if (obj is System.Collections.ICollection)
                        {
                            continue;
                        }

                        if (pi.GetValue(component) != null)
                        {
                            jw.WriteObjectStart();

                            jw.WritePropertyName("name");
                            jw.Write(pi.Name);

                            jw.WritePropertyName("type");
                            jw.Write(pi.GetValue(component).GetType().ToString());

                            jw.WritePropertyName("value");
                            jw.Write(pi.GetValue(component).ToString());

                            jw.WriteObjectEnd();
                        }
                    }
                    else
                    {

                    }
                }

                for (int i = 0; i < fieldInfos.Length; ++i)
                {
                    FieldInfo fi = fieldInfos[i];
                    //Debug.LogError("Field:" + fi.Name);

                    if (fi.GetValue(component) != null)
                    {
                        jw.WriteObjectStart();

                        jw.WritePropertyName("name");
                        jw.Write(fi.Name);

                        jw.WritePropertyName("type");
                        jw.Write(fi.GetValue(component).GetType().ToString());

                        jw.WritePropertyName("value");
                        jw.Write(fi.GetValue(component).ToString());

                        jw.WriteObjectEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log(ex);
            }
        }
        void WriteJsonData(JsonWriter jw, Component component)
        {
            GameRunTimeDataSet.AddComponent(component);

            jw.WriteObjectStart();

            jw.WritePropertyName("id");
            jw.Write(component.GetInstanceID());

            jw.WritePropertyName("type");
            jw.Write(component.GetType().ToString());

            if (ComponentContainProperty(component, "enabled"))
            {
                jw.WritePropertyName("enabled");
                jw.Write(GetComponentValue<bool>(component, "enabled"));
            }

            jw.WritePropertyName("properties");
            jw.WriteArrayStart();

            GetComponentPropertys(component, jw);

            jw.WriteArrayEnd();

            jw.WriteObjectEnd();
        }
        private object GetInspector(string ip, string[] args)
        {
            try
            {
                int objId = int.Parse(args[1]);

                GameObject obj = null;

                if (GameRunTimeDataSet.TryGetGameObject(objId, out obj))
                {
                    JsonWriter jw = new JsonWriter();

                    jw.WriteObjectStart();

                    jw.WritePropertyName("name");
                    jw.Write(obj.name);

                    jw.WritePropertyName("id");
                    jw.Write(obj.GetInstanceID());

                    jw.WritePropertyName("enabled");
                    jw.Write(obj.activeInHierarchy);

                    jw.WritePropertyName("tag");
                    jw.Write(obj.tag);

                    jw.WritePropertyName("layer");
                    jw.Write(LayerMask.LayerToName(obj.layer));

                    jw.WritePropertyName("components");

                    jw.WriteArrayStart();

                    Component[] components = obj.GetComponents<Component>();
                    for (int j = 0; j < components.Length; ++j)
                    {
                        WriteJsonData(jw, components[j]);
                    }

                    jw.WriteArrayEnd();

                    jw.WriteObjectEnd();

                    return jw.ToString();
                }
                else
                {
                    throw new Exception(Error.NotFoundMessage);
                }
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }
        private void WriteJsonData(JsonWriter jw, GameObject go)
        {
            jw.WriteObjectStart();

            jw.WritePropertyName("id");
            jw.Write(go.GetInstanceID());

            jw.WritePropertyName("name");
            jw.Write(go.name);

            GameRunTimeDataSet.AddGameObject(go);

            if (go.transform.childCount > 0)
            {
                jw.WritePropertyName("children");
                jw.WriteArrayStart();

                for (int i = 0; i < go.transform.childCount; ++i)
                {
                    GameObject child = go.transform.GetChild(i).gameObject;
                    WriteJsonData(jw, child);
                }

                jw.WriteArrayEnd();
            }

            jw.WriteObjectEnd();
        }
        private object GetHierarchy(string ip, string[] args)
        {
            try
            {
                GameRunTimeDataSet.InitDataSet();

                JsonWriter jsonWriter = new JsonWriter();
                jsonWriter.WriteObjectStart();
                jsonWriter.WritePropertyName("objs");

                jsonWriter.WriteObjectStart();

                jsonWriter.WritePropertyName("id");
                jsonWriter.Write("root");

                jsonWriter.WritePropertyName("name");
                jsonWriter.Write("root");

                jsonWriter.WritePropertyName("children");

                jsonWriter.WriteArrayStart();

                List<GameObject> _RootGameObjects = new List<GameObject>();
                Transform[] arrTransforms = Transform.FindObjectsOfType<Transform>();
                for (int i = 0; i < arrTransforms.Length; ++i)
                {
                    Transform tran = arrTransforms[i];
                    if (tran.parent == null)
                    {
                        _RootGameObjects.Add(tran.gameObject);
                    }
                }

                for (int i = 0; i < _RootGameObjects.Count; ++i)
                {
                    WriteJsonData(jsonWriter, _RootGameObjects[i]);
                }

                jsonWriter.WriteArrayEnd();

                jsonWriter.WriteObjectEnd();


                jsonWriter.WriteObjectEnd();

                return jsonWriter.ToString();
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }
        #endregion


        #region 获取Unity版本
        private object GetUnityVersion(string ip,string[] args)
        {
            try
            {
                return Application.unityVersion;
            }
            catch(Exception ex)
            {
                return ex.ToString();
            }
        }
        #endregion

        #region 获取sdk版本
        private object GetSdkVersion(string ip,string[] args)
        {
            return "1.0.0";
        }
        #endregion

        #region 检查profilerdata逻辑
        private string GetProfileData()
        {
            JsonWriter jw = new JsonWriter();

            jw.WriteArrayStart();

            for (int i = 0; i < profilerDataNames.Count; ++i)
            {
                jw.WriteObjectStart();

                jw.WritePropertyName("path");//写入属性名称（'路径'）
                jw.Write(profilerDataPaths[i]);

                jw.WritePropertyName("name");
                jw.Write(profilerDataNames[i]);

                jw.WriteObjectEnd();
            }

            jw.WriteArrayEnd();

            profilerDataNames.Clear();
            profilerDataPaths.Clear();

            return jw.ToString();
        }
        private object CheckProfilerData(string ip, string[] args)
        {
            try
            {
                JsonWriter jw = new JsonWriter();
                jw.WriteObjectStart();
                if (Collection_item.Contains("profiler_gather"))
                {
                    jw.WritePropertyName("profiler_gather");
                    jw.Write(GetProfileData());
                }
                jw.WriteObjectEnd();

                return jw.ToString();
            }
            catch(Exception ex)
            {
                return ex.Message.ToString();
            }
        }
        #endregion

        #region 采集UnityProfilerData逻辑
        private void InitProfiler()
        {
            frameNum = 0;
            fileNum = 0;
            isGathering = false;

            profilerDataNames = new List<string>();
            profilerDataPaths = new List<string>();
        }

        /// <summary>
        /// 采集UnityProfiler数据
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private object GatherProfiler(string ip, string[] args)
        {
            Dictionary<string, bool> response = new Dictionary<string, bool> { { "DebugBuild", true }, { "code", true } ,{ "profiler_gather",false } };
            if (!Debug.isDebugBuild)
            {
                Debug.LogError("Current game is not a development build.");
                response["DebugBuild"] = false;
                return JsonMapper.ToJson(response);
            }
            foreach(string arg in args)
            {
                Debug.Log($"MatorySDK Profiler arg: {arg}");
            }

            try
            {
                string parameter = args[1];

                if(parameter == "1")
                {
                    //开始采集
                    Dictionary<string, string> Dicargs = JsonMapper.ToObject<Dictionary<string, string>>(args[2]);//解析参数案例
                    Dictionary<string, string> Diccollection = JsonMapper.ToObject<Dictionary<string, string>>(Dicargs["collection"]);
                    Collection_item = Diccollection.Keys.ToList<string>();
                    Dictionary<string, string> Dicdata = JsonMapper.ToObject<Dictionary<string, string>>(Dicargs["data"]);

                    //深度profiler采集
                    if(ProfileIEnumerator == null && Collection_item.Contains("profiler_gather"))
                    {
                        Profiler_path = Application.persistentDataPath;
                        if (Dicdata.ContainsKey("path"))
                        {
                            if (!string.IsNullOrEmpty(Dicdata["path"]) && Directory.Exists(Dicdata["path"]))
                            {
                                Profiler_path = Dicdata["path"];
                            }
                        }
                        startGatherMsg = true;
                        ProfileIEnumerator = StartGatherProfiler();
                        StartCoroutine(ProfileIEnumerator);
                        response["profiler_gather"] = true;
                    }
                    else
                    {
                        if (Collection_item.Contains("profiler_gather"))
                        {
                            response["profiler_gather"] = false;
                        }
                    }
                }
                else if(parameter == "0")
                {
                    //结束采集
                    if(ProfileIEnumerator != null && Collection_item.Contains("profiler_gather"))
                    {
                        if (startGatherMsg)
                        {
                            startGatherMsg = false;
                            StopGather();
                        }
                        StopCoroutine(ProfileIEnumerator);
                        ProfileIEnumerator = null;
                        response["profiler_gather"] = true;
                    }
                    else
                    {
                        if (Collection_item.Contains("profiler_gather"))
                        {
                            response["profiler_gather"] = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                response[ex.ToString()] = false;
                response["code"] = false;
            }
            return JsonMapper.ToJson(response);
        }

        private IEnumerator StartGatherProfiler()
        {
            try
            {
                InitProfiler();
                while (startGatherMsg)
                {
                    if (isGathering)
                    {
                        frameNum++;
                        if (frameNum >= 300)
                        {
                            StopGather();
                            fileNum++;
                            frameNum = 0;
                            isGathering = false;
                        }
                    }
                    else
                    {
                        BeginGather("ProfilerGather-" + DateTime.Now.ToString(format: "yyyy-MM-dd-HH-mm-ss") + "-" + fileNum);
                        isGathering = true;
                        frameNum++;
                    }
                }
            }
            catch(Exception ex)
            {
                Debug.LogError(ex.ToString());
            }
            yield return null;
        }

        private void BeginGather(string fileName)
        {
            Profiler.SetAreaEnabled(ProfilerArea.CPU, true);
            Profiler.SetAreaEnabled(ProfilerArea.Rendering, true);
            Profiler.SetAreaEnabled(ProfilerArea.Memory, true);
            Profiler.SetAreaEnabled(ProfilerArea.Physics, true);
            Profiler.SetAreaEnabled(ProfilerArea.UI, true);
            //标记data文件最大使用1GB储存空间
            Profiler.maxUsedMemory = 1024 * 1024 * 1024;

            Profiler.logFile = Profiler_path + "/" + fileName;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            profilerDataPath = Profiler_path;
            profilerDataName = fileName;
        }

        private void StopGather()
        {
            Profiler.enabled = false;
            Profiler.logFile = "";
            Profiler.enableBinaryLog = false;

            profilerDataNames.Add(profilerDataName);
            profilerDataPaths.Add(profilerDataPath);
        }
        #endregion

        #region 获取UI逻辑

        /// <summary>
        /// 获取当前界面所有UI按钮，返回路径以及Obj的Instacneid
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private object FindAllButton(string ip, string[] args)
        {
            JsonWriter jw = new JsonWriter();
            jw.WriteArrayStart();
            Canvas[] allcanvas = FindObjectsOfType<Canvas>();
            foreach(var can in allcanvas)
            {
                var allBtnInCanva = can.GetComponentsInChildren<Button>();
                foreach (var button in allBtnInCanva)
                {
                    jw.WriteObjectStart();
                    jw.WritePropertyName("InstanceId");
                    jw.Write(button.gameObject.GetInstanceID());
                    jw.WritePropertyName("ButtonName");
                    var textobj = button.gameObject.GetComponentInChildren<Text>();
                    if (textobj == null)
                    {
                        var textmeshInput = button.gameObject.GetComponentInChildren<InputField>(); //寻找是否有InputField组件
                        if (textmeshInput != null)
                            jw.Write(textmeshInput.text);
                        else
                            jw.Write("");
                    }
                    else
                        jw.Write(textobj.text);
                    jw.WritePropertyName("BtnPath");
                    string btnPath = GetHierarchyPath(button.transform);
                    jw.Write(btnPath);
                    jw.WriteObjectEnd();
                }
             }
            jw.WriteArrayEnd();
            return jw.ToString();
        }

        //获取UI上的文本对象
        private object FindText(string ip, string[] args)
        {
            JsonWriter jw = new JsonWriter();
            jw.WriteArrayStart();
            Canvas[] allcanva = FindObjectsOfType<Canvas>();
            foreach (var item in allcanva)
            {
                if(args.Length!=0 && args[0] != "")
                {
                    var allText = item.GetComponentsInChildren<Text>();
                    foreach (var text in allText)
                    {
                        if (text != null && text.text.Contains(args[0]))
                        {
                            jw.WriteObjectStart();
                            jw.WritePropertyName("TextUIPath");
                            string currentUIPath = GetHierarchyPath(text.transform);
                            jw.Write(currentUIPath);
                            jw.WritePropertyName("InstanceId");
                            jw.Write(text.GetInstanceID());
                            jw.WriteObjectEnd();
                        }
                    }
                    var allInputText = item.GetComponentsInChildren<InputField>();
                    foreach (var inputField in allInputText)
                    {
                        if (inputField != null && inputField.text.Contains(args[0]))
                        {
                            jw.WriteObjectStart();
                            jw.WritePropertyName("TextInputUIPath");
                            string currentUIPath = GetHierarchyPath(inputField.transform);
                            jw.Write(currentUIPath);
                            jw.WritePropertyName("InstanceId");
                            jw.Write(inputField.GetInstanceID());
                            jw.WriteObjectEnd();
                        }
                    }
                }
            }
            jw.WriteArrayEnd();
            return jw.ToString();
        }

        //获取元素的层级路径
        private string GetHierarchyPath(Transform transform)
        {
            if (transform.parent == null)
            {
                return transform.name;
            }
            else
            {
                return GetHierarchyPath(transform.parent) + "/" + transform.name;
            }
        }

        /// <summary>
        /// 判断当前对象是否存在
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args">"id"/"path" value</param>
        /// <returns></returns>
        private object IsObjectExist(string ip, string[] args)
        {
            bool res;
            string resMsg;
            if (args.Length >= 2)
            {
                if (args[0].Equals("id"))
                {
                    if (int.TryParse(args[1], out int insid))
                    {
                        if (FindObjectFromInstanceID(insid) != null)
                        {
                            res = true;
                            resMsg = "This object is exist.";
                        }
                        else
                        {
                            res = false;
                            resMsg = "This object is not exist.";
                        }
                    }
                    else
                    {
                        res = false;
                        resMsg = "This id is not int type";
                    }
                }
                else if (args[0].Equals("path"))
                {
                    if (args[1] != "")
                    {
                        targetObj = GameObject.Find(args[1]);
                        if(targetObj != null)
                        {
                            res = true;
                            resMsg = "This object is exist.";
                        }
                        else
                        {
                            res = false;
                            resMsg = "This object is not exist.";
                        }
                    }
                    else
                    {
                        res = false;
                        resMsg = "This path value is empty.";
                    }
                }
                else
                {
                    res = false;
                    resMsg = "This markMethod is not exist.";
                }
            }
            else
            {
                res = false;
                resMsg = "Current args is not enough.";
            }
            JsonWriter jw = new JsonWriter();
            jw.WriteObjectStart();
            jw.WritePropertyName("ObjectExistState");
            jw.Write(res);
            jw.WritePropertyName("RetrunMsg");
            jw.Write(resMsg);
            jw.WriteObjectEnd();
            return jw.ToString();
        }

        //private Text GetChildText(Transform parent, string currentText)
        //{
        //    foreach (Transform child in parent)
        //    {
        //        //寻找是否有Text组件
        //        Text text = child.GetComponent<Text>();
        //        if (text != null && text.text == currentText)
        //            return text;
        //        else if (text != null && text.text.Contains(currentText))
        //            return text;

        //        //寻找是否有InputField组件
        //        InputField inputText = child.GetComponent<InputField>();
        //        if (inputText != null && inputText.text == currentText)
        //            return inputText;
                
        //        //递归遍历一下子对象
        //        GetChildText(child, currentText);
        //    }
        //    return null;
        //}

        private Button GetChildButton(Transform parent)
        {
            foreach (Transform child in parent)
            {
                //检查一些是否有Button组件
                Button button = child.GetComponent<Button>();
                if (button != null)
                    return button;
                //递归遍历一下子对象
                GetChildButton(child);
            }
            return null;
        }

        private Button GetChildButton(Transform parent, string buttonName)
        {
            foreach (Transform child in parent)
            {
                //检查一些是否有Button组件
                Button button = child.GetComponent<Button>();
                if (button != null && button.name == buttonName)
                    return button;
                //递归遍历一下子对象
                GetChildButton(child, buttonName);
            }
            return null;
        }
        #endregion

        #region 获取截图功能
        private object GetScreenShot(string ip, string[] args)
        {
            try
            {
                ScreenShotTask(ip);
                return "ok";
            }
            catch(Exception ex)
            {
                return ex.ToString();
            }
        }

        private async Task ScreenShotTask(string ip)
        {
            Texture2D screenshot = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture();
            byte[] bytesPNG = UnityEngine.ImageConversion.EncodeToPNG(screenshot);
            string pngAsString = Convert.ToBase64String(bytesPNG);
            MsgForSend sendmsg = new MsgForSend();
            sendmsg.Ip = ip;
            sendmsg.Msg = pngAsString;
            SendMsgPool.Enqueue(sendmsg);
            sendCount += 1;
            await Task.CompletedTask;
        }

        #endregion

        #region 点击UI按钮
        /// <summary>
        /// 常规点击按钮
        /// </summary>
        /// <param name="args">args[1]是Hierarchy相对路径,args[2]是参数如path</param>
        /// <returns></returns>
        private object ClickOneButton(string ip, string[] args)
        {
            string res;
            try
            {
                if (args[0] == "leftclick")  //左键单击
                {
                    if (args[2] == "path")
                    {
                        var targetpath = args[1].Replace("//", "/");
                        targetObj = GameObject.Find(targetpath);
                        if (targetObj && targetObj.activeInHierarchy)
                        {
                            targetObj.GetComponent<Button>().onClick?.Invoke();
                            res = "click it success.";
                        }
                        else
                            res = "it is not found.";
                        throw new Exception(Error.NotFoundMessage);
                    }
                    else if (args[2] == "id")
                    {
                        int targetid = int.Parse(args[1]);
                        var targetpth = string.Empty;
                        targetObj = (GameObject)FindObjectFromInstanceID(targetid);
                        if (targetObj != null && targetObj.activeInHierarchy)
                        {
                            targetObj.GetComponent<Button>().onClick?.Invoke();
                            res = "click it success.";
                        }
                        else
                        {
                            res = "it is not found.";
                            throw new Exception(Error.NotFoundMessage);
                        }
                    }
                    else
                        res = "Tais args is not supported yet.";
                    return res;
                }
                else if (args[0] == "rightclick")   //TODO:右键单击
                {
                    res = "Test";
                    return res;
                }
                else if (args[0] == "leftdown")   //TODO:左键按下
                {
                    res = "Test";
                    return res;
                }
                else if(args[0] == "rightdown")   //TODO:右键按下
                {
                    res = "Test";
                    return res;
                }
                else if (args[0] == "leftlift")    //TODO:左键抬起
                {
                    res = "Test";
                    return res;
                }
                else if (args[0] == "rightlift")  //TODO:右键抬起
                {
                    res = "Test";
                    return res;
                }
                else
                {
                    res = "Test";
                    return res;
                }
            }
            catch(Exception ex)
            {
                return ex.ToString();
            }
        }
        #endregion
    }
    public static class GameRunTimeDataSet
    {
        public static void InitDataSet()
        {
            ms_gameObjectDict.Clear();
            ms_componentDict.Clear();
        }

        public static void AddGameObject(GameObject obj)
        {
            int nInstanceID = obj.GetInstanceID();
            if (!ms_gameObjectDict.ContainsKey(nInstanceID))
            {
                ms_gameObjectDict.Add(nInstanceID, obj);
            }
        }

        public static bool TryGetGameObject(int nInstanceID, out GameObject go)
        {
            return ms_gameObjectDict.TryGetValue(nInstanceID, out go);
        }

        public static void AddComponent(Component comp)
        {
            int nInstanceID = comp.GetInstanceID();
            if (!ms_componentDict.ContainsKey(nInstanceID))
            {
                ms_componentDict.Add(nInstanceID, comp);
            }
        }

        public static bool TryGetComponent(int nInstanceID, out UnityEngine.Component comp)
        {
            return ms_componentDict.TryGetValue(nInstanceID, out comp);
        }

        public static Dictionary<int, GameObject> ms_gameObjectDict = new Dictionary<int, GameObject>();
        public static Dictionary<int, Component> ms_componentDict = new Dictionary<int, Component>();
    }
    public class Error
    {
        public readonly static string NotFoundMessage = "error:notFound";
        public readonly static string ExceptionMessage = "error:exceptionOccur";
    }
    //public class ReflectionTool
    //{
    //    public static PropertyInfo GetPropertyNest(Type t, String name)
    //    {

    //        PropertyInfo pi = t.GetProperty(name);

    //        if (pi != null)
    //        {
    //            return pi;
    //        }

    //        if (t.BaseType != null)
    //        {
    //            return GetPropertyNest(t.BaseType, name);
    //        }

    //        return null;
    //    }

    //    public static object GetComponentAttribute(GameObject target, Type t, String attributeName)
    //    {
    //        if (target == null || t == null)
    //            return null;

    //        Component component = target.GetComponent(t);

    //        if (component == null)
    //            return null;

    //        PropertyInfo pi = GetPropertyNest(t, attributeName);

    //        if (pi == null || !pi.CanRead)
    //        {
    //            return null;
    //        }

    //        return pi.GetValue(component, null);
    //    }

    //    public static bool SetComponentAttribute(GameObject obj, Type t, String attributeName, object value)
    //    {

    //        if (t == null)
    //        {
    //            return false;
    //        }

    //        Component comp = obj.GetComponent(t);

    //        if (comp == null)
    //        {
    //            return false;
    //        }

    //        PropertyInfo pi = GetPropertyNest(t, attributeName);


    //        if (pi == null || !pi.CanWrite)
    //        {
    //            return false;
    //        }

    //        pi.SetValue(comp, value, null);

    //        return true;
    //    }
    //}
}

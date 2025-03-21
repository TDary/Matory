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
using Matory.HotMapSampler;
using Matory.Tools;

namespace Matory
{
    public class Mato : MonoBehaviour
    {
        private SocketServer socketServer;
        private int port = 2666;
        private bool startGatherMsg = false;
        private bool isGathering = false;
        public MsgProfiler m_Pro;
        private int frameNum = 0;
        private int ProfilerBeginFrame = 0;
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
        private Coroutine ProfileIEnumerator = null;
        private string SnapShotFilePath = string.Empty;
        private HotmapDataController _mHotmapController;
        private GeneratePoints _mGeneratePoints;
        private Dictionary<string, bool> m_profilerSampleModules = new Dictionary<string, bool>();
        public void Init()
        {
            DontDestroyOnLoad(this);
            m_Pro = new MsgProfiler();
            m_Pro.funMethods.Add("GetSdkVersion",GetSdkVersion);
            m_Pro.funMethods.Add("GetGameVersion", GetGameEngineVersion);
            m_Pro.funMethods.Add("StopConnection",StopConnection);
            m_Pro.funMethods.Add("Find_Text", FindText);
            m_Pro.funMethods.Add("Find_AllButton", FindAllButton);
            m_Pro.funMethods.Add("Set_ProfilerSampleModules", SetProfilerSampleModules);
            m_Pro.funMethods.Add("Gather_Profiler",GatherProfiler);
            m_Pro.funMethods.Add("Check_Profiler",CheckProfilerData);
            m_Pro.funMethods.Add("Get_Hierarchy",GetHierarchy);
            m_Pro.funMethods.Add("Get_Inspector",GetInspector);
            m_Pro.funMethods.Add("ClickOne", ClickOneButton);
            m_Pro.funMethods.Add("GetScreenShot",GetScreenShot);
            m_Pro.funMethods.Add("Object_Exist",IsObjectExist);
            m_Pro.funMethods.Add("ClickOneBySimulate", ClickOneSimulateMouse);
            m_Pro.funMethods.Add("PressOneBySimulate", PressOneSimulateMouse);
            m_Pro.funMethods.Add("UpOneBySimulate", UpOneSimulateMouse);
            m_Pro.funMethods.Add("CaptureMemorySnap",TakeMemorySnapShot);
            m_Pro.funMethods.Add("SetCamera", SetCameraPosition);
            m_Pro.funMethods.Add("SetGameObjectState", GameObjectSwitch);
            m_Pro.funMethods.Add("PerformanceData_Start",SampleDataStart);
            m_Pro.funMethods.Add("PerformanceData_Stop", SampleDataStop);
            m_Pro.funMethods.Add("PerformanceData_GetOne", GetOneFrameData);
            m_Pro.funMethods.Add("InitScenePointTool", InitGnerateTool);
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
                                                jw.Write(result.ToString());
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
            if (_mHotmapController != null) _mHotmapController.OnUpdate();
        }

        /// <summary>
        /// 发送消息给上层客户端逻辑
        /// </summary>
        /// <param name="msg"></param>
        private void SendMsg(string msg)
        {
            foreach (var session in socketServer.SessionPool.Values)
            {
                if (session.IP != "")
                {
                    JsonWriter jw = new JsonWriter();
                    jw.WriteObjectStart();
                    jw.WritePropertyName("Code");
                    jw.Write(200);
                    jw.WritePropertyName("Msg");
                    jw.Write(true);
                    jw.WritePropertyName("Data");
                    jw.Write(msg);
                    jw.WriteObjectEnd();
                    byte[] msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                    session.SockeClient.Send(msgBuffer);
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


        #region 获取游戏引擎版本
        private object GetGameEngineVersion(string ip,string[] args)
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
        /// 设置相机位置
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object SetCameraPosition(string ip, string[] args)
        {
            var position = args[0];
            var rotation = args[1];
            float position_x = Convert.ToSingle(position[0]);
            float position_y = Convert.ToSingle(position[1]);
            float position_z = Convert.ToSingle(position[2]);
            float rotate_x = Convert.ToSingle(rotation[0]);
            float rotate_y = Convert.ToSingle(rotation[1]);
            float rotate_z = Convert.ToSingle(rotation[2]);
            Vector3 changePosition = new Vector3(position_x, position_y, position_z);
            Camera.main.transform.position = changePosition;
            Camera.main.transform.rotation = Quaternion.Euler(rotate_x, rotate_y, rotate_z);
            return "ok";
        }

        /// <summary>
        /// 对象设置 激活和失活用的
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object GameObjectSwitch(string ip, string[] args)
        {
            var go = GameObject.Find(args[0]);
            if(bool.TryParse(args[1],out bool val))
            {
                if (go != null)
                    go.SetActive(val);
            }
            return "ok";
        }

        /// <summary>
        /// 性能数据采集开始
        /// </summary>
        /// <returns></returns>
        private object SampleDataStart(string ip, string[] args)
        {
            if (_mHotmapController == null)
            {
                _mHotmapController = new HotmapDataController();
                _mHotmapController.Init();
            }
            string resFilepath = args[0];
            if (int.TryParse(args[1],out int sampleArg))
            {
                return _mHotmapController.SampleStart(resFilepath, sampleArg);
            }
            else
            {
                return "Arg is error(not type int)";
            }
        }

        /// <summary>
        /// 获取单帧当前性能数据
        /// </summary>
        /// <returns></returns>
        private object GetOneFrameData(string ip, string[] args)
        {
            if (_mHotmapController == null) return "采集对象为空，无法获取数据";
            return _mHotmapController.GetOnePerformanceData();
        }

        /// <summary>
        /// 性能数据采集结束
        /// </summary>
        /// <returns></returns>
        private object SampleDataStop(string ip, string[] args)
        {
            if (_mHotmapController == null) return "采集对象为空，未开始采集不需要停止";
            return _mHotmapController.SampleStop();
        }

        /// <summary>
        /// 热力图点位生成工具初始化
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object InitGnerateTool(string ip, string[] args)
        {
            if (_mGeneratePoints == null)
                _mGeneratePoints = this.gameObject.AddComponent<GeneratePoints>();
            return "Gnerate tool Init successful.";
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
                            if (!string.IsNullOrEmpty(Dicdata["path"]))
                            {
                                Profiler_path = Dicdata["path"];
                                //判断下文件夹是否存在，不存在就创建一下
                                if(!Directory.Exists(Profiler_path))    
                                    Directory.CreateDirectory(Profiler_path);
                            }
                        }
                        startGatherMsg = true;
                        ProfileIEnumerator = StartCoroutine(StartGatherProfiler());
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
                            EndGather();
                        }
                        StopCoroutine(ProfileIEnumerator);
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
            InitProfiler();
            while (startGatherMsg)
            {
                if (isGathering)
                {
                    frameNum++;
                    if (frameNum >= 300)
                    {
                        EndGather();
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
                yield return null;
            }
        }

        private object SetProfilerSampleModules(string ip, string[] args)
        {
            try
            {
                Dictionary<string, bool> modules = JsonMapper.ToObject<Dictionary<string, bool>>(args[1]);
                foreach (var item in modules.Keys)
                {
                    bool dicVal = false;
                    if (m_profilerSampleModules.TryGetValue(item, out dicVal))
                        m_profilerSampleModules[item] = dicVal;
                    else
                        m_profilerSampleModules.Add(item, modules[item]);
                }
                return "SetSampleModule successful!";
            }
           catch(Exception ex)
            {
                return ex.ToString();
            }
        }

        private void BeginProfilerModules(Dictionary<string,bool> modules)
        {
            if (modules.Count < 1)  //未设置采集模块时将其他模块关闭，只开启CPU模块函数相关采集
            {
                Profiler.SetAreaEnabled(ProfilerArea.CPU, true);
                Profiler.SetAreaEnabled(ProfilerArea.Rendering, false);
                Profiler.SetAreaEnabled(ProfilerArea.Memory, false);
                Profiler.SetAreaEnabled(ProfilerArea.Physics, false);
                Profiler.SetAreaEnabled(ProfilerArea.Audio, false);
                Profiler.SetAreaEnabled(ProfilerArea.UI, false);
                Profiler.SetAreaEnabled(ProfilerArea.GlobalIllumination, false);
                Profiler.SetAreaEnabled(ProfilerArea.NetworkMessages, false);
                Profiler.SetAreaEnabled(ProfilerArea.Physics2D, false);
                Profiler.SetAreaEnabled(ProfilerArea.Video, false);
                Profiler.SetAreaEnabled(ProfilerArea.NetworkOperations, false);
            }
            else
            {
                foreach (var val in modules)
                {
                    switch (val.Key)
                    {
                        case "CPU":
                            Profiler.SetAreaEnabled(ProfilerArea.CPU, val.Value);
                            break;
                        case "Rendering":
                            Profiler.SetAreaEnabled(ProfilerArea.Rendering, val.Value);
                            break;
                        case "Memory":
                            Profiler.SetAreaEnabled(ProfilerArea.Memory, val.Value);
                            break;
                        case "Physics":
                            Profiler.SetAreaEnabled(ProfilerArea.Physics, val.Value);
                            Profiler.SetAreaEnabled(ProfilerArea.Physics2D, val.Value);
                            break;
                        case "Audio":
                            Profiler.SetAreaEnabled(ProfilerArea.Audio, val.Value);
                            break;
                        case "Video":
                            Profiler.SetAreaEnabled(ProfilerArea.Video, val.Value);
                            break;
                        case "UI":
                            Profiler.SetAreaEnabled(ProfilerArea.UI, val.Value);
                            break;
                        case "GI":
                            Profiler.SetAreaEnabled(ProfilerArea.GlobalIllumination, val.Value);
                            break;
                        case "Network":
                            Profiler.SetAreaEnabled(ProfilerArea.NetworkMessages, val.Value);
                            Profiler.SetAreaEnabled(ProfilerArea.NetworkOperations, val.Value);
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        private void BeginGather(string fileName)
        {
            ProfilerBeginFrame = Time.frameCount;
            BeginProfilerModules(m_profilerSampleModules);
            //标记data文件最大使用1GB储存空间,在磁盘存储空间比较紧张的情况下用
            Profiler.maxUsedMemory = 1024 * 1024 *1024;

            Profiler.logFile = Profiler_path + "/" + fileName;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            profilerDataPath = Profiler_path;
            profilerDataName = fileName;
        }

        private void EndGather()
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
                string capturefilePath = string.Empty;
                if (args.Length != 0 && args[0] != "")
                    capturefilePath = args[0];
                else
                    throw new Exception("The args is null or not exist.");
                _ = ScreenShotTask(ip, capturefilePath);
                return "ok";
            }
            catch(Exception ex)
            {
                return ex.ToString();
            }
        }

        private async Task ScreenShotTask(string ip,string filepath)
        {
            ScreenCapture.CaptureScreenshot(filepath);
            MsgForSend sendmsg = new MsgForSend();
            sendmsg.Ip = ip;
            sendmsg.Msg = $"{filepath}+截取完成";
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
                if (args[0] == "click")  //单击
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
                        if(int.TryParse(args[1], out int targetid))
                        {
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
                        {
                            res = "This id is not int type.";
                            throw new Exception(res);
                        }
                    }
                    else
                        res = "Tais args is not supported yet.";
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

        /// <summary>
        /// 模拟鼠标单击调用函数入口
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object ClickOneSimulateMouse(string ip, string[] args)
        {
            string res;
            if(args.Length >= 2)
            {
                if (args[0] != "")
                {
                    if (int.TryParse(args[1], out int targetid))
                    {
                        targetObj = (GameObject)FindObjectFromInstanceID(targetid);
                        if (targetObj != null && targetObj.activeInHierarchy)
                        {
                            var btnObj = targetObj.GetComponent<Button>();
                            if (btnObj != null)
                            {
                                switch (args[0])
                                {
                                    case "left":
                                        SimulateMouseClickModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Left);
                                        res = "click it success";
                                        break;
                                    case "right":
                                        SimulateMouseClickModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Right);
                                        res = "click it success";
                                        break;
                                    case "middle":
                                        SimulateMouseClickModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Middle);
                                        res = "click it success";
                                        break;
                                    default:
                                        res = "this value is not supported.";
                                        break;
                                }
                            }
                            else
                                res = "This Object has not a button component.";
                        }
                        else
                            res = "This Object is not exist.";
                    }
                    else
                        res = "This value is not int type.";
                }
                else
                    res = "This args[0] is empty.";
            }
            else
            {
                res = "This args has not enough value.";
            }
            return res;
        }


        /// <summary>
        /// 模拟鼠标按下调用函数入口
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object PressOneSimulateMouse(string ip, string[] args)
        {
            string res;
            if (args.Length >= 2)
            {
                if (args[0] != "")
                {
                    if (int.TryParse(args[1], out int targetid))
                    {
                        targetObj = (GameObject)FindObjectFromInstanceID(targetid);
                        if (targetObj != null && targetObj.activeInHierarchy)
                        {
                            var btnObj = targetObj.GetComponent<Button>();
                            if (btnObj != null)
                            {
                                switch (args[0])
                                {
                                    case "left":
                                        SimulateMousePressModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Left);
                                        res = "Press it success";
                                        break;
                                    case "right":
                                        SimulateMousePressModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Right);
                                        res = "Press it success";
                                        break;
                                    case "middle":
                                        SimulateMousePressModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Middle);
                                        res = "Press it success";
                                        break;
                                    default:
                                        res = "this value is not supported.";
                                        break;
                                }
                            }
                            else
                                res = "This Object has not a button component.";
                        }
                        else
                            res = "This Object is not exist.";
                    }
                    else
                        res = "This value is not int type.";
                }
                else
                    res = "This args[0] is empty.";
            }
            else
            {
                res = "This args has not enough value.";
            }
            return res;
        }

        /// <summary>
        /// 模拟鼠标抬起调用函数入口
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object UpOneSimulateMouse(string ip, string[] args)
        {
            string res;
            if (args.Length >= 2)
            {
                if (args[0] != "")
                {
                    if (int.TryParse(args[1], out int targetid))
                    {
                        targetObj = (GameObject)FindObjectFromInstanceID(targetid);
                        if (targetObj != null && targetObj.activeInHierarchy)
                        {
                            var btnObj = targetObj.GetComponent<Button>();
                            if (btnObj != null)
                            {
                                switch (args[0])
                                {
                                    case "left":
                                        SimulateMouseUpModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Left);
                                        res = "Up it success";
                                        break;
                                    case "right":
                                        SimulateMouseUpModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Right);
                                        res = "Up it success";
                                        break;
                                    case "middle":
                                        SimulateMouseUpModule(btnObj.gameObject, UnityEngine.EventSystems.PointerEventData.InputButton.Middle);
                                        res = "Up it success";
                                        break;
                                    default:
                                        res = "this value is not supported.";
                                        break;
                                }
                            }
                            else
                                res = "This Object has not a button component.";
                        }
                        else
                            res = "This Object is not exist.";
                    }
                    else
                        res = "This value is not int type.";
                }
                else
                    res = "This args[0] is empty.";
            }
            else
            {
                res = "This args has not enough value.";
            }
            return res;
        }

        /// <summary>
        /// 模拟鼠标操作按下模块
        /// </summary>
        /// <param name="objBtn">组件对象</param>
        /// <param name="btntype">左右中鼠标键</param>
        private void SimulateMousePressModule(GameObject objBtn, UnityEngine.EventSystems.PointerEventData.InputButton btntype)
        {
            var pointerEventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerEventData.button = btntype;
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            objBtn.SendMessage("OnMouseEnter", UnityEngine.SendMessageOptions.DontRequireReceiver);
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            objBtn.SendMessage("OnMouseDown", UnityEngine.SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// 模拟鼠标操作抬起模块
        /// </summary>
        /// <param name="objBtn"></param>
        /// <param name="btntype"></param>
        private void SimulateMouseUpModule(GameObject objBtn, UnityEngine.EventSystems.PointerEventData.InputButton btntype)
        {
            var pointerEventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerEventData.button = btntype;
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.initializePotentialDrag);
            objBtn.SendMessage("OnMouseOver", UnityEngine.SendMessageOptions.DontRequireReceiver);
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            objBtn.SendMessage("OnMouseUp", UnityEngine.SendMessageOptions.DontRequireReceiver);
            try
            {
                //避免点击后删除空间出现错误
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                objBtn.SendMessage("OnMouseUpAsButton", UnityEngine.SendMessageOptions.DontRequireReceiver);
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                objBtn.SendMessage("OnMouseExit", UnityEngine.SendMessageOptions.DontRequireReceiver);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 模拟鼠标操作单击模块
        /// </summary>
        /// <param name="objBtn"></param>
        /// <param name="btntype"></param>
        private void SimulateMouseClickModule(GameObject  objBtn, UnityEngine.EventSystems.PointerEventData.InputButton btntype)
        {
            var pointerEventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerEventData.button = btntype;
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            objBtn.SendMessage("OnMouseEnter",UnityEngine.SendMessageOptions.DontRequireReceiver);
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            objBtn.SendMessage("OnMouseDown", UnityEngine.SendMessageOptions.DontRequireReceiver);
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.initializePotentialDrag);
            objBtn.SendMessage("OnMouseOver", UnityEngine.SendMessageOptions.DontRequireReceiver);
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            objBtn.SendMessage("OnMouseUp", UnityEngine.SendMessageOptions.DontRequireReceiver);
            try
            {
                //避免点击后删除空间出现错误
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                objBtn.SendMessage("OnMouseUpAsButton", UnityEngine.SendMessageOptions.DontRequireReceiver);
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                objBtn.SendMessage("OnMouseExit", UnityEngine.SendMessageOptions.DontRequireReceiver);
            }
            catch(Exception e)
            {
                Debug.LogException(e);
            }
        }

        ///// <summary>
        ///// 模拟鼠标滚轮模块
        ///// </summary>
        //private void MouseWheel()
        //{

        //}
        #endregion

        #region 截取内存快照逻辑

        /// <summary>
        /// 内存快照回调函数
        /// </summary>
        /// <param name="str"></param>
        /// <param name="bl"></param>
        private void MemorySnapShotCallBack(string str,bool bl)
        {
            if (bl)
            {
                //截图然后发送成功截取快照的消息
                ScreenCapture.CaptureScreenshot(SnapShotFilePath.Replace("snap","png"));
                SendMsg("截取一次内存快照完成——");
            }
            else
            {
                Debug.LogError("截取一次内存快照失败——" + str);
                SendMsg("截取一次内存快照失败——"+str);
            }
        }

        /// <summary>
        /// 内存快照截取
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args">args[0]为type值 args[1]为文件path</param>
        private object TakeMemorySnapShot(string ip, string[] args)
        {
            Dictionary<string, bool> response = new Dictionary<string, bool> { { "DebugBuild", true }, { "Code", true }, { "SendMsg", false }};

            if (!Debug.isDebugBuild)
            {
                Debug.LogError("Current game is not a development build.");
                response["DebugBuild"] = false;
                return JsonMapper.ToJson(response);
            }

            foreach (string arg in args)
            {
                Debug.Log($"MatorySDK Profiler arg: {arg}");
            }

            if (args[0] != "" && args[1]!="")
            {
                string dir = Path.GetDirectoryName(args[1]);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                SnapShotFilePath = args[1];
                if (!args[1].Contains(".snap"))
                {
                    response["The args is not contains snap"] = false;
                    response["Code"] = false;
                    return JsonMapper.ToJson(response);
                }
                switch (args[0])
                {
#if UNITY_2022_3_OR_NEWER
                    case "All":
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, 
                            Unity.Profiling.Memory.CaptureFlags.ManagedObjects | Unity.Profiling.Memory.CaptureFlags.NativeObjects | 
                            Unity.Profiling.Memory.CaptureFlags.NativeAllocations | Unity.Profiling.Memory.CaptureFlags.NativeAllocationSites | 
                            Unity.Profiling.Memory.CaptureFlags.NativeStackTraces);
                        response["SendMsg"] = true;
                        break;
                    case "ManagedObjects":
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, Unity.Profiling.Memory.CaptureFlags.ManagedObjects);
                        response["SendMsg"] = true;
                        break;
                    case "NativeObjects":
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, Unity.Profiling.Memory.CaptureFlags.NativeObjects);
                        response["SendMsg"] = true;
                        break;
                    case "NativeAllocations":
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, Unity.Profiling.Memory.CaptureFlags.NativeAllocations);
                        response["SendMsg"] = true;
                        break;
                    case "NativeAllocationSites":
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, Unity.Profiling.Memory.CaptureFlags.NativeAllocationSites);
                        response["SendMsg"] = true;
                        break;
                    case "NativeStackTraces":
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, Unity.Profiling.Memory.CaptureFlags.NativeStackTraces);
                        response["SendMsg"] = true;
                        break;
                    default:
                        response["The typemark is not supported."] = false;
                        break;
#elif UNITY_2021_1_OR_NEWER
                    case "All":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack,
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.ManagedObjects | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeObjects |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocations | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocationSites |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeStackTraces);
                        response["SendMsg"] = true;
                        break;
                    case "ManagedObjects":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.ManagedObjects);
                        response["SendMsg"] = true;
                        break;
                    case "NativeObjects":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeObjects);
                        response["SendMsg"] = true;
                        break;
                    case "NativeAllocations":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocations);
                        response["SendMsg"] = true;
                        break;
                    case "NativeAllocationSites":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocationSites);
                        response["SendMsg"] = true;
                        break;
                    case "NativeStackTraces":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeStackTraces);
                        response["SendMsg"] = true;
                        break;
                    default:
                        response["The typemark is not supported."] = false;
                        break;
#else
                    case "All":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack,
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.ManagedObjects | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeObjects |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocations | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocationSites |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeStackTraces);
                        response["SendMsg"] = true;
                        break;
                    case "ManagedObjects":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.ManagedObjects);
                        response["SendMsg"] = true;
                        break;
                    case "NativeObjects":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeObjects);
                        response["SendMsg"] = true;
                        break;
                    case "NativeAllocations":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocations);
                        response["SendMsg"] = true;
                        break;
                    case "NativeAllocationSites":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocationSites);
                        response["SendMsg"] = true;
                        break;
                    case "NativeStackTraces":
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(args[1], MemorySnapShotCallBack, UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeStackTraces);
                        response["SendMsg"] = true;
                        break;
                    default:
                        response["The typemark is not supported."] = false;
                        break;
#endif
                }
                return JsonMapper.ToJson(response);
            }
            else
            {
                response["code"] = false;
                response["sendmsg"] = false;
                response["args is empty"] = false;
                return JsonMapper.ToJson(response);
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

using Matory.Net;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using Matory.MatoryServer;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Matory
{
    public class Mato : MonoBehaviour
    {
        private SocketServer _socketServer;
        private readonly int _port = 2666;
        private bool _startGatherMsg = false, _isGathering = false, _isRecording=false;
        public MsgProfiler mPro;
        private int _frameNum = 0, ProfilerBeginFrame = 0;
        private int _fileNum = 0;
        private List<string> _profilerDataNames;
        private List<string> _profilerDataPaths;
        private string _profilerDataName = "";
        private string _profilerDataPath = "";
        private GameObject _targetObj;
        private readonly ConcurrentQueue<MsgForSend> _sendMsgPool = new ConcurrentQueue<MsgForSend>();
        private readonly ConcurrentQueue<TransData> _getMsgPool = new ConcurrentQueue<TransData>();
        private readonly Dictionary<string,TransData> _getTransDataPool = new Dictionary<string,TransData>(20);
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();
        private int _requestCount = 0, _sendCount = 0;
        private string _profilerPath;
        private List<string> _collectionItem = new List<string>();//存放采集项目
        private Coroutine _profileIEnumerator = null;
        private Coroutine _recordUIOperateCoroutine = null;
        private string _snapShotFilePath = string.Empty;
        private HotmapDataController _mHotMapController;
        private readonly StringBuilder _dataJson = new StringBuilder();
        private readonly Dictionary<string, bool> _mProfilerSampleModules = new Dictionary<string, bool>();

        // Performance caches
        private const int MaxMessagesPerFrame = 5;
        private Canvas[] _mCanvasCache;
        private float _mCanvasCacheTime;
        private Dictionary<string, GameObject> _mPathCache = new Dictionary<string, GameObject>();
        private float _mPathCacheTime;
        private List<Graphic> _mRecordGraphicCache = new List<Graphic>();
        private float _mRecordCacheTimestamp;
        private readonly Dictionary<GameObject, string> _mRecordPathCache = new Dictionary<GameObject, string>();
        #region DMemTracker
        [DllImport("MemTrace.dll", EntryPoint = "InitMemTrace")]
        private static extern bool InitMemTrace();
        [DllImport("MemTrace.dll", EntryPoint = "UpdateMemory")]
        private static extern void UpdateMemory();
        [DllImport("MemTrace.dll", EntryPoint = "GetCurrentProcessMemory")]
        private static extern ulong GetProcessMemory();
        [DllImport("MemTrace.dll", EntryPoint = "GetCurrentCPUUsage")]
        public static extern double GetCurrentCPUUsage();
        private bool _isInitTrack = false;
        public double memoryLimitMb = 2048;
        #endregion
        public void Init()
        {
            DontDestroyOnLoad(this);
            mPro = new MsgProfiler();
            mPro.funMethods.Add("GetSdkVersion",GetSdkVersion);
            mPro.funMethods.Add("GetGameVersion", GetGameEngineVersion);
            mPro.funMethods.Add("StopConnection",StopConnection);
            mPro.funMethods.Add("Find_Text", FindText);
            mPro.funMethods.Add("Find_AllButton", FindAllButton);
            mPro.funMethods.Add("Set_ProfilerSampleModules", SetProfilerSampleModules);
            mPro.funMethods.Add("Gather_Profiler",GatherProfiler);
            mPro.funMethods.Add("Check_Profiler",CheckProfilerData);
            mPro.funMethods.Add("Get_Hierarchy",GetHierarchy);
            mPro.funMethods.Add("Get_Inspector",GetInspector);
            mPro.funMethods.Add("ClickOne", ClickOneButton);
            mPro.funMethods.Add("GetScreenShot",GetScreenShot);
            mPro.funMethods.Add("Object_Exist",IsObjectExist);
            mPro.funMethods.Add("ClickOneBySimulate", ClickOneSimulateMouse);
            mPro.funMethods.Add("PressOneBySimulate", PressOneSimulateMouse);
            mPro.funMethods.Add("UpOneBySimulate", UpOneSimulateMouse);
            mPro.funMethods.Add("CaptureMemorySnap",TakeMemorySnapShot);
            mPro.funMethods.Add("SetCamera", SetCameraPosition);
            mPro.funMethods.Add("SetGameObjectState", GameObjectSwitch);
            mPro.funMethods.Add("PerformanceData_Start", SampleHotMapDataStart);
            mPro.funMethods.Add("PerformanceData_Stop", SampleHotMapDataStop);
            mPro.funMethods.Add("PerformanceData_GetOne", GetOneFrameData);
            mPro.funMethods.Add("Start_UIRecord", StartRecordUIOperate);
            mPro.funMethods.Add("Stop_UIRecord", StopRecordUIOperate);
            mPro.funMethods.Add("Start_DTracker", StartTracker);
            mPro.funMethods.Add("Set_DTrackerLimit", SetSnapAndMemoryLimit);

            for (int i = 0; i < 5; i++)
            {
                bool thisPort = IsPortInUse(_port + i);
                if (thisPort)
                {
                    Debug.Log($"This port {_port + i} is in used");
                    continue;
                }
                else
                {
                    _socketServer = new SocketServer();
                    _socketServer.start(_port + i);    //监听端口号
                    _socketServer.mydelegate = ParseData;
                    Debug.Log($"Matory is Listen success for {_port + i}");
                    break;
                }
            }
        }

        void Update()
        {
            if (_requestCount > 0 || _sendCount > 0)
            {
                for (var i = 0; i < MaxMessagesPerFrame && (_requestCount > 0 || _sendCount > 0); i++)
                {
                    if (_getMsgPool.Count != 0)   //处理函数并执行，返回消息给客户端
                    {
                        if (_getMsgPool.TryDequeue(out var data))
                        {
                            bool isHasFun = false;
                            foreach (var item in _getTransDataPool)
                            {
                                if (item.Value.FuncArgs == data.FuncArgs && item.Value.FuncName == data.FuncName)
                                {
                                    isHasFun = true;
                                    var result = mPro.RunMethod(item.Key, mPro.funMethods, data);
                                    if (result != null)
                                    {
                                        string resMsg = result.ToString();
                                        var res = new ResData(200, true, resMsg);
                                        if (_socketServer.SessionPool.TryGetValue(item.Key, out var session))
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
                                            var msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                            session.SockeClient.Send(msgBuffer);
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        if (_socketServer.SessionPool.TryGetValue(item.Key, out var session2))
                                        {
                                            var jw = new JsonWriter();
                                            jw.WriteObjectStart();
                                            jw.WritePropertyName("Code");
                                            jw.Write(200);
                                            jw.WritePropertyName("Msg");
                                            jw.Write(true);
                                            jw.WritePropertyName("Data");
                                            jw.Write(result?.ToString() ?? "null");
                                            jw.WriteObjectEnd();
                                            var msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                            session2.SockeClient.Send(msgBuffer);
                                        }
                                    }
                                }
                            }
                            if (!isHasFun)
                            {
                                foreach (var item in _getTransDataPool)
                                {
                                    if (_socketServer.SessionPool.TryGetValue(item.Key, out var session3))
                                    {
                                        var jw = new JsonWriter();
                                        jw.WriteObjectStart();
                                        jw.WritePropertyName("Code");
                                        jw.Write(200);
                                        jw.WritePropertyName("Msg");
                                        jw.Write(true);
                                        jw.WritePropertyName("Data");
                                        jw.Write("There is no this Function.");
                                        jw.WriteObjectEnd();
                                        var msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                        session3.SockeClient.Send(msgBuffer);
                                        break;
                                    }
                                }
                            }
                            _requestCount--;
                        }
                    }
                    else if (_sendMsgPool.Count != 0)   //返回消息给客户端
                    {
                        if (_sendMsgPool.TryDequeue(out var data))
                        {
                            foreach (var session in _socketServer.SessionPool.Values)
                            {
                                if (session.IP == data.Ip)
                                {
                                    var jw = new JsonWriter();
                                    jw.WriteObjectStart();
                                    jw.WritePropertyName("Code");
                                    jw.Write(200);
                                    jw.WritePropertyName("Msg");
                                    jw.Write(data.Msg);
                                    jw.WriteObjectEnd();
                                    var msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                                    session.SockeClient.Send(msgBuffer);
                                    break;
                                }
                            }
                        }
                        _sendCount--;
                    }
                }
            }
            if (_mHotMapController != null) _mHotMapController.OnUpdate();
            if (_isInitTrack) UpdateMemory();
        }

        /// <summary>
        /// 发送消息给上层客户端逻辑
        /// </summary>
        /// <param name="msg"></param>
        private void SendMsg(string msg)
        {
            foreach (var session in _socketServer.SessionPool.Values)
            {
                if (session.IP != "")
                {
                    var jw = new JsonWriter();
                    jw.WriteObjectStart();
                    jw.WritePropertyName("Code");
                    jw.Write(200);
                    jw.WritePropertyName("Msg");
                    jw.Write(true);
                    jw.WritePropertyName("Data");
                    jw.Write(msg);
                    jw.WriteObjectEnd();
                    var msgBuffer = Encoding.UTF8.GetBytes(jw.ToString());
                    session.SockeClient.Send(msgBuffer);
                }
            }
        }

        public void ParseData(string ip,string msg)
        {
            TransData data = null;
            var runData = JsonMapper.ToObject<TransData>(msg);
            if (_getTransDataPool.TryGetValue(ip, out data))
                _getTransDataPool[ip] = runData;
            else
                _getTransDataPool.Add(ip, runData);
            _getMsgPool.Enqueue(runData);
            _requestCount += 1;
        }

        /// <summary>
        /// IsPortInUse
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        private static bool IsPortInUse(int port)
        {
            var isPortInUse = false;

            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            var activeUdpListeners = ipGlobalProperties.GetActiveUdpListeners();

            foreach (var endPoint in activeTcpListeners)
            {
                if (endPoint.Port == port)
                {
                    isPortInUse = true;
                    break;
                }
            }

            if (!isPortInUse)
            {
                foreach (var endPoint in activeUdpListeners)
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
            if (_socketServer != null)
            {
                _socketServer.stop();
            }
            return null;
        }

        private static UnityEngine.Object FindObjectFromInstanceID(int id)
        {
            return (UnityEngine.Object)typeof(UnityEngine.Object)
                    .GetMethod("FindObjectFromInstanceID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { id });
        }

        #region 获取Unity面板数据
        private bool ComponentContainProperty(Component component, string propertyName)
        {
            if (component != null && !string.IsNullOrEmpty(propertyName))
            {
                var findPropertyInfo = component.GetType().GetProperty(propertyName);
                return (findPropertyInfo != null);
            }
            return false;
        }
        private T GetComponentValue<T>(Component component, string propertyName)
        {
            if (component != null && !string.IsNullOrEmpty(propertyName))
            {
                var propertyInfo = component.GetType().GetProperty(propertyName);
                if (propertyInfo != null)
                {
                    var mi = propertyInfo.GetGetMethod(true);
                    if (mi != null)
                    {
                        return (T)mi.Invoke(component, null);
                    }

                    //return (T)propertyInfo.GetValue(component, null);
                }
            }
            return default(T);
        }
        private static void GetComponentProperty(Component component, JsonWriter jw)
        {
            try
            {
                var propertyInfos = component.GetType().GetProperties(BindingFlags.Public |
                                                                      BindingFlags.Instance |
                                                                      BindingFlags.SetProperty |
                                                                      BindingFlags.GetProperty);

                var fieldInfos = component.GetType().GetFields(BindingFlags.Public |
                                                               BindingFlags.Instance |
                                                               BindingFlags.SetField |
                                                               BindingFlags.GetField);

                for (var i = 0; i < propertyInfos.Length; ++i)
                {
                    var pi = propertyInfos[i];

                    //Debug.LogError("Property:" + pi.Name);

                    if (pi.CanWrite && pi.CanRead)
                    {
                        // call getter with these Property name will create new object;
                        if (pi.Name == "mesh" || pi.Name == "material" || pi.Name == "materials")
                        {
                            continue;
                        }

                        var obj = pi.GetValue(component, null);
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

                for (var i = 0; i < fieldInfos.Length; ++i)
                {
                    var fi = fieldInfos[i];
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
        private void WriteJsonData(JsonWriter jw, Component component)
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

            GetComponentProperty(component, jw);

            jw.WriteArrayEnd();

            jw.WriteObjectEnd();
        }
        private object GetInspector(string ip, string[] args)
        {
            try
            {
                var objId = int.Parse(args[0]);

                GameObject obj = null;

                if (GameRunTimeDataSet.TryGetGameObject(objId, out obj))
                {
                    var jw = new JsonWriter();

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

                    var components = obj.GetComponents<Component>();
                    for (var j = 0; j < components.Length; ++j)
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
        private static void WriteJsonData(JsonWriter jw, GameObject go)
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
                    var child = go.transform.GetChild(i).gameObject;
                    WriteJsonData(jw, child);
                }

                jw.WriteArrayEnd();
            }

            jw.WriteObjectEnd();
        }
        private static object GetHierarchy(string ip, string[] args)
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

                var rootGameObjects = new List<GameObject>();
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    foreach (var rootGo in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    {
                        rootGameObjects.Add(rootGo);
                    }
                }

                for (var i = 0; i < rootGameObjects.Count; ++i)
                {
                    WriteJsonData(jsonWriter, rootGameObjects[i]);
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

        #region 录制客户端UI操作
        private IEnumerator RecordUIClick(string currentIp)
        {
            float quitTime = 0, maxQuitTime = 7;
            float lastTime = Time.unscaledTime, nowTime = Time.unscaledTime;
            Selectable selectable = null;
            GameObject lastPressGameObject = null;
            GameObject lastSelectedGameObject = null, flagGameObject = null;
            string textVaule;
            while (true)
            {
                if(!_socketServer.IsInConnecting(currentIp))
                {
                    Debug.LogWarning("由于客户端断开链接，终止录制----");
                    _isRecording = false;
                    StopCoroutine(_recordUIOperateCoroutine);
                    _recordUIOperateCoroutine = null;
                    break;
                }

                if (_isRecording)
                {
                    if (Input.GetMouseButton(0))
                    {
                        quitTime += Time.unscaledDeltaTime;
                        if (Time.unscaledTime - _mRecordCacheTimestamp > 0.25f)
                        {
                            _mRecordGraphicCache = FindAllGameObject<Graphic>();
                            _mRecordCacheTimestamp = Time.unscaledTime;
                            _mRecordPathCache.Clear();
                            foreach (var g in _mRecordGraphicCache)
                            {
                                _mRecordPathCache[g.gameObject] = GetGameObjectPath(g.gameObject);
                            }
                        }
                        var mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                        _dataJson.Clear();
                        _dataJson.Append("[");
                        foreach (var graphic in _mRecordGraphicCache)
                        {
                            var rect = graphic.gameObject.GetComponent<RectTransform>();
                            var targetPoint = GetScreenCoordinates(rect);
                            if (targetPoint[0].x < mousePos.x && targetPoint[2].x > mousePos.x && targetPoint[0].y < mousePos.y && targetPoint[2].y > mousePos.y)
                            {
                                string path = _mRecordPathCache[graphic.gameObject];
                                _dataJson.Append("{\"path\":\"" + path + "\",\"id\":\"" + rect.gameObject.GetInstanceID().ToString() + "\"},");
                            }
                        }
                        if (_dataJson.Length > 1)
                            _dataJson.Remove(_dataJson.Length - 1, 1);
                        _dataJson.Append("]");
                        //发送给上层python端数据
                        SendMsg(_dataJson.ToString());
                    }
                    else
                    {
                        quitTime = 0;
                    }

                    if(quitTime >= maxQuitTime)
                    {
                        Debug.LogWarning("Close record ui operation.");
                        //发送给上层python端数据
                        SendMsg("Close record ui operation.");
                        StopCoroutine(_recordUIOperateCoroutine);
                        _recordUIOperateCoroutine = null;
                        yield break;
                    }

                    try
                    {
                        if(selectable!=null && selectable is IDragHandler && Input.GetMouseButtonUp(0))
                        {
                            _data.Add("name", GetGameObjectPath(selectable.gameObject));
                            _data.Add("type", selectable.GetType().ToString());
                            _data.Add("end position", Input.mousePosition.ToString());
                            _data.Add("time", (nowTime - lastTime).ToString());
                            SendMsg(JsonMapper.ToJson(_data));
                            _data.Clear();
                            selectable = null;
                        }
                        if (Input.GetMouseButtonDown(0))
                        {
                            Vector2 pos = Input.mousePosition;
                            _data.Add("press position", pos.ToString());
                            var percentX = pos.x / Screen.width;
                            var percentY = pos.y / Screen.height;
                            _data.Add("percent position", $"({percentX},{percentY})");
                            var touch = new Touch { position = pos };
                            var pointerEventData = MockUpPointerInputModule.GetPointerEventData(touch);
                            if (pointerEventData.pointerPress != null)
                                lastPressGameObject = pointerEventData.pointerPress;
                        }
                        else
                            lastPressGameObject = null;

                        if(flagGameObject != lastPressGameObject)
                        {
                            lastTime = nowTime;
                            nowTime = Time.unscaledTime;
                            flagGameObject = lastPressGameObject;
                            if (!lastSelectedGameObject == flagGameObject)
                            {
                                //重复选中物体时
                                //quitFlag++;
                            }
                            else
                            {
                                //当切换选中物体时
                                if (lastSelectedGameObject != null)
                                {
                                    InputField inputField = lastSelectedGameObject.GetComponent<InputField>();
                                    if (inputField)
                                    {
                                        textVaule = inputField.text;

                                        _data.Add("name", GetGameObjectPath(lastSelectedGameObject));
                                        _data.Add("type", inputField.GetType().ToString());
                                        _data.Add("value", textVaule);
                                        _data.Add("time", (nowTime - lastTime).ToString());
                                        SendMsg(JsonMapper.ToJson(_data));
                                        _data.Clear();
                                    }
                                }
                                lastSelectedGameObject = flagGameObject;
                            }
                            if (flagGameObject != null) 
                                selectable = flagGameObject.GetComponent<Selectable>();
                            else 
                                selectable = null;
                            if (_socketServer.IsInConnecting(currentIp))
                            {
                                _data.Add("name", GetGameObjectPath(flagGameObject));
                                if (selectable)
                                {
                                    _data.Add("type", selectable.GetType().ToString());
                                    if(selectable is IDragHandler && Input.GetMouseButtonDown(0))
                                    {
                                        _data.Add("start position", Input.mousePosition.ToString());
                                    }
                                }
                                _data.Add("time", (nowTime - lastTime).ToString());
                                SendMsg(JsonMapper.ToJson(_data));
                                _data.Clear();
                            }
                            if (!(selectable is InputField))
                            {
                                //不将这个置为空点击相同的控件就不会发送数据，但将这个置为空后，会影响ui的使用
                                // EventSystem.current.SetSelectedGameObject(null);
                                flagGameObject = null;
                            }
                        }
                        if(_data.Count > 0)
                        {
                            SendMsg(JsonMapper.ToJson(_data));
                            _data.Clear();
                        }
                    }
                    catch(Exception ex)
                    {
                        SendMsg(ex.ToString());
                    }
                }
                yield return null;
            }
        }

        private object StartRecordUIOperate(string ip, string[] args)
        {
            try
            {
                if (!_isRecording)
                {
                    _isRecording = true;
                    _recordUIOperateCoroutine = StartCoroutine(RecordUIClick(ip));
                }
                else
                {
                    Debug.LogWarning("UI录制已经是开启的状态----");
                }
                return "ok";
            }
            catch(Exception ex)
            {
                return ex.ToString();
            }
        }

        private object SetSnapAndMemoryLimit(string ip, string[] args)
        {
            if (args.Length > 1)
            {
                _snapShotFilePath = args[0];  // snapFilePath
                memoryLimitMb = double.Parse(args[1]); // memoryLimitMB
                Debug.Log($"Set snap path {_snapShotFilePath} and memory limit {memoryLimitMb}MB.");
                return "Set snap and memoryLimit sucessfull.";
            }
            return "Set snap and memoryLimit failed.";
        }

        private object StartTracker(string ip, string[] args)
        {
            if (_isInitTrack)
            {
                StartCoroutine(MonitorLogic());
                return "Tracker has been started.";
            }
            if (InitMemTrace())
            {
                _isInitTrack = true;
                StartCoroutine(MonitorLogic());
                return "Start Tracker sucessfull.";
            }
            return "Start Tracker failed.";
        }

        private object StopRecordUIOperate(string ip, string[] args)
        {
            _isRecording = false;
            StopCoroutine(_recordUIOperateCoroutine);
            return "ok";
        }

        // 转换单位
        double ToMBMemory(ulong mem)
        {
            return Math.Round((double)mem / (1024 * 1024), 2);
        }

        IEnumerator MonitorLogic()
        {
            if (memoryLimitMb == 0)
                yield return null;
            var waitSecond = new WaitForSeconds(1f);
            Debug.Log("开始进行内存监控——");
            while (true)
            {
                if (ToMBMemory(GetProcessMemory()) >= memoryLimitMb)
                {
                    if (string.IsNullOrEmpty(_snapShotFilePath))
                    {
                        _snapShotFilePath = Path.Combine(Application.persistentDataPath, DateTimeOffset.Now.ToUnixTimeSeconds().ToString(), ".snap");
                    }
                    else
                    {
#if UNITY_2022_3_OR_NEWER
                        Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(SnapShotFilePath, MemorySnapShotCallBack, 
                            Unity.Profiling.Memory.CaptureFlags.ManagedObjects | Unity.Profiling.Memory.CaptureFlags.NativeObjects | 
                            Unity.Profiling.Memory.CaptureFlags.NativeAllocations | Unity.Profiling.Memory.CaptureFlags.NativeAllocationSites | 
                            Unity.Profiling.Memory.CaptureFlags.NativeStackTraces);
#elif UNITY_2021_1_OR_NEWER
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(SnapShotFilePath, MemorySnapShotCallBack,
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.ManagedObjects | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeObjects |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocations | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocationSites |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeStackTraces);
#else
                        UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot(_snapShotFilePath, MemorySnapShotCallBack,
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.ManagedObjects | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeObjects |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocations | UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeAllocationSites |
                            UnityEngine.Profiling.Memory.Experimental.CaptureFlags.NativeStackTraces);
#endif
                        break;
                    }
                }
                yield return waitSecond;  // 每秒执行检测一次
            }
        }

        private class Node
        {
            public string path;

            [NonSerialized]
            public GameObject obj;
        }

        private List<T> FindAllGameObject<T>()
        {
            var gameObjects = new List<T>();

            Action<Node> action = n =>
            {
                T t = n.obj.GetComponent<T>();
                if (n.obj != null && t != null)
                {
                    gameObjects.Add(t);
                }
            };
            FindAllGameObject(action);
            return gameObjects;
        }

        private void FindAllGameObject(Action<Node> action)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                foreach (var obj in SceneManager.GetSceneAt(i).GetRootGameObjects())
                {
                    try
                    {
                        FindAllObjectFromParent(obj, GetGameObjectPath(obj), action);
                    }
                    catch(Exception exception)
                    {
                        Debug.Log(exception.Message);
                    }
                }
            }
            GameObject temp = null;
            try
            {
                temp = new GameObject();
                DontDestroyOnLoad(temp);
                var dontDestroyOnLoad = temp.scene;
                DestroyImmediate(temp);
                temp = null;
                foreach (var obj in dontDestroyOnLoad.GetRootGameObjects())
                {
                    try
                    {
                        FindAllObjectFromParent(obj, GetGameObjectPath(obj), action);
                    }
                    catch(Exception exception)
                    {
                        Debug.Log(exception.Message);
                    }
                }
            }
            finally
            {
                if (temp != null)
                    DestroyImmediate(temp);
            }
        }

        private string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "null";
            var path = "/" + obj.name;
            var parentTransform = obj.transform.parent;
            while (parentTransform != null)
            {
                path = "/" + parentTransform.name + path;
                parentTransform = parentTransform.parent;
            }
            return path;
        }

        /// <summary>
        /// 获取物体的全部子物体
        /// </summary>
        /// <param name="parent">父物体</param>
        /// <param name="parentPath">父路径</param>
        /// <param name="each">对于每一个子物体执行的行为</param>
        private void FindAllObjectFromParent(GameObject parent, string parentPath, Action<Node> each = null, bool isActive = false)
        {
            if (parent == null || (isActive && !parent.activeInHierarchy)) throw new Exception(Error.NotFoundMessage);
            var root = new Node()
            {
                path = parentPath,
                obj = parent,
            };
            var nodes = new List<Node>();
            nodes.Add(root);
            var index = 0;
            while (index != nodes.Count)
            {
                var now = nodes[index];
                var p = now.obj.transform;
                foreach (Transform transForm in p)
                {
                    var temp = new Node()
                    {
                        path = now.path + "/" + transForm.name,
                        obj = transForm.gameObject,
                    };
                    nodes.Add(temp);
                    if (each != null)
                    {
                        each(temp);
                    }
                }
                index++;
            }
        }

        /// <summary>
        /// 获取RectTransform屏幕空间的下的四个点,顺序：左下、左上、右上、右下 
        /// </summary>
        /// <param name="uiElement">目标RectTransform</param>
        /// <returns></returns>
        private static Vector3[] GetScreenCoordinates(RectTransform uiElement)
        {
            var worldCorners = new Vector3[4];
            var screenCorners = new Vector3[4];
            uiElement.GetWorldCorners(worldCorners);
            Canvas canvas = uiElement.GetComponentInParent<Canvas>();
            if (canvas == null) canvas = uiElement.GetComponent<Canvas>();
            if (canvas == null) return worldCorners;
            Camera camera;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                camera = canvas.worldCamera;
            else
                camera = Camera.main;
            if (camera != null && camera != Camera.main)
            {
                for (var i = 0; i < 4; i++)
                {
                    screenCorners[i] = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[i]);
                }
                return screenCorners;
            }
            return worldCorners;
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
            var jw = new JsonWriter();

            jw.WriteArrayStart();

            for (var i = 0; i < _profilerDataNames.Count; ++i)
            {
                jw.WriteObjectStart();

                jw.WritePropertyName("path");//写入属性名称（'路径'）
                jw.Write(_profilerDataPaths[i]);

                jw.WritePropertyName("name");
                jw.Write(_profilerDataNames[i]);

                jw.WriteObjectEnd();
            }

            jw.WriteArrayEnd();

            _profilerDataNames.Clear();
            _profilerDataPaths.Clear();

            return jw.ToString();
        }
        private object CheckProfilerData(string ip, string[] args)
        {
            try
            {
                var jw = new JsonWriter();
                jw.WriteObjectStart();
                if (_collectionItem.Contains("profiler_gather"))
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
            _frameNum = 0;
            _fileNum = 0;
            _isGathering = false;

            _profilerDataNames = new List<string>();
            _profilerDataPaths = new List<string>();
        }

        /// <summary>
        /// 设置相机位置
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private object SetCameraPosition(string ip, string[] args)
        {
            var position = args[0].Split(',');
            var rotation = args[1].Split(',');
            var positionX = float.Parse(position[0],CultureInfo.InvariantCulture);
            var positionY = float.Parse(position[1], CultureInfo.InvariantCulture);
            var positionZ = float.Parse(position[2], CultureInfo.InvariantCulture);
            var rotateX = float.Parse(rotation[0], CultureInfo.InvariantCulture);
            var rotateY = float.Parse(rotation[1], CultureInfo.InvariantCulture);
            var rotateZ = float.Parse(rotation[2], CultureInfo.InvariantCulture);
            var changePosition = new Vector3(positionX, positionY, positionZ);
            Camera.main.transform.position = changePosition;
            Camera.main.transform.rotation = Quaternion.Euler(rotateX, rotateY, rotateZ);
            return "ok";
        }

        /// <summary>
        /// 对象设置 激活和失活用的
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private static object GameObjectSwitch(string ip, string[] args)
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
        private object SampleHotMapDataStart(string ip, string[] args)
        {
            if (_mHotMapController == null)
            {
                _mHotMapController = new HotmapDataController();
                _mHotMapController.Init();
            }
            var resFilepath = args[0];  // args[0]输出结果路径 args[1]设置采集模式1为每帧采集写入，0为不每帧写，需要自己获取单帧数据
            if (int.TryParse(args[1],out int sampleArg))
            {
                return _mHotMapController.SampleStart(resFilepath, sampleArg);
            }
            else
            {
                return "Arg is error(not type int)";
            }
        }

        /// <summary>
        /// 获取单帧当前性能数据,在采集模式为0时使用
        /// </summary>
        /// <returns></returns>
        private object GetOneFrameData(string ip, string[] args)
        {
            if (_mHotMapController == null) return "采集对象为空，无法获取数据";
            return _mHotMapController.GetOnePerformanceData();
        }

        /// <summary>
        /// 性能数据采集结束
        /// </summary>
        /// <returns></returns>
        private object SampleHotMapDataStop(string ip, string[] args)
        {
            if (_mHotMapController == null) return "采集对象为空，未开始采集不需要停止";
            return _mHotMapController.SampleStop();
        }

        /// <summary>
        /// 采集UnityProfiler数据
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private object GatherProfiler(string ip, string[] args)
        {
            var response = new Dictionary<string, bool> { { "DebugBuild", true }, { "code", true } ,{ "profiler_gather",false } };
            if (!Debug.isDebugBuild)
            {
                Debug.LogError("Current game is not a development build.");
                response["DebugBuild"] = false;
                return JsonMapper.ToJson(response);
            }

            foreach(var arg in args)
            {
                Debug.Log($"MatorySDK Profiler arg: {arg}");
            }

            try
            {
                var parameter = args[1];

                if(parameter == "1")
                {
                    //开始采集
                    var dicArgs = JsonMapper.ToObject<Dictionary<string, string>>(args[2]);//解析参数案例
                    var dicCollection = JsonMapper.ToObject<Dictionary<string, string>>(dicArgs["collection"]);
                    _collectionItem = dicCollection.Keys.ToList<string>();
                    var dicData = JsonMapper.ToObject<Dictionary<string, string>>(dicArgs["data"]);

                    //深度profiler采集
                    if(_profileIEnumerator == null && _collectionItem.Contains("profiler_gather"))
                    {
                        _profilerPath = Application.persistentDataPath;
                        if (dicData.ContainsKey("path"))
                        {
                            if (!string.IsNullOrEmpty(dicData["path"]))
                            {
                                _profilerPath = dicData["path"];
                                //判断下文件夹是否存在，不存在就创建一下
                                if(!Directory.Exists(_profilerPath))    
                                    Directory.CreateDirectory(_profilerPath);
                            }
                        }
                        _startGatherMsg = true;
                        _profileIEnumerator = StartCoroutine(StartGatherProfiler(ip));
                        response["profiler_gather"] = true;
                    }
                    else
                    {
                        if (_collectionItem.Contains("profiler_gather"))
                        {
                            response["profiler_gather"] = false;
                        }
                    }
                }
                else if(parameter == "0")
                {
                    //结束采集
                    if(_profileIEnumerator != null && _collectionItem.Contains("profiler_gather"))
                    {
                        if (_startGatherMsg)
                        {
                            _startGatherMsg = false;
                            EndGather();
                        }
                        StopCoroutine(_profileIEnumerator);
                        response["profiler_gather"] = true;
                    }
                    else
                    {
                        if (_collectionItem.Contains("profiler_gather"))
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

        private IEnumerator StartGatherProfiler(string currentIp)
        {
            InitProfiler();
            while (_startGatherMsg)
            {
                if (_isGathering)
                {
                    _frameNum++;
                    if (_frameNum >= 300)
                    {
                        EndGather();
                        _fileNum++;
                        _frameNum = 0;
                        _isGathering = false;
                    }
                }
                else
                {
                    BeginGather("ProfilerGather-" + DateTime.Now.ToString(format: "yyyy-MM-dd-HH-mm-ss") + "-" + _fileNum);
                    _isGathering = true;
                    _frameNum++;
                }
                if (!_socketServer.IsInConnecting(currentIp))
                {
                    Debug.LogWarning("由于Socket断开链接，终止数据采集----");
                    break;
                }
                yield return null;
            }
        }

        private object SetProfilerSampleModules(string ip, string[] args)
        {
            try
            {
                var modules = JsonMapper.ToObject<Dictionary<string, bool>>(args[1]); // {"CPU":"true",...}
                foreach (var item in modules.Keys)
                {
                    if (_mProfilerSampleModules.TryGetValue(item, out var dicVal))
                        _mProfilerSampleModules[item] = dicVal;
                    else
                        _mProfilerSampleModules.Add(item, modules[item]);
                }
                return "SetSampleModule successful!";
            }
            catch(Exception ex)
            {
                return ex.ToString();
            }
        }

        private Canvas[] GetCanvasCache()
        {
            if (_mCanvasCache == null || Time.time - _mCanvasCacheTime > 1f)
            {
                _mCanvasCache = FindObjectsOfType<Canvas>();
                _mCanvasCacheTime = Time.time;
            }
            return _mCanvasCache;
        }

        private static void BeginProfilerModules(Dictionary<string,bool> modules)
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
            BeginProfilerModules(_mProfilerSampleModules);
            //标记data文件最大使用1GB储存空间,在磁盘存储空间比较紧张的情况下用
            Profiler.maxUsedMemory = 1024 * 1024 *1024;

            Profiler.logFile = _profilerPath + "/" + fileName;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            _profilerDataPath = _profilerPath;
            _profilerDataName = fileName;
        }

        private void EndGather()
        {
            Profiler.enabled = false;
            Profiler.logFile = "";
            Profiler.enableBinaryLog = false;

            _profilerDataNames.Add(_profilerDataName);
            _profilerDataPaths.Add(_profilerDataPath);
        }
        #endregion

        #region 获取UI逻辑

        /// <summary>
        /// 获取当前界面所有UI按钮，返回路径以及Obj的instanceId
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private object FindAllButton(string ip, string[] args)
        {
            var jw = new JsonWriter();
            jw.WriteArrayStart();
            var allCanvas = GetCanvasCache();
            foreach(var can in allCanvas)
            {
                var allBtnInCanva = can.GetComponentsInChildren<Button>();
                foreach (var button in allBtnInCanva)
                {
                    jw.WriteObjectStart();
                    jw.WritePropertyName("InstanceId");
                    jw.Write(button.gameObject.GetInstanceID());
                    jw.WritePropertyName("ButtonName");
                    var textObj = button.gameObject.GetComponentInChildren<Text>();
                    if (textObj == null)
                    {
                        var textMeshInput = button.gameObject.GetComponentInChildren<InputField>(); //寻找是否有InputField组件
                        if (textMeshInput != null)
                            jw.Write(textMeshInput.text);
                        else
                            jw.Write("");
                    }
                    else
                        jw.Write(textObj.text);
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
            var jw = new JsonWriter();
            jw.WriteArrayStart();
            var allCanva = GetCanvasCache();
            foreach (var item in allCanva)
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
                            var currentUIPath = GetHierarchyPath(text.transform);
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
                            var currentUIPath = GetHierarchyPath(inputField.transform);
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
        private static string GetHierarchyPath(Transform transform)
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
                    if (int.TryParse(args[1], out int inSid))
                    {
                        if (FindObjectFromInstanceID(inSid) != null)
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
                        _targetObj = GameObject.Find(args[1]);
                        if(_targetObj != null)
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
            var jw = new JsonWriter();
            jw.WriteObjectStart();
            jw.WritePropertyName("ObjectExistState");
            jw.Write(res);
            jw.WritePropertyName("ReturnMsg");
            jw.Write(resMsg);
            jw.WriteObjectEnd();
            return jw.ToString();
        }

        private Button GetChildButton(Transform parent)
        {
            foreach (Transform child in parent)
            {
                var button = child.GetComponent<Button>();
                if (button != null)
                    return button;
                var result = GetChildButton(child);
                if (result != null) return result;
            }
            return null;
        }

        private Button GetChildButton(Transform parent, string buttonName)
        {
            foreach (Transform child in parent)
            {
                var button = child.GetComponent<Button>();
                if (button != null && button.name == buttonName)
                    return button;
                var result = GetChildButton(child, buttonName);
                if (result != null) return result;
            }
            return null;
        }
        #endregion

        #region 获取截图功能
        private object GetScreenShot(string ip, string[] args)
        {
            try
            {
                string captureFilePath = string.Empty;
                if (args.Length != 0 && args[0] != "")
                    captureFilePath = args[0];
                else
                    throw new Exception("The args is null or not exist.");
                _ = ScreenShotTask(ip, captureFilePath);
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
            var sendMsg = new MsgForSend();
            sendMsg.Ip = ip;
            sendMsg.Msg = $"{filepath}+截取完成";
            _sendMsgPool.Enqueue(sendMsg);
            _sendCount += 1;
            await Task.CompletedTask;
        }

        #endregion

        #region 点击UI按钮
        /// <summary>
        /// 常规点击按钮
        /// </summary>
        /// <param name="args">args[1]是Hierarchy相对路径,args[2]是参数如path</param>
        /// <returns></returns>
        private object ClickOneButton(string Ip, string[] args)
        {
            try
            {
                string res;
                if (args[0] == "click")  //单击
                {
                    if (args[2] == "path")
                    {
                        var targetPath = args[1].Replace("//", "/");
                        _targetObj = GameObject.Find(targetPath);
                        if (_targetObj && _targetObj.activeInHierarchy)
                        {
                            _targetObj.GetComponent<Button>().onClick?.Invoke();
                            res = "click it success.";
                        }
                        else
                            res = "it is not found.";
                        throw new Exception(Error.NotFoundMessage);
                    }
                    else if (args[2] == "id")
                    {
                        if(int.TryParse(args[1], out var targetId))
                        {
                            _targetObj = (GameObject)FindObjectFromInstanceID(targetId);
                            if (_targetObj != null && _targetObj.activeInHierarchy)
                            {
                                _targetObj.GetComponent<Button>().onClick?.Invoke();
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
                    if (int.TryParse(args[1], out int targetId))
                    {
                        _targetObj = (GameObject)FindObjectFromInstanceID(targetId);
                        if (_targetObj != null && _targetObj.activeInHierarchy)
                        {
                            var btnObj = _targetObj.GetComponent<Button>();
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
                    if (int.TryParse(args[1], out int targetId))
                    {
                        _targetObj = (GameObject)FindObjectFromInstanceID(targetId);
                        if (_targetObj != null && _targetObj.activeInHierarchy)
                        {
                            var btnObj = _targetObj.GetComponent<Button>();
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
                    if (int.TryParse(args[1], out int targetId))
                    {
                        _targetObj = (GameObject)FindObjectFromInstanceID(targetId);
                        if (_targetObj != null && _targetObj.activeInHierarchy)
                        {
                            var btnObj = _targetObj.GetComponent<Button>();
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
        /// <param name="btnType">左右中鼠标键</param>
        private void SimulateMousePressModule(GameObject objBtn, UnityEngine.EventSystems.PointerEventData.InputButton btnType)
        {
            var pointerEventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerEventData.button = btnType;
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            objBtn.SendMessage("OnMouseEnter", UnityEngine.SendMessageOptions.DontRequireReceiver);
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(objBtn, pointerEventData, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            objBtn.SendMessage("OnMouseDown", UnityEngine.SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// 模拟鼠标操作抬起模块
        /// </summary>
        /// <param name="objBtn"></param>
        /// <param name="btnType"></param>
        private void SimulateMouseUpModule(GameObject objBtn, UnityEngine.EventSystems.PointerEventData.InputButton btnType)
        {
            var pointerEventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerEventData.button = btnType;
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
        /// <param name="btnType"></param>
        private void SimulateMouseClickModule(GameObject  objBtn, UnityEngine.EventSystems.PointerEventData.InputButton btnType)
        {
            var pointerEventData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointerEventData.button = btnType;
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
                ScreenCapture.CaptureScreenshot(_snapShotFilePath.Replace("snap","png"));
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
                _snapShotFilePath = args[1];
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
                        response["The typeMark is not supported."] = false;
                        break;
#endif
                }
                return JsonMapper.ToJson(response);
            }
            else
            {
                response["code"] = false;
                response["sendMsg"] = false;
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
            MSGameObjectDict.Clear();
            MSComponentDict.Clear();
        }

        public static void AddGameObject(GameObject obj)
        {
            int nInstanceID = obj.GetInstanceID();
            if (!MSGameObjectDict.ContainsKey(nInstanceID))
            {
                MSGameObjectDict.Add(nInstanceID, obj);
            }
        }

        public static bool TryGetGameObject(int nInstanceID, out GameObject go)
        {
            return MSGameObjectDict.TryGetValue(nInstanceID, out go);
        }

        public static void AddComponent(Component comp)
        {
            int nInstanceID = comp.GetInstanceID();
            if (!MSComponentDict.ContainsKey(nInstanceID))
            {
                MSComponentDict.Add(nInstanceID, comp);
            }
        }

        public static bool TryGetComponent(int nInstanceID, out UnityEngine.Component comp)
        {
            return MSComponentDict.TryGetValue(nInstanceID, out comp);
        }

        private static readonly Dictionary<int, GameObject> MSGameObjectDict = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, Component> MSComponentDict = new Dictionary<int, Component>();
    }
    public class Error
    {
        public static readonly string NotFoundMessage = "error:notFound";
        public static readonly string ExceptionMessage = "error:exceptionOccur";
    }
}

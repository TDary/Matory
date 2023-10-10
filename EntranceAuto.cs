using LitJson;
using Matory.Net;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using UnityEngine.Profiling;
using System.Reflection;

namespace Matory
{
    public class Mato : MonoBehaviour
    {
        private SocketServer webs;
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
        private Dictionary<int, string> idPool = new Dictionary<int, string>(500);
        private void Init()
        {
            DontDestroyOnLoad(this);
            m_Pro = new MsgProfiler();
            m_Pro.funMethods.Add("GetVersion",GetSdkVersion);
            m_Pro.funMethods.Add("GetUnityVersion", GetUnityVersion);
            m_Pro.funMethods.Add("StopConnection",StopConnection);
            m_Pro.funMethods.Add("Find_Text", FindText);
            m_Pro.funMethods.Add("Find_AllButton", FindAllButton);
            m_Pro.funMethods.Add("Gather_Profiler",GatherProfiler);
            m_Pro.funMethods.Add("Check_Profiler",CheckProfilerData);
            m_Pro.funMethods.Add("Get_Hierarchy",GetHierarchy);
            m_Pro.funMethods.Add("Get_Inspector",GetInspector);
            m_Pro.funMethods.Add("ClickOne", ClickOneButton);
            m_Pro.funMethods.Add("OpenScreenShot",StartScreenShot);
            for (int i = 0; i < 5; i++)
            {
                bool thisport = IsPortInUse(port + i);
                if (thisport)
                {
                    Debug.Log($"This port {thisport} is in used");
                    continue;
                }
                else
                {
                    webs = new SocketServer();
                    webs.start(port + i);    //监听端口号
                    webs.mydelegate = ParseData;
                    Debug.Log($"Listen success for {thisport}");
                    break;
                }
            }
        }

        public ResData ParseData(string msg)
        {
            ResData res;
            if (msg != null)
            {
                try
                {
                    var resData = JsonMapper.ToObject<TransData>(msg);
                    var result = m_Pro.RunMethod(m_Pro.funMethods, resData);
                    res = new ResData(200, true, JsonMapper.ToJson(result));
                }
                catch(Exception ex)
                {
                    res = new ResData(500, false, ex.Message.ToString());
                }
            }
            else
            {
                res = new ResData(400,false,"The given msg is null");
            }
            return res;
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
        private object StopConnection(string[] args)
        {
            if (webs != null)
            {
                webs.stop();
            }
            return null;
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
        private object GetInspector(string[] args)
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
        private object GetHierarchy(string[] args)
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
        private object GetUnityVersion(string[] args)
        {
            try
            {
                return Application.unityVersion;
            }
            catch(Exception ex)
            {
                return ex.Message.ToString();
            }
        }
        #endregion

        #region 获取sdk版本
        private object GetSdkVersion(string[] args)
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
        private object CheckProfilerData(string[] args)
        {
            try
            {
                return GetProfileData();
            }
            catch(Exception ex)
            {
                return ex.Message.ToString();
            }
        }
        #endregion

        #region 采集UnityProfilerData逻辑
        private IEnumerator startGatherProfilerEnum = null;
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
        private object GatherProfiler(string[] args)
        {
            string res;
            try
            {
                bool startArg = args.Length > 1 && (args[1] == "1");
                res = "ok";

                if (startArg)
                {
                    if (startGatherProfilerEnum == null)
                    {
                        startGatherMsg = true;
                        startGatherProfilerEnum = StartGatherProfiler();
                        StartCoroutine(startGatherProfilerEnum);

                        res = "start gather profiler.";
                    }
                    else
                    {
                        res = "it's already started.";
                    }
                }
                else
                {
                    if (startGatherProfilerEnum != null)
                    {
                        if (isGathering)
                        {
                            startGatherMsg = false;
                            StopGather();
                        }

                        StopCoroutine(startGatherProfilerEnum);
                        startGatherProfilerEnum = null;

                        res = "stop gather profiler.";
                    }
                    else
                        res = "it's has been stop.";
                }
            }
            catch (Exception e)
            {
                res = e.Message.ToString();
            }
            return res;
        }

        IEnumerator StartGatherProfiler()
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
                    BeginGather("AutoTest-" + DateTime.Now.ToString(format: "yyyy-MM-dd-HH-mm-ss") + "-" + fileNum);
                    isGathering = true;
                    frameNum++;
                }
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

            Profiler.logFile = Application.persistentDataPath + "/" + fileName;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;

            profilerDataPath = Application.persistentDataPath;
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
        private object FindAllButton(string[] args)
        {
            JsonWriter jw = new JsonWriter();
            jw.WriteArrayStart();
            Canvas[] allcanvas = FindObjectsOfType<Canvas>();
            foreach(var can in allcanvas)
            {
                Button btn = GetChildButton(can.transform);
                if(btn != null)
                {
                    jw.WriteObjectStart();
                    jw.WritePropertyName("id");//写入属性名称（"id"）
                    jw.Write(btn.gameObject.GetInstanceID());

                    jw.WritePropertyName("path");
                    jw.Write(GetHierarchyPath(btn.transform));
                    jw.WriteObjectEnd();
                }
            }
            jw.WriteArrayEnd();
            return jw.ToString();
        }

        //获取UI上的文本对象
        private object FindText(string[] args)
        {
            List<string> allres = new List<string>();
            Canvas[] allcanva = FindObjectsOfType<Canvas>();
            foreach (var item in allcanva)
            {
                Text result = GetChildText(item.transform, args[1]);
                if (result != null)
                {
                    string currentUIPath = GetHierarchyPath(result.transform);
                    allres.Add(currentUIPath);
                }
            }
            return allres;
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

        private Text GetChildText(Transform parent, string currentText)
        {
            foreach (Transform child in parent)
            {
                //寻找是否有Text组件
                Text text = child.GetComponent<Text>();
                if (text != null && text.text == currentText)
                    return text;

                //寻找是否有InputField组件
                InputField inputText = child.GetComponent<InputField>();
                if (inputText != null && inputText.text == currentText)
                    return text;
                
                //递归遍历一下子对象
                GetChildText(child, currentText);
            }
            return null;
        }

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

        #region 开启截图功能

        private object StartScreenShot(string[] args)
        {
            try
            {
                StartCoroutine(ScreenShotCoroutine());
                return "ok";
            }
            catch(Exception ex)
            {
                return ex.Message.ToString();
            }
        }

        private IEnumerator ScreenShotCoroutine()
        {
            yield return new WaitForEndOfFrame();
            Texture2D screenshot = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture();
            byte[] bytesPNG = UnityEngine.ImageConversion.EncodeToPNG(screenshot);
            string pngAsString = Convert.ToBase64String(bytesPNG);
            //server.Send(client.TcpClient, prot.pack(pngAsString));
        }
        #endregion

        #region 点击UI按钮
        /// <summary>
        /// 常规点击按钮
        /// </summary>
        /// <param name="args">args[1]是Hierarchy相对路径,args[2]是参数如path</param>
        /// <returns></returns>
        private object ClickOneButton(string[] args)
        {
            string res;
            try
            {
                if (args[2] == "path")
                {
                    var targetpath = args[1].Replace("//", "/");
                    targetObj = GameObject.Find(targetpath);
                    if (targetObj && targetObj.activeInHierarchy)
                    {
                        idPool.Add(targetObj.GetInstanceID(), targetpath);
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
                    if (idPool.TryGetValue(targetid, out targetpth))
                    {
                        targetObj = GameObject.Find(targetpth);
                        if (targetObj.activeInHierarchy)
                        {
                            targetObj.GetComponent<Button>().onClick?.Invoke();
                            res = "click it success.";
                        }
                        else
                            res = "it has not active yet.";
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
            catch(Exception ex)
            {
                return ex.Message.ToString();
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
    public class ReflectionTool
    {
        public static PropertyInfo GetPropertyNest(Type t, String name)
        {

            PropertyInfo pi = t.GetProperty(name);

            if (pi != null)
            {
                return pi;
            }

            if (t.BaseType != null)
            {
                return GetPropertyNest(t.BaseType, name);
            }

            return null;
        }

        public static object GetComponentAttribute(GameObject target, Type t, String attributeName)
        {
            if (target == null || t == null)
                return null;

            Component component = target.GetComponent(t);

            if (component == null)
                return null;

            PropertyInfo pi = GetPropertyNest(t, attributeName);

            if (pi == null || !pi.CanRead)
            {
                return null;
            }

            return pi.GetValue(component, null);
        }

        public static bool SetComponentAttribute(GameObject obj, Type t, String attributeName, object value)
        {

            if (t == null)
            {
                return false;
            }

            Component comp = obj.GetComponent(t);

            if (comp == null)
            {
                return false;
            }

            PropertyInfo pi = GetPropertyNest(t, attributeName);


            if (pi == null || !pi.CanWrite)
            {
                return false;
            }

            pi.SetValue(comp, value, null);

            return true;
        }
    }
}

using LitJson;
using Matory.Net;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using LitJson;
using System;
using System.Collections;
using UnityEngine.Profiling;

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
        private void Init()
        {
            DontDestroyOnLoad(this);
            m_Pro = new MsgProfiler();
            m_Pro.funMethods.Add("Find_Text", FindText);
            m_Pro.funMethods.Add("Gather_Profiler",GatherProfiler);
            m_Pro.funMethods.Add("Check_Profiler",CheckProfilerData);
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
            string res = "";
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
                {
                    return text;
                }
                //递归遍历一下子对象
                GetChildText(child, currentText);
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
                {
                    return button;
                }
                //递归遍历一下子对象
                GetChildButton(child, buttonName);
            }
            return null;
        }
        #endregion

    }
}

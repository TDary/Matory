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

namespace Matory
{
    public class Mato : MonoBehaviour
    {
        private SocketServer webs;
        private int port = 2666;
        private MsgProfiler m_Pro;
        private void Init()
        {
            DontDestroyOnLoad(this);
            m_Pro = new MsgProfiler();
            m_Pro.funMethods.Add("Find_Text", FindText);
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

        //获取UI上的文本对象
        public object FindText(string[] args)
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

        private Text GetChildText(Transform parent,string currentText)
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

        private Button GetChildButton(Transform parent,string buttonName)
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
    }
}

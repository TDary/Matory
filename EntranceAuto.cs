using LitJson;
using Matory.Net;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using UnityEngine;

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
            ResData res = null;
            if (msg != null)
            {
                var resData = JsonMapper.ToObject<TransData>(msg);
                m_Pro.RunMethod(m_Pro.funMethods,resData);
                res = new ResData(200, true, "");
            }
            else
            {
                res = null;
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


    }
}

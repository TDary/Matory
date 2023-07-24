using Matory.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Matory
{
    public class Mato : MonoBehaviour
    {
        private SocketServer webs;
        private string receiveMsg;
        private bool msgBegin = false;
        private int port = 2666;
        private Thread th;
        private void Init()
        {
            for(int i = 0; i < 5; i++)
            {
                bool thisport = IsPortInUse(port + i);
                if (thisport)
                {
                    Debug.Log($"This port {thisport} is in use");
                    continue;
                }
                else
                {
                    webs = new SocketServer();
                    webs.start(port + i);    //监听端口号
                    webs.mydelegate = ParseProfiler;
                    Debug.Log($"Listen success for {thisport}");
                    break;
                }
            }


        }
        private void Update()
        {
            ProfilerData();
        }
        private void ParseProfiler(string msg)
        {
            receiveMsg = msg;
            msgBegin = true;
        }
        private void ProfilerData()
        {
            if (msgBegin)
            {
                //ParseDataFor receiveMsg
                msgBegin = false;
            }
            else
            {
                //waitMsg
            }
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

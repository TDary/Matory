using LitJson;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Matory.Tools
{
    /// <summary>
    /// 场景热力图点位生成工具
    /// </summary>
    public class GeneratePoints:MonoBehaviour
    {
        public Vector3 originalPosition = new Vector3(1000, 2000, 3000);
        public Vector3 fromPosition = new Vector3(0, 2000, 0);
        public Vector3 endPosition = new Vector3(20000, 1000, 20000);
        public bool distanceLimit = false,onlyBottom = true;
        public int distanceInterval = 50;
        public int rayRadius = 5;  //射线半径
        public int timesLimit = 200000;
        int times = 0;
        public List<Vector3> positionList;
        public bool isrunning
        {
            get;
            private set;
        }
        private string outputResPath = Application.streamingAssetsPath + "/Matory/ScenePoint.txt";
        WaitForFixedUpdate waitFixedUpdate = new WaitForFixedUpdate();
        WaitForEndOfFrame waitEndOfFrame = new WaitForEndOfFrame();
        /// <summary>
        /// 设置起始点位
        /// </summary>
        /// <param name="val"></param>
        public void SetOriginalPosition(Vector3 val)
        {
            originalPosition = val;
        }

        /// <summary>
        /// 设置结束点位
        /// </summary>
        /// <param name="val"></param>
        public void SetEndPosition(Vector3 val)
        {
            endPosition = val;
        }

        public void BeginGenerate(string filepath,string scenename)
        {
            if (!string.IsNullOrEmpty(filepath))
                outputResPath = filepath;

            if (fromPosition != null && endPosition != null)
            {
                var middle = endPosition;
                if (fromPosition.x > endPosition.x)
                {
                    endPosition.x = fromPosition.x;
                    fromPosition.x = middle.x;
                }
                if (fromPosition.y > endPosition.y)
                {
                    endPosition.y = fromPosition.y;
                    fromPosition.y = middle.y;
                }
                if (fromPosition.z > endPosition.z)
                {
                    endPosition.z = fromPosition.z;
                    fromPosition.z = middle.z;
                }
            }
            isrunning = true;
            positionList = new List<Vector3>();
            positionList.Add(originalPosition);
            File.WriteAllText(outputResPath, originalPosition.ToString());
            StartCoroutine(BeginGenerate(originalPosition));
        }

        /// <summary>
        /// 真正执行遍历场景点位并生成
        /// </summary>
        /// <param name="from"></param>
        /// <returns></returns>
        IEnumerator BeginGenerate(Vector3 from)
        {
            if (!isrunning)
                yield break;

            Action<Vector3> rayCast = (dir) =>
            {
                int distance = distanceInterval;
                if (dir == Vector3.up || dir == Vector3.down)
                {
                    distance = 100;
                }
                times++;
                Vector3 pass = from + dir * distance;
                if (!positionList.Contains(pass))
                {
                    Ray ray = new Ray(from, dir);
                    RaycastHit[] hitInfos = Physics.CapsuleCastAll(from, from + Vector3.up, rayRadius, pass - from, distance);
                    if(hitInfos.Length == 0 || hitInfos.All(p=>p.collider.isTrigger == true))
                    {
                        if (distanceLimit)
                        {
                            if (pass.x <= endPosition.x && pass.y <= endPosition.y && pass.z <= endPosition.z &&
                            pass.x >= fromPosition.x && pass.y >= fromPosition.y && pass.z >= fromPosition.z)
                            {
                                positionList.Add(pass);
                                StartCoroutine(BeginGenerate(pass));
                            }
                        }
                        else
                        {
                            positionList.Add(pass);
                            StartCoroutine(BeginGenerate(pass));
                        }
                    }
                }
            };
            rayCast.Invoke(Vector3.up);
            rayCast.Invoke(Vector3.down);
            rayCast.Invoke(Vector3.right);
            rayCast.Invoke(Vector3.left);
            rayCast.Invoke(Vector3.forward);
            rayCast.Invoke(Vector3.back);
            yield return waitFixedUpdate;
            yield return waitEndOfFrame;
            times = times - 6;
            if (times > timesLimit)
            {
                Debug.LogError($"警告：同时检测的射线超过{timesLimit}，请检查空间是否完全封闭");
                File.WriteAllLines(outputResPath, positionList.Select(p => p.ToString()));
                isrunning = false;
                yield break;
            }
            if (times == 0)
            {
                Debug.LogError("该密闭空间已采集完毕");
                File.WriteAllLines(outputResPath, positionList.Select(p => p.ToString()));
                isrunning = false;
                yield break;
            }
        }

        public void StopGnerate()
        {
            isrunning = false;
        }

        public void AppendRotation(string scenename)
        {
            Dictionary<string, List<Vector3>> vecDic = new Dictionary<string, List<Vector3>>();
            Dictionary<string, List<string>> transinfoDic = new Dictionary<string, List<string>>();
            string[] lines = File.ReadAllLines(outputResPath);
            foreach(var line in lines)
            {
                Vector3 vector = ParseVector3(line);
                string key = $"{vector.x},{vector.z}";
                if(!vecDic.ContainsKey(key))
                {
                    vecDic.Add(key, new List<Vector3>());
                }
                vecDic[key].Add(vector);
            }
            List<int> xlists = vecDic.Select(x => int.Parse(x.Key.Split(',')[0])).Distinct().OrderBy(p => p).ToList();
            Dictionary<int, int> orderRuleDic = new Dictionary<int, int>();
            int orderRule = 1;
            foreach(var x in xlists)
            {
                orderRuleDic.Add(x, orderRule);
                orderRule *= -1;
            }
            vecDic = vecDic.OrderBy(p => int.Parse(p.Key.Split(',')[0])).ThenBy(p => orderRuleDic[int.Parse(p.Key.Split(',')[0])] * int.Parse(p.Key.Split(',')[1])).ToDictionary(key => key.Key, key => key.Value);

            foreach(var keyv in vecDic)
            {
                List<Vector3> vecLists = keyv.Value.OrderBy(v => v.y).ToList();
                string key = vecLists.First().ToString();
                if (!transinfoDic.ContainsKey(key))
                {
                    transinfoDic.Add(key, new List<string>());
                }
                transinfoDic[key].Add(new Vector3(0, 0, 0).ToString());  //四个方向角
                transinfoDic[key].Add(new Vector3(0, 90, 0).ToString());
                transinfoDic[key].Add(new Vector3(0, 180, 0).ToString());
                transinfoDic[key].Add(new Vector3(0, 270, 0).ToString());

                if (!onlyBottom)
                {
                    if (vecLists.Count > 1)
                    {
                        for (int i = 1; i < vecLists.Count; i++)
                        {
                            if (i == 2) continue;
                            if (i > 3)
                                break;
                            key = vecLists[i].ToString();
                            if (!transinfoDic.ContainsKey(key))
                            {
                                transinfoDic.Add(key, new List<string>());
                            }
                            transinfoDic[key].Add(new Vector3(0, 0, 0).ToString());
                            transinfoDic[key].Add(new Vector3(0, 90, 0).ToString());
                            transinfoDic[key].Add(new Vector3(0, 180, 0).ToString());
                            transinfoDic[key].Add(new Vector3(0, 270, 0).ToString());
                            transinfoDic[key].Add(new Vector3(45, 0, 0).ToString());
                            transinfoDic[key].Add(new Vector3(45, 90, 0).ToString());
                            transinfoDic[key].Add(new Vector3(45, 180, 0).ToString());
                            transinfoDic[key].Add(new Vector3(45, 270, 0).ToString());
                        }
                    }
                }
            }
            string jsonData = JsonMapper.ToJson(transinfoDic);
            string jsonPath = Application.streamingAssetsPath + $"/Matory/{scenename}_Point.json";
            File.WriteAllText(jsonPath, jsonData);
        }

        /// <summary>
        /// 转换字符串的数据为Vector3对象
        /// </summary>
        /// <param name="infostr"></param>
        /// <returns></returns>
       Vector3 ParseVector3(string infostr)
        {
            infostr = infostr.Replace("(", "").Replace(")", "");
            var splitData = infostr.Split(',');
            if (splitData.Length > 2)
                return new Vector3(float.Parse(splitData[0]), float.Parse(splitData[1]), float.Parse(splitData[2]));
            else
                return Vector3.zero;
        }
    }
}

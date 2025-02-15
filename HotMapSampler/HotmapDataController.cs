using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Matory.HotMapSampler
{
    public class HotmapDataController
    {
        protected bool isRunning = false;
        protected PerformanceData perfData;
        protected string result_filePath;
        protected int sampleArg;
        StreamWriter sw;

        public void Init() 
        {
            perfData = new PerformanceData();
            perfData.OnInit();
        }

        /// <summary>
        /// 开始采集数据
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        public object SampleStart(string filepath,int sample_arg)
        {
            if (isRunning) return "The sample is beginning,please don't start again.";
            result_filePath = filepath;
            //写入表头
            sw = new StreamWriter(result_filePath);
            sw.WriteLine("RealTimeLogicFrame,FPS,GpuFrameTime,DrawCalls,SetPassCalls,Vertices,Triangles,TotalAllocatedMemory");
            perfData.InitRecordData();
            isRunning = true;
            switch (sample_arg)
            {
                case 0:
                    // 默认情况不进行每帧采集
                    sampleArg = sample_arg;
                    return "Sample begin run.";
                case 1:
                    // 开启每帧采集并写入
                    sampleArg = sample_arg;
                    return "Sample begin run.";
            }
            return "Args is error";
        }

        /// <summary>
        /// 获取单帧数据
        /// </summary>
        /// <returns></returns>
        public string GetOnePerformanceData()
        {
            return perfData.GetPerformaneceData();
        }

        /// <summary>
        /// 每帧写入
        /// </summary>
        /// <param name="logicframe"></param>
        void WriteData()
        {
            sw.WriteLine(GetOnePerformanceData());
        }

        /// <summary>
        /// 停止采集数据
        /// </summary>
        /// <returns></returns>
        public object SampleStop()
        {
            if (!isRunning) return "The sample is not running.";
            perfData.StopRecordData();
            sw.Close();
            sw = null;
            return "Sample stop successful.";
        }

        /// <summary>
        ///  放弃数据
        /// </summary>
        public void GiveUp()
        {
            if (result_filePath != null)
            {
                if (File.Exists(result_filePath))
                    File.Delete(result_filePath);
            }
        }

        /// <summary>
        /// 每帧更新数据
        /// </summary>
        public void OnUpdate()
        {
            if(perfData!=null) perfData.OnUpdate();
            if (sampleArg == 1 && isRunning) WriteData();
        }

    }
}

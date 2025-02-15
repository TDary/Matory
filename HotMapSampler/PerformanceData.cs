using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if UNITY_2022_1_OR_NEWER
using Unity.Profiling;
#endif
using UnityEngine;
using UnityEngine.Profiling;

namespace Matory.HotMapSampler
{
    public class PerformanceData
    {
#if UNITY_2022_1_OR_NEWER
        ProfilerRecorder drawCallsRecorder;
        ProfilerRecorder setPassCallsRecorder;
        ProfilerRecorder verticesRecorder;
        ProfilerRecorder trianglesRecorder;
        ProfilerRecorder totalAllocatedMemoryRecorder;
#endif
        protected FpsCounter fpsCounter;
        private FrameTiming[] timing;
        List<Sampler> _cacheSamplers;
        static StringBuilder sb = new StringBuilder();
        public float get_CurrentFps
        {
            get { return fpsCounter.CurrentFps; }
        }

        /// <summary>
        /// 初始化函数
        /// </summary>
        public void OnInit()
        {
            fpsCounter = new FpsCounter(1.0f);
            timing = new FrameTiming[1];
        }

        /// <summary>
        /// 每帧更新函数
        /// </summary>
        public void OnUpdate()
        {
            fpsCounter.Update(Time.deltaTime, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 获取数据
        /// </summary>
        /// <returns></returns>
        public string GetPerformaneceData()
        {
            sb.Clear();
            sb.AppendFormat("{0},", Time.frameCount);
            sb.AppendFormat("{0},", fpsCounter.CurrentFps);
            FrameTimingManager.GetLatestTimings(1, timing);
            sb.AppendFormat("{0},", timing[0].gpuFrameTime);
            //float HDrenderpipe = GetPassGpuTimeData("HDRenderPipeline::Render Main Camera");
            //sb.AppendFormat("{0},", HDrenderpipe);  //示例使用 如有需要可进行添加
#if UNITY_2022_1_OR_NEWER
            sb.AppendFormat("{0},", drawCallsRecorder.LastValue);
            sb.AppendFormat("{0},", setPassCallsRecorder.LastValue);
            sb.AppendFormat("{0},", verticesRecorder.LastValue);
            sb.AppendFormat("{0},", trianglesRecorder.LastValue);
            sb.AppendFormat("{0}", totalAllocatedMemoryRecorder.LastValue);
#endif
            return sb.ToString();
        }

        /// <summary>
        /// 初始化赋值并开始采集
        /// </summary>
        public void InitRecordData()
        {
#if UNITY_2022_1_OR_NEWER
            drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPassCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            verticesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            totalAllocatedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
#endif
            //if (_cacheSamplers.Count != 0)
            //{
            //    foreach (var sample in _cacheSamplers)
            //    {
            //        sample.GetRecorder().enabled = true;
            //    }
            //}
            //else
            //{
            //    var hdRenderCamera = CustomSampler.Get("HDRenderPipeline::Render Main Camera");
            //    if (hdRenderCamera != null && hdRenderCamera.GetRecorder().isValid)
            //    {
            //        hdRenderCamera.GetRecorder().enabled = true;
            //        _cacheSamplers.Add(hdRenderCamera);
            //    }
            //}
        }
        
        /// <summary>
        /// 获取采样器的GPU数据或者CPU数据
        /// </summary>
        /// <param name="passName"></param>
        /// <returns></returns>
        float GetPassGpuTimeData(string passName)
        {
            foreach (var item in _cacheSamplers)
            {
                if (item.name.Equals(passName))
                {
#if UNITY_2022_1_OR_NEWER
                    return item.GetRecorder().gpuElapsedNanoseconds / 1000000.0f;
#endif
                }
            }
            return 0;
        }

        /// <summary>
        /// 停止采集并释放资源对象
        /// </summary>
        public void StopRecordData()
        {
#if UNITY_2022_1_OR_NEWER
            drawCallsRecorder.Dispose();
            setPassCallsRecorder.Dispose();
            verticesRecorder.Dispose();
            trianglesRecorder.Dispose();
            totalAllocatedMemoryRecorder.Dispose();
#endif
            //foreach (var sample in _cacheSamplers)
            //{
            //    sample.GetRecorder().enabled = false;
            //}
        }
    }
 }

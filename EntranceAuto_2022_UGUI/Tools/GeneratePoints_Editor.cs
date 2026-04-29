// 将此脚本放到UnityEditor文件夹中
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Matory.Tools
{
    public class GeneratePoints_Editor:EditorWindow
    {
        static GeneratePoints_Editor m_window;
        static string generateResultFile = null;
        Vector3 from = new Vector3(2300, 610, 1350), end = new Vector3(2300, 610, 1250);
        GeneratePoints generate_obj = new GeneratePoints();
        [MenuItem("TestTools/场景采样点生成工具")]
        public static void ShowToolWindow()
        {
            if(m_window==null)
                m_window = EditorWindow.GetWindow<GeneratePoints_Editor>();
            m_window.titleContent = new UnityEngine.GUIContent("场景采样点生成");
            m_window.minSize = new UnityEngine.Vector2(500, 300);
            m_window.Show();
            if(m_window.generate_obj == null)
                m_window.generate_obj = new GeneratePoints();
        }

        void OnGUI()
        {
            generateResultFile = EditorGUILayout.TextField("生成结果文件路径",generateResultFile);
            string currentSceneName = EditorSceneManager.GetActiveScene().name;
            generate_obj.originalPosition = EditorGUILayout.Vector3Field("起点位置", generate_obj.originalPosition);
            generate_obj.distanceInterval = EditorGUILayout.IntField("间隔", generate_obj.distanceInterval);
            generate_obj.rayRadius = EditorGUILayout.IntField("射线半径", generate_obj.rayRadius);
            generate_obj.distanceLimit = EditorGUILayout.Toggle("开启距离限制", generate_obj.distanceLimit);
            if (generate_obj.distanceLimit)
            {
                generate_obj.fromPosition = EditorGUILayout.Vector3Field("起点", generate_obj.fromPosition);
                generate_obj.endPosition = EditorGUILayout.Vector3Field("终点", generate_obj.endPosition);
            }

            if (generate_obj.isrunning)
            {
                if (GUILayout.Button("停止生成", GUILayout.Width(150)))
                    generate_obj.StopGnerate();
            }
            else
            {
                if (GUILayout.Button("开始生成", GUILayout.Width(150)))
                {
                    generate_obj.BeginGenerate(generateResultFile, currentSceneName);
                }
            }
            GUILayout.BeginHorizontal();
            generate_obj.onlyBottom = EditorGUILayout.Toggle("只要最底层", generate_obj.onlyBottom);
            if (GUILayout.Button("添加旋转", GUILayout.Width(150)))
            {
                generate_obj.AppendRotation(currentSceneName);
            }
            from = EditorGUILayout.Vector3Field("开始", from);
            end = EditorGUILayout.Vector3Field("结束", end);
            if (GUILayout.Button("测试射线", GUILayout.Width(150)))
            {
                RaycastHit[] hitInfos = Physics.CapsuleCastAll(from, from + Vector3.up, 20, end - from, 100);
                if (hitInfos.Length == 0 || hitInfos.All(p => p.collider.isTrigger == true))
                {
                    Debug.Log("无碰撞");
                }
                else
                {
                    foreach (var v in hitInfos)
                    {
                        Debug.Log(v.collider.name);
                    }
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}
#endif

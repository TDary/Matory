# Matory - Unity 自动化测试 SDK

Matory 是一个用于 Unity 游戏的自动化测试 SDK，支持 UGUI 和 NGUI。它提供了远程调用、UI 自动化操作、性能数据采集、内存监控和截图等功能。

## 项目结构

```
EntranceAuto_2022_UGUI/
├── DataAO/                 # 数据访问对象
│   ├── MsgForSend.cs      # 发送消息数据结构
│   ├── ResData.cs         # 响应数据结构
│   ├── Session.cs         # 会话管理
│   └── TransData.cs       # 传输数据结构
├── HotMapSampler/          # 热力图采样器
│   ├── FpsCounter.cs      # FPS 计数器
│   ├── HotmapDataController.cs  # 热图数据控制器
│   └── PerformanceData.cs # 性能数据采集
├── LitJson/               # JSON 解析库
├── MatoryServer/          # 服务器核心
│   ├── MockUpPointerInputModule.cs  # 模拟触摸输入模块
│   ├── MsgProfiler.cs     # 消息分析器
│   └── ParseServer.cs     # 解析服务器
├── Net/                   # 网络模块
│   └── SocketServer.cs    # WebSocket/TCP 服务器
├── Tools/                 # 工具类
│   ├── EditorCoroutine.cs # 编辑器协程
│   ├── EditorCoroutineUtility.cs
│   ├── EditorWaitForSeconds.cs
│   ├── EditorWindowCoroutineExtension.cs
│   ├── GeneratePoints.cs  # 场景热力图点位生成
│   └── GeneratePoints_Editor.cs
├── EntranceAuto_2022_UGUI.cs  # 主入口文件
└── Matory_2022_UGUI.csproj    # 项目文件
```

## 技术栈

- **目标框架**: .NET Framework 4.8
- **Unity 版本**: Unity 2019.4.0f1 / Unity 2022
- **JSON 库**: LitJson 0.9.0
- **网络协议**: WebSocket / TCP Socket

## 核心功能

### 1. 远程命令调用

通过 WebSocket/TCP 连接，客户端可以远程调用 Unity 中的函数。支持的命令包括：

| 命令名 | 功能描述 |
|--------|----------|
| `GetSdkVersion` | 获取 SDK 版本 |
| `GetGameVersion` | 获取 Unity 引擎版本 |
| `StopConnection` | 停止连接 |
| `Find_Text` | 查找 UI 文本对象 |
| `Find_AllButton` | 获取所有按钮 |
| `Get_Hierarchy` | 获取场景层级结构 |
| `Get_Inspector` | 获取对象检查器数据 |
| `ClickOne` | 点击 UI 按钮 |
| `ClickOneBySimulate` | 模拟鼠标点击 |
| `PressOneBySimulate` | 模拟鼠标按下 |
| `UpOneBySimulate` | 模拟鼠标抬起 |
| `GetScreenShot` | 获取游戏截图 |
| `Object_Exist` | 检查对象是否存在 |
| `SetCamera` | 设置相机位置和旋转 |
| `SetGameObjectState` | 设置 GameObject 激活状态 |
| `CaptureMemorySnap` | 捕获内存快照 |
| `Set_DTrackerLimit` | 设置内存监控阈值 |

### 2. 性能数据采集

通过 `HotMapSampler` 模块采集游戏性能数据：

- FPS（帧率）
- GPU Frame Time
- Draw Calls
- SetPass Calls
- Vertices 数量
- Triangles 数量
- 内存占用

**使用示例**：
```
命令：PerformanceData_Start
参数：[输出文件路径，采集模式 (0/1)]
```

### 3. UI 操作录制

支持录制和回放 UI 操作流程：

- `Start_UIRecord` - 开始录制 UI 操作
- `Stop_UIRecord` - 停止录制 UI 操作

录制时会记录：
- 点击的 UI 元素路径
- 输入框文本内容
- 拖拽操作
- 操作时间间隔

### 4. 内存监控与快照

支持设置内存阈值，当超过阈值时自动捕获内存快照：

- 使用 `MemTrace.dll` 进行内存追踪
- 支持 Unity Profiler Memory 快照
- 可自定义内存限制（默认 2048MB）

**使用示例**：
```
命令：Set_DTrackerLimit
参数：[快照保存路径，内存限制 MB]

命令：Start_DTracker
```

### 5. Profiler 数据采集

支持 Unity Profiler 数据的远程采集：

- 可选择性开启 CPU、GPU、内存、物理、音频等模块
- 每 300 帧保存一次数据
- 支持远程开始/停止采集

**使用示例**：
```
命令：Set_ProfilerSampleModules
参数：{"CPU": true, "Rendering": false, "Memory": true}

命令：Gather_Profiler
参数：[IP, "1", 配置 JSON]  // 开始采集
```

### 6. 场景热力图点位生成

`GeneratePoints` 工具用于生成场景热力图的采样点位：

- 从起始点开始 BFS 遍历
- 使用射线检测判断可行走区域
- 支持距离限制和方向限制
- 输出点位坐标和旋转角度

## 通信协议

### 请求格式 (TransData)
```json
{
    "FuncName": "函数名",
    "FuncArgs": ["参数 1", "参数 2"]
}
```

### 响应格式 (ResData)
```json
{
    "Code": 200,
    "Msg": true,
    "Data": "返回数据"
}
```

## 使用方法

### 1. 集成到 Unity 项目

1. 将 `Matory` 文件夹复制到 Unity 项目的 `Assets` 目录
2. 在场景中创建 GameObject 并挂载 `Mato` 脚本
3. 调用 `Init()` 方法启动服务

### 2. 启动服务

```csharp
// 在 MonoBehaviour 中初始化
public class GameStart : MonoBehaviour
{
    void Start()
    {
        GameObject matoryObj = new GameObject("Matory");
        Mato mato = matoryObj.AddComponent<Mato>();
        mato.Init();  // 默认监听 2666 端口
    }
}
```

### 3. 客户端连接示例 (Python)

```python
import websocket
import json

ws = websocket.WebSocket()
ws.connect("ws://127.0.0.1:2666")

# 发送命令
cmd = {
    "FuncName": "GetGameVersion",
    "FuncArgs": []
}
ws.send(json.dumps(cmd))

# 接收响应
response = ws.recv()
print(response)
```

## 配置说明

### 端口配置
- 默认端口：2666
- 自动检测端口占用，使用下一个可用端口 (2666-2670)

### 内存监控配置
```csharp
// 设置内存限制为 4GB
mato.SetSnapAndMemoryLimit("/path/to/snapshot", 4096);
```

### Profiler 模块配置
| 模块名 | 说明 |
|--------|------|
| CPU | CPU 性能分析 |
| Rendering | 渲染性能分析 |
| Memory | 内存分析 |
| Physics | 物理系统分析 |
| Audio | 音频系统分析 |
| UI | UI 系统分析 |
| GI | 全局光照分析 |
| Network | 网络分析 |

## 注意事项

1. **Profiler 采集** 需要在 Development Build 模式下才能正常工作
2. **内存快照** 需要 `MemTrace.dll` 支持（仅 Windows）
3. **性能数据采集** 在 Unity 2022+ 版本功能更完整
4. **UI 录制** 依赖于 UGUI 的 `Button` 和 `InputField` 组件

## 版本历史

- **1.0.0** - 初始版本
  - 基础远程调用功能
  - UI 自动化操作
  - 性能数据采集
  - 内存监控
  - 截图功能

## 许可证

Copyright © P R C 2023

## 联系方式

如有问题或建议，请联系项目维护者。

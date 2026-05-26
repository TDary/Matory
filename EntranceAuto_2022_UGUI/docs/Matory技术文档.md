# Matory — Unity 游戏自动化测试框架技术文档

## 一、项目背景

### 1.1 痛点与需求

在 Unity 手机游戏的开发和测试流程中，传统的手工测试面临以下问题：

- **重复性劳动密集**：UI 功能回归测试需要人工反复点击、输入、验证，耗时且容易遗漏。
- **性能数据采集困难**：需要人工操作的同时采集 FPS、DrawCall、内存等数据，难以保证采集条件的一致性。
- **跨平台兼容验证**：同一套 UI 逻辑需在 Android/iOS 等不同平台上验证，人工覆盖成本高。
- **场景遍历不可控**：性能热力图需要在固定坐标、固定视角下采集数据，人工操作难以精确复现。

### 1.2 Matory 的定位

Matory 是一个**内嵌于 Unity 游戏运行时的自动化测试 SDK**，以 `MonoBehaviour` 组件形态挂载在游戏场景中。它对外提供 TCP/WebSocket 双协议接口，允许外部测试脚本（通常为 Python）远程控制游戏内的 UI 交互、采集运行时数据、获取场景层级结构、截取屏幕快照等。

### 1.3 核心设计目标

1. **非侵入式集成**：游戏方只需将 SDK 的 UnityPackage 导入工程，在启动脚本中调用 `Init()` 即可。
2. **跨平台运行**：同一套命令在 Windows Editor、Android、iOS 上均可工作。
3. **线程安全**：网络 I/O 在后台线程完成，命令执行在主线程派发，避免 Unity API 线程冲突。
4. **低性能影响**：限制每帧处理的消息数量，确保测试过程不影响游戏帧率。

---

## 二、整体架构

```
┌─────────────────────────────────────────────────────┐
│                    外部测试客户端                      │
│                  (Python / 任意语言)                   │
└────────────┬───────────────┬────────────────────────┘
             │ TCP (Raw)     │ WebSocket
             ▼               ▼
┌─────────────────────────────────────────────────────┐
│                  SocketServer                        │
│  - 端口 2666~2670 自动选择                            │
│  - 自动检测 TCP / WebSocket 协议                      │
│  - WebSocket RFC 6455 握手 & 帧解析                  │
│  - async socket (BeginAccept / BeginReceive)         │
│  - SessionPool 管理多客户端连接                        │
└─────────────────────┬───────────────────────────────┘
                      │ mydelegate 回调 (后台线程)
                      ▼
┌─────────────────────────────────────────────────────┐
│               Mato.ParseData()                       │
│  - JSON 反序列化 → TransData 对象                     │
│  - 入队到 ConcurrentQueue<TransData> (_getMsgPool)   │
└─────────────────────┬───────────────────────────────┘
                      │
                      ▼  (Unity 主线程, Update 每帧消费)
┌─────────────────────────────────────────────────────┐
│              Mato.Update()                           │
│  - 每帧从 _getMsgPool 取出最多 5 条消息                 │
│  - MsgProfiler.RunMethod() 查表分发命令               │
│  - 执行对应的业务方法，返回结果                         │
│  - 结果序列化为 JSON，通过 Session.SockeClient 回传     │
└─────────────────────────────────────────────────────┘
```

### 分层说明

| 层次 | 组件 | 职责 |
|------|------|------|
| 传输层 | `SocketServer` | TCP/WebSocket 双协议网络通信，客户端会话管理 |
| 协议层 | `TransData` / `ResData` | 请求与响应的 JSON 序列化/反序列化 |
| 调度层 | `MsgProfiler` + `Mato.Update()` | 命令注册与分发，线程安全的生产者-消费者模型 |
| 业务层 | 24 个命令处理函数 | 具体自动化操作：点击、查找、截图、性能采集等 |

---

## 三、核心实现原理

### 3.1 双协议网络层 (SocketServer)

#### 3.1.1 端口自动选择

初始化时从 2666 端口开始尝试绑定，最多递增 5 次（2666~2670）。在非 IL2CPP 平台会先通过 `IPGlobalProperties.GetIPGlobalProperties()` 检查端口占用；在 IL2CPP 平台（Android/iOS）上该 API 不可用，直接尝试绑定，依赖 Socket 异常来跳过被占用端口。

#### 3.1.2 TCP 与 WebSocket 自动识别

`SocketServer` 在收到客户端首条消息时，通过正则检测是否包含 `Sec-WebSocket-Key` 头部来判断协议类型：

- **WebSocket 连接**：执行完整的 RFC 6455 握手流程 —— 将 `Sec-WebSocket-Key` 与 Magic GUID `258EAFA5-E914-47DA-95CA-C5AB0DC85B11` 拼接，计算 SHA-1 哈希后 Base64 编码，构造 `Sec-WebSocket-Accept` 响应头。后续数据帧按 WebSocket 协议解析：读取 opcode、mask bit、payload length（支持 7/16/64 位扩展长度），对 masked payload 执行 4 字节 XOR 循环解码。
- **TCP 连接**：将收到的原始字节按 UTF-8 解码后直接作为消息文本。

#### 3.1.3 异步 Socket 模型

使用 .NET 的异步编程模型（APM）：

```
SockeServer.BeginAccept(Accept, SockeServer)
  └─ Accept() 回调:
       ├─ SockeClient.BeginReceive(Recieve, SockeClient)  // 开始监听该客户端数据
       ├─ SessionPool.Add(IP, session)                      // 记录会话
       └─ SockeServer.BeginAccept(Accept, SockeServer)     // 继续接受下一个客户端
```

每收到一个完整消息（TCP 以换行符 `\n` 分隔，WebSocket 通过帧头 payload length 界定），通过 `mydelegate` 委托回调到 `Mato.ParseData()`。注意：此回调运行在 Socket 的**后台线程**上，不能直接调用 Unity API。

### 3.2 线程安全的命令调度模型

这是整个框架最关键的设计。Unity 的大部分 API 只能在主线程调用，而网络数据到达在后台线程，因此需要一个线程安全的桥接机制。

#### 3.2.1 入向链路（请求处理）

```
后台线程                         主线程 (Update)
────────                        ────────────────
Socket 收到数据                   每帧轮询
  │                                │
  ▼                                ▼
ParseData(ip, msg)              _getMsgPool.TryDequeue()
  │                                │
  ├─ JsonMapper.ToObject           ├─ 查 _getTransDataPool
  │  → TransData                   │   获取源 IP
  ├─ 存入 _getTransDataPool        ├─ MsgProfiler.RunMethod()
  │  (key=FuncName+FuncArgs)       │   查表 → 执行业务方法
  └─ _getMsgPool.Enqueue()         └─ ResData → JSON → Send()
```

#### 3.2.2 出向链路（异步推送）

部分操作（如 UI 录制、截图完毕）需要**主动向客户端推送消息**，而非响应式回复：

```
业务方法（主线程）
  │
  ├─ 构造 MsgForSend { TargetIP, Msg }
  └─ _sendMsgPool.Enqueue()

Update() 末尾:
  └─ _sendMsgPool.TryDequeue() → 查找 Session → Socket.Send()
```

#### 3.2.3 流量控制

每帧最多处理 **5 条请求**（`MaxMessagesPerFrame = 5`），防止大量命令积压时导致帧率骤降。

### 3.3 命令注册与分发 (MsgProfiler)

`MsgProfiler` 维护一个 `Dictionary<string, FunMethod>`，其中 `FunMethod` 的签名为：

```csharp
delegate object FunMethod(string remoteIp, string[] param);
```

在 `Mato.Init()` 中统一注册全部 24 个命令。添加新命令只需两步：
1. 实现 `object MethodName(string remoteIp, string[] param)` 方法
2. 在 `Init()` 中加一行 `mPro.funMethods.Add("CommandName", MethodName);`

### 3.4 通信协议

#### 请求格式

```json
{
  "FuncName": "ClickOneBySimulate",
  "FuncArgs": ["left", "12345"]
}
```

#### 响应格式

```json
{
  "Code": 200,
  "Msg": true,
  "Data": "click it success"
}
```

- `Code`：HTTP 风格状态码，200 表示成功
- `Msg`：操作的布尔结果
- `Data`：返回数据的 JSON 字符串（可以是简单字符串、JSON 数组或 JSON 对象）

### 3.5 24 个命令功能概览

| 分类 | 命令 | 功能 |
|------|------|------|
| **基础信息** | `GetSdkVersion` | 返回 SDK 版本号 |
| | `GetGameVersion` | 返回 Unity 引擎版本 |
| | `StopConnection` | 关闭 Socket 服务器 |
| **UI 查找** | `Find_Text` | 在所有 Canvas 下的 Text/InputField 组件中搜索匹配子串 |
| | `Find_AllButton` | 返回所有 Button 组件的 InstanceID、名称和路径 |
| | `Get_Hierarchy` | 递归遍历所有场景，返回完整 GameObject 层级树 JSON |
| | `Get_Inspector` | 按 InstanceID 查找 GameObject，返回所有 Component 及其公开字段/属性 |
| | `Object_Exist` | 按路径或 InstanceID 判断 GameObject 是否存在 |
| **UI 操作** | `ClickOne` | 直接触发 Button.onClick 事件 |
| | `ClickOneBySimulate` | 通过 EventSystem 完整模拟鼠标点击事件链（pointerEnter → pointerDown → pointerClick → pointerUp → pointerExit） |
| | `PressOneBySimulate` | 仅模拟按下阶段（支持拖拽操作的起手） |
| | `UpOneBySimulate` | 仅模拟抬起阶段（支持拖拽操作的收尾） |
| | `SetGameObjectState` | 按路径激活/禁用 GameObject |
| **输入录制** | `Start_UIRecord` | 开始录制并实时推送用户 UI 交互事件到客户端 |
| | `Stop_UIRecord` | 停止 UI 录制 |
| **截图** | `GetScreenShot` | 截取当前屏幕画面，保存为 PNG 文件 |
| **性能采集** | `Set_ProfilerSampleModules` | 配置 Unity Profiler 采集模块开关（CPU/Rendering/Memory 等 9 项） |
| | `Gather_Profiler` | 启动/停止 Profiler 数据采集，每 300 帧输出一个二进制文件 |
| | `Check_Profiler` | 查询已完成的 Profiler 数据文件路径 |
| | `PerformanceData_Start` | 启动 Hotmap 性能数据采集（CSV 格式：FPS、GPU帧时间、DrawCall、SetPassCall、顶点数、三角形数、内存） |
| | `PerformanceData_Stop` | 停止并落盘性能采集数据 |
| | `PerformanceData_GetOne` | 获取单帧性能数据快照 |
| **内存** | `CaptureMemorySnap` | 捕获 Unity Memory Profiler 快照 (.snap 文件) |
| **其他** | `SetCamera` | 设置主摄像机位置和旋转 |

### 3.6 UI 交互模拟机制

#### 方式一：直接事件触发

```csharp
// 直接调用 Button 组件的 onClick 事件
targetButton.onClick?.Invoke();
```

优点：简单直接。缺点：不经过 Unity EventSystem，无法验证 Raycaster 遮挡、无法触发 `IPointerClickHandler` 等接口回调。

#### 方式二：EventSystem 完整模拟

通过 `ExecuteEvents.ExecuteHierarchy()` 逐级执行事件链，模拟真实的鼠标点击流程：

```
pointerEnter → OnMouseEnter
  → pointerDown → OnMouseDown → initializePotentialDrag
    → OnMouseOver
  → pointerUp → OnMouseUp → pointerClick → OnMouseUpAsButton
  → pointerExit → OnMouseExit
```

`PressOneBySimulate` 和 `UpOneBySimulate` 将此流程拆分为两阶段，允许外部测试脚本插入拖拽操作：

```
PressOneBySimulate(GameObject A)
  → pointerEnter(A) + pointerDown(A)
  → 客户端可在此期间移动鼠标到 B
UpOneBySimulate(GameObject B)
  → pointerUp(B) + pointerClick(B) + pointerExit(B)
```

#### 方式三：Touch 模拟 (MockUpPointerInputModule)

`MockUpPointerInputModule` 继承 `StandaloneInputModule`，将 `UnityEngine.Touch` 结构体转换为 `PointerEventData`，支持完整的触摸事件阶段（Began → Moved → Ended → Canceled），用于移动端的自动化测试。

### 3.7 场景层级遍历

`GetHierarchy` 的实现逻辑：

1. 遍历 `SceneManager.sceneCount` 获取所有加载的场景
2. 对每个场景，通过 `scene.GetRootGameObjects()` 获取根节点
3. 递归遍历每个 GameObject 的 `Transform` 子节点
4. 使用 `GetGameObjectPath()` 构建以 `/` 分隔的路径字符串（从根节点向上追溯到场景根）
5. DontDestroyOnLoad 场景通过临时创建一个 GameObject 来获取其所属场景，再删除临时对象
6. 输出为递归嵌套的 JSON 树结构

### 3.8 性能数据采集

#### Hotmap 采样 (HotMapSampler)

使用 Unity 2022.1+ 的 `ProfilerRecorder` API 进行低开销的逐帧性能监控：

| 指标 | 数据来源 |
|------|----------|
| FPS | `FpsCounter` 平滑计算（默认 1 秒平滑窗口） |
| GPU 帧时间 | `FrameTimingManager.GetLatestTimings()` |
| Draw Calls | `ProfilerRecorder` (Render category) |
| SetPass Calls | `ProfilerRecorder` (Render category) |
| 顶点数 | `ProfilerRecorder` (Render category) |
| 三角形数 | `ProfilerRecorder` (Render category) |
| 总分配内存 | `ProfilerRecorder` (Memory category) |

输出格式为 CSV：`RealTimeLogicFrame, FPS, GpuFrameTime, DrawCalls, SetPassCalls, Vertices, Triangles, TotalAllocatedMemory`

#### Unity Profiler 采集

需要 Development Build。每次采集 300 帧的二进制 Profiler 数据，最多 1GB 存储空间。可通过 `Set_ProfilerSampleModules` 配置要启用的 Profiler 区域。

### 3.9 UI 操作录制

`Start_UIRecord` 启动后，每帧检测输入状态，发现点击操作时向客户端推送被点击 UI 元素的路径：

1. 同时监听 `Input.touchCount`（移动端）和 `Input.GetMouseButtonDown`（桌面端）
2. 通过 `EventSystem.current.RaycastAll()` 获取当前指针下的所有 `Graphic` 组件
3. 通过缓存机制（`_mRecordGraphicCache`、`_mRecordPathCache`）避免每帧计算 Canvas 列表和路径
4. 将点击的 UI 元素路径通过 `_sendMsgPool` 异步推送给客户端

### 3.10 性能优化策略

- **消息限流**：每帧最多处理 5 条入向消息
- **路径缓存**：Canvas 列表和 GameObject 路径使用 1 秒 TTL 的内存缓存
- **StringBuilder 复用**：JSON 序列化使用成员变量 `_dataJson` 而非每次新建
- **并发容器**：`ConcurrentQueue<T>` 保证入向/出向消息池的线程安全无锁操作

---

## 四、平台兼容性

| 平台 | 端口检测 | 输入模拟 | 性能采集 | 内存快照 |
|------|----------|----------|----------|----------|
| Windows Editor | `IPGlobalProperties` | Mouse + Touch | 完整支持 | 完整支持 |
| Android (IL2CPP) | 回退到直接绑定 | Touch (MockUpPointerInputModule) | 需要 Dev Build | `UNITY_2022_3_OR_NEWER` |
| iOS (IL2CPP) | 回退到直接绑定 | Touch (MockUpPointerInputModule) | 需要 Dev Build | `UNITY_2022_3_OR_NEWER` |

- **IL2CPP 特殊处理**：`IPGlobalProperties.GetIPGlobalProperties()` 在 IL2CPP 平台不可用，代码通过 `catch (NotImplementedException)` 回退到直接尝试 Socket 绑定的方式。
- **Unity 版本条件编译**：使用 `UNITY_2022_3_OR_NEWER`、`UNITY_2022_1_OR_NEWER` 等预处理指令适配不同 Unity 版本的 API 差异。

---

## 五、编辑器工具

### 场景采样点生成器 (GeneratePoints)

用于自动化性能热力图测试的场景准备工具：

1. 使用 **Flood Fill 算法**配合 `Physics.CapsuleCastAll` 检测场景中可行走区域
2. 自动在每个采样点生成 4 个旋转角度（0°/90°/180°/270°），可选垂直角度变化
3. 结果保存为 JSON 至 `StreamingAssets/Matory/`
4. 附带 EditorWindow UI（`GeneratePoints_Editor`）用于可视化配置参数

### Editor 协程引擎

自定义实现的 Editor 协程系统（`EditorCoroutine`），用于在不阻塞 Editor UI 的情况下执行耗时的场景遍历操作。

---

## 六、集成与使用方式

### 游戏侧集成

1. 将 `UAutoSDK_UGUI.unitypackage` 导入 Unity 工程
2. 在启动脚本中添加：
   ```csharp
   var mato = gameObject.AddComponent<Mato>();
   mato.Init();
   ```

### 测试侧使用

测试客户端通过 TCP 或 WebSocket 连接到 `<设备IP>:2666`，发送 JSON 命令即可：

```python
import socket, json

sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect(("192.168.1.100", 2666))

# 获取场景层级
sock.send(json.dumps({"FuncName": "Get_Hierarchy", "FuncArgs": []}).encode())
response = json.loads(sock.recv(4096))

# 模拟点击
sock.send(json.dumps({
    "FuncName": "ClickOneBySimulate",
    "FuncArgs": ["left", "/Canvas/Panel/Button"]
}).encode())
```

---

## 七、项目结构一览

```
EntranceAuto_2022_UGUI/
├── EntranceAuto_2022_UGUI.cs       # 主入口类 Mato（~2150 行，MonoBehaviour）
├── Net/
│   └── SocketServer.cs             # TCP/WebSocket 双协议异步服务器
├── DataAO/
│   ├── TransData.cs                # 入向请求模型 (FuncName + FuncArgs)
│   ├── ResData.cs                  # 出向响应模型 (Code + Msg + Data)
│   ├── Session.cs                  # 客户端会话 (Socket + Buffer + IP)
│   └── MsgForSend.cs              # 异步推送消息模型
├── MatoryServer/
│   ├── MsgProfiler.cs              # 命令注册与分发表
│   ├── MockUpPointerInputModule.cs # Touch 事件模拟
│   └── ParseServer.cs              # 预留桩（未实现）
├── HotMapSampler/
│   ├── PerformanceData.cs          # ProfilerRecorder 性能指标采集
│   ├── HotmapDataController.cs     # 采集生命周期管理 + CSV 写入
│   └── FpsCounter.cs               # 平滑 FPS 计数器
├── Tools/
│   ├── GeneratePoints.cs           # Flood Fill 场景采样点生成
│   ├── GeneratePoints_Editor.cs    # EditorWindow 配置界面
│   ├── EditorCoroutine.cs          # Editor 协程引擎
│   ├── EditorCoroutineUtility.cs
│   ├── EditorWaitForSeconds.cs
│   └── EditorWindowCoroutineExtension.cs
├── LitJson/
│   └── LitJsonHelper.cs            # JSON 序列化辅助类
├── Properties/
│   └── AssemblyInfo.cs
├── Matory_2022_UGUI.csproj         # MSBuild 项目文件 (.NET Framework 4.8)
└── Matory_2022_UGUI.sln            # 解决方案文件
```

---

## 八、总结

Matory 是一个轻量但功能完备的 Unity 游戏自动化测试框架。其核心设计亮点在于：

1. **线程安全的异步消息模型**：通过 `ConcurrentQueue` + 主线程轮询消费，解决了网络 I/O 线程与 Unity 主线程之间的安全通信问题。
2. **双协议自适应网络层**：自动识别 TCP/WebSocket，使得测试客户端可以使用任意主流网络库。
3. **多维度的 UI 交互模拟**：从最简单的 `onClick.Invoke()` 到完整的 EventSystem 事件链，再到 Touch 模拟，覆盖不同测试精度需求。
4. **运行时性能采集**：利用 Unity 2022.1+ 的 `ProfilerRecorder` API 实现低开销的逐帧性能监控，不影响游戏本体表现。
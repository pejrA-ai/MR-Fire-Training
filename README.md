# MR Fire Training — 初始 Demo

基于 Unity 与 Meta Quest，实现虚拟火焰叠加现实环境、瞄准喷射及灭火反馈的初始 MR 效果。

## 功能

- MR 透视与地面火点摆放。
- 模拟按压喷射、火焰根部命中判定与火势衰减。
- 状态面板：瞄准提示、输入强度、剩余火势、计时与重置。
- 键盘预览及 Meta XR Simulator 调试。
- 预留 ESP32 压力输入接口；串口/BLE 通信与插销检测待接入。

## 打开与协作

使用 **Unity 6000.3.23f1**，在 Unity Hub 添加仓库根目录，等待依赖导入后打开 `Assets/FireDrill/FireDrill_MR.unity`。Meta SDK 205.0.0 通过官方包服务器下载，首次导入需要联网。

普通桌面预览：方向键瞄准，空格喷射，R 重置。Quest/XR：右手柄扳机喷射，A 键放置火点；XR Simulator 需另外安装和启用 Meta XR Simulator，并非仅按 Play 即自动启用。

硬件接入点：`Assets/FireDrill/FireDrill.cs` 中的 `SetPressureSource(PressureSource.External)` 与 `SubmitPressure(float)`（归一化到 0–1）。外部输入超时会停止喷射；传感器原始值方向、阈值与插销 0/1 含义需校准。

队友从自己的分支修改后提交 PR；请保留并提交资源 `.meta` 文件，勿提交 Library、缓存和 APK。Fire 素材仅随项目协作使用，请遵守原资源许可。

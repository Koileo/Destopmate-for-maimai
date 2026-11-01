# 第三方格式与运行时

本地 CBv3 转换工具参考了 [Xyzstk/DesktopMateAssetDecryptor](https://github.com/Xyzstk/DesktopMateAssetDecryptor) 公开描述的容器和网格还原方式。参考仓库未标明许可证；本仓库不包含其 C# 源码或 `keys.bin`。运行转换工具所需数据由使用者自行从该来源取得。

转换过程使用 UnityPy、NumPy、cryptography，依赖列表在 `tools/requirements.txt`；这些库保留各自的许可证。

Mod 编译时引用 Unity、MelonLoader 和 Harmony。游戏 DLL、Desktop Mate `.cb`、角色模型与原贴图不随本项目或发布包分发。文档截图来自本机运行，用于展示适配效果。

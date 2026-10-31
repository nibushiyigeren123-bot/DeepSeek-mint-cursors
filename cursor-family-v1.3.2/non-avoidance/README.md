# v1.3.2 非避让可编辑运行源码

本源码包直接提取自`DeepSeek-mint-cursors-non-avoidance-v1.3.2.zip`，运行源码、控制脚本和配置与该完整包逐字节一致。

本包包含C#、控制脚本、重建入口、配置及全部profile manifest；全部PNG/ANI、EXE和用户CMD在配套完整ZIP中。它不是可单独启动的安装包。

修改前先完整解压对应安装ZIP。将本源码中的源文件放回相同相对路径，完成修改后从完整包目录运行`rebuild.ps1`；它调用Windows自带C#编译器并更新payload-hashes，不安装或启用光标。修改配置和媒体还需另行核验帧数、热点、尺寸及预算。

本源码包中的payload-hashes.json属于完整安装包，不是此精简源码包的文件清单。源码包自己的成员来源见source-manifest.json。原仓库美术生成代码和历史源码保留，程序及美术许可继续适用。

完整包SHA256：`ee44bd650aed2f2fd05770e53648e2b2018348d182af9c824988451ff01f8abd`。

# 半透明边缘的捕获方式复核

旧ScopedAudit.Current经System.Drawing.Graphics.GetHdc绘制到GDI+位图，实测半透明边缘混入错误背景，出现假黑点。旧图保留verification/private-alpha/legacy-gethdc-capture.png以及原VM全尺寸结果。普通DIB CUR、PNG CUR、PNG ANI都可重现，不能由此把问题归咎于发布资产或ANI编码。

新方法用CreateCompatibleDC/CreateDIBSection建立明确填充背景的24bpp原生DIB，再调用DrawIconEx，直接读取RGB。96px Gemini原文件与源PNG正确alpha合成最大误差1；额外手动预乘反而使其变暗，最大误差41，因此没有改动发布文件。随后对16角色、原版与Plus全部档位的208个私有句柄逐个渲染核对，全部最大误差不超过1色阶。宿主只创建/销毁私有句柄，未替换系统指针。见native-alpha-all-sizes-validation.json。

真实VM另补32次原版/Plus96实际系统当前指针捕获，见vm-results/native-dib-final16-attempt2。旧全尺寸的GetIconInfo尺寸、当前可见指针与默认方案前后相同的核查仍有效；旧捕获的半透明边缘不作为正确外观证据。

Win32绘制接口说明：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawiconex 。上述差异归因依据本机可复现对照实验，并非声称文档确认了GDI+具体内部实现。

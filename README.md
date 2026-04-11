# MoeCatnip

这是一个用于 Moebooru 的 UWP 客户端。

支持 Yande.re 和 Konachan。

~~本应用也曾包含 ExHentai 客户端。~~

原项目地址：[AmazingDM/PRPR](https://github.com/AmazingDM/PRPR)

微软商店原链接：
~~https://www.microsoft.com/store/p/prpr/9nblggh4stnm~~

该应用目前已被微软商店下架。

离线包发布：
[sw1313/PRPR](https://github.com/sw1313/PRPR/releases/tag/2.7.4.0)

## 项目结构

- `PRPR/PRPR.sln`：解决方案
- `PRPR/PRPR/`：UWP 主项目
- `PRPR/Imaging/`：原生图像处理项目

## 这个分支的修复内容

- 为网络请求补充 `User-Agent` 等必要请求头，改善接口访问兼容性
- 修复浏览页偶发空白、只加载少量图片、增量加载不稳定等问题
- 优化浏览页的缩略图加载、预加载与拼接布局，减少白屏、闪动和回到顶部的问题
- 修复图片详情页的部分稳定性问题，补全收藏、来源链接等常用交互
- 修复未登录状态下收藏不可用的问题，改为使用本地收藏
- 改善壁纸、锁屏、手动刷新等相关功能的可用性与稳定性
- 优化筛选、搜索与返回浏览页时的状态保持体验
- 补充本地保存、路径配置、收藏数据等实用能力
- 调整项目结构、构建路径与仓库说明，方便在当前仓库布局下继续维护

## 说明

- 本仓库只保留源码与必要项目文件
- 证书、商店关联等不适合提交到 GitHub 的文件不包含在仓库中

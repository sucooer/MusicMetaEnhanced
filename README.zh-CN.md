# Music Meta Enhanced

![GitHub release](https://img.shields.io/github/v/release/anyaer/applemusic-emby)
![GitHub](https://img.shields.io/github/license/anyaer/applemusic-emby)
![Build](https://img.shields.io/github/actions/workflow/status/anyaer/applemusic-emby/build.yml)

为 **Emby** 媒体服务器提供高质量音乐元数据的插件，支持 **Apple Music（多店面）** 与 **网易云音乐**，并提供别名桥接与仪表盘工具。

---

## ✨ 功能特性

| 功能 | 说明 |
|------|------|
| **Apple Music 供应商** | 通过网页抓取或 JSON API（实验性）获取专辑/艺人/曲目元数据与图片 |
| **多店面支持** | 搜索店面（`Storefront`）与专辑数据店面（`AlbumStorefront`）可分离 — 如用 `cn` 搜索但用 `jp` 获取日文原文拼写 |
| **网易云音乐** | 通过 [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) 补全中文艺人传记与图片兜底 |
| **别名桥接** | 关联 Apple Music ID ↔ MusicBrainz ID ↔ 网易云 ID，提升匹配准确率 |
| **仪表盘 UI** | 服务器内配置页面 + 诊断工具 |
| **图片供应商** | 为专辑、艺人、曲目提供高分辨率封面 |

---

## 🚀 安装

### 从 Release 安装（推荐）
1. 从 [Releases](https://github.com/anyaer/applemusic-emby/releases) 下载最新 `MusicMetaEnhanced.zip`
2. Emby 中：**仪表盘 → 插件 → 从文件安装** → 选择压缩包
3. 重启 Emby

### 从源码构建
```bash
git clone https://github.com/anyaer/applemusic-emby.git
cd applemusic-emby/src/Emby.Plugin.MusicMetaEnhanced
dotnet publish -c Release -o ./publish
# 将 publish 文件夹打包为 zip 后在 Emby 中安装
```

---

## ⚙️ 配置

安装后打开 **仪表盘 → Music Meta Enhanced**。

| 设置项 | 默认值 | 说明 |
|--------|--------|------|
| **Storefront（搜索店面）** | `cn` | Apple Music 搜索使用的国家代码（ISO 3166-1 alpha-2） |
| **Album Storefront（专辑店面）** | *空* | 专辑元数据专用店面；留空则跟随主店面 |
| **Use JSON API（使用 JSON API）** | `false` | 启用实验性 JSON API 替代网页抓取 |
| **Netease API Base URL** | `http://127.0.0.1:3000` | 自托管 NeteaseCloudMusicApi 地址 |
| **Prefer Netease Bio（优先网易传记）** | `true` | 两者都有时优先使用网易云传记 |

### 常用店面代码
| 地区 | 代码 |
|------|------|
| 中国大陆 | `cn` |
| 日本 | `jp` |
| 美国 | `us` |
| 英国 | `gb` |
| 香港 | `hk` |
| 台湾 | `tw` |

---

## 🔧 工作原理

### 元数据流程
```
Emby 库扫描
      │
      ▼
┌──────────────────┐
│  专辑元数据供应商 │──▶ Apple Music (iTunes API) ──▶ 专辑元数据 + 图片
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  艺人元数据供应商 │──▶ Apple Music / 网易云 ──▶ 传记 + 图片
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  曲目元数据供应商 │──▶ Apple Music ──▶ 曲目元数据
└──────────────────┘
```

### 存储的外部 ID
- `AppleMusicAlbum` / `AppleMusicArtist` / `AppleMusicSong` — Apple Music 原生 ID
- `MusicBrainzAlbum` / `MusicBrainzArtist` / `MusicBrainzTrack` — 用于别名桥接
- `NeteaseAlbum` / `NeteaseArtist` / `NeteaseSong` — 网易云音乐 ID

---

## 📦 项目结构

```
src/Emby.Plugin.MusicMetaEnhanced/
├── Plugin.cs                      # 入口点，IHasWebPages，IHasThumbImage
├── Configuration/
│   ├── PluginConfiguration.cs     # 设置模型
│   └── configPage.html            # 仪表盘 UI
├── Providers/
│   ├── AlbumMetadataProvider.cs
│   ├── ArtistMetadataProvider.cs
│   ├── SongMetadataProvider.cs
│   ├── AlbumImageProvider.cs
│   ├── ArtistImageProvider.cs
│   └── NeteaseArtistImageProvider.cs
├── MetadataSources/
│   ├── IMetadataSource.cs
│   ├── MetadataSourceFactory.cs
│   ├── Json/                      # JSON API 实现
│   │   ├── JsonMetadataSource.cs
│   │   ├── ApiClient/             # HTTP、Token、模型
│   │   └── Models/                # DTO
│   ├── Web/                       # 网页抓取
│   │   ├── WebMetadataSource.cs
│   │   └── AppleMusicPageParser.cs
│   ├── Itunes/                    # iTunes Search API（专辑）
│   │   └── ItunesAlbumSource.cs
│   └── Netease/                   # 网易云音乐
│       └── NeteaseMusicSource.cs
├── ExternalIds/                   # 外部 ID 定义
├── Dtos/                          # 数据传输对象
└── Utils/                         # 工具类（TitleMatcher、JapaneseKana 等）
```

---

## 🛠️ 开发

### 依赖
- .NET 8 SDK
- Emby Server 4.8+（用于测试）

### 构建
```bash
cd src/Emby.Plugin.MusicMetaEnhanced
dotnet build -c Release
```

### 运行测试
```bash
dotnet test
```

---

## 🤝 贡献

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feat/amazing-feature`)
3. 提交变更 (`git commit -m 'feat: add amazing feature'`)
4. 推送分支 (`git push origin feat/amazing-feature`)
5. 发起 Pull Request

---

## 📄 许可证

MIT License — 详见 [LICENSE](LICENSE)。

---

## 🙏 致谢

- [Emby](https://emby.media/) — 媒体服务器平台
- [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) — 网易云 API
- [MusicBrainz](https://musicbrainz.org/) — 开放音乐百科全书

---

[English](README.md) | 简体中文

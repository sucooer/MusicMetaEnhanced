# Music Meta Enhanced

![GitHub release](https://img.shields.io/github/v/release/anyaer/applemusic-emby)
![GitHub](https://img.shields.io/github/license/anyaer/applemusic-emby)
![Build](https://img.shields.io/github/actions/workflow/status/anyaer/applemusic-emby/build.yml)

An **Emby plugin** that enriches your music library with high-quality metadata from **Apple Music** (multi-storefront) and **Netease Cloud Music**, plus alias bridging and dashboard tools.

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **Apple Music Providers** | Album/artist/song metadata & images via web scraping **or** JSON API (experimental) |
| **Multi-Storefront** | Separate storefront for search (`Storefront`) and album data (`AlbumStorefront`) — e.g., use `cn` for search but `jp` for original Japanese spellings |
| **Netease Cloud Music** | Chinese artist biographies & image fallback via [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) |
| **Alias Bridging** | Links Apple Music IDs ↔ MusicBrainz IDs ↔ Netease IDs for better matching |
| **Dashboard UI** | In-server configuration page + diagnostic tools |
| **Image Providers** | High-res artwork for albums, artists, and songs |

---

## 🚀 Installation

### From Release (Recommended)
1. Download the latest `MusicMetaEnhanced.zip` from [Releases](https://github.com/anyaer/applemusic-emby/releases)
2. In Emby: **Dashboard → Plugins → Install from file** → select the zip
3. Restart Emby

### From Source
```bash
git clone https://github.com/anyaer/applemusic-emby.git
cd applemusic-emby/src/Emby.Plugin.MusicMetaEnhanced
dotnet publish -c Release -o ./publish
# Zip the publish folder and install in Emby
```

---

## ⚙️ Configuration

After installation, open **Dashboard → Music Meta Enhanced**.

| Setting | Default | Description |
|---------|---------|-------------|
| **Storefront** | `cn` | Apple Music country code for search (ISO 3166-1 alpha-2) |
| **Album Storefront** | *(empty)* | Override for album metadata; empty = follows main storefront |
| **Use JSON API** | `false` | Enable experimental JSON API instead of web scraping |
| **Netease API Base URL** | `http://127.0.0.1:3000` | Your NeteaseCloudMusicApi instance |
| **Prefer Netease Bio** | `true` | Use Netease biography over Apple Music when both exist |

### Common Storefront Codes
| Region | Code |
|--------|------|
| China | `cn` |
| Japan | `jp` |
| United States | `us` |
| United Kingdom | `gb` |
| Hong Kong | `hk` |
| Taiwan | `tw` |

---

## 🔧 How It Works

### Metadata Flow
```
Emby Library Scan
       │
       ▼
┌──────────────────┐
│  Album Provider  │──▶ Apple Music (iTunes API) ──▶ Album metadata + images
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Artist Provider │──▶ Apple Music / Netease ──▶ Bio + images
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Song Provider   │──▶ Apple Music ──▶ Track metadata
└──────────────────┘
```

### External IDs Stored
- `AppleMusicAlbum` / `AppleMusicArtist` / `AppleMusicSong` — native Apple Music IDs
- `MusicBrainzAlbum` / `MusicBrainzArtist` / `MusicBrainzTrack` — for alias bridging
- `NeteaseAlbum` / `NeteaseArtist` / `NeteaseSong` — Netease Cloud Music IDs

---

## 📦 Project Structure

```
src/Emby.Plugin.MusicMetaEnhanced/
├── Plugin.cs                      # Entry point, IHasWebPages, IHasThumbImage
├── Configuration/
│   ├── PluginConfiguration.cs     # Settings model
│   └── configPage.html            # Dashboard UI
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
│   ├── Json/                      # JSON API implementation
│   │   ├── JsonMetadataSource.cs
│   │   ├── ApiClient/             # HTTP, tokens, models
│   │   └── Models/                # DTOs
│   ├── Web/                       # Web scraping
│   │   ├── WebMetadataSource.cs
│   │   └── AppleMusicPageParser.cs
│   ├── Itunes/                    # iTunes Search API (albums)
│   │   └── ItunesAlbumSource.cs
│   └── Netease/                   # Netease Cloud Music
│       └── NeteaseMusicSource.cs
├── ExternalIds/                   # External ID definitions
├── Dtos/                          # Data transfer objects
└── Utils/                         # Helpers (TitleMatcher, JapaneseKana, etc.)
```

---

## 🛠️ Development

### Prerequisites
- .NET 8 SDK
- Emby Server 4.8+ (for testing)

### Build
```bash
cd src/Emby.Plugin.MusicMetaEnhanced
dotnet build -c Release
```

### Run Tests
```bash
dotnet test
```

---

## 🤝 Contributing

1. Fork the repo
2. Create a feature branch (`git checkout -b feat/amazing-feature`)
3. Commit changes (`git commit -m 'feat: add amazing feature'`)
4. Push to branch (`git push origin feat/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

MIT License — see [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgements

- [Emby](https://emby.media/) — Media server platform
- [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) — Netease API
- [MusicBrainz](https://musicbrainz.org/) — Open music encyclopedia

---

English | [简体中文](README.zh-CN.md)

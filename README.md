# Usual Audio Converter (Avalonia Cross-Platform Edition)

A modern, fast, cross-platform batch audio converter powered by **.NET 8** and **Avalonia UI**.  
It leverages **FFmpeg**, **fdkaac**, and **exhale** using diskless .NET asynchronous stream pipelines, delivering high-performance batch conversions across Windows, Linux, and macOS.

**.NET 8** と **Avalonia UI** で構築された、高速でモダンなクロスプラットフォーム対応バッチ音声変換ツールです。  
**FFmpeg**、**fdkaac**、**exhale** を .NET ネイティブの非同期ストリームパイプラインで直結し、中間ファイルを作成しないディスクレス高速変換を Windows、Linux、macOS で実現します。

---

## 🌟 Key Features / 主な特徴

- **True Cross-Platform GUI / クロスプラットフォーム対応**
  - Native look & feel across Windows, Linux (X11 / Wayland), and macOS.
  - Automatic dark / light system theme following powered by Avalonia 11 Fluent Theme.
- **Smart 3-Tier AAC Fallback / 3段階 AAC 自動フォールバック**
  - **Tier 1 (Fastest)**: Uses standalone FFmpeg if `libfdk_aac` is compiled into your FFmpeg build.
  - **Tier 2 (High Compatibility & Quality)**: Uses `fdkaac` CLI via asynchronous standard I/O piping when using generic FFmpeg distributions without `libfdk_aac`.
  - **Tier 3 (Guaranteed Fallback)**: Automatically falls back to FFmpeg's native built-in `aac` encoder if neither FDK option is available.
- **Dynamic HE-AAC Profile Filtering / HE-AAC プロファイルの動的 UI 制御**
  - Automatically hides `HE-AAC v1` and `HE-AAC v2` options if no FDK-capable encoder is present, preventing encoding errors beforehand.
- **Advanced Codec Support / 多彩なコーデック対応**
  - **Lossy**: MP3 (LAME VBR), AAC-LC, HE-AAC v1/v2, Ogg Vorbis, Opus (VBR/ABR), xHE-AAC / USAC (exhale).
  - **Lossless**: FLAC, ALAC (.m4a), WavPack (.wv).
- **Diskless Piping Architecture / ディスクレス・パイプ処理**
  - High-speed in-memory stream piping (`FFmpeg WAV output` ➔ `fdkaac / exhale` ➔ `Metadata tagging`) eliminates intermediate temp file overhead on SSDs/NVMe drives.
- **Album Companion File Copying / コンパニオンファイルの自動コピー**
  - Automatically copies album cover artwork (`.jpg`, `.png`, `.webp`) and album companion files (`.cue`, `.log`) alongside converted files.
- **Directory Hierarchy Preservation / フォルダ階層の維持出力**
  - Preserves nested source folder structures when exporting to a designated destination directory.

---

## 📦 Prerequisites / 動作要件

Usual Audio Converter acts as a front-end orchestrator and uses external CLI binaries. Place them in your system `PATH` or specify their paths via the `⚙ 外部ツール設定` (Settings) dialog:

| Tool | Status | Purpose / 用途 |
| :--- | :--- | :--- |
| **`ffmpeg`** | **Required / 必須** | Core audio decoding, conversion, and metadata tagging |
| **`fdkaac`** | **Recommended / 推奨** | High-quality FDK-AAC & HE-AAC (v1/v2) encoding |
| **`exhale`** | **Optional / 任意** | xHE-AAC (Extended HE-AAC / USAC) encoding |

---

## 🚀 Building & Publishing / ビルド手順

### 1. Requirements
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher

### 2. Development Run / ローカル実行
```bash
dotnet restore
dotnet run
```

### 3. Self-Contained Single-File Binary Publish / 単一バイナリの発行

#### Windows (x64)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `bin/Release/net8.0/win-x64/publish/UsualAudioConverter.exe`

#### Linux (x64)
```powershell
dotnet publish -c Release -r linux-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `bin/Release/net8.0/linux-x64/publish/UsualAudioConverter`

```bash
# On Linux: Make executable and launch
chmod +x ./UsualAudioConverter
./UsualAudioConverter
```

#### macOS (Apple Silicon arm64)
```bash
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

---

## 📄 License / ライセンス

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.
# MarkLeaf

[简体中文](../README.md) | [English](./README.en.md) | [繁體中文](./README.zh-TW.md)

これはネイティブな軽量Markdownビジュアルエディタであり、シンプルなインターフェースと組版を追求し、思考・読書・執筆に集中するためのスペースを提供します。

このプロジェクトは、もともと [fcz](https://github.com/zhuanshunjishi2017) によって発起・制作され、初版はWindowsのみをサポートしていました。その後、[Na Bian](https://github.com/Na-Bian) がmacOS版のサポートを提供しました。**現在、Windows版とmacOS版は共同で更新されています。**

## スクリーンショット

![screenshot-light](./assets/screenshot-light.png)

## 機能紹介

### 豊富な組版スタイルとカラースキーム

#### **組版スタイル**

アプリケーションには豊富な組版スタイルが内蔵されており、例えば：

- **Web**：スクリーンでの閲覧や日常的な編集に適しており、多くのエディタで主流のMarkdownレンダリングスタイルで、効率性と明確な体験を追求します。**(上のスクリーンショットの左上のウィンドウで使用されている組版)**
- **印刷物**：印刷物でよく使われるセリフフォントと太字を使用し、段落は両端揃え、最初の行はインデント、見出しは中央揃え、余白が広く、現代の書籍の組版をシミュレートします。長文の執筆や読書に適しています。
- **LaTeX**：CMUフォントとLaTeXのdocumentクラスに似た組版を使用し、引用やコールアウトボックスはtcolorboxスタイルで、可能な限りLaTeXのレンダリングスタイルに近づけています。**(上のスクリーンショットの中央のウィンドウで使用されている組版)**
- **活版印刷**：Terry Wang氏制作の匯文・朝華シリーズフォントおよび京華老宋体を使用し、印刷物のレイアウトをベースによりレトロなスタイルを演出します。**(上のスクリーンショットの右上のウィンドウで使用されている組版)**

> [!NOTE]
> 一部のテーマは、より良い体験のために特定のフォントが必要な場合があります。以下のページにアクセスするか、[リリース](https://github.com/zhuanshunjishi2017/markleaf/releases) から関連するフォントパッケージを直接ダウンロードしてコンピュータにインストールしてください。
> 
> - [Computer Modern シリーズフォント](https://www.fontsquirrel.com/fonts/computer-modern)（LaTeXデフォルト組版フォント）
> - [匯文・朝華シリーズフォントおよび京華老宋体](https://huozi.cool/)（活版印刷組版、Terry Wang制作の無料フォント）
> - [霞鶩文楷](https://github.com/lxgw/LxgwWenKai)（Lxgw制作の優れたオープンソース中文字体）

#### カラースキーム

アプリケーションは**複数のカラーテーマ**をサポートしており、ダークモードとライトモードを含み、<strong>Win32メニューのダークモード対応を実装しています。</strong>以下は、あらかじめ設定された一部のカラーテーマの効果です。

> [!TIP]
> カラースキームとレンダリングテーマは**どちらもCSSスタイル**であるため、**カラーテーマと組版スタイルを完全にカスタマイズ**することができます。将来的には、関連するテーマエディタもリリースする予定です。

### Markdown構文サポート

**Tiptap/ProseMirror** エディタコアをベースにしており、完全なCommonMarkおよびGitHub Flavored Markdown構文をサポートします。

**さらに以下の機能もサポート：**

- LaTeX数式（KaTeXによるレンダリング）
- Mermaidダイアグラム（SVGとしてレンダリング）
- 脚注の定義・参照・ジャンプ
- GitHubスタイルのアラートブロック（備考、ヒント、警告など）で、各テーマごとに異なる表示効果があります。
- <strong>（カスタム構文）</strong>画像・表のキャプション。

### 優れたエクスポート品質

現在、PDF/HTML/長画像へのエクスポートが可能です。PDFでは用紙サイズ、余白、ヘッダー/フッターなどをカスタマイズできます。また、表のページ分割を禁止するなどの高度な設定もサポートしています。印刷物/LaTeXなどのテーマでPDFにエクスポートすると、読書や印刷に非常に適しており、学術的な執筆の組版要件も満たすことができます。

### ミニマルでありながら完全な操作ロジックと機能

- **ワークスペース管理**：フォルダをワークスペースとして開くことをサポートし、ツリービューまたはリストビューでファイルを表示し、名前/内容でドキュメントを検索します。
- **複数ウィンドウと複数タブ**：複数のウィンドウインスタンスを開くことをサポートし、ドキュメントを新しいウィンドウで開くこともできます。さらに、アプリケーションは同じウィンドウ内で複数のタブを開くことをサポートしており、各タブは独自にドキュメント内容を管理します。
- **ソースモード**：CodeMirror 6ソース編集モードを内蔵しており、ビジュアル編集とMarkdownソース間で即座に切り替えられます。
- **非準拠Markdownマーカーの自動変換**：中国語のMarkdownテキストで**よく見られるリテラルアスタリスクの問題**に対処するため、アプリケーションはCommonMark仕様に準拠しないアスタリスクマーカーを検出し、HTMLタグに変換できます。
- **メニューとショートカット**：すべての段落および書式操作は、コンテキストメニューと段落書式ボタンを介して実行できます。アプリケーションには完全なショートカットカスタマイズシステムもあります。
- **LaTeX数式入力補助**：LaTeXソースを覚える必要はなく、ほとんどの数学記号を網羅しており、クリックするだけで複雑なLaTeX数式を入力できます。
- **集中した読書と執筆**：集中モード、タイプライターモード、ミニマルモード、フルスクリーン編集を提供します。
- **中文・欧文組版に優しい**：環境設定で優先する漢字字形規格（簡体字中国語/繁体字中国語/日本語/韓国語）を選択できます。同時に、**アプリケーションは中文と欧文の間に自動的にスペースを追加するため、手動で空白を挿入する必要はありません。**

## プラットフォームサポート


| プラットフォーム | 使用技術                             | コードディレクトリ               |
| -------- | -------------------------------- | ----------------------- |
| Windows  | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS    | Swift + AppKit + WKWebView       | `apps/macos`            |


両方のプラットフォームで同じエディタフロントエンドとスタイルを共有し、アプリケーションはプラットフォームネイティブな方法で実装されています。

## プロジェクト構造

```text
markleaf/
├── apps/
│   ├── windows/                  # Windowsネイティブアプリ（C# WinForms）
│   │   ├── MarkLeaf/             #   メインプログラム（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setupインストーラ
│   └── macos/                    # macOSネイティブアプリ（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   メインプログラム
│       ├── Changelog/            #   製品更新履歴（4言語）
│       └── script/               #   ビルド／リリーススクリプト
├── packages/
│   ├── editor-web/               # 共有エディタフロントエンド（Tiptap/ProseMirror + CodeMirror 6）
│   └── styles/                   # 共有組版／テーマスタイル（印刷スタイル、両プラットフォーム共有）
├── MarkLeaf.slnx                 # Windowsソリューション
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共有アプリアイコン
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 技術アーキテクチャ

```text
apps/windows（C# WinForms）        apps/macos（Swift AppKit）
  メインウィンドウ / メニュー / ワークスペース / エクスポート       メインウィンドウ / メニュー / ワークスペース / エクスポート
        │                                  │
        ├── packages/editor-web ───────────┤   共有フロントエンド（Tiptap + CodeMirror）
        ├── packages/styles ───────────────┤   共有印刷スタイル
        └── WebView2 / WKWebView ──────────┘   native-shim.js メッセージブリッジ
```

## ビルドと実行

### Webフロントエンドエディタ

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # 出力先 packages/editor-web/dist
pnpm --dir packages/editor-web test        # vitest フロントエンドテスト
```

### Windows

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# ワンショット（フロントエンドビルド + コンパイル + .app パッケージ + 起動）
./apps/macos/script/build_and_run.sh

# リリースパッケージング（.app / ZIP / ブランドDMG / チェックサム）
./apps/macos/script/release/package.sh
```

## ライセンス

アプリケーションはMITライセンスの下で提供されています。詳細は [LICENSE](../LICENSE) をご覧ください。
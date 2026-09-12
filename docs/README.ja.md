# MarkLeaf

[简体中文](../README.md) | [English](./README.en.md) | [繁體中文](./README.zh-TW.md)

MarkLeaf は軽量な Markdown ビジュアルエディタです。Windows/macOS のネイティブアプリと VS Code 拡張機能を提供し、シンプルなインターフェースと組版で、思考・読書・執筆に集中できる空間を目指しています。

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

Windows のネイティブ版は PDF、HTML、PNG/JPG の縦長画像、印刷に対応し、macOS 版は PDF、HTML、PNG/JPG の縦長画像、システム印刷に対応します。VS Code 拡張機能にも PDF、単独 HTML、PNG/JPG 画像、プレビュー、ブラウザー印刷を追加しました。既定の組版は MarkLeaf の「Web · 極簡」です。PDF では用紙、向き、余白、ヘッダー・フッター、ページ番号を設定でき、縦長画像は連番ファイルに分割できます。HTML ファイルの出力以外は `puppeteer-core` でインストール済みの Chrome/Edge を使います。ブラウザーを同梱・ダウンロードしません。

### ミニマルでありながら完全な操作ロジックと機能

以下のワークスペース、複数ウィンドウ、内蔵ソースモードの説明は主にネイティブアプリを対象としています。拡張機能では VS Code のエクスプローラー、ウィンドウ、タブ、ソースエディタを使用します。拡張機能固有の操作は下記ガイドを参照してください。

- **ワークスペース管理**：フォルダをワークスペースとして開くことをサポートし、ツリービューまたはリストビューでファイルを表示し、名前/内容でドキュメントを検索します。ファイル変更による自動更新でも、フォルダの展開、選択、スクロール位置を維持します。現在は `.md` と `.txt` のテキストファイルおよびフォルダのみを表示します。PDF、画像、アーカイブの内容は読み込みません。
- **複数ウィンドウと複数タブ**：複数のウィンドウインスタンスを開くことをサポートし、ドキュメントを新しいウィンドウで開くこともできます。さらに、アプリケーションは同じウィンドウ内で複数のタブを開くことをサポートしており、各タブは独自にドキュメント内容を管理します。
- **ソースモード**：CodeMirror 6ソース編集モードを内蔵しており、ビジュアル編集とMarkdownソース間で即座に切り替えられます。
- **非準拠Markdownマーカーの自動変換**：中国語のMarkdownテキストで**よく見られるリテラルアスタリスクの問題**に対処するため、アプリケーションはCommonMark仕様に準拠しないアスタリスクマーカーを検出し、HTMLタグに変換できます。
- **メニューとショートカット**：すべての段落および書式操作は、コンテキストメニューと段落書式ボタンを介して実行できます。アプリケーションには完全なショートカットカスタマイズシステムもあります。
- **LaTeX数式入力補助**：LaTeXソースを覚える必要はなく、ほとんどの数学記号を網羅しており、クリックするだけで複雑なLaTeX数式を入力できます。
- **集中した読書と執筆**：集中モード、タイプライターモード、ミニマルモード、フルスクリーン編集を提供します。
- **中文・欧文組版に優しい**：環境設定で優先する漢字字形規格（簡体字中国語/繁体字中国語/日本語/韓国語）を選択できます。同時に、**アプリケーションは中文と欧文の間に自動的にスペースを追加するため、手動で空白を挿入する必要はありません。**

## プラットフォームサポート

| プラットフォーム | 使用技術 | コードディレクトリ |
| --- | --- | --- |
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS | Swift + AppKit + WKWebView | `apps/macos` |
| VS Code 拡張機能 | TypeScript + CustomTextEditorProvider + Webview | `apps/vscode` |

3 つのホストは編集コアと組版スタイルを共有します。VS Code 拡張機能は既存の VS Code 実行環境を使い、独立した Electron 依存関係やデスクトップシェルを追加しません。閲覧とビジュアル編集、書式のコピー、表、脚注、数式と Mermaid、画像の貼り付けとドロップ、検索・置換、アウトライン、表示設定に対応します。保存、元に戻す・やり直し、タブ、Markdown ソース編集は VS Code が管理し、ソースとの切り替えや横並び表示が可能です。

拡張機能 0.2.8 は 36 項目の設定と、ショートカットを設定できる 67 項目の書式操作を提供します。数式と図の選択、再クリックによるソース展開、ビューポート内の配置は共有カーネルに統一されています。ショートカット設定は VS Code 内の MarkLeaf のみに適用され、ネイティブアプリの設定には影響しません。プロジェクトと拡張機能の README は簡体字中国語、英語、日本語、繁体字中国語で提供します。拡張機能 UI の翻訳は一部のみです。詳しくは [拡張機能ガイド](../apps/vscode/docs/README.ja.md) と [機能対応表（簡体字中国語）](../apps/vscode/docs/feature-parity.md) を参照してください。

3 製品のコピーと貼り付けは Windows の動作に合わせています。「HTML をコピー」はソース文字列を取得し、ビジュアル編集では通常のテキスト貼り付けとプレーンテキスト貼り付けの両方で Markdown を解析します。ソース編集では文字をそのまま挿入します。貼り付け結果には成功、書式変換、プレーンテキストへのフォールバックと理由、失敗を表示します。

通常の文書表示も `minimal`（Web · 極簡）が既定です。文字の階層、余白、表の細部を保ち、既存のユーザー・ワークスペース設定を優先します。

VS Code 1.120 以降では、Markdown の Git 比較に標準のソース差分エディタを既定で使用し、追加・削除を強調表示します。通常のファイルは引き続き MarkLeaf で開きます。

## プロジェクト構造

```text
markleaf/
├── apps/
│   ├── windows/                  # Windowsネイティブアプリ（C# WinForms）
│   │   ├── MarkLeaf/             #   メインプログラム（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setupインストーラ
│   ├── vscode/                   # VS Code Markdown 閲覧・編集拡張機能（TypeScript）
│   │   ├── src/                  #   拡張プロセス：文書アダプター、コマンド、エクスポート
│   │   └── webview/              #   webview アダプター：拡張プロトコル、設定、ショートカット
│   └── macos/                    # macOSネイティブアプリ（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   メインプログラム
│       ├── Changelog/            #   製品更新履歴（4言語）
│       └── script/               #   ビルド／リリーススクリプト
├── packages/
│   ├── editor-core/              # 共有文書・レンダリングカーネル（TypeScript）
│   ├── editor-web/               # macOS／Windows 用 webview アダプター
│   └── styles/                   # 共有組版／テーマスタイル（3 つのホストで共有）
├── MarkLeaf.slnx                 # Windowsソリューション
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共有アプリアイコン
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 技術アーキテクチャ

```text
packages/editor-core（共有文書・レンダリングカーネル）+ packages/styles（共有組版）
├── packages/editor-web    → apps/windows → WinForms + WebView2 → ネイティブメッセージブリッジ
│                          → apps/macos   → AppKit + WKWebView  → ネイティブメッセージブリッジ
└── apps/vscode/webview    → apps/vscode  → VS Code Webview    → TextDocument / WorkspaceEdit

カーネルは文書規則、レンダリング、編集、エクスポート、組版を担い、ホスト通信と
ホスト UI を一切含みません。ホストの差異は host-capabilities の能力注入で表現し、
カーネル内でホスト種別を判定しません。レンダリングスタック（Tiptap / ProseMirror /
CodeMirror / Mermaid / KaTeX）はカーネルが単独で保持し、アダプター側では再宣言
しないため、2 つ目の複製が解決されることはありません。

Windows/macOS：editor-web/src/main.ts、内蔵 CodeMirror 6 ソースモード
VS Code：apps/vscode/webview/src/vscode.ts、VS Code 標準の Markdown ソースエディタ
```

`build:kernel` は共有レンダラーと DOM 非依存の `document-kernel.cjs` を生成します。Webview は同じ描画ファイルを使い、macOS JavaScriptCore、Windows Jint、VS Code Node.js は同じ文書規則を読み込みます。`build:products` はカーネルを一度ビルドして各製品に配布します。[責務の詳細](./kernel-boundaries.md)。

## ビルドと実行

### VS Code 拡張機能

Node.js 22.12 以降と Corepack 経由でプロジェクト指定の pnpm 11.9.0 を使い、リポジトリのルートで実行します。

```bash
corepack pnpm install:vscode
corepack pnpm package:vscode
```

生成されるパッケージは `artifacts/markleaf-vscode-0.2.8.vsix` です。

VS Code の **Install from VSIX…** で生成したパッケージをインストールします。新しく開く `.md`、`.markdown` は既定で MarkLeaf を使用します。既存のソースタブは **Reopen Editor With… → MarkLeaf** で切り替え、既定の関連付けは **Configure default editor for…** で変更します。**Ctrl+Shift+V**（macOS は **Cmd+Shift+V**）でソースとレンダリング表示を切り替えます。

アップグレード後は文書を保存して **Developer: Reload Window** を実行してください。設定項目がない場合や `markleaf.shortcuts` が未登録と表示される場合も、ウィンドウ全体の再読み込みが必要です。**视图 → 快捷键…**（表示 → ショートカット）で書式操作のキーを記録できます。閲覧だけではファイルを書き換えません。ビジュアル編集では Markdown の書式が正規化される場合があります。[拡張機能の使い方と保持範囲](../apps/vscode/docs/README.ja.md) を参照してください。

### Webフロントエンドエディタ

macOS と Windows で共用します。`editor-web` は `link:` で `editor-core` に依存するため、
シンボリックリンクを有効にするにはカーネル側の依存を先に導入する必要があります。

```bash
corepack pnpm install:editor-web
corepack pnpm build:editor-web
```

フロントエンドの出力先は `packages/editor-web/dist` です。`kernel/` には一度ビルドした共有レンダラーをそのまま配置します。

テストは個別に実行します。ネイティブホストのプロトコルは `corepack pnpm test:editor-web`、共有カーネルの契約は `corepack pnpm test:editor-core` です。

### Windows

```powershell
corepack pnpm install:editor-web
corepack pnpm build:editor-web
dotnet restore .\apps\windows\MarkLeaf\MarkLeaf.csproj
dotnet build .\apps\windows\MarkLeaf\MarkLeaf.csproj --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# ワンショット（フロントエンドビルド + コンパイル + .app パッケージ + 起動）
./apps/macos/script/build_and_run.sh

# リリースパッケージング（.app / ZIP / ブランドDMG / チェックサム）
./apps/macos/script/release/package.sh
```

既定の出力先は `apps/macos/dist/release` で、arm64 アプリの ZIP、DMG、dSYM、チェックサムを含みます。`corepack pnpm build:products` の実行後は `MARKLEAF_USE_BUILT_EDITOR_WEB=1` を指定して、同じカーネルとフロントエンドを再利用できます。

## ライセンス

アプリケーションはMITライセンスの下で提供されています。詳細は [LICENSE](../LICENSE) をご覧ください。

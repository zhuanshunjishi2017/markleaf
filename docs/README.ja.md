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

Windows/macOS のネイティブアプリは PDF/HTML/縦長画像へのエクスポートに対応します。VS Code 拡張機能にはエクスポートと印刷の機能はありません。PDFでは用紙サイズ、余白、ヘッダー/フッターなどをカスタマイズできます。また、表のページ分割を禁止するなどの高度な設定もサポートしています。印刷物/LaTeXなどのテーマでPDFにエクスポートすると、読書や印刷に非常に適しており、学術的な執筆の組版要件も満たすことができます。

### ミニマルでありながら完全な操作ロジックと機能

以下のワークスペース、複数ウィンドウ、内蔵ソースモードの説明は主にネイティブアプリを対象としています。拡張機能では VS Code のエクスプローラー、ウィンドウ、タブ、ソースエディタを使用します。拡張機能固有の操作は下記ガイドを参照してください。

- **ワークスペース管理**：フォルダをワークスペースとして開くことをサポートし、ツリービューまたはリストビューでファイルを表示し、名前/内容でドキュメントを検索します。
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

拡張機能 0.2.4 は 35 項目の設定と、ショートカットを設定できる 67 項目の書式操作を提供します。数式と図のソースパネルは対応する内容の下に開き、文書と一緒にスクロールします。ショートカット設定は VS Code 内の MarkLeaf のみに適用され、ネイティブアプリの設定には影響しません。プロジェクトと拡張機能の README は簡体字中国語、英語、日本語、繁体字中国語で提供します。拡張機能 UI の翻訳は一部のみです。詳しくは [拡張機能ガイド](../apps/vscode/docs/README.ja.md) と [機能対応表（簡体字中国語）](../apps/vscode/docs/feature-parity.md) を参照してください。

## プロジェクト構造

```text
markleaf/
├── apps/
│   ├── windows/                  # Windowsネイティブアプリ（C# WinForms）
│   │   ├── MarkLeaf/             #   メインプログラム（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setupインストーラ
│   ├── vscode/                   # VS Code Markdown 閲覧・編集拡張機能（TypeScript）
│   └── macos/                    # macOSネイティブアプリ（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   メインプログラム
│       ├── Changelog/            #   製品更新履歴（4言語）
│       └── script/               #   ビルド／リリーススクリプト
├── packages/
│   ├── editor-web/               # 共有エディタフロントエンド（Tiptap/ProseMirror + CodeMirror 6）
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
packages/editor-web（共有編集コア）+ packages/styles（共有組版）
├── apps/windows → WinForms + WebView2 → ネイティブメッセージブリッジ
├── apps/macos   → AppKit + WKWebView  → ネイティブメッセージブリッジ
└── apps/vscode  → VS Code Webview    → TextDocument / WorkspaceEdit

Windows/macOS：main.ts、内蔵 CodeMirror 6 ソースモード
VS Code：vscode.ts、VS Code 標準の Markdown ソースエディタ
```

## ビルドと実行

### VS Code 拡張機能

Node.js 22.12 以降とプロジェクト指定の pnpm を使い、リポジトリのルートで実行します。

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir apps/vscode install --frozen-lockfile
pnpm package:vscode                # artifacts/markleaf-vscode-0.2.4.vsix
```

VS Code の **Install from VSIX…** で生成したパッケージをインストールします。新しく開く `.md`、`.markdown` は既定で MarkLeaf を使用します。既存のソースタブは **Reopen Editor With… → MarkLeaf** で切り替え、既定の関連付けは **Configure default editor for…** で変更します。**Ctrl+Shift+V**（macOS は **Cmd+Shift+V**）でソースとレンダリング表示を切り替えます。

アップグレード後は文書を保存して **Developer: Reload Window** を実行してください。設定項目がない場合や `markleaf.shortcuts` が未登録と表示される場合も、ウィンドウ全体の再読み込みが必要です。**视图 → 快捷键…**（表示 → ショートカット）で書式操作のキーを記録できます。閲覧だけではファイルを書き換えません。ビジュアル編集では Markdown の書式が正規化される場合があります。[拡張機能の使い方と保持範囲](../apps/vscode/docs/README.ja.md) を参照してください。

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

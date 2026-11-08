# SAMERIDER 開発・配布

## Windows版

既存のWindows向け発行は `scripts/publish-windows.ps1` を使用します。Windows版は従来のJSON＋`SAMERIDER_Import`画像フォルダー形式と、画像同梱の`.samerider`プロジェクトbundleの両方を読み書きできます。

## ブラウザ版（WebAssembly）

ブラウザ版はサーバー側へ画像をアップロードせず、PNG/GIFの読込とPNGの編集・出力をブラウザ内で行います。GIFを読み込むと、各フレームを連番PNGに変換してセルへ配置できます。Web版では`.samerider`を読み書きできます。Windows版の旧JSONをWeb版で開く場合は、JSONを選んだ後、表示される画像ごとの選択ダイアログで対応するPNGを選んでください。プロジェクト内に同名PNGがある場合も、ダイアログに表示される参照先パスを確認して個別に割り当てます。画像は個別128MiB、合計512MiBまでです。

### 発行

初回のみ.NET 8向けWebAssembly workloadを導入し、Release発行します。

```sh
cd SAMERIDER/developer
dotnet workload install wasm-tools-net8
dotnet publish src/SAMERIDER.Browser/SAMERIDER.Browser.csproj -c Release
```

発行後、次のディレクトリの内容を静的ホスティングへ配置します（SDKによって出力先が異なります）。

- `src/SAMERIDER.Browser/bin/SAMERIDER.Browser/Release/net8.0-browser/publish/wwwroot`
- 旧SDKの場合: `src/SAMERIDER.Browser/bin/SAMERIDER.Browser/Release/net8.0-browser/browser-wasm/AppBundle`

### ローカル確認

発行ディレクトリをHTTPサーバーで配信してブラウザから開きます。例:

```sh
cd src/SAMERIDER.Browser/bin/SAMERIDER.Browser/Release/net8.0-browser/publish/wwwroot
python3 -m http.server 8000
```

`http://localhost:8000`を開いてください。ホスティング側は`.wasm`を`application/wasm`のContent-Typeで配信する必要があります。

`.samerider`はZIPベースの自己完結bundle形式です。ローカル配布・保存に対応しており、外部サーバーへの保存や同期は行いません。

## 非破壊編集

セル一覧の＋／−の隣から、次の編集ができます。編集値はプロジェクトに保存され、原本画像ファイルは変更しません。両操作ともUndo/Redoに対応します。

- **全体サイズ変更**: 現在のセル／シート寸法と縮小プレビューを確認しながらセル幅・高さを変更します。拡縮はNearest Neighborです。「スナップ」は、シートの幅・高さがそれぞれ4の倍数になる最寄りのセル寸法を選びます。実行後の「サイズ変更解除」で元の寸法に戻せます。切り出しサイズを変える場合は先に解除してください。
- **選択セルの色調変更**: 選択中の画像セルに色相・彩度・明るさ・コントラストを適用します。選択内に調整済みセルがあれば「色調変更を解除」に切り替わり、選択セルの調整を解除できます。全体サイズの解除と色調の解除は独立しています。

プレビューは上限付きの縮小画像から生成し、色調スライダーは更新頻度を制限して操作ごとの原本再デコードを避けています。

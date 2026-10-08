# SAMERIDER 開発・配布

## Windows版

既存のWindows向け発行は `scripts/publish-windows.ps1` を使用します。Windows版は従来のJSON＋`SAMERIDER_Import`画像フォルダー形式と、画像同梱の`.samerider`プロジェクトbundleの両方を読み書きできます。

## ブラウザ版（WebAssembly）

ブラウザ版はサーバー側へ画像をアップロードせず、PNGの読込・編集・出力をブラウザ内で行います。Web版では`.samerider`を読み書きできます。Windows版の旧JSONをWeb版で開く場合は、JSONを選んだ後、表示される画像ごとの選択ダイアログで対応するPNGを選んでください。プロジェクト内に同名PNGがある場合も、ダイアログに表示される参照先パスを確認して個別に割り当てます。画像は個別128MiB、合計512MiBまでです。

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

# Unity Blendshape Transfer Tool

Unity 2022+ Editor 用ツール。  
異なるモデル間でシェイプキー（BlendShape）を転送するためのエディタ拡張です。

## 機能
- モデルA（ソース）の衣装に含まれるシェイプキーを抽出
- モデルB（ターゲット）の衣装へ、KD-Treeベースで近傍頂点を対応付けて転送
- 複数のシェイプキーを選択して一括転送可能
- 元のターゲットメッシュは変更せず、複製メッシュを生成

## 導入手順
1. 本リポジトリをクローンまたはダウンロード
2. Unityプロジェクトの `Assets` フォルダに配置
```
Assets/
└─ BlendshapeTransferTool/
   └─ Editor/
      └─ BlendshapeTransferWindow.cs
```
3. Unityを起動すると、メニューに `Tools -> Blendshape Transfer` が追加されます。

## 使用方法
1. Unityメニューから `Tools -> Blendshape Transfer` を開く
2. **Source** にシェイプキーを持つモデルA衣装の `SkinnedMeshRenderer` を指定
3. **Target** に転送先モデルB衣装の `SkinnedMeshRenderer` を指定
4. 「転送するシェイプキー」リストから、転送したいものをチェック
5. 「シェイプキー転送 実行」をクリック
6. 出力結果は `Assets/BlendshapeTransfer/Output/` に保存されます

## 注意事項
- トポロジーが大きく異なる場合は誤差や破綻が発生します
- 法線やタンジェントの再計算は行いません
- BlendShapeフレームが複数ある場合、最後のフレームのみを使用します
- 出力メッシュは新規アセットとして保存され、元のアセットは変更されません

## ライセンス
MIT License

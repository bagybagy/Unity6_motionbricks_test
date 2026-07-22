# Unity6_motionbricks

NVIDIA MotionBricks の公開 G1 モデルを、Unity 6 のゲーム画面から操作するためのローカル実行プロジェクトです。MotionBricks は別プロセスの Python／PyTorch 上で動作し、Unity とは UDP で制御入力と生成姿勢を送受信します。

このプロジェクトで確認できるもの:

- 現在姿勢から指定位置・方向・終端姿勢までのモーション生成
- MotionBricks が生成した将来のルート軌道と終端姿勢の可視化
- 公式 G1 チェックポイントに含まれる15種類のモーションモード
- G1 の29関節を編集する任意終端ポーズ
- G1 29DoF から Unity-chan の Unity Humanoid への意味的リターゲティング

## デモの起動

先にリポジトリ直下の `StartMotionBricksServer.cmd` をダブルクリックして、ローカル CUDA サーバーを起動します。終了するときは `StopMotionBricksServer.cmd` をダブルクリックしてください。これらのスクリプトは UDP ポート 5005 の二重起動を防ぎ、停止時には `motionbricks_server.py` を実行している Python プロセスだけを終了します。

PowerShell から手動で起動する場合は、クローンフォルダ `Unity6_motionbricks` で次を実行します。

```powershell
$env:TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD = '1'
Bridge\.venv\Scripts\python.exe -u Bridge\motionbricks_server.py
```

`WinError 10048` が表示された場合は、既に別のプロセスが UDP ポート 5005 を使用しています。二重起動せず、起動済みの MotionBricks サーバーかポートを使用している別のアプリケーションを確認してください。

Unity `6000.3.13f1` で `Assets/MotionBricks/Scenes/MotionBricksDemo.unity` を開き、Play を押します。シーンを作り直す場合は Unity メニューの **MotionBricks > Create or Reset Demo Scene** を実行します。

画面表示の意味:

- 通常色の G1: 現在の生成姿勢
- オレンジのマーカーと矢印: 指定した終端位置と方向
- シアンの線: 現在以降に生成済みのルート軌道
- 半透明シアンの G1: MotionBricks が生成した終端姿勢
- Unity-chan: G1 の合成ボーン回転差分を Unity Humanoid 骨へ適用した比較表示

## 操作

### 移動目標

- 地面を左クリック: 固定目標を設定
- 目標設定前の WASD: キャラクターを直接操作
- 目標設定後の WASD: 目標位置を微調整
- Q／Eまたは左上の **End yaw** スライダー: 目標の終端方向を回転
- **Turn 180°**: 現在の終端方向を反転
- Escape: 固定目標を解除
- マウス横移動: 直接操作時の向きを変更

### 公式モーションモード

右側パネルまたは F1〜F15 で選択します。Python へは下記の正確なモード名を送ります。

| キー | モード | 内容 |
|---|---|---|
| F1 | `idle` | 待機 |
| F2 | `slow_walk` | 低速歩行 |
| F3 | `walk` | 通常歩行 |
| F4 | `hand_crawling` | 四つ這い移動 |
| F5 | `walk_boxing` | ボクシング歩行 |
| F6 | `elbow_crawling` | 肘を使った這い移動 |
| F7 | `stealth_walk` | ステルス歩行 |
| F8 | `injured_walk` | 負傷歩行 |
| F9 | `walk_stealth` | しゃがみ歩行 |
| F10 | `walk_happy_dance` | ハッピーダンス歩行 |
| F11 | `walk_zombie` | ゾンビ歩行 |
| F12 | `walk_gun` | 銃を構えた歩行 |
| F13 | `walk_scared` | 怯え歩行 |
| F14 | `walk_left` | 左移動 |
| F15 | `walk_right` | 右移動 |

### 任意終端ポーズ

右側パネルの29関節スライダーで G1 の終端関節角を編集します。値は G1 MJCF のヒンジ正方向を基準にしたラジアンです。

- **Enable joint-space target**: 任意ポーズ制約を有効化
- **Neutral**: 29関節を0ラジアンへ設定して有効化
- **Capture Current**: 現在表示中の姿勢を編集値へ取り込む
- **Clear**: 任意ポーズ制約を解除し、選択モード由来の終端姿勢へ戻す

Python 側では既知の関節角をロード済み MJCF の `jnt_range` へクランプし、公式 converter の FK を使って4フレームの終端姿勢制約へ変換します。公式 NVIDIA サブモジュール自体は変更しません。部分的な関節指定では、指定されていない関節に生成時点の現在姿勢を使用します。

MotionBricks は生成モデルなので、指定角度への完全一致、自己衝突の回避、足裏接地は保証されません。極端なしゃがみ姿勢などでは足の浮きや地面への貫通が発生することがあります。

## Unity Humanoid リターゲティング

`G1HumanoidRetargeter` は Python 側の MJCF `qpos` を修正済み G1 FK 階層へ適用し、hip・shoulder など複数ヒンジの合成回転を計算します。初回だけ各モデルの HumanPose から人型の基準姿勢を作ります。Spine はキャラクタールートの解剖学的3軸、Hand は肩・肘・手首から作る前腕フレームを基準に向きを変換するため、FBXごとに異なるボーンローカル軸へ依存しません。脚と腕の位置は、G1 と対象 Humanoid の左右幅・骨長・基準姿勢から作った座標系で正規化し、二関節 IK で UpperLeg／LowerLeg／Foot と UpperArm／LowerArm／Hand へ反映します。HumanoidのHand位置はG1の`wrist_roll`（手首の付け根）、Handの向きは`wrist_roll`・`wrist_pitch`・`wrist_yaw`の合成に対応します。モデル固有の角度補正ではなく実際の骨配置から計算するため、有効な Humanoid Avatar なら同じ手順でモデルを交換できます。

G1の腰と両手首にはローカル方向を確認できる軸マーカーを表示します。赤がX、緑がY、青がZです。球体だけでは判別できない手首の向きも、Humanoid側と目視比較できます。

標準デモでは公式 Unity-chan 3D Model Data v1.4.0 のHumanoid FBXを使用し、G1の右側へ2 m離して表示します。両者は同じ受信ルート姿勢を使い、比較用オフセットを保ったまま移動します。付属アニメーションや旧入力スクリプトは使用せず、MotionBricksが生成した姿勢だけを適用します。描画は Unity 6 の URP と公式 Unity Toon Shader を使用します。

### キャラクターモデルの交換

Unity-chan、Mixamo、その他の一般的な二足キャラクターは、次の共通手順で交換できます。モデルごとのボーン名を MotionBricks 側へ登録する必要はありません。

1. Projectで対象FBXを選択し、Inspectorの **Rig > Animation Type** を **Humanoid** にして **Apply** します。Avatar Definitionは通常 **Create From This Model** で構いません。
2. `MotionBricksDemo` シーンの **Humanoid Retarget Preview** を選択します。
3. `Simple Humanoid Demo Builder` の **Humanoid Prefab** へFBXまたはPrefabをドラッグします。
4. **Build / Swap Humanoid** を押します。次回Play時にも指定モデルが自動生成されます。

有効なHumanoid Avatarなら、同じG1合成回転、体型正規化、二関節IKが自動的に使われます。Generic／Legacy、Avatar未設定、Humanoid設定に失敗したモデルは明示エラーで拒否され、その時点で表示中の正常なモデルは残ります。元がGenericの人型FBXでも、UnityのRig設定でHumanoidとして正しく構成できれば使用できます。人型でないGenericリグは共通Humanoid APIの対象外なので、個別のボーンアダプターが必要です。

これはロボット骨格と人型骨格の意味的な対応です。脚と腕の長さ・左右幅の違いは基準姿勢から自動的に吸収しますが、指、眼、顎、表情BlendShapeはMotionBricksの姿勢制御対象外です。G1の各ヒンジをHumanoid muscleへ1対1で加算せず、修正済みG1階層の合成回転と終端位置を使用します。

## ローカル通信

実行時にオンライン推論サービスは使用しません。

```text
Unity
  └─ control JSON → udp://127.0.0.1:5005
ローカル Python + PyTorch + MotionBricks
  └─ pose JSON    → udp://127.0.0.1:5006
Unity
```

UnityのPlayごとに新しいセッションIDを発行します。Play停止後もCUDAモデルはPythonプロセス内にロードしたままですが、制御パケットが0.75秒途切れると生成と姿勢送信を休止します。次のPlayではシーケンス番号を即座にリセットして新セッションとして開始するため、前回の番号へ追いつく待ち時間はありません。

## Unity-chan ライセンス

Unity-chanは Unity-chan License Terms Version 3.0 に基づいて使用しています。

> This work is provided under Unity-Chan License Terms.
>
> © Unity Technologies Japan/UCL

公式配布物に指定されたライセンス関連ファイル一式は [`ThirdPartyLicenses/UnityChan/UCL3.0`](ThirdPartyLicenses/UnityChan/UCL3.0) に同梱しています。配布元は [Unity-chan公式サイト](https://unity3d.jp/unity-chan/) です。Unity-chanのモデル、画像、音声をMotionBricksその他のAIモデルの学習データまたは入力データとして使用していません。

モデル、Git LFS アセット、Python パッケージの初回ダウンロードにはインターネット接続が必要です。準備後の推論は同じPC内で完結します。姿勢パケットは通常の Ethernet MTU を超える場合があるため、現時点では別PCへのUDP転送を対応範囲にしていません。

## 初回セットアップ

### リポジトリの取得

モデルウェイトを最初からダウンロードせずにサブモジュールを取得します。

```powershell
$env:GIT_LFS_SKIP_SMUDGE = '1'
git clone --recurse-submodules https://github.com/bagybagy/Unity6_motionbricks_test.git
cd Unity6_motionbricks_test
```

### Python環境

Windows に [uv](https://docs.astral.sh/uv/) と Git LFS を用意し、Python 3.10／CUDA版 PyTorch 環境を作成します。

```powershell
.\Bridge\setup_windows.ps1
```

公式 G1 チェックポイントとメッシュを取得します。これらは Git へコミットしません。

```powershell
cd External\GR00T-WholeBodyControl
git -c lfs.concurrenttransfers=1 lfs pull --include="motionbricks/out/**" --exclude=""
git -c lfs.concurrenttransfers=1 lfs pull --include="motionbricks/assets/skeletons/g1/meshes/**" --exclude=""
cd ..\..
```

`TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD` は、現在の公式 NVIDIA Lightning チェックポイントを新しい PyTorch で読み込むために必要です。追跡済みの NVIDIA サブモジュールから取得したチェックポイントにだけ使用してください。

## モックサーバーとテスト

CUDAモデルを起動せず、Unity／Python間の通信とUIを確認できます。

```powershell
cd Bridge
python -m unittest discover -s tests -v
python mock_server.py
```

モックも実サーバーと同じポートを使用するため、同時には起動できません。

## GPUベンチマーク

別のCUDA GPUで初期化時間、VRAM、フレーム遅延を測定します。

```powershell
$env:TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD = '1'
Bridge\.venv\Scripts\python.exe -u Bridge\benchmark_runtime.py --frames 180
```

開発機の RTX 5070 Ti では、公開 Python ランタイムの CUDA予約量は約818 MiBでした。ウォームアップ後180フレームの平均は約2.37 msですが、再計画が発生した8フレームは33.3 msを超え、最大約57.5 msでした。RTX 4050 Laptopの6 GB VRAMでも容量上は十分ですが、低TGPモデルでは再計画時の引っ掛かりが大きくなる可能性があります。

## 主な制約

- CUDA対応NVIDIA GPUが必要です。CPU推論には対応していません。
- 公開チェックポイントが学習していないモーションを、モード名の追加だけで増やすことはできません。
- 任意終端ポーズは学習分布内で近似され、入力角度への厳密一致ではありません。
- Humanoid変換は意味的リターゲティングです。左右幅と四肢長は自動調整しますが、モデル固有の足裏位置や指の接触まで保証するものではありません。
- Bridge は公式実装の内部 `_generate_target_joint_transforms` を薄くラップしています。NVIDIA サブモジュールを更新した場合は、実モデル統合テストを再実行してください。
